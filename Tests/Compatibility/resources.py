#!/usr/bin/env python3
"""资源回归：空缓存、阻断基础素材、真实重连；可在隔离服验证建角和进图。"""
import argparse
import http.server
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument("--server", default="http://127.0.0.1:5080")
parser.add_argument("--connection-failures", action="store_true")
parser.add_argument("--fixture-world", action="store_true", help="仅用于可写的隔离服，将注册并创建测试角色")
parser.add_argument("--published-client", type=Path, help="搬出源码的发布客户端目录")
args = parser.parse_args()
if args.fixture_world and args.server == "http://127.0.0.1:5080":
    parser.error("--fixture-world 必须指定隔离服地址")
root = Path(__file__).resolve().parents[2]
evidence = Path(tempfile.mkdtemp(prefix="mir2-resources."))
manifest_requests = []
blocked = []
map_requests = []


class Proxy(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        if self.path == "/assets/manifest":
            manifest_requests.append(time.monotonic())
            if args.connection_failures and len(manifest_requests) <= 5:
                self.send_error(503, "Server unavailable")
                return
            if not args.connection_failures:
                time.sleep(2)
        path, _, query = self.path.partition("?")
        resource = urllib.parse.unquote(path).split("/", 3)[-1].lower()
        if resource.startswith(("frame/chrsel", "frame/title", "frame/prguse2")) or (
            resource == "frame/prguse" and query in {"index=0", "index=1", "index=65", "index=995", "index=1084"}
        ) or resource in {"sound/103.wav", "sound/login2.wav", "sound/select2.wav"}:
            blocked.append(self.path)
            self.send_error(404, "Packaged art is unavailable over HTTP")
            return
        if resource.startswith("map/"):
            map_requests.append(self.path)
        if self.path == "/game/item-catalog":
            time.sleep(0.6)
        try:
            response = urllib.request.urlopen(args.server + self.path, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            data = response.read()
            self.send_response(response.status)
            for header in ("Content-Type", "X-Content-SHA256"):
                if response.headers.get(header):
                    self.send_header(header, response.headers[header])
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

    def log_message(self, *_args):
        pass


http.server.ThreadingHTTPServer.request_queue_size = 64
proxy = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Proxy)
thread = threading.Thread(target=proxy.serve_forever, daemon=True)
thread.start()
try:
    if not args.published_client:
        subprocess.run(["dotnet", "build", "src/Client/Client.Godot.csproj", "-p:RequireClientStartupAssets=true",
                        "--verbosity", "quiet"], cwd=root, check=True)
    env = dict(os.environ, MIR2_CLIENT_CACHE=str(evidence / "cache"), MIR2_CLIENT_SETTINGS=str(evidence / "settings.json"))
    godot = env.get("GODOT_BIN", "/Applications/Godot_mono.app/Contents/MacOS/Godot")
    check = "--verify-connection-failures" if args.connection_failures else "--verify-resource-startup"
    launcher = [str(args.published_client.resolve() / "start.sh")] if args.published_client else [godot, "--path", str(root / "src/Client")]
    smoke_args = ["--fixture-world"] if args.fixture_world else []
    with (evidence / "client.log").open("w") as output:
        mode = ["--headless"] if args.connection_failures else []
        result = subprocess.run([*launcher, *mode, "--max-fps", "60",
            "--log-file", str(evidence / "godot.log"), "--", check,
            f"--server=http://127.0.0.1:{proxy.server_port}", f"--screenshot={evidence / 'client.png'}", *smoke_args],
            cwd=evidence, env=env, stdout=output, stderr=subprocess.STDOUT, timeout=180)
    log = (evidence / "client.log").read_text()
    print(log)
    marker = "PASS native connection retry" if args.connection_failures else "PASS native resource startup"
    if result.returncode or marker not in log:
        raise SystemExit(result.returncode or 1)
    assert not blocked, blocked
    if args.fixture_world:
        assert "PASS native resource world" in log and map_requests, map_requests
    if args.connection_failures:
        assert len(manifest_requests) in (6, 7), manifest_requests
        intervals = [b - a for a, b in zip(manifest_requests[:4], manifest_requests[1:5])]
        assert all(29.9 <= interval <= 32 for interval in intervals), intervals
        print(f"PASS connection schedule: five attempts, intervals={intervals}, manual retry and exit.")
finally:
    proxy.shutdown()
    proxy.server_close()
    print(f"Resource evidence: {evidence}")
