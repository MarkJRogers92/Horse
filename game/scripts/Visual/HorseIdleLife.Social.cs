using System;
using Godot;

namespace Cloverbrook.Game.Visual;

public partial class HorseIdleLife
{
    private double _attentionLeft, _attentionDelay, _attentionClock;
    private float _attentionWeight, _attentionYaw, _attentionLift, _attentionNod;
    public float AttentionWeight => Smooth(_attentionWeight);
    private float AttentionPitch => (-_attentionLift + Mathf.Sin((float)_attentionClock * 4.2f) * _attentionNod) * AttentionWeight;

    /// <summary>A brief bounded glance/nod. Only the Idle modifier owns these offsets; Clear removes them before every bridge.</summary>
    public void Attend(Vector3 localDirection, float liftDegrees, float nodDegrees, double duration, double delay = 0, bool lookAway = false)
    {
        float yaw = Mathf.RadToDeg(Mathf.Atan2(-localDirection.Z, localDirection.X));
        _attentionYaw = Mathf.Clamp(yaw, -24f, 24f) * (lookAway ? -0.65f : 1f);
        _attentionLift = Mathf.Clamp(liftDegrees, -5f, 9f);
        _attentionNod = Mathf.Clamp(nodDegrees, 0f, 4f);
        _attentionLeft = Math.Clamp(duration, 1.3, 4);
        _attentionDelay = Math.Clamp(delay, 0, 1.2);
        _attentionClock = 0;
        _grazeLeft = 0; // head rises before the glance; no stacked graze/look pose
    }

    private void TickAttention(double delta)
    {
        if (_attentionDelay > 0) _attentionDelay -= delta;
        bool active = _attentionLeft > 0 && _attentionDelay <= 0 && _grazeT < 0.12f;
        if (active) { _attentionLeft = Math.Max(0, _attentionLeft - delta); _attentionClock += delta; }
        _attentionWeight = Mathf.MoveToward(_attentionWeight, active ? 1f : 0f, (float)delta * 1.7f);
    }
}
