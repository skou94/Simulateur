# explosions/tsar.py — Bombe thermonucléaire de mégatonne (type Tsar Bomba)
# Réutilise ExplosionNucleaire avec des yields > 1 000 kt
from explosions.nuclear import ExplosionNucleaire


class ExplosionTsar(ExplosionNucleaire):
    """
    Bombe H de forte mégatonne.
    Identique à ExplosionNucleaire physiquement, mais la plage de puissance
    commence à 100 kt (tactique) jusqu'à 100 000 kt (50 MT = Tsar Bomba).
    La durée de l'animation est plus longue.
    """

    def __init__(self, x: float, y: float, puissance_kt: float, altitude_m: float):
        super().__init__(x, y, puissance_kt, altitude_m)
        # Animation plus longue pour rendre compte de l'échelle
        self.duree = 12.0
