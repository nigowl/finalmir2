// 与 Godot WorldView 共用 48×32 格子和原素材偏移，仅请求当前视口的地图块。
import { watch } from './map-thumbnail.js';
export function attach(scroll, minimap, coordinates, error, receiver, id, version, width, height, focusNpcId) {
    detach(scroll);
    scroll.mir2Thumbnail = watch(minimap.querySelector('[data-map-thumbnail]'));
    const tiles = scroll.querySelector('.native-map-tiles'), viewport = minimap.querySelector('[data-viewport]');
    let frame = 0, revision = 0, pointer = null;
    const updateError = () => { error.hidden = !tiles.querySelector('[data-failed]'); };
    const render = () => {
        frame = 0;
        const left = scroll.scrollLeft, top = scroll.scrollTop;
        if (pointer) showCoordinates(pointer);
        viewport.setAttribute('x', left / 48); viewport.setAttribute('y', top / 32);
        viewport.setAttribute('width', Math.min(width, scroll.clientWidth / 48));
        viewport.setAttribute('height', Math.min(height, scroll.clientHeight / 32));
        const visible = new Set();
        for (let y = Math.floor(top / 1024); y * 1024 < Math.min(height * 32, top + scroll.clientHeight); y++) {
            for (let x = Math.floor(left / 1024); x * 1024 < Math.min(width * 48, left + scroll.clientWidth); x++) {
                const key = `${x}:${y}`; visible.add(key);
                if (tiles.querySelector(`[data-tile="${key}"]`)) continue;
                const image = document.createElement('img');
                image.dataset.tile = key; image.alt = ''; image.draggable = false;
                image.width = image.height = 1024; image.style.left = `${x * 1024}px`; image.style.top = `${y * 1024}px`;
                image.onerror = () => { image.dataset.failed = 'true'; updateError(); };
                image.onload = () => { delete image.dataset.failed; updateError(); };
                image.src = `/native-map-tile?id=${id}&x=${x}&y=${y}&version=${encodeURIComponent(version)}&revision=${revision}`;
                tiles.append(image);
            }
        }
        for (const image of [...tiles.children]) if (!visible.has(image.dataset.tile)) image.remove();
        updateError();
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(render); };
    const point = event => {
        const bounds = scroll.getBoundingClientRect();
        return [Math.floor((event.clientX - bounds.left - scroll.clientLeft + scroll.scrollLeft) / 48),
            Math.floor((event.clientY - bounds.top - scroll.clientTop + scroll.scrollTop) / 32)];
    };
    const showCoordinates = event => { const [x, y] = point(event); coordinates.textContent = `坐标 ${x}, ${y}`; };
    const hover = event => { pointer = { clientX: event.clientX, clientY: event.clientY }; showCoordinates(pointer); };
    const leave = () => { pointer = null; coordinates.textContent = '移入地图显示坐标'; };
    const pick = event => {
        if (event.button !== 0 || event.target.closest('[data-npc]')) return;
        const [x, y] = point(event); receiver.invokeMethodAsync('Pick', x, y);
    };
    const locate = event => {
        if (event.button !== 0) return;
        const matrix = minimap.getScreenCTM(); if (!matrix) return;
        const target = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
        scroll.scrollTo(target.x * 48 - scroll.clientWidth / 2, target.y * 32 - scroll.clientHeight / 2); minimap.focus();
    };
    const key = event => {
        const directions = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
        const direction = directions[event.key]; if (!direction && event.key !== 'Home') return;
        event.preventDefault();
        if (event.key === 'Home') scroll.scrollTo(0, 0);
        else scroll.scrollBy(direction[0] * scroll.clientWidth / 2, direction[1] * scroll.clientHeight / 2);
    };
    const retry = () => { revision++; tiles.replaceChildren(); render(); };
    scroll.addEventListener('scroll', schedule, { passive: true });
    scroll.addEventListener('pointermove', hover); scroll.addEventListener('pointerleave', leave); scroll.addEventListener('click', pick);
    minimap.addEventListener('pointerdown', locate); minimap.addEventListener('keydown', key); error.querySelector('button').addEventListener('click', retry);
    const observer = new ResizeObserver(schedule); observer.observe(scroll);
    const first = focusNpcId != null ? scroll.querySelector(`[data-npc="${focusNpcId}"]`) : scroll.querySelector('[data-npc]');
    if (first) scroll.scrollTo(parseFloat(first.style.left) - scroll.clientWidth / 2, parseFloat(first.style.top) - scroll.clientHeight / 2);
    if (focusNpcId != null && first) first.focus({ preventScroll: true });
    render();
    scroll.mir2Detach = () => {
        cancelAnimationFrame(frame); observer.disconnect();
        scroll.removeEventListener('scroll', schedule); scroll.removeEventListener('pointermove', hover); scroll.removeEventListener('pointerleave', leave); scroll.removeEventListener('click', pick);
        minimap.removeEventListener('pointerdown', locate); minimap.removeEventListener('keydown', key); error.querySelector('button').removeEventListener('click', retry);
    };
}
export function detach(scroll) { scroll?.mir2Thumbnail?.(); scroll?.mir2Detach?.(); if (scroll) delete scroll.mir2Detach; }
