shader_type canvas_item;
// D-012 horse portrait card: tinted background behind the transparent render,
// rounded corners and a white rim, without clip_children (which fails in dialogs).
uniform vec4 background : source_color = vec4(0.99, 0.89, 0.93, 1.0);
uniform float corner = 0.5;   // corner radius as a fraction of the half-size
uniform float rim = 0.06;     // rim width as a fraction of the half-size

void fragment() {
	vec4 picture = texture(TEXTURE, UV);
	vec4 colour = vec4(mix(background.rgb, picture.rgb, picture.a), 1.0);
	vec2 p = abs(UV - 0.5) * 2.0;
	vec2 q = max(p - (1.0 - corner), 0.0);
	float d = length(q) / corner;                 // rounded-box distance, 1 at the edge
	float edge = max(max(p.x, p.y), d);
	float aa = max(fwidth(edge), 0.002);
	float inside = 1.0 - smoothstep(1.0 - aa, 1.0, edge);
	float ring = smoothstep(1.0 - rim - aa, 1.0 - rim, edge);
	colour.rgb = mix(colour.rgb, vec3(1.0), ring);
	COLOR = vec4(colour.rgb, inside);
}
