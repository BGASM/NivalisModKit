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

    /// <summary>Called by BepInEx.</summary>
    public override void Load()
    {
        L = Log;
        SimulateMissing = Config.Bind("Debug", "SimulateMissing", "",
            "Comma-separated event names to install against a method that doesn't exist, " +
            "to test the missing-event fallback. Leave empty.");
        var liveReload = Config.Bind("General", "LiveConfigReload", true,
            "Reload a mod's settings when its .cfg file in BepInEx\\config is saved, while the game runs.");
        BridgeEnabled = Config.Bind("DevBridge", "Enabled", false,
            "Read-only HTTP endpoint on 127.0.0.1 for development tools to query the running game. " +
            "Only this computer can reach it. Takes effect after a restart.");
        BridgePort = Config.Bind("DevBridge", "Port", 5710, "Port for the dev bridge.");

        L.LogInfo($"{ModKit.Name} {ModKit.Version} loaded");
        CheckGameVersion();
        EventPatches.InstallAll();
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

    static void CheckGameVersion()
    {
        try
        {
            string running = Application.version;
            string tested = ModKit.TestedGameVersion; // local, so the compiler doesn't fold the const
            if (tested == "")
                L.LogInfo($"Game version {running} (tested version not recorded)");
            else if (running == tested)
                L.LogInfo($"Game version {running} (tested)");
            else
                L.LogWarning($"Game version {running}, kit tested on {tested}. " +
                             "Check the event list below for anything missing.");
        }
        catch (Exception e) { L.LogWarning($"Could not read game version: {e.Message}"); }
    }
}
