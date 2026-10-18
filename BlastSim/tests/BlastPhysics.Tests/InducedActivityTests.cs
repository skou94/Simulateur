using BlastPhysics.Nuclear;

namespace BlastPhysics.Tests;

public class InducedActivityTests
{
    private static InducedActivity Hiroshima() => new(Scenarios.Hiroshima().Radiation!);

    [Fact]
    public void Hiroshima_HypocenterDoseMatchesEstimates()
    {
        // ~80 rad cumulés à l'hypocentre pour une présence illimitée (estimations DS86/DS02).
        Assert.InRange(Hiroshima().InfiniteDoseAt(0), 60, 100);
    }

    [Fact]
    public void InducedActivity_IsConfinedNearGroundZero()
    {
        // Négligeable au-delà de ~1 km : les arrivants tardifs n'étaient exposés qu'au centre.
        var a = Hiroshima();
        Assert.True(a.InfiniteDoseAt(1000) < 0.05 * a.InfiniteDoseAt(0));
    }

    [Fact]
    public void MostOfTheDoseIsDeliveredInTheFirstDay()
    {
        double day = InducedActivity.DoseFromInfinite(100, 24);
        Assert.InRange(day, 60, 90);
        Assert.Equal(100, InducedActivity.DoseFromInfinite(100, 24 * 30), 1);
        Assert.True(InducedActivity.DoseRateFromInfinite(100, 1) > 10 * InducedActivity.DoseRateFromInfinite(100, 48));
    }

    [Fact]
    public void EnhancedRadiationWeapon_InducesMoreActivity()
    {
        var er = new InducedActivity(Scenarios.NeutronBomb().Radiation!);
        var fission = new InducedActivity(Detonation.Nuclear(new NuclearDevice(1), 150).Radiation!);
        Assert.True(er.InfiniteDoseAt(0) > 3 * fission.InfiniteDoseAt(0));
    }
}
