using Dalamud.Configuration;
using System.Numerics;

namespace PositionalCue;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled = true;
    public bool CombatOnly = true;
    public bool HideWhenCorrect;
    public bool HideDuringTrueNorth = true;
    public bool SoundEnabled = true;
    public float Volume = 0.12f;
    public float SoundLeadSeconds = 1.2f;
    public float Scale = 1f;
    public Vector2 Offset = new(110, 100);
    public int LookAheadGcds = 3;
    public int Language; // 0 = English, 1 = Russian; existing settings migrate to English.
    public int DisplayMode; // 0 = HUD, 1 = target ring
    public float RingThickness = 5f;
    public float RingPadding = 0.15f;

    public void Normalize()
    {
        Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 0.4f) : 0.12f;
        SoundLeadSeconds = float.IsFinite(SoundLeadSeconds) ? Math.Clamp(SoundLeadSeconds, 0.3f, 3f) : 1.2f;
        Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, 0.7f, 2f) : 1;
        LookAheadGcds = Math.Clamp(LookAheadGcds, 1, 3);
        Language = Math.Clamp(Language, 0, 1);
        DisplayMode = Math.Clamp(DisplayMode, 0, 1);
        RingThickness = float.IsFinite(RingThickness) ? Math.Clamp(RingThickness, 1, 12) : 5;
        RingPadding = float.IsFinite(RingPadding) ? Math.Clamp(RingPadding, 0, 3) : 0.15f;
        if (!float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y)) Offset = new(110, 100);
    }
}
