using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using System.Globalization;
using System.Numerics;

namespace PositionalCue;

public sealed partial class Plugin
{
    private bool hudDragDirty;
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
            DrawIndicator(new(previewRear ? Direction.Rear : Direction.Flank, 0, 1, 1, 1000, previewCorrect), 0.9f, true);
        else if (config.Enabled && hint is { } current && !(config.HideWhenCorrect && current.Satisfied))
            DrawIndicator(current, seconds, false);
    }

    private void DrawIndicator(Hint current, float? eta, bool demo)
    {
        if (config.DisplayMode == 1) DrawRing(current, demo);
        else DrawHud(current, eta, demo);
    }

    private void DrawRing(Hint current, bool demo)
    {
        var target = Targets.Target;
        if (Objects.LocalPlayer == null || target == null || (!demo && (uint)target.GameObjectId != current.TargetId)) return;
        var radius = Math.Max(0.5f, target.HitboxRadius) + config.RingPadding;
        var center = target.Position + new Vector3(0, 0.05f, 0);
        var rotation = target.Rotation;
        if (!float.IsFinite(radius) || !float.IsFinite(rotation) || radius > 200) return;
        var draw = ImGui.GetBackgroundDrawList();
        var viewport = ImGui.GetMainViewport();
        draw.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, true);
        var bright = ImGui.GetColorU32(current.Satisfied ? new Vector4(0.4f, 0.95f, 0.65f, 1) : new Vector4(1, 0.72f, 0.25f, 1));
        var dim = ImGui.GetColorU32(new Vector4(0.65f, 0.72f, 0.8f, 0.4f));
        const int segments = 128;
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathF.Tau / segments;
            var b = (i + 1) * MathF.Tau / segments;
            if (!GameGui.WorldToScreen(RingGeometry.Point(center, rotation, radius, a), out var p1)
                || !GameGui.WorldToScreen(RingGeometry.Point(center, rotation, radius, b), out var p2)) continue;
            if (!float.IsFinite(p1.X) || !float.IsFinite(p1.Y) || !float.IsFinite(p2.X) || !float.IsFinite(p2.Y)) continue;
            var highlight = RingGeometry.Highlight((a + b) / 2, current.Direction);
            draw.AddLine(p1, p2, highlight ? bright : dim, highlight ? config.RingThickness : Math.Max(1, config.RingThickness * 0.35f));
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
            ImGui.TextUnformatted(time != null ? $"~{time} {T("s", "с")} | {current.GcdsUntil} GCD" : $"{current.GcdsUntil} GCD");
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
                    changed |= ImGui.Checkbox(L("Enable", "Включить"), ref config.Enabled);
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
                    }
                    ImGui.Separator();
                    ImGui.Checkbox(L("Preview", "Предпросмотр"), ref preview);
                    if (preview)
                    {
                        ImGui.Checkbox(L("Rear (otherwise flank)", "Сзади (иначе сбоку)"), ref previewRear);
                        ImGui.Checkbox(L("Correct sector", "Правильный сектор"), ref previewCorrect);
                        ImGui.TextWrapped(config.DisplayMode == 0
                            ? T("Drag the preview window to position it. Release to save.", "Перетащите окно предпросмотра. Положение сохранится при отпускании мыши.")
                            : T("Select a target to preview its ring outside combat.", "Выберите цель для предпросмотра кольца вне боя."));
                    }
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
                    DrawDependency("Wrath Combo", "WrathCombo", true, hintIpc.HasFunction);
                    ImGui.Separator();
                    DrawDependency("BossMod", "BossMod", false, Pi.GetIpcSubscriber<string?>("BossMod.Presets.GetActive").HasFunction);
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
