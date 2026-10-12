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
        var panel = new PanelContainer { Position = new Vector2(48, 150) };
        var card = CozyTheme.Box(CozyTheme.Cream, 32, 20); card.BorderColor = CozyTheme.Accent; card.SetBorderWidthAll(4);
        card.ShadowColor = new Color(0.35f, 0.12f, 0.3f, 0.22f); card.ShadowSize = 16; card.ShadowOffset = new Vector2(0, 6);
        panel.AddThemeStyleboxOverride("panel", card);
        root.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        box.AddChild(CozyTheme.Ribbon("Cloverbrook Stables", 34));
        var greeting = new BunnySpeaker { CustomMinimumSize = new Vector2(300, 0) };
        greeting.Say("Hello! I'm Pip. Your horses are waiting in Manage stable.");
        box.AddChild(greeting);
        AddButton(box, "Manage stable ♥", "res://scenes/stable_day.tscn");
        AddButton(box, "Horse viewer", "res://scenes/viewer.tscn");
        AddButton(box, "Appearance studio", "res://scenes/appearance_demo.tscn");
        AddButton(box, "Visit the stable", "res://scenes/stable.tscn");
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(300, 44) }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);

        // CI smoke test: prove the project boots, links the domain and presents a horse.
        if (OS.GetCmdlineUserArgs().Contains("--smoke-test"))
            _ = RunWalkVariantSmokeTest(stage);
    }

    private static void AddButton(Container box, string text, string scene)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(300, 46) };
        b.Pressed += () => b.GetTree().ChangeSceneToFile(scene);
        box.AddChild(b);
    }
}
