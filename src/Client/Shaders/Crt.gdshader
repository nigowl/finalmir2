shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float strength : hint_range(1.0, 5.0, 1.0) = 3.0;

void fragment() {
    vec3 source = texture(screen_texture, SCREEN_UV).rgb;
    float level = clamp((strength - 1.0) / 4.0, 0.0, 1.0);
    // 扫描线跟随逻辑画布，荧光点使用实际屏幕像素，避免高DPI缩放抹掉纹理。
    float scanline = 1.0 - mix(0.12, 0.38, level) * mod(floor(UV.y * 768.0), 2.0);
    vec2 pixel = floor(SCREEN_UV / SCREEN_PIXEL_SIZE);
    vec3 mask = vec3(1.0 - mix(0.10, 0.28, level));
    int channel = int(mod(pixel.x, 3.0));
    mask[channel] = 1.0;
    float phosphor = 1.0 - mix(0.04, 0.14, level) * step(2.0, mod(pixel.y, 3.0));
    vec2 edge = UV * (1.0 - UV);
    float vignette = mix(0.96, 1.0, clamp(pow(16.0 * edge.x * edge.y, 0.3), 0.0, 1.0));
    COLOR = vec4(source * mask * scanline * phosphor * vignette, 1.0);
}
