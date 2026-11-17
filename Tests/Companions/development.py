"""隔离原TCP：满药仍主动跨图购书、学习、补符、换右戒指并真实施法。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.error,urllib.request

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
    subprocess.run(['dotnet',str(compat),'--companion-development',str(cid)],cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-development-fixture.json').read_text());start()
    assert not call('/management/settings')['values']['autoLearnSkills']
    initial=call('/management/admin/characters/'+str(cid));assert initial['spells']==[] and initial['experience']==0
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
