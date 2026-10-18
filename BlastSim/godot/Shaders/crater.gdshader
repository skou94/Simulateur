// Cratère : terre retournée sur le bourrelet, sol vitrifié et noirci dans la cuvette.
shader_type spatial;

#include "res://Shaders/noise.gdshaderinc"

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float n = fbm2(world_pos.xz * 0.05) * 0.6 + fbm2(world_pos.xz * 0.6) * 0.4;
	float inside = COLOR.r;
	vec3 dirt = mix(vec3(0.30, 0.24, 0.17), vec3(0.42, 0.35, 0.26), n);
	vec3 glass = mix(vec3(0.06, 0.05, 0.045), vec3(0.14, 0.12, 0.10), n);
	ALBEDO = mix(dirt, glass, smoothstep(0.0, 0.4, inside));
	ROUGHNESS = mix(0.95, 0.55, inside);
}
