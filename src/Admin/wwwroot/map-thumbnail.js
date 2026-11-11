// 重新生成覆盖同一份服务端缓存；已打开的地图只在内容校验值变化时换图。
export function watch(image) {
    if (!image) return () => {};
    const controller = new AbortController();
    let revision = '', busy = false;
    const failed = () => { revision = ''; };
    image.addEventListener('error', failed);
    const refresh = async () => {
        if (busy || document.hidden) return;
        busy = true;
        try {
            const response = await fetch(`/map-thumbnail-version/${image.dataset.mapThumbnail}`, { cache: 'no-store', signal: controller.signal });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const current = await response.json();
            if (current.revision !== revision) {
                image.setAttribute('href', `/map-preview/${image.dataset.mapThumbnail}?version=${encodeURIComponent(current.version)}&revision=${current.revision}`);
                revision = current.revision;
            }
            image.removeAttribute('data-thumbnail-error');
        } catch (error) {
            if (error.name !== 'AbortError') image.dataset.thumbnailError = '更新检查失败，保留当前缩略图并自动重试';
        } finally { busy = false; }
    };
    const timer = setInterval(refresh, 5000);
    refresh();
    return () => { clearInterval(timer); controller.abort(); image.removeEventListener('error', failed); };
}
