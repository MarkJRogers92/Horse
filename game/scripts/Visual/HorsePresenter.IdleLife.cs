using System.Linq;
using Godot;

namespace Cloverbrook.Game.Visual;

public partial class HorsePresenter
{
    private int? _idleLifeSeed;
    private int _idleLifeGeneration;
    private static bool _idleLifeLogged;
    private double? _idleLifeCarry; // rest time already spent toward the next graze, kept across Idle/Walk swaps

    /// <summary>D-030 procedural idle life (grazing, head lifts, ear flicks, tail swish) of the current Idle model, or null.</summary>
    public HorseIdleLife? IdleLife { get; private set; }

    /// <summary>
    /// Opt in to idle life for this presenter (the stable yard does; portraits and captures do not, so they
    /// stay still). It lives only on the Idle model and is re-created after every Idle/Walk bridge.
    /// </summary>
    public void EnableIdleLife(int seed)
    {
        _idleLifeSeed = seed;
        if (IdleLife == null) AttachIdleLife();
    }

    private void AttachIdleLife()
    {
        IdleLife = null;
        if (_idleLifeSeed is not { } seed || Model == null || UsesWalkVariant || Descriptor == null ||
            !string.Equals(CurrentAnimation, "Idle", System.StringComparison.OrdinalIgnoreCase)) return;
        var skeleton = FindAll<Skeleton3D>(Model).FirstOrDefault();
        if (skeleton == null) return;
        var life = new HorseIdleLife { Name = "IdleLife" };
        skeleton.AddChild(life);
        life.Setup(this, skeleton, unchecked(seed * 31 + _idleLifeGeneration++), Descriptor.IsFoal, Descriptor.MeshFamilyId ?? "");
        if (!life.HasNeck) { life.QueueFree(); return; }
        if (_idleLifeCarry is { } carry) life.NextGraze = carry;
        IdleLife = life;
        if (!_idleLifeLogged)
        {
            _idleLifeLogged = true;
            GD.Print($"[idle-life] bones {Descriptor.MeshFamilyId}: {life.DescribeBones()}");
        }
    }
}
