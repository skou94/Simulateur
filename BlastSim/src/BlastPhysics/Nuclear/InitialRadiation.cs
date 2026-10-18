namespace BlastPhysics.Nuclear;

/// <summary>
/// Dose de radiation nucléaire initiale (première minute) selon la méthode de
/// Glasstone &amp; Dolan 3e éd. §8.60-8.83 : neutrons + gammas secondaires (captures)
/// + gammas des produits de fission, avec l'augmentation hydrodynamique des gammas de
/// fission pour les forts rendements. La densité de l'air est prise en compte par la
/// « distance massique » D·ρ/ρ0 (§8.62).
/// Doses en rad (≈ cGy) pour un individu exposé sans protection.
/// </summary>
public sealed class InitialRadiation
{
    // Sous ~90 m (300 ft) de hauteur, le sol absorbe une partie des neutrons et
    // gammas secondaires : facteur 0.5 au sol, 1 au-delà (Glasstone §8.64).
    private const double FullHeightM = 300.0 * Units.MetersPerFoot;

    private readonly NuclearDevice _device;
    private readonly double _heightOfBurstM;
    private readonly double _densityRatio;
    private readonly bool _surfaceBurst;

    public InitialRadiation(NuclearDevice device, Fireball fireball, AirState ambient)
    {
        _device = device;
        _heightOfBurstM = fireball.HeightOfBurstM;
        _densityRatio = ambient.DensityKgM3 / Atmosphere.SeaLevelDensity;
        _surfaceBurst = fireball.TouchesGround;
    }

    public readonly record struct Dose(double NeutronRad, double SecondaryGammaRad, double FissionGammaRad)
    {
        public double TotalRad => NeutronRad + SecondaryGammaRad + FissionGammaRad;
    }

    public Dose AtGroundRange(double groundRangeM)
    {
        double slantM = Math.Sqrt(groundRangeM * groundRangeM + _heightOfBurstM * _heightOfBurstM);
        double yd = slantM * _densityRatio / Units.MetersPerYard;
        double y = _device.YieldKt;
        double heightFactor = Curve.Lerp(_heightOfBurstM, 0.0, FullHeightM, 0.5, 1.0);

        // Au-delà de 20 % de produits de fusion, Glasstone classe l'arme comme thermonucléaire.
        bool fissionDesign = _device.Design == NuclearDesign.Fission && _device.FissionFraction >= 0.8;
        Curve neutronCurve, gammaCurve;
        if (fissionDesign)
        {
            neutronCurve = _device.HighRadiationOutput ? RadiationTables.FissionNeutronHigh : RadiationTables.FissionNeutronLow;
            gammaCurve = _device.HighRadiationOutput ? RadiationTables.FissionSecondaryGammaHigh : RadiationTables.FissionSecondaryGammaLow;
        }
        else
        {
            neutronCurve = RadiationTables.ThermonuclearNeutron;
            gammaCurve = RadiationTables.ThermonuclearSecondaryGamma;
        }

        double neutron = Math.Pow(10, neutronCurve.Evaluate(yd)) * y * heightFactor * _device.NeutronMultiplier;
        double secondary = Math.Pow(10, gammaCurve.Evaluate(yd)) * y * heightFactor;
        double fission = Math.Pow(10, RadiationTables.FissionFragmentGamma.Evaluate(yd))
                         * EffectiveYieldKt(y, yd) * _device.FissionFraction;
        double k = _device.RadiationScale;
        return new Dose(neutron * k, secondary * k, fission * k);
    }

    public double TotalRadAtGroundRange(double groundRangeM) => AtGroundRange(groundRangeM).TotalRad;

    /// <summary>Plus grande distance au sol où la dose totale atteint le seuil (rad).</summary>
    public double RangeForDose(double doseRad)
        => RangeFinder.OuterRange(TotalRadAtGroundRange, doseRad, maxRangeM: 3.0e4);

    // Rendement effectif des gammas de fission (augmentation hydrodynamique) : la
    // boule de feu raréfie l'air et laisse passer davantage de gammas tardifs.
    private double EffectiveYieldKt(double yieldKt, double slantYd)
    {
        var table = _surfaceBurst ? RadiationTables.SurfaceEffectiveYield : RadiationTables.AirburstEffectiveYield;
        double logY = Math.Log10(Math.Max(yieldKt, 1e-3));
        double sr = Math.Clamp(slantYd, table[0].SlantRangeYd, table[^1].SlantRangeYd);
        int hi = 1;
        while (hi < table.Length - 1 && table[hi].SlantRangeYd < sr) hi++;
        var (r0, c0) = table[hi - 1];
        var (r1, c1) = table[hi];
        double logEff = Curve.Lerp(sr, r0, r1, c0.Evaluate(logY), c1.Evaluate(logY));
        return Math.Pow(10, logEff);
    }
}
