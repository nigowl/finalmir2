"""独立版本的原TCP建会扣费/入会/攻城/重启；不接触正式角色或GM资产。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-guild-')).resolve();data=root/'version-a'
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
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('行会流程等待超时')
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
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text();process=None;log.close();log=None
base='/management/companions'
def detail(profile):
    d=call(base+'/'+profile['id']);assert not d['row']['lastError'],d
    return d
def membership(profile,receipt):
    d=detail(profile)
    return d if d['runtime'] and d['runtime']['guildName']=='NativeGuild' and any(e['text']==receipt+':成功' for e in d['events']) else False
def command(profile,action):
    row=detail(profile)['row'];return call(base+'/'+profile['id']+'/command',{'action':action,'version':row['profile']['version']})
def configure(profile,preferences):
    row=detail(profile)['row'];return call(base+'/'+profile['id'],row['profile']|{'guilds':preferences},'PUT')
def settings(**values):
    overview=call(base+'/overview');call(base+'/settings',{'settings':overview['settings']|values},'PUT')
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();settings(enabled=True,maxOnline=3)
    profiles=[]
    for name in ['GuildFounder','GuildJoiner']:
        p=call(base,{'name':name,'class':0,'gender':0,'enabled':True,'chat':True,'acceptGroup':False,
            'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}})
        command(p,'start')
        wait(lambda:detail(p)['row']['characterId']>0 and detail(p)['runtime'] is not None)
        command(p,'stop');wait(lambda:call(base+'/overview')['online']==0)
        profiles.append(p)
    founder,joiner=profiles;fid=detail(founder)['row']['characterId'];jid=detail(joiner)['row']['characterId'];stop()
    def fixture(mode):subprocess.run(['dotnet',str(compat),'--companion-guild',str(fid),str(jid),mode],cwd=root,env=env,check=True)
    fixture('prepare');source=json.loads((data/'runtime/companion-guild-fixture.json').read_text());start()
    configure(founder,{'mode':'create','name':'NativeGuild','siege':False});configure(joiner,{'mode':'join','name':'NativeGuild','siege':False})
    command(founder,'start');wait(lambda:detail(founder)['runtime'] is not None)
    before=call('/management/admin/characters/'+str(fid));time.sleep(5)
    d=detail(founder);assert d['runtime']['gold']==1500 and not d['runtime']['guildName'],d
    assert any(int(i['uniqueId'])==source['uniqueId'] and i['count']==1 for i in call('/management/admin/characters/'+str(fid))['inventory'])
    command(founder,'stop');wait(lambda:call(base+'/overview')['online']==0);stop();fixture('ready');start()
    command(founder,'start')
    created=wait(lambda:membership(founder,'guild-create'))
    assert created['runtime']['gold']==4000 and any(e['text']=='guild-create:成功' for e in created['events']),created
    assert not any(i['itemId']==source['token'] for i in call('/management/admin/characters/'+str(fid))['inventory'])
    command(joiner,'start');joined=wait(lambda:membership(joiner,'guild-join'))
    assert joined['runtime']['gold']==1000 and any(e['text']=='guild-join:成功' for e in joined['events']),joined
    command(founder,'pause');command(joiner,'pause')
    configure(joiner,{'mode':'join','name':'NativeGuild','siege':True})
    wait(lambda:'PK' in detail(joiner)['runtime']['guildGoal'])
    assert (detail(joiner)['runtime'].get('goal') or {}).get('kind')!='guild-siege'
    settings(allowPk=True)
    wait(lambda:(detail(joiner)['runtime'].get('goal') or {}).get('kind')=='guild-siege')
    command(joiner,'resume');samples=[]
    def captured():
        d=detail(joiner);c=call('/management/admin/characters/'+str(jid));samples.append({'detail':d,'character':c})
        return c if c['mapId']==source['palace'] and (d['runtime'].get('goal') or {}).get('kind')!='guild-siege' else False
    wait(captured,90);(root/'guild-observation.json').write_text(json.dumps(samples,ensure_ascii=False))
    for p in profiles:command(p,'stop')
    wait(lambda:call(base+'/overview')['online']==0);stop();fixture('proof');start()
    a=call('/management/admin/characters/'+str(fid));b=call('/management/admin/characters/'+str(jid))
    assert a['guildId']==b['guildId'] and a['guildName']=='NativeGuild' and b['mapId']==source['palace']
    assert detail(joiner)['row']['profile']['guilds']=={'mode':'join','name':'NativeGuild','siege':True}
    stop()
    fixture('classic');start();command(joiner,'start')
    wait(lambda:detail(joiner)['runtime'] and (detail(joiner)['runtime'].get('goal') or {}).get('mapId')==source['palace'])
    def classic_captured():
        d=detail(joiner);c=call('/management/admin/characters/'+str(jid))
        return c if c['mapId']==source['palace'] and d['runtime']['guildGoal'].startswith('守卫') else False
    wait(classic_captured,90);wait(lambda:detail(joiner)['row']['state']=='行会驻守')
    configure(joiner,{'mode':'join','name':'NativeGuild','siege':False})
    wait(lambda:(detail(joiner)['runtime'].get('goal') or {}).get('kind')!='guild-siege')
    wait(lambda:'行会' not in detail(joiner)['row']['state'])
    command(joiner,'stop');wait(lambda:call(base+'/overview')['online']==0);stop();fixture('classic-proof');start()
    assert call('/management/admin/characters/'+str(jid))['mapId']==source['palace']
    assert detail(joiner)['row']['profile']['guilds']=={'mode':'join','name':'NativeGuild','siege':False}
    assert detail(founder)['row']['characterId']==fid and detail(joiner)['row']['characterId']==jid
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP行会：预算不足不扣物，原NPC建会5000→4000/材料原编号移除，主动申请/邀请确认入会无扣款，PK关闭阻断/开启后公开步行夺取王宫，原归属/成员/设置和资产重启保持。',flush=True)
    print('PASS 原TCP经典攻城：山丘在外图仍进入实际王宫，原引擎确认本会归属；取消参战后恢复和平模式，正常重启保持身份/会籍/归属及关闭设置。',flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Guild fixture: '+str(root),flush=True)
