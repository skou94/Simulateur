// Sol : campagne, réseau de rues de la ville, brûlures, front de choc et zones d'effet.
shader_type spatial;

#include "res://Shaders/noise.gdshaderinc"

uniform vec2 ground_zero = vec2(0.0);
uniform float city_half = 5000.0;
uniform float block_pitch = 120.0;
uniform float street_width = 14.0;
uniform sampler2D block_map : filter_nearest;   // R = 1 pour un parc

uniform float scorch_radius = 0.0;   // fluence suffisante pour carboniser le sol
uniform float scorch_amount = 0.0;
uniform float front_radius = 0.0;    // position du front de choc
uniform float front_strength = 0.0;
uniform float glow_radius = 0.0;     // incandescence sous la boule de feu
uniform float glow_amount = 0.0;

uniform vec4 zone_radii = vec4(0.0);   // 140 kPa, 35 kPa, 7 kPa, brûlures 3e degré
uniform float radiation_radius = 0.0;  // 500 rad
uniform float zones_visible = 0.0;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float ring(float d, float r) {
	if (r <= 0.0) return 0.0;
	float w = max(r * 0.006, 3.0);
	return 1.0 - smoothstep(0.0, w, abs(d - r));
}

void fragment() {
	vec2 p = world_pos.xz;
	float n = fbm2(p * 0.004) * 0.6 + fbm2(p * 0.05) * 0.4;

	// Campagne : champs en damier légèrement teintés.
	vec2 field = floor(p / 260.0);
	float fh = hash12(field);
	vec3 col = mix(vec3(0.19, 0.25, 0.12), vec3(0.36, 0.34, 0.19), fh * 0.7 + n * 0.3);
	float rough = 0.95;

	if (abs(p.x) < city_half && abs(p.y) < city_half) {
		vec2 q = (p + city_half) / block_pitch;
		vec2 cell = floor(q);
		vec2 f = fract(q) * block_pitch;
		float hs = street_width * 0.5;
		bool street = f.x < hs || f.x > block_pitch - hs || f.y < hs || f.y > block_pitch - hs;
		vec2 size = vec2(textureSize(block_map, 0));
		float park = texture(block_map, (cell + 0.5) / size).r;
		if (street) {
			col = vec3(0.13, 0.13, 0.14) * (0.85 + 0.3 * n);
			rough = 0.8;
		} else if (park > 0.5) {
			col = mix(vec3(0.14, 0.24, 0.10), vec3(0.22, 0.30, 0.13), n);
		} else {
			col = vec3(0.40, 0.39, 0.36) * (0.8 + 0.35 * n);
		}
	}

	float d = distance(p, ground_zero);

	float s = scorch_amount * (1.0 - smoothstep(scorch_radius * 0.75, scorch_radius, d));
	col = mix(col, vec3(0.045, 0.035, 0.03), s * (0.75 + 0.25 * n));

	float band = front_strength * exp(-pow((d - front_radius) / max(front_radius * 0.015, 4.0), 2.0));
	col += vec3(0.30, 0.27, 0.22) * band;

	vec3 emission = vec3(1.0, 0.32, 0.06) * glow_amount * (1.0 - smoothstep(0.0, glow_radius, d)) * (0.6 + 0.8 * n);

	if (zones_visible > 0.5) {
		emission += vec3(1.0, 0.15, 0.1) * ring(d, zone_radii.x) * 2.0;
		emission += vec3(1.0, 0.55, 0.1) * ring(d, zone_radii.y) * 2.0;
		emission += vec3(1.0, 0.95, 0.3) * ring(d, zone_radii.z) * 2.0;
		emission += vec3(1.0, 0.3, 0.8) * ring(d, zone_radii.w) * 2.0;
		emission += vec3(0.3, 1.0, 0.4) * ring(d, radiation_radius) * 2.0;
	}

	ALBEDO = col;
	ROUGHNESS = rough;
	EMISSION = emission;
}
