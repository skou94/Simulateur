// Boule de feu : couleur de corps noir approchée selon la « chaleur » (1 = plasma blanc).
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never;

#include "res://Shaders/noise.gdshaderinc"

uniform float heat = 1.0;
uniform float opacity = 1.0;
uniform float seed = 0.0;

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
	float n = fbm3(VERTEX * 2.2 + vec3(seed, TIME * 0.15, 0.0));
	VERTEX += NORMAL * (n - 0.5) * 0.18 * (1.0 - heat * 0.7);
}

vec3 blackbody(float h) {
	vec3 dark = vec3(0.35, 0.05, 0.02);
	vec3 red = vec3(1.0, 0.25, 0.04);
	vec3 orange = vec3(1.0, 0.62, 0.18);
	vec3 white = vec3(1.0, 0.96, 0.88);
	if (h < 0.33) return mix(dark, red, h / 0.33);
	if (h < 0.66) return mix(red, orange, (h - 0.33) / 0.33);
	return mix(orange, white, (h - 0.66) / 0.34);
}

void fragment() {
	float n = fbm3(local_pos * 3.5 + vec3(seed, -TIME * 0.4, 0.0));
	float rim = 1.0 - abs(dot(NORMAL, VIEW));
	float h = clamp(heat * (0.7 + 0.6 * n) - rim * 0.25, 0.0, 1.0);
	float intensity = 1.0 + 40.0 * heat * heat * heat;
	ALBEDO = blackbody(h) * intensity;
	ALPHA = opacity * clamp(1.0 - pow(rim, 3.0) * 0.8, 0.0, 1.0);
}
