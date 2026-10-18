// Nuage en champignon volumétrique : marche de rayon dans une boîte englobante.
// Forme analytique (tore du chapeau, dôme, tige, jupe de poussière) érodée par du bruit,
// éclairage solaire avec ombrage interne, lueur des gaz encore chauds.
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, depth_test_disabled, blend_mix, fog_disabled;

#include "res://Shaders/noise.gdshaderinc"

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

uniform vec3 box_min;
uniform vec3 box_max;
uniform vec3 ground_center;          // pied de la tige (monde)
uniform float cap_y = 1000.0;        // hauteur du centre du chapeau au-dessus du sol
uniform float cap_r = 500.0;         // rayon du chapeau
uniform float ring_ratio = 0.55;     // rayon du tore / rayon du chapeau
uniform float stem_r = 60.0;         // rayon de la tige (0 : pas de tige)
uniform float stem_top = 800.0;      // haut de la tige
uniform float skirt_r = 0.0;         // rayon de la jupe de poussière au sol
uniform float density = 1.0;         // 0..1
uniform float heat = 0.0;            // 0..1 lueur interne
uniform float roll = 0.0;            // angle de roulement du tore (rad)
uniform float rise = 0.0;            // défilement vertical de la tige (m)
uniform float age = 1.0;             // 0 : nuage jeune, brun-rouge ; 1 : refroidi, gris-blanc
uniform vec3 smoke_color : source_color = vec3(0.62, 0.58, 0.54);
uniform vec3 dust_color : source_color = vec3(0.52, 0.45, 0.37);
uniform vec3 sun_dir = vec3(0.3, 0.8, 0.3);   // vers le soleil
uniform vec3 sun_color : source_color = vec3(1.0, 0.96, 0.9);
uniform float sun_energy = 1.2;
uniform vec3 ambient_color : source_color = vec3(0.45, 0.52, 0.62);

const int STEPS = 110;
const int LIGHT_STEPS = 3;

float smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

// Distance signée approchée à la forme du nuage (sans bruit), en mètres.
float shape(vec3 q) {
	float R = cap_r;
	float a = ring_ratio * R;
	float b = (1.0 - ring_ratio) * R * 0.85;
	vec2 tq = vec2(length(q.xz) - a, (q.y - cap_y) * 1.25);
	float d = length(tq) - b;
	// Dôme au-dessus du tore.
	vec3 dq = (q - vec3(0.0, cap_y + 0.18 * R, 0.0)) / vec3(1.0, 0.62, 1.0);
	d = smin(d, (length(dq) - 0.62 * R) * 0.62, 0.15 * R);
	if (stem_r > 0.0) {
		// Tige évasée au pied, rattachée au chapeau.
		float w = stem_r * (1.0 + 1.2 * exp(-max(q.y, 0.0) / (0.08 * R + 1.0)));
		float ds = max(length(q.xz) - w, max(-q.y, q.y - stem_top));
		d = smin(d, ds, 0.2 * R);
	}
	if (skirt_r > 0.0) {
		vec3 sq = q / vec3(skirt_r, 0.12 * skirt_r + 20.0, skirt_r);
		d = smin(d, (length(sq) - 1.0) * min(skirt_r, 0.12 * skirt_r + 20.0), 0.1 * R);
	}
	return d;
}

// Relief du nuage en un point de l'espace de bruit : trois échelles, dont un bruit
// « en choux-fleurs » (1 − |2n − 1|) pour les bourgeonnements. Fréquences limitées : le pas
// de marche doit résoudre le plus petit détail. Le fbm reste proche de sa moyenne
// (~0.5 ± 0.1) : on étire son contraste.
float relief(vec3 nc) {
	float n1 = fbm3(nc);
	float n2 = 1.0 - abs(vnoise3(nc * 2.1) * 2.0 - 1.0);
	float n3 = 1.0 - abs(vnoise3(nc * 4.7) * 2.0 - 1.0);
	return clamp((n1 * 0.45 + n2 * 0.35 + n3 * 0.2 - 0.5) * 2.6 + 0.5, 0.0, 1.0);
}

// Le chapeau roule (rotation autour du cercle du tore), la tige monte. On mélange les
// reliefs des deux repères, jamais leurs coordonnées (ce qui étirerait le bruit en stries).
float cloud_relief(vec3 q) {
	float R = cap_r;
	float scale = 0.26 * R;
	float in_cap = smoothstep(stem_top - 0.3 * R, stem_top + 0.2 * R, q.y);
	float rising = 0.0, rolled = 0.0;
	if (in_cap < 0.999) rising = relief((q - vec3(0.0, rise, 0.0)) / scale);
	if (in_cap > 0.001) {
		float a = ring_ratio * R;
		float phi = atan(q.z, q.x);
		vec2 tq = vec2(length(q.xz) - a, q.y - cap_y);
		float c = cos(roll), s = sin(roll);
		vec2 rt = vec2(c * tq.x - s * tq.y, s * tq.x + c * tq.y);
		rolled = relief(vec3(cos(phi) * (a + rt.x), cap_y + rt.y, sin(phi) * (a + rt.x)) / scale);
	}
	return mix(rising, rolled, in_cap);
}

// Densité : forme érodée par le relief. detail sert aussi à la teinte (creux plus sombres).
float sample_density(vec3 q, out float core, out float detail) {
	float d = shape(q);
	float R = cap_r;
	core = 0.0;
	detail = 0.0;
	if (d > 0.2 * R) return 0.0;
	float base = clamp(-d / (0.10 * R) + 0.5, 0.0, 1.0);
	detail = cloud_relief(q);
	core = clamp(-d / (0.6 * R), 0.0, 1.0);
	return clamp(base * 2.0 - (1.0 - detail) * 1.45, 0.0, 1.0) * density;
}

vec3 blackbody(float h) {
	return mix(vec3(0.5, 0.06, 0.02), mix(vec3(1.0, 0.35, 0.06), vec3(1.0, 0.75, 0.35), clamp(h * 2.0 - 1.0, 0.0, 1.0)),
	           clamp(h * 2.0, 0.0, 1.0));
}

void fragment() {
	vec3 ro = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 rd = normalize(wp - ro);

	vec3 inv = 1.0 / rd;
	vec3 t0 = (box_min - ro) * inv, t1 = (box_max - ro) * inv;
	vec3 tlo = min(t0, t1), thi = max(t0, t1);
	float tmin = max(max(max(tlo.x, tlo.y), tlo.z), 0.0);
	float tmax = min(min(thi.x, thi.y), thi.z);

	// Arrêt sur la géométrie de la scène (bâtiments, sol) devant le nuage.
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene = length(view.xyz / view.w);
	tmax = min(tmax, scene);
	if (tmax <= tmin || density < 0.002) discard;

	float R = cap_r;
	float sigma = 7.0 / R;                    // extinction : quelques épaisseurs optiques à travers le chapeau
	// Marche adaptative : grands sauts dans le vide (guidés par la distance à la forme),
	// pas fins et réguliers dans le nuage, pour que la tige mince soit bien échantillonnée.
	float dt = clamp((tmax - tmin) / 48.0, 0.02 * R, 0.045 * R);
	float t = tmin + dt * hash12(SCREEN_UV * 917.0 + TIME);
	float trans = 1.0;
	vec3 col = vec3(0.0);
	vec3 L = normalize(sun_dir);
	for (int i = 0; i < STEPS; i++) {
		if (t > tmax) break;
		vec3 q = ro + rd * t - ground_center;
		float gap = shape(q) - 0.2 * R;
		if (gap > 0.0) {
			t += max(gap, dt);
			continue;
		}
		float core, detail;
		float dens = sample_density(q, core, detail);
		if (dens > 0.001) {
			// Ombrage interne vers le soleil, bruit compris : chaque bourgeon ombre ses voisins.
			float od = 0.0;
			float ls = 0.12 * R;
			for (int j = 1; j <= LIGHT_STEPS; j++) {
				float c2, d2;
				od += sample_density(q + L * ls * float(j) * (1.0 + 0.6 * float(j - 1)), c2, d2);
			}
			float tl = exp(-od * ls * sigma * 1.4);
			float height01 = clamp(q.y / max(cap_y + R, 1.0), 0.0, 1.0);
			// Jeune : brun-rouge (oxydes d'azote, poussières) ; refroidi : gris-blanc condensé.
			vec3 smoke = mix(vec3(0.36, 0.22, 0.15), smoke_color, age);
			vec3 albedo = mix(dust_color, smoke, smoothstep(0.05, 0.6, height01)) * (0.7 + 0.45 * detail);
			// Dessous à l'ombre, sommet au soleil ; un peu de lumière diffuse du ciel partout.
			float sky = 0.25 + 0.35 * height01;
			vec3 lit = albedo * (sun_color * sun_energy * (0.12 + 1.05 * tl) + ambient_color * sky);
			vec3 glow = blackbody(heat * 0.8) * heat * heat * 7.0 * (0.2 + core) * (1.0 - 0.5 * height01);
			float a = 1.0 - exp(-dens * sigma * dt);
			col += trans * a * (lit + glow);
			trans *= 1.0 - a;
			if (trans < 0.02) break;
		}
		t += dt;
	}
	float alpha = 1.0 - trans;
	if (alpha < 0.003) discard;
	ALBEDO = col / alpha;
	ALPHA = alpha;
}
