"""已停止的英雄夹具副本上验证升级、五选角、英雄恢复与 GM 登录确认。"""
from pathlib import Path
import argparse
import json
import os
import shutil
import socket
import sqlite3
import struct
import subprocess
import tempfile
import time
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-version', type=Path, required=True)
parser.add_argument('--program-root', type=Path, default=Path(__file__).resolve().parents[2])
parser.add_argument('--reference-version', type=Path, default=Path(__file__).resolve().parents[2] / 'data/versions/default')
parser.add_argument('--output', type=Path)
parser.add_argument('--skip-selection', action='store_true', help='仅用于继续已独立通过的选角检查')
args = parser.parse_args()
source = args.program_root.resolve()
reference = args.reference_version.resolve()
old = args.source_version.resolve()
assert '/mir2-heroes.' in str(old), '只接受隔离英雄夹具，禁止复制正式库'
assert not subprocess.run(['lsof', str(old / 'runtime/mir2.sqlite3')], capture_output=True).stdout, '源库仍被使用'
root = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix='mir2-recovery.'))
root.mkdir(parents=True, exist_ok=True)
version = root / 'mir2-heroes.recovery'
assert not version.exists(), '拒绝覆盖已运行的验证资料'
shutil.copytree(old, version, symlinks=True, ignore=shutil.ignore_patterns('backups', 'cache', 'logs', '*.lock'))

def port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]

env = os.environ | {'MIR2_PROJECT_ROOT': str(source), 'MIR2_DATA_ROOT': str(version),
    'MIR2_RUNTIME_ROOT': str(version / 'runtime'), 'MIR2_GAME_PORT': str(port()), 'MIR2_RESOURCE_PORT': str(port()),
    'DOTNET_ENVIRONMENT': 'Development', 'ASPNETCORE_ENVIRONMENT': 'Development'}
compat = source / 'tests/Compatibility/bin/Debug/net8.0/Compatibility.dll'
server_dll = source / 'src/Server/bin/Debug/net8.0/Mir2.Server.dll'

def run(command, name, extra=None, timeout=240):
    with (root / (name + '.log')).open('w') as log:
        subprocess.run(command, cwd=source, env=env | (extra or {}), stdout=log, stderr=subprocess.STDOUT, timeout=timeout, check=True)
    output = (root / (name + '.log')).read_text()
    assert 'ERROR:' not in output and 'FAIL ' not in output, name
    print('PASS ' + name, flush=True)
    return output

def start(name):
    with (root / (name + '.log')).open('w') as log:
        process = subprocess.Popen(['dotnet', str(server_dll), f'--mir2-instance={source}:{version}/runtime:server'],
            cwd=source, env=env, stdout=log, stderr=subprocess.STDOUT)
    for _ in range(180):
        assert process.poll() is None, name + ' 启动失败'
        try:
            urllib.request.urlopen('http://127.0.0.1:' + env['MIR2_RESOURCE_PORT'] + '/health', timeout=1)
            if any(v in (root / (name + '.log')).read_text() for v in ['Network Started', '网络已启动']):
                return process
        except OSError:
            pass
        time.sleep(.5)
    process.terminate()
    process.wait(timeout=90)
    raise TimeoutError(name)

def native(name, flags, cache='mir2-heroes.cache'):
    time.sleep(5.2)
    run(['/Applications/Godot_mono.app/Contents/MacOS/Godot', '--path', str(source / 'src/Client'), '--max-fps', '60', '--',
        '--server=http://127.0.0.1:' + env['MIR2_RESOURCE_PORT'], f'--screenshot={root}/{name}.png'] + flags, name,
        {'MIR2_CLIENT_CACHE': str(root / cache), 'MIR2_CLIENT_SETTINGS': str(root / (name + '.json'))})

process = None
try:
    before = json.loads(run(['dotnet', str(compat), '--hero-recovery-audit'], 'legacy-before').splitlines()[-1])
    process = start('legacy-upgrade')
    process.terminate(); process.wait(timeout=90); process = None
    after = json.loads(run(['dotnet', str(compat), '--hero-recovery-audit'], 'legacy-after').splitlines()[-1])
    assert before == after and before['Heroes'] > 0, '完整旧账户/角色/英雄升级改变资料'
    backups = list((version / 'runtime/backups').glob('player-upgrade-*/mir2.sqlite3'))
    assert backups, '缺少升级前完整 SQLite 备份'
    with sqlite3.connect(backups[0]) as db:
        original = db.execute("SELECT data FROM snapshots WHERE name LIKE '%MirADB'").fetchone()[0]
        assert struct.unpack('<ii', original[:8]) == (117, 2), '升级备份不是原 v2 存档'
        assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    run(['dotnet', str(compat), '--hero-recovery-fixture'], 'recovery-fixture')
    shutil.copy2(reference / 'assets/maps/R001.map', version / 'assets/maps/R001.map')
    scripts = version / 'world/Envir/SystemScripts/00Default'
    shutil.copytree(reference / 'world/Envir/SystemScripts/00Default/Login', scripts / 'Login', dirs_exist_ok=True)
    shutil.copy2(Path(__file__).with_name('GmLogin.txt'), scripts / 'Login.txt')
    with (version / 'world/Envir/NPCs/00Default.txt').open('a') as file:
        file.write('\n#INSERT [SystemScripts\\00Default\\Login.txt]\n')
    run(['dotnet', str(compat), '--gm-login-checks'], 'gm-script')
    process = start('recovery-first')
    if not args.skip_selection:
        native('selection-five', ['--verify-class-selection'], 'mir2-native.cache')
        # 第六次创建沿原规则触发 IP 保护；新隔离进程继续其余玩法，不修改保护规则。
        process.terminate(); process.wait(timeout=90); process = None
        process = start('recovery-after-selection')
    native('gm-normal', ['--verify-gm-login', '--gm-login-action=normal'])
    native('gm-cancel', ['--verify-gm-login', '--gm-login-action=cancel'])
    native('gm-close', ['--verify-gm-login', '--gm-login-action=close'])
    native('gm-enter', ['--verify-gm-login', '--gm-login-action=enter'])
    record = f'--hero-recovery-record={root}/hero-assets.json'
    native('hero-death', ['--verify-hero-recovery', '--hero-recovery-stage=death', record])
    process.terminate(); process.wait(timeout=90); process = None
    process = start('recovery-second')
    native('hero-recover', ['--verify-hero-recovery', '--hero-recovery-stage=recover', record])
    process.terminate(); process.wait(timeout=90); process = None
    process = start('recovery-third')
    native('hero-saved', ['--verify-hero-recovery', '--hero-recovery-stage=saved', record])
finally:
    if process is not None:
        process.terminate(); process.wait(timeout=90)
with sqlite3.connect(version / 'runtime/mir2.sqlite3') as db:
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS hero recovery, five selection slots, GM login and full restart: ' + str(root), flush=True)
