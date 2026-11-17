"""隔离原TCP：保留旧猎杀，送信后主动访问两图三石柱，再交付并核对升级、资产与重启。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-progression-')).resolve();data=root/'version-a'
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
    raise AssertionError('主线推进等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
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
    profile=call(base,{'name':'ProgressCheck','class':0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-progression',str(cid)],cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-progression-fixture.json').read_text());start()
    initial=call('/management/admin/characters/'+str(cid));assert initial['activeQuests']==[1] and initial['completedQuests']==[] and initial['level']==10
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    samples=[];seen_carry=False;maps=set();markers=set()
    def progressed():
        global seen_carry
        detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid));runtime=detail.get('runtime')
        assert not detail['row']['lastError'],detail
        if runtime is None:return False
        markers.update(index for index in range(1,4) if any(task.startswith(f'石柱{index} ') and task.strip()!=f'石柱{index}' for task in runtime['tasks']))
        maps.add(character['mapId']);items=character['inventory']
        assert {fixture['weaponId'],fixture['protectedId']}<={int(i['uniqueId']) for i in items},character
        assert character['activeQuests'].count(1)==1 and 1 not in character['completedQuests'],'原猎杀任务被丢弃或被伪造完成'
        letter=[i for i in items if i['itemId']==fixture['token']]
        if letter:assert sum(i['count'] for i in letter)==1;seen_carry=True
        combat=runtime['combat'];assert combat['stats']['values']['HP']==runtime['maxHp'],runtime
        skill=next(s for s in combat['skills'] if s['spell']==1)
        assert skill['level']==1 and skill['experience']>=23 and skill['requiredExperience']>0,combat
        if 3 in character['completedQuests']:assert 2 in character['completedQuests']
        samples.append({'utc':time.time(),'detail':detail,'character':character})
        acknowledged=sum(e['kind']=='asset' and e['text']=='quest:成功' for e in detail['events'])>=4
        return (detail,character) if character['completedQuests']==[2,3] and acknowledged else False
    try:detail,character=wait(progressed,120)
    finally:(root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
    assert seen_carry and maps=={fixture['origin'],fixture['remote']},(seen_carry,maps)
    assert markers=={1,2,3},markers
    assert character['level']>10 and detail['runtime']['gold']==333,(character,detail['runtime'])
    assert not any(i['itemId']==fixture['token'] for i in character['inventory']),character
    assert sum(e['kind']=='asset' and e['text']=='quest:成功' for e in detail['events'])>=4,detail
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
    stored=call('/management/admin/characters/'+str(cid))
    assert stored['completedQuests']==[2,3] and stored['activeQuests']==[1] and stored['level']==character['level'],stored
    assert {fixture['weaponId'],fixture['protectedId']}<={int(i['uniqueId']) for i in stored['inventory']}
    assert not any(i['itemId']==fixture['token'] for i in stored['inventory'])
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    def restored():
        d=call(base+'/'+ident);runtime=d.get('runtime')
        if runtime is None:return False
        assert runtime['gold']==333 and runtime['combat']['skills'][0]['level']==1,d
        return True
    wait(restored)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print(f'PASS 原TCP主线推进：保留100杀原任务，送信后跨两图触发521/522/523再交付2→3，333金币/等级10→{character["level"]}、信件移除、原资产编号及技能熟练度；完整重启/SQLite完整性。')
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Progression fixture: '+str(root))
