using BlastPhysics.Blast;
using BlastPhysics.Conventional;
using BlastPhysics.Nuclear;

namespace BlastPhysics;

/// <summary>Effets combinés reçus en un point du sol.</summary>
public readonly record struct EffectsAtPoint(
    double GroundRangeM,
    BlastLoad Blast,
    double ThermalFluenceCalPerCm2,
    double InitialRadiationRad,
    double FragmentHitProbability);

/// <summary>Rayons d'effet au sol (m) ; 0 si l'effet n'existe pas pour cet engin.</summary>
public readonly record struct EffectRadii(
    double FireballM,
    double Overpressure140KPaM,
    double Overpressure35KPaM,
    double Overpressure7KPaM,
    double ThirdDegreeBurnsM,
    double FirstDegreeBurnsM,
    double Radiation500RadM,
    double FragmentHazardM);

/// <summary>
/// Point d'entrée de la bibliothèque : une explosion définie par son engin, sa hauteur
/// et le terrain, qui fournit les effets en tout point du sol. Le point zéro est
/// l'origine ; seules comptent les distances horizontales.
/// </summary>
public sealed class Detonation
{
    // Seuils de surpression usuels : 140 kPa (20 psi, béton armé détruit),
    // 35 kPa (5 psi, la plupart des bâtiments effondrés), 7 kPa (1 psi, vitres brisées).
    public const double SevereBlastPa = 140e3;
    public const double ModerateBlastPa = 35e3;
    public const double LightBlastPa = 7e3;
    public const double RadiationThresholdRad = 500.0;

    private Detonation(string name, double heightOfBurstM, double groundElevationM, Airblast blast)
    {
        Name = name;
        HeightOfBurstM = heightOfBurstM;
        GroundElevationM = groundElevationM;
        Blast = blast;
    }

    public string Name { get; }
    public double HeightOfBurstM { get; }
    public double GroundElevationM { get; }
    public AirState Ambient => Blast.Ambient;

    public Airblast Blast { get; }
    public NuclearDevice? NuclearDevice { get; private init; }
    public ConventionalCharge? Charge { get; private init; }
    public Fireball? Fireball { get; private init; }
    public ThermalRadiation? Thermal { get; private init; }
    public InitialRadiation? Radiation { get; private init; }

    /// <summary>Radioactivité induite dans le sol par les neutrons (nucléaire seulement).</summary>
    public InducedActivity? Induced { get; private init; }
    public Fragmentation? Fragments { get; private init; }

    /// <summary>Cratère apparent (rayon nul pour une explosion en altitude).</summary>
    public Crater Crater { get; private init; } = Crater.None;

    /// <summary>Énergie totale libérée, kt.</summary>
    public double YieldKt => NuclearDevice?.YieldKt ?? Charge!.TntEquivalentKt;

    public static Detonation Nuclear(NuclearDevice device, double heightOfBurstM,
                                     double groundElevationM = 0.0, double visibilityKm = 20.0,
                                     string? name = null)
    {
        var ambient = Atmosphere.At(groundElevationM);
        var blast = new Airblast(NuclearFreeAirCurve.Instance, device.BlastEquivalentKt, heightOfBurstM, ambient);
        var fireball = new Fireball(device, heightOfBurstM, ambient);
        var radiation = new InitialRadiation(device, fireball, ambient);
        return new Detonation(name ?? $"Nucléaire {device.YieldKt:G4} kt", heightOfBurstM, groundElevationM, blast)
        {
            NuclearDevice = device,
            Fireball = fireball,
            Thermal = new ThermalRadiation(device, fireball, visibilityKm),
            Radiation = radiation,
            Crater = Crater.ForNuclear(device, fireball),
            Induced = new InducedActivity(radiation),
        };
    }

    public static Detonation Conventional(ConventionalCharge charge, double heightOfBurstM,
                                          double groundElevationM = 0.0, string? name = null)
    {
        var ambient = Atmosphere.At(groundElevationM);
        var blast = new Airblast(TntFreeAirCurve.Instance, charge.TntEquivalentKt, heightOfBurstM, ambient);
        bool fragments = charge.CasingMassKg > 0 && charge.Explosive.GurneyVelocityMs > 0;
        return new Detonation(name ?? $"{charge.Explosive.Name} {charge.ExplosiveMassKg:G4} kg", heightOfBurstM, groundElevationM, blast)
        {
            Charge = charge,
            Fragments = fragments ? new Fragmentation(charge, ambient) : null,
            Crater = Crater.ForConventional(charge, heightOfBurstM),
        };
    }

    public EffectsAtPoint At(double groundRangeM)
    {
        double slant = Math.Sqrt(groundRangeM * groundRangeM + HeightOfBurstM * HeightOfBurstM);
        return new EffectsAtPoint(
            groundRangeM,
            Blast.AtGroundRange(groundRangeM),
            Thermal?.FluenceCalPerCm2(groundRangeM) ?? 0.0,
            Radiation?.TotalRadAtGroundRange(groundRangeM) ?? 0.0,
            Fragments?.PersonHitProbability(slant) ?? 0.0);
    }

    public EffectRadii ComputeRadii()
    {
        double fragmentGround = 0.0;
        if (Fragments is { } f && f.HazardRangeM > HeightOfBurstM)
            fragmentGround = Math.Sqrt(f.HazardRangeM * f.HazardRangeM - HeightOfBurstM * HeightOfBurstM);
        return new EffectRadii(
            Fireball?.MaxRadiusM ?? 0.0,
            Blast.RangeForOverpressure(SevereBlastPa),
            Blast.RangeForOverpressure(ModerateBlastPa),
            Blast.RangeForOverpressure(LightBlastPa),
            Thermal?.BurnRangeM(BurnDegree.Third) ?? 0.0,
            Thermal?.BurnRangeM(BurnDegree.First) ?? 0.0,
            Radiation?.RangeForDose(RadiationThresholdRad) ?? 0.0,
            fragmentGround);
    }
}
