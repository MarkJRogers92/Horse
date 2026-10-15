// D-042: temporary dirt on a private grooming presenter. Never replaces its coat shader or UVs.
// Fixed camera, frozen pose: patches use the same projected coordinates as the brush hit test.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, shadows_disabled;
uniform vec2 patch_centers[3];
uniform vec2 patch_radius = vec2(0.045, 0.09);
uniform vec3 remaining = vec3(1.0);
float grain(vec2 p) { return fract(sin(dot(floor(p), vec2(127.1, 311.7))) * 43758.5453); }
void vertex() { VERTEX += NORMAL * 0.0004; }
void fragment() {
    float dirt = 0.0;
    for (int i = 0; i < 3; i++) {
        vec2 p = (SCREEN_UV - patch_centers[i]) / patch_radius;
        float edge = length(p) + sin(p.x * 14.0 + p.y * 9.0) * 0.055 + sin(p.y * 19.0) * 0.035;
        dirt = max(dirt, (1.0 - smoothstep(0.58, 1.0, edge)) * remaining[i]);
    }
    float speckle = grain(SCREEN_UV * 1400.0);
    ALBEDO = mix(vec3(0.24, 0.15, 0.075), vec3(0.54, 0.39, 0.20), speckle);
    ROUGHNESS = 1.0;
    ALPHA = dirt * (0.58 + speckle * 0.23);
}
