using BlastPhysics.Conventional;
using BlastPhysics.Nuclear;

namespace BlastPhysics;

/// <summary>
/// Scénarios de référence tirés de sources ouvertes, pour les préréglages et la validation.
/// </summary>
public static class Scenarios
{
    /// <summary>Grenade défensive type M67 : 180 g de Composition B, ~200 g d'acier.</summary>
    public static Detonation HandGrenade() => Detonation.Conventional(
        new ConventionalCharge(Explosive.CompositionB, 0.180, CasingMassKg: 0.20, FragmentMassKg: 2.0e-4),
        heightOfBurstM: 0.0, name: "Grenade M67");

    /// <summary>Bombe Mk 82 : 89 kg de tritonal dans ~150 kg d'acier.</summary>
    public static Detonation Mk82() => Detonation.Conventional(
        new ConventionalCharge(Explosive.Tritonal, 89.0, CasingMassKg: 150.0, FragmentMassKg: 5.0e-3),
        heightOfBurstM: 0.0, name: "Mk 82 (227 kg)");

    /// <summary>GBU-43/B « MOAB » : 8 482 kg de H-6 (~11 t d'équivalent TNT), explosion à ~2 m du sol.</summary>
    public static Detonation Moab() => Detonation.Conventional(
        new ConventionalCharge(Explosive.H6, 8482.0),
        heightOfBurstM: 2.0, name: "GBU-43/B MOAB");

    /// <summary>Port de Beyrouth, 4 août 2020 : ~2 750 t de nitrate d'ammonium au sol.</summary>
    public static Detonation Beirut2020() => Detonation.Conventional(
        new ConventionalCharge(Explosive.AmmoniumNitrate, 2.75e6),
        heightOfBurstM: 0.0, name: "Beyrouth 2020");

    /// <summary>
    /// Hiroshima, 6 août 1945 : Little Boy, arme à canon à uranium, 15 kt à 580 m.
    /// Cas bas de Glasstone pour la radiation (~10 Gy à 1 km) : environ 2 fois la dosimétrie
    /// de référence DS02 (~4-5 Gy à 1 km), contre ~5 fois pour le cas haut.
    /// </summary>
    public static Detonation Hiroshima() => Detonation.Nuclear(
        new NuclearDevice(15.0, NuclearDesign.Fission),
        heightOfBurstM: 580.0, name: "Hiroshima (Little Boy)");

    /// <summary>Nagasaki, 9 août 1945 : Fat Man, implosion au plutonium, 21 kt à 503 m.</summary>
    public static Detonation Nagasaki() => Detonation.Nuclear(
        new NuclearDevice(21.0, NuclearDesign.Fission),
        heightOfBurstM: 503.0, name: "Nagasaki (Fat Man)");

    /// <summary>Arme à rayonnement renforcé de 1 kt (type W70 Mod 3), explosion à 150 m.</summary>
    public static Detonation NeutronBomb() => Detonation.Nuclear(
        new NuclearDevice(1.0, NuclearDesign.EnhancedRadiation, FissionFraction: 0.25),
        heightOfBurstM: 150.0, name: "Bombe à neutrons 1 kt");

    /// <summary>Ogive thermonucléaire de 1.2 Mt (type B83), explosion aérienne à 2 400 m.</summary>
    public static Detonation StrategicWarhead() => Detonation.Nuclear(
        new NuclearDevice(1200.0, NuclearDesign.Thermonuclear, FissionFraction: 0.5),
        heightOfBurstM: 2400.0, name: "Ogive stratégique 1.2 Mt");

    /// <summary>Tsar Bomba, 30 octobre 1961 : ~50 Mt (fission ~3 %), explosion à ~4 000 m.</summary>
    public static Detonation TsarBomba() => Detonation.Nuclear(
        new NuclearDevice(50_000.0, NuclearDesign.Thermonuclear, FissionFraction: 0.03),
        heightOfBurstM: 4000.0, name: "Tsar Bomba");

    /// <summary>
    /// Castle Bravo, 1er mars 1954 (atoll de Bikini) : 15 Mt (fission ~67 %) au sol, le pire
    /// accident de retombées de l'histoire des essais américains.
    /// </summary>
    public static Detonation CastleBravo() => Detonation.Nuclear(
        new NuclearDevice(15_000.0, NuclearDesign.Thermonuclear, FissionFraction: 0.67),
        heightOfBurstM: 0.0, name: "Castle Bravo (au sol)");

    public static IReadOnlyList<Func<Detonation>> All { get; } = new Func<Detonation>[]
    {
        HandGrenade, Mk82, Moab, Beirut2020, Hiroshima, Nagasaki, NeutronBomb, StrategicWarhead, TsarBomba, CastleBravo,
    };
}
