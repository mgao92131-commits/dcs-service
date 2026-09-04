import contextlib
import csv
import io
import importlib.util
import json
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import tempfile
import threading
import unittest
from urllib.parse import parse_qs, urlsplit


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "skills" / "dcs-data" / "scripts" / "dcs.py"
REPO_ROOT = SCRIPT_PATH.parents[3]
SPEC = importlib.util.spec_from_file_location("dcs_data_skill_cli", SCRIPT_PATH)
DCS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DCS)


HISTORY_CSV = (
    "Timestamp,Value,DataType,DeltaVStatus,ArchiveStatus,SequenceNo,IsHistoryHole,IsCRHole,IsManuallyDeleted,IsManuallyInserted\r\n"
    "2026-09-04T08:00:01.0000000,81.23,Float,Good,Valid,7,false,false,false,false\r\n"
)

EVENT_CSV = (
    "DateTime,FracSec,Ord,EventType,EventSubType,Category,Area,Node,Unit,Module,ModuleDescription,Attribute,State,EventLevel,Desc1,Desc2,IsArchived\r\n"
    "2026-09-04T08:00:01.000,12,34,Alarm,High,Process,AREA1,NODE1,UNIT1,MOD1,Description,PV,Active,Warning,desc one,desc two,1\r\n"
)


def send_body(handler, status, body, content_type="application/json; charset=utf-8"):
    body = body.encode("utf-8") if isinstance(body, str) else body
    handler.send_response(status)
    handler.send_header("Content-Type", content_type)
    handler.send_header("Content-Length", str(len(body)))
    handler.end_headers()
    handler.wfile.write(body)


def send_chunked(handler, body, complete=True):
    body = body.encode("utf-8") if isinstance(body, str) else body
    handler.send_response(200)
    handler.send_header("Content-Type", "text/csv; charset=utf-8")
    handler.send_header("Transfer-Encoding", "chunked")
    handler.send_header("Connection", "close")
    handler.end_headers()
    for offset in range(0, len(body), 17):
        chunk = body[offset : offset + 17]
        handler.wfile.write(("%X\r\n" % len(chunk)).encode("ascii"))
        handler.wfile.write(chunk)
        handler.wfile.write(b"\r\n")
    if complete:
        handler.wfile.write(b"0\r\n\r\n")
        handler.wfile.flush()
    else:
        handler.wfile.flush()
        handler.close_connection = True
        handler.connection.close()


class TestServer(object):
    def __init__(self, responder):
        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                parsed = urlsplit(self.path)
                self.server.requests.append(parsed)
                self.server.responder(self, parsed)

            def log_message(self, format_string, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.responder = responder
        self.server.requests = []
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    @property
    def base_url(self):
        return "http://127.0.0.1:%s" % self.server.server_port

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


def run_cli(arguments):
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        return_code = DCS.main(arguments)
    lines = output.getvalue().splitlines()
    return return_code, json.loads(lines[-1])


class DcsDataSkillTests(unittest.TestCase):
    def test_time_normalization_and_last_range(self):
        parsed = DCS.parse_source_time("2026-09-04 08:00", "--from")
        self.assertEqual(DCS.format_source_time(parsed), "2026-09-04T08:00:00")
        start, end = DCS.resolve_range(last_value="1.5h", now=datetime(2026, 9, 4, 16, 0, 0))
        self.assertEqual(DCS.format_source_time(start), "2026-09-04T14:30:00")
        self.assertEqual(DCS.format_source_time(end), "2026-09-04T16:00:00")
        with self.assertRaises(DCS.DcsCliError):
            DCS.parse_source_time("2026-09-04T08:00:00+08:00", "--from")

    def test_history_json_uses_encoded_query_and_normalized_rows(self):
        def responder(handler, parsed):
            self.assertEqual(parsed.path, "/api/v1/history")
            query = parse_qs(parsed.query)
            self.assertEqual(query["tag"], ["A/B.CV"])
            self.assertEqual(query["from"], ["2026-09-04T08:00:00"])
            self.assertEqual(query["to"], ["2026-09-04T09:00:00"])
            send_chunked(handler, HISTORY_CSV)

        with TestServer(responder) as server:
            code, result = run_cli(
                [
                    "--base-url",
                    server.base_url,
                    "history",
                    "A/B.CV",
                    "--from",
                    "2026-09-04 08:00",
                    "--to",
                    "2026-09-04 09:00",
                    "--json",
                ]
            )
        self.assertEqual(code, 0)
        self.assertEqual(result["row_count"], 1)
        self.assertEqual(result["data"][0]["value"], 81.23)
        self.assertFalse(result["data"][0]["is_history_hole"])

    def test_event_file_is_validated_and_atomically_replaced(self):
        def responder(handler, parsed):
            send_chunked(handler, EVENT_CSV)

        with tempfile.TemporaryDirectory(dir=str(REPO_ROOT)) as temp_dir:
            final_path = Path(temp_dir) / "events.csv"
            with TestServer(responder) as server:
                code, result = run_cli(
                    [
                        "--base-url",
                        server.base_url,
                        "events",
                        "--from",
                        "2026-09-04 08:00",
                        "--to",
                        "2026-09-04 09:00",
                        "--output",
                        str(final_path),
                    ]
                )
            self.assertEqual(code, 0)
            self.assertEqual(result["row_count"], 1)
            self.assertTrue(final_path.exists())
            self.assertFalse(Path(str(final_path) + ".part").exists())
            with final_path.open("r", encoding="utf-8", newline="") as source:
                rows = list(csv.reader(source))
            self.assertEqual(rows[0][0], "DateTime")
            self.assertEqual(rows[1][1:3], ["12", "34"])
            self.assertNotIn(b"11\r\n", final_path.read_bytes())

    def test_busy_and_unavailable_errors_are_translated(self):
        def busy_responder(handler, parsed):
            send_body(handler, 429, '{"ok":false,"error":{"code":"service_busy","message":"queue full"}}')

        with TestServer(busy_responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "health"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "dcs_busy")

        def unavailable_responder(handler, parsed):
            send_body(handler, 503, '{"ok":false,"error":{"code":"event_unavailable","message":"SQL down"}}')

        with TestServer(unavailable_responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "info"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "dcs_unavailable")

    def test_unknown_tag_and_incomplete_download_do_not_overwrite_final_file(self):
        def tag_responder(handler, parsed):
            send_body(handler, 200, '{"tag":"XXX","status":"HistoryTagUnknown","dataType":""}')

        with TestServer(tag_responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "tag", "XXX"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "tag_not_found")
        self.assertEqual(result["tag"], "XXX")

        def incomplete_responder(handler, parsed):
            send_chunked(handler, HISTORY_CSV, complete=False)

        with tempfile.TemporaryDirectory(dir=str(REPO_ROOT)) as temp_dir:
            final_path = Path(temp_dir) / "history.csv"
            final_path.write_text("old result", encoding="utf-8")
            with TestServer(incomplete_responder) as server:
                code, result = run_cli(
                    [
                        "--base-url",
                        server.base_url,
                        "history",
                        "T",
                        "--from",
                        "2026-09-04 08:00",
                        "--to",
                        "2026-09-04 09:00",
                        "--output",
                        str(final_path),
                    ]
                )
            self.assertEqual(code, 1)
            self.assertEqual(result["error"], "incomplete_download")
            self.assertEqual(final_path.read_text(encoding="utf-8"), "old result")
            self.assertFalse(Path(str(final_path) + ".part").exists())


if __name__ == "__main__":
    unittest.main()
