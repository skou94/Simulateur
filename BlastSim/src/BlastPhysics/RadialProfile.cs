namespace BlastPhysics;

/// <summary>
/// Effets précalculés sur une grille radiale logarithmique, pour interroger
/// rapidement des dizaines de milliers de points (bâtiments, pixels…) par
/// interpolation plutôt que par un calcul physique complet.
/// </summary>
public sealed class RadialProfile
{
    private readonly double[] _logR;
    private readonly double[] _overpressure;
    private readonly double[] _impulse;
    private readonly double[] _arrival;
    private readonly double[] _thermal;
    private readonly double[] _radiation;

    private RadialProfile(int n, double maxRangeM)
    {
        MaxRangeM = maxRangeM;
        _logR = new double[n];
        _overpressure = new double[n];
        _impulse = new double[n];
        _arrival = new double[n];
        _thermal = new double[n];
        _radiation = new double[n];
    }

    public double MaxRangeM { get; }

    public static RadialProfile Build(Detonation detonation, double maxRangeM, int samples = 1200)
    {
        const double minRangeM = 0.5;
        var p = new RadialProfile(samples, maxRangeM);
        double l0 = Math.Log(minRangeM), l1 = Math.Log(maxRangeM);
        for (int i = 0; i < samples; i++)
        {
            double lr = l0 + (l1 - l0) * i / (samples - 1);
            var e = detonation.At(Math.Exp(lr));
            p._logR[i] = lr;
            p._overpressure[i] = e.Blast.OverpressurePa;
            p._impulse[i] = e.Blast.ImpulsePaS;
            p._arrival[i] = e.Blast.ArrivalTimeS;
            p._thermal[i] = e.ThermalFluenceCalPerCm2;
            p._radiation[i] = e.InitialRadiationRad;
        }
        // Le temps d'arrivée doit croître strictement pour pouvoir être inversé.
        for (int i = 1; i < samples; i++)
            p._arrival[i] = Math.Max(p._arrival[i], p._arrival[i - 1] + 1e-9);
        return p;
    }

    public readonly record struct Sample(
        double OverpressurePa, double ImpulsePaS, double ArrivalTimeS,
        double ThermalCalPerCm2, double RadiationRad);

    public Sample At(double groundRangeM)
    {
        var (i, t) = Locate(Math.Log(Math.Max(groundRangeM, 1e-3)));
        return new Sample(
            LerpLog(_overpressure, i, t),
            LerpLog(_impulse, i, t),
            Lerp(_arrival, i, t),
            LerpLog(_thermal, i, t),
            LerpLog(_radiation, i, t));
    }

    /// <summary>Rayon atteint par le front de choc au temps t (0 avant l'explosion).</summary>
    public double FrontRadiusAt(double timeS)
    {
        if (timeS <= _arrival[0]) return Math.Exp(_logR[0]) * Math.Max(timeS, 0) / Math.Max(_arrival[0], 1e-9);
        if (timeS >= _arrival[^1]) return MaxRangeM;
        int hi = Array.BinarySearch(_arrival, timeS);
        if (hi >= 0) return Math.Exp(_logR[hi]);
        hi = ~hi;
        double t = (timeS - _arrival[hi - 1]) / (_arrival[hi] - _arrival[hi - 1]);
        return Math.Exp(_logR[hi - 1] + t * (_logR[hi] - _logR[hi - 1]));
    }

    private (int, double) Locate(double lr)
    {
        if (lr <= _logR[0]) return (0, 0.0);
        if (lr >= _logR[^1]) return (_logR.Length - 2, 1.0);
        double step = (_logR[^1] - _logR[0]) / (_logR.Length - 1);
        int i = Math.Min((int)((lr - _logR[0]) / step), _logR.Length - 2);
        return (i, (lr - _logR[i]) / step);
    }

    private static double Lerp(double[] a, int i, double t) => a[i] + t * (a[i + 1] - a[i]);

    // Interpolation géométrique : les effets décroissent en loi de puissance.
    private static double LerpLog(double[] a, int i, double t)
    {
        double x = a[i], y = a[i + 1];
        if (x <= 0 || y <= 0) return x + t * (y - x);
        return x * Math.Pow(y / x, t);
    }
}
