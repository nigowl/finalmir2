"""Real administration API + original TCP sessions; disposable world only."""
import base64, datetime, hashlib, json, os, pathlib, sqlite3, struct, subprocess, sys, time, urllib.error, urllib.parse, urllib.request

root = pathlib.Path(sys.argv[1]).resolve()
repo = pathlib.Path(__file__).resolve().parents[2]
runtime = pathlib.Path(os.environ.get('MIR2_RUNTIME_ROOT', str(root/'runtime')))
base = os.environ['MIR2_SERVER'].rstrip('/')
key = (runtime/'admin.key').read_text().strip()
checks = 0
children = []
last_connection = 0

def call(path, method='GET', value=None, expected=200, auth=True, raw=False):
    global checks
    headers = {'Authorization': 'Bearer '+key, 'X-Mir2-Version':hashlib.sha256(str(root).encode()).hexdigest().upper()} if auth else {}
    data = None if value is None else json.dumps(value).encode()
    if data is not None: headers['Content-Type'] = 'application/json'
    req = urllib.request.Request(base+path, data=data, headers=headers, method=method)
    try: response = urllib.request.urlopen(req, timeout=180)
    except urllib.error.HTTPError as error: response = error
    body = response.read()
    assert response.code == expected, (path, response.code, body[:1500])
    checks += 1
    return (body if raw else json.loads(body)) if response.code == 200 else None

def check(result, message):
    global checks
    assert result, message
    checks += 1

def until(condition):
    end = time.monotonic()+30
    while time.monotonic() < end:
        result = condition()
        if result: return result
        time.sleep(.1)
    raise AssertionError('Timed out waiting for a native session')

def session(account, password, character=0, *extra):
    global last_connection
    # The original engine blocks reconnects from the same IP for five seconds.
    time.sleep(max(0, 5.2-(time.monotonic()-last_connection)))
    last_connection = time.monotonic()
    output = runtime/f'admin-session-{time.time_ns()}-{len(children)}.json'
    log = open(output.with_suffix('.log'), 'w')
    command = ['dotnet', str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'), '--admin-session', os.environ['MIR2_GAME_PORT'], account, password, str(character), str(output), *extra]
    child = subprocess.Popen(command, cwd=repo, env=os.environ, stdout=log, stderr=subprocess.STDOUT)
    children.append((child, log))
    def state():
        if child.poll() not in (None, 0): raise AssertionError(output.with_suffix('.log').read_text())
        return json.loads(output.read_text()) if output.exists() else None
    result = until(state)
    if extra and any(a.startswith('--expect-') for a in extra):
        child.wait(timeout=10); check(child.returncode == 0, 'Native login rejection check failed')
    else: check(result.get('ready') and (character == 0 or result.get('entered')), 'Native session did not enter the expected scene')
    return child, state

def account_edit(a, **changes):
    return {k: a[k] for k in ('id','gold','credit','isGM','banned','banReason','banUntil','requirePasswordChange','version')} | {'confirmation':a['accountID']} | changes

def character_edit(c, **changes):
    return {k:c[k] for k in ('id','level','experience','pkPoints','banned','banReason','banUntil','muted','muteUntil','version')} | {'confirmation':c['name']} | changes

def guild_edit(g, **changes):
    return {k:g[k] for k in ('id','name','level','experience','gold','sparePoints','notice','version')} | {'confirmation':g['name']} | changes

def command(name, version=''): return {'confirmation':name,'version':version}
prefix = '/management/admin'
def account(id): return call(f'{prefix}/accounts/{id}')
def character(id): return call(f'{prefix}/characters/{id}')
def guild(id): return call(f'{prefix}/guilds/{id}')
def kick(a): call(f"{prefix}/accounts/{a['id']}/kick",'POST',command(a['accountID']))

try:
    if '--verify' not in sys.argv:
        system=call('/management/settings')
        call('/management/settings',auth=False,expected=401)
        status_before=call('/management/status')
        edited=system['values']|{'maxUser':system['values']['maxUser']+1,'maxIP':system['values']['maxIP']+2,'expRate':1.5,'dropRate':1.75,'monsterDamageRate':1.4,'playerPhysicalDamageRate':1.2,'playerMagicDamageRate':1.3,'playerPvpDamageRate':.8,'petDamageRate':1.6,'poisonDamageRate':1.7,'monsterHpRate':1.8,'monsterDefenceRate':1.9,'monsterAttackSpeedRate':1.1,'monsterMoveSpeedRate':.9,'poisonDurationRate':1.2,'controlDurationRate':.7,'skillBuffDurationRate':1.8,'playerAttackSpeedBonus':4,'criticalDamageWeight':45,'maxDropGold':3000,'dropRange':5,'dropStackSize':6,'maxBossTames':2}
        hot=call('/management/settings','PUT',{'values':edited,'version':system['version']})
        check(hot['values']==edited,'System settings failed to apply live values')
        status_after=call('/management/status')
        check(status_before['lastSaveUtc']==status_after['lastSaveUtc'],'MaxIP change unexpectedly restarted the engine')
        call('/management/settings','PUT',{'values':edited,'version':system['version']},expected=409)
        call('/management/settings','PUT',{'values':edited|{'dropRate':0},'version':hot['version']},expected=400)
        cold=call('/management/settings','PUT',{'values':edited|{'skillCooldownRate':.75},'version':hot['version']})
        check(call('/management/status')['lastSaveUtc']!=status_after['lastSaveUtc'],'Skill cooldown change skipped required metadata restart')
        check(cold['values']==edited|{'skillCooldownRate':.75},'Balance values changed after engine reload')
        status_after=call('/management/status')
        safe=call('/management/settings','PUT',{'values':edited|{'safeZoneBorder':not edited['safeZoneBorder']},'version':cold['version']})
        check(call('/management/status')['lastSaveUtc']!=status_after['lastSaveUtc'],'Safe zone change skipped the required engine restart')
        restored=call('/management/settings','PUT',{'values':system['values']|{'monsterDamageRate':1.25,'poisonDurationRate':1.5},'version':safe['version']})
        check(restored['values']==system['values']|{'monsterDamageRate':1.25,'poisonDurationRate':1.5},'Settings did not survive safe engine restart')
        (runtime/'settings-saved.json').write_text(json.dumps(restored['values']))
        print('PASS system settings live MaxIP/rates, authorization/conflicts/validation, safe zone restart and restoration.')
    if '--verify' in sys.argv:
        check(call('/management/settings')['values']==json.loads((runtime/'settings-saved.json').read_text()),'System settings lost after full-host restart')
        saved=json.loads((runtime/'administration-saved.json').read_text())
        for path, expected in saved.items():
            actual=call(path)
            if path==prefix+'/spawns?mapId=1': actual=[v|{'alive':0,'secondsUntilSpawn':0} for v in actual]
            check(actual==expected, 'Full-host restart mismatch: '+path)
        call(f"{prefix}/accounts/{(runtime/'deleted-account-id.txt').read_text()}",expected=404)
        live, _=session('OpsAccount','Changed123',saved[prefix+'/characters/1']['id'])
        kick(call(prefix+'/accounts/1')); live.wait(timeout=15)
        print(f'Passed {checks} administration full-host SQLite restart checks.')
        sys.exit(0)

    call(prefix+'/catalog/monsters?page=1',auth=False,expected=401)
    call(prefix+'/catalog/monsters?page=0',expected=400)
    call(prefix+'/catalog/unknown?page=1',expected=400)
    monsters=call(prefix+'/catalog/monsters?page=1')
    check(monsters['total']==3 and len(monsters['items'])==3 and next(m for m in monsters['items'] if m['name']=='NativeDanger')['attributes']['生命']=='9999','Monster figure catalog lost original attributes')
    npcs=call(prefix+'/catalog/npcs?page=1&search=Native')
    check(npcs['total']==1 and npcs['items'][0]['name']=='Native Shop','Global NPC catalog did not return the original definitions')
    check(call(prefix+'/catalog/npcs?page=2')['items']==[],'Out-of-range catalog page did not remain empty')
    call(prefix+'/accounts',auth=False,expected=401)
    call(prefix+'/characters',auth=False,expected=401)
    for invalid in ({'accountID':'../bad'},{'password':'x'},{'accountID':None},{'password':None}):
        call(prefix+'/accounts','POST',{'accountID':'OpsAccount','password':'Start123',}|invalid,expected=400)
    a=call(prefix+'/accounts','POST',{'accountID':'OpsAccount','password':'Start123',})
    other=call(prefix+'/accounts','POST',{'accountID':'OpsSecond','password':'Other123',})
    call(prefix+'/accounts','POST',{'accountID':'opsaccount','password':'Start123',},expected=409)
    aid=a['id']; check(aid==1, 'Fixture must start without player accounts')
    check(not any(k in json.dumps(a).lower() for k in ('salt','hash','start123','storagepassword')), 'Account DTO leaked credentials')
    # Account deletion retains target confirmation, concurrency, backups and rollback.
    empty=call(prefix+'/accounts','POST',{'accountID':'OpsEmpty','password':'Start123'})
    eid=empty['id']; deletion=f'{prefix}/accounts/{eid}/delete'
    call(deletion,'POST',command(empty['accountID'],empty['version']),auth=False,expected=401)
    call(deletion,'POST',command('wrong',empty['version']),expected=400)
    call(deletion,'POST',command(empty['accountID'],'stale'),expected=409)
    call(prefix+'/accounts','PUT',account_edit(empty,gold=1))
    funded=account(eid)
    call(deletion,'POST',command(funded['accountID'],funded['version']),expected=409)
    call(prefix+'/accounts','PUT',account_edit(funded,gold=0))
    empty=account(eid)
    with sqlite3.connect(runtime/'mir2.sqlite3') as db:
        db.execute("CREATE TRIGGER fail_account_delete BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirADB' AND NEW.data!=OLD.data BEGIN SELECT RAISE(FAIL,'injected delete failure'); END")
    try: call(deletion,'POST',command(empty['accountID'],empty['version']),expected=500)
    finally:
        with sqlite3.connect(runtime/'mir2.sqlite3') as db: db.execute('DROP TRIGGER fail_account_delete')
    check(account(eid)==empty,'Failed account delete lost the in-memory account')
    call('/management/restart','POST')
    check(account(eid)==empty,'Failed account delete persisted a partial removal')
    call(deletion,'POST',{'confirmation':empty['accountID'],'version':empty['version']})
    call(f'{prefix}/accounts/{eid}',expected=404)
    replacement=call(prefix+'/accounts','POST',{'accountID':'OpsFresh','password':'Start123',})
    check(replacement['id']>eid,'Deleting an account allowed ID reuse')
    (runtime/'deleted-account-id.txt').write_text(str(eid))
    for invalid,code in [({'confirmation':'wrong'},400),({'version':''},409),({'banned':True,'banReason':'test','banUntil':'2020-01-01T00:00:00Z'},400)]:
        call(prefix+'/accounts','PUT',account_edit(a,**invalid),expected=code)
    call(prefix+'/accounts','PUT',account_edit(a,gold=12345,credit=456,isGM=True))
    call(prefix+'/accounts','PUT',account_edit(a,gold=9),expected=409)
    a=account(aid); check((a['gold'],a['credit'],a['isGM'])==(12345,456,True),'Account edit did not persist')
    call(prefix+'/accounts','PUT',account_edit(a,isGM=False,banned=True,banReason='integration ban'))
    session('OpsAccount','Start123',0,'--expect-banned')
    a=account(aid); call(prefix+'/accounts','PUT',account_edit(a,banned=False))
    # Inject a storage failure after data edits to verify both memory and SQLite rollback.
    a=account(aid)
    with sqlite3.connect(runtime/'mir2.sqlite3') as db:
        db.execute("CREATE TRIGGER fail_admin BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirADB' AND NEW.data!=OLD.data BEGIN SELECT RAISE(FAIL,'injected snapshot failure'); END")
    try: call(prefix+'/accounts','PUT',account_edit(a,gold=999999),expected=500)
    finally:
        with sqlite3.connect(runtime/'mir2.sqlite3') as db: db.execute('DROP TRIGGER fail_admin')
    check(account(aid)==a,'Failed storage write leaked an in-memory account edit')
    call('/management/restart','POST'); check(account(aid)==a,'Failed storage write persisted partial data')
    check(account(other['id'])==other,'Engine restart discarded an account without characters')
    for invalid_class in (3,4,255):
        call(prefix+'/characters','POST',{'accountId':aid,'name':'InvalidClass','class':invalid_class,'gender':0},expected=400)
    c=call(prefix+'/characters','POST',{'accountId':aid,'name':'OpsHero','class':0,'gender':0,})
    cid=c['id']; check(c['level']==0 and not c['inventory'], 'New character bypassed original first-login initialization')
    call(prefix+'/characters','POST',{'accountId':aid,'name':'OpsHero','class':0,'gender':0,},expected=409)
    call(prefix+'/characters','POST',{'accountId':aid,'name':'../bad','class':0,'gender':0,},expected=400)
    call(prefix+'/characters','PUT',character_edit(c,level=10),expected=409)
    call(f'{prefix}/characters/{cid}/items','POST',{'itemId':1,'count':1,**command(c['name'],c['version'])},expected=409)
    live,_=session('OpsAccount','Start123')
    a=account(aid); check(a['online'],'Character-selection connection was not treated as online')
    call(f'{prefix}/accounts/{aid}/delete','POST',command(a['accountID'],a['version']),expected=409)
    call(prefix+'/accounts','PUT',account_edit(a,gold=1),expected=409)
    call(prefix+'/characters','PUT',character_edit(c),expected=409)
    call(f'{prefix}/accounts/{aid}/password','POST',{'password':'Wrong123','requireChange':False,**command(a['accountID'],a['version'])},expected=409)
    kick(a); live.wait(timeout=15)
    live,native=session('OpsAccount','Start123',cid)
    until(lambda: native().get('baseStatsReady'))
    speed=call('/management/settings'); before_speed=native()['attackSpeed']
    faster=call('/management/settings','PUT',{'values':speed['values']|{'playerAttackSpeedBonus':3},'version':speed['version']})
    until(lambda: native()['attackSpeed']==before_speed+3)
    call('/management/settings','PUT',{'values':speed['values'],'version':faster['version']})
    until(lambda: native()['attackSpeed']==before_speed)
    print('PASS online original BaseStatsInfo updates attack speed and restores it without reconnecting.')
    c=character(cid); check(c['online'] and c['level']>=1 and c['inventory'],'Original login did not initialize the character')
    call(prefix+'/characters','PUT',character_edit(c),expected=409)
    spawns=call(prefix+'/spawns?mapId=1'); sx=spawns[0]['x']-1; sy=spawns[0]['y']-1
    travel={'mapId':2,'x':sx,'y':sy+1,'setBind':False,**command(c['name'])}
    call(f'{prefix}/characters/{cid}/travel','POST',travel|{'x':-1},expected=400)
    call(f'{prefix}/characters/{cid}/travel','POST',travel|{'setBind':True},expected=409)
    call(f'{prefix}/characters/{cid}/travel','POST',travel)
    until(lambda: native().get('mapId')==2 and native().get('x')==sx and native().get('y')==sy+1)
    check(character(cid)['mapId']==2,'Online teleport did not update engine state')
    kick(account(aid)); live.wait(timeout=15)
    c=character(cid); call(prefix+'/characters','PUT',character_edit(c,level=5,experience=0,pkPoints=50,muted=True))
    c=character(cid); check((c['level'],c['pkPoints'],c['muted'])==(5,50,True),'Character edit failed')
    call(prefix+'/characters','PUT',character_edit(c,level=0),expected=400)
    call(prefix+'/characters','PUT',character_edit(c,level=501),expected=400)
    call(f'{prefix}/characters/{cid}/travel','POST',travel|{'mapId':1,'setBind':True})
    c=character(cid); check((c['bindMapId'],c['bindX'],c['bindY'])==(1,sx,sy+1),'Offline relocation lost bind point')
    item=call('/management/development/catalog/items?search=NativeChoice0')[0]
    grant={'itemId':item['id'],'count':2,**command(c['name'],c['version'])}
    call(f'{prefix}/characters/{cid}/items','POST',grant|{'count':60},expected=409)
    call(f'{prefix}/characters/{cid}/items','POST',grant|{'itemId':999999},expected=400)
    call(f'{prefix}/characters/{cid}/items','POST',grant|{'count':65536},expected=400)
    call(f'{prefix}/characters/{cid}/items','POST',grant)
    call(f'{prefix}/characters/{cid}/items','POST',grant,expected=409)
    c=character(cid); granted=[i for i in c['inventory'] if i['itemId']==item['id']]
    check(len(granted)==2 and all(i['count']==1 and i['grid']=='Inventory' for i in granted),'Original item stacking/empty-slot rule failed')
    token=call('/management/development/catalog/items?search=NativeToken1')[0]
    call(f'{prefix}/characters/{cid}/items','POST',{'itemId':token['id'],'count':21,**command(c['name'],c['version'])})
    c=character(cid); tokens=[i for i in c['inventory'] if i['itemId']==token['id']]
    check([i['count'] for i in tokens]==[20,1] and all(i['grid']=='QuestInventory' for i in tokens),'Quest grants used the wrong inventory or stack size')
    check(len({i['uniqueId'] for i in c['inventory']})==len(c['inventory']) and all(isinstance(i['uniqueId'],str) for i in c['inventory']),'Issued items have duplicate or unsafe JSON IDs')
    call(f'{prefix}/characters/{cid}/delete','POST',command(c['name'],c['version']))
    c=character(cid); check(c['deleted'],'Soft delete failed')
    a=account(aid); call(f'{prefix}/accounts/{aid}/delete','POST',command(a['accountID'],a['version']),expected=409)
    call(f'{prefix}/characters/{cid}/restore','POST',command(c['name'],c['version']))
    c=character(cid); check(not c['deleted'] and len(c['inventory'])>=4,'Character restoration lost items')
    a=account(aid)
    call(f'{prefix}/accounts/{aid}/storage-password','POST',{'password':'Store123','requireChange':False,**command(a['accountID'],a['version'])})
    a=account(aid)
    call(f'{prefix}/accounts/{aid}/password','POST',{'password':'Force123','requireChange':True,**command(a['accountID'],a['version'])})
    session('OpsAccount','Start123',0,'--expect-login=4')
    session('OpsAccount','Force123',0,'--expect-login=5')
    live,_=session('OpsAccount','Force123',cid,'--new-password=Changed123')
    check(not account(aid)['requirePasswordChange'],'Original password-change protocol did not clear force-change')
    kick(account(aid)); live.wait(timeout=15)
    mate=call(prefix+'/characters','POST',{'accountId':other['id'],'name':'OpsMate','class':1,'gender':1,})
    g=call(prefix+'/guilds','POST',{'name':'OpsGuild','leaderId':cid,})
    gid=g['id']; check(g['members'][0]['characterId']==cid and character(cid)['guildId']==gid,'Guild creation left inconsistent membership')
    with sqlite3.connect(runtime/'mir2.sqlite3') as db:
        db.execute("CREATE TRIGGER fail_guild BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirADB' AND NEW.data!=OLD.data BEGIN SELECT RAISE(FAIL,'injected cross-snapshot failure'); END")
    try: call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':mate['id'],'operation':'add',**command(g['name'],g['version'])},expected=500)
    finally:
        with sqlite3.connect(runtime/'mir2.sqlite3') as db: db.execute('DROP TRIGGER fail_guild')
    check(guild(gid)==g and character(mate['id'])['guildId']==-1,'Failed guild mutation leaked cross-snapshot edits')
    call('/management/restart','POST')
    check(guild(gid)==g and character(mate['id'])['guildId']==-1,'Failed guild mutation persisted partial membership')
    c=character(cid); call(f'{prefix}/characters/{cid}/delete','POST',command(c['name'],c['version']),expected=409)
    call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':mate['id'],'operation':'add',**command(g['name'],g['version'])})
    g=guild(gid); check(len(g['members'])==2 and character(mate['id'])['guildId']==gid,'Guild add failed')
    call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':cid,'operation':'remove',**command(g['name'],g['version'])},expected=409)
    call(prefix+'/guilds','PUT',guild_edit(g,gold=9000,notice='Integration notice\nSecond line',sparePoints=2))
    g=guild(gid)
    call(f'{prefix}/guilds/{gid}/ranks','POST',{'rankId':0,'name':'Leader','permissions':0,**command(g['name'],g['version'])},expected=400)
    call(f'{prefix}/guilds/{gid}/ranks','POST',{'rankId':1,'name':'Officers','permissions':3,**command(g['name'],g['version'])})
    g=guild(gid)
    call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':mate['id'],'operation':'leader',**command(g['name'],g['version'])})
    g=guild(gid); check(next(m for m in g['members'] if m['characterId']==mate['id'])['rank']==0,'Leadership transfer failed')
    call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':cid,'operation':'remove',**command(g['name'],g['version'])})
    g=guild(gid); call('/management/restart','POST')
    check(guild(gid)==g and g['ranks'][1]['permissions']==3 and character(cid)['guildId']==-1,'Guild restart lost an empty rank or cross-snapshot membership')
    call(f'{prefix}/guilds/{gid}/members','POST',{'characterId':cid,'operation':'add',**command(g['name'],g['version'])})
    g=guild(gid); live,_=session('OpsAccount','Changed123',cid)
    call(prefix+'/guilds','PUT',guild_edit(g,gold=1),expected=409)
    kick(account(aid)); live.wait(timeout=15)
    # New ranks retain the native trailing basic-member rank.
    g=guild(gid)
    call(f'{prefix}/guilds/{gid}/ranks','POST',{'rankId':-1,'name':'Recruiters','permissions':2,**command(g['name'],g['version'])})
    g=guild(gid); check(len(g['ranks'])==3 and g['ranks'][1]['name']=='Recruiters','New rank was not inserted before the basic rank')
    call(f'{prefix}/guilds/{gid}/disband','POST',command(g['name'],g['version']),expected=409)
    tempchar=call(prefix+'/characters','POST',{'accountId':other['id'],'name':'TempLeader','class':0,'gender':0,})
    tempguild=call(prefix+'/guilds','POST',{'name':'TempGuild','leaderId':tempchar['id'],})
    call(f"{prefix}/guilds/{tempguild['id']}/disband",'POST',command(tempguild['name'],tempguild['version']))
    check(character(tempchar['id'])['guildId']==-1,'Disband left a character referencing a deleted guild')
    call('/management/restart','POST')
    call(f"{prefix}/guilds/{tempguild['id']}",expected=404)
    check(guild(gid)==g,'Disbanding a guild corrupted the remaining guild')
    # World population uses the original spawn/NPC records, including their live status.
    spawns=call(prefix+'/spawns?mapId=1'); original=spawns[0]
    value=original|{'id':0,'version':'','count':0,'x':sx+2,'y':sy-2,'direction':8}
    def mutation(v, confirm='Native Test Field'): return {'value':v,**command(confirm)}
    for invalid in ({'monsterId':999999},{'x':-1},{'count':1001},{'randomDelayMinutes':601},{'direction':9},{'route':'../../outside'}):
        call(prefix+'/spawns','POST',mutation(value|invalid),expected=400)
    call(prefix+'/spawns','POST',mutation(value))
    created=max(call(prefix+'/spawns?mapId=1'),key=lambda s:s['id']); label=created['mapName']+'/'+str(created['id'])
    check(created['id']>3 and created['alive']==0 and created['count']==0,'Disabled new spawn appeared live')
    call(prefix+'/spawns','PUT',mutation(created|{'count':2,'delayMinutes':10,'randomDelayMinutes':1},label))
    call(prefix+'/spawns','PUT',mutation(created,label),expected=409)
    created=next(s for s in call(prefix+'/spawns?mapId=1') if s['id']==created['id'])
    for _ in range(2): call(f"{prefix}/spawns/1/{created['id']}/refill",'POST',command(label,created['version']))
    created=next(s for s in call(prefix+'/spawns?mapId=1') if s['id']==created['id'])
    check(created['alive']<=2,'Immediate refill exceeded the original spawn cap')
    call(f"{prefix}/spawns/1/{created['id']}/delete",'POST',command(label,created['version']))
    check(len(call(prefix+'/spawns?mapId=1'))==len(spawns),'Spawn deletion lost other definitions')
    npc=call('/management/development/npcs/1'); fresh=npc|{'id':0,'version':'','name':'Ops NPC','x':sx-3,'y':sy,'minLevel':1,'maxLevel':500,'classRequired':'Warrior','dayOfWeek':'Monday','timeVisible':True,'startMinute':60,'endMinute':120,'flagNeeded':1,'showOnBigMap':True,'bigMapIcon':2,'canTeleportTo':True}
    call(prefix+'/npcs','POST',mutation(fresh,'Ops NPC'),expected=409)
    doc=call('/management/development/file?path=Envir/NPCs/OpsNpc.txt')
    call('/management/development/file','PUT',{'path':doc['path'],'content':'[@MAIN]\n#SAY\nOperations NPC.\n','version':doc['version']})
    fresh['fileName']='OpsNpc'
    call(f"{prefix}/npcs/1/delete",'POST',command(npc['name'],npc['version']),expected=409)
    call(prefix+'/spawns','POST',mutation(None),expected=400)
    call(prefix+'/npcs','POST',mutation(None,'Ops NPC'),expected=400)
    for invalid in ({'flagNeeded':2000},{'dayOfWeek':'bad'},{'classRequired':'bad'},{'maxLevel':-1},{'endMinute':59},{'fileName':'../../outside'}):
        call(prefix+'/npcs','POST',mutation(fresh|invalid,'Ops NPC'),expected=400)
    call(prefix+'/npcs','POST',mutation(fresh,'Ops NPC'))
    new_id=max(r['id'] for r in call('/management/development/catalog/npcs'))
    created_npc=call(f'/management/development/npcs/{new_id}')
    check(all(created_npc[k]==fresh[k] for k in ('classRequired','dayOfWeek','timeVisible','startMinute','endMinute','flagNeeded','showOnBigMap','bigMapIcon','canTeleportTo')),'Advanced NPC fields were not saved')
    call(prefix+'/npcs','PUT',mutation(created_npc|{'minLevel':2,'name':'Ops NPC Edited'},'Ops NPC'))
    call(prefix+'/npcs','PUT',mutation(created_npc,'Ops NPC'),expected=409)
    check(call('/management/development/npcs/1')==npc,'NPC creation altered the source NPC')
    disposable=created_npc|{'id':0,'version':'','name':'Temporary NPC'}
    call(prefix+'/npcs','POST',mutation(disposable,'Temporary NPC'))
    disposable_id=max(r['id'] for r in call('/management/development/catalog/npcs'))
    disposable=call(f'/management/development/npcs/{disposable_id}')
    call(f'{prefix}/npcs/{disposable_id}/delete','POST',command(disposable['name'],disposable['version']))
    call(f'/management/development/npcs/{disposable_id}',expected=404)
    # The editor uses the original terrain/collision mask, with authenticated versioned images.
    call(prefix+'/maps',auth=False,expected=401)
    check({m['id'] for m in call(prefix+'/maps')}=={1,2},'Map picker includes unloaded maps')
    overview=call(prefix+'/maps/1')
    call(prefix+'/maps/999999',expected=404)
    call(prefix+'/maps/1/image?version='+overview['imageVersion'],auth=False,expected=401)
    call(prefix+'/maps/1/image?version=stale',expected=410)
    png=call(prefix+'/maps/1/image?version='+overview['imageVersion'],raw=True)
    check(png[:8]==b'\x89PNG\r\n\x1a\n' and all(0<n<=4096 for n in struct.unpack('>II',png[16:24])),'Map thumbnail PNG is invalid')
    mask=base64.b64decode(overview['walkable']); width=overview['width']; height=overview['height']
    check(len(mask)==(width*height+7)//8 and mask[(sy*width+sx)//8] & (1<<((sy*width+sx)%8)),'Collision mask does not include the player start')
    blocked=next(i for i in range(width*height) if not mask[i//8] & (1<<(i%8)))
    points=[value|{'x':sx+2,'y':sy-2,'count':2,'spread':1},value|{'x':sx-2,'y':sy-2,'count':3,'spread':1}]
    def batch(values, version=None): return {**command(overview['title']),'mapId':1,'values':values,'version':overview['spawnVersion'] if version is None else version}
    for invalid in ([],[points[0],points[1]|{'x':-1}], [points[0]|{'x':width}], [points[0]|{'mapId':2}], [points[0]]*101, [points[0]|{'count':1000}]*11):
        call(prefix+'/spawns/batch','POST',batch(invalid),expected=400)
    check(call(prefix+'/maps/1')['spawnVersion']==overview['spawnVersion'],'Invalid multi-point batch partially changed the map')
    call(prefix+'/spawns/batch','POST',batch(points,'stale'),expected=409)
    with sqlite3.connect(runtime/'mir2.sqlite3') as db:
        db.execute("CREATE TRIGGER fail_batch BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirDB' AND NEW.data!=OLD.data BEGIN SELECT RAISE(FAIL,'injected batch failure'); END")
    try: call(prefix+'/spawns/batch','POST',batch(points),expected=500)
    finally:
        with sqlite3.connect(runtime/'mir2.sqlite3') as db: db.execute('DROP TRIGGER fail_batch')
    check(call(prefix+'/maps/1')['spawnVersion']==overview['spawnVersion'],'Failed batch leaked partial definitions or IDs')
    blocked_value=value|{'x':blocked%width,'y':blocked//width,'count':1,'spread':0}
    call(prefix+'/spawns','POST',mutation(blocked_value))
    corrected=max(call(prefix+'/spawns?mapId=1'),key=lambda s:s['id'])
    corrected_label=corrected['mapName']+'/'+str(corrected['id'])
    call(f"{prefix}/spawns/1/{corrected['id']}/refill",'POST',command(corrected_label,corrected['version']))
    until(lambda:next(v for v in call(prefix+'/spawns?mapId=1') if v['id']==corrected['id'])['alive']>0)
    check((corrected['x'],corrected['y'])==(blocked%width,blocked//width),'Engine corrected the saved spawn coordinates instead of the runtime placement')
    call(f"{prefix}/spawns/1/{corrected['id']}/delete",'POST',command(corrected_label,corrected['version']))
    overview=call(prefix+'/maps/1')
    live,native=session('OpsAccount','Changed123',cid)
    call(prefix+'/spawns/batch','POST',batch(points))
    after=call(prefix+'/maps/1'); added=[v for v in after['spawns'] if v['id'] not in {v['id'] for v in overview['spawns']}]
    check(len(added)==2 and len({v['id'] for v in added})==2 and [v['count'] for v in added]==[2,3],'Batch lost individual point counts or unique IDs')
    check(after['imageVersion']==overview['imageVersion'],'Population edit invalidated static terrain')
    until(lambda: native().get('ready') and all(next(v for v in call(prefix+'/spawns?mapId=1') if v['id']==a['id'])['alive']>0 for a in added))
    check(live.poll() is None and native()['entered'],'Spawn hot apply disconnected the player')
    call(prefix+'/spawns/batch','POST',batch(points),expected=409)
    overview=after
    call(prefix+'/spawns/batch','POST',batch([added[0],added[0]]),expected=400)
    call(prefix+'/spawns/batch','POST',batch([added[0]|{'version':'stale'}]),expected=409)
    call(prefix+'/spawns/batch','POST',batch([v|{'count':0} for v in added]))
    # An online native client sees creates, moves, visibility edits and deletion without reconnecting.
    hot=fresh|{'name':'Visual NPC','x':sx-3,'y':sy+2,'dayOfWeek':'','classRequired':'','timeVisible':False,'flagNeeded':0,'minLevel':0,'maxLevel':0}
    call(prefix+'/npcs','POST',mutation(hot,'Visual NPC'))
    hot_id=max(r['id'] for r in call('/management/development/catalog/npcs'))
    hot=call(f'/management/development/npcs/{hot_id}')
    def visible(name): return next((a for a in native().get('actors',[]) if a['kind']=='Merchant' and a['name']==name),None)
    actor=until(lambda: visible('Visual NPC')); actor_id=actor['id']
    def edit_hot(**fields):
        global hot
        call(prefix+'/npcs','PUT',mutation(hot|fields,hot['name']))
        hot=call(f'/management/development/npcs/{hot_id}')
    edit_hot(name='Visual NPC Moved',x=sx-3,y=sy-2,image=1)
    actor=until(lambda: visible('Visual NPC Moved'))
    check(actor['id']==actor_id and (actor['x'],actor['y'],actor['image'])==(sx-3,sy-2,1) and not visible('Visual NPC'),'NPC hot move duplicated/recreated the actor or lost appearance')
    for restriction in ({'minLevel':500},{'flagNeeded':1},{'classRequired':'Wizard'},{'dayOfWeek':(datetime.datetime.now()+datetime.timedelta(days=1)).strftime('%A')},{'timeVisible':True,'startMinute':720 if datetime.datetime.now().hour<12 else 0,'endMinute':721 if datetime.datetime.now().hour<12 else 1}):
        edit_hot(**restriction)
        until(lambda: not visible('Visual NPC Moved'))
        edit_hot(minLevel=0,flagNeeded=0,classRequired='',dayOfWeek='',timeVisible=False)
        check(until(lambda: visible('Visual NPC Moved'))['id']==actor_id,'Removing visibility restrictions did not restore the same NPC actor')
    edit_hot(mapId=2)
    until(lambda: not visible('Visual NPC Moved'))
    check(not any(n['id']==hot_id for n in call(prefix+'/maps/1')['npcs']) and any(n['id']==hot_id for n in call(prefix+'/maps/2')['npcs']),'Map overview retained NPC at the old map')
    edit_hot(mapId=1)
    check(until(lambda: visible('Visual NPC Moved'))['id']==actor_id,'Cross-map NPC move recreated the object')
    call(f'{prefix}/npcs/{hot_id}/delete','POST',command(hot['name'],hot['version']))
    until(lambda: not visible('Visual NPC Moved'))
    check(live.poll() is None and native()['ready'],'NPC editing disconnected the online native player')
    kick(account(aid)); live.wait(timeout=15)
    # Script replacement intentionally uses the legacy restart path, and remains persistent.
    replacement=call('/management/development/file?path=Envir/NPCs/OpsReplacement.txt')
    call('/management/development/file','PUT',{'path':replacement['path'],'content':'[@MAIN]\n#SAY\nReplacement script.\n','version':replacement['version']})
    edited=call(f'/management/development/npcs/{new_id}')
    result=call(prefix+'/npcs','PUT',mutation(edited|{'fileName':'OpsReplacement'},edited['name']))
    check('重启' in result['message'] and call(f'/management/development/npcs/{new_id}')['fileName']=='OpsReplacement','NPC script replacement skipped the safe reload path')
    check([v['count'] for v in call(prefix+'/spawns?mapId=1') if v['id'] in {a['id'] for a in added}]==[0,0],'Engine restart lost edited batch counts')
    call(prefix+'/audit',expected=404)
    with sqlite3.connect(runtime/'mir2.sqlite3') as db:
        check(not db.execute("SELECT 1 FROM sqlite_master WHERE name='admin_audit'").fetchone(), 'Obsolete audit table was recreated')

    backups=list((runtime/'backups').glob('admin-*/mir2.sqlite3'))
    check(len(backups)>=20,'Persistent operations did not create full backups')
    for path in (backups[0],backups[-1]):
        with sqlite3.connect(path) as db:
            check(db.execute('PRAGMA integrity_check').fetchone()[0]=='ok','Full backup integrity failed')
            names={r[0] for r in db.execute('SELECT name FROM snapshots')}
            check({'Server.MirDB','Server.MirADB'}<=names,'Backup lacks world or player snapshots')
    saved={path:call(path) for path in [f'{prefix}/accounts/{aid}',f'{prefix}/characters/{cid}',f'{prefix}/characters/{mate["id"]}',f'{prefix}/guilds/{gid}',f'/management/development/npcs/{new_id}']}
    check(saved[f'{prefix}/characters/{cid}']['muted'],'Permanent mute was lost after engine restarts')
    saved[prefix+'/spawns?mapId=1']=[v|{'alive':0,'secondsUntilSpawn':0} for v in call(prefix+'/spawns?mapId=1')]
    (runtime/'administration-saved.json').write_text(json.dumps(saved))
    print(f'Passed {checks} administration API, validation, backup/rollback and native TCP session checks.')
finally:
    for child,log in children:
        if child.poll() is None:
            child.terminate()
            try: child.wait(timeout=10)
            except subprocess.TimeoutExpired: child.kill(); child.wait(timeout=10)
        log.close()
