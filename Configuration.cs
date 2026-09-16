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

    public void Normalize()
    {
        Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 0.4f) : 0.12f;
        SoundLeadSeconds = float.IsFinite(SoundLeadSeconds) ? Math.Clamp(SoundLeadSeconds, 0.3f, 3f) : 1.2f;
        Scale = float.IsFinite(Scale) ? Math.Clamp(Scale, 0.7f, 2f) : 1;
        LookAheadGcds = Math.Clamp(LookAheadGcds, 1, 3);
        if (!float.IsFinite(Offset.X) || !float.IsFinite(Offset.Y)) Offset = new(110, 100);
    }
}
