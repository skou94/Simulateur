namespace BlastPhysics.Conventional;

/// <summary>
/// Éclats d'une charge à enveloppe métallique.
/// <list type="bullet">
/// <item>Vitesse initiale de Gurney pour une enveloppe sphérique :
/// V0 = √(2E) · (M/C + 3/5)^(−1/2).</item>
/// <item>Freinage aérodynamique : V(x) = V0 · e^(−x/L), L = 2m / (Cd ρ A).</item>
/// <item>Éclat dangereux : énergie cinétique ≥ 79 J (58 ft·lbf, critère DoD 6055.09).</item>
/// <item>Densité dangereuse : 1 éclat par 55.7 m² (600 ft², même critère).</item>
/// </list>
/// Hypothèses : éclats d'acier de masse uniforme, projection isotrope, sans gravité
/// (valable aux distances où les éclats restent dangereux).
/// </summary>
public sealed class Fragmentation
{
    public const double HazardousEnergyJ = 79.0;
    public const double HazardousAreaPerFragmentM2 = 55.7;

    // Surface présentée moyenne d'un éclat d'acier : A = k·m^(2/3) (k ≈ 0.005 m²/kg^(2/3),
    // éclat compact qui tourne sur lui-même) et Cd ≈ 1.
    private const double ShapeFactor = 0.005;
    private const double DragCoefficient = 1.0;

    // Surface exposée d'une personne debout, de face.
    private const double PersonExposedAreaM2 = 0.5;

    private readonly double _fragmentMassKg;
    private readonly double _decayLengthM;

    public Fragmentation(ConventionalCharge charge, AirState ambient)
    {
        if (charge.CasingMassKg <= 0 || charge.Explosive.GurneyVelocityMs <= 0)
            throw new ArgumentException("La charge n'a pas d'enveloppe fragmentable.", nameof(charge));
        _fragmentMassKg = charge.FragmentMassKg;
        FragmentCount = charge.CasingMassKg / charge.FragmentMassKg;
        InitialVelocityMs = charge.Explosive.GurneyVelocityMs
                            / Math.Sqrt(charge.CasingMassKg / charge.ExplosiveMassKg + 0.6);
        double area = ShapeFactor * Math.Pow(_fragmentMassKg, 2.0 / 3.0);
        _decayLengthM = 2.0 * _fragmentMassKg / (DragCoefficient * ambient.DensityKgM3 * area);
    }

    public double FragmentCount { get; }
    public double InitialVelocityMs { get; }

    public double VelocityAt(double distanceM) => InitialVelocityMs * Math.Exp(-distanceM / _decayLengthM);

    public double KineticEnergyAt(double distanceM)
    {
        double v = VelocityAt(distanceM);
        return 0.5 * _fragmentMassKg * v * v;
    }

    /// <summary>Densité surfacique d'éclats (par m²) à la distance donnée.</summary>
    public double ArealDensityAt(double distanceM)
        => FragmentCount / (4.0 * Math.PI * Math.Max(distanceM * distanceM, 1e-6));

    /// <summary>Distance au-delà de laquelle un éclat n'est plus dangereux (79 J).</summary>
    public double EnergyHazardRangeM
    {
        get
        {
            double vCrit = Math.Sqrt(2.0 * HazardousEnergyJ / _fragmentMassKg);
            return InitialVelocityMs <= vCrit ? 0.0 : _decayLengthM * Math.Log(InitialVelocityMs / vCrit);
        }
    }

    /// <summary>Distance où la densité d'éclats tombe sous 1 par 55.7 m².</summary>
    public double DensityHazardRangeM
        => Math.Sqrt(FragmentCount * HazardousAreaPerFragmentM2 / (4.0 * Math.PI));

    /// <summary>Distance de danger : les deux critères doivent être remplis.</summary>
    public double HazardRangeM => Math.Min(EnergyHazardRangeM, DensityHazardRangeM);

    /// <summary>
    /// Probabilité qu'une personne debout exposée soit touchée par au moins un éclat
    /// dangereux (statistique de Poisson).
    /// </summary>
    public double PersonHitProbability(double distanceM)
    {
        if (KineticEnergyAt(distanceM) < HazardousEnergyJ) return 0.0;
        return 1.0 - Math.Exp(-ArealDensityAt(distanceM) * PersonExposedAreaM2);
    }
}
