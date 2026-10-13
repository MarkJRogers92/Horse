using System;
using System.Security.Cryptography;
using System.Text;
using Godot;

namespace Cloverbrook.Game.Visual;

/// <summary>
/// D-024 individual coat variation (an artistic shading modifier, not genetics). Real horses of one colour class
/// span a range: light copper to blood bay to dark bay, light sorrel to liver chestnut, pale to deep dun. Each horse's
/// place in that range, and how sooty or dappled it is, comes deterministically from its marking realisation key,
/// so the same horse always looks the same and portraits stay cached by descriptor.
/// </summary>
public readonly record struct CoatLook(Color Body, Color Counter, Color Primitive, float Sooty, float Dapple, float Shade)
{
    public static CoatLook For(CoatRecipe coat, string? realizationKey)
    {
        var r = Rolls(realizationKey ?? coat.Label);
        // Shades cluster around the classic middle tone; the extremes are rarer.
        var shade = Mathf.Clamp(0.5f + (r.A + r.B - 1f) * 0.75f, 0f, 1f);
        var body = coat.Body;
        if (coat.BodyRange is { } range)
            body = shade < 0.5f ? range.Light.Lerp(coat.Body, shade * 2f) : coat.Body.Lerp(range.Dark, (shade - 0.5f) * 2f);
        // Keep the countershade and primitive markings in step with the chosen shade.
        var delta = new Color(body.R - coat.Body.R, body.G - coat.Body.G, body.B - coat.Body.B);
        var counter = new Color(Mathf.Clamp(coat.Counter.R + delta.R, 0, 1), Mathf.Clamp(coat.Counter.G + delta.G, 0, 1), Mathf.Clamp(coat.Counter.B + delta.B, 0, 1));
        var primitive = (coat.Primitive ?? coat.Points).Darkened(Mathf.Max(0f, shade - 0.5f) * 0.4f);
        float Pick((float Min, float Max)? range, float fallback, float roll) => range is { } x ? Mathf.Lerp(x.Min, x.Max, roll) : fallback;
        // Darker horses tend to carry more sooty shading; pale ones dapple more readily.
        var sooty = Pick(coat.SootyRange, coat.Sooty, Mathf.Clamp(r.C * 0.7f + shade * 0.3f, 0f, 1f));
        var dapple = Pick(coat.DappleRange, coat.Dapple, Mathf.Clamp(r.D * 0.8f + (1f - shade) * 0.2f, 0f, 1f));
        return new CoatLook(body, counter, primitive, sooty, dapple, shade);
    }

    /// <summary>D-026 individual pattern amounts: how much white a tobiano carries (minimal to mostly white, usually
    /// moderate) and how large a leopard-complex blanket is. Deterministic per horse, like the shade.</summary>
    public static (float TobianoExtent, float Blanket) PatternAmounts(string? realizationKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("coat-pattern.v1|" + (realizationKey ?? "")));
        float At(int i) => BitConverter.ToUInt16(hash, i) / 65535f;
        return (Mathf.Clamp(0.2f + (At(0) + At(2)) * 0.32f, 0f, 1f), 0.22f + At(4) * 0.3f);
    }

    private static (float A, float B, float C, float D) Rolls(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("coat-look.v1|" + key));
        float At(int i) => BitConverter.ToUInt16(hash, i) / 65535f;
        return (At(0), At(2), At(4), At(6));
    }
}
