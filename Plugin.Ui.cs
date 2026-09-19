using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using System.Globalization;
using System.Numerics;

namespace PositionalCue;

public sealed partial class Plugin
{
    private bool hudDragDirty;
    private readonly GroundRing groundRing = new();
    private float previewSeconds = 2;
    private string T(string english, string russian) => config.Language == 1 ? russian : english;
    private string L(string english, string russian) => T(english, russian) + "###" + english;

    private void Draw()
    {
        if (settingsOpen) DrawSettings();
        if (hudDragDirty && (!preview || !ImGui.IsMouseDown(ImGuiMouseButton.Left)))
        {
            Save();
            hudDragDirty = false;
        }
        if (preview)
            DrawIndicator(new(previewRear ? Direction.Rear : Direction.Flank, 0, 1, 1, 1000, previewCorrect), previewSeconds, true);
        else if (config.Enabled && hint is { } current && !(config.HideWhenCorrect && current.Satisfied))
            DrawIndicator(current, seconds, false);
        DrawPlayerDot();
    }

    private void DrawPlayerDot()
    {
        // Deliberately independent of hint availability, display mode and hint enable state.
        var player = Objects.LocalPlayer;
        if (!config.RingPlayerDot || player == null || player.IsDead) return;
        if (Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51]
            || Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent]
            || Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78]) return;
        if (!preview && config.PlayerDotCombatOnly && !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat]) return;
        if (config.PlayerDotRequireTarget && Targets.Target == null) return;
        if (GroundRing.Project(player.Position) is not { } ground
            || !GameGui.WorldToScreen(ground + new Vector3(0, config.RingHeight, 0), out var screen)
            || !float.IsFinite(screen.X) || !float.IsFinite(screen.Y)) return;
        var draw = ImGui.GetBackgroundDrawList();
        var viewport = ImGui.GetMainViewport();
        draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, true);
        draw.AddCircleFilled(screen, config.RingPlayerDotSize + 1.5f,
            ImGui.GetColorU32(new Vector4(0, 0, 0, config.PlayerDotColor.W * 0.87f)));
        draw.AddCircleFilled(screen, config.RingPlayerDotSize, ImGui.GetColorU32(config.PlayerDotColor));
        draw.PopClipRect();
    }

    private void DrawIndicator(Hint current, float? eta, bool demo)
    {
        if (config.DisplayMode == 1) DrawRing(current, eta, demo);
        else DrawHud(current, eta, demo);
    }

    private void DrawRing(Hint current, float? eta, bool demo)
    {
        var target = Targets.Target;
        if (Objects.LocalPlayer == null || target == null || (!demo && (uint)target.GameObjectId != current.TargetId)) return;
        var radius = Math.Max(0.5f, target.HitboxRadius) + config.RingPadding;
        var center = target.Position;
        var rotation = target.Rotation;
        if (!float.IsFinite(radius) || !float.IsFinite(rotation) || radius > 200) return;
        if (Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]
            || Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51]) return;
        groundRing.Update(target.GameObjectId, center, radius, rotation);
        var draw = ImGui.GetBackgroundDrawList();
        var viewport = ImGui.GetMainViewport();
        draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, true);
        var activeColor = current.Satisfied ? config.RingCorrectColor : config.RingRequiredColor;
        var bright = ImGui.GetColorU32(activeColor);
        var dim = ImGui.GetColorU32(config.RingBaseColor);
        var dark = ImGui.GetColorU32(activeColor * new Vector4(0.38f, 0.38f, 0.38f, 1));
        var progress = eta is { } remaining ? RingGeometry.CountdownProgress(remaining, gcdLength * config.LookAheadGcds) : 1;
        const int segments = GroundRing.Segments;
        for (var i = 0; i < segments; i++)
        {
            if (groundRing.Points[i] is not { } start || groundRing.Points[(i + 1) % segments] is not { } end) continue;
            if (MathF.Abs(start.Y - end.Y) > 1) continue;
            start.Y += config.RingHeight;
            end.Y += config.RingHeight;
            if (!GameGui.WorldToScreen(start, out var p1) || !GameGui.WorldToScreen(end, out var p2)) continue;
            if (!float.IsFinite(p1.X) || !float.IsFinite(p1.Y) || !float.IsFinite(p2.X) || !float.IsFinite(p2.Y)) continue;
            var relativeAngle = (i + 0.5f) * MathF.Tau / segments - rotation;
            var highlight = RingGeometry.Highlight(relativeAngle, current.Direction);
            var filled = !config.RingCountdownFill || RingGeometry.SectorProgress(relativeAngle, current.Direction) <= progress;
            var thickness = highlight ? config.RingThickness : Math.Max(1, config.RingThickness * 0.45f);
            if (config.RingOutline)
                draw.AddLine(p1, p2, ImGui.GetColorU32(new Vector4(0.015f, 0.02f, 0.03f,
                    0.65f * (highlight ? activeColor.W : config.RingBaseColor.W))), thickness + 2);
            draw.AddLine(p1, p2, highlight ? (filled ? bright : dark) : dim, thickness);
        }
        if (config.RingQuarterLines)
        {
            for (var quarter = 0; quarter < 4; quarter++)
                for (var step = 0; step < 16; step++)
                {
                    if (groundRing.Dividers[quarter, step] is not { } a || groundRing.Dividers[quarter, step + 1] is not { } b
                        || MathF.Abs(a.Y - b.Y) > 1) continue;
                    var offset = new Vector3(0, config.RingHeight, 0);
                    if (!GameGui.WorldToScreen(a + offset, out var pa) || !GameGui.WorldToScreen(b + offset, out var pb)) continue;
                    if (config.RingOutline) draw.AddLine(pa, pb, 0xA6000000, 3);
                    draw.AddLine(pa, pb, dim, 1.5f);
                }
        }
        if (config.RingTimer && (eta.HasValue || current.GcdsUntil > 0))
        {
            // Place the timer next to a visible ring point, not at the target model's height.
            var anchorIndex = ((int)MathF.Round((rotation + MathF.PI) / MathF.Tau * segments) % segments + segments) % segments;
            if (groundRing.Points[anchorIndex] is { } anchor && GameGui.WorldToScreen(anchor + new Vector3(0, config.RingHeight, 0), out var labelPos))
            {
                var time = eta?.ToString("0.0", config.Language == 1 ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture);
                var label = time != null ? $"~{time} {T("s", "с")}" : $"{current.GcdsUntil} GCD";
                var textSize = ImGui.CalcTextSize(label);
                labelPos += new Vector2(-textSize.X / 2, 10);
                draw.AddRectFilled(labelPos - new Vector2(5, 3), labelPos + textSize + new Vector2(5, 3),
                    ImGui.GetColorU32(new Vector4(0.02f, 0.025f, 0.035f, 0.82f)), 4);
                draw.AddText(labelPos, bright, label);
            }
        }
        draw.PopClipRect();
    }

    private void DrawHud(Hint current, float? eta, bool demo)
    {
        var scale = config.Scale;
        var viewport = ImGui.GetMainViewport();
        var size = new Vector2(300, 110) * scale;
        var center = viewport.Pos + viewport.Size * 0.5f;
        var desired = Vector2.Clamp(center + config.Offset, viewport.Pos,
            Vector2.Max(viewport.Pos, viewport.Pos + viewport.Size - size));
        ImGui.SetNextWindowPos(desired, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.87f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 9 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 9) * scale);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
        if (!demo) flags |= ImGuiWindowFlags.NoInputs;
        if (ImGui.Begin("Positional Cue HUD###PositionalCueHud", flags))
        {
            ImGui.SetWindowFontScale(scale);
            var top = ImGui.GetCursorScreenPos();
            if (demo)
            {
                ImGui.InvisibleButton("###DragHud", ImGui.GetContentRegionAvail());
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0))
                {
                    var next = Vector2.Clamp(desired + ImGui.GetIO().MouseDelta, viewport.Pos,
                        Vector2.Max(viewport.Pos, viewport.Pos + viewport.Size - size));
                    config.Offset = next - center;
                    hudDragDirty = true;
                }
                ImGui.SetCursorScreenPos(top);
            }
            var color = current.Satisfied ? new Vector4(0.40f, 0.91f, 0.65f, 1) : new Vector4(1, 0.72f, 0.30f, 1);
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
            ImGui.TextColored(color, current.Direction == Direction.Rear ? T("REAR", "СЗАДИ") : T("FLANK", "СБОКУ"));
            var time = eta?.ToString("0.0", config.Language == 1 ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture);
            ImGui.TextUnformatted(time != null ? $"~{time} {T("s", "с")} | {current.GcdsUntil} GCD"
                : current.GcdsUntil > 0 ? $"{current.GcdsUntil} GCD" : T("Timing unavailable", "Время неизвестно"));
            ImGui.TextColored(color, current.Satisfied ? T("Correct sector", "В нужном секторе") : T("Change position", "Смените позицию"));
            ImGui.EndGroup();
            ImGui.TextDisabled(demo ? T("PREVIEW — drag to move", "ПРЕДПРОСМОТР — перетащите мышью") : actionName);
            ImGui.SetWindowFontScale(1);
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(590, 570), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(T("Positional Cue — Settings", "Positional Cue — Настройки") + "###PositionalCueSettings", ref settingsOpen, ImGuiWindowFlags.NoCollapse))
        {
            var changed = ImGui.Combo(L("Language", "Язык"), ref config.Language, new[] { "English", "Русский" }, 2);
            if (ImGui.BeginTabBar("###SettingsTabs"))
            {
                if (ImGui.BeginTabItem(L("Display", "Отображение")))
                {
                    changed |= ImGui.Checkbox(L("Enable hints", "Включить подсказки"), ref config.Enabled);
                    var source = (int)config.Source;
                    if (ImGui.Combo(L("Rotation source", "Источник ротации"), ref source,
                        new[] { "Wrath Combo", "BossMod Reborn", "Rotation Solver Reborn", T("Auto (active rotation)", "Авто (активная ротация)") }, 4))
                    {
                        config.Source = (RotationSource)source;
                        Clear(T("Source changed", "Источник изменён"));
                        changed = true;
                    }
                    if (config.Source != RotationSource.Wrath)
                        ImGui.TextWrapped(T("Reborn/RSR supply direction only, drawn on your selected target; keep it aligned with the rotation's target. No action timer. The chime sounds when the hint appears.",
                            "Reborn/RSR сообщают только сторону для отображения на выбранной цели: она должна совпадать с целью ротации. Без таймера скилла. Звук — при появлении подсказки."));
                    ImGui.TextWrapped(status);
                    changed |= ImGui.Combo(L("Display mode", "Режим отображения"), ref config.DisplayMode,
                        new[] { T("HUD window", "Окно HUD"), T("Target ring", "Кольцо вокруг цели") }, 2);
                    changed |= ImGui.Checkbox(L("Only in combat", "Только в бою"), ref config.CombatOnly);
                    changed |= ImGui.Checkbox(L("Hide in correct sector", "Скрывать в правильном секторе"), ref config.HideWhenCorrect);
                    changed |= ImGui.Checkbox(L("Hide during True North", "Скрывать во время True North"), ref config.HideDuringTrueNorth);
                    changed |= ImGui.SliderInt(L("Lookahead (GCD)", "Упреждение (GCD)"), ref config.LookAheadGcds, 1, 3);
                    if (config.DisplayMode == 0)
                    {
                        changed |= ImGui.SliderFloat(L("Scale", "Масштаб"), ref config.Scale, 0.7f, 2f, "%.2f");
                        if (ImGui.Button(L("Reset position", "Сбросить положение"))) { config.Offset = new(110, 100); changed = true; }
                        if (ImGui.TreeNode(L("Fine adjustment", "Точная настройка")))
                        {
                            changed |= ImGui.DragFloat2(L("Center offset", "Смещение от центра"), ref config.Offset, 1, -4000, 4000, "%.0f");
                            ImGui.TreePop();
                        }
                    }
                    else
                    {
                        changed |= ImGui.SliderFloat(L("Line thickness", "Толщина линии"), ref config.RingThickness, 1, 12, "%.1f");
                        changed |= ImGui.SliderFloat(L("Hitbox padding", "Отступ от хитбокса"), ref config.RingPadding, 0, 3, "%.2f");
                        changed |= ImGui.SliderFloat(L("Vertical offset", "Смещение по высоте"), ref config.RingHeight, -1, 3, "%.2f");
                        changed |= ImGui.ColorEdit4(L("Required sector", "Нужный сектор"), ref config.RingRequiredColor, ImGuiColorEditFlags.AlphaBar);
                        changed |= ImGui.ColorEdit4(L("Correct position", "Правильная позиция"), ref config.RingCorrectColor, ImGuiColorEditFlags.AlphaBar);
                        changed |= ImGui.ColorEdit4(L("Rest of ring", "Остальное кольцо"), ref config.RingBaseColor, ImGuiColorEditFlags.AlphaBar);
                        changed |= ImGui.Checkbox(L("Contrast outline", "Контрастная обводка"), ref config.RingOutline);
                        changed |= ImGui.Checkbox(L("Countdown fill", "Заполнение по таймеру"), ref config.RingCountdownFill);
                        changed |= ImGui.Checkbox(L("Show time remaining", "Показывать оставшееся время"), ref config.RingTimer);
                        changed |= ImGui.Checkbox(L("Quarter boundaries", "Границы четвертей"), ref config.RingQuarterLines);
                        if (ImGui.Button(L("Reset ring colors", "Сбросить цвета кольца")))
                        {
                            config.RingRequiredColor = new(1, 0.72f, 0.25f, 1);
                            config.RingCorrectColor = new(0.4f, 0.95f, 0.65f, 1);
                            config.RingBaseColor = new(0.65f, 0.72f, 0.8f, 0.55f);
                            changed = true;
                        }
                    }
                    ImGui.Separator();
                    ImGui.Checkbox(L("Preview", "Предпросмотр"), ref preview);
                    if (preview)
                    {
                        ImGui.Checkbox(L("Rear (otherwise flank)", "Сзади (иначе сбоку)"), ref previewRear);
                        ImGui.Checkbox(L("Correct sector", "Правильный сектор"), ref previewCorrect);
                        ImGui.SliderFloat(L("Preview time (seconds)", "Время в предпросмотре (сек.)"), ref previewSeconds, 0, 8, "%.1f");
                        ImGui.TextWrapped(config.DisplayMode == 0
                            ? T("Drag the preview window to position it. Release to save.", "Перетащите окно предпросмотра. Положение сохранится при отпускании мыши.")
                            : T("Select a target to preview its ring outside combat.", "Выберите цель для предпросмотра кольца вне боя."));
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(L("Player dot", "Точка персонажа")))
                {
                    changed |= ImGui.Checkbox(L("Show player dot", "Показывать точку персонажа"), ref config.RingPlayerDot);
                    changed |= ImGui.Checkbox(L("Dot only in combat", "Точка только в бою"), ref config.PlayerDotCombatOnly);
                    changed |= ImGui.Checkbox(L("Require selected target", "Только с выбранной целью"), ref config.PlayerDotRequireTarget);
                    changed |= ImGui.SliderFloat(L("Dot size", "Размер точки"), ref config.RingPlayerDotSize, 2, 10, "%.1f");
                    changed |= ImGui.ColorEdit4(L("Dot color", "Цвет точки"), ref config.PlayerDotColor, ImGuiColorEditFlags.AlphaBar);
                    changed |= ImGui.SliderFloat(L("Shared ground offset", "Общее смещение от земли"), ref config.RingHeight, -1, 3, "%.2f");
                    ImGui.Checkbox(L("Preview", "Предпросмотр"), ref preview);
                    ImGui.TextWrapped(T("Independent of hints, ring and rotation source. Shares the ring's ground offset. Preview bypasses the combat restriction.",
                        "Не зависит от подсказок, кольца и источника ротации. Смещение от земли общее с кольцом. Предпросмотр работает вне боя."));
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(L("Sound", "Звук")))
                {
                    changed |= ImGui.Checkbox(L("Soft chime", "Мягкий сигнал"), ref config.SoundEnabled);
                    changed |= ImGui.SliderFloat(L("Volume", "Громкость"), ref config.Volume, 0, 0.4f, "%.2f");
                    changed |= ImGui.SliderFloat(L("Warning lead (seconds)", "Упреждение сигнала (сек.)"), ref config.SoundLeadSeconds, 0.3f, 3f, "%.1f");
                    if (ImGui.Button(L("Test sound", "Проверить звук"))) chime.Play(config.Volume);
                    ImGui.TextWrapped(T("Plays once when the next positional needs a sector change. Combat only.", "Один сигнал перед ближайшей позиционкой, если нужно сменить сектор. Только в бою."));
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(L("Dependencies", "Зависимости")))
                {
                    DrawDependency("Wrath Combo", "WrathCombo", config.Source == RotationSource.Wrath, hintIpc.HasFunction);
                    ImGui.Separator();
                    DrawDependency("BossMod Reborn", "BossModReborn", config.Source == RotationSource.BossMod,
                        Loaded("BossModReborn") && !Loaded("BossMod") && Pi.GetIpcSubscriber<int>("BossMod.Hints.RecommendedPositional").HasFunction);
                    ImGui.Separator();
                    DrawDependency("Rotation Solver Reborn", "RotationSolver", config.Source == RotationSource.RotationSolver,
                        Pi.GetIpcSubscriber<byte>("RotationSolverReborn.GetDesiredPositional").HasFunction);
                    ImGui.Separator();
                    ImGui.TextWrapped(status);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (changed) { config.Normalize(); Save(); }
        }
        ImGui.End();
        if (!settingsOpen) preview = false;
    }

    private void DrawDependency(string name, string internalName, bool required, bool ipcAvailable)
    {
        var plugin = Pi.InstalledPlugins.FirstOrDefault(p => p.InternalName == internalName);
        ImGui.TextUnformatted(name + " — " + (required ? T("Required", "Обязательный") : T("Optional integration", "Дополнительная интеграция")));
        ImGui.TextUnformatted(T("Installed: ", "Установлен: ") + (plugin != null ? T("Yes", "Да") : T("No", "Нет")));
        ImGui.TextUnformatted(T("Loaded: ", "Загружен: ") + (plugin?.IsLoaded == true ? T("Yes", "Да") : T("No", "Нет")));
        if (plugin != null) ImGui.TextUnformatted(T("Version: ", "Версия: ") + plugin.Version);
        ImGui.TextColored(ipcAvailable ? new Vector4(0.4f, 0.9f, 0.65f, 1) : new Vector4(1, 0.7f, 0.3f, 1),
            "IPC: " + (ipcAvailable ? T("Available", "Доступен") : T("Unavailable", "Недоступен")));
    }
}
