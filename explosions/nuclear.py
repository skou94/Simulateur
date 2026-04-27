# explosions/nuclear.py — Explosion nucléaire (fireball, onde, champignon, radiation)
import math
import random
from physics import zones_dommages_nucleaire, densite_atmosphere, rayon_boule_feu


class ExplosionNucleaire:
    """Bombe atomique : fireball, onde de choc, champignon, radiation thermique."""

    def __init__(self, x: float, y: float, puissance_kt: float, altitude_m: float):
        self.x = x
        self.y = y
        self.puissance_kt = puissance_kt
        self.altitude_m = altitude_m
        rho = densite_atmosphere(altitude_m)
        self.zones = zones_dommages_nucleaire(puissance_kt, rho)

        # Durée plus longue pour le nucléaire
        self.duree = 8.0
        self.temps = 0.0
        self.termine = False

        rayon_max = self.zones["leger"]
        self.vitesse_onde = rayon_max / self.duree if self.duree > 0 else 1.0

        # Champignon : paramètres
        self.hauteur_champignon_max = rayon_boule_feu(puissance_kt) * 3.0
        self.rayon_tige = rayon_boule_feu(puissance_kt) * 0.15
        self.rayon_chapeau = rayon_boule_feu(puissance_kt) * 0.6

        # Particules de chaleur/cendres
        self.particules = [_creer_particule(x, y) for _ in range(200)]

    def mettre_a_jour(self, dt: float):
        if self.termine:
            return
        self.temps += dt
        if self.temps >= self.duree:
            self.termine = True

        for p in self.particules:
            p["x"] += p["vx"] * dt
            p["y"] += p["vy"] * dt
            p["vy"] -= p["flottabilite"] * dt  # monte (y inversé en pygame)
            p["vx"] *= (1 - 0.3 * dt)
            p["vie"] -= dt
            p["alpha"] = max(0, min(255, int(255 * p["vie"] / p["vie_max"])))

    @property
    def rayon_onde_px_ratio(self) -> float:
        return min(self.temps / self.duree, 1.0)

    @property
    def phase_fireball(self) -> float:
        """0→1 pendant la 1re seconde (expansion boule de feu)."""
        return min(self.temps / 1.0, 1.0)

    @property
    def phase_champignon(self) -> float:
        """0→1 à partir de t=0.5 s, durée 4 s (montée du champignon)."""
        t = max(0.0, self.temps - 0.5) / 4.0
        return min(t, 1.0)

    @property
    def rayon_fireball_actuel(self) -> float:
        """Rayon de la boule de feu en mètres (simulés)."""
        # Croît jusqu'à t=1 s puis décroît lentement
        t = self.temps
        r_max = self.zones["fireball"]
        if t <= 1.0:
            return r_max * math.sin(t * math.pi / 2)
        else:
            decay = math.exp(-(t - 1.0) / 1.5)
            return r_max * decay

    @property
    def alpha_fireball(self) -> int:
        """Transparence de la boule de feu (0–255)."""
        t = self.temps
        if t <= 0.5:
            return int(255 * t / 0.5)
        elif t <= 2.0:
            return 255
        else:
            decay = math.exp(-(t - 2.0) / 1.0)
            return int(255 * decay)


def _creer_particule(cx: float, cy: float) -> dict:
    angle = random.uniform(-math.pi, 0)  # vers le haut principalement
    vitesse = random.uniform(30, 200)
    vie = random.uniform(2.0, 6.0)
    return {
        "x": cx + random.uniform(-50, 50),
        "y": cy + random.uniform(-20, 20),
        "vx": math.cos(angle) * vitesse * random.uniform(0.3, 1.0),
        "vy": math.sin(angle) * vitesse,
        "flottabilite": random.uniform(20, 80),
        "vie": vie,
        "vie_max": vie,
        "alpha": 255,
        "taille": random.uniform(2, 6),
        "couleur_idx": random.randint(0, 2),  # 0=orange, 1=rouge, 2=gris
    }
