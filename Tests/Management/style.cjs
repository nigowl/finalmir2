const assert=require('node:assert/strict');
const expect=require('playwright/test').expect.configure({timeout:20000});
module.exports=async function checkStyle(page){
    const root=page.locator('.admin-compact');
    const buttons=await root.locator('.mud-button').evaluateAll(nodes=>nodes.filter(n=>n.getClientRects().length).map(n=>({
        text:n.innerText,minHeight:getComputedStyle(n).minHeight,row:!!n.closest('tbody')
    })));
    for(const b of buttons)assert.equal(b.minHeight,b.row?'28px':'32px',b.text);
    for(const table of await root.locator('.mud-table').all()){
        if(await table.isVisible()){await expect(table).toHaveCSS('border-top-width','0px');await expect(table).toHaveCSS('border-radius','0px');}
    }
    for(const tabs of await page.locator('.admin-compact .mud-tabs,.admin-dialog .mud-tabs').all()){
        if(!await tabs.isVisible())continue;
        await expect(tabs.locator(':scope>.mud-tabs-tabbar .mud-tabs-tabbar-inner').first()).toHaveCSS('min-height','0px');
        await expect(tabs.locator(':scope>.mud-tabs-panels')).toHaveCSS('border-top-width','0px');
        const sizes=await tabs.evaluate(node=>{
            const bar=node.querySelector(':scope>.mud-tabs-tabbar');
            const tabs=[...bar.querySelectorAll('[role="tab"]')].filter(t=>t.getClientRects().length);
            return {width:bar.getBoundingClientRect().width,tabs:tabs.map(t=>t.getBoundingClientRect().width)};
        });
        if(sizes.tabs.length){assert(Math.max(...sizes.tabs)-Math.min(...sizes.tabs)<2,JSON.stringify(sizes));assert(Math.abs(sizes.tabs.reduce((a,b)=>a+b,0)-sizes.width)<4,JSON.stringify(sizes));}
    }
};
