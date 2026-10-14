// Mane / tail hair cards: procedural strands, alpha-scissored, anisotropic sheen.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
uniform vec3 hair_color : source_color = vec3(0.1);
uniform vec3 tip_color : source_color = vec3(0.2);
uniform float strand_count = 18.0;
uniform float seed = 0.0;
uniform bool solid_clumps = false;
uniform float grey_amount = 0.0;    // D-026: mane and tail grey with the coat, a little later than the body
uniform vec3 dark_color : source_color = vec3(0.1);
uniform float dark_fraction = 0.0;  // D-037: lightened (silver) manes and tails keep a share of dark strands
uniform float white_fraction = 0.0; // D-026: share of white locks (tobiano, leopard complex)
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
	// Thin strands run along the hair (v) and wander a little across the card, so a flaxen or silver mane reads as
	// many fine hairs rather than chunky per-card patches. Dark strands are a share of these thin strands.
	float strands = strand_count * 6.0;
	float wander = sin(v * 9.0 + hash(lane + 5.0) * 6.2832) * 0.004;
	float sid = floor((u + wander) * strands);
	float streak = (0.84 + 0.24 * hash(sid + 3.0)) * (0.94 + 0.06 * sin(u * 120.0 + v * 6.0));
	vec3 col = mix(hair_color, tip_color, smoothstep(0.3, 1.0, v) * 0.5) * streak;
	col = mix(col, vec3(0.86, 0.86, 0.84) * streak, smoothstep(0.3, 1.0, grey_amount) * 0.92);
	if (hash(sid + 13.0) < dark_fraction) col = mix(col, dark_color, 0.8) * (0.9 + 0.2 * hash(sid + 29.0));
	if (hash(lane + 11.0) < white_fraction) col = vec3(0.93, 0.92, 0.9) * streak;
	ALBEDO = col;
	ROUGHNESS = solid_clumps ? 0.72 : 0.55;
	SPECULAR = solid_clumps ? 0.22 : 0.3;
	ANISOTROPY = solid_clumps ? 0.3 : 0.7;
	BACKLIGHT = col * 0.3;
}
