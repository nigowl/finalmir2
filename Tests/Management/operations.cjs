const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const {chromium}=require('playwright');
const expect=require('playwright/test').expect.configure({timeout:30000});
const [root,address,server]=process.argv.slice(2),data=path.join(root,'version');
if(!root.includes('/mir2-prices-'))throw new Error('仅允许隔离运营版本');
const headers={Authorization:'Bearer '+fs.readFileSync(path.join(data,'runtime/admin.key'),'utf8').trim(),'X-Mir2-Version':crypto.createHash('sha256').update(data).digest('hex').toUpperCase(),'Content-Type':'application/json'};
const api=async(url,body)=>{const response=await fetch(server+url,{method:body?'POST':'GET',headers,body:body&&JSON.stringify(body)});assert.equal(response.status,200,await response.clone().text());return response.json();};
(async()=>{
 const browser=await chromium.launch({headless:true,executablePath:process.env.MIR2_BROWSER_EXECUTABLE});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  const dialog=name=>page.locator('.mud-dialog:visible').filter({has:page.getByRole('heading',{name,exact:true})});
  await page.goto(address,{waitUntil:'networkidle'});
  const table=page.locator('.mud-table').filter({has:page.getByText('在线玩家',{exact:true})});
  await expect(table.locator('tbody tr')).toHaveCount(10);await expect(table.locator('.mud-table-pagination')).toContainText('共 12 条');
  await table.getByRole('button',{name:/下一页|Next page/}).click();await expect(table.locator('tbody tr')).toHaveCount(2);
  await page.waitForTimeout(3500);await expect(table.locator('tbody tr')).toHaveCount(2);
  await page.getByRole('button',{name:'保存并重启游戏',exact:true}).click();await expect(dialog('确认运行操作')).toBeVisible();
  await dialog('确认运行操作').getByRole('button',{name:'取消',exact:true}).click();await expect(table.locator('tbody tr')).toHaveCount(2);
  await require('./style.cjs')(page);await page.screenshot({path:path.join(root,'online-players-pagination.png'),fullPage:true});
  const rows=(await api('/management/companions?pageSize=100')).items.slice(0,7);
  await api('/management/companions/selected-command',{action:'stop',profiles:rows.map(r=>({id:r.profile.id,version:r.profile.version}))});
  await expect.poll(async()=> (await api('/management/status')).players.length,{timeout:60000}).toBe(5);
  await expect(table.locator('tbody tr')).toHaveCount(5);await expect(table.locator('.mud-table-pagination')).toContainText('共 5 条');
  for(const route of ['/development','/accounts','/characters','/resources']){
   await page.goto(address+route,{waitUntil:'networkidle'});await require('./style.cjs')(page);
   if(route==='/resources'){
    const field=await page.getByLabel('搜索资源路径',{exact:true}).locator('..').boundingBox();
    const control=await page.getByRole('button',{name:'搜索 / 刷新',exact:true}).boundingBox();
    assert(Math.abs(field.y+field.height/2-control.y-control.height/2)<3,'资源搜索按钮未水平居中');
    await expect(page.locator('a.mud-button').filter({hasText:'下载'}).first()).toHaveCSS('border-top-width','1px');
   }
   await page.screenshot({path:path.join(root,route.slice(1)+'-compact.png'),fullPage:true});
  }
  const character=(await api('/management/admin/characters')).find(c=>c.name==='NativeGM');
  await page.goto(address+'/characters?selected='+character.id,{waitUntil:'networkidle'});const edit=dialog('编辑角色');await expect(edit.getByLabel('金币',{exact:true})).toBeVisible();
  const old=await edit.getByLabel('金币',{exact:true}).inputValue();await edit.getByLabel('金币',{exact:true}).fill('123456');
  await edit.getByRole('button',{name:'取消',exact:true}).click();await dialog('放弃未保存修改').getByRole('button',{name:'放弃修改',exact:true}).click();
  assert.equal(String((await api('/management/admin/characters/'+character.id)).gold),old);
  await page.goto(address+'/population',{waitUntil:'networkidle'});const map=page.locator('.population-map');await expect(map).toBeVisible();
  const overview=await api('/management/admin/maps/1'),initial=Number(await map.getAttribute('data-scale'));
  await page.getByRole('button',{name:'放大',exact:true}).click();await expect.poll(async()=>Number(await map.getAttribute('data-scale'))).toBe(Math.min(1,initial*2));
  await expect.poll(()=>map.locator('[data-map-tiles] image[data-loaded]').count()).toBeGreaterThan(0);
  await page.getByRole('button',{name:'1:1',exact:true}).click();await expect(map).toHaveAttribute('data-scale','1');
  const box=await map.boundingBox();assert(Math.abs(box.width-overview.width*48)<1&&Math.abs(box.height-overview.height*32)<1);
  await expect(page.getByRole('button',{name:'放大',exact:true})).toBeDisabled();
  const point={x:5,y:5};await map.evaluate((node,p)=>node.parentElement.scrollTo(p.x*48,p.y*32),point);
  await expect.poll(()=>map.locator('[data-tile^="0:"][data-loaded]').count()).toBeGreaterThan(0);
  const pixel=await map.evaluate((node,p)=>{const q=new DOMPoint(p.x+.5,p.y+.5).matrixTransform(node.getScreenCTM());return {x:q.x,y:q.y};},point);
  await page.mouse.click(pixel.x,pixel.y);await expect(page.getByLabel('X',{exact:true})).toHaveValue('5');await expect(page.getByLabel('Y',{exact:true})).toHaveValue('5');
  await page.getByRole('button',{name:'取消',exact:true}).click();await page.screenshot({path:path.join(root,'spawn-native-scale.png'),fullPage:true});
  await page.getByRole('button',{name:'全图',exact:true}).click();assert(Number(await map.getAttribute('data-scale'))<=1);
  assert.deepEqual(errors,[]);console.log('PASS 浏览器：真实在线10条分页/刷新/尾页收缩、重启取消、按钮实际32/28px、资源下载边框及对齐、金币取消保护、地图原素材比例/最大1:1/布点坐标。');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
