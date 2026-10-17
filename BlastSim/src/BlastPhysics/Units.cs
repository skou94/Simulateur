namespace BlastPhysics;

/// <summary>
/// Constantes physiques et conversions. Toute la bibliothèque travaille en SI
/// (m, s, kg, Pa, J) ; les rendements nucléaires sont exprimés en kilotonnes.
/// </summary>
public static class Units
{
    /// <summary>Énergie d'un kilotonne de TNT (définition conventionnelle), J.</summary>
    public const double JoulesPerKiloton = 4.184e12;

    /// <summary>Masse de TNT équivalente à 1 kt, kg.</summary>
    public const double KgTntPerKiloton = 1.0e6;

    /// <summary>1 cal/cm² = 41 840 J/m².</summary>
    public const double JPerM2PerCalPerCm2 = 41840.0;

    public const double MetersPerFoot = 0.3048;
    public const double MetersPerYard = 0.9144;

    public static double KgTntToKt(double kg) => kg / KgTntPerKiloton;
    public static double CalPerCm2(double joulesPerM2) => joulesPerM2 / JPerM2PerCalPerCm2;
}
