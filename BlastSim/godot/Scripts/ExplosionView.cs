using System;
using BlastPhysics;
using BlastPhysics.Blast;
using BlastPhysics.Nuclear;
using Godot;

namespace BlastSimGame;

/// <summary>
/// Effets visuels d'une explosion, chacun lié à une grandeur physique :
/// <list type="bullet">
/// <item>flash : puissance thermique normalisée (courbe de Glasstone) ;</item>
/// <item>boule de feu : rayon de Taylor-Sedov puis rayon maximal, refroidissement ;</item>
/// <item>nuage de Wilson : coquille de condensation qui suit le front de choc en air libre ;</item>
/// <item>champignon volumétrique : modèle de montée et de taille du nuage ;</item>
/// <item>mur de poussière : position du front de choc au sol et surpression au front.</item>
/// </list>
/// </summary>
public partial class ExplosionView : Node3D
{
    private Detonation _det = null!;
    private RadialProfile _profile = null!;
    private Fireball? _fireball;
    private MushroomCloud? _cloud;

    private MeshInstance3D _fireballMesh = null!;
    private ShaderMaterial _fireballMat = null!;
    private OmniLight3D _flashLight = null!;
    private MeshInstance3D _dustWall = null!;
    private ShaderMaterial _dustMat = null!;
    private MeshInstance3D _cloudBox = null!;
    private ShaderMaterial _cloudMat = null!;
    private MeshInstance3D _wilson = null!;
    private ShaderMaterial _wilsonMat = null!;

    // Explosion conventionnelle : boule de feu R ≈ 1.5·W^(1/3) m (W en kg de TNT), durée
    // ≈ 0.25·W^(1/6) s. Ordres de grandeur visuels (≈140 m et ≈2.4 s pour Beyrouth).
    private double _conventionalRadiusM, _conventionalDurationS;
    private float _cloudAge = 1f;

    /// <summary>Puissance lumineuse normalisée de l'explosion (0..1).</summary>
    public float FlashPower { get; private set; }

    /// <summary>Centre de la boule de feu dans le repère du monde.</summary>
    public Vector3 FireballCenter => GlobalPosition + _fireballMesh.Position;

    /// <summary>Rayon maximal de la boule de feu : échelle de distance de l'éblouissement.</summary>
    public float FireballScaleM { get; private set; } = 1f;

    /// <summary>Rayon et intensité du front de choc, pour le shader du sol.</summary>
    public float FrontRadius { get; private set; }
    public float FrontStrength { get; private set; }
    public float GroundGlow { get; private set; }
    public float GroundGlowRadius { get; private set; }

    // Les nœuds sont créés une seule fois puis reconfigurés à chaque détonation.
    public override void _Ready()
    {
        _fireballMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/fireball.gdshader") };
        _fireballMesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = _fireballMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_fireballMesh);

        _flashLight = new OmniLight3D
        {
            LightColor = new Color(1f, 0.85f, 0.65f),
            OmniAttenuation = 0.6f,
            ShadowEnabled = true,
        };
        AddChild(_flashLight);

        _cloudMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/cloud.gdshader") };
        _cloudBox = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = _cloudMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_cloudBox);

        _wilsonMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/wilson.gdshader") };
        _wilson = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = _wilsonMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_wilson);

        _dustMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/dust_wall.gdshader") };
        _dustWall = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 1f, BottomRadius = 1f, Height = 1f,
                CapTop = false, CapBottom = false, RadialSegments = 256, Rings = 1,
            },
            MaterialOverride = _dustMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_dustWall);

        Deactivate();
    }

    /// <summary>Éclairage de la scène, pour que le nuage volumétrique soit éclairé comme le reste.</summary>
    public void SetLighting(Vector3 towardSun, Color sunColor, float sunEnergy, Color ambient)
    {
        _cloudMat.SetShaderParameter("sun_dir", towardSun);
        _cloudMat.SetShaderParameter("sun_color", sunColor);
        _cloudMat.SetShaderParameter("sun_energy", sunEnergy);
        _cloudMat.SetShaderParameter("ambient_color", ambient);
    }

    public void Setup(Detonation detonation, RadialProfile profile, EffectRadii radii)
    {
        _det = detonation;
        _profile = profile;
        _fireball = detonation.Fireball;
        _cloud = null;
        if (detonation.NuclearDevice is { } device)
        {
            _cloud = new MushroomCloud(device, detonation.HeightOfBurstM);
            FireballScaleM = (float)_fireball!.MaxRadiusM;
            _cloudMat.SetShaderParameter("smoke_color", new Color(0.66f, 0.62f, 0.58f));
            _cloudMat.SetShaderParameter("dust_color", new Color(0.50f, 0.42f, 0.34f));
        }
        else
        {
            double w = detonation.Charge!.TntEquivalentKg;
            _conventionalRadiusM = 1.5 * Math.Cbrt(w);
            _conventionalDurationS = 0.25 * Math.Pow(w, 1.0 / 6.0);
            FireballScaleM = (float)_conventionalRadiusM;
            _cloudMat.SetShaderParameter("smoke_color", new Color(0.20f, 0.19f, 0.18f));
            _cloudMat.SetShaderParameter("dust_color", new Color(0.32f, 0.28f, 0.24f));
        }
        _fireballMat.SetShaderParameter("seed", (float)GD.RandRange(0.0, 100.0));
        _flashLight.OmniRange = (float)Math.Clamp(radii.Overpressure7KPaM * 3.0, 300.0, 200_000.0);
        Active = true;
        Visible = true;
        Update(0.0);
    }

    public bool Active { get; private set; }

    public void HideLight() => _flashLight.Visible = false;

    /// <summary>Masque tous les effets en attendant la prochaine détonation.</summary>
    public void Deactivate()
    {
        Active = false;
        Visible = false;
        FlashPower = FrontRadius = FrontStrength = GroundGlow = GroundGlowRadius = 0f;
    }

    public void Update(double t)
    {
        if (_fireball is not null) UpdateNuclear(t);
        else UpdateConventional(t);
        UpdateShockFront(t);
    }

    private void UpdateNuclear(double t)
    {
        var fb = _fireball!;
        var cloud = _cloud!;
        double tMax = fb.ThermalPulseMaxTimeS;
        float power = (float)fb.ThermalPowerFraction(t);
        // Refroidissement : blanc pendant l'impulsion thermique, rouge sombre ensuite.
        float heat = (float)Math.Exp(-t / (12.0 * tMax));

        double progress = cloud.Progress(t);
        double radius = fb.RadiusAt(t);
        if (radius >= fb.MaxRadiusM) radius = fb.MaxRadiusM * (1.0 + 0.4 * progress);
        double centerY = Math.Max(cloud.CapCenterHeightAt(t), fb.HeightOfBurstM);
        // Tant que la boule de feu touche le sol, elle repose sur lui.
        centerY = Math.Max(centerY, fb.TouchesGround ? radius * 0.6 : 0.0);

        _fireballMesh.Position = new Vector3(0f, (float)centerY, 0f);
        _fireballMesh.Scale = Vector3.One * (float)Math.Max(radius, 0.01);
        _fireballMat.SetShaderParameter("heat", heat);
        // La boule de feu s'efface pendant que le nuage, encore incandescent, prend sa place.
        float fireballOpacity = (float)Smooth(0.15, 0.45, heat);
        _fireballMat.SetShaderParameter("opacity", fireballOpacity);
        _fireballMesh.Visible = fireballOpacity > 0.01f;

        _flashLight.Position = _fireballMesh.Position;
        _flashLight.LightEnergy = 40f * power + 6f * heat;
        _flashLight.Visible = _flashLight.LightEnergy > 0.02f;
        FlashPower = Mathf.Clamp(power, 0f, 1f);

        // Champignon : le chapeau prend le relais de la boule de feu qui refroidit et grossit
        // en montant ; le tore se creuse et roule ; la tige et la jupe de poussière montent du sol.
        double capR = fb.MaxRadiusM + (cloud.StabilizedCapRadiusM - fb.MaxRadiusM) * progress;
        float density = Mathf.Clamp((1f - heat) * 1.5f, 0f, 1f);
        float ringRatio = Mathf.Lerp(0.3f, 0.55f, Mathf.Clamp((float)progress * 4f, 0f, 1f));
        double stemTopFull = centerY - 0.45 * capR;
        double stemGrowth = Math.Clamp(progress * 8.0 - 0.05, 0.0, 1.0);
        double stemTop = stemTopFull * stemGrowth;
        double stemR = stemGrowth > 0 ? 0.13 * capR : 0.0;
        // Jupe : forte pour une explosion au sol, discrète en altitude.
        double contact = fb.TouchesGround ? 1.0 : Math.Clamp(1.0 - fb.HeightOfBurstM / (4.0 * fb.MaxRadiusM), 0.06, 1.0);
        double skirtR = capR * 0.75 * contact * Math.Clamp(progress * 10.0, 0.0, 1.0);
        float cloudHeat = (float)Math.Exp(-t / (40.0 * tMax));
        _cloudAge = Mathf.Clamp((float)progress * 3f, 0f, 1f);
        ConfigureCloud(centerY, capR, ringRatio, stemR, stemTop, skirtR, density, cloudHeat,
                       roll: t / (0.4 * cloud.RiseTimeConstantS) * Mathf.Tau, rise: t * centerY / cloud.RiseTimeConstantS);

        UpdateWilson(t, fb);

        GroundGlow = (float)Math.Clamp(heat * 3.0, 0.0, 1.0);
        GroundGlowRadius = (float)(fb.MaxRadiusM * 1.6);
    }

    // Nuage de Wilson : coquille de condensation au rayon du front de choc en air libre,
    // visible pendant que le front s'éloigne de 1.2 à ~4 rayons de boule de feu.
    private void UpdateWilson(double t, Fireball fb)
    {
        double cbrtW = Math.Cbrt(_det.NuclearDevice!.BlastEquivalentKt);
        double lo = 1.0, hi = 1e5;
        for (int k = 0; k < 40; k++)
        {
            double mid = Math.Sqrt(lo * hi);
            if (NuclearFreeAirCurve.Instance.ArrivalTime(mid) * cbrtW < t) lo = mid; else hi = mid;
        }
        double shell = lo * cbrtW;
        double R = fb.MaxRadiusM;
        double strength = 0.95 * Smooth(1.2 * R, 1.7 * R, shell) * (1.0 - Smooth(3.0 * R, 5.0 * R, shell));
        _wilson.Visible = strength > 0.01;
        if (!_wilson.Visible) return;
        _wilson.Position = new Vector3(0f, (float)fb.HeightOfBurstM, 0f);
        _wilson.Scale = Vector3.One * (float)shell;
        _wilsonMat.SetShaderParameter("strength", (float)strength);
        _wilsonMat.SetShaderParameter("ground_y", (float)(-fb.HeightOfBurstM / shell));
    }

    private void UpdateConventional(double t)
    {
        double tau = _conventionalDurationS;
        double grow = 1.0 - Math.Exp(-t / (0.12 * tau));
        float heat = (float)Math.Exp(-t / (0.45 * tau));
        double radius = _conventionalRadiusM * (0.2 + 0.8 * grow);
        double rise = _conventionalRadiusM * 2.5 * (1.0 - Math.Exp(-t / (6.0 * tau)));
        double centerY = _det.HeightOfBurstM + radius * 0.7 + rise;

        _fireballMesh.Position = new Vector3(0f, (float)centerY, 0f);
        _fireballMesh.Scale = Vector3.One * (float)radius;
        _fireballMat.SetShaderParameter("heat", heat * 0.85f);
        float opacity = Mathf.Clamp(heat * 4f, 0f, 1f);
        _fireballMat.SetShaderParameter("opacity", opacity);
        _fireballMesh.Visible = opacity > 0.01f;

        _flashLight.Position = _fireballMesh.Position;
        _flashLight.LightEnergy = 25f * heat;
        _flashLight.Visible = heat > 0.01f;
        FlashPower = Mathf.Clamp(heat * 0.6f, 0f, 1f) * (float)Math.Exp(-t / (0.08 * tau));

        // Panache de fumée noire qui prend le relais de la boule de feu.
        float smoke = Mathf.Clamp((float)(t / tau) * 1.5f, 0f, 1f) * (float)Math.Exp(-t / (60.0 * tau));
        double plume = _conventionalRadiusM * (1.2 + 1.5 * grow + t / tau * 0.3);
        ConfigureCloud(centerY, plume, ringRatio: 0.25f, stemR: 0.0, stemTop: 0.0, skirtR: 0.0,
                       density: smoke, heat: heat, roll: t / (8.0 * tau), rise: 0.0);
        _wilson.Visible = false;

        GroundGlow = heat;
        GroundGlowRadius = (float)(_conventionalRadiusM * 1.3);
    }

    private void ConfigureCloud(double capY, double capR, float ringRatio, double stemR, double stemTop, double skirtR,
                                float density, float heat, double roll, double rise)
    {
        _cloudBox.Visible = density > 0.002f;
        if (!_cloudBox.Visible) return;
        float half = (float)(Math.Max(capR, skirtR) * 1.3);
        float top = (float)(capY + capR * 1.15);
        var ground = GlobalPosition;
        _cloudBox.Position = new Vector3(0f, top / 2f, 0f);
        _cloudBox.Scale = new Vector3(2f * half, top, 2f * half);
        _cloudMat.SetShaderParameter("box_min", ground + new Vector3(-half, 0f, -half));
        _cloudMat.SetShaderParameter("box_max", ground + new Vector3(half, top, half));
        _cloudMat.SetShaderParameter("ground_center", ground);
        _cloudMat.SetShaderParameter("cap_y", (float)capY);
        _cloudMat.SetShaderParameter("cap_r", (float)capR);
        _cloudMat.SetShaderParameter("ring_ratio", ringRatio);
        _cloudMat.SetShaderParameter("stem_r", (float)stemR);
        _cloudMat.SetShaderParameter("stem_top", (float)stemTop);
        _cloudMat.SetShaderParameter("skirt_r", (float)skirtR);
        _cloudMat.SetShaderParameter("density", density);
        _cloudMat.SetShaderParameter("heat", heat);
        _cloudMat.SetShaderParameter("roll", (float)roll);
        _cloudMat.SetShaderParameter("rise", (float)rise);
        _cloudMat.SetShaderParameter("age", _cloudAge);
    }

    private void UpdateShockFront(double t)
    {
        double r = _profile.FrontRadiusAt(t);
        double p = _profile.At(r).OverpressurePa;
        // Poussière visible de ~1.5 kPa (pleine intensité vers 50 kPa).
        float strength = (float)Math.Clamp(Math.Log10(p / 1500.0) / 1.5, 0.0, 1.0);
        FrontRadius = (float)r;
        FrontStrength = strength;
        bool visible = strength > 0.01f && r > 1.0 && r < _profile.MaxRangeM * 0.98;
        _dustWall.Visible = visible;
        if (!visible) return;
        float height = (float)Math.Clamp(r * 0.05, 3.0, 900.0) * (0.4f + 0.6f * strength);
        _dustWall.Position = new Vector3(0f, height / 2f, 0f);
        _dustWall.Scale = new Vector3((float)r, height, (float)r);
        _dustMat.SetShaderParameter("strength", strength);
    }

    private static double Smooth(double a, double b, double x)
    {
        double u = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return u * u * (3 - 2 * u);
    }
}
