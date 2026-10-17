namespace BlastPhysics.Damage;

public enum StructureType
{
    /// <summary>Maison à ossature bois.</summary>
    WoodFrame,
    /// <summary>Maçonnerie non armée (brique, pierre).</summary>
    UnreinforcedMasonry,
    /// <summary>Immeuble en béton armé.</summary>
    ReinforcedConcrete,
    /// <summary>Bâtiment à charpente métallique.</summary>
    SteelFrame,
}

/// <summary>États de dégâts, du plus léger au plus grave (échelle de type HAZUS).</summary>
public enum DamageState { None = 0, Light = 1, Moderate = 2, Severe = 3, Collapse = 4 }

/// <summary>Seuils pression-impulsion d'un état de dégât : asymptotes P0 (Pa) et I0 (Pa·s).</summary>
public readonly record struct IsoDamageThreshold(double PressurePa, double ImpulsePaS);

/// <summary>Probabilités d'atteindre chaque état de dégât, ou un état plus grave.</summary>
public readonly record struct DamageAssessment(double PLight, double PModerate, double PSevere, double PCollapse)
{
    // Taux de perte moyens par état (HAZUS-MH) : léger 2 %, modéré 10 %, grave 50 %, effondrement 100 %.
    private static readonly double[] LossRatio = { 0.0, 0.02, 0.10, 0.50, 1.0 };

    /// <summary>Taux de dommage moyen attendu, de 0 (intact) à 1 (détruit).</summary>
    public double ExpectedDamageRatio =>
        (PLight - PModerate) * LossRatio[1]
        + (PModerate - PSevere) * LossRatio[2]
        + (PSevere - PCollapse) * LossRatio[3]
        + PCollapse * LossRatio[4];
}

/// <summary>
/// Vulnérabilité des bâtiments au souffle.
/// <para>
/// Diagramme pression-impulsion (Baker et al., « Explosion Hazards and Evaluation »,
/// 1983) : chaque état de dégât suit une hyperbole aux asymptotes P0 (onde longue, le
/// régime nucléaire) et I0 (onde brève, le régime d'une petite charge). Sur la forme
/// normalisée (P/λP0 − 1)(I/λI0 − 1) = 1, le facteur de sollicitation λ = ab/(a+b), avec
/// a = P/P0 et b = I/I0, vaut 1 exactement sur l'hyperbole.
/// </para>
/// <para>
/// La dispersion entre bâtiments est rendue par une fragilité lognormale :
/// P(état) = Φ(ln λ / β), β = 0.4.
/// </para>
/// <para>
/// Seuils de pression : ordres de grandeur de Glasstone &amp; Dolan ch. 5 (maisons bois
/// gravement touchées vers 35 kPa, béton armé au-delà de 100 kPa). Les impulsions I0 sont
/// <b>indicatives</b> et restent à calibrer sur données d'essais.
/// </para>
/// </summary>
public static class BuildingDamage
{
    public const double LognormalDispersion = 0.4;

    private static readonly Dictionary<StructureType, IsoDamageThreshold[]> Thresholds = new()
    {
        // Ordre : Light, Moderate, Severe, Collapse — (P0 en Pa, I0 en Pa·s)
        [StructureType.WoodFrame] = new IsoDamageThreshold[]
            { new(5e3, 100), new(12e3, 200), new(24e3, 350), new(35e3, 500) },
        [StructureType.UnreinforcedMasonry] = new IsoDamageThreshold[]
            { new(7e3, 150), new(17e3, 300), new(30e3, 500), new(45e3, 700) },
        [StructureType.ReinforcedConcrete] = new IsoDamageThreshold[]
            { new(15e3, 300), new(40e3, 600), new(80e3, 1000), new(140e3, 1500) },
        [StructureType.SteelFrame] = new IsoDamageThreshold[]
            { new(12e3, 250), new(30e3, 500), new(60e3, 900), new(100e3, 1300) },
    };

    /// <summary>Facteur de sollicitation λ = ab/(a+b) : 1 sur l'hyperbole iso-dégât.</summary>
    public static double DemandRatio(double overpressurePa, double impulsePaS, IsoDamageThreshold threshold)
    {
        double a = overpressurePa / threshold.PressurePa;
        double b = impulsePaS / threshold.ImpulsePaS;
        if (a <= 0 || b <= 0) return 0.0;
        return a * b / (a + b);
    }

    public static double ExceedanceProbability(double overpressurePa, double impulsePaS, IsoDamageThreshold threshold)
    {
        double lambda = DemandRatio(overpressurePa, impulsePaS, threshold);
        if (lambda <= 0) return 0.0;
        return StandardNormalCdf(Math.Log(lambda) / LognormalDispersion);
    }

    public static DamageAssessment Assess(StructureType type, double overpressurePa, double impulsePaS)
    {
        var t = Thresholds[type];
        return new DamageAssessment(
            ExceedanceProbability(overpressurePa, impulsePaS, t[0]),
            ExceedanceProbability(overpressurePa, impulsePaS, t[1]),
            ExceedanceProbability(overpressurePa, impulsePaS, t[2]),
            ExceedanceProbability(overpressurePa, impulsePaS, t[3]));
    }

    /// <summary>Fonction de répartition de la loi normale centrée réduite.</summary>
    public static double StandardNormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2.0));

    // Fonction d'erreur complémentaire (Numerical Recipes, erfcc ; erreur relative < 1.2e-7).
    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
                   + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
                   + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0 ? r : 2.0 - r;
    }
}
