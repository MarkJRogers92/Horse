using System;
using System.Collections.Generic;
using System.Linq;
using Cloverbrook.Game.Audio;
using Cloverbrook.Game.Visual;
using Godot;

namespace Cloverbrook.Game.UI;

/// <summary>A small isolated 3D grooming view. The actual horse's markings remain underneath removable dirt.</summary>
public partial class GroomingView : Control
{
    public static readonly string[] PatchNames = { "Hindquarters", "Barrel", "Shoulder" };
    private const int Width = 800, Height = 400;
    private readonly List<(Vector2 At, float Age, int Seed)> _dust = new();
    private SubViewport _world = null!;
    private HorsePresenter _horse = null!;
    private ShaderMaterial _dirt = null!;
    private Vector2[] _centers = Array.Empty<Vector2>();
    private Vector2 _cursor;
    private bool _hover, _relaxed;
    private int _touch = -1, _dustSeed;
    private float _soundCooldown;
    public GroomingSession Session { get; private set; } = null!;
    public event Action? Changed;
    public event Action? CancelRequested;
    public IReadOnlyList<Vector2> PatchCenters => _centers;
    public HorsePresenter Horse => _horse;
    public bool Supported { get; private set; }
    public string SupportMessage { get; private set; } = "";

    public GroomingView()
    {
        CustomMinimumSize = new Vector2(640, 320);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = true;
        MouseEntered += () => { _hover = true; QueueRedraw(); };
        MouseExited += () => { _hover = false; Session?.EndStroke(); QueueRedraw(); };
    }

    public void Present(VisualDescriptor descriptor)
    {
        _world = new SubViewport
        {
            Size = new Vector2I(Width, Height), OwnWorld3D = true, TransparentBg = false,
            Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(_world);
        _world.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("dce6d5"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("edf0e7"), AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
        } });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-32, 35, 0), LightColor = new Color("fff0d7"), LightEnergy = 1.8f });
        _world.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-15, -120, 0), LightColor = new Color("dce8ff"), LightEnergy = 0.6f });
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("bacbb0"), Roughness = 1 },
        };
        _world.AddChild(ground);
        _horse = new HorsePresenter { Name = "GroomingHorse" }; _world.AddChild(_horse);
        // Set tack aside on this private presenter only, so rugs cannot hide the coat being brushed.
        var support = _horse.Present(descriptor with { CosmeticLayer = null }, "idle");
        Supported = support.Supported; SupportMessage = support.Summary;
        if (!Supported || _horse.Model == null) return;
        _horse.Pose("idle", 0.4);
        var body = FindBody(_horse.Model);
        var (positions, map) = BodyPoints(body);
        var min = positions.Aggregate(Vector3.Inf, (a, b) => new Vector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
        var max = positions.Aggregate(-Vector3.Inf, (a, b) => new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
        var target = (min + max) * 0.5f;
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = Math.Max((max.Y - min.Y) * 1.18f, (max.X - min.X) * 0.59f), Current = true };
        _world.AddChild(camera);
        camera.Position = target + new Vector3(0, 0.05f, 7); camera.LookAt(target, Vector3.Up);
        _centers = new Vector2[3];
        for (int p = 0; p < 3; p++)
        {
            var desired = new Vector3(0.20f + p * 0.31f, 0.53f, 0.96f);
            int nearest = Enumerable.Range(0, map.Length).MinBy(i =>
            {
                var d = map[i] - desired; return d.X * d.X + d.Y * d.Y * 0.45f + d.Z * d.Z * 0.2f;
            });
            var projected = camera.UnprojectPosition(positions[nearest]);
            _centers[p] = projected / new Vector2(Width, Height);
        }
        Session = new GroomingSession(_centers.Select(c => (c.X, c.Y)).ToArray(), 0.047f, 0.094f);
        _dirt = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grooming_dirt.gdshader") };
        _dirt.SetShaderParameter("patch_centers", _centers);
        _dirt.SetShaderParameter("patch_radius", new Vector2(0.047f, 0.094f));
        body.MaterialOverlay = _dirt;
        var texture = new TextureRect
        {
            Texture = _world.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(texture); texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Drawing feedback must sit above the viewport image.
        var ink = new GroomingInk { View = this, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(ink); ink.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _ink = ink;
        UpdateDirt();
    }

    private GroomingInk? _ink;
    public override void _Input(InputEvent input)
    {
        if (IsVisibleInTree() && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { GetViewport().SetInputAsHandled(); CancelRequested?.Invoke(); }
    }

    public override void _GuiInput(InputEvent input)
    {
        if (!Supported || Session == null || Session.Saving) return;
        int changed = -1;
        switch (input)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse:
                _cursor = mouse.Position;
                if (mouse.Pressed) changed = Session.BeginStroke(mouse.Position.X / Size.X, mouse.Position.Y / Size.Y);
                else Session.EndStroke();
                break;
            case InputEventMouseMotion motion:
                _cursor = motion.Position;
                changed = Session.DragTo(motion.Position.X / Size.X, motion.Position.Y / Size.Y);
                break;
            case InputEventScreenTouch touch when _touch < 0 || _touch == touch.Index:
                _cursor = touch.Position;
                if (touch.Pressed) { _touch = touch.Index; changed = Session.BeginStroke(touch.Position.X / Size.X, touch.Position.Y / Size.Y); }
                else { _touch = -1; Session.EndStroke(); }
                break;
            case InputEventScreenDrag drag when drag.Index == _touch:
                _cursor = drag.Position;
                changed = Session.DragTo(drag.Position.X / Size.X, drag.Position.Y / Size.Y);
                break;
        }
        if (changed >= 0) StrokeFeedback(changed, _cursor);
        _ink?.QueueRedraw(); AcceptEvent();
    }

    public void BrushPatch(int patch)
    {
        if (Session == null) return;
        int changed = Session.BrushPatch(patch);
        if (changed >= 0) StrokeFeedback(changed, _centers[changed] * Size);
    }

    private void StrokeFeedback(int patch, Vector2 at)
    {
        _dust.Add((at, 0, _dustSeed++));
        if (_dust.Count > 24) _dust.RemoveAt(0);
        if (_soundCooldown <= 0) { GameAudio.Play("page", -22); _soundCooldown = 0.12f; }
        UpdateDirt(); Changed?.Invoke();
        if (Session.Complete && !_relaxed)
        {
            _relaxed = true;
            _horse.Animator?.Play();
            _horse.SetPace(0.65);
            GameAudio.Play("horse_nicker", -16);
        }
    }

    private void UpdateDirt() => _dirt.SetShaderParameter("remaining", new Vector3(Session.DirtRemaining(0), Session.DirtRemaining(1), Session.DirtRemaining(2)));

    public override void _Process(double delta)
    {
        _soundCooldown -= (float)delta;
        for (int i = _dust.Count - 1; i >= 0; i--)
        {
            var d = _dust[i]; d.Age += (float)delta;
            if (d.Age > 0.7f) _dust.RemoveAt(i); else _dust[i] = d;
        }
        if (_dust.Count > 0 || _hover) _ink?.QueueRedraw();
    }

    private void DrawFeedback(Control ink)
    {
        foreach (var d in _dust)
            for (int n = 0; n < 5; n++)
            {
                float spread = ((d.Seed * 17 + n * 31) % 53 - 26) * d.Age;
                var at = d.At + new Vector2(spread, -d.Age * (22 + n * 6));
                ink.DrawCircle(at, 2.5f + d.Age * 3, new Color(0.66f, 0.52f, 0.34f, (1 - d.Age / 0.7f) * 0.6f));
            }
        if (_hover && !Session.Complete && !Session.Saving)
        {
            ink.DrawArc(_cursor, 23, 0, Mathf.Tau, 32, new Color(1, 0.98f, 0.88f, 0.85f), 2, true);
            ink.DrawStyleBox(CozyTheme.Box(new Color("bc8b55"), 8, 0), new Rect2(_cursor - new Vector2(15, 8), new Vector2(30, 16)));
            for (int i = -2; i <= 2; i++) ink.DrawLine(_cursor + new Vector2(i * 5, 7), _cursor + new Vector2(i * 5, 14), new Color("efe0bd"), 2, true);
        }
    }

    private partial class GroomingInk : Control
    {
        public GroomingView View = null!;
        public override void _Draw() => View.DrawFeedback(this);
    }

    private static MeshInstance3D FindBody(Node root)
    {
        if (root is MeshInstance3D mesh && mesh.Name == "Body") return mesh;
        foreach (Node child in root.GetChildren())
        {
            var found = FindBodyOrNull(child); if (found != null) return found;
        }
        throw new InvalidOperationException("The horse has no groomable body.");
    }
    private static MeshInstance3D? FindBodyOrNull(Node root)
    {
        if (root is MeshInstance3D mesh && mesh.Name == "Body") return mesh;
        foreach (Node child in root.GetChildren()) { var found = FindBodyOrNull(child); if (found != null) return found; }
        return null;
    }

    // Read existing mapped vertices; do not rewrite the source mesh, UVs, skinning, or materials.
    private static (Vector3[] Positions, Vector3[] Map) BodyPoints(MeshInstance3D mesh)
    {
        var skeleton = mesh.GetNode<Skeleton3D>(mesh.Skeleton); var skin = mesh.Skin!;
        var transforms = Enumerable.Range(0, skin.GetBindCount()).Select(i =>
        {
            int bone = skin.GetBindName(i).IsEmpty ? skin.GetBindBone(i) : skeleton.FindBone(skin.GetBindName(i));
            return skeleton.GetBoneGlobalPose(bone) * skin.GetBindPose(i);
        }).ToArray();
        var world = skeleton.GlobalTransform;
        var points = new List<Vector3>(); var mapped = new List<Vector3>();
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(surface);
            var basis = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(); var positions = basis.ToArray();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var custom = arrays[(int)Mesh.ArrayType.Custom0]; float[] coords;
            if (custom.VariantType == Variant.Type.PackedByteArray)
            { var bytes = custom.AsByteArray(); coords = new float[bytes.Length / sizeof(float)]; Buffer.BlockCopy(bytes, 0, coords, 0, bytes.Length); }
            else coords = custom.AsFloat32Array();
            if (coords.Length != positions.Length * 4) throw new InvalidOperationException("The horse has no grooming body map.");
            var targets = mesh.Mesh.SurfaceGetBlendShapeArrays(surface);
            bool relative = ((ArrayMesh)mesh.Mesh).BlendShapeMode == Mesh.BlendShapeMode.Relative;
            for (int b = 0; b < mesh.GetBlendShapeCount(); b++)
            {
                float amount = mesh.GetBlendShapeValue(b); if (amount == 0) continue;
                var shape = targets[b][(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (int i = 0; i < positions.Length; i++) positions[i] += amount * (relative ? shape[i] : shape[i] - basis[i]);
            }
            int influences = weights.Length / positions.Length;
            for (int i = 0; i < positions.Length; i++)
            {
                var position = Vector3.Zero;
                for (int w = 0; w < influences; w++) position += weights[i * influences + w] * (transforms[bones[i * influences + w]] * positions[i]);
                points.Add(world * position); mapped.Add(new Vector3(coords[i * 4], coords[i * 4 + 1], coords[i * 4 + 2]));
            }
        }
        return (points.ToArray(), mapped.ToArray());
    }
}
