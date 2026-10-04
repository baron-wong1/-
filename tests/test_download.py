"""Verify segmented component transport against a loopback HTTP server."""
import importlib.util
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

spec = importlib.util.spec_from_file_location('component_download', Path(__file__).resolve().parents[1] / 'scripts/download-component.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def test_component_ranges_assemble_exact_bytes(tmp_path):
    payload = bytes(range(256)) * 81
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            start, end = map(int, self.headers['Range'].removeprefix('bytes=').split('-'))
            end = min(end, len(payload) - 1)
            part = payload[start:end + 1]
            self.send_response(206)
            self.send_header('Content-Range', f'bytes {start}-{end}/{len(payload)}')
            self.send_header('Content-Length', str(len(part)))
            self.end_headers()
            self.wfile.write(part)
        def log_message(self, *args):
            pass
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        target = tmp_path / 'component.bin'
        module.download(f'http://127.0.0.1:{server.server_port}/component', target, chunk_size=4096)
        assert target.read_bytes() == payload
        assert not target.with_suffix('.bin.partial').exists()
    finally:
        server.shutdown()
        thread.join()
        server.server_close()


def test_component_download_requires_tls(tmp_path):
    with pytest.raises(ValueError, match='HTTPS'):
        module.download('http://example.com/component', tmp_path / 'component.bin')
