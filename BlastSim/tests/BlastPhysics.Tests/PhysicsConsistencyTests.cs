using BlastPhysics.Blast;
using BlastPhysics.Conventional;
using BlastPhysics.Damage;
using BlastPhysics.Nuclear;

namespace BlastPhysics.Tests;

/// <summary>Propriétés physiques que les modèles doivent respecter, indépendamment des données.</summary>
public class PhysicsConsistencyTests
{
    [Fact]
    public void Atmosphere_MatchesIsaReferenceValues()
    {
        var sea = Atmosphere.At(0);
        Assert.Equal(101325, sea.PressurePa, 1);
        Assert.Equal(1.225, sea.DensityKgM3, 3);
        Assert.Equal(340.3, sea.SoundSpeedMs, 1);

        var trop = Atmosphere.At(11000);
        Assert.Equal(216.65, trop.TemperatureK, 2);
        Assert.Equal(22632, trop.PressurePa, 0); // ±0.5 Pa
    }

    [Fact]
    public void ShockRelations_HaveCorrectWeakAndStrongLimits()
    {
        Assert.Equal(2.0, ShockRelations.NormalReflectionFactor(10), 2);
        Assert.Equal(1.0, ShockRelations.ShockMach(1), 3);
        // Choc fort : réflexion entre 6 et 13 selon la dissociation.
        double strong = ShockRelations.NormalReflectionFactor(5e6);
        Assert.InRange(strong, 6.0, 13.0);
    }

    [Fact]
    public void KinneyGraham_ReproducesPublishedValueAtUnitScaledDistance()
    {
        // Z = 1 m/kg^(1/3) → Ps/Pa ≈ 9.9 (Kinney & Graham 1985, éq. 6-2).
        double ps = TntFreeAirCurve.Instance.Overpressure(100.0) / Atmosphere.SeaLevelPressurePa;
        Assert.InRange(ps, 9.5, 10.3);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(200.0)]
    [InlineData(1000.0)]
    public void Overpressure_DecreasesWithGroundRange(double heightM)
    {
        var blast = new Airblast(NuclearFreeAirCurve.Instance, 20, heightM, Atmosphere.SeaLevel);
        double previous = double.MaxValue;
        for (double r = 1500; r <= 30000; r *= 1.2)
        {
            double p = blast.AtGroundRange(r).OverpressurePa;
            Assert.True(p < previous, $"r={r:F0} m, h={heightM} m");
            previous = p;
        }
    }

    [Fact]
    public void Overpressure_IsFiniteEverywhere()
    {
        foreach (double y in new[] { 1e-6, 0.01, 1, 100, 10000 })
        foreach (double h in new[] { 0.0, 1, 50, 500, 3000 })
        {
            var blast = new Airblast(NuclearFreeAirCurve.Instance, y, h, Atmosphere.SeaLevel);
            foreach (double r in new[] { 0.0, 0.5, 10, 300, 5000, 50000 })
            {
                var load = blast.AtGroundRange(r);
                Assert.True(double.IsFinite(load.OverpressurePa) && load.OverpressurePa > 0, $"y={y} h={h} r={r}");
                Assert.True(double.IsFinite(load.ImpulsePaS) && double.IsFinite(load.ArrivalTimeS));
            }
        }
    }

    [Fact]
    public void CubeRootScaling_HoldsForSurfaceBursts()
    {
        var oneKt = new Airblast(NuclearFreeAirCurve.Instance, 1, 0, Atmosphere.SeaLevel);
        var oneMt = new Airblast(NuclearFreeAirCurve.Instance, 1000, 0, Atmosphere.SeaLevel);
        double p1 = oneKt.AtGroundRange(800).OverpressurePa;
        double p2 = oneMt.AtGroundRange(8000).OverpressurePa;
        Assert.Equal(p1, p2, p1 * 1e-9);
    }

    [Fact]
    public void Altitude_ReducesOverpressureAtSameRange()
    {
        var sea = new Airblast(NuclearFreeAirCurve.Instance, 10, 0, Atmosphere.At(0));
        var high = new Airblast(NuclearFreeAirCurve.Instance, 10, 0, Atmosphere.At(4000));
        Assert.True(high.AtGroundRange(3000).OverpressurePa < sea.AtGroundRange(3000).OverpressurePa);
    }

    /// <summary>Courbe DNA dont le temps d'arrivée est recalculé par intégration de Rankine-Hugoniot.</summary>
    private sealed class IntegratedArrivalCurve : FreeAirCurve
    {
        public override double Overpressure(double r) => NuclearFreeAirCurve.Instance.Overpressure(r);
        public override double PositivePhaseDuration(double sgr, double shob) => 0;
    }

    [Theory]
    [InlineData(200.0)]
    [InlineData(500.0)]
    [InlineData(1000.0)]
    [InlineData(3000.0)]
    public void ArrivalTime_IntegratedFromShockSpeed_MatchesDnaFit(double scaledRangeM)
    {
        double fit = NuclearFreeAirCurve.Instance.ArrivalTime(scaledRangeM);
        double integrated = new IntegratedArrivalCurve().ArrivalTime(scaledRangeM);
        Assert.InRange(integrated / fit, 0.9, 1.1);
    }

    [Fact]
    public void ArrivalTime_IsFasterThanSoundButApproachesIt()
    {
        var blast = new Airblast(NuclearFreeAirCurve.Instance, 15, 0, Atmosphere.SeaLevel);
        double c = Atmosphere.SeaLevel.SoundSpeedMs;
        Assert.True(blast.AtGroundRange(1000).ArrivalTimeS < 1000 / c);
        Assert.InRange(blast.AtGroundRange(40000).ArrivalTimeS / (40000 / c), 0.97, 1.0);
    }

    [Fact]
    public void DynamicPressure_IsZeroDirectlyBelowAnAirburst()
    {
        var blast = new Airblast(NuclearFreeAirCurve.Instance, 15, 580, Atmosphere.SeaLevel);
        Assert.True(blast.AtGroundRange(0).DynamicPressurePa < 1.0);
        Assert.True(blast.AtGroundRange(1500).DynamicPressurePa > 1000.0);
    }

    [Fact]
    public void Thermal_FollowsInverseSquareInClearAir()
    {
        var device = new NuclearDevice(10);
        var fireball = new Fireball(device, 2000, Atmosphere.SeaLevel);
        var thermal = new ThermalRadiation(device, fireball, visibilityKm: 1e6);
        double q1 = thermal.FluenceAtGroundRange(3000);
        double q2 = thermal.FluenceAtGroundRange(6000);
        double d1 = Math.Sqrt(3000.0 * 3000 + 2000 * 2000), d2 = Math.Sqrt(6000.0 * 6000 + 2000 * 2000);
        Assert.Equal(d2 * d2 / (d1 * d1), q1 / q2, 2);
    }

    [Fact]
    public void Thermal_BurnThresholdsGrowWithYield()
    {
        var small = new NuclearDevice(1);
        var large = new NuclearDevice(1000);
        var ts = new ThermalRadiation(small, new Fireball(small, 500, Atmosphere.SeaLevel));
        var tl = new ThermalRadiation(large, new Fireball(large, 500, Atmosphere.SeaLevel));
        Assert.True(tl.BurnThresholdCalPerCm2(BurnDegree.Third) > ts.BurnThresholdCalPerCm2(BurnDegree.Third));
        Assert.True(ts.BurnThresholdCalPerCm2(BurnDegree.First) < ts.BurnThresholdCalPerCm2(BurnDegree.Third));
    }

    [Fact]
    public void Radiation_DecreasesWithRangeIncludingBeyondTables()
    {
        var det = Detonation.Nuclear(new NuclearDevice(100, NuclearDesign.Thermonuclear, 0.5), 500);
        double previous = double.MaxValue;
        for (double r = 300; r <= 6000; r += 300)
        {
            double dose = det.Radiation!.TotalRadAtGroundRange(r);
            Assert.True(dose < previous, $"r={r}");
            previous = dose;
        }
    }

    [Fact]
    public void Radiation_ThinnerAirCarriesFurther()
    {
        var device = new NuclearDevice(10);
        var sea = Detonation.Nuclear(device, 300, groundElevationM: 0);
        var plateau = Detonation.Nuclear(device, 300, groundElevationM: 3000);
        Assert.True(plateau.Radiation!.RangeForDose(500) > sea.Radiation!.RangeForDose(500));
    }

    [Fact]
    public void EnhancedRadiation_TradesBlastForRadiation()
    {
        var er = Detonation.Nuclear(new NuclearDevice(1, NuclearDesign.EnhancedRadiation, 0.25), 150);
        var fission = Detonation.Nuclear(new NuclearDevice(1), 150);
        Assert.True(er.Radiation!.RangeForDose(500) > fission.Radiation!.RangeForDose(500));
        Assert.True(er.Blast.RangeForOverpressure(35e3) < fission.Blast.RangeForOverpressure(35e3));
    }

    [Fact]
    public void Fireball_TaylorSedovMatchesTrinityPhotograph()
    {
        // Taylor (1950) : Trinity, ~20 kt, rayon ≈ 130 m à t = 25 ms.
        var fb = new Fireball(new NuclearDevice(20), 30, Atmosphere.SeaLevel);
        Assert.InRange(fb.RadiusAt(0.025), 120, 145);
    }

    [Fact]
    public void Fragmentation_UsesGurneySphereFormula()
    {
        var charge = new ConventionalCharge(Explosive.Tnt, 1.0, CasingMassKg: 2.0);
        var frag = new Fragmentation(charge, Atmosphere.SeaLevel);
        Assert.Equal(2440 / Math.Sqrt(2.0 + 0.6), frag.InitialVelocityMs, 6);
        Assert.True(frag.VelocityAt(50) < frag.VelocityAt(10));
    }

    [Fact]
    public void Damage_DemandRatioIsOneOnTheIsoDamageHyperbola()
    {
        var t = new IsoDamageThreshold(10e3, 200);
        Assert.Equal(1.0, BuildingDamage.DemandRatio(20e3, 400, t), 9);
        // Onde très longue : le seuil tend vers l'asymptote en pression.
        Assert.Equal(1.0, BuildingDamage.DemandRatio(10e3, 1e12, t), 6);
        Assert.Equal(0.5, BuildingDamage.ExceedanceProbability(20e3, 400, t), 6);
    }

    [Fact]
    public void Damage_ProbabilitiesAreOrderedAndBounded()
    {
        foreach (StructureType type in Enum.GetValues<StructureType>())
        foreach (double p in new[] { 1e3, 2e4, 1e5, 1e6 })
        {
            var a = BuildingDamage.Assess(type, p, 1e4);
            Assert.True(a.PLight >= a.PModerate && a.PModerate >= a.PSevere && a.PSevere >= a.PCollapse);
            Assert.InRange(a.ExpectedDamageRatio, 0.0, 1.0);
        }
    }

    [Fact]
    public void Damage_ShortPulseSparesBuildingsThatALongPulseFlattens()
    {
        // Même crête de 50 kPa : grenade à 3 m (impulsion ~40 Pa·s) contre souffle nucléaire.
        var grenade = BuildingDamage.Assess(StructureType.UnreinforcedMasonry, 50e3, 40);
        var nuclear = BuildingDamage.Assess(StructureType.UnreinforcedMasonry, 50e3, 1e4);
        Assert.True(grenade.PCollapse < 0.01);
        Assert.True(nuclear.PCollapse > 0.5);
    }

    [Fact]
    public void StandardNormalCdf_IsAccurate()
    {
        Assert.Equal(0.5, BuildingDamage.StandardNormalCdf(0), 7);
        Assert.Equal(0.841345, BuildingDamage.StandardNormalCdf(1), 5);
        Assert.Equal(0.022750, BuildingDamage.StandardNormalCdf(-2), 5);
    }

    [Fact]
    public void RadialProfile_MatchesDirectEvaluationAndInvertsArrivalTime()
    {
        var det = Scenarios.Hiroshima();
        var profile = RadialProfile.Build(det, 20000);
        foreach (double r in new[] { 50.0, 800, 1600, 4000, 12000 })
        {
            var exact = det.At(r);
            var s = profile.At(r);
            Assert.InRange(s.OverpressurePa / exact.Blast.OverpressurePa, 0.98, 1.02);
            Assert.InRange(s.ThermalCalPerCm2 / exact.ThermalFluenceCalPerCm2, 0.98, 1.02);
            Assert.InRange(profile.FrontRadiusAt(exact.Blast.ArrivalTimeS) / r, 0.98, 1.02);
        }
    }

    [Fact]
    public void RangeFinder_FindsOutermostCrossing()
    {
        double r = RangeFinder.OuterRange(x => 1000.0 / x, 2.0, maxRangeM: 10000);
        Assert.Equal(500.0, r, 1.0);
        Assert.Equal(0.0, RangeFinder.OuterRange(x => 1.0, 2.0, maxRangeM: 100));
    }
}
