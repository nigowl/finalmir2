"""隔离原TCP验证智能托管，故障信号只发送给本测试宿主的直接陪玩子进程。"""
import datetime,hashlib,json,os,pathlib,signal,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-supervision-')).resolve();data=root/'version'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';sequence=0
base='/management/companions';samples=[]
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=20) as response:return json.load(response)
def wait(predicate,seconds=100):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        value=predicate()
        if value:return value
        time.sleep(.4)
    raise AssertionError('智能托管验证等待超时：'+json.dumps(call(base),ensure_ascii=False))
def start():
    global process,log,key,sequence
    log=open(root/f'server-{sequence}.log','w');sequence+=1
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    call('/management/stop',method='POST');process.terminate();process.wait(60);assert process.returncode==0
    process=None;log.close();log=None
def records():
    with sqlite3.connect('file:'+str(data/'runtime/mir2.sqlite3')+'?mode=ro',uri=True) as db:
        return {r['Value']['Profile']['Name']:r['Value'] for (value,) in db.execute("select data from snapshots where name like 'companions/profiles/%'") if (r:=json.loads(value))}
def worker_pid():
    lines=subprocess.check_output(['ps','-axo','pid=,ppid=,command='],text=True).splitlines()
    children=[int(p[0]) for line in lines if len(p:=line.split(None,2))==3 and int(p[1])==process.pid and 'Mir2.Companion' in p[2]]
    assert len(children)==1,children
    return children[0]
def row(name):return next(r for r in call(base)['items'] if r['profile']['name']==name)
def assert_protected():
    values=records()
    assert not values['保留人工']['Profile']['Enabled'] and values['保留人工']['RecoveryDisabled']
    assert not values['保留资产']['Profile']['Enabled'] and '回执未知' in values['保留资产']['Error']
    assert values['保留资产']['RecoveryAt'] is None and values['保留人工']['RecoveryAt'] is None
def online_pair():
    assert call('/health')['ready'];assert_protected()
    a,b=row('托管主角'),row('恢复旧错')
    current=call('/management/status')['players'];assert len(current)<=2
    samples.append({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'rows':[a,b],'players':len(current)})
    return a if len(current)==2 and all(r['profile']['enabled'] and r['characterId']>0 and not r['lastError'] and r['state']!='托管恢复中' for r in [a,b]) else False
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start()
    settings=call(base+'/overview')['settings'];assert not settings['smartRecovery']
    for name in ['托管主角','恢复旧错','保留人工','保留资产']:
        call(base,{'name':name,'class':0,'gender':0,'enabled':False,'scheduled':False,'chat':False,'acceptGroup':False,'deathLimit':3,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':120}})
    call(base+'/settings',{'settings':settings|{'enabled':True,'smartRecovery':True,'smartScheduling':False,'maxOnline':2,'reservePlayers':1}},'PUT')
    if os.environ.get('MIR2_COMPANION_UI')=='1':
        admin_port=port()
        with open(root/'admin.log','w') as admin_log:
            admin=subprocess.Popen(['dotnet',str(repo/'src/Admin/bin/Release/net8.0/Mir2.Admin.dll'),f'--mir2-instance={repo}:{data/"runtime"}:admin'],cwd=root,
                env=env|{'MIR2_ADMIN_PORT':str(admin_port),'MIR2_SERVER':address},stdout=admin_log,stderr=subprocess.STDOUT)
            try:subprocess.run(['node',str(repo/'tests/Companions/supervision-ui.cjs'),str(root),f'http://127.0.0.1:{admin_port}',address],env=os.environ,check=True)
            finally:admin.terminate();admin.wait(30)
    stop()
    subprocess.run(['dotnet',str(compat),'--companion-supervision-setup'],cwd=root,env=env,check=True)
    start();wait(online_pair)
    second=row('恢复旧错');call(base+'/'+second['profile']['id']+'/command',{'action':'pause','version':second['profile']['version']})
    original=records();identities={name:(r['AccountId'],r['CharacterId']) for name,r in original.items()}
    deadline=original['托管主角']['SessionUntil'];usage=sum(original['托管主角']['OnlineSeconds'].values())
    assert original['托管主角']['SessionDeaths']==2
    print('PASS 旧技术故障自动恢复，人工停用/资产未知保持；原TCP创建登录两角色，本次死亡次数为2。',flush=True)
    killed=worker_pid();os.kill(killed,signal.SIGKILL)
    wait(lambda:records()['托管主角']['RecoveryAt'],20)
    pending=records()['托管主角'];assert pending['Profile']['Enabled'] and pending['Desired']=='pause'
    stop();start();assert call(base+'/overview')['settings']['smartRecovery']
    persisted=records()['托管主角'];assert persisted['RecoveryAt']==pending['RecoveryAt'] and persisted['RecoveryAttempts']==pending['RecoveryAttempts']
    wait(online_pair,130)
    assert worker_pid()!=killed
    recovered=records()['托管主角'];assert recovered['SessionUntil']==deadline and recovered['SessionDeaths']==2
    assert sum(recovered['OnlineSeconds'].values())>=usage
    print('PASS 子进程异常退出与宿主重启后自动恢复，退避计时/暂停意愿/本次截止/死亡预算和用量保持。',flush=True)
    frozen=worker_pid();os.kill(frozen,signal.SIGSTOP)
    try:
        wait(lambda:records()['托管主角']['RecoveryAt'],65)
        wait(online_pair,200)
        assert worker_pid()!=frozen
    finally:
        try:os.kill(frozen,signal.SIGCONT)
        except ProcessLookupError:pass
    assert records()['托管主角']['SessionDeaths']==2
    assert records()['托管主角']['RecoveryAttempts']>recovered['RecoveryAttempts']
    print('PASS 执行组件卡死自动清理重建并恢复，两次故障退避递增，游戏宿主持续健康。',flush=True)
    master=row('托管主角');call(base+'/'+master['profile']['id']+'/command',{'action':'stop','version':master['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(master['characterId']))['online'],40)
    until=time.monotonic()+35
    while time.monotonic()<until:
        assert not call('/management/admin/characters/'+str(master['characterId']))['online'];assert_protected();time.sleep(.5)
    legacy=row('恢复旧错');name='companions/profiles/'+legacy['profile']['id'].replace('-','')+'.json'
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:
        db.execute("CREATE TRIGGER reject_supervision BEFORE UPDATE ON snapshots WHEN NEW.name='"+name+"' BEGIN SELECT RAISE(ABORT,'injected supervision storage failure'); END")
    wait(lambda:next(c for c in call(base+'/overview')['capabilities'] if c['name']=='运行监管')['state']=='阻断',30)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:db.execute('DROP TRIGGER reject_supervision')
    until=time.monotonic()+35
    while time.monotonic()<until:
        assert call('/health')['ready'];assert call(base+'/overview')['online']==0;assert_protected();time.sleep(.5)
    assert next(c for c in call(base+'/overview')['capabilities'] if c['name']=='运行监管')['state']=='阻断'
    print('PASS 开启智能托管时存储写入故障仍阻断组件，解除故障后不擅自自动恢复，游戏宿主持续健康。',flush=True)
    final=records();assert {name:(r['AccountId'],r['CharacterId']) for name,r in final.items()}==identities
    assert final['托管主角']['RecoveryDisabled'] and final['托管主角']['RecoveryAt'] is None
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    (root/'proof.json').write_text(json.dumps({'samples':samples,'records':{n:{k:r[k] for k in ['AccountId','CharacterId','SessionUntil','SessionDeaths','RecoveryAt','RecoveryAttempts','RecoveryDisabled','Desired','Error']} for n,r in final.items()}},ensure_ascii=False,indent=2))
    print('PASS 人工下线跨巡检周期不被拉起，保护记录/角色身份保持，正常保存退出及SQLite完整。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Supervision fixture: '+str(root),flush=True)
