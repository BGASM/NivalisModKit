using System;
using System.Collections.Generic;
using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.SkillSystem;

namespace NivalisModKit;

// The game code behind Kitchen. Each patch does nothing unless its event has handlers.
static class KitchenPatches
{
    static Harmony harmony;
    internal static bool Installed;

    internal static void EnsureInstalled()
    {
        if (harmony != null) return;
        harmony = new Harmony(ModKit.Guid + ".kitchen");
        int ok = 0, failed = 0;

        void Patch(string what, Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                // The class's own method first: since build 25828494 the sub-actions' base class has its own Enter/Tick,
                // which makes a lookup that includes inherited methods ambiguous.
                var target = AccessTools.DeclaredMethod(type, method) ?? AccessTools.Method(type, method)
                             ?? throw new Exception($"{type.Name}.{method} not found");
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(KitchenPatches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(KitchenPatches), postfix));
                ok++;
            }
            catch (Exception e) { failed++; KitPlugin.L.LogWarning($"Kitchen: {what}: missing ({e.Message})"); }
        }

        // Job setup: the game takes the first job of its kind in the queue (then a free station).
        Patch("plating job choice", typeof(MakeMealAgentActionType.__c), "_Initialize_b__1_0", prefix: nameof(BeforePlatingChoice));
        Patch("prep job choice", typeof(ProcessIngredientAgentActionType.__c), "_Initialize_b__1_0", prefix: nameof(BeforePrepChoice));
        // Who may cook: has work, free hands, Cook job, on shift.
        Patch("plating requirements", typeof(MakeMealAgentActionType), nameof(MakeMealAgentActionType.MeetsRequirements), postfix: nameof(MayPlate));
        Patch("prep requirements", typeof(ProcessIngredientAgentActionType), nameof(ProcessIngredientAgentActionType.MeetsRequirements), postfix: nameof(MayPrep));
        // Step times: plating's character command timer, prep's game-time wait, both set when the step starts.
        Patch("plating time", typeof(MakeMealAgentActionType.CompleteMealSubAction), "Enter", nameof(PlatingStarting), nameof(PlatingStarted));
        Patch("prep time", typeof(ProcessIngredientAgentActionType.CompleteCraftingSubAction), "Enter", nameof(PrepStarting), nameof(PrepStarted));
        // Plating's end (XP, then quality, in one tick) and prep's end (the task completes).
        Patch("plating result", typeof(MakeMealAgentActionType.CompleteMealSubAction), "Tick", nameof(PlatingTick), nameof(PlatingTicked));
        Patch("plating XP", typeof(Person), nameof(Person.AddExperience), prefix: nameof(Experience));
        Patch("prep result", typeof(ProcessIngredientAgentActionType.ExitCraftingSubAction), "Enter", nameof(PrepEnding), nameof(PrepEnded));

        Installed = failed == 0;
        KitPlugin.L.LogInfo($"Kitchen: {ok} patches installed" + (failed > 0 ? $", {failed} missing (see above)" : ""));
    }

    // ---------- job choice ----------

    static bool BeforePlatingChoice(AgentGhost agent, ref ActionState __result) =>
        Choose(agent, ref __result, KitchenStep.Plating, t => t.TryCast<StaffTaskQueue.MakeMealTask>() is { } m && !m.IsObsolete ? m.Meal : null);

    static bool BeforePrepChoice(AgentGhost agent, ref ActionState __result) =>
        Choose(agent, ref __result, KitchenStep.Prep, t => t.TryCast<StaffTaskQueue.ProcessIngredientTask>() is { } m && !m.IsObsolete ? m.Meal : null);

    static bool Choose(AgentGhost agent, ref ActionState __result, KitchenStep step, Func<StaffTaskQueue.StaffTask, FoodItemType> mealOf)
    {
        var handlers = Kitchen.FilterHandlers;
        if (handlers == null) return true;
        try
        {
            var person = agent?.Person;
            var area = person?.RuntimeData?._worksAt?.RuntimeData;
            var queue = area?.TaskQueue?.Queue;
            if (queue == null || queue.Count == 0) return true;

            var jobs = new List<KitchenJob>();
            for (int i = 0; i < queue.Count; i++)
            {
                var task = queue[i];
                var meal = task == null ? null : mealOf(task);
                if (meal != null) jobs.Add(new KitchenJob(task, meal, i));
            }
            if (jobs.Count == 0) return true;   // the game finds nothing anyway

            var ctx = new KitchenJobsContext(person, step, area, jobs);
            Kitchen.Run(nameof(Kitchen.FilterJobs), handlers, ctx);
            if (!ctx.Changed) return true;
            if (ctx.Jobs.Count == 0)
            {
                __result = null;   // nothing for them: they do something else
                return false;
            }
            int pick = ctx.Jobs[0].QueueIndex;
            if (pick > 0)
            {
                var job = queue[pick];
                queue.RemoveAt(pick);
                queue.Insert(0, job);   // the game takes the first available job
            }
            return true;
        }
        catch (Exception e)
        {
            KitPlugin.L.LogWarning($"Kitchen: job choice: {e.Message}");
            return true;
        }
    }

    // ---------- who may cook ----------

    static void MayPlate(AgentGhost ghost, ref bool __result) => MayWork(ghost, ref __result, KitchenStep.Plating);
    static void MayPrep(AgentGhost ghost, ref bool __result) => MayWork(ghost, ref __result, KitchenStep.Prep);

    static void MayWork(AgentGhost ghost, ref bool __result, KitchenStep step)
    {
        var handlers = Kitchen.MayWorkHandlers;
        if (__result || handlers == null) return;
        try
        {
            var person = ghost?.Person;
            if (person == null || !ghost.state.HeldItem.IsNull || !Staff.IsOnShift(person)) return;
            var ctx = new KitchenMayWorkContext(person, ghost, step);
            Kitchen.Run(nameof(Kitchen.MayWork), handlers, ctx);
            if (ctx.Allow) __result = true;
        }
        catch { }
    }

    // ---------- step times ----------

    static void PlatingStarting(MakeMealAgentActionType.State state, out CharacterCommand __state)
    {
        __state = null;
        try { if (Kitchen.StepHandlers != null) __state = state?._characterCommand; } catch { }
    }

    static void PlatingStarted(AgentGhost agent, MakeMealAgentActionType.State state, CharacterCommand __state)
    {
        var handlers = Kitchen.StepHandlers;
        if (handlers == null) return;
        try
        {
            var cmd = state?._characterCommand;
            if (cmd == null || (__state != null && cmd.Pointer == __state.Pointer)) return;   // no new timer (resumed)
            var ctx = new KitchenStepContext(agent?.Person, KitchenStep.Plating, state.Task?.Meal);
            Kitchen.Run(nameof(Kitchen.StepStarting), handlers, ctx);
            if (ctx.TimeScale > 0f && ctx.TimeScale != 1f) cmd.SimulateTimer *= ctx.TimeScale;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Kitchen: plating time: {e.Message}"); }
    }

    static void PrepStarting(ProcessIngredientAgentActionType.State state, out bool __state)
    {
        __state = false;
        try { __state = Kitchen.StepHandlers != null && state != null && state.RemainingTime == null; } catch { }
    }

    static void PrepStarted(AgentGhost agent, ProcessIngredientAgentActionType.State state, bool __state)
    {
        var handlers = Kitchen.StepHandlers;
        if (!__state || handlers == null) return;   // the wait existed already: resumed from a save
        try
        {
            var t = state?.RemainingTime;
            if (t == null) return;
            var ctx = new KitchenStepContext(agent?.Person, KitchenStep.Prep, state.Task?.Meal);
            Kitchen.Run(nameof(Kitchen.StepStarting), handlers, ctx);
            if (ctx.TimeScale <= 0f || ctx.TimeScale == 1f) return;
            int was = t._waitGameSeconds;
            int now = Math.Max(1, (int)Math.Round(was * ctx.TimeScale));
            t._untilTime += now - was;
            t._waitGameSeconds = now;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Kitchen: prep time: {e.Message}"); }
    }

    // ---------- plating result ----------

    // During a plating tick: the plater, their XP once the game awards it (held back until the meal's quality is
    // set later in the same tick), and whether the kit itself is awarding XP (let through).
    static Person plating;
    static SkillDefinition platingSkill;
    static float platingXp;
    static bool plated, awarding;

    static void PlatingTick(AgentGhost agent)
    {
        plating = null;
        plated = false;
        if (Kitchen.PlatedHandlers == null) return;
        try { plating = agent?.Person; } catch { }
    }

    static bool Experience(Person __instance, SkillDefinition skill, float value)
    {
        if (awarding || plating == null || plated) return true;
        try
        {
            if (__instance.Pointer != plating.Pointer || !StaffSkills.Same(skill, StaffSkills.Cooking)) return true;
            plated = true;
            platingSkill = skill;
            platingXp = value;
            return false;   // awarded after the handlers, with their skill and amount
        }
        catch { return true; }
    }

    static void PlatingTicked(MakeMealAgentActionType.State state)
    {
        var person = plating;
        bool done = plated;
        plating = null;
        plated = false;
        if (person == null || !done) return;

        var skill = platingSkill;
        float xp = platingXp;
        try
        {
            var meal = state?.Meal.Value;
            if (meal != null)
            {
                var ctx = new MealPlatedContext(person, meal, state.Task?.Meal, skill, xp);
                Kitchen.Run(nameof(Kitchen.MealPlated), Kitchen.PlatedHandlers, ctx);
                if (ctx.Quality != ctx.GameQuality) meal.Quality = ctx.Quality;
                skill = ctx.ExperienceSkill;
                xp = ctx.Experience;
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Kitchen: meal plated: {e.Message}"); }
        Award(person, skill, xp);
    }

    static void Award(Person person, SkillDefinition skill, float xp)
    {
        if (person == null || skill == null || xp == 0f) return;
        awarding = true;
        try { person.AddExperience(skill, xp); }
        catch (Exception e) { KitPlugin.L.LogWarning($"Kitchen: XP: {e.Message}"); }
        finally { awarding = false; }
    }

    // ---------- prep result ----------

    static void PrepEnding(ProcessIngredientAgentActionType.State state, out bool __state)
    {
        __state = false;
        try { __state = Kitchen.PrepHandlers != null && state?.Task != null && !state.Task.IsCompleted; } catch { }
    }

    static void PrepEnded(AgentGhost agent, ProcessIngredientAgentActionType.State state, bool __state)
    {
        if (!__state) return;
        try
        {
            if (state?.Task == null || !state.Task.IsCompleted) return;
            var person = agent?.Person;
            if (person == null) return;
            var ctx = new PrepCompletedContext(person, state.Task.Meal);
            Kitchen.Run(nameof(Kitchen.PrepCompleted), Kitchen.PrepHandlers, ctx);
            Award(person, ctx.ExperienceSkill, ctx.Experience);
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Kitchen: prep completed: {e.Message}"); }
    }
}
