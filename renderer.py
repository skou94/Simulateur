# renderer.py — Rendu visuel des explosions
import pygame
import math
import random

# Couleurs des zones de dommages
COULEUR_MORTEL  = (220, 50,  50,  80)   # rouge semi-transparent
COULEUR_MODERE  = (230, 140, 30,  70)   # orange
COULEUR_LEGER   = (60,  200, 60,  50)   # vert

# Couleurs de l'onde de choc
COULEUR_ONDE = (200, 220, 255)

# Échelle : 1 mètre = combien de pixels (ajusté dynamiquement)
ECHELLE_BASE = 0.15  # pixels/m pour petites explosions


class Renderer:
    def __init__(self, surface: pygame.Surface, zone_sim: pygame.Rect):
        """
        surface  : surface pygame principale
        zone_sim : rectangle de la zone de simulation (hors UI)
        """
        self.surface = surface
        self.zone = zone_sim
        # Surface de dessin dédiée à la simulation
        self.sim_surf = pygame.Surface((zone_sim.width, zone_sim.height), pygame.SRCALPHA)

    def effacer(self):
        """Efface la zone de simulation (fond sombre)."""
        self.sim_surf.fill((15, 15, 25))

    def _echelle(self, explosion) -> float:
        """Calcule l'échelle pixels/m adaptée au rayon max de l'explosion."""
        # On veut que le rayon leger occupe 40% de la demi-largeur
        rayon_m = max(1.0, _rayon_leger(explosion))
        demi = min(self.zone.width, self.zone.height) / 2 * 0.40
        return demi / rayon_m

    def _centre(self):
        return (self.zone.width // 2, self.zone.height // 2)

    def dessiner_explosion(self, explosion):
        """Point d'entrée : dessine l'explosion selon son type."""
        typ = type(explosion).__name__
        if typ == "ExplosionGrenade":
            self._dessiner_grenade(explosion)
        elif typ == "ExplosionConventionnelle":
            self._dessiner_conventionnelle(explosion)
        elif typ == "ExplosionNucleaire":
            self._dessiner_nucleaire(explosion)

    # ── Grenade ────────────────────────────────────────────────────────────────

    def _dessiner_grenade(self, expl):
        echelle = self._echelle(expl)
        cx, cy = self._centre()

        # Zones de dommages (cercles colorés)
        self._dessiner_zone(cx, cy, expl.zones["leger"],    echelle, COULEUR_LEGER)
        self._dessiner_zone(cx, cy, expl.zones["modere"],   echelle, COULEUR_MODERE)
        self._dessiner_zone(cx, cy, expl.zones["mortel"],   echelle, COULEUR_MORTEL)
        self._dessiner_zone(cx, cy, expl.zones["fragmentation"], echelle, (180, 180, 180, 60))

        # Onde de choc
        r_onde = expl.zones["leger"] * expl.rayon_onde_px_ratio * echelle
        alpha_onde = int(220 * (1 - expl.rayon_onde_px_ratio))
        self._cercle_alpha(cx, cy, int(r_onde), COULEUR_ONDE[:3], alpha_onde, epaisseur=2)

        # Flash central
        if expl.temps < 0.15:
            alpha_flash = int(255 * (1 - expl.temps / 0.15))
            r_flash = int(20 * (1 - expl.temps / 0.15))
            self._cercle_alpha(cx, cy, max(1, r_flash), (255, 255, 200), alpha_flash)

        # Fragments
        for f in expl.fragments:
            if f["vie"] > 0:
                fx = cx + (f["x"] - expl.x)
                fy = cy + (f["y"] - expl.y)
                alpha = int(255 * min(f["vie"], 1.0))
                r = max(1, int(f["taille"]))
                self._cercle_alpha(int(fx), int(fy), r, (200, 150, 80), alpha)

    # ── Conventionnelle ───────────────────────────────────────────────────────

    def _dessiner_conventionnelle(self, expl):
        echelle = self._echelle(expl)
        cx, cy = self._centre()

        self._dessiner_zone(cx, cy, expl.zones["leger"],  echelle, COULEUR_LEGER)
        self._dessiner_zone(cx, cy, expl.zones["modere"], echelle, COULEUR_MODERE)
        self._dessiner_zone(cx, cy, expl.zones["mortel"], echelle, COULEUR_MORTEL)

        # Boule de feu
        if expl.fireball_active:
            r_fb = int(expl.rayon_fireball_max * expl.rayon_fireball_ratio * echelle)
            if r_fb > 0:
                # Couche externe orange
                self._cercle_alpha(cx, cy, r_fb, (255, 120, 30),
                                   int(180 * expl.rayon_fireball_ratio))
                # Cœur blanc
                r_coeur = max(1, r_fb // 2)
                self._cercle_alpha(cx, cy, r_coeur, (255, 255, 220),
                                   int(220 * expl.rayon_fireball_ratio))

        # Onde de choc
        r_onde = expl.zones["leger"] * expl.rayon_onde_px_ratio * echelle
        alpha_onde = int(200 * (1 - expl.rayon_onde_px_ratio ** 0.6))
        self._cercle_alpha(cx, cy, int(r_onde), COULEUR_ONDE[:3], alpha_onde, epaisseur=3)

        # Débris
        for d in expl.debris:
            if d["vie"] > 0:
                dx = cx + (d["x"] - expl.x)
                dy = cy + (d["y"] - expl.y)
                alpha = int(255 * min(d["vie"] / 1.0, 1.0))
                r = max(1, int(d["taille"]))
                self._cercle_alpha(int(dx), int(dy), r, (160, 100, 50), alpha)

    # ── Nucléaire ─────────────────────────────────────────────────────────────

    def _dessiner_nucleaire(self, expl):
        echelle = self._echelle(expl)
        cx, cy = self._centre()

        # Zones thermiques (radiation)
        self._dessiner_zone(cx, cy, expl.zones["brulures_1"], echelle, (255, 200, 50, 40))
        self._dessiner_zone(cx, cy, expl.zones["brulures_3"], echelle, (255, 130, 0,  55))

        # Zones de souffle
        self._dessiner_zone(cx, cy, expl.zones["leger"],    echelle, COULEUR_LEGER)
        self._dessiner_zone(cx, cy, expl.zones["modere"],   echelle, COULEUR_MODERE)
        self._dessiner_zone(cx, cy, expl.zones["mortel"],   echelle, COULEUR_MORTEL)
        self._dessiner_zone(cx, cy, expl.zones["fireball"], echelle, (255, 80, 80, 90))

        # Boule de feu
        r_fb = int(expl.rayon_fireball_actuel * echelle)
        alpha_fb = expl.alpha_fireball
        if r_fb > 0:
            self._cercle_alpha(cx, cy, r_fb,      (255, 180, 30), alpha_fb)
            self._cercle_alpha(cx, cy, r_fb // 2, (255, 255, 180), min(255, alpha_fb + 40))

        # Champignon
        self._dessiner_champignon(expl, cx, cy, echelle)

        # Onde de choc
        r_onde = expl.zones["leger"] * expl.rayon_onde_px_ratio * echelle
        alpha_onde = int(220 * (1 - expl.rayon_onde_px_ratio ** 0.5))
        self._cercle_alpha(cx, cy, int(r_onde), (220, 240, 255), alpha_onde, epaisseur=4)

        # Particules de chaleur
        for p in expl.particules:
            if p["vie"] > 0 and p["alpha"] > 10:
                px = cx + (p["x"] - expl.x)
                py = cy + (p["y"] - expl.y)
                couleurs = [(255, 140, 0), (220, 50, 30), (180, 180, 180)]
                c = couleurs[p["couleur_idx"]]
                r = max(1, int(p["taille"]))
                self._cercle_alpha(int(px), int(py), r, c, p["alpha"])

    def _dessiner_champignon(self, expl, cx, cy, echelle):
        """Dessine le champignon atomique stylisé."""
        phase = expl.phase_champignon
        if phase <= 0:
            return

        hauteur_px = int(expl.hauteur_champignon_max * echelle * phase)
        rayon_tige = max(3, int(expl.rayon_tige * echelle))
        rayon_chapeau = max(5, int(expl.rayon_chapeau * echelle * phase))

        # Tige
        surf_tige = pygame.Surface((rayon_tige * 2, hauteur_px), pygame.SRCALPHA)
        alpha_tige = int(180 * phase)
        pygame.draw.rect(surf_tige, (150, 100, 60, alpha_tige),
                         (0, 0, rayon_tige * 2, hauteur_px))
        self.sim_surf.blit(surf_tige, (cx - rayon_tige, cy - hauteur_px))

        # Chapeau (ellipse)
        if rayon_chapeau > 2:
            surf_chapeau = pygame.Surface((rayon_chapeau * 2, rayon_chapeau), pygame.SRCALPHA)
            alpha_ch = int(200 * phase)
            pygame.draw.ellipse(surf_chapeau, (180, 120, 80, alpha_ch),
                                (0, 0, rayon_chapeau * 2, rayon_chapeau))
            # Cœur plus clair
            pygame.draw.ellipse(surf_chapeau, (220, 180, 120, alpha_ch // 2),
                                (rayon_chapeau // 3, rayon_chapeau // 4,
                                 rayon_chapeau * 4 // 3, rayon_chapeau // 2))
            self.sim_surf.blit(surf_chapeau,
                               (cx - rayon_chapeau, cy - hauteur_px - rayon_chapeau // 2))

    # ── Helpers ───────────────────────────────────────────────────────────────

    def _dessiner_zone(self, cx, cy, rayon_m, echelle, couleur_rgba):
        r = int(rayon_m * echelle)
        if r < 2:
            return
        surf = pygame.Surface((r * 2, r * 2), pygame.SRCALPHA)
        pygame.draw.circle(surf, couleur_rgba, (r, r), r)
        pygame.draw.circle(surf, (*couleur_rgba[:3], min(255, couleur_rgba[3] + 60)),
                           (r, r), r, 2)
        self.sim_surf.blit(surf, (cx - r, cy - r))

    def _cercle_alpha(self, x, y, rayon, couleur_rgb, alpha, epaisseur=0):
        if rayon < 1 or alpha <= 0:
            return
        alpha = min(255, max(0, alpha))
        r = rayon
        surf = pygame.Surface((r * 2, r * 2), pygame.SRCALPHA)
        pygame.draw.circle(surf, (*couleur_rgb, alpha), (r, r), r, epaisseur)
        self.sim_surf.blit(surf, (x - r, y - r))

    def dessiner_overlay(self, explosion, police: pygame.font.Font):
        """Affiche les valeurs numériques en overlay."""
        if explosion is None:
            return

        typ = type(explosion).__name__
        lignes = []

        if typ == "ExplosionGrenade":
            from physics import pression_sadovskiy, densite_atmosphere
            rho = densite_atmosphere(explosion.altitude_m)
            masse = explosion.masse_tnt_g / 1000.0
            rayon_courant = explosion.rayon_onde_m
            pression = pression_sadovskiy(max(rayon_courant, 0.1), masse, rho)
            lignes = [
                f"Type : Grenade",
                f"Masse TNT : {explosion.masse_tnt_g:.0f} g",
                f"Rayon souffle : {rayon_courant:.1f} m",
                f"Pression actuelle : {pression:.1f} kPa",
                f"Rayon mortel : {explosion.zones['mortel']:.1f} m",
                f"Rayon modéré : {explosion.zones['modere']:.1f} m",
                f"Rayon fragmentation : {explosion.zones['fragmentation']:.1f} m",
            ]

        elif typ == "ExplosionConventionnelle":
            from physics import pression_sadovskiy, densite_atmosphere
            rho = densite_atmosphere(explosion.altitude_m)
            masse = explosion.masse_tnt_kg
            rayon_courant = explosion.zones["leger"] * explosion.rayon_onde_px_ratio
            pression = pression_sadovskiy(max(rayon_courant, 0.1), masse, rho)
            lignes = [
                f"Type : Bombe conventionnelle",
                f"Masse TNT : {masse:.1f} kg",
                f"Rayon souffle : {rayon_courant:.1f} m",
                f"Pression actuelle : {pression:.1f} kPa",
                f"Rayon mortel (>100 kPa) : {explosion.zones['mortel']:.1f} m",
                f"Rayon modéré (>35 kPa)  : {explosion.zones['modere']:.1f} m",
                f"Rayon léger (>7 kPa)    : {explosion.zones['leger']:.1f} m",
            ]

        elif typ == "ExplosionNucleaire":
            lignes = [
                f"Type : Bombe nucléaire",
                f"Puissance : {explosion.puissance_kt:.2f} kt",
                f"Rayon boule de feu : {explosion.zones['fireball']:.0f} m",
                f"Rayon mortel (>100 kPa) : {explosion.zones['mortel']:.0f} m",
                f"Rayon modéré (>35 kPa)  : {explosion.zones['modere']:.0f} m",
                f"Rayon léger (>7 kPa)    : {explosion.zones['leger']:.0f} m",
                f"Brûlures 3° : {explosion.zones['brulures_3']:.0f} m",
                f"Brûlures 1° : {explosion.zones['brulures_1']:.0f} m",
            ]

        y_off = 10
        for ligne in lignes:
            txt = police.render(ligne, True, (220, 220, 220))
            # Fond semi-transparent
            bg = pygame.Surface((txt.get_width() + 8, txt.get_height() + 4), pygame.SRCALPHA)
            bg.fill((0, 0, 0, 140))
            self.sim_surf.blit(bg, (6, y_off - 2))
            self.sim_surf.blit(txt, (10, y_off))
            y_off += txt.get_height() + 4

    def finaliser(self):
        """Copie la surface de simulation sur la surface principale."""
        self.surface.blit(self.sim_surf, self.zone.topleft)

    def dessiner_legende(self, police: pygame.font.Font):
        """Légende des couleurs de zones."""
        items = [
            ((220, 50,  50), "Zone mortelle (>100 kPa)"),
            ((230, 140, 30), "Zone modérée (35–100 kPa)"),
            ((60,  200, 60), "Zone légère (7–35 kPa)"),
        ]
        x0 = self.zone.width - 240
        y0 = self.zone.height - len(items) * 22 - 10
        for couleur, texte in items:
            pygame.draw.rect(self.sim_surf, couleur, (x0, y0, 14, 14))
            txt = police.render(texte, True, (200, 200, 200))
            self.sim_surf.blit(txt, (x0 + 18, y0))
            y0 += 22


def _rayon_leger(explosion) -> float:
    typ = type(explosion).__name__
    if typ == "ExplosionGrenade":
        return explosion.zones.get("leger", 1.0)
    elif typ == "ExplosionConventionnelle":
        return explosion.zones.get("leger", 1.0)
    elif typ == "ExplosionNucleaire":
        return explosion.zones.get("leger", 1.0)
    return 1.0
