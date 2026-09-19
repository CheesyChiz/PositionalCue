<p align="center"><img src="assets/icon.png" width="128" alt="Positional Cue"></p>

# Positional Cue

A Dalamud overlay that helps melee players prepare for positional attacks.
Reads positional hints through IPC from Wrath Combo, BossMod Reborn or Rotation Solver Reborn.
Choose a source manually or select Auto to follow the active rotation.
Auto pauses hints when multiple supported rotations are active; it does not change their settings.
Wrath supplies upcoming actions, icons and an approximate GCD countdown.
Reborn and RSR supply direction only: no action icon, countdown or multi-GCD lookahead.
Their hints are drawn on your selected target, which must match the rotation's target.
Direction-only sound cues play when a required position change is reported, not at a timed lead.
An optional soft chime warns when a position change is needed.

Choose a draggable HUD or a target ring highlighting the required rear/flank sectors.
The ring follows ground collision surfaces, with adjustable colors, opacity, thickness
and contrast outline. Vertical offset adjusts height relative to the ground.
Segments without detected ground are omitted.
An independent player dot has its own enable, combat-only, target-required, size and color settings;
it shares the ring's ground offset and does not require a positional hint. Optional quarter boundaries
rotate with the target to separate front, rear and both flanks.
Highlighted sectors fill as the action approaches; an optional label shows the estimated
seconds remaining. Timing follows Wrath hints and the current GCD, not a guaranteed cast time.
Use Preview to position the HUD with the mouse or inspect the ring on a selected target.
English is the default language; Russian is available in settings.
The Dependencies tab shows installation, load and IPC status for each source.
Original BossMod is not supported; keep it disabled when using Reborn because they share IPC names.
Hints are hidden during True North by default. Does not execute combat actions.

## Optional positional movement

Requires BossMod Reborn and Wrath Combo or RSR as the hint source. Enable in `/pcue` → Movement;
movement starts disabled after each plugin load. Select the movement-only preset named in that tab (default: `Move`) in Reborn.
It must contain `GoToPositional` with `Positional = Any`, and `NormalMovement` with `Destination = Pathfind`.
Disable Reborn's `FollowRSRDesiredPositional` (`/bmr cfg AutorotationConfig FollowRSRDesiredPositional false`).
Keep Reborn and the rotation targeting the selected enemy; do not share this preset's Positional track with another controller.

Only the temporary Positional strategy is changed through IPC. No permanent Reborn settings or presets are edited.
Wrath requests are limited to the next GCD; RSR requests use its direction-only hint. Requests are removed
when unnecessary, outside combat, during True North, on disable or unload. `/pcue stop` releases our request.
Reborn may still move for dodges, range or other preset goals. It cannot avoid unknown mechanics;
this is not restricted to small clockwise/counterclockwise steps. If cleanup fails or the plugin crashes,
disable Reborn's movement preset. Test on a training dummy first.

## Requirements

One of:

- [Wrath Combo](https://github.com/PunishXIV/WrathCombo) with positional-hint IPC
- [BossMod Reborn](https://github.com/FFXIV-CombatReborn/BossmodReborn)
- [Rotation Solver Reborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)

## Installation

Add this URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/CheesyChiz/DalamudPlugins/main/repo.json
```

Install **Positional Cue** through `/xlplugins`, then open `/pcue`.

## Commands

| Command | Action |
| --- | --- |
| `/pcue` | Settings |
| `/pcue test` | Toggle HUD preview |
| `/pcue on` / `/pcue off` | Enable / disable |
| `/pcue toggle` | Toggle enabled state |
| `/pcue sound` | Test sound |
| `/pcue stop` | Disable positional requests and release the temporary strategy |
| `/pcue help` | Command list |

Alias: `/positionalcue`.

## Build

.NET 10 SDK and Dalamud API 15 development assemblies.

```powershell
dotnet build -c Release
dotnet run --project Tests/PositionalCue.Tests.csproj -c Release
```
