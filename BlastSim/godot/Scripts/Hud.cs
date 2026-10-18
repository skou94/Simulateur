using System;
using System.Linq;
using BlastPhysics;
using BlastPhysics.Casualties;
using BlastPhysics.Conventional;
using BlastPhysics.Nuclear;
using Godot;

namespace BlastSimGame;

/// <summary>Panneau de commande : scénarios, paramètres de l'engin, temps, affichage.</summary>
public partial class Hud : CanvasLayer
{
    public event Action? DetonateRequested;
    public event Action? ResetRequested;
    public event Action? NewCityRequested;
    public event Action<bool>? ZonesToggled;
    public event Action<TimeOfDay>? TimeOfDayChanged;
    public event Action<int>? CitySelected;

    private const int CustomIndex = 0;
    private static readonly string[] ModeNames =
        { "Nucléaire — fission", "Nucléaire — thermonucléaire", "Nucléaire — rayonnement renforcé", "Conventionnel — TNT" };

    private OptionButton _scenario = null!, _mode = null!, _moment = null!, _cityChoice = null!;
    private Label _cityStatus = null!, _attribution = null!;
    private HSlider _yield = null!, _height = null!, _time = null!, _windDir = null!, _windSpeed = null!;
    private Label _yieldLabel = null!, _heightLabel = null!, _timeLabel = null!, _windLabel = null!;
    private RichTextLabel _info = null!;
    private Label _live = null!;
    private ColorRect _flash = null!;
    private bool _applyingPreset;

    public double TimeScale => Math.Pow(10.0, _time.Value);

    /// <summary>Direction vers laquelle souffle le vent (0 = nord, 90 = est).</summary>
    public double WindTowardDeg => _windDir.Value;
    public double WindSpeedMs => _windSpeed.Value;
    public bool Paused { get; set; }

    private bool IsConventional => _mode.Selected == 3;

    public override void _Ready()
    {
        _flash = new ColorRect { Color = new Color(1f, 0.97f, 0.9f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.88f),
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 16, ContentMarginBottom = 16,
            CornerRadiusBottomLeft = 10, CornerRadiusTopLeft = 10,
        });
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.OffsetLeft = -360;
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var title = new Label { Text = "BLASTSIM" };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.25f));
        box.AddChild(title);

        box.AddChild(Caption("Ville"));
        _cityChoice = new OptionButton();
        _cityChoice.AddItem("Ville procédurale (10 × 10 km)");
        foreach (var preset in CityData.CityPresets.All) _cityChoice.AddItem($"{preset.Name} (OSM)");
        _cityChoice.ItemSelected += i => CitySelected?.Invoke((int)i);
        box.AddChild(_cityChoice);
        _cityStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.8f, 0.4f) };
        _cityStatus.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_cityStatus);

        box.AddChild(Caption("Scénario"));
        _scenario = new OptionButton();
        _scenario.AddItem("Personnalisé");
        foreach (var make in Scenarios.All) _scenario.AddItem(make().Name);
        _scenario.ItemSelected += OnScenarioSelected;
        box.AddChild(_scenario);

        box.AddChild(Caption("Type d'engin"));
        _mode = new OptionButton();
        foreach (var name in ModeNames) _mode.AddItem(name);
        _mode.ItemSelected += _ => { RefreshLabels(); MarkCustom(); };
        box.AddChild(_mode);

        (_yieldLabel, _yield) = AddSlider(box, 0, 1000, 470);
        (_heightLabel, _height) = AddSlider(box, 0, 1000, 270);
        box.AddChild(new HSeparator());
        // Jusqu'à ×4 000 : les retombées se jouent sur des heures.
        (_timeLabel, _time) = AddSlider(box, -2.0, 3.6, 0.0, step: 0.01);
        (_windLabel, _windDir) = AddSlider(box, 0, 355, 90, step: 5);
        _windSpeed = new HSlider { MinValue = 0.5, MaxValue = 30, Step = 0.5, Value = 7 };
        box.AddChild(_windSpeed);
        _windDir.ValueChanged += _ => RefreshLabels();
        _windSpeed.ValueChanged += _ => RefreshLabels();
        _yield.ValueChanged += _ => { RefreshLabels(); MarkCustom(); };
        _height.ValueChanged += _ => { RefreshLabels(); MarkCustom(); };
        _time.ValueChanged += _ => RefreshLabels();

        box.AddChild(Caption("Moment de l'explosion"));
        var moment = new OptionButton();
        moment.AddItem("Jour (population au travail, 25 % dehors)");
        moment.AddItem("Nuit (population chez elle, 4 % dehors)");
        moment.ItemSelected += i => TimeOfDayChanged?.Invoke(i == 0 ? TimeOfDay.Day : TimeOfDay.Night);
        box.AddChild(moment);
        _moment = moment;

        var zones = new CheckBox { Text = "Afficher les zones d'effet", ButtonPressed = true };
        zones.Toggled += on => ZonesToggled?.Invoke(on);
        box.AddChild(zones);

        var go = new Button { Text = "DÉTONER", CustomMinimumSize = new Vector2(0, 48) };
        go.AddThemeFontSizeOverride("font_size", 20);
        go.Pressed += () => DetonateRequested?.Invoke();
        box.AddChild(go);

        var row = new HBoxContainer();
        var reset = new Button { Text = "Réinitialiser", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        reset.Pressed += () => ResetRequested?.Invoke();
        var city = new Button { Text = "Nouvelle ville", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        city.Pressed += () => NewCityRequested?.Invoke();
        row.AddChild(reset);
        row.AddChild(city);
        box.AddChild(row);

        box.AddChild(new HSeparator());
        _info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
        _info.AddThemeFontSizeOverride("normal_font_size", 14);
        box.AddChild(_info);

        var help = new Label
        {
            Text = "Clic : point d'impact · Glisser : orbite\nClic droit : déplacer · Molette : zoom\nEspace : pause",
            Modulate = new Color(1, 1, 1, 0.55f),
        };
        help.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(help);

        _attribution = new Label { Modulate = new Color(1, 1, 1, 0.75f) };
        _attribution.AddThemeFontSizeOverride("font_size", 12);
        _attribution.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _attribution.OffsetLeft = 12;
        _attribution.OffsetTop = -28;
        _attribution.OffsetBottom = -8;
        AddChild(_attribution);

        _live = new Label { Position = new Vector2(20, 16) };
        _live.AddThemeFontSizeOverride("font_size", 17);
        _live.AddThemeColorOverride("font_outline_color", Colors.Black);
        _live.AddThemeConstantOverride("outline_size", 6);
        AddChild(_live);

        _scenario.Select(Array.FindIndex(Scenarios.All.ToArray(), f => f().Name.StartsWith("Hiroshima")) + 1);
        OnScenarioSelected(_scenario.Selected);
    }

    /// <summary>Sélectionne un scénario par son rang dans <see cref="Scenarios.All"/> (1 = premier).</summary>
    public void SelectScenario(int index)
    {
        _scenario.Select(Math.Clamp(index, 0, Scenarios.All.Count));
        OnScenarioSelected(_scenario.Selected);
    }

    public void SelectTimeOfDay(TimeOfDay time)
    {
        _moment.Select(time == TimeOfDay.Day ? 0 : 1);
        TimeOfDayChanged?.Invoke(time);
    }

    public void SelectCity(int index)
    {
        _cityChoice.Select(Math.Clamp(index, 0, CityData.CityPresets.All.Count));
        if (index != 0) CitySelected?.Invoke(_cityChoice.Selected);
    }

    public void SetCityStatus(string text) => _cityStatus.Text = text;

    public void SetAttribution(string text) => _attribution.Text = text;

    /// <summary>Engin personnalisé : type (rang dans la liste), puissance (kt ou kg de TNT), hauteur (m).</summary>
    public void SetCustom(int mode, double yieldValue, double heightM)
    {
        _scenario.Select(CustomIndex);
        _mode.Select(Math.Clamp(mode, 0, ModeNames.Length - 1));
        _yield.Value = IsConventional
            ? (Math.Log10(yieldValue) + 1.0) / 7.7 * 1000.0
            : (Math.Log10(yieldValue) + 2.0) / 7.0 * 1000.0;
        _height.Value = Math.Sqrt(heightM / (IsConventional ? 300.0 : 8000.0)) * 1000.0;
        _scenario.Select(CustomIndex);
        RefreshLabels();
    }

    public void SetWind(double towardDeg, double speedMs)
    {
        _windDir.Value = towardDeg;
        _windSpeed.Value = speedMs;
    }

    public void SetTimeScale(double scale) => _time.Value = Math.Log10(Math.Clamp(scale, 0.01, 4000.0));

    public void SetFlash(float alpha) => _flash.Color = _flash.Color with { A = Mathf.Clamp(alpha, 0f, 1f) };
    public void SetInfo(string bbcode) => _info.Text = bbcode;
    public void SetLive(string text) => _live.Text = text;

    /// <summary>Construit l'explosion décrite par le panneau.</summary>
    public Detonation BuildDetonation()
    {
        if (_scenario.Selected != CustomIndex)
            return Scenarios.All[_scenario.Selected - 1]();
        double h = HeightM;
        if (IsConventional)
            return Detonation.Conventional(new ConventionalCharge(Explosive.Tnt, YieldValue), h,
                                           name: $"TNT {Format.Mass(YieldValue)}");
        var design = (NuclearDesign)_mode.Selected;
        double ff = design switch { NuclearDesign.Fission => 1.0, NuclearDesign.Thermonuclear => 0.5, _ => 0.25 };
        return Detonation.Nuclear(new NuclearDevice(YieldValue, design, ff), h,
                                  name: $"{ModeNames[_mode.Selected]} {Format.Yield(YieldValue)}");
    }

    // Puissance : kt pour le nucléaire (10 t à 100 Mt), kg de TNT pour le conventionnel (100 g à 5 000 t).
    private double YieldValue => IsConventional
        ? Math.Pow(10.0, Mathf.Lerp(-1f, 6.7f, (float)_yield.Value / 1000f))
        : Math.Pow(10.0, Mathf.Lerp(-2f, 5f, (float)_yield.Value / 1000f));

    private double HeightM
    {
        get
        {
            double u = _height.Value / 1000.0;
            return (IsConventional ? 300.0 : 8000.0) * u * u;
        }
    }

    private void OnScenarioSelected(long index)
    {
        if (index == CustomIndex) return;
        var d = Scenarios.All[(int)index - 1]();
        _applyingPreset = true;
        if (d.NuclearDevice is { } dev)
        {
            _mode.Select((int)dev.Design);
            _yield.Value = (Math.Log10(dev.YieldKt) + 2.0) / 7.0 * 1000.0;
            _height.Value = Math.Sqrt(d.HeightOfBurstM / 8000.0) * 1000.0;
        }
        else
        {
            _mode.Select(3);
            _yield.Value = (Math.Log10(d.Charge!.TntEquivalentKg) + 1.0) / 7.7 * 1000.0;
            _height.Value = Math.Sqrt(d.HeightOfBurstM / 300.0) * 1000.0;
        }
        _applyingPreset = false;
        RefreshLabels();
    }

    private void MarkCustom()
    {
        if (!_applyingPreset && _scenario.Selected != CustomIndex) _scenario.Select(CustomIndex);
    }

    private void RefreshLabels()
    {
        _yieldLabel.Text = IsConventional
            ? $"Masse de TNT : {Format.Mass(YieldValue)}"
            : $"Puissance : {Format.Yield(YieldValue)}";
        _heightLabel.Text = $"Hauteur d'explosion : {Format.Distance(HeightM)}";
        _timeLabel.Text = $"Vitesse du temps : ×{Format.Scale(TimeScale)}";
        _windLabel.Text = $"Vent vers {Format.Compass(WindTowardDeg)} : {WindSpeedMs:0.#} m/s ({WindSpeedMs * 3.6:0} km/h)";
    }

    private static Label Caption(string text)
    {
        var l = new Label { Text = text, Modulate = new Color(1, 1, 1, 0.7f) };
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    private static (Label, HSlider) AddSlider(VBoxContainer box, double min, double max, double value, double step = 1.0)
    {
        var label = Caption("");
        var slider = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value };
        box.AddChild(label);
        box.AddChild(slider);
        return (label, slider);
    }
}

/// <summary>Mise en forme des grandeurs pour l'affichage.</summary>
public static class Format
{
    public static string Distance(double m) => m >= 1000 ? $"{m / 1000:0.##} km" : $"{m:0} m";

    public static string Yield(double kt) => kt >= 1000 ? $"{kt / 1000:0.##} Mt"
                                           : kt >= 1 ? $"{kt:0.##} kt"
                                           : $"{kt * 1000:0.#} t";

    public static string Mass(double kg) => kg >= 1000 ? $"{kg / 1000:0.##} t" : kg >= 1 ? $"{kg:0.#} kg" : $"{kg * 1000:0} g";

    public static string Pressure(double pa) => pa >= 1e6 ? $"{pa / 1e6:0.#} MPa" : $"{pa / 1000:0.#} kPa";

    public static string Compass(double deg)
    {
        string[] names = { "le nord", "le nord-est", "l'est", "le sud-est", "le sud", "le sud-ouest", "l'ouest", "le nord-ouest" };
        return $"{names[(int)Math.Round(deg / 45.0) % 8]} ({deg:0}°)";
    }

    /// <summary>Durée lisible : secondes, puis minutes, puis heures.</summary>
    public static string Duration(double s) => s < 120 ? $"{s:0.00} s"
        : s < 7200 ? $"{(int)(s / 60)} min {(int)(s % 60):00} s"
        : $"{(int)(s / 3600)} h {(int)(s % 3600 / 60):00} min";

    public static string Scale(double s) => s >= 10 ? $"{s:0}" : s >= 1 ? $"{s:0.#}" : $"{s:0.###}";
}
