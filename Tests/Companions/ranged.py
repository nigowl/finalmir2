"""隔离原TCP验证长冷却、含惩罚的缺蓝普攻、受击退场及重启；不修改正式数据。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-ranged-')).resolve()
data=root/'version-a'
def port():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper()
process=None;log=None;key='';starts=0
healing_retire=os.environ.get('MIR2_COMPANION_RETIRE_HEALING')=='1'
amulets=os.environ.get('MIR2_COMPANION_AMULETS')=='1'
crowd_only=os.environ.get('MIR2_COMPANION_CROWD')=='1'
server_dll=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server_dll.parent.parent.name/'net8.0/Compatibility.dll'
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('远程战斗等待超时')
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
    start();base='/management/companions'
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'RangedWizard','class':2 if healing_retire or amulets else 1,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id']
    call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row']
        if row['lastError']:raise AssertionError(row)
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered,40);character_id=row['characterId']
    call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0 and not call('/management/admin/characters/'+str(character_id))['online'],15)
    observations=[]
    modes=["crowd"] if crowd_only else ["missing","bag","equipped","last"] if amulets else ["healing"] if healing_retire else ["penalty"] if os.environ.get("MIR2_COMPANION_MANA_PENALTY")=="1" else ["persistent"] if os.environ.get("MIR2_COMPANION_RETIRE_PERSISTENT")=="1" else ["retire"] if os.environ.get("MIR2_COMPANION_RETIRE_ONLY")=="1" else ["ranged","empty","penalty","retire","persistent"]
    for mode in modes:
        empty=mode=="empty";penalty=mode=="penalty";physical=empty or penalty or amulets and mode in ("missing","last");threat=mode in ("retire","persistent","crowd");persistent=mode in ("persistent","crowd")
        stop()
        command=['dotnet',str(compat),'--companion-ranged',str(character_id)]
        if healing_retire:command=['dotnet',str(compat),'--companion-healing',str(character_id),'--retirement-healing']
        if empty:command.append('--empty-mana')
        if penalty:command.append('--penalty-mana')
        if amulets:command.extend(['--amulet-case',mode])
        if threat:command.append('--retire-threat')
        if persistent:command.append('--persistent-threat')
        subprocess.run(command,cwd=root,env=env,check=True)
        start();initial=call('/management/admin/characters/'+str(character_id));origin=(initial['x'],initial['y'])
        row=call(base+'/'+ident)['row']
        if persistent:
            call(base+'/'+ident,row['profile']|{'traits':row['profile']['traits']|{'risk':100}},'PUT')
            row=call(base+'/'+ident)['row']
        call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
        if crowd_only:
            row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'pause','version':row['profile']['version']})
            wait(lambda:call('/management/admin/characters/'+str(character_id))['hp']<52,20)
            row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'resume','version':row['profile']['version']})
            samples=[];began=time.monotonic()
            def escaped():
                c=call('/management/admin/characters/'+str(character_id));d=call(base+'/'+ident)
                samples.append({'character':c,'events':d['events']});(root/'crowd.json').write_text(json.dumps(samples,ensure_ascii=False))
                assert not d['row']['lastError'] and c['experience']==0 and c['hp']>0,(c,d)
                retreated=any(e['state']=='撤退中' for e in d['events'])
                return c if retreated and max(abs(c['x']-origin[0]),abs(c['y']-origin[1]))>=3 else False
            result=wait(escaped,12)
            row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
            wait(lambda:not call('/management/admin/characters/'+str(character_id))['online'],35)
            assert not call(base+'/'+ident)['row']['lastError']
            print(f'PASS 原TCP普通战斗遭三怪真实围攻后{time.monotonic()-began:.2f}秒内主动撤离并正常退出，未杀怪、未死亡、未解除预算。',flush=True)
            continue
        if healing_retire:
            row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'pause','version':row['profile']['version']})
            def healed():
                detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id))
                assert not detail['row']['lastError'],detail
                return detail if character['online'] and character['mp']<initial['mp'] and any(e['kind']=='healing' for e in detail['events']) else False
            healing_before=wait(healed,20);stopping=time.monotonic();row=healing_before['row']
            call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']});samples=[]
            def healing_stopped():
                detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id))
                samples.append({'time':time.monotonic()-stopping,'character':character,'row':detail['row'],'events':detail['events']})
                assert detail['row']['profile']['enabled'] and not detail['row']['lastError'],detail
                return character if call(base+'/overview')['online']==0 and not character['online'] else False
            try:result=wait(healing_stopped,35)
            finally:(root/'healing-retirement-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
            assert time.monotonic()-stopping<30 and result['hp']>0 and result['experience']==0,result
            healing_events={e['time'] for sample in samples for e in sample['events'] if e['kind']=='healing'}
            assert healing_events<={e['time'] for e in healing_before['events'] if e['kind']=='healing'},'安静退场仍重复施法刷新原退出保护'
            assert any('原服确认退出角色' in e['reason'] for sample in samples for e in sample['events']),samples
            observations.append({'healingRetirement':True,'seconds':time.monotonic()-stopping,'samples':samples})
            print(f'PASS 原TCP安静低血道士：暂停时真实自疗，退场停止主动施法，{time.monotonic()-stopping:.2f}秒原服确认；生命{result["hp"]}、零经验，无误停用。',flush=True)
            continue
        if threat:
            row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'pause','version':row['profile']['version']})
            threat_samples=[]
            def struck():
                character=call('/management/admin/characters/'+str(character_id));detail=call(base+'/'+ident)
                threat_samples.append({'character':character,'row':detail['row'],'runtime':detail['runtime'],'events':detail['events'][-5:]})
                assert not detail['row']['lastError'],detail
                assert character['experience']==0,('暂停前已误杀退场目标',character)
                goal=(detail['runtime'] or {}).get('goal')
                if persistent and character['online'] and goal:assert goal['kind']=='hunt',('三怪场景须经过原可猎杀规划',goal)
                return character if character['online'] and character['hp']<initial['hp'] and (not persistent or goal is not None) else False
            try:wait(struck,35)
            finally:(root/'retirement-threat.json').write_text(json.dumps(threat_samples,ensure_ascii=False))
            row=call(base+'/'+ident)['row'];stopping=time.monotonic()
            call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']});samples=[]
            def retired():
                detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id))
                samples.append({'time':time.monotonic()-stopping,'row':detail['row'],'character':character,'events':detail['events']})
                assert detail['row']['profile']['enabled'] and not detail['row']['lastError'],detail
                return character if call(base+'/overview')['online']==0 and not character['online'] else False
            try:result=wait(retired,35)
            finally:(root/'retirement-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
            observations.append({'retireThreat':True,'samples':samples})
            (root/'retirement-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
            assert result['hp']>0 and time.monotonic()-stopping<30,result
            # 退出目标是安全脱战；成功撤离不要求额外击杀或无谓耗药。
            assert result['experience']>0 or (result['x'],result['y'])!=origin,result
            if persistent:
                assert result['experience']==0 and (result['x'],result['y'])!=origin,('持续围攻退场不应先打死目标',result)
                assert any((sample['character']['x'],sample['character']['y'])!=origin for sample in samples if sample['character']['online']),samples
            medicine=[i for i in initial['inventory'] if i['name']=='NativeRetirementMedicine'];assert len(medicine)==1 and medicine[0]['count']==10
            remaining=[i for i in result['inventory'] if i['uniqueId']==medicine[0]['uniqueId']];count=sum(i['count'] for i in remaining);assert 0<=count<=10,result
            assert not any(e['kind']=='death' for sample in samples for e in sample['events'])
            assert any('原服确认退出角色' in e['reason'] for sample in samples for e in sample['events'])
            assert any(e['state'] in ['撤退中','自卫中','自疗中'] for sample in samples for e in sample['events'])
            messages=[json.loads((repo/f'src/Game/Localization/{language}.json').read_text(encoding='utf-8-sig'))['Text']['UserLoggedOutSelectCharReasonMessage'].format('RangedWizard') for language in ['English','Chinese']]
            assert any(message in pathlib.Path(log.name).read_text() for message in messages),pathlib.Path(log.name).read_text()
            print(f'PASS 原TCP退场{"三怪持续追击" if persistent else "受击"}：实际扣血后保命/撤退，生命{result["hp"]}、药品10→{count}，30秒内原服确认退出，无死亡或误停用。',flush=True)
            continue
        samples=[];previous_mp=initial['mp'];casts=0;combat_started=time.monotonic()
        def killed():
            global previous_mp,casts
            detail=call(base+'/'+ident);character=call('/management/admin/characters/'+str(character_id))
            assert not detail['row']['lastError'],detail
            if not character['online']:return False
            point=(character['x'],character['y'])
            samples.append({'x':point[0],'y':point[1],'mp':character['mp'],'hp':character['hp'],'experience':character['experience']})
            if character['mp']<=previous_mp-5:casts+=1
            previous_mp=character['mp']
            assert not any(e['kind']=='death' for e in detail['events']),detail
            if not physical and not character['experience']:assert point==origin,('冷却时主动走近目标',origin,samples)
            if penalty:
                assert casts==0,('实际耗蓝不足时不应强行施法',samples)
                assert time.monotonic()-combat_started<8 or any((s['x'],s['y'])!=origin for s in samples),('只够基础耗蓝时仍原地空放，没有回退普攻',samples)
            if amulets and mode=='missing':
                assert casts==0,('没有已装备护身符仍空放耗蓝',mode,samples)
                assert time.monotonic()-combat_started<8 or point!=origin,('缺护身符未回退普攻',mode,samples)
            return character if character['experience']>0 else False
        try:result=wait(killed,90)
        finally:(root/f'combat-{mode}.json').write_text(json.dumps(samples,ensure_ascii=False))
        assert (any((s['x'],s['y'])!=origin for s in samples) if physical else casts>=2),(mode,casts,samples)
        if amulets:
            before=[i for i in initial['inventory'] if i['name']=='NativeCastingAmulet'];after=[i for i in result['inventory'] if i['name']=='NativeCastingAmulet']
            if mode=='bag':assert len(after)==1 and after[0]['uniqueId']==before[0]['uniqueId'] and after[0]['grid']=='Equipment' and after[0]['count']==20-casts and casts>=2,(before,after)
            if mode=='missing':assert not before and not after
            if mode=='equipped':assert len(after)==1 and after[0]['uniqueId']==before[0]['uniqueId'] and after[0]['count']==20-casts and casts>=2,after
            if mode=='last':assert len(before)==1 and before[0]['count']==1 and not after and casts==1,('最后1符用尽没有回退普攻',casts,result)
            if mode=='bag':assert [(i['uniqueId'],i['count']) for i in initial['inventory'] if i['name']=='NativeWornPoison']==[(i['uniqueId'],i['count']) for i in result['inventory'] if i['name']=='NativeWornPoison']
        observations.append({'mode':mode,'emptyMana':empty,'casts':casts,'samples':samples,'materials':{'before':before,'after':after} if amulets else None})
        stopping=time.monotonic();row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:call(base+'/overview')['online']==0 and not call('/management/admin/characters/'+str(character_id))['online'],20)
        row=call(base+'/'+ident)['row']
        assert time.monotonic()-stopping>=5 and not row['lastError'] and row['profile']['enabled'],'战斗后退出未等待原服战斗保护，或误停用了档案'
        print(f'PASS 原TCP{"护身符"+mode+"真实消耗/缺失回退" if amulets else "含惩罚后缺蓝仍走近普攻击杀" if penalty else "缺蓝仍走近普攻击杀" if empty else "5秒冷却保持原地射程，至少两次实际扣蓝后击杀"}：经验{result["experience"]}，施法扣蓝{casts}次，等待原战斗保护后确认退出。',flush=True)
    (root/'ranged-observation.json').write_text(json.dumps(observations,ensure_ascii=False))
    saved=call('/management/admin/characters/'+str(character_id));stop();start()
    restored=call('/management/admin/characters/'+str(character_id))
    for field in ('id','name','accountId','level','experience','hp','mp','mapId','x','y','inventory','spells','activeQuests','completedQuests'):
        assert saved[field]==restored[field],('退场正常重启后状态变化',field,saved[field],restored[field])
    assert not restored['online'] and not call(base+'/'+ident)['row']['lastError']
    stop();print('PASS 退场后完整重启：身份、位置、生命/魔力、经验及全部物品编号/数量/耐久保持。',flush=True)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Ranged fixture: '+str(root),flush=True)
