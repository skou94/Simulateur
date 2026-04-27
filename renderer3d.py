# renderer3d.py — Rendu OpenGL 3D (PyOpenGL + shaders GLSL 3.3)
import math
import ctypes
import threading

# Désactive le context-checking PyOpenGL et corrige la détection EGL/Wayland.
# Doit être fait AVANT tout autre import OpenGL.
import OpenGL
OpenGL.CONTEXT_CHECKING = False

from OpenGL import contextdata as _ctxdata, platform as _glplat

_orig_get_ctx = _ctxdata.getContext

def _patched_get_ctx(context=None):
    """Fallback : utilise l'ID du thread si la plateforme (EGL/Wayland) ne retourne pas de contexte."""
    if context is not None:
        return context
    ctx = _glplat.GetCurrentContext()
    return ctx if ctx else threading.get_ident()

_ctxdata.getContext = _patched_get_ctx  # type: ignore

import numpy as np
import pygame
from OpenGL.GL import *
from OpenGL.GL import shaders as gl_shaders

from shaders import SOL_VERT, SOL_FRAG, BAT_VERT, BAT_FRAG, FLAT_VERT, FLAT_FRAG
from shaders import OVERLAY_VERT, OVERLAY_FRAG
from city_paris import geo_batiment

N_SEG = 96   # segments pour les cercles/anneaux


# ── Helpers OpenGL ────────────────────────────────────────────────────────────

def _compile(vert_src: str, frag_src: str) -> int:
    vert = gl_shaders.compileShader(vert_src, GL_VERTEX_SHADER)
    frag = gl_shaders.compileShader(frag_src, GL_FRAGMENT_SHADER)
    return gl_shaders.compileProgram(vert, frag)


def _vao_vbo(data: np.ndarray, prog: int, attribs: list) -> tuple[int, int]:
    """Crée un VAO + VBO à partir d'un tableau numpy et d'une liste d'attributs."""
    vao = glGenVertexArrays(1)
    vbo = glGenBuffers(1)
    glBindVertexArray(vao)
    glBindBuffer(GL_ARRAY_BUFFER, vbo)
    glBufferData(GL_ARRAY_BUFFER, data.nbytes, data, GL_STATIC_DRAW)
    stride = data.shape[1] * 4  # float32 = 4 octets
    offset = 0
    for name, size in attribs:
        loc = glGetAttribLocation(prog, name)
        if loc >= 0:
            glEnableVertexAttribArray(loc)
            glVertexAttribPointer(loc, size, GL_FLOAT, GL_FALSE, stride,
                                  ctypes.c_void_p(offset * 4))
        offset += size
    glBindVertexArray(0)
    return vao, vbo


def _update_vbo(vbo: int, data: np.ndarray):
    glBindBuffer(GL_ARRAY_BUFFER, vbo)
    glBufferData(GL_ARRAY_BUFFER, data.nbytes, data, GL_DYNAMIC_DRAW)
    glBindBuffer(GL_ARRAY_BUFFER, 0)


# ── Matrices ──────────────────────────────────────────────────────────────────

def mat_perspective(fov_deg: float, aspect: float, near: float, far: float) -> np.ndarray:
    f = 1.0 / math.tan(math.radians(fov_deg) / 2)
    d = near - far
    return np.array([
        [f/aspect, 0,              0,                    0],
        [0,        f,              0,                    0],
        [0,        0, (far+near)/d,  (2*far*near)/d],
        [0,        0,             -1,                    0],
    ], dtype=np.float32)


def mat_lookat(eye: np.ndarray, center: np.ndarray, up: np.ndarray) -> np.ndarray:
    f = center - eye; f /= np.linalg.norm(f)
    r = np.cross(f, up); r /= np.linalg.norm(r)
    u = np.cross(r, f)
    return np.array([
        [ r[0],  r[1],  r[2], -np.dot(r, eye)],
        [ u[0],  u[1],  u[2], -np.dot(u, eye)],
        [-f[0], -f[1], -f[2],  np.dot(f, eye)],
        [    0,      0,     0,               1],
    ], dtype=np.float32)


# ── Caméra orbitale ───────────────────────────────────────────────────────────

class Camera:
    def __init__(self, aspect: float):
        self.aspect   = aspect
        self.theta    = math.radians(25)   # azimut
        self.phi      = math.radians(52)   # élévation
        self.dist     = 9000.0             # mètres
        self.cible    = np.zeros(3, dtype=np.float32)

    def eye(self) -> np.ndarray:
        s = math.sin(self.phi)
        return self.cible + self.dist * np.array([
            s * math.sin(self.theta),
            math.cos(self.phi),
            s * math.cos(self.theta),
        ], dtype=np.float32)

    def mvp(self) -> np.ndarray:
        proj = mat_perspective(45.0, self.aspect, 20.0, 60000.0)
        view = mat_lookat(self.eye(), self.cible, np.array([0,1,0], dtype=np.float32))
        return (proj @ view)

    def orbiter(self, dth: float, dph: float):
        self.theta += dth
        self.phi = np.clip(self.phi + dph, math.radians(8), math.radians(85))

    def zoomer(self, f: float):
        self.dist = float(np.clip(self.dist * f, 300, 40000))


# ── Renderer principal ────────────────────────────────────────────────────────

class Renderer3D:
    def __init__(self, largeur: int, hauteur: int):
        self.W, self.H = largeur, hauteur
        self.camera = Camera(largeur / hauteur)

        # Programmes shaders
        self.prog_sol     = _compile(SOL_VERT, SOL_FRAG)
        self.prog_bat     = _compile(BAT_VERT, BAT_FRAG)
        self.prog_flat    = _compile(FLAT_VERT, FLAT_FRAG)
        self.prog_overlay = _compile(OVERLAY_VERT, OVERLAY_FRAG)

        # VAO sol (plan UV)
        uv = np.array([[0,0],[1,0],[1,1],[0,0],[1,1],[0,1]], dtype=np.float32)
        self.vao_sol, self.vbo_sol = _vao_vbo(uv, self.prog_sol, [("in_uv", 2)])

        # VAO cercle (anneau onde de choc)
        angles = np.array([[a] for a in np.linspace(0, 2*math.pi, N_SEG+1)],
                           dtype=np.float32)
        # Stocké comme position 3D calculée côté CPU dans _draw_ring
        # On passe juste l'angle ; la conversion en (x,z) se fait dans _draw_ring

        # VAO overlay (quad plein écran)
        # Sans flip : V=0 = haut pygame → haut écran (NDC y=+1)
        ovl = np.array([
            [-1, 1, 0,0],[ 1, 1, 1,0],[ 1,-1, 1,1],
            [-1, 1, 0,0],[ 1,-1, 1,1],[-1,-1, 0,1],
        ], dtype=np.float32)
        self.vao_ovl, _ = _vao_vbo(ovl, self.prog_overlay,
                                    [("in_pos", 2), ("in_uv", 2)])

        # Textures
        self.tex_sol     = None
        self.tex_overlay = _create_tex(largeur, hauteur)
        self.half_carte  = 14000.0   # demi-taille de la carte en mètres

        # Bâtiments : liste de (vao, vbo, n_verts)
        self._bvao: list[tuple] = []

        glEnable(GL_DEPTH_TEST)
        glEnable(GL_BLEND)
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
        glClearColor(0.06, 0.07, 0.12, 1.0)

    # ── Sol ───────────────────────────────────────────────────────────────────

    def charger_carte(self, surf: pygame.Surface):
        self.tex_sol = _surface_to_tex(surf)

    # ── Bâtiments ─────────────────────────────────────────────────────────────

    def init_batiments(self, bats: list):
        self._bvao = []
        for b in bats:
            geo = geo_batiment(b)
            vao, vbo = _vao_vbo(geo, self.prog_bat, [("in_pos", 3), ("in_norm", 3)])
            self._bvao.append((vao, vbo, len(geo)))
            b["_last_damage"] = b["damage"]

    def maj_batiments(self, bats: list):
        """Recrée les VBOs des bâtiments dont le damage a changé."""
        for i, b in enumerate(bats):
            if abs(b["damage"] - b.get("_last_damage", -1)) > 0.015:
                geo = geo_batiment(b)
                _update_vbo(self._bvao[i][1], geo)
                b["_last_damage"] = b["damage"]

    # ── Rendu principal ───────────────────────────────────────────────────────

    def dessiner(self, bats: list, explosions: list):
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT)
        mvp = self.camera.mvp()

        self._draw_sol(mvp)
        self._draw_bats(mvp, bats)
        glDepthMask(GL_FALSE)
        for expl in explosions:
            self._draw_explosion(mvp, expl)
        glDepthMask(GL_TRUE)

    def _draw_sol(self, mvp: np.ndarray):
        if self.tex_sol is None:
            return
        glUseProgram(self.prog_sol)
        glUniformMatrix4fv(glGetUniformLocation(self.prog_sol, "u_mvp"),
                           1, GL_TRUE, mvp.astype(np.float32))
        glUniform1f(glGetUniformLocation(self.prog_sol, "u_half"), self.half_carte)
        glActiveTexture(GL_TEXTURE0)
        glBindTexture(GL_TEXTURE_2D, self.tex_sol)
        glUniform1i(glGetUniformLocation(self.prog_sol, "u_tex"), 0)
        glBindVertexArray(self.vao_sol)
        glDrawArrays(GL_TRIANGLES, 0, 6)
        glBindVertexArray(0)

    def _draw_bats(self, mvp: np.ndarray, bats: list):
        glUseProgram(self.prog_bat)
        glUniformMatrix4fv(glGetUniformLocation(self.prog_bat, "u_mvp"),
                           1, GL_TRUE, mvp.astype(np.float32))
        loc_col = glGetUniformLocation(self.prog_bat, "u_col")
        loc_dmg = glGetUniformLocation(self.prog_bat, "u_dmg")
        for i, b in enumerate(bats):
            if i >= len(self._bvao):
                break
            vao, _, n = self._bvao[i]
            glUniform3f(loc_col, *b["couleur"])
            glUniform1f(loc_dmg, float(b["damage"]))
            glBindVertexArray(vao)
            glDrawArrays(GL_TRIANGLES, 0, n)
        glBindVertexArray(0)

    # ── Effets d'explosion ────────────────────────────────────────────────────

    def _draw_explosion(self, mvp: np.ndarray, expl):
        typ  = type(expl).__name__
        zones = getattr(expl, "zones", {})
        ratio = getattr(expl, "rayon_onde_px_ratio", 0.0)
        zone_width = self._zone_outline_width()

        # Zones de dommages (disques colorés au sol)
        self._draw_disk(mvp, 0, 0, zones.get("leger",  0), (0.15, 0.75, 0.15, 0.30))
        self._draw_disk(mvp, 0, 0, zones.get("modere", 0), (0.85, 0.45, 0.08, 0.36))
        self._draw_disk(mvp, 0, 0, zones.get("mortel", 0), (0.82, 0.12, 0.12, 0.42))
        self._draw_ring(mvp, 0, 0, zones.get("leger", 0),  (0.35, 0.95, 0.35, 0.95), width=zone_width)
        self._draw_ring(mvp, 0, 0, zones.get("modere", 0), (1.00, 0.62, 0.15, 0.95), width=zone_width)
        self._draw_ring(mvp, 0, 0, zones.get("mortel", 0), (1.00, 0.25, 0.25, 1.00), width=zone_width)

        # Zones radiation (neutron)
        if typ == "ExplosionNeutron":
            self._draw_disk(mvp, 0, 0, zones.get("radiation_1000rad", 0),
                            (0.3, 0.9, 0.1, 0.20))
            self._draw_disk(mvp, 0, 0, zones.get("radiation_letale", 0),
                            (0.1, 0.9, 0.5, 0.25))
            self._draw_ring(mvp, 0, 0, zones.get("radiation_1000rad", 0),
                            (0.6, 1.0, 0.25, 0.9), width=zone_width)
            self._draw_ring(mvp, 0, 0, zones.get("radiation_letale", 0),
                            (0.25, 1.0, 0.65, 0.9), width=zone_width)

        # Onde de choc (anneau expansif)
        r_onde = zones.get("leger", 0) * ratio
        if r_onde > 1:
            alpha = 0.7 * (1 - ratio ** 0.5)
            self._draw_ring(mvp, 0, 0, r_onde, (0.8, 0.9, 1.0, alpha), width=max(zone_width, 120))

        # Boule de feu
        r_fb = 0.0
        a_fb = 200
        if typ in ("ExplosionNucleaire", "ExplosionTsar", "ExplosionNeutron"):
            r_fb = getattr(expl, "rayon_fireball_actuel", 0)
            a_fb = getattr(expl, "alpha_fireball", 200)
        elif typ in ("ExplosionConventionnelle", "ExplosionMOAB", "ExplosionThermobarique"):
            if getattr(expl, "fireball_active", False):
                r_fb = expl.rayon_fireball_max * getattr(expl, "rayon_fireball_ratio", 0)
                a_fb = int(200 * getattr(expl, "rayon_fireball_ratio", 0))

        if r_fb > 1:
            af = a_fb / 255.0
            self._draw_sphere(mvp, 0, 0, 0, r_fb, (1.0, 0.55, 0.08, af * 0.85))
            self._draw_sphere(mvp, 0, 0, 0, r_fb * 0.45, (1.0, 1.0, 0.65, af))

        # Champignon (nucléaire / neutron / tsar)
        if typ in ("ExplosionNucleaire", "ExplosionTsar", "ExplosionNeutron"):
            self._draw_mushroom(mvp, expl)

    def _draw_mushroom(self, mvp: np.ndarray, expl):
        phase = expl.phase_champignon
        if phase <= 0:
            return
        h_max   = expl.hauteur_champignon_max * phase
        r_tige  = expl.rayon_tige
        r_chap  = expl.rayon_chapeau

        # Tige : sphères empilées
        n = 10
        for i in range(n):
            t  = i / n
            cy = h_max * t
            r  = r_tige * (1 + 0.4 * math.sin(t * math.pi))
            al = 0.55 * phase * (1 - t * 0.25)
            self._draw_sphere(mvp, 0, cy, 0, r, (0.55, 0.38, 0.22, al))

        # Chapeau
        for k, (rf, al) in enumerate([(r_chap*1.1, 0.5), (r_chap, 0.6), (r_chap*0.7, 0.45)]):
            self._draw_sphere(mvp, 0, h_max, 0, rf * phase, (0.62, 0.42, 0.25, al * phase))

    def _zone_outline_width(self) -> float:
        return float(np.clip(self.camera.dist * 0.02, 40.0, 220.0))

    # ── Primitives géométriques ───────────────────────────────────────────────

    def _draw_disk(self, mvp, cx, cz, rayon, couleur):
        if rayon < 1:
            return
        # Disque = éventail de triangles depuis le centre
        pts = []
        for i in range(N_SEG + 1):
            a = 2 * math.pi * i / N_SEG
            pts.extend([0.0, 1.0, 0.0])         # centre (y=1m au-dessus du sol)
            pts.extend([cx + math.cos(a) * rayon, 1.0, cz + math.sin(a) * rayon])
            a2 = 2 * math.pi * (i + 1) / N_SEG
            pts.extend([cx + math.cos(a2) * rayon, 1.0, cz + math.sin(a2) * rayon])

        data = np.array(pts, dtype=np.float32).reshape(-1, 3)
        vao, vbo = _vao_vbo(data, self.prog_flat, [("in_pos", 3)])
        glUseProgram(self.prog_flat)
        glUniformMatrix4fv(glGetUniformLocation(self.prog_flat, "u_mvp"),
                           1, GL_TRUE, mvp.astype(np.float32))
        glUniform4f(glGetUniformLocation(self.prog_flat, "u_col"), *couleur)
        glBindVertexArray(vao)
        glDrawArrays(GL_TRIANGLES, 0, len(data))
        glBindVertexArray(0)
        glDeleteBuffers(1, [vbo])
        glDeleteVertexArrays(1, [vao])

    def _draw_ring(self, mvp, cx, cz, rayon, couleur, width=80):
        """Anneau (deux cercles concentriques en triangle strip)."""
        r0, r1 = rayon - width / 2, rayon + width / 2
        pts = []
        for i in range(N_SEG + 1):
            a = 2 * math.pi * i / N_SEG
            ca, sa = math.cos(a), math.sin(a)
            pts.extend([cx + ca * r0, 2.0, cz + sa * r0])
            pts.extend([cx + ca * r1, 2.0, cz + sa * r1])

        data = np.array(pts, dtype=np.float32).reshape(-1, 3)
        vao, vbo = _vao_vbo(data, self.prog_flat, [("in_pos", 3)])
        glUseProgram(self.prog_flat)
        glUniformMatrix4fv(glGetUniformLocation(self.prog_flat, "u_mvp"),
                           1, GL_TRUE, mvp.astype(np.float32))
        glUniform4f(glGetUniformLocation(self.prog_flat, "u_col"), *couleur)
        glBindVertexArray(vao)
        glDrawArrays(GL_TRIANGLE_STRIP, 0, len(data))
        glBindVertexArray(0)
        glDeleteBuffers(1, [vbo])
        glDeleteVertexArrays(1, [vao])

    def _draw_sphere(self, mvp, cx, cy, cz, rayon, couleur):
        """Sphère UV approximée par des quads."""
        N_PH, N_TH = 16, 32
        pts = []
        for i in range(N_PH):
            ph0 = math.pi * i / N_PH
            ph1 = math.pi * (i + 1) / N_PH
            for j in range(N_TH):
                th0 = 2 * math.pi * j / N_TH
                th1 = 2 * math.pi * (j + 1) / N_TH
                def v(ph, th):
                    return [cx + rayon * math.sin(ph) * math.cos(th),
                            cy + rayon * math.cos(ph),
                            cz + rayon * math.sin(ph) * math.sin(th)]
                for tri in [[v(ph0,th0),v(ph0,th1),v(ph1,th0)],
                            [v(ph1,th0),v(ph0,th1),v(ph1,th1)]]:
                    for p in tri:
                        pts.extend(p)

        data = np.array(pts, dtype=np.float32).reshape(-1, 3)
        vao, vbo = _vao_vbo(data, self.prog_flat, [("in_pos", 3)])
        glUseProgram(self.prog_flat)
        glUniformMatrix4fv(glGetUniformLocation(self.prog_flat, "u_mvp"),
                           1, GL_TRUE, mvp.astype(np.float32))
        glUniform4f(glGetUniformLocation(self.prog_flat, "u_col"), *couleur)
        glBindVertexArray(vao)
        glDrawArrays(GL_TRIANGLES, 0, len(data))
        glBindVertexArray(0)
        glDeleteBuffers(1, [vbo])
        glDeleteVertexArrays(1, [vao])

    # ── Overlay UI (pygame_gui → texture) ─────────────────────────────────────

    def maj_overlay(self, surf: pygame.Surface):
        # Pas de flip : cohérent avec _surface_to_tex, UV overlay déjà correct
        data = pygame.image.tobytes(surf, "RGBA")
        glBindTexture(GL_TEXTURE_2D, self.tex_overlay)
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, self.W, self.H,
                        GL_RGBA, GL_UNSIGNED_BYTE, data)
        glBindTexture(GL_TEXTURE_2D, 0)

    def dessiner_overlay(self):
        glDisable(GL_DEPTH_TEST)
        glUseProgram(self.prog_overlay)
        glActiveTexture(GL_TEXTURE0)
        glBindTexture(GL_TEXTURE_2D, self.tex_overlay)
        glUniform1i(glGetUniformLocation(self.prog_overlay, "u_tex"), 0)
        glBindVertexArray(self.vao_ovl)
        glDrawArrays(GL_TRIANGLES, 0, 6)
        glBindVertexArray(0)
        glEnable(GL_DEPTH_TEST)

    # ── Texte overlay (dessiné via pygame sur surface UI) ─────────────────────

    def dessiner_stats(self, surf: pygame.Surface, explosion, police: pygame.font.Font):
        """Dessine les valeurs numériques sur la surface UI pygame_gui."""
        if explosion is None:
            return
        typ   = type(explosion).__name__
        zones = getattr(explosion, "zones", {})
        lignes = [
            f"Type : {_nom_type(typ)}",
            f"Rayon mortel  : {zones.get('mortel', 0):.0f} m",
            f"Rayon modéré  : {zones.get('modere', 0):.0f} m",
            f"Rayon léger   : {zones.get('leger',  0):.0f} m",
        ]
        if "fireball" in zones:
            lignes.insert(1, f"Boule de feu  : {zones['fireball']:.0f} m")
        if "radiation_letale" in zones:
            lignes.append(f"Radiation létale : {zones['radiation_letale']:.0f} m")
        if "equiv_tnt" in zones:
            lignes.append(f"Equiv. TNT    : {zones['equiv_tnt']:.0f} kg")

        y = surf.get_height() - len(lignes) * 18 - 15
        for l in lignes:
            txt = police.render(l, True, (220, 220, 220))
            bg  = pygame.Surface((txt.get_width() + 8, txt.get_height() + 3), pygame.SRCALPHA)
            bg.fill((0, 0, 0, 140))
            surf.blit(bg, (8, y - 2))
            surf.blit(txt, (12, y))
            y += 18

    def dessiner_annotations(self, surf: pygame.Surface, bats: list, police: pygame.font.Font, ui_width: int):
        visibles = []
        half_h = self.H / 2
        scene_limit_x = self.W - ui_width - 8
        mvp = self.camera.mvp()
        for b in bats:
            pos = self._project_point(mvp, b["cx"], b["h"] * max(0.02, 1.0 - b["damage"] * 0.95), b["cz"])
            if pos is None:
                continue
            sx, sy, depth = pos
            if not (8 <= sx <= scene_limit_x and 8 <= sy <= self.H - 8):
                continue
            score = depth + abs(sy - half_h) / self.H
            visibles.append((score, sx, sy, b))

        for _, sx, sy, b in sorted(visibles, key=lambda item: item[0])[:14]:
            hauteur = b["h"] * max(0.02, 1.0 - b["damage"] * 0.95)
            txt = police.render(f"{hauteur:.0f} m", True, (235, 244, 255))
            bg = pygame.Surface((txt.get_width() + 8, txt.get_height() + 4), pygame.SRCALPHA)
            bg.fill((8, 12, 18, 180))
            surf.blit(bg, (sx - txt.get_width() // 2 - 4, sy - txt.get_height() - 16))
            surf.blit(txt, (sx - txt.get_width() // 2, sy - txt.get_height() - 14))

    def _project_point(self, mvp: np.ndarray, x: float, y: float, z: float):
        clip = mvp @ np.array([x, y, z, 1.0], dtype=np.float32)
        w = clip[3]
        if w <= 0:
            return None
        ndc = clip[:3] / w
        if not (-1.15 <= ndc[0] <= 1.15 and -1.15 <= ndc[1] <= 1.15 and -1.15 <= ndc[2] <= 1.15):
            return None
        sx = int((ndc[0] * 0.5 + 0.5) * self.W)
        sy = int((1.0 - (ndc[1] * 0.5 + 0.5)) * self.H)
        return sx, sy, float(ndc[2])


def _nom_type(cls: str) -> str:
    noms = {
        "ExplosionGrenade":       "Grenade",
        "ExplosionConventionnelle": "Bombe conventionnelle",
        "ExplosionNucleaire":     "Bombe atomique",
        "ExplosionMOAB":          "MOAB (GBU-43)",
        "ExplosionThermobarique": "Thermobarique / FAE",
        "ExplosionNeutron":       "Bombe à neutrons",
        "ExplosionTsar":          "Mégatonne / Tsar Bomba",
    }
    return noms.get(cls, cls)


# ── Utilitaires textures ──────────────────────────────────────────────────────

def _surface_to_tex(surf: pygame.Surface) -> int:
    # Pas de flip : pygame row 0 (haut) → texture V=0 → shaders UV y=0 = bord haut ✓
    data = pygame.image.tobytes(surf, "RGB")
    tex  = glGenTextures(1)
    glBindTexture(GL_TEXTURE_2D, tex)
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB, surf.get_width(), surf.get_height(),
                 0, GL_RGB, GL_UNSIGNED_BYTE, data)
    glGenerateMipmap(GL_TEXTURE_2D)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR)
    return tex


def _create_tex(w: int, h: int) -> int:
    tex = glGenTextures(1)
    glBindTexture(GL_TEXTURE_2D, tex)
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, None)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR)
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR)
    return tex
