namespace BlastPhysics.Blast;

/// <summary>Chargement de souffle reçu en un point du sol.</summary>
public readonly record struct BlastLoad(
    double OverpressurePa,
    double DynamicPressurePa,
    double ImpulsePaS,
    double ArrivalTimeS,
    double PositivePhaseS);

/// <summary>
/// Souffle au sol d'une explosion : loi en air libre (nucléaire DNA ou TNT
/// Kinney-Graham), réflexion au sol selon la hauteur d'explosion, échelle cube
/// de Hopkinson-Cranz pour le rendement et échelle de Sachs pour l'altitude.
/// </summary>
public sealed class Airblast
{
    private readonly FreeAirCurve _curve;
    private readonly double _cbrtYield;
    private readonly double _sp, _sd, _st;

    /// <param name="curve">Loi en air libre de référence (1 kt, niveau de la mer).</param>
    /// <param name="blastYieldKt">Rendement exprimé dans l'unité de la courbe (kt nucléaires ou kt de TNT).</param>
    /// <param name="heightOfBurstM">Hauteur de l'explosion au-dessus du sol.</param>
    /// <param name="ambient">Air ambiant au niveau du sol.</param>
    public Airblast(FreeAirCurve curve, double blastYieldKt, double heightOfBurstM, AirState ambient)
    {
        if (blastYieldKt <= 0) throw new ArgumentOutOfRangeException(nameof(blastYieldKt));
        _curve = curve;
        BlastYieldKt = blastYieldKt;
        HeightOfBurstM = Math.Max(0.0, heightOfBurstM);
        Ambient = ambient;
        _cbrtYield = Math.Cbrt(blastYieldKt);
        (_sp, _sd, _st) = Atmosphere.SachsFactors(ambient);
    }

    public double BlastYieldKt { get; }
    public double HeightOfBurstM { get; }
    public AirState Ambient { get; }

    /// <summary>Distance réduite équivalente au niveau de la mer pour 1 kt.</summary>
    private double Scale(double meters) => meters / (_cbrtYield * _sd);

    public BlastLoad AtGroundRange(double groundRangeM)
    {
        double sgr = Scale(Math.Max(groundRangeM, 0.0));
        double shob = Scale(HeightOfBurstM);

        double p = GroundReflection.PeakOverpressure(_curve, sgr, shob) * _sp;
        double q = GroundReflection.PeakDynamicPressure(_curve, sgr, shob) * _sp;
        double toa = GroundReflection.ArrivalTime(_curve, sgr, shob) * _cbrtYield * _st;
        double dur = _curve.PositivePhaseDuration(sgr, shob) * _cbrtYield * _st;
        double impulse = Impulse(sgr, shob, p, dur);
        return new BlastLoad(p, q, impulse, toa, dur);
    }

    private double Impulse(double sgr, double shob, double overpressurePa, double durationS)
    {
        if (_curve is TntFreeAirCurve tnt)
        {
            // Impulsion Kinney-Graham à la distance oblique, charge doublée par l'image au sol.
            // Échelle de Sachs : I ∝ Sp^(2/3) · c0/c.
            double slant = Math.Sqrt(sgr * sgr + shob * shob) / GroundReflection.Cbrt2;
            double soundRatio = Atmosphere.SeaLevel.SoundSpeedMs / Ambient.SoundSpeedMs;
            return tnt.PositiveImpulse(slant) * GroundReflection.Cbrt2 * _cbrtYield
                   * Math.Pow(_sp, 2.0 / 3.0) * soundRatio;
        }
        // Onde de Friedlander à décroissance b = 1 : I = Ps·td·(1/b − (1 − e^−b)/b²) ≈ 0.368·Ps·td.
        // Approximation (±30 %) : en nucléaire, la phase positive est longue et c'est
        // la pression de crête qui pilote les dégâts.
        return overpressurePa * durationS * Math.Exp(-1.0);
    }

    /// <summary>
    /// Plus grande distance au sol où la surpression atteint encore le seuil donné,
    /// ou 0 si le seuil n'est jamais atteint.
    /// </summary>
    public double RangeForOverpressure(double thresholdPa)
        => RangeFinder.OuterRange(r => AtGroundRange(r).OverpressurePa, thresholdPa,
                                  maxRangeM: 2.0e4 * _cbrtYield * _sd);
}
