// 开发 watcher 重启后旧 Blazor 会话无法复用，恢复连接被拒绝时重新加载页面。
(() => {
    let generation = 0;
    const showError = visible => {
        const element = document.getElementById('blazor-error-ui');
        if (element) element.style.display = visible ? 'block' : 'none';
    };
    async function recover(current) {
        let lastError;
        for (let retry = 0; retry < 30 && current === generation; retry++) {
            await new Promise(resolve => setTimeout(resolve, 1000));
            if (current !== generation) return;
            try {
                const connected = await Blazor.reconnect();
                if (current !== generation) return;
                if (!connected) location.reload();
                return;
            } catch (error) { lastError = error; }
        }
        if (current === generation) {
            console.warn('开发管理端重连失败，请检查编译结果并刷新页面。', lastError);
            showError(true);
        }
    }
    Blazor.start({ circuit: { reconnectionHandler: {
        onConnectionDown: () => { void recover(++generation); },
        onConnectionUp: () => { generation++; showError(false); }
    } } }).catch(error => { console.error('开发管理端启动失败。', error); showError(true); });
})();
