using System.Text;
using BlastPhysics.Casualties;
using BlastPhysics.Damage;
using CityData;

namespace BlastPhysics.Tests;

public class CityDataTests
{
    private static List<Point2> Rectangle(double cx, double cz, double length, double width, double angle)
    {
        var u = new Point2(Math.Cos(angle), Math.Sin(angle));
        var v = new Point2(-u.Z, u.X);
        var c = new Point2(cx, cz);
        return new List<Point2>
        {
            c + u * (length / 2) + v * (width / 2), c - u * (length / 2) + v * (width / 2),
            c - u * (length / 2) - v * (width / 2), c + u * (length / 2) - v * (width / 2),
        };
    }

    [Fact]
    public void MinimumAreaRectangle_RecoversARotatedRectangle()
    {
        var ring = Rectangle(10, -5, 40, 12, 0.6);
        var box = Polygon.MinimumAreaRectangle(ring);
        Assert.Equal(10, box.Center.X, 6);
        Assert.Equal(-5, box.Center.Z, 6);
        Assert.Equal(40 * 12, box.Length * box.Width, 4);
        Assert.Equal(480, Polygon.Area(ring), 6);
    }

    [Fact]
    public void Projection_KeepsDistances()
    {
        // 0.01° de latitude ≈ 1 106 m ; le nord est vers −Z.
        var proj = new GeoProjection(48.85, 2.35);
        var p = proj.ToLocal(48.86, 2.35);
        Assert.Equal(-1105.7, p.Z, 0);
        Assert.Equal(0, p.X, 6);
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12.5 m", 12.5)]
    [InlineData("40 ft", 12.192)]
    [InlineData("7,5", 7.5)]
    public void ParseMeters_ReadsCommonFormats(string text, double expected)
        => Assert.Equal(expected, BuildingClassifier.ParseMeters(text)!.Value, 3);

    [Fact]
    public void Classifier_UsesLevelsMaterialAndRegion()
    {
        var region = CityPresets.Paris.Region;
        var tags = new Dictionary<string, string> { ["building"] = "apartments", ["building:levels"] = "6" };
        Assert.Equal(6 * 3.2 + 1, BuildingClassifier.Height(tags, region), 6);
        Assert.Equal(StructureType.UnreinforcedMasonry, BuildingClassifier.Structure(tags, 20, region));
        Assert.Equal(Occupancy.Residential, BuildingClassifier.Use(tags));

        var tower = new Dictionary<string, string> { ["building"] = "office", ["height"] = "180" };
        Assert.Equal(StructureType.SteelFrame, BuildingClassifier.Structure(tower, 180, CityPresets.Manhattan.Region));
        Assert.Equal(Occupancy.Office, BuildingClassifier.Use(tower));

        var house = new Dictionary<string, string> { ["building"] = "house" };
        Assert.Equal(StructureType.WoodFrame, BuildingClassifier.Structure(house, 7, CityPresets.Hiroshima.Region));
        var wooden = new Dictionary<string, string> { ["building"] = "yes", ["building:material"] = "wood" };
        Assert.Equal(StructureType.WoodFrame, BuildingClassifier.Structure(wooden, 7, region));
    }

    [Fact]
    public void AssembleRings_JoinsPiecesInAnyDirection()
    {
        var a = new List<Point2> { new(0, 0), new(10, 0) };
        var b = new List<Point2> { new(10, 10), new(10, 0) }; // à l'envers
        var c = new List<Point2> { new(10, 10), new(0, 10), new(0, 0) };
        var rings = OsmCityLoader.AssembleRings(new List<List<Point2>> { a, b, c });
        Assert.Single(rings);
        Assert.Equal(100, Polygon.Area(rings[0]), 6);
    }

    [Fact]
    public void Parse_ReadsBuildingsRoadsAndWater()
    {
        // Réponse Overpass miniature autour de (0, 0) : un bâtiment, une rue, un lac.
        const string json = """
        {"elements":[
          {"type":"way","id":1,"geometry":[{"lat":0,"lon":0},{"lat":0,"lon":0.0003},{"lat":0.0002,"lon":0.0003},{"lat":0.0002,"lon":0},{"lat":0,"lon":0}],
           "tags":{"building":"yes","building:levels":"4"}},
          {"type":"way","id":2,"geometry":[{"lat":-0.001,"lon":-0.001},{"lat":-0.001,"lon":0.001}],"tags":{"highway":"primary"}},
          {"type":"way","id":3,"geometry":[{"lat":0.005,"lon":0.005},{"lat":0.005,"lon":0.006},{"lat":0.006,"lon":0.006},{"lat":0.005,"lon":0.005}],
           "tags":{"natural":"water"}}
        ]}
        """;
        var preset = new CityPreset("test", "Test", 0, 0, 2000, CityPresets.Paris.Region);
        var city = OsmCityLoader.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)), preset);
        var b = Assert.Single(city.Buildings);
        Assert.InRange(b.FootprintM2, 700, 780); // ≈ 33.4 m × 22.1 m
        Assert.Equal(4 * 3.2 + 1, b.HeightM, 6);
        Assert.Equal(RoadClass.Major, Assert.Single(city.Roads).Class);
        Assert.Equal(AreaKind.Water, Assert.Single(city.Areas).Kind);
    }

    [Fact]
    public void MapRaster_FillsAreasAndKeepsHoles()
    {
        var outer = Rectangle(0, 0, 800, 800, 0);
        var hole = Rectangle(0, 0, 200, 200, 0);
        var city = new RealCity("t", 500, Array.Empty<CityBuilding>(), Array.Empty<Road>(),
            new[] { new Area(outer, AreaKind.Water, false), new Area(hole, AreaKind.Water, true) });
        var img = MapRaster.Render(city, 100);
        byte Water(int x, int y) => img[(y * 100 + x) * 4 + 2];
        Assert.Equal(255, Water(20, 50));  // dans l'eau
        Assert.Equal(0, Water(50, 50));    // dans l'île
        Assert.Equal(0, Water(2, 2));      // hors du lac
    }
}
