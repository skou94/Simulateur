namespace BlastPhysics.Nuclear;

/// <summary>
/// Nuage en champignon : ordres de grandeur pour l'affichage, pas pour le calcul
/// d'effets. Hauteur du sommet stabilisé ajustée en loi de puissance sur des
/// observations ouvertes (~6 km pour 1 kt, ~11 km pour 15 kt, ~64 km pour Tsar Bomba ;
/// cf. Glasstone &amp; Dolan fig. 2.16). Précision ±30 %, et la météo (tropopause,
/// humidité) change beaucoup le résultat réel.
/// </summary>
public sealed class MushroomCloud
{
    public MushroomCloud(NuclearDevice device, double heightOfBurstM)
    {
        double w = Math.Max(device.YieldKt, 1e-3);
        HeightOfBurstM = heightOfBurstM;
        StabilizedTopM = Math.Max(6300.0 * Math.Pow(w, 0.214), heightOfBurstM + 200.0);
        // Rayon du chapeau à stabilisation : ~0.5 × la hauteur du sommet (≈0.7 pour Tsar Bomba).
        StabilizedCapRadiusM = 0.5 * StabilizedTopM;
        // Constante de temps de la montée : ~1 min pour 1 kt, ~2 min pour 1 Mt.
        RiseTimeConstantS = 60.0 * Math.Pow(w, 0.1);
    }

    public double HeightOfBurstM { get; }
    public double StabilizedTopM { get; }
    public double StabilizedCapRadiusM { get; }
    public double RiseTimeConstantS { get; }

    /// <summary>Progression de la montée, de 0 à 1.</summary>
    public double Progress(double timeS) => timeS <= 0 ? 0.0 : 1.0 - Math.Exp(-timeS / RiseTimeConstantS);

    /// <summary>Altitude du centre du chapeau (au-dessus du sol) au temps t.</summary>
    public double CapCenterHeightAt(double timeS)
    {
        double capCenterFinal = StabilizedTopM - 0.35 * StabilizedCapRadiusM;
        return HeightOfBurstM + (capCenterFinal - HeightOfBurstM) * Progress(timeS);
    }
}
