using System.Globalization;
using System.Text.Json;

namespace CityData;

/// <summary>
/// Téléchargement (API Overpass) et conversion d'une ville OpenStreetMap : bâtiments,
/// voirie, eau et parcs dans un cercle autour du centre. La réponse brute est gardée
/// en cache pour ne télécharger qu'une fois (les données OSM sont sous licence ODbL :
/// « © les contributeurs d'OpenStreetMap »).
/// </summary>
public static class OsmCityLoader
{
    public const string Endpoint = "https://overpass-api.de/api/interpreter";

    // Les bâtiments plus petits (abris, kiosques) n'apportent rien et alourdissent le rendu.
    private const double MinFootprintM2 = 15.0;

    public static string BuildQuery(CityPreset p)
    {
        string around = string.Create(CultureInfo.InvariantCulture, $"(around:{p.RadiusM:0},{p.Latitude:0.######},{p.Longitude:0.######})");
        return $"""
            [out:json][timeout:240];
            (
              way["building"]{around};
              way["highway"~"^(motorway|trunk|primary|secondary|tertiary|residential|unclassified|living_street|pedestrian)$"]{around};
              way["natural"="water"]{around};
              relation["natural"="water"]{around};
              way["waterway"="riverbank"]{around};
              way["leisure"~"^(park|garden)$"]{around};
              way["landuse"~"^(grass|forest|meadow|recreation_ground|cemetery)$"]{around};
            );
            out geom qt;
            """;
    }

    /// <summary>Charge la ville depuis le cache, ou la télécharge (30 s à 2 min selon la ville).</summary>
    public static async Task<RealCity> LoadAsync(CityPreset preset, string cacheDirectory,
                                                 IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        string cacheFile = Path.Combine(cacheDirectory, $"{preset.Id}_{preset.RadiusM:0}.json");
        if (!File.Exists(cacheFile))
        {
            progress?.Report("Téléchargement des données OpenStreetMap…");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlastSim/1.0 (simulateur educatif)");
            using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", BuildQuery(preset)) });
            using var response = await http.PostAsync(Endpoint, content, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string tmp = cacheFile + ".part";
            await using (var file = File.Create(tmp))
                await response.Content.CopyToAsync(file, cancel).ConfigureAwait(false);
            File.Move(tmp, cacheFile, overwrite: true);
        }
        progress?.Report("Lecture des données…");
        await using var stream = File.OpenRead(cacheFile);
        return await Task.Run(() => Parse(stream, preset), cancel).ConfigureAwait(false);
    }

    /// <summary>Convertit une réponse Overpass (« out geom ») en ville.</summary>
    public static RealCity Parse(Stream json, CityPreset preset)
    {
        using var doc = JsonDocument.Parse(json);
        var proj = new GeoProjection(preset.Latitude, preset.Longitude);
        var buildings = new List<CityBuilding>();
        var roads = new List<Road>();
        var areas = new List<Area>();

        foreach (var e in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            var tags = ReadTags(e);
            string type = e.GetProperty("type").GetString()!;
            if (type == "way" && e.TryGetProperty("geometry", out var geom))
            {
                var pts = ReadGeometry(geom, proj);
                if (pts.Count < 2) continue;
                if (tags.ContainsKey("building")) AddBuilding(buildings, pts, tags, preset);
                else if (tags.TryGetValue("highway", out var hw)) roads.Add(new Road(pts, Classify(hw)));
                else if (AreaOf(tags) is { } kind && pts.Count >= 3) areas.Add(new Area(pts, kind, IsHole: false));
            }
            else if (type == "relation" && AreaOf(tags) is { } kind && e.TryGetProperty("members", out var members))
            {
                // Multipolygone (fleuves, lacs) : ses anneaux sont découpés en plusieurs voies,
                // qu'il faut raccorder bout à bout. Extérieurs pleins, intérieurs évidés.
                var outer = new List<List<Point2>>();
                var inner = new List<List<Point2>>();
                foreach (var m in members.EnumerateArray())
                {
                    if (!m.TryGetProperty("geometry", out var mg)) continue;
                    var pts = ReadGeometry(mg, proj);
                    if (pts.Count < 2) continue;
                    bool isInner = m.TryGetProperty("role", out var role) && role.GetString() == "inner";
                    (isInner ? inner : outer).Add(pts);
                }
                foreach (var ring in AssembleRings(outer)) areas.Add(new Area(ring, kind, IsHole: false));
                foreach (var ring in AssembleRings(inner)) areas.Add(new Area(ring, kind, IsHole: true));
            }
        }
        return new RealCity(preset.Name, preset.RadiusM, buildings, roads, areas);
    }

    private static void AddBuilding(List<CityBuilding> list, List<Point2> ring, Dictionary<string, string> tags, CityPreset preset)
    {
        double area = Polygon.Area(ring);
        if (area < MinFootprintM2) return;
        var box = Polygon.MinimumAreaRectangle(ring);
        if (box.Center.Length > preset.RadiusM) return;
        // Le rectangle orienté est ramené à l'aire réelle : les formes en L ou à cour
        // gardent leur surface de plancher, donc leur population.
        double k = Math.Sqrt(area / Math.Max(box.Length * box.Width, 1e-6));
        double height = BuildingClassifier.Height(tags, preset.Region);
        list.Add(new CityBuilding(
            box.Center.X, box.Center.Z, box.Length * k, box.Width * k, box.Angle, height,
            BuildingClassifier.Structure(tags, height, preset.Region), BuildingClassifier.Use(tags), area));
    }

    /// <summary>
    /// Raccorde des morceaux de contour qui se touchent par leurs extrémités (en les
    /// retournant si besoin) jusqu'à refermer chaque anneau.
    /// </summary>
    public static List<List<Point2>> AssembleRings(List<List<Point2>> pieces)
    {
        const double snapM = 0.5;
        static bool Near(Point2 a, Point2 b) => (a - b).Length < snapM;
        var pool = new List<List<Point2>>(pieces);
        var rings = new List<List<Point2>>();
        while (pool.Count > 0)
        {
            var ring = new List<Point2>(pool[^1]);
            pool.RemoveAt(pool.Count - 1);
            bool extended = true;
            while (!Near(ring[0], ring[^1]) && extended)
            {
                extended = false;
                for (int i = 0; i < pool.Count; i++)
                {
                    var p = pool[i];
                    if (Near(ring[^1], p[0])) ring.AddRange(p.Skip(1));
                    else if (Near(ring[^1], p[^1])) ring.AddRange(Enumerable.Reverse(p).Skip(1));
                    else continue;
                    pool.RemoveAt(i);
                    extended = true;
                    break;
                }
            }
            // Un anneau resté ouvert (coupé par la zone de téléchargement) est fermé tel quel.
            if (ring.Count >= 3) rings.Add(ring);
        }
        return rings;
    }

    private static RoadClass Classify(string highway) => highway switch
    {
        "motorway" or "trunk" or "primary" => RoadClass.Major,
        "secondary" or "tertiary" => RoadClass.Secondary,
        "pedestrian" => RoadClass.Pedestrian,
        _ => RoadClass.Local,
    };

    private static AreaKind? AreaOf(Dictionary<string, string> tags)
    {
        if (tags.GetValueOrDefault("natural") == "water" || tags.GetValueOrDefault("waterway") == "riverbank") return AreaKind.Water;
        if (tags.ContainsKey("leisure") || tags.ContainsKey("landuse")) return AreaKind.Park;
        return null;
    }

    private static Dictionary<string, string> ReadTags(JsonElement e)
    {
        var tags = new Dictionary<string, string>();
        if (e.TryGetProperty("tags", out var t))
            foreach (var p in t.EnumerateObject()) tags[p.Name] = p.Value.GetString() ?? "";
        return tags;
    }

    private static List<Point2> ReadGeometry(JsonElement geom, GeoProjection proj)
    {
        var pts = new List<Point2>(geom.GetArrayLength());
        foreach (var g in geom.EnumerateArray())
        {
            // Overpass met « null » pour les nœuds hors de la zone demandée.
            if (g.ValueKind != JsonValueKind.Object) continue;
            pts.Add(proj.ToLocal(g.GetProperty("lat").GetDouble(), g.GetProperty("lon").GetDouble()));
        }
        return pts;
    }
}
