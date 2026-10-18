using BlastPhysics.Damage;

namespace BlastPhysics.Nuclear;

/// <summary>
/// Retombées radioactives locales selon le modèle WSEG-10 (Weapons Systems Evaluation
/// Group, 1959), tel que documenté par D. W. Hanifen, « Documentation and Analysis of
/// the WSEG-10 Fallout Prediction Model », AFIT, 1980. Champ « cas moyen » : une ellipse
/// le long de l'axe du vent, gaussienne en travers, avec une correction au vent.
/// <para>
/// Portage d'après « glasstone » (MIT), avec trois corrections d'unités évidentes de ce
/// portage : hauteur du nuage calculée avec le rendement en Mt (et non en kt), vent en
/// mph dans L0 et α1 (et non dans l'unité d'entrée), coordonnée sous le vent dans α2 ;
/// temps d'arrivée écrit sous forme dimensionnellement cohérente.
/// </para>
/// <para>
/// Décroissance de Way-Wigner : débit ∝ t^-1.2, d'où une dose cumulée de l'arrivée ta au
/// temps t de 5·Ḋ1·(ta^-0.2 − t^-0.2), Ḋ1 étant le débit normalisé à H+1 h.
/// </para>
/// Unités internes du modèle : milles terrestres, mph, kft, heures, roentgens (≈ rad).
/// </summary>
public sealed class Fallout
{
    private const double MetersPerMile = 1609.344;
    private const double MphPerMs = 2.236936;
    private const double KftPerKm = 3.280840;

    private readonly double _yieldMt, _ff, _windMph, _shear, _activity;
    private readonly double _hc, _s0, _s02, _sh, _tc, _l0, _l02, _sx, _sx2, _l, _l2, _n, _a1, _gammaNorm;
    private readonly double _dirX, _dirZ;

    /// <param name="windSpeedMs">Vitesse moyenne du vent dans la couche du nuage.</param>
    /// <param name="windTowardDeg">Direction vers laquelle souffle le vent (0 = nord, 90 = est).</param>
    /// <param name="shearMsPerKm">Cisaillement vertical du vent (étale le panache en travers).</param>
    public Fallout(NuclearDevice device, Fireball fireball, double windSpeedMs, double windTowardDeg,
                   double shearMsPerKm = 0.3)
    {
        _yieldMt = Math.Max(device.YieldKt / 1000.0, 1e-6);
        _ff = Math.Clamp(device.FissionFraction, 0.01, 1.0);
        _windMph = Math.Max(windSpeedMs, 0.5) * MphPerMs;
        _shear = shearMsPerKm * MphPerMs / KftPerKm;
        // Seule la part de la boule de feu en contact avec le sol aspire des débris activés :
        // les retombées locales deviennent négligeables quand elle ne touche plus le sol.
        ContactFraction = Math.Clamp(1.0 - fireball.HeightOfBurstM / fireball.AirburstMaxRadiusM, 0.0, 1.0);
        _activity = ContactFraction;

        double lnY = Math.Log(_yieldMt);
        double d = lnY + 2.42;
        _hc = 44.0 + 6.1 * lnY - 0.205 * Math.Abs(d) * d;
        _s0 = Math.Exp(0.7 + lnY / 3.0 - 3.25 / (4.0 + Math.Pow(lnY + 5.4, 2)));
        _s02 = _s0 * _s0;
        _sh = 0.18 * _hc;
        double h60 = _hc / 60.0;
        _tc = 1.0573203 * (12.0 * h60 - 2.5 * h60 * h60) * (1.0 - 0.5 * Math.Exp(-Math.Pow(_hc / 25.0, 2)));
        _l0 = _windMph * _tc;
        _l02 = _l0 * _l0;
        _sx2 = _s02 * (_l02 + 8.0 * _s02) / (_l02 + 2.0 * _s02);
        _sx = Math.Sqrt(_sx2);
        _l2 = _l02 + 2.0 * _sx2;
        _l = Math.Sqrt(_l2);
        _n = (_ff * _l02 + _sx2) / (_l02 + 0.5 * _sx2);
        _a1 = 1.0 / (1.0 + 0.001 * _hc * _windMph / _s0);
        _gammaNorm = 1.0 / (_l * Gamma(1.0 + 1.0 / _n));

        double a = windTowardDeg * Math.PI / 180.0;
        _dirX = Math.Sin(a);   // est = +X
        _dirZ = -Math.Cos(a);  // nord = −Z
    }

    /// <summary>Part de la boule de feu en contact avec le sol (0 : explosion aérienne).</summary>
    public double ContactFraction { get; }

    /// <summary>Hauteur du centre du nuage stabilisé selon WSEG, m.</summary>
    public double CloudCenterHeightM => _hc * 304.8;

    /// <summary>Direction sous le vent dans le plan local (X est, Z sud).</summary>
    public (double X, double Z) Downwind => (_dirX, _dirZ);

    /// <summary>Débit de dose normalisé à H+1 h (R/h ≈ rad/h) au point (x, z) relatif au point zéro, en m.</summary>
    public double DoseRateAtH1(double xM, double zM)
    {
        if (_activity <= 0) return 0.0;
        var (u, v) = ToWindFrame(xM, zM);
        double fx = _yieldMt * 2e6 * Phi(u) * G(u) * _ff;
        double xp = u + 2.0 * _sx;
        double sy = Math.Sqrt(_s02 + 8.0 * Math.Abs(xp) * _s02 / _l
                              + 2.0 * Math.Pow(_sx * _tc * _sh * _shear, 2) / _l2
                              + Math.Pow(xp * _l0 * _tc * _sh * _shear, 2) / (_l2 * _l2));
        double a2 = 1.0 / (1.0 + 0.001 * _hc * _windMph / _s0 * (1.0 - BuildingDamage.StandardNormalCdf(2.0 * u / _windMph)));
        double fy = Math.Exp(-0.5 * Math.Pow(v / (a2 * sy), 2)) / (2.5066282746310002 * sy);
        return fx * fy * _activity;
    }

    /// <summary>Temps d'arrivée moyen des retombées (h), au moins 0.5 h.</summary>
    public double ArrivalTimeHours(double xM, double zM)
    {
        var (u, _) = ToWindFrame(xM, zM);
        double denom = _l02 + 0.5 * _sx2;
        return Math.Sqrt(0.25 + _l02 * Math.Pow(u + 2.0 * _sx, 2) * _tc * _tc / (_l2 * denom) + 2.0 * _sx2 / denom);
    }

    /// <summary>Dose cumulée en plein air (rad) de l'arrivée des retombées jusqu'au temps t (h).</summary>
    public double AccumulatedDose(double xM, double zM, double timeHours)
        => DoseFromRate(DoseRateAtH1(xM, zM), ArrivalTimeHours(xM, zM), timeHours);

    /// <summary>Dose cumulée (rad) d'un débit H+1 donné, de l'arrivée au temps t (h).</summary>
    public static double DoseFromRate(double rateH1, double arrivalHours, double timeHours)
    {
        if (rateH1 <= 0 || timeHours <= arrivalHours) return 0.0;
        return 5.0 * rateH1 * (Math.Pow(arrivalHours, -0.2) - Math.Pow(timeHours, -0.2));
    }

    // Repère du vent : u sous le vent, v en travers, en milles.
    private (double U, double V) ToWindFrame(double xM, double zM)
    {
        double u = (xM * _dirX + zM * _dirZ) / MetersPerMile;
        double v = (-xM * _dirZ + zM * _dirX) / MetersPerMile;
        return (u, v);
    }

    private double G(double u) => Math.Exp(-Math.Pow(Math.Abs(u) / _l, _n)) * _gammaNorm;

    private double Phi(double u) => BuildingDamage.StandardNormalCdf(_l0 / _l * u / (_sx * _a1));

    // Fonction gamma (approximation de Lanczos, g = 7 ; erreur relative ~1e-15).
    private static double Gamma(double x)
    {
        if (x < 0.5) return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1.0 - x));
        double[] c = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
                       -176.61503916999185, 12.507343278686905, -0.13857109526572012,
                       9.9843695780195716e-6, 1.5056327351493116e-7 };
        x -= 1.0;
        double a = c[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++) a += c[i] / (x + i);
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * a;
    }
}
