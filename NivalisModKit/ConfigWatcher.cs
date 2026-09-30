using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace NivalisModKit;

// Reloads a plugin's ConfigFile when its .cfg changes on disk, so edits take effect while the
// game runs. The watcher's events arrive on a worker thread; they only queue the path, and the
// reload happens on the main thread in KitLoop, where mods' SettingChanged handlers are safe.
internal static class ConfigWatcher
{
    const double DebounceMs = 300;   // editors often write a file in several steps
    const int MaxAttempts = 10;      // retries while another process holds the file

    static FileSystemWatcher watcher;
    static readonly object gate = new();
    static readonly Dictionary<string, (DateTime when, int attempts)> pending =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void Start()
    {
        watcher = new FileSystemWatcher(Paths.ConfigPath, "*.cfg")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        watcher.Changed += (_, e) => Queue(e.FullPath, 0);
        watcher.Created += (_, e) => Queue(e.FullPath, 0);
        watcher.Renamed += (_, e) => Queue(e.FullPath, 0);
        watcher.EnableRaisingEvents = true;

        KitLoop.Tick += Poll;
        KitPlugin.L.LogInfo("Live config reload: watching BepInEx\\config");
    }

    static void Queue(string path, int attempts)
    {
        lock (gate) pending[Path.GetFullPath(path)] = (DateTime.UtcNow, attempts);
    }

    static void Poll()
    {
        List<(string path, DateTime when, int attempts)> ready = null;
        lock (gate)
        {
            if (pending.Count == 0) return;
            var now = DateTime.UtcNow;
            foreach (var kv in pending)
                if ((now - kv.Value.when).TotalMilliseconds >= DebounceMs)
                    (ready ??= new()).Add((kv.Key, kv.Value.when, kv.Value.attempts));
        }
        if (ready == null) return;

        foreach (var (path, when, attempts) in ready)
        {
            bool done = TryReload(path);
            lock (gate)
            {
                // A newer write may have arrived meanwhile; leave that one queued.
                if (!pending.TryGetValue(path, out var cur) || cur.when != when) continue;
                if (done || attempts + 1 >= MaxAttempts) pending.Remove(path);
                else pending[path] = (DateTime.UtcNow, attempts + 1);
            }
        }
    }

    // False means the file couldn't be read yet; try again shortly.
    static bool TryReload(string path)
    {
        ConfigFile cfg = Find(path);
        if (cfg == null) return true;   // not a loaded plugin's config (e.g. BepInEx.cfg)

        var before = Snapshot(cfg);
        try { cfg.Reload(); }
        catch (IOException) { return false; }

        var changes = Snapshot(cfg)
            .Where(kv => !before.TryGetValue(kv.Key, out var old) || old != kv.Value)
            .Select(kv => $"{kv.Key.Key}: {(before.TryGetValue(kv.Key, out var old) ? old : "?")} -> {kv.Value}")
            .ToList();

        // A mod saving its own settings also triggers a reload; stay quiet when nothing changed.
        if (changes.Count > 0)
            KitPlugin.L.LogInfo($"Config reloaded: {Path.GetFileName(path)}: {string.Join(", ", changes)}");
        return true;
    }

    static ConfigFile Find(string path)
    {
        foreach (var info in IL2CPPChainloader.Instance.Plugins.Values)
        {
            if (info.Instance is not BasePlugin plugin || plugin.Config == null) continue;
            if (string.Equals(Path.GetFullPath(plugin.Config.ConfigFilePath), path, StringComparison.OrdinalIgnoreCase))
                return plugin.Config;
        }
        return null;
    }

    static Dictionary<ConfigDefinition, string> Snapshot(ConfigFile cfg) =>
        cfg.ToDictionary(kv => kv.Key, kv => kv.Value.GetSerializedValue());
}
