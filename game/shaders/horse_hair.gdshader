// Mane / tail hair cards: procedural strands, alpha-scissored, anisotropic sheen.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
uniform vec3 hair_color : source_color = vec3(0.1);
uniform vec3 tip_color : source_color = vec3(0.2);
uniform float strand_count = 18.0;
uniform float seed = 0.0;
uniform bool solid_clumps = false;
float hash(float x) { return fract(sin(x * 91.7 + seed) * 43758.5453); }
void fragment() {
	// Each card is a hair CLUMP: solid body, ragged tips, soft edges, fine strand shading.
	float u = UV.x, v = 1.0 - UV.y; // restore Blender root-to-tip coordinates after glTF's V flip
	float lane = floor(u * strand_count);
	float len = 0.72 + 0.28 * hash(lane + 7.0);
	float edge = smoothstep(0.0, 0.12, u) * (1.0 - smoothstep(0.88, 1.0, u));
	float tip = 1.0 - smoothstep(len - 0.12, len, v);
	ALPHA = solid_clumps ? 1.0 : edge * tip * (0.75 + 0.25 * hash(lane));
	ALPHA_SCISSOR_THRESHOLD = 0.4;
	float streak = 0.82 + 0.25 * hash(lane + 3.0) + 0.06 * sin(u * 120.0 + v * 6.0);
	vec3 col = mix(hair_color, tip_color, smoothstep(0.3, 1.0, v) * 0.5) * streak;
	ALBEDO = col;
	ROUGHNESS = solid_clumps ? 0.72 : 0.55;
	SPECULAR = solid_clumps ? 0.22 : 0.3;
	ANISOTROPY = solid_clumps ? 0.3 : 0.7;
	BACKLIGHT = col * 0.3;
}
