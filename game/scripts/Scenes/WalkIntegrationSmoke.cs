using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.Tools;
using Godot;

namespace Cloverbrook.Game.Scenes;

// Exercises the normal main-entry presentation code and the actual viewer controls.
public partial class MainMenu
{
    private async System.Threading.Tasks.Task RunWalkVariantSmokeTest(HorseStage stage)
    {
        try
        {
            Check(stage.Presenter.Support?.Supported == true && stage.Presenter.Model != null, "main scene presented a supported horse");
            string? evidence = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--walk-evidence=", StringComparison.Ordinal))?.Split('=', 2)[1];
            if (evidence != null)
            {
                System.IO.Directory.CreateDirectory(evidence);
                foreach (var child in GetChildren()) if (child is CanvasLayer layer) layer.Visible = false;
            }
            var catalog = VisualCatalog.Instance;
            foreach (string familyId in new[] { "horse.standard.adult.v1", "horse.compact.adult.v1", "horse.standard.foal.v1", "horse.compact.foal.v1" })
            {
                var family = catalog.MeshFamilies[familyId];
                var walk = family.WalkVariant;
                Check(walk != null, familyId + " has checked Walk evidence: " + family.WalkVariantDisabledReason);
                bool foal = family.Age == "foal";
                var original = FixtureLibrary.Get("bay", foal) with { MeshFamilyId = familyId };
                var weights = original.MorphWeights.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                weights["body_length"] = 1200; weights["chest_width"] = -700; weights["height"] = 500;
                original = original with { MorphWeights = weights };
                var neutral = Neutral(original);
                Check(stage.Presenter.Present(original, "idle").Supported && stage.Presenter.IsAnimationAvailable("walk"), familyId + " offers Walk from Idle");
                Check(!stage.Presenter.Present(original, "walk").Supported && stage.Presenter.Model == null, familyId + " refuses direct nonneutral Walk");
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Check(stage.Presenter.Present(neutral, "walk").Supported && stage.Presenter.UsesWalkVariant &&
                          OriginalDescriptorValuesMatch(stage.Presenter, neutral, catalog), familyId + " Walk applies neutral morphs and unit scale");
                    Check(stage.Presenter.Animator?.IsPlaying() == true && stage.Presenter.CurrentAnimation == "Walk" &&
                          stage.Presenter.Animator.GetAnimation("Walk").LoopMode == Animation.LoopModeEnum.Linear, familyId + " plays looping Walk");
                    double speed = walk!.ForwardSpeedMetersPerSecond;
                    double period = (double)walk.PeriodFrames / walk.FramesPerSecond;
                    Check(Math.Abs(stage.Presenter.LocomotionSpeedMetersPerSecond-speed) < 1e-12 &&
                          Math.Abs(stage.Presenter.LocomotionPeriodSeconds-period) < 1e-12, familyId + " uses its exact profile speed and duration");
                    stage.FrameView("q3");
                    await WaitSeconds(0.04);
                    double startTime = stage.Presenter.Animator!.CurrentAnimationPosition;
                    float startX = stage.Presenter.Position.X, startCamera = stage.Camera.Target.X;
                    await WaitSeconds(0.25);
                    double advance = stage.Presenter.Animator.CurrentAnimationPosition-startTime;
                    if (advance < 0) advance += period;
                    float distance = stage.Presenter.Position.X-startX;
                    Check(distance > 0.02 && advance > 0.1 && Math.Abs(distance/advance-speed) < 0.025 &&
                          Math.Abs(stage.Camera.Target.X-startCamera-distance) < 0.001, familyId + " moves once at profile speed with camera follow");
                    stage.Presenter.Pose("walk", 0.4);
                    startX = stage.Presenter.Position.X; startCamera = stage.Camera.Target.X;
                    await WaitSeconds(0.08);
                    Check(stage.Presenter.Animator?.IsPlaying() != true && Math.Abs(stage.Presenter.Position.X-startX) < 1e-6 &&
                          Math.Abs(stage.Camera.Target.X-startCamera) < 1e-6, familyId + " pause stops route and camera");
                    stage.Presenter.Play("walk");
                    await WaitSeconds(0.08);
                    Check(stage.Presenter.Animator?.IsPlaying() == true && stage.Presenter.Position.X > startX, familyId + " resumes Walk");
                    Check(stage.Presenter.Present(original, "idle").Supported && !stage.Presenter.UsesWalkVariant &&
                          OriginalDescriptorValuesMatch(stage.Presenter, original, catalog) && stage.Presenter.CurrentAnimation == "Idle", familyId + " restores original Idle descriptor, all component morphs and height");
                    await WaitSeconds(0.04);
                    Check(Math.Abs(stage.Presenter.Position.X) < 1e-6, familyId + " Idle resets route");
                }
                stage.Presenter.Present(neutral, "walk");
                stage.FrameView("q3");
                float cameraOffset = stage.Camera.Target.X;
                stage._Process(stage.Presenter.LocomotionPeriodSeconds*4 + 0.125);
                Check(Math.Abs(stage.Presenter.Position.X-walk!.ForwardSpeedMetersPerSecond*0.125) < 1e-5 &&
                      Math.Abs(stage.Camera.Target.X-cameraOffset-stage.Presenter.Position.X) < 1e-5, familyId + " four-cycle route stays bounded and camera wraps with actor");
                foreach (string coat in new[] { "bay", "chestnut", "cremello" })
                {
                    var descriptor = Neutral(FixtureLibrary.Get(coat, foal) with { MeshFamilyId = familyId });
                    Check(stage.Presenter.Present(descriptor, "walk").Supported, familyId + " moving " + coat + " coat");
                    var body = FindMesh(stage.Presenter.Model!, "Body")!;
                    var material = (ShaderMaterial)body.GetSurfaceOverrideMaterial(0);
                    bool light = familyId == "horse.standard.adult.v1";
                    Check(material.GetShaderParameter("use_signed_detail").AsBool() == light &&
                          Math.Abs(material.GetShaderParameter("signed_detail_strength").AsSingle()-(light ? 1 : 0)) < 1e-6,
                        familyId + (light ? " retains accepted Light detail" : " uses its own procedural coat without Light atlas"));
                    Check(material.GetShaderParameter("body_color").AsColor().IsEqualApprox(catalog.Coats[descriptor.CoatRecipe].Body), familyId + " binds selected coat recipe");
                    stage.FrameView("q3");
                    await WaitSeconds(0.1);
                    await CaptureWalk(evidence, familyId + "-" + coat + "-moving");
                }
                var marked = FixtureLibrary.MarkingVariety("bay", foal, 200).First(d => Markings.FromKey(d.MarkingRealizationKey).Face == FaceMarking.Star && Markings.FromKey(d.MarkingRealizationKey).LegHeights.Any(h => h > 0.1f));
                stage.Presenter.Present(Neutral(marked with { MeshFamilyId = familyId }), "walk");
                var markingMaterial = (ShaderMaterial)FindMesh(stage.Presenter.Model!, "Body")!.GetSurfaceOverrideMaterial(0);
                Check(markingMaterial.GetShaderParameter("face_type").AsInt32() == (int)FaceMarking.Star &&
                      markingMaterial.GetShaderParameter("leg_white").AsVector4().Length() > 0.1f, familyId + " binds star and leg whites");
                foreach (string view in new[] { "head", "legs" })
                {
                    stage.FrameView(view); await WaitSeconds(0.1); await CaptureWalk(evidence, familyId + "-markings-" + view);
                }
            }
            foreach (var probe in FixtureLibrary.UnsupportedProbes())
                Check(!stage.Presenter.Present(probe, "walk").Supported && !stage.Presenter.IsAnimationAvailable("walk"), probe.FixtureId + " remains refused");
            CheckCatalogFailClosed();
            await CheckViewerControls(stage, evidence);
            GD.Print("[smoke] OK all four family mappings, coats, neutral Walk/Idle, paused route, camera, viewer controls and evidence guards");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"[smoke] FAILED checked Walk integration: {error}");
            GetTree().Quit(1);
        }
    }

    private static VisualDescriptor Neutral(VisualDescriptor descriptor) => descriptor with
    {
        MorphWeights = descriptor.MorphWeights.ToDictionary(pair => pair.Key, _ => 0, StringComparer.Ordinal)
    };

    private async System.Threading.Tasks.Task CaptureWalk(string? folder, string name)
    {
        if (folder == null) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Check(GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, name + ".png")) == Error.Ok, "saved " + name);
    }

    private static void CheckCatalogFailClosed()
    {
        string source = FileAccess.GetFileAsString(VisualCatalog.Path);
        foreach (string family in new[] { "horse.compact.adult.v1", "horse.standard.foal.v1", "horse.compact.foal.v1" })
        {
            foreach (string field in new[] { "scene", "sourceGlbSha256", "candidateBlendSha256", "motionProfilePath", "motionProfileSha256", "receiptPath", "receiptSha256", "cpuMotionAuditPath", "cpuMotionAuditSha256", "fps", "periodFrames", "forwardSpeedMetersPerSecond", "dutyFactor", "bodySignedDetail" })
            {
                var json = JsonNode.Parse(source)!;
                var variant = json["meshFamilies"]![family]!["walkVariant"]!;
                if (field == "bodySignedDetail") variant[field] = json["meshFamilies"]!["horse.standard.adult.v1"]!["walkVariant"]![field]!.DeepClone();
                else if (field is "fps" or "periodFrames" or "forwardSpeedMetersPerSecond" or "dutyFactor") variant[field] = 999;
                else variant[field] = "stale-or-missing";
                var familyResult = VisualCatalog.Load(json.ToJsonString()).MeshFamilies[family];
                Check(familyResult.WalkVariant == null && familyResult.WalkVariantDisabledReason != null, family + " fails closed: " + field);
            }
            var wrongAge = JsonNode.Parse(source)!;
            wrongAge["meshFamilies"]![family]!["age"] = family.Contains("foal", StringComparison.Ordinal) ? "adult" : "foal";
            Check(VisualCatalog.Load(wrongAge.ToJsonString()).MeshFamilies[family].WalkVariant == null, family + " refuses mismatched age");
        }
        var noLightDetail = JsonNode.Parse(source)!;
        noLightDetail["meshFamilies"]!["horse.standard.adult.v1"]!["walkVariant"]!.AsObject().Remove("bodySignedDetail");
        Check(VisualCatalog.Load(noLightDetail.ToJsonString()).MeshFamilies["horse.standard.adult.v1"].WalkVariant == null, "Light still requires accepted signed detail");
    }

    private async System.Threading.Tasks.Task CheckViewerControls(HorseStage oldStage, string? evidence)
    {
        oldStage.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var viewer = new HorseViewer(); AddChild(viewer);
        var stage = viewer.GetNode<HorseStage>("Stage");
        var body = FindControl<OptionButton>(viewer, "BodyFamilySelector")!;
        var animation = FindControl<OptionButton>(viewer, "AnimationSelector")!;
        var coat = FindControl<OptionButton>(viewer, "CoatSelector")!;
        var foal = FindControl<CheckButton>(viewer, "FoalToggle")!;
        foreach (int bodyIndex in new[] { 0, 1 })
        foreach (bool juvenile in new[] { false, true })
        {
            body.Select(bodyIndex); body.EmitSignal(OptionButton.SignalName.ItemSelected, bodyIndex);
            foal.ButtonPressed = juvenile;
            animation.Select(3); animation.EmitSignal(OptionButton.SignalName.ItemSelected, 3);
            await WaitForViewerTransition(stage);
            Check(stage.Presenter.UsesWalkVariant && stage.Presenter.Descriptor!.IsFoal == juvenile &&
                  stage.Presenter.Descriptor.MeshFamilyId.Contains(bodyIndex == 0 ? ".standard." : ".compact.", StringComparison.Ordinal), "viewer selects family Walk");
            coat.Select(0); coat.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            Check(stage.Presenter.UsesWalkVariant && stage.Presenter.Descriptor!.CoatRecipe == FixtureLibrary.Get("chestnut", juvenile).CoatRecipe, "viewer changes coat during Walk");
            animation.Select(0); animation.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            await WaitForViewerTransition(stage);
            var restored = stage.Presenter.Descriptor!;
            Check(!stage.Presenter.UsesWalkVariant && stage.Presenter.CurrentAnimation == "Idle" &&
                  OriginalDescriptorValuesMatch(stage.Presenter, restored, VisualCatalog.Instance) &&
                  restored.MorphWeights.OrderBy(p => p.Key).SequenceEqual(FixtureLibrary.Get("chestnut", juvenile).MorphWeights.OrderBy(p => p.Key)), "viewer Idle restores selected shape");
            animation.Select(3); animation.EmitSignal(OptionButton.SignalName.ItemSelected, 3);
            await WaitForViewerTransition(stage);
            await WaitSeconds(0.1); await CaptureWalk(evidence, "viewer-" + stage.Presenter.Descriptor!.MeshFamilyId);
        }
        coat.Select(coat.ItemCount-1); coat.EmitSignal(OptionButton.SignalName.ItemSelected, coat.ItemCount-1);
        foal.ButtonPressed = !foal.ButtonPressed;
        body.Select(0); body.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
        Check(stage.Presenter.Support?.Supported == false && stage.Presenter.Model == null && animation.IsItemDisabled(3), "viewer family/age switches keep unsupported probe refused");
        viewer.QueueFree();
    }

    private static T? FindControl<T>(Node node, string name) where T : Node
    {
        if (node is T found && node.Name == name) return found;
        foreach (var child in node.GetChildren())
        {
            var result = FindControl<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }
    private async System.Threading.Tasks.Task WaitSeconds(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private async System.Threading.Tasks.Task WaitForViewerTransition(HorseStage stage)
    {
        var result = await TransitionCompletionWait.Wait(this, stage.Presenter);
        GD.Print($"[smoke] viewer transition wait: {result.StopReason}, {result.Frames} frames, " +
                 $"{result.EngineSeconds:F3}s engine / {result.WallSeconds:F3}s wall");
        Check(result.Completed, "viewer transition completed");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        GD.Print("[smoke] OK " + message);
    }

    private static MeshInstance3D? FindMesh(Node root, string name)
    {
        if (root is MeshInstance3D mesh && mesh.Name == name) return mesh;
        foreach (Node child in root.GetChildren())
        {
            var found = FindMesh(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static bool OriginalDescriptorValuesMatch(HorsePresenter presenter, VisualDescriptor original, VisualCatalog catalog)
    {
        if (presenter.Model == null || presenter.Descriptor != original) return false;
        float expectedHeight = 1f + (original.MorphWeights.TryGetValue("height", out int height) ? height : 0) * catalog.HeightScalePerUnit;
        if (Math.Abs(presenter.Model.Scale.X - expectedHeight) > 1e-6f ||
            Math.Abs(presenter.Model.Scale.Y - expectedHeight) > 1e-6f ||
            Math.Abs(presenter.Model.Scale.Z - expectedHeight) > 1e-6f) return false;
        if (!catalog.FamilyMeshBlendShapes.TryGetValue(original.MeshFamilyId, out var meshes)) return false;
        foreach (var (component, channels) in meshes)
        {
            var mesh = FindMesh(presenter.Model, component);
            if (mesh?.Mesh == null) return false;
            foreach (var (morph, shape) in channels)
            {
                int shapeIndex = mesh.FindBlendShapeByName(shape);
                if (shapeIndex < 0) return false;
                float expected = original.MorphWeights.TryGetValue(morph, out int weight)
                    ? Mathf.Clamp(weight / catalog.BodyBlendShapeNormalization, -1f, 1f)
                    : 0f;
                if (Math.Abs(mesh.GetBlendShapeValue(shapeIndex) - expected) > 1e-6f) return false;
            }
        }
        return true;
    }


}
