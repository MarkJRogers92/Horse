using System;
using Godot;

namespace Cloverbrook.Game.Stable;

/// <summary>
/// D-043: per-yard weather polish. Four existing draw surfaces share a small set of
/// uniforms; wind and ripples run on the GPU, never in per-instance CPU loops.
/// </summary>
public partial class StableYard
{
    private ShaderMaterial? _windGrassMat, _windFlowerMat, _pathWeatherMat;
    private float _groundWetness, _targetWetness;
    private float _windStrength = 0.24f, _targetWind = 0.24f;
    private double _atmosphereClock;
    private float? _atmospherePreviewTime;

    /// <summary>Presentation state for reproducible previews and weather-transition checks.</summary>
    public (float Wetness, float TargetWetness, float Wind, float TargetWind, float Time) AtmosphereState =>
        (_groundWetness, _targetWetness, _windStrength, _targetWind, _atmospherePreviewTime ?? (float)_atmosphereClock);

    private void InitializeAtmosphere(MeshInstance3D path)
    {
        // Materials belong to this yard. Shader resources may be cached, but no material
        // or global shader uniform is shared with a menu, portrait, or another viewport.
        var windShader = GD.Load<Shader>("res://shaders/yard_foliage_wind.gdshader");
        _windGrassMat = new ShaderMaterial { Shader = windShader };
        _windFlowerMat = new ShaderMaterial { Shader = windShader };
        _windFlowerMat.SetShaderParameter("flower_head", true);
        _blades.MaterialOverride = _windGrassMat;
        _flowers.MaterialOverride = _windFlowerMat;
        // Wind extends beyond the static mesh's bounds by at most a few centimetres.
        _blades.ExtraCullMargin = 0.08f;
        _flowers.ExtraCullMargin = 0.08f;
        _pathWeatherMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/yard_path.gdshader") };
        path.MaterialOverride = _pathWeatherMat;
        ApplyAtmosphereWeather();
        UpdateAtmosphereUniforms();
    }

    private void ApplyAtmosphereWeather()
    {
        bool liquidRain = _weather == YardWeather.Rain && _season != 3;
        _targetWetness = liquidRain ? 1f : 0f;
        _targetWind = _weather switch { YardWeather.Rain => 0.72f, YardWeather.Cloudy => 0.44f, _ => 0.24f };
        // The existing winter snow surface must never inherit liquid puddles from autumn.
        if (_season == 3) _groundWetness = 0f;
        _windGrassMat?.SetShaderParameter("season_tint", _bladeMat.AlbedoColor);
        _sandMat.SetShaderParameter("rain_amount", liquidRain ? 1f : 0f);
        UpdateAtmosphereUniforms();
    }

    private void UpdateAtmosphere(double delta)
    {
        if (_windGrassMat == null || !double.IsFinite(delta) || delta <= 0) return;
        if (!_atmospherePreviewTime.HasValue) _atmosphereClock += delta;
        // Exponential easing is independent of frame rate: wetting takes a few seconds,
        // drying noticeably longer. Ripples stop as soon as the actual rain stops.
        float wetRate = _targetWetness > _groundWetness ? 0.24f : 0.065f;
        _groundWetness = Mathf.Lerp(_groundWetness, _targetWetness, 1f - MathF.Exp(-wetRate * (float)delta));
        _windStrength = Mathf.Lerp(_windStrength, _targetWind, 1f - MathF.Exp(-0.6f * (float)delta));
        if (Mathf.Abs(_groundWetness - _targetWetness) < 0.0001f) _groundWetness = _targetWetness;
        UpdateAtmosphereUniforms();
    }

    private void UpdateAtmosphereUniforms()
    {
        if (_windGrassMat == null || _windFlowerMat == null || _pathWeatherMat == null) return;
        float time = _atmospherePreviewTime ?? (float)_atmosphereClock;
        _windGrassMat.SetShaderParameter("atmosphere_time", time);
        _windFlowerMat.SetShaderParameter("atmosphere_time", time);
        _windGrassMat.SetShaderParameter("wind_strength", _windStrength);
        _windFlowerMat.SetShaderParameter("wind_strength", _windStrength);
        _sandMat.SetShaderParameter("atmosphere_time", time);
        _sandMat.SetShaderParameter("wetness", _groundWetness);
        _pathWeatherMat.SetShaderParameter("wetness", _groundWetness);
    }

    /// <summary>
    /// Freeze shader motion at a repeatable time and optionally settle the current weather.
    /// Pass null to resume normal animation. This changes presentation only, never a save.
    /// </summary>
    public void SetAtmospherePreviewTime(float? seconds, bool settleWeather = true)
    {
        if (seconds.HasValue && (!float.IsFinite(seconds.Value) || seconds.Value < 0))
            throw new ArgumentOutOfRangeException(nameof(seconds), "Preview time must be finite and non-negative.");
        _atmospherePreviewTime = seconds;
        if (seconds.HasValue && settleWeather)
        {
            _groundWetness = _targetWetness;
            _windStrength = _targetWind;
        }
        UpdateAtmosphereUniforms();
    }
}
