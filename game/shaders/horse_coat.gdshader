// Cloverbrook horse coat. Renders a HorseVisualDescriptor recipe (never a genome).
// Layer order (D-001 / workflow s10): pigment regions -> dilution (baked into recipe colours)
// -> primitive markings -> white markings -> presentation.
// Mesh contract: COLOR = (points, soft-nose skin, midline, countershade);
// UV2 = canonical marking coords (head: s,t | legs: 10+leg, height | other: -10,50).
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 body_color : source_color = vec3(0.5, 0.25, 0.1);
uniform vec3 points_color : source_color = vec3(0.05);
uniform float points_amount = 1.0;
uniform vec3 counter_color : source_color = vec3(0.6, 0.35, 0.2);
uniform vec3 primitive_color : source_color = vec3(0.1);
uniform float primitive_strength = 0.0;   // descriptor primitiveMarkingStrength / 10000
uniform vec3 skin_color : source_color = vec3(0.17, 0.15, 0.15);
uniform float nose_amount = 0.55;
uniform vec3 white_color : source_color = vec3(0.97, 0.95, 0.93);
uniform vec3 pink_skin : source_color = vec3(0.9, 0.7, 0.66);
uniform vec3 hoof_dark : source_color = vec3(0.16, 0.14, 0.13);
uniform vec3 hoof_pale : source_color = vec3(0.79, 0.72, 0.62);
uniform float sooty = 0.2;
uniform float dapple = 0.2;
uniform float sheen = 0.55;
uniform sampler2D coat_normal : hint_normal, filter_linear_mipmap;
uniform bool use_coat_normal = false;
uniform float coat_normal_depth = 0.35;
// Optional Light-only data map. No source_color hint: RGB8 is linear data, not albedo.
uniform sampler2D signed_detail_map : filter_linear_mipmap, repeat_disable;
uniform bool use_signed_detail = false;
uniform float signed_detail_strength = 0.0;
// markings
uniform int face_type = 0;          // FaceMarking enum
uniform vec4 face_params = vec4(0.08, 0.15, 0.35, 0.07); // star, strip, blaze, snip
uniform float face_offset = 0.0;
uniform vec4 leg_white = vec4(0.0);  // FL FR HL HR heights (0 = none)
uniform float edge_seed = 0.0;
uniform int debug_view = 0; // 1 = COLOR masks, 2 = canonical coords

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { float a = 0.5, s = 0.0; for (int i = 0; i < 4; i++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

float face_white(vec2 c, float front) {
	// c.x: 0 poll .. 1 muzzle tip, c.y: lateral (-1..1, 0 = face midline)
	float s = c.x, t = c.y - face_offset * (1.0 - s);
	float jag = (fbm(c * 9.0 + edge_seed) - 0.5) * 0.06;
	float star = 1.0 - smoothstep(face_params.x - 0.01, face_params.x + 0.01, length(vec2((s - 0.30) * 0.9, t * 0.55)) + jag);
	float strip_w = face_params.y * (0.6 + 0.6 * s);
	float strip = (1.0 - smoothstep(strip_w * 0.5 - 0.02, strip_w * 0.5 + 0.02, abs(t) + jag)) * smoothstep(0.30, 0.36, s) * (1.0 - smoothstep(0.80, 0.86, s));
	float blaze_w = face_params.z * (0.7 + 0.8 * s);
	float blaze = (1.0 - smoothstep(blaze_w * 0.5 - 0.02, blaze_w * 0.5 + 0.02, abs(t) + jag)) * smoothstep(0.22, 0.27, s);
	float snip = 1.0 - smoothstep(face_params.w - 0.01, face_params.w + 0.01, length(vec2((s - 0.92) * 1.2, t * 0.6)) + jag * 0.5);
	float bald = (1.0 - smoothstep(0.75, 0.85, abs(t) + jag)) * smoothstep(0.18, 0.24, s);
	float w = 0.0;
	if (face_type == 1) w = star;
	else if (face_type == 2) w = max(star, strip);
	else if (face_type == 3) w = blaze;
	else if (face_type == 4) w = snip;
	else if (face_type == 5) w = max(star, snip);
	else if (face_type == 6) w = bald;
	return w * smoothstep(0.25, 0.6, front);
}

void fragment() {
	vec4 m = COLOR;
	vec2 c = vec2(UV2.x, 1.0 - UV2.y); // glTF V flip (verified on legs)
	bool is_head = c.x > -0.5 && c.x < 1.6 && abs(c.y) < 2.0;
	bool is_leg = c.x > 9.5 && c.x < 14.0;
	bool is_trunk = !is_head && !is_leg;

	vec2 nuv = UV * vec2(40.0, 22.0);
	float hair_var = fbm(nuv * 3.0);
	vec3 col = body_color;
	// Encoding rgb8_signed_multiplier_v1: 1 + ((R*255 - 128)/127)*0.12.
	// Apply only to base trunk coat, before point/skin/primitive/white/hoof layers.
	if (use_signed_detail && is_trunk) {
		float detail_code = texture(signed_detail_map, UV).r * 255.0;
		float signed_detail = ((detail_code - 128.0) / 127.0) * 0.12;
		float detail_mask = (1.0 - smoothstep(0.25, 0.7, m.r)) * (1.0 - smoothstep(0.2, 0.9, m.g));
		col *= 1.0 + signed_detail * clamp(signed_detail_strength, 0.0, 1.0) * detail_mask;
	}
	// shading modifiers (artistic, labelled): soft dapples and sooty topline
	float dap = smoothstep(0.45, 0.75, fbm(UV * vec2(26.0, 14.0) + 3.1)) * dapple * (is_trunk ? 1.0 : 0.3);
	col = mix(col, col * 1.12 + vec3(0.01), dap * 0.35);
	col = mix(col, counter_color, m.a * 0.65);
	col = mix(col, col * 0.7, sooty * clamp(is_trunk ? m.b * 0.8 : 0.0, 0.0, 1.0));
	// points (lower legs, ear rims); bay/buckskin = black points
	float pts = smoothstep(0.25, 0.7, m.r);
	col = mix(col, points_color, pts * points_amount);
	// primitive markings (dun, nd1): dorsal stripe + leg barring
	if (primitive_strength > 0.0) {
		float stripe = is_trunk ? smoothstep(0.35, 0.75, m.b) : 0.0;
		float bars = 0.0;
		if (is_leg && primitive_strength > 0.5) {
			bars = smoothstep(0.55, 0.85, sin(c.y * 42.0 + hair_var * 2.0) * 0.5 + 0.5) * smoothstep(0.5, 0.7, c.y) * (1.0 - smoothstep(1.25, 1.5, c.y));
		}
		col = mix(col, primitive_color, clamp(stripe + bars * 0.7, 0.0, 1.0) * primitive_strength);
	}
	// soft-nose / eye skin shows through the fine hair
	col = mix(col, skin_color, smoothstep(0.2, 0.9, m.g) * nose_amount);
	col *= 0.94 + 0.12 * hair_var;

	// white markings
	float white = 0.0;
	if (is_head) white = face_white(c, m.b);
	if (is_leg) {
		int leg = int(round(c.x - 10.0));
		float h = leg == 0 ? leg_white.x : leg == 1 ? leg_white.y : leg == 2 ? leg_white.z : leg_white.w;
		if (h > 0.0) {
			float jag = (fbm(vec2(c.y * 7.0, float(leg) * 3.1 + edge_seed)) - 0.5) * 0.08;
			white = 1.0 - smoothstep(h - 0.02, h + 0.02, c.y + jag);
		}
	}
	vec3 white_here = mix(white_color, pink_skin, smoothstep(0.3, 0.9, m.g) * 0.7);
	col = mix(col, white_here, white);

	float rough = mix(0.72, 0.52, sheen);
	// hooves (below the coronet in canonical leg height)
	if (is_leg && c.y < 0.165) {
		float h = 1.0 - smoothstep(0.15, 0.17, c.y);
		vec3 hoof = mix(hoof_dark, hoof_pale, white > 0.5 ? 1.0 : 0.0);
		hoof *= 0.9 + 0.2 * noise(vec2(c.y * 160.0, UV.x * 30.0));
		col = mix(col, hoof, h);
		rough = mix(rough, 0.38, h);
	}
	if (debug_view == 1) col = m.rgb;
	if (debug_view == 2) col = is_head ? vec3(c.x, c.y * 0.5 + 0.5, m.b) : is_leg ? vec3(0.0, c.y, 0.0) : vec3(0.1);
	ALBEDO = col;
	ROUGHNESS = rough;
	SPECULAR = 0.28;
	if (use_coat_normal) {
		NORMAL_MAP = texture(coat_normal, UV).rgb;
		NORMAL_MAP_DEPTH = (is_leg && c.y < 0.165) ? 0.0 : coat_normal_depth;
	}
	// fine hair direction sheen
	ANISOTROPY = 0.25 * sheen;
	BACKLIGHT = vec3(0.03) * col;
}
