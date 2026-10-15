// D-039: opaque sculpted coverage beneath layered, alpha-scissored hair cards.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

// Keep the catalog / HorsePresenter palette contract shared by both layers.
uniform vec3 hair_color : source_color = vec3(0.1);
uniform vec3 tip_color : source_color = vec3(0.2);
uniform float strand_count = 18.0;
uniform float seed = 0.0;
uniform bool solid_clumps = false;
uniform float grey_amount = 0.0;
uniform vec3 dark_color : source_color = vec3(0.1);
uniform float dark_fraction = 0.0;
uniform float white_fraction = 0.0;

float hash(float x) {
	return fract(sin(x * 91.7 + seed) * 43758.5453);
}

vec3 hair_palette(float root_to_tip, float dark_share, float white_share) {
	vec3 col = mix(hair_color, tip_color, smoothstep(0.25, 1.0, root_to_tip) * 0.5);
	col = mix(col, vec3(0.86, 0.86, 0.84), smoothstep(0.3, 1.0, grey_amount) * 0.92);
	col = mix(col, dark_color, clamp(dark_share, 0.0, 1.0) * 0.55);
	return mix(col, vec3(0.93, 0.92, 0.9), clamp(white_share, 0.0, 1.0));
}

void fragment() {
	vec3 col;
	ALPHA_SCISSOR_THRESHOLD = 0.45;
	// HairCards supplies an across-card tangent, so the binormal is along the hair.
	ANISOTROPY_FLOW = vec2(0.0, 1.0);
	NORMAL_MAP = vec3(0.5, 0.5, 1.0);

	if (solid_clumps) {
		// Imported Mane/Tail UVs do not describe roots and tips. Never project card
		// locks onto them. Use the palette's mean for quiet, continuous coverage;
		// directional detail belongs to the correctly mapped cards above this core.
		// No position-projected noise either: it can swim on an animated skeleton.
		col = hair_palette(0.48, dark_fraction, white_fraction) * 0.94;
		ALPHA = 1.0;
		ROUGHNESS = 0.76;
		SPECULAR = 0.2;
		ANISOTROPY = 0.0; // The original tangent basis follows its unrelated UV projection.
		BACKLIGHT = col * 0.08;
	} else {
		// Generated card contract: U across, UV.y = 1 at the root and 0 at the tip.
		// COLOR.r is a deterministic card variation; COLOR.g marks generated cards.
		float u = clamp(UV.x, 0.0, 1.0);
		float v = clamp(1.0 - UV.y, 0.0, 1.0);
		// Recover the stored 8-bit value before hashing. Interpolation roundoff
		// otherwise makes a constant card seed sparkle and its tips turn stippled.
		float card = COLOR.g > 0.5 ? floor(COLOR.r * 255.0 + 0.5) : 0.0;
		float clumps = clamp(strand_count * 0.4, 4.0, 10.0);
		float lane_position = u * clumps;
		float lane = floor(lane_position);
		float lane_edge = abs(fract(lane_position) * 2.0 - 1.0);
		float tip_length = 0.8 + 0.2 * hash(lane + card * 17.0 + 7.0);
		// Small pointed fingers at the end, with a full opaque root and mid-length.
		tip_length -= 0.055 * lane_edge * lane_edge;
		float tip_aa = max(fwidth(v), 0.012);
		float tip = 1.0 - smoothstep(tip_length - tip_aa, tip_length + tip_aa, v);
		float edge = smoothstep(0.0, 0.055, u) * (1.0 - smoothstep(0.945, 1.0, u));
		ALPHA = edge * tip;

		// Fine strands fade to their coverage average below one pixel. This keeps
		// silver strands from becoming dark veins or shimmering in 720p yard views.
		float wander = sin(v * 4.5 + card) * 0.004;
		float strand_position = (u + wander) * clamp(strand_count * 0.55, 4.0, 20.0);
		float strand = floor(strand_position);
		float resolved = 1.0 - smoothstep(0.45, 1.4, fwidth(strand_position));
		float dark_lock = 1.0 - step(clamp(dark_fraction, 0.0, 1.0), hash(strand + card * 19.0 + 13.0));
		dark_lock = mix(clamp(dark_fraction, 0.0, 1.0), dark_lock, resolved);
		float white_lock = 1.0 - step(clamp(white_fraction, 0.0, 1.0), hash(lane + card * 23.0 + 11.0));
		float lock_resolved = 1.0 - smoothstep(0.65, 1.8, fwidth(lane_position));
		white_lock = mix(clamp(white_fraction, 0.0, 1.0), white_lock, lock_resolved);
		float streak = 1.0 + (hash(strand + card * 11.0 + 3.0) - 0.5) * 0.14 * resolved;
		float card_tone = 0.98 + 0.04 * hash(card + 31.0);
		// A tiny cylindrical strand profile breaks up flat panels in close-ups.
		// It fades with the strand color as its width becomes subpixel.
		NORMAL_MAP.x += sin(strand_position * TAU) * 0.045 * resolved;
		col = hair_palette(v, dark_lock, white_lock) * streak * card_tone;
		// Slight root depth makes overlapping locks readable without painting stripes.
		col *= mix(0.93, 1.0, smoothstep(0.0, 0.4, v));
		// Broad, soft highlights avoid a silver-looking panel at grazing angles,
		// including Compatibility where directional anisotropy is unavailable.
		ROUGHNESS = 0.78 + 0.04 * hash(card + 43.0);
		SPECULAR = 0.22;
		ANISOTROPY = 0.65;
		BACKLIGHT = col * 0.2;
	}
	ALBEDO = col;
}
