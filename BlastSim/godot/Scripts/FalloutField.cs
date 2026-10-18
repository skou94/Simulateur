using System;
using BlastPhysics;
using BlastPhysics.Nuclear;
using Godot;

namespace BlastSimGame;

/// <summary>
/// Carte des doses de retombées pour le sol : grille dans le repère du vent (u sous le
/// vent, v en travers), débit H+1 et temps d'arrivée précalculés par cellule, dose
/// cumulée recalculée au fil du temps et envoyée au shader sous forme de texture.
/// </summary>
public sealed class FalloutField
{
    public const int CellsDownwind = 384;
    public const int CellsCrosswind = 160;

    // Limite d'affichage : débit H+1 en dessous duquel on considère la zone propre.
    private const double VisibleRateH1 = 0.5;

    private readonly float[] _rate, _arrival, _dose;
    private readonly byte[] _bytes;
    private readonly Image _image;
    private double _lastHours = -1;

    public FalloutField(Fallout model)
    {
        Model = model;
        var (dx, dz) = model.Downwind;
        Downwind = new Vector2((float)dx, (float)dz);

        // Étendue : jusqu'où le débit H+1 dépasse le seuil, dans l'axe puis en travers.
        double length = RangeFinder.OuterRange(r => model.DoseRateAtH1(r * dx, r * dz), VisibleRateH1, maxRangeM: 2.0e6, minRangeM: 50);
        length = Math.Max(length, 2000);
        double half = 500;
        foreach (double f in new[] { 0.15, 0.35, 0.6, 0.85 })
        {
            double u = f * length;
            double w = RangeFinder.OuterRange(v => model.DoseRateAtH1(u * dx - v * dz, u * dz + v * dx), VisibleRateH1,
                                             maxRangeM: length, minRangeM: 10);
            half = Math.Max(half, w);
        }
        UMin = (float)(-0.15 * length);
        UMax = (float)(length * 1.05);
        VHalf = (float)(half * 1.15);

        int n = CellsDownwind * CellsCrosswind;
        _rate = new float[n];
        _arrival = new float[n];
        _dose = new float[n];
        for (int j = 0; j < CellsCrosswind; j++)
        for (int i = 0; i < CellsDownwind; i++)
        {
            float u = Mathf.Lerp(UMin, UMax, (i + 0.5f) / CellsDownwind);
            float v = Mathf.Lerp(-VHalf, VHalf, (j + 0.5f) / CellsCrosswind);
            // Repère local : x = u·d − v·d⊥ … avec d⊥ = (−dz, dx).
            double x = u * dx - v * dz, z = u * dz + v * dx;
            _rate[j * CellsDownwind + i] = (float)model.DoseRateAtH1(x, z);
            _arrival[j * CellsDownwind + i] = (float)model.ArrivalTimeHours(x, z);
        }
        _bytes = new byte[n * 4];
        _image = Image.CreateFromData(CellsDownwind, CellsCrosswind, false, Image.Format.Rf, _bytes);
        Texture = ImageTexture.CreateFromImage(_image);

        // Distance sous le vent où la dose sur 48 h atteint encore 100 rad.
        Range100Rad48hM = RangeFinder.OuterRange(r => model.AccumulatedDose(r * dx, r * dz, 48), 100, maxRangeM: 2.0e6, minRangeM: 50);
    }

    public Fallout Model { get; }
    public Vector2 Downwind { get; }
    public float UMin { get; }
    public float UMax { get; }
    public float VHalf { get; }
    public ImageTexture Texture { get; }
    public double Range100Rad48hM { get; }

    /// <summary>Met à jour la dose cumulée au temps donné (h depuis l'explosion).</summary>
    public void Update(double hours)
    {
        // Inutile de retransférer la texture pour des variations imperceptibles.
        if (_lastHours >= 0 && Math.Abs(hours - _lastHours) < 0.002 * Math.Max(hours, 0.05)) return;
        _lastHours = hours;
        for (int k = 0; k < _rate.Length; k++)
            _dose[k] = (float)Fallout.DoseFromRate(_rate[k], _arrival[k], hours);
        Buffer.BlockCopy(_dose, 0, _bytes, 0, _bytes.Length);
        _image.SetData(CellsDownwind, CellsCrosswind, false, Image.Format.Rf, _bytes);
        Texture.Update(_image);
    }
}
