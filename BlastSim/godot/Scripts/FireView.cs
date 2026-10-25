using System;
using Godot;

namespace BlastSimGame;

/// <summary>Incendie à afficher : point du toit, taille du bâtiment, intensité 0..1, aléa.</summary>
public readonly record struct FireInstance(Vector3 Roof, float Size, float Intensity, float Seed);

/// <summary>
/// Flammes et colonnes de fumée des bâtiments en feu, en deux MultiMesh (un seul appel de
/// dessin chacun quel que soit le nombre d'incendies). Panneaux orientés vers la caméra
/// dans les shaders ; la fumée se couche dans le sens du vent.
/// </summary>
public partial class FireView : Node3D
{
    public const int MaxFires = 6000;

    // Format MultiMesh : transformation 3×4 puis données d'instance (pas de couleur).
    private const int Stride = 16;

    private MultiMesh _flames = null!, _smoke = null!;
    private ShaderMaterial _smokeMat = null!;
    private readonly FireInstance[] _fires = new FireInstance[MaxFires];
    private readonly float[] _flameBuf = new float[MaxFires * Stride];
    private readonly float[] _smokeBuf = new float[MaxFires * Stride];

    public override void _Ready()
    {
        _flames = NewLayer("res://Shaders/flame.gdshader", out _);
        _smoke = NewLayer("res://Shaders/fire_smoke.gdshader", out _smokeMat);
    }

    private MultiMesh NewLayer(string shader, out ShaderMaterial material)
    {
        material = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = MaxFires,
            VisibleInstanceCount = 0,
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Les panneaux sont déplacés dans le shader : pas d'élimination hors champ par Godot.
            CustomAabb = new Aabb(new Vector3(-300_000, -1000, -300_000), new Vector3(600_000, 80_000, 600_000)),
        });
        return mm;
    }

    /// <summary>Vent (direction sous le vent, vitesse) et éclairage de la fumée (jour 1, nuit faible).</summary>
    public void SetConditions(Vector3 downwind, float windSpeedMs, float brightness)
    {
        _smokeMat.SetShaderParameter("wind_dir", downwind.Normalized());
        _smokeMat.SetShaderParameter("lean", Mathf.Clamp(windSpeedMs / 8f, 0.1f, 2.5f));
        _smokeMat.SetShaderParameter("brightness", brightness);
    }

    public void Clear()
    {
        _flames.VisibleInstanceCount = 0;
        _smoke.VisibleInstanceCount = 0;
    }

    public void Update(CityView city)
    {
        int n = city.CollectFires(_fires);
        for (int k = 0; k < n; k++)
        {
            var f = _fires[k];
            // Flammes : largeur du bâtiment, hauteur selon l'intensité ; fumée bien plus haute.
            float w = Mathf.Clamp(f.Size * 0.9f, 4f, 70f);
            float flameH = w * (0.6f + 1.2f * f.Intensity);
            Write(_flameBuf, k, f.Roof, w, flameH, f.Intensity, f.Seed);
            float smokeH = 60f + 9f * w * f.Intensity;
            Write(_smokeBuf, k, f.Roof + new Vector3(0f, flameH * 0.5f, 0f), w * 1.4f, smokeH, f.Intensity, f.Seed);
        }
        _flames.Buffer = _flameBuf;
        _smoke.Buffer = _smokeBuf;
        _flames.VisibleInstanceCount = n;
        _smoke.VisibleInstanceCount = n;
    }

    // Matrice d'échelle pure (largeur, hauteur) + position ; le shader fait l'orientation.
    private static void Write(float[] buf, int k, Vector3 pos, float width, float height, float intensity, float seed)
    {
        int o = k * Stride;
        buf[o] = width; buf[o + 1] = 0; buf[o + 2] = 0; buf[o + 3] = pos.X;
        buf[o + 4] = 0; buf[o + 5] = height; buf[o + 6] = 0; buf[o + 7] = pos.Y;
        buf[o + 8] = 0; buf[o + 9] = 0; buf[o + 10] = 1; buf[o + 11] = pos.Z;
        buf[o + 12] = intensity; buf[o + 13] = seed; buf[o + 14] = 0; buf[o + 15] = 0;
    }
}
