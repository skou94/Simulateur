# explosions/neutron.py — Bombe à neutrons (ER — Enhanced Radiation)
# Petite boule de feu, fort rayonnement neutronique
import math
from physics import zones_dommages_neutron, densite_atmosphere, rayon_boule_feu


class ExplosionNeutron:
    """Arme à rayonnement renforcé : blast réduit, radiation létale étendue."""

    def __init__(self, x: float, y: float, puissance_kt: float, altitude_m: float):
        self.x = x
        self.y = y
        self.puissance_kt = puissance_kt
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_neutron(puissance_kt, rho)

        self.duree = 6.0
        self.temps = 0.0
        self.termine = False

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
    def rayon_fireball_actuel(self) -> float:
        r_max = self.zones["fireball"]
        t = self.temps
        if t <= 0.8:
            return r_max * math.sin(t / 0.8 * math.pi / 2)
        return r_max * math.exp(-(t - 0.8) / 1.2)

    @property
    def alpha_fireball(self) -> int:
        t = self.temps
        if t <= 0.4:
            return int(255 * t / 0.4)
        elif t <= 1.5:
            return 255
        return int(255 * math.exp(-(t - 1.5) / 0.8))

    @property
    def phase_champignon(self) -> float:
        return min(max(self.temps - 0.3, 0) / 3.5, 1.0)

    # Propriétés attendues par renderer3d pour champignon
    @property
    def hauteur_champignon_max(self) -> float:
        return rayon_boule_feu(self.puissance_kt) * 2.0

    @property
    def rayon_tige(self) -> float:
        return rayon_boule_feu(self.puissance_kt) * 0.1

    @property
    def rayon_chapeau(self) -> float:
        return rayon_boule_feu(self.puissance_kt) * 0.4
