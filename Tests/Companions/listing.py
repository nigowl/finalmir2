"""隔离检查陪玩管理10条分页、全量状态/搜索过滤及真实浏览器，不接触正式数据。"""
import hashlib,json,os,pathlib,socket,subprocess,tempfile,time,urllib.error,urllib.request,sqlite3

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-listing-')).resolve();data=root/'version-a'
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Debug/net8.0/Mir2.Server.dll')))
configuration=server.parent.parent.name
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http,admin_port=port(),port(),port();address=f'http://127.0.0.1:{http}';children=[];key=''
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http),
    'MIR2_ADMIN_PORT':str(admin_port),'MIR2_SERVER':address,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development'}
version=hashlib.sha256(str(data).encode()).hexdigest().upper()
def call(path,body=None,method=None,expected=200):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version,'Content-Type':'application/json'}
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=15) as response:
            assert response.status==expected,(path,response.status,expected)
            return json.load(response)
    except urllib.error.HTTPError as error:
        assert error.code==expected,(path,error.code,error.read().decode())
        return None
def start(dll,role):
    log=open(root/(role+'.log'),'w')
    process=subprocess.Popen(['dotnet',str(dll),f'--mir2-instance={repo}:{data/"runtime"}:{role}'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    children.append((process,log));return process
try:
    compat=repo/f'tests/Compatibility/bin/{configuration}/net8.0/Compatibility.dll'
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    process=start(server,'server');deadline=time.monotonic()+90
    while True:
        assert process.poll() is None,(root/'server.log').read_text()
        try:
            if call('/health')['ready']:break
        except OSError:pass
        assert time.monotonic()<deadline;time.sleep(.2)
    key=(data/'runtime/admin.key').read_text().strip();base='/management/companions'
    profiles=[];account_count=call('/management/status')['counts']['accounts'];classes=call(base+'/overview')['availableClasses']
    for i in range(25):
        profiles.append(call(base,{'name':f'分页测试{i:02}','alias':'needle' if i in (0,24) else '', 'class':classes[i%len(classes)],'enabled':i>=23,'scheduled':False,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}}))
    call(base+'/'+profiles[24]['id']+'/command',{'action':'pause','version':profiles[24]['version']})
    pages=[call(base+f'?page={i}&pageSize=10') for i in range(3)]
    assert [len(p['items']) for p in pages]==[10,10,5] and all(p['total']==25 for p in pages)
    assert len({r['profile']['id'] for p in pages for r in p['items']})==25
    assert len(call(base)['items'])==20,'未指定页长的既有巡检调用被改变'
    for status,total in [('online',0),('offline',25),('queued',0),('paused',1),('resting',0),('error',0),('disabled',23)]:
        result=call(base+'?pageSize=10&status='+status);assert result['total']==total and len(result['items'])==min(10,total),(status,result)
    assert call(base+'?pageSize=10&search=needle')['total']==2
    assert call(base+'?pageSize=10&search=needle&status=disabled')['total']==1
    assert call(base+'?pageSize=10&status=disabled&page=2')['total']==23
    assert len(call(base+'?pageSize=10&status=disabled&page=2')['items'])==3
    call(base+'?status=unknown',expected=400);call(base+'?pageSize=0',expected=400);call(base+'?pageSize=51',expected=400)
    if os.environ.get('MIR2_COMPANION_UI')=='1':
        start(repo/f'src/Admin/bin/{configuration}/net8.0/Mir2.Admin.dll','admin')
        deadline=time.monotonic()+30
        while True:
            try:
                with urllib.request.urlopen(f'http://127.0.0.1:{admin_port}/login',timeout=2):break
            except OSError:
                assert time.monotonic()<deadline;time.sleep(.2)
        subprocess.run(['node',str(repo/'tests/Companions/listing-ui.cjs'),str(root),f'http://127.0.0.1:{admin_port}'],env=env,check=True,timeout=90)
    assert call('/management/status')['counts']['accounts']==account_count,'列表检查意外注册了游戏账户'
    assert call(base+'/overview')['settings']['enabled'] is False
    print('PASS 25档案：10/10/5完整分页、七类状态、搜索交集、跨页筛选总数、非法参数拒绝；未开启陪玩或注册账户。',flush=True)
finally:
    for process,log in reversed(children):
        if process.poll() is None:process.terminate();process.wait(60)
        log.close()
    if (data/'runtime/mir2.sqlite3').exists():
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('Listing fixture: '+str(root),flush=True)
