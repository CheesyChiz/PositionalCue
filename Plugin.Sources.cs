namespace PositionalCue;

public sealed partial class Plugin
{
    private RotationSource? activeSource;
    private static bool Loaded(string name) => Pi.InstalledPlugins.Any(p => p.InternalName == name && p.IsLoaded);
    private static bool ReadBool(string name)
    {
        try
        {
            var ipc = Pi.GetIpcSubscriber<bool>(name);
            return ipc.HasFunction && ipc.InvokeFunc();
        }
        catch { return false; }
    }

    private string SourceName => activeSource switch
    {
        RotationSource.Wrath => "Wrath Combo",
        RotationSource.BossMod => "BossMod Reborn",
        RotationSource.RotationSolver => "Rotation Solver Reborn",
        _ => "Auto",
    };

    private bool TryReadSource(out Hint value)
    {
        value = default;
        var wrath = Loaded("WrathCombo") && ReadBool("WrathCombo.GetAutoRotationState")
            && ReadBool("WrathCombo.IsCurrentJobAutoRotationReady");
        // Both forks register the BossMod.* prefix. Never consume an ambiguous provider.
        var bossConflict = Loaded("BossMod") && Loaded("BossModReborn");
        if (bossConflict && config.Source is RotationSource.BossMod or RotationSource.Auto)
        { Clear(T("Both BossMod forks are loaded — disable the original", "Загружены обе версии BossMod — отключите оригинал")); return false; }
        var boss = BossActive();
        var solver = Loaded("RotationSolver") && ReadBool("RotationSolverReborn.AutorotationActive");
        var selected = SourceSelection.Select(config.Source, wrath, boss, solver);
        if (selected != activeSource) { cueGate.Reset(); cachedAction = uint.MaxValue; activeSource = selected; }
        if (selected == null)
        {
            Clear(wrath || boss || solver
                ? T("Multiple autorotations active — select a source manually", "Активны несколько авторотаций — выберите источник вручную")
                : T("No active autorotation detected", "Активная авторотация не обнаружена"));
            return false;
        }
        if (selected == RotationSource.Wrath)
        {
            if (!Loaded("WrathCombo") || !hintIpc.HasFunction)
            { Clear(T("Wrath positional IPC unavailable", "IPC позиционок Wrath недоступен")); return false; }
            if (Hint.TryParse(hintIpc.InvokeFunc(), out value)) return true;
            Clear(T("Wrath connected — no upcoming positional", "Wrath подключён — предстоящей позиционки нет"));
            return false;
        }
        if (selected == RotationSource.BossMod)
        {
            var ipc = Pi.GetIpcSubscriber<int>("BossMod.Hints.RecommendedPositional");
            if (!Loaded("BossModReborn") || !ipc.HasFunction)
            { Clear(T("Reborn positional IPC unavailable", "IPC позиционок Reborn недоступен")); return false; }
            if (!boss) { Clear(T("Reborn autorotation inactive", "Авторотация Reborn неактивна")); return false; }
            return DirectionHint(SourceSelection.BossDirection(ipc.InvokeFunc()), out value);
        }
        var positional = Pi.GetIpcSubscriber<byte>("RotationSolverReborn.GetDesiredPositional");
        if (!Loaded("RotationSolver") || !positional.HasFunction)
        { Clear(T("RSR positional IPC unavailable — enable/update RSR", "IPC позиционок RSR недоступен — включите/обновите RSR")); return false; }
        if (!solver) { Clear(T("RSR autorotation inactive", "Авторотация RSR неактивна")); return false; }
        var direction = positional.InvokeFunc();
        return DirectionHint(direction is 1 or 2 ? (Direction)direction : null, out value);
    }

    private static bool BossActive()
    {
        if (!Loaded("BossModReborn") || Loaded("BossMod")) return false;
        try
        {
            var disabled = Pi.GetIpcSubscriber<bool>("BossMod.Presets.GetForceDisabled");
            var preset = Pi.GetIpcSubscriber<string?>("BossMod.Presets.GetActive");
            return disabled.HasFunction && !disabled.InvokeFunc()
                && ((preset.HasFunction && !string.IsNullOrEmpty(preset.InvokeFunc()))
                    || ReadBool("BossMod.Rotation.ActionQueue.HasEntries"));
        }
        catch { return false; }
    }

    private bool DirectionHint(Direction? direction, out Hint value)
    {
        value = default;
        var target = Targets.Target;
        var player = Objects.LocalPlayer;
        if (direction == null || target is not Dalamud.Game.ClientState.Objects.Types.IBattleNpc npc
            || npc.IsDead || !npc.IsTargetable || player == null)
        { Clear(SourceName + T(" — no positional / battle target", " — нет позиционки или боевой цели")); return false; }
        var delta = player.Position - target.Position;
        var angle = MathF.Atan2(delta.X, delta.Z) - target.Rotation;
        // IPC supplies direction only, not action, target, timing or a multi-GCD forecast.
        value = new(direction.Value, 0, 0, (uint)target.GameObjectId, 100, RingGeometry.Highlight(angle, direction.Value));
        return true;
    }
}
