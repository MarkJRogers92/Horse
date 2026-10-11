using Godot;

namespace Cloverbrook.Game.World;

public enum LightingMode
{
    /// <summary>Validation: neutral grey cyclorama, white key light, no colour grading.</summary>
    Neutral,
    /// <summary>Presentation: warm late-afternoon sun, pastel sky, soft bloom.</summary>
    Stable,
}

/// <summary>Creates the lights + WorldEnvironment for a lighting mode under one node.</summary>
public static class Lighting
{
    public static Node3D Build(LightingMode mode)
    {
        var root = new Node3D { Name = "Lighting_" + mode };
        var env = new Godot.Environment();
        var sky = new Sky();
        if (mode == LightingMode.Neutral)
        {
            sky.SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.42f, 0.42f, 0.43f), SkyHorizonColor = new Color(0.55f, 0.55f, 0.56f),
                GroundBottomColor = new Color(0.3f, 0.3f, 0.3f), GroundHorizonColor = new Color(0.5f, 0.5f, 0.5f),
                SunAngleMax = 0f,
            };
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.Sky = sky;
            env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            env.AmbientLightEnergy = 0.9f;
            env.TonemapMode = Godot.Environment.ToneMapper.Agx;
            env.TonemapExposure = 1.0f;
            env.SsaoEnabled = true; env.SsaoRadius = 0.6f; env.SsaoIntensity = 1.2f;
            AddSun(root, new Color(1f, 1f, 1f), 1.6f, new Vector3(-50, 35, 0), shadows: true, tunePreviewShadows: true);
            AddSun(root, new Color(1f, 1f, 1f), 0.35f, new Vector3(-20, -140, 0), shadows: false); // fill
        }
        else
        {
            sky.SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.36f, 0.62f, 0.92f), SkyHorizonColor = new Color(0.98f, 0.84f, 0.89f),
                GroundBottomColor = new Color(0.35f, 0.45f, 0.30f), GroundHorizonColor = new Color(0.86f, 0.88f, 0.78f),
                SunAngleMax = 25f, SkyCurve = 0.12f,
            };
            env.BackgroundMode = Godot.Environment.BGMode.Sky;
            env.Sky = sky;
            env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            env.AmbientLightEnergy = 1.0f;
            env.TonemapMode = Godot.Environment.ToneMapper.Agx;
            env.TonemapExposure = 0.95f;
            env.SsaoEnabled = true; env.SsaoRadius = 0.8f; env.SsaoIntensity = 1.4f;
            env.GlowEnabled = true; env.GlowIntensity = 0.35f; env.GlowBloom = 0.08f; env.GlowHdrThreshold = 1.1f;
            env.FogEnabled = true; env.FogLightColor = new Color(0.98f, 0.88f, 0.92f); env.FogDensity = 0.0015f;
            env.FogSkyAffect = 0.0f;
            env.AdjustmentEnabled = true; env.AdjustmentSaturation = 1.25f; env.AdjustmentContrast = 1.10f;
            AddSun(root, new Color(1f, 0.90f, 0.78f), 1.9f, new Vector3(-38, 40, 0), shadows: true);
            AddSun(root, new Color(0.80f, 0.85f, 1f), 0.30f, new Vector3(-25, -150, 0), shadows: false); // sky fill
            AddSun(root, new Color(1f, 0.85f, 0.9f), 0.45f, new Vector3(-12, 200, 0), shadows: false);  // pink rim
        }
        root.AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });
        return root;
    }

    private static void AddSun(Node3D root, Color color, float energy, Vector3 rotationDeg, bool shadows, bool tunePreviewShadows = false)
    {
        var sun = new DirectionalLight3D
        {
            LightColor = color, LightEnergy = energy, ShadowEnabled = shadows,
            RotationDegrees = rotationDeg,
            // Preserve the Stable scene's previous 1.5 blur and 40m range.
            ShadowBlur = tunePreviewShadows ? 1.0f : 1.5f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = tunePreviewShadows ? 30f : 40f,
        };
        if (tunePreviewShadows)
        {
            // The old 40m neutral preview map produced self-shadow bands on
            // smooth meshes. Keep contact shadows and tune only this art-test
            // light; Stable/menu lighting retains Godot's prior bias/split values.
            sun.ShadowBias = 0.15f;
            sun.ShadowNormalBias = 2.5f;
            sun.DirectionalShadowSplit1 = 0.15f;
            sun.DirectionalShadowPancakeSize = 12f;
        }
        root.AddChild(sun);
    }
}
