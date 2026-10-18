using BlastPhysics.Conventional;
using BlastPhysics.Nuclear;

namespace BlastPhysics.Tests;

public class CraterTests
{
    private static Crater Surface(double kt, SoilType soil)
    {
        var device = new NuclearDevice(kt);
        return Crater.ForNuclear(device, new Fireball(device, 0, Atmosphere.SeaLevel), soil);
    }

    [Fact]
    public void Cactus_CraterDiameter()
    {
        // Cactus, 1958 : 18 kt au sol sur le corail de Runit, cratère de ~110 m de diamètre.
        Assert.InRange(2 * Surface(18, SoilType.WetSoil).RadiusM, 80, 150);
    }

    [Fact]
    public void Airburst_LeavesNoCrater()
    {
        Assert.False(Scenarios.Hiroshima().Crater.Exists);
        Assert.False(Scenarios.StrategicWarhead().Crater.Exists);
        // Tsar Bomba : boule de feu effleurant le sol à 4 km d'altitude, pas de cratère notable.
        Assert.True(Scenarios.TsarBomba().Crater.RadiusM < 10);
    }

    [Fact]
    public void Crater_GrowsWithYieldSlowerThanCubeRoot()
    {
        var small = Surface(1, SoilType.DrySoil);
        var large = Surface(1000, SoilType.DrySoil);
        double ratio = large.RadiusM / small.RadiusM;
        Assert.InRange(ratio, 7.0, 10.0); // 1000^(1/3.4) ≈ 7.6, sous 1000^(1/3) = 10
        Assert.True(large.DepthM < large.RadiusM && large.LipRadiusM > large.RadiusM);
    }

    [Fact]
    public void Crater_ShrinksWithHeightOfBurst()
    {
        var device = new NuclearDevice(100);
        var ground = Crater.ForNuclear(device, new Fireball(device, 0, Atmosphere.SeaLevel));
        var low = Crater.ForNuclear(device, new Fireball(device, 60, Atmosphere.SeaLevel));
        Assert.True(low.RadiusM < ground.RadiusM && low.Exists);
    }

    [Fact]
    public void Conventional_CraterOrderOfMagnitude()
    {
        // 1 t de TNT posée au sol : cratère de quelques mètres de diamètre.
        var c = Crater.ForConventional(new ConventionalCharge(Explosive.Tnt, 1000), 0);
        Assert.InRange(2 * c.RadiusM, 5, 12);
        Assert.False(Crater.ForConventional(new ConventionalCharge(Explosive.Tnt, 1000), 50).Exists);
    }
}
