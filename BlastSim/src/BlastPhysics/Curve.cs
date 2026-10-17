namespace BlastPhysics;

/// <summary>
/// Courbe tabulée interpolée linéairement, bornée aux extrémités.
/// Les points sont triés à la construction : les tables issues de numérisations
/// de graphiques contiennent parfois de petites inversions.
/// </summary>
public sealed class Curve
{
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly bool _extrapolateAbove;

    /// <param name="extrapolateAbove">
    /// Au-delà du dernier point, prolonge la pente moyenne du dernier quart au lieu
    /// de rester constant (utile pour une décroissance logarithmique de dose).
    /// </param>
    public Curve(double[] x, double[] y, bool extrapolateAbove = false)
    {
        _extrapolateAbove = extrapolateAbove;
        if (x.Length != y.Length || x.Length < 2)
            throw new ArgumentException("Une courbe exige au moins deux points (x, y) appariés.");
        var order = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
        _x = order.Select(i => x[i]).ToArray();
        _y = order.Select(i => y[i]).ToArray();
    }

    public double Evaluate(double x)
    {
        if (x <= _x[0]) return _y[0];
        if (x >= _x[^1])
        {
            if (!_extrapolateAbove) return _y[^1];
            // Pente moyenne du dernier quart : plus robuste que le dernier segment,
            // parfois plat dans les numérisations.
            int k = Math.Min(_x.Length - 2, _x.Length * 3 / 4);
            double slope = (_y[^1] - _y[k]) / (_x[^1] - _x[k]);
            return _y[^1] + slope * (x - _x[^1]);
        }
        int hi = Array.BinarySearch(_x, x);
        if (hi >= 0) return _y[hi];
        hi = ~hi;
        int lo = hi - 1;
        double t = (x - _x[lo]) / (_x[hi] - _x[lo]);
        return _y[lo] + t * (_y[hi] - _y[lo]);
    }

    public static double Lerp(double x, double x0, double x1, double y0, double y1)
    {
        if (x1 == x0) return y0;
        double t = Math.Clamp((x - x0) / (x1 - x0), 0.0, 1.0);
        return y0 + t * (y1 - y0);
    }
}
