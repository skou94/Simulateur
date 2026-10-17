// Mur de poussière soulevé par le front de choc (cylindre ouvert à l'échelle du front).
shader_type spatial;
render_mode cull_disabled, depth_draw_never, diffuse_lambert, specular_disabled;

#include "res://Shaders/noise.gdshaderinc"

uniform float strength = 0.0;

void fragment() {
	// UV.y : 0 en haut du cylindre, 1 au pied.
	float n = fbm2(vec2(UV.x * 180.0, UV.y * 4.0 - TIME * 0.6));
	float base = smoothstep(0.0, 0.9, UV.y);
	ALBEDO = vec3(0.56, 0.51, 0.44) * (0.7 + 0.5 * n);
	ROUGHNESS = 1.0;
	ALPHA = clamp(strength * base * (0.25 + 0.9 * n), 0.0, 0.85);
}
