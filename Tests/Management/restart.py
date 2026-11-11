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
    first_admin=launch('admin');jar=check_admin()
    second_admin=launch('admin',first_admin.pid);first_admin.wait(timeout=30)
    browser=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    assert '运行监控' in browser.open(admin+'/').read().decode(),'Admin restart lost the existing login cookie'
    with socket.socket() as port_probe:
        port_probe.bind(('127.0.0.1',0));other_port=port_probe.getsockname()[1]
    other_address=f'http://127.0.0.1:{other_port}'
    other_env=env|{'MIR2_RUNTIME_ROOT':str(runtime/'other-admin'),'MIR2_ADMIN_PORT':str(other_port),'MIR2_ADMIN_KEY':(runtime/'admin.key').read_text().strip()}
    other_log=open(runtime/'restart-admin-isolated.log','w');outputs.append(other_log)
    other_admin=subprocess.Popen([str(repo/'start-admin.sh')],cwd='/tmp',env=other_env,stdout=other_log,stderr=subprocess.STDOUT);children.append(other_admin)
    until(lambda:read_json(other_address+'/health')['ready'])
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
