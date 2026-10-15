// D-043: reused by the existing grass and flower MultiMeshes. No extra instances.
shader_type spatial;
render_mode diffuse_burley, cull_disabled, specular_disabled;

uniform vec3 season_tint : source_color = vec3(1.0);
uniform float atmosphere_time = 0.0;
uniform float wind_strength : hint_range(0.0, 1.0) = 0.24;
uniform bool flower_head = false;

void vertex() {
	vec2 anchor = MODEL_MATRIX[3].xz;
	float phase = dot(anchor, vec2(0.63, 0.37));
	float breeze = sin(atmosphere_time * 1.25 + phase) * 0.70
		+ sin(atmosphere_time * 2.10 + phase * 1.73) * 0.30;
	// Grass PrismMesh runs from -0.10 to +0.10 m. Its foot never moves.
	// Flower heads move as a rigid whole on their implied flexible stem.
	float root_weight = flower_head ? 1.0 : pow(clamp((VERTEX.y + 0.10) / 0.20, 0.0, 1.0), 2.0);
	vec3 direction = normalize(vec3(0.92, 0.0, 0.39));
	vec3 local_direction = vec3(dot(normalize(MODEL_MATRIX[0].xyz), direction),
		dot(normalize(MODEL_MATRIX[1].xyz), direction), dot(normalize(MODEL_MATRIX[2].xyz), direction));
	float bend = (0.30 + breeze) * wind_strength * (flower_head ? 0.025 : 0.038) * root_weight;
	VERTEX += local_direction * bend;
}

void fragment() {
	ALBEDO = COLOR.rgb * season_tint;
	ROUGHNESS = flower_head ? 0.80 : 1.0;
}
