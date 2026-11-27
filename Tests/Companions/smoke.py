"""隔离验证陪玩管理、真实 TCP 成长、启停与 SQLite 重启；不接触正式库。"""
import datetime,hashlib,json,os,pathlib,socket,subprocess,tempfile,time,urllib.request,urllib.error,shutil,uuid,sqlite3,signal as process_signal
from http.server import HTTPServer,BaseHTTPRequestHandler
from threading import Thread

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-')).resolve()
children=[]
llm_requests=[]
class ModelStub(BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    def do_POST(self):
        if self.headers.get('Transfer-Encoding')=='chunked':
            body=b''
            while True:
                size=int(self.rfile.readline().split(b';')[0],16)
                if not size:self.rfile.readline();break
                body+=self.rfile.read(size);self.rfile.read(2)
        else:body=self.rfile.read(int(self.headers['Content-Length']))
        llm_requests.append(json.loads(body))
        if self.path in ('/slow','/slow/chat/completions'):time.sleep(2)
        if self.path in ('/unauthorized','/unauthorized/chat/completions'):self.send_response(401);self.end_headers();return
        if self.path in ('/bad','/bad/chat/completions'):
            self.send_response(200);self.end_headers();self.wfile.write(b'{"choices":[{"message":{"content":"not json"}}]}');return
        body=json.dumps({'choices':[{'message':{'content':json.dumps({'text':'模型测试回复：我先补药。'},ensure_ascii=False)}}]}).encode()
        self.send_response(200);self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
model_server=HTTPServer(('127.0.0.1',0),ModelStub)
Thread(target=model_server.serve_forever,daemon=True).start()
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
game,http=port(),port()
address=f'http://127.0.0.1:{http}'
data=root/'version-a'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
key='';version=hashlib.sha256(str(data).encode()).hexdigest().upper()
def call(path,body=None,method=None,expected=200,headers=None):
    h={'Authorization':'Bearer '+key,'X-Mir2-Version':version}|(headers or {})
    if body is not None:h['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=h,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=30) as response:
            assert response.status==expected;return json.load(response)
    except urllib.error.HTTPError as e:
        if e.code!=expected:raise AssertionError(f'{path}: {e.code} {e.read().decode()}') from e
        body=e.read();return json.loads(body) if body else None
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        value=predicate()
        if value:return value
        time.sleep(.3)
    raise AssertionError('等待条件超时')
def start():
    global key
    log=open(root/f'server-{len(children)}.log','w')
    command=[os.environ['MIR2_COMPANION_SERVER_EXE']] if 'MIR2_COMPANION_SERVER_EXE' in os.environ else ['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')]
    command.append(f'--mir2-instance={repo}:{data / "runtime"}:server')
    p=subprocess.Popen(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    children.append((p,log))
    def ready():
        if p.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        try:return json.load(urllib.request.urlopen(address+'/health',timeout=2))['ready']
        except (OSError,ValueError):return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    p,log=children[-1];p.terminate();p.wait(60);assert p.returncode==0,pathlib.Path(log.name).read_text();log.close()
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data),'--companion-fixture'],cwd=repo,check=True)
    start()
    base='/management/companions'
    server_settings=call('/management/settings')
    call('/management/settings',expected=409,headers={'X-Mir2-Version':'wrong'})
    call('/management/settings',expected=409,headers={'X-Mir2-Version':''})
    changed=call('/management/settings',{'values':server_settings['values']|{'maxUser':100,'maxIP':1},'version':server_settings['version']},'PUT')
    call('/management/settings',{'values':server_settings['values'],'version':server_settings['version']},'PUT',expected=409)
    assert changed['values']['maxIP']==1
    overview=call(base+'/overview');assert len(overview['presets'])==12
    assert overview['limits']['maxPerIp']==1 and overview['limits']['effectiveMaxOnline']==0
    call(base+'/overview',expected=409,headers={'X-Mir2-Version':'wrong'})
    settings=overview['settings'];settings['enabled']=True
    call(base+'/settings',{'settings':settings},'PUT')
    preset=overview['presets'][2]
    profile=call(base,{'name':'CompanionOne','alias':'隔离测试','class':0,'gender':0,'enabled':True,'presetKey':preset['key'],'presetRevision':preset['revision'],'traits':preset['traits'],
        'schedule':{'timeZone':'Asia/Shanghai','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id']
    assert call(base,profile)['id']==ident
    assert call(base)['total']==1
    call(base+'/'+ident,profile|{'alias':'已保存'},'PUT')
    call(base+'/'+ident,profile|{'alias':'冲突覆盖'},'PUT',expected=409)
    row=call(base+'/'+ident)['row'];assert row['profile']['alias']=='已保存'
    assert 'password' not in json.dumps(row).lower()
    pool=['BatchAlpha','BatchBeta','BatchGamma','CompanionOne','BatchAlpha','无效@昵称']
    preview=call(base+'/batch-preview',{'count':3,'nicknames':pool,'template':{'tags':'隔离批次'},'randomClass':True,'randomGender':True,'randomPersonality':True})
    assert len(preview['profiles'])==3 and len({p['name'] for p in preview['profiles']})==3 and preview['available']==3
    assert call(base)['total']==1
    batch={'profiles':preview['profiles'],'nicknames':pool,'settingsVersion':call(base+'/overview')['settings']['version']}
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:
        target='companions/profiles/'+preview['profiles'][1]['id'].replace('-','')+'.json'
        database.execute("CREATE TRIGGER reject_companion_batch BEFORE INSERT ON snapshots WHEN NEW.name='"+target+"' BEGIN SELECT RAISE(ABORT, 'isolated failure'); END")
    call(base+'/batch',batch,expected=500);assert call(base)['total']==1
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:database.execute('DROP TRIGGER reject_companion_batch')
    created=call(base+'/batch',batch);assert len(created)==3 and call(base)['total']==4
    assert [p['id'] for p in call(base+'/batch',batch)]==[p['id'] for p in created]
    assert call(base)['total']==4
    for p in created:call(base+'/'+p['id']+'/command',{'action':'release','version':p['version']})
    settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'nicknames':overview['settings']['nicknames']}},'PUT')
    call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    def blocked_by_ip():
        value=call(base+'/'+ident)['row']
        return value if '同 IP 连接' in value['reason'] else False
    blocked=wait(blocked_by_ip,10);assert blocked['characterId']==0
    # 提高 MaxIP 后同一会话自动排队上线，无需重启游戏宿主。
    changed=call('/management/settings',{'values':changed['values']|{'maxIP':32},'version':changed['version']},'PUT')
    assert call(base+'/overview')['limits']['maxPerIp']==32
    def entered():
        value=call(base+'/'+ident)['row']
        if value['lastError']:raise AssertionError(value)
        return value if value['characterId']>0 and value['level']>0 and call('/management/admin/characters/'+str(value['characterId']))['online'] else False
    online=wait(entered,50)
    assert isinstance(online['x'],int) and isinstance(online['y'],int)
    assert all('time' in e and 'kind' in e for e in call(base+'/'+ident)['events'])
    wait(lambda:call(base+'/'+ident)['row']['level']>=2,60)
    def quest_done():
        row=call(base+'/'+ident)['row']
        if row['lastError']:raise AssertionError(row)
        character=call('/management/admin/characters/'+str(row['characterId']))
        return character if 1 in character['completedQuests'] else False
    completed=wait(quest_done,70)
    row=call(base+'/'+ident)['row']
    call(base+'/'+ident+'/command',{'action':'pause','version':row['profile']['version']})
    wait(lambda:call(base+'/'+ident)['row']['state']=='已暂停',10)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    saved=call(base+'/'+ident)['row'];assert saved['todaySeconds']>0
    if os.environ.get('MIR2_COMPANION_UI')=='1':
        admin_port=port();admin_log=open(root/'admin.log','w')
        admin=subprocess.Popen(['dotnet',str(repo/'src/Admin/bin/Release/net8.0/Mir2.Admin.dll'),f'--mir2-instance={repo}:{data / "runtime"}:admin'],cwd=root,env=env|{'MIR2_ADMIN_PORT':str(admin_port),'MIR2_SERVER':address},stdout=admin_log,stderr=subprocess.STDOUT)
        children.append((admin,admin_log))
        subprocess.run(['node',str(repo/'tests/Companions/ui.cjs'),str(root),f'http://127.0.0.1:{admin_port}',address,f'http://127.0.0.1:{model_server.server_port}/'],check=True,timeout=180)
        admin.terminate();admin.wait(30);admin_log.close();children.pop()
        wait(lambda:not call('/management/admin/characters/'+str(saved['characterId']))['online'],15)
        saved=call(base+'/'+ident)['row']
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-supplies',str(saved['characterId'])],cwd=root,env=env,check=True)
    supply_fixture=json.loads((data/'runtime/companion-supply-fixture.json').read_text())
    start()
    assert call('/management/settings')['values']['maxIP']==32
    restored=call(base+'/'+ident)['row'];assert restored['characterId']==saved['characterId'] and restored['level']==saved['level']
    assert restored['profile']['traits']==saved['profile']['traits'] and restored['todaySeconds']>=saved['todaySeconds']
    assert 1 in call('/management/admin/characters/'+str(saved['characterId']))['completedQuests']
    call(base+'/'+ident+'/command',{'action':'start','version':restored['profile']['version']})
    supply_maps=set();supply_reasons=set();supply_assets=set();supply_events=set();stock_samples=[]
    def supplied():
        detail=call(base+'/'+ident)
        supply_maps.add(detail['row']['map']);supply_reasons.add(detail['row']['reason'])
        supply_assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        supply_events.update((e['kind'],e['state'],e['reason']) for e in detail['events'])
        if detail['row']['lastError']:raise AssertionError(detail)
        character=call('/management/admin/characters/'+str(saved['characterId']))
        town=sum(item['count'] for item in character['inventory'] if item['itemId']==supply_fixture['townId'])
        red=sum(item['count'] for item in character['inventory'] if item['itemId']==supply_fixture['potionId'])
        stock_samples.append({'town':town,'red':red,'gold':(detail['runtime'] or {}).get('gold')})
        assert town<=1,character
        if town:assert red>=12,(detail,character)
        return detail if town==1 and all(kind+':成功' in supply_assets for kind in ['sell','repair','buy']) else False
    try:wait(supplied,100)
    except AssertionError:
        print(json.dumps(call(base+'/'+ident),ensure_ascii=False),flush=True)
        print(json.dumps(call('/management/admin/characters/'+str(saved['characterId'])),ensure_ascii=False),flush=True)
        raise
    character=call('/management/admin/characters/'+str(saved['characterId']))
    weapon=next(i for i in character['inventory'] if i['grid']=='Equipment' and i['slot']==0)
    assert weapon['durability']>100 and weapon['durability']==weapon['maxDurability']
    assert {'Native Test Return','Native Test Field'}.issubset(supply_maps),supply_maps
    assert any(state=='撤离中' and '补给途中遭受攻击' in reason for _,state,reason in supply_events),supply_events
    assert not any(kind=='death' for kind,_,_ in supply_events),supply_events
    assert any('出售普通余货' in reason for reason in supply_reasons),supply_reasons
    assert not any(item['name']=='NativeSurplus' for item in character['inventory'])
    assert all(any(item['name']==name for item in character['inventory']) for name in ['NativeBackup','NativeProtected'])
    reserves=[sum(item['count'] for item in character['inventory'] if item['name']==name) for name in ['NativeMaterial','NativeFood']]
    assert all(5<=count<=11 for count in reserves) and any(count<11 for count in reserves),reserves
    for name,count in [('NativeQuestMaterial',10),('NativeRecipeMaterial',10)]:
        assert sum(item['count'] for item in character['inventory'] if item['name']==name)==count,(name,character['inventory'])
    item_ids={int(item['uniqueId']) for item in character['inventory']}
    assert all(ident not in item_ids for ident in supply_fixture['armourSold']),character['inventory']
    assert all(ident in item_ids for ident in [supply_fixture['armourKeep'],supply_fixture['armourEnhanced'],*supply_fixture['armourQuest']]),character['inventory']
    assert any(int(item['uniqueId'])==supply_fixture['equippedArmour'] and item['grid']=='Equipment' and item['slot']==1 for item in character['inventory'])
    assert sum(item['count'] for item in character['inventory'] if item['itemId']==supply_fixture['armourItemId'])==2,character['inventory']
    print('PASS 原TCP重复衣服减负：两件普通重复件出售，最好耐久备装、强化件、三件未挂当前NPC的任务衣服及已穿衣服保留。')
    time.sleep(3)
    character=call('/management/admin/characters/'+str(saved['characterId']))
    assert sum(item['count'] for item in character['inventory'] if item['itemId']==supply_fixture['townId'])==1,character
    assert any(b['town']==0 and a['town']==1 and b['gold']-a['gold']==supply_fixture['townPrice'] for b,a in zip(stock_samples,stock_samples[1:])),stock_samples
    (root/'town-purchase-observation.json').write_text(json.dumps(stock_samples))
    print('PASS 原TCP先补红药再购回城卷：动态物品编号，金币实际扣1000，库存0→1且不重复囤购。')
    row=call(base+'/'+ident)['row']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-stocked-sale',str(saved['characterId'])],cwd=root,env=env,check=True)
    stocked_fixture=json.loads((data/'runtime/companion-stocked-sale-fixture.json').read_text())
    assert stocked_fixture['sold']
    start()
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    stocked_assets=set()
    def sold_without_shortage():
        detail=call(base+'/'+ident)
        if detail['row']['lastError']:raise AssertionError(detail)
        stocked_assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        character=call('/management/admin/characters/'+str(saved['characterId']))
        ids={int(item['uniqueId']) for item in character['inventory']}
        return character if 'sell:成功' in stocked_assets and sum(item not in ids for item in stocked_fixture['sold'])>=3 else False
    character=wait(sold_without_shortage,80)
    for _ in range(10):
        time.sleep(.3);sold_without_shortage()
    character=call('/management/admin/characters/'+str(saved['characterId']))
    assert 'buy:成功' not in stocked_assets,stocked_assets
    assert not any(item['name']=='NativeTemptingArmour' for item in character['inventory']),character
    item_ids={int(item['uniqueId']) for item in character['inventory']}
    assert all(item in item_ids for item in [supply_fixture['armourKeep'],supply_fixture['armourEnhanced'],*supply_fixture['armourQuest'],supply_fixture['equippedArmour']]),character
    print('PASS 原TCP仅出售BUYSELL：药卷齐全时实际减负，保留保护物；货架有廉价高防升级衣服也不顺带购买。')
    row=call(base+'/'+ident)['row']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-meat',str(saved['characterId'])],cwd=root,env=env,check=True)
    meat_fixture=json.loads((data/'runtime/companion-meat-fixture.json').read_text())
    start()
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    meat_assets=set();meat_routed=False;meat_samples=[]
    def meat_unloaded():
        global meat_routed
        detail=call(base+'/'+ident);row=detail['row'];runtime=detail['runtime'] or {};goal=runtime.get('goal') or {}
        if row['lastError']:raise AssertionError(detail)
        planned={item['uniqueId'] for item in goal.get('sellItems') or []}
        if goal.get('key','').startswith('unload/'):
            assert planned and planned.issubset(set(meat_fixture['saleIds'])),goal
            if goal['mapId']==meat_fixture['destination'] and goal.get('nextExit'):meat_routed=True
        meat_assets.update(event['text'] for event in detail['events'] if event['kind']=='asset')
        character=call('/management/admin/characters/'+str(saved['characterId']))
        ids={int(item['uniqueId']) for item in character['inventory']}
        assert all(item in ids for item in meat_fixture['protectedIds']),character
        meat_samples.append({'runtime':runtime,'map':row['map'],'gold':runtime.get('gold'),'removed':sum(item not in ids for item in meat_fixture['saleIds'])})
        return detail if meat_routed and 'sell:成功' in meat_assets and meat_samples[-1]['removed']>=3 and not goal.get('key','').startswith('unload/') else False
    detail=wait(meat_unloaded,90)
    for _ in range(10):
        time.sleep(.3);meat_unloaded()
    assert detail['runtime']['gold']>meat_fixture['gold'],detail
    character=call('/management/admin/characters/'+str(saved['characterId']))
    assert all(any(int(item['uniqueId'])==meat_fixture[key] and item['grid']=='Equipment' for item in character['inventory']) for key in ['weapon','armour']),character
    assert 'buy:成功' not in meat_assets,meat_assets
    (root/'meat-unload-observation.json').write_text(json.dumps(meat_samples))
    print('PASS 原TCP满包普通肉主动跨图减负：实际出售/金币增长，未接任务肉、配方、绑定/强化肉和装备保留，无其他任务或怪物引路。')
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-exit',str(saved['characterId'])],cwd=root,env=env,check=True)
    exit_fixture=json.loads((data/'runtime/companion-exit-fixture.json').read_text())
    start()
    blockers=call('/management/development/catalog/npcs?search=Native%20Exit%20Blocker')
    assert len(blockers)==8,blockers
    blocker_locations=set()
    for blocker in blockers:
        definition=call('/management/development/npcs/'+str(blocker['id']))
        assert definition['mapId']==exit_fixture['origin'] and not definition['timeVisible']
        blocker_locations.add((definition['x'],definition['y']))
    assert blocker_locations=={(p['x'],p['y']) for p in exit_fixture['neighbors']}
    character=call('/management/admin/characters/'+str(saved['characterId']))
    assert (character['mapId'],character['x'],character['y'])==(exit_fixture['origin'],exit_fixture['x'],exit_fixture['y'])
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    exit_assets=set()
    def escaped_crowded_exit():
        detail=call(base+'/'+ident)
        if detail['row']['lastError']:raise AssertionError(detail)
        exit_assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        character=call('/management/admin/characters/'+str(saved['characterId']))
        assert not any(e['kind']=='death' for e in detail['events']),detail
        return detail if character['mapId']==exit_fixture['destination'] and 'buy:成功' in exit_assets else False
    wait(escaped_crowded_exit,30)
    print('PASS 原TCP出口八邻格均被NPC占用：站在出口直接转向触发原切图，30秒内抵达商店并补药，无死亡。')
    row=call(base+'/'+ident)['row']
    model_id=str(uuid.uuid4());alternate_id=str(uuid.uuid4())
    models=[{'id':model_id,'name':'社交模型','endpoint':f'http://127.0.0.1:{model_server.server_port}/','model':'social-fixture','enabled':True},
            {'id':alternate_id,'name':'规划模型','endpoint':f'http://127.0.0.1:{model_server.server_port}/','model':'planning-fixture','enabled':True}]
    settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'models':settings['models']+models,'defaultModelId':alternate_id,'dailyLlmCalls':20},'modelKeys':[{'id':model_id,'key':'isolated-key'}]},'PUT')
    configured=call(base+'/'+ident,row['profile']|{'pkMode':'duel','llmMode':1,'llmModelId':model_id,'traits':row['profile']['traits']|{'aggression':95,'social':95,'helpfulness':95,'talkative':95}},'PUT')
    wait(lambda:any(configured['version'] in e['text'] for e in call(base+'/'+ident)['events']),10)
    test=call(base+'/models/test',{'model':models[1]});assert test['success'],test
    assert llm_requests[-1]['model']=='planning-fixture'
    invalid_model=models[1]|{'id':str(uuid.uuid4()),'endpoint':models[1]['endpoint']+'bad'}
    rejected=call(base+'/models/test',{'model':invalid_model});assert not rejected['success'] and '格式' in rejected['message']
    settings=call(base+'/overview')['settings'];assert next(m for m in settings['models'] if m['id']==model_id)['hasKey'] and 'isolated-key' not in json.dumps(settings)
    call(base+'/settings',{'settings':settings|{'models':[models[1]]}},'PUT',expected=409)
    signal=root/'social-ready'
    social_log=open(root/'social.log','w')
    human=subprocess.Popen(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-social',str(game),'CompanionOne',str(signal)],cwd=root,env=env,stdout=social_log,stderr=subprocess.STDOUT)
    children.append((human,social_log))
    def social_ready():
        if human.poll() is not None:raise AssertionError(pathlib.Path(social_log.name).read_text())
        return signal.exists()
    wait(social_ready,65)
    settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'allowPk':True}},'PUT')
    signal.with_suffix('.continue').write_text('continue')
    human.wait(140);social_log.close();children.pop()
    if human.returncode!=0:
        print(json.dumps(call(base+'/'+ident),ensure_ascii=False),flush=True)
        raise AssertionError(pathlib.Path(social_log.name).read_text())
    print(pathlib.Path(social_log.name).read_text().strip())
    assert llm_requests and all('已保存' not in json.dumps(request,ensure_ascii=False) for request in llm_requests)
    assert llm_requests[-1]['model']=='social-fixture'
    # 只终止本脚本所启动服务端的直接子进程，不能按进程名结束正式实例。
    server_pid=next(p.pid for p,_ in reversed(children) if p.poll() is None)
    def worker_pid():
        listing=subprocess.check_output(['ps','-axo','pid=,ppid=,command='],text=True)
        matches=[int(parts[0]) for line in listing.splitlines() if len(parts:=line.split(None,2))==3 and int(parts[1])==server_pid and 'Mir2.Companion' in parts[2]]
        assert len(matches)==1,matches
        return matches[0]
    worker=worker_pid();os.kill(worker,process_signal.SIGKILL)
    wait(lambda:call(base+'/'+ident)['row']['lastError'],10)
    assert call('/health')['ready']
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    wait(entered,30)
    worker=worker_pid();os.kill(worker,process_signal.SIGSTOP)
    try:
        time.sleep(32)
        assert call('/health')['ready']
        assert call(base+'/overview')['online']==0
    finally:
        try:os.kill(worker,process_signal.SIGCONT)
        except ProcessLookupError:pass
    wait(lambda:not next(a for a in call('/management/admin/accounts') if a['id']==saved['accountId'])['online'],10)
    for cycle in range(3):
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
        wait(entered,30)
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not next(a for a in call('/management/admin/accounts') if a['id']==saved['accountId'])['online'],10)
    print('PASS 陪玩进程异常退出/卡死隔离、游戏宿主存活、重新启动及连续三轮启停。')
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']});wait(entered,30)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:
        database.execute("CREATE TRIGGER reject_companion_usage BEFORE UPDATE ON snapshots WHEN NEW.name LIKE 'companions/profiles/%' BEGIN SELECT RAISE(ABORT,'isolated storage failure'); END")
    wait(lambda:next(c for c in call(base+'/overview')['capabilities'] if c['name']=='运行监管')['state']=='阻断',20)
    assert call('/health')['ready']
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:database.execute('DROP TRIGGER reject_companion_usage')
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']});wait(entered,30)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not next(a for a in call('/management/admin/accounts') if a['id']==saved['accountId'])['online'],10)
    print('PASS 陪玩用量落库失败时停止组件且游戏宿主存活，解除故障后可恢复。')
    soak_seconds=int(os.environ.get('MIR2_COMPANION_SOAK_SECONDS','0'))
    if soak_seconds:soak_seconds=max(30,soak_seconds)
    if soak_seconds:
        settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'maxOnline':2,'allowPk':False}},'PUT')
        row=call(base+'/'+ident)['row'];main_profile=call(base+'/'+ident,row['profile']|{'llmMode':0,'pkMode':'avoid','acceptGroup':False},'PUT')
        extra=call(base,{'name':'SoakCompanion','enabled':True,'acceptGroup':False,'schedule':main_profile['schedule']})
        for p in [main_profile,extra]:call(base+'/'+p['id']+'/command',{'action':'start','version':p['version']})
        wait(lambda:call(base+'/'+extra['id'])['row']['characterId']>0 and call(base+'/overview')['online']==2,40)
        samples=[];locations={ident:set(),extra['id']:set()};deadline=time.monotonic()+soak_seconds
        while time.monotonic()<deadline:
            assert call('/health')['ready'] and call(base+'/overview')['online']==2
            for pid in locations:
                row=call(base+'/'+pid)['row'];assert not row['lastError'],row
                char=call('/management/admin/characters/'+str(row['characterId']));locations[pid].add((char['mapId'],char['x'],char['y']))
            rss=subprocess.check_output(['ps','-o','rss=,%cpu=','-p',str(worker_pid())],text=True).split()
            samples.append([round(float(rss[0])/1024,1),float(rss[1])]);time.sleep(2)
        assert all(len(points)>1 for points in locations.values()),locations
        (root/'soak-metrics.json').write_text(json.dumps({'seconds':soak_seconds,'samplesRssMiBAndPsCpu':samples,'locationCounts':[len(v) for v in locations.values()]}))
        cache=data/'runtime/cache/companions';assert not list(cache.glob('*.map')) and not list(cache.glob('*.tmp'))
        call(base+'/stop-all',{});wait(lambda:call(base+'/overview')['online']==0,10)
        extra_row=call(base+'/'+extra['id'])['row'];call(base+'/'+extra['id'],extra_row['profile']|{'enabled':False},'PUT')
        settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'enabled':True,'maxOnline':1}},'PUT')
        print(f'PASS 双陪玩连续{soak_seconds}秒采样{len(samples)}次，位置变化{[len(v) for v in locations.values()]}，工作进程RSS范围{min(s[0] for s in samples)}–{max(s[0] for s in samples)}MiB，CPU范围{min(s[1] for s in samples)}–{max(s[1] for s in samples)}%，地图暂存无残留。')
    row=call(base+'/'+ident)['row'];limited=call(base+'/'+ident,row['profile']|{'schedule':row['profile']['schedule']|{'dailyMinutes':1}},'PUT')
    call(base+'/'+ident+'/command',{'action':'start','version':limited['version']})
    wait(lambda:'额度已用完' in call(base+'/'+ident)['row']['reason'],10)
    wait(lambda:not next(a for a in call('/management/admin/accounts') if a['id']==saved['accountId'])['online'],10)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident,row['profile']|{'schedule':row['profile']['schedule']|{'dailyMinutes':60}},'PUT')
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    saved=call(base+'/'+ident)['row']
    healer=call(base,{'name':'HealingBuddy','class':2,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,'schedule':saved['profile']['schedule']})
    healer_id=healer['id']
    call(base+'/'+healer_id+'/command',{'action':'start','version':healer['version']})
    healer_row=wait(lambda:(value if (value:=call(base+'/'+healer_id)['row'])['characterId']>0 else False),35)
    call(base+'/'+healer_id+'/command',{'action':'stop','version':healer_row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(healer_row['characterId']))['online'],15)
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-healing',str(healer_row['characterId'])],cwd=root,env=env,check=True)
    start()
    healer_row=call(base+'/'+healer_id)['row']
    call(base+'/'+healer_id+'/command',{'action':'start','version':healer_row['profile']['version']})
    call(base+'/'+healer_id+'/command',{'action':'pause','version':healer_row['profile']['version']})
    healing_events={};healing_samples=[]
    def self_healed():
        detail=call(base+'/'+healer_id)
        assert not detail['row']['lastError'],detail
        for event in detail['events']:
            if event['kind']=='healing':healing_events[event['time']]=event
        runtime=detail['runtime']
        if runtime:healing_samples.append(runtime)
        return detail if runtime and runtime['hp']>=runtime['maxHp']*.8 and runtime['mp']<74 and healing_events else False
    healed=wait(self_healed,40)
    assert len(healing_events)<=5,healing_events
    event_times=[datetime.datetime.fromisoformat(stamp) for stamp in sorted(healing_events)]
    assert all((b-a).total_seconds()>=4.8 for a,b in zip(event_times,event_times[1:])),event_times
    assert not call('/management/admin/characters/'+str(healer_row['characterId']))['inventory']
    (root/'healing-observation.json').write_text(json.dumps({'events':list(healing_events.values()),'samples':healing_samples},ensure_ascii=False))
    print(f'PASS 原TCP低血道士实际自疗：25→{healed["runtime"]["hp"]}HP，74→{healed["runtime"]["mp"]}MP，{len(healing_events)}次原服确认，暂停发展仍保命，无药品。')
    healer_row=call(base+'/'+healer_id)['row']
    call(base+'/'+healer_id+'/command',{'action':'stop','version':healer_row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(healer_row['characterId']))['online'],15)
    town_observations=[]
    for denied in [True,False]:
        stop()
        command=['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-town',str(healer_row['characterId'])]
        if denied:command.append('--deny-town')
        subprocess.run(command,cwd=root,env=env,check=True)
        start();healer_row=call(base+'/'+healer_id)['row']
        call(base+'/'+healer_id+'/command',{'action':'start','version':healer_row['profile']['version']})
        call(base+'/'+healer_id+'/command',{'action':'pause','version':healer_row['profile']['version']})
        wait(lambda:call(base+'/'+healer_id)['runtime'],20)
        if denied:
            deadline=time.monotonic()+8
            while time.monotonic()<deadline:
                detail=call(base+'/'+healer_id);character=call('/management/admin/characters/'+str(healer_row['characterId']))
                assert not detail['row']['lastError'] and character['mapId']==2,(detail,character)
                assert sum(item['count'] for item in character['inventory'])==2 and not any(e['kind']=='escape' for e in detail['events']),(detail,character)
                time.sleep(.3)
        else:
            def returned():
                detail=call(base+'/'+healer_id)
                assert not detail['row']['lastError'],detail
                character=call('/management/admin/characters/'+str(healer_row['characterId']))
                return (detail,character) if character['mapId']==character['bindMapId'] and any(e['kind']=='escape' and '原服确认' in e['text'] for e in detail['events']) else False
            detail,character=wait(returned,20)
            time.sleep(3)
            detail=call(base+'/'+healer_id);character=call('/management/admin/characters/'+str(healer_row['characterId']))
            assert sum(item['count'] for item in character['inventory'])==1,character
            assert len([e for e in detail['events'] if e['kind']=='escape'])==1,detail
        town_observations.append({'denied':denied,'detail':detail,'character':character})
        healer_row=call(base+'/'+healer_id)['row']
        call(base+'/'+healer_id+'/command',{'action':'stop','version':healer_row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(healer_row['characterId']))['online'],15)
    (root/'town-observation.json').write_text(json.dumps(town_observations,ensure_ascii=False))
    print('PASS 原TCP回城保护：禁回城地图不消耗/不停止；允许回城的禁药/禁地牢逃脱地图正常回绑定地图，卷轴2→1，单次确认，无重复。')
    wait(lambda:call(base+'/overview')['online']==0 and not next(a for a in call('/management/admin/accounts') if a['id']==healer_row['accountId'])['online'],15)
    healer_row=call(base+'/'+healer_id)['row'];call(base+'/'+healer_id+'/command',{'action':'release','version':healer_row['profile']['version']})
    wizard=call(base,{'name':'TreeWizard','class':1,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,'schedule':saved['profile']['schedule']})
    wizard_id=wizard['id']
    call(base+'/'+wizard_id+'/command',{'action':'start','version':wizard['version']})
    wizard_row=wait(lambda:(value if (value:=call(base+'/'+wizard_id)['row'])['characterId']>0 else False),35)
    call(base+'/'+wizard_id+'/command',{'action':'stop','version':wizard_row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(wizard_row['characterId']))['online'],15)
    stop()
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-tree',str(wizard_row['characterId'])],cwd=root,env=env,check=True)
    start();wizard_row=call(base+'/'+wizard_id)['row']
    call(base+'/'+wizard_id+'/command',{'action':'start','version':wizard_row['profile']['version']})
    tree_samples=[]
    def chopped():
        detail=call(base+'/'+wizard_id);assert not detail['row']['lastError'],detail
        runtime=detail['runtime']
        if runtime:
            tree_samples.append(runtime)
            assert runtime['mp']==160 and runtime['gold']==0,runtime
        return detail if runtime and runtime['experience']>0 else False
    chopped_tree=wait(chopped,40)
    assert 'FireBall' in call('/management/admin/characters/'+str(wizard_row['characterId']))['spells']
    (root/'tree-combat-observation.json').write_text(json.dumps(tree_samples,ensure_ascii=False))
    print('PASS 原TCP法师改用普攻击杀动态编号原栗树：取得经验，全部采样160MP/0金币，无药品或NPC。')
    wizard_row=call(base+'/'+wizard_id)['row'];call(base+'/'+wizard_id+'/command',{'action':'stop','version':wizard_row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0 and not next(a for a in call('/management/admin/accounts') if a['id']==wizard_row['accountId'])['online'],15)
    wizard_row=call(base+'/'+wizard_id)['row'];call(base+'/'+wizard_id+'/command',{'action':'release','version':wizard_row['profile']['version']})
    stop()
    copy=root/'version-b';shutil.copytree(data,copy,symlinks=True)
    data=copy;version=hashlib.sha256(str(data).encode()).hexdigest().upper()
    env|={'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime')}
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-variant',str(saved['characterId'])],cwd=root,env=env,check=True)
    start();assert not call(base+'/overview')['settings']['enabled'];assert call(base+'/'+ident)['row']['profile']['traits']==saved['profile']['traits']
    settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'enabled':True}},'PUT')
    row=call(base+'/'+ident)['row'];edited=call(base+'/'+ident,row['profile']|{'goal':'探索优先'},'PUT')
    call(base+'/'+ident+'/command',{'action':'start','version':edited['version']})
    def crossed():
        row=call(base+'/'+ident)['row']
        if row['lastError']:raise AssertionError(row)
        return call('/management/admin/characters/'+str(saved['characterId']))['mapId']==22
    wait(crossed,55)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0,10)
    wait(lambda:not next(a for a in call('/management/admin/accounts') if a['id']==saved['accountId'])['online'],10)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'release','version':row['profile']['version']})
    assert all(x['profile']['id']!=ident for x in call(base)['items'])
    assert any(c['id']==saved['characterId'] for c in call('/management/admin/characters'))
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as database:assert database.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 陪玩：管理冲突/隔离、真实注册建角升级/交任务/补药/修理、暂停下线、重启存档、复制版本/变更地图编号/公开入口跨图、解除保留角色。')
finally:
    model_server.shutdown();model_server.server_close()
    for p,log in children:
        if p.poll() is None:p.terminate();p.wait(60)
        if not log.closed:log.close()
    if os.environ.get('MIR2_TEST_KEEP')=='1':print(root)
    else:shutil.rmtree(root)
