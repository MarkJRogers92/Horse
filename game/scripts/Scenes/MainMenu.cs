using System.Linq;
using System;
using Cloverbrook.Game.DomainIntegration;
using Cloverbrook.Game.UI;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Scenes;

/// <summary>Title screen: the stable with a slowly orbiting horse behind a small menu.</summary>
public partial class MainMenu : Node
{
    public override void _Ready()
    {
        DomainBridge.LogVersions();
        var stage = new HorseStage { Name = "Stage" };
        AddChild(stage);
        stage.SetMode(LightingMode.Stable);
        stage.Presenter.Present(FixtureLibrary.Get("palomino", false), "idle");
        stage.FrameView("wide");
        stage.Camera.AutoRotate = !OS.GetCmdlineUserArgs().Contains("--smoke-test");

        var layer = new CanvasLayer(); AddChild(layer);
        var root = new Control { Theme = CozyTheme.Build() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(root);
        var panel = new PanelContainer { Position = new Vector2(48, 200) };
        root.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        box.AddChild(CozyTheme.Title("Cloverbrook Stables", 40));
        box.AddChild(CozyTheme.Title("appearance studio · adult shape demo", 15));
        AddButton(box, "Horse viewer", "res://scenes/viewer.tscn");
        AddButton(box, "Appearance studio", "res://scenes/appearance_demo.tscn");
        AddButton(box, "Visit the stable", "res://scenes/stable.tscn");
        AddButton(box, "Manage stable", "res://scenes/stable_day.tscn");
        var quit = new Button { Text = "Quit" }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);

        // CI smoke test: prove the project boots, links the domain and presents a horse.
        if (OS.GetCmdlineUserArgs().Contains("--smoke-test"))
            _ = RunWalkVariantSmokeTest(stage);
    }

    private async System.Threading.Tasks.Task RunWalkVariantSmokeTest(HorseStage stage)
    {
        try
        {
            Check(stage.Presenter.Support?.Supported == true && stage.Presenter.Model != null,
                "main scene presented a supported horse");

            var catalog = VisualCatalog.Instance;
            var light = FixtureLibrary.Get("bay", false);
            var walkFamily = catalog.MeshFamilies["horse.standard.adult.v1"];
            var walkVariant = walkFamily.WalkVariant;
            Check(walkVariant != null && walkFamily.Age == "adult", "checked Light adult Walk variant is available");
            Check(stage.Presenter.IsAnimationAvailable("walk"), "Light Walk selector reports available");

            var neutralWeights = light.MorphWeights.ToDictionary(pair => pair.Key, _ => 0, StringComparer.Ordinal);
            var neutralBay = light with { MorphWeights = neutralWeights };
            var support = stage.Presenter.Present(neutralBay, "walk");
            Check(support.Supported && stage.Presenter.UsesWalkVariant && stage.Presenter.Model != null,
                "neutral Bay uses the checked Walk scene");
            Check(OriginalDescriptorValuesMatch(stage.Presenter, neutralBay, catalog),
                "Walk presentation applies all-zero morph weights at unit scale");
            Check(stage.Presenter.Animator?.IsPlaying() == true &&
                  string.Equals(stage.Presenter.CurrentAnimation, "Walk", StringComparison.OrdinalIgnoreCase),
                "Walk animation is playing");

            double expectedSpeed = walkVariant?.ForwardSpeedMetersPerSecond ?? 0.0;
            Check(double.IsFinite(expectedSpeed) && expectedSpeed > 0 &&
                  Math.Abs(stage.Presenter.LocomotionSpeedMetersPerSecond - expectedSpeed) < 1e-12,
                "scene route uses the pinned profile speed");
            Check(Math.Abs(stage.Presenter.LocomotionPeriodSeconds -
                ((walkVariant == null) ? 0.0 : (double)walkVariant.PeriodFrames / walkVariant.FramesPerSecond)) < 1e-12,
                "scene route uses the pinned profile period");

            var body = FindMesh(stage.Presenter.Model!, "Body");
            var bodyMaterial = body?.GetSurfaceOverrideMaterial(0) as ShaderMaterial;
            Check(bodyMaterial != null && bodyMaterial.GetShaderParameter("use_signed_detail").AsBool() &&
                  Math.Abs(bodyMaterial.GetShaderParameter("signed_detail_strength").AsSingle() - 1f) < 1e-6f,
                "Walk Body signed-detail shader is enabled at strength 1");

            await WaitSeconds(0.05);
            var walkAnimator = stage.Presenter.Animator!;
            double movingStartAnimation = walkAnimator.CurrentAnimationPosition;
            float movingStartX = stage.Presenter.Position.X;
            float movingStartTargetX = stage.Camera.Target.X;
            await WaitSeconds(0.25);
            double animationAdvance = walkAnimator.CurrentAnimationPosition - movingStartAnimation;
            if (animationAdvance < 0) animationAdvance += stage.Presenter.LocomotionPeriodSeconds;
            float movedX = stage.Presenter.Position.X - movingStartX;
            float targetMovedX = stage.Camera.Target.X - movingStartTargetX;
            double observedSpeed = animationAdvance > 0 ? movedX / animationAdvance : 0;
            Check(movedX > 0.05f && animationAdvance > 0.1 && Math.Abs(observedSpeed - expectedSpeed) < 0.02 &&
                  Math.Abs(targetMovedX - movedX) < 0.001f,
                $"actor advances at profile speed and camera follows (observed {observedSpeed:F3} m/s over {animationAdvance:F3}s, expected {expectedSpeed:F3})");

            stage.Presenter.Pose("walk", 0.4);
            float pausedX = stage.Presenter.Position.X;
            float pausedTargetX = stage.Camera.Target.X;
            await WaitSeconds(0.15);
            Check(stage.Presenter.Animator?.IsPlaying() != true &&
                  Math.Abs(stage.Presenter.Position.X - pausedX) < 1e-6f &&
                  Math.Abs(stage.Camera.Target.X - pausedTargetX) < 1e-6f,
                "posing Walk pauses actor route and camera");

            var originalSupport = stage.Presenter.Present(light, "idle");
            Check(originalSupport.Supported && !stage.Presenter.UsesWalkVariant &&
                  ReferenceEquals(stage.Presenter.Descriptor, light) &&
                  stage.Presenter.Animator?.IsPlaying() == true &&
                  string.Equals(stage.Presenter.CurrentAnimation, "Idle", StringComparison.OrdinalIgnoreCase),
                "original Bay Idle scene and descriptor are restored");
            Check(OriginalDescriptorValuesMatch(stage.Presenter, light, catalog),
                "original Bay shape weights and height are restored");
            float idleX = stage.Presenter.Position.X;
            await WaitSeconds(0.1);
            Check(Math.Abs(idleX) < 1e-6f && Math.Abs(stage.Presenter.Position.X - idleX) < 1e-6f,
                "Idle route is stopped at its reset position");

            var foalSupport = stage.Presenter.Present(FixtureLibrary.Get("bay", true), "idle");
            Check(foalSupport.Supported && !stage.Presenter.IsAnimationAvailable("walk"),
                "foal Walk remains unavailable");
            var stock = FixtureLibrary.AppearanceStudio.First(item => item.MeshFamilyId == "horse.compact.adult.v1");
            var stockSupport = stage.Presenter.Present(stock, "idle");
            Check(stockSupport.Supported && !stage.Presenter.IsAnimationAvailable("walk"),
                "Stock Walk remains unavailable");

            var probe = FixtureLibrary.UnsupportedProbes().First();
            var probeSupport = stage.Presenter.Present(probe, "walk");
            Check(!probeSupport.Supported && !stage.Presenter.IsAnimationAvailable("walk"),
                "unsupported probe remains refused");

            GD.Print("[smoke] OK checked Light Walk variant, neutral shape and signed detail");
            GD.Print("[smoke] OK profile-speed actor route, camera follow, and paused-pose behavior");
            GD.Print("[smoke] OK original Bay Idle restoration; foal, Stock, and unsupported probes cannot select Walk");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"[smoke] FAILED checked Walk integration: {error.Message}");
            GetTree().Quit(1);
        }
    }

    private async System.Threading.Tasks.Task WaitSeconds(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
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

    private static void AddButton(Container box, string text, string scene)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 44) };
        b.Pressed += () => b.GetTree().ChangeSceneToFile(scene);
        box.AddChild(b);
    }
}
