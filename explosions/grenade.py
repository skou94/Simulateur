# explosions/grenade.py — Paramètres et état d'une explosion de grenade
import math
import random
from physics import zones_dommages_grenade, densite_atmosphere


class ExplosionGrenade:
    """Gère l'état d'une explosion de grenade (fragmentation + souffle)."""

    def __init__(self, x: float, y: float, masse_tnt_g: float, altitude_m: float):
        self.x = x
        self.y = y
        self.masse_tnt_g = masse_tnt_g
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_grenade(masse_tnt_g, rho)

        # Durée totale de l'animation (secondes)
        self.duree = 2.5
        self.temps = 0.0
        self.termine = False

        # Vitesse d'expansion de l'onde de choc (m/s simulés → pixels/s calculés ailleurs)
        rayon_max = self.zones["leger"]
        self.vitesse_onde = rayon_max / self.duree if self.duree > 0 else 1.0

        # Fragments
        nb_fragments = max(20, int(masse_tnt_g * 0.8))
        nb_fragments = min(nb_fragments, 120)
        self.fragments = [_creer_fragment(x, y) for _ in range(nb_fragments)]

    def mettre_a_jour(self, dt: float):
        """Avance l'animation de dt secondes."""
        if self.termine:
            return
        self.temps += dt
        if self.temps >= self.duree:
            self.termine = True

        for f in self.fragments:
            f["x"] += f["vx"] * dt
            f["y"] += f["vy"] * dt
            f["vy"] += 200 * dt  # gravité en pixels
            f["vie"] -= dt
            # Décélération (résistance de l'air)
            f["vx"] *= (1 - 0.8 * dt)
            f["vy"] *= (1 - 0.5 * dt)

    @property
    def rayon_onde_px_ratio(self) -> float:
        """Ratio d'avancement de l'onde de choc (0→1)."""
        return min(self.temps / self.duree, 1.0)

    @property
    def rayon_onde_m(self) -> float:
        """Rayon actuel de l'onde en mètres simulés."""
        return self.zones["leger"] * self.rayon_onde_px_ratio


def _creer_fragment(cx: float, cy: float) -> dict:
    angle = random.uniform(0, 2 * math.pi)
    vitesse = random.uniform(150, 600)
    return {
        "x": cx,
        "y": cy,
        "vx": math.cos(angle) * vitesse,
        "vy": math.sin(angle) * vitesse,
        "vie": random.uniform(0.4, 1.2),
        "taille": random.uniform(2, 5),
    }
