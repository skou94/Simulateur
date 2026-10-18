using System;
using System.Collections.Generic;
using BlastPhysics.Casualties;
using BlastPhysics.Damage;
using Godot;

namespace BlastSimGame;

/// <summary>Bâtiment généré : géométrie et type de construction.</summary>
public sealed class Building
{
    public float X, Z;              // centre au sol (m)
    public float Width, Depth, Height;
    public float Rotation;          // rad autour de la verticale
    public StructureType Structure;
    public Occupancy Use;
    public Color Color;
    public float Seed;              // aléa propre au bâtiment, 0..1

    /// <summary>Surface de plancher : emprise × nombre d'étages (3.2 m par niveau).</summary>
    public float FloorAreaM2 => Width * Depth * MathF.Max(1f, MathF.Round(Height / 3.2f));
}

/// <summary>
/// Ville procédurale en damier : quartier d'affaires de tours au centre, immeubles
/// de maçonnerie et de béton autour, maisons à ossature bois en périphérie.
/// La grille des rues est régulière pour que le shader du sol la retrouve.
/// </summary>
public sealed class CityModel
{
    public const float BlockPitch = 120f;
    public const float StreetWidth = 14f;

    private const float DowntownRadius = 1000f;
    private const float UrbanRadius = 3600f;

    public CityModel(float halfSize, int blocksPerSide, List<Building> buildings, bool[] parks)
    {
        HalfSize = halfSize;
        BlocksPerSide = blocksPerSide;
        Buildings = buildings;
        Parks = parks;
    }

    /// <summary>Nom affiché ; null pour la ville procédurale.</summary>
    public string? RealName { get; private init; }

    /// <summary>Carte au sol d'une ville réelle (RGBA : chaussée, parc, eau), ou null.</summary>
    public byte[]? Map { get; private init; }
    public int MapSize { get; private init; }

    /// <summary>
    /// Ville réelle OpenStreetMap : chaque emprise devient un rectangle orienté de même
    /// aire ; hauteur, type de construction et usage viennent des étiquettes OSM.
    /// </summary>
    public static CityModel FromReal(CityData.RealCity city, int seed, int mapSize = 2048)
    {
        var rng = new Random(seed);
        var list = new List<Building>(city.Buildings.Count);
        foreach (var b in city.Buildings)
        {
            var palette = b.Structure switch
            {
                StructureType.SteelFrame => GlassTints,
                StructureType.ReinforcedConcrete => ConcreteTints,
                StructureType.WoodFrame => WoodTints,
                _ => StoneTints,
            };
            list.Add(new Building
            {
                X = (float)b.X, Z = (float)b.Z,
                Width = (float)Math.Max(b.Length, 2.0), Depth = (float)Math.Max(b.Width, 2.0),
                Height = (float)b.HeightM,
                // L'axe local X de la boîte doit suivre le côté « Length » du rectangle OSM.
                Rotation = (float)-b.AngleRad,
                Structure = b.Structure,
                Use = b.Use,
                Color = Pick(rng, palette),
                Seed = (float)rng.NextDouble(),
            });
        }
        return new CityModel((float)city.RadiusM, 0, list, Array.Empty<bool>())
        {
            RealName = city.Name,
            Map = CityData.MapRaster.Render(city, mapSize),
            MapSize = mapSize,
        };
    }

    public float HalfSize { get; }
    public int BlocksPerSide { get; }
    public List<Building> Buildings { get; }
    /// <summary>Parcs, indexés par bloc (j * BlocksPerSide + i).</summary>
    public bool[] Parks { get; }

    public static CityModel Generate(int seed, float sizeM = 10080f)
    {
        var rng = new Random(seed);
        int n = (int)(sizeM / BlockPitch);
        float half = n * BlockPitch / 2f;
        var buildings = new List<Building>(60000);
        var parks = new bool[n * n];

        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + i * BlockPitch + StreetWidth / 2f;
            float z0 = -half + j * BlockPitch + StreetWidth / 2f;
            float usable = BlockPitch - StreetWidth;
            float cx = x0 + usable / 2f, cz = z0 + usable / 2f;
            float dist = MathF.Sqrt(cx * cx + cz * cz);

            if (dist > DowntownRadius * 0.6f && rng.NextDouble() < 0.06)
            {
                parks[j * n + i] = true;
                continue;
            }

            if (dist < DowntownRadius)
                FillBlock(buildings, rng, x0, z0, usable, lots: 2, Downtown);
            else if (dist < UrbanRadius)
                FillBlock(buildings, rng, x0, z0, usable, lots: 3, Urban);
            else
            {
                // La périphérie se vide progressivement vers les champs.
                double occupancy = Math.Clamp(1.0 - (dist - UrbanRadius) / 2600.0, 0.15, 1.0) * 0.7;
                if (rng.NextDouble() < 0.12) continue;
                FillBlock(buildings, rng, x0, z0, usable, lots: 4, (r, lot) => Suburb(r, lot, occupancy));
            }
        }
        return new CityModel(half, n, buildings, parks);
    }

    private delegate Building? LotBuilder(Random rng, float lotSize);

    private static void FillBlock(List<Building> list, Random rng, float x0, float z0, float usable, int lots, LotBuilder make)
    {
        float lot = usable / lots;
        for (int b = 0; b < lots; b++)
        for (int a = 0; a < lots; a++)
        {
            var bld = make(rng, lot);
            if (bld is null) continue;
            float slackX = MathF.Max(0f, lot - bld.Width) / 2f;
            float slackZ = MathF.Max(0f, lot - bld.Depth) / 2f;
            bld.X = x0 + (a + 0.5f) * lot + (float)(rng.NextDouble() * 2 - 1) * slackX * 0.6f;
            bld.Z = z0 + (b + 0.5f) * lot + (float)(rng.NextDouble() * 2 - 1) * slackZ * 0.6f;
            bld.Seed = (float)rng.NextDouble();
            list.Add(bld);
        }
    }

    private static Building? Downtown(Random rng, float lot)
    {
        // Hauteurs lognormales : médiane ~45 m, quelques tours au-delà de 150 m.
        float h = (float)Math.Clamp(45.0 * Math.Exp(0.55 * Gaussian(rng)), 20.0, 230.0);
        bool steel = rng.NextDouble() < 0.5;
        var tint = steel ? Pick(rng, GlassTints) : Pick(rng, ConcreteTints);
        return new Building
        {
            Width = Range(rng, 0.62f, 0.9f) * lot,
            Depth = Range(rng, 0.62f, 0.9f) * lot,
            Height = h,
            Structure = steel ? StructureType.SteelFrame : StructureType.ReinforcedConcrete,
            Use = Occupancy.Office,
            Color = tint,
        };
    }

    private static Building? Urban(Random rng, float lot)
    {
        if (rng.NextDouble() < 0.1) return null; // cours intérieures
        bool concrete = rng.NextDouble() < 0.35;
        int floors = rng.Next(3, concrete ? 12 : 8);
        return new Building
        {
            Width = Range(rng, 0.75f, 0.95f) * lot,
            Depth = Range(rng, 0.75f, 0.95f) * lot,
            Height = floors * 3.2f + 1f,
            Structure = concrete ? StructureType.ReinforcedConcrete : StructureType.UnreinforcedMasonry,
            Color = concrete ? Pick(rng, ConcreteTints) : Pick(rng, MasonryTints),
        };
    }

    private static Building? Suburb(Random rng, float lot, double occupancy)
    {
        if (rng.NextDouble() > occupancy) return null;
        bool wood = rng.NextDouble() < 0.7;
        return new Building
        {
            Width = Range(rng, 0.4f, 0.6f) * lot,
            Depth = Range(rng, 0.4f, 0.6f) * lot,
            Height = rng.Next(1, 3) * 3.0f + 1.5f,
            Rotation = rng.NextDouble() < 0.15 ? Range(rng, -0.3f, 0.3f) : 0f,
            Structure = wood ? StructureType.WoodFrame : StructureType.UnreinforcedMasonry,
            Color = wood ? Pick(rng, WoodTints) : Pick(rng, MasonryTints),
        };
    }

    private static readonly Color[] GlassTints =
        { new(0.42f, 0.52f, 0.60f), new(0.35f, 0.45f, 0.50f), new(0.55f, 0.60f, 0.64f), new(0.30f, 0.38f, 0.46f) };
    private static readonly Color[] ConcreteTints =
        { new(0.72f, 0.71f, 0.68f), new(0.62f, 0.62f, 0.60f), new(0.78f, 0.75f, 0.68f), new(0.55f, 0.56f, 0.57f) };
    private static readonly Color[] MasonryTints =
        { new(0.66f, 0.38f, 0.28f), new(0.75f, 0.62f, 0.45f), new(0.82f, 0.76f, 0.64f), new(0.58f, 0.34f, 0.26f) };
    // Pierre calcaire et enduits clairs (immeubles anciens européens).
    private static readonly Color[] StoneTints =
        { new(0.82f, 0.77f, 0.66f), new(0.76f, 0.72f, 0.63f), new(0.86f, 0.82f, 0.74f), new(0.70f, 0.62f, 0.52f) };
    private static readonly Color[] WoodTints =
        { new(0.86f, 0.82f, 0.72f), new(0.62f, 0.70f, 0.74f), new(0.66f, 0.72f, 0.58f), new(0.80f, 0.66f, 0.52f) };

    private static Color Pick(Random rng, Color[] palette)
    {
        var c = palette[rng.Next(palette.Length)];
        float k = Range(rng, 0.9f, 1.08f);
        return new Color(c.R * k, c.G * k, c.B * k);
    }

    private static float Range(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
