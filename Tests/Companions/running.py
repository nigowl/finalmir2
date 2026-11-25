"""隔离原TCP验证长途跑步、相邻门、折弯/跨图/交互范围，以及超重步行回退。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
server=pathlib.Path(os.environ.get('MIR2_COMPANION_SERVER_DLL',str(repo/'src/Server/bin/Release/net8.0/Mir2.Server.dll')))
compat=repo/'tests/Compatibility/bin'/server.parent.parent.name/'net8.0/Compatibility.dll'
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-running-')).resolve();data=root/'version-a'
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
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if process.poll() is not None:raise AssertionError(pathlib.Path(log.name).read_text())
        result=predicate()
        if result:return result
        time.sleep(.12)
    raise AssertionError('步跑跨图等待超时')
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
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();base='/management/companions';overview=call(base+'/overview')
    call(base+'/settings',{'settings':overview['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'RunningVisitor','class':0,'gender':0,'acceptGroup':False,'chat':False,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
    ident=profile['id'];call(base+'/'+ident+'/command',{'action':'start','version':profile['version']})
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered,40);cid=row['characterId'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
    wait(lambda:call(base+'/overview')['online']==0 and not call('/management/admin/characters/'+str(cid))['online'],35)
    modes=[(False,False)] if os.environ.get('MIR2_COMPANION_RUN_ONLY')=='1' else [(False,False),(True,False),(False,True)]
    for heavy,classic in modes:
        stop();command=['dotnet',str(compat),'--companion-running',str(cid)]
        if heavy:command.append('--heavy')
        if classic:command.append('--classic-map')
        subprocess.run(command,cwd=root,env=env,check=True);start()
        initial=call('/management/admin/characters/'+str(cid));fixture=json.loads((data/'runtime/companion-running-fixture.json').read_text())
        settings=call('/management/settings')
        updated=call('/management/settings',{'values':settings['values']|{'expRate':1000},'version':settings['version']},'PUT')
        assert updated['values']['expRate']==1000 and updated['values']['dropRate']==settings['values']['dropRate']
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'start','version':row['profile']['version']})
        samples=[];started=time.monotonic()
        def completed():
            c=call('/management/admin/characters/'+str(cid));detail=call(base+'/'+ident)
            assert not detail['row']['lastError'],detail
            assert not any(e['kind']=='death' for e in detail['events']),detail
            if heavy and detail.get('runtime'):
                assert detail['runtime']['bagWeight']>detail['runtime']['maxBagWeight'],'超重夹具未按当前真实物品重量超重'
            if c['online']:
                samples.append({'time':time.monotonic()-started,'map':c['mapId'],'x':c['x'],'y':c['y'],'hp':c['hp']})
                x,y=c['x'],c['y'];assert y==5 and 5<=x<=35 or x==35 and 5<=y<=13 or y==13 and 10<=x<=35,c
            return c if 1 in c['completedQuests'] else False
        try:result=wait(completed,160)
        finally:(root/f'observations-{heavy}-{classic}.json').write_text(json.dumps(samples,ensure_ascii=False))
        elapsed=time.monotonic()-started
        moves=[]
        for a,b in zip(samples,samples[1:]):
            if a['map']==b['map'] and b['time']-a['time']<.5:
                distance=max(abs(a['x']-b['x']),abs(a['y']-b['y']))
                if distance:moves.append(distance)
        assert {s['map'] for s in samples}=={fixture['first'],fixture['second']},samples
        assert moves and moves[0]==1,('未先步行起步',moves)
        for map_id in [fixture['first'],fixture['second']]:
            steps=[max(abs(a['x']-b['x']),abs(a['y']-b['y'])) for a,b in zip(samples,samples[1:]) if a['map']==b['map']==map_id and (a['x'],a['y'])!=(b['x'],b['y'])]
            assert steps and steps[0]==1,('切图后未重新步行起步',map_id,steps)
        assert all(d==1 for d in moves) if heavy else any(d==2 for d in moves),('超重未步行回退' if heavy else '没有原服确认的两格跑步',moves)
        assert max(abs(result['x']-fixture['end']['X']),abs(result['y']-fixture['end']['Y']))<=4,result
        account=call('/management/admin/accounts/'+str(result['accountId']));assert account['gold']==17,account
        assert result['experience']==5000,('1000倍任务经验未通过原交付生效',result)
        if heavy:assert initial['inventory']==result['inventory'],'超重物被擅自处理'
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':'stop','version':row['profile']['version']})
        wait(lambda:call(base+'/overview')['online']==0 and not call('/management/admin/characters/'+str(cid))['online'],35)
        saved=call('/management/admin/characters/'+str(cid));stop();start()
        restored=call('/management/admin/characters/'+str(cid))
        assert all(restored[k]==saved[k] for k in ['id','mapId','x','y','inventory','completedQuests','experience']),restored
        assert call('/management/admin/accounts/'+str(restored['accountId']))['gold']==17
        print(f'PASS 原TCP格式{0 if classic else 1}/{"超重步行" if heavy else "先走后跑"}：{elapsed:.2f}秒完成，实际两格回执{moves.count(2)}次，折弯/两门/同向并排出口/两图/交付17金币与5000经验、重启资产守恒。',flush=True)
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Running fixture: '+str(root),flush=True)
