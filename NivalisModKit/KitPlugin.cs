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

    /// <summary>Called by BepInEx.</summary>
    public override void Load()
    {
        L = Log;
        SimulateMissing = Config.Bind("Debug", "SimulateMissing", "",
            "Comma-separated event names to install against a method that doesn't exist, " +
            "to test the missing-event fallback. Leave empty.");

        L.LogInfo($"{ModKit.Name} {ModKit.Version} loaded");
        CheckGameVersion();
        EventPatches.InstallAll();
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
