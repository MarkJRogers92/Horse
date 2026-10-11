using System.Linq;
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
        {
            var ok = stage.Presenter.Support?.Supported == true && stage.Presenter.Model != null;
            GD.Print(ok ? "[smoke] OK main scene presented a supported horse" : "[smoke] FAILED");
            GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(ok ? 0 : 1);
        }
    }

    private static void AddButton(Container box, string text, string scene)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(260, 44) };
        b.Pressed += () => b.GetTree().ChangeSceneToFile(scene);
        box.AddChild(b);
    }
}
