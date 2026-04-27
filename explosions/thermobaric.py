# explosions/thermobaric.py — Bombe thermobarique / FAE
# L'onde de surpression dure plus longtemps et est plus étendue que du TNT pur
import math
import random
from physics import zones_dommages_thermobarique, densite_atmosphere


class ExplosionThermobarique:
    """FAE : Fuel-Air Explosive — onde longue, pression étendue."""

    def __init__(self, x: float, y: float, masse_kg: float, altitude_m: float):
        self.x = x
        self.y = y
        self.masse_kg = masse_kg
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_thermobarique(masse_kg, rho)

        # Onde plus lente (propagation air enrichi)
        self.duree = 4.0
        self.temps = 0.0
        self.termine = False
        self.rayon_fireball_max = 1.8 * (masse_kg ** 0.33)

    def mettre_a_jour(self, dt: float):
        if self.termine:
            return
        self.temps += dt
        if self.temps >= self.duree:
            self.termine = True

    @property
    def rayon_onde_px_ratio(self) -> float:
        return min(self.temps / self.duree, 1.0)

    @property
    def rayon_fireball_ratio(self) -> float:
        # La boule de feu FAE est plus plate et dure plus longtemps
        t = self.temps / 2.0
        if t >= 1.0:
            return 0.0
        return math.sin(t * math.pi) ** 0.7

    @property
    def fireball_active(self) -> bool:
        return self.temps < 2.0
