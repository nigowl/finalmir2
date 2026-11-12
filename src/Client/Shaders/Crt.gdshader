shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform float strength : hint_range(1.0, 5.0, 1.0) = 3.0;

void fragment() {
    vec3 source = texture(screen_texture, SCREEN_UV).rgb;
    float scanline = mix(0.90, 1.0, mod(floor(UV.y * 768.0), 2.0));
    vec3 mask = vec3(0.96);
    int channel = int(mod(floor(FRAGCOORD.x), 3.0));
    mask[channel] = 1.0;
    vec2 edge = UV * (1.0 - UV);
    float vignette = mix(0.88, 1.0, clamp(pow(16.0 * edge.x * edge.y, 0.3), 0.0, 1.0));
    COLOR = vec4(mix(source, source * mask * scanline * vignette, strength / 3.0), 1.0);
}
