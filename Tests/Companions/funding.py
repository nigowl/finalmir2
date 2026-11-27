"""隔离原TCP：38金币无红药，真实筹资/含税补药/跨图任务交付/重启。"""
import argparse,hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

parser=argparse.ArgumentParser(description=__doc__)
mode=parser.add_mutually_exclusive_group();mode.add_argument('--no-source',action='store_true');mode.add_argument('--harvest-source',action='store_true');mode.add_argument('--evasive-source',action='store_true');mode.add_argument('--harvest-ground',action='store_true')
mode.add_argument('--crowded-source',action='store_true');mode.add_argument('--blocked-exit',action='store_true');mode.add_argument('--blocked-route',action='store_true')
mode.add_argument('--friendly-guard',action='store_true')
mode.add_argument('--passive-neighbours',action='store_true')
parser.add_argument('--remote-delivery',action='store_true');parser.add_argument('--unarmed',action='store_true')
parser.add_argument('--caster',action='store_true')
args=parser.parse_args();waiting=args.no_source or args.harvest_source or args.blocked_exit or args.blocked_route
repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-funding-')).resolve();data=root/'version-a'
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
        result=predicate()
        if result:return result
        time.sleep(.2)
    raise AssertionError('筹资闭环等待超时；现场 '+str(root))
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
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
def command(action):
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':action,'version':row['profile']['version']})
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)]+(['--skill-fixture'] if args.caster else []),cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    preset=overview['presets'][0]
    profile=call(base,{'name':'FundingPlanner','class':1 if args.caster else 0,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,'goal':'探索优先' if waiting else '任务优先',
        'presetKey':preset['key'],'presetRevision':preset['revision'],'traits':preset['traits']|{'frugality':50},
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];command('start')
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId'];aid=row['accountId'];command('stop')
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    subprocess.run(['dotnet',str(compat),'--companion-funding',str(cid)]+(['--blocked-route'] if args.blocked_route else ['--blocked-exit'] if args.blocked_exit else ['--no-source'] if args.no_source else ['--harvest-source'] if args.harvest_source else ['--evasive-source'] if args.evasive_source else ['--harvest-ground'] if args.harvest_ground else ['--crowded-source'] if args.crowded_source else ['--friendly-guard'] if args.friendly_guard else ['--passive-neighbours'] if args.passive_neighbours else [])+(['--remote-delivery'] if args.remote_delivery else [])+(['--unarmed'] if args.unarmed else [])+(['--caster'] if args.caster else []),cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-funding-fixture.json').read_text());start()
    if args.friendly_guard:
        guard=wait(lambda:next((spawn for spawn in call('/management/admin/spawns?mapId='+str(fixture['origin'])) if spawn['monsterName']=='隔离友好城镇守卫' and spawn['alive']>0),None))
        assert max(abs(guard['x']-fixture['startX']),abs(guard['y']-fixture['startY']))<=3,guard
    command('start')
    samples=[];funded=False;prebuy=None;purchased=None;maps=set();assets=set();wait_since=None
    def completed():
        global funded,prebuy,purchased,wait_since
        detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(cid));runtime=detail['runtime']
        samples.append({'utc':time.time(),'detail':detail,'character':character})
        (root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
        assert not detail['row']['lastError'] and not any(e['kind']=='death' for e in detail['events']),detail
        if runtime is None:return False
        goal=runtime.get('goal') or {};red=sum(i['count'] for i in character['inventory'] if i['itemId']==fixture['red']);maps.add(character['mapId'])
        assert any(int(i['uniqueId'])==fixture['tokenId'] for i in character['inventory']),character
        assert not fixture['weaponId'] or any(int(i['uniqueId'])==fixture['weaponId'] and i['grid']=='Equipment' for i in character['inventory']),character
        assets.update(e['text'] for e in detail['events'] if e['kind']=='asset')
        if waiting:
            if args.blocked_route:assert character['x']<fixture['startX']+4,'步行进入了可见强怪的筹资避让范围'
            if args.blocked_exit:assert (character['x'],character['y'])==(fixture['startX'],fixture['startY']),'缺药缺钱仍走向强怪围堵的出口'
            assert character['mapId']==fixture['origin'] and character['activeQuests']==[1] and not character['completedQuests'],character
            assert runtime['gold']==fixture['gold'] and runtime['experience']==0 and red==0,runtime
            if detail['row']['state']=='待规划' and (goal.get('kind')=='wait' or args.blocked_route and not goal):
                wait_since=wait_since or time.monotonic()
                return (character,runtime) if time.monotonic()-wait_since>=8 else False
            return False
        if goal.get('key','').startswith('fund/'):
            funded=True
            if args.evasive_source:assert goal.get('monsterIds')==[3],('把高闪避目标当成低风险金币来源',goal)
            if args.crowded_source:assert goal.get('monsterIds')==[4],('选择了被强怪包围的近处金币源',goal)
        if not red:
            assert character['mapId']==fixture['origin'] and not character['completedQuests'],character
            if args.caster:
                fire=next(s for s in runtime['combat']['skills'] if s['spell']==31)
                assert fire['experience']==0 and runtime['mp']==runtime['maxMp'],('低风险筹资仍消耗法术/蓝药成本',runtime)
            if runtime['gold']>38:prebuy=runtime['gold']
        elif not character['completedQuests']:
            assert prebuy is not None and 8<=red<=15,(prebuy,red,runtime)
            assert runtime['gold']==prebuy-red*fixture['price'],(prebuy,red,runtime)
            purchased={'red':red,'gold':runtime['gold'],'received':prebuy-38}
        return (character,runtime) if character['completedQuests']==[1] and 'quest:成功' in assets else False
    character,runtime=wait(completed,150)
    if not waiting:
        assert funded and purchased and maps=={fixture['origin'],fixture['remote']},(funded,purchased,maps)
        assert 'buy:成功' in assets and 'quest:成功' in assets,assets
        assert runtime['gold']==purchased['gold']+fixture['reward'],(runtime,purchased)
        if args.caster and not args.remote_delivery:assert any(s['spell']==31 and s['experience']>0 for s in runtime['combat']['skills']),'正常任务战斗未恢复职业技能'
    command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online'])
    settled=call('/management/admin/characters/'+str(cid));settled_gold=call('/management/admin/accounts/'+str(aid))['gold']
    # 交付回执与下线控制之间可能合法补满刚消耗的红药，须按实际数量核对该笔支出。
    extra_red=sum(i['count'] for i in settled['inventory'] if i['itemId']==fixture['red'])-sum(i['count'] for i in character['inventory'] if i['itemId']==fixture['red'])
    assert extra_red>=0 and settled_gold==runtime['gold']-extra_red*fixture['price'],(runtime['gold'],settled_gold,extra_red)
    assert settled['completedQuests']==character['completedQuests'] and settled['activeQuests']==character['activeQuests'],settled
    assert sorted((i['uniqueId'],i['itemId'],i['count']) for i in settled['inventory'] if i['itemId']!=fixture['red'])==sorted((i['uniqueId'],i['itemId'],i['count']) for i in character['inventory'] if i['itemId']!=fixture['red']),settled
    character=settled;runtime=runtime|{'gold':settled_gold}
    stop();start()
    stored=call('/management/admin/characters/'+str(cid));account=call('/management/admin/accounts/'+str(aid))
    assert stored['completedQuests']==character['completedQuests'] and stored['activeQuests']==character['activeQuests'],stored
    assert account['gold']==runtime['gold'],account
    assert sorted((i['uniqueId'],i['itemId'],i['count']) for i in stored['inventory'])==sorted((i['uniqueId'],i['itemId'],i['count']) for i in character['inventory']),stored
    assert not fixture['weaponId'] or any(int(i['uniqueId'])==fixture['weaponId'] and i['grid']=='Equipment' for i in stored['inventory']),stored
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    if args.blocked_route:
        print('PASS 原TCP筹资沿途避险，资产、任务和金币跨重启保持。',flush=True)
        raise SystemExit(0)
    if args.blocked_exit:
        print('PASS 原TCP受堵出口：零金币/无红药，远处存在合法金币源仍原地保命等待，任务/原物品编号和金币完整重启保持。',flush=True)
        raise SystemExit(0)
    print('PASS 原TCP'+('采集怪分组金币不出货' if args.harvest_source else '无筹资来源')+'：探索优先仍原地等待/保命、不硬冲任务，38金币/无药/任务/编号正常重启保持。' if waiting else
        'PASS 原TCP筹资：'+json.dumps(purchased,ensure_ascii=False)+('；原3格内活守卫不阻挡白名筹资' if args.friendly_guard else '')+('；跳过低血但高闪避的近处金币源，实际猎杀更远可靠来源' if args.evasive_source else '')+'；原弱怪真实拾金→150%含税补药→'+('已齐任务跨图交付' if args.remote_delivery else '原跨图任务击杀及交付')+'333金币→正常重启/资产编号/SQLite完整性。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Funding fixture: '+str(root),flush=True)
