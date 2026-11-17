"""仅接续ui.sh的临时版本，验证完整宿主重启不会复活已删除脚本。"""
import json, os, pathlib, sqlite3, subprocess, sys, time, urllib.request

root = pathlib.Path(sys.argv[1]).resolve()
repo = pathlib.Path(__file__).resolve().parents[2]
assert root.name.startswith('mir2-admin-ui.') and (root/'server.log').is_file()
data = root/'data'
assert pathlib.Path(os.environ['MIR2_DATA_ROOT']).resolve() == data
source = data/'import/Crystal Database/Envir/Drops/不应复活.txt'
source.parent.mkdir(parents=True, exist_ok=True)
source.write_text('; 已由运营清理的旧导入文件\n')
target = data/'world/Envir/Drops/不应复活.txt'
target.write_text(source.read_text())
target.unlink()
key = (data/'runtime/admin.key').read_text().strip()
address = os.environ['MIR2_SERVER']

def call(path, method='GET'):
    request = urllib.request.Request(address+path, method=method, headers={'Authorization': 'Bearer '+key})
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.load(response)

for cycle in (1, 2):
    with (root/f'drop-startup-{cycle}.log').open('w') as log:
        child = subprocess.Popen(['dotnet', str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'),
            f'--mir2-instance={repo}:{data / "runtime"}:server'], cwd=repo, env=os.environ, stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic()+60
            while True:
                if child.poll() is not None:
                    raise AssertionError((root/f'drop-startup-{cycle}.log').read_text())
                try:
                    if call('/health')['ready']:
                        break
                except OSError:
                    pass
                assert time.monotonic() < deadline, '临时宿主启动超时'
                time.sleep(.2)
            assert not target.exists(), f'第{cycle}次完整启动复活旧掉落'
            call('/management/stop', 'POST')
        finally:
            child.terminate()
            child.wait(timeout=60)
        assert child.returncode == 0
    assert not target.exists()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:
        assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS 首次导入后的两次完整宿主启动/正常保存退出，旧掉落不复活，SQLite完整。')
