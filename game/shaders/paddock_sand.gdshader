// Raked paddock sand (D-012): grain, soft hoof-churned patches, a darker worn
// track and a grassy fade at the rim. Procedural, so CI needs no texture.
shader_type spatial;
render_mode diffuse_burley;
uniform vec3 sand_light : source_color = vec3(0.93, 0.84, 0.69);
uniform vec3 sand_dark : source_color = vec3(0.80, 0.68, 0.52);
uniform vec3 rim_grass : source_color = vec3(0.62, 0.76, 0.48);
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) { vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
varying vec3 wpos;
varying vec2 local;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; local = VERTEX.xz; }
void fragment() {
	vec2 p = wpos.xz;
	float r = length(local);                       // 0 centre, 1 rim (unit cylinder scaled by the node)
	float churn = noise(p * 0.9) * 0.55 + noise(p * 3.5) * 0.3 + hash(floor(p * 60.0)) * 0.15;
	float track = smoothstep(0.55, 0.72, r) * (1.0 - smoothstep(0.78, 0.9, r));
	vec3 col = mix(sand_light, sand_dark, clamp(churn * 0.7 + track * 0.35, 0.0, 1.0));
	float edge = smoothstep(0.86, 1.0, r + (noise(p * 2.2) - 0.5) * 0.12);
	col = mix(col, rim_grass, edge);
	ALBEDO = col;
	ROUGHNESS = 1.0;
}
