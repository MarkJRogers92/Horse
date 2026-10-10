using System;
using System.Globalization;

namespace Cloverbrook.Game.Visual;

/// <summary>Face marking families drawn in canonical face coordinates.</summary>
public enum FaceMarking { None, Star, StarStrip, Blaze, Snip, StarSnip, Bald }

/// <summary>
/// Individual white markings, realised deterministically from the descriptor's
/// markingRealizationKey. Same key => same markings on every screen, LOD and age.
/// These are labelled ARTISTIC modifiers (no white-spotting genetics are modelled).
/// </summary>
public sealed record Markings(FaceMarking Face, float StarSize, float StripWidth, float BlazeWidth, float SnipSize,
    float FaceOffset, float[] LegHeights, float EdgeSeed)
{
    public const string Version = "markings.render.v1";

    public static Markings FromKey(string hexKey)
    {
        if (hexKey.Length < 32) throw new ArgumentException("marking key too short", nameof(hexKey));
        byte B(int i) => byte.Parse(hexKey.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        float U(int i) => B(i) / 255f;

        int f = B(0) % 100;
        var face = f < 30 ? FaceMarking.None : f < 50 ? FaceMarking.Star : f < 62 ? FaceMarking.StarStrip
            : f < 80 ? FaceMarking.Blaze : f < 88 ? FaceMarking.Snip : f < 96 ? FaceMarking.StarSnip : FaceMarking.Bald;

        var legs = new float[4];
        for (int i = 0; i < 4; i++)
        {
            int r = B(1 + i) % 100;
            // height in canonical leg units (0 ground .. 1 knee/hock); 0 = no white
            legs[i] = r < 50 ? 0f : r < 64 ? 0.20f : r < 77 ? 0.30f : r < 90 ? 0.55f : 0.92f;
            if (legs[i] > 0) legs[i] += (U(5 + i) - 0.5f) * 0.06f;
        }
        return new Markings(face,
            StarSize: 0.06f + 0.06f * U(9),
            StripWidth: 0.10f + 0.10f * U(10),
            BlazeWidth: 0.28f + 0.2f * U(11),
            SnipSize: 0.05f + 0.05f * U(12),
            FaceOffset: (U(13) - 0.5f) * 0.25f,
            LegHeights: legs,
            EdgeSeed: U(14) * 100f);
    }

    public string Describe()
    {
        string[] names = { "FL", "FR", "HL", "HR" };
        var parts = new System.Collections.Generic.List<string> { "face: " + Face };
        for (int i = 0; i < 4; i++)
            if (LegHeights[i] > 0) parts.Add($"{names[i]} {(LegHeights[i] < 0.25f ? "coronet" : LegHeights[i] < 0.4f ? "pastern" : LegHeights[i] < 0.7f ? "sock" : "stocking")}");
        return string.Join(", ", parts);
    }
}
