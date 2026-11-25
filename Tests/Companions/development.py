"""隔离原TCP：满药仍主动跨图购书、学习、补符、换右戒指并真实施法。"""
import argparse,hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.error,urllib.request

parser=argparse.ArgumentParser(description=__doc__);modes=parser.add_mutually_exclusive_group();modes.add_argument('--blocked-supply',action='store_true');modes.add_argument('--switching-shops',action='store_true');modes.add_argument('--public-services',action='store_true');args=parser.parse_args()

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-development-')).resolve();data=root/'version-a'
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();process=None;log=None;key='';starts=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    try:
        with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
    except urllib.error.HTTPError as error:raise AssertionError(f'{path}: {error.code}: '+error.read().decode()) from error
def wait(predicate,seconds=90):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        assert process.poll() is None,pathlib.Path(log.name).read_text()
        result=predicate()
        if result:return result
        time.sleep(.25)
    raise AssertionError('等待原TCP发展结果超时；'+str(root))
def start():
    global process,log,key,starts
    starts+=1;log=(root/f'server-{starts}.log').open('w')
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()
    process=None;log.close();log=None
try:
    reference=os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':reference},check=True)
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'DevTaoist','class':2,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id']
    def command(action):
        row=call(base+'/'+ident)['row'];return call(base+'/'+ident+'/command',{'action':action,'version':row['profile']['version']})
    command('start')
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId'];aid=row['accountId'];command('stop')
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    subprocess.run(['dotnet',str(compat),'--companion-development',str(cid)]+(['--public-companions'] if args.public_services else ['--blocked-supply'] if args.blocked_supply else ['--switching-shops'] if args.switching_shops else []),cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-development-fixture.json').read_text());start()
    assert not call('/management/settings')['values']['autoLearnSkills']
    initial=call('/management/admin/characters/'+str(cid));assert initial['spells']==[] and initial['experience']==0
    if args.public_services:
        assert sum(i['count'] for i in initial['inventory'] if i['grid']=='Inventory' and i['name']=='隔离任务材料')==7
        protected=[(i['uniqueId'],i['count']) for i in initial['inventory'] if i['name']=='隔离任务材料']
        initial_items={i['uniqueId']:i for i in initial['inventory']};command('start');samples=[]
        def supplied():
            c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident)
            samples.append({'character':c,'detail':d});(root/'public-services.json').write_text(json.dumps(samples,ensure_ascii=False))
            assert not d['row']['lastError'],d
            assert not any(i['name']=='隔离未达技能等级书' for i in c['inventory']),'购买了实际技能等级不足的便宜书'
            assert c['mapId']==initial['mapId'] and c['x']==initial['x'] and c['y']==initial['y'],'商城/回收仍导致角色走路'
            assert [(i['uniqueId'],i['count']) for i in c['inventory'] if i['name']=='隔离任务材料']==protected,'回收了未来任务材料'
            return c if 'SoulFireBall' in c['spells'] and any(i['itemId']==fixture['amulet'] and i['count']==500 for i in c['inventory']) and any(i['itemId']==fixture['red'] and i['count']>=12 for i in c['inventory']) else False
        c=wait(supplied,60)
        command('pause');wait(lambda:call(base+'/'+ident)['row']['state']=='已暂停')
        c=call('/management/admin/characters/'+str(cid));events=call(base+'/'+ident)['events']
        assert any(e['kind']=='asset' and e['text']=='recycle:成功' for e in events)
        assert any(e['kind']=='asset' and e['text']=='public-buy:成功' for e in events)
        assert not any(e['kind']=='asset' and e['text']=='buy:成功' for e in events)
        assert not any(e['kind']=='status' and e['text']=='跨图中' for e in events)
        removed=[i for uid,i in initial_items.items() if i['name']=='隔离普通余肉' and not any(j['uniqueId']==uid for j in c['inventory'])]
        red=sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['red'])
        town=sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['town'])
        expected=10000+len(removed)*50-red*60-town*150-150-750
        gold=call('/management/admin/accounts/'+str(aid))['gold'];assert gold==expected,(gold,expected,removed)
        command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
        restored=call('/management/admin/characters/'+str(cid))
        assert restored['spells']==c['spells'] and {(i['uniqueId'],i['count']) for i in restored['inventory']}=={(i['uniqueId'],i['count']) for i in c['inventory']}
        assert call('/management/admin/accounts/'+str(aid))['gold']==gold
        stop()
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        print('PASS 原TCP原地回收/补红/卷轴/职业书籍学习/整包500次护符，未来任务材料保留，金币精确结算及完整重启；'+str(root),flush=True)
        raise SystemExit(0)
    if args.switching_shops:
        command('start');samples=[];fallback=None;started=None
        def purchased():
            global fallback,started
            c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident);goal=(d.get('runtime') or {}).get('goal') or {}
            samples.append({'time':time.monotonic(),'character':c,'detail':d});(root/'switching-shops.json').write_text(json.dumps(samples,ensure_ascii=False))
            assert not d['row']['lastError'] and c['hp']>0,d
            if goal.get('key','').startswith('develop/') and '发展书店' in goal.get('reason',''):
                if fallback is None:fallback=goal['key'];started=time.monotonic()
            if fallback is not None and 'SoulFireBall' not in c['spells']:
                assert goal.get('key')==fallback,'替代商店赶路未完成，旧店排除到期后再次折返'
            upgraded=any(i['itemId']==fixture['ring'] and i['grid']=='Equipment' and i['slot']==8 for i in c['inventory'])
            return c if 'SoulFireBall' in c['spells'] and upgraded and any(i['itemId']==fixture['amulet'] and i['grid']=='Equipment' for i in c['inventory']) else False
        c=wait(purchased,210);assert fallback is not None and time.monotonic()-started>90,'替代商店路线没有跨过原90秒排除期'
        assert any(e['kind']=='goal-blocked' and e['text']!=fallback for s in samples for e in s['detail']['events'])
        assert call('/management/admin/accounts/'+str(aid))['gold']==9750
        assert c['experience']==0 and c['completedQuests']==initial['completedQuests']
        assert any(int(i['uniqueId'])==fixture['right'] and i['grid']=='Inventory' for i in c['inventory'])
        command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
        restored=call('/management/admin/characters/'+str(cid));assert restored['spells']==c['spells'] and {(i['uniqueId'],i['count']) for i in restored['inventory']}=={(i['uniqueId'],i['count']) for i in c['inventory']}
        assert call('/management/admin/accounts/'+str(aid))['gold']==9750;stop()
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        print('PASS 原TCP隔墙近店失败后，替代商店赶路超过90秒不折返，真实购书学习/换装/补符、扣250金币及原编号完整重启保持。',flush=True)
        raise SystemExit(0)
    if args.blocked_supply:
        originals={(i['uniqueId'],i['count']) for i in initial['inventory']};command('start');samples=[]
        def waiting():
            c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident);r=d.get('runtime') or {};goal=r.get('goal') or {}
            samples.append({'character':c,'detail':d});(root/'blocked-supply.json').write_text(json.dumps(samples,ensure_ascii=False))
            assert not d['row']['lastError'] and c['hp']>0,d
            assert c['spells']==[] and {(i['uniqueId'],i['count']) for i in c['inventory']}==originals,'缺红药期间仍采购/学习装备书籍'
            assert call('/management/admin/accounts/'+str(aid))['gold']==10000,'药店不可达期间发生支出'
            assert not goal.get('key','').startswith('develop/'),'药店暂不可达后缺红药仍安排发展'
            assert not any(e['kind']=='status' and e['reason'].startswith('按补给需要查找') for e in d['events']),'保命等待仍随机访问书店'
            return d if goal.get('kind')=='wait' and any(e['kind']=='goal-blocked' and e['text'].startswith('supply/') for e in d['events']) else False
        wait(waiting,35)
        for _ in range(24):time.sleep(.25);assert waiting()
        command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
        subprocess.run(['dotnet',str(compat),'--open-companion-supply'],cwd=root,env=env,check=True)
        start();command('start');purchases=[]
        def recovered():
            c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident);r=d.get('runtime') or {};purchases.append({'character':c,'detail':d})
            (root/'supply-recovered.json').write_text(json.dumps(purchases,ensure_ascii=False));assert not d['row']['lastError'],d
            red=sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['red'])
            if 'SoulFireBall' in c['spells']:assert red>=6,'红药储备未恢复就先学书'
            upgraded=any(i['itemId']==fixture['ring'] and i['grid']=='Equipment' and i['slot']==8 for i in c['inventory'])
            return c if 'SoulFireBall' in c['spells'] and upgraded and any(i['itemId']==fixture['amulet'] and i['grid']=='Equipment' for i in c['inventory']) else False
        c=wait(recovered,60);red=sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['red']);assert red>=12
        gold=call('/management/admin/accounts/'+str(aid))['gold'];assert gold==10000-red*40-250,(gold,red)
        first=next(s['character'] for s in purchases if sum(i['count'] for i in s['character']['inventory'] if i['itemId']==fixture['red'])>0)
        assert first['spells']==[] and not any(i['itemId']==fixture['ring'] for i in first['inventory']),'没有先原服购药再发展'
        assert c['experience']==0
        command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
        restored=call('/management/admin/characters/'+str(cid));assert restored['spells']==c['spells'] and {(i['uniqueId'],i['count']) for i in restored['inventory']}=={(i['uniqueId'],i['count']) for i in c['inventory']}
        assert call('/management/admin/accounts/'+str(aid))['gold']==gold;stop()
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
        print(f'PASS 原TCP药店不可达时缺红药保命等待、零支出/学习；药店恢复后先买{red}红，再购书学习/换装/补符，余额{gold}、原编号及完整重启保持。',flush=True)
        raise SystemExit(0)
    assert sum(i['count'] for i in initial['inventory'] if i['name']=='NativeDevelopmentRed')==20
    command('start');samples=[];maps=set();assets=set()
    def developed():
        c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident);rt=d.get('runtime') or {}
        samples.append({'character':c,'runtime':rt,'events':d['events']});(root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
        assert not d['row']['lastError'],d
        assert not any(e['kind']=='death' for e in d['events']),d
        maps.add(c['mapId']);assets.update(e['text'] for e in d['events'] if e['kind']=='asset')
        upgraded=any(i['itemId']==fixture['ring'] and i['grid']=='Equipment' and i['slot']==8 for i in c['inventory'])
        return (c,rt) if 'SoulFireBall' in c['spells'] and upgraded and c['experience']>0 else False
    character,runtime=wait(developed,150)
    assert maps=={fixture['origin'],fixture['remote']},maps
    assert all(x in assets for x in ['buy:成功','use:成功','equip:成功']),assets
    assert not any(i['itemId']==fixture['book'] for i in character['inventory'])
    assert any(int(i['uniqueId'])==fixture['left'] and i['slot']==7 and i['grid']=='Equipment' for i in character['inventory'])
    assert any(int(i['uniqueId'])==fixture['right'] and i['grid']=='Inventory' for i in character['inventory'])
    charms=[i for i in character['inventory'] if i['itemId']==fixture['amulet']]
    assert len(charms)==1 and charms[0]['grid']=='Equipment' and 0<charms[0]['count']<50,charms
    assert character['mp']<initial['mp'],('没有真实扣蓝',character)
    assert runtime['gold']==9750,('药品充足的购书100+戒指100+50符，没有正确扣款',runtime)
    command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();start()
    restored=call('/management/admin/characters/'+str(cid));account=call('/management/admin/accounts/'+str(aid))
    assert restored['spells']==character['spells'] and restored['experience']==character['experience']
    assert sorted((i['uniqueId'],i['itemId'],i['count']) for i in restored['inventory'])==sorted((i['uniqueId'],i['itemId'],i['count']) for i in character['inventory'])
    assert account['gold']==9750
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP满药主动跨图购书学习、50符实际装备消耗/扣蓝击杀、替换右戒指保留左戒指及旧编号、扣250金币、完整重启和SQLite完整性。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Development fixture: '+str(root),flush=True)
