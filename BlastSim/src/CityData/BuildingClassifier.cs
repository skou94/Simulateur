using System.Globalization;
using BlastPhysics.Casualties;
using BlastPhysics.Damage;

namespace CityData;

/// <summary>
/// Pratiques de construction d'une région, pour les bâtiments dont OSM ne précise
/// ni la hauteur ni le matériau (la majorité).
/// </summary>
/// <param name="DefaultLevels">Nombre d'étages typique d'un bâtiment sans information.</param>
/// <param name="House">Structure des maisons individuelles.</param>
/// <param name="MidRise">Structure des immeubles de moins de 8 étages.</param>
/// <param name="HighRise">Structure des immeubles de 8 étages et plus.</param>
public sealed record RegionProfile(int DefaultLevels, StructureType House, StructureType MidRise, StructureType HighRise);

/// <summary>Hauteur, type de construction et usage déduits des étiquettes OpenStreetMap.</summary>
public static class BuildingClassifier
{
    public const double MetersPerLevel = 3.2;

    private static readonly HashSet<string> Houses = new()
        { "house", "detached", "semidetached_house", "terrace", "bungalow", "cabin", "hut", "farm" };
    private static readonly HashSet<string> Workplaces = new()
        { "office", "commercial", "retail", "industrial", "warehouse", "hotel", "school", "university",
          "college", "hospital", "public", "civic", "government", "train_station", "supermarket", "kindergarten" };
    private static readonly HashSet<string> Sheds = new()
        { "garage", "garages", "shed", "carport", "roof", "kiosk", "service", "toilets" };

    public static double Height(IReadOnlyDictionary<string, string> tags, RegionProfile region)
    {
        if (tags.TryGetValue("height", out var h) && ParseMeters(h) is > 0 and var height) return height;
        if (tags.TryGetValue("building:levels", out var l) && ParseNumber(l) is > 0 and var levels)
        {
            double roof = tags.TryGetValue("roof:levels", out var rl) && ParseNumber(rl) is > 0 and var r ? r : 0;
            return (levels + 0.5 * roof) * MetersPerLevel + 1.0;
        }
        string type = tags.GetValueOrDefault("building", "yes");
        if (Sheds.Contains(type)) return 3.0;
        if (Houses.Contains(type)) return 2 * MetersPerLevel + 1.5;
        if (type is "church" or "cathedral") return 25.0;
        if (type is "industrial" or "warehouse") return 9.0;
        return region.DefaultLevels * MetersPerLevel + 1.0;
    }

    public static StructureType Structure(IReadOnlyDictionary<string, string> tags, double heightM, RegionProfile region)
    {
        string material = tags.GetValueOrDefault("building:material", "");
        switch (material)
        {
            case "wood" or "timber_framing" or "bamboo": return StructureType.WoodFrame;
            case "brick" or "stone" or "masonry" or "sandstone" or "limestone" or "adobe": return StructureType.UnreinforcedMasonry;
            case "concrete" or "reinforced_concrete" or "cement_block": return StructureType.ReinforcedConcrete;
            case "steel" or "metal" or "glass": return StructureType.SteelFrame;
        }
        string type = tags.GetValueOrDefault("building", "yes");
        if (Houses.Contains(type)) return region.House;
        if (type is "industrial" or "warehouse") return StructureType.SteelFrame;
        bool high = heightM >= 8 * MetersPerLevel;
        if (high && type is "office" or "commercial" && region.HighRise != StructureType.UnreinforcedMasonry)
            return StructureType.SteelFrame;
        return high ? region.HighRise : region.MidRise;
    }

    public static Occupancy Use(IReadOnlyDictionary<string, string> tags)
        => Workplaces.Contains(tags.GetValueOrDefault("building", "yes")) ? Occupancy.Office : Occupancy.Residential;

    /// <summary>Lit « 12 », « 12.5 m », « 40 ft » ; null si illisible.</summary>
    public static double? ParseMeters(string text)
    {
        string t = text.Trim().ToLowerInvariant();
        double factor = 1.0;
        if (t.EndsWith("ft") || t.EndsWith("'")) { factor = 0.3048; t = t.TrimEnd('f', 't', '\'', ' '); }
        else t = t.TrimEnd('m', ' ');
        return ParseNumber(t) is { } v ? v * factor : null;
    }

    private static double? ParseNumber(string text)
    {
        string t = text.Trim().Replace(',', '.');
        int end = 0;
        while (end < t.Length && (char.IsDigit(t[end]) || t[end] == '.')) end++;
        return end > 0 && double.TryParse(t[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
