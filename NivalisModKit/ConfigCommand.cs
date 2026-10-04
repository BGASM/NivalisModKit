using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace NivalisModKit;

// The "config" dev command: read or change any mod's BepInEx settings.
//   config                                   mods with settings
//   config mod=orderfix                      that mod's settings and values
//   config mod=orderfix key=VendorSort       one setting in detail
//   config mod=orderfix key=VendorSort value=Cheapest
// A change goes through BepInEx like an edit in the Mods browser: converted to the setting's type, SettingChanged
// raised, the .cfg saved. It ignores the browser's opt-in and read-only rules: it's a developer tool, behind
// [DevBridge] AllowCommands or [DevConsole] Enabled.
internal static class ConfigCommand
{
    internal static void Register() =>
        DevCommands.Register(ModKit.Guid, "config",
            "[mod=name] [key=Section.Key] [value=...]: list mods with settings, a mod's settings, or change one", Run);

    static object Run(CommandArgs a)
    {
        var plugins = IL2CPPChainloader.Instance.Plugins.Values
            .Select(i => (info: i, cfg: (i.Instance as BasePlugin)?.Config))
            .Where(p => p.cfg != null && p.cfg.Count > 0)
            .ToList();

        if (!a.Has("mod"))
            return plugins.OrderBy(p => p.info.Metadata.Name)
                .Select(p => new { mod = p.info.Metadata.GUID, name = p.info.Metadata.Name, settings = p.cfg.Count })
                .ToArray();

        var (info, cfg) = FindMod(plugins, a.Get("mod"));
        string modName = info.Metadata.Name;

        if (!a.Has("key"))
            return new
            {
                mod = info.Metadata.GUID,
                file = Path.GetFileName(cfg.ConfigFilePath),
                settings = cfg.Keys.OrderBy(k => k.Section).ThenBy(k => k.Key)
                    .Select(k => $"{k.Section}.{k.Key} = {cfg[k].GetSerializedValue()}").ToArray(),
            };

        var entry = FindKey(cfg, a.Get("key"), modName);
        string key = $"{entry.Definition.Section}.{entry.Definition.Key}";

        if (!a.Has("value"))
            return new
            {
                mod = info.Metadata.GUID,
                key,
                value = entry.GetSerializedValue(),
                type = entry.SettingType.Name,
                @default = entry.GetType().GetProperty("DefaultValue")?.GetValue(entry)?.ToString(),
                allowed = Allowed(entry),
                description = entry.Description?.Description,
            };

        string before = entry.GetSerializedValue();
        string requested = a.Get("value");
        bool save = cfg.SaveOnConfigSet;
        cfg.SaveOnConfigSet = false;
        try { entry.SetSerializedValue(requested); }   // BepInEx converts; text it can't read leaves the value as it was
        finally { cfg.SaveOnConfigSet = save; }
        string after = entry.GetSerializedValue();

        if (after == before && !string.Equals(requested, before, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{key} didn't accept '{requested}' (a {entry.SettingType.Name}" +
                                        (Allowed(entry) is string allowed ? $"; {allowed}" : "") + ")");
        cfg.Save();
        ConfigWatcher.MarkOwnWrite(cfg.ConfigFilePath);   // live reload doesn't need to re-read our own write
        KitPlugin.L.LogInfo($"DevCommands: config {info.Metadata.GUID} {key}: {before} -> {after}");
        return new { mod = info.Metadata.GUID, key, before, now = after };
    }

    // What a setting accepts: its acceptable values, or an enum's names; null for anything goes.
    static string Allowed(ConfigEntryBase entry)
    {
        var av = entry.Description?.AcceptableValues;
        if (av != null) return av.ToDescriptionString().TrimStart('#', ' ');
        if (entry.SettingType.IsEnum) return "Acceptable values: " + string.Join(", ", Enum.GetNames(entry.SettingType));
        return null;
    }

    // GUID exactly, else part of the GUID or name; more than one match is an error that lists them.
    static (PluginInfo info, ConfigFile cfg) FindMod(List<(PluginInfo info, ConfigFile cfg)> plugins, string query)
    {
        var exact = plugins.Where(p => p.info.Metadata.GUID.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        var matches = plugins.Where(p =>
            p.info.Metadata.GUID.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            p.info.Metadata.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0) throw new ArgumentException($"no mod with settings matches '{query}' (config lists them)");
        throw new ArgumentException($"'{query}' matches {matches.Count} mods: {string.Join(", ", matches.Select(m => m.info.Metadata.GUID))}");
    }

    // "Section.Key" exactly, else the key alone when only one section has it.
    static ConfigEntryBase FindKey(ConfigFile cfg, string query, string modName)
    {
        var keys = cfg.Keys.ToList();
        var exact = keys.Where(k => $"{k.Section}.{k.Key}".Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return cfg[exact[0]];
        var byKey = keys.Where(k => k.Key.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byKey.Count == 1) return cfg[byKey[0]];
        if (byKey.Count > 1) throw new ArgumentException($"'{query}' is in several sections: {string.Join(", ", byKey.Select(k => $"{k.Section}.{k.Key}"))}");
        throw new ArgumentException($"{modName} has no setting '{query}' (config mod=... lists them)");
    }
}
