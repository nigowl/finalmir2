shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform float strength : hint_range(1.0, 5.0, 1.0) = 3.0;
uniform float curvature : hint_range(0.0, 5.0, 1.0) = 3.0;

// CRT-Mattias风格：弧面、色散、荧光晕、扫描线和暗角，五次采样避免多遍模糊。
void fragment() {
    float level = clamp((strength - 1.0) / 4.0, 0.0, 1.0);
    vec2 position = SCREEN_UV * 2.0 - 1.0;
    vec2 curve = position * (1.0 + position.yx * position.yx * clamp(curvature, 0.0, 5.0) * 0.012);
    vec2 uv = curve * 0.5 + 0.5;
    vec2 offset = SCREEN_PIXEL_SIZE * 1.15;
    vec3 centre = texture(screen_texture, uv).rgb;
    vec3 left = texture(screen_texture, uv - vec2(offset.x, 0.0)).rgb;
    vec3 right = texture(screen_texture, uv + vec2(offset.x, 0.0)).rgb;
    vec3 above = texture(screen_texture, uv - vec2(0.0, offset.y)).rgb;
    vec3 below = texture(screen_texture, uv + vec2(0.0, offset.y)).rgb;
    vec3 colour = mix(centre, vec3(left.r, centre.g, right.b), 0.5);
    colour += (left + right + above + below) * 0.035;
    colour = clamp(colour * 0.85 + colour * colour * 0.28, 0.0, 1.0);
    float line = 0.5 + 0.5 * cos(uv.y * 768.0 * 3.14159265);
    float scan = 1.0 - mix(0.16, 0.38, level) * line;
    vec2 pixel = floor(SCREEN_UV / SCREEN_PIXEL_SIZE);
    vec3 mask = vec3(1.0 - mix(0.12, 0.28, level));
    mask[int(mod(pixel.x, 3.0))] = 1.0;
    float vignette = pow(max(16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y), 0.0), 0.185);
    float flicker = 1.0 + sin(TIME * 50.0) * 0.0025;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    COLOR = vec4(colour * mask * scan * vignette * flicker * inside * 1.12, 1.0);
}
