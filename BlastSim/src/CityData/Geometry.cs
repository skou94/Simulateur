namespace CityData;

/// <summary>Point du plan local, en mètres : X vers l'est, Z vers le sud (convention du rendu).</summary>
public readonly record struct Point2(double X, double Z)
{
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Point2 operator *(Point2 a, double k) => new(a.X * k, a.Z * k);
    public double Dot(Point2 o) => X * o.X + Z * o.Z;
    public double Length => Math.Sqrt(X * X + Z * Z);
}

/// <summary>
/// Projection équirectangulaire locale autour d'un centre : précise à mieux que 0.1 %
/// sur quelques kilomètres, ce qui suffit à l'échelle d'une ville.
/// </summary>
public sealed class GeoProjection
{
    private const double MetersPerDegreeLat = 110_574.0;
    private const double MetersPerDegreeLonAtEquator = 111_320.0;

    private readonly double _lat0, _lon0, _kx;

    public GeoProjection(double centerLat, double centerLon)
    {
        _lat0 = centerLat;
        _lon0 = centerLon;
        _kx = MetersPerDegreeLonAtEquator * Math.Cos(centerLat * Math.PI / 180.0);
    }

    public Point2 ToLocal(double lat, double lon)
        => new((lon - _lon0) * _kx, -(lat - _lat0) * MetersPerDegreeLat);
}

/// <summary>Rectangle orienté : centre, demi-dimensions et angle de son grand axe.</summary>
/// <param name="Angle">Angle (rad) du côté de longueur <paramref name="Length"/> par rapport à l'axe X.</param>
public readonly record struct OrientedBox(Point2 Center, double Length, double Width, double Angle);

public static class Polygon
{
    /// <summary>Aire (formule du lacet), positive quel que soit le sens de parcours.</summary>
    public static double Area(IReadOnlyList<Point2> ring)
    {
        double s = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            s += ring[j].X * ring[i].Z - ring[i].X * ring[j].Z;
        return Math.Abs(s) * 0.5;
    }

    /// <summary>Enveloppe convexe (chaîne monotone d'Andrew), sens trigonométrique.</summary>
    public static List<Point2> ConvexHull(IEnumerable<Point2> points)
    {
        var p = points.Distinct().OrderBy(q => q.X).ThenBy(q => q.Z).ToList();
        if (p.Count < 3) return p;
        var hull = new List<Point2>(2 * p.Count);
        static double Cross(Point2 o, Point2 a, Point2 b) => (a.X - o.X) * (b.Z - o.Z) - (a.Z - o.Z) * (b.X - o.X);
        foreach (var q in p)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], q) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(q);
        }
        int lower = hull.Count + 1;
        for (int i = p.Count - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], p[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    /// <summary>
    /// Rectangle d'aire minimale contenant le polygone : l'un de ses côtés porte toujours
    /// une arête de l'enveloppe convexe (Freeman &amp; Shapira, 1975).
    /// </summary>
    public static OrientedBox MinimumAreaRectangle(IReadOnlyList<Point2> ring)
    {
        var hull = ConvexHull(ring);
        if (hull.Count < 3)
        {
            var c = hull.Count == 0 ? new Point2(0, 0) : hull.Aggregate((a, b) => a + b) * (1.0 / hull.Count);
            return new OrientedBox(c, 1, 1, 0);
        }
        OrientedBox best = default;
        double bestArea = double.MaxValue;
        for (int i = 0; i < hull.Count; i++)
        {
            var edge = hull[(i + 1) % hull.Count] - hull[i];
            double len = edge.Length;
            if (len < 1e-9) continue;
            var u = edge * (1.0 / len);
            var v = new Point2(-u.Z, u.X);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (var q in hull)
            {
                double a = q.Dot(u), b = q.Dot(v);
                minU = Math.Min(minU, a); maxU = Math.Max(maxU, a);
                minV = Math.Min(minV, b); maxV = Math.Max(maxV, b);
            }
            double area = (maxU - minU) * (maxV - minV);
            if (area >= bestArea) continue;
            bestArea = area;
            double cu = 0.5 * (minU + maxU), cv = 0.5 * (minV + maxV);
            var center = u * cu + v * cv;
            best = new OrientedBox(center, maxU - minU, maxV - minV, Math.Atan2(u.Z, u.X));
        }
        return best;
    }
}
