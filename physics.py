# physics.py — Calculs physiques des explosions
import math


# Densité atmosphérique standard au niveau de la mer (kg/m³)
RHO_SEA_LEVEL = 1.225


def densite_atmosphere(altitude_m: float) -> float:
    """Densité atmosphérique selon le modèle ISA simplifié (kg/m³)."""
    if altitude_m < 0:
        altitude_m = 0
    # Formule barométrique simplifiée jusqu'à 11 000 m (troposphère)
    T0 = 288.15  # K
    L = 0.0065   # K/m
    g = 9.80665
    R = 287.05
    T = T0 - L * altitude_m
    if T <= 0:
        T = 1.0
    rho = RHO_SEA_LEVEL * (T / T0) ** (g / (R * L) - 1)
    return rho


def rayon_souffle_hopkinson(masse_tnt_kg: float, pression_cible_kpa: float,
                             rho: float = RHO_SEA_LEVEL) -> float:
    """
    Loi de Hopkinson-Cranz (cube root scaling).
    Retourne le rayon (m) auquel la pression de crête = pression_cible_kpa.
    masse_tnt_kg : équivalent TNT en kg
    pression_cible_kpa : pression de crête souhaitée (kPa)
    rho : densité atmosphérique (kg/m³)
    """
    if masse_tnt_kg <= 0 or pression_cible_kpa <= 0:
        return 0.0
    # Facteur de correction densité
    facteur_rho = (RHO_SEA_LEVEL / rho) ** (1 / 3)
    # Distance réduite Z (m/kg^1/3) correspondant à la pression cible
    # via formule de Sadovskiy inversée
    Z = _Z_depuis_pression(pression_cible_kpa)
    rayon = Z * (masse_tnt_kg ** (1 / 3)) * facteur_rho
    return rayon


def pression_sadovskiy(distance_m: float, masse_tnt_kg: float,
                        rho: float = RHO_SEA_LEVEL) -> float:
    """
    Pression de crête (kPa) à distance_m d'une explosion de masse_tnt_kg.
    Formule de Sadovskiy avec correction de densité.
    """
    if distance_m <= 0 or masse_tnt_kg <= 0:
        return 0.0
    facteur_rho = (rho / RHO_SEA_LEVEL) ** (1 / 3)
    Z = distance_m / ((masse_tnt_kg ** (1 / 3)) * facteur_rho)
    return _pression_depuis_Z(Z)


def _pression_depuis_Z(Z: float) -> float:
    """
    Pression de crête (kPa) depuis la distance réduite Z (m/kg^1/3).
    Formule de Sadovskiy (0.84/Z + 2.7/Z² + 7.15/Z³ atm) convertie en kPa.
    Valide pour 0.05 < Z < 40 m/kg^1/3.
    """
    if Z <= 0:
        return 1e9
    # Coefficients = valeurs en atm × 101.325 kPa/atm
    return 85.1 / Z + 273.6 / Z ** 2 + 724.5 / Z ** 3


def _Z_depuis_pression(pression_kpa: float) -> float:
    """Distance réduite Z (m/kg^1/3) correspondant à une pression (kPa), par dichotomie."""
    if pression_kpa <= 0:
        return 40.0
    lo, hi = 0.05, 40.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if _pression_depuis_Z(mid) > pression_kpa:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def zones_dommages_conventionnel(masse_tnt_kg: float,
                                  rho: float = RHO_SEA_LEVEL) -> dict:
    """
    Calcule les rayons des zones de dommages pour une explosion conventionnelle.
    Retourne un dict avec les clés 'mortel', 'modere', 'leger' (mètres).
    """
    return {
        "mortel":  rayon_souffle_hopkinson(masse_tnt_kg, 100.0, rho),   # > 100 kPa
        "modere":  rayon_souffle_hopkinson(masse_tnt_kg, 35.0, rho),    # 35–100 kPa
        "leger":   rayon_souffle_hopkinson(masse_tnt_kg, 7.0, rho),     # 7–35 kPa
    }


# ── Physique nucléaire ────────────────────────────────────────────────────────

# 1 kt TNT = 4.184e12 J
JOULES_PAR_KT = 4.184e12


def rayon_boule_feu(puissance_kt: float) -> float:
    """Rayon maximal de la boule de feu (m). Loi empirique : R ≈ 130 * W^0.41."""
    if puissance_kt <= 0:
        return 0.0
    return 130.0 * (puissance_kt ** 0.41)


def rayon_onde_choc_nucleaire(puissance_kt: float, pression_cible_kpa: float,
                               rho: float = RHO_SEA_LEVEL) -> float:
    """Rayon de l'onde de choc pour une bombe nucléaire (m)."""
    masse_tnt_kg = puissance_kt * 1e6  # 1 kt = 10^6 kg TNT
    return rayon_souffle_hopkinson(masse_tnt_kg, pression_cible_kpa, rho)


def dose_thermique(puissance_kt: float, distance_m: float,
                   rho: float = RHO_SEA_LEVEL) -> float:
    """
    Dose thermique reçue (cal/cm²) à distance_m.
    Simplification : 1/3 de l'énergie est thermique, propagation sphérique avec absorption.
    """
    if distance_m <= 0 or puissance_kt <= 0:
        return 0.0
    energie_thermique_j = 0.33 * puissance_kt * JOULES_PAR_KT
    # Absorption atmosphérique : facteur empirique
    absorption = math.exp(-distance_m / 15000.0)
    surface = 4 * math.pi * distance_m ** 2
    flux_j_m2 = energie_thermique_j * absorption / surface
    # 1 J/m² = 0.0239 cal/cm² * 1e-4 → 1 J/m² = 2.39e-6 cal/cm²...
    # Conversion : 1 cal/cm² = 41 840 J/m²
    return flux_j_m2 / 41840.0


def zones_dommages_nucleaire(puissance_kt: float,
                              rho: float = RHO_SEA_LEVEL) -> dict:
    """
    Zones de dommages d'une bombe nucléaire.
    Retourne un dict avec les clés 'fireball', 'mortel', 'modere', 'leger',
    'brulures_3', 'brulures_1' (mètres).
    """
    masse_tnt_kg = puissance_kt * 1e6
    return {
        "fireball":   rayon_boule_feu(puissance_kt),
        "mortel":     rayon_onde_choc_nucleaire(puissance_kt, 100.0, rho),
        "modere":     rayon_onde_choc_nucleaire(puissance_kt, 35.0, rho),
        "leger":      rayon_onde_choc_nucleaire(puissance_kt, 7.0, rho),
        # Rayon brûlures 3e degré ≈ dose > 12 cal/cm²
        "brulures_3": _rayon_dose_thermique(puissance_kt, 12.0, rho),
        # Rayon brûlures 1er degré ≈ dose > 3 cal/cm²
        "brulures_1": _rayon_dose_thermique(puissance_kt, 3.0, rho),
    }


def _rayon_dose_thermique(puissance_kt: float, dose_cible_cal: float,
                           rho: float = RHO_SEA_LEVEL) -> float:
    """Rayon (m) auquel la dose thermique = dose_cible_cal (cal/cm²), par dichotomie."""
    if puissance_kt <= 0:
        return 0.0
    lo, hi = 1.0, 500000.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if dose_thermique(puissance_kt, mid, rho) > dose_cible_cal:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


# ── Physique thermobarique (FAE) ─────────────────────────────────────────────

def zones_dommages_thermobarique(masse_kg: float,
                                  rho: float = RHO_SEA_LEVEL) -> dict:
    """
    Bombe thermobarique / FAE (Fuel-Air Explosive).
    Équivalent TNT ≈ 3× la masse en rayon de souffle (onde longue durée).
    """
    equiv_tnt = masse_kg * 3.0
    return {
        "mortel":  rayon_souffle_hopkinson(equiv_tnt, 100.0, rho),
        "modere":  rayon_souffle_hopkinson(equiv_tnt, 35.0, rho),
        "leger":   rayon_souffle_hopkinson(equiv_tnt, 7.0, rho),
        "equiv_tnt": equiv_tnt,
    }


def zones_dommages_neutron(puissance_kt: float,
                            rho: float = RHO_SEA_LEVEL) -> dict:
    """
    Bombe à neutrons (ER — Enhanced Radiation).
    Blast réduit (~10% de la puissance effective en souffle),
    rayonnement neutronique très élevé.
    Dose létale neutrons ≈ 800 rad → rayon ~2× le rayon mortel blast pour 1kt.
    """
    masse_blast = puissance_kt * 1e5  # ~10% en énergie cinétique
    r_mortel_rad = rayon_onde_choc_nucleaire(puissance_kt, 1.0, rho) * 1.8
    r_1000rad    = rayon_onde_choc_nucleaire(puissance_kt, 0.5, rho) * 2.2
    return {
        "fireball":   rayon_boule_feu(puissance_kt) * 0.4,
        "mortel":     rayon_souffle_hopkinson(masse_blast, 100.0, rho),
        "modere":     rayon_souffle_hopkinson(masse_blast, 35.0, rho),
        "leger":      rayon_souffle_hopkinson(masse_blast, 7.0, rho),
        "radiation_letale": r_mortel_rad,
        "radiation_1000rad": r_1000rad,
    }


# ── Physique grenade ──────────────────────────────────────────────────────────

def zones_dommages_grenade(masse_tnt_g: float,
                            rho: float = RHO_SEA_LEVEL) -> dict:
    """
    Zones de dommages d'une grenade (masse en grammes d'équivalent TNT).
    Retourne rayon de fragmentation estimé et zones de souffle.
    """
    masse_tnt_kg = masse_tnt_g / 1000.0
    # Rayon de fragmentation efficace : empirique, ≈ 15 * m^0.3 pour grenades
    rayon_frag = 15.0 * (masse_tnt_kg ** 0.3)
    return {
        "fragmentation": rayon_frag,
        "mortel":  rayon_souffle_hopkinson(masse_tnt_kg, 100.0, rho),
        "modere":  rayon_souffle_hopkinson(masse_tnt_kg, 35.0, rho),
        "leger":   rayon_souffle_hopkinson(masse_tnt_kg, 7.0, rho),
    }
