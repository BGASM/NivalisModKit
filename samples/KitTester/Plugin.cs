using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using NivalisModKit;

namespace KitTester;

// Logs every kit event. Doubles as the regression test after game updates:
// every kit feature should be exercised here.
[BepInPlugin("will.nivalis.kittester", "Kit Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;

    public override void Load()
    {
        L = Log;
        L.LogInfo($"Kit Tester loaded against {ModKit.Name} {ModKit.Version}");
    }
}
