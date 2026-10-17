namespace BlastPhysics.Blast;

/// <summary>
/// Relations de Rankine-Hugoniot pour un choc dans l'air, avec le gamma effectif
/// des chocs forts (dissociation). Ajustements de Speicher &amp; Brode repris dans
/// BLAST.EXE (DNA) ; portage d'après la bibliothèque « glasstone » (MIT).
/// </summary>
public static class ShockRelations
{
    /// <summary>Rapport de pression absolue à travers le choc, ξ = (ΔP + P0) / P0.</summary>
    public static double Strength(double overpressurePa, double ambientPa = Atmosphere.SeaLevelPressurePa)
        => overpressurePa / ambientPa + 1.0;

    /// <summary>Gamma effectif derrière le choc (≈1.40 pour un choc faible).</summary>
    public static double EffectiveGamma(double overpressurePa, double ambientPa = Atmosphere.SeaLevelPressurePa)
    {
        double xi = Strength(overpressurePa, ambientPa);
        double t = 1e-12 * Math.Pow(xi, 6);
        double z = Math.Log(xi) - 0.47 * t / (100.0 + t);
        return 1.402 - 3.4e-4 * Math.Pow(z, 4) / (1.0 + 2.22e-5 * Math.Pow(z, 6));
    }

    /// <summary>
    /// Rapport de densité ρ1/ρ0 à travers le choc : (1 + μξ)/(μ + ξ), μ = (γ+1)/(γ-1).
    /// BLAST.EXE remplace μ par 5.975 au dénominateur, ce qui ne tend pas vers 1 pour
    /// un choc faible ; la forme exacte garde les deux limites correctes.
    /// </summary>
    public static double DensityRatio(double overpressurePa, double ambientPa = Atmosphere.SeaLevelPressurePa)
    {
        double xi = Strength(overpressurePa, ambientPa);
        double g = EffectiveGamma(overpressurePa, ambientPa);
        double mu = (g + 1.0) / (g - 1.0);
        return (1.0 + mu * xi) / (mu + xi);
    }

    /// <summary>
    /// Coefficient de réflexion normale Pr/Ps : 2 pour un choc acoustique,
    /// jusqu'à ~8 (et au-delà avec dissociation) pour un choc fort.
    /// </summary>
    public static double NormalReflectionFactor(double overpressurePa, double ambientPa = Atmosphere.SeaLevelPressurePa)
    {
        double g = EffectiveGamma(overpressurePa, ambientPa);
        double n = DensityRatio(overpressurePa, ambientPa);
        return 2.0 + (g + 1.0) * (n - 1.0) / 2.0;
    }

    /// <summary>Nombre de Mach du front de choc (vitesse du front / c0).</summary>
    public static double ShockMach(double overpressurePa, double ambientPa = Atmosphere.SeaLevelPressurePa)
    {
        if (overpressurePa <= 0) return 1.0;
        double g = EffectiveGamma(overpressurePa, ambientPa);
        return Math.Sqrt(1.0 + (g + 1.0) / (2.0 * g) * overpressurePa / ambientPa);
    }
}
