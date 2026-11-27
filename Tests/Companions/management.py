"""复用 listing.py 的隔离服，核对批量、日程、昵称和模型列表边界。"""
import copy,json,os,pathlib,subprocess,threading,uuid
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer

def run(call,root,address,version,env):
    base='/management/companions'
    def rows():
        first=call(base+'?pageSize=50');assert len(first['items'])==25;return first['items']
    def selected(values):return [{'id':r['profile']['id'],'version':r['profile']['version']} for r in values]
    def profiles():return {r['profile']['id']:r['profile'] for r in rows()}
    before=profiles(); targets=rows()[::24];ids={r['profile']['id'] for r in targets}
    schedule=call(base+'/schedules')[2]['schedule']; original=copy.deepcopy(schedule)
    payload={'profiles':selected(targets),'scheduled':True,'schedule':schedule}
    call(base+'/selected-schedule',payload)
    after=profiles()
    assert all(p==before[i] for i,p in after.items() if i not in ids)
    assert all(after[i]['schedule']==schedule and after[i]['scheduled'] for i in ids)
    call(base+'/selected-schedule',payload,expected=409)
    call(base+'/selected-command',{'action':'stop','profiles':[]},expected=400)
    stale=selected(targets);current=selected([r for r in rows() if r['profile']['id'] in ids])
    call(base+'/selected-command',{'action':'stop','profiles':current})
    assert not call(base+'/overview')['settings']['enabled']
    assert all(call(base+'/'+i)['row']['profile']['schedule']==original for i in ids)
    preset={'id':str(uuid.uuid4()),'name':'接口测试日程','schedule':schedule}
    call(base+'/schedules',preset)
    saved=next(p for p in call(base+'/schedules') if p['id']==preset['id'])
    call(base+'/schedules',preset|{'id':str(uuid.uuid4())},expected=400)
    call(base+'/schedules/'+saved['id'],saved|{'name':'接口更新日程'},'PUT')
    call(base+'/schedules/'+saved['id']+'/delete',{'action':'delete','version':saved['version']},expected=409)
    saved=next(p for p in call(base+'/schedules') if p['id']==preset['id'])
    call(base+'/schedules/'+saved['id']+'/delete',{'action':'delete','version':saved['version']})
    assert len(call(base+'/schedules'))==8
    assert all(call(base+'/'+i)['row']['profile']['schedule']==original for i in ids)
    events=[]
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def do_GET(self):
            events.append((self.path,self.headers.get('Authorization')))
            status=401 if self.path.startswith('/denied/') else 302 if self.path.startswith('/redirect/') else 200
            if self.path.startswith('/invalid/'):body=b'[]'
            elif self.path.startswith('/oversized/'):body=b' '*(524288+1)
            else:body=json.dumps({'data':[{'id':'model-b'},{'id':'model-a'},{'id':'model-b'},{'id':''},{'object':'bad'}]}).encode()
            self.send_response(status)
            if status==302:self.send_header('Location','/leak/models')
            self.send_header('Content-Length',str(len(body)));self.end_headers()
            try:self.wfile.write(body)
            except (BrokenPipeError,ConnectionResetError):pass
    mock=ThreadingHTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=mock.serve_forever,daemon=True);thread.start()
    endpoint=f'http://127.0.0.1:{mock.server_port}'
    try:
        discovered=call(base+'/models/discover',{'endpoint':endpoint,'key':'test-private-key'})
        assert discovered['models']==['model-a','model-b'] and events[-1]==('/v1/models','Bearer test-private-key')
        call(base+'/models/discover',{'endpoint':endpoint,'key':'test-private-key'},expected=409)
        for path,expected in [('denied',409),('redirect',409),('invalid',400),('oversized',400)]:
            call(base+'/models/discover',{'endpoint':endpoint+'/'+path,'key':'test-private-key'},expected=expected)
        assert not any(p.startswith('/leak/') for p,_ in events)
        settings=call(base+'/overview')['settings']
        model={'id':str(uuid.uuid4()),'name':'本地测试服务商','endpoint':endpoint+'/saved/chat/completions','model':'model-a','enabled':True}
        settings['models']=[model];settings['defaultModelId']=model['id']
        call(base+'/settings',{'settings':settings,'modelKeys':[{'id':model['id'],'key':'saved-private-key'}]},'PUT')
        assert 'saved-private-key' not in json.dumps(call(base+'/overview'))
        call(base+'/models/discover',{'endpoint':endpoint+'/saved','modelId':model['id']})
        assert events[-1]==('/saved/models','Bearer saved-private-key')
        count=len(events)
        call(base+'/models/discover',{'endpoint':endpoint+'/other','modelId':model['id']},expected=400)
        assert len(events)==count,'跨地址泄露已有密钥'
        settings=call(base+'/overview')['settings'];settings['models'][0]['endpoint']=endpoint+'/changed/chat/completions'
        call(base+'/settings',{'settings':settings},'PUT',expected=400)
        settings=call(base+'/overview')['settings'];assert settings['models'][0]['endpoint']==model['endpoint']
        if os.environ.get('MIR2_COMPANION_UI')=='1':
            (root/'management-test.json').write_text(json.dumps({'api':address,'modelEndpoint':endpoint+'/ui','settings':settings}))
            subprocess.run(['node',str(pathlib.Path(__file__).with_name('management-ui.cjs')),str(root),env['MIR2_MANAGEMENT_ADMIN']],env=env,check=True,timeout=150)
            settings=call(base+'/overview')['settings']
            assert len(settings['nicknames'])==1000 and '浏览器自定昵称' in settings['nicknames']
            assert len(settings['models'])==3 and all(m['hasKey'] for m in settings['models'])
            assert len(call(base+'/schedules'))==9
            results=rows();scheduled=[r['profile'] for r in results if r['profile']['name']<'分页测试20']
            presets=call(base+'/schedules');assert len(scheduled)==20
            assert all(any(p['schedule']==r['schedule'] for p in presets) for r in scheduled)
            assert len({json.dumps(r['schedule'],sort_keys=True) for r in scheduled})>1,'整批共用一次抽取'
            enhanced=[r['profile'] for r in results if r['profile']['llmMode']==1]
            assert len(enhanced)==10 and all('分页测试10'<=r['name']<'分页测试20' and r['llmModelId']==model['id'] for r in enhanced)
        assert all(auth in ('Bearer test-private-key','Bearer saved-private-key','Bearer ui-private-key') for _,auth in events)
    finally:mock.shutdown();mock.server_close();thread.join()
    print('PASS 隔离管理API：所选角色、过期整批拒绝、日程CRUD、昵称、模型列表鉴权/重定向/大小边界/跨地址密钥保护。',flush=True)
