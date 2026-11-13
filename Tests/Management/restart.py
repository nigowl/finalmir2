"""Exercise all three root launchers against a disposable fixture, including PID reuse safety."""
import http.cookiejar, html.parser, json, os, pathlib, socket, subprocess, sys, time, urllib.error, urllib.parse, urllib.request
repo=pathlib.Path(__file__).resolve().parents[2]
data=pathlib.Path(sys.argv[1]).resolve(); runtime=pathlib.Path(os.environ.get('MIR2_RUNTIME_ROOT',str(data/'runtime'))).resolve()
server=os.environ.get('MIR2_SERVER','http://127.0.0.1:15100'); admin='http://127.0.0.1:'+os.environ.get('MIR2_ADMIN_PORT','15101')
env=os.environ.copy(); env.update(MIR2_DATA_ROOT=str(data),MIR2_RUNTIME_ROOT=str(runtime),MIR2_SERVER=server,MIR2_CLIENT_CACHE=str(runtime/'client-cache'))
children=[]; outputs=[]

def read_json(url):
    with urllib.request.urlopen(url,timeout=3) as response:return json.load(response)

def until(condition, timeout=120):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        try:
            value=condition()
            if value:return value
        except (OSError,ValueError,urllib.error.URLError):pass
        time.sleep(.2)
    raise AssertionError('Launcher readiness timed out')

def launch(role, previous=0):
    log=open(runtime/f'restart-{role}-{len(children)}.log','w');outputs.append(log)
    args=[str(repo/f'start-{role}.sh')]+(['--headless','--max-fps','30'] if role=='client' else [])
    child=subprocess.Popen(args,cwd='/tmp',env=env,stdout=log,stderr=subprocess.STDOUT);children.append(child)
    file=runtime/'processes'/f'{role}.pid'
    def started():
        if child.poll() is not None: raise RuntimeError(f'{role} exited: '+pathlib.Path(log.name).read_text())
        return file.exists() and int(file.read_text())==child.pid and child.pid!=previous
    until(started)
    if role!='client': until(lambda:read_json((server if role=='server' else admin)+'/health')['ready'])
    else: time.sleep(6);assert child.poll() is None,'Native client exited during startup'
    return child

class TokenParser(html.parser.HTMLParser):
    token=''
    def handle_starttag(self,tag,attrs):
        attrs=dict(attrs)
        if tag=='input' and attrs.get('name')=='__RequestVerificationToken':self.token=attrs.get('value','')

def check_admin(address=admin, jar=None):
    if jar is None:jar=http.cookiejar.CookieJar()
    browser=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    page=browser.open(address+'/').read().decode();assert '管理密钥' in page
    parser=TokenParser();parser.feed(page);assert parser.token
    key=(runtime/'admin.key').read_text().strip()
    try:
        browser.open(urllib.request.Request(address+'/auth/login',data=urllib.parse.urlencode({'key':key}).encode()))
        raise AssertionError('Login without antiforgery token was accepted')
    except urllib.error.HTTPError as error:assert error.code==400
    page=browser.open(urllib.request.Request(address+'/auth/login',data=urllib.parse.urlencode({'key':key,'__RequestVerificationToken':parser.token}).encode())).read().decode()
    assert '运行监控' in page and key not in page
    for name in ('/app.css','/_content/MudBlazor/MudBlazor.min.css','/_content/MudBlazor/MudBlazor.min.js','/resources','/development','/accounts','/characters','/guilds','/population'):
        assert browser.open(address+name).status==200,name
    print('PASS authenticated Blazor pages, static assets, antiforgery and key isolation.')
    return jar

def check_debug(address=admin):
    page=urllib.request.urlopen(address+'/').read().decode()
    assert '运行监控' in page and '管理密钥' not in page,'Debug Development 未直接进入管理页'
    assert urllib.request.urlopen(address+'/login').url.rstrip('/')==address,'Debug登录页未跳转首页'
    for headers in ({'Host':'evil.example'},{'Origin':'https://evil.example'},{'Sec-Fetch-Site':'cross-site'}):
        try:urllib.request.urlopen(urllib.request.Request(address+'/settings',headers=headers));raise AssertionError('开发免密允许跨站请求')
        except urllib.error.HTTPError as error:assert error.code==403
    try:urllib.request.urlopen(urllib.request.Request(address+'/auth/logout',data=b''));raise AssertionError('Debug绕过防伪')
    except urllib.error.HTTPError as error:assert error.code==400
    try:urllib.request.urlopen(server+'/management/status');raise AssertionError('Debug绕过服务端管理密钥')
    except urllib.error.HTTPError as error:assert error.code==401
    print('PASS 本机 Debug Development免密、登录跳转、Host/跨站拒绝、防伪及原管理API鉴权。')

def direct_admin(address, port, target_runtime, configuration='Release', environment='Development'):
    log=open(runtime/f'restart-direct-{len(children)}.log','w');outputs.append(log)
    settings=env|{'MIR2_RUNTIME_ROOT':str(target_runtime),'MIR2_ADMIN_PORT':str(port),
        'ASPNETCORE_ENVIRONMENT':environment,'DOTNET_ENVIRONMENT':environment,
        'MIR2_ADMIN_KEY':(runtime/'admin.key').read_text().strip()}
    child=subprocess.Popen(['dotnet',str(repo/f'src/Admin/bin/{configuration}/net8.0/Mir2.Admin.dll'),
        f'--mir2-instance={repo}:{target_runtime}:admin'],cwd='/tmp',env=settings,stdout=log,stderr=subprocess.STDOUT)
    children.append(child);until(lambda:read_json(address+'/health')['ready']);return child

try:
    foreign=subprocess.Popen(['sleep','300']);children.append(foreign)
    stale=runtime/'processes/server.starting';stale.mkdir(parents=True,exist_ok=True)
    (stale/'pid').write_text(str(foreign.pid))
    first=launch('server'); old=first.pid
    other_env=env|{'MIR2_RUNTIME_ROOT':str(runtime/'other-runtime')}
    duplicate=subprocess.run(['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')],cwd='/tmp',env=other_env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=20)
    assert duplicate.returncode!=0 and b'.server.lock' in duplicate.stdout,'Another runtime bypassed the shared data-directory lock'
    assert read_json(server+'/health')['ready']
    (runtime/'processes/server.pid').write_text(str(foreign.pid))
    second=launch('server',old);first.wait(timeout=60)
    assert first.returncode==0 and foreign.poll() is None,'Server restart failed to save or killed an unrelated PID'
    foreign.terminate();foreign.wait(timeout=5)
    first_admin=launch('admin');check_debug()
    command=subprocess.check_output(['ps','-ww','-p',str(first_admin.pid),'-o','command='],text=True)
    assert '/bin/Debug/' in command,'start-admin不是Debug'
    second_admin=launch('admin',first_admin.pid);first_admin.wait(timeout=30);check_debug()
    second_admin.terminate();second_admin.wait(timeout=30)
    production=direct_admin(admin,os.environ['MIR2_ADMIN_PORT'],runtime,'Debug','Production')
    assert '管理密钥' in urllib.request.urlopen(admin+'/').read().decode(),'Debug Production错误免密'
    production.terminate();production.wait(timeout=30)
    release=direct_admin(admin,os.environ['MIR2_ADMIN_PORT'],runtime)
    jar=check_admin();release.terminate();release.wait(timeout=30)
    direct_admin(admin,os.environ['MIR2_ADMIN_PORT'],runtime)
    browser=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    assert '运行监控' in browser.open(admin+'/').read().decode(),'Release重启丢失独立Cookie'
    with socket.socket() as port_probe:
        port_probe.bind(('127.0.0.1',0));other_port=port_probe.getsockname()[1]
    other_address=f'http://127.0.0.1:{other_port}'
    other_env=env|{'MIR2_RUNTIME_ROOT':str(runtime/'other-admin'),'MIR2_ADMIN_PORT':str(other_port),'MIR2_ADMIN_KEY':(runtime/'admin.key').read_text().strip()}
    other_admin=direct_admin(other_address,other_port,runtime/'other-admin')
    check_admin(other_address,jar)
    for address in (admin,other_address):
        page=browser.open(address+'/').read().decode()
        assert '运行监控' in page and '管理密钥' not in page,'Local admin instances overwrote each other\'s cookies'
    auth_cookies=[cookie.name for cookie in jar if cookie.name.startswith('Mir2.Admin.') and 'Antiforgery' not in cookie.name]
    assert len(auth_cookies)==2 and len(set(auth_cookies))==2
    print('PASS concurrent admin cookie isolation and login persistence across restart.')
    first_client=launch('client');second_client=launch('client',first_client.pid);first_client.wait(timeout=30)
    assert second_client.poll() is None
    print('PASS shared data lock; server/client/admin launchers replace owned processes, preserve unrelated PIDs and launch from another working directory.')
finally:
    for child in reversed(children):
        if child.poll() is None:child.terminate()
    for child in reversed(children):
        try:child.wait(timeout=90)
        except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
    for log in outputs:log.close()
