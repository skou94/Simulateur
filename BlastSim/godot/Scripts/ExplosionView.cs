using System;
using System.Collections.Generic;
using BlastPhysics;
using BlastPhysics.Nuclear;
using Godot;

namespace BlastSimGame;

/// <summary>
/// Effets visuels d'une explosion, chacun lié à une grandeur physique :
/// <list type="bullet">
/// <item>flash : puissance thermique normalisée (courbe de Glasstone) ;</item>
/// <item>boule de feu : rayon de Taylor-Sedov puis rayon maximal, montée du nuage ;</item>
/// <item>mur de poussière : position du front de choc et surpression au front ;</item>
/// <item>champignon : modèle de montée et de taille du nuage.</item>
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
    private ShaderMaterial _capMat = null!, _stemMat = null!;
    private readonly List<(MeshInstance3D Mesh, Vector3 Offset, float Scale)> _capPuffs = new();
    private readonly List<(MeshInstance3D Mesh, float T, float Scale, Vector2 Jitter)> _stemPuffs = new();

    // Explosion conventionnelle : boule de feu R ≈ 1.5·W^(1/3) m (W en kg de TNT), durée
    // ≈ 0.25·W^(1/6) s. Ordres de grandeur visuels (≈140 m et ≈2.4 s pour Beyrouth).
    private double _conventionalRadiusM, _conventionalDurationS;


    /// <summary>Intensité courante du flash vu de loin (0..1), pour l'écran blanc.</summary>
    public float FlashIntensity { get; private set; }

    /// <summary>Rayon et intensité du front de choc, pour le shader du sol.</summary>
    public float FrontRadius { get; private set; }
    public float FrontStrength { get; private set; }
    public float GroundGlow { get; private set; }
    public float GroundGlowRadius { get; private set; }

    // Les nœuds sont créés une seule fois puis reconfigurés à chaque détonation :
    // reconstruire ~70 maillages et matériaux coûtait ~50 ms au déclenchement.
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

        BuildCloud();
        Deactivate();
    }

    public void Setup(Detonation detonation, RadialProfile profile, EffectRadii radii)
    {
        _det = detonation;
        _profile = profile;
        _fireball = detonation.Fireball;
        _cloud = null;
        if (detonation.NuclearDevice is { } device)
            _cloud = new MushroomCloud(device, detonation.HeightOfBurstM);
        else
        {
            double w = detonation.Charge!.TntEquivalentKg;
            _conventionalRadiusM = 1.5 * Math.Cbrt(w);
            _conventionalDurationS = 0.25 * Math.Pow(w, 1.0 / 6.0);
        }
        _fireballMat.SetShaderParameter("seed", (float)GD.RandRange(0.0, 100.0));
        _capMat.SetShaderParameter("smoke_color", _cloud is null ? new Color(0.22f, 0.20f, 0.19f) : new Color(0.66f, 0.63f, 0.60f));
        _flashLight.OmniRange = (float)Math.Clamp(radii.Overpressure7KPaM * 3.0, 300.0, 200_000.0);
        foreach (var puff in _stemPuffs) puff.Mesh.Visible = false;
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
        FlashIntensity = FrontRadius = FrontStrength = GroundGlow = GroundGlowRadius = 0f;
    }

    private void BuildCloud()
    {
        var shader = GD.Load<Shader>("res://Shaders/smoke.gdshader");
        _capMat = new ShaderMaterial { Shader = shader };
        _capMat.SetShaderParameter("smoke_color", new Color(0.66f, 0.63f, 0.60f));
        _stemMat = new ShaderMaterial { Shader = shader };
        _stemMat.SetShaderParameter("smoke_color", new Color(0.58f, 0.53f, 0.46f));
        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 };
        var rng = new RandomNumberGenerator { Seed = 1234 };

        // Chapeau : tore de bouffées autour d'un dôme (circulation toroïdale).
        const int ring = 16;
        for (int k = 0; k < ring; k++)
        {
            float a = Mathf.Tau * k / ring + rng.RandfRange(-0.1f, 0.1f);
            var offset = new Vector3(Mathf.Cos(a) * 0.62f, rng.RandfRange(-0.12f, 0.08f), Mathf.Sin(a) * 0.62f);
            AddPuff(_capPuffs, sphere, _capMat, offset, rng.RandfRange(0.42f, 0.55f), rng);
        }
        for (int k = 0; k < 7; k++)
        {
            float a = Mathf.Tau * k / 7f;
            var offset = new Vector3(Mathf.Cos(a) * 0.28f, rng.RandfRange(0.12f, 0.3f), Mathf.Sin(a) * 0.28f);
            AddPuff(_capPuffs, sphere, _capMat, offset, rng.RandfRange(0.45f, 0.6f), rng);
        }

        // Tige, puis jupe de poussière au pied (nucléaire seulement).
        const int stem = 34;
        for (int k = 0; k < stem; k++)
        {
            var mesh = NewPuff(sphere, _stemMat, rng);
            var jitter = new Vector2(rng.RandfRange(-0.35f, 0.35f), rng.RandfRange(-0.35f, 0.35f));
            _stemPuffs.Add((mesh, k / (float)(stem - 1), rng.RandfRange(0.8f, 1.2f), jitter));
        }
    }

    private void AddPuff(List<(MeshInstance3D, Vector3, float)> list, Mesh mesh, ShaderMaterial mat,
                         Vector3 offset, float scale, RandomNumberGenerator rng)
        => list.Add((NewPuff(mesh, mat, rng), offset, scale));

    private MeshInstance3D NewPuff(Mesh mesh, ShaderMaterial mat, RandomNumberGenerator rng)
    {
        var m = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            Visible = false,
        };
        m.SetInstanceShaderParameter("seed", rng.RandfRange(0f, 50f));
        AddChild(m);
        return m;
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
        // Refroidissement : blanc pendant l'impulsion thermique, rouge sombre après ~40 t_max.
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
        float fireballOpacity = Mathf.Clamp(heat * 6f, 0f, 1f);
        _fireballMat.SetShaderParameter("opacity", fireballOpacity);
        _fireballMesh.Visible = fireballOpacity > 0.01f;

        _flashLight.Position = _fireballMesh.Position;
        _flashLight.LightEnergy = 40f * power + 6f * heat;
        _flashLight.Visible = _flashLight.LightEnergy > 0.02f;
        FlashIntensity = Mathf.Clamp(power * 1.2f, 0f, 1f);

        // Chapeau : grossit en montant ; fumée de plus en plus opaque quand la boule refroidit.
        double capRadius = fb.MaxRadiusM + (cloud.StabilizedCapRadiusM - fb.MaxRadiusM) * progress;
        float capDensity = Mathf.Clamp((1f - heat) * 1.6f, 0f, 1f) * Mathf.Clamp((float)progress * 30f, 0f, 1f);
        _capMat.SetShaderParameter("density", capDensity);
        // Lueur interne : intense tant que la boule de feu est chaude, quelques braises ensuite.
        _capMat.SetShaderParameter("glow", Mathf.Clamp(heat * 2f, 0f, 1f) + 0.06f * (float)Math.Exp(-t / 8.0));
        var center = new Vector3(0f, (float)centerY, 0f);
        foreach (var (mesh, offset, scale) in _capPuffs)
        {
            mesh.Visible = capDensity > 0.01f;
            var squash = new Vector3(1f, 0.55f, 1f);
            mesh.Position = center + offset * (float)capRadius * squash;
            mesh.Scale = Vector3.One * (float)(capRadius * scale);
        }

        // Tige : aspirée sous le chapeau une fois le front réfléchi par le sol.
        float stemDensity = Mathf.Clamp((float)(progress - 0.015) * 12f, 0f, 0.95f);
        _stemMat.SetShaderParameter("density", stemDensity);
        _stemMat.SetShaderParameter("glow", 0f);
        double stemTop = centerY - capRadius * 0.35;
        foreach (var (mesh, s, scale, jitter) in _stemPuffs)
        {
            mesh.Visible = stemDensity > 0.01f && stemTop > 0;
            double y = s * stemTop;
            // Évasée au pied (poussière soulevée), fine au milieu, élargie sous le chapeau.
            double width = capRadius * (0.12 + 0.35 * Math.Pow(1.0 - s, 6) + 0.12 * Math.Pow(s, 4));
            var lateral = jitter * (float)width;
            mesh.Position = new Vector3(lateral.X, (float)y, lateral.Y);
            mesh.Scale = new Vector3((float)width, (float)Math.Max(stemTop / 7.0, width * 0.8), (float)width) * scale;
        }

        GroundGlow = (float)Math.Clamp(heat * 3.0, 0.0, 1.0);
        GroundGlowRadius = (float)(fb.MaxRadiusM * 1.6);
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
        FlashIntensity = Mathf.Clamp(heat * 0.6f, 0f, 1f) * (float)Math.Exp(-t / (0.08 * tau));

        // Panache de fumée noire qui prend le relais.
        float smoke = Mathf.Clamp((float)(t / tau) * 1.5f, 0f, 1f) * (float)Math.Exp(-t / (60.0 * tau));
        _capMat.SetShaderParameter("density", smoke);
        _capMat.SetShaderParameter("glow", heat);
        double plume = _conventionalRadiusM * (1.2 + 1.5 * grow + t / tau * 0.3);
        var center = new Vector3(0f, (float)centerY, 0f);
        foreach (var (mesh, offset, scale) in _capPuffs)
        {
            mesh.Visible = smoke > 0.01f;
            mesh.Position = center + offset * (float)plume * 0.8f;
            mesh.Scale = Vector3.One * (float)(plume * scale * 0.8);
        }

        GroundGlow = heat;
        GroundGlowRadius = (float)(_conventionalRadiusM * 1.3);
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
}
