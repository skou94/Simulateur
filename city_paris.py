# city_paris.py — Génération procédurale des bâtiments pour un sandbox urbain
import math
import random
import numpy as np

PALETTE = [
    (0.27, 0.81, 0.95),
    (0.41, 0.89, 0.61),
    (0.99, 0.76, 0.30),
    (0.97, 0.45, 0.61),
    (0.68, 0.52, 0.98),
    (0.96, 0.59, 0.23),
]


def generer_batiments(
    n: int = 2500,
    seed: int = 42,
    world_size_m: float = 28000.0,
    footprint_scale: float = 1.0,
    height_scale: float = 1.0,
) -> list[dict]:
    rng = random.Random(seed)
    bats = []
    essais = 0
    half_world = max(world_size_m * 0.46, 500.0)
    zones = _build_zones(half_world)
    footprint_scale = max(0.2, footprint_scale)
    height_scale = max(0.2, height_scale)
    max_essais = int(n * 18)

    while len(bats) < n and essais < max_essais:
        essais += 1
        cx = rng.uniform(-half_world, half_world)
        cz = rng.uniform(-half_world, half_world)
        dist = math.hypot(cx, cz)

        densite, hmax = _zone(dist, zones)
        if rng.random() > densite:
            continue

        lx = rng.uniform(12, 40) * footprint_scale
        lz = rng.uniform(12, 40) * footprint_scale
        h  = rng.uniform(10, hmax) * height_scale

        pr, pg, pb = rng.choice(PALETTE)
        lum = rng.uniform(0.82, 1.08)
        r = min(1.0, pr * lum)
        g = min(1.0, pg * lum)
        b = min(1.0, pb * lum)

        bats.append({
            "cx": cx, "cz": cz,
            "lx": lx, "lz": lz, "h": h,
            "couleur": (r, g, b),
            "damage": 0.0,
            "damage_cible": 0.0,
            "_last_damage": -1.0,
        })
    return bats


def _build_zones(half_world: float) -> list[tuple[float, float, float]]:
    return [
        (half_world * 0.10, 0.92, 44),
        (half_world * 0.22, 0.74, 34),
        (half_world * 0.42, 0.58, 28),
        (half_world * 0.68, 0.36, 22),
        (half_world * 0.95, 0.18, 16),
    ]


def _zone(dist: float, zones: list[tuple[float, float, float]]) -> tuple[float, float]:
    for rmax, dens, hmax in zones:
        if dist <= rmax:
            return dens, hmax
    return 0.04, 10.0


def calculer_dommages(bats: list, zones: dict):
    """Applique les dommages à chaque bâtiment selon les zones de pression."""
    r_mortel = zones.get("mortel", 0)
    r_modere = zones.get("modere", 0)
    r_leger  = zones.get("leger",  0)

    for b in bats:
        dist = math.hypot(b["cx"], b["cz"])
        if dist <= r_mortel:
            b["damage_cible"] = 1.0
        elif dist <= r_modere:
            t = (dist - r_mortel) / max(r_modere - r_mortel, 1)
            b["damage_cible"] = 0.95 - 0.45 * t
        elif dist <= r_leger:
            t = (dist - r_modere) / max(r_leger - r_modere, 1)
            b["damage_cible"] = 0.5 - 0.45 * t
        else:
            b["damage_cible"] = max(b["damage_cible"], 0.0)


def animer_dommages(bats: list, dt: float):
    for b in bats:
        if b["damage"] < b["damage_cible"]:
            b["damage"] = min(b["damage_cible"], b["damage"] + dt * 0.6)


def geo_batiment(b: dict) -> np.ndarray:
    """
    Retourne les vertices+normales d'un bâtiment (boîte).
    Format : float32 tableau (N, 6) — [x,y,z, nx,ny,nz].
    La hauteur est réduite proportionnellement aux dommages.
    """
    cx, cz = b["cx"], b["cz"]
    lx, lz = b["lx"] / 2, b["lz"] / 2
    h = b["h"] * max(0.02, 1.0 - b["damage"] * 0.95)

    def quad(v0, v1, v2, v3, n):
        return [*v0, *n, *v1, *n, *v2, *n, *v0, *n, *v2, *n, *v3, *n]

    verts = (
        quad([cx-lx,0,cz+lz],[cx+lx,0,cz+lz],[cx+lx,h,cz+lz],[cx-lx,h,cz+lz],[0,0,1]) +
        quad([cx+lx,0,cz-lz],[cx-lx,0,cz-lz],[cx-lx,h,cz-lz],[cx+lx,h,cz-lz],[0,0,-1]) +
        quad([cx+lx,0,cz+lz],[cx+lx,0,cz-lz],[cx+lx,h,cz-lz],[cx+lx,h,cz+lz],[1,0,0]) +
        quad([cx-lx,0,cz-lz],[cx-lx,0,cz+lz],[cx-lx,h,cz+lz],[cx-lx,h,cz-lz],[-1,0,0]) +
        quad([cx-lx,h,cz-lz],[cx+lx,h,cz-lz],[cx+lx,h,cz+lz],[cx-lx,h,cz+lz],[0,1,0])
    )
    return np.array(verts, dtype=np.float32).reshape(-1, 6)
