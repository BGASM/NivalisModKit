using System;
using System.Collections.Generic;
using System.Linq;
using Nivalis;

namespace NivalisModKit;

// Phase 10: UI events. UIPanel.OnStateChange is a static game event every panel raises when it is
// shown or hidden (UIPanel.SetVisible), so one subscription covers every screen; nothing is patched.
static partial class EventPatches
{
    // Panel -> visible as last reported, so a repeated state change doesn't raise twice.
    static readonly Dictionary<IntPtr, bool> panelVisible = new();

    static void InstallUi()
    {
        Subscribe(nameof(GameEvents.PanelShown), () =>
        {
            Il2CppSystem.Action<UIPanel> action = (Action<UIPanel>)(panel =>
                Raise("UIPanel.OnStateChange", () => PanelStateChanged(panel)));
            keepAlive.Add(action);
            UIPanel.add_OnStateChange(action);   // static event: once is enough
            return () => true;
        });
        // Same subscription; marked live alongside PanelShown.
        if (GameEvents.Live.Contains(nameof(GameEvents.PanelShown)))
        {
            GameEvents.Live.Add(nameof(GameEvents.PanelHidden));
            KitPlugin.L.LogInfo("Event PanelHidden: live (from UIPanel.OnStateChange)");
        }
        else KitPlugin.L.LogWarning("Event PanelHidden: missing (needs UIPanel.OnStateChange)");
        attempted++;
    }

    // How often each panel raises OnStateChange (most calls are repeats of its current state).
    internal sealed class PanelCalls { public string Name; public long Calls, Changes; }
    internal static readonly Dictionary<IntPtr, PanelCalls> panelCalls = new();
    internal static float panelCallsSince = -1f;

    static void PanelStateChanged(UIPanel panel)
    {
        if (panel == null) return;
        if (panelCallsSince < 0) panelCallsSince = UnityEngine.Time.unscaledTime;
        if (!panelCalls.TryGetValue(panel.Pointer, out var pc))
            panelCalls[panel.Pointer] = pc = new PanelCalls { Name = $"{Ui.NameOf(panel)} ({panel.gameObject?.name})" };
        pc.Calls++;
        bool visible = panel._isVisible;   // a field read, not a call into the game (some panels repeat 40x/s)
        bool known = panelVisible.TryGetValue(panel.Pointer, out bool was);
        panelVisible[panel.Pointer] = visible;
        if (known ? was == visible : !visible) return;   // panels hide themselves at startup; only report real changes
        pc.Changes++;

        var args = new PanelArgs(panel, Ui.NameOf(panel));
        if (visible) GameEvents.RaisePanelShown(args);
        else GameEvents.RaisePanelHidden(args);
    }

    // Noisiest panels by OnStateChange calls, for the dev bridge (/perf) and the frame-timing log.
    internal static object PanelCallStats()
    {
        float secs = panelCallsSince < 0 ? 0 : UnityEngine.Time.unscaledTime - panelCallsSince;
        return panelCalls.Values.OrderByDescending(p => p.Calls).Take(10).Select(p => new
        {
            panel = p.Name, calls = p.Calls, realChanges = p.Changes,
            perSecond = secs > 0 ? System.Math.Round(p.Calls / secs, 1) : 0,
        }).ToArray();
    }
}
