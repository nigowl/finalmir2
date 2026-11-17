"""远处NPC、公开跨图、真实任务掉落和返回交付；仅使用独立临时版本。"""
import hashlib,json,os,pathlib,socket,subprocess,tempfile,time,urllib.request,sqlite3

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companion-guidance-')).resolve()
data=root/'version-a'
def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=free_port(),free_port()
address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
instance=hashlib.sha256(str(data).encode()).hexdigest().upper()
key='';process=None;log=None
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':instance}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError((root/'server.log').read_text())
        result=predicate()
        if result:return result
        time.sleep(.4)
    raise AssertionError('引导任务等待超时')
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data),'--guided-companions'],env=os.environ|{'MIR2_DATA_ROOT':str(repo/'data/versions/default')},check=True)
    log=open(root/'server.log','w')
    process=subprocess.Popen(['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'),f'--mir2-instance={repo}:{data / "runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip()
    base='/management/companions'
    overview=call(base+'/overview');preset=overview['presets'][2]
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'引导测试员','class':0,'gender':0,'goal':'任务优先','presetKey':preset['key'],'presetRevision':preset['revision'],'traits':preset['traits']|{'exploration':0},
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    report=call(base+'/world-checks')
    assert report['questCount']==3 and not any(q['warnings'] for q in report['quests']),report
    maps=set();positions=set();reasons=set();assets=set()
    def completed():
        detail=call(base+'/'+ident);row=detail['row'];maps.add(row['map']);positions.add((row['map'],row['x'],row['y']));reasons.add(row['reason'])
        assets.update(event['text'] for event in detail['events'] if event['kind']=='asset')
        if row['lastError']:raise AssertionError(detail)
        if detail['runtime'] is not None:
            runtime=detail['runtime'];combat=runtime.get('combat')
            assert combat is not None,'运行详情缺少原引擎战斗属性与技能熟练度'
            assert combat['stats']['values']['HP']==runtime['maxHp'] and combat['stats']['values']['MP']==runtime['maxMp'],combat
            assert isinstance(combat['skills'],list) and all(skill['name'] and skill['experience']>=0 for skill in combat['skills']),combat
        if not row['characterId']:return False
        character=call('/management/admin/characters/'+str(row['characterId']))
        return character if {1,2,3}.issubset(character['completedQuests']) and 'sell:成功' in assets else False
    character=wait(completed,300)
    assert len(maps-{''})>=2 and len(positions)>25,(maps,len(positions))
    assert any('任务物品' in reason for reason in reasons),reasons
    assert any('割取尸体掉落' in reason for reason in reasons),reasons
    assert character['level']>=2 and call(base+'/'+ident)['row']['profile']['enabled']
    assert all('人格允许的无进度' not in reason for reason in reasons)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0)
    process.terminate();process.wait(60);assert process.returncode==0;process=None;log.close();log=None
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:assert database.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print(f'PASS server-guided companion: three quests (collection/carry/visit), actual harvest drops, meat sale receipt, two maps, {len(positions)} positions, normal save and SQLite integrity.')
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Guidance fixture: '+str(root))
