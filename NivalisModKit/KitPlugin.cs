using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace NivalisModKit;

/// <summary>BepInEx entry point. Mods use the static API, not this class.</summary>
[BepInPlugin(ModKit.Guid, ModKit.Name, ModKit.Version)]
public class KitPlugin : BasePlugin
{
    internal static ManualLogSource L;

    /// <summary>Called by BepInEx.</summary>
    public override void Load()
    {
        L = Log;
        L.LogInfo($"{ModKit.Name} {ModKit.Version} loaded");
    }
}
