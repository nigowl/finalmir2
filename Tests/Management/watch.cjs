// GPL-3.0-only；由 watch.py 创建临时组件及隔离管理端。
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{execFileSync}=require('node:child_process');
const {chromium}=require('playwright');
const expect=require('playwright/test').expect.configure({timeout:60000});
const [root,address,route,component,source,css,name]=process.argv.slice(2);
const pid=()=>{
    const lines=execFileSync('ps',['-axo','pid=,command='],{encoding:'utf8'}).split('\n');
    const rows=lines.filter(line=>line.includes('/bin/Debug/net8.0/Mir2.Admin')&&line.includes(root+'/data/runtime:admin'));
    assert.equal(rows.length,1,rows.join('\n'));return rows[0].trim().split(/\s+/)[0];
};
(async()=>{
    const browser=await chromium.launch({headless:true,...(process.env.MIR2_BROWSER_EXECUTABLE?{executablePath:process.env.MIR2_BROWSER_EXECUTABLE}:{})});
    try{
        const page=await browser.newPage({viewport:{width:1280,height:850}});
        await page.goto(address+route);await expect(page.getByRole('heading',{name:'热更新前',exact:true})).toBeVisible();
        await require('./style.cjs')(page);
        const original=pid();
        fs.writeFileSync(component,fs.readFileSync(component,'utf8').replace('热更新前','热更新后'));
        await expect(page.getByRole('heading',{name:'热更新后',exact:true})).toBeVisible();assert.equal(pid(),original);
        fs.writeFileSync(css,'.watch-probe{color:rgb(23,166,99)}\n');
        await expect(page.locator('.watch-probe')).toHaveCSS('color','rgb(23, 166, 99)');assert.equal(pid(),original);
        fs.writeFileSync(source,`namespace Mir2.Admin.Services; public class ${name} { public string Value => ; }\n`);
        await expect.poll(()=>fs.readFileSync(path.join(root,'watch-0.log'),'utf8')).toMatch(/error CS/);
        assert((await fetch(address+'/health')).ok);assert.equal(pid(),original);
        fs.writeFileSync(source,`namespace Mir2.Admin.Services; public struct ${name} { public string Value => "重启后"; }\n`);
        await expect(page.getByText('重启后',{exact:true})).toBeVisible();assert.notEqual(pid(),original);
        await page.screenshot({path:path.join(root,'admin-watch.png'),fullPage:true,animations:'disabled'});
        console.log('PASS Debug Razor 热更新、CSS 自动刷新（PID不变）、编译错误保留旧应用，以及不支持的类型修改自动重启并恢复浏览器。');
    }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
