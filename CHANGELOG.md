# v0.5.0 — spell forecast (experimental)

- Independent draggable Wrath ST/AoE combo-button forecast with per-job bindings and enable switches.
- Current-state adjusted cast time and instant-cast label; approximate AoE selection using Wrath's threshold.
- Read-only hotbar/cache integration; no rotation hooks or action execution.
- Removed all character movement requests, settings and commands. Existing positional sources and player dot retained.
- Build and automated geometry tests verified; live game validation required.

# v0.4.0 — optional positional movement

- Forward Wrath/RSR positional requests to Reborn GoToPositional via temporary strategy IPC.
- Explicit per-load opt-in, movement-preset validation, range limit and cleanup on stop/unload.
- Movement-only Reborn presets no longer conflict with external combat rotations in Auto mode.
- Require neutral positional baseline and disable Reborn's direct RSR override before requesting movement.
- Correct the True North status ID.

# v0.3.0 — rotation sources

- Manual source selection or automatic selection of the active rotation.
- BossMod Reborn and Rotation Solver Reborn direction-only IPC support.
- Pause automatic hints when multiple rotations are active; detect conflicting BossMod forks.
- No fabricated action IDs or countdowns for direction-only providers.
- Source-specific dependency status; Wrath Combo is no longer mandatory.
- Independent player dot with combat/target conditions, size and color controls.

# v0.2.2 — position references

- Optional player position dot with adjustable size and the ring's ground offset.
- Optional target-relative quarter boundaries separating front, rear and both flanks.

# v0.2.1 — ground ring styling

- Project ring samples onto ground collision surfaces; skip missing terrain and sharp drops.
- Cache ground queries and keep sector highlighting aligned with target rotation.
- Configurable required-sector, correct-position and base-ring colors, including opacity.
- Subtle contrast outline and a more visible base ring; line thickness remains adjustable.
- Required sectors fill from dark to bright as the estimated action time approaches.
- Optional time label beside the ring and an adjustable preview countdown.
- Vertical offset slider relative to detected ground, from -1 to +3 game units.

# v0.2.0 — display modes and localization

- English by default, with optional Russian in settings.
- Drag the HUD in Preview; the position is saved on release.
- Target ring mode highlights rear or both flank sectors relative to the target's rotation.
- Separate Display, Sound and Dependencies tabs.
- Live installation, loaded-state and IPC checks for Wrath Combo and BossMod.
- Existing position, sound and display preferences are retained.

# v0.1.1 — catalog and presentation

- Added an original rear/flank icon and plugin metadata.
- Available through the shared CheesyChiz/DalamudPlugins `repo.json` catalog.
- Added `/positionalcue` alias and `/pcue on`, `off`, `sound`, `help`.
- Existing `/pcue`, `test` and `toggle` commands remain available.
- In-game validation is still pending; this remains a test release.

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
