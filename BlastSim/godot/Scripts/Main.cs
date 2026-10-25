using System;
using BlastPhysics;
using BlastPhysics.Casualties;
using BlastPhysics.Nuclear;
using Godot;
using GEnvironment = Godot.Environment;

namespace BlastSimGame;

/// <summary>
/// Scène principale : environnement, sol, ville, caméra, interface, et boucle de
/// simulation. Le temps de simulation (secondes depuis l'explosion) pilote tous les
/// effets ; il avance au rythme choisi dans l'interface.
/// </summary>
public partial class Main : Node3D
{
    // Fluence au-delà de laquelle le sol apparaît carbonisé (cal/cm²).
    private const double GroundScorchCalPerCm2 = 15.0;

    private OrbitCamera _camera = null!;
    private Hud _hud = null!;
    private CityView _cityView = null!;
    private CityModel _city = null!;
    private ShaderMaterial _groundMat = null!;
    private int _citySeed = 7;

    private Detonation? _detonation;
    private RadialProfile? _profile;
    private EffectRadii _radii;
    private ExplosionView _fx = null!;
    private FalloutField? _fallout;
    private CraterView _crater = null!;
    private FireView _fires = null!;
    private Vector2 _groundZero = Vector2.Zero;
    private MeshInstance3D _marker = null!;
    private double _time;
    private double _thermalTime;
    private double _scorchRadius;
    private bool _zonesVisible = true;
    private bool _cameraShaken;
    private GEnvironment _environment = null!;
    private ProceduralSkyMaterial _sky = null!;
    private DirectionalLight3D _sun = null!;

    public override void _Ready()
    {
        BuildEnvironment();
        BuildGround();

        _cityView = new CityView();
        AddChild(_cityView);

        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.Clicked += OnClicked;

        _selectionBox = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.3f, 0.9f, 1f, 0.25f),
                EmissionEnabled = true,
                Emission = new Color(0.3f, 0.9f, 1f),
                EmissionEnergyMultiplier = 1.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_selectionBox);

        _fx = new ExplosionView();
        AddChild(_fx);
        _crater = new CraterView();
        AddChild(_crater);
        _fires = new FireView();
        AddChild(_fires);
        StartShaderWarmup();

        _marker = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.25f, 0.1f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.25f, 0.1f),
                EmissionEnergyMultiplier = 3f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        AddChild(_marker);

        _hud = new Hud();
        AddChild(_hud);
        _hud.DetonateRequested += Detonate;
        _hud.ResetRequested += ResetScene;
        _hud.NewCityRequested += () => { _citySeed++; _hud.SelectCity(0); GenerateCity(); ResetScene(); };
        _hud.ZonesToggled += on => _zonesVisible = on;
        _hud.TimeOfDayChanged += SetTimeOfDay;
        _hud.CitySelected += OnCitySelected;
        GenerateCity();
        _hud.SetInfo("Choisissez un scénario puis [b]DÉTONER[/b].");
        ApplyCommandLine();
    }

    // Préchauffage des shaders : Godot compile les pipelines d'un matériau à son premier
    // affichage, ce qui figeait la première image de la détonation (~115 ms). On affiche
    // donc une explosion fictive quelques images au démarrage, sous le sol, dans l'axe de
    // la caméra : dessinée (donc compilée) mais cachée par le sol.
    private int _warmupFrames;

    private void StartShaderWarmup()
    {
        var det = Scenarios.Hiroshima();
        var profile = RadialProfile.Build(det, 20000, samples: 100);
        var offset = new Vector3(Mathf.Cos(_camera.Pitch) * Mathf.Sin(_camera.Yaw), Mathf.Sin(_camera.Pitch),
                                 Mathf.Cos(_camera.Pitch) * Mathf.Cos(_camera.Yaw));
        var cameraPos = _camera.Target + offset * _camera.Distance;
        _fx.Position = cameraPos - offset * (_camera.Distance * 6f);
        _fx.Setup(det, profile, det.ComputeRadii());
        _warmupFrames = 6;
    }

    private void StepShaderWarmup()
    {
        // Boule de feu d'abord, puis nuage, tige et poussière.
        _fx.Update(_warmupFrames > 3 ? 0.3 : 40.0);
        _fx.HideLight();
        if (--_warmupFrames == 0) _fx.Deactivate();
    }

    /// <summary>
    /// Arguments après « -- » : --demo (détone le scénario affiché après 1 s),
    /// --scenario=N, --time-scale=x, --camera=distance,yaw,pitch.
    /// </summary>
    private bool _demoPending;

    private void ApplyCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            string[] kv = arg.TrimStart('-').Split('=', 2);
            string value = kv.Length > 1 ? kv[1] : "";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (kv[0])
            {
                case "demo":
                    _demoPending = true;
                    break;
                case "benchmark":
                    _benchmarkSeconds = double.Parse(value, inv);
                    DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                    RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
                    break;
                case "city":
                    _hud.SelectCity(int.Parse(value, inv));
                    break;
                case "custom":
                    // --custom=type,puissance,hauteur (type : 0 fission, 1 thermo, 2 neutrons, 3 TNT)
                    var cv = value.Split(',');
                    _hud.SetCustom(int.Parse(cv[0], inv), double.Parse(cv[1], inv), double.Parse(cv[2], inv));
                    break;
                case "wind":
                    var wv = value.Split(',');
                    _hud.SetWind(double.Parse(wv[0], inv), double.Parse(wv[1], inv));
                    break;
                case "select":
                    // --select=x,z : clic simulé à la verticale d'un point (tests, démonstrations).
                    var sv = value.Split(',');
                    OnClicked(new Vector3(float.Parse(sv[0], inv), 5000f, float.Parse(sv[1], inv)), Vector3.Down, null);
                    break;
                case "no-shielding":
                    _hud.ShieldingEnabled = false;
                    break;
                case "wind-later":
                    // --wind-later=secondes,direction,vitesse : change le vent en cours de route (tests).
                    var wl = value.Split(',');
                    GetTree().CreateTimer(double.Parse(wl[0], inv)).Timeout +=
                        () => _hud.SetWind(double.Parse(wl[1], inv), double.Parse(wl[2], inv));
                    break;
                case "charts":
                    _hud.ShowCharts();
                    break;
                case "previous":
                    // --previous=N : courbes d'un scénario de comparaison, avant la détonation.
                    var prev = Scenarios.All[int.Parse(value, inv) - 1]();
                    _hud.SetCharts(ChartData.From(prev, Math.Max(prev.ComputeRadii().Overpressure7KPaM * 3, 2000), TimeOfDay.Day));
                    break;
                case "night":
                    _hud.SelectTimeOfDay(TimeOfDay.Night);
                    break;
                case "scenario":
                    _hud.SelectScenario(int.Parse(value, inv));
                    break;
                case "time-scale":
                    _hud.SetTimeScale(double.Parse(value, inv));
                    break;
                case "camera":
                    var c = value.Split(',');
                    _camera.Distance = float.Parse(c[0], inv);
                    if (c.Length > 1) _camera.Yaw = float.Parse(c[1], inv);
                    if (c.Length > 2) _camera.Pitch = float.Parse(c[2], inv);
                    break;
            }
        }
        StartPendingDemo();
    }

    private void BuildEnvironment()
    {
        var sky = _sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.22f, 0.40f, 0.68f),
            SkyHorizonColor = new Color(0.66f, 0.72f, 0.80f),
            GroundHorizonColor = new Color(0.50f, 0.52f, 0.50f),
            GroundBottomColor = new Color(0.20f, 0.20f, 0.18f),
            SunAngleMax = 30f,
        };
        var env = _environment = new GEnvironment
        {
            BackgroundMode = GEnvironment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = GEnvironment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            TonemapMode = GEnvironment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.2f,
            FogEnabled = true,
            FogLightColor = new Color(0.70f, 0.75f, 0.82f),
            FogDensity = 0.00002f,
            FogAerialPerspective = 0.6f,
            FogSkyAffect = 0.15f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = _sun = new DirectionalLight3D
        {
            LightEnergy = 1.25f,
            LightColor = new Color(1f, 0.96f, 0.9f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 9000f,
            RotationDegrees = new Vector3(-38f, 35f, 0f),
        };
        AddChild(sun);
    }

    /// <summary>Jour ou nuit : population (à la prochaine détonation) et éclairage de la scène.</summary>
    private void SetTimeOfDay(TimeOfDay time)
    {
        bool night = time == TimeOfDay.Night;
        _cityView.TimeOfDay = time;
        if (!_fx.Active) _cityView.ResetDamage();
        _cityView.SetNight(night ? 1f : 0f);
        // Nuit : clair de lune bleuté, assez pour lire la ville et le nuage.
        _sun.LightEnergy = night ? 0.22f : 1.25f;
        _sun.LightColor = night ? new Color(0.55f, 0.65f, 1f) : new Color(1f, 0.96f, 0.9f);
        _sky.SkyTopColor = night ? new Color(0.015f, 0.025f, 0.07f) : new Color(0.22f, 0.40f, 0.68f);
        _sky.SkyHorizonColor = night ? new Color(0.07f, 0.09f, 0.16f) : new Color(0.66f, 0.72f, 0.80f);
        _sky.GroundHorizonColor = night ? new Color(0.03f, 0.03f, 0.04f) : new Color(0.50f, 0.52f, 0.50f);
        _sky.GroundBottomColor = night ? new Color(0.01f, 0.01f, 0.01f) : new Color(0.20f, 0.20f, 0.18f);
        _environment.AmbientLightEnergy = night ? 0.35f : 0.7f;
        _environment.FogLightColor = night ? new Color(0.05f, 0.06f, 0.09f) : new Color(0.70f, 0.75f, 0.82f);
    }

    private void BuildGround()
    {
        _groundMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/ground.gdshader") };
        _groundMat.SetShaderParameter("block_pitch", CityModel.BlockPitch);
        _groundMat.SetShaderParameter("street_width", CityModel.StreetWidth);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(600_000f, 600_000f) },
            MaterialOverride = _groundMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void GenerateCity() => ShowCity(CityModel.Generate(_citySeed));

    private void ShowCity(CityModel city)
    {
        Select(-1);
        _city = city;
        _cityView.Build(_city);
        _groundZero = Vector2.Zero;
        _camera.Target = Vector3.Zero;

        if (city.Map is { } rgba)
        {
            var map = Image.CreateFromData(city.MapSize, city.MapSize, false, Image.Format.Rgba8, rgba);
            _groundMat.SetShaderParameter("map_tex", ImageTexture.CreateFromImage(map));
            _groundMat.SetShaderParameter("map_half", city.HalfSize);
            _groundMat.SetShaderParameter("use_map", 1f);
        }
        else
        {
            int n = city.BlocksPerSide;
            var map = Image.CreateEmpty(n, n, false, Image.Format.R8);
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                map.SetPixel(i, j, city.Parks[j * n + i] ? Colors.White : Colors.Black);
            _groundMat.SetShaderParameter("block_map", ImageTexture.CreateFromImage(map));
            _groundMat.SetShaderParameter("city_half", city.HalfSize);
            _groundMat.SetShaderParameter("use_map", 0f);
        }
        _hud.SetAttribution(city.RealName is null ? "" : "Données © les contributeurs d'OpenStreetMap (ODbL)");
        GD.Print($"Ville : {city.RealName ?? "procédurale"}, {city.Buildings.Count} bâtiments.");
    }

    // Chargement d'une ville réelle en tâche de fond : l'interface reste fluide pendant le
    // téléchargement (jusqu'à ~1 min la première fois, puis lecture du cache).
    private System.Threading.Tasks.Task<CityModel>? _cityLoading;
    private string _cityStatus = "";

    private void OnCitySelected(int index)
    {
        if (_cityLoading is { IsCompleted: false }) return;
        if (_fx.Active) ResetScene();
        if (index == 0) { GenerateCity(); return; }
        var preset = CityData.CityPresets.All[index - 1];
        string cache = ProjectSettings.GlobalizePath("user://osm");
        var progress = new Progress<string>(msg => _cityStatus = msg);
        _cityStatus = "Préparation…";
        _cityLoading = System.Threading.Tasks.Task.Run(async () =>
            CityModel.FromReal(await CityData.OsmCityLoader.LoadAsync(preset, cache, progress), _citySeed));
    }

    // En mode démo, la détonation attend que la ville soit prête.
    private void StartPendingDemo()
    {
        if (!_demoPending || _cityLoading is { IsCompleted: false }) return;
        _demoPending = false;
        GetTree().CreateTimer(1.0).Timeout += Detonate;
    }

    private void PollCityLoading()
    {
        if (_cityLoading is null) return;
        if (!_cityLoading.IsCompleted)
        {
            _hud.SetCityStatus(_cityStatus);
            return;
        }
        if (_cityLoading.IsCompletedSuccessfully)
        {
            ShowCity(_cityLoading.Result);
            _hud.SetCityStatus("");
            StartPendingDemo();
        }
        else
        {
            var error = _cityLoading.Exception?.GetBaseException().Message ?? "erreur inconnue";
            GD.PushError($"Chargement de la ville impossible : {error}");
            _hud.SetCityStatus($"Échec du chargement : {error}");
        }
        _cityLoading = null;
    }

    // Clic : un bâtiment visé est sélectionné ; sinon, hors simulation, on place le point d'impact.
    private int _selected = -1;
    private double _selectionRefresh;
    private MeshInstance3D _selectionBox = null!;

    private void OnClicked(Vector3 origin, Vector3 direction, Vector3? ground)
    {
        int hit = _cityView.Pick(origin, direction);
        if (hit >= 0)
        {
            Select(hit);
            return;
        }
        Select(-1);
        if (!_fx.Active && ground is { } p) _groundZero = new Vector2(p.X, p.Z);
    }

    private void Select(int index)
    {
        _selected = index;
        _selectionRefresh = 0;
        _selectionBox.Visible = index >= 0;
        if (index < 0) _hud.SetBuildingInfo(null);
    }

    private void UpdateSelection(double delta)
    {
        if (_selected < 0) return;
        _selectionBox.Transform = _cityView.InstanceTransform(_selected).ScaledLocal(new Vector3(1.06f, 1.02f, 1.06f));
        _selectionRefresh -= delta;
        if (_selectionRefresh > 0) return;
        _selectionRefresh = 0.2;
        _hud.SetBuildingInfo(DescribeBuilding(_cityView.Report(_selected)));
    }

    private string DescribeBuilding(BuildingReport r)
    {
        static string Structure(BlastPhysics.Damage.StructureType t) => t switch
        {
            BlastPhysics.Damage.StructureType.WoodFrame => "ossature bois",
            BlastPhysics.Damage.StructureType.UnreinforcedMasonry => "maçonnerie",
            BlastPhysics.Damage.StructureType.ReinforcedConcrete => "béton armé",
            _ => "charpente acier",
        };
        string state = r.Vaporized ? "[color=#ff5050]pulvérisé (dans le cratère)[/color]" : r.State switch
        {
            BlastPhysics.Damage.DamageState.Collapse => "[color=#ff5050]effondré[/color]",
            BlastPhysics.Damage.DamageState.Severe => "[color=#ff9a40]dégâts graves[/color]",
            BlastPhysics.Damage.DamageState.Moderate => "[color=#ffd060]dégâts modérés[/color]",
            BlastPhysics.Damage.DamageState.Light => "[color=#e0e070]dégâts légers[/color]",
            _ => "[color=#80e080]intact[/color]",
        };
        var sb = new System.Text.StringBuilder();
        sb.Append($"[b]Bâtiment n° {r.Index}[/b] — {Structure(r.Structure)}, {(r.Use == Occupancy.Office ? "bureaux" : "logements")}\n");
        sb.Append($"Hauteur {r.OriginalHeightM:0} m ({r.Floors} niveaux), emprise {r.FootprintM2:N0} m²\n");
        sb.Append($"Occupants présents : {r.Occupants:N0}\n");
        if (!r.Active)
        {
            sb.Append("[color=#999999]Déclenchez une explosion pour voir ce que ce bâtiment reçoit.[/color]");
            return sb.ToString();
        }
        sb.Append($"Distance au point zéro : [b]{Format.Distance(r.DistanceM)}[/b]\n\n");
        if (!r.BlastArrived)
        {
            sb.Append($"Onde de choc attendue à T+{Format.Duration(r.Load.ArrivalTimeS)}\n");
            sb.Append($"Surpression prévue : {Format.Pressure(r.Load.OverpressurePa)}\n");
            return sb.ToString();
        }
        sb.Append("[b]Reçu[/b]\n");
        sb.Append($"  surpression {Format.Pressure(r.Load.OverpressurePa)}, impulsion {r.Load.ImpulsePaS:N0} Pa·s\n");
        sb.Append($"  onde arrivée à T+{Format.Duration(r.Load.ArrivalTimeS)}\n");
        if (r.BlastFactor < 0.99f) sb.Append($"  abrité du souffle par un bâtiment : pression ×{r.BlastFactor:0.00}\n");
        if (r.StreetThermal > 0.1 || r.Load.ThermalCalPerCm2 > 0.1)
            sb.Append($"  exposé à l'éclair : façade {r.FacadeLit:P0}, rue {r.StreetThermal:0.#} cal/cm²\n");
        if (r.Load.ThermalCalPerCm2 > 0.1) sb.Append($"  rayonnement thermique {r.Load.ThermalCalPerCm2:0.#} cal/cm²{(r.Burning ? " — [color=#ff8040]en feu[/color]" : "")}\n");
        if (r.Load.RadiationRad > 1) sb.Append($"  radiation initiale {r.Load.RadiationRad:N0} rad (en plein air)\n");
        if (r.FalloutRad > 1) sb.Append($"  retombées cumulées {r.FalloutRad:N0} rad (en plein air)\n");
        if (r.InducedRad > 1) sb.Append($"  radioactivité induite cumulée {r.InducedRad:N0} rad (en plein air)\n");
        sb.Append($"\n[b]Sort[/b] : {state}\n");
        sb.Append($"  probabilités : légers {r.Damage.PLight:P0}, modérés {r.Damage.PModerate:P0}, graves {r.Damage.PSevere:P0}, effondrement {r.Damage.PCollapse:P0}\n");
        sb.Append($"  occupants : [b]{r.Deaths:N0}[/b] morts, [b]{r.Injured:N0}[/b] blessés attendus");
        return sb.ToString();
    }

    private void Detonate()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DetonateCore();
        if (_benchmarkSeconds > 0) GD.Print($"[bench] Detonate : {sw.Elapsed.TotalMilliseconds:0} ms");
    }

    private void DetonateCore()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string step) { if (_benchmarkSeconds > 0) GD.Print($"[bench]   {step} : {sw.Elapsed.TotalMilliseconds:0.0} ms"); sw.Restart(); }
        _warmupFrames = 0;
        ClearExplosion();
        _detonation = _hud.BuildDetonation();
        Mark("BuildDetonation");
        _radii = _detonation.ComputeRadii();
        Mark("ComputeRadii");
        double maxRange = Math.Max(Math.Max(_radii.Overpressure7KPaM * 2.5, _radii.FirstDegreeBurnsM * 1.3),
                                   _city.HalfSize * 3.0);
        _profile = RadialProfile.Build(_detonation, maxRange);
        Mark("RadialProfile");
        _scorchRadius = RangeFinder.OuterRange(r => _profile.At(r).ThermalCalPerCm2, GroundScorchCalPerCm2, maxRange);
        _thermalTime = _detonation.Fireball is { } fb ? 3.0 * fb.ThermalPulseMaxTimeS : double.PositiveInfinity;

        var casualty = _detonation.Thermal is { } th
            ? new CasualtyContext(th.BurnThresholdCalPerCm2(BurnDegree.Third), th.BurnThresholdCalPerCm2(BurnDegree.Second))
            : new CasualtyContext(0, 0);
        Vector3? fireballCenter = _detonation.Fireball is { } ball
            ? new Vector3(_groundZero.X, (float)Math.Max(ball.HeightOfBurstM, 0.6 * ball.MaxRadiusM), _groundZero.Y)
            : null;
        _cityView.Prepare(_profile, _groundZero, _thermalTime, casualty, _detonation.Crater.RadiusM,
                          fireballCenter, _hud.ShieldingEnabled);
        if (_detonation.Thermal is not null) _cityView.StartFires(_hud.WindSpeedMs, _hud.WindTowardDeg);
        ApplyWindToFireVisuals();
        _appliedWind = (_hud.WindSpeedMs, _hud.WindTowardDeg);
        Mark("CityView.Prepare (dont écran et incendies)");
        _crater.Display(_detonation.Crater, _groundZero);
        _fallout = null;
        if (_detonation.NuclearDevice is { } device)
        {
            var model = new Fallout(device, _detonation.Fireball!, _hud.WindSpeedMs, _hud.WindTowardDeg);
            if (model.ContactFraction > 0.01) _fallout = new FalloutField(model);
        }
        _cityView.SetResidual(_fallout?.Model, _detonation.Induced, _groundZero);
        _inducedRadius = _detonation.Induced?.RangeForInfiniteDose(InducedZoneRad) ?? 0;
        Mark("Retombées");
        Mark("CityView.Prepare");
        _fx.Position = new Vector3(_groundZero.X, 0f, _groundZero.Y);
        PushCloudLighting();
        _fx.Setup(_detonation, _profile, _radii);
        _hud.SetCharts(ChartData.From(_detonation, Math.Max(_radii.Overpressure7KPaM * 3, 2000), _cityView.TimeOfDay));
        Mark("Courbes");
        Mark("ExplosionView.Setup");
        _time = 0.0;
        _cameraShaken = false;
        _hud.Paused = false;
        _hud.SetInfo(DescribeRadii(_detonation, _radii, _fallout));
        Mark("SetInfo");
    }

    private void ClearExplosion()
    {
        _fx.Deactivate();
        _crater.Clear();
        _fires.Clear();
        _fallout = null;
        _detonation = null;
        _profile = null;
        _hud.SetFlash(0f);
        _environment.TonemapExposure = 1f;
        _environment.GlowIntensity = 0.9f;
    }

    private void ResetScene()
    {
        ClearExplosion();
        _cityView.ResetDamage();
        _hud.SetInfo("Choisissez un scénario puis [b]DÉTONER[/b].");
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space }) _hud.Paused = !_hud.Paused;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) Select(-1);
    }

    // Banc d'essai (--benchmark=secondes) : temps CPU et GPU par image, une ligne par seconde.
    private double _benchmarkSeconds;
    private double _benchmarkClock, _benchmarkWindow;
    private int _benchmarkFrames;
    private double _cpuMs, _gpuMs, _cpuMax, _gpuMax;

    private readonly System.Diagnostics.Stopwatch _probe = new();
    private double _lastSimulateMs, _lastFxMs, _simSum, _fxSum;

    private void Benchmark(double delta)
    {
        var vp = GetViewport().GetViewportRid();
        double cpu = delta * 1000.0;
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        if (cpu > 25.0)
            GD.Print($"[bench] image lente {cpu:0} ms à sim={_time:0.00}s (simulate {_lastSimulateMs:0.0} ms, fx {_lastFxMs:0.0} ms, gpu {gpu:0.0} ms)");
        _simSum += _lastSimulateMs; _fxSum += _lastFxMs;
        _cpuMs += cpu; _gpuMs += gpu; _cpuMax = Math.Max(_cpuMax, cpu); _gpuMax = Math.Max(_gpuMax, gpu);
        _benchmarkFrames++;
        _benchmarkClock += delta;
        _benchmarkWindow += delta;
        if (_benchmarkWindow >= 1.0)
        {
            GD.Print($"[bench] t={_benchmarkClock:0.0}s sim={_time:0.0}s fps={_benchmarkFrames / _benchmarkWindow:0.0} " +
                     $"image={_cpuMs / _benchmarkFrames:0.0}ms (max {_cpuMax:0.0}) gpu={_gpuMs / _benchmarkFrames:0.0}ms (max {_gpuMax:0.0}) " +
                     $"simulate={_simSum / _benchmarkFrames:0.00}ms fx={_fxSum / _benchmarkFrames:0.00}ms " +
                     $"draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} " +
                     $"prims={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:0.00}M");
            _benchmarkWindow = 0; _benchmarkFrames = 0; _cpuMs = _gpuMs = _cpuMax = _gpuMax = _simSum = _fxSum = 0;
        }
        if (_benchmarkClock >= _benchmarkSeconds)
        {
            var t = _cityView.Tally;
            GD.Print($"[bench] bilan : {t.Deaths:N0} morts, {t.Injured:N0} blessés, {t.Collapsed} effondrés, {t.Burning} en feu");
            GetTree().Quit();
        }
    }

    public override void _Process(double delta)
    {
        if (_benchmarkSeconds > 0) Benchmark(delta);
        if (_warmupFrames > 0) StepShaderWarmup();
        PollCityLoading();
        UpdateSelection(delta);
        if (_fx.Active && _profile is not null && !_hud.Paused)
        {
            _time += delta * _hud.TimeScale;
            _probe.Restart();
            _cityView.Simulate(_time);
            _lastSimulateMs = _probe.Elapsed.TotalMilliseconds;
            _probe.Restart();
            _fx.Update(_time);
            _lastFxMs = _probe.Elapsed.TotalMilliseconds;
            _fallout?.Update(_time / 3600.0);
            _crater.Update(_time);
            _fires.Update(_cityView);
            FollowWindChanges(delta);
            UpdateFlash();
            ShakeCameraWhenFrontPasses();
        }
        UpdateGroundUniforms();
        UpdateMarker();
        // Brouillard réglé pour la ville ; on l'atténue en vue lointaine (retombées sur des centaines de km).
        _environment.FogDensity = 2e-5f * Mathf.Clamp(8000f / _camera.Distance, 0.03f, 1f);
        _hud.SetLive(DescribeLive());
    }

    // Éblouissement : la puissance lumineuse vue par la caméra décroît avec la distance
    // (échelle : 40 rayons de boule de feu) et dépend de la direction du regard ; il se
    // traduit par une surexposition brève (image qui « brûle » puis se réadapte), un halo
    // renforcé et, face à l'explosion de près, un voile blanc.
    // Le nuage volumétrique calcule lui-même son éclairage : on lui transmet celui de la scène.
    private void PushCloudLighting()
    {
        bool night = _cityView.TimeOfDay == TimeOfDay.Night;
        _fx.SetLighting(_sun.GlobalTransform.Basis.Z.Normalized(), _sun.LightColor, _sun.LightEnergy,
                        night ? new Color(0.05f, 0.06f, 0.10f) : new Color(0.45f, 0.52f, 0.62f));
    }

    private void UpdateFlash()
    {
        var toFireball = _fx.FireballCenter - _camera.GlobalPosition;
        float distance = Mathf.Max(toFireball.Length(), 1f);
        float near = 1f / (1f + Mathf.Pow(distance / (40f * _fx.FireballScaleM), 2f));
        var forward = -_camera.GlobalTransform.Basis.Z;
        float facing = Mathf.Clamp((forward.Dot(toFireball / distance) - 0.2f) / 0.6f, 0f, 1f);
        float seen = _fx.FlashPower * near * (0.35f + 0.65f * facing);
        _environment.TonemapExposure = 1f + 5f * seen;
        _environment.GlowIntensity = 0.9f + 2.5f * seen;
        _hud.SetFlash(Mathf.Clamp(seen * seen * 1.2f, 0f, 0.85f));
    }

    // Vent modifié en cours de simulation : la propagation des incendies et l'inclinaison de
    // la fumée suivent (au plus 5 fois par seconde, le curseur envoyant beaucoup de valeurs).
    // Les retombées gardent le vent de la détonation : WSEG-10 suppose un vent constant.
    private (double Speed, double Toward) _appliedWind;
    private double _windCheck;

    private void FollowWindChanges(double delta)
    {
        _windCheck -= delta;
        if (_windCheck > 0) return;
        _windCheck = 0.2;
        var wind = (_hud.WindSpeedMs, _hud.WindTowardDeg);
        if (wind == _appliedWind) return;
        _appliedWind = wind;
        _cityView.SetWind(wind.Item1, wind.Item2, _time);
        ApplyWindToFireVisuals();
    }

    private void ApplyWindToFireVisuals()
    {
        float a = Mathf.DegToRad((float)_hud.WindTowardDeg);
        _fires.SetConditions(new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)), (float)_hud.WindSpeedMs,
                             _cityView.TimeOfDay == TimeOfDay.Night ? 0.15f : 1f);
    }

    private void ShakeCameraWhenFrontPasses()
    {
        if (_cameraShaken || !_fx.Active || _profile is null) return;
        float d = new Vector2(_camera.Target.X, _camera.Target.Z).DistanceTo(_groundZero);
        if (_fx.FrontRadius < d) return;
        _cameraShaken = true;
        double p = _profile.At(d).OverpressurePa;
        _camera.Shake((float)(Math.Log10(Math.Max(p, 1.0) / 300.0) / 2.5));
    }

    private void UpdateGroundUniforms()
    {
        _groundMat.SetShaderParameter("ground_zero", _groundZero);
        bool active = _fx.Active && _detonation is not null;
        bool afterFlash = active && _time >= _thermalTime;
        _groundMat.SetShaderParameter("scorch_radius", (float)_scorchRadius);
        _groundMat.SetShaderParameter("scorch_amount", afterFlash ? Mathf.Clamp((float)(_time - _thermalTime) * 2f, 0f, 1f) : 0f);
        _groundMat.SetShaderParameter("front_radius", active ? _fx.FrontRadius : 0f);
        _groundMat.SetShaderParameter("front_strength", active ? _fx.FrontStrength : 0f);
        _groundMat.SetShaderParameter("glow_radius", active ? _fx.GroundGlowRadius : 0f);
        _groundMat.SetShaderParameter("glow_amount", active ? _fx.GroundGlow : 0f);
        _groundMat.SetShaderParameter("zones_visible", active && _zonesVisible ? 1f : 0f);
        _groundMat.SetShaderParameter("zone_radii", new Vector4(
            (float)_radii.Overpressure140KPaM, (float)_radii.Overpressure35KPaM,
            (float)_radii.Overpressure7KPaM, (float)_radii.ThirdDegreeBurnsM));
        _groundMat.SetShaderParameter("radiation_radius", (float)_radii.Radiation500RadM);
        _groundMat.SetShaderParameter("induced_radius", (float)_inducedRadius);
        _groundMat.SetShaderParameter("crater_radius", active ? (float)_detonation!.Crater.RadiusM : 0f);
        _groundMat.SetShaderParameter("fallout_on", active && _fallout is not null ? 1f : 0f);
        if (active && _fallout is { } f)
        {
            _groundMat.SetShaderParameter("fallout_tex", f.Texture);
            _groundMat.SetShaderParameter("fallout_dir", f.Downwind);
            _groundMat.SetShaderParameter("fallout_u", new Vector2(f.UMin, f.UMax));
            _groundMat.SetShaderParameter("fallout_v", f.VHalf);
        }
    }

    private void UpdateMarker()
    {
        _marker.Visible = !_fx.Active;
        float r = Mathf.Clamp(_camera.Distance * 0.006f, 4f, 600f);
        _marker.Position = new Vector3(_groundZero.X, r * 0.5f, _groundZero.Y);
        _marker.Scale = new Vector3(r, r, r);
    }

    private string DescribeLive()
    {
        string fps = $"{Engine.GetFramesPerSecond():0} img/s";
        if (_detonation is null || _profile is null || !_fx.Active)
            return $"Point d'impact : ({_groundZero.X:0}, {_groundZero.Y:0}) m — cliquez sur la ville pour le déplacer\n" +
                   $"Population présente : {_cityView.Tally.Population:N0} habitants\n{fps}";
        var tally = _cityView.Tally;
        double front = _fx.FrontRadius;
        string pressure = front < _profile.MaxRangeM * 0.98 ? Format.Pressure(_profile.At(front).OverpressurePa) : "—";
        string pause = _hud.Paused ? "   [PAUSE]" : "";
        return $"{_detonation.Name}   T+{Format.Duration(_time)}   ×{Format.Scale(_hud.TimeScale)}{pause}\n" +
               $"Front de choc : {Format.Distance(front)}   surpression {pressure}\n" +
               $"Bâtiments atteints : {tally.Hit} / {tally.Total}\n" +
               $"  effondrés {tally.Collapsed}   graves {tally.Severe}   modérés {tally.Moderate}   légers {tally.Light}\n" +
               $"  en feu {tally.Burning}   brûlés {tally.BurnedOut}\n" +
               $"Victimes : {tally.Deaths:N0} morts   {tally.Injured:N0} blessés   (sur {tally.Population:N0} présents)\n" +
               DescribeFallout() + fps;
    }

    // Zone de radioactivité induite affichée : dose cumulée ≥ 10 rad pour une présence illimitée.
    private const double InducedZoneRad = 10.0;
    private double _inducedRadius;

    private string DescribeFallout()
    {
        string induced = "";
        if (_detonation?.Induced is { } ia && ia.InfiniteDoseAt(0) >= 1)
            induced = $"Radioactivité induite au point zéro : {ia.DoseRateAt(0, _time / 3600.0):0.#} rad/h\n";
        if (_fallout is not { } f) return induced;
        double hours = _time / 3600.0;
        if (hours < f.FirstArrivalHours)
            return induced + $"Retombées : premières arrivées dans {Format.Duration((f.FirstArrivalHours - hours) * 3600)} (zone prévue en pointillés)\n";
        return induced + $"Retombées : dose cumulée maximale {f.MaxDoseNow:N0} rad (en plein air)\n";
    }

    private string DescribeRadii(Detonation d, EffectRadii r, FalloutField? fallout)
    {
        double inducedRange = d.Induced?.RangeForInfiniteDose(InducedZoneRad) ?? 0;
        string inducedRow = inducedRange > 0
            ? $"[color=#40e0e0]●[/color] Radioactivité induite ≥ 10 rad (présence illimitée) : [b]{Format.Distance(inducedRange)}[/b]\n"
            : "";
        string craterRow = d.Crater.Exists
            ? $"[color=#a07850]●[/color] Cratère : [b]{Format.Distance(2 * d.Crater.RadiusM)}[/b] de diamètre, {Format.Distance(d.Crater.DepthM)} de profondeur (sol sec)\n"
            : "";
        string falloutRow = fallout is null
            ? d.NuclearDevice is null ? ""
              : "[color=#999999]● Retombées : aucune — la boule de feu ne touche pas le sol (explosion trop haute). " +
                "Mettez la hauteur à 0 m pour des retombées poussées par le vent.[/color]\n"
            : $"[color=#c070ff]●[/color] Retombées, 100 rad en 48 h : [b]{Format.Distance(fallout.Range100Rad48hM)}[/b] sous le vent\n" +
              "[color=#999999]Zone prévue en pointillés au sol ; accélérez le temps (×1 000) et dézoomez pour les voir tomber.[/color]\n";
        static string Row(string color, string label, double m) =>
            m > 0 ? $"[color={color}]●[/color] {label} : [b]{Format.Distance(m)}[/b]\n" : "";
        string yieldText = d.NuclearDevice is not null ? Format.Yield(d.YieldKt) : $"{Format.Mass(d.Charge!.TntEquivalentKg)} éq. TNT";
        return $"[b]{d.Name}[/b]\n{yieldText}, hauteur {Format.Distance(d.HeightOfBurstM)}\n\n" +
               Row("#ffffff", "Boule de feu (rayon)", r.FireballM) +
               Row("#ff4a3a", "140 kPa — béton armé détruit", r.Overpressure140KPaM) +
               Row("#ff9a2a", "35 kPa — la plupart des bâtiments", r.Overpressure35KPaM) +
               Row("#fff070", "7 kPa — vitres, dégâts légers", r.Overpressure7KPaM) +
               Row("#ff5ad0", "Brûlures du 3e degré", r.ThirdDegreeBurnsM) +
               Row("#55ff77", "Radiation initiale 500 rad (1re minute, cercle)", r.Radiation500RadM) +
               Row("#bbbbbb", "Danger des éclats", r.FragmentHazardM) + craterRow + inducedRow + falloutRow;
    }
}
