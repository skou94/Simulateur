// Colonne de fumée d'un incendie : panneau vertical tourné vers la caméra, couché par le
// vent en montant, qui s'élargit et s'éclaircit. Données d'instance : x = intensité, y = aléa.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never;

#include "res://Shaders/noise.gdshaderinc"

uniform vec3 wind_dir = vec3(1.0, 0.0, 0.0);   // direction sous le vent (horizontale)
uniform float lean = 0.8;                      // déport horizontal / hauteur au sommet
uniform float brightness = 1.0;                // éclairage ambiant (jour 1, nuit ~0.15)

varying vec2 uv;
varying float intensity;
varying float seed;

void vertex() {
	intensity = INSTANCE_CUSTOM.x;
	seed = INSTANCE_CUSTOM.y;
	uv = UV;
	vec3 base = MODEL_MATRIX[3].xyz;
	float sx = length(MODEL_MATRIX[0].xyz), sy = length(MODEL_MATRIX[1].xyz);
	float h = VERTEX.y + 0.5;                  // 0 au pied, 1 au sommet
	vec3 to_cam = INV_VIEW_MATRIX[3].xyz - base;
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), to_cam));
	// Panache qui s'élargit en montant et se couche sous le vent (profil parabolique).
	float spread = 0.35 + 1.3 * h;
	vec3 world = base + right * VERTEX.x * sx * spread + vec3(0.0, h * sy, 0.0) + wind_dir * lean * sy * h * h;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}

void fragment() {
	float y = 1.0 - uv.y;
	float x = uv.x * 2.0 - 1.0;
	float t = TIME * 0.12 + seed * 5.0;
	float n = fbm2(vec2(uv.x * 2.5 + seed * 7.0, y * 3.0 - t));
	float edge = smoothstep(1.0, 0.35, abs(x) + (n - 0.5) * 0.7);
	float fade = smoothstep(0.0, 0.08, y) * (1.0 - smoothstep(0.55, 1.0, y));
	vec3 col = mix(vec3(0.10, 0.09, 0.085), vec3(0.42, 0.40, 0.38), y) * (0.75 + 0.5 * n);
	ALBEDO = col * brightness;
	ALPHA = clamp(edge * fade * (0.35 + 0.6 * n) * intensity * 0.85, 0.0, 0.85);
}
