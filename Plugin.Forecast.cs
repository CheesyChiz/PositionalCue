using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using ActionRow = Lumina.Excel.Sheets.Action;

namespace PositionalCue;

public sealed partial class Plugin
{
    private bool forecastPreview, forecastDragDirty;
    private long forecastPoll;
    private string forecastStatus = "";
    private ForecastCard? forecastCard;
    private sealed record ForecastCard(uint Icon, string Name, bool Aoe, float Cast, int Count, int? Threshold);

    private bool ForecastSafe => Objects.LocalPlayer is { IsDead: false }
        && !Conditions[ConditionFlag.BetweenAreas] && !Conditions[ConditionFlag.BetweenAreas51]
        && !Conditions[ConditionFlag.OccupiedInCutSceneEvent] && !Conditions[ConditionFlag.WatchingCutscene78];

    private unsafe void UpdateForecast()
    {
        if (Environment.TickCount64 < forecastPoll) return;
        forecastPoll = Environment.TickCount64 + 100;
        forecastCard = null;
        try
        {
            if (!config.ForecastEnabled || !ForecastSafe) return;
            var job = Objects.LocalPlayer!.ClassJob.RowId;
            if (!config.ForecastJobs.TryGetValue(job, out var binding) || !binding.Enabled)
            { forecastStatus = T("Configure this job's ST/AoE buttons below", "Настройте ST/AoE-кнопки этого класса ниже"); return; }
            if (!Loaded("WrathCombo") || !ReadBool("WrathCombo.GetAutoRotationState"))
            { forecastStatus = T("Wrath autorotation inactive", "Авторотация Wrath выключена"); return; }
            var ipc = Pi.GetIpcSubscriber<object, object?>("WrathCombo.GetAutoRotationConfigState");
            if (!ipc.HasFunction) { forecastStatus = T("Wrath config IPC unavailable", "IPC настроек Wrath недоступен"); return; }
            // WrathCombo.API.Enum.AutoRotationConfigOption.DPSAoETargets = 16.
            var raw = ipc.InvokeFunc(16);
            int? threshold = raw == null ? null : Convert.ToInt32(raw);
            if (threshold is <= 0 or > 100) { forecastStatus = T("Unsupported AoE threshold", "Неизвестный порог AoE"); return; }
            var count = 0;
            if (threshold != null)
            {
                if (!ReadForecastSlot(binding.AoeBar, binding.AoeSlot, out var aoeBase, out _, out _))
                { forecastStatus = T("Bind the AoE button (keep its hotbar visible)", "Укажите AoE-кнопку (хотбар должен быть видимым)"); return; }
                var aoe = Data.GetExcelSheet<ActionRow>().GetRowOrDefault(aoeBase);
                if (aoe == null || aoe.Value.CastType is not (2 or 3 or 4))
                { forecastStatus = T("AoE base action has unsupported geometry", "Геометрия базовой AoE-атаки не поддерживается"); return; }
                count = ForecastEnemyCount(aoe.Value);
            }
            var useAoe = ForecastMath.UseAoe(count, threshold);
            if (!ReadForecastSlot(useAoe ? binding.AoeBar : binding.StBar, useAoe ? binding.AoeSlot : binding.StSlot,
                out var baseId, out var apparent, out var icon))
            { forecastStatus = T("Bind the selected combo button", "Укажите нужную комбо-кнопку"); return; }

            // Read an existing cache only. Never call TryInvoke/GetAdjustedActionId or patch Wrath.
            var id = ReadWrathCachedAction(baseId);
            if (id == 0) id = apparent;
            var action = Data.GetExcelSheet<ActionRow>().GetRowOrDefault(id);
            if (action == null || icon == 0 || action.Value.Icon != icon)
            { forecastStatus = T("Waiting for a matching Wrath icon/cache", "Ожидание совпадения иконки и кэша Wrath"); return; }
            var cast = ActionManager.GetAdjustedCastTime(ActionType.Action, id) / 1000f;
            if (!float.IsFinite(cast) || cast < 0 || cast > 60) return;
            forecastCard = new(icon, action.Value.Name.ToString(), useAoe, cast, count, threshold);
            forecastStatus = $"{(useAoe ? "AoE" : "ST")} | {T("estimated targets", "оценка целей")}: {count} | {T("Wrath threshold", "порог Wrath")}: {threshold?.ToString() ?? T("AoE disabled", "AoE выключено")}";
        }
        catch (Exception ex)
        {
            forecastStatus = T("Forecast unavailable (Wrath/API changed)", "Прогноз недоступен (изменился Wrath/API)");
            if (Environment.TickCount64 - lastError > 30000) { lastError = Environment.TickCount64; Log.Warning(ex, "Forecast read failed"); }
        }
    }

    private static uint ReadWrathCachedAction(uint baseId)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "WrathCombo"))
        {
            var service = assembly.GetType("WrathCombo.Services.Service");
            var replacer = service?.GetField("<ActionReplacer>k__BackingField", flags)?.GetValue(null);
            if (replacer?.GetType().GetField("LastActionInvokeFor")?.GetValue(replacer) is Dictionary<uint, uint> cache
                && cache.TryGetValue(baseId, out var id)) return id;
        }
        return 0;
    }

    private static unsafe bool ReadForecastSlot(int bar, int slot, out uint baseId, out uint apparent, out uint icon)
    {
        baseId = apparent = icon = 0;
        if (bar is < 1 or > 10 || slot is < 1 or > 12) return false;
        var module = RaptureHotbarModule.Instance();
        if (module == null) return false;
        var value = module->Hotbars[bar - 1].GetHotbarSlot((uint)(slot - 1));
        if (value == null || value->CommandType != RaptureHotbarModule.HotbarSlotType.Action) return false;
        baseId = value->CommandId;
        apparent = value->ApparentActionId;
        icon = value->IconId;
        return baseId != 0;
    }

    private static Vector2 Xz(Vector3 p) => new(p.X, p.Z);
    private unsafe int ForecastEnemyCount(ActionRow aoe)
    {
        var player = Objects.LocalPlayer!;
        var target = Targets.Target;
        if (target == null) return 0;
        var origin = aoe.CastType == 2 && !aoe.CanTargetSelf ? Xz(target.Position) : Xz(player.Position);
        var count = 0;
        foreach (var npc in Objects.OfType<IBattleNpc>())
        {
            // A self-centred AoE cannot target enemies directly. Use a hostile-target
            // action only for the relationship check, not for range or readiness.
            if (npc.IsDead || !npc.IsTargetable || !ActionManager.CanUseActionOnTarget(9,
                (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address)) continue;
            if (ForecastMath.Hits(aoe.CastType, origin, Xz(target.Position), Xz(npc.Position),
                aoe.EffectRange, Math.Max((int)aoe.Range, (int)aoe.EffectRange), aoe.XAxisModifier, npc.HitboxRadius)) count++;
        }
        return count;
    }

    private bool DrawForecastSettings()
    {
        if (!ImGui.BeginTabItem(L("Forecast", "Прогноз"))) return false;
        var changed = ImGui.Checkbox(L("Enable forecast panel", "Панель прогноза"), ref config.ForecastEnabled);
        changed |= ImGui.Checkbox(L("Forecast in combat only", "Прогноз только в бою"), ref config.ForecastCombatOnly);
        changed |= ImGui.SliderFloat(L("Forecast scale", "Масштаб прогноза"), ref config.ForecastScale, 0.7f, 2, "%.2f");
        ImGui.Checkbox(L("Preview / drag forecast", "Предпросмотр / перемещение прогноза"), ref forecastPreview);
        if (ImGui.Button(L("Reset forecast position", "Сбросить положение прогноза"))) { config.ForecastOffset = new(110, 225); changed = true; }
        ImGui.TextWrapped(T("Approximate combo-button candidate, not a queue. Reads Wrath's AoE threshold; estimates targets around your selected target. Ignored NPCs, target selection and ST/AoE locks may differ. Keep both bound hotbars visible. No rotation calls or hooks.",
            "Примерный кандидат комбо-кнопки, не очередь. Порог AoE читается из Wrath; цели оцениваются относительно выбранной цели. Исключённые NPC, выбор цели и блокировки ST/AoE могут отличаться. Оба хотбара должны быть видимыми. Без вызовов ротации и перехватов."));
        ImGui.TextWrapped(forecastStatus);
        foreach (var job in Data.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().Where(j => j.RowId > 0))
        {
            // Include base combat classes, exclude crafting/gathering.
            if (job.RowId is >= 8 and <= 18) continue;
            ImGui.PushID((int)job.RowId);
            if (ImGui.TreeNode(job.Abbreviation.ToString()))
            {
                if (!config.ForecastJobs.TryGetValue(job.RowId, out var binding)) config.ForecastJobs[job.RowId] = binding = new();
                changed |= ImGui.Checkbox(L("Enabled for this job", "Включить для класса"), ref binding.Enabled);
                changed |= ImGui.SliderInt(L("ST hotbar", "ST панель"), ref binding.StBar, 1, 10);
                changed |= ImGui.SliderInt(L("ST slot (0 = unbound)", "ST слот (0 = не задан)"), ref binding.StSlot, 0, 12);
                changed |= ImGui.SliderInt(L("AoE hotbar", "AoE панель"), ref binding.AoeBar, 1, 10);
                changed |= ImGui.SliderInt(L("AoE slot (0 = unbound)", "AoE слот (0 = не задан)"), ref binding.AoeSlot, 0, 12);
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        ImGui.EndTabItem();
        return changed;
    }

    private void DrawForecast()
    {
        if (forecastDragDirty && (!forecastPreview || !ImGui.IsMouseDown(ImGuiMouseButton.Left))) { Save(); forecastDragDirty = false; }
        if (!forecastPreview && (!config.ForecastEnabled || !ForecastSafe || forecastCard == null
            || (config.ForecastCombatOnly && !Conditions[ConditionFlag.InCombat]))) return;
        var card = forecastPreview ? new ForecastCard(0, T("Example spell", "Пример заклинания"), false, 2.5f, 1, 3) : forecastCard!;
        var scale = config.ForecastScale;
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(320, 115) * scale;
        var center = viewport.Pos + viewport.Size * 0.5f;
        var max = Vector2.Max(viewport.Pos, viewport.Pos + viewport.Size - size);
        var position = Vector2.Clamp(center + config.ForecastOffset, viewport.Pos, max);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.87f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
        if (!forecastPreview) flags |= ImGuiWindowFlags.NoInputs;
        if (ImGui.Begin("###PositionalCueForecast", flags))
        {
            ImGui.SetWindowFontScale(scale);
            var top = ImGui.GetCursorScreenPos();
            if (forecastPreview)
            {
                ImGui.InvisibleButton("###DragForecast", ImGui.GetContentRegionAvail());
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
                { config.ForecastOffset = Vector2.Clamp(position + ImGui.GetIO().MouseDelta, viewport.Pos, max) - center; forecastDragDirty = true; }
                ImGui.SetCursorScreenPos(top);
            }
            var texture = card.Icon != 0 ? Textures.GetFromGameIcon(new GameIconLookup(card.Icon)).GetWrapOrDefault() : null;
            if (texture != null) ImGui.Image(texture.Handle, new Vector2(48) * scale);
            else ImGui.Dummy(new Vector2(48) * scale);
            ImGui.SameLine(); ImGui.BeginGroup();
            ImGui.TextUnformatted($"{(card.Aoe ? "AoE" : "ST")} · {T("FORECAST", "ПРОГНОЗ")} ~");
            ImGui.TextColored(card.Cast == 0 ? new Vector4(0.4f, 0.9f, 0.65f, 1) : new Vector4(1, 0.72f, 0.3f, 1),
                card.Cast == 0 ? T("Instant (current buffs)", "Мгновенно (текущие баффы)") : $"{T("Cast", "Каст")}: {card.Cast:0.0} {T("s", "с")}");
            ImGui.EndGroup();
            ImGui.TextUnformatted(card.Name);
            ImGui.TextDisabled(forecastPreview ? T("PREVIEW — drag to move", "ПРЕДПРОСМОТР — перетащите")
                : T("May change · does not cancel current cast", "Может измениться · текущий каст не прерывать"));
            ImGui.SetWindowFontScale(1);
        }
        ImGui.End();
    }
}
