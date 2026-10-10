// Soft pastel lawn: low-frequency colour variation, no textures needed.
shader_type spatial;
render_mode diffuse_burley;
uniform vec3 grass_a : source_color = vec3(0.56, 0.74, 0.45);
uniform vec3 grass_b : source_color = vec3(0.70, 0.83, 0.52);
uniform vec3 clover : source_color = vec3(0.47, 0.68, 0.42);
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) { vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	vec2 p = wpos.xz;
	float n = noise(p * 0.15) * 0.6 + noise(p * 0.8) * 0.3 + noise(p * 6.0) * 0.1;
	vec3 col = mix(grass_a, grass_b, n);
	col = mix(col, clover, smoothstep(0.62, 0.7, noise(p * 1.7 + 4.0)) * 0.5);
	ALBEDO = col;
	ROUGHNESS = 0.95;
}
