namespace BlastPhysics.Nuclear;

public enum NuclearDesign
{
    /// <summary>Arme à fission pure (A).</summary>
    Fission,
    /// <summary>Arme thermonucléaire à deux étages (H).</summary>
    Thermonuclear,
    /// <summary>Arme à rayonnement renforcé (« bombe à neutrons »).</summary>
    EnhancedRadiation,
}

/// <summary>
/// Caractéristiques d'un engin nucléaire.
/// Répartition de l'énergie d'une arme classique (Glasstone §1.24) : ~50 % souffle,
/// ~35 % thermique, ~5 % radiation initiale, ~10 % radiation résiduelle.
/// </summary>
/// <param name="YieldKt">Rendement total, kt.</param>
/// <param name="FissionFraction">Part du rendement issue de la fission (1 pour une arme A).</param>
/// <param name="HighRadiationOutput">
/// Cas haut de Glasstone pour la radiation initiale (ex. arme à canon à uranium,
/// peu écrantée) ; sinon cas bas.
/// </param>
public sealed record NuclearDevice(
    double YieldKt,
    NuclearDesign Design = NuclearDesign.Fission,
    double FissionFraction = 1.0,
    bool HighRadiationOutput = false)
{
    /// <summary>
    /// Rendement à injecter dans la courbe de souffle DNA, qui est normalisée pour
    /// une arme classique. Une arme à rayonnement renforcé met ~40 % de son énergie
    /// dans le souffle au lieu de ~50 % : facteur 0.8 (hypothèse documentée,
    /// sources ouvertes seulement).
    /// </summary>
    public double BlastEquivalentKt => Design == NuclearDesign.EnhancedRadiation ? 0.8 * YieldKt : YieldKt;

    /// <summary>Part du rendement émise en rayonnement thermique (explosion aérienne).</summary>
    public double ThermalFraction => Design == NuclearDesign.EnhancedRadiation ? 0.25 : 0.35;

    /// <summary>
    /// Multiplicateur de dose neutronique par rapport à la courbe thermonucléaire de
    /// Glasstone. 10 donne ~8 000 rad à ~700 m pour 1 kt, l'ordre de grandeur publié
    /// pour les armes ER (hypothèse documentée).
    /// </summary>
    public double NeutronMultiplier => Design == NuclearDesign.EnhancedRadiation ? 10.0 : 1.0;

    public double EnergyJ => YieldKt * Units.JoulesPerKiloton;
}
