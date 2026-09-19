namespace PositionalCue;

public enum Direction : uint { Rear = 1, Flank = 2 }

// Wire contract: WrathCombo.API/PositionalHintSnapshot.cs (7 uints).
// ExpiresInMs is validity/TTL, NOT the time until the action.
public readonly record struct Hint(Direction Direction, uint ActionId, int GcdsUntil,
    uint TargetId, uint ExpiresInMs, bool Satisfied)
{
    public static bool TryParse(uint[]? wire, out Hint hint)
    {
        hint = default;
        if (wire is null || wire.Length < 7 || wire[0] is not (1 or 2)
            || wire[1] == 0 || wire[2] is < 1 or > 3 || wire[3] == 0
            || wire[4] == 0 || wire[6] > 1)
            return false;
        hint = new((Direction)wire[0], wire[1], (int)wire[2], wire[3], wire[4], wire[6] == 1);
        return true;
    }

    public static float? EstimateSeconds(int gcdsUntil, float total, float elapsed)
    {
        if (!float.IsFinite(total) || !float.IsFinite(elapsed) || total is < 1 or > 5 || elapsed < 0)
            return null;
        return Math.Max(0, total - elapsed) + (gcdsUntil - 1) * total;
    }
}

// One sound per hint/GCD window, with a hard minimum interval between sounds.
public sealed class CueGate
{
    private (uint Action, uint Target, Direction Direction)? key;
    private bool played;
    private int lastGcds;
    private float? lastSeconds;
    private long lastSound = long.MinValue / 2;

    public void Reset()
    {
        key = null;
        played = false;
        lastSeconds = null;
        lastGcds = 0;
    }

    public bool Update(Hint hint, float? seconds, bool eligible, float leadSeconds, long now)
    {
        var nextKey = (hint.ActionId, hint.TargetId, hint.Direction);
        // A new cooldown cycle can reuse the same action and target.
        var nextCycle = lastSeconds.HasValue && seconds.HasValue && seconds > lastSeconds + 0.8f;
        if (key != nextKey || hint.GcdsUntil > lastGcds || nextCycle)
        {
            key = nextKey;
            played = false;
        }
        lastGcds = hint.GcdsUntil;
        lastSeconds = seconds;
        var near = hint.GcdsUntil <= 1 && (!seconds.HasValue || seconds <= leadSeconds);
        if (!eligible || hint.Satisfied || !near || played || now - lastSound < 2500)
            return false;
        played = true;
        lastSound = now;
        return true;
    }
}
