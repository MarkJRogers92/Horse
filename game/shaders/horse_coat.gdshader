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
// D-026 white and pattern families (need the body map; ignored without it)
uniform float grey_amount = 0.0;       // progressive grey: 0 born coloured .. 1 flea-bitten white
uniform float roan = 0.0;              // 1 = roan: white hairs through the body, solid head and lower legs
uniform float tobiano = 0.0;           // 1 = tobiano white pattern
uniform float tobiano_extent = 0.5;    // individual amount of white, 0 minimal .. 1 mostly white
uniform float leopard = 0.0;           // 1 = leopard complex (LP)
uniform float leopard_coverage = 0.3;  // white coverage: low = hip blanket, high (PATN1) = full leopard
// D-037 rarer families (also need the body map for the white patterns)
uniform float champagne = 0.0;         // 1 = champagne: metallic sheen, freckled pink skin, darker foals
uniform float champagne_foal = 0.0;    // 1 = born darker, lightening as it grows
uniform float freckles = 0.0;          // skin recipe: freckled skin on the muzzle and around the eyes
uniform float sabino = 0.0;            // 0 none, 1 SB1/N, 2 SB1/SB1 (sabino white)
uniform float sabino_extent = 0.5;     // individual amount of white
uniform float splash = 0.0;            // 0 none, 1 SW1/N, 2 SW1/SW1
uniform float splash_extent = 0.5;
uniform bool use_body_map = false;        // D-025: CUSTOM0 carries the rest-pose body coordinates
uniform vec3 body_aspect = vec3(1.0, 0.5, 0.25); // body-length units per (u, h, s), for round spots
uniform int debug_view = 0; // 1 = COLOR masks, 2 = canonical coords, 3 = body map, 4 = primitive markings, 5 = patterns

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
	vec3 skin_here = skin_color;
	if (freckles > 0.0 && use_body_map) {
		// Champagne: pink skin with dark freckles (mottling) round the muzzle, eyes and lips; the coat above stays clean.
		float fk = smoothstep(0.62, 0.7, noise3(bp * 190.0 + vec3(edge_seed * 5.0))) * freckles;
		skin_here = mix(skin_color, skin_color * 0.45 + vec3(0.16, 0.06, 0.04), fk);
	}
	col = mix(col, skin_here, smoothstep(0.2, 0.9, m.g) * nose_amount);
	col *= 0.94 + 0.12 * hair_var;
	// Champagne foals are born darker (a smoky taupe) and lighten into the adult coat.
	col = mix(col, col * vec3(0.64, 0.58, 0.56) + vec3(0.01), champagne_foal);

	float pattern_white = 0.0;
	vec3 pattern_debug = vec3(0.0);
	if (use_body_map) {
		float seed = edge_seed * 13.0;
		// Progressive grey (STX17): born coloured; white hairs arrive on the head first, then a steel or rose grey,
		// then dapple grey (light rings on a darker net), light grey and finally flea-bitten white with coloured
		// specks. Lower legs and points grey last. Skin stays dark.
		if (grey_amount > 0.0) {
			float g = clamp(grey_amount, 0.0, 1.0);
			float local = g * (is_head ? 1.35 : is_leg ? mix(0.55, 0.95, smoothstep(0.3, 0.9, c.y)) : 1.0);
			float lum = dot(col, vec3(0.3, 0.59, 0.11));
			vec3 steel = mix(col, vec3(lum) * vec3(0.98, 0.99, 1.02), smoothstep(0.05, 0.45, local));
			vec3 warp = vec3(fbm3(bp * 6.0 + vec3(seed)), fbm3(bp * 6.0 + vec3(seed + 9.0)), 0.0) * 0.12;
			vec2 w = worley3(bp * 9.0 + warp * 9.0 + vec3(seed + 5.0));
			float dapples = (smoothstep(0.62, 0.15, w.x) - 0.5) * 0.8;    // + inside a dapple, - on the net
			float dappleStage = smoothstep(0.25, 0.5, g) * (1.0 - smoothstep(0.7, 0.9, g)) * (is_head ? 0.3 : 1.0);
			float whiten = clamp(local * 1.15 - 0.15 + dapples * 0.45 * dappleStage + (hair_var - 0.5) * 0.15, 0.0, 1.0);
			vec3 greyWhite = vec3(0.9, 0.9, 0.88);
			col = mix(steel * mix(1.0, 0.75, smoothstep(0.1, 0.5, local)), greyWhite, whiten);
			// Flea-bitten: small specks of the original colour come back late in life.
			float specks = (1.0 - smoothstep(0.1, 0.17, worley3(bp * 45.0 + vec3(seed)).x)) * smoothstep(0.8, 0.97, g) * (is_head ? 0.5 : 1.0);
			col = mix(col, body_color * 0.8, specks * 0.85);
			pattern_debug.r = whiten;
		}
		// Roan: white hairs evenly through the body; the head, mane, tail and lower legs stay solid, and dark
		// hair reaches up the legs in points.
		if (roan > 0.0) {
			float where = is_head ? 0.08 : is_leg ? smoothstep(0.55, 0.95, c.y) : smoothstep(-0.1, 0.12, body.x) * (1.0 - smoothstep(1.3, 1.7, body.x)) * 0.85 + 0.15;
			float hairs = 0.6 + 0.4 * noise3(bp * 400.0);
			float amount = clamp(where * roan * hairs, 0.0, 1.0);
			col = mix(col, white_color * 0.9, amount * 0.31);
			pattern_debug.g = amount;
		}
		// Leopard complex (LP): a white blanket over the hips with dark oval spots; with PATN1 the white covers
		// the whole body (leopard). Spots keep the base colour; the head stays mostly coloured.
		if (leopard > 0.0) {
			float coverage = clamp(leopard_coverage, 0.0, 1.0);
			float blanket = body.y * 0.8 + (0.8 - abs(body.x - 0.35) * 1.1) * 0.9 + (fbm3(bp * 3.0 + vec3(seed)) - 0.5) * 0.45 + (is_leg ? coverage * 1.2 : 0.0);
			float threshold = mix(1.25, 0.05, coverage);
			float lw = smoothstep(threshold - 0.03, threshold + 0.03, blanket) * (is_head ? 0.35 : 1.0);
			vec2 sp = worley3(bp * vec3(7.0, 8.5, 7.0) + vec3(seed + 11.0));
			float size = 0.24 + 0.14 * hash3(floor(bp * vec3(7.0, 8.5, 7.0) + vec3(seed + 11.0)));
			float spot = 1.0 - smoothstep(size - 0.03, size + 0.03, sp.x + (fbm3(bp * 25.0) - 0.5) * 0.08);
			lw *= 1.0 - spot;
			pattern_white = max(pattern_white, lw * leopard);
			pattern_debug.b = lw;
		}
		// Tobiano (KIT): smooth, rounded white that crosses the back between the withers and the tail, white legs,
		// a coloured head (normal face markings), and coloured flanks and chest that shrink as the white grows.
		if (tobiano > 0.0) {
			float e = clamp(tobiano_extent, 0.0, 1.0);
			float field = fbm3(bp * 2.1 + vec3(seed + 3.0)) - 0.5;
			float crossing = exp(-pow((body.x - 0.55) / 0.28, 2.0)) * smoothstep(0.35, 1.0, body.y);
			float legs = is_leg ? mix(0.55, 1.1, 1.0 - smoothstep(0.2, 1.0, c.y)) : smoothstep(0.1, -0.5, body.y) * 0.6;
			float darkHead = (is_head ? 1.2 : 0.0) + smoothstep(1.35, 1.8, body.x) * 0.6;
			float t = field * 1.3 + crossing * 0.9 + legs - darkHead + (e - 0.5) * 1.1 - 0.15;
			float tw = smoothstep(-0.02, 0.02, t);
			pattern_white = max(pattern_white, tw * tobiano);
			pattern_debug.g = max(pattern_debug.g, tw);
		}
		// Sabino-1 (KIT, SB1): irregular white that rises from below: high stockings with lacy, roaned edges, white on the
		// belly and chin, and a broad irregular blaze. Edges are speckled (roaning) rather than crisp. SB1/SB1 is sabino
		// white: nearly all white, with coloured patches left over the back, shoulder and ears.
		if (sabino > 0.0) {
			float e = clamp(sabino_extent, 0.0, 1.0);
			float dbl = step(1.5, sabino);
			float speck = noise3(bp * 260.0 + vec3(seed + 17.0));
			// Lacy, spiky edges: blotchy low-frequency noise plus vertical streaks, so white reaches up the flanks in spikes.
			float irregular = (fbm3(bp * 3.2 + vec3(seed + 21.0)) - 0.5) * mix(0.75, 0.9, dbl) + (fbm3(bp * 10.0 + vec3(seed + 4.0)) - 0.5) * 0.3
				+ (noise3(vec3(bp.x * 11.0, bp.y * 1.6, bp.z * 11.0) + vec3(seed)) - 0.5) * 0.55;
			float d = 0.0; // > 0 inside the white
			if (is_head) {
				float lat = abs(c.y) + (fbm3(bp * 7.0 + vec3(seed + 8.0)) - 0.5) * 0.45;
				float blaze_w = mix(0.28, 0.8, e);
				float face = (1.0 - smoothstep(blaze_w - 0.05, blaze_w + 0.05, lat)) * smoothstep(0.16, 0.34, c.x);
				float chin = smoothstep(0.82, 0.9, c.x + (fbm3(bp * 9.0) - 0.5) * 0.12) * smoothstep(0.12, 0.3, e);
				float wholeFace = dbl * smoothstep(0.16 + irregular * 0.2, 0.3 + irregular * 0.2, c.x);
				d = (max(max(face, chin), wholeFace) - 0.5) * 0.4;
			} else if (is_leg) {
				// Stockings climb with the extent; once the belly is white the legs are white to the elbow, so no coloured patch is stranded.
				float legthr = mix(0.5, 1.45, smoothstep(0.15, 0.6, e)) + irregular * 0.5;
				d = (dbl > 0.5 ? 1.6 : legthr) - c.y;
			} else {
				float thr = mix(0.2, 0.66, e) + irregular * 0.55;
				if (dbl > 0.5) thr = mix(0.78, 1.02, e) + irregular * 0.5;
				d = thr - body.y;
			}
			float inside = smoothstep(0.1, 0.11, d);
			float roaned = step(speck, smoothstep(-0.16, 0.1, d)) * (1.0 - inside);
			float sabino_white = clamp(inside + roaned * 0.92, 0.0, 1.0);
			pattern_white = max(pattern_white, sabino_white);
			pattern_debug.g = max(pattern_debug.g, sabino_white);
		}
		// Splash white (MITF, SW1): "dipped in white" from below. Legs and belly are white up to a smooth, crisp, gently
		// wavy line, the face carries a broad blaze with crisp edges, and the topline, ears and poll stay coloured.
		// SW1/SW1 is far more extensive and usually has blue eyes.
		if (splash > 0.0) {
			float e = clamp(splash_extent, 0.0, 1.0);
			float dbl = step(1.5, splash);
			float wave = (fbm3(bp * 1.7 + vec3(seed + 31.0)) - 0.5) * 0.3 + (fbm3(bp * 5.0 + vec3(seed + 7.0)) - 0.5) * 0.06;
			float sw = 0.0;
			if (is_head) {
				float blaze_w = mix(0.4, 1.05, e) + dbl * 0.8;
				float lat = abs(c.y) + wave * 0.3;
				float face = (1.0 - smoothstep(blaze_w - 0.04, blaze_w + 0.04, lat)) * smoothstep(0.17, 0.2, c.x + wave * 0.1);
				float chin = smoothstep(0.84, 0.88, c.x);
				sw = max(face, chin * smoothstep(0.1, 0.25, e));
			} else if (is_leg) {
				float legthr = mix(0.75, 1.4, smoothstep(0.0, 0.5, e)) + wave * 0.5 + dbl;
				sw = 1.0 - smoothstep(legthr - 0.025, legthr + 0.025, c.y);
			} else {
				// Barrel edge (h: 0 belly .. 1 topline). The double stays low: its line tops out near h 0.33, so it never
				// reaches mid-barrel. Its wave is damped so the edge cannot climb. The extra ripple uses the raw body
				// coordinates (along, height, side), so the edge meanders irregularly instead of running straight.
				float ripple = (noise3(vec3(body.x * 6.0, body.y * 3.0, body.z * 5.0) + vec3(seed + 19.0)) - 0.5) * 0.11;
				float scallop = sin(body.x * 9.0 + body.z * 3.5 + seed * 2.0) * 0.025;
				float base = dbl > 0.5 ? mix(0.02, 0.22, e) : mix(-0.02, 0.5, e);
				float amp = dbl > 0.5 ? 0.6 : 1.0;
				float line = base + amp * (wave + ripple + scallop);
				sw = 1.0 - smoothstep(line - 0.02, line + 0.02, body.y);
			}
			pattern_white = max(pattern_white, sw);
			pattern_debug.g = max(pattern_debug.g, sw);
		}
	}

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
	white = max(white, pattern_white);
	vec3 white_here = mix(white_color, pink_skin, smoothstep(0.3, 0.9, m.g) * 0.7);
	col = mix(col, white_here, white);

	float rough = mix(0.72, 0.52, sheen);
	// hooves (below the coronet in canonical leg height)
	if (is_leg && c.y < 0.165) {
		float h = 1.0 - smoothstep(0.15, 0.17, c.y);
		vec3 hoof = mix(hoof_dark, hoof_pale, white > 0.5 ? 1.0 : 0.0);
		// Leopard-complex hooves are often striped.
		if (leopard > 0.0) hoof = mix(hoof, hoof_pale, step(0.55, noise(vec2(UV.x * 60.0, 0.5))) * leopard);
		hoof *= 0.9 + 0.2 * noise(vec2(c.y * 160.0, UV.x * 30.0));
		col = mix(col, hoof, h);
		rough = mix(rough, 0.38, h);
	}
	if (debug_view == 1) col = m.rgb;
	if (debug_view == 4) col = prim_debug;
	if (debug_view == 5) col = pattern_debug; // R grey whitening, G roan/tobiano, B leopard white
	if (debug_view == 3) col = use_body_map ? vec3(fract(body.x * 5.0), fract(body.y * 5.0), body.z * 0.5 + 0.5) : vec3(1, 0, 1);
	if (debug_view == 2) col = is_head ? vec3(c.x, c.y * 0.5 + 0.5, m.b) : is_leg ? vec3(0.0, c.y, 0.0) : vec3(0.1);
	ALBEDO = col;
	// Champagne: a metallic sheen (lower roughness, a little metallic, warm grazing glints) over the coat; the hooves and
	// white areas keep their own look.
	float metal = champagne * (1.0 - clamp(white, 0.0, 1.0) * 0.8) * ((is_leg && c.y < 0.165) ? 0.0 : 1.0);
	rough = mix(rough, 0.44, metal);
	METALLIC = 0.1 * metal;
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	EMISSION = vec3(1.0, 0.82, 0.58) * col * rim * 0.22 * metal;
	ROUGHNESS = rough;
	SPECULAR = 0.28 + 0.12 * metal;
	if (use_coat_normal) {
		NORMAL_MAP = texture(coat_normal, UV).rgb;
		NORMAL_MAP_DEPTH = (is_leg && c.y < 0.165) ? 0.0 : coat_normal_depth;
	}
	// fine hair direction sheen
	ANISOTROPY = 0.25 * sheen;
	BACKLIGHT = vec3(0.03) * col;
}
