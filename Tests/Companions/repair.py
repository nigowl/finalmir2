"""隔离原TCP验证主动跨图修理、真实税价和装备恢复；不修改正式数据。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-repair-')).resolve()
data=root/'version-a'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper()
process=None;log=None;key='';starts=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('主动修理等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':str(repo/'data/versions/default')},check=True)
    start();base='/management/companions'
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'RepairWarrior','class':0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row']
        if row['lastError']:raise AssertionError(row)
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered,40);character_id=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(character_id))['online'],15);stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-repair',str(character_id)],cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-repair-fixture.json').read_text())
    start();row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    samples=[];assets=set();maps=set();planned=False
    def repaired():
        global planned
        detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id));runtime=detail['runtime'] or {}
        assert not detail['row']['lastError'],detail
        assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        assert not any(e['kind']=='death' for e in detail['events']),detail
        goal=runtime.get('goal') or {}
        if goal.get('key','').startswith('repair/'):
            planned=True;assert goal['mapId']==fixture['destination'] and goal['reason'].endswith('：Native Repair'),goal
        maps.add(character['mapId']);items={int(i['uniqueId']):i for i in character['inventory']}
        assert items[fixture['spareId']]['durability']==1000 and items[fixture['spareId']]['grid']=='Inventory',items
        assert not any(i['itemId']==fixture['temptingId'] for i in items.values()),items
        samples.append({'gold':runtime.get('gold'),'map':character['mapId'],'goal':goal,'inventory':character['inventory']})
        return detail if runtime and all(items.get(i['id'],{}).get('grid')=='Equipment' and items[i['id']]['slot']==i['slot'] and items[i['id']]['durability']==i['maxDura'] and items[i['id']]['maxDurability']==i['maxDura'] for i in fixture['repairs']) else False
    detail=wait(repaired,70)
    assert planned and {fixture['origin'],fixture['destination']}<=maps,(planned,maps)
    assert all(kind+':成功' in assets for kind in ['remove','repair','equip']),assets
    assert 'buy:成功' not in assets and 'sell:成功' not in assets,assets
    expected=fixture['gold']-sum(i['cost'] for i in fixture['repairs'])
    assert detail['runtime']['gold']==expected,(detail,fixture)
    for _ in range(10):time.sleep(.3);repaired()
    assert samples[-1]['gold']==expected,samples[-1]
    (root/'repair-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(character_id))['online'],15);stop()
    start();stored=call('/management/admin/characters/'+str(character_id));stored_items={int(i['uniqueId']):i for i in stored['inventory']}
    assert all(stored_items[i['id']]['grid']=='Equipment' and stored_items[i['id']]['slot']==i['slot'] and stored_items[i['id']]['durability']==stored_items[i['id']]['maxDurability']==i['maxDura'] for i in fixture['repairs']),stored
    assert stored_items[fixture['spareId']]['durability']==1000 and stored_items[fixture['spareId']]['grid']=='Inventory',stored
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print(f'PASS 原TCP药卷齐全仍主动跨图修理：跳过错误类型/超预算NPC，125%原税价扣{fixture["gold"]-expected}金币，零耐久衣服和低耐久武器原编号重穿，备装未修/无顺购，正常保存及SQLite完整性。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Repair fixture: '+str(root),flush=True)
