# city_texture.py — Texture de sol procédurale style interface de test isométrique
import math
import pygame

C_FOND    = (8, 10, 16)
C_GRILLE  = (25, 31, 46)
C_AXE     = (54, 78, 118)
C_MAJEUR  = (38, 52, 77)
C_TEXTE   = (132, 170, 245)
C_EAU     = (24, 52, 90)
C_BORD_EAU = (60, 108, 178)
C_PARC    = (18, 46, 34)
C_PARC_BORD = (42, 96, 68)
C_CENTRE  = (110, 164, 255)

TAILLE_TEX = 2048          # pixels de la texture carrée


def generer_fond_ville(taille_monde_m: float = 28000) -> pygame.Surface:
    """
    Génère un sol sombre de sandbox avec grille métrique, repères et rivière.
    taille_monde_m : largeur totale de la carte en mètres.
    """
    surf = pygame.Surface((TAILLE_TEX, TAILLE_TEX))
    surf.fill(C_FOND)

    scale = TAILLE_TEX / taille_monde_m  # pixels / mètre
    font = pygame.font.SysFont("monospace", 18)
    font_small = pygame.font.SysFont("monospace", 14)

    def m2px(x_m: float, z_m: float) -> tuple[int, int]:
        """Mètres (centrés sur le sandbox, +x=Est, +z=Sud) → pixels sur la texture."""
        px = int((x_m + taille_monde_m / 2) * scale)
        pz = int((z_m + taille_monde_m / 2) * scale)
        return px, pz

    cx_px, cz_px = TAILLE_TEX // 2, TAILLE_TEX // 2

    # ── Grille métrique ───────────────────────────────────────────────────────
    pas_mineur_m = _pick_step(taille_monde_m / 14)
    pas_majeur_m = pas_mineur_m * 5
    largeur_mineure = max(1, int(2 * scale * pas_mineur_m / 250))
    largeur_majeure = max(2, largeur_mineure + 1)

    i = 0
    while True:
        offset_m = i * pas_mineur_m
        offset_px = int(offset_m * scale)
        if offset_px > TAILLE_TEX // 2:
            break
        couleur = C_AXE if i == 0 else C_MAJEUR if i % 5 == 0 else C_GRILLE
        largeur = largeur_majeure if i % 5 == 0 or i == 0 else largeur_mineure
        for sign in (-1, 1):
            x = cx_px + sign * offset_px
            z = cz_px + sign * offset_px
            if 0 <= x < TAILLE_TEX:
                pygame.draw.line(surf, couleur, (x, 0), (x, TAILLE_TEX), largeur)
            if 0 <= z < TAILLE_TEX:
                pygame.draw.line(surf, couleur, (0, z), (TAILLE_TEX, z), largeur)
        i += 1

    # ── Diagonales de lecture isométrique ────────────────────────────────────
    for angle_deg in (30, 60, 120, 150):
        a = math.radians(angle_deg)
        ca, sa = math.cos(a), math.sin(a)
        L = TAILLE_TEX
        pygame.draw.line(
            surf,
            (18, 24, 36),
            (int(cx_px - ca * L), int(cz_px - sa * L)),
            (int(cx_px + ca * L), int(cz_px + sa * L)),
            2,
        )

    # ── Anneaux de distance ───────────────────────────────────────────────────
    rayon = pas_majeur_m
    while rayon < taille_monde_m * 0.45:
        pygame.draw.circle(surf, C_MAJEUR, (cx_px, cz_px), int(rayon * scale), 2)
        rayon += pas_majeur_m

    # ── Rivière centrale (tracé sinueux) ─────────────────────────────────────
    pts_riviere = []
    for x_m in range(-13500, 13500, 80):
        z_m = (-480
                + 260 * math.sin(x_m / 2100 + 0.2)
                - 180 * math.sin(x_m / 850 + 0.6)
                + 80  * math.sin(x_m / 380))
        pts_riviere.append(m2px(x_m, z_m))

    w_riviere = max(10, int(90 * scale))
    if len(pts_riviere) > 1:
        pygame.draw.lines(surf, C_EAU, False, pts_riviere, w_riviere)
        w_bord = max(2, int(22 * scale))
        pygame.draw.lines(surf, C_BORD_EAU, False, pts_riviere, w_bord)

    # ── Parcs ─────────────────────────────────────────────────────────────────
    parcs = [
        (-4600, -50,   900, 1400),
        ( 5900,  600,  750, 1250),
        ( -170, -870,  350,  230),
        (   50, -490,  220,  160),
        ( -750,  300,  500,  400),
        (  900, -900,  200,  200),
        (-1500, 3000, 1200, 1000),
        ( 2800, 3200,  600,  800),
    ]
    for x_m, z_m, dw, dh in parcs:
        px0, pz0 = m2px(x_m - dw, z_m - dh)
        px1, pz1 = m2px(x_m + dw, z_m + dh)
        r = pygame.Rect(min(px0,px1), min(pz0,pz1), abs(px1-px0), abs(pz1-pz0))
        r = r.clip(surf.get_rect())
        if r.width > 0 and r.height > 0:
            pygame.draw.ellipse(surf, C_PARC, r)
            pygame.draw.ellipse(surf, C_PARC_BORD, r, max(2, int(10*scale)))

    # ── Repères texte au sol ──────────────────────────────────────────────────
    _dessiner_etiquettes_distance(surf, font, font_small, cx_px, cz_px, scale, taille_monde_m, pas_majeur_m)

    # ── Repère central ────────────────────────────────────────────────────────
    r_place = max(4, int(60 * scale))
    pygame.draw.circle(surf, C_CENTRE, (cx_px, cz_px), r_place)
    pygame.draw.circle(surf, (210, 230, 255), (cx_px, cz_px), max(2, r_place // 3))

    return surf


def _pick_step(target_m: float) -> int:
    steps = [100, 200, 250, 500, 1000, 2000]
    return min(steps, key=lambda step: abs(step - target_m))


def _dessiner_etiquettes_distance(surf, font, font_small, cx_px, cz_px, scale, taille_monde_m, pas_majeur_m):
    titre = font.render("TEST GRID", True, (205, 225, 255))
    surf.blit(titre, (18, 18))
    sous_titre = font_small.render(f"{taille_monde_m/1000:.1f} km sandbox", True, C_TEXTE)
    surf.blit(sous_titre, (18, 42))

    max_rayon = taille_monde_m * 0.45
    distance = pas_majeur_m
    while distance < max_rayon:
        label = _fmt_distance(distance)
        txt = font_small.render(label, True, C_TEXTE)
        x = cx_px + int(distance * scale) + 8
        y = cz_px + 8
        if x + txt.get_width() < TAILLE_TEX:
            surf.blit(txt, (x, y))
        txt2 = font_small.render(label, True, C_TEXTE)
        x2 = cx_px + 8
        y2 = cz_px + int(distance * scale) + 8
        if y2 + txt2.get_height() < TAILLE_TEX:
            surf.blit(txt2, (x2, y2))
        distance += pas_majeur_m

    axes = [
        ("X+", (TAILLE_TEX - 48, cz_px + 6)),
        ("X-", (12, cz_px + 6)),
        ("Z+", (cx_px + 6, TAILLE_TEX - 28)),
        ("Z-", (cx_px + 6, 10)),
    ]
    for texte, pos in axes:
        surf.blit(font_small.render(texte, True, (164, 198, 255)), pos)


def _fmt_distance(distance_m: float) -> str:
    if distance_m >= 1000:
        return f"{distance_m/1000:.1f} km"
    return f"{int(distance_m)} m"
