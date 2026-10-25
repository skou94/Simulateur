using BlastPhysics.Damage;
using BlastPhysics.Fire;

namespace BlastPhysics.Tests;

public class FireSpreadTests
{
    // Quartier dense de maisons en bois : 10 m de côté, 15 m d'axe en axe (5 m d'écart).
    private static List<FireSite> WoodGrid(int n, StructureType type = StructureType.WoodFrame)
    {
        var sites = new List<FireSite>();
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
            sites.Add(new FireSite((i - n / 2) * 15.0, (j - n / 2) * 15.0, 10, 10, type));
        return sites;
    }

    // Distance maximale atteinte par le feu, partant du centre, après la durée donnée.
    private static double Reach(FireSpread fire, List<FireSite> sites, double hours, Func<FireSite, double> along)
    {
        int center = sites.FindIndex(s => s.X == 0 && s.Z == 0);
        fire.Ignite(center, 0);
        var ignited = new List<int>();
        fire.Advance(hours * 3600, ignited, new List<int>());
        return ignited.Count == 0 ? 0 : ignited.Max(i => along(sites[i]));
    }

    [Fact]
    public void DenseWoodenCity_SpreadsAtHamadaRates()
    {
        // ~100-200 m/h sans vent en ville dense de bois.
        var sites = WoodGrid(41);
        var fire = new FireSpread(sites, windSpeedMs: 0, windTowardDeg: 90);
        double reach = Reach(fire, sites, 1.0, s => s.X);
        Assert.True(reach is >= 60 and <= 260, $"portée en 1 h : {reach:0} m");
    }

    [Fact]
    public void Wind_PushesFireDownwind()
    {
        var sites = WoodGrid(41);
        // Vent vers l'est (+X) à 8 m/s.
        double downwind = Reach(new FireSpread(sites, 8, 90), sites, 1.0, s => s.X);
        double upwind = Reach(new FireSpread(sites, 8, 90), sites, 1.0, s => -s.X);
        Assert.True(downwind > 1.8 * upwind, $"sous le vent {downwind:0} m, au vent {upwind:0} m");
    }

    [Fact]
    public void ConcreteCity_SpreadsMuchLess()
    {
        var wood = WoodGrid(31);
        var concrete = WoodGrid(31, StructureType.ReinforcedConcrete);
        int Burned(List<FireSite> s)
        {
            var fire = new FireSpread(s, 0, 90);
            fire.Ignite(s.FindIndex(x => x.X == 0 && x.Z == 0), 0);
            var ignited = new List<int>();
            fire.Advance(3 * 3600, ignited, new List<int>());
            return ignited.Count;
        }
        int c = Burned(concrete), w = Burned(wood);
        Assert.True(c * 5 < w, $"béton {c}, bois {w}");
    }

    [Fact]
    public void WindChange_RedirectsTheFire()
    {
        // Vent vers l'est, puis vers l'ouest au bout de 20 min : le feu repart vers l'ouest.
        var sites = WoodGrid(41);
        int center = sites.FindIndex(s => s.X == 0 && s.Z == 0);
        double WestReach(bool turn)
        {
            var fire = new FireSpread(sites, 8, 90);
            fire.Ignite(center, 0);
            var ignited = new List<int>();
            var burned = new List<int>();
            fire.Advance(1200, ignited, burned);
            if (turn) fire.SetWind(8, 270, 1200);
            fire.Advance(3600, ignited, burned);
            return ignited.Max(i => -sites[i].X);
        }
        double steady = WestReach(false), turned = WestReach(true);
        Assert.True(turned > 1.3 * steady, $"vers l'ouest : {steady:0} m sans bascule, {turned:0} m avec");
    }

    [Fact]
    public void WindChange_KeepsPendingPrimaryIgnitions()
    {
        // Un allumage par l'éclair encore à venir survit au changement de vent.
        var sites = WoodGrid(5);
        var fire = new FireSpread(sites, 3, 90);
        fire.Ignite(0, 500);
        fire.SetWind(10, 180, 100);
        var ignited = new List<int>();
        fire.Advance(600, ignited, new List<int>());
        Assert.Contains(0, ignited);
    }

    [Fact]
    public void WideGaps_StopTheFire()
    {
        // Rangées séparées par un boulevard de 60 m : le feu ne traverse pas.
        var sites = new List<FireSite>();
        for (int i = 0; i < 10; i++) sites.Add(new FireSite(i * 15.0, 0, 10, 10, StructureType.WoodFrame));
        for (int i = 0; i < 10; i++) sites.Add(new FireSite(i * 15.0, 70, 10, 10, StructureType.WoodFrame));
        var fire = new FireSpread(sites, 0, 0);
        fire.Ignite(0, 0);
        var ignited = new List<int>();
        fire.Advance(10 * 3600, ignited, new List<int>());
        Assert.DoesNotContain(ignited, i => sites[i].Z > 50);
        Assert.True(ignited.Count > 3);
    }

    [Fact]
    public void BuildingsBurnOutAfterTheirDuration()
    {
        var sites = WoodGrid(3);
        var fire = new FireSpread(sites, 0, 90);
        fire.Ignite(4, 100);
        var ignited = new List<int>();
        var burned = new List<int>();
        fire.Advance(100 + FireSpread.BurnDurationS(StructureType.WoodFrame) + 1, ignited, burned);
        Assert.Contains(4, burned);
        Assert.False(fire.IsBurning(4, 100 + 2 * 3600));
        Assert.True(fire.IsBurning(4, 200));
    }
}
