using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using Nivalis;

namespace NivalisModKit;

// Warns when a save uses custom content that is no longer installed.
//
// The game skips what it can't find when a save loads: items, recipes and placed objects from a removed content mod
// vanish for the session, and the next save drops them for good. Until then the save file still has them, so the
// player can quit, reinstall and get everything back. This tells them before they save.
//
// Two sources:
//  - The game's own complaints while the save loads (Unity warnings with the missing GUID). They say exactly what this
//    save used and couldn't find, so content that was installed but never used doesn't raise a warning.
//  - A record of the kit's content written into each save's .modkit.json, which turns those GUIDs back into names
//    and mods. Saves from before the record existed still warn, without names.
// If Unity's log isn't reaching BepInEx (UnityLogListening off), the record alone is used: everything recorded that's
// no longer installed, used or not.
internal static class ContentGuard
{
    const string RecordKey = "content";

    sealed class Entry
    {
        public string Guid { get; set; }
        public string Kind { get; set; }    // item, placed, recipe
        public string Owner { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
    }

    static readonly Regex[] complaints =
    {
        new(@"SO reference of type ItemType with GUID ([0-9a-fA-F-]{36})"),
        new(@"Couldn't find saved recipe with GUID ([0-9a-fA-F-]{36})"),
        new(@"Cannot find prefab with id ([0-9a-fA-F-]{36})"),
    };

    static readonly object gate = new();
    static readonly HashSet<string> missing = new(StringComparer.OrdinalIgnoreCase);
    static bool listening, sawUnityLog, saveLoaded;
    static ModSaveData store;

    internal static void Install(Harmony harmony)
    {
        store = SaveData.For(ModKit.Guid);
        Logger.Listeners.Add(new Listener());
        harmony.Patch(AccessTools.Method(typeof(SerializationManager), nameof(SerializationManager.Load)),
            prefix: new HarmonyMethod(typeof(ContentGuard), nameof(StartListening)));
        GameEvents.GameLoaded += _ => saveLoaded = true;
        GameEvents.NewGameStarted += Stop;
        GameEvents.GameReady += OnReady;
        SaveData.Saving += Record;
    }

    static void StartListening()
    {
        lock (gate) { missing.Clear(); listening = true; sawUnityLog = false; }
        saveLoaded = false;
    }

    static void Stop()
    {
        lock (gate) { listening = false; missing.Clear(); }
        saveLoaded = false;
    }

    // Some complaints come after the game is ready (placed objects are spawned as districts settle), so wait a little.
    static void OnReady()
    {
        if (!saveLoaded) { Stop(); return; }
        Scheduler.AfterSeconds(5f, Check);
    }

    static void Check()
    {
        string[] guids;
        bool fallback;
        lock (gate)
        {
            listening = false;
            guids = missing.ToArray();
            fallback = !sawUnityLog;
            missing.Clear();
        }
        saveLoaded = false;
        if (!KitPlugin.WarnMissingContent.Value) return;

        var recorded = (store.Get<List<Entry>>(RecordKey) ?? new List<Entry>())
            .Where(e => !string.IsNullOrEmpty(e.Guid))
            .GroupBy(e => e.Guid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var installed = new HashSet<string>(Current().Select(e => e.Guid), StringComparer.OrdinalIgnoreCase);

        var found = new List<Entry>();
        int unknown = 0;
        if (fallback)
        {
            found.AddRange(recorded.Values.Where(e => !installed.Contains(e.Guid)));
            if (found.Count > 0)
                KitPlugin.L.LogWarning("Content: Unity's log isn't reaching BepInEx ([Logging] UnityLogListening), so the " +
                                       "missing-content check lists everything recorded in this save that isn't installed now");
        }
        else
        {
            foreach (var g in guids)
            {
                if (recorded.TryGetValue(g, out var e)) found.Add(e);
                else unknown++;
                if (installed.Contains(g))
                    KitPlugin.L.LogWarning($"Content: the game couldn't find {g} although it's installed; please report this");
            }
        }
        if (found.Count == 0 && unknown == 0) return;

        // One line per thing: an item and its placed copy are the same thing to the player.
        var things = found
            .GroupBy(e => (e.Owner, e.Id))
            .Select(g => g.OrderBy(e => e.Kind == "recipe" ? 1 : 0).First())
            .OrderBy(e => e.Owner).ThenBy(e => e.Name).ToList();
        foreach (var e in found)
            KitPlugin.L.LogWarning($"Content: this save uses {(e.Kind == "placed" ? "placed object" : e.Kind)} '{e.Name}' ({e.Owner}/{e.Id}, {e.Guid}), which isn't installed");
        if (unknown > 0)
            KitPlugin.L.LogWarning($"Content: this save uses {unknown} other thing(s) the game couldn't find " +
                                   "(saved before the kit kept a record, or added another way)");
        KitPlugin.L.LogWarning("Content: saving now removes them from this save; quit without saving and reinstall to keep them");

        var text = new StringBuilder("This save uses things from mods that aren't installed or didn't load:\n\n");
        const int shown = 8;
        foreach (var e in things.Take(shown))
            text.Append($"• {e.Name}{(e.Kind == "recipe" ? " (recipe)" : "")}  <size=85%><alpha=#99>{e.Owner}</size><alpha=#FF>\n");
        if (things.Count > shown) text.Append($"• ...and {things.Count - shown} more (listed in the log)\n");
        if (unknown > 0) text.Append($"• {unknown} unnamed {(unknown == 1 ? "thing" : "things")} (saved before the kit kept a record)\n");
        text.Append("\nIf you save now, they will be removed from this save for good. To keep them, quit without saving, " +
                    "reinstall the mods, then load this save again.");
        Ui.Dialog("Missing mod content", text.ToString(), ("I understand", null));
    }

    // Everything the kit has built right now, as the record stores it.
    static IEnumerable<Entry> Current()
    {
        foreach (var c in Content.AllItems.Where(c => c.Item != null))
        {
            yield return new Entry { Guid = c.Guid, Kind = "item", Owner = c.Owner, Id = c.Id, Name = c.Spec.Name };
            if (!string.IsNullOrEmpty(c.PrefabGuid))
                yield return new Entry { Guid = c.PrefabGuid, Kind = "placed", Owner = c.Owner, Id = c.Id, Name = c.Spec.Name };
        }
        foreach (var r in Content.AllRecipes.Where(r => r.Recipe != null))
            yield return new Entry { Guid = r.Guid, Kind = "recipe", Owner = r.Owner, Id = r.Id, Name = r.Spec?.Output ?? r.Id };
    }

    // On save: what the save may now reference. Content that was missing at load is gone from the save by now.
    static void Record()
    {
        var list = Current().ToList();
        if (list.Count == 0) store.Remove(RecordKey);
        else store.Set(RecordKey, list);
    }

    sealed class Listener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;

        public void LogEvent(object sender, LogEventArgs e)
        {
            if (!listening || e.Source?.SourceName != "Unity") return;
            string msg = e.Data?.ToString();
            lock (gate)
            {
                if (!listening) return;
                sawUnityLog = true;
                if (string.IsNullOrEmpty(msg)) return;
                foreach (var re in complaints)
                {
                    var m = re.Match(msg);
                    if (m.Success) { missing.Add(m.Groups[1].Value); break; }
                }
            }
        }

        public void Dispose() { }
    }
}
