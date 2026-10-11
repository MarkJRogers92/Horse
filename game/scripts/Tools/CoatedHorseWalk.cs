using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Cloverbrook.Game.Visual;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Cloverbrook.Game.Tools;

/// <summary>Test-only native HorsePresenter preview of the geometry/UV-preserved Light Walk candidate.</summary>
public partial class CoatedHorseWalk : Node3D
{
    private const string PreviewCatalogPath = "res://data/visual_catalog_light_walk_preview.json";
    private const string FamilyId = "horse.standard.adult.v1";
    private const string CandidateScenePath = "res://assets/horse/production/horse_light_walk.glb";
    private const string MotionEvidenceDirectory = "res://assets/horse/production/evidence/light-walk-v5-detail/";
    private const string MotionProfilePath = MotionEvidenceDirectory + "walk-motion-profile.json";
    private const string MotionReceiptPath = MotionEvidenceDirectory + "walk-motion-receipt.json";
    private const string NeutralMotionAuditPath = MotionEvidenceDirectory + "neutral-motion-audit.json";
    private const float DefaultRouteSpeed = 0.90f;
    private static readonly FieldInfo CatalogSingletonField = typeof(VisualCatalog)
        .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("VisualCatalog singleton field not found");

    private VisualCatalog? _originalCatalog;
    private VisualCatalog? _previewCatalog;
    private HorsePresenter _presenter = null!;
    private Node3D _movementRoot = null!;
    private Camera3D _camera = null!;
    private CanvasLayer _uiLayer = null!;
    private Label _caption = null!;
    private string _output = "/tmp/coated-horse-walk-preview";
    private string _currentView = "side";
    private string _currentCoat = "bay";
    private string _markingKey = "";
    private IReadOnlyDictionary<string, int> _currentMorphWeights = new Dictionary<string, int>();
    private float _routeSpeed = DefaultRouteSpeed;
    private bool _routeSpeedOverridden;
    private MotionProfile? _motionProfile;
    private float _routeDistance;
    private float _walkLength;
    private double _elapsed;
    private bool _driveRoot;
    private bool _interactive;
    private readonly List<string> _failures = new();
    private readonly Dictionary<string, object?> _report = new();

    public override void _Ready()
    {
        _output = ReadArg("--out", _output);
        _interactive = OS.GetCmdlineUserArgs().Contains("--interactive");
        try
        {
            _routeSpeedOverridden = TryReadSpeed(out _routeSpeed);
            Directory.CreateDirectory(_output);
            _originalCatalog = VisualCatalog.Instance;
            LoadPreviewCatalog();
            LoadBoundMotionProfile();
            BuildStage();
            BuildControls();
            InitializePresenter();
            if (_interactive)
            {
                _presenter.Play("walk");
                _driveRoot = true;
                return;
            }
            CallDeferred(nameof(RunProof));
        }
        catch (Exception e)
        {
            _failures.Add("startup: " + e.Message);
            GD.PushError("[CoatedHorseWalk] " + e);
            Finish(1);
        }
    }

    public override void _ExitTree() => CatalogSingletonField.SetValue(null, _originalCatalog);

    public override void _Process(double delta)
    {
        if (!_driveRoot || _movementRoot == null) return;
        _elapsed += delta;
        _routeDistance += _routeSpeed * (float)delta;
        var routeLength = Math.Max(0.01f, _walkLength * _routeSpeed * 4.0f);
        _movementRoot.Position = new Vector3(_routeDistance % routeLength, 0f, 0f);
        AimCamera(_currentView);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.Escape) GetTree().Quit();
        else if (key.Keycode == Key.Key1) SelectCoat("bay");
        else if (key.Keycode == Key.Key2) SelectCoat("chestnut");
        else if (key.Keycode == Key.Key3) SelectCoat("cremello");
        else if (key.Keycode == Key.S) SelectView("side");
        else if (key.Keycode == Key.Q) SelectView("quarter");
        else if (key.Keycode == Key.H) SelectView("head");
        else if (key.Keycode == Key.Space) ToggleWalk();
    }

    private void LoadPreviewCatalog()
    {
        if (!ResourceLoader.Exists(PreviewCatalogPath))
            throw new FileNotFoundException("Isolated Light Walk preview catalog is missing", PreviewCatalogPath);
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(PreviewCatalogPath));
        var root = doc.RootElement;
        if (!root.TryGetProperty("previewOnly", out var previewOnly) || previewOnly.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Preview catalog is not marked previewOnly=true");
        var family = root.GetProperty("meshFamilies").GetProperty(FamilyId);
        if (family.GetProperty("scene").GetString() != CandidateScenePath)
            throw new InvalidDataException("Preview catalog does not point to the dedicated Light Walk candidate scene");
        _previewCatalog = VisualCatalog.Load(root.GetRawText());
        CatalogSingletonField.SetValue(null, _previewCatalog);
    }

    private void LoadBoundMotionProfile()
    {
        var detail = _previewCatalog!.MeshFamilies[FamilyId].BodySignedDetail
            ?? throw new InvalidOperationException("The isolated preview catalog has no validated signed-detail binding");
        var derivation = detail.RigOnlyDerivation
            ?? throw new InvalidOperationException("The isolated preview catalog has no accepted rig-only derivation proof");
        if (!FileAccess.FileExists(MotionReceiptPath) || !FileAccess.FileExists(MotionProfilePath) || !FileAccess.FileExists(NeutralMotionAuditPath))
            throw new FileNotFoundException("Hash-bound Light Walk motion profile, receipt, or neutral CPU audit is missing");

        byte[] receiptBytes = FileAccess.GetFileAsBytes(MotionReceiptPath);
        byte[] profileBytes = FileAccess.GetFileAsBytes(MotionProfilePath);
        byte[] auditBytes = FileAccess.GetFileAsBytes(NeutralMotionAuditPath);
        using var receiptDoc = JsonDocument.Parse(receiptBytes);
        var receipt = receiptDoc.RootElement;
        if (receipt.GetProperty("schema").GetString() != "horse.light_walk.motion_receipt.v1" ||
            receipt.GetProperty("candidateBlendSha256").GetString() != derivation.CandidateBlendSha256 ||
            receipt.GetProperty("sourceGlbSha256").GetString() != detail.SourceGlbSha256 ||
            receipt.GetProperty("motionProfilePath").GetString() != MotionProfilePath ||
            receipt.GetProperty("motionProfileSha256").GetString() != Sha256(profileBytes) ||
            receipt.GetProperty("cpuMotionAuditPath").GetString() != NeutralMotionAuditPath ||
            receipt.GetProperty("cpuMotionAuditSha256").GetString() != Sha256(auditBytes) ||
            receipt.GetProperty("neutralPhysicalGatesPassed").ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Light Walk motion receipt hashes, paths, or physical-gate status do not match the current candidate");

        using var profileDoc = JsonDocument.Parse(profileBytes);
        var profile = profileDoc.RootElement;
        if (profile.GetProperty("schema").GetString() != "horse.light_walk.motion_profile.v1" ||
            profile.GetProperty("candidateBlendSha256").GetString() != derivation.CandidateBlendSha256 ||
            profile.GetProperty("sourceGlbSha256").GetString() != detail.SourceGlbSha256)
            throw new InvalidDataException("Light Walk motion profile source hashes do not match the bound candidate");
        int period = profile.GetProperty("periodFrames").GetInt32();
        int fps = profile.GetProperty("fps").GetInt32();
        double speed = profile.GetProperty("forwardSpeedMetersPerSecond").GetDouble();
        double duty = profile.GetProperty("dutyFactor").GetDouble();
        if (period != 29 || fps != 30 || !double.IsFinite(speed) || speed <= 0 || !double.IsFinite(duty) || Math.Abs(duty - 0.60) > 1e-9 ||
            !profile.TryGetProperty("sourceRootInPlace", out var sourceRootInPlace) || sourceRootInPlace.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Motion profile period, frame rate, shared speed, or duty factor is incompatible");

        string[] limbNames = { "fore_l", "hind_r", "fore_r", "hind_l" };
        var expectedPhaseOffsets = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["fore_l"] = 0.0, ["hind_r"] = 0.25, ["fore_r"] = 0.5, ["hind_l"] = 0.75,
        };
        var limbsNode = profile.GetProperty("limbs");
        if (limbsNode.ValueKind != JsonValueKind.Object ||
            !limbsNode.EnumerateObject().Select(p => p.Name).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(limbNames.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Motion profile must identify exactly the four controlled hoof chains");
        var limbs = new List<MotionLimb>();
        var sourceVertices = new HashSet<int>();
        foreach (var name in limbNames)
        {
            var limb = limbsNode.GetProperty(name);
            double stanceStart = limb.GetProperty("stanceStartFrame").GetDouble();
            string boneName = limb.GetProperty("boneName").GetString() ?? "";
            var restHead = ReadVector3(limb.GetProperty("restBoneHead"));
            var pointsNode = limb.GetProperty("solePoints");
            if (!double.IsFinite(stanceStart) || stanceStart < 0 || stanceStart >= period ||
                Math.Abs(stanceStart / period - expectedPhaseOffsets[name]) > 1e-10 || string.IsNullOrWhiteSpace(boneName) ||
                pointsNode.ValueKind != JsonValueKind.Array || pointsNode.GetArrayLength() == 0)
                throw new InvalidDataException($"Motion profile limb {name} has invalid stance, bone, or sole-point data");
            var points = new List<MotionPoint>();
            foreach (var pointNode in pointsNode.EnumerateArray())
            {
                int sourceVertex = pointNode.GetProperty("sourceVertex").GetInt32();
                var position = ReadVector3(pointNode.GetProperty("restPosition"));
                if (sourceVertex < 0 || !sourceVertices.Add(sourceVertex))
                    throw new InvalidDataException("Motion profile sole vertices are negative or duplicated");
                points.Add(new MotionPoint(sourceVertex, position));
            }
            limbs.Add(new MotionLimb(name, stanceStart, boneName, restHead, points.ToArray()));
        }
        if (sourceVertices.Count != 429)
            throw new InvalidDataException($"Expected 429 distinct rigid sole vertices across four hoof groups, got {sourceVertices.Count}");

        ValidateNeutralCpuMotionAudit(auditBytes, derivation.CandidateBlendSha256, period, fps, duty, speed, expectedPhaseOffsets);

        _motionProfile = new MotionProfile(derivation.CandidateBlendSha256, detail.SourceGlbSha256,
            Sha256(profileBytes), Sha256(receiptBytes), period, fps, speed, duty, limbs.ToArray());
        if (!_routeSpeedOverridden) _routeSpeed = (float)speed;
    }

    private static void ValidateNeutralCpuMotionAudit(byte[] auditBytes, string candidateBlendSha256, int period, int fps,
        double duty, double speed, IReadOnlyDictionary<string, double> expectedPhaseOffsets)
    {
        using var auditDoc = JsonDocument.Parse(auditBytes);
        var audit = auditDoc.RootElement;
        if (audit.GetProperty("stage").GetString() != "footplant_neutral_61_phase_audit" ||
            audit.GetProperty("candidate_sha256").GetString() != candidateBlendSha256 ||
            audit.GetProperty("accepted_cpu_neutral").ValueKind != JsonValueKind.True ||
            audit.GetProperty("fps").GetInt32() != fps ||
            Math.Abs(audit.GetProperty("period_frames").GetDouble() - period) > 1e-10 ||
            Math.Abs(audit.GetProperty("duty_factor").GetDouble() - duty) > 1e-10 ||
            Math.Abs(audit.GetProperty("shared_speed_mps").GetDouble() - speed) > 1e-8)
            throw new InvalidDataException("Pinned neutral CPU audit has incompatible provenance, phase parameters, or rejected acceptance state");
        var frames = audit.GetProperty("sampled_frames");
        if (frames.ValueKind != JsonValueKind.Array || frames.GetArrayLength() != 61)
            throw new InvalidDataException("Pinned neutral CPU audit must contain 61 source phase samples");
        for (int i = 0; i < frames.GetArrayLength(); i++)
            if (Math.Abs(frames[i].GetDouble() - period * i / 60.0) > 1e-8)
                throw new InvalidDataException("Pinned neutral CPU audit sample phases do not match the 61 runtime phases");
        var phaseOffsets = audit.GetProperty("phase_offsets_cycles");
        if (phaseOffsets.ValueKind != JsonValueKind.Object || phaseOffsets.EnumerateObject().Count() != expectedPhaseOffsets.Count)
            throw new InvalidDataException("Pinned neutral CPU audit phase-offset map is incomplete");
        foreach (var (limb, expected) in expectedPhaseOffsets)
            if (Math.Abs(phaseOffsets.GetProperty(limb).GetDouble() - expected) > 1e-10)
                throw new InvalidDataException($"Pinned neutral CPU audit phase offset for {limb} does not match the motion profile");
        var gates = audit.GetProperty("metric_gates");
        string[] requiredGates =
        {
            "normalized_weights", "whole_body_stretch_le_1_6", "collar_stretch_le_1_6", "loop_root_relative_le_1mm",
            "stance_slip_le_5mm", "sole_min_z_ge_minus_2mm", "stance_p05_z_le_3mm",
            "swing_middle_clearance_ge_30mm", "endpoint_target_mismatch_le_2mm",
        };
        if (gates.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Pinned neutral CPU audit has no metric-gate results");
        foreach (var gate in requiredGates)
            if (!gates.TryGetProperty(gate, out var passed) || passed.ValueKind != JsonValueKind.True)
                throw new InvalidDataException($"Pinned neutral CPU audit gate '{gate}' is missing or failed");
        foreach (var gate in gates.EnumerateObject())
            if (gate.Value.ValueKind != JsonValueKind.True)
                throw new InvalidDataException($"Pinned neutral CPU audit reports a failed metric gate '{gate.Name}'");
    }

    private static Vector3 ReadVector3(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() != 3)
            throw new InvalidDataException("Expected a three-component Godot-space vector");
        float x = node[0].GetSingle(), y = node[1].GetSingle(), z = node[2].GetSingle();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new InvalidDataException("Motion profile contains a non-finite vector");
        return new Vector3(x, y, z);
    }

    private void BuildStage()
    {
        _movementRoot = new Node3D { Name = "WalkRouteRoot" };
        AddChild(_movementRoot);
        var floor = new MeshInstance3D { Name = "ReferenceFloor", Mesh = new PlaneMesh { Size = new Vector2(20, 8) } };
        floor.Position = Vector3.Zero;
        floor.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.23f, 0.26f), Roughness = 1.0f };
        AddChild(floor);
        for (int x = -4; x <= 12; x++)
        {
            var marker = new MeshInstance3D { Name = $"RouteMarker{x}", Mesh = new BoxMesh { Size = new Vector3(0.015f, 0.006f, 5.0f) } };
            marker.Position = new Vector3(x, 0.003f, 0);
            marker.MaterialOverride = new StandardMaterial3D { AlbedoColor = x % 2 == 0 ? new Color(0.78f, 0.48f, 0.18f) : new Color(0.40f, 0.44f, 0.48f), Roughness = 1.0f };
            AddChild(marker);
        }
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-42, -30, 0), LightEnergy = 1.5f, ShadowEnabled = true });
        AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.60f, 0.69f, 0.76f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.63f, 0.67f, 0.70f),
            AmbientLightEnergy = 0.8f,
        }});
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 4.6f, Current = true };
        AddChild(_camera);
        _caption = new Label { Text = "Light Horse Walk · marked bay" };
        _caption.Position = new Vector2(20, 12);
        _caption.AddThemeColorOverride("font_color", new Color(0.08f, 0.10f, 0.12f));
        _caption.AddThemeFontSizeOverride("font_size", 16);
        _uiLayer = new CanvasLayer { Name = "PreviewHud" };
        AddChild(_uiLayer);
        _uiLayer.AddChild(_caption);
    }

    private void BuildControls()
    {
        var panel = new PanelContainer { Name = "Controls" };
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        panel.OffsetTop = -48;
        _uiLayer.AddChild(panel);
        var row = new HBoxContainer();
        panel.AddChild(row);
        AddButton(row, "Bay", () => SelectCoat("bay"));
        AddButton(row, "Chestnut", () => SelectCoat("chestnut"));
        AddButton(row, "Cremello", () => SelectCoat("cremello"));
        AddButton(row, "Side", () => SelectView("side"));
        AddButton(row, "Quarter View", () => SelectView("quarter"));
        AddButton(row, "Head", () => SelectView("head"));
        AddButton(row, "Play / Pause", ToggleWalk);
    }

    private static void AddButton(HBoxContainer row, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(112, 40) };
        button.Pressed += action;
        row.AddChild(button);
    }

    private void InitializePresenter()
    {
        _presenter = new HorsePresenter { Name = "HorsePresenter" };
        _movementRoot.AddChild(_presenter);
        _markingKey = DiagnosticMarkingKey(30);
        SelectCoat("bay");
        _walkLength = (float)_presenter.Animator!.GetAnimation(_presenter.CurrentAnimation).Length;
        if (!float.IsFinite(_walkLength) || _walkLength <= 0 || !_presenter.IsAnimationAvailable("walk"))
            throw new InvalidOperationException("Candidate imported Walk animation is unavailable or has invalid length");
        _routeDistance = 0;
        AimCamera("side");
        _report["preview"] = "Test-only Meshy Light model using the existing HorsePresenter, current shader, and isolated catalog; no donor substitute.";
        _report["catalogVersion"] = _previewCatalog?.Version;
        _report["candidateScene"] = CandidateScenePath;
        _report["walkAnimationName"] = _presenter.CurrentAnimation;
        _report["walkLengthSeconds"] = _walkLength;
        _report["sharedRootVelocityMetersPerSecond"] = _routeSpeed;
        _report["routeVelocityInterpretation"] = _motionProfile != null && Mathf.IsEqualApprox(_routeSpeed, (float)_motionProfile.ForwardSpeed)
            ? "Scene route speed matches the hash-bound motion profile; pose-derived contact measurements are recorded independently."
            : "Custom scene route speed differs from the hash-bound profile; contact gates apply only at the profile speed.";
        _report["markingRealizationKey"] = _markingKey;
        _report["morphWeights"] = new Dictionary<string, int>(_currentMorphWeights);
        _report["modelScale"] = new[] { _presenter.Model!.Scale.X, _presenter.Model.Scale.Y, _presenter.Model.Scale.Z };
        _report["catalogFamilyScene"] = _previewCatalog!.MeshFamilies[FamilyId].Scene;
        _report["previewSourceHashes"] = new Dictionary<string, string>
        {
            ["candidateGlbSha256"] = Sha256(FileAccess.GetFileAsBytes(CandidateScenePath)),
            ["previewCatalogSha256"] = Sha256(FileAccess.GetFileAsBytes(PreviewCatalogPath)),
            ["previewSceneSha256"] = Sha256(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://scenes/tools/CoatedHorseWalk.tscn"))),
            ["previewScriptSha256"] = Sha256(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://scripts/Tools/CoatedHorseWalk.cs"))),
            ["coatShaderSha256"] = Sha256(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://shaders/horse_coat.gdshader"))),
        };
        _report["bodySignedDetailDisabledReason"] = _previewCatalog.MeshFamilies[FamilyId].BodySignedDetailDisabledReason;
        _report["bodySignedDetail"] = _previewCatalog.MeshFamilies[FamilyId].BodySignedDetail == null
            ? null
            : DetailMetadataEvidence(_previewCatalog.MeshFamilies[FamilyId].BodySignedDetail!);
    }

    private async void RunProof()
    {
        try
        {
            await VerifyDetailAndMeshes();
            await VerifyMotionProfileContacts();
            await VerifyWalkAndRoute();
            await CaptureSet();
            _report["failures"] = _failures;
            await WriteReport();
        }
        catch (Exception e)
        {
            _failures.Add("proof: " + e.Message);
            GD.PushError("[CoatedHorseWalk] " + e);
            _report["fatalError"] = e.ToString();
            _report["failures"] = _failures;
            await WriteReport();
        }
        Finish(_failures.Count == 0 ? 0 : 1);
    }

    private async Task VerifyDetailAndMeshes()
    {
        var detail = _previewCatalog!.MeshFamilies[FamilyId].BodySignedDetail;
        if (detail == null)
            throw new InvalidOperationException("Signed detail is disabled by fail-closed catalog validation: " + _previewCatalog.MeshFamilies[FamilyId].BodySignedDetailDisabledReason);
        if (detail.RigOnlyDerivation == null)
            throw new InvalidOperationException("Preview Body detail did not pass the required rig-only geometry/UV preservation evidence gate");
        if (detail.Strength != 1f || detail.SourceGlbSha256 != Sha256(FileAccess.GetFileAsBytes(CandidateScenePath)))
            throw new InvalidOperationException("Signed detail source hash or runtime strength does not match preview candidate");
        if (!FileAccess.FileExists(detail.Path)) throw new FileNotFoundException("Signed detail PNG missing", detail.Path);
        var pngBytes = FileAccess.GetFileAsBytes(detail.Path);
        string rawPngSha = Sha256(pngBytes);
        if (rawPngSha != detail.TextureSha256) throw new InvalidDataException("Signed detail PNG SHA mismatch");
        var body = FindAll<MeshInstance3D>(_presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body")
            ?? throw new InvalidOperationException("Presented Light model does not have one Body component");
        var mat = body.GetSurfaceOverrideMaterial(0) as ShaderMaterial
            ?? throw new InvalidOperationException("Presented Body has no HorsePresenter shader material");
        var detailEnabled = mat.GetShaderParameter("use_signed_detail").AsBool();
        var strength = mat.GetShaderParameter("signed_detail_strength").AsSingle();
        var importedTexture = mat.GetShaderParameter("signed_detail_map").AsGodotObject() as Texture2D;
        if (!detailEnabled || Mathf.Abs(strength - 1f) > 1e-6f || importedTexture == null)
            throw new InvalidOperationException($"Presented Body signed-detail shader binding is not active at strength 1 (enabled={detailEnabled}, strength={strength})");
        var importedImage = importedTexture.GetImage();
        var importedPixelSha = Sha256(importedImage.GetData());
        if (importedImage.GetWidth() != 2048 || importedImage.GetHeight() != 2048 || importedImage.GetFormat() != Image.Format.Rgb8 ||
            importedPixelSha != detail.PixelSha256)
            throw new InvalidDataException("Imported signed-detail texture pixels are not lossless or do not match catalog evidence");
        _report["signedDetailRuntime"] = new Dictionary<string, object?>
        {
            ["enabledInCatalog"] = true, ["enabledOnPresentedBodyShader"] = detailEnabled,
            ["shaderStrength"] = strength, ["metadataStrength"] = detail.Strength,
            ["sourceGlbSha256"] = detail.SourceGlbSha256, ["rawPngSha256"] = rawPngSha,
            ["importedPixelSha256"] = importedPixelSha, ["expectedPixelSha256"] = detail.PixelSha256,
            ["textureResolution"] = new[] { importedImage.GetWidth(), importedImage.GetHeight() },
            ["textureFormat"] = importedImage.GetFormat().ToString(),
            ["preservationEvidence"] = detail.RigOnlyDerivation == null ? null : new Dictionary<string, object?>
            {
                ["reportPath"] = detail.RigOnlyDerivation.PreservationReportPath,
                ["reportSha256"] = detail.RigOnlyDerivation.PreservationReportSha256,
                ["baselineBlendSha256"] = detail.RigOnlyDerivation.BaselineBlendSha256,
                ["baselineGlbSha256"] = detail.RigOnlyDerivation.BaselineGlbSha256,
                ["candidateBlendSha256"] = detail.RigOnlyDerivation.CandidateBlendSha256,
                ["candidateGlbSha256"] = detail.RigOnlyDerivation.CandidateGlbSha256,
                ["parentBindingSha256"] = detail.RigOnlyDerivation.ParentBindingSha256,
                ["parentHandoffSha256"] = detail.RigOnlyDerivation.ParentHandoffSha256,
            },
        };
        await VerifyComponentMorphAndSkinInventory();
    }

    private async Task VerifyComponentMorphAndSkinInventory()
    {
        var expected = new[] { "Body", "Eyes", "Mane", "Tail" };
        var morphNames = new[] { "body_length", "chest_width", "neck_length", "muscle" };
        var groups = FindAll<MeshInstance3D>(_presenter.Model!).GroupBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var inventory = new List<Dictionary<string, object?>>();
        var skeleton = FindAll<Skeleton3D>(_presenter.Model!).SingleOrDefault()
            ?? throw new InvalidOperationException("Expected one imported Skeleton3D");
        foreach (var component in expected)
        {
            if (!groups.TryGetValue(component, out var meshes) || meshes.Length != 1)
                throw new InvalidOperationException($"Expected one {component} mesh, found {(meshes?.Length ?? 0)}");
            var mesh = meshes[0];
            string[] foundMorphs = morphNames.Where(name => mesh.FindBlendShapeByName(name) >= 0).ToArray();
            if (!foundMorphs.SequenceEqual(morphNames))
                throw new InvalidOperationException($"{component} does not expose all four expected morph channels");
            int[] bindToBone = ResolveSkinBindBones(mesh.Skin, skeleton, component);
            var surfaces = new List<Dictionary<string, object?>>();
            if (mesh.Mesh == null || mesh.Mesh.GetSurfaceCount() == 0)
                throw new InvalidDataException($"{component} has no imported mesh surfaces to validate");
            for (int s = 0; mesh.Mesh != null && s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                var arrays = mesh.Mesh.SurfaceGetArrays(s);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                bool finiteVertices = vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
                bool finiteWeights = weights.All(w => float.IsFinite(w) && w >= -1e-7f);
                int width = vertices.Length > 0 && bones.Length == weights.Length && bones.Length % vertices.Length == 0 ? bones.Length / vertices.Length : 0;
                bool validBones = true;
                var minimumWeightSum = float.PositiveInfinity;
                var maximumWeightSum = float.NegativeInfinity;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    float sum = 0;
                    for (int influence = 0; influence < width; influence++)
                    {
                        int index = vertex * width + influence;
                        sum += weights[index];
                        if (weights[index] > 1e-7f && (bones[index] < 0 || bones[index] >= bindToBone.Length || bindToBone[bones[index]] < 0)) validBones = false;
                    }
                    minimumWeightSum = Math.Min(minimumWeightSum, sum);
                    maximumWeightSum = Math.Max(maximumWeightSum, sum);
                }
                bool weightsNormalized = width > 0 && float.IsFinite(minimumWeightSum) && float.IsFinite(maximumWeightSum) &&
                                         minimumWeightSum >= 0.998f && maximumWeightSum <= 1.002f;
                if (vertices.Length == 0 || !finiteVertices || !finiteWeights || (width != 4 && width != 8) || !validBones || !weightsNormalized)
                    throw new InvalidDataException($"{component} surface {s} has invalid positions/weights/influence indices");
                surfaces.Add(new Dictionary<string, object?>
                {
                    ["surface"] = s, ["vertices"] = vertices.Length,
                    ["boneIndexWidth"] = width, ["weightWidth"] = vertices.Length == 0 ? 0 : weights.Length / vertices.Length,
                    ["positionsFinite"] = finiteVertices, ["weightsFiniteNonnegative"] = finiteWeights,
                    ["activeBoneIndicesInRange"] = validBones, ["weightsNormalized"] = weightsNormalized,
                    ["minimumWeightSum"] = minimumWeightSum, ["maximumWeightSum"] = maximumWeightSum,
                });
            }
            inventory.Add(new Dictionary<string, object?>
            {
                ["component"] = component, ["blendShapes"] = foundMorphs,
                ["surfaceCount"] = mesh.Mesh?.GetSurfaceCount() ?? 0, ["surfaces"] = surfaces,
            });
        }
        if (inventory.Count != expected.Length || inventory.Any(component => ((List<Dictionary<string, object?>>)component["surfaces"]!).Count == 0))
            throw new InvalidDataException("Component skin inventory is empty or incomplete");
        _report["componentsMorphsAndSkin"] = inventory;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task VerifyMotionProfileContacts()
    {
        var profile = _motionProfile ?? throw new InvalidOperationException("Hash-bound Light Walk motion profile was not loaded");
        var animator = _presenter.Animator ?? throw new InvalidOperationException("HorsePresenter did not expose AnimationPlayer");
        var skeleton = FindAll<Skeleton3D>(_presenter.Model!).SingleOrDefault()
            ?? throw new InvalidOperationException("Expected one imported Skeleton3D for contact proof");
        var body = FindAll<MeshInstance3D>(_presenter.Model!).SingleOrDefault(mesh => mesh.Name == "Body")
            ?? throw new InvalidOperationException("Expected one imported Body for contact registration proof");
        if (Math.Abs(_walkLength - (double)profile.PeriodFrames / profile.Fps) > 1e-6)
            throw new InvalidDataException($"Imported Walk length {_walkLength:R}s does not match profile period {profile.PeriodFrames}/{profile.Fps}s");
        if (_currentMorphWeights.Values.Any(weight => weight != 0) || _presenter.Model!.Scale.DistanceTo(Vector3.One) > 1e-6f)
            throw new InvalidDataException("Contact evidence requires neutral descriptor morphs and whole-model scale 1");
        if (!float.IsFinite(skeleton.GlobalTransform.Basis.Determinant()) || Math.Abs(skeleton.GlobalTransform.Basis.Determinant()) < 1e-8f)
            throw new InvalidDataException("Imported Skeleton3D global transform is singular or non-finite");

        int[] bodyBindToBone = ResolveSkinBindBones(body.Skin, skeleton, "Body contact profile");
        var skeletonFromBody = skeleton.GlobalTransform.AffineInverse() * body.GlobalTransform;
        var importedSurfaceData = new List<(Vector3[] Vertices, int[] Bones, float[] Weights, int Width)>();
        for (int surface = 0; body.Mesh != null && surface < body.Mesh.GetSurfaceCount(); surface++)
        {
            var arrays = body.Mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int width = vertices.Length > 0 && bones.Length == weights.Length && bones.Length % vertices.Length == 0 ? bones.Length / vertices.Length : 0;
            if ((width != 4 && width != 8) || vertices.Length == 0)
                throw new InvalidDataException($"Body surface {surface} does not expose a nonempty 4- or 8-influence skin buffer");
            importedSurfaceData.Add((vertices, bones, weights, width));
        }
        if (importedSurfaceData.Count == 0) throw new InvalidDataException("Body has no skin surface for source/body registration");

        var boneIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var registeredPointCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var limb in profile.Limbs)
        {
            int boneIndex = skeleton.FindBone(limb.BoneName);
            if (boneIndex < 0) throw new InvalidDataException($"Profile bone '{limb.BoneName}' was not found in the imported Skeleton3D");
            var importedRestHead = skeleton.GetBoneGlobalRest(boneIndex).Origin;
            if (importedRestHead.DistanceTo(limb.RestBoneHead) > 2e-5f)
                throw new InvalidDataException($"Profile rest head for {limb.Name}/{limb.BoneName} differs from imported Godot skeleton by {importedRestHead.DistanceTo(limb.RestBoneHead):R}m");
            boneIndices.Add(limb.Name, boneIndex);
            int registered = 0;
            foreach (var point in limb.SolePoints)
            {
                bool foundRigidMatch = false;
                foreach (var surface in importedSurfaceData)
                {
                    for (int vertexIndex = 0; vertexIndex < surface.Vertices.Length && !foundRigidMatch; vertexIndex++)
                    {
                        var inSkeletonSpace = skeletonFromBody * surface.Vertices[vertexIndex];
                        if (inSkeletonSpace.DistanceTo(point.RestPosition) > 2e-5f) continue;
                        float expectedMass = 0, otherMass = 0;
                        for (int influence = 0; influence < surface.Width; influence++)
                        {
                            int offset = vertexIndex * surface.Width + influence;
                            int bindIndex = surface.Bones[offset];
                            if (bindIndex < 0 || bindIndex >= bodyBindToBone.Length) continue;
                            if (bodyBindToBone[bindIndex] == boneIndex) expectedMass += surface.Weights[offset];
                            else otherMass += surface.Weights[offset];
                        }
                        foundRigidMatch = expectedMass >= 0.99999f && otherMass <= 1e-6f;
                    }
                    if (foundRigidMatch) break;
                }
                if (!foundRigidMatch)
                    throw new InvalidDataException($"Source sole vertex {point.SourceVertex} for {limb.Name} did not register to an imported Body vertex rigidly owned by {limb.BoneName}");
                registered++;
            }
            registeredPointCounts.Add(limb.Name, registered);
        }

        _driveRoot = false;
        animator.Stop();
        const int phaseCount = 61;
        double cycleDistance = profile.ForwardSpeed * profile.PeriodFrames / profile.Fps;
        var allSamples = new List<Dictionary<string, object?>>();
        var stanceY = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        var stanceRanges = new Dictionary<string, Dictionary<int, List<Vector3>>>(StringComparer.Ordinal);
        var centralSwingY = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        var localFirst = new Dictionary<int, Vector3>();
        var localLast = new Dictionary<int, Vector3>();
        double maxClosure = 0;
        bool allFinite = true;

        foreach (var limb in profile.Limbs)
        {
            stanceY[limb.Name] = new List<float>();
            stanceRanges[limb.Name] = limb.SolePoints.ToDictionary(point => point.SourceVertex, _ => new List<Vector3>());
            centralSwingY[limb.Name] = new List<float>();
        }

        for (int sampleIndex = 0; sampleIndex < phaseCount; sampleIndex++)
        {
            double frame = profile.PeriodFrames * sampleIndex / (double)(phaseCount - 1);
            double animationTime = frame / profile.Fps;
            _presenter.Pose("walk", animationTime);
            _movementRoot.Position = new Vector3((float)(profile.ForwardSpeed * animationTime), 0, 0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var limbSamples = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var limb in profile.Limbs)
            {
                int boneIndex = boneIndices[limb.Name];
                var boneTransform = skeleton.GetBoneGlobalPose(boneIndex) * skeleton.GetBoneGlobalRest(boneIndex).AffineInverse();
                double tau = ((frame - limb.StanceStartFrame) % profile.PeriodFrames + profile.PeriodFrames) % profile.PeriodFrames;
                bool isStance = tau <= profile.DutyFactor * profile.PeriodFrames;
                double swingPhase = isStance ? -1 : (tau - profile.DutyFactor * profile.PeriodFrames) / ((1 - profile.DutyFactor) * profile.PeriodFrames);
                var pointSamples = new List<Dictionary<string, object?>>();
                foreach (var point in limb.SolePoints)
                {
                    Vector3 skeletonPosition = boneTransform * point.RestPosition;
                    Vector3 worldPosition = skeleton.GlobalTransform * skeletonPosition;
                    bool finite = float.IsFinite(skeletonPosition.X) && float.IsFinite(skeletonPosition.Y) && float.IsFinite(skeletonPosition.Z) &&
                                  float.IsFinite(worldPosition.X) && float.IsFinite(worldPosition.Y) && float.IsFinite(worldPosition.Z);
                    allFinite &= finite;
                    pointSamples.Add(new Dictionary<string, object?>
                    {
                        ["sourceVertex"] = point.SourceVertex,
                        ["worldPosition"] = VectorValues(worldPosition),
                    });
                    if (sampleIndex == 0) localFirst[point.SourceVertex] = skeletonPosition;
                    if (sampleIndex == phaseCount - 1)
                    {
                        localLast[point.SourceVertex] = skeletonPosition;
                        if (localFirst.TryGetValue(point.SourceVertex, out var first))
                            maxClosure = Math.Max(maxClosure, skeletonPosition.DistanceTo(first));
                    }

                    // End phase is retained for explicit loop-closure comparison, not counted twice as contact.
                    if (sampleIndex < phaseCount - 1)
                    {
                        if (isStance)
                        {
                            stanceY[limb.Name].Add(worldPosition.Y);
                            float unwrappedX = worldPosition.X + (frame < limb.StanceStartFrame ? (float)cycleDistance : 0f);
                            stanceRanges[limb.Name][point.SourceVertex].Add(new Vector3(unwrappedX, worldPosition.Y, worldPosition.Z));
                        }
                        else if (swingPhase >= 0.20 && swingPhase <= 0.80)
                        {
                            centralSwingY[limb.Name].Add(worldPosition.Y);
                        }
                    }
                }
                limbSamples[limb.Name] = new Dictionary<string, object?>
                {
                    ["stance"] = isStance,
                    ["stancePhaseFrames"] = tau,
                    ["centralSwingPhase"] = swingPhase,
                    ["points"] = pointSamples,
                };
            }
            allSamples.Add(new Dictionary<string, object?>
            {
                ["phaseIndex"] = sampleIndex,
                ["framePhase"] = frame,
                ["animationTimeSeconds"] = animationTime,
                ["sceneRootX"] = _movementRoot.Position.X,
                ["limbs"] = limbSamples,
            });
        }

        var perLimb = new Dictionary<string, object?>(StringComparer.Ordinal);
        var allStanceY = new List<float>();
        bool allPhysicalGates = allFinite && maxClosure <= 0.001;
        foreach (var limb in profile.Limbs)
        {
            var yValues = stanceY[limb.Name];
            var swingValues = centralSwingY[limb.Name];
            if (yValues.Count == 0 || swingValues.Count == 0)
                throw new InvalidDataException($"Contact profile produced no stance or central-swing samples for {limb.Name}");
            float minY = yValues.Min();
            double p05Y = Percentile(yValues, 0.05);
            float maxPointXRange = 0, maxPointZRange = 0;
            foreach (var trace in stanceRanges[limb.Name].Values)
            {
                if (trace.Count == 0) continue;
                maxPointXRange = Math.Max(maxPointXRange, trace.Max(v => v.X) - trace.Min(v => v.X));
                maxPointZRange = Math.Max(maxPointZRange, trace.Max(v => v.Z) - trace.Min(v => v.Z));
            }
            float swingMinimum = swingValues.Min();
            allStanceY.AddRange(yValues);
            bool gates = minY >= -0.002f && p05Y <= 0.003 && maxPointXRange <= 0.005f && maxPointZRange <= 0.005f && swingMinimum >= 0.030f;
            allPhysicalGates &= gates;
            perLimb[limb.Name] = new Dictionary<string, object?>
            {
                ["boneName"] = limb.BoneName, ["boneIndex"] = boneIndices[limb.Name],
                ["restBoneHead"] = VectorValues(limb.RestBoneHead), ["registeredRigidPointCount"] = registeredPointCounts[limb.Name],
                ["stanceStartFrame"] = limb.StanceStartFrame,
                ["stanceMinimumY"] = minY, ["stanceYPercentile05"] = p05Y,
                ["maximumPerPointStanceXRange"] = maxPointXRange, ["maximumPerPointStanceZRange"] = maxPointZRange,
                ["centralSwingMinimumY"] = swingMinimum, ["physicalGatesPassed"] = gates,
            };
        }
        double allP05 = Percentile(allStanceY, 0.05);
        float allMinimumY = allStanceY.Min();
        _report["motionProfileContactRuntime"] = new Dictionary<string, object?>
        {
            ["profilePath"] = MotionProfilePath, ["profileSha256"] = profile.Sha256,
            ["receiptPath"] = MotionReceiptPath, ["receiptSha256"] = profile.ReceiptSha256,
            ["neutralMotionAuditPath"] = NeutralMotionAuditPath,
            ["candidateBlendSha256"] = profile.CandidateBlendSha256, ["sourceGlbSha256"] = profile.SourceGlbSha256,
            ["periodFrames"] = profile.PeriodFrames, ["fps"] = profile.Fps,
            ["sharedSpeedMetersPerSecond"] = profile.ForwardSpeed, ["dutyFactor"] = profile.DutyFactor,
            ["sampleCount"] = phaseCount, ["phaseCoordinate"] = "frame = 29*i/60, i=0..60; endpoint retained only for rig-local closure",
            ["rootMotionContract"] = "Profile is an in-place source clip; only the preview scene root advances at the hash-bound shared speed.",
            ["neutralMorphWeights"] = new Dictionary<string, int>(_currentMorphWeights), ["wholeModelScale"] = VectorValues(_presenter.Model!.Scale),
            ["sourceBodySkeletonRegistration"] = "Every profile rest bone head matched imported global rest within 20 micrometres; every source sole point matched a Body surface vertex rigidly weighted to the declared bone.",
            ["registeredPointCounts"] = registeredPointCounts,
            ["allStanceMinimumY"] = allMinimumY, ["allStanceYPercentile05"] = allP05,
            ["maximumRigLocalFirstLastClosureMeters"] = maxClosure, ["finiteSamples"] = allFinite,
            ["limbs"] = perLimb, ["physicalGatesPassed"] = allPhysicalGates,
            ["samples"] = allSamples,
        };
        _movementRoot.Position = Vector3.Zero;
        _presenter.Pose("walk", 0);
        if (!allPhysicalGates)
            throw new InvalidDataException("Runtime hoof contact gates failed; see motionProfileContactRuntime metrics and sample positions");
    }

    private static double Percentile(List<float> source, double percentile)
    {
        if (source.Count == 0) return double.NaN;
        var values = source.OrderBy(value => value).ToArray();
        int index = Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1);
        return values[index];
    }

    private static float[] VectorValues(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private async Task VerifyWalkAndRoute()
    {
        var animator = _presenter.Animator ?? throw new InvalidOperationException("HorsePresenter did not expose AnimationPlayer");
        var skeleton = FindAll<Skeleton3D>(_presenter.Model!).SingleOrDefault()
            ?? throw new InvalidOperationException("Expected one imported Skeleton3D");
        _walkLength = (float)animator.GetAnimation(_presenter.CurrentAnimation).Length;
        _presenter.Play("walk");
        _driveRoot = true;
        _elapsed = 0;
        _routeDistance = 0;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var timeStart = animator.CurrentAnimationPosition;
        var rootStart = _movementRoot.Position.X;
        var elapsedStart = _elapsed;
        var poseStart = ReadPose(skeleton);
        await ToSignal(GetTree().CreateTimer(0.30), SceneTreeTimer.SignalName.Timeout);
        var timeEnd = animator.CurrentAnimationPosition;
        var rootEnd = _movementRoot.Position.X;
        var elapsedEnd = _elapsed;
        var poseEnd = ReadPose(skeleton);
        double elapsed = elapsedEnd - elapsedStart;
        _driveRoot = false;
        var positionDelta = 0f; var angleDelta = 0f; var scaleDelta = 0f;
        for (int i = 0; i < Math.Min(poseStart.Positions.Length, poseEnd.Positions.Length); i++)
        {
            positionDelta = Math.Max(positionDelta, poseStart.Positions[i].DistanceTo(poseEnd.Positions[i]));
            angleDelta = Math.Max(angleDelta, poseStart.Rotations[i].AngleTo(poseEnd.Rotations[i]));
            scaleDelta = Math.Max(scaleDelta, poseStart.Scales[i].DistanceTo(poseEnd.Scales[i]));
        }
        var rootDelta = rootEnd - rootStart;
        var expectedDelta = _routeSpeed * elapsed;
        bool moving = timeEnd > timeStart && (positionDelta > 1e-5f || angleDelta > 1e-5f || scaleDelta > 1e-5f);
        bool rootCorrect = rootDelta > 0 && Math.Abs(rootDelta - expectedDelta) <= 0.003;
        if (!moving || !rootCorrect || !poseStart.Finite || !poseEnd.Finite)
            throw new InvalidOperationException($"Walk proof failed: time {timeStart:F3}->{timeEnd:F3}, pose delta {positionDelta}/{angleDelta}/{scaleDelta}, finite={poseStart.Finite && poseEnd.Finite}, root {rootDelta:F4}/{expectedDelta:F4}");
        animator.Play(_presenter.CurrentAnimation);
        animator.Seek(Math.Max(0, _walkLength - 0.05), true);
        var wrapStart = animator.CurrentAnimationPosition;
        double wrapObservedAfterSeconds = 0;
        double wrapEnd = wrapStart;
        bool wrapped = false;
        var wrapWallStart = Time.GetTicksMsec();
        for (int i = 0; i < 180; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            wrapEnd = animator.CurrentAnimationPosition;
            wrapObservedAfterSeconds = (Time.GetTicksMsec() - wrapWallStart) / 1000.0;
            if (wrapEnd < wrapStart - 0.01f) { wrapped = true; break; }
            if (wrapObservedAfterSeconds > 2.0) break;
        }
        if (!wrapped) throw new InvalidOperationException($"Imported Walk did not wrap under the presenter's imported looping clip mode within 2 seconds: {wrapStart}->{wrapEnd}");
        _report["playback"] = new Dictionary<string, object?>
        {
            ["currentAnimation"] = _presenter.CurrentAnimation, ["clipLengthSeconds"] = _walkLength,
            ["positionAdvanced"] = timeEnd > timeStart, ["poseChanged"] = moving,
            ["maxBonePositionDeltaMeters"] = positionDelta, ["maxBoneRotationDeltaRadians"] = angleDelta,
            ["maxBoneScaleDelta"] = scaleDelta, ["poseValuesFinite"] = poseStart.Finite && poseEnd.Finite,
            ["rootDeltaMeters"] = rootDelta, ["elapsedSeconds"] = elapsed,
            ["expectedRootDeltaMeters"] = expectedDelta,
            ["rootSpeedMeasuredMetersPerSecond"] = elapsed > 0 ? rootDelta / elapsed : 0,
            ["loopWrappedDuringAnimationPlayerPlayback"] = true,
            ["wrapStartPositionSeconds"] = wrapStart, ["wrapEndPositionSeconds"] = wrapEnd,
            ["wrapObservedWithinSeconds"] = wrapObservedAfterSeconds,
            ["interpretation"] = "Observed imported AnimationPlayer playback and scene route; this does not certify authored seam quality, skin validity, foot contact, or gait quality.",
        };
        _presenter.Pose("walk", 0.0);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CaptureSet()
    {
        var shots = new[]
        {
            ("walk_00_side.png", "bay", 0.02f, "side"),
            ("walk_17_side.png", "bay", _walkLength * 0.17f, "side"),
            ("walk_33_side.png", "bay", _walkLength * 0.33f, "side"),
            ("walk_50_side.png", "bay", _walkLength * 0.50f, "side"),
            ("walk_67_side.png", "bay", _walkLength * 0.67f, "side"),
            ("walk_95_side.png", "bay", _walkLength * 0.95f, "side"),
            ("coat_chestnut_q3.png", "chestnut", _walkLength * 0.50f, "quarter"),
            ("coat_cremello_q3.png", "cremello", _walkLength * 0.50f, "quarter"),
            ("marked_head_q3.png", "bay", _walkLength * 0.50f, "head"),
        };
        var images = new List<Dictionary<string, object?>>();
        foreach (var (filename, coat, time, view) in shots)
        {
            SelectCoat(coat);
            _presenter.Pose("walk", time);
            _movementRoot.Position = new Vector3(_routeSpeed * time, 0, 0);
            SelectView(view);
            _caption.Text = $"Light Horse Walk · {coat} · Walk {time / _walkLength:P0}";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var actual = _presenter.Animator?.CurrentAnimationPosition ?? -1;
            var image = GetViewport().GetTexture().GetImage();
            var path = Path.Combine(_output, filename);
            var saveError = image.SavePng(path);
            var pose = ReadPose(FindAll<Skeleton3D>(_presenter.Model!).Single());
            if (saveError != Error.Ok || image.GetWidth() != 1280 || image.GetHeight() != 720 || !pose.Finite)
                throw new InvalidOperationException($"Capture {filename} invalid: save={saveError}, size={image.GetWidth()}x{image.GetHeight()}, finite={pose.Finite}");
            images.Add(new Dictionary<string, object?>
            {
                ["file"] = filename, ["sha256"] = Sha256(File.ReadAllBytes(path)),
                ["dimensions"] = new[] { image.GetWidth(), image.GetHeight() },
                ["coat"] = coat, ["view"] = view, ["requestedPhaseSeconds"] = time,
                ["morphWeights"] = new Dictionary<string, int>(_currentMorphWeights),
                ["modelScale"] = new[] { _presenter.Model!.Scale.X, _presenter.Model.Scale.Y, _presenter.Model.Scale.Z },
                ["actualAnimationPositionSeconds"] = actual, ["poseFinite"] = pose.Finite,
                ["saveError"] = saveError.ToString(),
            });
        }
        _report["captures"] = images;
        _report["coatCycle"] = new Dictionary<string, object?>
        {
            ["choices"] = new[] { "marked bay", "chestnut", "cremello" },
            ["runtimeControls"] = new[] { "1/2/3 or buttons select coat; S/Q/H or buttons select side/quarter/head; Space toggles Walk; Escape closes" },
            ["markingRealizationKeyStableAcrossChoices"] = true, ["markingRealizationKey"] = _markingKey,
            ["customCremelloRecipe"] = "pheomelanin_full:cream_double",
        };
    }

    private void SelectCoat(string coat)
    {
        string baseFixture = coat == "bay" ? "light_endurance.bay" : "light_racer.chestnut";
        var baseDescriptor = FixtureLibrary.GetAppearanceStudio(baseFixture);
        var coatClass = coat switch { "chestnut" => "chestnut", "cremello" => "cremello", _ => "bay" };
        string[] requiredClasses = coat switch
        {
            "cremello" => new[] { "chestnut", "cremello" },
            _ => new[] { coatClass },
        };
        var (recipe, hair, skin, layers, label) = coat switch
        {
            "chestnut" => ("pheomelanin_full:none", "base_chestnut", "pigmented", new[] { "base_pigment", "individual_white_markings", "shading_modifiers" }, "chestnut"),
            "cremello" => ("pheomelanin_full:cream_double", "double_cream_pale", "pink", new[] { "base_pigment", "cream_double", "individual_white_markings", "shading_modifiers" }, "cremello"),
            _ => ("eumelanin_points_pheomelanin_body:none", "base_bay", "pigmented", new[] { "base_pigment", "individual_white_markings", "shading_modifiers" }, "marked bay"),
        };
        _currentCoat = coat;
        var descriptor = baseDescriptor.WithTestMarkingKey(_markingKey, "TEST ONLY Light Walk " + label) with
        {
            FixtureId = "light_walk_preview." + coat, DisplayLabel = "TEST ONLY Light Walk " + label,
            CoatRecipe = recipe, HairRecipe = hair, SkinRecipe = skin, CoatLayers = layers,
            CoatClassId = coatClass, RequiredCoatClasses = requiredClasses,
            EyeRecipe = coat == "cremello" ? "blue" : "brown",
            MorphWeights = baseDescriptor.MorphWeights.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal),
            TestOnly = true, MarkingRealizationKey = _markingKey,
        };
        _currentMorphWeights = descriptor.MorphWeights;
        var support = _presenter.Present(descriptor, "walk");
        if (!support.Supported || _presenter.Model == null)
            throw new InvalidOperationException($"HorsePresenter refused {label}: {support.Summary}");
        if (_currentMorphWeights.Values.Any(weight => weight != 0) || _presenter.Model.Scale.DistanceTo(Vector3.One) > 1e-6f)
            throw new InvalidOperationException("Neutral preview must use zero descriptor morphs and whole-model scale 1");
        if (_caption != null) _caption.Text = $"Light Horse Walk · {label}";
        AssertPresentedDetailActive();
        _presenter.Play("walk");
        if (_interactive) _driveRoot = true;
        if (_presenter.CurrentAnimation.Length == 0)
            throw new InvalidOperationException($"HorsePresenter could not start Walk after {label} coat selection");
    }

    private void SelectView(string view)
    {
        _currentView = view;
        AimCamera(view);
    }

    private void ToggleWalk()
    {
        if (_presenter.Animator?.IsPlaying() == true)
        {
            _presenter.Animator.Pause();
            _driveRoot = false;
        }
        else
        {
            _presenter.Play("walk");
            _driveRoot = true;
        }
    }

    private void AssertPresentedDetailActive()
    {
        var body = _presenter.Model == null ? null : FindAll<MeshInstance3D>(_presenter.Model).SingleOrDefault(mesh => mesh.Name == "Body");
        var material = body?.GetSurfaceOverrideMaterial(0) as ShaderMaterial;
        if (material == null || !material.GetShaderParameter("use_signed_detail").AsBool() ||
            Mathf.Abs(material.GetShaderParameter("signed_detail_strength").AsSingle() - 1f) > 1e-6f ||
            material.GetShaderParameter("signed_detail_map").AsGodotObject() is not Texture2D)
            throw new InvalidOperationException("HorsePresenter did not keep Light signed detail active at full strength on the selected coat");
    }

    private void AimCamera(string view)
    {
        var head = _presenter?.HeadViewTarget ?? new Vector3(1.0f, 1.65f, 0);
        var route = _movementRoot?.Position ?? Vector3.Zero;
        Vector3 focus, offset;
        switch (view)
        {
            case "head":
                focus = route + head;
                offset = new Vector3(2.8f, 0.45f, -0.35f);
                _camera.Size = 1.65f;
                break;
            case "quarter":
                focus = route + new Vector3(0.0f, 1.0f, 0);
                offset = new Vector3(3.8f, 2.15f, -4.0f);
                _camera.Size = 4.1f;
                break;
            default:
                focus = route + new Vector3(0.0f, 0.95f, 0);
                offset = new Vector3(0.0f, 2.2f, -6.0f);
                _camera.Size = 4.6f;
                break;
        }
        _camera.Position = focus + offset;
        _camera.LookAt(focus, Vector3.Up);
    }

    private static int[] ResolveSkinBindBones(Skin? skin, Skeleton3D skeleton, string component)
    {
        if (skin == null || skin.GetBindCount() == 0)
            throw new InvalidDataException($"{component} has no imported Skin bind table");
        var map = new int[skin.GetBindCount()];
        for (int bindIndex = 0; bindIndex < map.Length; bindIndex++)
        {
            string bindName = skin.GetBindName(bindIndex).ToString();
            int bindBone = skin.GetBindBone(bindIndex);
            int namedBone = string.IsNullOrWhiteSpace(bindName) ? -1 : skeleton.FindBone(bindName);
            if (!string.IsNullOrWhiteSpace(bindName) && namedBone < 0)
                throw new InvalidDataException($"{component} Skin bind {bindIndex} names unknown Skeleton3D bone '{bindName}'");
            if (namedBone >= 0 && bindBone >= 0 && namedBone != bindBone)
                throw new InvalidDataException($"{component} Skin bind {bindIndex} name/index disagree ({bindName} => {namedBone}, index => {bindBone})");
            int resolvedBone = namedBone >= 0 ? namedBone : bindBone;
            if (resolvedBone < 0 || resolvedBone >= skeleton.GetBoneCount())
                throw new InvalidDataException($"{component} Skin bind {bindIndex} does not resolve to a valid Skeleton3D bone");
            var inverseBind = skin.GetBindPose(bindIndex);
            var rest = skeleton.GetBoneGlobalRest(resolvedBone);
            float alignmentError = TransformIdentityError(inverseBind * rest);
            if (!float.IsFinite(alignmentError) || alignmentError > 2e-5f)
                throw new InvalidDataException($"{component} Skin bind {bindIndex}/{bindName} is not aligned to the imported bone rest (inverse-bind error {alignmentError:R})");
            map[bindIndex] = resolvedBone;
        }
        return map;
    }

    private static float TransformIdentityError(Transform3D transform)
    {
        var identity = Transform3D.Identity;
        return Math.Max(transform.Origin.Length(), Math.Max(
            (transform.Basis.X - identity.Basis.X).Length(), Math.Max(
            (transform.Basis.Y - identity.Basis.Y).Length(), (transform.Basis.Z - identity.Basis.Z).Length())));
    }

    private static (Vector3[] Positions, Quaternion[] Rotations, Vector3[] Scales, bool Finite) ReadPose(Skeleton3D skeleton)
    {
        var p = new Vector3[skeleton.GetBoneCount()];
        var q = new Quaternion[p.Length];
        var s = new Vector3[p.Length];
        bool finite = true;
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = skeleton.GetBonePosePosition(i);
            q[i] = skeleton.GetBonePoseRotation(i);
            s[i] = skeleton.GetBonePoseScale(i);
            finite &= float.IsFinite(p[i].X) && float.IsFinite(p[i].Y) && float.IsFinite(p[i].Z) &&
                      float.IsFinite(q[i].X) && float.IsFinite(q[i].Y) && float.IsFinite(q[i].Z) && float.IsFinite(q[i].W) &&
                      float.IsFinite(s[i].X) && float.IsFinite(s[i].Y) && float.IsFinite(s[i].Z);
        }
        return (p, q, s, finite);
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        if (root is T found) yield return found;
        foreach (var child in root.GetChildren())
            foreach (var nested in FindAll<T>(child)) yield return nested;
    }

    private static string DiagnosticMarkingKey(int faceCode)
    {
        var bytes = new byte[32];
        bytes[0] = (byte)faceCode;
        bytes[1] = 95;
        bytes[3] = 85;
        bytes[9] = 64;
        bytes[10] = 128;
        bytes[11] = 128;
        bytes[12] = 128;
        bytes[13] = 127;
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ReadArg(string name, string fallback)
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name) return Path.GetFullPath(args[i + 1]);
        return fallback;
    }

    private Dictionary<string, object?> DetailMetadataEvidence(BodySignedDetail detail) => new()
    {
        ["path"] = detail.Path, ["encoding"] = detail.Encoding, ["strength"] = detail.Strength,
        ["sourceGlbSha256"] = detail.SourceGlbSha256, ["textureSha256"] = detail.TextureSha256,
        ["handoffPath"] = detail.HandoffPath, ["handoffSha256"] = detail.HandoffSha256,
        ["bindingPath"] = detail.BindingPath, ["bindingSha256"] = detail.BindingSha256,
        ["retainedTriangleCount"] = detail.RetainedTriangleCount,
        ["neutralRecoveredFaceCount"] = detail.NeutralRecoveredFaceCount,
        ["neutralRecoveredPixelCount"] = detail.NeutralRecoveredPixelCount,
        ["rigOnlyDerivation"] = detail.RigOnlyDerivation?.PreservationReportSha256,
    };

    private bool TryReadSpeed(out float speed)
    {
        speed = DefaultRouteSpeed;
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "--forward-speed") continue;
            if (!float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out speed) || !float.IsFinite(speed) || speed <= 0)
                throw new ArgumentException("--forward-speed must be finite and positive, in metres per second");
            return true;
        }
        return false;
    }

    private sealed record MotionPoint(int SourceVertex, Vector3 RestPosition);
    private sealed record MotionLimb(string Name, double StanceStartFrame, string BoneName, Vector3 RestBoneHead, MotionPoint[] SolePoints);
    private sealed record MotionProfile(string CandidateBlendSha256, string SourceGlbSha256, string Sha256, string ReceiptSha256,
        int PeriodFrames, int Fps, double ForwardSpeed, double DutyFactor, MotionLimb[] Limbs);

    private async Task WriteReport()
    {
        _report["godotVersion"] = Engine.GetVersionInfo()["string"].AsString();
        _report["renderer"] = RenderingServer.GetVideoAdapterName() + " / " + RenderingServer.GetVideoAdapterApiVersion();
        _report["failures"] = _failures;
        await File.WriteAllTextAsync(Path.Combine(_output, "coated_horse_walk_report.json"), JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Finish(int code)
    {
        CatalogSingletonField.SetValue(null, _originalCatalog);
        GetTree().Quit(code);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
