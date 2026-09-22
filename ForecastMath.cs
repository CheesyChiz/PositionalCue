using System.Numerics;

namespace PositionalCue;

public static class ForecastMath
{
    public static bool UseAoe(int count, int? threshold) => threshold is > 0 && count >= threshold;

    // Approximation: selected-target aim, 90-degree cone, no ignored-NPC list.
    public static bool Hits(int shape, Vector2 origin, Vector2 aim, Vector2 enemy,
        float radius, float length, float width, float hitbox)
    {
        var offset = enemy - origin;
        hitbox = Math.Max(0, hitbox);
        if (shape == 2) return offset.LengthSquared() <= MathF.Pow(Math.Max(0, radius) + hitbox, 2);
        var forward = aim - origin;
        if (forward.LengthSquared() < 0.0001f) return false;
        forward = Vector2.Normalize(forward);
        var along = Vector2.Dot(offset, forward);
        if (along < -hitbox || along > length + hitbox) return false;
        var across = MathF.Abs(offset.X * forward.Y - offset.Y * forward.X);
        return shape switch
        {
            3 => across <= Math.Max(0, along) + hitbox,
            4 => across <= Math.Max(0, width) * 0.5f + hitbox,
            _ => false,
        };
    }
}
