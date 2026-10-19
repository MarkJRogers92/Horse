using System;
using Godot;

namespace Cloverbrook.Game.Tools;

/// <summary>Test-harness shutdown only, after scenes and static caches are released.</summary>
public partial class ProbeResources : RefCounted
{
    public static void CollectForShutdown()
    {
        GD.Print("[probe-resources] collecting managed wrappers before renderer shutdown");
        // Match StableDay's existing teardown order while Godot is still alive.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GD.Print("[probe-resources] managed finalizers completed");
    }
}
