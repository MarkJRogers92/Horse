// Horse eye: dark, glossy, horizontally oval pupil; colour from descriptor eyeRecipe.
// UV.x = outwardness (0 centre plane .. 1 outer pole), UV.y = vertical position.
shader_type spatial;
render_mode cull_back, specular_schlick_ggx;
uniform vec3 iris : source_color = vec3(0.23, 0.14, 0.08);
uniform vec3 ring : source_color = vec3(0.1, 0.06, 0.03);
void fragment() {
	float r = 1.0 - UV.x;                        // 0 at the outer pole
	float v = (UV.y - 0.5) * 2.0;
	float pupil = 1.0 - smoothstep(0.3, 0.38, length(vec2(r * 1.6, v * 0.9)));
	float iris_m = 1.0 - smoothstep(0.8, 0.95, r);
	vec3 col = mix(vec3(0.12, 0.09, 0.08), mix(ring, iris, smoothstep(0.8, 0.2, r)), iris_m);
	col = mix(col, vec3(0.02, 0.015, 0.012), pupil);
	ALBEDO = col;
	ROUGHNESS = 0.08;
	SPECULAR = 0.9;
	CLEARCOAT = 1.0;
	CLEARCOAT_ROUGHNESS = 0.03;
}
