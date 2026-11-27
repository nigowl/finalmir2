"""检查自包含管理端的监听、Host 边界和真实登录；所有写入使用临时目录。"""
import html.parser
import http.cookiejar
import json
import os
import pathlib
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

published = pathlib.Path(sys.argv[1]).resolve()
expected = (published / 'VERSION').read_text().strip()


class Token(html.parser.HTMLParser):
    value = ''

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'input' and attrs.get('name') == '__RequestVerificationToken':
            self.value = attrs.get('value', '')


with tempfile.TemporaryDirectory(prefix='mir2-admin-deployment-') as temporary:
    root = pathlib.Path(temporary)
    for public, key_source in ((False, 'generated'), (True, 'file'), (True, 'environment')):
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0))
            port = probe.getsockname()[1]
        data = root / key_source
        variables = {key: value for key, value in os.environ.items() if not key.startswith('MIR2_')}
        variables.update(MIR2_DATA_ROOT=str(data), MIR2_ADMIN_PORT=str(port),
                         DOTNET_ENVIRONMENT='Production', ASPNETCORE_ENVIRONMENT='Production')
        if public:
            variables['MIR2_ADMIN_BIND_ADDRESS'] = '0.0.0.0'
        if key_source == 'file':
            (data / 'runtime').mkdir(parents=True)
            (data / 'runtime/admin.key').write_text('test123')
        elif key_source == 'environment':
            variables['MIR2_ADMIN_KEY'] = 'env1234'
        address = 'http://127.0.0.1:' + str(port)
        with (root / ('public.log' if public else 'loopback.log')).open('w+') as log:
            process = subprocess.Popen([str(published / 'start.sh'), f'--mir2-instance={root}:{data}/runtime:admin'],
                                       cwd='/tmp', env=variables, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 40
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        log.seek(0)
                        raise AssertionError(log.read())
                    try:
                        with urllib.request.urlopen(address + '/health', timeout=2) as response:
                            health = json.load(response)
                            assert health['ready'] and health['programVersion'] == expected
                            break
                    except OSError:
                        time.sleep(.1)
                else:
                    raise AssertionError('管理端启动超时')
                for host, allowed in (('127.0.0.1', True), ('203.0.113.10', public), ('attacker.example', False)):
                    request = urllib.request.Request(address + '/health', headers={'Host': host})
                    try:
                        with urllib.request.urlopen(request, timeout=5) as response:
                            assert allowed and response.status == 200, host
                    except urllib.error.HTTPError as error:
                        assert not allowed and error.code == 403, host
                cookies = http.cookiejar.CookieJar()
                browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
                response = browser.open(address + '/characters')
                assert '/login' in response.url
                token = Token()
                token.feed(response.read().decode())
                assert token.value
                try:
                    browser.open(urllib.request.Request(address + '/auth/login', data=b'key=invalid'))
                    raise AssertionError('缺少防伪令牌的登录被接受')
                except urllib.error.HTTPError as error:
                    assert error.code == 400
                key = variables['MIR2_ADMIN_KEY'] if key_source == 'environment' else (data / 'runtime/admin.key').read_text().strip()
                wrong = urllib.parse.urlencode({'key': key + '-wrong', '__RequestVerificationToken': token.value}).encode()
                rejected = browser.open(urllib.request.Request(address + '/auth/login', data=wrong))
                assert '/login?error=1' in rejected.url
                form = urllib.parse.urlencode({'key': key, '__RequestVerificationToken': token.value}).encode()
                page = browser.open(urllib.request.Request(address + '/auth/login', data=form)).read().decode()
                assert '程序 v' + expected in page and 'v@Mir2' not in page
                assert key not in page
                auth = next(cookie for cookie in cookies if cookie.name.startswith('Mir2.Admin.') and 'Antiforgery' not in cookie.name)
                attributes = {name.lower(): value for name, value in auth._rest.items()}
                assert 'httponly' in attributes and attributes.get('samesite', '').lower() == 'strict'
                for path in ('/app.css', '/_content/MudBlazor/MudBlazor.min.css', '/_content/MudBlazor/MudBlazor.min.js'):
                    assert browser.open(address + path).status == 200
            finally:
                if process.poll() is None:
                    process.terminate()
                process.wait(timeout=90)
                assert process.returncode == 0
    print('PASS 管理端随机/短文件/短环境变量密钥、错误密钥拒绝、默认回环/公网绑定、Host、防伪/Cookie、实际版本与静态资源。')
