"""隔离原TCP仓库：材料守恒、实际链接/密码/容量、拆分取回/补药、重启与复制改图号。"""
import hashlib,json,os,pathlib,shutil,socket,sqlite3,subprocess,tempfile,time,urllib.request
repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-storage-')).resolve();data=root/'version-a'
def port():
    with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
game,http=port(),port();address=f'http://127.0.0.1:{http}';process=None;log=None;key='';starts=0
base='/management/companions'
def environment():return os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':hashlib.sha256(str(data).encode()).hexdigest().upper()}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=15) as r:return json.load(r)
def wait(predicate,seconds=60):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text()[-2500:])
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('仓库流程等待超时')
def start():
    global process,log,key,starts
    log=open(root/f'server-{starts}.log','w');starts+=1
    process=subprocess.Popen(['dotnet',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll'),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=environment(),stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text()[-2000:]
    process=None;log.close();log=None
def prepare(mode):
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--companion-storage',str(cid),mode],cwd=root,env=environment(),check=True)
    return json.loads((data/'runtime/companion-storage-fixture.json').read_text())
def command(action):
    row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':action,'version':row['profile']['version']})
def off():command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online'])
def snapshots():
    # 两个管理端点不是同一快照，存取恰发生在请求间时不能拿新旧两侧相加。
    c=call('/management/admin/characters/'+str(cid));a=call('/management/admin/accounts/'+str(aid))
    for _ in range(10):
        next_c=call('/management/admin/characters/'+str(cid));next_a=call('/management/admin/accounts/'+str(aid))
        if c['inventory']==next_c['inventory'] and a['storage']==next_a['storage']:
            return call(base+'/'+ident),next_c,next_a
        c,a=next_c,next_a
    raise AssertionError('无法取得稳定的背包/仓库快照')
samples=[];events=set()
def check_stock(fixture,expected):
    d,c,a=snapshots();assert not d['row']['lastError'],d['row']['lastError']
    entries=c['inventory']+a['storage'];ids=[i['uniqueId'] for i in entries];assert len(ids)==len(set(ids)),entries
    material=[i for i in entries if i['itemId']==fixture['material']]
    assert sum(i['count'] for i in material)==expected,material
    for original in fixture['original']:
        if original['index'] in [fixture['material'],fixture['red'],fixture['blue']]:continue
        assert any(int(i['uniqueId'])==original['id'] and i['count']==original['count'] for i in c['inventory']),original
    events.update(e['text'] for e in d['events'] if e['kind']=='asset')
    samples.append({'detail':d,'character':c,'account':a})
    return d,c,a
try:
    subprocess.run(['dotnet',str(repo/'tests/Compatibility/bin/Release/net8.0/Compatibility.dll'),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':str(repo/'data/versions/default')},check=True)
    start();overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    p=call(base,{'name':'StorageBot','class':2,'gender':0,'enabled':True,'acceptGroup':False,'llmMode':0,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=p['id'];command('start')
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row['lastError']
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId'];aid=row['accountId'];off();stop()
    fixture=prepare('prepare');start();command('start')
    def stored_and_stocked():
        d,c,a=check_stock(fixture,200);r=d['runtime'] or {};items=c['inventory']
        bag=sum(i['count'] for i in items if i['itemId']==fixture['material']);bank=sum(i['count'] for i in a['storage'] if i['itemId']==fixture['material'])
        blue=sum(i['count'] for i in items if i['itemId']==fixture['blue']);red=sum(i['count'] for i in items if i['itemId']==fixture['red'])
        return (d,c,a) if bag==5 and bank==195 and blue==8 and red==15 and 'storage-take:成功' in events else False
    d,c,a=wait(stored_and_stocked,100)
    assert d['runtime']['gold']==830 and d['runtime']['bagWeight']<d['runtime']['maxBagWeight']*4//5,d['runtime']
    assert {'storage-password:成功','storage-store:成功','storage-split:成功','storage-take:成功','buy:成功'}<=events,events
    assert any(e['kind']=='goal-blocked' and '仓库链接' in e['reason'] for e in d['events']),d['events']
    assert any(int(i['uniqueId'])==fixture['materialId'] and i['count']==195 for i in a['storage']),a['storage']
    for _ in range(10):time.sleep(.3);assert check_stock(fixture,200)[0]['runtime']['gold']==830
    (root/'main-samples.json').write_text(json.dumps(samples,ensure_ascii=False));off();stop();start()
    persisted=call('/management/admin/accounts/'+str(aid))
    assert sum(i['count'] for i in persisted['storage'] if i['itemId']==fixture['material'])==195
    stop();copy=root/'version-b';shutil.copytree(data,copy,symlinks=True);data=copy;fixture=prepare('copy');start()
    assert not call(base+'/overview')['settings']['enabled'],'复制版本未关闭总开关'
    settings=call(base+'/overview')['settings'];call(base+'/settings',{'settings':settings|{'enabled':True}},'PUT');events.clear();samples=[];command('start')
    total=fixture['materialCount']+sum(i['count'] for i in fixture['stored'] if i['index']==fixture['material'])
    def retrieved():
        d,c,a=check_stock(fixture,total)
        return (d,c,a) if sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['material'])==5 and 'storage-unlock:成功' in events and 'storage-take:成功' in events else False
    d,c,a=wait(retrieved,60);assert c['mapId']>=71 and 'storage-password:成功' not in events
    (root/'copy-samples.json').write_text(json.dumps(samples,ensure_ascii=False));off();stop()
    fixture=prepare('equipment');start();command('start');samples=[];events.clear();allowed=set(fixture['equipmentAllowed'])
    def stored_equipment():
        d,c,a=snapshots();assert not d['row']['lastError'],d['row']['lastError']
        entries=c['inventory']+a['storage'];ids=[int(i['uniqueId']) for i in entries];assert len(ids)==len(set(ids)),entries
        assert all(any(int(i['uniqueId'])==o['id'] and i['count']==o['count'] for i in entries) for o in fixture['original']),entries
        assert all(int(i['uniqueId']) in allowed for i in a['storage']),a['storage']
        assert all(any(int(i['uniqueId'])==o['id'] and i['grid']=='Equipment' and i['slot']==o['slot'] for i in c['inventory']) for o in fixture['equipped'])
        assert a['gold']==fixture['gold']==1000,a
        samples.append({'detail':d,'character':c,'account':a});events.update(e['text'] for e in d['events'] if e['kind']=='asset')
        return (d,c,a) if len(a['storage'])==3 and d['runtime'] and d['runtime']['emptySlots']>=3 and 'storage-store:成功' in events else False
    d,c,a=wait(stored_equipment,75)
    assert d['runtime']['bagWeight']*5<d['runtime']['maxBagWeight']*4,'未隔离出低负重、仅满格卡点'
    for _ in range(10):time.sleep(.3);stored_equipment()
    (root/'equipment-samples.json').write_text(json.dumps(samples,ensure_ascii=False));off();stop();start()
    persisted=call('/management/admin/accounts/'+str(aid));assert {int(i['uniqueId']) for i in persisted['storage']}==allowed
    stop();fixture=prepare('full');start();before=call('/management/admin/accounts/'+str(aid));command('start');wait(entered)
    for _ in range(25):
        time.sleep(.3);d,c,a=snapshots();assert not d['row']['lastError'],d['row']['lastError']
        assert a['storage']==before['storage'],'满仓仍改变了仓库物品'
        assert any(int(i['uniqueId'])==fixture['materialId'] and i['count']==200 for i in c['inventory']),c['inventory']
    off();stop();fixture=prepare('optional');start();events.clear();command('start')
    def stored_without_password():
        d,c,a=check_stock(fixture,200)
        return (d,c,a) if sum(i['count'] for i in c['inventory'] if i['itemId']==fixture['material'])==5 and sum(i['count'] for i in a['storage'] if i['itemId']==fixture['material'])==195 and 'storage-take:成功' in events else False
    d,c,a=wait(stored_without_password,60)
    assert 'storage-store:成功' in events and 'storage-take:成功' in events and not any('password' in e or 'unlock' in e for e in events),events
    off();stop();fixture=prepare('wrong');start();before=call('/management/admin/accounts/'+str(aid));command('start')
    wait(lambda:call(base+'/'+ident)['row']['lastError'],40);d,c,a=snapshots()
    assert '仓库认证' in d['row']['lastError'],d['row']['lastError']
    assert a['storage']==before['storage'] and a['version']==before['version'],'密码/账户版本在拒绝后变化'
    assert any(int(i['uniqueId'])==fixture['materialId'] and i['count']==200 for i in c['inventory'])
    wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop();prepare('verify-password')
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP跨图仓库：实际链接/初次密码，整叠200→仓库195+背包5，原编号与特殊/未来任务物保留，补足药品/扣170，无重复；重启/B复制改图号后原密码解锁/拆回3份；低负重满46格时原编号暂存2项链/1戒指，保留最好1项链/2戒指、已穿/强化/改最大耐久/未来任务装备，金币不变及重启；满仓不写锁定扩展格，无密码正常存取，人工密码拒绝且不重置。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Storage fixture: '+str(root))
