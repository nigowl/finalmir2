"""只读备份当前版本到隔离目录，以五职业原生新角色测量自然升到10级的时间，不资助或改倍率。"""
import argparse, hashlib, json, os, pathlib, shutil, socket, sqlite3, subprocess, tempfile, time, urllib.request

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source-version',type=pathlib.Path,required=True)
parser.add_argument('--server-dll',type=pathlib.Path,required=True)
parser.add_argument('--seconds',type=int,default=900)
args=parser.parse_args()
if not 30<=args.seconds<=3600:parser.error('采样时间必须为30–3600秒')
source=args.source_version.resolve();server=args.server_dll.resolve()
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-companions-efficiency-')).resolve();data=root/'version'
(data/'runtime').mkdir(parents=True)
shutil.copytree(source/'world',data/'world')
(data/'assets').mkdir()
for name in ['maps','libraries','sounds']:(data/'assets'/name).symlink_to(source/'assets'/name,target_is_directory=True)
for name in ['manifest.json','import-report.json']:
    if (source/'assets'/name).exists():shutil.copy2(source/'assets'/name,data/'assets'/name)
with sqlite3.connect('file:'+str(source/'runtime/mir2.sqlite3')+'?mode=ro',uri=True) as original,sqlite3.connect(data/'runtime/mir2.sqlite3') as db:
    original.backup(db)
    # 仅清除本次临时副本的身份、会籍和模型配置，保留真实世界、商品、怪物及掉落。
    db.execute("DELETE FROM snapshots WHERE name='Server.MirADB' OR name LIKE 'companions/%' OR name LIKE 'Guilds/%'")
    db.execute('DELETE FROM previous_snapshots');db.commit()

def freeport():
    with socket.socket() as sock:sock.bind(('127.0.0.1',0));return sock.getsockname()[1]
game,http=freeport(),freeport();address=f'http://127.0.0.1:{http}'
env=os.environ|{'MIR2_DATA_ROOT':str(data),'MIR2_RUNTIME_ROOT':str(data/'runtime'),'MIR2_GAME_PORT':str(game),'MIR2_RESOURCE_PORT':str(http)}
version=hashlib.sha256(str(data).encode()).hexdigest().upper();key='';process=None;log=None;generation=0
def call(path,body=None,method=None):
    headers={'Authorization':'Bearer '+key,'X-Mir2-Version':version}
    if body is not None:headers['Content-Type']='application/json'
    request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),headers=headers,method=method or ('POST' if body is not None else 'GET'))
    with urllib.request.urlopen(request,timeout=20) as response:return json.load(response)
def wait(predicate,seconds=90):
    deadline=time.monotonic()+seconds
    while time.monotonic()<deadline:
        assert process.poll() is None,'隔离服务提前退出：'+str(root)
        value=predicate()
        if value:return value
        time.sleep(.25)
    raise AssertionError('等待超时：'+str(root))
def start():
    global process,log,key,generation
    log=open(root/f'server-{generation}.log','w');generation+=1
    process=subprocess.Popen(['dotnet',str(server),f'--mir2-instance={pathlib.Path(__file__).resolve().parents[2]}:{data/"runtime"}:server'],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
    def ready():
        try:return call('/health')['ready']
        except OSError:return False
    wait(ready);key=(data/'runtime/admin.key').read_text().strip()
def stop():
    global process,log
    process.terminate();process.wait(60);assert process.returncode==0,'隔离服务未正常保存退出'
    process=None;log.close();log=None
def command(ident,action):
    row=call('/management/companions/'+ident)['row']
    call('/management/companions/'+ident+'/command',{'action':action,'version':row['profile']['version']})

try:
    start();assert not call('/management/admin/characters'),'隔离角色未清空'
    settings=call('/management/settings')['values']
    overview=call('/management/companions/overview')
    call('/management/companions/settings',{'settings':overview['settings']|{'enabled':True,'maxOnline':5,'smartScheduling':False,'smartRecovery':False}},'PUT')
    preset=next(p for p in overview['presets'] if p['key']=='balanced')
    profiles=[];started=time.monotonic();reached={};samples=[]
    for job,name in enumerate(['效率战士','效率法师','效率道士','效率刺客','效率弓手']):
        profile=call('/management/companions',{'name':name,'class':job,'gender':0,'enabled':True,'acceptGroup':False,'chat':False,'llmMode':0,
            'presetKey':preset['key'],'presetRevision':preset['revision'],'traits':preset['traits']|{'diligence':80},
            'schedule':{'timeZone':'Asia/Shanghai','windows':[{'days':127,'allDay':True}],'attendance':100,'jitterMinutes':0,'sessionMinutes':60,'restMinutes':0,'dailyMinutes':1440}})
        profiles.append(profile['id']);command(profile['id'],'start')
    while True:
        elapsed=round(time.monotonic()-started,1);rows=[]
        for ident in profiles:
            detail=call('/management/companions/'+ident);row=detail['row'];runtime=detail.get('runtime') or {}
            assert not row['lastError'],row['lastError']
            if row['level']>=10:reached.setdefault(ident,elapsed)
            rows.append({'id':ident,'name':row['profile']['name'],'level':row['level'],'characterId':row['characterId'],
                'x':row['x'],'y':row['y'],'state':row['state'],'reason':row['reason'],'runtime':runtime,'events':detail['events']})
        samples.append({'seconds':elapsed,'rows':rows});(root/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
        print(json.dumps({'seconds':elapsed,'levels':{r['name']:r['level'] for r in rows},'to10':reached},ensure_ascii=False),flush=True)
        if len(reached)==5 or elapsed>=args.seconds:break
        time.sleep(15)
    assert call('/management/settings')['values']==settings,'效率验证修改了真实版本倍率/系统设置'
    for ident in profiles:command(ident,'stop')
    wait(lambda:all(not call('/management/admin/characters/'+str(r['characterId']))['online'] for r in rows),90)
    settled=[call('/management/admin/characters/'+str(r['characterId'])) for r in rows]
    stop();start()
    for before in settled:
        after=call('/management/admin/characters/'+str(before['id']))
        for field in ['level','experience','gold','inventory','activeQuests','completedQuests','spells']:
            assert before[field]==after[field],(before['name'],field)
    stop()
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:assert db.execute('pragma integrity_check').fetchone()[0]=='ok'
    result={'passed':len(reached)==5,'to10Seconds':{r['name']:reached.get(r['id']) for r in rows},
        'finalLevels':{r['name']:r['level'] for r in rows},'expRate':settings['expRate'],'dropRate':settings['dropRate'],'noFunding':True,'restartPreserved':True}
    (root/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(result,ensure_ascii=False),flush=True)
    assert result['passed'],'部分职业未在时限内自然到10级：'+str(root)
finally:
    if process is not None and process.poll() is None:process.terminate();process.wait(60)
    if log is not None:log.close()
    print('Efficiency fixture: '+str(root),flush=True)
