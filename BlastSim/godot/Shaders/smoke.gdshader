// Bouffée de fumée/poussière éclairée, éclairée par en dessous tant que le nuage est chaud.
shader_type spatial;
render_mode cull_back, depth_draw_never, diffuse_lambert, specular_disabled;

#include "res://Shaders/noise.gdshaderinc"

uniform vec3 smoke_color : source_color = vec3(0.42, 0.38, 0.34);
uniform float density = 1.0;
uniform float glow = 0.0;
instance uniform float seed = 0.0;

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
	float n = fbm3(VERTEX * 1.6 + vec3(seed * 13.0, TIME * 0.03, seed));
	VERTEX += NORMAL * (n - 0.45) * 0.55;
}

void fragment() {
	float n = fbm3(local_pos * 2.8 + vec3(seed * 7.0, -TIME * 0.05, 0.0));
	float facing = abs(dot(NORMAL, VIEW));
	float underside = clamp(-local_pos.y * 0.5 + 0.5, 0.0, 1.0);
	ALBEDO = smoke_color * (0.65 + 0.7 * n);
	EMISSION = vec3(1.0, 0.38, 0.1) * glow * underside * (0.3 + n) * 4.0;
	ROUGHNESS = 1.0;
	ALPHA = clamp(density * smoothstep(0.0, 0.55, facing) * (0.55 + 0.7 * n), 0.0, 1.0);
}
