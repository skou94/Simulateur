namespace BlastPhysics.Nuclear;

/// <summary>
/// Radioactivité induite dans le sol par les neutrons, près du point zéro (Glasstone &amp;
/// Dolan §9.31-9.37). Elle existe aussi pour une explosion aérienne, qui ne produit
/// pas de retombées locales.
/// <list type="bullet">
/// <item>Radionucléides dominants d'un sol courant : ²⁸Al (2.24 min), ⁵⁶Mn (2.58 h),
/// ²⁴Na (15.0 h) ; part de la dose totale attribuée à chacun : 20 / 40 / 40 % (ordre de
/// grandeur, selon la composition du sol).</item>
/// <item>Intensité proportionnelle à la dose neutronique reçue au sol, calibrée sur
/// Hiroshima : ~80 rad cumulés à l'hypocentre pour une présence illimitée (estimations
/// associées aux dosimétries DS86/DS02), pour ~8 200 rad de neutrons dans ce modèle.</item>
/// </list>
/// Incertitude : facteur ~3, davantage pour les engins dont les courbes neutroniques
/// génériques sont mal adaptées (Nagasaki, surestimée).
/// </summary>
public sealed class InducedActivity
{
    /// <summary>Dose induite totale (rad) par rad de neutrons reçu au même endroit.</summary>
    public const double InfiniteDosePerNeutronRad = 80.0 / 8166.0;

    private static readonly (double Fraction, double HalfLifeH)[] Nuclides =
    {
        (0.20, 2.24 / 60.0), // aluminium 28
        (0.40, 2.58),        // manganèse 56
        (0.40, 15.0),        // sodium 24
    };

    private readonly InitialRadiation _radiation;

    public InducedActivity(InitialRadiation radiation) => _radiation = radiation;

    /// <summary>Dose cumulée pour une présence illimitée à partir de l'explosion (rad).</summary>
    public double InfiniteDoseAt(double groundRangeM)
        => _radiation.AtGroundRange(groundRangeM).NeutronRad * InfiniteDosePerNeutronRad;

    /// <summary>Débit de dose (rad/h) au temps t (h) après l'explosion.</summary>
    public double DoseRateAt(double groundRangeM, double hours)
        => DoseRateFromInfinite(InfiniteDoseAt(groundRangeM), hours);

    /// <summary>Dose cumulée (rad) d'une personne présente de l'explosion jusqu'au temps t (h).</summary>
    public double AccumulatedDose(double groundRangeM, double hours)
        => DoseFromInfinite(InfiniteDoseAt(groundRangeM), hours);

    public static double DoseFromInfinite(double infiniteDose, double hours)
    {
        if (infiniteDose <= 0 || hours <= 0) return 0.0;
        double sum = 0;
        foreach (var (f, half) in Nuclides) sum += f * (1.0 - Math.Exp(-Math.Log(2) * hours / half));
        return infiniteDose * sum;
    }

    public static double DoseRateFromInfinite(double infiniteDose, double hours)
    {
        if (infiniteDose <= 0 || hours < 0) return 0.0;
        double sum = 0;
        foreach (var (f, half) in Nuclides)
        {
            double lambda = Math.Log(2) / half;
            sum += f * lambda * Math.Exp(-lambda * hours);
        }
        return infiniteDose * sum;
    }

    /// <summary>Plus grande distance où la dose induite totale atteint le seuil (rad).</summary>
    public double RangeForInfiniteDose(double doseRad)
        => RangeFinder.OuterRange(InfiniteDoseAt, doseRad, maxRangeM: 1e4);
}
