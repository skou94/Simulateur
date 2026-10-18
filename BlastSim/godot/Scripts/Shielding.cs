using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlastSimGame;

/// <summary>
/// Effet d'écran des bâtiments, calculé une fois à la détonation sur la géométrie
/// d'origine (l'éclair et le front de choc passent avant les effondrements).
/// <list type="bullet">
/// <item>Éclair thermique : propagation en ligne droite. Un point est protégé si un
/// bâtiment coupe le segment qui le relie au centre de la boule de feu (« ombres »
/// d'Hiroshima). Fractions éclairées de la façade tournée vers l'explosion (deux hauteurs)
/// et de la rue (devant et derrière le bâtiment, à 1.5 m).</item>
/// <item>Souffle : l'onde contourne les obstacles. Derrière un obstacle de hauteur H, la
/// surpression est réduite d'au plus 50 % (ordre de grandeur des murs de protection,
/// UFC 3-340-02), avec un effet qui s'efface sur ~5 H et pondéré par H / (H + 2L), L étant
/// la longueur de l'onde (c·durée de phase positive) : quelques % pour une onde nucléaire
/// longue de ~150 m (Glasstone : écran peu efficace), notable pour une petite charge.</item>
/// </list>
/// </summary>
public sealed class Shielding
{
    private const float CellSize = 50f;
    private const float MaxBlastReduction = 0.5f;
    private const float BlastShadowLengthInHeights = 5f;
    private const float SoundSpeed = 340f;

    private readonly CityModel _city;
    private readonly float _originX, _originZ;
    private readonly int _cells;
    private readonly List<int>[] _grid;
    private readonly float[] _cellTop; // hauteur du plus haut bâtiment de chaque cellule
    private readonly float _maxHeight;
    private readonly float[] _cos, _sin;

    public Shielding(CityModel city)
    {
        _city = city;
        _originX = _originZ = -city.HalfSize - CellSize;
        _cells = (int)Math.Ceiling((2 * city.HalfSize + 2 * CellSize) / CellSize);
        _grid = new List<int>[_cells * _cells];
        _cellTop = new float[_cells * _cells];
        _cos = new float[city.Buildings.Count];
        _sin = new float[city.Buildings.Count];
        for (int i = 0; i < city.Buildings.Count; i++)
        {
            var b = city.Buildings[i];
            _cos[i] = MathF.Cos(b.Rotation);
            _sin[i] = MathF.Sin(b.Rotation);
            _maxHeight = MathF.Max(_maxHeight, b.Height);
            float r = 0.5f * MathF.Sqrt(b.Width * b.Width + b.Depth * b.Depth);
            int x0 = Cell(b.X - r - _originX), x1 = Cell(b.X + r - _originX);
            int z0 = Cell(b.Z - r - _originZ), z1 = Cell(b.Z + r - _originZ);
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                int cell = cz * _cells + cx;
                (_grid[cell] ??= new List<int>()).Add(i);
                _cellTop[cell] = MathF.Max(_cellTop[cell], b.Height);
            }
        }
    }

    /// <summary>Fractions éclairées (0..1) de la façade et de la rue, pour une source au point (sx, sy, sz).</summary>
    public (float[] Facade, float[] Street) Thermal(float sx, float sy, float sz)
    {
        int n = _city.Buildings.Count;
        var facade = new float[n];
        var street = new float[n];
        Parallel.For(0, n, i =>
        {
            var b = _city.Buildings[i];
            float dx = sx - b.X, dz = sz - b.Z;
            float len = MathF.Max(MathF.Sqrt(dx * dx + dz * dz), 1e-3f);
            float ux = dx / len, uz = dz / len;
            // Juste devant la façade tournée vers l'explosion, puis dans la rue devant et derrière.
            float front = HalfExtentAlong(i, ux, uz) + 0.5f;
            float back = front + 6f;
            int lit = 0;
            foreach (float h in new[] { 0.3f * b.Height, 0.85f * b.Height })
                if (Visible(i, b.X + ux * front, h, b.Z + uz * front, sx, sy, sz)) lit++;
            facade[i] = lit / 2f;
            int litStreet = 0;
            if (Visible(i, b.X + ux * (front + 3f), 1.5f, b.Z + uz * (front + 3f), sx, sy, sz)) litStreet++;
            if (Visible(i, b.X - ux * back, 1.5f, b.Z - uz * back, sx, sy, sz)) litStreet++;
            street[i] = litStreet / 2f;
        });
        return (facade, street);
    }

    /// <summary>
    /// Facteur de surpression (0.5..1) pour chaque bâtiment, selon l'obstacle le plus proche
    /// entre lui et le point zéro. <paramref name="positivePhaseS"/> donne la durée de phase
    /// positive reçue par chaque bâtiment.
    /// </summary>
    public float[] Blast(float gx, float gz, Func<int, float> positivePhaseS)
    {
        int n = _city.Buildings.Count;
        var factor = new float[n];
        Parallel.For(0, n, i =>
        {
            var b = _city.Buildings[i];
            float y = 0.3f * b.Height;
            float wave = SoundSpeed * MathF.Max(positivePhaseS(i), 1e-4f);
            float reach = MathF.Min(BlastShadowLengthInHeights * _maxHeight, Distance(b.X, b.Z, gx, gz));
            float dx = gx - b.X, dz = gz - b.Z;
            float len = MathF.Max(MathF.Sqrt(dx * dx + dz * dz), 1e-3f);
            float ux = dx / len, uz = dz / len;
            float start = HalfExtentAlong(i, ux, uz) + 0.5f;
            var (hit, distance, height) = FirstObstacle(i, b.X + ux * start, y, b.Z + uz * start,
                                                        b.X + ux * (start + reach), y, b.Z + uz * (start + reach));
            if (!hit) { factor[i] = 1f; return; }
            float shadow = Math.Clamp(1f - distance / (BlastShadowLengthInHeights * height), 0f, 1f);
            float weight = height / (height + 2f * wave);
            factor[i] = 1f - MaxBlastReduction * weight * shadow;
        });
        return factor;
    }

    // Demi-largeur du rectangle du bâtiment dans la direction (ux, uz).
    private float HalfExtentAlong(int i, float ux, float uz)
    {
        var b = _city.Buildings[i];
        float lx = MathF.Abs(ux * _cos[i] - uz * _sin[i]), lz = MathF.Abs(ux * _sin[i] + uz * _cos[i]);
        return 0.5f * (b.Width * lx + b.Depth * lz);
    }

    private bool Visible(int self, float px, float py, float pz, float sx, float sy, float sz)
    {
        // Au-delà de la hauteur du plus haut bâtiment, plus rien ne peut masquer la source.
        float top = _maxHeight;
        float ex = sx, ey = sy, ez = sz;
        if (sy > top && py < top)
        {
            float t = (top - py) / (sy - py);
            ex = px + (sx - px) * t; ey = top; ez = pz + (sz - pz) * t;
        }
        else if (py >= top) return true;
        return !FirstObstacle(self, px, py, pz, ex, ey, ez).Hit;
    }

    /// <summary>Premier bâtiment (autre que self) coupé par le segment p→e : distance et hauteur.</summary>
    private (bool Hit, float Distance, float Height) FirstObstacle(int self, float px, float py, float pz, float ex, float ey, float ez)
    {
        float dx = ex - px, dy = ey - py, dz = ez - pz;
        float length = MathF.Sqrt(dx * dx + dz * dz);
        int steps = Math.Max(1, (int)(length / (0.5f * CellSize)));
        int lastCell = -1;
        float best = float.MaxValue, bestHeight = 0;
        for (int k = 0; k <= steps; k++)
        {
            float t = (float)k / steps;
            int cx = Cell(px + dx * t - _originX), cz = Cell(pz + dz * t - _originZ);
            if (cx < 0 || cz < 0 || cx >= _cells || cz >= _cells) break;
            int cell = cz * _cells + cx;
            if (cell == lastCell) continue;
            lastCell = cell;
            var list = _grid[cell];
            if (list is null) continue;
            // Cellule entièrement sous le rayon sur ce pas : aucun bâtiment ne peut le couper.
            float stepT = 1f / steps;
            float rayLow = py + dy * MathF.Max(t - stepT, 0f);
            float rayHigh = py + dy * MathF.Min(t + stepT, 1f);
            if (MathF.Min(rayLow, rayHigh) > _cellTop[cell]) continue;
            foreach (int j in list)
            {
                if (j == self) continue;
                if (SegmentHitsBox(j, px, py, pz, dx, dy, dz, out float hitT) && hitT < best)
                {
                    best = hitT;
                    bestHeight = _city.Buildings[j].Height;
                }
            }
            // Le premier obstacle trouvé dans une cellule proche est forcément le plus proche
            // à une cellule près : on s'arrête dès qu'on en a un.
            if (best < float.MaxValue && best * length < (k + 2) * 0.5f * CellSize) break;
        }
        return best < float.MaxValue ? (true, best * length, bestHeight) : (false, 0, 0);
    }

    // Test segment-boîte (méthode des dalles) dans le repère du bâtiment ; t ∈ [0, 1].
    private bool SegmentHitsBox(int j, float px, float py, float pz, float dx, float dy, float dz, out float tHit)
    {
        var b = _city.Buildings[j];
        float c = _cos[j], s = _sin[j];
        float ox = px - b.X, oz = pz - b.Z;
        float lox = ox * c - oz * s, loz = ox * s + oz * c;
        float ldx = dx * c - dz * s, ldz = dx * s + dz * c;
        float tMin = 0f, tMax = 1f;
        tHit = 0;
        if (!Slab(lox, ldx, 0.5f * b.Width, ref tMin, ref tMax)) return false;
        if (!Slab(py - 0.5f * b.Height, dy, 0.5f * b.Height, ref tMin, ref tMax)) return false;
        if (!Slab(loz, ldz, 0.5f * b.Depth, ref tMin, ref tMax)) return false;
        tHit = tMin;
        return true;
    }

    private static bool Slab(float o, float d, float half, ref float tMin, ref float tMax)
    {
        if (MathF.Abs(d) < 1e-9f) return MathF.Abs(o) <= half;
        float t1 = (-half - o) / d, t2 = (half - o) / d;
        if (t1 > t2) (t1, t2) = (t2, t1);
        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }

    private int Cell(float v) => (int)MathF.Floor(v / CellSize);

    private static float Distance(float ax, float az, float bx, float bz)
        => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
