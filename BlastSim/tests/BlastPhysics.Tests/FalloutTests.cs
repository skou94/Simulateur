using BlastPhysics.Nuclear;

namespace BlastPhysics.Tests;

public class FalloutTests
{
    private static Fallout SurfaceBurst(double kt, double ff, double windMs = 6.7)
    {
        var device = new NuclearDevice(kt, NuclearDesign.Thermonuclear, ff);
        return new Fallout(device, new Fireball(device, 0, Atmosphere.SeaLevel), windMs, windTowardDeg: 90);
    }

    [Fact]
    public void Airburst_ProducesNoLocalFallout()
    {
        var h = Scenarios.Hiroshima();
        var f = new Fallout(h.NuclearDevice!, h.Fireball!, 6.7, 90);
        Assert.Equal(0.0, f.ContactFraction);
        Assert.Equal(0.0, f.DoseRateAtH1(10000, 0));
    }

    [Fact]
    public void Plume_FollowsTheWind()
    {
        // Vent vers l'est (+X) : panache sous le vent, presque rien au vent ni sur les côtés.
        var f = SurfaceBurst(1000, 0.5);
        double downwind = f.DoseRateAtH1(30000, 0);
        Assert.True(downwind > 100);
        Assert.True(f.DoseRateAtH1(-30000, 0) < 0.01 * downwind);
        Assert.True(f.DoseRateAtH1(30000, 20000) < 0.5 * downwind);
        Assert.True(f.DoseRateAtH1(0, 30000) < 0.05 * downwind);
    }

    [Fact]
    public void WindDirectionRotatesThePlume()
    {
        var device = new NuclearDevice(1000, NuclearDesign.Thermonuclear, 0.5);
        var north = new Fallout(device, new Fireball(device, 0, Atmosphere.SeaLevel), 6.7, windTowardDeg: 0);
        // Nord = −Z dans le repère local.
        Assert.True(north.DoseRateAtH1(0, -30000) > 100 * north.DoseRateAtH1(30000, 0));
    }

    [Fact]
    public void FalloutArrivesLaterFartherAway()
    {
        var f = SurfaceBurst(1000, 0.5);
        Assert.True(f.ArrivalTimeHours(10000, 0) >= 0.5);
        Assert.True(f.ArrivalTimeHours(100000, 0) > 3 * f.ArrivalTimeHours(10000, 0));
    }

    [Fact]
    public void AccumulatedDose_GrowsAndSaturates()
    {
        double rate = 100, ta = 2;
        Assert.Equal(0.0, Fallout.DoseFromRate(rate, ta, 1.5));
        double day = Fallout.DoseFromRate(rate, ta, 24), week = Fallout.DoseFromRate(rate, ta, 168);
        Assert.True(week > day && day > 0);
        Assert.True(week < 5 * rate * Math.Pow(ta, -0.2)); // dose infinie : 5·Ḋ1·ta^-0.2
    }

    [Fact]
    public void CastleBravo_DoseAtRongelapDistance()
    {
        // Castle Bravo (1954) : 15 Mt au sol ; l'atoll de Rongelap, ~180 km sous le vent mais
        // hors de l'axe, a reçu ~190 rad. Dans l'axe, on attend de quelques centaines à
        // quelques milliers de rad en 48 h (ordre de grandeur seulement).
        var f = SurfaceBurst(15000, 0.67, windMs: 10);
        Assert.InRange(f.AccumulatedDose(180000, 0, 48), 100, 3000);
    }
}
