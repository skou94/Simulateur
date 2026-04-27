# explosions/conventional.py — Explosion de bombe conventionnelle
import math
import random
from physics import zones_dommages_conventionnel, densite_atmosphere


class ExplosionConventionnelle:
    """Bombe conventionnelle : souffle puissant + débris."""

    def __init__(self, x: float, y: float, masse_tnt_kg: float, altitude_m: float):
        self.x = x
        self.y = y
        self.masse_tnt_kg = masse_tnt_kg
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_conventionnel(masse_tnt_kg, rho)

        self.duree = 3.5
        self.temps = 0.0
        self.termine = False

        rayon_max = self.zones["leger"]
        self.vitesse_onde = rayon_max / self.duree if self.duree > 0 else 1.0

        # Débris plus gros et plus lents qu'une grenade
        nb_debris = min(60 + int(math.log10(max(masse_tnt_kg, 1)) * 20), 150)
        self.debris = [_creer_debris(x, y) for _ in range(nb_debris)]

        # Boule de feu initiale
        self.rayon_fireball_max = max(5.0, 2.5 * (masse_tnt_kg ** 0.33))  # mètres
        self.fireball_active = True

    def mettre_a_jour(self, dt: float):
        if self.termine:
            return
        self.temps += dt
        if self.temps >= self.duree:
            self.termine = True
        if self.temps > 0.8:
            self.fireball_active = False

        for d in self.debris:
            d["x"] += d["vx"] * dt
            d["y"] += d["vy"] * dt
            d["vy"] += 150 * dt
            d["vx"] *= (1 - 0.4 * dt)
            d["vie"] -= dt

    @property
    def rayon_onde_px_ratio(self) -> float:
        return min(self.temps / self.duree, 1.0)

    @property
    def rayon_fireball_ratio(self) -> float:
        """Ratio expansion boule de feu (croît puis disparaît)."""
        t = self.temps / 0.8
        if t >= 1.0:
            return 0.0
        # Croissance rapide puis disparition
        return math.sin(t * math.pi)


def _creer_debris(cx: float, cy: float) -> dict:
    angle = random.uniform(0, 2 * math.pi)
    vitesse = random.uniform(80, 350)
    return {
        "x": cx,
        "y": cy,
        "vx": math.cos(angle) * vitesse,
        "vy": math.sin(angle) * vitesse,
        "vie": random.uniform(0.8, 2.5),
        "taille": random.uniform(3, 8),
    }
