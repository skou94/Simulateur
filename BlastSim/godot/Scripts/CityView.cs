using System;
using System.Collections.Generic;
using BlastPhysics;
using BlastPhysics.Damage;
using Godot;

namespace BlastSimGame;

/// <summary>Bilan des dégâts affiché dans l'interface.</summary>
public readonly record struct DamageTally(int Total, int Hit, int Light, int Moderate, int Severe, int Collapsed, int Burning);

/// <summary>
/// Rendu de la ville en un seul MultiMesh, et application des effets physiques :
/// chaque bâtiment reçoit le souffle à son temps d'arrivée calculé, son état de dégât
/// est tiré selon les probabilités du modèle de vulnérabilité, puis il s'effondre
/// en chute libre et tombe à l'opposé du point zéro.
/// <para>
/// Les instances sont écrites dans un tampon C# envoyé en un seul transfert par image
/// (au lieu d'un appel au moteur par bâtiment modifié).
/// </para>
/// </summary>
public partial class CityView : MultiMeshInstance3D
{
    // Exposition thermique : surfaces carbonisées au-delà de ~15 cal/cm², incendies
    // déclarés vers 10 (bois) à 25 cal/cm² (béton, intérieurs) — ordres de grandeur
    // de Glasstone & Dolan tableau 7.40.
    private const double CharStartCalPerCm2 = 15.0;
    private const double CharFullCalPerCm2 = 60.0;

    // Vitrages : bris dès ~2 kPa (Glasstone §5.14 : 0.5-1 psi pour la plupart des vitres).
    private const double GlassBreakPa = 2000.0;

    // Disposition du tampon MultiMesh : transformation 3×4, couleur, données d'instance.
    private const int Stride = 20;
    private const int ColorOffset = 12;
    private const int CustomOffset = 16;

    private struct State
    {
        public float Distance;
        public RadialProfile.Sample Load;
        public DamageState Damage;
        public bool BlastResolved;
        public bool Burning;
        public float CurrentHeight, TargetHeight, StartHeight;
        public float Tilt, TargetTilt;
        public double AnimStart, AnimDuration;
        public double FireStart;
        public float Fire, Char, Broken;
    }

    private CityModel _city = null!;
    private MultiMesh _mesh = null!;
    private float[] _buffer = Array.Empty<float>();
    private bool _dirty;
    private State[] _state = Array.Empty<State>();
    private int[] _byArrival = Array.Empty<int>();
    private int _nextArrival;
    private readonly List<int> _animating = new();
    private readonly List<int> _igniting = new();
    private RadialProfile? _profile;
    private double _thermalTimeS;
    private bool _thermalDone;
    private int _hit, _light, _moderate, _severe, _collapsed, _burning;

    // Précalculés pour l'animation : rotation propre (cos, sin) et axe horizontal de
    // bascule (perpendiculaire à la direction du point zéro), par bâtiment.
    private float[] _cosYaw = Array.Empty<float>(), _sinYaw = Array.Empty<float>();
    private float[] _tiltAxisX = Array.Empty<float>(), _tiltAxisZ = Array.Empty<float>();

    public DamageTally Tally => new(_state.Length, _hit, _light, _moderate, _severe, _collapsed, _burning);

    public void Build(CityModel city)
    {
        _city = city;
        _mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One },
        };
        _mesh.InstanceCount = city.Buildings.Count;
        Multimesh = _mesh;
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/building.gdshader") };
        CastShadow = ShadowCastingSetting.On;

        int n = city.Buildings.Count;
        _state = new State[n];
        _buffer = new float[n * Stride];
        _cosYaw = new float[n]; _sinYaw = new float[n];
        _tiltAxisX = new float[n]; _tiltAxisZ = new float[n];
        for (int i = 0; i < n; i++)
        {
            _cosYaw[i] = MathF.Cos(city.Buildings[i].Rotation);
            _sinYaw[i] = MathF.Sin(city.Buildings[i].Rotation);
            var c = city.Buildings[i].Color;
            int o = i * Stride + ColorOffset;
            _buffer[o] = c.R; _buffer[o + 1] = c.G; _buffer[o + 2] = c.B; _buffer[o + 3] = 1f;
        }
        ResetDamage();
    }

    public void ResetDamage()
    {
        _profile = null;
        _thermalDone = false;
        _animating.Clear();
        _igniting.Clear();
        _hit = _light = _moderate = _severe = _collapsed = _burning = 0;
        for (int i = 0; i < _state.Length; i++)
        {
            var b = _city.Buildings[i];
            _state[i] = new State { CurrentHeight = b.Height, TargetHeight = b.Height };
            WriteTransform(i);
            WriteCustom(i);
        }
        Flush();
    }

    /// <summary>Prépare l'impact : charge de chaque bâtiment selon sa distance au point zéro.</summary>
    public void Prepare(RadialProfile profile, Vector2 groundZero, double thermalTimeS)
    {
        ResetDamage();
        _profile = profile;
        _thermalTimeS = thermalTimeS;
        var arrival = new double[_state.Length];
        for (int i = 0; i < _state.Length; i++)
        {
            var b = _city.Buildings[i];
            float d = new Vector2(b.X, b.Z).DistanceTo(groundZero);
            _state[i].Distance = d;
            _state[i].Load = profile.At(d);
            // Le bâtiment bascule à l'opposé du point zéro, dans le sens du souffle :
            // axe horizontal (rz, -rx) pour la direction radiale (rx, rz).
            float rx = d > 1e-3f ? (b.X - groundZero.X) / d : 1f;
            float rz = d > 1e-3f ? (b.Z - groundZero.Y) / d : 0f;
            _tiltAxisX[i] = rz;
            _tiltAxisZ[i] = -rx;
            arrival[i] = _state[i].Load.ArrivalTimeS;
        }
        _byArrival = new int[_state.Length];
        for (int i = 0; i < _byArrival.Length; i++) _byArrival[i] = i;
        Array.Sort(arrival, _byArrival);
        _nextArrival = 0;
    }

    /// <summary>Fait avancer les effets jusqu'au temps de simulation t (s depuis l'explosion).</summary>
    public void Simulate(double t)
    {
        if (_profile is null) return;

        if (!_thermalDone && t >= _thermalTimeS)
        {
            ApplyThermal(t);
            _thermalDone = true;
        }

        while (_nextArrival < _byArrival.Length && _state[_byArrival[_nextArrival]].Load.ArrivalTimeS <= t)
            ApplyBlast(_byArrival[_nextArrival++], t);

        for (int k = _animating.Count - 1; k >= 0; k--)
        {
            int i = _animating[k];
            ref var s = ref _state[i];
            double u = Math.Clamp((t - s.AnimStart) / s.AnimDuration, 0.0, 1.0);
            float e = (float)(u * u); // chute accélérée
            s.CurrentHeight = Mathf.Lerp(s.StartHeight, s.TargetHeight, e);
            s.Tilt = Mathf.Lerp(0f, s.TargetTilt, (float)u);
            WriteTransform(i);
            if (u >= 1.0) RemoveSwap(_animating, k);
        }

        for (int k = _igniting.Count - 1; k >= 0; k--)
        {
            int i = _igniting[k];
            ref var s = ref _state[i];
            if (t < s.FireStart) continue;
            if (!s.Burning) { s.Burning = true; _burning++; }
            s.Fire = (float)Math.Clamp((t - s.FireStart) / 20.0, 0.0, 1.0);
            WriteCustom(i);
            if (s.Fire >= 1f) RemoveSwap(_igniting, k);
        }

        Flush();
    }

    private void Flush()
    {
        if (!_dirty) return;
        _mesh.Buffer = _buffer;
        _dirty = false;
    }

    // Retrait en O(1) : l'ordre des listes de travail n'a pas d'importance.
    private static void RemoveSwap(List<int> list, int k)
    {
        list[k] = list[^1];
        list.RemoveAt(list.Count - 1);
    }

    private void ApplyThermal(double t)
    {
        for (int i = 0; i < _state.Length; i++)
        {
            ref var s = ref _state[i];
            double q = s.Load.ThermalCalPerCm2;
            if (q <= 0) continue;
            s.Char = (float)Math.Clamp((q - CharStartCalPerCm2) / (CharFullCalPerCm2 - CharStartCalPerCm2), 0.0, 1.0);
            if (q >= IgnitionThreshold(_city.Buildings[i].Structure))
            {
                // Départs de feu étalés : les foyers mettent du temps à prendre.
                s.FireStart = t + 2.0 + 30.0 * Hash(i, 3);
                _igniting.Add(i);
            }
            if (s.Char > 0) WriteCustom(i);
        }
    }

    private static double IgnitionThreshold(StructureType type) => type switch
    {
        StructureType.WoodFrame => 10.0,
        StructureType.UnreinforcedMasonry => 15.0,
        _ => 25.0,
    };

    private void ApplyBlast(int i, double t)
    {
        ref var s = ref _state[i];
        var b = _city.Buildings[i];
        s.BlastResolved = true;
        _hit++;
        if (s.Load.OverpressurePa >= GlassBreakPa) s.Broken = 1f;

        var a = BuildingDamage.Assess(b.Structure, s.Load.OverpressurePa, s.Load.ImpulsePaS);
        double draw = Hash(i, 1);
        s.Damage = draw < a.PCollapse ? DamageState.Collapse
                 : draw < a.PSevere ? DamageState.Severe
                 : draw < a.PModerate ? DamageState.Moderate
                 : draw < a.PLight ? DamageState.Light
                 : DamageState.None;
        switch (s.Damage)
        {
            case DamageState.Light: _light++; break;
            case DamageState.Moderate: _moderate++; break;
            case DamageState.Severe: _severe++; break;
            case DamageState.Collapse: _collapsed++; break;
        }

        float r = (float)Hash(i, 2);
        (float heightFactor, float tiltDeg) = s.Damage switch
        {
            DamageState.Collapse => (0.05f + 0.1f * r, 4f + 10f * r),
            DamageState.Severe => (0.4f + 0.3f * r, 3f + 6f * r),
            DamageState.Moderate => (0.96f, 0.5f + 1.5f * r),
            _ => (1f, 0f),
        };
        if (s.Damage >= DamageState.Moderate)
        {
            s.StartHeight = s.CurrentHeight;
            s.TargetHeight = b.Height * heightFactor;
            s.TargetTilt = Mathf.DegToRad(tiltDeg);
            s.AnimStart = t;
            // Durée d'une chute libre de la hauteur perdue, un peu freinée par la structure.
            double drop = Math.Max(s.StartHeight - s.TargetHeight, 0.5);
            s.AnimDuration = 1.4 * Math.Sqrt(2.0 * drop / 9.81) + 0.2;
            _animating.Add(i);
        }
        if (s.Damage >= DamageState.Severe) s.Char = Math.Max(s.Char, 0.35f);
        WriteCustom(i);
    }

    // M = R_bascule · R_lacet · S, écrite directement dans le tampon (format MultiMesh :
    // lignes de la matrice 3×4). R_bascule par la formule de Rodrigues autour de l'axe
    // horizontal a = (ax, 0, az) : R = cosθ·I + sinθ·[a]× + (1 − cosθ)·a·aᵀ.
    private void WriteTransform(int i)
    {
        var b = _city.Buildings[i];
        ref var s = ref _state[i];
        float w = b.Width, h = MathF.Max(s.CurrentHeight, 0.3f), d = b.Depth;
        float cy = _cosYaw[i], sy = _sinYaw[i];
        float ax = _tiltAxisX[i], az = _tiltAxisZ[i];
        float c = MathF.Cos(s.Tilt), sn = MathF.Sin(s.Tilt), k = 1f - c;

        float r00 = c + k * ax * ax, r01 = -sn * az, r02 = k * ax * az;
        float r10 = sn * az, r11 = c, r12 = -sn * ax;
        float r20 = k * ax * az, r21 = sn * ax, r22 = c + k * az * az;

        // Colonnes de R_lacet·S : (cy·w, 0, −sy·w), (0, h, 0), (sy·d, 0, cy·d).
        float ux = cy * w, uz = -sy * w;
        float qx = sy * d, qz = cy * d;
        float m00 = r00 * ux + r02 * uz, m10 = r10 * ux + r12 * uz, m20 = r20 * ux + r22 * uz;
        float m01 = r01 * h, m11 = r11 * h, m21 = r21 * h;
        float m02 = r00 * qx + r02 * qz, m12 = r10 * qx + r12 * qz, m22 = r20 * qx + r22 * qz;

        // Le pied de la boîte unitaire (y = −0.5) reste posé au sol.
        int o = i * Stride;
        var f = _buffer;
        f[o] = m00; f[o + 1] = m01; f[o + 2] = m02; f[o + 3] = b.X + 0.5f * m01;
        f[o + 4] = m10; f[o + 5] = m11; f[o + 6] = m12; f[o + 7] = 0.5f * m11;
        f[o + 8] = m20; f[o + 9] = m21; f[o + 10] = m22; f[o + 11] = b.Z + 0.5f * m21;
        _dirty = true;
    }

    private void WriteCustom(int i)
    {
        ref var s = ref _state[i];
        int o = i * Stride + CustomOffset;
        _buffer[o] = s.Char;
        _buffer[o + 1] = s.Fire;
        _buffer[o + 2] = s.Broken;
        _buffer[o + 3] = _city.Buildings[i].Seed;
        _dirty = true;
    }

    // Aléa déterministe par bâtiment (reproductible d'une détonation à l'autre).
    private double Hash(int i, int salt)
    {
        uint h = (uint)i * 2654435761u ^ (uint)(salt * 40503) ^ (uint)(_city.Buildings[i].Seed * 1e6f);
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return h / (double)uint.MaxValue;
    }
}
