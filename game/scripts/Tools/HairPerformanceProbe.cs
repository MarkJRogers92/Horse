using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cloverbrook.Game.Scenes;
using Cloverbrook.Game.Visual;
using Cloverbrook.Game.World;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>
/// Baseline-compatible eight-horse comparison. No HairCards dependency: copy this script and scene
/// to the unchanged checkout and use identical renderer/resolution/arguments. This measures the
/// local machine and rendering backend, not expected performance on a player's Mac.
/// </summary>
public partial class HairPerformanceProbe : Node3D
{
    public override void _Ready() => CallDeferred(nameof(Run));

    private async void Run()
    {
        string output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "/tmp/hair-performance-probe";
        string label = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--label=", StringComparison.Ordinal))?.Split('=', 2)[1] ?? "unspecified";
        Directory.CreateDirectory(output);
        try
        {
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var startup = Stopwatch.StartNew();
            var stage = new HorseStage { Name = "Stage" }; AddChild(stage);
            stage.SetMode(LightingMode.Neutral); stage.SetProcess(false);
            var startupRows = new List<object>(); var horses = new List<HorsePresenter>();
            string[] families = { "horse.standard.adult.v1", "horse.compact.adult.v1", "horse.standard.foal.v1", "horse.compact.foal.v1" };
            for (int i = 0; i < 8; i++)
            {
                var presenter = i == 0 ? stage.Presenter : new HorsePresenter { Name = "Horse" + i };
                if (i != 0) AddChild(presenter);
                string family = families[i % families.Length];
                string animation = i < 4 ? "idle" : "walk";
                var descriptor = FixtureLibrary.Get("bay", family.Contains("foal", StringComparison.Ordinal)) with
                {
                    MeshFamilyId = family,
                    MarkingRealizationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("hair-perf-horse-" + i))).ToLowerInvariant(),
                    TestOnly = true,
                };
                var timer = Stopwatch.StartNew();
                var support = presenter.Present(descriptor, animation);
                if (!support.Supported) throw new InvalidOperationException(support.Summary);
                timer.Stop();
                presenter.Position = new Vector3((i % 4 - 1.5f) * 2.1f, 0, (i / 4 - 0.5f) * 2.4f);
                startupRows.Add(new { family, animation, presentMilliseconds = timer.Elapsed.TotalMilliseconds });
                horses.Add(presenter);
            }
            stage.Camera.SetView(12, 19, 13.8f, new Vector3(0, 0.9f, 0));
            startup.Stop();
            const int warmupFrames = 60, measuredFrames = 120;
            for (int i = 0; i < warmupFrames; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var milliseconds = new List<double>(); var drawCalls = new List<double>(); var primitives = new List<double>();
            var clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalMilliseconds;
            for (int i = 0; i < measuredFrames; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                double now = clock.Elapsed.TotalMilliseconds; milliseconds.Add(now - previous); previous = now;
                drawCalls.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                primitives.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            }
            if (drawCalls.All(v => v <= 0)) throw new InvalidOperationException("No rendered draw calls were measured; headless/dummy render cannot pass this benchmark.");
            double Percentile(double[] values, double p) => values[(int)((values.Length - 1) * p)];
            var sorted = milliseconds.OrderBy(v => v).ToArray();
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty() || image.SavePng(Path.Combine(output, "eight-horses.png")) != Error.Ok)
                throw new InvalidOperationException("Rendered eight-horse screenshot was not saved.");
            int cardNodes = horses.Sum(h => All<MeshInstance3D>(h).Count(m => m.Name == "ManeCards" || m.Name == "TailCards"));
            var report = new
            {
                label, horseCount = horses.Count, uniqueSeeds = 8, generatedHairNodes = cardNodes,
                viewport = GetViewport().GetVisibleRect().Size.ToString(), engine = Engine.GetVersionInfo()["string"].AsString(),
                startupMilliseconds = startup.Elapsed.TotalMilliseconds, startupRows,
                warmupFrames, measuredFrames,
                frameMilliseconds = new { mean = milliseconds.Average(), median = Percentile(sorted, 0.5), p95 = Percentile(sorted, 0.95), maximum = sorted[^1] },
                averageDrawCalls = drawCalls.Average(), averagePrimitives = primitives.Average(),
                comparison = "Run baseline and changed checkout sequentially on the same hardware/backend/resolution. Startup includes first resource loads and eight presentations, but asynchronous shader compilation is absorbed by warmup. Software-renderer timings are comparative only and are not Mac FPS."
            };
            File.WriteAllText(Path.Combine(output, "hair-performance-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[hair-performance] PASS {label}: eight horses, {cardNodes} hair card nodes, median {Percentile(sorted, 0.5):F2} ms; {output}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "FAILED.txt"), error.ToString());
            GD.PushError("[hair-performance] FAILED " + error);
            GetTree().Quit(1);
        }
    }

    private static IEnumerable<T> All<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in All<T>(child)) yield return nested;
        }
    }
}
