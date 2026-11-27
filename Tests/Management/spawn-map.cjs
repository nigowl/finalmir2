const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const expect=require('playwright/test').expect.configure({timeout:20000});
const [root,address,server]=process.argv.slice(2),data=path.join(root,'version');
if(!root.includes('/mir2-spawn-map-'))throw new Error('仅允许隔离刷怪地图版本');
const headers={Authorization:'Bearer '+fs.readFileSync(path.join(data,'runtime/admin.key'),'utf8').trim(),'X-Mir2-Version':crypto.createHash('sha256').update(data).digest('hex').toUpperCase()};
const api=async url=>{const r=await fetch(server+url,{headers});assert.equal(r.status,200);return r.json();};
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.MIR2_BROWSER_EXECUTABLE});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[],modules=[];
  page.on('pageerror',e=>errors.push(e.message));page.on('request',r=>{if(r.url().includes('/map-editor.js'))modules.push(r.url());});
  // 预载旧接口到同一页面模块缓存，导航不重载页面，重现升级后仍持有旧脚本。
  await page.route(/\/map-editor\.js$/,r=>r.fulfill({status:200,contentType:'text/javascript',body:'export function attach(svg){svg.dataset.legacyMap="true";} export function detach(){}'}));
  await page.goto(address+'/',{waitUntil:'networkidle'});await page.evaluate(()=>import('/map-editor.js'));await page.unroute(/\/map-editor\.js$/);
  await page.getByRole('link',{name:'刷怪管理',exact:true}).click();
  const map=page.locator('.population-map'),scale=()=>map.evaluate(n=>Number(n.dataset.scale));
  await expect.poll(scale).toBeGreaterThan(0);assert(modules.some(url=>/\/map-editor\.js\?v=/.test(url)),'地图模块没有内容版本，仍会复用旧接口');
  await expect(map).not.toHaveAttribute('data-legacy-map','true');
  const overview=await api('/management/admin/maps/1');assert(overview.spawns.length>0);
  await expect(map.locator('[data-marker]')).toHaveCount(new Set(overview.spawns.map(s=>s.x+','+s.y)).size);
  const grouped=map.locator('[data-marker-count="13"]');await expect(grouped).toHaveCount(1);
  await expect(grouped.locator('text')).toHaveText('13组×91');await expect(grouped.locator('.map-spread')).toHaveCount(1);
  await expect(grouped.locator('.map-spread')).toHaveAttribute('r','4');
  const label=map.locator('.map-spawn-label').first();
  const geometry=()=>label.evaluate(t=>{const m=t.getScreenCTM(),g=t.closest('[data-marker]'),p=/坐标 (\d+),(\d+)/.exec(g.getAttribute('aria-label')),s=t.ownerSVGElement;
   const point=new DOMPoint(Number(p[1])+.5,Number(p[2])+.5).matrixTransform(s.getScreenCTM());
   return {a:m.a,d:m.d,x:m.e-point.x,y:m.f-point.y,size:getComputedStyle(t).fontSize,width:t.getBoundingClientRect().width};});
  let width;
  const check=async()=>{
   await expect.poll(async()=>{const g=await geometry();return Math.abs(g.a-1)<.001&&Math.abs(g.d-1)<.001;}).toBe(true);
   const g=await geometry();assert.equal(g.size,'12px');assert(Math.abs(g.x)<.01&&Math.abs(g.y)<.01,'刷怪标记偏离原格中心');
   if(width!==undefined)assert(Math.abs(g.width-width)<.1,'标记文字随地图缩放变大或缩小');else width=g.width;
   const current=await scale();assert(current>0&&current<=1);
   const box=await map.boundingBox();assert(Math.abs(box.width-overview.width*48*current)<1&&Math.abs(box.height-overview.height*32*current)<1);
  };
  await check();const initial=await scale();
  for(let i=0;i<2;i++){
   await page.getByRole('button',{name:'放大',exact:true}).click();await expect.poll(scale).toBeCloseTo(initial*2,6);await check();
   await page.getByRole('button',{name:'缩小',exact:true}).click();await expect.poll(scale).toBeCloseTo(initial,6);await check();
  }
  await page.getByRole('button',{name:'1:1',exact:true}).click();await expect.poll(scale).toBe(1);await check();
  await expect(page.getByRole('button',{name:'放大',exact:true})).toBeDisabled();
  await expect.poll(()=>map.locator('[data-tile^="0:"][data-loaded]').count()).toBeGreaterThan(0);
  await page.getByRole('button',{name:'缩小',exact:true}).click();await expect.poll(scale).toBe(.5);await check();
  await page.getByRole('button',{name:'全图',exact:true}).click();await expect.poll(scale).toBeCloseTo(initial,6);await check();
  await page.setViewportSize({width:1100,height:900});await expect.poll(scale).toBeGreaterThan(initial);await check();
  await page.setViewportSize({width:1440,height:1000});await expect.poll(scale).toBeCloseTo(initial,6);await check();
  await page.getByRole('checkbox',{name:'显示阻挡',exact:true}).check();await check();
  await grouped.focus();await grouped.press('Enter');
  const chooser=page.getByRole('dialog').filter({has:page.getByRole('heading',{name:'选择刷怪点',exact:true})});
  await expect(chooser.locator('tbody tr')).toHaveCount(10);
  await chooser.getByRole('button',{name:/下一页|Next page/}).click();await expect(chooser.locator('tbody tr')).toHaveCount(3);
  const selected=overview.spawns.filter(s=>s.x===overview.spawns[0].x&&s.y===overview.spawns[0].y).at(-1);
  await chooser.locator('tbody tr').last().getByRole('button',{name:'选择',exact:true}).click();
  const selectedDialog=page.getByRole('dialog').filter({has:page.getByRole('heading',{name:'刷怪点 #'+selected.id,exact:true})});
  await expect(selectedDialog.getByLabel('X',{exact:true})).toHaveValue(String(selected.x));await expect(selectedDialog.getByLabel('Y',{exact:true})).toHaveValue(String(selected.y));
  await selectedDialog.getByRole('button',{name:'取消',exact:true}).click();await check();
  const candidate=await map.locator('[data-marker]').evaluateAll(nodes=>nodes.findIndex(n=>{const b=n.querySelector('text').getBoundingClientRect();return document.elementFromPoint(b.x+b.width/2,b.y+b.height/2)?.closest('[data-marker]')===n;}));
  assert(candidate>=0,'没有可点击的刷怪标记');
  const marker=map.locator('[data-marker]').nth(candidate),id=Number((await marker.getAttribute('data-marker')).split(':')[1]);
  await marker.locator('text').click();
  if(Number(await marker.getAttribute('data-marker-count'))>1)await chooser.locator('tbody tr').first().getByRole('button',{name:'选择',exact:true}).click();
  const dialog=page.getByRole('dialog').filter({has:page.getByRole('heading',{name:'刷怪点 #'+id,exact:true})});
  const spawn=overview.spawns.find(s=>s.id===id);await expect(dialog.getByLabel('X',{exact:true})).toHaveValue(String(spawn.x));await expect(dialog.getByLabel('Y',{exact:true})).toHaveValue(String(spawn.y));
  await dialog.getByRole('button',{name:'取消',exact:true}).click();await check();
  await page.screenshot({path:path.join(root,'spawn-warm-cache-fixed.png'),fullPage:true});
  const definitions=spawns=>spawns.map(({alive,secondsUntilSpawn,...definition})=>definition);
  assert.deepEqual(definitions((await api('/management/admin/maps/1')).spawns),definitions(overview.spawns),'只读检查修改了刷怪配置');
  assert.deepEqual(errors,[]);console.log('PASS 旧模块缓存升级、缩放往返/1:1/窗口变化、标记12px/格中心、重合点合计/范围去重/选择具体配置、真实点击及取消保持；无浏览器错误。');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
