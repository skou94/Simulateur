// Flammes au-dessus d'un bâtiment en feu : panneau toujours tourné vers la caméra autour
// de la verticale, bruit qui monte, dégradé jaune-orange-rouge. Données d'instance :
// x = intensité (0..1), y = aléa.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

#include "res://Shaders/noise.gdshaderinc"

varying vec2 uv;
varying float intensity;
varying float seed;

void vertex() {
	intensity = INSTANCE_CUSTOM.x;
	seed = INSTANCE_CUSTOM.y;
	uv = UV;
	vec3 base = MODEL_MATRIX[3].xyz;
	float sx = length(MODEL_MATRIX[0].xyz), sy = length(MODEL_MATRIX[1].xyz);
	vec3 to_cam = INV_VIEW_MATRIX[3].xyz - base;
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), to_cam));
	// Le quad (−0.5..0.5) est posé sur le toit : sa base est au point d'ancrage.
	vec3 world = base + right * VERTEX.x * sx + vec3(0.0, (VERTEX.y + 0.5) * sy, 0.0);
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
}

void fragment() {
	float y = 1.0 - uv.y;                      // 0 en bas, 1 en haut
	float x = uv.x * 2.0 - 1.0;
	float t = TIME * 1.6 + seed * 37.0;
	float n = fbm2(vec2(uv.x * 3.0 + seed * 11.0, y * 2.2 - t));
	// Enveloppe : large au pied, effilée en haut, bords déchiquetés.
	float width = (1.0 - y) * 0.95 + 0.05;
	float body = smoothstep(width, width * 0.3, abs(x) + (n - 0.5) * 0.7) * smoothstep(1.0, 0.45, y + (n - 0.5) * 0.5);
	// Plusieurs langues de feu qui montent et vacillent.
	float tongues = smoothstep(0.38, 0.62, fbm2(vec2(uv.x * 4.0 + seed * 13.0, y * 1.5 - t * 1.3)) + (1.0 - y) * 0.45 - 0.2);
	float shape = body * tongues;
	float core = clamp(shape * (1.3 - 1.4 * y), 0.0, 1.0);
	vec3 col = mix(vec3(0.75, 0.10, 0.02), mix(vec3(1.0, 0.38, 0.04), vec3(1.0, 0.72, 0.25), core), clamp(shape * 1.4, 0.0, 1.0));
	ALBEDO = col * shape * intensity * 1.9;
	ALPHA = 1.0;
}
