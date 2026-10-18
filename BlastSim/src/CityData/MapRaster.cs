namespace CityData;

/// <summary>
/// Carte au sol d'une ville réelle en image RGBA : R = chaussée, G = parc, B = eau.
/// L'image couvre le carré [−rayon, +rayon]² ; le pixel (0, 0) est au nord-ouest.
/// </summary>
public static class MapRaster
{
    public static byte[] Render(RealCity city, int size)
    {
        var rgba = new byte[size * size * 4];
        double half = city.RadiusM;
        double scale = size / (2.0 * half); // pixels par mètre

        (double, double) ToPixel(Point2 p) => ((p.X + half) * scale, (p.Z + half) * scale);

        FillEvenOdd(rgba, size, city.Areas.Where(a => a.Kind == AreaKind.Park).Select(a => a.Ring), ToPixel, channel: 1);
        FillEvenOdd(rgba, size, city.Areas.Where(a => a.Kind == AreaKind.Water).Select(a => a.Ring), ToPixel, channel: 2);
        foreach (var road in city.Roads)
            StrokePolyline(rgba, size, road.Points, ToPixel, road.WidthM * scale * 0.5, channel: 0);
        return rgba;
    }

    // Remplissage par balayage, règle pair-impair sur l'ensemble des anneaux : les îles et
    // les anneaux intérieurs des multipolygones restent des trous.
    private static void FillEvenOdd(byte[] rgba, int size, IEnumerable<IReadOnlyList<Point2>> rings,
                                    Func<Point2, (double X, double Y)> toPixel, int channel)
    {
        var edges = new List<(double X0, double Y0, double X1, double Y1)>();
        foreach (var ring in rings)
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = toPixel(ring[j]);
                var b = toPixel(ring[i]);
                if (a.Y != b.Y) edges.Add((a.X, a.Y, b.X, b.Y));
            }
        if (edges.Count == 0) return;

        var crossings = new List<double>();
        for (int y = 0; y < size; y++)
        {
            double sy = y + 0.5;
            crossings.Clear();
            foreach (var (x0, y0, x1, y1) in edges)
                if ((sy >= y0) != (sy >= y1))
                    crossings.Add(x0 + (sy - y0) / (y1 - y0) * (x1 - x0));
            crossings.Sort();
            for (int k = 0; k + 1 < crossings.Count; k += 2)
            {
                int from = Math.Max(0, (int)Math.Ceiling(crossings[k] - 0.5));
                int to = Math.Min(size - 1, (int)Math.Floor(crossings[k + 1] - 0.5));
                for (int x = from; x <= to; x++) rgba[(y * size + x) * 4 + channel] = 255;
            }
        }
    }

    private static void StrokePolyline(byte[] rgba, int size, IReadOnlyList<Point2> points,
                                       Func<Point2, (double X, double Y)> toPixel, double halfWidthPx, int channel)
    {
        double hw = Math.Max(halfWidthPx, 0.6);
        for (int i = 0; i + 1 < points.Count; i++)
        {
            var (ax, ay) = toPixel(points[i]);
            var (bx, by) = toPixel(points[i + 1]);
            int x0 = Math.Max(0, (int)(Math.Min(ax, bx) - hw - 1)), x1 = Math.Min(size - 1, (int)(Math.Max(ax, bx) + hw + 1));
            int y0 = Math.Max(0, (int)(Math.Min(ay, by) - hw - 1)), y1 = Math.Min(size - 1, (int)(Math.Max(ay, by) + hw + 1));
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + 0.5 - ax, py = y + 0.5 - ay;
                double t = len2 > 0 ? Math.Clamp((px * dx + py * dy) / len2, 0, 1) : 0;
                double ex = px - t * dx, ey = py - t * dy;
                double d = Math.Sqrt(ex * ex + ey * ey);
                // Bord adouci sur un pixel.
                double cover = Math.Clamp(hw + 0.5 - d, 0, 1);
                if (cover <= 0) continue;
                ref byte v = ref rgba[(y * size + x) * 4 + channel];
                v = (byte)Math.Max(v, (int)(cover * 255));
            }
        }
    }
}
