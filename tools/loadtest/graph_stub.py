"""A stand-in for Meta's Graph API during load tests: accepts every send and answers like Meta.

    python tools/loadtest/graph_stub.py 8099

Point the backend under test at it with WhatsApp__GraphBaseUrl=http://<host>:8099 so bot replies
are counted instead of being delivered to (fictional) numbers. Optional latency: STUB_DELAY_MS.
"""
import json
import os
import sys
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DELAY = int(os.environ.get("STUB_DELAY_MS", "150")) / 1000
counts = {"messages": 0}
lock = threading.Lock()


class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        self.rfile.read(length)
        time.sleep(DELAY)
        with lock:
            counts["messages"] += 1
        body = json.dumps({
            "messaging_product": "whatsapp",
            "contacts": [{"input": "stub", "wa_id": "stub"}],
            "messages": [{"id": f"wamid.stub.{uuid.uuid4().hex}"}],
        }).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        body = json.dumps({"data": [], "sent": counts["messages"]}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *_):
        pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8099
    print(f"Graph stub on :{port} (delay {DELAY * 1000:.0f} ms). GET / shows how many sends it received.")
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
