using System;
using System.Collections.Generic;
using HarmonyLib;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NivalisModKit;

// Mod jobs in the venue's Staff tab: per row, one copy of the row's Cook job per registered job, with the job's icon,
// badge and toggle. Rows are reused by the game, so a copy is made once and kept.
static class StaffJobsUi
{
    const float Slot = 68f;   // one job's width in the row
    static readonly Color On = new Color32(0xcc, 0xa2, 0x69, 0xff), Off = new Color32(0x68, 0x68, 0x68, 0xff);   // if the Cook icon can't be read

    static readonly Dictionary<int, (StaffJob job, Person person, Toggle toggle)> toggles = new();   // toggle instance id ->
    static readonly Dictionary<int, float> baseSize = new();   // badge label -> the prefab's font size
    static VenueStaffOverviewTab lastTab;
    static Venue lastVenue;

    internal static void Install(Harmony harmony)
    {
        try
        {
            harmony.Patch(AccessTools.Method(typeof(VenueStaffOverviewTab), nameof(VenueStaffOverviewTab.RefreshStaffList), new[] { typeof(Venue) }),
                postfix: new HarmonyMethod(typeof(StaffJobsUi), nameof(AfterRefresh)));
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: staff tab: missing ({e.Message})"); }
        DevCommands.Register(ModKit.Guid, "staff-jobs-ui", "The first mod job's UI tree next to the Cook job's (for layout problems)", _ => Inspect());
    }

    static void AfterRefresh(VenueStaffOverviewTab __instance, Venue venue)
    {
        lastTab = __instance;
        lastVenue = venue;
        try { Decorate(__instance, venue); }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: staff tab: {e.Message}"); }
    }

    internal static void Refresh()
    {
        try { if (lastTab != null && lastTab.isActiveAndEnabled) Decorate(lastTab, lastVenue); } catch { }
    }

    static string Key(StaffJob job) => "KitJob_" + job.Id;

    static void Decorate(VenueStaffOverviewTab tab, Venue venue)
    {
        var parent = tab?.staffList?._itemDisplayParent;
        if (parent == null || StaffJobs.All.Count == 0) return;

        // Rows by name, for rows whose list item can't be read.
        var byName = new Dictionary<string, Person>();
        var staff = venue?.RuntimeData?.staff?.backingList;
        if (staff != null)
            for (int i = 0; i < staff.Count; i++)
            {
                string n = StaffJobs.NameOf(staff[i]);
                if (n != "?" && !byName.ContainsKey(n)) byName[n] = staff[i];
            }

        for (int c = 0; c < parent.childCount; c++)
        {
            var row = parent.GetChild(c);
            var panel = row.Find("WorkerPanel");
            var jobsRow = panel?.Find("LeftSide/JobAssignDropdown");
            var cook = jobsRow?.Find("CookJob");
            if (jobsRow == null || cook == null) continue;

            Person person = null;
            try { person = row.GetComponent<VenueStaffListItem>()?._person; } catch { }
            if (person == null)
            {
                var label = panel.Find("LeftSide/NameAndHappiness/StaffName")?.GetComponent<TMP_Text>();
                if (label != null) byName.TryGetValue(label.text ?? "", out person);
            }

            var cookWrap = cook.Find("WrapperCook");
            var waiterWrap = jobsRow.Find("WaiterJob/WrapperWaiter");
            foreach (var job in StaffJobs.All)
            {
                var copy = jobsRow.Find(Key(job)) ?? Build(jobsRow, cook, job);
                var wrapper = copy?.Find("WrapperCook");
                if (wrapper == null) continue;

                bool show = row.gameObject.activeSelf && person != null && Can(job, person);
                wrapper.gameObject.SetActive(show);
                if (!show) continue;

                var toggle = wrapper.GetComponent<Toggle>();
                if (toggle != null)
                {
                    toggles[toggle.GetInstanceID()] = (job, person, toggle);
                    toggle.enabled = true;
                    toggle.interactable = true;
                    toggle.SetIsOnWithoutNotify(job.On(person));
                    try { toggle.TryCast<TheraBytes.BetterUi.BetterToggle>()?.ForceUpdate(true); } catch { }
                }

                // The game lays its jobs out after this refresh: copy a shown one's sizes once it has them.
                var source = cookWrap != null && cookWrap.gameObject.activeSelf ? cookWrap : waiterWrap;
                if (source != null)
                {
                    var w = wrapper; var offset = job.IconOffset;
                    Scheduler.NextFrame(() => Mirror(source, w, offset));
                    Scheduler.AfterSeconds(0.25f, () => Mirror(source, w, offset));
                }
                Badge(job, person, wrapper, cookWrap);
            }
        }
    }

    static bool Can(StaffJob job, Person p)
    {
        try { return job.CanDo?.Invoke(p) ?? true; } catch { return false; }
    }

    // The badge: the job's text, one line, shrunk to fit when longer than the game's one or two digits.
    static void Badge(StaffJob job, Person person, Transform wrapper, Transform cookWrap)
    {
        var level = wrapper.Find("Level/LevelText")?.GetComponent<TMP_Text>();
        if (level == null) return;
        string text = null;
        try { text = job.Badge?.Invoke(person); } catch { }
        text ??= cookWrap?.Find("Level/LevelText")?.GetComponent<TMP_Text>()?.text ?? "";
        int id = level.GetInstanceID();
        if (!baseSize.ContainsKey(id)) baseSize[id] = level.fontSize;
        float size = baseSize[id];
        level.text = text;
        level.enableWordWrapping = false;
        level.overflowMode = TextOverflowModes.Overflow;
        level.enableAutoSizing = text.Length > 2;
        level.fontSizeMax = size;
        level.fontSizeMin = size * 0.4f;
        if (!level.enableAutoSizing) level.fontSize = size;
    }

    // ---------- the copy ----------

    static Transform Build(Transform jobsRow, Transform cook, StaffJob job)
    {
        try
        {
            var go = UnityEngine.Object.Instantiate(cook.gameObject, jobsRow);
            go.name = Key(job);
            // Cut it loose from the game: no skill display (it would flip the Cooking job), no listeners from it.
            var display = go.GetComponent<LocaleStaffSkillDisplayUi>();
            if (display != null) UnityEngine.Object.DestroyImmediate(display);
            // The row drives the real job toggles, not this copy, so the copy's only listener is BetterToggle's own
            // (its on/off look): keep it and add ours.
            var toggle = go.transform.Find("WrapperCook")?.GetComponent<Toggle>();
            if (toggle != null)
            {
                toggle.enabled = true;   // the prefab's toggle starts disabled; the row enables the real ones at setup
                toggle.interactable = true;
                toggle.group = null;
                int id = toggle.GetInstanceID();
                toggle.onValueChanged.AddListener(
                    Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>(new Action<bool>(on => Changed(id, on))));
            }
            // The job's icon, coloured like the row's own Cook icons (grey off, gold on).
            var sprite = SpriteOf(job);
            foreach (var (part, fallback) in new[] { ("Job", Off), ("Job_On", On) })
            {
                var img = go.transform.Find($"WrapperCook/JobWrapper/{part}")?.GetComponent<Image>();
                if (img == null || sprite == null) continue;
                var native = cook.Find($"WrapperCook/JobWrapper/{part}")?.GetComponent<Image>();
                img.sprite = sprite;
                img.color = native != null ? native.color : fallback;
                img.preserveAspect = true;
            }
            // Room for one more job: widen the job row and its row by one slot.
            Widen(jobsRow);
            Widen(jobsRow.parent);
            Match(cook, go.transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(jobsRow.parent.GetComponent<RectTransform>());
            return go.transform;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: couldn't add {job.Name ?? job.Id}: {e.Message}"); return null; }
    }

    static Sprite SpriteOf(StaffJob job)
    {
        if (job.Icon != null) return job.Icon;
        if (job.sprite != null || job.IconPng == null) return job.sprite;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            ImageConversion.LoadImage(tex, job.IconPng);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            job.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            job.sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            job.sprite.name = job.Name ?? job.Id;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: {job.Id} icon: {e.Message}"); }
        return job.sprite;
    }

    static void Widen(Transform t)
    {
        var le = t?.GetComponent<LayoutElement>();
        if (le == null) return;
        if (le.preferredWidth > 0f) le.preferredWidth += Slot;
        if (le.minWidth > 0f) le.minWidth += Slot;
    }

    // Every element of the copy takes the original's size and place, and its images are on and opaque.
    static void Match(Transform from, Transform to)
    {
        try
        {
            var a = from.GetComponent<RectTransform>();
            var b = to.GetComponent<RectTransform>();
            if (a != null && b != null)
            {
                b.anchorMin = a.anchorMin; b.anchorMax = a.anchorMax; b.pivot = a.pivot;
                b.sizeDelta = a.sizeDelta; b.anchoredPosition = a.anchoredPosition; b.localScale = a.localScale;
            }
            var img = to.GetComponent<Image>();
            if (img != null) { img.enabled = true; to.GetComponent<CanvasRenderer>()?.SetAlpha(1f); }
            for (int i = 0; i < from.childCount && i < to.childCount; i++) Match(from.GetChild(i), to.GetChild(i));
        }
        catch { }
    }

    // A shown job's live layout onto the copy, below its top level (whose slot the row's layout decides): the game's
    // skill display sizes the icon area and badge at runtime, and the copy has none, so its own layout group would
    // squash them. That layout group is switched off and the sizes copied instead.
    static void Mirror(Transform from, Transform to, Vector2 offset)
    {
        try
        {
            if (from == null || to == null) return;
            if (from.childCount == 0 || from.GetChild(0).GetComponent<RectTransform>()?.rect.width <= 0f) return;   // not laid out yet
            var group = to.GetComponent<VerticalLayoutGroup>();
            if (group != null) group.enabled = false;
            for (int i = 0; i < from.childCount && i < to.childCount; i++) Copy(from.GetChild(i), to.GetChild(i));
            if (offset != Vector2.zero)
                foreach (var part in new[] { "JobWrapper/Job", "JobWrapper/Job_On" })
                {
                    var rt = to.Find(part)?.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition += offset;
                }
        }
        catch { }
    }

    static void Copy(Transform from, Transform to)
    {
        var a = from.GetComponent<RectTransform>();
        var b = to.GetComponent<RectTransform>();
        if (a != null && b != null)
        {
            b.anchorMin = a.anchorMin; b.anchorMax = a.anchorMax; b.pivot = a.pivot;
            b.sizeDelta = a.sizeDelta; b.anchoredPosition = a.anchoredPosition; b.localScale = Vector3.one;
        }
        var la = from.GetComponent<LayoutElement>();
        var lb = to.GetComponent<LayoutElement>();
        if (la != null && lb != null)
        {
            lb.minWidth = la.minWidth; lb.minHeight = la.minHeight;
            lb.preferredWidth = la.preferredWidth; lb.preferredHeight = la.preferredHeight;
            lb.flexibleWidth = la.flexibleWidth; lb.flexibleHeight = la.flexibleHeight; lb.ignoreLayout = la.ignoreLayout;
        }
        for (int i = 0; i < from.childCount && i < to.childCount; i++) Copy(from.GetChild(i), to.GetChild(i));
    }

    // ---------- the player's click ----------

    static void Changed(int id, bool on)
    {
        try
        {
            if (!toggles.TryGetValue(id, out var t) || t.person == null) return;
            var (job, person, toggle) = t;
            if (job.On(person) == on) return;
            if (!on && !StaffJobs.HasGameJob(person) && !StaffJobs.OtherJobOn(job, person))
            {
                // Their last job: put the toggle back, as the game does for its own jobs.
                Scheduler.NextFrame(() =>
                {
                    try
                    {
                        toggle.SetIsOnWithoutNotify(true);
                        toggle.TryCast<TheraBytes.BetterUi.BetterToggle>()?.ForceUpdate(true);
                    }
                    catch { }
                });
                return;
            }
            job.Set(person, on);
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"StaffJobs: {e.Message}"); }
    }

    // ---------- dev command ----------

    static object Inspect()
    {
        var parent = lastTab?.staffList?._itemDisplayParent;
        if (parent == null) return "open a venue's Staff tab";
        if (StaffJobs.All.Count == 0) return "no mod jobs registered";
        var key = Key(StaffJobs.All[0]);
        for (int c = 0; c < parent.childCount; c++)
        {
            var jobsRow = parent.GetChild(c).Find("WorkerPanel/LeftSide/JobAssignDropdown");
            var mine = jobsRow?.Find(key);
            var cook = jobsRow?.Find("CookJob");
            if (mine == null || cook == null || !(mine.Find("WrapperCook")?.gameObject.activeSelf ?? false)) continue;
            return new { cook = Tree(cook), job = Tree(mine) };
        }
        return "no row with the job showing";
    }

    static object Tree(Transform t)
    {
        var rt = t.GetComponent<RectTransform>();
        var kids = new List<object>();
        for (int i = 0; i < t.childCount; i++) kids.Add(Tree(t.GetChild(i)));
        return new
        {
            name = t.name, active = t.gameObject.activeSelf,
            size = rt != null ? $"{rt.rect.width:0}x{rt.rect.height:0}" : null,
            pos = rt != null ? $"{rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}" : null,
            children = kids,
        };
    }
}
