using BlastPhysics.Damage;

namespace CityData;

/// <summary>Ville réelle à charger : centre (point zéro par défaut), rayon et pratiques locales.</summary>
public sealed record CityPreset(string Id, string Name, double Latitude, double Longitude, double RadiusM, RegionProfile Region);

public static class CityPresets
{
    public static readonly CityPreset Paris = new("paris", "Paris — Hôtel de Ville", 48.8566, 2.3522, 2500,
        new RegionProfile(6, StructureType.UnreinforcedMasonry, StructureType.UnreinforcedMasonry, StructureType.ReinforcedConcrete));

    public static readonly CityPreset Manhattan = new("manhattan", "New York — Midtown Manhattan", 40.7549, -73.9840, 2500,
        new RegionProfile(5, StructureType.WoodFrame, StructureType.UnreinforcedMasonry, StructureType.SteelFrame));

    public static readonly CityPreset London = new("london", "Londres — Trafalgar Square", 51.5080, -0.1281, 2500,
        new RegionProfile(4, StructureType.UnreinforcedMasonry, StructureType.UnreinforcedMasonry, StructureType.ReinforcedConcrete));

    /// <summary>Hiroshima actuelle, centrée sur l'hypocentre de 1945 (hôpital Shima).</summary>
    public static readonly CityPreset Hiroshima = new("hiroshima", "Hiroshima — hypocentre de 1945", 34.3955, 132.4536, 2500,
        new RegionProfile(3, StructureType.WoodFrame, StructureType.ReinforcedConcrete, StructureType.ReinforcedConcrete));

    /// <summary>Beyrouth, centrée sur l'entrepôt 12 du port (explosion du 4 août 2020).</summary>
    public static readonly CityPreset Beirut = new("beirut", "Beyrouth — port", 33.9013, 35.5190, 2500,
        new RegionProfile(5, StructureType.UnreinforcedMasonry, StructureType.ReinforcedConcrete, StructureType.ReinforcedConcrete));

    public static IReadOnlyList<CityPreset> All { get; } = new[] { Paris, Manhattan, London, Hiroshima, Beirut };
}
