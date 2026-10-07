using System;
using System.Collections.Generic;
using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// A mod's own staff job, shown in the venue's Staff tab next to Cook, Waiter, Cleaner and Manager. The kit
/// draws it as a copy of the game's Cook job (same frame, hover, sounds, level badge) with your icon, and
/// handles the rules that come with a fifth job: room in the row, the game's "at least one job" rule, and
/// saves that still work if your mod is removed. What the job does is up to you (see <see cref="Kitchen"/>).
/// </summary>
[Experimental("New in 0.6.")]
public sealed class StaffJob
{
    /// <summary>Unique id, e.g. your plugin GUID + ".bartender".</summary>
    public string Id { get; init; }
    /// <summary>Display name, for logs.</summary>
    public string Name { get; init; }
    /// <summary>The icon: a white glyph on transparent (tinted grey off, gold on, like the game's). PNG bytes.</summary>
    public byte[] IconPng { get; init; }
    /// <summary>Or the icon as a ready sprite (used instead of <see cref="IconPng"/> when set).</summary>
    public Sprite Icon { get; init; }
    /// <summary>Pixels to move the icon from where the Cook icon sits (art whose figure isn't centred).</summary>
    public Vector2 IconOffset { get; init; }
    /// <summary>Whether the person may have this job (shown in their row). Usually: has the skills it uses.</summary>
    public Func<Person, bool> CanDo { get; init; }
    /// <summary>Whether the person has the job on. Keep the state yourself (e.g. in <see cref="SaveData"/>).</summary>
    public Func<Person, bool> IsOn { get; init; }
    /// <summary>The player switched it (the kit has already refused switching off someone's last job).</summary>
    public Action<Person, bool> Set { get; init; }
    /// <summary>The level badge's text (e.g. "3" or "3/1"), or null to show the Cook job's.</summary>
    public Func<Person, string> Badge { get; init; }
    /// <summary>
    /// The game job a worker gets in the save file when this is their only job, so the save works without your
    /// mod (taken off again right after the save). Default: Cook if they can cook, else Waiter, Cleaner, Manager.
    /// </summary>
    public Func<Person, VenueTasks> SaveAs { get; init; }

    internal Sprite sprite;
    internal bool On(Person p)
    {
        try { return p != null && (CanDo?.Invoke(p) ?? true) && (IsOn?.Invoke(p) ?? false); }
        catch { return false; }
    }
}

/// <summary>Registry of <see cref="StaffJob"/>s.</summary>
[Experimental("New in 0.6.")]
public static class StaffJobs
{
    static readonly List<StaffJob> jobs = new();
    static Harmony harmony;
    const string SavedKey = "staffJobs.savedAs";   // "guid|Job" for workers saved with a stand-in game job
    static readonly List<(Person person, VenueTasks job)> standIns = new();

    internal static IReadOnlyList<StaffJob> All => jobs;
    internal static bool OtherJobOn(StaffJob job, Person p) => jobs.Exists(j => j != job && j.On(p));

    /// <summary>Adds a job to every venue's Staff tab. Returns false (and logs why) if it can't.</summary>
    public static bool Register(StaffJob job)
    {
        if (job == null || string.IsNullOrEmpty(job.Id) || job.IsOn == null || job.Set == null)
        {
            KitPlugin.L.LogError("StaffJobs.Register: a job needs an Id, IsOn and Set");
            return false;
        }
        if (jobs.Exists(j => j.Id == job.Id))
        {
            KitPlugin.L.LogError($"StaffJobs.Register: {job.Id} is already registered");
            return false;
        }
        jobs.Add(job);
        KitPlugin.L.LogInfo($"StaffJobs: {job.Name ?? job.Id} added" + (jobs.Count > 1 ? $" ({jobs.Count} mod jobs)" : ""));
        Install();
        return true;
    }

    /// <summary>Whether the person has one of the game's four jobs on.</summary>
    public static bool HasGameJob(Person person)
    {
        try { return person?.RuntimeData != null && (person.RuntimeData.Tasks & GameJobs) != 0; }
        catch { return false; }
    }

    /// <summary>Whether the person has a mod job on (one they may do).</summary>
    public static bool HasModJob(Person person) => jobs.Exists(j => j.On(person));

    /// <summary>
    /// Gives a game job to staff left with none (e.g. your job's <see cref="StaffJob.CanDo"/> changed with a
    /// setting): Cook if they can cook, else Waiter, Cleaner, Manager. Done by the kit after each load; call it
    /// after changing who may do your job.
    /// </summary>
    public static void CheckJobless()
    {
        foreach (var p in AllStaff())
        {
            if (HasGameJob(p) || HasModJob(p)) continue;
            var job = DefaultJob(p);
            try { p.RuntimeData.Tasks |= job; } catch { continue; }
            KitPlugin.L.LogInfo($"StaffJobs: {NameOf(p)} had no job left: {job}");
        }
    }

    /// <summary>Redraws the open Staff tab's mod jobs (after a state change made elsewhere).</summary>
    public static void Refresh() => StaffJobsUi.Refresh();

    const VenueTasks GameJobs = VenueTasks.Cooking | VenueTasks.Serving | VenueTasks.Cleaning | VenueTasks.Managing;

    static VenueTasks DefaultJob(Person p)
    {
        if (StaffSkills.Has(p, StaffSkills.Cooking)) return VenueTasks.Cooking;
        if (StaffSkills.Has(p, StaffSkills.Serving)) return VenueTasks.Serving;
        if (StaffSkills.Has(p, StaffSkills.Cleaning)) return VenueTasks.Cleaning;
        if (StaffSkills.Has(p, StaffSkills.Managing)) return VenueTasks.Managing;
        return VenueTasks.Cooking;
    }

    static VenueTasks SaveJobFor(Person p)
    {
        foreach (var j in jobs)
        {
            if (!j.On(p) || j.SaveAs == null) continue;
            try
            {
                var t = j.SaveAs(p) & GameJobs;
                if (t != 0) return t;
            }
            catch { }
        }
        return DefaultJob(p);
    }

    internal static IEnumerable<Person> AllStaff()
    {
        var list = new List<Person>();
        try
        {
            foreach (var area in Venues.PlayerOwned)
            {
                var staff = area?.staff?.backingList;
                if (staff == null) continue;
                for (int i = 0; i < staff.Count; i++) if (staff[i] != null) list.Add(staff[i]);
            }
        }
        catch { }
        return list;
    }

    internal static string NameOf(Person p)
    {
        try { return p?.DisplayedName ?? p?.Name ?? "?"; } catch { return "?"; }
    }

    // ---------- the game's rules ----------

    static void Install()
    {
        if (harmony != null) return;
        harmony = new Harmony(ModKit.Guid + ".staffjobs");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(VenueStaffListItem), "CountTogglesOn"),
                postfix: new HarmonyMethod(typeof(StaffJobs), nameof(CountsAsJob)));
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: job count rule: missing ({e.Message})"); }
        StaffJobsUi.Install(harmony);
        SaveData.BeforeGameWrite += BeforeWrite;
        SaveData.AfterGameWrite += AfterWrite;
        GameEvents.GameReady += AfterLoad;
    }

    // A worker must keep at least one job on: each mod job on counts, so a worker can switch the game's off.
    static void CountsAsJob(VenueStaffListItem __instance, ref int __result)
    {
        try
        {
            var p = __instance._person;
            foreach (var j in jobs) if (j.On(p)) __result++;
        }
        catch { }
    }

    // Workers whose only jobs are mod jobs get a stand-in game job for the save file.
    static void BeforeWrite()
    {
        standIns.Clear();
        var saved = new List<string>();
        foreach (var p in AllStaff())
        {
            if (HasGameJob(p) || !HasModJob(p)) continue;
            var job = SaveJobFor(p);
            try { p.RuntimeData.Tasks |= job; } catch { continue; }
            standIns.Add((p, job));
            try { saved.Add($"{p.Guid}|{job}"); } catch { }
        }
        SaveData.For(ModKit.Guid).Set(SavedKey, saved);
    }

    static void AfterWrite()
    {
        foreach (var (p, job) in standIns)
            try { p.RuntimeData.Tasks &= ~job; } catch { }
        standIns.Clear();
    }

    // After a load: take the stand-ins off workers who still have their mod job (not if the mod or setting is gone).
    static void AfterLoad()
    {
        try
        {
            var saved = SaveData.For(ModKit.Guid).Get<List<string>>(SavedKey);
            if (saved != null && saved.Count > 0)
            {
                var byGuid = new Dictionary<string, VenueTasks>();
                foreach (var e in saved)
                {
                    var parts = e.Split('|');
                    if (parts.Length == 2 && Enum.TryParse<VenueTasks>(parts[1], out var t)) byGuid[parts[0]] = t;
                }
                int n = 0;
                foreach (var p in AllStaff())
                {
                    string g = null;
                    try { g = p.Guid; } catch { }
                    if (g == null || !byGuid.TryGetValue(g, out var job) || !HasModJob(p)) continue;
                    try { p.RuntimeData.Tasks &= ~job; n++; } catch { }
                }
                if (n > 0) KitPlugin.L.LogInfo($"StaffJobs: {n} worker(s) back to only their mod jobs");
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: after load: {e.Message}"); }
        CheckJobless();
    }
}
