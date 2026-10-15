using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cloverbrook.Game.Stable;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// D-043: real weather/material integration checks, plus optional fixed-clock renders.
/// Run YardAtmosphereProbe.tscn -- --out=DIR [--capture]. Headless checks need no GPU.
/// </summary>
public partial class YardAtmosphereProbe : Node
{
    private int _checks;
    public override void _Ready() => CallDeferred(nameof(Run));

    private async void Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string output = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "/tmp/yard-atmosphere";
        Directory.CreateDirectory(output);
        try
        {
            var yard = new StableYard { Wandering = false };
            AddChild(yard);
            yard.SetProcess(false);
            yard.SetSeason(0); yard.SetWeather(YardWeather.Sunny);
            var sand = Material(yard, "paddock_sand.gdshader");
            var path = Material(yard, "yard_path.gdshader");
            var vegetation = All<MultiMeshInstance3D>(yard).ToArray();
            Check(vegetation.Length == 2, "exactly the two existing vegetation draw surfaces");
            Check(vegetation.Sum(m => m.Multimesh.InstanceCount) == 15600, "unchanged grass and flower instance budget");
            Check(vegetation.All(m => m.MaterialOverride is ShaderMaterial sm && sm.Shader.ResourcePath.EndsWith("yard_foliage_wind.gdshader", StringComparison.Ordinal)), "both existing MultiMeshes use GPU wind");
            var materialIds = vegetation.Select(m => m.MaterialOverride.GetInstanceId()).Append(sand.GetInstanceId()).Append(path.GetInstanceId()).ToArray();
            Check(materialIds.Distinct().Count() == 4, "four per-yard materials, no per-instance material copies");
            int nodeCount = All<Node>(yard).Count();
            int particles = All<CpuParticles3D>(yard).Count();
            Check(particles == 2, "no new particle emitter beyond existing rain and seasonal petals");
            Check(yard.AtmosphereState.Wetness == 0, "sunny yard starts dry");

            yard.SetWeather(YardWeather.Rain);
            Check(yard.AtmosphereState.Wetness == 0 && yard.AtmosphereState.TargetWetness == 1, "rain begins without a wetness pop");
            yard._Process(1.0 / 60);
            Check(yard.AtmosphereState.Wetness > 0 && yard.AtmosphereState.Wetness < 0.01f, "first rain frame dampens gradually");
            for (int i = 0; i < 600; i++) yard._Process(1.0 / 60);
            Check(yard.AtmosphereState.Wetness > 0.90f && yard.AtmosphereState.Wetness < 1, "rain settles smoothly over ten seconds");
            float wet = yard.AtmosphereState.Wetness;
            Check(Close((float)sand.GetShaderParameter("wetness"), wet) && Close((float)path.GetShaderParameter("wetness"), wet), "paddock and path receive matching wetness");
            Check((float)sand.GetShaderParameter("rain_amount") == 1, "liquid rain enables ripple input");
            yard.SetWeather(YardWeather.Sunny);
            Check(Close(yard.AtmosphereState.Wetness, wet), "sunshine does not instantly dry the surface");
            Check((float)sand.GetShaderParameter("rain_amount") == 0, "rain ripples stop immediately when weather clears");
            yard._Process(1);
            Check(yard.AtmosphereState.Wetness < wet && yard.AtmosphereState.Wetness > wet * 0.9f, "drying is slower than wetting");

            var seasons = new List<object>();
            for (int season = 0; season < 4; season++)
            {
                yard.SetSeason(season); yard.SetWeather(YardWeather.Rain);
                yard.SetAtmospherePreviewTime(3.25f);
                Check(yard.AtmosphereState.Wetness == (season == 3 ? 0 : 1), "liquid wetness follows season " + season);
                Check((float)sand.GetShaderParameter("rain_amount") == (season == 3 ? 0 : 1), "winter cannot render rain ripples");
                Check(vegetation.Count(m => m.Visible) == (season == 3 ? 0 : season == 2 ? 1 : 2), "seasonal vegetation visibility preserved");
                yard._Process(2);
                Check((float)sand.GetShaderParameter("atmosphere_time") == 3.25f, "preview clock stays frozen despite processing");
                Check(vegetation.All(m => (float)((ShaderMaterial)m.MaterialOverride).GetShaderParameter("atmosphere_time") == 3.25f), "vegetation and ground use the same deterministic clock");
                Check(All<Node>(yard).Count() == nodeCount, "weather/season switch adds no scene nodes");
                Check(All<CpuParticles3D>(yard).Count() == particles, "weather/season switch adds no particle systems");
                Check(materialIds.SequenceEqual(vegetation.Select(m => m.MaterialOverride.GetInstanceId()).Append(sand.GetInstanceId()).Append(path.GetInstanceId())), "weather switch reuses its materials");
                seasons.Add(new { season, wetness = yard.AtmosphereState.Wetness, wind = yard.AtmosphereState.Wind });
            }
            // A second yard must not inherit another viewport's material or weather.
            var other = new StableYard { Wandering = false }; AddChild(other); other.SetProcess(false);
            other.SetSeason(0); other.SetWeather(YardWeather.Sunny);
            Check(Material(other, "paddock_sand.gdshader").GetInstanceId() != sand.GetInstanceId(), "ground materials are isolated between yards");
            Check(Material(other, "yard_path.gdshader").GetInstanceId() != path.GetInstanceId(), "path materials are isolated between yards");
            Check(All<MultiMeshInstance3D>(other).All(m => !materialIds.Contains(m.MaterialOverride.GetInstanceId())), "wind materials are isolated between yards");
            Check(other.AtmosphereState.Wetness == 0, "new yard does not inherit rain");
            other.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            yard.SetSeason(0); yard.SetWeather(YardWeather.Sunny); yard.SetAtmospherePreviewTime(0);
            yard.SetAtmospherePreviewTime(null); yard._Process(0.5);
            float resumed = yard.AtmosphereState.Time;
            yard._Process(0.5);
            Check(yard.AtmosphereState.Time > resumed, "releasing preview resumes the animation clock");
            bool rejected = false;
            try { yard.SetAtmospherePreviewTime(float.NaN); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "preview rejects non-finite time");

            if (args.Contains("--capture")) await CaptureWeather(yard, output);
            File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
            {
                checks = _checks, passed = true, vegetationInstances = 15600, atmosphereMaterials = 4,
                additionalDrawSurfaces = 0, additionalParticles = 0, seasons,
                screenshots = args.Contains("--capture"),
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[yard-atmosphere] OK {_checks} checks; no added draw surfaces/particles; {output}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError("[yard-atmosphere] FAILED " + ex);
            GetTree().Quit(1);
        }
    }

    private async Task CaptureWeather(StableYard yard, string output)
    {
        // Rain and petals have their own pre-existing stochastic clock. Hide them only
        // in this focused probe so the wetness/ripple/wind comparison is repeatable.
        foreach (var (name, season, weather, time) in new[]
        {
            ("01_spring_dry", 0, YardWeather.Sunny, 3.25f), ("02_spring_rain", 0, YardWeather.Rain, 3.25f),
            ("03_spring_ripples", 0, YardWeather.Rain, 3.75f), ("04_summer_rain", 1, YardWeather.Rain, 3.25f),
            ("05_autumn_rain", 2, YardWeather.Rain, 3.25f), ("06_winter_snow", 3, YardWeather.Rain, 3.25f),
        })
        {
            yard.SetSeason(season); yard.SetWeather(weather); yard.SetAtmospherePreviewTime(time);
            foreach (var emitter in All<CpuParticles3D>(yard)) emitter.Visible = false;
            yard.SetCameraHome(new Vector3(0.45f, 3.7f, 11.6f), new Vector3(0.45f, 0.75f, -0.4f));
            await Capture(Path.Combine(output, name + ".png"));
        }
        yard.SetSeason(0); yard.SetWeather(YardWeather.Rain);
        foreach (var emitter in All<CpuParticles3D>(yard)) emitter.Visible = false;
        yard.SetCameraHome(new Vector3(1.5f, 2.3f, 4.8f), new Vector3(0, 0, -0.5f));
        yard.SetAtmospherePreviewTime(3.25f); await Capture(Path.Combine(output, "07_puddles_close.png"));
        yard.SetAtmospherePreviewTime(3.75f); await Capture(Path.Combine(output, "08_puddles_motion.png"));
        yard.SetCameraHome(new Vector3(-9, 0.7f, 4), new Vector3(-8, 0.18f, 0));
        yard.SetWeather(YardWeather.Sunny);
        yard.SetAtmospherePreviewTime(3.25f); await Capture(Path.Combine(output, "09_wind_a.png"));
        yard.SetAtmospherePreviewTime(4.5f); await Capture(Path.Combine(output, "10_wind_b.png"));
    }

    private async Task Capture(string path)
    {
        // Automatic sky radiance updates one roughness layer per frame after a
        // season/weather change. Let every layer settle before comparing pixels.
        int settleFrames = (int)ProjectSettings.GetSetting("rendering/reflections/sky_reflections/roughness_layers", 8) + 2;
        for (int i = 0; i < settleFrames; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check(!image.IsEmpty() && image.SavePng(path) == Error.Ok, "rendered screenshot saved: " + Path.GetFileName(path));
    }

    private static ShaderMaterial Material(Node yard, string filename) => All<MeshInstance3D>(yard)
        .Select(m => m.MaterialOverride).OfType<ShaderMaterial>()
        .Single(m => m.Shader.ResourcePath.EndsWith(filename, StringComparison.Ordinal));
    private static bool Close(float a, float b) => Mathf.Abs(a - b) < 0.00001f;
    private void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<T> All<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in All<T>(child)) yield return nested;
        }
    }
}
