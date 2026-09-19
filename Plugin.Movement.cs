using Dalamud.Game.ClientState.Conditions;
using System.Numerics;

namespace PositionalCue;

public sealed partial class Plugin
{
    private string? ownedMovementPreset;
    private Direction? sentMovementDirection;
    private string movementStatus = "Disabled";
    private string? checkedPreset;
    private bool checkedPresetValid;
    private long checkPresetAfter;
    private bool directRsrDisabled;

    private string? RebornPreset()
    {
        var ipc = Pi.GetIpcSubscriber<string?>("BossMod.Presets.GetActive");
        return Loaded("BossModReborn") && !Loaded("BossMod") && ipc.HasFunction ? ipc.InvokeFunc() : null;
    }

    private bool IsMovementPreset(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var now = Environment.TickCount64;
        if (checkedPreset == name && now < checkPresetAfter) return checkedPresetValid;
        checkedPreset = name;
        checkPresetAfter = now + 500;
        var ipc = Pi.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        checkedPresetValid = ipc.HasFunction && MovementPreset.IsCompatible(ipc.InvokeFunc(name));
        var readConfig = Pi.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration");
        var response = readConfig.HasFunction ? readConfig.InvokeFunc(new() { "AutorotationConfig", "FollowRSRDesiredPositional" }, false) : null;
        directRsrDisabled = response is { Count: 1 } && bool.TryParse(response[0], out var follow) && !follow;
        return checkedPresetValid;
    }

    private bool ReleaseMovement()
    {
        if (ownedMovementPreset == null) return true;
        try
        {
            if (!Loaded("BossModReborn")) { ownedMovementPreset = null; sentMovementDirection = null; return true; }
            if (Loaded("BossMod")) return false;
            var ipc = Pi.GetIpcSubscriber<string, string, string, bool>("BossMod.Presets.ClearTransientStrategy");
            if (!ipc.HasFunction || !ipc.InvokeFunc(ownedMovementPreset, MovementPreset.PositionalModule, "Positional")) return false;
            ownedMovementPreset = null;
            sentMovementDirection = null;
            return true;
        }
        catch { return false; }
    }

    private void UpdateMovement()
    {
        try
        {
            var player = Objects.LocalPlayer;
            var target = Targets.Target;
            var eligible = config.AutoPosition && config.Enabled && !preview
                && activeSource is RotationSource.Wrath or RotationSource.RotationSolver
                && hint is { } h && !h.Satisfied && h.GcdsUntil <= 1
                && player != null && !player.IsDead && target != null && !target.IsDead
                && h.TargetId == (uint)target.GameObjectId
                && Conditions[ConditionFlag.InCombat] && !Conditions[ConditionFlag.BetweenAreas]
                && !Conditions[ConditionFlag.BetweenAreas51] && !Conditions[ConditionFlag.OccupiedInCutSceneEvent]
                && !Conditions[ConditionFlag.WatchingCutscene78]
                && !player.StatusList.Any(s => s.StatusId == 7546)
                && Vector2.Distance(new(player.Position.X, player.Position.Z), new(target.Position.X, target.Position.Z))
                    <= target.HitboxRadius + config.MovementMaxDistance;
            if (!eligible)
            {
                movementStatus = ReleaseMovement()
                    ? T("No movement request", "Нет запроса на позиционку")
                    : T("Could not clear request — disable Reborn movement", "Не удалось снять запрос — отключите движение Reborn");
                return;
            }
            var name = RebornPreset();
            if (name != config.MovementPresetName || !BossActive() || !IsMovementPreset(name))
            {
                var released = ReleaseMovement();
                movementStatus = released
                    ? T("Select the configured movement-only preset in Reborn (Positional = Any)", "Выберите указанный пресет движения в Reborn (Positional = Any)")
                    : T("Could not clear request — disable Reborn movement", "Не удалось снять запрос — отключите движение Reborn");
                return;
            }
            if (!directRsrDisabled)
            {
                ReleaseMovement();
                movementStatus = T("Disable FollowRSRDesiredPositional in Reborn (or IPC check unavailable)",
                    "Отключите FollowRSRDesiredPositional в Reborn (либо проверка IPC недоступна)");
                return;
            }
            if (ownedMovementPreset != null && ownedMovementPreset != name && !ReleaseMovement()) return;
            var direction = hint!.Value.Direction;
            if (ownedMovementPreset == name && sentMovementDirection == direction) return;
            var ipc = Pi.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
            if (!ipc.HasFunction) { ReleaseMovement(); movementStatus = "Movement IPC unavailable"; return; }
            // Remember ownership before calling so an uncertain IPC failure still triggers cleanup.
            ownedMovementPreset = name;
            if (!ipc.InvokeFunc(name!, MovementPreset.PositionalModule, "Positional", direction == Direction.Rear ? "Rear" : "Flank"))
            { ReleaseMovement(); movementStatus = "Movement request rejected"; return; }
            sentMovementDirection = direction;
            movementStatus = T("Requested: ", "Запрошено: ") + direction;
        }
        catch (Exception ex)
        {
            ReleaseMovement();
            movementStatus = T("Movement IPC error; check Reborn", "Ошибка IPC движения; проверьте Reborn");
            if (Environment.TickCount64 - lastError > 30000)
            { lastError = Environment.TickCount64; Log.Warning(ex, "Positional movement IPC failed"); }
        }
    }
}
