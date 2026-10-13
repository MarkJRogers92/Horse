// Cloverbrook horse coat. Renders a HorseVisualDescriptor recipe (never a genome).
// Layer order (D-001 / workflow s10): pigment regions -> dilution (baked into recipe colours)
// -> primitive markings -> white markings -> presentation.
// Mesh contract: COLOR = (points, soft-nose skin, midline, countershade);
// UV2 = canonical marking coords (head: s,t | legs: 10+leg, height | other: -10,50).
// CUSTOM0 = D-025 body map from the rest pose (u along, h height, s side, 1); see BodyMap.cs.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 body_color : source_color = vec3(0.5, 0.25, 0.1);
uniform vec3 points_color : source_color = vec3(0.05);
uniform float points_amount = 1.0;
uniform vec3 counter_color : source_color = vec3(0.6, 0.35, 0.2);
uniform vec3 primitive_color : source_color = vec3(0.1);
uniform float primitive_strength = 0.0;   // descriptor primitiveMarkingStrength / 10000
uniform float dun_head = 0.0;             // D-024: dun leaves the head undiluted, so it reads darker than the body
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
uniform bool use_body_map = false;        // D-025: CUSTOM0 carries the rest-pose body coordinates
uniform vec3 body_aspect = vec3(1.0, 0.5, 0.25); // body-length units per (u, h, s), for round spots
uniform int debug_view = 0; // 1 = COLOR masks, 2 = canonical coords, 3 = body map, 4 = primitive markings

varying vec4 body;
void vertex() { body = CUSTOM0; }

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { float a = 0.5, s = 0.0; for (int i = 0; i < 4; i++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }
// 3D noise on the body map: seamless across the whole horse.
float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
vec3 hash33(vec3 p) { return fract(sin(vec3(dot(p, vec3(127.1, 311.7, 74.7)), dot(p, vec3(269.5, 183.3, 246.1)), dot(p, vec3(113.5, 271.9, 124.6)))) * 43758.5453); }
float noise3(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float fbm3(vec3 p) { float a = 0.5, s = 0.0; for (int i = 0; i < 4; i++) { s += a * noise3(p); p *= 2.03; a *= 0.5; } return s; }
// Cellular noise: x = distance to the nearest cell centre, y = to the second nearest.
vec2 worley3(vec3 p) {
	vec3 i = floor(p); vec3 f = fract(p); vec2 d = vec2(8.0);
	for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec3 g = vec3(float(x), float(y), float(z));
		float r = length(g + hash33(i + g) * 0.8 + 0.1 - f);
		if (r < d.x) { d.y = d.x; d.x = r; } else if (r < d.y) d.y = r;
	}
	return d;
}

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
	// shading modifiers (artistic, labelled): dapples and sooty topline
	vec3 bp = body.xyz * body_aspect; // body-length units, continuous over the whole horse
	if (use_body_map) {
		// D-025 dapples: round lighter rings netted by darker hair, richest over the barrel and hindquarters,
		// fading down the legs, along the neck and on the head. 3D cells on the body map never meet a seam.
		vec2 w = worley3(bp * 14.0 + vec3(edge_seed * 7.0));
		float region = smoothstep(-0.15, 0.15, body.x) * (1.0 - smoothstep(1.25, 1.75, body.x)) * smoothstep(-0.55, 0.15, body.y);
		region *= is_head ? 0.15 : 1.0;
		float wobble = (fbm3(bp * 38.0) - 0.5) * 0.14;
		// Round light spots of slightly varying size; the hair between them stays a touch darker.
		float size = 0.4 + 0.12 * hash3(floor(bp * 14.0 + vec3(edge_seed * 7.0)));
		float spot = 1.0 - smoothstep(size - 0.2, size + 0.05, w.x + wobble);
		float between = smoothstep(size, size + 0.25, w.x + wobble);
		col = mix(col, col * 1.2 + vec3(0.02), spot * dapple * region * 0.6);
		col = mix(col, col * 0.88, between * dapple * region * 0.45);
	} else {
		float dap = smoothstep(0.45, 0.75, fbm(UV * vec2(26.0, 14.0) + 3.1)) * dapple * (is_trunk ? 1.0 : 0.3);
		col = mix(col, col * 1.12 + vec3(0.01), dap * 0.35);
	}
	col = mix(col, counter_color, m.a * 0.65);
	if (use_body_map) {
		// D-025 sooty: dark hairs gather along the topline, croup, crest and shoulders and thin out downwards in
		// a mottled, slightly dappled way (not a hard dorsal band).
		float mottle = fbm3(bp * 9.0 + vec3(3.7, 1.3, edge_seed));
		float down = smoothstep(0.0, 1.0, body.y + (mottle - 0.5) * 0.4);
		float where = is_head ? 0.45 : is_leg ? 0.0 : 1.0;
		col = mix(col, col * 0.42, sooty * clamp(down * where * (0.7 + 0.6 * mottle), 0.0, 1.0));
	} else {
		col = mix(col, col * 0.7, sooty * clamp(is_trunk ? m.b * 0.8 : 0.0, 0.0, 1.0));
	}
	// points (lower legs, ear rims); bay/buckskin = black points
	float pts = smoothstep(0.25, 0.7, m.r);
	col = mix(col, points_color, pts * points_amount);
	// primitive markings (dun, nd1): dorsal stripe + leg barring
	vec3 prim_debug = vec3(0.0);
	if (primitive_strength > 0.0) {
		// A crisp, continuous dorsal stripe from withers to tail (slightly wavy edge).
		float stripe = is_trunk ? smoothstep(0.42, 0.62, m.b + (hair_var - 0.5) * 0.08) : 0.0;
		if (use_body_map && !is_head) {
			// D-025: a real dorsal stripe is 3-7 cm wide from the withers over the croup to the tail head, wide
			// enough to read from the side; the baked midline mask alone is a hairline on the very top.
			float off = abs(body.z) + (fbm3(bp * 30.0) - 0.5) * 0.03;
			stripe = max(stripe, (1.0 - smoothstep(0.075, 0.11, off)) * smoothstep(0.62, 0.8, body.y) * smoothstep(-0.08, 0.04, body.x) * (1.0 - smoothstep(1.02, 1.12, body.x)));
		}
		float bars = 0.0;
		if (is_leg && primitive_strength > 0.5) {
			// D-025: zebra barring runs up the forearm above the knee and around the hock (canonical leg height
			// 1 is the elbow/stifle line, the knee is near 0.48), 2-3 soft bars.
			float band = smoothstep(0.46, 0.54, c.y) * (1.0 - smoothstep(0.76, 0.84, c.y));
			bars = smoothstep(0.6, 0.85, sin(c.y * 58.0 + hair_var * 1.5) * 0.5 + 0.5) * band;
			if (use_body_map) bars *= smoothstep(0.25, 0.6, fbm3(bp * 22.0 + vec3(edge_seed))); // broken, uneven bars
		}
		// D-025 shoulder stripe: a faint transverse bar across the withers on strongly marked duns.
		float shoulder = 0.0;
		if (use_body_map && is_trunk && primitive_strength > 0.5) {
			float across = abs(body.x - 1.0 + (1.0 - body.y) * 0.22 + (hair_var - 0.5) * 0.03);
			shoulder = (1.0 - smoothstep(0.008, 0.022, across)) * smoothstep(0.55, 0.9, body.y) * (1.0 - smoothstep(1.15, 1.3, body.y));
		}
		col = mix(col, primitive_color, clamp(stripe * 0.95 + bars * 0.65 + shoulder * 0.4, 0.0, 1.0) * primitive_strength);
		prim_debug = vec3(stripe, bars, shoulder);
	}
	// D-024 dun mask: the undiluted head is darker than the body, strongest over the face and cheeks.
	if (dun_head > 0.0 && is_head) {
		float face = smoothstep(-0.2, 0.25, c.x) * (1.0 - smoothstep(1.05, 1.3, c.x));
		col = mix(col, col * 0.5 + primitive_color * 0.5, clamp(dun_head * face * 1.3, 0.0, 1.0));
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
	if (debug_view == 4) col = prim_debug;
	if (debug_view == 3) col = use_body_map ? vec3(fract(body.x * 5.0), fract(body.y * 5.0), body.z * 0.5 + 0.5) : vec3(1, 0, 1);
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
