using BlastPhysics.Casualties;
using BlastPhysics.Damage;

namespace CityData;

/// <summary>Élément OpenStreetMap brut : géométrie projetée et étiquettes.</summary>
public sealed record OsmWay(long Id, IReadOnlyList<Point2> Points, IReadOnlyDictionary<string, string> Tags, bool IsInnerRing = false);

public enum AreaKind { Water, Park }

/// <summary>Classe de voie, qui fixe sa largeur sur la carte.</summary>
public enum RoadClass { Major, Secondary, Local, Pedestrian }

public sealed record Road(IReadOnlyList<Point2> Points, RoadClass Class)
{
    /// <summary>Largeur de chaussée typique (m).</summary>
    public double WidthM => Class switch
    {
        RoadClass.Major => 22,
        RoadClass.Secondary => 14,
        RoadClass.Local => 9,
        _ => 6,
    };
}

public sealed record Area(IReadOnlyList<Point2> Ring, AreaKind Kind, bool IsHole);

/// <summary>Bâtiment prêt à l'emploi : rectangle orienté, hauteur, type et usage.</summary>
public sealed record CityBuilding(
    double X, double Z, double Length, double Width, double AngleRad,
    double HeightM, StructureType Structure, Occupancy Use, double FootprintM2);

/// <summary>Ville réelle extraite d'OpenStreetMap, en coordonnées locales (m).</summary>
public sealed record RealCity(
    string Name,
    double RadiusM,
    IReadOnlyList<CityBuilding> Buildings,
    IReadOnlyList<Road> Roads,
    IReadOnlyList<Area> Areas);
