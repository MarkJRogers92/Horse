using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Cloverbrook.Game.Visual;

public sealed record CoatRecipe(string Label, Color Body, Color Points, float PointsAmount, Color Counter, Color? Primitive,
    float Sooty, float Dapple, float Sheen);
public sealed record HairRecipe(Color Color, Color Tip);
public sealed record SkinRecipe(Color Color, float NoseAmount);
public sealed record EyeRecipe(Color Iris, Color Ring);
public sealed record MeshFamily(string Scene, bool Placeholder, string Age, string HairGeometry = "cards", string? Limitation = null, Vector3? HeadViewTarget = null);

/// <summary>Result of checking a descriptor against what this renderer can actually draw.</summary>
public sealed record VisualSupport(bool Supported, IReadOnlyList<string> Problems, IReadOnlyList<string> Limitations)
{
    public string Summary => Supported
        ? (Limitations.Count == 0 ? "Supported" : "Supported with limitations: " + string.Join("; ", Limitations))
        : "UNSUPPORTED: " + string.Join("; ", Problems);
}

/// <summary>
/// The renderer's capability list (data/visual_catalog.json). It maps descriptor recipe
/// strings to artistic parameters. A string that is not listed is reported, never guessed.
/// </summary>
public sealed class VisualCatalog
{
    public const string Path = "res://data/visual_catalog.json";

    public string Version { get; private init; } = "";
    public HashSet<string> ImplementedLayers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, MeshFamily> MeshFamilies { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, CoatRecipe> Coats { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, HairRecipe> Hair { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, SkinRecipe> Skin { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EyeRecipe> Eyes { get; } = new(StringComparer.Ordinal);
    public Color White { get; private set; }
    public Color PinkSkin { get; private set; }
    public Color HoofDark { get; private set; }
    public Color HoofPale { get; private set; }
    public float HeightScalePerUnit { get; private set; }
    public float BodyBlendShapeNormalization { get; private set; }
    public Dictionary<string, string> BodyBlendShapes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Dictionary<string, string>> MeshBlendShapes { get; } = new(StringComparer.Ordinal);
    public List<string> ComponentMorphLimitations { get; } = new();
    public HashSet<string> UnsupportedMorphs { get; } = new(StringComparer.Ordinal);

    private static VisualCatalog? _instance;
    public static VisualCatalog Instance => _instance ??= Load(FileAccess.GetFileAsString(Path));

    public static VisualCatalog Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var cat = new VisualCatalog { Version = r.GetProperty("visualAssetCatalogVersion").GetString()! };
        foreach (var l in r.GetProperty("implementedLayers").EnumerateArray()) cat.ImplementedLayers.Add(l.GetString()!);
        foreach (var m in r.GetProperty("meshFamilies").EnumerateObject())
            cat.MeshFamilies[m.Name] = new MeshFamily(m.Value.GetProperty("scene").GetString()!, m.Value.GetProperty("placeholder").GetBoolean(),
                m.Value.GetProperty("age").GetString()!,
                m.Value.TryGetProperty("hairGeometry", out var hairGeometry) ? hairGeometry.GetString()! : "cards",
                m.Value.TryGetProperty("limitation", out var limitation) ? limitation.GetString() : null,
                m.Value.TryGetProperty("headViewTarget", out var headTarget)
                    ? new Vector3(headTarget[0].GetSingle(), headTarget[1].GetSingle(), headTarget[2].GetSingle()) : null);
        foreach (var c in r.GetProperty("coatRecipes").EnumerateObject())
        {
            var v = c.Value;
            cat.Coats[c.Name] = new CoatRecipe(v.GetProperty("label").GetString()!, Hex(v, "body"), Hex(v, "points"),
                v.GetProperty("pointsAmount").GetSingle(), Hex(v, "counter"),
                v.TryGetProperty("primitive", out _) ? Hex(v, "primitive") : null,
                v.GetProperty("sooty").GetSingle(), v.GetProperty("dapple").GetSingle(), v.GetProperty("sheen").GetSingle());
        }
        foreach (var h in r.GetProperty("hairRecipes").EnumerateObject()) cat.Hair[h.Name] = new HairRecipe(Hex(h.Value, "color"), Hex(h.Value, "tip"));
        foreach (var s in r.GetProperty("skinRecipes").EnumerateObject()) cat.Skin[s.Name] = new SkinRecipe(Hex(s.Value, "color"), s.Value.GetProperty("noseAmount").GetSingle());
        foreach (var e in r.GetProperty("eyeRecipes").EnumerateObject()) cat.Eyes[e.Name] = new EyeRecipe(Hex(e.Value, "iris"), Hex(e.Value, "ring"));
        var w = r.GetProperty("white");
        cat.White = Hex(w, "color"); cat.PinkSkin = Hex(w, "pinkSkin"); cat.HoofDark = Hex(w, "hoofDark"); cat.HoofPale = Hex(w, "hoofPale");
        var morph = r.GetProperty("morphSupport");
        cat.HeightScalePerUnit = morph.GetProperty("height").GetProperty("perUnit").GetSingle();
        cat.BodyBlendShapeNormalization = morph.GetProperty("bodyBlendShapeNormalization").GetSingle();
        if (!float.IsFinite(cat.BodyBlendShapeNormalization) || cat.BodyBlendShapeNormalization <= 0f)
            throw new JsonException("morphSupport.bodyBlendShapeNormalization must be a finite positive number");
        foreach (var shape in morph.GetProperty("bodyBlendShapes").EnumerateObject())
            cat.BodyBlendShapes[shape.Name] = shape.Value.GetString()!;
        cat.MeshBlendShapes["Body"] = cat.BodyBlendShapes;
        if (morph.TryGetProperty("meshBlendShapes", out var meshShapes))
            foreach (var mesh in meshShapes.EnumerateObject())
            {
                var channels = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var shape in mesh.Value.EnumerateObject()) channels[shape.Name] = shape.Value.GetString()!;
                cat.MeshBlendShapes[mesh.Name] = channels;
            }
        if (cat.MeshBlendShapes.TryGetValue("Body", out var declaredBody) &&
            (declaredBody.Count != cat.BodyBlendShapes.Count || cat.BodyBlendShapes.Any(pair =>
                !declaredBody.TryGetValue(pair.Key, out var shape) || !string.Equals(pair.Value, shape, StringComparison.Ordinal))))
            throw new JsonException("morphSupport.bodyBlendShapes must exactly match meshBlendShapes.Body");
        if (morph.TryGetProperty("componentMorphLimitations", out var componentLimits))
            foreach (var item in componentLimits.EnumerateArray()) cat.ComponentMorphLimitations.Add(item.GetString()!);
        foreach (var u in morph.GetProperty("unsupported").EnumerateArray()) cat.UnsupportedMorphs.Add(u.GetString()!);
        return cat;
    }

    private static Color Hex(JsonElement e, string name) => Color.FromHtml(e.GetProperty(name).GetString()!);

    /// <summary>Explicit support check. Problems block rendering; limitations are shown but allowed.</summary>
    public VisualSupport Check(VisualDescriptor d)
    {
        var problems = new List<string>();
        var limits = new List<string>();
        if (d.VisualAssetCatalogVersion != Version)
            problems.Add($"asset catalog '{d.VisualAssetCatalogVersion}' is not installed (renderer has '{Version}')");
        foreach (var layer in d.CoatLayers.Where(l => !ImplementedLayers.Contains(l)))
            problems.Add($"coat layer '{layer}' has no renderer implementation");
        if (!Coats.ContainsKey(d.CoatRecipe)) problems.Add($"coat recipe '{d.CoatRecipe}' is not in the visual catalog");
        if (!Hair.ContainsKey(d.HairRecipe)) problems.Add($"hair recipe '{d.HairRecipe}' is not in the visual catalog");
        if (!Skin.ContainsKey(d.SkinRecipe)) problems.Add($"skin recipe '{d.SkinRecipe}' is not in the visual catalog");
        if (!Eyes.ContainsKey(d.EyeRecipe)) problems.Add($"eye recipe '{d.EyeRecipe}' is not in the visual catalog");
        if (!MeshFamilies.TryGetValue(d.MeshFamilyId, out var family)) problems.Add($"mesh family '{d.MeshFamilyId}' has no asset");
        else
        {
            if (family.Placeholder) limits.Add("TEMPORARY placeholder mesh");
            if (!string.IsNullOrEmpty(family.Limitation)) limits.Add(family.Limitation);
        }
        if (d.GrayAmount != 0 && !ImplementedLayers.Contains("grey_age")) problems.Add("grey progression is not implemented");
        if (d.LpPatternPresent || d.Patn1Coverage != 0) problems.Add("leopard complex / PATN1 is not implemented");
        if (d.PrimitiveMarkingStrength > 0 && Coats.TryGetValue(d.CoatRecipe, out var c) && c.Primitive is null && !d.CoatLayers.Contains("primitive_markings"))
            problems.Add("primitive markings requested without a primitive_markings layer");
        if (!string.IsNullOrEmpty(d.CosmeticLayer)) limits.Add($"cosmetic layer '{d.CosmeticLayer}' ignored (not implemented)");
        var supportedMorphs = MeshBlendShapes.Values.SelectMany(channels => channels.Keys).ToHashSet(StringComparer.Ordinal);
        var unappliedMorphs = d.MorphWeights.Where(kv => kv.Value != 0 &&
            (UnsupportedMorphs.Contains(kv.Key) || (supportedMorphs.Contains(kv.Key) && d.IsFoal)))
            .Select(kv => kv.Key).ToArray();
        if (unappliedMorphs.Length > 0) limits.Add("morphs not applied: " + string.Join(",", unappliedMorphs));
        limits.AddRange(ComponentMorphLimitations);
        if (d.TestOnly) limits.Add("test-only descriptor");
        return new VisualSupport(problems.Count == 0, problems, limits);
    }
}
