const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const expect = require('playwright/test').expect.configure({ timeout: 20000 });
const root = process.argv[2], data = path.resolve(process.env.MIR2_DATA_ROOT), runtime = path.resolve(process.env.MIR2_RUNTIME_ROOT);
if (!/\/mir2-(heroes|native)\./.test(data) || !runtime.startsWith(data + path.sep)) throw new Error('商城检查仅允许隔离版本');
const server = process.env.MIR2_SERVER, admin = `http://127.0.0.1:${process.env.MIR2_ADMIN_PORT}`;
const key = fs.readFileSync(path.join(runtime, 'admin.key'), 'utf8').trim();
const headers = { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json',
    'X-Mir2-Version': crypto.createHash('sha256').update(data).digest('hex').toUpperCase() };
async function api(suffix = '', method = 'GET', body) {
    const response = await fetch(server + '/management/shop' + suffix, { method, headers, body: body && JSON.stringify(body) });
    assert.equal(response.status, 200, await response.clone().text()); return response.json();
}
(async () => {
    const initial = await api(), items = await api('/items');
    const browser = await chromium.launch({ headless: true, executablePath: process.env.MIR2_BROWSER_EXECUTABLE });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    try {
        await page.goto(admin + '/shop'); await page.waitForLoadState('networkidle');
        await expect(page.getByRole('heading', { name: '商城', exact: true })).toBeVisible();
        const edit = () => page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: '编辑商城', exact: true }) });
        const prompt = () => page.getByRole('dialog').filter({ has: page.locator('.admin-confirm-message') });
        const input = label => edit().getByLabel(label, { exact: true }).filter({ visible: true });
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await input('选项卡名称').fill('取消不会保存'); await input('选项卡名称').press('Tab');
        assert.deepEqual(await api(), initial);
        await edit().getByRole('button', { name: '取消', exact: true }).click();
        await prompt().getByRole('button', { name: '放弃修改', exact: true }).click();
        assert.deepEqual(await api(), initial);
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await input('选项卡名称').fill('常用药品');
        await edit().getByRole('button', { name: '新增选项卡', exact: true }).click();
        await expect(edit().getByRole('tab', { name: '分类1', exact: true })).toHaveAttribute('aria-selected', 'true');
        await input('选项卡名称').fill('临时上架'); await input('选项卡名称').press('Tab');
        const sword = items.find(item => item.details.startsWith('武器'));
        assert(sword, '缺少普通武器商品');
        await input('查找商品名称或编号').fill(String(sword.id));
        await page.locator('.mud-popover-open .mud-list-item').filter({ hasText: `${sword.name} (#${sword.id})` }).first().click();
        await edit().getByRole('button', { name: '添加', exact: true }).filter({ visible: true }).click();
        const row = edit().locator('tbody tr:visible').filter({ hasText: sword.name });
        await row.locator('input').fill('37'); await row.locator('input').press('Tab');
        await edit().getByRole('button', { name: '左移', exact: true }).click();
        await expect(edit().getByRole('tab').nth(4)).toHaveText('临时上架');
        await expect(input('选项卡名称')).toHaveValue('临时上架');
        await edit().getByRole('button', { name: '右移', exact: true }).click();
        await expect(edit().getByRole('tab').nth(5)).toHaveText('临时上架');
        await expect(input('选项卡名称')).toHaveValue('临时上架');
        await page.screenshot({ path: path.join(root, 'shop-editor.png'), animations: 'disabled' });
        await edit().getByRole('button', { name: '保存', exact: true }).click();
        await prompt().getByRole('button', { name: '保存', exact: true }).click(); await expect(edit()).toHaveCount(0);
        const saved = await api(); assert.equal(saved.catalog.tabs[0].name, '常用药品');
        const tab = saved.catalog.tabs.find(tab => tab.name === '临时上架');
        assert(tab && tab.products.some(product => product.itemIndex === sword.id && product.price === 37));
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await expect(input('选项卡名称')).toHaveValue('常用药品');
        await edit().getByRole('tab', { name: '临时上架', exact: true }).click();
        await expect(input('选项卡名称')).toHaveValue('临时上架');
        await edit().getByRole('button', { name: '移除', exact: true }).filter({ visible: true }).click();
        await prompt().getByRole('button', { name: '移除', exact: true }).click();
        await edit().getByRole('button', { name: '删除选项卡', exact: true }).filter({ visible: true }).click();
        await prompt().getByRole('button', { name: '删除', exact: true }).click();
        await edit().getByRole('button', { name: '保存', exact: true }).click();
        await prompt().getByRole('button', { name: '保存', exact: true }).click(); await expect(edit()).toHaveCount(0);
        assert(!(await api()).catalog.tabs.some(tab => tab.name === '临时上架'));
        await page.screenshot({ path: path.join(root, 'shop-page.png') }); assert.deepEqual(errors, []);
        fs.writeFileSync(path.join(root, 'shop-browser-saved.json'), JSON.stringify(await api()));
        console.log('PASS shop browser: draft discard, tab rename/add/delete, item search/add/remove, 37 gold edit, confirmation save; no browser errors.');
    } catch(error) {
        await page.screenshot({ path: path.join(root, 'shop-browser-failure.png') });
        fs.writeFileSync(path.join(root, 'shop-browser-failure.html'), await page.content());
        throw error;
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
