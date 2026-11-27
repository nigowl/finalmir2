"""独立版本的原TCP建会扣费/入会/攻城/重启；不接触正式角色或GM资产。"""
import argparse,hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request,uuid
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from threading import Thread

parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--multiple',action='store_true');parser.add_argument('--organized',action='store_true');args=parser.parse_args()

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-guild-')).resolve();data=root/'version-a'
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('行会流程等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text();process=None;log.close();log=None
base='/management/companions'
def detail(profile):
    d=call(base+'/'+profile['id']);assert not d['row']['lastError'],d
    return d
def membership(profile,receipt):
    d=detail(profile)
    return d if d['runtime'] and d['runtime']['guildName']=='NativeGuild' and any(e['text']==receipt+':成功' for e in d['events']) else False
def command(profile,action):
    row=detail(profile)['row'];return call(base+'/'+profile['id']+'/command',{'action':action,'version':row['profile']['version']})
def configure(profile,preferences):
    row=detail(profile)['row'];return call(base+'/'+profile['id'],row['profile']|{'guilds':preferences},'PUT')
def settings(**values):
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|values},'PUT')
def organized():
    system=call('/management/settings');call('/management/settings',{'values':system['values']|{'maxAccountsPerIPHour':0,'maxCharactersPerIPHour':0},'version':system['version']},'PUT')
    settings(maxOnline=4);profiles=[]
    for index,name in zip([1,2,4,5],['SiegeCaptainA','SiegeCaptainB','SiegeMemberA','SiegeMemberB']):
        p=call(base,{'id':f'{index:08x}-0000-0000-0000-000000000000','name':name,'class':0,'enabled':True,'chat':True,'acceptGroup':False,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
        command(p,'start');wait(lambda:detail(p)['row']['characterId']>0 and detail(p)['runtime'] is not None)
        command(p,'stop');wait(lambda:call(base+'/overview')['online']==0);profiles.append(p)
    ids=[detail(p)['row']['characterId'] for p in profiles]
    def fixture(mode):subprocess.run(['dotnet',str(compat),'--organized-siege']+[str(i) for i in ids]+[mode],cwd=root,env=env,check=True)
    stop();fixture('prepare');start()
    for p in profiles:configure(p,{'mode':'auto','name':'','siege':False})
    samples=[];peaks=[0,0]
    def founded(count):
        observations=[detail(p) for p in profiles];samples.append(observations)
        for i,d in enumerate(observations[:2]):
            if d['runtime']:peaks[i]=max(peaks[i],d['runtime']['gold'])
        return all(d['runtime'] and d['runtime']['guildName'] for d in observations[:count]) and len(call('/management/admin/guilds'))==count and all(
            any(e['text']=='guild-create:成功' for sample in samples for e in sample[index]['events']) for index in range(count))
    # 先分别验证筹资扣费，避免四人抢同一小片测试刷新点把资产用例变成资源争夺测试。
    for index,p in enumerate(profiles[:2]):
        command(p,'start')
        try:wait(lambda:founded(index+1),120)
        except BaseException:
            (root/'funding-observation.json').write_text(json.dumps(samples,ensure_ascii=False));raise
        command(p,'pause')
    for p in profiles[:2]:command(p,'resume')
    for p in profiles[2:]:command(p,'start')
    wait(lambda:all(detail(p)['runtime'] and detail(p)['runtime']['guildName'] for p in profiles),90)
    names=[detail(p)['runtime']['guildName'] for p in profiles];assert names[0]==names[2] and names[1]==names[3] and names[0]!=names[1],names
    assert all(g>=1002000 for g in peaks),peaks
    for index,p in enumerate(profiles[:2]):
        d=detail(p);history=[e for sample in samples for e in sample[index]['events']]
        assert d['runtime']['gold']<150000 and any(e['text']=='guild-create:成功' for e in history) and any(e['text']=='public-buy:成功' for e in history),d
    (root/'funding-observation.json').write_text(json.dumps({'peaks':peaks,'samples':samples},ensure_ascii=False))
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop();fixture('battle');start()
    settings(allowPk=False)
    for p in profiles:configure(p,{'mode':'auto','name':'','siege':True})
    wait(lambda:all(detail(p)['runtime'] for p in profiles));time.sleep(4)
    assert not any(any(e['text']=='guild-register:成功' for e in detail(p)['events']) for p in profiles)
    settings(allowPk=True)
    registered=wait(lambda:next((p for p in profiles[:2] if any(e['text']=='guild-register:成功' for e in detail(p)['events'])),False),45)
    assert registered['id']==profiles[0]['id']
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop();fixture('scheduled-proof');start()
    battles=[];fighting=set();organized_by=set();damaged=set();dead=set();owner_changed=False
    def fought():
        nonlocal owner_changed
        ds=[detail(p) for p in profiles];cs=[call('/management/admin/characters/'+str(i)) for i in ids]
        battles.append({'details':ds,'characters':cs})
        for i,d in enumerate(ds):
            if any(e['state']=='会长组织' for e in d['events']):organized_by.add(i)
            if d['row']['state']=='行会作战':fighting.add(i)
            if d['runtime'] and d['runtime']['hp']<d['runtime']['maxHp']:damaged.add(i)
            if any(e['kind']=='death' for e in d['events']):dead.add(i)
        owner_changed=any(d['runtime'] and d['runtime']['guildGoal'].startswith('守卫') for d in ds[::2])
        return len(fighting)>0 and {0,1}.issubset(organized_by) and damaged and dead and owner_changed
    wait(fought,150)
    assert all(detail(p)['row']['profile']['guilds']['siege'] for p in profiles)
    for p in profiles:configure(p,{'mode':'auto','name':'','siege':False})
    wait(lambda:all((detail(p)['runtime'].get('goal') or {}).get('kind') not in ['guild-siege','guild-rally','guild-register'] for p in profiles))
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop();fixture('battle-proof');start()
    assert len(call('/management/admin/guilds'))==2
    assert [call('/management/admin/characters/'+str(i))['guildName'] for i in ids]==names
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    (root/'organized-result.json').write_text(json.dumps({'guilds':names,'peakGold':peaks,'fighting':list(fighting),'organized':list(organized_by),'damaged':list(damaged),'dead':list(dead),'samples':battles},ensure_ascii=False))
    print('PASS 原TCP筹建与会长组织攻城：真实打怪拾金补齐资金，原商城各买100万沃玛角、原NPC各扣1000金币建两会并分别入会；PK关闭不报名，会长按当前NPC许可报名并保存重启，原战期自动开战、双方会长会聊集结、成员跨图真实交战/承伤/死亡、攻方取得原经典王宫归属；关闭攻城恢复发展、会籍与资产重启保持。',flush=True)
def multiple():
    requests=[]
    class Model(BaseHTTPRequestHandler):
        def log_message(self,*args):pass
        def do_POST(self):
            content=b''
            if self.headers.get('Transfer-Encoding')=='chunked':
                while True:
                    size=int(self.rfile.readline().strip().split(b';')[0],16)
                    if not size:self.rfile.readline();break
                    content+=self.rfile.read(size);assert self.rfile.read(2)==b'\r\n'
            else:content=self.rfile.read(int(self.headers['Content-Length']))
            payload=json.loads(content);suggestion=json.loads(payload['messages'][1]['content']);requests.append(suggestion)
            assert 'develop' in suggestion['Candidates'] and payload['max_tokens']==24,suggestion
            response=json.dumps({'choices':[{'message':{'content':json.dumps({'text':'develop'})}}]}).encode()
            self.send_response(200);self.send_header('Content-Length',str(len(response)));self.end_headers();self.wfile.write(response)
    provider=ThreadingHTTPServer(('127.0.0.1',0),Model);Thread(target=provider.serve_forever,daemon=True).start()
    try:
        system=call('/management/settings');call('/management/settings',{'values':system['values']|{'maxAccountsPerIPHour':0,'maxCharactersPerIPHour':0},'version':system['version']},'PUT')
        settings(maxOnline=4);profiles=[]
        for index,name in zip([1,2,4,5],['GuildMultiA','GuildMultiB','GuildMemberA','GuildMemberB']):
            p=call(base,{'id':f'{index:08x}-0000-0000-0000-000000000000','name':name,'class':0,'enabled':True,'chat':True,'acceptGroup':False,
                'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
            command(p,'start');wait(lambda:detail(p)['row']['characterId']>0 and detail(p)['runtime'] is not None)
            command(p,'stop');wait(lambda:call(base+'/overview')['online']==0);profiles.append(p)
        ids=[detail(p)['row']['characterId'] for p in profiles];stop()
        subprocess.run(['dotnet',str(compat),'--companion-multiple-guilds']+[str(i) for i in ids],cwd=root,env=env,check=True);start()
        for p in profiles:configure(p,{'mode':'auto','name':'','siege':False})
        for p in profiles[:2]:command(p,'start')
        def founded():
            found=call('/management/admin/guilds');return found if len(found)==2 and all(detail(p)['runtime'] and detail(p)['runtime']['guildName'] for p in profiles[:2]) else False
        guilds=wait(founded,90);assert len({g['name'] for g in guilds})==2
        for p in profiles[:2]:assert detail(p)['runtime']['gold']==4000,detail(p)
        model={'id':str(uuid.uuid4()),'name':'多行会抉择模型','model':'fixture','endpoint':f'http://127.0.0.1:{provider.server_port}/v1/chat/completions','enabled':True}
        overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'models':[model],'defaultModelId':model['id']}},'PUT')
        target=detail(profiles[2])['row']['profile'];call(base+'/'+profiles[2]['id'],target|{'llmMode':2},'PUT');command(profiles[2],'start')
        def declined():
            d=detail(profiles[2]);return d if d['runtime'] and not d['runtime']['guildName'] and '暂缓入会' in d['runtime']['guildGoal'] and any(e['kind']=='llm-log' and e['state']=='待执行' for e in d['events']) else False
        wait(declined,60);time.sleep(3);assert len(requests)==1,requests
        target=detail(profiles[2])['row']['profile'];call(base+'/'+profiles[2]['id'],target|{'llmMode':0},'PUT');command(profiles[3],'start')
        wait(lambda:all(detail(p)['runtime'] and detail(p)['runtime']['guildName'] for p in profiles) and
            all(any(e['text']=='guild-join:成功' for e in detail(p)['events']) for p in profiles[2:]),90)
        names=[detail(p)['runtime']['guildName'] for p in profiles];assert names[0]==names[2] and names[1]==names[3] and names[0]!=names[1],names
        saved=[call('/management/admin/characters/'+str(i)) for i in ids];assert all(any(e['text']=='guild-join:成功' for e in detail(p)['events']) for p in profiles[2:])
        for p in profiles:command(p,'stop')
        wait(lambda:call(base+'/overview')['online']==0);stop();start()
        actual=[call('/management/admin/characters/'+str(i)) for i in ids]
        assert [(x['guildId'],x['guildName'],x['inventory']) for x in actual]==[(x['guildId'],x['guildName'],x['inventory']) for x in saved]
        assert len(call('/management/admin/guilds'))==2 and len(requests)==1
        (root/'multiple-result.json').write_text(json.dumps({'guilds':call('/management/admin/guilds'),'memberships':names,'requests':requests},ensure_ascii=False))
        stop();print('PASS 原TCP多行会：首会已存在仍建第二会，均实际扣1000金币/材料；四人按稳定阵营分别入会，模型一次暂缓入会生效、关闭后规则继续，正常保存重启保持会籍及资产。',flush=True)
    finally:provider.shutdown();provider.server_close()
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();settings(enabled=True,maxOnline=3)
    if args.organized:organized();raise SystemExit(0)
    if args.multiple:multiple();raise SystemExit(0)
    profiles=[]
    for name in ['GuildFounder','GuildJoiner']:
        p=call(base,{'name':name,'class':0,'gender':0,'enabled':True,'chat':True,'acceptGroup':False,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
        command(p,'start')
        wait(lambda:detail(p)['row']['characterId']>0 and detail(p)['runtime'] is not None)
        command(p,'stop');wait(lambda:call(base+'/overview')['online']==0)
        profiles.append(p)
    founder,joiner=profiles;fid=detail(founder)['row']['characterId'];jid=detail(joiner)['row']['characterId'];stop()
    def fixture(mode):subprocess.run(['dotnet',str(compat),'--companion-guild',str(fid),str(jid),mode],cwd=root,env=env,check=True)
    fixture('prepare');source=json.loads((data/'runtime/companion-guild-fixture.json').read_text());start()
    configure(founder,{'mode':'create','name':'NativeGuild','siege':False});configure(joiner,{'mode':'join','name':'NativeGuild','siege':False})
    command(founder,'start');wait(lambda:detail(founder)['runtime'] is not None)
    before=call('/management/admin/characters/'+str(fid));time.sleep(5)
    d=detail(founder);assert d['runtime']['gold']==1500 and not d['runtime']['guildName'],d
    assert any(int(i['uniqueId'])==source['uniqueId'] and i['count']==1 for i in call('/management/admin/characters/'+str(fid))['inventory'])
    command(founder,'stop');wait(lambda:call(base+'/overview')['online']==0);stop();fixture('ready');start()
    command(founder,'start')
    created=wait(lambda:membership(founder,'guild-create'))
    assert created['runtime']['gold']==4000 and any(e['text']=='guild-create:成功' for e in created['events']),created
    assert not any(i['itemId']==source['token'] for i in call('/management/admin/characters/'+str(fid))['inventory'])
    command(joiner,'start');joined=wait(lambda:membership(joiner,'guild-join'))
    assert joined['runtime']['gold']==1000 and any(e['text']=='guild-join:成功' for e in joined['events']),joined
    command(founder,'pause');command(joiner,'pause')
    configure(joiner,{'mode':'join','name':'NativeGuild','siege':True})
    wait(lambda:'PK' in detail(joiner)['runtime']['guildGoal'])
    assert (detail(joiner)['runtime'].get('goal') or {}).get('kind')!='guild-siege'
    settings(allowPk=True)
    wait(lambda:(detail(joiner)['runtime'].get('goal') or {}).get('kind')=='guild-siege')
    command(joiner,'resume');samples=[]
    def captured():
        d=detail(joiner);c=call('/management/admin/characters/'+str(jid));samples.append({'detail':d,'character':c})
        return c if c['mapId']==source['palace'] and (d['runtime'].get('goal') or {}).get('kind')!='guild-siege' else False
    wait(captured,90);(root/'guild-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop();fixture('proof');start()
    a=call('/management/admin/characters/'+str(fid));b=call('/management/admin/characters/'+str(jid))
    assert a['guildId']==b['guildId'] and a['guildName']=='NativeGuild' and b['mapId']==source['palace']
    assert detail(joiner)['row']['profile']['guilds']=={'mode':'join','name':'NativeGuild','siege':True}
    stop()
    fixture('classic');start();command(joiner,'start')
    wait(lambda:detail(joiner)['runtime'] and (detail(joiner)['runtime'].get('goal') or {}).get('mapId')==source['palace'])
    def classic_captured():
        d=detail(joiner);c=call('/management/admin/characters/'+str(jid))
        return c if c['mapId']==source['palace'] and d['runtime']['guildGoal'].startswith('守卫') else False
    wait(classic_captured,90);wait(lambda:detail(joiner)['row']['state']=='行会驻守')
    configure(joiner,{'mode':'join','name':'NativeGuild','siege':False})
    wait(lambda:(detail(joiner)['runtime'].get('goal') or {}).get('kind')!='guild-siege')
    wait(lambda:'行会' not in detail(joiner)['row']['state'])
    command(joiner,'stop');wait(lambda:call(base+'/overview')['online']==0);stop();fixture('classic-proof');start()
    assert call('/management/admin/characters/'+str(jid))['mapId']==source['palace']
    assert detail(joiner)['row']['profile']['guilds']=={'mode':'join','name':'NativeGuild','siege':False}
    assert detail(founder)['row']['characterId']==fid and detail(joiner)['row']['characterId']==jid
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP行会：预算不足不扣物，原NPC建会5000→4000/材料原编号移除，主动申请/邀请确认入会无扣款，PK关闭阻断/开启后公开步行夺取王宫，原归属/成员/设置和资产重启保持。',flush=True)
    print('PASS 原TCP经典攻城：山丘在外图仍进入实际王宫，原引擎确认本会归属；取消参战后恢复和平模式，正常重启保持身份/会籍/归属及关闭设置。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Guild fixture: '+str(root),flush=True)
