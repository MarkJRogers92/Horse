using System;
using System.Linq;

namespace Cloverbrook.Game.UI;

/// <summary>
/// One disposable grooming activity. No horse, calendar, reward or save state lives here.
/// Motion is distance-sampled, not frame-counted; taps and accessible buttons use the same strokes.
/// </summary>
public sealed class GroomingSession
{
    public const int StrokesPerPatch = 8;
    private const float Step = 0.035f;
    private readonly (float X, float Y)[] _centers;
    private readonly float _radiusX, _radiusY;
    private readonly int[] _strokes;
    private bool _dragging, _cancelled, _saving, _saved;
    private float _lastX, _lastY, _remainder;

    public GroomingSession((float X, float Y)[] centers, float radiusX, float radiusY)
    {
        if (centers.Length != 3 || radiusX <= 0 || radiusY <= 0) throw new ArgumentException("Three bounded grooming patches are required.");
        _centers = centers.ToArray(); _radiusX = radiusX; _radiusY = radiusY;
        _strokes = new int[centers.Length];
    }

    public int CleanPatches => _strokes.Count(s => s >= StrokesPerPatch);
    public int TotalStrokes => _strokes.Sum();
    public bool Complete => CleanPatches == _strokes.Length;
    public bool Saving => _saving;
    private bool AcceptsInput => !_cancelled && !_saving && !_saved;
    public float DirtRemaining(int patch) => 1f - _strokes[patch] / (float)StrokesPerPatch;

    public int PatchAt(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) return -1;
        for (int i = 0; i < _centers.Length; i++)
        {
            float dx = (x - _centers[i].X) / _radiusX, dy = (y - _centers[i].Y) / _radiusY;
            if (dx * dx + dy * dy <= 1f) return i;
        }
        return -1;
    }

    public int Dab(float x, float y) => BrushPatch(PatchAt(x, y));

    public int BrushPatch(int patch)
    {
        if (!AcceptsInput || patch < 0 || patch >= _strokes.Length || _strokes[patch] >= StrokesPerPatch) return -1;
        _strokes[patch]++;
        return patch;
    }

    public int BeginStroke(float x, float y)
    {
        if (!AcceptsInput || !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) { EndStroke(); return -1; }
        _dragging = true; _lastX = x; _lastY = y; _remainder = 0;
        return Dab(x, y);
    }

    public int DragTo(float x, float y)
    {
        if (!_dragging || !AcceptsInput || !float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) { EndStroke(); return -1; }
        float dx = x - _lastX, dy = y - _lastY;
        float length = MathF.Sqrt(dx * dx * 4f + dy * dy); // preview is 2:1
        int changed = -1;
        if (length > 0)
        {
            for (float travelled = Step - _remainder; travelled <= length; travelled += Step)
            {
                float t = travelled / length;
                int result = Dab(_lastX + dx * t, _lastY + dy * t);
                if (result >= 0) changed = result;
            }
            _remainder = (_remainder + length) % Step;
        }
        _lastX = x; _lastY = y;
        return changed;
    }

    public void EndStroke() { _dragging = false; _remainder = 0; }
    public void Cancel() { if (!_saving) _cancelled = true; EndStroke(); }

    /// <summary>Latch before invoking the canonical atomic brush command. Eligibility must be checked afresh.</summary>
    public bool TryBeginFinish(bool eligible)
    {
        if (!AcceptsInput || !Complete || !eligible) return false;
        _saving = true; EndStroke(); return true;
    }

    public void ResolveFinish(bool saved)
    {
        if (!_saving) return;
        _saving = false; _saved = saved;
    }
}
