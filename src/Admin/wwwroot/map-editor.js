// Screen coordinates are transformed through the SVG matrix, including zoom and scroll.
import { watch } from './map-thumbnail.js';
export function attach(svg, receiver) {
    detach(svg);
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
export function detach(svg) {
    svg?.mir2Thumbnail?.();
    if (svg?.mir2Pick) { svg.removeEventListener("pointerdown", svg.mir2Pick); delete svg.mir2Pick; }
}
