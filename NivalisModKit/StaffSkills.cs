using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.SkillSystem;
using UnityEngine;

namespace NivalisModKit;

/// <summary>
/// Characters' work skills (cooking, serving, cleaning, managing): who has which, their level, XP and
/// the level's values. A person's skills are XP per skill (<c>RuntimePersonData.Skills</c>); the skill turns
/// XP into a level and the level into its data (<c>CookingLevel.PreparationQuality</c>,
/// <c>ServingLevel.ServiceQuality</c>, <c>ActionSpeed</c>...). The skill definitions are the ones the game's
/// own staff actions use, so they always match what the game reads.
/// </summary>
/// <remarks>
/// How the game uses them: <c>CookingLevel.ActionSpeed</c> multiplies a kitchen step's time (lower is faster);
/// the other skills' <c>ActionSpeed</c> is a speed (higher is faster). A plated meal's quality is the plater's
/// <c>PreparationQuality</c> × <see cref="HappinessInfluence"/>; a waiter adds <c>ServiceQuality</c> × the same
/// influence to the order's service score when taking it and at each delivery. Someone without the skill gets
/// 0.8 quality and a 1.2 time multiplier (in the game only the player can be in that position).
/// </remarks>
[Experimental("New in 0.6.")]
public static class StaffSkills
{
    static SkillDefinition cooking, serving, cleaning, managing;

    /// <summary>The cooking skill (the game's meal and prep jobs), or null before the game's data loads.</summary>
    public static SkillDefinition Cooking => cooking ??= SkillOf<MakeMealAgentActionType>();

    /// <summary>The serving skill (the game's serve job), or null before the game's data loads.</summary>
    public static SkillDefinition Serving => serving ??= SkillOf<ServeMealAgentActionType>();

    /// <summary>The cleaning skill (the game's meal cleanup job), or null before the game's data loads.</summary>
    public static SkillDefinition Cleaning => cleaning ??= SkillOf<CleanupMealAgentActionType>();

    /// <summary>The staff managing skill (the game's manage job), or null before the game's data loads.</summary>
    public static SkillDefinition Managing => managing ??= SkillOf<ManageAgentActionType>();

    static SkillDefinition SkillOf<T>() where T : AgentActionType
    {
        try
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()))
            {
                var s = o.TryCast<T>()?.Skill;
                if (s != null) return s;
            }
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffSkills: {typeof(T).Name}: {e.Message}"); }
        return null;
    }

    /// <summary>Whether the person has the skill at all (any XP, level 0 included).</summary>
    public static bool Has(Person person, SkillDefinition skill)
    {
        try
        {
            var skills = person?.RuntimeData?.Skills;
            return skills != null && skill != null && skills.ContainsKey(skill);
        }
        catch { return false; }
    }

    /// <summary>The person's XP in the skill, or 0 without it.</summary>
    public static float Experience(Person person, SkillDefinition skill)
    {
        try { return Has(person, skill) ? person.RuntimeData.Skills[skill] : 0f; }
        catch { return 0f; }
    }

    /// <summary>
    /// The person's level in the skill as the game shows it (1 = beginner), or 0 without the skill. (The game's
    /// <c>GetLevelForExperience</c> is 0-based; its <c>GetExperienceForLevel</c> takes this shown level.)
    /// </summary>
    public static int Level(Person person, SkillDefinition skill)
    {
        try { return Has(person, skill) ? skill.GetLevelForExperience(Experience(person, skill)) + 1 : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// The person's level data for the skill (cast it: <c>TryCast&lt;CookingLevel&gt;()</c>, <c>ServingLevel</c>,
    /// <c>CleaningLevel</c>, <c>ManagingLevel</c>), or null without the skill.
    /// </summary>
    public static SkillLevelData LevelData(Person person, SkillDefinition skill) => LevelData(skill, Level(person, skill));

    /// <summary>The person's cooking level data, or null without the skill.</summary>
    public static CookingLevel CookingOf(Person person) => LevelData(person, Cooking)?.TryCast<CookingLevel>();

    /// <summary>The person's serving level data, or null without the skill.</summary>
    public static ServingLevel ServingOf(Person person) => LevelData(person, Serving)?.TryCast<ServingLevel>();

    /// <summary>A skill's level data by shown level (1 for a beginner's values), or null.</summary>
    public static SkillLevelData LevelData(SkillDefinition skill, int level)
    {
        try { return skill != null && level >= 1 && level <= skill.LevelCount ? skill[level - 1] : null; }
        catch { return null; }
    }

    /// <summary>Total XP needed for the person's next level in the skill, or -1 at the top level or without it.</summary>
    public static float NextLevelExperience(Person person, SkillDefinition skill)
    {
        int level = Level(person, skill);
        try { return level >= 1 && level < skill.LevelCount ? skill.GetExperienceForLevel(level + 1) : -1f; }
        catch { return -1f; }
    }

    /// <summary>
    /// The game's happiness factor for skill results: 0.7 + 0.5 × work satisfaction (0 to 1), so 0.7 to 1.2.
    /// Plated meal quality and a waiter's service both use it. 1 when the person has no data.
    /// </summary>
    public static float HappinessInfluence(Person person)
    {
        try { return person?.RuntimeData?.WorkSatisfaction.CurrentSkillInfluence ?? 1f; }
        catch { return 1f; }
    }

    /// <summary>Gives the person XP in a skill, as the game does when they finish work (level-ups included).</summary>
    public static void AddExperience(Person person, SkillDefinition skill, float amount)
    {
        if (person == null || skill == null || amount == 0f) return;
        try { person.AddExperience(skill, amount); }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffSkills.AddExperience: {e.Message}"); }
    }

    /// <summary>A short name for one of the work skills ("cooking", "serving"...), else the asset's name.</summary>
    public static string NameOf(SkillDefinition skill)
    {
        if (skill == null) return "?";
        if (Same(skill, Cooking)) return "cooking";
        if (Same(skill, Serving)) return "serving";
        if (Same(skill, Cleaning)) return "cleaning";
        if (Same(skill, Managing)) return "managing";
        try { return skill.name; } catch { return "?"; }
    }

    /// <summary>"serving level 3, 81/100 XP" (the game shows only levels), or "no serving".</summary>
    public static string Describe(Person person, SkillDefinition skill)
    {
        string name = NameOf(skill);
        if (!Has(person, skill)) return $"no {name}";
        float next = NextLevelExperience(person, skill);
        return $"{name} level {Level(person, skill)}, {Experience(person, skill):0.#}" + (next >= 0 ? $"/{next:0} XP" : " XP (top level)");
    }

    /// <summary>Whether two skill references are the same asset (Il2Cpp wrappers aren't reference-equal).</summary>
    public static bool Same(SkillDefinition a, SkillDefinition b) =>
        a != null && b != null && a.Pointer == b.Pointer;

    // For the "skills" dev command: every skill asset's per-level table.
    internal static object Tables()
    {
        var skills = new List<object>();
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<SkillDefinition>()))
        {
            var def = o.TryCast<SkillDefinition>();
            if (def == null) continue;
            var levels = new List<Dictionary<string, object>>();
            int n = 0;
            try { n = def.LevelCount; } catch { }
            for (int i = 0; i < n; i++)
            {
                SkillLevelData d = null;
                try { d = def[i]; } catch { }
                if (d == null) continue;
                var row = new Dictionary<string, object> { ["level"] = i + 1 };   // as the game shows it
                try { row["xpToReach"] = def.GetExperienceForLevel(i + 1); } catch { }
                if (d.TryCast<StaffSkillLevelData>() is { } st) row["actionSpeed"] = st.ActionSpeed;
                if (d.TryCast<CookingLevel>() is { } c) row["preparationQuality"] = c.PreparationQuality;
                if (d.TryCast<ServingLevel>() is { } sv) { row["serviceQuality"] = sv.ServiceQuality; row["takeOrderTime"] = sv.TakeOrderTime; }
                levels.Add(row);
            }
            string name = "?";
            try { name = def.DisplayName; } catch { }
            skills.Add(new { asset = def.name, name, levels });
        }
        return skills;
    }
}
