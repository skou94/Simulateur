namespace BlastPhysics.Conventional;

/// <summary>
/// Explosif chimique.
/// </summary>
/// <param name="RelativeEffectiveness">
/// Équivalent TNT en souffle (facteur RE) : masse de TNT produisant la même
/// surpression. Valeurs usuelles des tables d'équivalence (UFC 3-340-02, DoD),
/// qui varient de ±10 % selon le critère retenu (pression ou impulsion).
/// </param>
/// <param name="GurneyVelocityMs">√(2E) de Gurney, pour la vitesse initiale des éclats.</param>
public sealed record Explosive(string Name, double RelativeEffectiveness, double GurneyVelocityMs)
{
    public static readonly Explosive Tnt = new("TNT", 1.00, 2440);
    public static readonly Explosive CompositionB = new("Composition B", 1.33, 2700);
    public static readonly Explosive C4 = new("C-4", 1.34, 2600);
    public static readonly Explosive Rdx = new("RDX", 1.60, 2830);
    public static readonly Explosive Tritonal = new("Tritonal", 1.05, 2320);
    public static readonly Explosive H6 = new("H-6", 1.35, 2500);
    public static readonly Explosive Anfo = new("ANFO", 0.74, 1800);

    /// <summary>
    /// Nitrate d'ammonium seul (Beyrouth 2020). Équivalence très incertaine
    /// (0.15 à 0.55 selon les études) ; 0.3 est une valeur médiane.
    /// </summary>
    public static readonly Explosive AmmoniumNitrate = new("Nitrate d'ammonium", 0.30, 1500);

    /// <summary>
    /// Explosif thermobarique / aérosol (FAE). Le facteur RE dépend fortement du
    /// critère : ~1.2-2.5 en impulsion. 1.6 est une valeur centrale, à l'onde plus longue
    /// et à la crête plus faible qu'une charge de TNT (non modélisé ici).
    /// </summary>
    public static readonly Explosive Thermobaric = new("Thermobarique (FAE)", 1.60, 0);
}

/// <summary>Charge conventionnelle, avec enveloppe métallique facultative.</summary>
/// <param name="ExplosiveMassKg">Masse d'explosif.</param>
/// <param name="CasingMassKg">Masse de l'enveloppe fragmentable (0 = charge nue).</param>
/// <param name="FragmentMassKg">Masse moyenne d'un éclat.</param>
public sealed record ConventionalCharge(
    Explosive Explosive,
    double ExplosiveMassKg,
    double CasingMassKg = 0.0,
    double FragmentMassKg = 2.0e-4)
{
    public double TntEquivalentKg => ExplosiveMassKg * Explosive.RelativeEffectiveness;
    public double TntEquivalentKt => Units.KgTntToKt(TntEquivalentKg);
}
