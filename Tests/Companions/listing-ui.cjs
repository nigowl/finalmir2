// 仅检查 listing.py 创建的隔离界面，不修改正式角色。
const fs=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const expect=require('playwright/test').expect.configure({timeout:20000});
const [root,address]=process.argv.slice(2);
(async()=>{
    const browser=await chromium.launch({headless:true,...(process.env.MIR2_BROWSER_EXECUTABLE?{executablePath:process.env.MIR2_BROWSER_EXECUTABLE}:{})});
    try{
        const page=await browser.newPage({viewport:{width:1440,height:1100}});const errors=[];
        page.on('pageerror',e=>errors.push(e.message));
        await page.goto(address+'/companions',{waitUntil:'networkidle'});
        const footer=page.getByText(/第 \d+ 页，共 \d+ 条，每页 10 条/);
        const rows=page.locator('table').first().locator('tbody tr');
        await expect(footer).toHaveText('第 1 页，共 25 条，每页 10 条');await expect(rows).toHaveCount(10);
        await page.getByRole('button',{name:'下一页',exact:true}).click();await expect(footer).toContainText('第 2 页');
        const select=page.locator('.mud-input-control.mud-select').filter({has:page.getByText('状态',{exact:true})});
        const filter=async text=>{await select.locator('div.mud-input-slot[tabindex="0"]').click();await page.locator('.mud-popover-open').getByText(text,{exact:true}).click();};
        await filter('已停用');await expect(footer).toHaveText('第 1 页，共 23 条，每页 10 条');await expect(rows).toHaveCount(10);
        await page.getByRole('button',{name:'下一页',exact:true}).click();await page.getByRole('button',{name:'下一页',exact:true}).click();
        await expect(footer).toHaveText('第 3 页，共 23 条，每页 10 条');await expect(rows).toHaveCount(3);
        await expect(page.getByRole('button',{name:'下一页',exact:true})).toBeDisabled();
        await filter('已暂停');await expect(footer).toHaveText('第 1 页，共 1 条，每页 10 条');await expect(rows).toHaveCount(1);await expect(rows).toContainText('分页测试24');
        await page.getByRole('button',{name:'刷新',exact:true}).click();await expect(footer).toContainText('共 1 条');
        await filter('全部状态');await page.getByLabel('昵称 / 别名 / 标签 / 人格',{exact:true}).fill('needle');
        await page.getByRole('button',{name:'查询',exact:true}).click();await expect(footer).toContainText('共 2 条');await expect(rows).toHaveCount(2);
        await filter('已停用');await expect(footer).toContainText('共 1 条');await expect(rows).toContainText('分页测试00');
        await filter('在线');await expect(footer).toHaveText('第 1 页，共 0 条，每页 10 条');await expect(page.getByText('当前版本没有匹配的陪玩档案。')).toBeVisible();
        await page.screenshot({path:path.join(root,'listing-empty.png'),fullPage:true});
        await page.getByLabel('昵称 / 别名 / 标签 / 人格',{exact:true}).fill('');await filter('全部状态');
        await expect(footer).toContainText('共 25 条');await expect(rows).toHaveCount(10);
        await page.screenshot({path:path.join(root,'listing.png'),fullPage:true});assert.deepEqual(errors,[]);
        console.log('PASS 浏览器10条分页、状态切换回首页、尾页、搜索交集、刷新保留状态和空结果。');
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
