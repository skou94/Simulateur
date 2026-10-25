using System;
using System.Collections.Generic;
using BlastPhysics;
using BlastPhysics.Casualties;
using BlastPhysics.Damage;
using BlastPhysics.Nuclear;
using Godot;

namespace BlastSimGame;

/// <summary>Effets d'une détonation échantillonnés selon la distance, pour les graphiques.</summary>
public sealed record ChartData(
    string Name, double[] DistanceM, double[] PressureKPa, double[] ThermalCal, double[] RadiationRad,
    double[] DeathIndoor, double[] DeathOutdoor, double ThirdDegreeCal)
{
    private const int Samples = 140;

    public static ChartData From(Detonation d, double maxRangeM, TimeOfDay time)
    {
        var dist = new double[Samples];
        var p = new double[Samples];
        var q = new double[Samples];
        var rad = new double[Samples];
        var indoor = new double[Samples];
        var outdoor = new double[Samples];
        double q3 = d.Thermal?.BurnThresholdCalPerCm2(BurnDegree.Third) ?? 0;
        double q2 = d.Thermal?.BurnThresholdCalPerCm2(BurnDegree.Second) ?? 0;
        double l0 = Math.Log(10.0), l1 = Math.Log(maxRangeM);
        for (int i = 0; i < Samples; i++)
        {
            double r = Math.Exp(l0 + (l1 - l0) * i / (Samples - 1));
            var e = d.At(r);
            dist[i] = r;
            p[i] = e.Blast.OverpressurePa / 1000.0;
            q[i] = e.ThermalFluenceCalPerCm2;
            rad[i] = e.InitialRadiationRad;
            var x = new Exposure(e.Blast.OverpressurePa, e.Blast.ImpulsePaS, e.ThermalFluenceCalPerCm2, q3, q2,
                                 e.InitialRadiationRad, 0, e.FragmentHitProbability);
            var damage = BuildingDamage.Assess(StructureType.UnreinforcedMasonry, x.OverpressurePa, x.ImpulsePaS);
            bool burning = x.ThermalCalPerCm2 >= 15.0;
            indoor[i] = 100.0 * CasualtyModel.Expected(StructureType.UnreinforcedMasonry, damage, burning, x, time).Death;
            outdoor[i] = 100.0 * CasualtyModel.Outdoor(x).Death;
        }
        return new ChartData(d.Name, dist, p, q, rad, indoor, outdoor, q3);
    }
}

/// <summary>
/// Quatre graphiques des effets selon la distance (échelle logarithmique) ; la détonation
/// précédente reste affichée en gris pointillé pour comparer deux engins.
/// </summary>
public partial class ChartsPanel : PanelContainer
{
    private readonly ChartsCanvas _canvas = new();

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });
        MouseFilter = MouseFilterEnum.Stop;
        _canvas.CustomMinimumSize = new Vector2(760, 430);
        AddChild(_canvas);
    }

    public void SetData(ChartData data)
    {
        if (_canvas.Current is not null && _canvas.Current != data) _canvas.Previous = _canvas.Current;
        _canvas.Current = data;
        _canvas.QueueRedraw();
    }
}

public partial class ChartsCanvas : Control
{
    public ChartData? Current;
    public ChartData? Previous;

    private static readonly Color Axis = new(1, 1, 1, 0.35f);
    private static readonly Color Grid = new(1, 1, 1, 0.08f);
    private static readonly Color Ghost = new(0.75f, 0.75f, 0.75f, 0.6f);

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        if (Current is null)
        {
            DrawString(font, new Vector2(10, 30), "Déclenchez une explosion pour afficher les courbes.", fontSize: 15,
                       modulate: new Color(1, 1, 1, 0.7f));
            return;
        }
        string legend = Previous is null ? Current.Name : $"{Current.Name}   (gris : {Previous.Name})";
        DrawString(font, new Vector2(4, 14), legend, fontSize: 14, modulate: Colors.White);

        float w = (Size.X - 10) / 2f, h = (Size.Y - 30) / 2f;
        var cells = new[]
        {
            new Rect2(0, 24, w, h), new Rect2(w + 10, 24, w, h),
            new Rect2(0, 30 + h, w, h), new Rect2(w + 10, 30 + h, w, h),
        };
        Chart(cells[0], "Surpression (kPa)", d => d.PressureKPa, true, 0.1, 1e4, new Color(1f, 0.55f, 0.2f),
              new[] { (140.0, "140"), (35.0, "35"), (7.0, "7") });
        var burns = Current.ThirdDegreeCal > 0 ? new[] { (Current.ThirdDegreeCal, "3e degré") } : Array.Empty<(double, string)>();
        Chart(cells[1], "Rayonnement thermique (cal/cm²)", d => d.ThermalCal, true, 0.1, 1e3, new Color(1f, 0.35f, 0.8f), burns);
        Chart(cells[2], "Radiation initiale (rad)", d => d.RadiationRad, true, 1, 1e5, new Color(0.35f, 1f, 0.45f),
              new[] { (500.0, "500"), (100.0, "100") });
        Chart(cells[3], "Mortalité (%) — dedans (maçonnerie) / dehors", d => d.DeathIndoor, false, 0, 100,
              new Color(1f, 0.3f, 0.3f), new[] { (50.0, "50 %") }, d => d.DeathOutdoor, new Color(1f, 0.85f, 0.4f));
    }

    private void Chart(Rect2 cell, string title, Func<ChartData, double[]> series, bool logY, double yMin, double yMax,
                       Color color, (double Value, string Label)[] marks,
                       Func<ChartData, double[]>? second = null, Color secondColor = default)
    {
        var font = ThemeDB.FallbackFont;
        var plot = new Rect2(cell.Position + new Vector2(42, 18), cell.Size - new Vector2(50, 40));
        DrawString(font, cell.Position + new Vector2(0, 12), title, fontSize: 13, modulate: new Color(1, 1, 1, 0.85f));
        DrawRect(plot, Axis, filled: false);

        // Axe des distances commun aux deux détonations comparées.
        double xMin = Current!.DistanceM[0];
        double xMax = Math.Max(Current.DistanceM[^1], Previous?.DistanceM[^1] ?? 0);
        float X(double m) => plot.Position.X + plot.Size.X * (float)((Math.Log(m) - Math.Log(xMin)) / (Math.Log(xMax) - Math.Log(xMin)));
        float Y(double v)
        {
            double u = logY
                ? (Math.Log(Math.Max(v, yMin)) - Math.Log(yMin)) / (Math.Log(yMax) - Math.Log(yMin))
                : (v - yMin) / (yMax - yMin);
            return plot.End.Y - plot.Size.Y * (float)Math.Clamp(u, 0.0, 1.0);
        }

        // Graduations de distance : 10 m, 100 m, 1 km, 10 km, 100 km.
        foreach (var (m, label) in new[] { (10.0, "10 m"), (100.0, "100 m"), (1e3, "1 km"), (1e4, "10 km"), (1e5, "100 km") })
        {
            if (m < xMin || m > xMax) continue;
            float x = X(m);
            DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, plot.End.Y), Grid);
            DrawString(font, new Vector2(x - 14, plot.End.Y + 14), label, fontSize: 11, modulate: Axis);
        }
        foreach (var (value, label) in marks)
        {
            float y = Y(value);
            DrawDashedLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), new Color(1, 1, 1, 0.25f), 1f, 4f);
            DrawString(font, new Vector2(cell.Position.X, y + 4), label, fontSize: 11, modulate: Axis);
        }

        if (Previous is not null) Line(Previous, series, X, Y, Ghost, dashed: true);
        Line(Current, series, X, Y, color, dashed: false);
        if (second is not null)
        {
            if (Previous is not null) Line(Previous, second, X, Y, Ghost, dashed: true);
            Line(Current, second, X, Y, secondColor, dashed: false);
        }
    }

    private void Line(ChartData data, Func<ChartData, double[]> series, Func<double, float> x, Func<double, float> y,
                      Color color, bool dashed)
    {
        var values = series(data);
        var pts = new List<Vector2>(values.Length);
        for (int i = 0; i < values.Length; i++) pts.Add(new Vector2(x(data.DistanceM[i]), y(values[i])));
        if (!dashed)
        {
            DrawPolyline(pts.ToArray(), color, 2f, antialiased: true);
            return;
        }
        for (int i = 0; i + 1 < pts.Count; i += 2) DrawLine(pts[i], pts[i + 1], color, 1.5f);
    }
}
