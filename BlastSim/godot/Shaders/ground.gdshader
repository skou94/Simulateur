// Sol : campagne, réseau de rues de la ville, brûlures, front de choc et zones d'effet.
shader_type spatial;

#include "res://Shaders/noise.gdshaderinc"

uniform vec2 ground_zero = vec2(0.0);
uniform float city_half = 5000.0;
uniform float block_pitch = 120.0;
uniform float street_width = 14.0;
uniform sampler2D block_map : filter_nearest;   // R = 1 pour un parc

// Ville réelle : carte OSM (R = chaussée, G = parc, B = eau) sur le disque de rayon map_half.
uniform float use_map = 0.0;
uniform sampler2D map_tex : filter_linear, repeat_disable;
uniform float map_half = 2500.0;

uniform float scorch_radius = 0.0;   // fluence suffisante pour carboniser le sol
uniform float scorch_amount = 0.0;
uniform float front_radius = 0.0;    // position du front de choc
uniform float front_strength = 0.0;
uniform float glow_radius = 0.0;     // incandescence sous la boule de feu
uniform float glow_amount = 0.0;

// Retombées : dose cumulée (rad) sur une grille dans le repère du vent.
uniform float fallout_on = 0.0;
uniform sampler2D fallout_tex : filter_linear, repeat_disable;
uniform vec2 fallout_dir = vec2(1.0, 0.0);   // sous le vent (x, z)
uniform vec2 fallout_u = vec2(-1.0, 1.0);    // étendue sous le vent (min, max), m
uniform float fallout_v = 1.0;               // demi-largeur en travers, m

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

	if (use_map > 0.5) {
		if (length(p) < map_half) {
			vec4 m = texture(map_tex, (p + map_half) / (2.0 * map_half));
			vec3 pavement = vec3(0.40, 0.39, 0.36) * (0.8 + 0.35 * n);
			vec3 park = mix(vec3(0.14, 0.24, 0.10), vec3(0.22, 0.30, 0.13), n);
			vec3 water = mix(vec3(0.05, 0.11, 0.15), vec3(0.08, 0.16, 0.20), n);
			vec3 asphalt = vec3(0.13, 0.13, 0.14) * (0.85 + 0.3 * n);
			col = mix(pavement, park, m.g);
			col = mix(col, water, m.b);
			col = mix(col, asphalt, m.r);
			rough = mix(mix(0.9, 0.08, m.b), 0.8, m.r);
		}
	} else if (abs(p.x) < city_half && abs(p.y) < city_half) {
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

	if (fallout_on > 0.5) {
		vec2 rel = p - ground_zero;
		float u = dot(rel, fallout_dir);
		float v = dot(rel, vec2(-fallout_dir.y, fallout_dir.x));
		vec2 fuv = vec2((u - fallout_u.x) / (fallout_u.y - fallout_u.x), (v + fallout_v) / (2.0 * fallout_v));
		if (all(greaterThanEqual(fuv, vec2(0.0))) && all(lessThanEqual(fuv, vec2(1.0)))) {
			float dose = texture(fallout_tex, fuv).r;
			if (dose > 1.0) {
				// Une couleur par décade : 1-10, 10-100, 100-1000, > 1000 rad.
				float level = log(dose) / log(10.0);
				vec3 c = level < 1.0 ? vec3(0.95, 0.90, 0.20)
				       : level < 2.0 ? vec3(1.00, 0.55, 0.10)
				       : level < 3.0 ? vec3(0.95, 0.15, 0.10)
				       : vec3(0.65, 0.10, 0.75);
				float line = 1.0 - smoothstep(0.0, 0.04, min(fract(level), 1.0 - fract(level)));
				col = mix(col, c, 0.38);
				emission += c * (0.08 + 1.2 * line);
			}
		}
	}

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
