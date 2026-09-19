using System.Text.Json;

namespace PositionalCue;

public static class MovementPreset
{
    public const string PositionalModule = "BossMod.Autorotation.MiscAI.GoToPositional";
    // Deliberately narrow: do not classify healing, targeting or job rotations as movement-only.
    public static bool IsCompatible(string? json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var modules = doc.RootElement.GetProperty("Modules");
            if (!modules.TryGetProperty(PositionalModule, out var positional)
                || !modules.TryGetProperty("BossMod.Autorotation.MiscAI.NormalMovement", out _)) return false;
            foreach (var module in modules.EnumerateObject())
                if (module.Name is not (PositionalModule or "BossMod.Autorotation.MiscAI.NormalMovement"
                    or "BossMod.Autorotation.MiscAI.StayWithinLeylines" or "BossMod.Autorotation.MiscAI.StayCloseToPartyRole")) return false;
            foreach (var setting in positional.EnumerateArray())
                if (setting.GetProperty("Track").GetString() == "Positional"
                    && setting.GetProperty("Option").GetString() != "Any") return false;
            return true;
        }
        catch { return false; }
    }
}
