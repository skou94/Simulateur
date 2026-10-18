using System;
using Godot;

namespace BlastSimGame;

/// <summary>
/// Caméra orbitale : glisser (clic gauche) pour tourner, clic droit ou milieu pour
/// déplacer, molette pour zoomer, clic gauche simple pour désigner un point du sol.
/// </summary>
public partial class OrbitCamera : Camera3D
{
    public Vector3 Target = Vector3.Zero;
    public float Distance = 4200f;
    public float Yaw = 0.7f;
    public float Pitch = 0.42f;

    /// <summary>Clic simple : origine et direction du rayon, et point du sol visé (null au-dessus de l'horizon).</summary>
    public event Action<Vector3, Vector3, Vector3?>? Clicked;

    private const float MinDistance = 40f;
    private const float MaxDistance = 250_000f;
    private const float ClickTolerancePx = 5f;

    private Vector2 _pressPosition;
    private bool _leftDown, _dragging, _panning;
    private float _shake;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Fov = 55f;
        Far = 800_000f;
    }

    /// <summary>Secousse lorsque le front de choc passe sous la caméra (0..1).</summary>
    public void Shake(float amount) => _shake = Mathf.Max(_shake, Mathf.Clamp(amount, 0f, 1f));

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Distance = Mathf.Clamp(Distance * 0.88f, MinDistance, MaxDistance);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Distance = Mathf.Clamp(Distance * 1.14f, MinDistance, MaxDistance);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _leftDown = true;
                    _dragging = false;
                    _pressPosition = mb.Position;
                }
                else
                {
                    if (_leftDown && !_dragging)
                        Clicked?.Invoke(ProjectRayOrigin(mb.Position), ProjectRayNormal(mb.Position), GroundPoint(mb.Position));
                    _leftDown = false;
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } mb:
                _panning = mb.Pressed;
                break;
            case InputEventMouseMotion mm:
                if (_leftDown)
                {
                    if (mm.Position.DistanceTo(_pressPosition) > ClickTolerancePx) _dragging = true;
                    if (_dragging)
                    {
                        Yaw -= mm.Relative.X * 0.005f;
                        Pitch = Mathf.Clamp(Pitch + mm.Relative.Y * 0.004f, 0.05f, 1.5f);
                    }
                }
                else if (_panning)
                {
                    var right = new Vector3(Mathf.Cos(Yaw), 0f, -Mathf.Sin(Yaw));
                    var forward = new Vector3(-Mathf.Sin(Yaw), 0f, -Mathf.Cos(Yaw));
                    float k = Distance * 0.0015f;
                    Target += (-right * mm.Relative.X + forward * mm.Relative.Y) * k;
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        var offset = new Vector3(
            Mathf.Cos(Pitch) * Mathf.Sin(Yaw),
            Mathf.Sin(Pitch),
            Mathf.Cos(Pitch) * Mathf.Cos(Yaw)) * Distance;
        var position = Target + offset;
        if (_shake > 0.001f)
        {
            float a = _shake * _shake * Mathf.Min(Distance * 0.01f, 25f);
            position += new Vector3(_rng.RandfRange(-a, a), _rng.RandfRange(-a, a), _rng.RandfRange(-a, a));
            _shake *= Mathf.Exp(-(float)delta * 2.5f);
        }
        // Plan proche adapté à la distance : précision de profondeur de 40 m à 250 km.
        Near = Mathf.Clamp(Distance * 0.0008f, 0.2f, 60f);
        LookAtFromPosition(position, Target, Vector3.Up);
    }

    /// <summary>Intersection du rayon de la souris avec le sol (y = 0).</summary>
    public Vector3? GroundPoint(Vector2 screen)
    {
        var origin = ProjectRayOrigin(screen);
        var dir = ProjectRayNormal(screen);
        if (dir.Y >= -1e-4f) return null;
        float t = -origin.Y / dir.Y;
        return origin + dir * t;
    }
}
