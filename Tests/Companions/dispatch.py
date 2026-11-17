"""临时版本真实TCP：20人/10名额轮换、持久随机新增，不写正式数据。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request,urllib.error

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-dispatch-')).resolve();data=root/'version-a'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http,admin=port(),port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
def call(path,body=None,method=None,expected=200):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=20) as response:
            assert response.status==expected;return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code!=expected:raise AssertionError(f'{path}: {error.code} {error.read().decode()}') from error
        return None
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        result=predicate()
        if result:return result
        time.sleep(.4)
    raise AssertionError('调度等待超时')
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
def setting(**values):
    settings=call(base+'/overview')['settings']|values
    call(base+'/settings',{'settings':settings},'PUT');return settings
def all_rows():
    first=call(base);rows=first['items']
    for page in range(1,(first['total']+19)//20):rows+=call(base+f'?page={page}')['items']
    return rows
def instant(value):
    from datetime import datetime
    return datetime.fromisoformat(value).timestamp()
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data)],cwd=repo,check=True)
    start();stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-dispatch'],cwd=root,env=env,check=True)
    start();base='/management/companions';seen=set();maximum=0;samples=[];deadlines={};ended=set()
    def rotated():
        global maximum
        overview=call(base+'/overview');rows=call(base)['items'];players=call('/management/status')['players'];online={p['name'] for p in players}
        maximum=max(maximum,len(online));assert len(online)<=10 and overview['online']<=10,(overview,players)
        assert len(rows)==20 and all(not r['lastError'] and r['profile']['enabled'] for r in rows),rows
        for row in rows:
            name=row['profile']['name']
            if name in online and row['state']!='下线中':
                assert row['sessionUntil'],row
                if name not in deadlines:deadlines[name]=row['sessionUntil']
            if row['lastSessionReason']:ended.add(name)
        added=online-seen
        seen.update(online);samples.append({'time':time.time(),'overview':overview,'rows':rows,'players':players})
        if added:print(f'轮换进度：已验证{len(seen)}/20人进入，当前{len(online)}人，峰值{maximum}人。',flush=True)
        return len(seen)==20 and maximum==10 and len(ended)>=10
    wait(rotated,190)
    (root/'rotation-samples.json').write_text(json.dumps(samples,ensure_ascii=False))
    assert set(f'Queue{i:02}' for i in range(10))<=ended,ended
    last=call(base)['items'];accounts={r['accountId'] for r in last};characters={r['characterId'] for r in last}
    assert len(accounts)==20 and len(characters)==20
    # 关闭系统先正常退出，避免后续计时测试被角色继续轮换影响。
    setting(enabled=False);wait(lambda:call(base+'/overview')['online']==0 and not call('/management/status')['players'])
    translations=[json.loads((repo/f'src/Game/Localization/{language}.json').read_text(encoding='utf-8-sig'))['Text'] for language in ['English','Chinese']]
    def normal_logouts():
        logs='\n'.join(path.read_text(errors='replace') for path in root.glob('server-*.log'))
        for i in range(20):
            assert not any(text['UserLogoutReasonTimeoutMessage'].format(f'Queue{i:02}') in logs for text in translations),'正常轮换被原服记为连接超时'
        return all(any(text['UserLoggedOutSelectCharReasonMessage'].format(f'Queue{i:02}') in logs for text in translations) for i in range(20))
    wait(normal_logouts,5)
    for row in call(base)['items']:
        call(base+'/'+row['profile']['id'],row['profile']|{'enabled':False},'PUT')
    defaults=call(base+'/overview')['settings']['arrivals'];arrivals=defaults|{'enabled':True,'minMinutes':1,'maxMinutes':1,'maxProfiles':21}
    for invalid in [arrivals|{'minMinutes':0},arrivals|{'maxMinutes':43201},arrivals|{'minMinutes':2,'maxMinutes':1}]:
        settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'arrivals':invalid}},'PUT',expected=400)
    setting(enabled=True,arrivals=arrivals,nicknames=['新来蹭经验','再刷一把吧'])
    first=call(base+'/overview')['arrivals']['state'];assert first['nextAt'] and first['created']==0
    stop();start()
    assert call(base+'/overview')['arrivals']['state']['nextAt']==first['nextAt'],'重启重抽下一次随机新增时间'
    def joined():
        overview=call(base+'/overview');rows=all_rows();assert len(rows)<=21 and all(not r['lastError'] for r in rows),rows
        state=overview['arrivals']['state']
        if state['created']!=1:return False
        newcomers=[r for r in rows if r['profile']['tags']=='随机加入']
        if not newcomers:return False
        new=newcomers[0]
        return new if new['characterId']>0 and call('/management/admin/characters/'+str(new['characterId']))['online'] else False
    newcomer=wait(joined,90)
    assert newcomer['profile']['scheduled'] and newcomer['profile']['enabled'] and newcomer['profile']['llmMode']==0
    assert newcomer['profile']['name'] in ['新来蹭经验','再刷一把吧']
    overview=call(base+'/overview');assert overview['arrivals']['state']['nextAt'] is None and '上限' in overview['arrivals']['status']
    assert call(base)['total']==21
    # 支持月级间隔并可显示单位，修改其他设置不重抽。
    setting(arrivals=arrivals|{'maxProfiles':22,'minMinutes':43200,'maxMinutes':43200})
    planned=call(base+'/overview')['arrivals']['state'];assert abs(instant(planned['nextAt'])-time.time()-30*86400)<5
    setting(dailyLlmCalls=101)
    assert call(base+'/overview')['arrivals']['state']['nextAt']==planned['nextAt']
    if os.environ.get('MIR2_COMPANION_UI')=='1':
        admin_env=env|{'MIR2_SERVER':address,'MIR2_ADMIN_PORT':str(admin),'ASPNETCORE_ENVIRONMENT':'Production'}
        with open(root/'admin.log','w') as admin_log:
            ui=subprocess.Popen(['dotnet',str(repo/'src/Admin/bin/Release/net8.0/Mir2.Admin.dll'),f'--mir2-instance={repo}:{data/"runtime"}:admin'],cwd=root,env=admin_env,stdout=admin_log,stderr=subprocess.STDOUT)
            try:subprocess.run(['node',str(repo/'tests/Companions/dispatch-ui.cjs'),str(root),f'http://127.0.0.1:{admin}',address],env=os.environ,check=True)
            finally:
                if ui.poll() is None:ui.terminate();ui.wait(30)
    setting(enabled=False);wait(lambda:call(base+'/overview')['online']==0 and not call('/management/status')['players'])
    preserved=call(base+'/overview')['arrivals']['state'];stop();start()
    assert call(base)['total']==21 and call(base+'/overview')['arrivals']['state']==preserved
    stored=all_rows()
    assert accounts<={r['accountId'] for r in stored} and characters<={r['characterId'] for r in stored}
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:assert database.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 真实TCP20档案/10在线：懒惰短玩休息、勤奋更久、20人均进入游戏/名额不重叠，原服20人均确认返回选角、无超时退出；1分钟随机新增/重启不重抽/只增1人/正常注册建角/上限暂停；1月边界/无关保存不重抽/正常退出与SQLite完整性。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Dispatch fixture: '+str(root),flush=True)
