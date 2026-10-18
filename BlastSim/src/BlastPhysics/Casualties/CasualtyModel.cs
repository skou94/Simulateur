using BlastPhysics.Damage;

namespace BlastPhysics.Casualties;

/// <summary>Usage d'un bâtiment, qui fixe sa densité d'occupation.</summary>
public enum Occupancy { Residential, Office }

/// <summary>Moment de l'explosion : répartition des gens entre logements, bureaux et extérieur.</summary>
public enum TimeOfDay { Day, Night }

/// <summary>Ce que reçoit un point du sol (et donc ses occupants).</summary>
/// <param name="ThirdDegreeCalPerCm2">Seuil de brûlure du 3ᵉ degré pour cet engin (0 sans rayonnement thermique).</param>
/// <param name="SecondDegreeCalPerCm2">Seuil de brûlure du 2ᵉ degré pour cet engin.</param>
/// <param name="ResidualRad">
/// Dose résiduelle cumulée en plein air (retombées et radioactivité induite), atténuée à
/// l'intérieur par le facteur de protection du bâtiment.
/// </param>
public readonly record struct Exposure(
    double OverpressurePa,
    double ImpulsePaS,
    double ThermalCalPerCm2,
    double ThirdDegreeCalPerCm2,
    double SecondDegreeCalPerCm2,
    double PromptRadiationRad,
    double ResidualRad,
    double FragmentHitProbability);

/// <summary>Probabilités individuelles : décès, et blessure parmi les survivants.</summary>
public readonly record struct CasualtyRisk(double Death, double Injury)
{
    public static CasualtyRisk Mix(CasualtyRisk a, CasualtyRisk b, double weightOfB) => new(
        a.Death + (b.Death - a.Death) * weightOfB,
        a.Injury + (b.Injury - a.Injury) * weightOfB);
}

/// <summary>
/// Estimation des victimes à partir des effets physiques reçus.
/// <para>
/// Chaque cause agit comme un risque indépendant : P(décès) = 1 − Π(1 − pᵢ), et une
/// personne survivante est blessée avec 1 − Π(1 − qᵢ). Les seuils suivent des lois
/// lognormales P = Φ(ln(x / x50) / β).
/// </para>
/// <list type="bullet">
/// <item>À l'intérieur : le sort du bâtiment (effondrement, dégâts graves…) domine, d'après
/// les taux de victimes par état de dégât de HAZUS-MH, majorés pour un souffle ; incendie
/// dans un bâtiment détruit (personnes coincées) ; radiation atténuée par les murs.</item>
/// <item>À l'extérieur : projection et débris (souffle), brûlures par éclair, radiation
/// complète, éclats.</item>
/// <item>Radiation (initiale + retombées) : DL50 de 450 rad sans soins (Glasstone §12.108),
/// mal des rayons au-delà de ~150 rad.</item>
/// </list>
/// Les paramètres sont calibrés pour reproduire la mortalité observée à Hiroshima selon la
/// distance (Oughterson &amp; Warren 1956). Précision attendue : facteur ~1.5 sur un bilan.
/// </summary>
public static class CasualtyModel
{
    // Densité d'occupation en surface brute (murs, circulations, commerces compris) :
    // ~50 m² par habitant (≈ 40 m² habitables), ~25 m² par poste de travail.
    public const double ResidentialM2PerPerson = 50.0;
    public const double OfficeM2PerPerson = 25.0;

    /// <summary>Part des occupants nominaux présents, selon l'usage et le moment.</summary>
    public static double Presence(Occupancy occupancy, TimeOfDay time) => (occupancy, time) switch
    {
        (Occupancy.Residential, TimeOfDay.Day) => 0.45,
        (Occupancy.Residential, TimeOfDay.Night) => 0.95,
        // Taux de présence observé dans les bureaux un jour ordinaire : ~60-70 %.
        (Occupancy.Office, TimeOfDay.Day) => 0.65,
        _ => 0.05,
    };

    /// <summary>Part des gens dehors, donc exposés directement au souffle et à l'éclair.</summary>
    public static double OutdoorFraction(TimeOfDay time) => time == TimeOfDay.Day ? 0.25 : 0.04;

    public static double Occupants(double floorAreaM2, Occupancy occupancy, TimeOfDay time)
        => floorAreaM2 / (occupancy == Occupancy.Office ? OfficeM2PerPerson : ResidentialM2PerPerson)
           * Presence(occupancy, time);

    // Victimes à l'intérieur selon l'état du bâtiment (décès, blessés).
    private static (double Death, double Injury) BuildingStateRates(StructureType type, DamageState state) => state switch
    {
        DamageState.Collapse => (type == StructureType.WoodFrame ? 0.30 : 0.55, 0.60),
        DamageState.Severe => (type == StructureType.WoodFrame ? 0.06 : 0.10, 0.50),
        DamageState.Moderate => (0.005, 0.20),
        DamageState.Light => (0.0005, 0.04), // vitres brisées
        _ => (0.0, 0.0),
    };

    // Transmission moyenne de la radiation initiale à l'intérieur (Glasstone tableau 8.72,
    // ordres de grandeur) : faible écran pour une maison, fort au cœur d'un grand immeuble.
    private static double PromptRadiationTransmission(StructureType type) => type switch
    {
        StructureType.WoodFrame => 0.8,
        StructureType.UnreinforcedMasonry => 0.6,
        StructureType.SteelFrame => 0.35,
        _ => 0.2,
    };

    /// <summary>
    /// Facteur de protection contre les retombées (dose dehors / dose dedans), en restant
    /// à l'intérieur : maison ~2-3, immeuble de briques ~5, grand immeuble en béton ~10-40
    /// (FEMA, « Protection Factor » ; valeurs prudentes au rez-de-chaussée).
    /// </summary>
    public static double FalloutProtectionFactor(StructureType type) => type switch
    {
        StructureType.WoodFrame => 2.5,
        StructureType.UnreinforcedMasonry => 5.0,
        StructureType.SteelFrame => 10.0,
        _ => 15.0,
    };

    /// <summary>Risque des personnes à l'intérieur d'un bâtiment dans l'état donné.</summary>
    public static CasualtyRisk Indoor(StructureType type, DamageState state, bool burning, in Exposure e)
    {
        var (death, injury) = BuildingStateRates(type, state);
        var d = new Combiner(death, injury);
        if (burning)
        {
            // Un bâtiment détruit en feu piège ses occupants ; ailleurs on évacue le plus souvent.
            if (state >= DamageState.Severe) d.Add(0.35, 0.3);
            else d.Add(0.02, 0.1);
        }
        double dose = e.PromptRadiationRad * PromptRadiationTransmission(type)
                      + e.ResidualRad / FalloutProtectionFactor(type);
        d.Add(RadiationDeath(dose), RadiationInjury(dose));
        return d.Result;
    }

    /// <summary>Risque des personnes à l'extérieur, exposées directement.</summary>
    public static CasualtyRisk Outdoor(in Exposure e)
    {
        var d = new Combiner(0.0, 0.0);
        // Projection, chocs et débris (onde longue : seuil en pression, onde brève : en impulsion).
        d.Add(Lognormal(BuildingDamage.DemandRatio(e.OverpressurePa, e.ImpulsePaS, new IsoDamageThreshold(140e3, 600)), 1.0, 0.45),
              Lognormal(BuildingDamage.DemandRatio(e.OverpressurePa, e.ImpulsePaS, new IsoDamageThreshold(20e3, 80)), 1.0, 0.5));
        if (e.ThirdDegreeCalPerCm2 > 0)
            d.Add(Lognormal(e.ThermalCalPerCm2, 1.3 * e.ThirdDegreeCalPerCm2, 0.3),
                  Lognormal(e.ThermalCalPerCm2, e.SecondDegreeCalPerCm2, 0.3));
        double dose = e.PromptRadiationRad + e.ResidualRad;
        d.Add(RadiationDeath(dose), RadiationInjury(dose));
        d.Add(0.25 * e.FragmentHitProbability, 0.75 * e.FragmentHitProbability);
        return d.Result;
    }

    /// <summary>Risque moyen des occupants d'un bâtiment (intérieur et extérieur pondérés).</summary>
    public static CasualtyRisk Occupant(StructureType type, DamageState state, bool burning, in Exposure e, TimeOfDay time)
        => CasualtyRisk.Mix(Indoor(type, state, burning, e), Outdoor(e), OutdoorFraction(time));

    /// <summary>Risque moyen sur la distribution des états de dégât (sans tirage au sort).</summary>
    public static CasualtyRisk Expected(StructureType type, DamageAssessment damage, bool burning, in Exposure e, TimeOfDay time)
    {
        double pNone = 1 - damage.PLight, pLight = damage.PLight - damage.PModerate;
        double pMod = damage.PModerate - damage.PSevere, pSev = damage.PSevere - damage.PCollapse;
        double death = 0, injury = 0;
        foreach (var (state, p) in new[] { (DamageState.None, pNone), (DamageState.Light, pLight),
                                           (DamageState.Moderate, pMod), (DamageState.Severe, pSev),
                                           (DamageState.Collapse, damage.PCollapse) })
        {
            var r = Occupant(type, state, burning, e, time);
            death += p * r.Death;
            injury += p * r.Injury;
        }
        return new CasualtyRisk(death, injury);
    }

    public static double RadiationDeath(double doseRad) => Lognormal(doseRad, 450.0, 0.25);
    public static double RadiationInjury(double doseRad) => Lognormal(doseRad, 150.0, 0.35);

    private static double Lognormal(double x, double median, double beta)
        => x <= 0 ? 0.0 : BuildingDamage.StandardNormalCdf(Math.Log(x / median) / beta);

    // Combinaison de risques indépendants.
    private struct Combiner
    {
        private double _survive, _unhurt;

        public Combiner(double death, double injury)
        {
            _survive = 1 - Math.Clamp(death, 0, 1);
            _unhurt = 1 - Math.Clamp(injury, 0, 1);
        }

        public void Add(double death, double injury)
        {
            _survive *= 1 - Math.Clamp(death, 0, 1);
            _unhurt *= 1 - Math.Clamp(injury, 0, 1);
        }

        public CasualtyRisk Result => new(1 - _survive, _survive * (1 - _unhurt));
    }
}
