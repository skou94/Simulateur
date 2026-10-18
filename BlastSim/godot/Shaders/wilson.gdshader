// Nuage de Wilson : condensation éphémère de la vapeur d'eau dans la détente qui suit le
// front de choc. Coquille blanche translucide, plus dense vue en incidence rasante.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

#include "res://Shaders/noise.gdshaderinc"

uniform float strength = 0.0;
uniform float ground_y = 0.0;   // altitude du sol dans le repère local de la sphère (m, avant mise à l'échelle)

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	float rim = 1.0 - abs(dot(NORMAL, VIEW));
	float n = fbm3(local_pos * 6.0 + vec3(0.0, TIME * 0.2, 0.0));
	// Rien sous le sol ; bande plus dense vers l'équateur, comme sur les films d'essais.
	float above = step(ground_y, local_pos.y);
	float band = 0.35 + 0.65 * smoothstep(0.75, 0.15, abs(local_pos.y - 0.15));
	// Brume diffuse et plutôt opaque, effilochée par le bruit (pas un reflet de bulle).
	float wisps = smoothstep(0.35, 0.75, n);
	ALBEDO = vec3(0.86, 0.87, 0.89) * (0.85 + 0.2 * n);
	ROUGHNESS = 1.0;
	ALPHA = clamp(strength * above * band * (0.45 + 0.35 * rim) * (0.25 + 0.95 * wisps), 0.0, 0.92);
}
