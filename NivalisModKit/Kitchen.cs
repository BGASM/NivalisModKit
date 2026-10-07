using System;
using System.Collections.Generic;
using Nivalis.CraftingSystem;
using Nivalis.GhostSystem;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;
using Nivalis.SkillSystem;

namespace NivalisModKit;

/// <summary>A step of making a meal: prep jobs (chop, grill, blend) first, then plating, which finishes the meal.</summary>
public enum KitchenStep
{
    /// <summary>A prep job (<c>ProcessIngredientTask</c>) at an ingredient processor. Gives no XP in the game.</summary>
    Prep,
    /// <summary>The plating job (<c>MakeMealTask</c>) at a meal station: sets the meal's quality, gives the plater 1 cooking XP.</summary>
    Plating,
}

/// <summary>
/// Venue kitchen work: who may take prep and plating jobs, which of the waiting jobs they take, how long a step
/// takes, and what a plated meal's quality and XP are. Patches install when the first handler is added; with
/// no handlers the game runs untouched.
/// </summary>
/// <remarks>
/// How the game hands out kitchen work: an order becomes prep jobs plus one plating job, which waits until its
/// prep is done. A cook looking for work takes the first job of that kind in the venue's task queue, then a free
/// station for it (or retries in 3 s). Each step's time is scaled by the cook's cooking <c>ActionSpeed</c> and
/// happiness. Plating sets the meal's quality (plater's <c>PreparationQuality</c> × happiness influence) and
/// freshness (the ingredients'), and gives the plater 1 cooking XP. Several mods can use each hook: handlers
/// run in the order added and each sees the previous one's result.
/// </remarks>
[Experimental("New in 0.6.")]
public static class Kitchen
{
    static Action<KitchenMayWorkContext> mayWork;
    static Action<KitchenJobsContext> filterJobs;
    static Action<KitchenStepContext> stepStarting;
    static Action<MealPlatedContext> mealPlated;
    static Action<PrepCompletedContext> prepCompleted;

    /// <summary>
    /// A worker without the Cook job is checked for a kitchen step: set <c>Allow</c> to let them take it (e.g. a
    /// mod's own kitchen job). Raised only when the game said no and the person has work, free hands and is on shift.
    /// </summary>
    public static event Action<KitchenMayWorkContext> MayWork
    {
        add { mayWork += value; Added(nameof(MayWork), value, mayWork); }
        remove { mayWork -= value; }
    }

    /// <summary>
    /// A worker is about to take a prep or plating job: remove the waiting jobs they shouldn't take. The worker
    /// then takes the first remaining job in queue order (none left: no kitchen job this time). If no handler
    /// removes anything, the game picks as usual.
    /// </summary>
    public static event Action<KitchenJobsContext> FilterJobs
    {
        add { filterJobs += value; Added(nameof(FilterJobs), value, filterJobs); }
        remove { filterJobs -= value; }
    }

    /// <summary>A prep or plating step started: multiply <c>TimeScale</c> to make it faster (below 1) or slower.</summary>
    public static event Action<KitchenStepContext> StepStarting
    {
        add { stepStarting += value; Added(nameof(StepStarting), value, stepStarting); }
        remove { stepStarting -= value; }
    }

    /// <summary>
    /// A meal was plated: read or change its quality, and the XP the plater gets for it (skill and amount). Also
    /// the place to read a finished meal's final quality and freshness.
    /// </summary>
    public static event Action<MealPlatedContext> MealPlated
    {
        add { mealPlated += value; Added(nameof(MealPlated), value, mealPlated); }
        remove { mealPlated -= value; }
    }

    /// <summary>A prep job was finished: set <c>Experience</c> (and <c>ExperienceSkill</c>) to reward it; the game gives none.</summary>
    public static event Action<PrepCompletedContext> PrepCompleted
    {
        add { prepCompleted += value; Added(nameof(PrepCompleted), value, prepCompleted); }
        remove { prepCompleted -= value; }
    }

    /// <summary>True once the kitchen patches installed (after the first handler was added and the game's methods were found).</summary>
    public static bool IsAvailable => KitchenPatches.Installed;

    /// <summary>Whether a dish is a drink (its recipe's type).</summary>
    public static bool IsDrink(FoodItemType meal)
    {
        try { return meal?.RecipeDefinition?.RecipeType == RecipeTypes.Drink; } catch { return false; }
    }

    /// <summary>
    /// How many prep jobs one of this dish takes: its recipe's inputs that need processing (blender, grill...),
    /// counted by amount. 0 for dishes plated straight from their ingredients (beer).
    /// </summary>
    public static int PrepSteps(FoodItemType meal)
    {
        try
        {
            var inputs = meal?.RecipeDefinition?.inputsNew;
            if (inputs == null) return 0;
            int n = 0;
            foreach (var input in inputs)
            {
                string processing = null;
                try { processing = input?.ProcessingType.ToString(); } catch { }
                if (!string.IsNullOrEmpty(processing) && processing != "None") n += Math.Max(1, input.Amount);
            }
            return n;
        }
        catch { return 0; }
    }

    internal static Action<KitchenMayWorkContext> MayWorkHandlers => mayWork;
    internal static Action<KitchenJobsContext> FilterHandlers => filterJobs;
    internal static Action<KitchenStepContext> StepHandlers => stepStarting;
    internal static Action<MealPlatedContext> PlatedHandlers => mealPlated;
    internal static Action<PrepCompletedContext> PrepHandlers => prepCompleted;

    // Each handler on its own, so one failing doesn't stop the rest.
    internal static void Run<T>(string name, Action<T> handlers, T context)
    {
        if (handlers == null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action<T>)d)(context); }
            catch (Exception e)
            {
                string who = d.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
                KitPlugin.L.LogError($"Kitchen.{name} handler from {who}: {e}");
            }
        }
    }

    static void Added(string name, Delegate handler, Delegate all)
    {
        string who = handler?.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
        int n = all?.GetInvocationList().Length ?? 0;
        KitPlugin.L.LogInfo($"Kitchen.{name}: handler added by {who}" + (n > 1 ? $" ({n} handlers, run in the order added)" : ""));
        try { KitchenPatches.EnsureInstalled(); }
        catch (Exception e) { KitPlugin.L.LogError($"Kitchen patches: missing ({e.Message})"); }
    }
}

/// <summary>Arguments for <see cref="Kitchen.MayWork"/>.</summary>
public sealed class KitchenMayWorkContext
{
    /// <summary>The worker.</summary>
    public Person Person { get; }
    /// <summary>Their agent (position, held item, current action).</summary>
    public AgentGhost Agent { get; }
    /// <summary>Prep or plating.</summary>
    public KitchenStep Step { get; }
    /// <summary>Set true to let them take this kind of job. Any handler setting it is enough.</summary>
    public bool Allow { get; set; }

    internal KitchenMayWorkContext(Person person, AgentGhost agent, KitchenStep step)
    {
        Person = person; Agent = agent; Step = step;
    }
}

/// <summary>A prep or plating job waiting in a venue's task queue.</summary>
public sealed class KitchenJob
{
    /// <summary>The game's task (<c>StaffTaskQueue.ProcessIngredientTask</c> or <c>MakeMealTask</c>).</summary>
    public StaffTaskQueue.StaffTask Task { get; }
    /// <summary>The dish it's for.</summary>
    public FoodItemType Meal { get; }
    /// <summary>Whether the dish is a drink.</summary>
    public bool IsDrink { get; }
    /// <summary>Its place in the venue's task queue (0 = first).</summary>
    public int QueueIndex { get; }

    internal KitchenJob(StaffTaskQueue.StaffTask task, FoodItemType meal, int index)
    {
        Task = task; Meal = meal; QueueIndex = index; IsDrink = Kitchen.IsDrink(meal);
    }
}

/// <summary>Arguments for <see cref="Kitchen.FilterJobs"/>.</summary>
public sealed class KitchenJobsContext
{
    readonly List<KitchenJob> jobs;

    /// <summary>The worker looking for a job.</summary>
    public Person Person { get; }
    /// <summary>Prep or plating.</summary>
    public KitchenStep Step { get; }
    /// <summary>The venue whose queue this is.</summary>
    public VenueAreaGhost Area { get; }
    /// <summary>The jobs of this step still allowed, in queue order.</summary>
    public IReadOnlyList<KitchenJob> Jobs => jobs;
    /// <summary>True once a handler removed a job (the kit then picks; otherwise the game does).</summary>
    public bool Changed { get; private set; }

    /// <summary>Removes one job from this worker's choice.</summary>
    public void Remove(KitchenJob job)
    {
        if (job != null && jobs.Remove(job)) Changed = true;
    }

    /// <summary>Removes every job matching <paramref name="match"/> from this worker's choice.</summary>
    public void RemoveWhere(Func<KitchenJob, bool> match)
    {
        if (match != null && jobs.RemoveAll(j => match(j)) > 0) Changed = true;
    }

    /// <summary>Removes every job: the worker takes none of this step now.</summary>
    public void RemoveAll()
    {
        if (jobs.Count > 0) Changed = true;
        jobs.Clear();
    }

    internal KitchenJobsContext(Person person, KitchenStep step, VenueAreaGhost area, List<KitchenJob> jobs)
    {
        Person = person; Step = step; Area = area; this.jobs = jobs;
    }
}

/// <summary>Arguments for <see cref="Kitchen.StepStarting"/>.</summary>
public sealed class KitchenStepContext
{
    /// <summary>The worker.</summary>
    public Person Person { get; }
    /// <summary>Prep or plating.</summary>
    public KitchenStep Step { get; }
    /// <summary>The dish.</summary>
    public FoodItemType Meal { get; }
    /// <summary>Whether the dish is a drink.</summary>
    public bool IsDrink { get; }
    /// <summary>Multiplies the step's time as the game set it (from the worker's cooking skill and happiness). 1 = unchanged.</summary>
    public float TimeScale { get; set; } = 1f;

    internal KitchenStepContext(Person person, KitchenStep step, FoodItemType meal)
    {
        Person = person; Step = step; Meal = meal; IsDrink = Kitchen.IsDrink(meal);
    }
}

/// <summary>Arguments for <see cref="Kitchen.MealPlated"/>.</summary>
public sealed class MealPlatedContext
{
    /// <summary>The worker who plated it.</summary>
    public Person Person { get; }
    /// <summary>The finished meal.</summary>
    public MealGhost Meal { get; }
    /// <summary>The dish.</summary>
    public FoodItemType Item { get; }
    /// <summary>Whether the dish is a drink.</summary>
    public bool IsDrink { get; }
    /// <summary>The quality the game gave it (plater's cooking <c>PreparationQuality</c> × happiness influence).</summary>
    public float GameQuality { get; }
    /// <summary>The meal's freshness (from its ingredients).</summary>
    public float Freshness { get; }
    /// <summary>The meal's quality: change it to override the game's.</summary>
    public float Quality { get; set; }
    /// <summary>The skill the plater's XP goes to (the game: cooking).</summary>
    public SkillDefinition ExperienceSkill { get; set; }
    /// <summary>The XP the plater gets (the game: 1). 0 gives none.</summary>
    public float Experience { get; set; }

    internal MealPlatedContext(Person person, MealGhost meal, FoodItemType item, SkillDefinition skill, float xp)
    {
        Person = person; Meal = meal; Item = item; IsDrink = Kitchen.IsDrink(item);
        try { GameQuality = Quality = meal.Quality; Freshness = meal.Freshness; } catch { }
        ExperienceSkill = skill; Experience = xp;
    }
}

/// <summary>Arguments for <see cref="Kitchen.PrepCompleted"/>.</summary>
public sealed class PrepCompletedContext
{
    /// <summary>The worker who did the prep.</summary>
    public Person Person { get; }
    /// <summary>The dish it was for.</summary>
    public FoodItemType Meal { get; }
    /// <summary>Whether the dish is a drink.</summary>
    public bool IsDrink { get; }
    /// <summary>How many prep jobs the dish takes in all (see <see cref="Kitchen.PrepSteps"/>), at least 1 here.</summary>
    public int PrepSteps { get; }
    /// <summary>The skill to give XP in (default: cooking).</summary>
    public SkillDefinition ExperienceSkill { get; set; }
    /// <summary>The XP to give (default 0, as in the game).</summary>
    public float Experience { get; set; }

    internal PrepCompletedContext(Person person, FoodItemType meal)
    {
        Person = person; Meal = meal; IsDrink = Kitchen.IsDrink(meal); ExperienceSkill = StaffSkills.Cooking;
        PrepSteps = Math.Max(1, Kitchen.PrepSteps(meal));
    }
}
