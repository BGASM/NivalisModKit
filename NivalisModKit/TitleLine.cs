using System;
using Il2CppInterop.Runtime;
using Nivalis;
using TMPro;
using UnityEngine;

namespace NivalisModKit;

// A line above the title screen's copyright text: "Nivalis ModKit 0.2.0 · game 1.0 patch 2", in the same font.
// On a build the kit wasn't tested on it says so in orange, and warns once in game.
internal static class TitleLine
{
    const string Name = "Kit_VersionLine";
    static bool warnedInGame;

    internal static void Install()
    {
        GameEvents.PanelShown += a =>
        {
            if (a.Name != nameof(MainMenuUI) || GameEvents.IsInGame) return;   // the title screen, not the pause menu
            Scheduler.NextFrame(Add);   // after the screen has laid out
        };
        GameEvents.GameReady += () =>
        {
            if (GameBuild.IsTested || warnedInGame) return;
            warnedInGame = true;
            Ui.Notify(ModKit.Name, $"Version {ModKit.Version} wasn't tested on this game version ({GameBuild.Describe()}). " +
                                   "If something misbehaves, check for a kit update.");
        };
    }

    static void Add()
    {
        try
        {
            var copyright = FindCopyright();
            if (copyright == null) { LogOnce("TitleLine: no copyright text found on the title screen"); return; }
            if (copyright.transform.parent != null && copyright.transform.parent.Find(Name) != null) return;

            string text = $"{ModKit.Name} {ModKit.Version} · game {GameBuild.Describe()}";
            if (!GameBuild.IsTested) text = $"<color=#E8A33D>{text} (untested: mods may misbehave)</color>";
            var line = Ui.CloneText(copyright, text, -Ui.Below(copyright.gameObject));   // one line height above it
            if (line == null) return;
            line.gameObject.name = Name;
            line.enableWordWrapping = false;
            KitPlugin.L.LogInfo($"TitleLine: added above '{copyright.gameObject.name}'");
        }
        catch (Exception e) { LogOnce($"TitleLine: {e.Message}"); }
    }

    // The visible text on the title screen that carries the copyright notice.
    static TMP_Text FindCopyright()
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_Text>()))
        {
            var t = o.TryCast<TMP_Text>();
            if (t == null || !t.gameObject.activeInHierarchy || t.gameObject.name == Name) continue;
            string s = t.text ?? "";
            if (s.Contains("©") || s.IndexOf("copyright", StringComparison.OrdinalIgnoreCase) >= 0) return t;
        }
        return null;
    }

    static bool logged;
    static void LogOnce(string message)
    {
        if (logged) return;
        logged = true;
        KitPlugin.L.LogWarning(message);
    }
}
