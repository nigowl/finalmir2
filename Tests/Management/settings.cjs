// 仅对独立测试版本运行，验证重置草稿、确认、取消和版本内保存。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const expect = require('playwright/test').expect.configure({ timeout: 20000 });
const root = process.argv[2];
const data = process.env.MIR2_DATA_ROOT || path.join(root, 'data');
if (!/mir2-(?:native|admin-ui)\./.test(root) || !path.resolve(data).startsWith(path.resolve(root) + path.sep)) throw new Error('系统设置检查必须使用隔离版本');
const runtime = process.env.MIR2_RUNTIME_ROOT || path.join(data, 'runtime');
if (!path.resolve(runtime).startsWith(path.resolve(root) + path.sep)) throw new Error('系统设置检查必须使用隔离运行目录');
const server = process.env.MIR2_SERVER;
const admin = `http://127.0.0.1:${process.env.MIR2_ADMIN_PORT}`;
let headers;
async function api(method = 'GET', body) {
    const response = await fetch(server + '/management/settings', { method, headers, body: body && JSON.stringify(body) });
    assert.equal(response.status, 200, await response.clone().text()); return response.json();
}

(async () => {
    for (const url of [server, admin]) await expect.poll(async () => {
        try { return (await (await fetch(url + '/health')).json()).ready; } catch { return false; }
    }, { timeout: 60000 }).toBe(true);
    const key = fs.readFileSync(path.join(runtime, 'admin.key'), 'utf8').trim();
    headers = { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json',
        'X-Mir2-Version': crypto.createHash('sha256').update(data).digest('hex').toUpperCase() };
    const original = await api();
    const browser = await chromium.launch({ headless: true, ...(process.env.MIR2_BROWSER_EXECUTABLE ? { executablePath: process.env.MIR2_BROWSER_EXECUTABLE } : {}) });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    try {
        await page.goto(admin + '/login');
        const token = await page.locator('input[name="__RequestVerificationToken"]').inputValue();
        assert((await page.request.post(admin + '/auth/login', { form: { key, __RequestVerificationToken: token } })).ok());
        await api('PUT', { values: { ...original.values, maxUser: 73, maxIP: 17, expRate: 2.5, playerPhysicalDamageRate: 1.4,
            allowCreateWarrior: false, allowCreateWizard: false, allowCreateTaoist: false, allowCreateAssassin: false, allowCreateArcher: false }, version: original.version });
        await page.goto(admin + '/settings');
        await expect(page.getByRole('heading', { name: '系统设置', exact: true })).toBeVisible();
        const table = () => page.locator('.settings-summary:visible');
        await expect(table().locator('thead th')).toHaveText(['参数', '当前值', '默认值', '说明']);
        const row = table().locator('tr').filter({ has: page.locator('td').filter({ hasText: /^最大在线连接数$/ }) });
        await expect(row.locator('td')).toHaveText(['最大在线连接数', '73', '50', '登录、选角和游戏内连接的总上限']);
        for (const [title, first] of [['连接与权限', '最大在线连接数'], ['成长与掉落', '经验倍率'], ['战斗平衡', '玩家物理伤害倍率'],
            ['状态效果', '中毒持续伤害倍率'], ['怪物平衡', '怪物生命倍率'], ['存档与保护', '自动存档间隔']]) {
            await page.getByRole('tab', { name: title, exact: true }).click();
            await expect(table().locator('tbody tr').first().locator('td').first()).toHaveText(first);
            await expect(table().locator('thead th').nth(2)).toHaveText('默认值');
            const values = await table().locator('tbody tr').evaluateAll(rows => rows.map(row => row.children[2].textContent.trim()));
            assert(values.length > 0 && values.every(Boolean), `${title}缺少默认值`);
        }
        await page.getByRole('tab', { name: '连接与权限', exact: true }).click();
        await expect(row.locator('td').first()).toHaveText('最大在线连接数');
        await page.screenshot({ path: path.join(root, 'settings-defaults.png') });
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        const edit = page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: '编辑系统设置', exact: true }) });
        const prompt = () => page.getByRole('dialog').filter({ has: page.locator('.admin-confirm-message') });
        const roles = ['战士', '法师', '道士', '刺客', '弓箭手'];
        for (const role of roles) await expect(edit.getByRole('checkbox', { name: `启用${role}职业`, exact: true })).not.toBeChecked();
        const beforeReset = await api();
        await edit.getByRole('button', { name: '重置', exact: true }).click();
        await prompt().getByRole('button', { name: '取消', exact: true }).click();
        await expect(edit.getByLabel('最大在线连接数', { exact: true })).toHaveValue('73');
        await edit.getByRole('button', { name: '重置', exact: true }).click();
        await prompt().getByRole('button', { name: '重置', exact: true }).click();
        await expect(edit.getByLabel('最大在线连接数', { exact: true })).toHaveValue('50');
        for (const role of roles) await expect(edit.getByRole('checkbox', { name: `启用${role}职业`, exact: true })).toBeChecked();
        assert.deepEqual((await api()).values, beforeReset.values, '重置草稿提前写入服务端');
        await page.screenshot({ path: path.join(root, 'settings-reset-draft.png') });
        await edit.getByRole('button', { name: '取消', exact: true }).click();
        await prompt().getByRole('button', { name: '放弃修改', exact: true }).click();
        assert.deepEqual((await api()).values, beforeReset.values, '取消编辑仍保存了默认值');
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await edit.getByRole('button', { name: '重置', exact: true }).click();
        await prompt().getByRole('button', { name: '重置', exact: true }).click();
        await edit.getByRole('button', { name: '保存', exact: true }).click();
        await prompt().getByRole('button', { name: '保存', exact: true }).click();
        await expect(edit).toHaveCount(0);
        const saved = await api();
        assert.equal(saved.values.maxUser, 50); assert.equal(saved.values.maxIP, 5); assert.equal(saved.values.expRate, 1);
        assert.equal(saved.values.playerPhysicalDamageRate, 1); assert.equal(saved.values.magicResistWeight, 10);
        for (const role of ['Warrior', 'Wizard', 'Taoist', 'Assassin', 'Archer']) assert.equal(saved.values['allowCreate' + role], true);
        assert(!/原客户端|原版|原引擎|原服/.test(await page.locator('.admin-page').innerText()));
        assert.deepEqual(errors, []);
        console.log('PASS focused settings browser: default values in six tabs; five class switches; reset cancel/draft/save and discard protection; persisted defaults, concise copy, no browser errors.');
    } finally {
        try { const latest = await api(); await api('PUT', { values: original.values, version: latest.version }); }
        finally { await browser.close(); }
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
