"""用受保护API桩验证只读巡检的全量分页及不完整名单拒绝，不访问正式服务。"""
import contextlib,hashlib,io,json,pathlib,runpy,sys,tempfile,urllib.parse
from unittest.mock import patch

repo=pathlib.Path(__file__).resolve().parents[2]
script=repo/'scripts/companions-observe.py'
checks=0
for mode,count in [('empty',0),('boundary',20),('two',21),('three',41),('max',500),('changed',21),('duplicate',21),('truncated',21)]:
    with tempfile.TemporaryDirectory(prefix='mir2-companions-observe-') as temporary:
        version=pathlib.Path(temporary)/'version';(version/'runtime').mkdir(parents=True)
        (version/'runtime/admin.key').write_text('OnlyTestKey')
        path=version/'runtime/logs/companion-observation-latest.json'
        if mode=='duplicate':
            path.parent.mkdir();path.write_text('[{"utc":"previous-complete-sample"}]')
        fingerprint=hashlib.sha256(str(version.resolve()).encode()).hexdigest().upper()
        pages=[];rounds=0
        def api(request,timeout):
            global rounds
            assert request.get_method()=='GET' and request.data is None,'只读巡检发出了写入请求'
            assert request.get_header('Authorization')=='Bearer OnlyTestKey'
            assert request.get_header('X-mir2-version')==fingerprint
            url=urllib.parse.urlparse(request.full_url)
            assert url.netloc=='127.0.0.1:1','测试请求意外指向真实服务'
            if url.path=='/management/companions/world-checks':body={'questCount':0,'quests':[]}
            elif url.path=='/management/status':body={'loopMilliseconds':1}
            elif url.path=='/management/companions':
                page=int(urllib.parse.parse_qs(url.query)['page'][0]);pages.append(page)
                if page==0:rounds+=1
                total=count+int(mode=='changed' and (rounds>1 or page>0))
                ids=list(range(page*20,min(total,(page+1)*20)))
                if mode=='duplicate' and page==1:ids=[19]
                if mode=='truncated' and page==1:ids=[]
                body={'total':total,'items':[{'profile':{'id':str(i),'name':'Test'+str(i)},'characterId':0,'level':1,
                    'map':'Test','x':0,'y':0,'state':'离线','reason':'','lastError':''} for i in ids]}
            else:
                assert url.path.startswith('/management/companions/'),url.path
                body={'runtime':None,'events':[]}
            return io.BytesIO(json.dumps(body).encode())
        output=io.StringIO();failed=False
        with patch.object(sys,'argv',[str(script),'--version',str(version),'--server','http://127.0.0.1:1','--seconds','0']),patch('urllib.request.urlopen',api),contextlib.redirect_stdout(output):
            try:runpy.run_path(str(script),run_name='__main__')
            except RuntimeError as error:
                assert mode in ['duplicate','truncated'] and '部分名单' in str(error),error
                failed=True
        if mode in ['duplicate','truncated']:
            assert failed and pages==[0,1,0,1],(mode,pages)
            assert path.read_text()=='[{"utc":"previous-complete-sample"}]' if mode=='duplicate' else not path.exists()
        else:
            rows=json.loads(path.read_text())[0]['rows'];expected=count+int(mode=='changed')
            assert len(rows)==expected and {r['id'] for r in rows}=={str(i) for i in range(expected)},(mode,len(rows))
            assert pages==([0,1,0,1] if mode=='changed' else list(range(max(1,(count+19)//20)))),(mode,pages)
        assert 'OnlyTestKey' not in output.getvalue()
        checks+=1
print(f'PASS {checks} 巡检分页场景：0/20/21/41/500人、并发新增重读、重复/截断拒绝，保留鉴权及版本头。')
