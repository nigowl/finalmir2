// 仅在隔离版本验证公共商店倍率的默认值、取消及确认保存。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const expect = require('playwright/test').expect.configure({ timeout: 20000 });
const root = process.argv[2], data = process.env.MIR2_DATA_ROOT, runtime = process.env.MIR2_RUNTIME_ROOT;
if (!root.includes('mir2-native.') || !path.resolve(data).startsWith(path.resolve(root) + path.sep) ||
    !path.resolve(runtime).startsWith(path.resolve(root) + path.sep)) throw new Error('只允许隔离公共商店版本');
const server = process.env.MIR2_SERVER, admin = `http://127.0.0.1:${process.env.MIR2_ADMIN_PORT}`;
const key = fs.readFileSync(path.join(runtime, 'admin.key'), 'utf8').trim();
const headers = { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json',
    'X-Mir2-Version': crypto.createHash('sha256').update(path.resolve(data)).digest('hex').toUpperCase() };
async function api(method = 'GET', body) {
    const response = await fetch(server + '/management/settings', { method, headers, body: body && JSON.stringify(body) });
    assert.equal(response.status, 200, await response.clone().text()); return response.json();
}
(async () => {
    const original = await api();
    const browser = await chromium.launch({ headless: true, executablePath: process.env.MIR2_BROWSER_EXECUTABLE });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    try {
        await page.goto(admin + '/login');
        const token = await page.locator('input[name="__RequestVerificationToken"]').inputValue();
        assert((await page.request.post(admin + '/auth/login', { form: { key, __RequestVerificationToken: token } })).ok());
        await page.goto(admin + '/settings');
        await page.getByRole('tab', { name: '成长与掉落', exact: true }).click();
        const summary = () => page.locator('.settings-summary:visible tr').filter({ hasText: '公共商店价格倍率' });
        await expect(summary().locator('td').nth(2)).toHaveText('1.5 倍');
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        const edit = page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: '编辑系统设置', exact: true }) });
        const prompt = () => page.getByRole('dialog').filter({ has: page.locator('.admin-confirm-message') });
        await edit.getByRole('tab', { name: '成长与掉落', exact: true }).click();
        const rate = edit.getByLabel('公共商店价格倍率', { exact: true });
        await rate.fill('1.8'); await rate.press('Tab');
        assert.equal((await api()).values.publicShopPriceRate, original.values.publicShopPriceRate);
        await edit.getByRole('button', { name: '保存', exact: true }).click();
        await prompt().getByRole('button', { name: '保存', exact: true }).click(); await expect(edit).toHaveCount(0);
        assert.equal((await api()).values.publicShopPriceRate, 1.8);
        await expect(summary().locator('td').nth(1)).toHaveText('1.8 倍');
        await page.screenshot({ path: path.join(root, 'public-shop-settings.png') });
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await edit.getByRole('tab', { name: '成长与掉落', exact: true }).click(); await rate.fill('2'); await rate.press('Tab');
        await edit.getByRole('button', { name: '取消', exact: true }).click();
        await prompt().getByRole('button', { name: '放弃修改', exact: true }).click();
        assert.equal((await api()).values.publicShopPriceRate, 1.8); assert.deepEqual(errors, []);
        console.log('PASS public shop settings browser: 1.5 default, draft isolation, confirmed 1.8 save and discard; no browser errors.');
    } finally {
        try { const latest = await api(); await api('PUT', { values: original.values, version: latest.version }); }
        finally { await browser.close(); }
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
