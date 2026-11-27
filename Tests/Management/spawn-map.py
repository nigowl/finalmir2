"""仅在独立版本验证刷怪地图，不改正式世界或重启游戏实例。"""
import hashlib,json,os,pathlib,socket,subprocess,tempfile,time,urllib.request
repo=pathlib.Path(__file__).resolve().parents[2];root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-spawn-map-')).resolve();data=root/'version'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http,admin=port(),port(),port();address=f'http://127.0.0.1:{http}';children=[]
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http),'MIR2_ADMIN_PORT':str(admin),'MIR2_SERVER':address,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development'}
def start(role):
    log=open(root/(role+'.log'),'w');dll=repo/f'src/{role.title()}/bin/Debug/net8.0/Mir2.{role.title()}.dll'
    if role=='admin':dll=pathlib.Path(os.environ.get('MIR2_SPAWN_ADMIN_DLL',str(dll)))
    process=subprocess.Popen(['dotnet',str(dll),f'--mir2-instance={repo}:{data/"runtime"}:{role}'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT);children.append((process,log))
    health=(address if role=='server' else f'http://127.0.0.1:{admin}')+'/health'
    deadline=time.monotonic()+90
    while True:
        assert process.poll() is None,(root/(role+'.log')).read_text()
        try:
            with urllib.request.urlopen(health,timeout=2) as response:ready=json.load(response)['ready']
            if ready:break
        except OSError:pass
        assert time.monotonic()<deadline;time.sleep(.2)
try:
    compat=repo/'tests/Compatibility/bin/Debug/net8.0/Compatibility.dll'
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=root,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start('server');start('admin')
    headers={'Authorization':'Bearer '+(data/'runtime/admin.key').read_text().strip(),'X-Mir2-Version':hashlib.sha256(str(data).encode()).hexdigest().upper(),'Content-Type':'application/json'}
    with urllib.request.urlopen(urllib.request.Request(address+'/management/admin/maps/1',headers=headers)) as response:overview=json.load(response)
    for count in range(2,14):
        value=overview['spawns'][0]|{'id':0,'monsterId':2,'count':count,'spread':4,'version':''}
        request=urllib.request.Request(address+'/management/admin/spawns',headers=headers,data=json.dumps({'value':value,'confirmation':overview['title']}).encode())
        with urllib.request.urlopen(request) as response:assert response.status==200
    subprocess.run(['node',str(repo/'tests/Management/spawn-map.cjs'),str(root),f'http://127.0.0.1:{admin}',address],cwd=repo,env=env,check=True,timeout=150)
finally:
    for process,log in reversed(children):
        if process.poll() is None:process.terminate();process.wait(timeout=90)
        log.close()
    print('Spawn map fixture: '+str(root),flush=True)
