// Screen coordinates are transformed through the SVG matrix, including zoom and scroll.
import { watch } from './map-thumbnail.js';
export function attach(svg, receiver, error, id, version, width, height) {
    if (svg.mir2Version === version) return;
    detach(svg);
    svg.mir2Version = version;
    const scroll = svg.parentElement, tiles = svg.querySelector('[data-map-tiles]');
    let scale = 1, minimum = 1, fit = true, frame = 0, revision = 0;
    const updateError = () => { error.hidden = !tiles.querySelector('[data-failed]'); };
    const notify = () => receiver.invokeMethodAsync('ScaleChanged', scale, minimum);
    const render = () => {
        frame = 0;
        // 全图沿用缩略图；放大时按显示比例重新绘制原素材块，不放大低分辨率图片。
        if (scale <= 1024 / Math.max(width * 48, height * 32)) { tiles.replaceChildren(); updateError(); return; }
        const level = Math.max(0, Math.min(6, Math.floor(-Math.log2(scale))));
        const tileScale = 1 / (1 << level), size = 1024 / tileScale;
        const left = scroll.scrollLeft / scale, top = scroll.scrollTop / scale;
        const visible = new Set();
        for (let y = Math.floor(top / size); y * size < Math.min(height * 32, top + scroll.clientHeight / scale); y++) {
            for (let x = Math.floor(left / size); x * size < Math.min(width * 48, left + scroll.clientWidth / scale); x++) {
                const key = `${level}:${x}:${y}`; visible.add(key);
                if (tiles.querySelector(`[data-tile="${key}"]`)) continue;
                const image = document.createElementNS('http://www.w3.org/2000/svg', 'image');
                image.dataset.tile = key;
                image.setAttribute('x', x * size / 48); image.setAttribute('y', y * size / 32);
                image.setAttribute('width', size / 48); image.setAttribute('height', size / 32);
                image.setAttribute('preserveAspectRatio', 'none');
                image.onerror = () => { image.dataset.failed = 'true'; updateError(); };
                image.onload = () => { image.dataset.loaded = 'true'; delete image.dataset.failed; updateError(); };
                image.setAttribute('href', `/native-map-tile?id=${id}&x=${x}&y=${y}&level=${level}&version=${encodeURIComponent(version)}&revision=${revision}`);
                tiles.append(image);
            }
        }
        for (const image of [...tiles.children]) if (!visible.has(image.dataset.tile)) image.remove();
        updateError();
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(render); };
    const apply = (value, centered) => {
        const x = (scroll.scrollLeft + scroll.clientWidth / 2) / scale;
        const y = (scroll.scrollTop + scroll.clientHeight / 2) / scale;
        scale = Math.max(minimum, Math.min(1, value));
        svg.style.width = `${width * 48 * scale}px`;
        svg.dataset.scale = scale;
        if (centered) scroll.scrollTo(Math.max(0, x * scale - scroll.clientWidth / 2), Math.max(0, y * scale - scroll.clientHeight / 2));
        else scroll.scrollTo(0, 0);
        notify(); schedule();
    };
    svg.mir2Zoom = factor => { fit = factor === 0; apply(fit ? minimum : factor === 1 ? 1 : scale * factor, !fit); };
    const resize = new ResizeObserver(() => {
        minimum = Math.min(1, scroll.clientWidth / (width * 48));
        apply(fit ? minimum : scale, !fit);
    });
    const retry = () => { revision++; tiles.replaceChildren(); render(); };
    error.querySelector('button').addEventListener('click', retry);
    scroll.addEventListener('scroll', schedule, { passive: true }); resize.observe(scroll);
    svg.mir2Detach = () => {
        cancelAnimationFrame(frame); resize.disconnect(); scroll.removeEventListener('scroll', schedule);
        error.querySelector('button').removeEventListener('click', retry);
        tiles.replaceChildren(); delete svg.mir2Zoom; delete svg.mir2Version;
    };
    svg.mir2Pick = event => {
        if (event.button !== 0 || svg.dataset.disabled === "true" || event.target.closest("[data-marker]")) return;
        const matrix = svg.getScreenCTM();
        if (!matrix) return;
        const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
        receiver.invokeMethodAsync("Pick", Math.floor(point.x), Math.floor(point.y));
    };
    svg.addEventListener("pointerdown", svg.mir2Pick);
    svg.mir2Thumbnail = watch(svg.querySelector('[data-map-thumbnail]'));
}
export function zoom(svg, factor) { svg.mir2Zoom?.(factor); }
export function detach(svg) {
    svg?.mir2Detach?.(); if (svg) delete svg.mir2Detach;
    svg?.mir2Thumbnail?.();
    if (svg?.mir2Pick) { svg.removeEventListener("pointerdown", svg.mir2Pick); delete svg.mir2Pick; }
}
