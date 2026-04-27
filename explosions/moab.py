# explosions/moab.py — MOAB GBU-43/B (Massive Ordnance Air Blast)
# 8 482 kg ANFO ≈ 11 tonnes TNT équivalent
import math
import random
from physics import zones_dommages_conventionnel, densite_atmosphere


class ExplosionMOAB:
    """GBU-43/B : la plus grande bombe conventionnelle non-nucléaire US."""

    MASSE_TNT_KG_DEFAULT = 11000.0  # équivalent TNT

    def __init__(self, x: float, y: float, masse_tnt_kg: float, altitude_m: float):
        self.x = x
        self.y = y
        self.masse_tnt_kg = masse_tnt_kg
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_conventionnel(masse_tnt_kg, rho)

        self.duree = 5.0
        self.temps = 0.0
        self.termine = False

        # Boule de feu plus grande qu'une bombe standard
        self.rayon_fireball_max = 2.8 * (masse_tnt_kg ** 0.33)
        self.debris = [_debris(x, y) for _ in range(180)]

    def mettre_a_jour(self, dt: float):
        if self.termine:
            return
        self.temps += dt
        if self.temps >= self.duree:
            self.termine = True
        for d in self.debris:
            d["x"]  += d["vx"] * dt
            d["y"]  += d["vy"] * dt
            d["vy"] += 120 * dt
            d["vx"] *= (1 - 0.35 * dt)
            d["vie"] -= dt

    @property
    def rayon_onde_px_ratio(self) -> float:
        return min(self.temps / self.duree, 1.0)

    @property
    def rayon_fireball_ratio(self) -> float:
        t = self.temps / 1.2
        if t >= 1.0:
            return 0.0
        return math.sin(t * math.pi)

    @property
    def fireball_active(self) -> bool:
        return self.temps < 1.2


def _debris(cx, cy):
    a = random.uniform(0, 2 * math.pi)
    v = random.uniform(60, 280)
    return {
        "x": cx, "y": cy,
        "vx": math.cos(a) * v, "vy": math.sin(a) * v,
        "vie": random.uniform(1.0, 3.5), "taille": random.uniform(4, 12),
    }
