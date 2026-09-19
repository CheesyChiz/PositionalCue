using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System.Numerics;

namespace PositionalCue;

internal sealed class GroundRing
{
    public const int Segments = 96;
    public readonly Vector3?[] Points = new Vector3?[Segments];
    public readonly Vector3?[,] Dividers = new Vector3?[4, 17];
    private float lastRotation;
    private ulong targetId;
    private Vector3 lastCenter;
    private float lastRadius;
    private long refreshAt;

    // World-aligned samples: rotation changes only which segments are highlighted.
    // Cache collision queries rather than running them on every rendered frame.
    public void Update(ulong id, Vector3 center, float radius, float rotation)
    {
        var now = Environment.TickCount64;
        if (id == targetId && now < refreshAt && Vector3.DistanceSquared(center, lastCenter) < 0.01f
            && MathF.Abs(radius - lastRadius) < 0.01f && MathF.Abs(rotation - lastRotation) < 0.01f) return;
        lastRotation = rotation;
        targetId = id;
        lastCenter = center;
        lastRadius = radius;
        refreshAt = now + 150;
        var floor = center.Y;
        if (BGCollisionModule.RaycastMaterialFilter(center + new Vector3(0, 2, 0), -Vector3.UnitY, out var centerHit, 50))
            floor = centerHit.Point.Y;
        for (var i = 0; i < Segments; i++)
        {
            var point = RingGeometry.Point(new(center.X, floor, center.Z), 0, radius, i * MathF.Tau / Segments);
            if (BGCollisionModule.RaycastMaterialFilter(point + new Vector3(0, 2, 0), -Vector3.UnitY, out var hit, 6)
                && float.IsFinite(hit.Point.Y))
                Points[i] = new Vector3(point.X, hit.Point.Y, point.Z);
            else Points[i] = null; // Do not bridge a cliff or fabricate a floor.
        }
        for (var quarter = 0; quarter < 4; quarter++)
            for (var step = 0; step <= 16; step++)
                Dividers[quarter, step] = Project(RingGeometry.Point(new(center.X, floor, center.Z),
                    rotation, radius * step / 16f, MathF.PI / 4 + quarter * MathF.PI / 2));
    }

    public static Vector3? Project(Vector3 point) =>
        BGCollisionModule.RaycastMaterialFilter(point + new Vector3(0, 2, 0), -Vector3.UnitY, out var hit, 6)
            && float.IsFinite(hit.Point.Y) ? new Vector3(point.X, hit.Point.Y, point.Z) : null;
}
