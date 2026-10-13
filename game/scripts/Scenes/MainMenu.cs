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
        bool smoke = OS.GetCmdlineUserArgs().Contains("--smoke-test");
        HorseStage? stage = null;
        if (smoke)
        {
            // CI smoke test keeps the single-horse stage it was written against.
            stage = new HorseStage { Name = "Stage" };
            AddChild(stage);
            stage.SetMode(LightingMode.Stable);
            stage.Presenter.Present(FixtureLibrary.Get("palomino", false), "idle");
            stage.FrameView("wide");
        }
        else
        {
            // Title backdrop (D-012): the stable yard with sample horses living in it.
            var yard = new Stable.StableYard { Name = "Yard" };
            AddChild(yard);
            yard.SetCameraHome(new Vector3(-2.6f, 3.0f, 10.5f), new Vector3(-1.2f, 0.9f, -1.0f));
            yard.WanderCentre = new Vector3(0.6f, 0, -0.8f); yard.WanderRadius = new Vector2(3.6f, 2.6f);
            yard.SetHorses(new (string, VisualDescriptor?)[]
            {
                ("menu-palomino", FixtureLibrary.Get("palomino", false)),
                ("menu-chestnut", FixtureLibrary.Get("chestnut", false)),
                ("menu-bay-foal", FixtureLibrary.Get("bay", true)),
            });
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--menu-preview=", StringComparison.Ordinal)) is { } preview)
                _ = PreviewAsync(yard, preview["--menu-preview=".Length..]);
        }

        var layer = new CanvasLayer(); AddChild(layer);
        var root = new Control { Theme = CozyTheme.Build() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(root);
        var panel = new PanelContainer();
        panel.AnchorTop = panel.AnchorBottom = 0.5f;
        panel.OffsetLeft = 48; panel.OffsetTop = -250;
        var card = CozyTheme.Box(new Color(CozyTheme.Cream, 0.94f), 32, 22); card.BorderColor = CozyTheme.Accent; card.SetBorderWidthAll(4);
        card.ShadowColor = new Color(0.35f, 0.12f, 0.3f, 0.22f); card.ShadowSize = 16; card.ShadowOffset = new Vector2(0, 6);
        panel.AddThemeStyleboxOverride("panel", card);
        root.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        box.AddChild(CozyTheme.Ribbon("Cloverbrook Stables", 36));
        var tagline = CozyTheme.Title("Breed, raise and gallop your dream horses", 16);
        tagline.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        box.AddChild(tagline);
        var greeting = new BunnySpeaker { CustomMinimumSize = new Vector2(320, 0) };
        greeting.Say("Hello! I'm Pip. Your horses are waiting for you!");
        box.AddChild(greeting);
        var play = new Button { Text = "Play ♥", CustomMinimumSize = new Vector2(320, 62) };
        play.AddThemeFontSizeOverride("font_size", 26);
        play.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/stable_day.tscn");
        box.AddChild(play);
        var extrasTitle = CozyTheme.Title("EXTRAS", 11); extrasTitle.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        box.AddChild(extrasTitle);
        var extras = new HBoxContainer(); extras.AddThemeConstantOverride("separation", 8);
        AddSmallButton(extras, "Horse viewer", "res://scenes/viewer.tscn");
        AddSmallButton(extras, "Coat studio", "res://scenes/appearance_demo.tscn");
        box.AddChild(extras);
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(320, 40) }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);

        // CI smoke test: prove the project boots, links the domain and presents a horse.
        if (stage != null)
            _ = RunWalkVariantSmokeTest(stage);
    }

    /// <summary>--menu-preview=FILE: screenshot the title screen after the yard has settled.</summary>
    private async System.Threading.Tasks.Task PreviewAsync(Stable.StableYard yard, string file)
    {
        while (yard.ElapsedSeconds < 14) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print("[menu-preview] OK " + file);
        GetTree().Quit(0);
    }

    private static void AddSmallButton(Container row, string text, string scene)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(156, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.AddThemeFontSizeOverride("font_size", 15);
        b.Pressed += () => b.GetTree().ChangeSceneToFile(scene);
        row.AddChild(b);
    }

}
