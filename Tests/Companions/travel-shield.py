"""隔离原TCP：法师赶路危险区先开盾、实际扣蓝/减伤属性、真实交付及完整重启。"""
import hashlib,json,os,pathlib,socket,sqlite3,subprocess,tempfile,time,urllib.request

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-travel-shield-')).resolve();data=root/'version-a'
server=pathlib.Path(os.environ['MIR2_COMPANION_SERVER_DLL'])
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
def wait(predicate,seconds=90):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        assert process.poll() is None,pathlib.Path(log.name).read_text()
        value=predicate()
        if value:return value
        time.sleep(.2)
    raise AssertionError('赶路护盾原TCP等待超时；'+str(root))
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
    process.terminate();process.wait(60);assert process.returncode==0,pathlib.Path(log.name).read_text();process=None;log.close();log=None
try:
    subprocess.run(['dotnet',str(compat),'--fixture',str(data)],cwd=repo,env=os.environ|{'MIR2_DATA_ROOT':os.environ.get('MIR2_DATA_ROOT',str(repo/'data/versions/default'))},check=True)
    start();base='/management/companions';o=call(base+'/overview');call(base+'/settings',{'settings':o['settings']|{'enabled':True}},'PUT')
    profile=call(base,{'name':'TravelWizard','class':1,'gender':0,'enabled':True,'acceptGroup':False,'chat':False,
        'schedule':{'timeZone':'UTC','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':30,'restMinutes':0,'dailyMinutes':60}});ident=profile['id']
    def command(action):
        row=call(base+'/'+ident)['row'];call(base+'/'+ident+'/command',{'action':action,'version':row['profile']['version']})
    command('start')
    def entered():
        row=call(base+'/'+ident)['row'];assert not row['lastError'],row
        return row if row['characterId'] and call('/management/admin/characters/'+str(row['characterId']))['online'] else False
    row=wait(entered);cid=row['characterId'];aid=row['accountId'];command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);stop()
    for quiet in [False,True]:
        subprocess.run(['dotnet',str(compat),'--companion-travel-shield',str(cid)]+(['--quiet'] if quiet else []),cwd=root,env=env,check=True)
        start();initial=call('/management/admin/characters/'+str(cid));assert initial['completedQuests']==[]
        command('start');wait(entered);command('pause')
        wait(lambda:call(base+'/'+ident)['row']['state']=='已暂停')
        paused=call(base+'/'+ident)['runtime'];assert paused and paused['mp']==paused['maxMp']
        time.sleep(1);assert call('/management/admin/characters/'+str(cid))['mp']==paused['mp'],'暂停发展期间仍提前开盾'
        command('resume');samples=[];shielded=False;mana=[];maximum=0
        def delivered():
            global shielded,maximum
            c=call('/management/admin/characters/'+str(cid));d=call(base+'/'+ident);rt=d['runtime'] or {};stats=(rt.get('combat') or {}).get('stats',{}).get('values',{})
            assert not d['row']['lastError'] and c['hp']>0 and c['experience']==0,(c,d)
            samples.append({'character':c,'detail':d});(root/('quiet.json' if quiet else 'threat.json')).write_text(json.dumps(samples,ensure_ascii=False))
            if rt:maximum=rt['maxMp']
            if not shielded and stats.get('DamageReductionPercent',0)>=20:
                assert c['mp']<=maximum-20 and c['x']<49,'没有实际扣蓝或到达交付后才开盾';shielded=True
            if c['online'] and rt:mana.append(c['mp'])
            return c if 1 in c['completedQuests'] else False
        result=wait(delivered,75)
        assert shielded!=quiet,('无威胁仍开盾' if quiet else '赶路期间没有原服魔法盾减伤生效',root)
        if quiet:assert min(mana)==maximum,'安静赶路额外耗蓝'
        else:
            assert not samples[-1]['detail']['runtime']['combat']['stats']['values'].get('DamageReductionPercent',0),'交付前短盾尚未结束，未覆盖补盾间隔'
            assert min(mana)==maximum-20,'重复开盾或额外耗蓝'
            assert any(e['state']=='护盾中' for s in samples for e in s['detail']['events'])
        command('stop');wait(lambda:not call('/management/admin/characters/'+str(cid))['online']);saved=call('/management/admin/characters/'+str(cid));gold=call('/management/admin/accounts/'+str(aid))['gold'];assert gold==10333
        stop();start();restored=call('/management/admin/characters/'+str(cid))
        assert restored['completedQuests']==saved['completedQuests']==[1] and restored['spells']==saved['spells']
        assert {(i['uniqueId'],i['count']) for i in restored['inventory']}=={(i['uniqueId'],i['count']) for i in saved['inventory']}
        assert call('/management/admin/accounts/'+str(aid))['gold']==gold;stop()
    with sqlite3.connect('file:'+str(data/'runtime/mir2.sqlite3')+'?mode=ro',uri=True) as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    print('PASS 原TCP危险赶路一次扣20MP及原护盾减伤属性、安静赶路不耗蓝、真实交付333金币、原编号与完整重启；'+str(root),flush=True)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Travel shield fixture: '+str(root),flush=True)
