using System.Numerics;

namespace PositionalCue;

public static class RingGeometry
{
    public static float CountdownProgress(float seconds, float horizon) =>
        float.IsFinite(seconds) && float.IsFinite(horizon) && horizon > 0
            ? Math.Clamp(1 - seconds / horizon, 0, 1) : 0;

    // Each highlighted quarter has its own 0..1 sweep; both flanks fill together.
    public static float SectorProgress(float relativeAngle, Direction direction)
    {
        var angle = (relativeAngle % MathF.Tau + MathF.Tau) % MathF.Tau;
        var start = direction == Direction.Rear ? 3 * MathF.PI / 4
            : angle < MathF.PI ? MathF.PI / 4 : 5 * MathF.PI / 4;
        return Math.Clamp((angle - start) / (MathF.PI / 2), 0, 1);
    }

    // FFXIV forward = (sin(rotation), 0, cos(rotation)). Angles are relative to forward.
    public static Vector3 Point(Vector3 center, float rotation, float radius, float relativeAngle)
        => center + new Vector3(MathF.Sin(rotation + relativeAngle) * radius, 0,
            MathF.Cos(rotation + relativeAngle) * radius);

    public static bool Highlight(float relativeAngle, Direction direction)
    {
        var angle = MathF.Abs(MathF.IEEERemainder(relativeAngle, MathF.Tau));
        return direction == Direction.Rear
            ? angle >= 3 * MathF.PI / 4
            : angle >= MathF.PI / 4 && angle <= 3 * MathF.PI / 4;
    }
}
