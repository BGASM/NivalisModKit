using System.Collections.Generic;
using BepInEx.Logging;

namespace NivalisModKit;

/// <summary>
/// Logging for things that repeat: a line is written only when it differs from the last one under the same
/// key. Use it for per-person or per-object status that the game re-checks often (an idle cook looking for
/// work every few seconds), so a verbose log shows changes instead of thousands of copies.
/// </summary>
[Experimental("New in 0.6.")]
public static class LogChanges
{
    static readonly Dictionary<ManualLogSource, Dictionary<string, string>> last = new();

    /// <summary>Logs <paramref name="line"/> at Info if it differs from the last line logged under <paramref name="key"/>.</summary>
    public static bool Info(ManualLogSource log, string key, string line)
    {
        if (log == null || line == null) return false;
        if (!last.TryGetValue(log, out var lines)) last[log] = lines = new();
        if (lines.TryGetValue(key ?? "", out var was) && was == line) return false;
        lines[key ?? ""] = line;
        log.LogInfo(line);
        return true;
    }

    /// <summary>Forgets the last line under a key (or all keys for the log), so the next one is logged.</summary>
    public static void Reset(ManualLogSource log, string key = null)
    {
        if (log == null || !last.TryGetValue(log, out var lines)) return;
        if (key == null) lines.Clear();
        else lines.Remove(key);
    }
}
