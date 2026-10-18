using System;
using BlastPhysics;
using Godot;

namespace BlastSimGame;

/// <summary>
/// Cratère : cuvette parabolique et bourrelet de déblais, maillage radial. Le sol est
/// percé à l'intérieur du rayon (shader du sol), le bourrelet recouvre le pourtour.
/// </summary>
public partial class CraterView : MeshInstance3D
{
    private const int Rings = 40;
    private const int Segments = 128;

    private Crater _crater = Crater.None;
    private double _formTimeS;

    public override void _Ready()
    {
        MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/crater.gdshader") };
        CastShadow = ShadowCastingSetting.On;
        Visible = false;
    }

    public void Display(Crater crater, Vector2 groundZero)
    {
        _crater = crater;
        Visible = crater.Exists;
        if (!crater.Exists) return;
        Position = new Vector3(groundZero.X, 0f, groundZero.Y);
        Mesh = BuildMesh(crater);
        // Excavation : quelques secondes, plus longue pour un grand cratère.
        _formTimeS = 0.5 + crater.RadiusM / 150.0;
        Scale = new Vector3(1f, 0.001f, 1f);
    }

    public void Clear()
    {
        _crater = Crater.None;
        Visible = false;
    }

    public void Update(double t)
    {
        if (!_crater.Exists) return;
        float k = (float)Math.Clamp(t / _formTimeS, 0.0, 1.0);
        Scale = new Vector3(1f, Mathf.Max(k * (2f - k), 0.001f), 1f);
    }

    private static ArrayMesh BuildMesh(Crater c)
    {
        double R = c.RadiusM, D = c.DepthM, Rl = c.LipRadiusM, H = c.LipHeightM;
        double Height(double r) => r < R
            ? -D + (D + H) * (r / R) * (r / R)
            : H * Math.Pow(1.0 - Math.Clamp((r - R) / (Rl - R), 0.0, 1.0), 2.0);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rng = new RandomNumberGenerator { Seed = 99 };
        var verts = new Vector3[(Rings + 1) * Segments];
        for (int i = 0; i <= Rings; i++)
        {
            double r = Rl * i / Rings;
            for (int j = 0; j < Segments; j++)
            {
                double a = Math.Tau * j / Segments;
                // Léger désordre du relief, nul au bord extérieur pour raccorder le sol.
                double jitter = (rng.Randf() - 0.5) * 0.08 * D * (1.0 - (double)i / Rings);
                verts[i * Segments + j] = new Vector3((float)(r * Math.Cos(a)), (float)(Height(r) + jitter), (float)(r * Math.Sin(a)));
            }
        }
        for (int i = 0; i < Rings; i++)
        for (int j = 0; j < Segments; j++)
        {
            int j1 = (j + 1) % Segments;
            int a = i * Segments + j, b = i * Segments + j1, cc = (i + 1) * Segments + j, d = (i + 1) * Segments + j1;
            Tri(st, verts[a], verts[cc], verts[b], R);
            Tri(st, verts[b], verts[cc], verts[d], R);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, double radius)
    {
        foreach (var v in new[] { a, b, c })
        {
            // Rouge : 1 dans la cuvette (terre vitrifiée, sombre), 0 sur le bourrelet.
            float inside = Mathf.Clamp(1f - new Vector2(v.X, v.Z).Length() / (float)radius, 0f, 1f);
            st.SetColor(new Color(inside, 0f, 0f));
            st.AddVertex(v);
        }
    }
}
