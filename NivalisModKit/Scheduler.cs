using System;
using System.Collections.Generic;

namespace NivalisModKit;

/// <summary>
/// Run code later, on the main thread: next frame, after some game time, at a clock hour, or
/// after some days. Game-time schedules are checked every frame against the clock, so sleeping
/// past the moment still runs them (once, when the clock passes it). Everything pending is
/// cancelled when a save loads or a new game starts; keep longer plans in <see cref="SaveData"/>.
/// </summary>
public static class Scheduler
{
    sealed class Job : IDisposable
    {
        public Action Action;
        public Func<bool> Due;
        public string Owner;
        public bool Cancelled;
        public void Dispose() => Cancelled = true;
    }

    static readonly List<Job> jobs = new();
    static bool hooked;

    /// <summary>Runs <paramref name="action"/> on the next frame. Dispose the result to cancel.</summary>
    public static IDisposable NextFrame(Action action) => Add(action, () => true);

    /// <summary>
    /// Runs <paramref name="action"/> after <paramref name="seconds"/> of real time (keeps counting while
    /// the game is paused). Dispose the result to cancel.
    /// </summary>
    public static IDisposable AfterSeconds(float seconds, Action action)
    {
        float target = UnityEngine.Time.unscaledTime + seconds;
        return Add(action, () => UnityEngine.Time.unscaledTime >= target);
    }

    /// <summary>Runs <paramref name="action"/> once <paramref name="hours"/> game hours have passed.</summary>
    public static IDisposable AfterGameHours(float hours, Action action)
    {
        float target = GameTime.TotalHours + hours;
        return Add(action, () => GameTime.TotalHours >= target);
    }

    /// <summary>
    /// Runs <paramref name="action"/> the next time the clock reaches <paramref name="hour"/>
    /// (0-23). If it's already past that hour today, it runs tomorrow.
    /// </summary>
    public static IDisposable AtHour(int hour, Action action)
    {
        if (hour < 0 || hour > 23) throw new ArgumentOutOfRangeException(nameof(hour));
        float now = GameTime.TotalHours;
        float hoursAhead = (hour - GameTime.Hour + 24) % 24;
        if (hoursAhead == 0) hoursAhead = 24;
        float target = (float)Math.Floor(now) + hoursAhead;
        return Add(action, () => GameTime.TotalHours >= target);
    }

    /// <summary>
    /// Runs <paramref name="action"/> when <paramref name="days"/> game days have started from now
    /// (days turn over at 08:00). AfterDays(1) runs at the next day start.
    /// </summary>
    public static IDisposable AfterDays(int days, Action action)
    {
        int target = GameTime.Day + Math.Max(days, 1);
        return Add(action, () => GameTime.Day >= target);
    }

    static IDisposable Add(Action action, Func<bool> due)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        Hook();
        var job = new Job
        {
            Action = action,
            Due = due,
            Owner = action.Method.DeclaringType?.Assembly.GetName().Name ?? "?",
        };
        jobs.Add(job);
        return job;
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        KitLoop.Tick += Run;
    }

    // Called by the kit before GameLoaded / NewGameStarted are raised, so work a mod schedules
    // in those handlers survives.
    internal static void Reset()
    {
        foreach (var j in jobs) j.Cancelled = true;
        jobs.Clear();
    }

    static void Run()
    {
        if (jobs.Count == 0) return;
        // Copy: an action may schedule more work.
        foreach (var job in jobs.ToArray())
        {
            if (job.Cancelled) { jobs.Remove(job); continue; }
            bool due;
            try { due = job.Due(); } catch { due = false; }
            if (!due) continue;

            jobs.Remove(job);
            try { job.Action(); }
            catch (Exception e) { KitPlugin.L.LogError($"Scheduled action in {job.Owner} threw: {e}"); }
        }
    }
}
