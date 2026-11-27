const assert=require('node:assert/strict');
const expect=require('playwright/test').expect.configure({timeout:20000});
module.exports=async function checkStyle(page){
    const root=page.locator('.admin-compact');
    const buttons=await root.locator('.mud-button').evaluateAll(nodes=>nodes.filter(n=>n.getClientRects().length).map(n=>({
        text:n.innerText,height:n.getBoundingClientRect().height,minHeight:getComputedStyle(n).minHeight,row:!!n.closest('tbody')
    })));
    for(const b of buttons){assert.equal(b.minHeight,b.row?'28px':'32px',b.text);assert(Math.abs(b.height-(b.row?28:32))<1,`${b.text}: 实际高度 ${b.height}`);}
    for(const button of await page.locator('.admin-row-actions .mud-button').all()){
        if(await button.isVisible()){
            await expect(button).toHaveCSS('border-top-width','1px');
            await expect(button).toHaveCSS('border-top-style','solid');
        }
    }
    for(const cell of await page.locator('.admin-row-actions').all()){
        if(!await cell.isVisible())continue;
        const boxes=await cell.locator('.mud-button').evaluateAll(nodes=>nodes.filter(n=>n.getClientRects().length).map(n=>{const b=n.getBoundingClientRect();return {x:b.x,y:b.y,w:b.width,h:b.height};}));
        for(let i=1;i<boxes.length;i++){
            const a=boxes[i-1],b=boxes[i];
            if(Math.abs(a.y-b.y)<1)assert(b.x-a.x-a.w>=5,`操作按钮相贴：${JSON.stringify(boxes)}`);
            else if(b.y>a.y)assert(b.y-a.y-a.h>=5,`换行操作按钮相贴：${JSON.stringify(boxes)}`);
        }
    }
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
