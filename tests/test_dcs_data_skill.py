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

            def do_POST(self):
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
    def test_realtime_single_never_probes_historian(self):
        def responder(handler, parsed):
            self.assertEqual(parsed.path, "/api/v1/realtime")
            self.assertEqual(parse_qs(parsed.query)["tag"], ["SY-218/AI1/PV.CV"])
            send_body(handler, 200, json.dumps({"ok": True, "tag": "SY-218/AI1/PV.CV", "value": 512.6943359375}))
        with TestServer(responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "realtime", "SY-218/AI1/PV.CV"])
            self.assertEqual(code, 0)
            self.assertEqual(result["value"], 512.6943359375)
            self.assertEqual(len(server.server.requests), 1)

    def test_realtime_batch_partial_preserves_errors_and_order(self):
        tags = ["A/AI1/PV.CV", "B/AI1/PV.CV", "A/AI1/PV.CV"]
        def responder(handler, parsed):
            self.assertEqual(handler.command, "POST")
            self.assertEqual(parsed.path, "/api/v1/realtime/batch")
            body = json.loads(handler.rfile.read(int(handler.headers["Content-Length"])))
            self.assertEqual(body, {"tags": tags})
            send_body(handler, 200, json.dumps({"ok": False, "complete": True, "total": 3, "successCount": 2, "failureCount": 1, "items": [{"tag": tags[0], "ok": True, "value": "中文"}, {"tag": tags[1], "ok": False, "error": {"code": "parameter_not_found"}}, {"tag": tags[2], "ok": True, "value": 1}]}))
        with TestServer(responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "realtime-batch"] + tags)
            self.assertEqual(code, 1)
            self.assertEqual([item["tag"] for item in result["items"]], tags)
            self.assertEqual(result["items"][0]["value"], "中文")

    def test_realtime_read_timeout_applies_before_response_headers(self):
        import time
        def responder(handler, parsed):
            time.sleep(0.2)
            try:
                send_body(handler, 200, json.dumps({"ok": True, "tag": "A/AI1/PV.CV", "value": 1}))
            except OSError:
                pass
        with TestServer(responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "--timeout-connect", "1", "--timeout-read", "0.05", "realtime", "A/AI1/PV.CV"])
            self.assertEqual(code, 1)
            self.assertEqual(result["error"], "dcs_unavailable")

    def test_realtime_batch_rejects_missing_value(self):
        def responder(handler, parsed):
            handler.rfile.read(int(handler.headers["Content-Length"]))
            send_body(handler, 200, json.dumps({"ok": True, "complete": True, "total": 1, "successCount": 1, "failureCount": 0, "items": [{"tag": "A/AI1/PV.CV", "ok": True}]}))
        with TestServer(responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "realtime-batch", "A/AI1/PV.CV"])
            self.assertEqual(code, 1)
            self.assertEqual(result["error"], "invalid_response")

    def test_realtime_rejects_module_only_input(self):
        code, result = run_cli(["realtime", "SY-218"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "invalid_request")

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

    def test_module_history_defaults_to_actual_and_probes_candidates(self):
        tag_requests = []

        def responder(handler, parsed):
            query = parse_qs(parsed.query)
            if parsed.path == "/api/v1/tag":
                requested = query["tag"][0]
                tag_requests.append(requested)
                if requested == "MOD1/PID1/PV.CV":
                    send_body(handler, 200, '{"tag":"MOD1/PID1/PV.CV","status":"HistoryTagUnknown","dataType":""}')
                elif requested == "MOD1/AI1/PV.CV":
                    send_body(handler, 200, '{"tag":"MOD1/AI1/PV.CV","status":"HistoryTagOK","dataType":"Float"}')
                else:
                    send_body(handler, 200, '{"tag":"%s","status":"HistoryTagUnknown","dataType":""}' % requested)
            else:
                self.assertEqual(parsed.path, "/api/v1/history")
                self.assertEqual(query["tag"], ["MOD1/AI1/PV.CV"])
                send_chunked(handler, HISTORY_CSV)

        with TestServer(responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "history", "MOD1", "--last", "1h", "--json"])
        self.assertEqual(code, 0)
        self.assertEqual(tag_requests, ["MOD1/PID1/PV.CV", "MOD1/AI1/PV.CV"])
        self.assertEqual(result["module"], "MOD1")
        self.assertEqual(result["role"], "actual")
        self.assertEqual(result["tag"], "MOD1/AI1/PV.CV")
        self.assertEqual(result["data_type"], "Float")

    def test_module_history_uses_requested_setpoint_or_output_role(self):
        for role, suffix in (("setpoint", "/PID1/SP.CV"), ("output", "/PID1/OUT.CV")):
            tag_requests = []
            expected_tag = "MOD2%s" % suffix

            def responder(handler, parsed):
                query = parse_qs(parsed.query)
                if parsed.path == "/api/v1/tag":
                    requested = query["tag"][0]
                    tag_requests.append(requested)
                    if requested == expected_tag:
                        send_body(handler, 200, '{"tag":"%s","status":"HistoryTagOK","dataType":"Float"}' % requested)
                    else:
                        send_body(handler, 200, '{"tag":"%s","status":"HistoryTagUnknown","dataType":""}' % requested)
                else:
                    self.assertEqual(parsed.path, "/api/v1/history")
                    self.assertEqual(query["tag"], [expected_tag])
                    send_chunked(handler, HISTORY_CSV)

            with TestServer(responder) as server:
                code, result = run_cli(
                    [
                        "--base-url",
                        server.base_url,
                        "history",
                        "MOD2",
                        "--role",
                        role,
                        "--last",
                        "1h",
                        "--json",
                    ]
                )
            self.assertEqual(code, 0)
            self.assertEqual(tag_requests, [expected_tag])
            self.assertEqual(result["module"], "MOD2")
            self.assertEqual(result["role"], role)
            self.assertEqual(result["tag"], expected_tag)

    def test_module_history_reports_missing_or_service_ambiguous_candidates(self):
        def missing_responder(handler, parsed):
            if parsed.path == "/api/v1/tag":
                query = parse_qs(parsed.query)
                requested = query["tag"][0]
                send_body(handler, 200, '{"tag":"%s","status":"HistoryTagUnknown","dataType":""}' % requested)
            else:
                self.fail("history must not be requested when no module candidate exists")

        with TestServer(missing_responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "history", "MOD3", "--last", "1h"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "tag_not_found")
        self.assertEqual(result["module"], "MOD3")
        self.assertEqual(result["role"], "actual")
        self.assertEqual(result["checked_tags"], [
            "MOD3/PID1/PV.CV",
            "MOD3/AI1/PV.CV",
            "MOD3/DI1/PV_D.CV",
            "MOD3/DC1/PV_D.CV",
        ])

        def ambiguous_responder(handler, parsed):
            if parsed.path == "/api/v1/tag":
                query = parse_qs(parsed.query)
                requested = query["tag"][0]
                if requested == "MOD4/PID1/PV.CV":
                    send_body(handler, 200, '{"tag":"%s","status":"HistoryTagAmbiguous","dataType":""}' % requested)
                else:
                    send_body(handler, 200, '{"tag":"%s","status":"HistoryTagUnknown","dataType":""}' % requested)
            else:
                self.fail("history must not be requested for an ambiguous candidate")

        with TestServer(ambiguous_responder) as server:
            code, result = run_cli(["--base-url", server.base_url, "history", "MOD4", "--last", "1h"])
        self.assertEqual(code, 1)
        self.assertEqual(result["error"], "tag_ambiguous")
        self.assertEqual(result["module"], "MOD4")
        self.assertEqual(result["tag"], "MOD4/PID1/PV.CV")

    def test_event_file_is_validated_and_atomically_replaced(self):
        def responder(handler, parsed):
            send_chunked(handler, EVENT_CSV)

        with tempfile.TemporaryDirectory() as temp_dir:
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

        with tempfile.TemporaryDirectory() as temp_dir:
            final_path = Path(temp_dir) / "history.csv"
            final_path.write_text("old result", encoding="utf-8")
            with TestServer(incomplete_responder) as server:
                code, result = run_cli(
                    [
                        "--base-url",
                        server.base_url,
                        "history",
                        "T/PID1/PV.CV",
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
