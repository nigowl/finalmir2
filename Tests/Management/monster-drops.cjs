// GPL-3.0-only. 仅由ui.sh针对独立临时版本运行。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const expect = require('playwright/test').expect.configure({ timeout: 20000 });
const root = process.argv[2], data = path.join(root, 'data');
const server = process.env.MIR2_SERVER, admin = `http://127.0.0.1:${process.env.MIR2_ADMIN_PORT}`;

(async () => {
    for (const host of [server, admin]) await expect.poll(async () => {
        try { return (await (await fetch(host + '/health')).json()).ready; } catch { return false; }
    }, { timeout: 120000 }).toBe(true);
    const key = fs.readFileSync(path.join(data, 'runtime/admin.key'), 'utf8').trim();
    const headers = { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json',
        'X-Mir2-Version': crypto.createHash('sha256').update(data).digest('hex').toUpperCase() };
    const request = (url, method = 'GET', value, override = headers) => fetch(server + url, { method, headers: override, body: value && JSON.stringify(value) });
    const api = async (url, method = 'GET', value) => {
        const response = await request(url, method, value);
        assert.equal(response.status, 200, await response.clone().text()); return response.json();
    };
    const browser = await chromium.launch({ headless: true, ...(process.env.MIR2_BROWSER_EXECUTABLE ? { executablePath: process.env.MIR2_BROWSER_EXECUTABLE } : {}) });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; page.on('pageerror', e => errors.push(e.message));
    try {
        // 模拟用户浏览器仍持有旧的无版本样式；新页面必须请求带内容版本的URL。
        await page.route('**/app.css', route => route.fulfill({ contentType: 'text/css', body: '.monster-catalog-entry{display:block}' }));
        await page.goto(admin + '/login');
        assert.match(await page.locator('link[rel="stylesheet"]').evaluateAll(links => links.map(link => link.getAttribute('href')).find(href => href.startsWith('/app.css'))), /^\/app\.css\?v=.+/);
        const token = await page.locator('input[name="__RequestVerificationToken"]').inputValue();
        assert((await page.request.post(admin + '/auth/login', { form: { key, __RequestVerificationToken: token } })).ok());
        await page.goto(admin + '/population');
        await page.getByRole('button', { name: '怪物列表', exact: true }).click();
        const gallery = page.getByRole('dialog').first();
        await expect(gallery.locator('.monster-catalog-card')).toHaveCount(50);
        assert.equal(await gallery.locator('thead').count(), 0);
        await expect.poll(() => gallery.locator('.monster-catalog-card').evaluateAll(cards => new Set(cards.map(card => Math.round(card.getBoundingClientRect().x))).size)).toBeGreaterThan(1);
        const card = gallery.getByRole('button', { name: '#2 NativeDanger', exact: true });
        await expect.poll(() => card.locator('img').evaluate(img => img.complete && img.naturalWidth > 0)).toBe(true);
        assert((await card.locator('.monster-catalog-name').boundingBox()).y >= (await card.locator('img').boundingBox()).y + (await card.locator('img').boundingBox()).height);
        const cardBox = await card.boundingBox(), portrait = await card.locator('.monster-catalog-portrait').boundingBox(), details = await card.locator('.monster-catalog-details').boundingBox();
        assert(cardBox.height > cardBox.width && details.y >= portrait.y + portrait.height, '怪物卡片应为上图下名称和属性的纵向长条');
        await expect(card.locator('[data-stat="生命"]')).toContainText('9999');
        await expect(card.locator('[data-stat="攻击"]')).toContainText('9999');
        const pagination = gallery.locator('.mud-pagination');
        const pager = await pagination.boundingBox(), galleryRect = await gallery.boundingBox();
        assert(Math.abs(pager.x + pager.width/2 - galleryRect.x - galleryRect.width/2) <= 2, '分页没有居中');
        await page.screenshot({ path: path.join(root, 'monster-gallery.png') });
        await card.click();
        let dialog = page.getByRole('dialog').filter({ has: page.getByLabel('怪物名称', { exact: true }) });
        await expect(dialog.getByLabel('生命', { exact: true })).toHaveValue('9999');
        const definition = await api('/management/admin/monsters/2');
        const originalDrop = await api('/management/admin/monsters/2/drops');
        await dialog.getByLabel('怪物名称', { exact: true }).fill('NativeDangerEdited');
        await dialog.getByLabel('生命', { exact: true }).fill('10001');
        await dialog.getByRole('checkbox', { name: '首领', exact: true }).check();
        const confirmation = () => page.getByRole('dialog').filter({ has: page.locator('.admin-confirm-message') });
        await dialog.getByRole('button', { name: '保存', exact: true }).click();
        await expect(confirmation().getByText(/正常重启游戏引擎/)).toBeVisible();
        await confirmation().getByRole('button', { name: '取消', exact: true }).click();
        assert.deepEqual(await api('/management/admin/monsters/2'), definition);
        await page.screenshot({ path: path.join(root, 'monster-properties.png') });
        await dialog.getByRole('button', { name: '保存', exact: true }).click();
        await confirmation().getByRole('button', { name: '保存', exact: true }).click();
        await expect(page.getByRole('dialog')).toHaveCount(1);
        const edited = await api('/management/admin/monsters/2');
        assert.equal(edited.name, 'NativeDangerEdited'); assert.equal(edited.stats.values.HP, 10001); assert(edited.isBoss);
        assert.equal((await api('/management/admin/monsters/2/drops')).script, originalDrop.script);
        assert.equal((await request('/management/admin/monsters/2', 'PUT', definition)).status, 409);
        const invalid = { ...edited, stats: { values: { ...edited.stats.values, HP: -1 } } };
        assert.equal((await request('/management/admin/monsters/2', 'PUT', invalid)).status, 400);
        await gallery.getByRole('button', { name: '#2 NativeDangerEdited', exact: true }).click();
        dialog = page.getByRole('dialog').filter({ has: page.getByLabel('怪物名称', { exact: true }) });
        // 先等打开弹窗的缩放动画结束，再单独测量页签切换。
        await expect.poll(() => dialog.evaluate(e => Math.abs(e.getBoundingClientRect().width-e.offsetWidth))).toBeLessThan(1);
        const beforeTab = await dialog.boundingBox();
        await page.evaluate(() => {
            window.monsterFrames = []; window.watchMonster = true;
            const el = document.querySelector('.monster-editor').closest('.mud-dialog');
            const frame = () => { if (!window.watchMonster) return; const r = el.getBoundingClientRect(); window.monsterFrames.push([r.y,r.height]); requestAnimationFrame(frame); }; frame();
        });
        await dialog.getByRole('tab', { name: '掉落', exact: true }).click();
        const editor = dialog.getByLabel('怪物自身规则', { exact: true });
        const final = dialog.getByLabel('最终掉落规则', { exact: true });
        await expect(editor).toBeVisible();
        const frames = await page.evaluate(() => { window.watchMonster = false; return window.monsterFrames; });
        const afterTab = await dialog.boundingBox();
        assert(Math.abs(beforeTab.height-afterTab.height) <= 2 && Math.abs(beforeTab.y-afterTab.y) <= 2, '页签切换改变弹窗高度或位置 ' + JSON.stringify({beforeTab,afterTab,frames:frames.slice(0,8),layout:await dialog.locator('.monster-editor').evaluate(e=>({parent:e.parentElement.outerHTML.slice(0,180),height:getComputedStyle(e.parentElement).height,maxHeight:getComputedStyle(e.parentElement).maxHeight,own:getComputedStyle(e).height}))}));
        assert(Math.max(...frames.map(v=>v[1])) - Math.min(...frames.map(v=>v[1])) <= 2, '异步加载时弹窗高度跳变');
        assert.equal(await dialog.getByText('掉落方式' , { exact: true }).count(), 0);
        assert.equal(await dialog.getByText(/关联怪物：/).count(), 0);
        let original = await api('/management/admin/monsters/2/drops');
        const item = (await api('/management/development/catalog/items'))[0].name;
        for (const [name, rule] of [['测试掉落', `10% ${item}\n`], ['测试掉落二', `20% ${item} 2\n`]]) {
            const file = await api('/management/development/file?path=' + encodeURIComponent(`Envir/Drops/公共/怪物/测试分类/${name}.txt`));
            await api('/management/development/file', 'PUT', { path: file.path, content: rule, version: file.version });
        }
        // 重开以刷新可选文件列表；读取不得把新公共文件加入当前怪物。
        await dialog.getByRole('button', { name: '关闭弹窗', exact: true }).click();
        await gallery.getByRole('button', { name: '#2 NativeDangerEdited', exact: true }).click();
        dialog = page.getByRole('dialog').filter({ has: page.getByLabel('怪物名称', { exact: true }) });
        await dialog.getByRole('tab', { name: '掉落', exact: true }).click();
        await expect(editor).toBeVisible();
        original = await api('/management/admin/monsters/2/drops');
        while (await dialog.getByRole('button', { name: /^移除脚本 / }).count()) {
            const buttons = dialog.getByRole('button', { name: /^移除脚本 / }); const count = await buttons.count();
            await buttons.first().click(); await expect(buttons).toHaveCount(count - 1);
        }
        await dialog.locator('.mud-select').filter({ has: page.getByText('脚本目录', { exact: true }) }).locator('.mud-input-slot[tabindex="0"]').click();
        await page.locator('.mud-popover-open').getByText('公共/怪物/测试分类', { exact: true }).click();
        const picker = dialog.getByLabel('添加公共脚本', { exact: true });
        for (const name of ['测试掉落', '测试掉落二']) {
            await picker.fill('测试分类 ' + name);
            await page.locator('.mud-popover-open').getByText('公共/怪物/测试分类/' + name, { exact: true }).click();
            await expect(dialog.getByRole('button', { name: '移除脚本 公共/怪物/测试分类/' + name, exact: true })).toBeVisible();
        }
        const save = async () => {
            await expect(dialog.getByRole('button', { name: '保存', exact: true })).toBeEnabled();
            await dialog.getByRole('button', { name: '保存', exact: true }).click();
            await confirmation().getByRole('button', { name: '保存', exact: true }).click();
        };
        await editor.fill('1/1 不存在的测试物品 2');
        await expect(dialog.getByText(/第\d+行/)).toBeVisible();
        await expect(dialog.getByRole('button', { name: '保存', exact: true })).toBeDisabled();
        assert.deepEqual(await api('/management/admin/monsters/2/drops'), original);
        const rule = `; 当前怪物增补\n5% ${item} 3\n1/10 Gold 100\n`;
        await editor.fill(rule);
        await expect.poll(() => final.inputValue()).toContain(`35.000% ${item} 6`);
        await dialog.getByRole('button', { name: '关闭弹窗', exact: true }).click();
        await expect(confirmation().getByText('关闭将放弃当前未保存的修改。')).toBeVisible();
        await confirmation().getByRole('button', { name: '取消', exact: true }).click();
        await expect(editor).toHaveValue(rule);
        await save(); await expect(dialog.getByText('掉落规则已保存，即时生效。')).toBeVisible();
        const saved = await api('/management/admin/monsters/2/drops');
        assert.equal(saved.selectedScripts.length, 2); assert.equal(saved.content, rule); assert.equal(saved.loadedRules, 2);
        assert(saved.finalContent.includes(`35.000% ${item} 6`));
        assert.equal((await request('/management/admin/monsters/2/drops', 'PUT', { scripts: [], content: rule, version: original.version })).status, 409);
        assert.equal((await request('/management/admin/monsters/2/drops/preview', 'POST', { scripts: ['../escape'], content: '', version: saved.version })).status, 400);
        assert.equal((await request('/management/admin/monsters/2/drops', 'GET', null, { Authorization: `Bearer ${key}` })).status, 409);
        const own = fs.readFileSync(path.join(data, 'world/Envir/Drops/怪物规则/怪物-2.txt'), 'utf8');
        assert(own.includes('#INSERT [公共/怪物/测试分类/测试掉落.txt]')); assert(own.endsWith(rule));
        await page.screenshot({ path: path.join(root, 'monster-drops.png') });
        await api('/management/restart', 'POST');
        await expect.poll(async () => { try { return (await api('/management/status')).ready; } catch { return false; } }).toBe(true);
        const restarted = await api('/management/admin/monsters/2/drops');
        assert.equal(restarted.content, rule); assert.equal(restarted.loadedRules, 2); assert.deepEqual(restarted.selectedScripts, saved.selectedScripts);
        assert.equal(restarted.finalContent, saved.finalContent);
        const text = await api('/management/development/file?path=' + encodeURIComponent('Envir/Drops/公共/怪物/测试分类/测试掉落.txt'));
        await api('/management/development/file', 'PUT', { path: text.path, content: text.content + '\n;冲突测试\n', version: text.version });
        assert.equal((await request('/management/admin/monsters/2/drops', 'PUT', { scripts: saved.selectedScripts, content: rule, version: saved.version, previewVersion: saved.previewVersion })).status, 409);
        assert.deepEqual(errors, []);
        console.log('PASS 卡片瀑布流/素材/属性、公共目录/多关键词/多选移除、自身规则编辑/最终合并预览、35.000%预览与数量6实际加载、非法规则/关闭保护/确认保存、版本/越界/依赖冲突及重启恢复。');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
