"""隔离原TCP：陪玩主动组队、同行会优先、协同猎杀/经验、跨图跟队及原奖励保存。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2];root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-party-')).resolve();data=root/'version-a'
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
    with urllib.request.urlopen(request,timeout=15) as response:return json.load(response)
def wait(predicate,seconds=60):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        assert process.poll() is None,pathlib.Path(log.name).read_text()
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('组队等待超时：'+str(root))
def start():
    global process,log,key,starts
    starts+=1;log=(root/f'server-{starts}.log').open('w')
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={repo}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready,90);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text();process=None;log.close();log=None
base='/management/companions'
def command(p,action):
    current=call(base+'/'+p['id'])['row']['profile'];call(base+'/'+p['id']+'/command',{'action':action,'version':current['version']})
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();settings=call('/management/settings');call('/management/settings',{'values':settings['values']|{'allowCreateArcher':True,'maxIP':20},'version':settings['version']},'PUT')
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|{'enabled':True,'maxOnline':3}},'PUT')
    profiles=[];ids=[]
    for role in [0,2,4]:
        p=call(base,{'name':'PartyRole'+str(role),'class':role,'gender':0,'enabled':True,'acceptGroup':True,'chat':False,'llmMode':0,
            'traits':{'risk':100,'social':90,'helpfulness':90,'diligence':100,'persistence':100,'frugality':50},
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
        profiles.append(p);command(p,'start');cid=wait(lambda:call(base+'/'+p['id'])['row']['characterId']);ids.append(cid);command(p,'pause')
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop()
    subprocess.run(['dotnet',str(compat),'--companion-party',*map(str,ids)],cwd=root,env=env,check=True);start()
    fixture=json.loads((data/'runtime/party-fixture.json').read_text());samples=[];parties={p['id']:False for p in profiles};invites=[]
    for p in profiles:
        command(p,'start');command(p,'pause');wait(lambda:call('/management/admin/characters/'+str(ids[profiles.index(p)]))['online'])
    for p in profiles:command(p,'resume')
    def finished():
        current=[]
        for p,cid in zip(profiles,ids):
            d=call(base+'/'+p['id']);c=call('/management/admin/characters/'+str(cid));current.append({'detail':d,'character':c})
            assert not d['row']['lastError'] and not any(e['kind']=='death' for e in d['events']),d
            if any(e['kind']=='party' and all(name in e['text'] for name in ['PartyRole0','PartyRole2','PartyRole4']) for e in d['events']):parties[p['id']]=True
            if p==profiles[0]:
                for e in d['events']:
                    if e['kind']=='party-invite' and e not in invites:invites.append(e)
        samples.append(current);(root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
        return current if all(parties.values()) and all(v['character']['experience']>0 and v['character']['mapId']==fixture['remote'] for v in current) and 1 in current[0]['character']['completedQuests'] else False
    result=wait(finished,160)
    assert invites and 'PartyRole4' in invites[0]['text'],'未优先向同行会弓箭手发原邀请'
    assert result[0]['character']['guildId']==result[2]['character']['guildId']==fixture['guild'] and result[1]['character']['guildId']<0
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);saved=[call('/management/admin/characters/'+str(cid)) for cid in ids];stop();start()
    for old,cid in zip(saved,ids):
        new=call('/management/admin/characters/'+str(cid));assert new['experience']==old['experience'] and new['completedQuests']==old['completedQuests'] and new['guildId']==old['guildId']
        assert new['inventory']==old['inventory']
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP三陪玩自主组队、同行会优先、原队伍回执/共享击杀经验、公开出口跨图跟队、队长真实交付及原资产/会籍完整重启。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Party fixture: '+str(root),flush=True)
