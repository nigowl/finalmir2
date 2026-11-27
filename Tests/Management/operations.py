"""复用价格专项的隔离版本，验证金币写入保护、运行操作和真实在线玩家分页。"""
import concurrent.futures,json,pathlib,sqlite3,time,urllib.error,urllib.request

def run(address,data,game):
    import hashlib
    assert '/mir2-prices-' in str(data)
    headers={'Authorization':'Bearer '+(data/'runtime/admin.key').read_text().strip(),'X-Mir2-Version':hashlib.sha256(str(data).encode()).hexdigest().upper(),'Content-Type':'application/json'}
    def api(path,body=None,method=None,expected=200):
        req=urllib.request.Request(address+path,headers=headers,method=method or ('POST' if body is not None else 'GET'),data=None if body is None else json.dumps(body).encode())
        try:r=urllib.request.urlopen(req,timeout=90)
        except urllib.error.HTTPError as error:r=error
        content=r.read();assert r.code==expected,(path,r.code,content[:500])
        return json.loads(content) if r.code==200 else None
    role=next(row for row in api('/management/admin/characters') if row['name']=='NativeGM');path=f'/management/admin/characters/{role["id"]}'
    deadline=time.monotonic()+15
    while api('/management/admin/accounts/'+str(role['accountId']))['online']:
        assert time.monotonic()<deadline,'原TCP已退出，但账户连接未清理';time.sleep(.1)
    def edit(c,gold):return {k:c[k] for k in ['id','level','experience','pkPoints','banned','banReason','banUntil','muted','muteUntil','version']}|{'confirmation':c['name'],'gold':gold}
    c=api(path);gold=c['gold'];api('/management/admin/characters',edit(c,gold+7),'PUT');changed=api(path)
    assert changed['gold']==gold+7 and next(a for a in api('/management/admin/accounts') if a['id']==c['accountId'])['gold']==gold+7
    api('/management/admin/characters',edit(c,gold),'PUT',409)
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:db.execute("CREATE TRIGGER fail_gold BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirADB' AND NEW.data!=OLD.data BEGIN SELECT RAISE(FAIL,'injected gold failure'); END")
    try:api('/management/admin/characters',edit(changed,gold+100),'PUT',500)
    finally:
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:db.execute('DROP TRIGGER fail_gold')
    assert api(path)==changed
    api('/management/admin/characters',edit(changed,gold),'PUT');c=api(path)
    for invalid in [-1,4294967296]:api('/management/admin/characters',edit(c,invalid),'PUT',400)
    saved=api('/management/status')['lastSaveUtc']
    with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:db.execute("CREATE TRIGGER fail_restart BEFORE UPDATE ON snapshots WHEN NEW.name='Server.MirADB' BEGIN SELECT RAISE(FAIL,'injected save failure'); END")
    try:api('/management/restart',method='POST',expected=500)
    finally:
        with sqlite3.connect(data/'runtime/mir2.sqlite3') as db:db.execute('DROP TRIGGER fail_restart')
    assert api('/health')['ready'] and api('/management/status')['lastSaveUtc']==saved
    print('PASS 角色金币共享、读取冲突/范围校验、保存失败回滚；重启预保存失败保持在线且不误报成功。',flush=True)
    settings=api('/management/settings');settings['values'].update(maxUser=100,maxIP=100,maxAccountsPerIPHour=0,maxCharactersPerIPHour=0)
    api('/management/settings',{'values':settings['values'],'version':settings['version']},'PUT')
    base='/management/companions';profiles=[]
    for i in range(12):
        profile=api(base,{'name':f'分页实测{i:02}','class':0,'enabled':True,'scheduled':False,'llmMode':0,'schedule':{'windows':[{'allDay':True}],'jitterMinutes':0,'sessionMinutes':60,'restMinutes':0,'dailyMinutes':120}})
        api(base+'/'+profile['id']+'/command',{'action':'pause','version':profile['version']});profiles.append(profile)
    settings=api(base+'/overview')['settings'];settings.update(enabled=True,maxOnline=12,reservePlayers=1,models=[])
    api(base+'/settings',{'settings':settings},'PUT')
    deadline=time.monotonic()+90
    while len(api('/management/status')['players'])!=12:
        assert time.monotonic()<deadline,api(base+'?pageSize=100');time.sleep(.3)
    accounts=api('/management/admin/accounts');assert all(a['accountID'].startswith('pw') and a['accountID'][2:].isdigit() for a in accounts if a['id']!=c['accountId'])
    api('/management/admin/characters',edit(api(path),gold+1),'PUT')
    with concurrent.futures.ThreadPoolExecutor(2) as pool:list(pool.map(lambda _:api('/management/restart',method='POST'),range(2)))
    assert api('/health')['ready'] and api(path)['gold']==gold+1
    api('/management/admin/characters',edit(api(path),gold),'PUT')
    deadline=time.monotonic()+90
    while len(api('/management/status')['players'])!=12:
        assert time.monotonic()<deadline,api(base+'?pageSize=100');time.sleep(.3)
    assert all(not row['lastError'] for row in api(base+'?pageSize=100')['items'])
    print('PASS 12个pw账号通过原TCP注册/建角上线，两次并发重启串行完成后全部重新登录，资产与暂停意愿保持。',flush=True)
    return c['id']
