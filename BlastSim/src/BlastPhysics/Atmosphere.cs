namespace BlastPhysics;

/// <summary>État de l'air ambiant en un point.</summary>
public readonly record struct AirState(
    double TemperatureK,
    double PressurePa,
    double DensityKgM3,
    double SoundSpeedMs);

/// <summary>
/// Atmosphère standard internationale (ISA, ISO 2533) de 0 à 20 km :
/// troposphère à gradient constant puis tropopause isotherme.
/// </summary>
public static class Atmosphere
{
    public const double SeaLevelTemperatureK = 288.15;
    public const double SeaLevelPressurePa = 101325.0;
    public const double SeaLevelDensity = 1.225;
    public const double Gamma = 1.4;
    public const double GasConstantAir = 287.053;   // J/(kg·K)
    public const double Gravity = 9.80665;

    private const double LapseRate = 0.0065;        // K/m
    private const double TropopauseAltitude = 11000.0;
    private const double MaxAltitude = 20000.0;

    public static AirState SeaLevel { get; } = At(0.0);

    /// <summary>État ISA à l'altitude géopotentielle donnée (bornée à [0, 20 km]).</summary>
    public static AirState At(double altitudeM)
    {
        double h = Math.Clamp(altitudeM, 0.0, MaxAltitude);
        double t, p;
        if (h <= TropopauseAltitude)
        {
            t = SeaLevelTemperatureK - LapseRate * h;
            p = SeaLevelPressurePa * Math.Pow(t / SeaLevelTemperatureK,
                                              Gravity / (GasConstantAir * LapseRate));
        }
        else
        {
            double tTrop = SeaLevelTemperatureK - LapseRate * TropopauseAltitude;
            double pTrop = SeaLevelPressurePa * Math.Pow(tTrop / SeaLevelTemperatureK,
                                                         Gravity / (GasConstantAir * LapseRate));
            t = tTrop;
            p = pTrop * Math.Exp(-Gravity * (h - TropopauseAltitude) / (GasConstantAir * t));
        }
        double rho = p / (GasConstantAir * t);
        double c = Math.Sqrt(Gamma * GasConstantAir * t);
        return new AirState(t, p, rho, c);
    }

    /// <summary>
    /// Facteurs d'échelle de Sachs pour transposer une loi établie au niveau de la mer :
    /// pression ×Sp, distances ×Sd, temps ×St.
    /// </summary>
    public static (double Sp, double Sd, double St) SachsFactors(AirState air)
    {
        double sp = air.PressurePa / SeaLevelPressurePa;
        double sd = Math.Cbrt(1.0 / sp);
        double st = sd * SeaLevel.SoundSpeedMs / air.SoundSpeedMs;
        return (sp, sd, st);
    }
}
