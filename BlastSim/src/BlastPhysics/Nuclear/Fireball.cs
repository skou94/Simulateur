namespace BlastPhysics.Nuclear;

/// <summary>
/// Boule de feu nucléaire : taille maximale, croissance initiale et impulsion thermique.
/// </summary>
public sealed class Fireball
{
    // Rayon maximal en explosion aérienne : R ≈ 55 m · W^0.4 (W en kt), cohérent avec
    // Glasstone §2.03 (~1 700 m de diamètre pour 1 Mt) et ~160 m de rayon pour Hiroshima.
    private const double AirburstRadiusCoefficientM = 55.0;
    private const double RadiusYieldExponent = 0.4;

    // Explosion au contact du sol : hémisphère d'une énergie doublée par réflexion, ×2^0.4.
    private static readonly double SurfaceRadiusFactor = Math.Pow(2.0, RadiusYieldExponent);

    // Constante de Taylor-Sedov pour γ = 1.4.
    private const double SedovConstant = 1.033;

    private readonly double _energyJ;
    private readonly double _ambientDensity;

    public Fireball(NuclearDevice device, double heightOfBurstM, AirState ambient)
    {
        YieldKt = device.YieldKt;
        _energyJ = device.EnergyJ;
        _ambientDensity = ambient.DensityKgM3;
        AirburstMaxRadiusM = AirburstRadiusCoefficientM * Math.Pow(YieldKt, RadiusYieldExponent);
        HeightOfBurstM = Math.Max(0.0, heightOfBurstM);
        // Contact partiel : interpolation entre explosion au sol et explosion aérienne libre.
        double contact = Math.Clamp(1.0 - HeightOfBurstM / AirburstMaxRadiusM, 0.0, 1.0);
        MaxRadiusM = AirburstMaxRadiusM * (1.0 + (SurfaceRadiusFactor - 1.0) * contact);
    }

    public double YieldKt { get; }
    public double HeightOfBurstM { get; }
    public double AirburstMaxRadiusM { get; }
    public double MaxRadiusM { get; }

    /// <summary>La boule de feu touche le sol (cratère, retombées locales importantes).</summary>
    public bool TouchesGround => HeightOfBurstM < MaxRadiusM;

    /// <summary>
    /// Instant du second maximum de puissance thermique : t_max ≈ 0.0417 · W^0.44 s
    /// (Glasstone éq. 7.85).
    /// </summary>
    public double ThermalPulseMaxTimeS => 0.0417 * Math.Pow(YieldKt, 0.44);

    /// <summary>
    /// Rayon du front au temps t : solution de Taylor-Sedov R = 1.033·(E t²/ρ)^(1/5)
    /// (Taylor, 1950 ; ~130 m à 25 ms pour Trinity), bornée par le rayon maximal.
    /// </summary>
    public double RadiusAt(double timeS)
    {
        if (timeS <= 0) return 0.0;
        double r = SedovConstant * Math.Pow(_energyJ * timeS * timeS / _ambientDensity, 0.2);
        return Math.Min(r, MaxRadiusM);
    }

    /// <summary>
    /// Puissance thermique normalisée P/Pmax au temps t, forme 2τ²/(1+τ⁴) avec τ = t/t_max
    /// (approximation de la courbe universelle de Glasstone, fig. 7.88).
    /// </summary>
    public double ThermalPowerFraction(double timeS)
    {
        if (timeS <= 0) return 0.0;
        double tau = timeS / ThermalPulseMaxTimeS;
        double tau2 = tau * tau;
        return 2.0 * tau2 / (1.0 + tau2 * tau2);
    }
}
