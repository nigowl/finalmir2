"""Run relocated published programs with copied, independent game versions."""
import hashlib, html.parser, http.cookiejar, json, os, pathlib, shutil, socket, sqlite3, subprocess, sys, tempfile, time, urllib.error, urllib.parse, urllib.request

repo = pathlib.Path(__file__).resolve().parents[2]
published = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else repo / 'build').resolve()
reference = pathlib.Path(os.environ.get('MIR2_DATA_ROOT', repo / 'data/versions/default')).resolve()
root = pathlib.Path(tempfile.mkdtemp(prefix='mir2-deployment-')).resolve()
children, logs = [], []
completed = False
environment = {key: value for key, value in os.environ.items() if not key.upper().startswith('MIR2_')}
environment['PATH'] = '/usr/bin:/bin:/usr/sbin:/sbin'

def port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]

def call(address, path='/health', key=None, method='GET', value=None, headers=None):
    supplied = dict(headers or {})
    if key: supplied['Authorization'] = 'Bearer ' + key
    if value is not None: supplied['Content-Type'] = 'application/json'
    request = urllib.request.Request(address + path, data=None if value is None else json.dumps(value).encode(), headers=supplied, method=method)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)

def start(role, variables, label):
    log = open(root / (label + '.log'), 'w'); logs.append(log)
    child = subprocess.Popen([str(root / 'build' / role / 'start.sh')], cwd='/tmp', env=environment | variables, stdout=log, stderr=subprocess.STDOUT)
    children.append(child)
    return child, pathlib.Path(log.name)

def ready(process, address):
    child, log = process
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        if child.poll() is not None: raise AssertionError(log.read_text())
        try:
            value = call(address)
            if value['ready']: return value
        except (OSError, ValueError): pass
        time.sleep(.2)
    raise AssertionError('Published program did not become ready: ' + log.read_text())

def stop(process):
    child, log = process
    child.terminate(); child.wait(timeout=90)
    assert child.returncode == 0, log.read_text()

def settings(role, version, address='http://127.0.0.1:5080'):
    target = root / 'build' / role / 'appsettings.json'
    target.write_text(json.dumps({'Mir2': {'VersionDirectory': '../../data/versions/' + version, 'ServerUrl': address}}, ensure_ascii=False))

class Token(html.parser.HTMLParser):
    value = ''
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'input' and attrs.get('name') == '__RequestVerificationToken': self.value = attrs.get('value', '')

def login(address, key):
    browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    token = Token(); token.feed(browser.open(address + '/').read().decode())
    assert token.value
    form = urllib.parse.urlencode({'key': key, '__RequestVerificationToken': token.value}).encode()
    page = browser.open(urllib.request.Request(address + '/auth/login', data=form)).read().decode()
    assert key not in page
    return browser, html.unescape(page)

def client(address, version, flag):
    log = root / ('client-' + flag + '.log')
    variables = {'MIR2_RUNTIME_ROOT': str(version / 'runtime'), 'MIR2_CLIENT_CACHE': str(root / 'client-cache')}
    args = [str(root / 'build/client/start.sh'), '--headless', '--max-fps', '60', '--', '--server=' + address, '--smoke-record=' + str(root / 'save.json'), flag]
    with log.open('w') as output:
        child = subprocess.Popen(args, cwd='/tmp', env=environment | variables, stdout=output, stderr=subprocess.STDOUT); children.append(child)
        child.wait(timeout=240)
    result = log.read_text()
    assert child.returncode == 0 and 'PASS native' in result and 'ERROR:' not in result, result
    print(result.strip())

try:
    shutil.copytree(published, root / 'build', symlinks=True)
    original = root / 'data/versions/default'; original.parent.mkdir(parents=True)
    subprocess.run(['dotnet', str(repo / 'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'), '--fixture', str(original), '--guild-fixture'], cwd='/tmp', env=os.environ | {'MIR2_DATA_ROOT': str(reference)}, check=True)
    game_a, resource_a, admin_port = port(), port(), port()
    address_a, admin_address = 'http://127.0.0.1:' + str(resource_a), 'http://127.0.0.1:' + str(admin_port)
    server_a = start('server', {'MIR2_GAME_PORT': str(game_a), 'MIR2_RESOURCE_PORT': str(resource_a)}, 'server-original')
    assert ready(server_a, address_a)['gameVersion'] == 'default'
    key = (original / 'runtime/admin.key').read_text().strip()
    assert call(address_a, '/management/status', key)['databasePath'] == str(original / 'runtime/mir2.sqlite3')
    call(address_a, '/management/admin/accounts', key, 'POST', {'accountID': 'OriginalOnly', 'password': 'Native123'})
    settings('admin', 'default', address_a)
    admin = start('admin', {'MIR2_ADMIN_PORT': str(admin_port)}, 'admin-original')
    assert ready(admin, admin_address)['gameVersion'] == 'default'
    browser, page = login(admin_address, key)
    assert 'default' in page and 'version directories do not match' not in page
    for path in ('/app.css', '/_content/MudBlazor/MudBlazor.min.css', '/_content/MudBlazor/MudBlazor.min.js', '/guilds', '/population'):
        assert browser.open(admin_address + path).status == 200, path
    stop(admin); stop(server_a)
    assert not (original / 'runtime/mir2.sqlite3-wal').exists() or (original / 'runtime/mir2.sqlite3-wal').stat().st_size == 0
    classic = original.with_name('复古'); shutil.copytree(original, classic, symlinks=True)
    script = classic / 'world/Envir/NPCs/NativeShop.txt'; script.write_text(script.read_text().replace('Native integration shop.', 'Classic copied shop.'))
    shutil.copyfile(classic / 'assets/maps/0.map', classic / 'assets/maps/classic-only.map')
    settings('server', '复古')
    game_b, resource_b = port(), port(); address_b = 'http://127.0.0.1:' + str(resource_b)
    server_b = start('server', {'MIR2_GAME_PORT': str(game_b), 'MIR2_RESOURCE_PORT': str(resource_b)}, 'server-classic')
    assert ready(server_b, address_b)['gameVersion'] == '复古'
    assert call(address_b, '/management/status', key)['databasePath'] == str(classic / 'runtime/mir2.sqlite3')
    call(address_b, '/management/admin/accounts', key, 'POST', {'accountID': 'ClassicOnly', 'password': 'Native123'})
    monster = call(address_b, '/management/development/definition/monsters/1', key)
    call(address_b, '/management/development/definition', key, 'PUT', monster | {'hp': monster['hp'] + 1})
    settings('server', 'default')
    server_a = start('server', {'MIR2_GAME_PORT': str(game_a), 'MIR2_RESOURCE_PORT': str(resource_a)}, 'server-original-reopened')
    assert ready(server_a, address_a)['gameVersion'] == 'default'
    assert call(address_a, '/management/status', key)['databasePath'] == str(original / 'runtime/mir2.sqlite3')
    assert not any(a['accountID'] == 'ClassicOnly' for a in call(address_a, '/management/admin/accounts', key))
    assert call(address_a, '/management/development/definition/monsters/1', key)['hp'] == monster['hp']
    assert call(address_b)['version'] != call(address_a)['version'] and not (original / 'assets/maps/classic-only.map').exists()
    assert 'Classic copied shop.' not in (original / 'world/Envir/NPCs/NativeShop.txt').read_text()
    wrong_version = hashlib.sha256(str(original).encode()).hexdigest().upper()
    try:
        call(address_b, '/management/status', key, headers={'X-Mir2-Version': wrong_version})
        raise AssertionError('Cross-version management request was accepted')
    except urllib.error.HTTPError as error: assert error.code == 409
    settings('admin', 'default', address_b)
    admin = start('admin', {'MIR2_ADMIN_PORT': str(admin_port)}, 'admin-mismatched')
    ready(admin, admin_address); _, page = login(admin_address, key)
    assert 'version directories do not match' in page
    stop(admin); settings('admin', '复古', address_b)
    admin = start('admin', {'MIR2_ADMIN_PORT': str(admin_port)}, 'admin-classic')
    assert ready(admin, admin_address)['gameVersion'] == '复古'
    _, page = login(admin_address, key)
    assert '复古' in page and 'version directories do not match' not in page
    refused = subprocess.run([str(repo / 'publish.sh'), str(root / 'build')], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    assert refused.returncode != 0 and '仍在运行' in refused.stdout, refused.stdout
    print('PASS relocated native hosts and Blazor assets; directory configuration, copied versions, concurrent SQLite/world/NPC/map isolation, version labels and mismatched-version rejection.')
    client(address_b, classic, '--smoke')
    stop(server_b)
    settings('server', '复古')
    server_b = start('server', {'MIR2_GAME_PORT': str(game_b), 'MIR2_RESOURCE_PORT': str(resource_b)}, 'server-classic-restarted')
    ready(server_b, address_b); client(address_b, classic, '--verify-save')
    stop(admin); stop(server_b); stop(server_a)
    for version in (original, classic):
        with sqlite3.connect(version / 'runtime/mir2.sqlite3') as db: assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
    saved = {role: (root / 'build' / role / 'appsettings.json').read_bytes() for role in ('server', 'admin')}
    with (root / 'republish.log').open('w') as log:
        subprocess.run([str(repo / 'publish.sh'), str(root / 'build')], stdout=log, stderr=subprocess.STDOUT, check=True, timeout=240)
    assert all((root / 'build' / role / 'appsettings.json').read_bytes() == value for role, value in saved.items())
    assert not list((root / 'build/client').rglob('mir2.sqlite3')) and not list((root / 'build/client').rglob('*.map'))
    print('PASS published Godot full gameplay and restart restoration; both databases intact; republish preserves local version configuration; client carries no game maps or database.')
    completed = True
except BaseException:
    print('Deployment fixture retained at ' + str(root), file=sys.stderr)
    raise
finally:
    for child in reversed(children):
        if child.poll() is None: child.terminate()
    for child in reversed(children):
        try: child.wait(timeout=90)
        except subprocess.TimeoutExpired: child.kill(); child.wait(timeout=10)
    for log in logs: log.close()
    if completed and os.environ.get('MIR2_TEST_KEEP') != '1': shutil.rmtree(root)
