# main.py — Simulateur d'explosions 3D — point d'entrée
import sys
import os

# Doit précéder tout import OpenGL (patch de compatibilité EGL/Wayland)
import OpenGL
OpenGL.CONTEXT_CHECKING = False

import pygame
import pygame_gui
from OpenGL.GL import *

sys.path.insert(0, os.path.dirname(__file__))

from renderer3d import Renderer3D
from ui import UI
from city_texture import generer_fond_ville
from city_paris import generer_batiments, calculer_dommages, animer_dommages
from explosions.grenade import ExplosionGrenade
from explosions.conventional import ExplosionConventionnelle
from explosions.nuclear import ExplosionNucleaire
from explosions.moab import ExplosionMOAB
from explosions.thermobaric import ExplosionThermobarique
from explosions.neutron import ExplosionNeutron
from explosions.tsar import ExplosionTsar

W, H       = 1280, 780
LARGEUR_UI = 320
FPS        = 60


def creer_explosion(type_nom: str, puissance: float, altitude: float) -> object | None:
    # Centre fictif pour les coordonnées 2D (non utilisé en 3D pour le rendu)
    cx, cy = 0, 0
    match type_nom:
        case "Grenade":         return ExplosionGrenade(cx, cy, puissance, altitude)
        case "Conventionnelle": return ExplosionConventionnelle(cx, cy, puissance, altitude)
        case "MOAB":            return ExplosionMOAB(cx, cy, puissance, altitude)
        case "Thermobarique":   return ExplosionThermobarique(cx, cy, puissance, altitude)
        case "Nucléaire":       return ExplosionNucleaire(cx, cy, puissance, altitude)
        case "Neutron":         return ExplosionNeutron(cx, cy, puissance, altitude)
        case "Mégatonne":       return ExplosionTsar(cx, cy, puissance, altitude)
    return None


def main():
    pygame.init()
    # Fenêtre OpenGL
    screen = pygame.display.set_mode(
        (W, H), pygame.OPENGL | pygame.DOUBLEBUF | pygame.RESIZABLE)
    pygame.display.set_caption("Simulateur d'Explosions 3D — Sandbox")
    horloge = pygame.time.Clock()

    # Manager pygame_gui (rendu sur surface séparée)
    manager = pygame_gui.UIManager((W, H))
    rect_ui = pygame.Rect(W - LARGEUR_UI, 0, LARGEUR_UI, H)
    ui      = UI(manager, rect_ui)

    # Surface pour le rendu de l'UI (sera uploadée comme texture)
    ui_surf = pygame.Surface((W, H), pygame.SRCALPHA)

    police = pygame.font.SysFont("monospace", 13)

    # Renderer 3D
    renderer = Renderer3D(W, H)

    def regenerer_sandbox() -> list[dict]:
        taille_monde_m = ui.taille_monde_m
        surface_km2 = (taille_monde_m / 1000.0) ** 2
        print("Génération du sol du sandbox…")
        carte_surf = generer_fond_ville(taille_monde_m=taille_monde_m)
        renderer.charger_carte(carte_surf)
        renderer.half_carte = taille_monde_m / 2
        renderer.camera.dist = max(2500.0, min(40000.0, taille_monde_m * 0.32))

        n_batiments = int(round(ui.densite_batiments_km2 * surface_km2))
        n_batiments = max(25, min(n_batiments, 12000))

        print("Génération des bâtiments…")
        batiments_gen = generer_batiments(
            n=n_batiments,
            world_size_m=taille_monde_m,
            footprint_scale=ui.superficie_batiments,
        )
        renderer.init_batiments(batiments_gen)
        print(
            f"Sandbox : {taille_monde_m/1000:.1f} km × {taille_monde_m/1000:.1f} km | "
            f"densité {ui.densite_batiments_km2:.0f} / km² | "
            f"superficie {ui.superficie_batiments:.2f}x | "
            f"{len(batiments_gen)} bâtiments"
        )
        return batiments_gen

    batiments = regenerer_sandbox()

    # État simulation
    explosion_active = None
    drag_last        = None   # pour l'orbite caméra

    running = True
    while running:
        dt = horloge.tick(FPS) / 1000.0

        # ── Événements ────────────────────────────────────────────────────────
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False
            elif event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                running = False

            # Contrôle caméra à la souris (hors panneau UI)
            elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                if event.pos[0] < W - LARGEUR_UI:
                    drag_last = event.pos
            elif event.type == pygame.MOUSEBUTTONUP and event.button == 1:
                drag_last = None
            elif event.type == pygame.MOUSEMOTION and drag_last:
                dx = event.pos[0] - drag_last[0]
                dy = event.pos[1] - drag_last[1]
                renderer.camera.orbiter(dx * 0.005, -dy * 0.005)
                drag_last = event.pos
            elif event.type == pygame.MOUSEWHEEL:
                if pygame.mouse.get_pos()[0] < W - LARGEUR_UI:
                    f = 0.9 if event.y > 0 else 1.1
                    renderer.camera.zoomer(f)

            manager.process_events(event)
            actions = ui.traiter(event)

            if actions["go"]:
                explosion_active = creer_explosion(
                    ui.type_explosion, ui.puissance, ui.altitude)
                if explosion_active:
                    calculer_dommages(batiments, explosion_active.zones)

            if actions["regen"]:
                explosion_active = None
                batiments = regenerer_sandbox()

            if actions["reset"]:
                explosion_active = None
                for b in batiments:
                    b["damage"]        = 0.0
                    b["damage_cible"]  = 0.0
                    b["_last_damage"]  = -1.0
                renderer.init_batiments(batiments)

        # ── Mise à jour ───────────────────────────────────────────────────────
        manager.update(dt)
        ui.maj_labels()

        if explosion_active:
            explosion_active.mettre_a_jour(dt)
        animer_dommages(batiments, dt)
        renderer.maj_batiments(batiments)

        # ── Rendu 3D ──────────────────────────────────────────────────────────
        renderer.dessiner(batiments, [explosion_active] if explosion_active else [])

        # ── Overlay UI ────────────────────────────────────────────────────────
        ui_surf.fill((0, 0, 0, 0))
        # Fond du panneau UI
        pygame.draw.rect(ui_surf, (22, 25, 38, 230), rect_ui)
        pygame.draw.line(ui_surf, (55, 62, 88), (W - LARGEUR_UI, 0), (W - LARGEUR_UI, H), 2)

        manager.draw_ui(ui_surf)
        ui.dessiner_legende(ui_surf)
        renderer.dessiner_stats(ui_surf, explosion_active, police)
        renderer.dessiner_annotations(ui_surf, batiments, police, LARGEUR_UI)

        # FPS
        fps_txt = police.render(f"FPS: {horloge.get_fps():.0f}", True, (80, 90, 120))
        ui_surf.blit(fps_txt, (8, H - 20))

        renderer.maj_overlay(ui_surf)
        renderer.dessiner_overlay()

        pygame.display.flip()

    pygame.quit()
    sys.exit(0)


if __name__ == "__main__":
    main()
