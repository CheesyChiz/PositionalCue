using System.Numerics;

namespace PositionalCue;

public static class RingGeometry
{
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
