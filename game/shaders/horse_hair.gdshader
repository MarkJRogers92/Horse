// Mane / tail hair cards: procedural strands, alpha-scissored, anisotropic sheen.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
uniform vec3 hair_color : source_color = vec3(0.1);
uniform vec3 tip_color : source_color = vec3(0.2);
uniform float strand_count = 14.0;
uniform float seed = 0.0;
float hash(float x) { return fract(sin(x * 91.7 + seed) * 43758.5453); }
void fragment() {
	float u = UV.x, v = UV.y;
	float lane = floor(u * strand_count);
	float off = hash(lane) * 0.35;
	float within = fract(u * strand_count);
	float len = 0.65 + 0.35 * hash(lane + 7.0);
	float strand = smoothstep(0.0, 0.25, within) * (1.0 - smoothstep(0.65, 1.0, within));
	float alive = step(v, len) * strand;
	// taper toward the card edges and tips
	alive *= 1.0 - smoothstep(len - 0.15, len, v) * 0.6;
	ALPHA = alive;
	ALPHA_SCISSOR_THRESHOLD = 0.35;
	vec3 col = mix(hair_color, tip_color, smoothstep(0.2, 1.0, v) * 0.6);
	col *= 0.85 + 0.3 * hash(lane + 3.0) + 0.05 * sin(v * 80.0 + lane);
	ALBEDO = col;
	ROUGHNESS = 0.45;
	SPECULAR = 0.45;
	ANISOTROPY = 0.6;
	BACKLIGHT = col * 0.25;
}
