// 只在隔离版本检查系统设置的五职业多选、确认保存、取消及重置草稿。
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { chromium } = require('playwright');
const expect = require('playwright/test').expect.configure({ timeout: 20000 });
const root = process.argv[2], data = process.env.MIR2_DATA_ROOT, runtime = process.env.MIR2_RUNTIME_ROOT;
if (!/mir2-(?:native|admin-ui)\./.test(root) || !path.resolve(data).startsWith(path.resolve(root) + path.sep) ||
    !path.resolve(runtime).startsWith(path.resolve(root) + path.sep)) throw new Error('自动学习检查必须使用隔离版本');
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
    const page = await browser.newPage({ viewport: { width: 1440, height: 1100 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const roles = ['战士', '法师', '道士', '刺客', '弓箭手'];
    const fields = ['autoLearnWarriorSkills', 'autoLearnWizardSkills', 'autoLearnTaoistSkills', 'autoLearnAssassinSkills', 'autoLearnArcherSkills'];
    const chosen = roles.map((_, index) => original.skillOptions.filter(option => option.class === index).slice(0, 2));
    assert(chosen.every(options => options.length === 2), '当前隔离版本必须提供五职业真实技能');
    try {
        await page.goto(admin + '/login');
        const token = await page.locator('input[name="__RequestVerificationToken"]').inputValue();
        assert((await page.request.post(admin + '/auth/login', { form: { key, __RequestVerificationToken: token } })).ok());
        await page.goto(admin + '/settings');
        await page.getByRole('tab', { name: '自动学习', exact: true }).click();
        await expect(page.locator('.settings-summary:visible tbody tr')).toHaveCount(6);
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        const edit = page.getByRole('dialog').filter({ has: page.getByRole('heading', { name: '编辑系统设置', exact: true }) });
        const prompt = () => page.getByRole('dialog').filter({ has: page.locator('.admin-confirm-message') });
        await edit.getByRole('tab', { name: '自动学习', exact: true }).click();
        await edit.getByRole('checkbox', { name: '升级自动学习', exact: true }).check();
        for (let index = 0; index < roles.length; index++) {
            await edit.getByRole('tab', { name: roles[index], exact: true }).click();
            for (const skill of chosen[index]) await edit.getByRole('checkbox', { name: `${skill.name}（${skill.level}级）`, exact: true }).check();
        }
        assert.deepEqual((await api()).values, original.values, '多选草稿提前写入服务端');
        await edit.getByRole('tab', { name: '战士', exact: true }).click();
        await expect(edit.getByRole('checkbox', { name: `${chosen[0][0].name}（${chosen[0][0].level}级）`, exact: true })).toBeVisible();
        await page.screenshot({ path: path.join(root, 'auto-learn-editor.png'), animations: 'disabled' });
        await edit.getByRole('button', { name: '保存', exact: true }).click();
        await prompt().getByRole('button', { name: '保存', exact: true }).click(); await expect(edit).toHaveCount(0);
        const saved = await api(); assert.equal(saved.values.autoLearnSkills, true);
        for (let index = 0; index < roles.length; index++) assert.deepEqual(saved.values[fields[index]], chosen[index].map(skill => skill.spell).sort((a,b) => a-b));
        await page.screenshot({ path: path.join(root, 'auto-learn-summary.png'), animations: 'disabled' });
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await edit.getByRole('tab', { name: '自动学习', exact: true }).click();
        await edit.getByRole('checkbox', { name: '升级自动学习', exact: true }).uncheck();
        await edit.getByRole('button', { name: '取消', exact: true }).click();
        await prompt().getByRole('button', { name: '放弃修改', exact: true }).click();
        assert.deepEqual((await api()).values, saved.values, '取消修改改变自动学习配置');
        await page.getByRole('button', { name: '编辑', exact: true }).click();
        await edit.getByRole('button', { name: '重置', exact: true }).click();
        await prompt().getByRole('button', { name: '重置', exact: true }).click();
        await edit.getByRole('tab', { name: '自动学习', exact: true }).click();
        await expect(edit.getByRole('checkbox', { name: '升级自动学习', exact: true })).not.toBeChecked();
        for (const role of roles) {
            await edit.getByRole('tab', { name: role, exact: true }).click();
            await expect(edit.getByRole('checkbox', { checked: true })).toHaveCount(0);
        }
        assert.deepEqual((await api()).values, saved.values, '重置草稿提前写入服务端');
        await edit.getByRole('button', { name: '取消', exact: true }).click();
        await prompt().getByRole('button', { name: '放弃修改', exact: true }).click();
        assert.deepEqual(errors, []);
        console.log('PASS automatic learning browser: five class tabs and independent multi-select, defaults/summary, confirmed save, cancel/discard and reset draft protection; no browser errors.');
    } finally {
        try { const latest = await api(); await api('PUT', { values: original.values, version: latest.version }); }
        finally { await browser.close(); }
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
