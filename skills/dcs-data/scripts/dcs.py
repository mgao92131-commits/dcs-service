#!/usr/bin/env python3
"""Small, dependency-free CLI adapter for the local dcs-service API."""

from __future__ import print_function

import argparse
import csv
import http.client
import io
import json
import math
import os
from pathlib import Path
import re
import socket
import sys
from contextlib import contextmanager
from datetime import datetime, timedelta
from urllib.parse import urlencode, urlsplit


HISTORY_HEADERS = (
    "Timestamp",
    "Value",
    "DataType",
    "DeltaVStatus",
    "ArchiveStatus",
    "SequenceNo",
    "IsHistoryHole",
    "IsCRHole",
    "IsManuallyDeleted",
    "IsManuallyInserted",
)

EVENT_HEADERS = (
    "DateTime",
    "FracSec",
    "Ord",
    "EventType",
    "EventSubType",
    "Category",
    "Area",
    "Node",
    "Unit",
    "Module",
    "ModuleDescription",
    "Attribute",
    "State",
    "EventLevel",
    "Desc1",
    "Desc2",
    "IsArchived",
)

DEFAULT_JSON_MAX_ROWS = 5000
MAX_JSON_BYTES = 16 * 1024 * 1024
MAX_ERROR_BYTES = 1024 * 1024
READ_BLOCK_BYTES = 1024 * 1024
INVALID_FILENAME_CHARS = re.compile(r"[<>:\"/\\|?*\x00-\x1f]")
NUMBER_RE = re.compile(r"^[+-]?(?:\d+|\d+\.\d*|\.\d+)(?:[eE][+-]?\d+)?$")
INTEGER_RE = re.compile(r"^[+-]?\d+$")
TAG_UNKNOWN_RE = re.compile(r"historytagunknown", re.IGNORECASE)
TAG_AMBIGUOUS_RE = re.compile(r"historytagambiguous", re.IGNORECASE)


class DcsCliError(Exception):
    """An expected, user-facing CLI failure with a stable error code."""

    def __init__(self, code, message, details=None):
        super(DcsCliError, self).__init__(message)
        self.code = code
        self.message = message
        self.details = details or {}


class ClientConfig(object):
    def __init__(self, base_url, timeout_connect, timeout_read):
        self.base_url = base_url
        self.timeout_connect = timeout_connect
        self.timeout_read = timeout_read


def _positive_number(value, name):
    try:
        number = float(value)
    except (TypeError, ValueError):
        raise DcsCliError("config_error", "%s must be a positive number." % name)
    if not math.isfinite(number) or number <= 0:
        raise DcsCliError("config_error", "%s must be a positive number." % name)
    return number


def _validate_base_url(value):
    if not isinstance(value, str) or not value.strip():
        raise DcsCliError("config_error", "base_url must be a non-empty HTTP(S) URL.")
    value = value.strip().rstrip("/")
    parsed = None
    try:
        parsed = urlsplit(value)
        hostname = parsed.hostname
        parsed.port  # Force validation of an invalid port.
    except ValueError:
        hostname = None
    if parsed is None or parsed.scheme not in ("http", "https") or not hostname or parsed.query or parsed.fragment:
        raise DcsCliError("config_error", "base_url must be an HTTP(S) URL without a query or fragment.")
    if parsed.username or parsed.password:
        raise DcsCliError("config_error", "base_url must not contain embedded credentials.")
    return value


def default_config_path():
    return Path(__file__).resolve().parent.parent / "config.json"


def load_config(path=None, base_url_override=None, timeout_connect_override=None, timeout_read_override=None):
    config_path = Path(path) if path else default_config_path()
    try:
        with config_path.open("r", encoding="utf-8") as source:
            raw = json.load(source)
    except FileNotFoundError:
        raise DcsCliError("config_error", "Configuration file was not found: %s" % config_path)
    except (OSError, ValueError) as exc:
        raise DcsCliError("config_error", "Could not read configuration file %s: %s" % (config_path, exc))
    if not isinstance(raw, dict):
        raise DcsCliError("config_error", "Configuration root must be a JSON object.")

    base_url = base_url_override if base_url_override is not None else raw.get("base_url")
    connect = timeout_connect_override if timeout_connect_override is not None else raw.get("timeout_connect", 30)
    read = timeout_read_override if timeout_read_override is not None else raw.get("timeout_read", 300)
    return ClientConfig(_validate_base_url(base_url), _positive_number(connect, "timeout_connect"), _positive_number(read, "timeout_read"))


def format_source_time(value):
    """Format a naive source-local datetime without a timezone marker."""
    base = value.strftime("%Y-%m-%dT%H:%M:%S")
    if value.microsecond:
        return base + ".%06d" % value.microsecond
    return base


def parse_source_time(value, option_name="time"):
    if not isinstance(value, str) or not value.strip():
        raise DcsCliError("invalid_request", "%s must be a source-local date/time." % option_name)
    text = value.strip()
    try:
        parsed = datetime.fromisoformat(text.replace(" ", "T", 1))
    except ValueError:
        raise DcsCliError("invalid_request", "%s must be a date/time such as 2026-09-04 08:00." % option_name)
    if parsed.tzinfo is not None:
        raise DcsCliError("invalid_request", "%s must not contain Z or a UTC offset." % option_name)
    fraction = re.search(r"\.(\d+)(?:$|[+-])", text)
    if fraction and len(fraction.group(1)) > 6:
        raise DcsCliError("invalid_request", "%s supports at most six fractional-second digits." % option_name)
    return parsed.replace(tzinfo=None)


def parse_duration(value):
    if not isinstance(value, str):
        raise DcsCliError("invalid_request", "--last must be a positive duration such as 1h, 8h, or 3d.")
    match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*([smhd])\s*", value, re.IGNORECASE)
    if not match:
        raise DcsCliError("invalid_request", "--last must be a positive duration such as 1h, 8h, or 3d.")
    amount = float(match.group(1))
    if not math.isfinite(amount) or amount <= 0:
        raise DcsCliError("invalid_request", "--last must be greater than zero.")
    seconds = amount * {"s": 1, "m": 60, "h": 3600, "d": 86400}[match.group(2).lower()]
    return timedelta(seconds=seconds)


def resolve_range(from_value=None, to_value=None, last_value=None, now=None):
    explicit_range = from_value is not None or to_value is not None
    if last_value is not None and explicit_range:
        raise DcsCliError("invalid_request", "Use either --from/--to or --last, not both.")
    if last_value is None:
        if from_value is None or to_value is None:
            raise DcsCliError("invalid_request", "Both --from and --to are required unless --last is used.")
        start = parse_source_time(from_value, "--from")
        end = parse_source_time(to_value, "--to")
    else:
        end = (now or datetime.now()).replace(microsecond=0)
        start = end - parse_duration(last_value)
    if end <= start:
        raise DcsCliError("invalid_request", "The end of the range must be after its start.")
    return start, end


def safe_filename_component(value):
    cleaned = INVALID_FILENAME_CHARS.sub("_", str(value)).strip().rstrip(" .")
    if not cleaned:
        cleaned = "_"
    return cleaned[:120]


def filename_time(value):
    result = value.strftime("%Y%m%d_%H%M%S")
    if value.microsecond:
        result += "_%06d" % value.microsecond
    return result


def default_history_filename(tag, start, end):
    return "history_%s_%s_%s.csv" % (safe_filename_component(tag), filename_time(start), filename_time(end))


def default_event_filename(start, end):
    return "events_%s_%s.csv" % (filename_time(start), filename_time(end))


def _server_error_payload(body):
    try:
        parsed = json.loads(body.decode("utf-8-sig")) if body else None
    except (UnicodeDecodeError, ValueError):
        return None, None
    if not isinstance(parsed, dict):
        return None, None
    error = parsed.get("error")
    if isinstance(error, dict):
        return error.get("code"), error.get("message")
    if isinstance(error, str):
        return error, parsed.get("message")
    return None, parsed.get("message")


def _translate_http_error(status, server_code, server_message):
    message = server_message or "DCS data service returned HTTP %s." % status
    if not isinstance(message, str):
        message = str(message)
    if TAG_UNKNOWN_RE.search(message):
        return "tag_not_found", "The requested TAG was not found."
    if TAG_AMBIGUOUS_RE.search(message):
        return "tag_ambiguous", "The requested TAG is ambiguous."
    if status == 429 or server_code == "service_busy":
        return "dcs_busy", "DCS data service is busy. Retry later."
    if status == 503 or server_code in ("historian_unavailable", "event_unavailable", "event_overflow", "event_journal_full", "event_source_changed"):
        return "dcs_unavailable", "Historian or Event Journal is currently unavailable."
    if status == 409:
        return "event_cursor_rejected", message
    if status == 400:
        return "invalid_request", message
    if status == 404:
        return "not_found", message
    if status == 405:
        return "method_not_allowed", message
    return "http_error", message


class DcsClient(object):
    def __init__(self, config):
        self.config = config
        self._base = urlsplit(config.base_url)

    def _target(self, path, params=None):
        base_path = self._base.path.rstrip("/")
        target = (base_path + path) or "/"
        if params:
            target += "?" + urlencode(params)
        return target

    @contextmanager
    def _open(self, path, params=None, accept="application/json"):
        connection_type = http.client.HTTPSConnection if self._base.scheme == "https" else http.client.HTTPConnection
        connection = None
        response = None
        try:
            connection = connection_type(self._base.hostname, self._base.port, timeout=self.config.timeout_connect)
            connection.request("GET", self._target(path, params), headers={"Accept": accept, "Connection": "close"})
            response = connection.getresponse()
            if connection.sock is not None:
                connection.sock.settimeout(self.config.timeout_read)
        except (OSError, socket.timeout, http.client.HTTPException) as exc:
            if connection is not None:
                connection.close()
            raise DcsCliError("dcs_unavailable", "Could not reach dcs-service: %s" % exc)
        try:
            yield response
        finally:
            try:
                if response is not None:
                    response.close()
            finally:
                if connection is not None:
                    connection.close()

    def _read_error(self, response):
        try:
            body = response.read(MAX_ERROR_BYTES)
        except (OSError, socket.timeout, http.client.HTTPException):
            body = b""
        server_code, server_message = _server_error_payload(body)
        code, message = _translate_http_error(response.status, server_code, server_message)
        return DcsCliError(code, message)

    def _read_json_response(self, response):
        try:
            body = response.read(MAX_JSON_BYTES + 1)
        except (OSError, socket.timeout, http.client.HTTPException) as exc:
            raise DcsCliError("incomplete_response", "The JSON response was incomplete: %s" % exc)
        if len(body) > MAX_JSON_BYTES:
            raise DcsCliError("invalid_response", "The JSON response is larger than the safety limit.")
        try:
            parsed = json.loads(body.decode("utf-8-sig"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise DcsCliError("invalid_response", "dcs-service returned invalid JSON: %s" % exc)
        if not isinstance(parsed, dict):
            raise DcsCliError("invalid_response", "dcs-service returned a JSON value instead of an object.")
        return parsed

    def get_json(self, path, params=None):
        with self._open(path, params, "application/json") as response:
            if response.status != 200:
                raise self._read_error(response)
            return self._read_json_response(response)

    def health(self):
        data = self.get_json("/health")
        if data.get("status") != "ok":
            raise DcsCliError("dcs_unavailable", "dcs-service is not healthy.")
        return {"ok": True, "type": "health", "status": data.get("status")}

    def info(self):
        data = self.get_json("/api/v1/info")
        result = {"ok": True, "type": "info"}
        result.update(data)
        return result

    def tag(self, tag):
        data = self.get_json("/api/v1/tag", {"tag": tag})
        status = data.get("status")
        if status == "HistoryTagUnknown":
            raise DcsCliError("tag_not_found", "The requested TAG was not found.", {"tag": tag})
        if status == "HistoryTagAmbiguous":
            raise DcsCliError("tag_ambiguous", "The requested TAG is ambiguous.", {"tag": tag})
        if status != "HistoryTagOK":
            raise DcsCliError("dcs_unavailable", "Historian could not validate the requested TAG.", {"tag": tag})
        return {
            "ok": True,
            "type": "tag",
            "tag": data.get("tag", tag),
            "status": status,
            "data_type": data.get("dataType"),
        }

    def _stream_csv(self, path, params, sink):
        with self._open(path, params, "text/csv") as response:
            if response.status != 200:
                raise self._read_error(response)
            content_type = response.getheader("Content-Type", "") or ""
            if content_type and not content_type.lower().startswith("text/csv"):
                try:
                    body = response.read(MAX_ERROR_BYTES)
                except (OSError, socket.timeout, http.client.HTTPException):
                    body = b""
                server_code, server_message = _server_error_payload(body)
                if server_code or server_message:
                    code, message = _translate_http_error(response.status, server_code, server_message)
                    raise DcsCliError(code, message)
                raise DcsCliError("invalid_response", "dcs-service returned a non-CSV success response.")
            try:
                while True:
                    block = response.read(READ_BLOCK_BYTES)
                    if not block:
                        break
                    sink(block)
            except (OSError, socket.timeout, http.client.HTTPException) as exc:
                raise DcsCliError("incomplete_download", "The CSV download was incomplete; retry the full range. (%s)" % exc)

    def csv_bytes(self, path, params, max_bytes=MAX_JSON_BYTES):
        data = bytearray()

        def append(block):
            if len(data) + len(block) > max_bytes:
                raise DcsCliError("json_too_large", "The CSV result is too large for JSON output; use the default file mode.")
            data.extend(block)

        self._stream_csv(path, params, append)
        return bytes(data)

    def download_csv(self, path, params, final_path, expected_headers):
        final_path = Path(final_path)
        part_path = Path(str(final_path) + ".part")
        try:
            final_path.parent.mkdir(parents=True, exist_ok=True)
            with part_path.open("wb") as output:
                self._stream_csv(path, params, output.write)
            row_count = csv_row_count(part_path, expected_headers)
            os.replace(str(part_path), str(final_path))
            return row_count
        except DcsCliError:
            _remove_if_exists(part_path)
            raise
        except (OSError, UnicodeError, csv.Error) as exc:
            _remove_if_exists(part_path)
            raise DcsCliError("output_error", "Could not save or validate the CSV output: %s" % exc)


def _remove_if_exists(path):
    try:
        path.unlink()
    except FileNotFoundError:
        pass
    except OSError:
        pass


def _csv_reader(stream):
    try:
        csv.field_size_limit(64 * 1024 * 1024)
    except OverflowError:
        pass
    return csv.reader(stream, strict=True)


def csv_row_count(path, expected_headers):
    try:
        with Path(path).open("r", encoding="utf-8-sig", newline="") as source:
            reader = _csv_reader(source)
            header = next(reader, None)
            if header != list(expected_headers):
                raise DcsCliError("invalid_response", "CSV header did not match the dcs-service contract.")
            count = 0
            for row in reader:
                if len(row) != len(expected_headers):
                    raise DcsCliError("invalid_response", "CSV row %s has %s fields; expected %s." % (count + 2, len(row), len(expected_headers)))
                count += 1
            return count
    except DcsCliError:
        raise
    except (OSError, UnicodeError, csv.Error) as exc:
        raise DcsCliError("invalid_response", "dcs-service returned invalid CSV: %s" % exc)


def _optional_text(value):
    return value if value != "" else None


def _required_int(value, field_name):
    if not INTEGER_RE.fullmatch(value):
        raise DcsCliError("invalid_response", "CSV field %s is not an integer." % field_name)
    try:
        return int(value)
    except ValueError:
        raise DcsCliError("invalid_response", "CSV field %s is not a valid integer." % field_name)


def _optional_int(value, field_name):
    if value == "":
        return None
    return _required_int(value, field_name)


def _required_bool(value, field_name):
    if value == "true":
        return True
    if value == "false":
        return False
    raise DcsCliError("invalid_response", "CSV field %s is not true or false." % field_name)


def _parse_value(value):
    if value == "":
        return None
    lowered = value.lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    if INTEGER_RE.fullmatch(value):
        try:
            return int(value)
        except ValueError:
            return value
    if NUMBER_RE.fullmatch(value):
        try:
            number = float(value)
            return number if math.isfinite(number) else value
        except ValueError:
            return value
    return value


def history_json_row(row):
    return {
        "timestamp": row[0],
        "value": _parse_value(row[1]),
        "data_type": _optional_text(row[2]),
        "delta_v_status": _optional_text(row[3]),
        "archive_status": _optional_text(row[4]),
        "sequence_no": _required_int(row[5], "SequenceNo"),
        "is_history_hole": _required_bool(row[6], "IsHistoryHole"),
        "is_cr_hole": _required_bool(row[7], "IsCRHole"),
        "is_manually_deleted": _required_bool(row[8], "IsManuallyDeleted"),
        "is_manually_inserted": _required_bool(row[9], "IsManuallyInserted"),
    }


def event_json_row(row):
    return {
        "date_time": row[0],
        "frac_sec": _required_int(row[1], "FracSec"),
        "ord": _required_int(row[2], "Ord"),
        "event_type": _optional_text(row[3]),
        "event_sub_type": _optional_text(row[4]),
        "category": _optional_text(row[5]),
        "area": _optional_text(row[6]),
        "node": _optional_text(row[7]),
        "unit": _optional_text(row[8]),
        "module": _optional_text(row[9]),
        "module_description": _optional_text(row[10]),
        "attribute": _optional_text(row[11]),
        "state": _optional_text(row[12]),
        "event_level": _optional_text(row[13]),
        "desc1": _optional_text(row[14]),
        "desc2": _optional_text(row[15]),
        "is_archived": _optional_int(row[16], "IsArchived"),
    }


def csv_json_rows(raw_bytes, expected_headers, row_converter, max_rows):
    try:
        stream = io.StringIO(raw_bytes.decode("utf-8-sig"), newline="")
        reader = _csv_reader(stream)
        header = next(reader, None)
        if header != list(expected_headers):
            raise DcsCliError("invalid_response", "CSV header did not match the dcs-service contract.")
        rows = []
        for row_number, row in enumerate(reader, start=2):
            if len(row) != len(expected_headers):
                raise DcsCliError("invalid_response", "CSV row %s has %s fields; expected %s." % (row_number, len(row), len(expected_headers)))
            if len(rows) >= max_rows:
                raise DcsCliError("json_too_large", "The CSV result exceeds --max-rows; use the default file mode.")
            rows.append(row_converter(row))
        return rows
    except DcsCliError:
        raise
    except (UnicodeError, csv.Error) as exc:
        raise DcsCliError("invalid_response", "dcs-service returned invalid CSV: %s" % exc)


def _json_dump(value):
    sys.stdout.write(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n")


def _error_payload(error):
    payload = {"ok": False, "error": error.code, "message": error.message}
    for key in ("tag",):
        if key in error.details:
            payload[key] = error.details[key]
    return payload


def _history_error(error, tag):
    if error.code == "invalid_request":
        if TAG_UNKNOWN_RE.search(error.message):
            return DcsCliError("tag_not_found", "The requested TAG was not found.", {"tag": tag})
        if TAG_AMBIGUOUS_RE.search(error.message):
            return DcsCliError("tag_ambiguous", "The requested TAG is ambiguous.", {"tag": tag})
    return error


def _run_history(client, args):
    start, end = resolve_range(args.from_time, args.to_time, args.last)
    from_text = format_source_time(start)
    to_text = format_source_time(end)
    params = {"tag": args.tag, "from": from_text, "to": to_text}
    if args.json:
        try:
            raw = client.csv_bytes("/api/v1/history", params)
            rows = csv_json_rows(raw, HISTORY_HEADERS, history_json_row, args.max_rows)
        except DcsCliError as error:
            raise _history_error(error, args.tag)
        return {
            "ok": True,
            "type": "history",
            "tag": args.tag,
            "from": from_text,
            "to": to_text,
            "row_count": len(rows),
            "data": rows,
        }

    output = Path(args.output) if args.output else Path(".work") / "dcs" / default_history_filename(args.tag, start, end)
    try:
        count = client.download_csv("/api/v1/history", params, output, HISTORY_HEADERS)
    except DcsCliError as error:
        raise _history_error(error, args.tag)
    return {"ok": True, "type": "history", "tag": args.tag, "from": from_text, "to": to_text, "row_count": count, "file": os.fspath(output)}


def _run_events(client, args):
    start, end = resolve_range(args.from_time, args.to_time, args.last)
    from_text = format_source_time(start)
    to_text = format_source_time(end)
    params = {"from": from_text, "to": to_text}
    if args.json:
        raw = client.csv_bytes("/api/v1/events", params)
        rows = csv_json_rows(raw, EVENT_HEADERS, event_json_row, args.max_rows)
        return {"ok": True, "type": "events", "from": from_text, "to": to_text, "row_count": len(rows), "data": rows}

    output = Path(args.output) if args.output else Path(".work") / "dcs" / default_event_filename(start, end)
    count = client.download_csv("/api/v1/events", params, output, EVENT_HEADERS)
    return {"ok": True, "type": "events", "from": from_text, "to": to_text, "row_count": count, "file": os.fspath(output)}


def _add_range_options(parser):
    parser.add_argument("--from", dest="from_time", help="source-local start time")
    parser.add_argument("--to", dest="to_time", help="source-local end time")
    parser.add_argument("--last", help="relative range, for example 1h, 8h, or 3d")
    parser.add_argument("--json", action="store_true", help="return small results as JSON instead of a CSV file")
    parser.add_argument("--max-rows", type=int, default=DEFAULT_JSON_MAX_ROWS, help="JSON row cap (default: %(default)s)")
    parser.add_argument("--output", help="final CSV path; ignored in --json mode")


def build_parser():
    parser = argparse.ArgumentParser(description="Read-only adapter for dcs-service.", allow_abbrev=False)
    parser.add_argument("--config", help="path to config.json")
    parser.add_argument("--base-url", help="override config base_url")
    parser.add_argument("--timeout-connect", type=float, help="override connect timeout in seconds")
    parser.add_argument("--timeout-read", type=float, help="override read timeout in seconds")
    commands = parser.add_subparsers(dest="command")

    commands.add_parser("health", help="check service liveness")
    commands.add_parser("info", help="read service metadata")

    tag = commands.add_parser("tag", help="diagnose one Historian tag")
    tag.add_argument("tag")

    history = commands.add_parser("history", help="read complete History CSV")
    history.add_argument("tag")
    _add_range_options(history)

    events = commands.add_parser("events", help="read complete Event CSV")
    _add_range_options(events)
    return parser


def _validate_command_args(args):
    if not args.command:
        raise DcsCliError("invalid_request", "A command is required: health, info, tag, history, or events.")
    if args.command in ("history", "events") and args.max_rows <= 0:
        raise DcsCliError("invalid_request", "--max-rows must be greater than zero.")


def main(argv=None):
    parser = build_parser()
    try:
        args = parser.parse_args(argv)
        _validate_command_args(args)
        config = load_config(args.config, args.base_url, args.timeout_connect, args.timeout_read)
        client = DcsClient(config)
        if args.command == "health":
            result = client.health()
        elif args.command == "info":
            result = client.info()
        elif args.command == "tag":
            result = client.tag(args.tag)
        elif args.command == "history":
            result = _run_history(client, args)
        else:
            result = _run_events(client, args)
        _json_dump(result)
        return 0
    except DcsCliError as error:
        _json_dump(_error_payload(error))
        return 1
    except KeyboardInterrupt:
        _json_dump({"ok": False, "error": "cancelled", "message": "The request was cancelled."})
        return 130


if __name__ == "__main__":
    sys.exit(main())
