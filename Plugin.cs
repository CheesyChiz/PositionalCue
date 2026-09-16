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

public sealed class Plugin : IDalamudPlugin
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

    private readonly Configuration config;
    private readonly ICallGateSubscriber<uint[]?> hintIpc;
    private readonly CueGate cueGate = new();
    private readonly SoftChime chime = new();
    private Hint? hint;
    private float? seconds;
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
                Chat.Print("[Positional Cue] /pcue — настройки; test — предпросмотр; on/off/toggle — включение; sound — проверить звук. /positionalcue — полная команда.");
                break;
            case "": settingsOpen = !settingsOpen; break;
            default: Chat.Print("[Positional Cue] Неизвестная команда. Список: /pcue help"); break;
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
            if (!config.Enabled) { Clear("Disabled"); return; }
            var player = Objects.LocalPlayer;
            if (player == null) { Clear("Not logged in"); return; }
            if (player.IsDead) { Clear("Character is KO"); return; }
            if (Conditions[ConditionFlag.BetweenAreas] || Conditions[ConditionFlag.BetweenAreas51]
                || Conditions[ConditionFlag.OccupiedInCutSceneEvent] || Conditions[ConditionFlag.WatchingCutscene78])
            { Clear("Loading / cutscene"); return; }
            if (!hintIpc.HasFunction) { Clear("Wrath positional IPC unavailable. Enable/update Wrath Combo."); return; }
            if (config.CombatOnly && !Conditions[ConditionFlag.InCombat]) { Clear("Waiting for combat"); return; }
            if (config.HideDuringTrueNorth && player.StatusList.Any(s => s.StatusId == 1250))
            { Clear("True North active — no positional required"); return; }
            var wire = hintIpc.InvokeFunc();
            if (!Hint.TryParse(wire, out var value))
            {
                Clear(wire == null ? "Wrath connected — no upcoming positional" : "Unrecognized / inactive Wrath hint");
                return;
            }
            // Wrath reports a uint target id. Match its explicit truncation of GameObjectId.
            // Reject old or alternate-target hints rather than prompt for the wrong enemy.
            var target = Targets.Target;
            if (target == null || (uint)target.GameObjectId != value.TargetId)
            { Clear("Wrath hint target differs from current target"); return; }
            if (value.GcdsUntil > config.LookAheadGcds) { Clear("Positional outside lookahead"); return; }
            hint = value;
            seconds = ReadGcd(value.GcdsUntil);
            if (cachedAction != value.ActionId)
            {
                cachedAction = value.ActionId;
                var row = Data.GetExcelSheet<ActionRow>().GetRowOrDefault(value.ActionId);
                actionName = row?.Name.ToString() ?? $"Action {value.ActionId}";
                iconId = row?.Icon ?? 0;
            }
            status = $"Wrath connected | action {value.ActionId} | {value.GcdsUntil} GCD | target {value.TargetId:X}";
            if (cueGate.Update(value, seconds,
                config.SoundEnabled && config.Volume > 0 && Conditions[ConditionFlag.InCombat] && !preview,
                config.SoundLeadSeconds, now))
                chime.Play(config.Volume);
        }
        catch (Exception ex)
        {
            Clear("Wrath unavailable / update error. See /xllog.");
            if (now - lastError > 30000)
            {
                lastError = now;
                Log.Warning(ex, "Unable to read positional hint");
            }
        }
    }

    private static unsafe float? ReadGcd(int gcdsUntil)
    {
        var manager = ActionManager.Instance();
        if (manager == null) return null;
        // Sprint is not a GCD. Use the melee role's basic shared-GCD weapon skill
        // Fast Blade (9) as a recast-group probe; it need not be learned/equipped.
        const uint sharedGcdProbe = 9;
        var total = manager->GetRecastTime(ActionType.Action, sharedGcdProbe);
        var elapsed = manager->GetRecastTimeElapsed(ActionType.Action, sharedGcdProbe);
        return Hint.EstimateSeconds(gcdsUntil, total, elapsed);
    }

    private void Draw()
    {
        if (settingsOpen) DrawSettings();
        if (preview) DrawHud(new(previewRear ? Direction.Rear : Direction.Flank, 0, 1, 1, 1000, previewCorrect), 0.9f, true);
        else if (config.Enabled && hint is { } current && !(config.HideWhenCorrect && current.Satisfied))
            DrawHud(current, seconds, false);
    }

    private void DrawHud(Hint current, float? eta, bool demo)
    {
        var scale = config.Scale;
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(270, 95) * scale;
        var desired = viewport.Pos + viewport.Size * 0.5f + config.Offset;
        desired = Vector2.Clamp(desired, viewport.Pos, Vector2.Max(viewport.Pos, viewport.Pos + viewport.Size - size));
        ImGui.SetNextWindowPos(desired, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.87f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 9 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 9) * scale);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs;
        if (ImGui.Begin("Positional Cue HUD###PositionalCueHud", flags))
        {
            ImGui.SetWindowFontScale(scale);
            var color = current.Satisfied ? new Vector4(0.40f, 0.91f, 0.65f, 1) : new Vector4(1, 0.72f, 0.30f, 1);
            var top = ImGui.GetCursorScreenPos();
            var iconSize = new Vector2(48) * scale;
            var texture = !demo && iconId != 0 ? Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault() : null;
            if (texture != null) ImGui.Image(texture.Handle, iconSize);
            else
            {
                ImGui.GetWindowDrawList().AddRectFilled(top, top + iconSize, ImGui.GetColorU32(color * new Vector4(0.35f, 0.35f, 0.35f, 1)), 6 * scale);
                ImGui.GetWindowDrawList().AddText(top + new Vector2(17, 14) * scale, ImGui.GetColorU32(color), current.Direction == Direction.Rear ? "R" : "F");
                ImGui.Dummy(iconSize);
            }
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextColored(color, current.Direction == Direction.Rear ? "REAR / СЗАДИ" : "FLANK / СБОКУ");
            ImGui.TextUnformatted(eta is { } time ? $"~{time:0.0} s  |  {current.GcdsUntil} GCD" : $"Через {current.GcdsUntil} GCD");
            ImGui.TextColored(color, current.Satisfied ? "В нужном секторе" : "Смените позицию");
            ImGui.EndGroup();
            ImGui.TextDisabled(demo ? "PREVIEW — пробный индикатор" : actionName);
            ImGui.SetWindowFontScale(1);
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(530, 540), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Positional Cue — настройки", ref settingsOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.TextWrapped("Подсказки позиционок от Wrath Combo. Плагин не управляет движением или ротацией.");
            ImGui.Separator();
            var changed = ImGui.Checkbox("Включить подсказчик", ref config.Enabled);
            changed |= ImGui.Checkbox("Показывать только в бою", ref config.CombatOnly);
            changed |= ImGui.Checkbox("Скрывать, если сектор уже правильный", ref config.HideWhenCorrect);
            changed |= ImGui.Checkbox("Скрывать во время True North", ref config.HideDuringTrueNorth);
            changed |= ImGui.SliderInt("Упреждение, GCD", ref config.LookAheadGcds, 1, 3);
            changed |= ImGui.SliderFloat("Масштаб", ref config.Scale, 0.7f, 2f, "%.2f");
            changed |= ImGui.DragFloat2("Смещение от центра", ref config.Offset, 1, -4000, 4000, "%.0f");
            if (ImGui.Button("Сбросить положение")) { config.Offset = new(110, 100); changed = true; }
            ImGui.Separator();
            changed |= ImGui.Checkbox("Мягкий звуковой сигнал", ref config.SoundEnabled);
            changed |= ImGui.SliderFloat("Громкость", ref config.Volume, 0, 0.4f, "%.2f");
            changed |= ImGui.SliderFloat("Сигнал за, сек. (примерно)", ref config.SoundLeadSeconds, 0.3f, 3f, "%.1f");
            if (ImGui.Button("Проверить звук")) chime.Play(config.Volume);
            ImGui.TextWrapped("Один сигнал, когда ближайшая позиционка требует смены сектора. Вне боя автоматический звук отключён.");
            ImGui.Separator();
            ImGui.Checkbox("Предпросмотр HUD", ref preview);
            if (preview)
            {
                ImGui.Checkbox("Пример: REAR (иначе FLANK)", ref previewRear);
                ImGui.Checkbox("Пример: правильный сектор", ref previewCorrect);
            }
            ImGui.TextWrapped(status);
            ImGui.TextDisabled("Секунды приблизительные; решение Wrath может измениться.");
            ImGui.TextWrapped("Нужен Wrath с GetUpcomingPositionalHint. BossMod и Avarice для этого HUD не требуются.");
            if (changed) { config.Normalize(); Save(); }
        }
        ImGui.End();
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Pi.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/pcue");
        Commands.RemoveHandler("/positionalcue");
        chime.Dispose();
    }
}
