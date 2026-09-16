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
