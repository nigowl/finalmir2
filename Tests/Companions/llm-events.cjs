const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');const expect=require('playwright/test').expect.configure({timeout:20000});
const [root,address,server]=process.argv.slice(2);
(async()=>{
  const browser=await chromium.launch({headless:true,...(process.env.MIR2_BROWSER_EXECUTABLE?{executablePath:process.env.MIR2_BROWSER_EXECUTABLE}:{})});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1100}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await expect.poll(async()=>{try{return(await(await fetch(address+'/health')).json()).ready;}catch{return false;}}).toBe(true);
    await page.goto(address+'/settings',{waitUntil:'networkidle'});
    const row=name=>page.getByRole('row').filter({has:page.getByRole('cell',{name,exact:true})});
    for(const name of ['同 IP 每小时注册次数','同 IP 每小时建角次数'])await expect(row(name)).toContainText('不限');
    await page.getByRole('button',{name:'编辑',exact:true}).click();let dialog=page.getByRole('dialog').first();
    await dialog.getByLabel('同 IP 每小时注册次数',{exact:true}).fill('6');await dialog.getByLabel('同 IP 每小时建角次数',{exact:true}).fill('8');
    await dialog.getByRole('button',{name:'取消',exact:true}).click();await page.getByRole('dialog').last().getByRole('button',{name:'放弃修改',exact:true}).click();
    await expect(row('同 IP 每小时注册次数')).toContainText('不限');
    await page.getByRole('button',{name:'编辑',exact:true}).click();dialog=page.getByRole('dialog').first();
    await dialog.getByLabel('同 IP 每小时注册次数',{exact:true}).fill('6');await dialog.getByLabel('同 IP 每小时建角次数',{exact:true}).fill('8');
    await dialog.getByRole('button',{name:'保存',exact:true}).click();await page.getByRole('dialog').filter({has:page.locator('.admin-confirm-message')}).getByRole('button',{name:'保存',exact:true}).click();await expect(dialog).toHaveCount(0);
    await expect(row('同 IP 每小时注册次数').locator('.metric-value').first()).toHaveText('6');
    await page.goto(address+'/companions',{waitUntil:'networkidle'});
    await expect(page.getByRole('link',{name:'商城管理',exact:true})).toHaveAttribute('href','/shop');
    const companion=page.getByRole('row').filter({hasText:'日志验证5'});await companion.getByRole('button',{name:'详情',exact:true}).click();
    dialog=page.getByRole('dialog').first();const events=dialog.locator('[aria-label="最近运行事件"]');
    await expect(events.getByRole('cell',{name:'LLM日志',exact:true}).first()).toBeVisible();
    const result=events.getByRole('row').filter({hasText:'已返回'});await expect(result.first()).toContainText('日志模型 / fixture-model');await expect(result.first()).toContainText('原TCP模型问答已收到');
    await expect(events.getByRole('row').filter({hasText:'已采用'}).first()).toContainText('原TCP');
    await events.scrollIntoViewIfNeeded();await page.screenshot({path:path.join(root,'llm-events.png'),fullPage:true});
    assert.deepEqual(errors,[]);console.log('PASS 浏览器：系统设置两项频率/0不限/取消与确认保存、角色详情LLM模型/耗时/结果、商城管理导航。');
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
