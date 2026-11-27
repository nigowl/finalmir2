"""隔离验证不限同IP注册/建角、原TCP上线及角色LLM日志，不访问正式账户。"""
import hashlib,json,os,pathlib,socket,subprocess,tempfile,time,urllib.error,urllib.request
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from threading import Thread

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companion-llm-events-')).resolve();data=root/'version-a'
configuration=os.environ.get('MIR2_CONFIGURATION','Debug');children=[];key='';requests=[]
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http,admin=port(),port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http),
    'MIR2_ADMIN_PORT':str(admin),'MIR2_SERVER':address,'ASPNETCORE_ENVIRONMENT':'Development','DOTNET_ENVIRONMENT':'Development'}
version=hashlib.sha256(str(data).encode()).hexdigest().upper()
def call(path,body=None,method=None,expected=200):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version,'Content-Type':'application/json'}
    request=urllib.request.Request(address+path,headers=headers,data=json.dumps(body).encode() if body is not None else None,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=20) as response:
            assert response.status==expected;return json.load(response)
    except urllib.error.HTTPError as error:
        assert error.code==expected,(path,error.code,error.read().decode());return None
def wait(predicate,seconds=90):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        for process,log in children:assert process.poll() is None,pathlib.Path(log.name).read_text()
        value=predicate()
        if value:return value
        time.sleep(.25)
    raise AssertionError('等待条件超时，现场 '+str(root))
def start(role):
    log=open(root/f'{role}-{len(children)}.log','w')
    process=subprocess.Popen(['dotnet',str(repo/f'src/{role.title()}/bin/{configuration}/net8.0/Mir2.{role.title()}.dll'),
        f'--mir2-instance={repo}:{data/"runtime"}:{role}'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    children.append((process,log));return process
def stop():
    process,log=children.pop();process.terminate();process.wait(60);log.close();assert process.returncode==0,pathlib.Path(log.name).read_text()
class Model(BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    def do_POST(self):
        if self.headers.get('Transfer-Encoding')=='chunked':
            content=b''
            while True:
                size=int(self.rfile.readline().split(b';')[0],16)
                if size==0:self.rfile.readline();break
                content+=self.rfile.read(size);self.rfile.read(2)
        else:content=self.rfile.read(int(self.headers['Content-Length']))
        requests.append(json.loads(content));assert self.headers.get('Authorization')=='Bearer isolated-llm-key'
        reply='explore' if requests[-1]['max_tokens']==24 else '原TCP模型问答已收到，请先准备药品再继续任务。'
        body=json.dumps({'choices':[{'message':{'content':json.dumps({'text':reply},ensure_ascii=False)}}]}).encode()
        self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
model=ThreadingHTTPServer(('127.0.0.1',0),Model);Thread(target=model.serve_forever,daemon=True).start()
try:
    subprocess.run(['dotnet',str(repo/f'tests/Compatibility/bin/{configuration}/net8.0/Compatibility.dll'),'--fixture',str(data),'--companion-fixture'],cwd=repo,check=True)
    process=start('server')
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip();base='/management/companions';pid=call('/management/status')['processId']
    settings=call('/management/settings');assert settings['values']['maxAccountsPerIPHour']==3 and settings['values']['maxCharactersPerIPHour']==5
    unlimited=settings['values']|{'maxIP':32,'maxAccountsPerIPHour':0,'maxCharactersPerIPHour':0}
    saved=call('/management/settings',{'values':unlimited,'version':settings['version']},'PUT');assert not saved['requiresRestart']
    for field in ('maxAccountsPerIPHour','maxCharactersPerIPHour'):
        for value in (-1,65536):call('/management/settings',{'values':unlimited|{field:value},'version':saved['version']},'PUT',400)
    overview=call(base+'/overview');import uuid
    selected={'id':str(uuid.uuid4()),'name':'日志模型','model':'fixture-model','endpoint':f'http://127.0.0.1:{model.server_port}/v1/chat/completions','enabled':True}
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True,'maxOnline':10,'dailyLlmCalls':10000,'models':[selected],'defaultModelId':selected['id']},
        'modelKeys':[{'id':selected['id'],'key':'isolated-llm-key'}]},'PUT')
    ids=[]
    for i in range(6):
        profile=call(base,{'name':f'日志验证{i}','enabled':True,'scheduled':True,'class':0,'chat':i==5,'llmMode':2 if i==5 else 0,'acceptGroup':False,
            'schedule':{'timeZone':'Asia/Shanghai','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':60,'restMinutes':0,'dailyMinutes':1440}})
        ids.append(profile['id'])
    def entered():
        rows=call(base+'?pageSize=100')['items'];assert not any(r['lastError'] for r in rows),rows
        return rows if len(rows)==6 and all(r['characterId']>0 for r in rows) and call(base+'?pageSize=100&status=online')['total']==6 else False
    rows=wait(entered,110);assert call('/management/status')['processId']==pid
    before_chat=len(requests);time.sleep(6)
    subprocess.run(['dotnet',str(repo/f'tests/Compatibility/bin/{configuration}/net8.0/Compatibility.dll'),'--llm-value-session',str(game),'日志验证5'],cwd=root,env=env,check=True,timeout=60)
    def logged():
        detail=call(base+'/'+ids[-1]);return detail if any(e['kind']=='llm-log' and e['state']=='已采用' for e in detail['events']) else False
    detail=wait(logged,70);assert requests
    logs=[e for e in detail['events'] if e['kind']=='llm-log'];assert any(e['state']=='调用中' for e in logs)
    assert any('原TCP模型问答已收到' in e['text'] and '耗时' in e['text'] and '日志模型 / fixture-model' in e['reason'] for e in logs)
    assert len(requests)==before_chat+1,'一次真人问答触发重复付费请求'
    public=json.dumps(detail,ensure_ascii=False);assert 'isolated-llm-key' not in public and selected['endpoint'] not in public
    (root/'detail.json').write_text(json.dumps(detail,ensure_ascii=False,indent=2))
    if os.environ.get('MIR2_COMPANION_UI')=='1':
        start('admin')
        subprocess.run(['node',str(pathlib.Path(__file__).with_suffix('.cjs')),str(root),f'http://127.0.0.1:{admin}',address],env=env,check=True,timeout=90)
        changed=call('/management/settings');assert changed['values']['maxAccountsPerIPHour']==6 and changed['values']['maxCharactersPerIPHour']==8
        call('/management/settings',{'values':changed['values']|{'maxAccountsPerIPHour':0,'maxCharactersPerIPHour':0},'version':changed['version']},'PUT')
        stop()
    assert call('/management/status')['processId']==pid and call(base+'?status=online')['total']==6
    configuration_before=call(base+'/overview')['settings'];call(base+'/stop-all',{})
    wait(lambda:call(base+'/overview')['online']==0);identities={(r['profile']['id'],r['accountId'],r['characterId']) for r in call(base+'?pageSize=100')['items']}
    stop();start('server');wait(ready)
    after=call('/management/settings')['values'];assert after['maxAccountsPerIPHour']==after['maxCharactersPerIPHour']==0
    assert identities=={(r['profile']['id'],r['accountId'],r['characterId']) for r in call(base+'?pageSize=100')['items']}
    assert call(base+'/overview')['settings']['models']==configuration_before['models']
    print('PASS 原TCP：同IP一小时内6陪玩账户/角色注册上线，0不限热更新/负值与越界拒绝/正常重启；真人私聊单次LLM实际回复与采用日志/模型/耗时，密钥不下发。',flush=True)
finally:
    while children:stop()
    model.shutdown();model.server_close()
    if (data/'runtime/mir2.sqlite3').exists():
        import sqlite3
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('LLM events fixture: '+str(root),flush=True)
