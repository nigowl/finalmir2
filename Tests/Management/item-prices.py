"""使用独立世界、原TCP与浏览器验证价格配置及完整重启，绝不写正式数据。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request
repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-prices-')).resolve();data=root/'version'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http,admin=port(),port(),port();address=f'http://127.0.0.1:{http}';children=[]
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http),'MIR2_ADMIN_PORT':str(admin),'MIR2_SERVER':address,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development'}
compat=repo/'tests/Compatibility/bin/Debug/net8.0/Compatibility.dll'
def start(kind,phase):
    log=open(root/f'{kind}-{phase}.log','w');dll=repo/f'src/{kind.title()}/bin/Debug/net8.0/Mir2.{kind.title()}.dll'
    process=subprocess.Popen(['dotnet',str(dll),f'--mir2-instance={repo}:{data/"runtime"}:{kind}'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT);children.append((process,log))
    endpoint=address+'/health' if kind=='server' else f'http://127.0.0.1:{admin}/health'
    deadline=time.monotonic()+90
    while True:
        assert process.poll() is None,(root/f'{kind}-{phase}.log').read_text()
        try:
            with urllib.request.urlopen(endpoint,timeout=2) as response:
                health=json.load(response)
                if kind!='server' or health['ready']:break
        except OSError:pass
        assert time.monotonic()<deadline;time.sleep(.2)
    return process
def stop(process):process.terminate();process.wait(90)
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data),'--gm-fixture','--item-price-fixture'],cwd=root,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    server=start('server','first');subprocess.run(['dotnet',str(compat),'--item-price-session',str(game),str(root/'assets.json')],cwd=root,env=env,check=True,timeout=90)
    if os.environ.get('MIR2_PRICE_UI')=='1':
        admin_process=start('admin','ui');subprocess.run(['node',str(repo/'tests/Management/item-prices.cjs'),str(root),f'http://127.0.0.1:{admin}'],cwd=root,env=env,check=True,timeout=150);stop(admin_process)
    stop(server);server=start('server','restored')
    subprocess.run(['dotnet',str(compat),'--item-price-session',str(game),str(root/'assets.json'),'--verify-save'],cwd=root,env=env,check=True,timeout=60)
    if os.environ.get('MIR2_OPERATIONS')=='1':
        import operations
        operations.run(address,data,game)
        if os.environ.get('MIR2_PRICE_UI')=='1':
            admin_process=start('admin','operations')
            subprocess.run(['node',str(repo/'tests/Management/operations.cjs'),str(root),f'http://127.0.0.1:{admin}',address],cwd=root,env=env,check=True,timeout=240)
            stop(admin_process)
    if os.environ.get('MIR2_PRICE_NATIVE')=='1':
        with open(root/'native-import.log','w') as log:
            subprocess.run(['/Applications/Godot_mono.app/Contents/MacOS/Godot','--headless','--editor','--path',str(repo/'src/Client'),'--import','--quit'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=60)
        native_env=env|{'MIR2_CLIENT_CACHE':str(root/'cache'),'MIR2_CLIENT_SETTINGS':str(root/'client-settings.json')}
        with open(root/'native.log','w') as log:
            subprocess.run(['/Applications/Godot_mono.app/Contents/MacOS/Godot','--path',str(repo/'src/Client'),'--max-fps','30','--',f'--server={address}','--verify-item-prices',f'--screenshot={root/"native.png"}'],cwd=root,env=native_env,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=120)
        assert 'PASS native item prices:' in (root/'native.log').read_text()
    stop(server)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:
        assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        for name in ['settings/item-prices.json','settings/public-shop.json']:
            assert db.execute('select count(*) from snapshots where name=?',(name,)).fetchone()[0]==1
    print('PASS 隔离价格API/原TCP/完整重启与SQLite完整性。',flush=True)
finally:
    for process,log in reversed(children):
        if process.poll() is None:stop(process)
        log.close()
    print('Price fixture: '+str(root),flush=True)
