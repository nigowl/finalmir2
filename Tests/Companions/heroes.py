"""独立版本原TCP：首英雄创建/激活/购装学书、30/40档真实日常奖励、60秒恢复与重启。"""
import argparse, datetime, hashlib, json, os, pathlib, shutil, socket, sqlite3, subprocess, tempfile, time, urllib.error, urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-version', required=True, type=pathlib.Path, help='已停止且安装九项盟重日常的测试版本')
parser.add_argument('--program-root', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2])
parser.add_argument('--character-id', type=int, default=1)
args = parser.parse_args()
repo = args.program_root.resolve(); source = args.source_version.resolve()
assert 'finalmir2-daily.' in str(source), '拒绝复制正式版本或未安装日常的来源'
assert subprocess.run(['lsof', str(source/'runtime/mir2.sqlite3')], capture_output=True).returncode == 1, '源SQLite仍被打开'
root = pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-heroes.')).resolve(); data = root/'version'; (data/'runtime').mkdir(parents=True)
print('隔离英雄/日常验证：'+str(root), flush=True)
shutil.copytree(source/'world', data/'world', symlinks=True)
shutil.copytree(source/'assets', data/'assets', symlinks=True)
with sqlite3.connect(f'file:{source}/runtime/mir2.sqlite3?mode=ro', uri=True) as original, sqlite3.connect(data/'runtime/mir2.sqlite3') as target:
    original.backup(target); assert target.execute('pragma integrity_check').fetchone()[0] == 'ok'
server = repo/'src/Server/bin/Debug/net8.0/Mir2.Server.dll'; compat = repo/'Tests/Compatibility/bin/Debug/net8.0/Compatibility.dll'
def port():
    with socket.socket() as listener: listener.bind(('127.0.0.1', 0)); return listener.getsockname()[1]
game, http = port(), port(); address = f'http://127.0.0.1:{http}'
env = os.environ | {'MIR2_PROJECT_ROOT': str(repo), 'MIR2_DATA_ROOT': str(data), 'MIR2_RUNTIME_ROOT': str(data/'runtime'),
    'MIR2_GAME_PORT': str(game), 'MIR2_RESOURCE_PORT': str(http), 'MIR2_BIND_ADDRESS': '127.0.0.1',
    'ASPNETCORE_ENVIRONMENT': 'Development', 'DOTNET_ENVIRONMENT': 'Development'}
version = hashlib.sha256(str(data).encode()).hexdigest().upper(); process = log = None; key = ''; starts = 0; cid = args.character_id
def fixture(mode):
    assert process is None, '夹具必须停服执行'
    subprocess.run(['dotnet', str(compat), '--companion-heroes-fixture', str(cid), mode], cwd=root, env=env, check=True, stdout=subprocess.DEVNULL)
    return json.loads((data/'runtime'/f'companion-heroes-{mode}.json').read_text())
def call(path, body=None, method=None):
    headers = {'Authorization': 'Bearer '+key, 'X-Mir2-Version': version}
    if body is not None: headers['Content-Type'] = 'application/json'
    request = urllib.request.Request(address+path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request, timeout=30) as response: return json.load(response)
    except urllib.error.HTTPError as error: raise AssertionError(f'{path}: {error.code}: '+error.read().decode()) from error
def wait(predicate, seconds=180):
    deadline = time.monotonic()+seconds
    while time.monotonic() < deadline:
        assert process.poll() is None, pathlib.Path(log.name).read_text()[-4000:]
        value = predicate()
        if value: return value
        time.sleep(.25)
    raise AssertionError('原TCP等待超时；证据：'+str(root))
def start():
    global process, log, key, starts
    starts += 1; log = (root/f'server-{starts}.log').open('w')
    process = subprocess.Popen(['dotnet', str(server), f'--mir2-instance={repo}:{data/"runtime"}:server'], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
    def ready():
        try: return call('/health')['ready']
        except OSError: return False
    wait(ready, 90); key = (data/'runtime/admin.key').read_text().strip()
def stop():
    global process, log
    process.terminate(); process.wait(60); assert process.returncode == 0, pathlib.Path(log.name).read_text()[-4000:]
    process = None; log.close(); log = None
def command(action):
    row = call(base+'/'+ident)['row']; return call(base+'/'+ident+'/command', {'action': action, 'version': row['profile']['version']})
def character(): return call('/management/admin/characters/'+str(cid))
def observe():
    detail = call(base+'/'+ident); role = character(); assert not detail['row']['lastError'], detail['row']
    with (root/'observations.jsonl').open('a') as out: out.write(json.dumps({'at': time.time(), 'role': role, 'detail': detail}, ensure_ascii=False)+'\n')
    return role, detail
def off():
    command('stop'); wait(lambda: not character()['online'], 45); stop()
try:
    tier30 = fixture('prepare'); start(); base = '/management/companions'
    first_page = call(base+'?page=0'); rows = first_page['items']
    for page in range(1, (first_page['total']+19)//20): rows += call(base+'?page='+str(page))['items']
    assert len(rows) == first_page['total']; row = next(row for row in rows if row['characterId'] == cid); ident = row['profile']['id']
    profile = row['profile'] | {'enabled': True, 'chat': False, 'acceptGroup': False, 'llmMode': 0,
        'schedule': row['profile']['schedule'] | {'timeZone': 'UTC', 'windows': [{'days': 127, 'allDay': True}], 'dailyMinutes': 120, 'sessionMinutes': 60, 'restMinutes': 0, 'jitterMinutes': 0}}
    call(base+'/'+ident, profile, 'PUT'); overview = call(base+'/overview')
    call(base+'/settings', {'settings': overview['settings'] | {'enabled': True, 'maxOnline': 1}}, 'PUT'); command('start')
    def completed30():
        role, detail = observe(); events = detail['events']
        return role if set(tier30['DailyIds']) <= set(role['completedQuests']) and any(event['text'] == 'buy:成功' for event in events) and sum(event['text'] == 'hero-equip:成功' for event in events) >= 2 and any(event['text'] == 'hero-use:成功' for event in events) else False
    first = wait(completed30, 240); off(); saved30 = fixture('audit')
    assert saved30['Heroes'] == 1 and saved30['Hero']['Class'] == 'Archer' and saved30['Hero']['HP'] > 0 and saved30['Seals'] == 0, saved30
    assert 'StraightShot' in saved30['Hero']['Spells'] and saved30['Hero']['Equipment'], saved30
    assert any(item['ItemIndex'] == tier30['HeroWeapon'] for item in saved30['Hero']['Equipment']), saved30
    assert sum(item['count'] for item in first['inventory'] if item['name'] == '回城卷轴') >= 100, first
    hero = saved30['Hero']; start(); command('start'); wait(lambda: character()['online']); command('pause')
    wait(lambda: call(base+'/'+ident)['row']['state'] == '已暂停'); off(); same = fixture('audit')
    assert all(same['Hero'][key] == hero[key] for key in ['Index', 'Name', 'Class', 'Spells', 'Equipment']), (saved30, same)
    assert same['CompletedQuests'] == saved30['CompletedQuests'] and same['Inventory'] == saved30['Inventory'] and same['Gold'] == saved30['Gold'], (saved30, same)
    tier40 = fixture('tier40'); start(); command('start')
    def completed40():
        role, detail = observe(); return role if set(tier40['DailyIds']) <= set(role['completedQuests']) else False
    second = wait(completed40, 240); off(); saved40 = fixture('audit')
    assert saved40['Gold']-saved30['Gold'] == 1_000_000, (saved30['Gold'], saved40['Gold'])
    assert saved40['Heroes'] == 1 and saved40['Hero']['Index'] == hero['Index'] and saved40['Hero']['Spells'] == hero['Spells'], saved40
    death = fixture('dead'); revive_at = datetime.datetime.fromisoformat(death['ReviveAtUtc'].replace('Z', '+00:00')).timestamp()
    start(); command('start')
    def recovered():
        role, detail = observe(); restored = [event for event in detail['events'] if event['text'] == 'hero-summon:成功']
        if restored:
            assert datetime.datetime.fromisoformat(restored[-1]['time'].replace('Z', '+00:00')).timestamp() >= revive_at-.1, '英雄冷却提前清除'
            return True
        assert not any(event['text'] == 'hero-create:成功' for event in detail['events']), '死亡后重新创建英雄'
        return False
    wait(recovered, 90); off(); recovered_state = fixture('audit')
    assert recovered_state['Heroes'] == 1 and recovered_state['Hero']['Index'] == hero['Index'] and recovered_state['Hero']['HP'] > 0, recovered_state
    assert recovered_state['Hero']['Equipment'] == saved40['Hero']['Equipment'] and recovered_state['Hero']['Spells'] == hero['Spells'], recovered_state
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db: assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
    (root/'result.json').write_text(json.dumps({'tier30': saved30, 'tier40': saved40, 'recovered': recovered_state}, ensure_ascii=False, indent=2))
    print('PASS 原TCP弓箭英雄创建/封印激活/共用包购装/普通学书，30/40档真实材料交付奖励，保存重启不重复与60秒恢复；'+str(root), flush=True)
finally:
    if process is not None: stop()
