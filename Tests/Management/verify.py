"""Verify management operations against a disposable running game world."""
import hashlib, json, os, pathlib, sys, urllib.error, urllib.parse, urllib.request
root=pathlib.Path(sys.argv[1]); base=os.environ.get('MIR2_SERVER','http://127.0.0.1:15090').rstrip('/')
runtime=pathlib.Path(os.environ.get("MIR2_RUNTIME_ROOT", str(root/"runtime")))
key=(runtime/"admin.key").read_text().strip()
checks=0

def request(path, method='GET', value=None, raw=None, auth=True, expected=200):
    global checks
    data=raw if raw is not None else json.dumps(value).encode() if value is not None else None
    headers={'Authorization':'Bearer '+key} if auth else {}
    if value is not None: headers['Content-Type']='application/json'
    req=urllib.request.Request(base+path,data=data,headers=headers,method=method)
    try:
        response=urllib.request.urlopen(req,timeout=180)
    except urllib.error.HTTPError as error: response=error
    body=response.read(); assert response.code==expected,(path,response.code,body[:1000]); checks+=1
    return body,response.headers

def get(path): return json.loads(request(path)[0])
request('/management/status',auth=False,expected=401)
request('/management/status',auth=False,method='POST',expected=401)
status=get('/management/status'); assert status['ready'] and status['counts']['maps']==2
manifest=get('/assets/manifest'); previous=manifest['version']
body,headers=request('/assets/'+previous+'/map/0',auth=False)
assert hashlib.sha256(body).hexdigest()==headers['X-Content-SHA256']
assert body==(root/'assets/maps/0.map').read_bytes()
request('/management/development/file?path=../../../outside.txt',expected=400)
request('/management/resources/stage/maps/bad.map','PUT',raw=b'bad',expected=400)
request('/management/resources/stage/libraries/bad.Lib','PUT',raw=b'bad',expected=400)
request('/management/resources/stage/maps/probe.map','PUT',raw=body)
listing=get('/management/resources'); assert 'maps/probe.map' in listing['staged']
request('/assets/'+previous+'/map/probe',auth=False,expected=404)
published=json.loads(request('/management/resources/publish','POST')[0]); assert published['version']!=previous and published['changedFiles']==1 and published['restarted']
assert get('/management/status')['ready']
request('/assets/'+previous+'/map/0',auth=False,expected=410)
new=published['version']; newbody,newheaders=request('/assets/'+new+'/map/0',auth=False)
assert body==newbody and hashlib.sha256(newbody).hexdigest()==newheaders['X-Content-SHA256']
request('/management/resources/publish','POST',expected=409)
assert get('/management/status')['ready']
# Fail after replacing an existing map and adding one, so both rollback paths run.
for name, content in [('0.map',body+b'\0'),('a-rollback.map',body),('z-rollback.map',body)]:
    request('/management/resources/stage/maps/'+name,'PUT',raw=content)
(runtime/'staging/resources/maps/z-rollback.map').write_bytes(b'broken staged map')
request('/management/resources/publish','POST',expected=400)
assert get('/management/status')['ready']
restored,_=request('/assets/'+new+'/map/0',auth=False)
assert restored==body and not (root/'assets/maps/a-rollback.map').exists()
assert not list((root/'assets/maps').glob('*.publishing'))
for name in ('0.map','a-rollback.map','z-rollback.map'):
    request('/management/resources/staged/maps/'+name,'DELETE')
inspection=get('/management/resources/inspect-map/0.map'); assert inspection['width']>0 and inspection['blockedCells']>0
png=request('/management/resources/preview/CArmour/00?index=0')[0]; assert png.startswith(b'\x89PNG\r\n\x1a\n')
path='Envir/NPCs/NativeShop.txt'
doc=get('/management/development/file?path='+urllib.parse.quote(path,safe=''))
edit={'path':path,'content':doc['content']+'\n// verified by integration\n','version':doc['version']}
saved=json.loads(request('/management/development/file','PUT',edit)[0]); assert saved['version']!=doc['version']
request('/management/development/file','PUT',edit,expected=409)
assert list((runtime/'backups').glob('text-*/Envir/NPCs/NativeShop.txt'))
request('/management/reload-scripts','POST')
rows=get('/management/development/catalog/monsters?search=NativeTarget'); assert rows and rows[0]['id']==1
definition=get('/management/development/definition/monsters/1'); definition['hp']=21
assert len(definition['version'])==64
request('/management/development/definition','PUT',definition|{'version':''},expected=409)
request('/management/development/definition','PUT',definition|{'name':'../outside'},expected=400)
request('/management/development/definition','PUT',definition|{'name':'NativeDanger'},expected=409)
request('/management/development/definition','PUT',definition|{'minDc':30,'maxDc':20},expected=400)
request('/management/development/definition','PUT',definition)
request('/management/development/definition','PUT',definition,expected=409)
source=get('/management/development/definition/monsters/1');assert source['hp']==21 and source['version']!=definition['version']
copy={'kind':'monsters','sourceId':1,'name':'NativeEditorMonster','version':source['version']}
request('/management/development/definition/copy','POST',copy|{'version':definition['version']},expected=409)
request('/management/development/definition/copy','POST',copy|{'kind':'maps'},expected=400)
request('/management/development/definition/copy','POST',copy|{'name':'../unsafe'},expected=400)
created=json.loads(request('/management/development/definition/copy','POST',copy)[0]);assert created['id']>2 and created['hp']==21
request('/management/development/definition/copy','POST',copy,expected=409)
request('/management/development/definition','PUT',created|{'hp':31})
assert get('/management/development/definition/monsters/1')==source,'Copied monster stats alias the source'
items=get('/management/development/catalog/items');last_id=max(i['id'] for i in items)
item=get('/management/development/definition/items/1')
item_copy=json.loads(request('/management/development/definition/copy','POST',{'kind':'items','sourceId':1,'name':'NativeEditorItem','version':item['version']})[0])
assert item_copy['id']>last_id and item_copy['price']==item['price']
assert get('/management/development/definition/items/1')==item

map_path='/management/development/maps'
original_map=get(map_path+'/2')
request(map_path,'PUT',original_map|{'light':99},expected=400)
request(map_path,'PUT',original_map|{'fileName':'../../outside'},expected=400)
request(map_path,'PUT',original_map|{'fileName':None},expected=400)
request(map_path,'PUT',original_map|{'title':'Edited Return','fileName':'probe','noDropMonster':True})
request(map_path,'PUT',original_map,expected=409)
edited_map=get(map_path+'/2');assert edited_map['title']=='Edited Return' and edited_map['fileName']=='probe' and edited_map['noDropMonster']
request(map_path,'PUT',original_map|{'version':edited_map['version']})
assert get(map_path+'/2')==original_map,'Map update lost other fields or references'

npc_path='/management/development/npcs'
original_npc=get(npc_path+'/1')
for invalid in ({'mapId':99999},{'x':-1},{'x':65535},{'rate':0},{'fileName':'../../runtime/admin.key'},{'fileName':'not-created'}):
    request(npc_path,'PUT',original_npc|invalid,expected=400)
request(npc_path,'PUT',original_npc|{'name':'Edited Shop','x':original_npc['x']+1,'rate':125})
request(npc_path,'PUT',original_npc,expected=409)
edited_npc=get(npc_path+'/1');assert edited_npc['name']=='Edited Shop' and edited_npc['x']==original_npc['x']+1 and edited_npc['rate']==125
request(npc_path,'PUT',original_npc|{'version':edited_npc['version']})
assert get(npc_path+'/1')==original_npc

quest_path='/management/development/quests'
original_quest=get(quest_path+'/2')
for invalid in ({'prerequisite':2},{'prerequisite':99999},{'minLevel':100,'maxLevel':1},{'requiredClass':0},{'requiredClass':32},{'type':99},{'timeLimitSeconds':-1},{'fileName':'../../runtime/admin.key'}):
    request(quest_path,'PUT',original_quest|invalid,expected=400)
request(quest_path,'PUT',original_quest|{'group':'Editor Tests','minLevel':2,'prerequisite':3})
request(quest_path,'PUT',original_quest,expected=409)
third=get(quest_path+'/3')
request(quest_path,'PUT',third|{'prerequisite':2},expected=400)
assert get(quest_path+'/3')==third,'Invalid cycle mutated quest data'
edited_quest=get(quest_path+'/2');assert edited_quest['group']=='Editor Tests' and edited_quest['prerequisite']==3
request(quest_path,'PUT',original_quest|{'version':edited_quest['version']})
assert get(quest_path+'/2')==original_quest
backups=list((runtime/'backups').glob('definitions-*/Server.MirDB'))
assert len(backups)>=10 and all(file.stat().st_size>1024 for file in backups),'World definition edits lost their independent backups'
request('/management/save','POST')
request('/management/stop','POST'); assert not get('/management/status')['ready']
request('/management/start','POST'); assert get('/management/status')['ready']
logs=get('/management/logs'); assert logs
print(f'PASS {checks} management API integration checks: authorization, canonical maps/checksums, staged publication and rollback, previews, safe file edits, five definition editors, stale writes, safe copy/IDs, map/NPC/quest validation and cycles, durable backups, save/stop/start and logs.')
