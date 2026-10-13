using System.Collections.Generic;
using Godot;

namespace Cloverbrook.Game.Audio;

/// <summary>
/// Music, ambience and sound effects (D-014). One node lives under the scene-tree root so music
/// carries across screens. Every Button clicks automatically; toggles get a softer pop.
/// Sounds are original synthesized files from tools/audio/synth.py.
/// </summary>
public partial class GameAudio : Node
{
    private const string SettingsPath = "user://settings.cfg";
    private static GameAudio? _instance;
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private readonly List<AudioStreamPlayer> _sfx = new();
    private AudioStreamPlayer _music = null!, _ambience = null!, _loop = null!;
    private string? _musicName;
    private bool _muted;

    public static bool Muted => _instance?._muted ?? false;

    /// <summary>Create the audio node once and start listening for new buttons.</summary>
    public static GameAudio Ensure(SceneTree tree)
    {
        if (_instance != null && IsInstanceValid(_instance)) return _instance;
        _instance = new GameAudio { Name = "GameAudio", ProcessMode = ProcessModeEnum.Always };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
        _instance.Setup(tree);
        return _instance;
    }

    private void Setup(SceneTree tree)
    {
        var config = new ConfigFile();
        float music = 0.8f, effects = 0.9f;
        if (config.Load(SettingsPath) == Error.Ok)
        {
            _muted = (bool)config.GetValue("audio", "muted", false);
            music = (float)config.GetValue("audio", "music", music);
            effects = (float)config.GetValue("audio", "effects", effects);
            if ((bool)config.GetValue("video", "fullscreen", false)) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
        EnsureBus("Music"); EnsureBus("SFX");
        _music = new AudioStreamPlayer { VolumeDb = -9, Bus = "Music" }; AddChild(_music);
        _ambience = new AudioStreamPlayer { VolumeDb = -16, Bus = "Music" }; AddChild(_ambience);
        _loop = new AudioStreamPlayer { VolumeDb = -8, Bus = "SFX" }; AddChild(_loop);
        for (var i = 0; i < 6; i++) { var p = new AudioStreamPlayer { VolumeDb = -6, Bus = "SFX" }; AddChild(p); _sfx.Add(p); }
        SetBusVolume("Music", music); SetBusVolume("SFX", effects);
        tree.NodeAdded += OnNodeAdded;
        ApplyMute();
    }

    private void OnNodeAdded(Node node)
    {
        if (node is CheckButton or CheckBox) ((BaseButton)node).Toggled += _ => Play("ui_toggle", -4);
        else if (node is Button button) button.Pressed += () => Play("ui_click", -8);
    }

    private AudioStream? Stream(string name, bool loop = false)
    {
        if (_streams.TryGetValue(name, out var cached)) return cached;
        var path = $"res://assets/audio/{name}.ogg";
        var stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = loop;
        _streams[name] = stream;
        return stream;
    }

    /// <summary>A one-shot sound effect.</summary>
    public static void Play(string name, float volumeDb = -6)
    {
        var self = _instance;
        if (self == null || self._muted || !IsInstanceValid(self) || !self.IsInsideTree()) return;
        var stream = self.Stream(name);
        if (stream == null) return;
        foreach (var player in self._sfx)
            if (!player.Playing) { player.Stream = stream; player.VolumeDb = volumeDb; player.Play(); return; }
        self._sfx[0].Stream = stream; self._sfx[0].VolumeDb = volumeDb; self._sfx[0].Play();
    }

    /// <summary>Background music (looping), faded in; calling again with the same track keeps it playing.</summary>
    public static void Music(string name)
    {
        var self = _instance;
        if (self == null || self._musicName == name) return;
        if (!self.IsInsideTree()) { self.CallDeferred(MethodName.StartMusicDeferred, name); return; }
        self._musicName = name;
        self._music.Stream = self.Stream(name, loop: true);
        self._music.VolumeDb = -40;
        self._music.Play();
        self.CreateTween().TweenProperty(self._music, "volume_db", -9f, 2.0);
        self._ambience.Stream = self.Stream("ambience_yard", loop: true);
        if (!self._ambience.Playing) self._ambience.Play();
    }

    private void StartMusicDeferred(string name) => Music(name);

    public override void _ExitTree()
    {
        // Release cached streams so the engine shuts down without leaked resources.
        foreach (var player in GetChildren()) if (player is AudioStreamPlayer p) { p.Stop(); p.Stream = null; }
        _streams.Clear();
        if (_instance == this) _instance = null;
    }

    /// <summary>A looping effect such as hoofbeats; null stops it.</summary>
    public static void Loop(string? name)
    {
        var self = _instance;
        if (self == null) return;
        if (name == null) { self._loop.Stop(); return; }
        if (!self.IsInsideTree()) return;
        self._loop.Stream = self.Stream(name, loop: true);
        self._loop.Play();
    }

    public static void SetMuted(bool muted)
    {
        var self = _instance;
        if (self == null) return;
        self._muted = muted;
        var config = new ConfigFile(); config.Load(SettingsPath);
        config.SetValue("audio", "muted", muted); config.Save(SettingsPath);
        self.ApplyMute();
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, "Master");
    }

    private static void SetBusVolume(string bus, float linear)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
        AudioServer.SetBusMute(index, linear <= 0.001f);
    }

    public static float Volume(string bus) => AudioServer.GetBusIndex(bus) is var i and >= 0 && !AudioServer.IsBusMute(i)
        ? Mathf.DbToLinear(AudioServer.GetBusVolumeDb(i)) : 0f;

    /// <summary>Set and remember a bus volume (0-1): "Music" or "SFX".</summary>
    public static void SetVolume(string bus, float linear)
    {
        SetBusVolume(bus, linear);
        var config = new ConfigFile(); config.Load(SettingsPath);
        config.SetValue("audio", bus == "Music" ? "music" : "effects", linear); config.Save(SettingsPath);
    }

    public static void SetFullscreen(bool on)
    {
        DisplayServer.WindowSetMode(on ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        var config = new ConfigFile(); config.Load(SettingsPath);
        config.SetValue("video", "fullscreen", on); config.Save(SettingsPath);
    }

    private void ApplyMute() => AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), _muted);

    /// <summary>A small round speaker button that toggles sound for the whole game.</summary>
    public static Button MuteButton()
    {
        var button = new Button { CustomMinimumSize = new Vector2(52, 44), TooltipText = "Sound on/off" };
        void Refresh() => button.Text = Muted ? "♪×" : "♪";
        Refresh();
        button.Pressed += () => { SetMuted(!Muted); Refresh(); };
        return button;
    }
}
