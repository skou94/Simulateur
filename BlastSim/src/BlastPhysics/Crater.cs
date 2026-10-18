using BlastPhysics.Conventional;
using BlastPhysics.Nuclear;

namespace BlastPhysics;

public enum SoilType
{
    /// <summary>Sol sec, roche tendre sèche.</summary>
    DrySoil,
    /// <summary>Sol saturé d'eau (bords de fleuve, ports, corail).</summary>
    WetSoil,
    /// <summary>Roche dure.</summary>
    HardRock,
}

/// <summary>
/// Cratère apparent d'une explosion au sol ou près du sol (cuvette visible après
/// retombée des déblais, et bourrelet de déblais autour).
/// <list type="bullet">
/// <item>Nucléaire : rayon R = R1·W^(1/3.4) (W en kt), l'exposant inférieur à 1/3 traduisant
/// le rôle de la gravité aux fortes puissances ; R1 ≈ 18 m en sol sec, 27 m en sol saturé,
/// 14 m dans la roche pour un contact au sol (Glasstone &amp; Dolan §6.70, ordres de grandeur).
/// Validé sur Cactus (18 kt sur corail, 1958 : ~110 m de diamètre) ; les cratères d'Ivy Mike
/// et de Castle Bravo, sur atolls gorgés d'eau, sont ~2 fois plus grands que prévu.</item>
/// <item>Explosif chimique au contact du sol : diamètre D ≈ 0.8·W^(1/3) m, W en kg de TNT
/// (Ambrosini et al., 2002, sol sec) ; ~0.4 m pour 100 g, ~4 m pour une Mk 82 au sol.</item>
/// <item>Hauteur d'explosion : le cratère se réduit vite puis disparaît (rayon nul quand la
/// boule de feu ne touche plus le sol en nucléaire, au-delà de ~1.5 R en conventionnel).</item>
/// </list>
/// Incertitude : facteur ~2 (nature du sol, enfouissement, eau).
/// </summary>
public sealed record Crater(double RadiusM, double DepthM, double LipRadiusM, double LipHeightM)
{
    public static Crater None { get; } = new(0, 0, 0, 0);

    public bool Exists => RadiusM > 0.05;

    public static Crater ForNuclear(NuclearDevice device, Fireball fireball, SoilType soil = SoilType.DrySoil)
    {
        var (r1, depthRatio) = soil switch
        {
            SoilType.WetSoil => (27.0, 0.35),
            SoilType.HardRock => (14.0, 0.5),
            _ => (18.0, 0.5),
        };
        double contact = Math.Clamp(1.0 - fireball.HeightOfBurstM / fireball.AirburstMaxRadiusM, 0.0, 1.0);
        // Décroissance rapide : une boule de feu qui effleure le sol (Tsar Bomba) ne creuse rien.
        double radius = r1 * Math.Pow(Math.Max(device.YieldKt, 1e-6), 1.0 / 3.4) * contact * contact;
        return Build(radius, depthRatio);
    }

    public static Crater ForConventional(ConventionalCharge charge, double heightOfBurstM, SoilType soil = SoilType.DrySoil)
    {
        double factor = soil switch { SoilType.WetSoil => 1.5, SoilType.HardRock => 0.6, _ => 1.0 };
        double contactRadius = 0.4 * Math.Cbrt(charge.TntEquivalentKg) * factor;
        double contact = Math.Clamp(1.0 - heightOfBurstM / (1.5 * contactRadius), 0.0, 1.0);
        return Build(contactRadius * Math.Sqrt(contact), soil == SoilType.WetSoil ? 0.35 : 0.5);
    }

    // Bourrelet : déblais jusqu'à ~2 rayons, d'une hauteur ~1/4 de la profondeur.
    private static Crater Build(double radius, double depthRatio)
        => radius <= 0.05 ? None : new Crater(radius, depthRatio * radius, 2.0 * radius, 0.25 * depthRatio * radius);
}
