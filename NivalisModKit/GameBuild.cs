using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;

namespace NivalisModKit;

/// <summary>
/// Which build of the game is running, and whether this kit was tested on it. The game's own version string
/// stays "1.0" across patches, so the kit identifies the build by its code (a fingerprint of GameAssembly.dll).
/// </summary>
public static class GameBuild
{
    // Builds the kit knows, newest first. Tested = this kit release was checked on it.
    static readonly (string fingerprint, string label, bool tested)[] Known =
    {
        ("0da6aac5209f504d", "1.0 patch 2", true),
        ("d7d7fef8b76699ae", "1.0 patch 1", false),
        ("9a0e32c2d09a5025", "1.0", false),
    };

    /// <summary>First 16 hex digits of GameAssembly.dll's SHA-256, or "" if it couldn't be read.</summary>
    public static string Fingerprint { get; private set; } = "";

    /// <summary>The build's name ("1.0 patch 2"), or null for a build this kit doesn't know.</summary>
    public static string Label { get; private set; }

    /// <summary>True if this kit release was tested on the running build.</summary>
    public static bool IsTested { get; private set; }

    /// <summary>Steam's build id from the install's app manifest, or null (not a Steam install).</summary>
    public static string SteamBuildId { get; private set; }

    /// <summary>"1.0 patch 2", or "unknown build 0da6aac5" for one the kit doesn't know.</summary>
    public static string Describe() =>
        Label ?? (Fingerprint == "" ? "unknown build" : $"unknown build {Fingerprint.Substring(0, 8)}");

    internal static void Detect()
    {
        try
        {
            using var sha = SHA256.Create();
            using var f = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
            Fingerprint = string.Concat(sha.ComputeHash(f).Take(8).Select(b => b.ToString("x2")));
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Game build: could not read GameAssembly.dll ({e.Message})"); }

        var known = Known.FirstOrDefault(k => k.fingerprint == Fingerprint);
        Label = known.label;
        IsTested = known.tested;
        SteamBuildId = ReadSteamBuildId();

        string steam = SteamBuildId != null ? $", Steam build {SteamBuildId}" : "";
        if (IsTested)
            KitPlugin.L.LogInfo($"Game build: {Label}{steam} (tested)");
        else
            KitPlugin.L.LogWarning($"Game build: {Describe()}{steam}. {ModKit.Name} {ModKit.Version} was tested on " +
                                   $"{string.Join(", ", Known.Where(k => k.tested).Select(k => k.label))}. " +
                                   "Mods using the kit may misbehave until the kit is updated for this build.");
    }

    // steamapps/appmanifest_<id>.acf two levels above the game folder, with "buildid" "25680465".
    static string ReadSteamBuildId()
    {
        try
        {
            var steamapps = Directory.GetParent(Paths.GameRootPath)?.Parent;
            if (steamapps == null) return null;
            string folder = Path.GetFileName(Paths.GameRootPath.TrimEnd('\\', '/'));
            foreach (var acf in steamapps.GetFiles("appmanifest_*.acf"))
            {
                var lines = File.ReadAllLines(acf.FullName);
                if (!lines.Any(l => l.Contains("\"installdir\"") && l.Contains($"\"{folder}\""))) continue;
                var line = lines.FirstOrDefault(l => l.Contains("\"buildid\""));
                return line?.Split('"').Where(p => p.Trim().Length > 0).LastOrDefault();
            }
        }
        catch { }
        return null;
    }
}
