// Bâtiments (MultiMesh). Couleur d'instance = teinte de façade.
// Données d'instance : x = carbonisation, y = incendie, z = vitres brisées, w = aléa.
shader_type spatial;

#include "res://Shaders/noise.gdshaderinc"

// 1 la nuit : une partie des fenêtres est éclairée, jusqu'à ce que le souffle coupe le courant.
uniform float night = 0.0;

varying vec4 state;
varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	state = INSTANCE_CUSTOM;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float charred = state.x;
	float fire = state.y;
	float broken = state.z;
	float seed = state.w;

	vec3 albedo = COLOR.rgb;
	float rough = 0.85;
	float metal = 0.0;
	vec3 emission = vec3(0.0);

	if (abs(world_normal.y) < 0.5) {
		// Façade : grille de fenêtres, étages de 3.2 m et travées de 3 m.
		vec2 tangent = normalize(vec2(-world_normal.z, world_normal.x));
		float u = dot(world_pos.xz, tangent);
		vec2 g = vec2(u / 3.0, world_pos.y / 3.2);
		vec2 f = fract(g);
		float window = step(0.2, f.x) * step(f.x, 0.8) * step(0.28, f.y) * step(f.y, 0.78);
		if (window > 0.5 && world_pos.y > 1.5) {
			float h = hash12(floor(g) + seed * 97.0);
			vec3 glass = mix(vec3(0.08, 0.11, 0.15), vec3(0.32, 0.40, 0.48), h * 0.7);
			albedo = mix(glass, vec3(0.02), broken);
			rough = mix(0.12, 0.95, broken);
			metal = mix(0.5, 0.0, broken);
			float flicker = 0.6 + 0.4 * sin(TIME * (6.0 + 5.0 * h) + h * 40.0);
			emission = vec3(1.0, 0.42, 0.08) * fire * flicker * (0.5 + h) * 9.0;
			float lit = step(0.55, hash12(floor(g) * 1.37 + seed * 31.0)) * (1.0 - broken) * (1.0 - fire);
			emission += vec3(1.0, 0.78, 0.48) * lit * night * (0.8 + 0.6 * h);
		}
	} else {
		albedo *= 0.75;
	}

	albedo = mix(albedo, vec3(0.04, 0.035, 0.03), charred);
	float embers = fbm3(world_pos * 0.15 + vec3(0.0, -TIME * 0.8, 0.0));
	emission += vec3(1.0, 0.3, 0.05) * fire * embers * embers * 5.0;

	ALBEDO = albedo;
	ROUGHNESS = rough;
	METALLIC = metal;
	EMISSION = emission;
}
