// D-043: dampen the existing path itself, without a coplanar overlay or puddles off the paddock.
shader_type spatial;
render_mode diffuse_burley;

uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform vec3 path_colour : source_color = vec3(0.902, 0.812, 0.651);
varying vec3 world_position;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 cell = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(cell), hash(cell + vec2(1, 0)), f.x),
		mix(hash(cell + vec2(0, 1)), hash(cell + vec2(1, 1)), f.x), f.y);
}
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
	float grain = noise(world_position.xz * 3.0);
	float damp = wetness * (0.18 + grain * 0.10);
	ALBEDO = path_colour * (1.0 - damp) * (0.97 + grain * 0.03);
	ROUGHNESS = mix(0.95, 0.75, wetness);
	SPECULAR = 0.15;
}
