// 只由dispatch.py的独立测试版本运行。
const assert=require('node:assert/strict');
const fs=require('node:fs');const path=require('node:path');const crypto=require('node:crypto');
const {chromium}=require('playwright');const {expect}=require('playwright/test');
const [root,address,server]=process.argv.slice(2);
(async()=>{
 const browser=await chromium.launch({headless:true,...(process.env.MIR2_BROWSER_EXECUTABLE?{executablePath:process.env.MIR2_BROWSER_EXECUTABLE}:{})});
 const page=await browser.newPage({viewport:{width:1440,height:1100}});const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 const key=fs.readFileSync(path.join(root,'version-a/runtime/admin.key'),'utf8').trim();
 const headers={Authorization:'Bearer '+key,'X-Mir2-Version':crypto.createHash('sha256').update(path.join(root,'version-a')).digest('hex').toUpperCase()};
 const settings=async()=>{const response=await fetch(server+'/management/companions/overview',{headers});assert(response.ok);return(await response.json()).settings;};
 const dialog=()=>page.getByRole('dialog').first();
 const unit=async(index,label)=>{
  await dialog().locator('.mud-input-control.mud-select').filter({has:page.getByText('单位',{exact:true})}).nth(index).locator('div.mud-input-slot[tabindex="0"]').click();
  await page.locator('.mud-popover-open').getByText(label,{exact:true}).click();
 };
 const open=async()=>{
  await page.getByRole('button',{name:'陪玩设置',exact:true}).click();
  await expect(async()=>{const tab=dialog().getByRole('tab',{name:'随机新增',exact:true});await tab.click();await expect(tab).toHaveAttribute('aria-selected','true');}).toPass({timeout:20000});
 };
 try{
  await expect.poll(async()=>{try{return(await(await fetch(address+'/health')).json()).ready;}catch{return false;}},{timeout:60000}).toBe(true);
  await page.goto(address+'/login');const token=await page.locator('[name="__RequestVerificationToken"]').inputValue();
  assert((await page.request.post(address+'/auth/login',{form:{key,__RequestVerificationToken:token}})).ok());
  await page.goto(address+'/companions');await expect(page.getByRole('heading',{name:'陪玩管理',exact:true})).toBeVisible();
  const original=await settings();await open();
  await expect(dialog().getByLabel('最短间隔',{exact:true})).toHaveValue('1');
  await expect(dialog().getByLabel('最长间隔',{exact:true})).toHaveValue('1');
  await expect(dialog()).toContainText('固定30天');
  await page.screenshot({path:path.join(root,'dispatch-month.png'),fullPage:true,animations:'disabled'});
  await unit(0,'分钟');await expect(dialog().getByLabel('最短间隔',{exact:true})).toHaveValue('43200');
  await dialog().getByLabel('最短间隔',{exact:true}).fill('30');await dialog().getByLabel('最短间隔',{exact:true}).press('Tab');
  await dialog().getByRole('button',{name:'取消',exact:true}).click();
  await page.getByRole('dialog').last().getByRole('button',{name:'放弃修改',exact:true}).click();
  assert.deepEqual(await settings(),original);
  await open();await unit(0,'分钟');await unit(1,'小时');
  await expect(dialog().getByLabel('最短间隔',{exact:true})).toHaveValue('43200');
  await expect(dialog().getByLabel('最长间隔',{exact:true})).toHaveValue('720');
  await dialog().getByLabel('最短间隔',{exact:true}).fill('30');await dialog().getByLabel('最短间隔',{exact:true}).press('Tab');
  await dialog().getByLabel('最长间隔',{exact:true}).fill('2');await dialog().getByLabel('最长间隔',{exact:true}).press('Tab');
  await expect(dialog().getByLabel('最长间隔',{exact:true})).toHaveValue('2');
  await dialog().getByRole('checkbox',{name:'随机新增陪玩',exact:true}).uncheck();
  await page.screenshot({path:path.join(root,'dispatch-settings.png'),fullPage:true,animations:'disabled'});
  await dialog().getByRole('button',{name:'保存',exact:true}).click();
  await page.getByRole('dialog').filter({has:page.locator('.admin-confirm-message')}).getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  const saved=await settings();assert.equal(saved.arrivals.minMinutes,30);assert.equal(saved.arrivals.maxMinutes,120);assert.equal(saved.arrivals.enabled,false);
  await open();
  await expect(dialog().getByText(/当前计划：随机新增已关闭/)).toBeVisible();
  await page.screenshot({path:path.join(root,'dispatch-overview.png'),fullPage:true,animations:'disabled'});
  assert.deepEqual(errors,[]);
  console.log('PASS Chromium：调度/随机新增/LLM页签、1月显示及单位换算、取消不写入、30分钟–2小时保存、随机新增关闭及状态展示，无页面错误。');
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exit(1);});
