using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using Dalamud.Bindings.ImGui;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Numerics;
using ActionRow = Lumina.Excel.Sheets.Action;

namespace PositionalCue;

public sealed partial class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IDataManager Data { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private readonly Configuration config;
    private readonly ICallGateSubscriber<uint[]?> hintIpc;
    private readonly CueGate cueGate = new();
    private readonly SoftChime chime = new();
    private Hint? hint;
    private float? seconds;
    private float gcdLength = 2.5f;
    private uint iconId;
    private uint cachedAction;
    private string actionName = "";
    private string status = "Waiting for Wrath Combo...";
    private bool settingsOpen;
    private bool preview;
    private bool previewRear = true;
    private bool previewCorrect;
    private long nextPoll;
    private long lastError;

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Normalize();
        hintIpc = Pi.GetIpcSubscriber<uint[]?>("WrathCombo.GetUpcomingPositionalHint");
        Commands.AddHandler("/pcue", new CommandInfo(OnCommand)
        {
            HelpMessage = "Positional Cue settings. Subcommands: test, toggle, on, off, sound, help.",
        });
        Commands.AddHandler("/positionalcue", new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /pcue. Open positional HUD settings; /pcue help lists commands.",
        });
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
        Pi.UiBuilder.OpenMainUi += OpenSettings;
        Framework.Update += Update;
    }

    private void OpenSettings() => settingsOpen = true;

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "test": preview = !preview; settingsOpen = true; break;
            case "toggle": config.Enabled = !config.Enabled; Save(); break;
            case "on": config.Enabled = true; Save(); break;
            case "off": config.Enabled = false; preview = false; Save(); break;
            case "sound": chime.Play(config.Volume); break;
            case "help":
                Chat.Print(T("[Positional Cue] /pcue — settings; test — preview; on/off/toggle — enable; sound — test chime. Alias: /positionalcue.", "[Positional Cue] /pcue — настройки; test — предпросмотр; on/off/toggle — включение; sound — проверить звук. /positionalcue — полная команда."));
                break;
            case "": settingsOpen = !settingsOpen; break;
            default: Chat.Print(T("[Positional Cue] Unknown command. See /pcue help", "[Positional Cue] Неизвестная команда. Список: /pcue help")); break;
        }
    }

    private void Save() => Pi.SavePluginConfig(config);

    private void Clear(string message)
    {
        hint = null;
        seconds = null;
        cueGate.Reset();
        status = message;
    }

    private void Update(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now < nextPoll) return;
        nextPoll = now + 50;
        try
        {
            if (!config.Enabled) { Clear(T("Disabled", "Выключено")); return; }
            var player = Objects.LocalPlayer;
            if (player == null) { Clear(T("Not logged in", "Персонаж не в игре")); return; }
            if (player.IsDead) { Clear(T("Character is KO", "Персонаж погиб")); return; }
            if (Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.BetweenAreas51]
                || Conditions[ConditionFlag.OccupiedInCutSceneEvent] || Conditions[ConditionFlag.WatchingCutscene78])
            { Clear(T("Loading / cutscene", "Загрузка / кат-сцена")); return; }
            if (!hintIpc.HasFunction) { Clear(T("Wrath positional IPC unavailable. Enable/update Wrath Combo.", "IPC позиционок Wrath недоступен. Включите или обновите Wrath Combo.")); return; }
            if (config.CombatOnly && !Conditions[ConditionFlag.InCombat]) { Clear(T("Waiting for combat", "Ожидание боя")); return; }
            if (config.HideDuringTrueNorth && player.StatusList.Any(s => s.StatusId == 1250))
            { Clear(T("True North active — no positional required", "True North активен — позиционка не требуется")); return; }
            var wire = hintIpc.InvokeFunc();
            if (!Hint.TryParse(wire, out var value))
            {
                Clear(wire == null ? T("Wrath connected — no upcoming positional", "Wrath подключён — предстоящей позиционки нет") : T("Unrecognized / inactive Wrath hint", "Подсказка Wrath не распознана или неактивна"));
                return;
            }
            // Wrath reports a uint target id. Match its explicit truncation of GameObjectId.
            // Reject old or alternate-target hints rather than prompt for the wrong enemy.
            var target = Targets.Target;
            if (target == null || (uint)target.GameObjectId != value.TargetId)
            { Clear(T("Wrath hint target differs from current target", "Цель подсказки Wrath отличается от текущей")); return; }
            if (value.GcdsUntil > config.LookAheadGcds) { Clear(T("Positional outside lookahead", "Позиционка за пределами упреждения")); return; }
            hint = value;
            seconds = ReadGcd(value.GcdsUntil, out gcdLength);
            if (cachedAction != value.ActionId)
            {
                cachedAction = value.ActionId;
                var row = Data.GetExcelSheet<ActionRow>().GetRowOrDefault(value.ActionId);
                actionName = row?.Name.ToString() ?? $"Action {value.ActionId}";
                iconId = row?.Icon ?? 0;
            }
            status = $"{T("Wrath connected", "Wrath подключён")} | {value.ActionId} | {value.GcdsUntil} GCD | {value.TargetId:X}";
            if (cueGate.Update(value, seconds,
                config.SoundEnabled && config.Volume > 0 && Conditions[ConditionFlag.InCombat] && !preview,
                config.SoundLeadSeconds, now))
                chime.Play(config.Volume);
        }
        catch (Exception ex)
        {
            Clear(T("Wrath unavailable / update error. See /xllog.", "Wrath недоступен / ошибка обновления. См. /xllog."));
            if (now - lastError > 30000)
            {
                lastError = now;
                Log.Warning(ex, "Unable to read positional hint");
            }
        }
    }

    private static unsafe float? ReadGcd(int gcdsUntil, out float gcdLength)
    {
        gcdLength = 2.5f;
        var manager = ActionManager.Instance();
        if (manager == null) return null;
        // Sprint is not a GCD. Use the melee role's basic shared-GCD weapon skill
        // Fast Blade (9) as a recast-group probe; it need not be learned/equipped.
        const uint sharedGcdProbe = 9;
        var total = manager->GetRecastTime(ActionType.Action, sharedGcdProbe);
        var elapsed = manager->GetRecastTimeElapsed(ActionType.Action, sharedGcdProbe);
        if (float.IsFinite(total) && total is >= 1 and <= 5) gcdLength = total;
        return Hint.EstimateSeconds(gcdsUntil, total, elapsed);
    }

    public void Dispose()
    {
        if (hudDragDirty) Save();
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Pi.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/pcue");
        Commands.RemoveHandler("/positionalcue");
        chime.Dispose();
    }
}
