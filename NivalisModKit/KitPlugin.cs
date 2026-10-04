using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace NivalisModKit;

/// <summary>BepInEx entry point. Mods use the static API, not this class.</summary>
[BepInPlugin(ModKit.Guid, ModKit.Name, ModKit.Version)]
public class KitPlugin : BasePlugin
{
    internal static ManualLogSource L;
    internal static ConfigEntry<string> SimulateMissing;
    internal static ConfigEntry<bool> BridgeEnabled;
    internal static ConfigEntry<int> BridgePort;
    internal static ConfigEntry<bool> BridgeCommands, ConsoleEnabled;
    internal static ConfigEntry<string> ConsoleKey, ConsoleFont;
    internal static ConfigEntry<bool> ModMenuEnabled, ShowOtherMods;
    internal static ConfigEntry<UntestedBuildMode> UntestedBuild;

    /// <summary>What the kit does on a game build it wasn't tested on.</summary>
    public enum UntestedBuildMode
    {
        /// <summary>Run normally; warn in the log, on the title screen and once in game.</summary>
        Warn,
        /// <summary>Install nothing: the game runs as if no kit mods were installed.</summary>
        Disable,
    }
    internal static ConfigEntry<string> ModMenuLabel, PreferredBrowser;

    static ConfigDescription Desc(string text, ModSetting flags) => new(text, null, flags);

    /// <summary>Called by BepInEx.</summary>
    public override void Load()
    {
        L = Log;
        var advanced = new ModSetting { IsAdvanced = true };
        var restart = new ModSetting { RequiresRestart = true };
        var advancedRestart = new ModSetting { IsAdvanced = true, RequiresRestart = true };
        SimulateMissing = Config.Bind("Debug", "SimulateMissing", "", Desc(
            "Comma-separated event names to install against a method that doesn't exist, " +
            "to test the missing-event fallback. Leave empty.", new ModSetting { Browsable = false }));
        var liveReload = Config.Bind("General", "LiveConfigReload", true, Desc(
            "Reload a mod's settings when its .cfg file in BepInEx\\config is saved, while the game runs.", restart));
        UntestedBuild = Config.Bind("General", "UntestedBuild", UntestedBuildMode.Warn, Desc(
            "On a game build this kit version wasn't tested on (usually right after a game patch): Warn = run, " +
            "with a warning on the title screen and in game; Disable = the kit installs nothing, so its mods are " +
            "inactive and the game runs unmodded until the kit is updated.", restart));
        ModMenuEnabled = Config.Bind("ModMenu", "Enabled", true,
            "Add a Mods button to the pause menu, for mods' settings and pages.");
        ShowOtherMods = Config.Bind("ModMenu", "ShowOtherMods", false,
            "Also list mods that don't offer their settings for in-game changes, read-only. Change those in their .cfg file and restart.");
        ModMenuLabel = Config.Bind("ModMenu", "ButtonLabel", "Mods", Desc("Text on the pause menu button.", restart));
        PreferredBrowser = Config.Bind("ModMenu", "Browser", "", Desc(
            "GUID of the mod whose browser the Mods button opens, when more than one mod provides one. " +
            "Empty = the last one loaded; " + ModKit.Guid + " = the kit's own.", advanced));
        BridgeEnabled = Config.Bind("DevBridge", "Enabled", false, Desc(
            "Read-only HTTP endpoint on 127.0.0.1 for development tools to query the running game. " +
            "Only this computer can reach it. Takes effect after a restart.", advancedRestart));
        BridgePort = Config.Bind("DevBridge", "Port", 5710, Desc("Port for the dev bridge.", advancedRestart));
        BridgeCommands = Config.Bind("DevBridge", "AllowCommands", false, Desc(
            "Let development tools run mods' dev commands through the bridge (POST /cmd/name), with this session's " +
            "token from BepInEx\\cache. Commands change the game (open screens, set the clock, whatever mods register). " +
            "Leave off unless you're developing.", advanced));
        ConsoleEnabled = Config.Bind("DevConsole", "Enabled", false, Desc(
            "In-game console for mods' dev commands: press Key (` by default) in gameplay, type a command (help lists them), Enter. " +
            "Commands change the game; leave off unless you're developing or testing.", advanced));
        ConsoleKey = Config.Bind("DevConsole", "Key", "Backquote", Desc(
            "Key that opens and closes the console (a Unity Input System key name: Backquote is the ` key; F8, Insert...).", advanced));
        ConsoleFont = Config.Bind("DevConsole", "Font", "", Desc(
            "Font for the console, by (part of) its name; the log lists the game's fonts the first time the console " +
            "opens. Empty = a plain one picked automatically. Applies after a restart.", advanced));
        var frameTiming = Config.Bind("Debug", "FrameTiming", false, Desc(
            "Log frames slower than FrameThresholdMs with how much of them was the kit's own work, and keep " +
            "per-event timings (dev bridge /perf). For diagnosing lag; leave off otherwise.", advanced));
        var frameThreshold = Config.Bind("Debug", "FrameThresholdMs", 50f, Desc("Frame time (ms) that counts as slow for FrameTiming.", advanced));
        Perf.On = frameTiming.Value;
        Perf.ThresholdMs = frameThreshold.Value;
        frameTiming.SettingChanged += (_, _) => { Perf.On = frameTiming.Value; if (Perf.On) Perf.Reset(); };
        frameThreshold.SettingChanged += (_, _) => Perf.ThresholdMs = frameThreshold.Value;

        L.LogInfo($"{ModKit.Name} {ModKit.Version} loaded");
        GameBuild.Detect();
        if (!GameBuild.IsTested && UntestedBuild.Value == UntestedBuildMode.Disable)
        {
            L.LogError($"{ModKit.Name}: untested game build and [General] UntestedBuild = Disable, so the kit installs " +
                       "nothing. Mods using the kit are inactive until the kit is updated (or set UntestedBuild = Warn).");
            return;
        }
        EventPatches.InstallAll();
        ModMenu.Install();
        ModMenu.ListSettings(ModKit.Guid);
        DevCommands.RegisterBuiltIns();
        MenuMode.Install();
        DevConsole.Install();
        ConfigBrowser.Install();
        TitleLine.Install();
        StartServices(liveReload.Value);
    }

    void StartServices(bool liveReload)
    {
        try { AddComponent<KitBehaviour>(); }
        catch (Exception e)
        {
            L.LogError($"Kit component: missing, per-frame services off ({e.Message})");
            return;
        }

        if (liveReload)
        {
            try { ConfigWatcher.Start(); }
            catch (Exception e) { L.LogError($"Live config reload: missing ({e.Message})"); }
        }

        if (BridgeEnabled.Value)
        {
            try { Bridge.Start(BridgePort.Value); }
            catch (Exception e) { L.LogError($"Dev bridge: could not start on port {BridgePort.Value} ({e.Message})"); }
        }
    }


}
