shader_type canvas_item;
// Rounded-corner image for dialog header art (D-012). Works with any stretch mode
// because it measures the control's local pixels, not texture UVs.
uniform vec2 size = vec2(600.0, 160.0);
uniform float radius = 22.0;
varying vec2 local;

void vertex() { local = VERTEX; }

void fragment() {
	vec2 half_size = size * 0.5;
	vec2 q = abs(local - half_size) - (half_size - vec2(radius));
	float d = length(max(q, 0.0)) - radius;
	COLOR.a *= 1.0 - smoothstep(-1.0, 0.5, d);
}
