namespace PositionalCue;

public enum RotationSource { Wrath, BossMod, RotationSolver, Auto }

public static class SourceSelection
{
    public static Direction? BossDirection(int positional) => positional switch
    {
        1 => Direction.Flank,
        2 => Direction.Rear,
        _ => null,
    };
    // Never resolve an ambiguous active state by arbitrary priority.
    public static RotationSource? Select(RotationSource requested, bool wrath, bool boss, bool solver)
    {
        if (requested != RotationSource.Auto) return requested;
        if ((wrath ? 1 : 0) + (boss ? 1 : 0) + (solver ? 1 : 0) != 1) return null;
        return wrath ? RotationSource.Wrath : boss ? RotationSource.BossMod : RotationSource.RotationSolver;
    }
}
