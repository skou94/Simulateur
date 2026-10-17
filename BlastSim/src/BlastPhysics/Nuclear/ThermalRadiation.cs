namespace BlastPhysics.Nuclear;

public enum BurnDegree { First = 1, Second = 2, Third = 3 }

/// <summary>
/// Fluence thermique reçue (Glasstone §7.93) : Q = f·E·τ / (4πD²), où f est la part
/// thermique, E l'énergie totale, D la distance oblique et τ la transmittance de l'air.
/// </summary>
public sealed class ThermalRadiation
{
    // Coefficient de Koschmieder : extinction σ = 3.912 / visibilité.
    private const double KoschmiederConstant = 3.912;

    private readonly double _radiatedEnergyJ;
    private readonly double _heightOfBurstM;
    private readonly double _extinctionPerM;

    /// <param name="visibilityKm">
    /// Visibilité météorologique : ~50 km très clair, 20 km clair (défaut), 5 km brumeux.
    /// </param>
    public ThermalRadiation(NuclearDevice device, Fireball fireball, double visibilityKm = 20.0)
    {
        YieldKt = device.YieldKt;
        _heightOfBurstM = fireball.HeightOfBurstM;
        // Explosion au contact : moitié de l'énergie absorbée par le sol et les poussières
        // (Glasstone §7.35), interpolée selon l'enfoncement de la boule de feu.
        double contact = Math.Clamp(1.0 - fireball.HeightOfBurstM / fireball.AirburstMaxRadiusM, 0.0, 1.0);
        EffectiveThermalFraction = device.ThermalFraction * (1.0 - 0.5 * contact);
        _radiatedEnergyJ = EffectiveThermalFraction * device.EnergyJ;
        _extinctionPerM = KoschmiederConstant / (Math.Max(visibilityKm, 0.1) * 1000.0);
    }

    public double YieldKt { get; }
    public double EffectiveThermalFraction { get; }

    /// <summary>
    /// Transmittance atmosphérique : atténuation de Beer-Lambert e^(−σD) corrigée par un
    /// facteur d'accumulation (1 + σD) pour la lumière diffusée vers l'avant
    /// (approximation de Glasstone fig. 7.106).
    /// </summary>
    public double Transmittance(double slantRangeM)
    {
        double s = _extinctionPerM * slantRangeM;
        return Math.Exp(-s) * (1.0 + s);
    }

    /// <summary>Fluence thermique à la distance au sol donnée, J/m².</summary>
    public double FluenceAtGroundRange(double groundRangeM)
    {
        double d2 = groundRangeM * groundRangeM + _heightOfBurstM * _heightOfBurstM;
        double d = Math.Max(Math.Sqrt(d2), 1.0);
        return _radiatedEnergyJ * Transmittance(d) / (4.0 * Math.PI * d * d);
    }

    public double FluenceCalPerCm2(double groundRangeM) => Units.CalPerCm2(FluenceAtGroundRange(groundRangeM));

    /// <summary>
    /// Fluence (cal/cm²) provoquant des brûlures du degré donné sur peau nue. L'impulsion
    /// s'allongeant avec le rendement, le seuil croît en ~W^0.07 : à 1 kt 2 / 3.8 / 5.6,
    /// à 1 Mt ~3.2 / 6.2 / 9.1 cal/cm² (approximation de Glasstone tableau 7.42, ±20 %).
    /// </summary>
    public double BurnThresholdCalPerCm2(BurnDegree degree)
    {
        double atOneKt = degree switch
        {
            BurnDegree.First => 2.0,
            BurnDegree.Second => 3.8,
            _ => 5.6,
        };
        return atOneKt * Math.Pow(Math.Max(YieldKt, 1e-3), 0.07);
    }

    /// <summary>Plus grande distance au sol où le degré de brûlure est atteint.</summary>
    public double BurnRangeM(BurnDegree degree)
    {
        double threshold = BurnThresholdCalPerCm2(degree);
        return RangeFinder.OuterRange(FluenceCalPerCm2, threshold, maxRangeM: 2.0e5);
    }
}
