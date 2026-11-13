"""隔离原TCP验证长冷却保留射程，以及缺蓝时仍能走近普攻；不修改正式数据。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-ranged-')).resolve()
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
    raise AssertionError('远程战斗等待超时')
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
    profile=call(base,{'name':'RangedWizard','class':1,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id']
    call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row']
        if row['lastError']:raise AssertionError(row)
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered,40);character_id=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(character_id))['online'],15)
    observations=[]
    for empty in [False,True]:
        stop()
        command=['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-ranged',str(character_id)]
        if empty:command.append('--empty-mana')
        subprocess.run(command,cwd=root,env=env,check=True)
        start();initial=call('/management/admin/characters/'+str(character_id));origin=(initial['x'],initial['y'])
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
        samples=[];previous_mp=0 if empty else 160;casts=0
        def killed():
            global previous_mp,casts
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id))
            assert not detail['row']['lastError'],detail
            if not character['online']:return False
            point=(character['x'],character['y'])
            samples.append({'x':point[0],'y':point[1],'mp':character['mp'],'hp':character['hp'],'experience':character['experience']})
            if character['mp']<=previous_mp-5:casts+=1
            previous_mp=character['mp']
            assert not any(e['kind']=='death' for e in detail['events']),detail
            if not empty and not character['experience']:assert point==origin,('冷却时主动走近目标',origin,samples)
            return character if character['experience']>0 else False
        result=wait(killed,90)
        assert (any((s['x'],s['y'])!=origin for s in samples) if empty else casts>=2),(empty,casts,samples)
        observations.append({'emptyMana':empty,'casts':casts,'samples':samples})
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(character_id))['online'],15)
        print(f'PASS 原TCP{"缺蓝仍走近普攻击杀" if empty else "5秒冷却保持原地射程，至少两次实际扣蓝后击杀"}：经验{result["experience"]}，施法扣蓝{casts}次。',flush=True)
    (root/'ranged-observation.json').write_text(json.dumps(observations,ensure_ascii=False))
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Ranged fixture: '+str(root),flush=True)
