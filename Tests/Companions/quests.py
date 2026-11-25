"""隔离原TCP多任务排程：本图收尾后再跨图，不改正式数据。"""
import hashlib,json,os,pathlib,re,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-quests-')).resolve();data=root/'version-a'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
travel_threat=os.environ.get('MIR2_COMPANION_QUEST_TRAVEL')=='1'
full_bag=os.environ.get('MIR2_COMPANION_QUEST_FULL_BAG')=='1'
blocked_reward=os.environ.get('MIR2_COMPANION_QUEST_BLOCKED_REWARD')=='1'
storage_pages=os.environ.get('MIR2_COMPANION_QUEST_STORAGE_PAGES')=='1'
half_items=os.environ.get('MIR2_COMPANION_QUEST_HALF_ITEMS')=='1'
assert not half_items or not (travel_threat or full_bag or blocked_reward or storage_pages)
server_dll=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server_dll.parent.parent.name/'net8.0/Compatibility.dll'
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        result=predicate()
        if result:return result
        time.sleep(.2)
    raise AssertionError('多任务排程等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',str(server_dll),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'QuestPlanner','class':0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId'];aid=row['accountId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    subprocess.run(['dotnet',str(compat),'--companion-quests',str(cid)]+(['--travel-threat'] if travel_threat else [])+(['--full-bag'] if full_bag else [])+(['--blocked-reward'] if blocked_reward else []),cwd=root,env=env,check=True)
    if half_items:
        script=data/'world/Envir/Quests/NativeQuestOrder2.txt'
        original=script.read_text()
        assert original.count('NativeQuestRemainder 10')==1
        script.write_text(original.replace('NativeQuestRemainder 10','NativeQuestRemainder 5'))
    fixture=json.loads((data/'runtime/companion-quests-fixture.json').read_text())
    if storage_pages:
        setup=data/'world/Configs/Setup.ini';text=setup.read_text()
        if '[Storage]' not in text:text+='\n[Storage]\nQuest2=2\nQuest3=1\nQuest4=1\n'
        else:
            for page,quest in [(2,2),(3,1),(4,1)]:text=re.sub(r'(?m)^Quest'+str(page)+r'=.*$',f'Quest{page}={quest}',text)
        setup.write_text(text)
    start();initial=call('/management/admin/characters/'+str(cid))
    assert initial['activeQuests']==[1,2] and initial['completedQuests']==[],initial
    assert any(int(i['uniqueId'])==fixture['tokenId'] and i['count']==(10 if full_bag or blocked_reward else 9) for i in initial['inventory']),initial
    assert sum(i['grid']=='Inventory' for i in initial['inventory'])==(46 if full_bag or blocked_reward else 45) and not any(i['itemId']==fixture['reward'] for i in initial['inventory']),initial
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    if blocked_reward:
        def playing():
            detail=call(base+'/'+ident)
            assert not detail['row']['lastError'],detail
            return detail if detail['row']['state'] not in ['连接中','排队中','加载中','准备中','运行中'] and detail['runtime'] else False
        wait(playing)
        originals={(i['uniqueId'],i['count']) for i in initial['inventory']}
        for _ in range(32):
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid))
            assert not detail['row']['lastError'],detail
            assert character['completedQuests']==[] and character['activeQuests']==[1,2],character
            assert {(i['uniqueId'],i['count']) for i in character['inventory']}==originals,character
            assert not any(e['kind']=='asset' for e in detail['events']),detail
            time.sleep(.25)
        assert detail['runtime']['goal']['kind']=='wait',detail
        (root/'blocked-reward.json').write_text(json.dumps({'detail':detail,'character':character},ensure_ascii=False))
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
        subprocess.run(['dotnet',str(compat),'--companion-quests',str(cid),'--reward-room'],cwd=root,env=env,check=True)
        start();row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    if travel_threat:
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'pause','version':row['profile']['version']})
        def hurt():
            character=call('/management/admin/characters/'+str(cid));detail=call(base+'/'+ident)
            assert not detail['row']['lastError'],detail
            return character if character['online'] and detail.get('runtime') and character['hp']<detail['runtime']['maxHp'] else False
        injured=wait(hurt,20)
        assert injured['experience']==0,('暂停等待受击前已推进任务，场景无效',injured)
        (root/'travel-threat-before.json').write_text(json.dumps({'character':injured,'runtime':call(base+'/'+ident)['runtime']},ensure_ascii=False))
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'resume','version':row['profile']['version']})
    resumed=time.monotonic()
    samples=[];maps=set();local_goal=False;collected=False;assets=set()
    def completed():
        global local_goal,collected
        # 完成集合先读，后读资产快照，避免交付恰在两请求间完成时用旧金币核对新完成状态。
        character=call('/management/admin/characters/'+str(cid));detail=call(base+'/'+ident);runtime=detail['runtime'] or {}
        assert not detail['row']['lastError'],detail
        assert not any(e['kind']=='death' for e in detail['events']),detail
        assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        maps.add(character['mapId']);goal=runtime.get('goal') or {}
        if not {1,2}<=set(character['completedQuests']):
            if not full_bag:
                assert not goal.get('storageItems'),'无当前用途的材料取回抢占了可完成任务'
                assert not any(e.startswith('storage-') for e in assets),assets
            assert any(int(i['uniqueId'])==fixture['reserveId'] and (2<=i['count']<=5 if full_bag else i['count']==2) for i in character['inventory']),character
            account=call('/management/admin/accounts/'+str(aid))
            if full_bag:
                # 已实际打开仓库时允许原安全回补至5份，核对守恒而非要求仓库源堆叠不拆分。
                assert sum(i['count'] for i in character['inventory']+account['storage'] if i['itemId']==fixture['reserve'])==42
            else:assert any(int(i['uniqueId'])==fixture['storedId'] and i['count']==40 for i in account['storage']),account
        if goal.get('questId')==2 and goal.get('kind')==('turn-in' if full_bag or blocked_reward or half_items else 'hunt'):local_goal=True
        items=[i for i in character['inventory'] if i['itemId']==fixture['token']]
        if sum(i['count'] for i in items)==(9 if half_items else 10) and any(int(i['uniqueId'])==fixture['tokenId'] for i in items):collected=True
        if travel_threat:
            assert character['experience']<800,('任务途中先杀了无关攻击者',character)
            assert collected or 2 in character['completedQuests'] or time.monotonic()-resumed<12,('近期受击后仍未优先取得同图任务物',character,runtime)
        if 2 not in character['completedQuests']:
            if not full_bag:assert character['mapId']==fixture['origin'],(goal,character)
        else:
            assert runtime.get('gold',222)>=222,detail
            assert not items,character
        samples.append({'utc':time.time(),'runtime':runtime,'character':character,'events':detail['events']})
        return (character,runtime) if {1,2}<=set(character['completedQuests']) else False
    try:character,runtime=wait(completed,150)
    finally:(root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
    assert local_goal and collected and maps=={fixture['origin'],fixture['remote']},(local_goal,collected,maps)
    assert character['completedQuests']==[2,1] and runtime['gold']==333 and 'quest:成功' in assets,(character,runtime,assets)
    rewards=[i for i in character['inventory'] if i['itemId']==fixture['reward']]
    assert len(rewards)==1 and rewards[0]['count']==1,('任务行末空白导致奖励丢失',character)
    reward_id=rewards[0]['uniqueId']
    if blocked_reward:
        account=call('/management/admin/accounts/'+str(aid))
        assert any(int(i['uniqueId'])==fixture['blockedItemId'] and i['count']==1 for i in account['storage']),account
        print('PASS 满包无可减负物时不发送被拒交付、不触发未知保护；隔离停服保留原编号腾格后，原TCP恢复真实奖励交付。',flush=True)
    if full_bag:
        account=call('/management/admin/accounts/'+str(aid))
        stored_spares=[i for i in account['storage'] if int(i['uniqueId']) in fixture['spareIds']]
        carried_spares=[i for i in character['inventory'] if int(i['uniqueId']) in fixture['spareIds']]
        assert len(stored_spares)==3 and len(carried_spares)==1 and 'storage-store:成功' in assets,(stored_spares,carried_spares,assets)
        assert {int(i['uniqueId']) for i in stored_spares+carried_spares}==set(fixture['spareIds'])
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    start();stored=call('/management/admin/characters/'+str(cid))
    assert stored['completedQuests']==[2,1] and stored['activeQuests']==[] and not any(i['itemId']==fixture['token'] for i in stored['inventory']),stored
    assert any(i['uniqueId']==reward_id and i['count']==1 for i in stored['inventory'] if i['itemId']==fixture['reward']),stored
    if full_bag:
        account=call('/management/admin/accounts/'+str(aid))
        assert {int(i['uniqueId']) for i in account['storage']+stored['inventory'] if int(i['uniqueId']) in fixture['spareIds']}==set(fixture['spareIds'])
        assert sum(i['count'] for i in account['storage']+stored['inventory'] if i['itemId']==fixture['reserve'])==42
        print('PASS 满46格且任务可交时，先按服务器计划跨图存仓3件普通重复备装，再交任务取得真实奖励；任务/原物品编号守恒且重启保持。',flush=True)
    stop()
    if storage_pages:
        subprocess.run(['dotnet',str(compat),'--storage-pages-saved',str(aid)],cwd=root,env=env,check=True)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    if not full_bag:print(f'PASS 原TCP多任务：原材料编号/数量不变，无抢先存取，交付顺序2→1、真实333金币、任务物移除；仅剩1格奖励恰好1件（编号{reward_id}），完整重启同编号/数量及SQLite完整性。',flush=True)
    if half_items:print('PASS 正常重启加载收集需求10→5，原进行中的任务用已有9份材料完成，无需再击杀收集。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Quest fixture: '+str(root))
