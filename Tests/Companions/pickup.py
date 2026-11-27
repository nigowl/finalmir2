"""隔离原TCP：高节俭缺红药补武器、持续刷怪捡金币红蓝、穿戴掉落装备及保存重启。"""
import argparse, hashlib, json, os, pathlib, socket, sqlite3, subprocess, tempfile, time, urllib.request

parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--combat', action='store_true'); args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parents[2]
root = pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-pickup-')).resolve(); data = root/'version-a'
server = pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL', str(repo/'src/Server/bin/Debug/net8.0/Mir2.Server.dll')))
compat = repo/'Tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
def port():
    with socket.socket() as sock: sock.bind(('127.0.0.1', 0)); return sock.getsockname()[1]
game, http = port(), port(); address = f'http://127.0.0.1:{http}'
env = os.environ | {'MIR2_DATA_ROOT':str(data), 'MIR2_RUNTIME_ROOT':str(data/'runtime'), 'MIR2_GAME_PORT':str(game), 'MIR2_RESOURCE_PORT':str(http)}
version = hashlib.sha256(str(data).encode()).hexdigest().upper(); key = ''; process = None; log = None; starts = 0
def call(path, body=None, method=None):
    headers = {'Authorization':'Bearer '+key, 'X-Mir2-Version':version}
    if body is not None: headers['Content-Type'] = 'application/json'
    request = urllib.request.Request(address+path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request, timeout=15) as response: return json.load(response)
def wait(predicate, seconds=120):
    deadline = time.monotonic()+seconds
    while time.monotonic() < deadline:
        assert process.poll() is None, pathlib.Path(log.name).read_text()
        result = predicate()
        if result: return result
        time.sleep(.35)
    raise AssertionError('等待拾取/穿戴原TCP结果超时；'+str(root))
def start():
    global process, log, key, starts
    starts += 1; log = (root/f'server-{starts}.log').open('w')
    process = subprocess.Popen(['dotnet', str(server), f'--mir2-instance={repo}:{data/"runtime"}:server'], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
    def ready():
        try: return call('/health')['ready']
        except OSError: return False
    wait(ready); key = (data/'runtime/admin.key').read_text().strip()
def stop():
    global process, log
    process.terminate(); process.wait(60); assert process.returncode == 0, pathlib.Path(log.name).read_text()
    process = None; log.close(); log = None
try:
    reference = os.environ.get('MIR2_DATA_ROOT', str(repo/'data/versions/default'))
    subprocess.run(['dotnet', str(compat), '--fixture', str(data)], cwd=repo, env=os.environ|{'MIR2_DATA_ROOT':reference}, check=True)
    start(); base = '/management/companions'
    settings = call(base+'/overview')['settings']; call(base+'/settings', {'settings':settings|{'enabled':True}}, 'PUT')
    profile = call(base, {'name':'PickupWizard', 'class':1, 'gender':0, 'enabled':True, 'acceptGroup':False, 'chat':False,
        'traits':{'diligence':75,'frugality':95,'collection':60,'risk':30}, 'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}], 'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident = profile['id']
    def command(action):
        row = call(base+'/'+ident)['row']; return call(base+'/'+ident+'/command', {'action':action,'version':row['profile']['version']})
    command('start')
    def entered():
        row = call(base+'/'+ident)['row']; assert not row['lastError'], row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row = wait(entered); cid, aid = row['characterId'], row['accountId']; command('stop')
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']); stop()
    subprocess.run(['dotnet',str(compat),'--companion-pickup',str(cid)]+(['--combat'] if args.combat else []), cwd=root, env=env, check=True)
    fixture = json.loads((data/'runtime/companion-pickup-fixture.json').read_text()); start(); command('start'); samples = []
    initial_count = 20 if args.combat else 1
    def collected():
        character = call('/management/admin/characters/'+str(cid)); detail = call(base+'/'+ident)
        gold = call('/management/admin/accounts/'+str(aid))['gold']; assert not detail['row']['lastError'], detail
        samples.append({'character':character,'detail':detail,'gold':gold}); (root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
        items = character['inventory']; red = sum(i['count'] for i in items if i['itemId']==fixture['red']); blue = sum(i['count'] for i in items if i['itemId']==fixture['blue'])
        worn = {i['itemId'] for i in items if i['grid']=='Equipment'}
        return character if fixture['blade'] in worn and fixture['ring'] in worn and red>initial_count and blue>20 and gold>(0 if args.combat else 306) else False
    character = wait(collected,180); command('pause'); wait(lambda:call(base+'/'+ident)['row']['state']=='已暂停')
    character = call('/management/admin/characters/'+str(cid)); gold = call('/management/admin/accounts/'+str(aid))['gold']; events = call(base+'/'+ident)['events']
    if not args.combat: assert any(e['text']=='buy:成功' for e in events), events
    weapon = next(i for i in character['inventory'] if i['grid']=='Equipment' and i['itemId']==fixture['blade'])
    if args.combat: assert int(weapon['uniqueId']) == fixture['weaponId'], (weapon,fixture)
    command('stop'); wait(lambda:not call('/management/admin/characters/'+str(cid))['online']); stop(); start()
    saved = call('/management/admin/characters/'+str(cid)); assert saved['inventory']==character['inventory']
    assert call('/management/admin/accounts/'+str(aid))['gold']==gold
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db: assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP'+('持续刷怪间隙捡金红蓝及掉落穿戴' if args.combat else '高节俭一红药空武器槽实际购装、穿戴及捡金红蓝')+'，原编号、真实资产及完整重启保持；'+str(root), flush=True)
finally:
    if process is not None and process.poll() is None: process.terminate(); process.wait(60)
    if log is not None: log.close()
    print('Pickup fixture: '+str(root), flush=True)
