<p align="center"><img src="assets/icon.png" width="128" alt="Positional Cue"></p>

# Positional Cue

A Dalamud overlay that helps melee players prepare for positional attacks.
Reads upcoming actions from Wrath Combo through IPC and displays rear/flank,
the action icon, an approximate GCD countdown and current sector correctness.
An optional soft chime warns when a position change is needed.

Choose a draggable HUD or a target ring highlighting the required rear/flank sectors.
Use Preview to position the HUD with the mouse or inspect the ring on a selected target.
English is the default language; Russian is available in settings.
The Dependencies tab shows installation, load and IPC status for Wrath Combo and BossMod.
Hints are hidden during True North by default. Does not control movement or rotation.

## Requirements

- [Wrath Combo](https://github.com/PunishXIV/WrathCombo)

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
| `/pcue help` | Command list |

Alias: `/positionalcue`.

## Build

.NET 10 SDK and Dalamud API 15 development assemblies.

```powershell
dotnet build -c Release
dotnet run --project Tests/PositionalCue.Tests.csproj -c Release
```
