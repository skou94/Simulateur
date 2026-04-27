# shaders.py — Shaders GLSL (OpenGL 3.3 core)

SOL_VERT = """
#version 330 core
in vec2 in_uv;
uniform mat4 u_mvp;
uniform float u_half;
out vec2 v_uv;
void main() {
    float x = (in_uv.x - 0.5) * 2.0 * u_half;
    float z = (in_uv.y - 0.5) * 2.0 * u_half;
    gl_Position = u_mvp * vec4(x, 0.0, z, 1.0);
    v_uv = in_uv;
}
"""

SOL_FRAG = """
#version 330 core
in vec2 v_uv;
uniform sampler2D u_tex;
out vec4 frag;
void main() { frag = texture(u_tex, v_uv); }
"""

BAT_VERT = """
#version 330 core
in vec3 in_pos;
in vec3 in_norm;
uniform mat4 u_mvp;
uniform vec3 u_col;
uniform float u_dmg;
out vec3 v_norm;
out vec3 v_col;
void main() {
    gl_Position = u_mvp * vec4(in_pos, 1.0);
    v_norm = in_norm;
    v_col  = mix(u_col, vec3(0.18, 0.12, 0.08), u_dmg);
}
"""

BAT_FRAG = """
#version 330 core
in vec3 v_norm;
in vec3 v_col;
out vec4 frag;
void main() {
    vec3 L = normalize(vec3(1.0, 2.0, 0.5));
    float d = clamp(dot(normalize(v_norm), L), 0.0, 1.0) * 0.65 + 0.35;
    frag = vec4(v_col * d, 1.0);
}
"""

FLAT_VERT = """
#version 330 core
in vec3 in_pos;
uniform mat4 u_mvp;
void main() { gl_Position = u_mvp * vec4(in_pos, 1.0); }
"""

FLAT_FRAG = """
#version 330 core
uniform vec4 u_col;
out vec4 frag;
void main() { frag = u_col; }
"""

OVERLAY_VERT = """
#version 330 core
in vec2 in_pos;
in vec2 in_uv;
out vec2 v_uv;
void main() {
    gl_Position = vec4(in_pos, 0.0, 1.0);
    v_uv = in_uv;
}
"""

OVERLAY_FRAG = """
#version 330 core
in vec2 v_uv;
uniform sampler2D u_tex;
out vec4 frag;
void main() { frag = texture(u_tex, v_uv); }
"""
