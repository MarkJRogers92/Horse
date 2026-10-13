using System;
using System.IO;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>The player's chosen stable name (D-016): a display preference stored beside the save, not save data.</summary>
public static class StableName
{
    public const string Default = "Cloverbrook Stables";

    private static string PathFor(string savePath) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(savePath) ?? ".", "stable.cfg");

    public static bool IsChosen(string savePath) => new ConfigFile().Load(PathFor(savePath)) == Error.Ok;

    public static string Get(string savePath)
    {
        var config = new ConfigFile();
        return config.Load(PathFor(savePath)) == Error.Ok ? (string)config.GetValue("stable", "name", Default) : Default;
    }

    public static string Set(string savePath, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? Default : name.Trim();
        if (name.Length > 28) name = name[..28];
        var config = new ConfigFile(); config.Load(PathFor(savePath));
        config.SetValue("stable", "name", name); config.Save(PathFor(savePath));
        return name;
    }
}
