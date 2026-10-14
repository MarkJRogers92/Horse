// Raked paddock sand (D-012): grain, soft hoof-churned patches, a darker worn
// track and a grassy fade at the rim. Procedural, so CI needs no texture.
shader_type spatial;
render_mode diffuse_burley;
uniform vec3 sand_light : source_color = vec3(0.93, 0.84, 0.69);
uniform vec3 sand_dark : source_color = vec3(0.80, 0.68, 0.52);
uniform vec3 rim_grass : source_color = vec3(0.62, 0.76, 0.48);
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform float rain_amount : hint_range(0.0, 1.0) = 0.0;
uniform float atmosphere_time = 0.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) { vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y); }
varying vec3 wpos;
varying vec2 local;
varying float top_surface;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; local = VERTEX.xz; top_surface = max(NORMAL.y, 0.0); }
void fragment() {
	vec2 p = wpos.xz;
	float r = length(local);                       // 0 centre, 1 rim (unit cylinder scaled by the node)
	float churn = noise(p * 0.9) * 0.55 + noise(p * 3.5) * 0.3 + hash(floor(p * 60.0)) * 0.15;
	float track = smoothstep(0.55, 0.72, r) * (1.0 - smoothstep(0.78, 0.9, r));
	vec3 col = mix(sand_light, sand_dark, clamp(churn * 0.7 + track * 0.35, 0.0, 1.0));
	float edge = smoothstep(0.86, 1.0, r + (noise(p * 2.2) - 0.5) * 0.12);
	col = mix(col, rim_grass, edge);
	// Sparse hollows collect water. The mask stays on the paddock top and fades
	// before the grassy rim; it cannot spill onto props, grass, or the cylinder side.
	float damp = wetness * (1.0 - edge) * top_surface;
	float puddle = 0.0;
	// A uniform branch skips all puddle/ripple work in dry weather.
	if (wetness > 0.001) {
	float basin = noise(p * 0.72 + vec2(13.3, 7.1)) * 0.75 + noise(p * 1.9) * 0.25;
	puddle = smoothstep(0.64, 0.76, basin) * smoothstep(0.32, 0.90, wetness)
		* (1.0 - smoothstep(0.80, 0.95, r)) * top_surface;
	col *= 1.0 - damp * 0.24;
	col = mix(col, vec3(0.46, 0.50, 0.47), puddle * 0.37);
	// One bounded, staggered ripple per procedural cell. This adds no particle
	// system, transparent surface, reflection probe, or screen-space reflection.
	vec2 cells = p * 1.4;
	vec2 cell = floor(cells);
	float seed = hash(cell + vec2(4.7, 9.2));
	vec2 centre = vec2(0.5) + vec2(seed - 0.5, hash(cell + 17.0) - 0.5) * 0.18;
	float progress = fract(atmosphere_time * 0.73 + seed);
	float radius = progress * 0.36;
	float ring = (1.0 - smoothstep(0.008, 0.026, abs(length(fract(cells) - centre) - radius)))
		* smoothstep(0.0, 0.18, progress) * (1.0 - progress);
	col += vec3(0.08, 0.085, 0.08) * ring * puddle * rain_amount;
	}
	ALBEDO = col;
	ROUGHNESS = mix(mix(1.0, 0.78, damp), 0.48, puddle);
	SPECULAR = mix(0.12, 0.20, puddle);
}
