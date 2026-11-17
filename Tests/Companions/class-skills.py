"""隔离原TCP：五职业陪玩实际增益、护盾聚元素、攻击技能熟练度、原TCP击杀和完整重启。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.error,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-classes-')).resolve();data=root/'version-a'
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
    subprocess.run(['dotnet',str(compat),'--fixture',str(data),'--skill-fixture'],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':reference},check=True)
    start();stop()
    subprocess.run(['dotnet',str(compat),'--companion-classes'],cwd=root,env=env,check=True)
    fixture=json.loads((data/'runtime/companion-class-fixture.json').read_text());start();base='/management/companions'
    rows=call(base)['items'];assert len(rows)==5
    def command(row,action):
        current=call(base+'/'+row['profile']['id'])['row'];call(base+'/'+row['profile']['id']+'/command',{'action':action,'version':current['profile']['version']})
    for row in rows:command(row,'start')
    samples=[];seen={row['id']:{} for row in fixture};achieved={}
    def practiced():
        for row in rows:
            detail=call(base+'/'+row['profile']['id']);assert not detail['row']['lastError'],detail
            assert not any(e['kind']=='death' or e['kind']=='diagnostic' and '职业技能' in e['reason'] for e in detail['events']),detail
            runtime=detail.get('runtime') or {};cid=detail['row']['characterId']
            for skill in (runtime.get('combat') or {}).get('skills',[]):
                seen[cid][skill['spell']]=max(seen[cid].get(skill['spell'],0),skill['experience'])
            character=call('/management/admin/characters/'+str(cid));samples.append({'id':cid,'runtime':runtime,'character':character,'events':detail['events']})
            (root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
            expected=next(e for e in fixture if e['id']==cid)
            if all(seen[cid].get(skill,0)>0 for skill in expected['skills']) and character['experience']>0:achieved[cid]=True
        return len(achieved)==5
    wait(practiced,180)
    for row in rows:command(row,'stop')
    wait(lambda:all(not call('/management/admin/characters/'+str(e['id']))['online'] for e in fixture))
    saved={e['id']:call('/management/admin/characters/'+str(e['id'])) for e in fixture};stop();start()
    for e in fixture:
        restored=call('/management/admin/characters/'+str(e['id']));old=saved[e['id']]
        assert restored['experience']==old['experience']>0 and restored['spells']==old['spells']
        assert sorted((i['uniqueId'],i['itemId'],i['count']) for i in restored['inventory'])==sorted((i['uniqueId'],i['itemId'],i['count']) for i in old['inventory'])
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('PASS 五职业陪玩原TCP实际辅助/双阶段元素屏障/技能熟练度/击杀经验、无死亡及技能拒绝、装备和耗材编号完整重启。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Class fixture: '+str(root),flush=True)
