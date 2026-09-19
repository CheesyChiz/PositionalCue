using PositionalCue;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

Check(!Hint.TryParse(null, out _), "absent provider snapshot");
Check(!Hint.TryParse([1, 2], out _), "truncated wire");
Check(!Hint.TryParse([3, 56, 1, 42, 7500, 0, 0], out _), "unknown direction");
Check(!Hint.TryParse([1, 56, uint.MaxValue, 42, 7500, 0, 0], out _), "invalid GCD count");
Check(!Hint.TryParse([1, 56, 1, 42, 0, 0, 0], out _), "expired snapshot");
Check(Hint.TryParse([2, 56, 1, 42, 7500, 0, 0], out var hint), "valid flank snapshot");
Check(Hint.EstimateSeconds(1, 2.5f, 1.5f) == 1, "ETA uses recast, not 7500ms TTL");
Check(Hint.EstimateSeconds(3, 2.5f, 1.5f) == 6, "multiple GCD ETA");
Check(Hint.EstimateSeconds(1, 0, 0) == null, "unknown GCD gives no bogus seconds");
var gate = new CueGate();
Check(!gate.Update(hint, 2, true, 1.2f, 10000), "do not beep too early");
Check(gate.Update(hint, 1, true, 1.2f, 11000), "beep near positional");
Check(!gate.Update(hint, 0.5f, true, 1.2f, 15000), "no repeated beep for same window");
gate.Reset();
Check(!gate.Update(hint with { Satisfied = true }, 1, true, 1.2f, 18000), "correct sector stays quiet");
Check(!gate.Update(hint, 1, false, 1.2f, 18000), "muted stays quiet");
Check(gate.Update(hint, 1, true, 1.2f, 18000), "wrong sector warning");
gate.Reset();
Check(!gate.Update(hint, 1, true, 1.2f, 18100), "dropouts cannot bypass cooldown");
Check(!gate.Update(hint with { GcdsUntil = 2 }, 3, true, 1.2f, 22000), "not yet next GCD");
Check(gate.Update(hint, 1, true, 1.2f, 24000), "next positional window");
Console.WriteLine("All checks passed.");
Check(RingGeometry.Highlight(MathF.PI, Direction.Rear), "rear arc highlights back");
Check(!RingGeometry.Highlight(0, Direction.Rear), "rear excludes front");
Check(RingGeometry.Highlight(MathF.PI / 2, Direction.Flank), "right flank");
Check(RingGeometry.Highlight(-MathF.PI / 2, Direction.Flank), "left flank");
Check(!RingGeometry.Highlight(MathF.PI, Direction.Flank), "flank excludes rear");
var back = RingGeometry.Point(System.Numerics.Vector3.Zero, 0, 2, MathF.PI);
Check(MathF.Abs(back.Z + 2) < 0.001f, "rotation zero rear is negative Z");
var rotated = RingGeometry.Point(System.Numerics.Vector3.Zero, MathF.PI / 2, 2, MathF.PI);
Check(MathF.Abs(rotated.X + 2) < 0.001f, "ring follows target rotation");
Check(RingGeometry.CountdownProgress(5, 5) == 0, "countdown starts empty");
Check(MathF.Abs(RingGeometry.CountdownProgress(1, 5) - 0.8f) < 0.001f, "one second remaining fills 80 percent");
Check(RingGeometry.CountdownProgress(0, 5) == 1, "ready fills entire sector");
Check(RingGeometry.CountdownProgress(7, 5) == 0, "progress clamps long ETA");
Check(MathF.Abs(RingGeometry.SectorProgress(MathF.PI, Direction.Rear) - 0.5f) < 0.001f, "rear midpoint fill");
Check(MathF.Abs(RingGeometry.SectorProgress(MathF.PI / 2, Direction.Flank) - 0.5f) < 0.001f, "right flank midpoint fill");
Check(MathF.Abs(RingGeometry.SectorProgress(-MathF.PI / 2, Direction.Flank) - 0.5f) < 0.001f, "left flank midpoint fill");
