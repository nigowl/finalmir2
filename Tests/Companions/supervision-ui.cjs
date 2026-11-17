// 只在隔离版本验证智能托管开关与保存、取消保护。
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {chromium}=require('playwright');
const expect=require('playwright/test').expect.configure({timeout:20000});
const [root,admin,server]=process.argv.slice(2),data=path.join(root,'version');
(async()=>{
 const browser=await chromium.launch({headless:true,...(process.env.MIR2_BROWSER_EXECUTABLE?{executablePath:process.env.MIR2_BROWSER_EXECUTABLE}:{})});
 const page=await browser.newPage({viewport:{width:1440,height:1050}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 const key=fs.readFileSync(path.join(data,'runtime/admin.key'),'utf8').trim();
 const headers={Authorization:'Bearer '+key,'X-Mir2-Version':crypto.createHash('sha256').update(data).digest('hex').toUpperCase()};
 const setting=async()=>{const r=await fetch(server+'/management/companions/overview',{headers});assert(r.ok);return(await r.json()).settings;};
 const dialog=()=>page.getByRole('dialog').first(),prompt=()=>page.getByRole('dialog').filter({has:page.locator('.admin-confirm-message')});
 const open=async()=>{await page.getByRole('button',{name:'陪玩设置',exact:true}).click();await expect(dialog().getByRole('tab',{name:'在线调度',exact:true})).toHaveAttribute('aria-selected','true');};
 const save=async()=>{await dialog().getByRole('button',{name:'保存',exact:true}).click();await prompt().getByRole('button',{name:'保存',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);};
 try{
  for(const host of [server,admin])await expect.poll(async()=>{try{return(await(await fetch(host+'/health')).json()).ready;}catch{return false;}},{timeout:90000}).toBe(true);
  await page.goto(admin+'/login');const token=await page.locator('[name="__RequestVerificationToken"]').inputValue();
  assert((await page.request.post(admin+'/auth/login',{form:{key,__RequestVerificationToken:token}})).ok());await page.goto(admin+'/companions');
  const original=await setting();await open();const toggle=()=>dialog().getByRole('checkbox',{name:'智能托管',exact:true});
  await expect(toggle()).toBeChecked({checked:original.smartRecovery});await toggle().setChecked(!original.smartRecovery);
  await dialog().getByRole('button',{name:'关闭弹窗',exact:true}).click();await prompt().getByRole('button',{name:'取消',exact:true}).click();
  await expect(toggle()).toBeChecked({checked:!original.smartRecovery});assert.equal((await setting()).smartRecovery,original.smartRecovery);
  await dialog().getByRole('button',{name:'关闭弹窗',exact:true}).click();await prompt().getByRole('button',{name:'放弃修改',exact:true}).click();
  await open();await toggle().setChecked(!original.smartRecovery);await save();assert.equal((await setting()).smartRecovery,!original.smartRecovery);
  await open();await toggle().setChecked(original.smartRecovery);await page.screenshot({path:path.join(root,'supervision-settings.png')});await save();
  assert.equal((await setting()).smartRecovery,original.smartRecovery);assert.deepEqual(errors,[]);
  console.log('PASS 智能托管位于在线调度、草稿不写入、关闭确认取消/放弃、二次确认保存开关和服务端持久设置。');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
