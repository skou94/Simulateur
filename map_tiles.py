# map_tiles.py — Téléchargement et assemblage des tuiles OSM pour Paris
import os
import math
import urllib.request
import pygame

CACHE_DIR = os.path.join(os.path.dirname(__file__), ".tile_cache")
TILE_PX   = 256
SERVER    = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"

# Centre de Paris
LAT_PARIS = 48.8566
LON_PARIS = 2.3522
ZOOM      = 13   # ~4.9 km/tuile à cette latitude


def latlon_to_tile(lat: float, lon: float, zoom: int) -> tuple[int, int]:
    n = 2 ** zoom
    x = int((lon + 180) / 360 * n)
    y = int((1 - math.asinh(math.tan(math.radians(lat))) / math.pi) / 2 * n)
    return x, y


def tile_size_m(zoom: int, lat: float) -> float:
    """Taille en mètres d'une tuile OSM à la latitude donnée."""
    return 40075016.686 * math.cos(math.radians(lat)) / (2 ** zoom)


def _get_tile(z: int, x: int, y: int) -> pygame.Surface | None:
    os.makedirs(CACHE_DIR, exist_ok=True)
    path = os.path.join(CACHE_DIR, f"{z}_{x}_{y}.png")
    if os.path.exists(path):
        return pygame.image.load(path)
    url = SERVER.format(z=z, x=x, y=y)
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "ExplosionSim/1.0 educational"})
        with urllib.request.urlopen(req, timeout=8) as r:
            data = r.read()
        with open(path, "wb") as f:
            f.write(data)
        return pygame.image.load(path)
    except Exception as e:
        print(f"[tuile] {z}/{x}/{y} : {e}")
        return None


def assembler_paris(rayon: int = 3) -> tuple[pygame.Surface, float]:
    """
    Assemble une grille (2r+1)×(2r+1) de tuiles OSM centrée sur Paris.
    Retourne (surface, taille_totale_m).
    """
    cx, cy = latlon_to_tile(LAT_PARIS, LON_PARIS, ZOOM)
    n = rayon * 2 + 1
    taille_px = n * TILE_PX
    surf = pygame.Surface((taille_px, taille_px))
    surf.fill((90, 110, 90))  # vert de secours

    for dy in range(-rayon, rayon + 1):
        for dx in range(-rayon, rayon + 1):
            t = _get_tile(ZOOM, cx + dx, cy + dy)
            if t:
                surf.blit(t, ((dx + rayon) * TILE_PX, (dy + rayon) * TILE_PX))

    taille_m = n * tile_size_m(ZOOM, LAT_PARIS)
    return surf, taille_m
