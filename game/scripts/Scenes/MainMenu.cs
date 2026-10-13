using System.Collections.Generic;
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
        Audio.GameAudio.Ensure(GetTree());
        Audio.GameAudio.Music("music_yard");
        bool smoke = OS.GetCmdlineUserArgs().Contains("--smoke-test");
        if (!smoke) _own = LoadOwnStable();
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
            // Your own saved horses when a stable exists (read-only load), otherwise sample horses.
            if (_own.Horses.Count > 0) yard.SetHorses(_own.Horses, _own.Names);
            else yard.SetHorses(new (string, VisualDescriptor?)[]
            {
                ("menu-palomino", FixtureLibrary.Get("palomino", false)),
                ("menu-chestnut", FixtureLibrary.Get("chestnut", false)),
                ("menu-bay-foal", FixtureLibrary.Get("bay", true)),
            });
            if (_own.Season >= 0) yard.SetSeason(_own.Season);
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
        box.AddChild(CozyTheme.Ribbon(Stable.StableName.Get(ProjectSettings.GlobalizePath("user://stable-management/stable.clvr")), 36));
        var tagline = CozyTheme.Title("Breed, raise and gallop your dream horses", 16);
        tagline.AddThemeColorOverride("font_color", CozyTheme.SoftText);
        box.AddChild(tagline);
        var greeting = new BunnySpeaker { CustomMinimumSize = new Vector2(320, 0) };
        greeting.Say(_own.Names.Count switch
        {
            0 => "Hello! I'm Pip. Your very own stable is ready. Shall we meet your horses?",
            1 => $"Welcome back! {_own.Names.Values.First()} has been waiting for you!",
            _ => $"Welcome back! {string.Join(", ", _own.Names.Values.Take(_own.Names.Count - 1))} and {_own.Names.Values.Last()} are waiting for you!",
        });
        box.AddChild(greeting);
        var play = new Button { Text = _own.Names.Count > 0 ? "Continue ♥" : "New stable ♥", CustomMinimumSize = new Vector2(320, 62) };
        play.AddThemeFontSizeOverride("font_size", 26);
        play.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/stable_day.tscn");
        box.AddChild(play);
        var extrasTitle = CozyTheme.Title("EXTRAS", 11); extrasTitle.AddThemeColorOverride("font_color", CozyTheme.AccentDeep);
        box.AddChild(extrasTitle);
        var extras = new HBoxContainer(); extras.AddThemeConstantOverride("separation", 8);
        AddSmallButton(extras, "Horse viewer", "res://scenes/viewer.tscn");
        AddSmallButton(extras, "Coat studio", "res://scenes/appearance_demo.tscn");
        var settings = new Button { Text = "Settings", CustomMinimumSize = new Vector2(110, 40) };
        settings.AddThemeFontSizeOverride("font_size", 15);
        settings.Pressed += () => _settings.Visible = true;
        extras.AddChild(settings);
        box.AddChild(extras);
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(320, 40) }; quit.Pressed += () => GetTree().Quit(); box.AddChild(quit);
        BuildSettings(root);

        // CI smoke test: prove the project boots, links the domain and presents a horse.
        if (stage != null)
            _ = RunWalkVariantSmokeTest(stage);
    }

    private (List<(string, VisualDescriptor?)> Horses, Dictionary<string, string> Names, int Season) _own = (new(), new(), -1);
    private Control _settings = null!;

    /// <summary>Read the player's saved stable without writing anything (AtomicSaveStore.Load is read-only).</summary>
    private static (List<(string, VisualDescriptor?)>, Dictionary<string, string>, int) LoadOwnStable()
    {
        var horses = new List<(string, VisualDescriptor?)>(); var names = new Dictionary<string, string>();
        try
        {
            var path = ProjectSettings.GlobalizePath("user://stable-management/stable.clvr");
            if (!System.IO.File.Exists(path)) return (horses, names, -1);
            var state = new Cloverbrook.Domain.AtomicSaveStore(path).Load().State;
            foreach (var h in state.Horses.Where(h => h.OwnerId.Equals(state.PlayerOwnerId) && !Cloverbrook.Domain.HorseMarket.IsSold(h)
                         && h.LifeState.Stage != Cloverbrook.Domain.LifeStage.Unborn).OrderBy(h => h.Name, StringComparer.Ordinal).Take(5))
            {
                var resolution = new Cloverbrook.Domain.PhenotypeResolver().ResolveForPresentation(h, DomainBridge.Matrix, VisualCatalog.Instance.Version, "canonical-anatomy.v1");
                var (visual, _) = DomainBridge.FromPresentation(resolution);
                if (visual == null) continue;
                horses.Add((h.HorseId.ToString(), visual)); names[h.HorseId.ToString()] = h.Name;
            }
            return (horses, names, state.Header.CalendarDay / Cloverbrook.Domain.StableCalendar.DaysPerSeason);
        }
        catch (Exception ex)
        {
            GD.PushWarning("[MainMenu] saved stable not shown: " + ex.Message);
            return (new(), new(), -1);
        }
    }

    private void BuildSettings(Control root)
    {
        _settings = CozyTheme.Dialog(root, "Settings", 460, out var body, out var buttons, out _);
        Slider(body, "Music", Audio.GameAudio.Volume("Music"), v => Audio.GameAudio.SetVolume("Music", v));
        Slider(body, "Sound effects", Audio.GameAudio.Volume("SFX"), v => { Audio.GameAudio.SetVolume("SFX", v); Audio.GameAudio.Play("ui_toggle"); });
        var fullscreen = new CheckButton { Text = "Fullscreen", ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen };
        fullscreen.Toggled += Audio.GameAudio.SetFullscreen;
        body.AddChild(fullscreen);
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(130, 46) };
        done.Pressed += () => _settings.Visible = false;
        buttons.AddChild(done);
        CozyTheme.FinishDialog(body, buttons);
    }

    private static void Slider(Container body, string label, float value, Action<float> changed)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12);
        var name = CozyTheme.Title(label, 16); name.CustomMinimumSize = new Vector2(140, 0);
        row.AddChild(name);
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(220, 24) };
        slider.DragEnded += _ => changed((float)slider.Value);
        row.AddChild(slider);
        body.AddChild(row);
    }

    /// <summary>--menu-preview=FILE: screenshot the title screen after the yard has settled.</summary>
    private async System.Threading.Tasks.Task PreviewAsync(Stable.StableYard yard, string file)
    {
        while (yard.ElapsedSeconds < 14) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (OS.GetCmdlineUserArgs().Contains("--menu-preview-settings")) { _settings.Visible = true; await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
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
