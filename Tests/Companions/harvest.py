"""隔离原TCP：已齐任务材料不绕过负重线，真正缺少的尸体任务物仍可采集。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-harvest-')).resolve();data=root/'version-a'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        result=predicate()
        if result:return result
        time.sleep(.2)
    raise AssertionError('尸体必要采集等待超时')
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
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'HarvestBudget','class':0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    for needed in [False,True]:
        subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-harvest',str(cid)]+(['--needed'] if needed else []),cwd=root,env=env,check=True)
        fixture=json.loads((data/'runtime/companion-harvest-fixture.json').read_text());start()
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
        samples=[]
        def sample():
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid));runtime=detail['runtime'] or {}
            assert not detail['row']['lastError'],detail
            assert not any(e['kind']=='death' for e in detail['events']),detail
            assert set(fixture['items'])<={int(i['uniqueId']) for i in character['inventory']},character
            samples.append({'detail':detail,'character':character})
            return runtime,character
        wait(lambda:sample()[0].get('experience',0)>0,35)
        if needed:
            wait(lambda:any(i['itemId']==fixture['token'] for i in sample()[1]['inventory']),35)
            assert any(i['itemId']==fixture['junk'] for i in samples[-1]['character']['inventory']),samples[-1]
        else:
            for _ in range(30):
                time.sleep(.3);runtime,character=sample()
                assert runtime['bagWeight']>runtime['maxBagWeight'],runtime
                assert not any(i['itemId']==fixture['junk'] for i in character['inventory']),('已齐材料仍触发超负重采集副产物',runtime['tasks'],runtime['bagWeight'])
        (root/('needed-samples.json' if needed else 'full-samples.json')).write_text(json.dumps(samples,ensure_ascii=False))
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    start();stored=call('/management/admin/characters/'+str(cid))
    assert any(i['itemId']==fixture['token'] for i in stored['inventory']) and any(i['itemId']==fixture['junk'] for i in stored['inventory']),stored
    assert set(fixture['items'])<={int(i['uniqueId']) for i in stored['inventory']},stored
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP实际击杀后：超重且此材料已齐时9秒无采集副产物；真正缺少任务物时原Harvest获物和副产物，原编号/正常重启/SQLite完整性。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Harvest fixture: '+str(root))
