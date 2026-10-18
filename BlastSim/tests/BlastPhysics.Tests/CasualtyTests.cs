using BlastPhysics.Casualties;
using BlastPhysics.Damage;
using BlastPhysics.Nuclear;

namespace BlastPhysics.Tests;

public class CasualtyTests
{
    private static Exposure ExposureAt(Detonation d, double rangeM, double falloutRad = 0)
    {
        var e = d.At(rangeM);
        double q3 = d.Thermal?.BurnThresholdCalPerCm2(BurnDegree.Third) ?? 0;
        double q2 = d.Thermal?.BurnThresholdCalPerCm2(BurnDegree.Second) ?? 0;
        return new Exposure(e.Blast.OverpressurePa, e.Blast.ImpulsePaS, e.ThermalFluenceCalPerCm2, q3, q2,
                            e.InitialRadiationRad, falloutRad, e.FragmentHitProbability);
    }

    private static double HiroshimaMortality(double rangeM)
    {
        // Ville de 1945 : maisons à ossature bois, explosion à 8 h 15, incendies dès ~10 cal/cm².
        var d = Scenarios.Hiroshima();
        var x = ExposureAt(d, rangeM);
        var damage = BuildingDamage.Assess(StructureType.WoodFrame, x.OverpressurePa, x.ImpulsePaS);
        return CasualtyModel.Expected(StructureType.WoodFrame, damage, x.ThermalCalPerCm2 >= 10, x, TimeOfDay.Day).Death;
    }

    [Theory]
    // Mortalité observée parmi les personnes présentes (Oughterson & Warren 1956 ;
    // Joint Commission) : ~90 % sous 1 km, ~50-60 % vers 1.5 km, ~20-30 % à 2 km,
    // quelques % à 3 km. Tolérances larges : données de 1945 et populations mal connues.
    [InlineData(600, 0.80, 1.00)]
    [InlineData(1500, 0.30, 0.75)]
    [InlineData(2000, 0.08, 0.40)]
    [InlineData(3000, 0.00, 0.08)]
    public void Hiroshima_MortalityByDistance(double rangeM, double min, double max)
        => Assert.InRange(HiroshimaMortality(rangeM), min, max);

    [Fact]
    public void Mortality_DecreasesWithDistance()
    {
        double previous = 1.0;
        for (double r = 800; r <= 5000; r += 200)
        {
            double m = HiroshimaMortality(r);
            Assert.True(m <= previous + 1e-9, $"r={r}");
            previous = m;
        }
    }

    [Fact]
    public void Risks_AreProbabilities()
    {
        var d = Scenarios.StrategicWarhead();
        foreach (double r in new[] { 0.0, 3000, 10000, 30000 })
        foreach (StructureType type in Enum.GetValues<StructureType>())
        foreach (DamageState state in Enum.GetValues<DamageState>())
        {
            var risk = CasualtyModel.Occupant(type, state, burning: true, ExposureAt(d, r, 2000), TimeOfDay.Day);
            Assert.InRange(risk.Death, 0.0, 1.0);
            Assert.InRange(risk.Injury, 0.0, 1.0 - risk.Death + 1e-12);
        }
    }

    [Fact]
    public void Radiation_FollowsLd50()
    {
        Assert.Equal(0.5, CasualtyModel.RadiationDeath(450), 6);
        Assert.True(CasualtyModel.RadiationDeath(200) < 0.01);
        Assert.True(CasualtyModel.RadiationDeath(900) > 0.99);
    }

    [Fact]
    public void Shelter_ProtectsAgainstFallout()
    {
        var x = new Exposure(0, 0, 0, 0, 0, 0, FalloutRad: 600, 0);
        double outdoors = CasualtyModel.Outdoor(x).Death;
        double inConcrete = CasualtyModel.Indoor(StructureType.ReinforcedConcrete, DamageState.None, false, x).Death;
        Assert.True(outdoors > 0.8);
        Assert.True(inConcrete < 0.01);
    }

    [Fact]
    public void NightReducesOutdoorExposureToTheFlash()
    {
        // Loin du centre, seul l'éclair tue (personnes dehors) : la nuit, presque tout le monde est à l'abri.
        var d = Scenarios.StrategicWarhead();
        var x = ExposureAt(d, 12000);
        var damage = BuildingDamage.Assess(StructureType.ReinforcedConcrete, x.OverpressurePa, x.ImpulsePaS);
        double day = CasualtyModel.Expected(StructureType.ReinforcedConcrete, damage, false, x, TimeOfDay.Day).Death;
        double night = CasualtyModel.Expected(StructureType.ReinforcedConcrete, damage, false, x, TimeOfDay.Night).Death;
        Assert.True(night < 0.5 * day);
    }

    [Fact]
    public void Grenade_KillsOnlyNearby()
    {
        var d = Scenarios.HandGrenade();
        Assert.True(CasualtyModel.Outdoor(ExposureAt(d, 2)).Death > 0.2);
        Assert.True(CasualtyModel.Outdoor(ExposureAt(d, 40)).Death < 0.01);
    }
}
