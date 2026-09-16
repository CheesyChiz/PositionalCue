# v0.1.0 — test release

- Read-only Wrath Combo positional IPC integration; no changes to other plugins.
- Rear/flank HUD with action icon, sector correctness and approximate GCD countdown.
- Configurable center-relative position, size and 1–3 GCD lookahead.
- Quiet synthesized 180 ms chime, volume and warning lead-time controls.
- Preview mode and sound test in `/pcue`; `/pcue toggle` switches the helper off/on.
- True North, combat, target mismatch, logout and unavailable-IPC handling.

Validation: compiled against installed Dalamud API 15 with zero warnings/errors;
18 automated checks passed for IPC decoding, ETA calculation and sound gating.
In-game rendering, real Wrath hint timing and sound level still require user testing.
