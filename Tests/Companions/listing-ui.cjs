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
        await expect(page.getByRole('link',{name:'商城管理',exact:true})).toHaveAttribute('href','/shop');
        await expect(page.getByText(/注册与建角频率由系统设置控制/)).toBeVisible();
        const footer=page.getByText(/第 \d+ 页，共 \d+ 条，每页 \d+ 条/);
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
        await page.getByRole('checkbox',{name:'选择分页测试00',exact:true}).check();
        await page.getByRole('button',{name:'下一页',exact:true}).click();
        const sizes=page.locator('.mud-input-control.mud-select').filter({has:page.getByText('每页条数',{exact:true})});
        const setSize=async size=>{await sizes.locator('div.mud-input-slot[tabindex="0"]').click();await page.locator('.mud-popover-open').getByText(String(size),{exact:true}).click();};
        for(const size of [20,30,50,100]){
            await setSize(size);await expect(footer).toHaveText(`第 1 页，共 25 条，每页 ${size} 条`);await expect(rows).toHaveCount(Math.min(size,25));
            await expect(page.getByRole('checkbox',{name:'选择分页测试00',exact:true})).toBeChecked();
        }
        await page.getByRole('button',{name:'刷新',exact:true}).click();await expect(footer).toHaveText('第 1 页，共 25 条，每页 100 条');
        await filter('已停用');await expect(footer).toHaveText('第 1 页，共 23 条，每页 100 条');await expect(rows).toHaveCount(23);
        await filter('全部状态');await setSize(20);await page.getByRole('button',{name:'下一页',exact:true}).click();
        await expect(footer).toHaveText('第 2 页，共 25 条，每页 20 条');await expect(rows).toHaveCount(5);
        await setSize(10);await expect(footer).toHaveText('第 1 页，共 25 条，每页 10 条');await expect(rows).toHaveCount(10);
        await require('../Management/style.cjs')(page);
        await page.screenshot({path:path.join(root,'listing.png'),fullPage:true});assert.deepEqual(errors,[]);
        console.log('PASS 浏览器10/20/30/50/100条分页、切换回首页/保留筛选与跨页勾选、状态切换回首页、尾页、搜索交集、刷新保留状态和空结果。');
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
