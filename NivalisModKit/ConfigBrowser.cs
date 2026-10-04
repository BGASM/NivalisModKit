using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using TMPro;

namespace NivalisModKit;

// The kit's built-in mod browser: mods' pages, then every mod's settings, edited live. Changes are written to the
// mod's .cfg (debounced while a slider drags) and raise the entry's SettingChanged, as editing the file would.
internal static class ConfigBrowser
{
    static KitWindow window;
    static bool showAdvanced;
    static readonly HashSet<ConfigFile> dirty = new();
    static IDisposable saveTimer;

    internal static void Install()
    {
        GameEvents.GameEnded += () => { SaveNow(); window = null; };
    }

    internal static void Open()
    {
        if (window == null || window.Root == null)
        {
            window = Ui.CreateWindow("Mods", WindowStyle.Panel);
            if (window == null) return;
            window.AddFooterButton("Close", window.Hide);
            window.Closed += SaveNow;
        }
        ShowList();
        ModMenu.OpenAsChild(window);
    }

    // ---------- pages ----------

    static void ShowList()
    {
        window.Clear();
        window.SetTitle("Mods");
        foreach (var page in ModMenu.Pages)
        {
            var p = page;
            window.AddButton(p.Title, () => ShowPage(p));
        }
        var mods = Plugins().ToList();
        var editable = mods.Where(m => m.editable).ToList();
        var others = mods.Where(m => !m.editable).ToList();
        if (editable.Count > 0) window.AddHeader("Settings");
        foreach (var m in editable) AddModButton(m.info, m.cfg);
        if (others.Count > 0)
        {
            window.AddHeader("Other mods");
            window.AddText("Read-only: change these in the mod's .cfg file and restart.");
            foreach (var m in others) AddModButton(m.info, m.cfg);
        }
        if (ModMenu.Pages.Count == 0 && mods.Count == 0) window.AddText("No mods have settings to show.");
    }

    static void AddModButton(PluginInfo info, ConfigFile cfg) =>
        window.AddButton($"{info.Metadata.Name}  <size=70%>{info.Metadata.Version}</size>", () => ShowSettings(info, cfg));

    static void ShowPage(ModMenu.Page page)
    {
        window.Clear();
        window.SetTitle(page.Title);
        window.AddButton("< Back", ShowList);
        try { page.Build(window); }
        catch (Exception e)
        {
            KitPlugin.L.LogError($"ModMenu page '{page.Title}' ({page.Owner}): {e}");
            window.AddText("This page failed to open; see the log.");
        }
    }

    static void ShowSettings(PluginInfo info, ConfigFile cfg)
    {
        window.Clear();
        window.SetTitle(info.Metadata.Name);
        window.AddButton("< Back", ShowList);
        var entries = Visible(info, cfg).ToList();
        if (!ModMenu.IsListed(info.Metadata.GUID) && entries.Any(e => !e.flags.Tagged))
            window.AddText("Settings this mod doesn't offer for in-game changes are read-only. To change them, edit " +
                           $@"BepInEx\config\{System.IO.Path.GetFileName(cfg.ConfigFilePath)} and restart the game.");
        if (entries.Any(e => e.flags.IsAdvanced == true))
            window.AddToggle("Show advanced", showAdvanced, v => { showAdvanced = v; ShowSettings(info, cfg); });

        foreach (var section in entries.Where(e => showAdvanced || e.flags.IsAdvanced != true)
                     .GroupBy(e => e.entry.Definition.Section))
        {
            window.AddHeader(Words(section.Key));
            foreach (var (entry, flags) in section.OrderByDescending(e => e.flags.Order ?? int.MinValue))
                AddRow(cfg, entry, flags);
        }
    }

    // ---------- rows ----------

    static void AddRow(ConfigFile cfg, ConfigEntryBase entry, ModSetting flags)
    {
        string name = flags.DisplayName ?? Words(entry.Definition.Key);
        string Label(params string[] notes)
        {
            var all = notes.Prepend(flags.RequiresRestart == true ? "restart" : null).Where(n => n != null).ToList();
            return all.Count == 0 ? name : $"{name} <size=70%>({string.Join(", ", all)})</size>";
        }
        string label = Label();
        var type = entry.SettingType;
        UnityEngine.GameObject row = null;

        if (flags.ReadOnly != true)
        {
            if (type == typeof(bool))
                row = window.AddToggle(label, (bool)entry.BoxedValue, v => Set(cfg, entry, v))?.gameObject;
            else if (IsNumber(type) && Range(entry, out float min, out float max))
            {
                bool whole = type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte);
                row = window.AddSlider(label, min, max, Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture),
                    // Saved as shown: whole numbers, or two decimals (a drag otherwise stores 0.13877352).
                    v => Set(cfg, entry, Convert.ChangeType(Math.Round(v, whole ? 0 : 2), type, CultureInfo.InvariantCulture)),
                    whole ? "0" : "0.##", whole)?.gameObject;
            }
            else if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) && Enum.GetNames(type).Length <= MaxChoices)
            {
                var names = Enum.GetNames(type);
                row = window.AddChoice(label, names, Array.IndexOf(names, entry.BoxedValue.ToString()),
                    i => Set(cfg, entry, Enum.Parse(type, names[i])));
            }
            else if (Choices(entry, out var options) && options.Count <= MaxChoices)
            {
                var shown = options.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToList();
                row = window.AddChoice(label, shown, options.IndexOf(entry.BoxedValue), i => Set(cfg, entry, options[i]));
            }
            else
            {
                // Anything else as text, converted the way the .cfg file is (numbers, text, key bindings, long lists).
                TMP_InputField field = null;
                field = window.AddTextField(label, entry.GetSerializedValue(), text =>
                {
                    SetText(cfg, entry, text);
                    field?.SetTextWithoutNotify(entry.GetSerializedValue());   // shows the value as stored (invalid input reverts)
                }, ContentType(type));
                row = field?.transform.parent?.gameObject;
            }
        }
        // No editor for this type yet (free text, numbers without a range, key bindings), or read-only.
        if (row == null)
        {
            // Listed but no editor could be made: say where to change it.
            if (flags.ReadOnly != true) label = Label("edit the .cfg");
            row = window.AddValue(label, Shorten(entry.GetSerializedValue()))?.transform.parent?.gameObject;
        }
        string desc = entry.Description?.Description;
        if (row != null && !string.IsNullOrEmpty(desc)) Ui.Tooltip(row, Wrap(desc, 60));
    }

    // "LiveConfigReload" -> "Live Config Reload", "UIScale" -> "UI Scale", "max_items" -> "max items".
    internal static string Words(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Contains(' ')) return key;
        return System.Text.RegularExpressions.Regex.Replace(key.Replace('_', ' '),
            "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
    }

    // The game's tooltip doesn't wrap; break long descriptions into lines.
    static string Wrap(string text, int width)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            int line = 0;
            foreach (var word in para.Split(' '))
            {
                if (line > 0 && line + word.Length + 1 > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    static string Shorten(string s) => s == null ? "" : s.Length > 80 ? s.Substring(0, 77) + "..." : s;

    const int MaxChoices = 12;   // longer lists (e.g. every key on the keyboard) are typed instead

    static TMP_InputField.ContentType ContentType(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ? TMP_InputField.ContentType.IntegerNumber
        : t == typeof(float) || t == typeof(double) || t == typeof(decimal) ? TMP_InputField.ContentType.DecimalNumber
        : TMP_InputField.ContentType.Standard;

    static void Set(ConfigFile cfg, ConfigEntryBase entry, object value)
    {
        if (Equals(entry.BoxedValue, value)) return;
        Apply(cfg, entry, () => entry.BoxedValue = value);   // raises the mod's SettingChanged
    }

    static void SetText(ConfigFile cfg, ConfigEntryBase entry, string text)
    {
        if (text == entry.GetSerializedValue()) return;
        Apply(cfg, entry, () => entry.SetSerializedValue(text));   // BepInEx converts, and ignores text it can't read
    }

    static void Apply(ConfigFile cfg, ConfigEntryBase entry, Action change)
    {
        bool save = cfg.SaveOnConfigSet;
        cfg.SaveOnConfigSet = false;   // written once the player stops changing it, not on every slider step
        try { change(); }
        catch (Exception e) { KitPlugin.L.LogError($"ModMenu: {entry.Definition}: {e}"); }
        finally { cfg.SaveOnConfigSet = save; }
        dirty.Add(cfg);
        saveTimer?.Dispose();
        saveTimer = Scheduler.AfterSeconds(0.5f, SaveNow);
    }

    static void SaveNow()
    {
        saveTimer?.Dispose();
        saveTimer = null;
        foreach (var cfg in dirty)
        {
            try { cfg.Save(); ConfigWatcher.MarkOwnWrite(cfg.ConfigFilePath); KitPlugin.L.LogInfo($"ModMenu: saved {System.IO.Path.GetFileName(cfg.ConfigFilePath)}"); }
            catch (Exception e) { KitPlugin.L.LogWarning($"ModMenu: could not save {cfg.ConfigFilePath}: {e.Message}"); }
        }
        dirty.Clear();
    }

    // ---------- reading configs ----------

    // Plugins with at least one visible setting, by name. Editable = at least one setting opted in.
    static IEnumerable<(PluginInfo info, ConfigFile cfg, bool editable)> Plugins() =>
        IL2CPPChainloader.Instance.Plugins.Values
            .Select(i => (info: i, cfg: (i.Instance as BasePlugin)?.Config))
            .Where(p => p.cfg != null)
            .Select(p => (p.info, p.cfg, entries: Visible(p.info, p.cfg).ToList()))
            .Where(p => p.entries.Count > 0)
            .Select(p => (p.info, p.cfg, editable: OptedIn(p.info, p.entries)))
            .OrderBy(p => p.info.Metadata.Name, StringComparer.OrdinalIgnoreCase);

    static bool OptedIn(PluginInfo info, List<(ConfigEntryBase entry, ModSetting flags)> entries) =>
        ModMenu.IsListed(info.Metadata.GUID) || entries.Any(e => e.flags.Tagged);

    // Opt-in: settings tagged with ModSetting, or all of a mod's settings once it called ModMenu.ListSettings.
    // With [ModMenu] ShowOtherMods on, the rest are listed too, read-only.
    static IEnumerable<(ConfigEntryBase entry, ModSetting flags)> Visible(PluginInfo info, ConfigFile cfg)
    {
        bool all = ModMenu.IsListed(info.Metadata.GUID);
        bool others = KitPlugin.ShowOtherMods?.Value == true;
        foreach (var kv in cfg)
        {
            var flags = ModSetting.Of(kv.Value);
            if (flags.Browsable == false) continue;
            if (all || flags.Tagged) yield return (kv.Value, flags);
            else if (others)
            {
                flags.ReadOnly = true;   // not offered by its mod: show, don't edit
                yield return (kv.Value, flags);
            }
        }
    }

    static bool IsNumber(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
        t == typeof(float) || t == typeof(double) || t == typeof(decimal);

    // AcceptableValueRange<T>: generic, so read MinValue / MaxValue by name.
    static bool Range(ConfigEntryBase entry, out float min, out float max)
    {
        min = max = 0f;
        var av = entry.Description?.AcceptableValues;
        if (av == null || !av.GetType().Name.StartsWith("AcceptableValueRange")) return false;
        try
        {
            min = Convert.ToSingle(av.GetType().GetProperty("MinValue")?.GetValue(av), CultureInfo.InvariantCulture);
            max = Convert.ToSingle(av.GetType().GetProperty("MaxValue")?.GetValue(av), CultureInfo.InvariantCulture);
            return max > min;
        }
        catch { return false; }
    }

    // AcceptableValueList<T>: the allowed values.
    static bool Choices(ConfigEntryBase entry, out List<object> options)
    {
        options = null;
        var av = entry.Description?.AcceptableValues;
        if (av == null || !av.GetType().Name.StartsWith("AcceptableValueList")) return false;
        try
        {
            if (av.GetType().GetProperty("AcceptableValues")?.GetValue(av) is not Array values || values.Length == 0) return false;
            options = values.Cast<object>().ToList();
            return true;
        }
        catch { return false; }
    }
}
