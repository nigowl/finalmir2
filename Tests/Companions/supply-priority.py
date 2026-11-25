"""隔离原TCP：最后容量先补低于储备线的药品，而非便宜药补满。"""
import argparse,hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request,sys

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--emergency-gold',type=int,choices=(40,120))
parser.add_argument('--vendor-routes',action='store_true',help='验证列表靠前的远店不会抢占近店')
parser.add_argument('--threat-route',action='store_true',help='验证补给绕开可见不适合战斗的怪物近身格')
parser.add_argument('--crowded-route',action='store_true',help='验证补给避让不可阻挡被夹击时的应急单步撤退')
parser.add_argument('--handoff-threat',action='store_true',help='验证药品充足时的接交赶路也避开强怪')
args=parser.parse_args()
if args.handoff_threat:args.threat_route=True
if args.crowded_route:args.threat_route=True
if args.threat_route:args.emergency_gold=120
emergency=args.emergency_gold is not None
if emergency and args.vendor_routes:parser.error('两种独立场景请分开运行')

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-supply-priority-')).resolve();data=root/'version-a'
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
        result=predicate()
        if result:return result
        time.sleep(.2)
    raise AssertionError('药品储备顺序等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
try:
    compat=repo/'tests/Compatibility/bin'/pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'))).parent.parent.name/'net8.0/Compatibility.dll'
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    preset=overview['presets'][0]
    profile=call(base,{'name':'SupplyPriority','class':2,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'presetKey':preset['key'],'presetRevision':preset['revision'],'traits':preset['traits']|{'frugality':50},
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    scenario=['--emergency-gold',str(args.emergency_gold)] if emergency else []
    if args.vendor_routes:scenario.append('--vendor-routes')
    if args.threat_route:scenario.append('--threat-route')
    if args.crowded_route:scenario.append('--crowded-route')
    if args.handoff_threat:scenario.append('--handoff-threat')
    subprocess.run(['dotnet',str(compat),'--companion-supply-priority',str(cid)]+scenario,cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-supply-priority-fixture.json').read_text());start()
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    if args.handoff_threat:
        initial=call('/management/admin/characters/'+str(cid));originals={(i['uniqueId'],i['count']) for i in initial['inventory']};samples=[]
        def handed_in():
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid));runtime=detail['runtime'] or {}
            samples.append({'detail':detail,'character':character})
            assert not detail['row']['lastError'],detail
            if character['online'] and runtime:
                assert runtime['bagWeight']>runtime['maxBagWeight'],'场景必须真实超重步行'
                assert character['hp']==runtime['maxHp'],('接交路线贴近强怪承伤',character['hp'],runtime['maxHp'])
            assert {(i['uniqueId'],i['count']) for i in character['inventory']}==originals,'满量药品或旧库存被消耗/改动'
            assert character['experience']==0 and not any(e['kind']=='death' for e in detail['events']),detail
            return character if 1 in character['completedQuests'] else False
        try:character=wait(handed_in,50)
        finally:(root/'handoff-threat.json').write_text(json.dumps(samples,ensure_ascii=False))
        assert call('/management/admin/accounts/'+str(character['accountId']))['gold']==fixture['gold']+17
        assert any(((sample['detail'].get('runtime') or {}).get('goal') or {}).get('kind')=='accept' for sample in samples),'没有实际接取任务'
        assert any(((sample['detail'].get('runtime') or {}).get('goal') or {}).get('kind')=='turn-in' and sample['character']['x']<35 for sample in samples),'没有实际步行前往交付NPC'
        assert any('quest:成功'==e['text'] for sample in samples for e in sample['detail']['events'] if e['kind']=='asset'),'缺少原任务回执'
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
        stored=call('/management/admin/characters/'+str(cid));assert 1 in stored['completedQuests'] and stored['experience']==0
        assert {(i['uniqueId'],i['count']) for i in stored['inventory']}==originals
        assert call('/management/admin/accounts/'+str(stored['accountId']))['gold']==fixture['gold']+17
        stop()
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        print('PASS 原TCP药品充足接取/交付赶路：绕开强怪、生命满值、库存编号/数量不变，完成任务1并获17金币，完整重启和SQLite通过。',flush=True)
        sys.exit(0)
    if args.crowded_route:
        observations=[]
        def escaped():
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid))
            observations.append({'detail':detail,'character':character})
            assert not detail['row']['lastError'] and character['hp']>0 and character['experience']==0,(detail,character)
            return character if character['x']<=8 and any(e['state']=='撤退中' for e in detail['events']) else False
        try:character=wait(escaped,12)
        finally:(root/'crowded-route.json').write_text(json.dumps(observations,ensure_ascii=False))
        assert any(((s['detail'].get('runtime') or {}).get('goal') or {}).get('key','').startswith('supply/') for s in observations),'场景未实际进入补给计划'
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        assert set(fixture['items'])=={int(i['uniqueId']) for i in character['inventory']}
        print('PASS 原TCP补给遭三怪夹击：保留减压单步撤退、生命存活/零击杀、原库存与正常保存；未把撤离算作购药或交付。',flush=True)
        sys.exit(0)
    samples=[];expected_red=fixture['hp']+int(emergency);expected_blue=fixture['mp']+int(not emergency);seen_near_goal=False
    def bought():
        global seen_near_goal
        detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid));runtime=detail['runtime'] or {}
        assert not detail['row']['lastError'],detail
        items=character['inventory'];red=sum(i['count'] for i in items if i['itemId']==fixture['red']);blue=sum(i['count'] for i in items if i['itemId']==fixture['blue'])
        samples.append({'detail':detail,'character':character})
        if args.vendor_routes:
            goal=runtime.get('goal') or {}
            assert character['mapId'] not in fixture['farMaps'] and goal.get('mapId') not in fixture['farMaps'],('为补药绕远店',character,goal)
            if goal.get('key','').startswith('supply/') and goal.get('mapId')==fixture['nearMap']:seen_near_goal=True
        if emergency:assert red<=expected_red and blue==0,('最低保命采购超买或顺购蓝药',red,blue,runtime)
        else:assert red==fixture['hp'],('最后空格被不缺的红药抢占',red,blue,runtime)
        assert set(fixture['items'])<={int(i['uniqueId']) for i in items},items
        return (detail,character) if red==expected_red and blue==expected_blue and 'buy:成功' in [e['text'] for e in detail['events'] if e['kind']=='asset'] else False
    detail,character=wait(bought,35)
    if args.vendor_routes:assert seen_near_goal and character['mapId']==fixture['nearMap'],(seen_near_goal,character)
    assert detail['runtime']['gold']==fixture['gold']-fixture['price'] and detail['runtime']['emptySlots']==(2 if emergency else 0),detail
    if emergency and not args.threat_route:
        assert character['mapId']==2,character
        assert any(((s['detail'].get('runtime') or {}).get('goal') or {}).get('key','').startswith('supply/') for s in samples),samples
    for _ in range(10):time.sleep(.3);assert bought()[0]['runtime']['gold']==fixture['gold']-fixture['price']
    potions={i['uniqueId']:i['count'] for i in character['inventory'] if i['itemId'] in (fixture['red'],fixture['blue'])}
    (root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
    if args.threat_route:
        online=[s for s in samples if s['character']['online'] and s['detail']['runtime']]
        initial_hp=online[0]['detail']['runtime']['maxHp']
        assert min(s['character']['hp'] for s in online)>=initial_hp,('补给路线经过强怪近身格并承伤',initial_hp,min(s['character']['hp'] for s in online),root)
        assert all(s['character']['experience']==0 for s in online),'补给途中误把守怪当成练级目标'
        assert not any(e['kind']=='death' for s in samples for e in s['detail']['events']),samples[-1]
        print(f'PASS 原TCP补给避险：绕开10000HP守怪，生命始终{initial_hp}，真实买1红扣40；无击杀、无死亡。',flush=True)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
    stored=call('/management/admin/characters/'+str(cid));assert sum(i['count'] for i in stored['inventory'] if i['itemId']==fixture['blue'])==expected_blue
    assert sum(i['count'] for i in stored['inventory'] if i['itemId']==fixture['red'])==expected_red
    assert set(fixture['items'])<={int(i['uniqueId']) for i in stored['inventory']}
    assert {i['uniqueId']:i['count'] for i in stored['inventory'] if i['itemId'] in (fixture['red'],fixture['blue'])}==potions,'采购药品原编号/数量跨重启不一致'
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
    def restored():
        runtime=call(base+'/'+ident)['runtime']
        if runtime is None:return False
        assert runtime['gold']==fixture['gold']-fixture['price'],runtime
        return True
    wait(restored)
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    if args.vendor_routes:print('PASS 原TCP跨图药店：跳过列表首个两图远店，走一图近店，最后空格先补蓝药、扣30金币、原编号及重启保持。',flush=True)
    print((f'PASS 原TCP最低保命采购：{fixture["gold"]}金币/无药只买1红药，扣40后剩{fixture["gold"]-40}；蓝药不买、原编号保留、重启/SQLite完整性。' if emergency else
        'PASS 原TCP最后1空格：红药14份不补满，先按原价30金币购买短缺蓝药3→4；全部原编号保留、无重复支出、正常重启/SQLite完整性。'),flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Supply priority fixture: '+str(root))
