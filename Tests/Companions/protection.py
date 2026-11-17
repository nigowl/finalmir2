"""隔离原TCP验证阻挡绑定回城/复活、禁回城、四怪围攻与五职业创建；不修改正式服。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.error,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-protection-')).resolve();data=root/'version-a'
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
    except urllib.error.HTTPError as error:raise AssertionError(f'{path}: {error.code}: '+error.read().decode()) from error
def wait(predicate,seconds=40):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        assert process.poll() is None,pathlib.Path(log.name).read_text()
        result=predicate()
        if result:return result
        time.sleep(.1)
    raise AssertionError('原保护等待超时；'+str(root))
def start():
    global process,log,key,starts
    starts+=1;log=(root/f'server-{starts}.log').open('w')
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
def command(ident,action):
    row=call('/management/companions/'+ident)['row']
    call('/management/companions/'+ident+'/command',{'action':action,'version':row['profile']['version']})
try:
    reference=os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':reference},check=True)
    start();base='/management/companions';settings=call('/management/settings')
    flags={f'allowCreate{c}':True for c in ['Warrior','Wizard','Taoist','Assassin','Archer']}
    call('/management/settings',{'values':settings['values']|flags|{'maxIP':20},'version':settings['version']},'PUT')
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'enabled':True,'maxOnline':5}},'PUT')
    assert call(base+'/overview')['availableClasses']==[0,1,2,3,4]
    profiles=[]
    for role in range(5):
        if role==3:
            for p in profiles:command(p['id'],'stop')
            wait(lambda:call(base+'/overview')['online']==0)
            # 原服每进程同IP每小时仅3次注册；正常重启分两批，不修改或跳过注册校验。
            stop();start()
        p=call(base,{'name':'ProtectRole'+str(role),'class':role,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,'chat':False,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
        profiles.append(p);command(p['id'],'start')
        wait(lambda:call(base+'/'+p['id'])['row']['characterId'])
    for p in profiles[:3]:command(p['id'],'start')
    wait(lambda:all(call('/management/admin/characters/'+str(call(base+'/'+p['id'])['row']['characterId']))['online'] for p in profiles))
    assert [call('/management/admin/characters/'+str(call(base+'/'+p['id'])['row']['characterId']))['class'] for p in profiles]==['Warrior','Wizard','Taoist','Assassin','Archer']
    for p in profiles:command(p['id'],'stop')
    wait(lambda:call(base+'/overview')['online']==0)
    # 只开放弓箭手时，随机预览必须全部选择弓箭手；不依赖随机恰好抽中五职业。
    settings=call('/management/settings');call('/management/settings',{'values':settings['values']|{k:k=='allowCreateArcher' for k in flags},'version':settings['version']},'PUT')
    request={'count':3,'nicknames':['PreviewOne','PreviewTwo','PreviewThree'],'template':profiles[0],'randomClass':True,'randomGender':False,'randomPersonality':False}
    preview=call(base+'/batch-preview',request);assert all(p['class']==4 for p in preview['profiles'])
    settings=call('/management/settings');call('/management/settings',{'values':settings['values']|flags,'version':settings['version']},'PUT')
    print('PASS 五职业管理API创建及原TCP实际建角，单独弓箭手开关控制随机批量。',flush=True)
    ident=profiles[1]['id'];cid=call(base+'/'+ident)['row']['characterId'];samples=[];saved=None
    for mode in os.environ.get('MIR2_PROTECTION_MODES','town,revive,denied,crowd').split(','):
        stop();subprocess.run(['dotnet',str(compat),'--companion-protection',str(cid),mode],cwd=root,env=env,check=True)
        fixture=json.loads((data/'runtime/protection-fixture.json').read_text());start()
        before=call('/management/admin/characters/'+str(cid));command(ident,'start');command(ident,'pause');began=time.monotonic();rounds=[]
        def observed():
            detail=call(base+'/'+ident);c=call('/management/admin/characters/'+str(cid));rounds.append({'seconds':time.monotonic()-began,'character':c,'runtime':detail['runtime'],'events':detail['events']})
            (root/(mode+'-samples.json')).write_text(json.dumps(rounds,ensure_ascii=False))
            assert not detail['row']['lastError'] and not any(e['kind'] in ['death','protection-rejected'] for e in detail['events']),detail
            if mode=='denied':
                assert c['mapId']==fixture['field'] and sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['scroll'])==2
                return c if c['online'] and time.monotonic()-began>=3 else False
            return c if c['online'] and c['hp']>0 and c['mapId']==fixture['town'] else False
        result=wait(observed,20)
        if mode!='denied':
            assert max(abs(result['x']-10),abs(result['y']-10))<=2 and (result['x'],result['y'])!=(10,10),result
            assert sum(i['count'] for i in result['inventory'] if i['itemId']==fixture['scroll'])==(2 if mode=='revive' else 1),result
        if mode=='crowd':assert any((r['runtime'] or {}).get('hp',68)<68 for r in rounds),'未观察到真实扣血'
        command(ident,'stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online'])
        saved=call('/management/admin/characters/'+str(cid));samples.append({'mode':mode,'seconds':time.monotonic()-began,'before':before,'after':saved})
        print(f'PASS 原TCP {mode}：生命{before["hp"]}→{saved["hp"]}，原卷轴数量/禁用权限与安全区实际落点通过。',flush=True)
    if os.environ.get('MIR2_PROTECTION_RECOVERY')=='1':
        stop();subprocess.run(['dotnet',str(compat),'--companion-protection',str(cid),'budget'],cwd=root,env=env,check=True);start()
        settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'smartRecovery':True,'maxOnline':1}},'PUT')
        profile=call(base+'/'+ident)['row']['profile'];assert profile['deathLimit']==10
        call(base+'/'+ident,profile|{'deathLimit':1},'PUT');command(ident,'start')
        def record():
            with sqlite3.connect('file:'+str(data/'runtime/mir2.sqlite3')+'?mode=ro',uri=True) as db:
                return json.loads(db.execute('select data from snapshots where name=?',('companions/profiles/'+ident.replace('-','')+'.json',)).fetchone()[0])['Value']
        wait(lambda:record()['RecoveryAt'],30);pending=record();assert pending['SessionDeaths']==1 and pending['Profile']['Enabled']
        dead=call('/management/admin/characters/'+str(cid));assert dead['hp']==0
        stop();start();assert record()['RecoveryAt']==pending['RecoveryAt'] and record()['SessionDeaths']==1
        def recovered():
            r=record();c=call('/management/admin/characters/'+str(cid));assert r['RecoveryAttempts']==pending['RecoveryAttempts']
            return c if c['online'] and c['hp']>0 and c['mapId']==fixture['town'] and r['RecoveryAt'] is None and r['SessionDeaths']==0 else False
        result=wait(recovered,180);assert record()['LastSessionReason'].startswith('智能托管死亡恢复：上次死亡1/1')
        assert result['inventory']==dead['inventory'] and result['experience']==dead['experience']
        command(ident,'stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);saved=call('/management/admin/characters/'+str(cid))
        print('PASS 原TCP真实死亡触发预算、持久退避跨宿主重启、安全补位重开预算、原服死亡重登复活；无GM回血/补资产。',flush=True)
    stop();start();restored=call('/management/admin/characters/'+str(cid))
    assert saved and restored['inventory']==saved['inventory'] and restored['experience']==saved['experience'] and restored['hp']==saved['hp']
    assert restored['mapId']==saved['mapId'] and restored['bindX']==10 and restored['bindY']==10
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    (root/'results.json').write_text(json.dumps(samples,ensure_ascii=False))
    print('PASS 正常保存/完整重启，原资产编号数量、绑定定义、经验和生命保持。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Protection fixture: '+str(root),flush=True)
