namespace BlastPhysics.Tests;

/// <summary>
/// Validation contre des valeurs de référence publiées. Les tolérances reflètent
/// l'incertitude des références elles-mêmes (rendements historiques ±20 %,
/// observations de terrain, précision annoncée des modèles DNA ±15-30 %).
/// </summary>
public class HistoricalValidationTests
{
    [Fact]
    public void Hiroshima_FivePsiRadius()
    {
        // 35 kPa (5 psi), destruction de la plupart des bâtiments : ~1.6-1.8 km
        // (US Strategic Bombing Survey 1946 ; Glasstone & Dolan ch. 5).
        var r = Scenarios.Hiroshima().ComputeRadii();
        Assert.InRange(r.Overpressure35KPaM, 1400, 1900);
    }

    [Fact]
    public void Hiroshima_GroundZeroOverpressure()
    {
        // Estimations au point zéro : ~200-350 kPa (« 35 t/m² », ville d'Hiroshima).
        var p = Scenarios.Hiroshima().At(0).Blast.OverpressurePa;
        Assert.InRange(p, 150e3, 450e3);
    }

    [Fact]
    public void Hiroshima_ThirdDegreeBurnRadius()
    {
        // Brûlures graves par éclair observées jusqu'à ~2 km, brûlures légères au-delà de 3 km.
        var r = Scenarios.Hiroshima().ComputeRadii();
        Assert.InRange(r.ThirdDegreeBurnsM, 1600, 2600);
        Assert.InRange(r.FirstDegreeBurnsM, 3000, 4500);
    }

    [Fact]
    public void Hiroshima_InitialRadiation500RadRadius()
    {
        // Dose DL50 (~450-500 rad) à ~1.2-1.4 km (réévaluation dosimétrique DS02).
        var r = Scenarios.Hiroshima().ComputeRadii();
        Assert.InRange(r.Radiation500RadM, 1000, 1600);
    }

    [Fact]
    public void MegatonAirburst_FivePsiRadius()
    {
        // 1 Mt à hauteur optimale : 5 psi vers ~6 km (Glasstone fig. 3.74) ; ×1.2^(1/3) pour 1.2 Mt.
        var r = Scenarios.StrategicWarhead().ComputeRadii();
        Assert.InRange(r.Overpressure35KPaM, 5500, 7500);
        Assert.InRange(r.Overpressure7KPaM, 15000, 22000);
    }

    [Fact]
    public void MegatonFireballRadius()
    {
        // Glasstone §2.03 : boule de feu d'1 Mt d'environ 1 700 m de diamètre au maximum.
        var fb = Detonation.Nuclear(new Nuclear.NuclearDevice(1000), 3000).Fireball!;
        Assert.InRange(fb.MaxRadiusM, 750, 1000);
    }

    [Fact]
    public void HandGrenade_FragmentHazardRadius()
    {
        // M67 : rayon de blessure ~15 m (manuel FM 3-23.30).
        var r = Scenarios.HandGrenade().ComputeRadii();
        Assert.InRange(r.FragmentHazardM, 10, 25);
    }

    [Fact]
    public void NeutronBomb_RadiationOutrangesHeavyBlast()
    {
        var r = Scenarios.NeutronBomb().ComputeRadii();
        Assert.True(r.Radiation500RadM > 1.5 * r.Overpressure35KPaM);
    }

    [Fact]
    public void AllScenarios_ProduceConsistentRadii()
    {
        foreach (var make in Scenarios.All)
        {
            var d = make();
            var r = d.ComputeRadii();
            Assert.True(r.Overpressure140KPaM < r.Overpressure35KPaM, d.Name);
            Assert.True(r.Overpressure35KPaM < r.Overpressure7KPaM, d.Name);
            if (d.Thermal is not null)
                Assert.True(r.ThirdDegreeBurnsM < r.FirstDegreeBurnsM, d.Name);
        }
    }
}
