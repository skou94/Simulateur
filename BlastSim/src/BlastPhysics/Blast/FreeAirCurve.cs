namespace BlastPhysics.Blast;

/// <summary>
/// Loi de souffle en air libre au niveau de la mer pour 1 kt d'équivalent souffle.
/// La distance réduite est en m/kt^(1/3) ; l'échelle cube (Hopkinson-Cranz) et
/// celle de Sachs (altitude) sont appliquées par <see cref="Airblast"/>.
/// </summary>
public abstract class FreeAirCurve
{
    private double[]? _toaRange;
    private double[]? _toaTime;

    /// <summary>Surpression incidente de crête, Pa.</summary>
    public abstract double Overpressure(double scaledRangeM);

    /// <summary>
    /// Durée de la phase positive au sol (s/kt^(1/3)) pour une distance au sol et une
    /// hauteur d'explosion réduites (m/kt^(1/3)).
    /// </summary>
    public abstract double PositivePhaseDuration(double scaledGroundRangeM, double scaledHeightM);

    /// <summary>
    /// Temps d'arrivée du front en air libre (s/kt^(1/3)), obtenu en intégrant
    /// dr / (c0·Ms(r)) le long du rayon, Ms tiré de Rankine-Hugoniot.
    /// </summary>
    public virtual double ArrivalTime(double scaledRangeM)
    {
        EnsureArrivalTable();
        double lr = Math.Log(Math.Max(scaledRangeM, _toaRange![0]));
        if (lr >= _toaRange[^1])
        {
            // Au-delà de la table, le choc est acoustique.
            double rEnd = Math.Exp(_toaRange[^1]);
            return _toaTime![^1] + (scaledRangeM - rEnd) / Atmosphere.SeaLevel.SoundSpeedMs;
        }
        int i = Array.BinarySearch(_toaRange, lr);
        if (i >= 0) return _toaTime![i];
        i = ~i;
        double t = (lr - _toaRange[i - 1]) / (_toaRange[i] - _toaRange[i - 1]);
        return _toaTime![i - 1] + t * (_toaTime[i] - _toaTime[i - 1]);
    }

    private void EnsureArrivalTable()
    {
        if (_toaRange != null) return;
        const int n = 2000;
        double l0 = Math.Log(0.05), l1 = Math.Log(2.0e5);
        var range = new double[n];
        var time = new double[n];
        double c0 = Atmosphere.SeaLevel.SoundSpeedMs;
        double Integrand(double lr)
        {
            double r = Math.Exp(lr);
            return r / (c0 * ShockRelations.ShockMach(Overpressure(r)));
        }
        range[0] = l0;
        time[0] = 0.0;
        for (int k = 1; k < n; k++)
        {
            range[k] = l0 + (l1 - l0) * k / (n - 1);
            double a = range[k - 1], b = range[k], m = 0.5 * (a + b);
            time[k] = time[k - 1] + (b - a) / 6.0 * (Integrand(a) + 4 * Integrand(m) + Integrand(b));
        }
        _toaTime = time;
        _toaRange = range;
    }
}

/// <summary>
/// Courbe standard DNA de surpression en air libre pour 1 kt nucléaire
/// (énergie totale, dont ~50 % en souffle). Source : BLAST.EXE / DNA EM-1,
/// portage d'après « glasstone » (MIT). Précision annoncée ±4 % (pire 11 %).
/// </summary>
public sealed class NuclearFreeAirCurve : FreeAirCurve
{
    public static NuclearFreeAirCurve Instance { get; } = new();

    public override double Overpressure(double r)
    {
        r = Math.Max(r, 0.05);
        double k = r / 445.42;
        double logTerm = Math.Log(k + 3.0 * Math.Exp(-Math.Sqrt(k) / 3.0));
        return 3.04e11 / (r * r * r) + 1.13e9 / (r * r) + 7.9e6 / (r * Math.Sqrt(logTerm));
    }

    /// <summary>Temps d'arrivée DNA en air libre (forme fermée).</summary>
    public override double ArrivalTime(double r)
    {
        double r2 = r * r;
        return r2 * (6.7 + r) / (7.12e6 + 7.32e4 * r + 340.5 * r2);
    }

    /// <summary>Phase positive DNA avec correction de hauteur d'explosion (Speicher &amp; Brode).</summary>
    public override double PositivePhaseDuration(double sgr, double shob)
    {
        double ta = GroundReflection.ArrivalTime(this, sgr, shob);
        double t0 = Math.Log(1000.0 * ta) / 3.77;
        double dpSurf = (155.0 * Math.Exp(-20.8 * ta) + Math.Exp(-t0 * t0 + 4.86 * t0 + 0.25)) / 1000.0;
        double dpUnmod = dpSurf * (1.0 - (1.0 - 1.0 / (1.0 + 4.5e-8 * Math.Pow(shob, 7)))
                                         * (0.04 + 0.61 / (1.0 + Math.Pow(ta, 1.5) / 0.027)));
        return dpUnmod * 1.16 * Math.Exp(-Math.Abs(shob / Units.MetersPerFoot - 156.0) / 1062.0);
    }
}

/// <summary>
/// Loi de Kinney &amp; Graham (1985, « Explosive Shocks in Air », 2e éd.) pour une charge
/// sphérique de TNT en air libre. Distance réduite Z en m/kg^(1/3) ; 1 kt = 10⁶ kg,
/// donc Z = r_kt / 100.
/// </summary>
public sealed class TntFreeAirCurve : FreeAirCurve
{
    public static TntFreeAirCurve Instance { get; } = new();

    private const double KtToKgScale = 100.0; // (10⁶)^(1/3)

    public override double Overpressure(double scaledRangeM)
    {
        double z = Math.Max(scaledRangeM / KtToKgScale, 0.01);
        double ratio = 808.0 * (1.0 + Sq(z / 4.5))
                       / (Math.Sqrt(1.0 + Sq(z / 0.048)) * Math.Sqrt(1.0 + Sq(z / 0.32)) * Math.Sqrt(1.0 + Sq(z / 1.35)));
        return ratio * Atmosphere.SeaLevelPressurePa;
    }

    /// <summary>
    /// Phase positive Kinney-Graham (s/kt^(1/3)) à la distance oblique, la charge étant
    /// doublée par la réflexion au sol (image miroir).
    /// </summary>
    public override double PositivePhaseDuration(double sgr, double shob)
        => FreeAirPositivePhase(Math.Sqrt(sgr * sgr + shob * shob) / GroundReflection.Cbrt2);

    /// <summary>Phase positive Kinney-Graham en air libre, s/kt^(1/3).</summary>
    public double FreeAirPositivePhase(double scaledRangeM)
    {
        double z = Math.Max(scaledRangeM / KtToKgScale, 0.01);
        double msPerKg13 = 980.0 * (1.0 + Math.Pow(z / 0.54, 10))
                           / ((1.0 + Math.Pow(z / 0.02, 3)) * (1.0 + Math.Pow(z / 0.74, 6)) * Math.Sqrt(1.0 + Sq(z / 6.9)));
        return msPerKg13 / 1000.0 * KtToKgScale;
    }

    /// <summary>Impulsion positive incidente Kinney-Graham, Pa·s/kt^(1/3).</summary>
    public double PositiveImpulse(double scaledRangeM)
    {
        double z = Math.Max(scaledRangeM / KtToKgScale, 0.01);
        double barMsPerKg13 = 0.067 * Math.Sqrt(1.0 + Math.Pow(z / 0.23, 4))
                              / (z * z * Math.Cbrt(1.0 + Math.Pow(z / 1.55, 3)));
        return barMsPerKg13 * 100.0 * KtToKgScale; // 1 bar·ms = 100 Pa·s
    }

    private static double Sq(double v) => v * v;
}
