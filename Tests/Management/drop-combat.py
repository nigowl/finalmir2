"""仅在临时版本，通过原TCP验证多脚本叠加实际击杀、生成及拾取六件物品。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-drop-combat-')).resolve();data=root/'version'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();key='';process=None;log=None

def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=90):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError((root/'server.log').read_text())
        value=predicate()
        if value:return value
        time.sleep(.1)
    raise AssertionError('原TCP组合掉落等待超时')
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data)],env=os.environ|{'MIR2_DATA_ROOT':str(repo/'data/versions/default')},check=True)
    folder=data/'world/Envir/Drops/公共/测试';folder.mkdir(parents=True)
    (folder/'公共甲-100.txt').write_text('1/1 NativeChoice0\n')
    (folder/'公共乙-100.txt').write_text('1/1 NativeChoice0 2\n')
    (data/'world/Envir/Drops/NativeTarget.txt').write_text('; MIR2-DROP-COMPOSE 1\n#INSERT [公共/测试/公共甲-100.txt]\n#INSERT [公共/测试/公共乙-100.txt]\n; MIR2-MONSTER-RULES\n1/1 NativeChoice0 3\n1/1 Gold 1000\n')
    log=open(root/'server.log','w')
    process=subprocess.Popen(['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip()
    item=next(i for i in call('/management/development/catalog/items') if i['name']=='NativeChoice0')
    document=call('/management/admin/monsters/1/drops');assert document['finalContent']=='100.000% NativeChoice0 6\n100.000% Gold 1000\n' and document['loadedRules']==2,document
    base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'掉落验证员','class':0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    samples=[]
    def collected():
        detail=call(base+'/'+ident);row=detail['row'];assert not row['lastError'],detail
        if not row['characterId']:return False
        character=call('/management/admin/characters/'+str(row['characterId']))
        amount=sum(i['count'] for i in character['inventory'] if i['itemId']==item['id'])
        samples.append({'experience':character['experience'],'count':amount,'completed':character['completedQuests']})
        if amount>=6 and amount%6==0:
            assert not character['completedQuests'],'不能把任务奖励计作掉落'
            assert character['experience']>0,'未有实际击杀经验'
            assert len({i['uniqueId'] for i in character['inventory'] if i['itemId']==item['id']})==amount,'装备没有逐件生成原唯一编号'
            return character
        return False
    character=wait(collected)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(row['characterId']))['online'])
    call('/management/stop',method='POST');process.terminate();process.wait(60);assert process.returncode==0;process=None;log.close();log=None
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    (root/'proof.json').write_text(json.dumps({'samples':samples,'character':character,'rule':document['finalContent']},ensure_ascii=False,indent=2))
    print('PASS 原TCP击杀/经验增长、多公共脚本+自身合并100%掉6件、实际拾取/原唯一编号、无任务奖励混入、正常退出保存/SQLite完整性。')
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Drop combat fixture: '+str(root))
