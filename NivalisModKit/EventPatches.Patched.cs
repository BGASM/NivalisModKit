using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.Boat;
using Nivalis.GhostSystem;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.Locale.UI;
using Nivalis.SkillSystem;

namespace NivalisModKit;

// Phase 9 batch (b): events that need a patch. Every target was checked for a unique address
// (research/hook-catalog.md) and is picked by exact parameter types.
static partial class EventPatches
{
    static void InstallPatched()
    {
        Type[] Args(params Type[] t) => t;

        // ---------- staff ----------
        Install(nameof(GameEvents.StaffHired), () => typeof(VenueManager), nameof(VenueManager.HireStaff),
            postfix: nameof(StaffHiredPostfix), args: () => Args(typeof(Venue), typeof(Person)));
        Install(nameof(GameEvents.StaffFired), () => typeof(VenueManager), nameof(VenueManager.FireStaff),
            postfix: nameof(StaffFiredPostfix), args: () => Args(typeof(Venue), typeof(Person)));
        // PayStaff charges RuntimeData.Wage only if the owner can afford it (otherwise the
        // employee's happiness drops), so compare the owner's money before and after.
        Install(nameof(GameEvents.StaffPaid), () => typeof(VenueAreaGhost), "PayStaff",
            prefix: nameof(StaffPaidPrefix), postfix: nameof(StaffPaidPostfix), args: () => Args(typeof(Person)));

        // The game's own events cover placing and storing; lifting into the hands is this method.
        Install(nameof(GameEvents.FurniturePickedUp), () => typeof(PlacementSystem), "OnPickUp",
            postfix: nameof(PickUpPostfix), args: () => Args(typeof(HoldableEntity)));
        Install(nameof(GameEvents.StaffSkillGained), () => typeof(Person), nameof(Person.AddExperience),
            postfix: nameof(SkillGainedPostfix), args: () => Args(typeof(SkillDefinition), typeof(float)));
        // The staff screen writes roles and hours straight into RuntimePersonData, so patch its
        // controls. Roles: one toggle per role, compared before and after.
        Install(nameof(GameEvents.StaffRolesChanged), () => typeof(VenueStaffListItem), nameof(VenueStaffListItem.CookToggleListener),
            prefix: nameof(RoleTogglePrefix), postfix: nameof(RoleTogglePostfix), args: () => Args(typeof(bool)));
        if (GameEvents.Live.Contains(nameof(GameEvents.StaffRolesChanged)))
        {
            Helper("serve toggle for StaffRolesChanged", () => typeof(VenueStaffListItem), nameof(VenueStaffListItem.ServeToggleListener),
                prefix: nameof(RoleTogglePrefix), postfix: nameof(RoleTogglePostfix));
            Helper("clean toggle for StaffRolesChanged", () => typeof(VenueStaffListItem), nameof(VenueStaffListItem.CleanToggleListener),
                prefix: nameof(RoleTogglePrefix), postfix: nameof(RoleTogglePostfix));
            Helper("manage toggle for StaffRolesChanged", () => typeof(VenueStaffListItem), nameof(VenueStaffListItem.ManageToggleListener),
                prefix: nameof(RoleTogglePrefix), postfix: nameof(RoleTogglePostfix));
            // Not called by the staff screen, but it is the game's API for the same change.
            Helper("ChangeStaffTasks for StaffRolesChanged", () => typeof(VenueManager), nameof(VenueManager.ChangeStaffTasks),
                postfix: nameof(RolesChangedPostfix));
        }
        // Hours: the slider writes on every step of a drag, so report once it has settled.
        Install(nameof(GameEvents.StaffHoursChanged), () => typeof(VenueStaffListItem), nameof(VenueStaffListItem.OnTimeSlotValueChanged),
            prefix: nameof(TimeSlotPrefix), args: () => Args(typeof(UnityEngine.Vector2)));
        if (GameEvents.Live.Contains(nameof(GameEvents.StaffHoursChanged))) KitLoop.Tick += FlushHours;

        // ---------- theft and security ----------
        Install(nameof(GameEvents.TheftCommitted), () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.CommitCrime),
            postfix: nameof(TheftPostfix), args: () => Args(typeof(Ghost)));
        // The catch itself (awareness reached 1). Patched rather than the game's OnPlayerCaught so the
        // event can say who spotted the player; Tuning.Catch can cancel it.
        Install(nameof(GameEvents.PlayerCaught), () => typeof(CurfewManager), nameof(CurfewManager.CatchPlayer),
            postfix: nameof(CatchPostfix), args: () => Args(typeof(bool)));
        Install(nameof(GameEvents.CameraDisabled), () => typeof(CurfewManager), nameof(CurfewManager.RegisterDisabledSecurityCamera),
            postfix: nameof(CameraDisabledPostfix), args: () => Args(typeof(SecurityCamera)));

        // ---------- boat ----------
        Install(nameof(GameEvents.BoatBoarded), () => typeof(BoatCockpit), nameof(BoatCockpit.Enter),
            postfix: nameof(BoatBoardedPostfix), args: () => Args(typeof(PlayerCharacter)));
        Install(nameof(GameEvents.BoatLeft), () => typeof(BoatCockpit), nameof(BoatCockpit.Exit),
            postfix: nameof(BoatLeftPostfix), args: () => Type.EmptyTypes);
        Install(nameof(GameEvents.BoatDocked), () => typeof(BoatDock), nameof(BoatDock.Dock),
            postfix: nameof(BoatDockedPostfix), args: () => Args(typeof(BoatController)));
        Install(nameof(GameEvents.BoatUndocked), () => typeof(BoatDock), nameof(BoatDock.Undock),
            postfix: nameof(BoatUndockedPostfix), args: () => Type.EmptyTypes);
        Install(nameof(GameEvents.BoatTravel), () => typeof(TravelManager), nameof(TravelManager.RequestTravel),
            postfix: nameof(BoatTravelPostfix), args: () => Args(typeof(PortalKey), typeof(bool), typeof(bool), typeof(BoatGhost)));
        // Fuel changes every frame while driving, so report whole refuels: fuel at start vs stop.
        Helper("fuel at start for BoatRefueled", () => typeof(BoatFuelDistributor), "TryStartFueling",
            postfix: nameof(FuelStartPostfix));
        Install(nameof(GameEvents.BoatRefueled), () => typeof(BoatFuelDistributor), "StopFueling",
            postfix: nameof(FuelStopPostfix), args: () => Type.EmptyTypes);

        // The end-of-day summary screen (venues, finances, debt, progress).
        Install(nameof(GameEvents.EndOfDayShown), () => typeof(Nivalis.UI.EndOfDayWindow), nameof(Nivalis.UI.EndOfDayWindow.Show),
            postfix: nameof(EndOfDayShownPostfix), args: () => Type.EmptyTypes);

        // ---------- venues open/close: derived from the hourly venue update, no patch ----------
        if (GameEvents.Live.Contains(nameof(GameEvents.VenueHour)))
        {
            GameEvents.Live.Add(nameof(GameEvents.VenueOpened));
            GameEvents.Live.Add(nameof(GameEvents.VenueClosed));
            KitPlugin.L.LogInfo("Event VenueOpened, VenueClosed: live (from VenueHour)");
        }
        else KitPlugin.L.LogWarning("Event VenueOpened, VenueClosed: missing (needs VenueHour)");
        attempted += 2;
    }

    // ---------- staff ----------

    static void StaffHiredPostfix(Venue forVenue, Person person) =>
        Raise(nameof(GameEvents.StaffHired), () => GameEvents.RaiseStaffHired(new StaffArgs(forVenue, person)));

    static void StaffFiredPostfix(Venue forVenue, Person person) =>
        Raise(nameof(GameEvents.StaffFired), () => GameEvents.RaiseStaffFired(new StaffArgs(forVenue, person)));

    static int? OwnerMoney(VenueAreaGhost area)
    {
        try
        {
            var inv = area?.ownerInventory;
            return inv == null ? null : new Nivalis.InventorySystem.IMoneyContainer(inv.Pointer).Money;
        }
        catch { return null; }
    }

    static void StaffPaidPrefix(VenueAreaGhost __instance, out int __state) =>
        __state = OwnerMoney(__instance) ?? int.MinValue;

    static void StaffPaidPostfix(VenueAreaGhost __instance, Person staff, int __state) =>
        Raise(nameof(GameEvents.StaffPaid), () =>
        {
            int wage = 0;
            try { wage = staff?.RuntimeData?.Wage ?? 0; } catch { }
            int? after = OwnerMoney(__instance);
            // Unknown money: assume the game's normal path (paid). Otherwise paid if money went down.
            bool paid = __state == int.MinValue || after == null || after.Value < __state;
            GameEvents.RaiseStaffPaid(new StaffPaidArgs(__instance, staff, wage, paid));
        });

    static void EndOfDayShownPostfix() => Raise(nameof(GameEvents.EndOfDayShown), GameEvents.RaiseEndOfDayShown);

    static void PickUpPostfix(HoldableEntity entity) =>
        Raise(nameof(GameEvents.FurniturePickedUp), () => GameEvents.RaiseFurniturePickedUp(new FurnitureArgs(entity)));

    static void SkillGainedPostfix(Person __instance, SkillDefinition skill, float value) =>
        Raise(nameof(GameEvents.StaffSkillGained), () => GameEvents.RaiseStaffSkillGained(new StaffSkillArgs(__instance, skill, value)));

    static void RolesChangedPostfix(Venue forVenue, RuntimePersonData person, VenueTasks venueTasks) =>
        Raise(nameof(GameEvents.StaffRolesChanged), () => GameEvents.RaiseStaffRolesChanged(new StaffRolesArgs(forVenue, person, venueTasks)));

    static RuntimePersonData DataOf(VenueStaffListItem item)
    {
        try { return item?._person?.RuntimeData; } catch { return null; }
    }

    // __state: the roles before the click, or -1 if unknown.
    static void RoleTogglePrefix(VenueStaffListItem __instance, out int __state)
    {
        __state = -1;
        try { var d = DataOf(__instance); if (d != null) __state = (int)d.Tasks; } catch { }
    }

    static void RoleTogglePostfix(VenueStaffListItem __instance, int __state) =>
        Raise(nameof(GameEvents.StaffRolesChanged), () =>
        {
            var d = DataOf(__instance);
            if (d == null || __state < 0 || (int)d.Tasks == __state) return;   // turning off the last role is refused
            GameEvents.RaiseStaffRolesChanged(new StaffRolesArgs(d.WorksAt, d, d.Tasks));
        });

    // Hours being dragged: hours before the drag, and when the slider last moved (real time,
    // since the game may be paused behind the window).
    sealed class HoursEdit { public RuntimePersonData Data; public UnityEngine.Vector2 Before; public float Last; }
    static readonly Dictionary<IntPtr, HoursEdit> hoursEdits = new();
    const float HoursSettleSeconds = 0.75f;

    static void TimeSlotPrefix(VenueStaffListItem __instance)
    {
        try
        {
            var d = DataOf(__instance);
            if (d == null) return;
            if (!hoursEdits.TryGetValue(d.Pointer, out var edit))
                hoursEdits[d.Pointer] = edit = new HoursEdit { Data = d, Before = d.WorkingHours };
            edit.Last = UnityEngine.Time.realtimeSinceStartup;
        }
        catch { }
    }

    static void FlushHours()
    {
        if (hoursEdits.Count == 0) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        List<IntPtr> done = null;
        foreach (var pair in hoursEdits)
        {
            var edit = pair.Value;
            if (now - edit.Last < HoursSettleSeconds) continue;
            (done ??= new()).Add(pair.Key);
            Raise(nameof(GameEvents.StaffHoursChanged), () =>
            {
                var after = edit.Data.WorkingHours;
                if (after == edit.Before) return;   // dragged back to where it was
                GameEvents.RaiseStaffHoursChanged(new StaffHoursArgs(edit.Data.WorksAt, edit.Data, edit.Before, after));
            });
        }
        if (done != null) foreach (var k in done) hoursEdits.Remove(k);
    }

    // ---------- theft and security ----------

    static void TheftPostfix(VenueAreaGhost __instance, Ghost ghost) =>
        Raise(nameof(GameEvents.TheftCommitted), () => GameEvents.RaiseTheftCommitted(new TheftArgs(__instance, ghost)));

    static void CatchPostfix(bool byDrone) =>
        Raise(nameof(GameEvents.PlayerCaught), () =>
        {
            if (Tuning.ConsumeCatchCancelled()) return;   // a mod let the player off
            WorldLocation district = null;
            try { if (Singleton<GameSceneManager>.InstanceExist(out var gsm)) district = gsm.CurrentWorldLocation; } catch { }
            GameEvents.RaisePlayerCaught(new PlayerCaughtArgs(byDrone, district, Security.Level));
        });

    static void CameraDisabledPostfix(SecurityCamera camera) =>
        Raise(nameof(GameEvents.CameraDisabled), () => GameEvents.RaiseCameraDisabled(new CameraArgs(camera)));

    // ---------- boat ----------

    static void BoatBoardedPostfix() => Raise(nameof(GameEvents.BoatBoarded), GameEvents.RaiseBoatBoarded);
    static void BoatLeftPostfix() => Raise(nameof(GameEvents.BoatLeft), GameEvents.RaiseBoatLeft);
    static void BoatDockedPostfix(BoatDock __instance) =>
        Raise(nameof(GameEvents.BoatDocked), () => GameEvents.RaiseBoatDocked(new BoatDockArgs(__instance)));
    static void BoatUndockedPostfix(BoatDock __instance) =>
        Raise(nameof(GameEvents.BoatUndocked), () => GameEvents.RaiseBoatUndocked(new BoatDockArgs(__instance)));

    static void BoatTravelPostfix(PortalKey keyTo, BoatGhost boat) =>
        Raise(nameof(GameEvents.BoatTravel), () => { if (boat != null) GameEvents.RaiseBoatTravel(new BoatTravelArgs(keyTo)); });

    static float fuelAtStart = -1;

    static float BoatFuel()
    {
        try { return BoatGhost.FindBoat()?.Fuel ?? -1; } catch { return -1; }
    }

    static void FuelStartPostfix() => fuelAtStart = BoatFuel();

    static void FuelStopPostfix() =>
        Raise(nameof(GameEvents.BoatRefueled), () =>
        {
            float now = BoatFuel();
            if (fuelAtStart >= 0 && now > fuelAtStart)
                GameEvents.RaiseBoatRefueled(new BoatRefueledArgs(now - fuelAtStart, now));
            fuelAtStart = -1;
        });

    // ---------- venue open/close, from VenueHourPostfix ----------

    static readonly Dictionary<IntPtr, bool> venueWasOpen = new();

    static void CheckVenueOpen(VenueAreaGhost area)
    {
        if (area == null) return;
        bool open;
        try { open = area.IsOpen?.Value ?? false; } catch { return; }
        bool known = venueWasOpen.TryGetValue(area.Pointer, out bool was);
        venueWasOpen[area.Pointer] = open;
        if (!known || was == open) return;   // first sighting only records

        if (open) Raise(nameof(GameEvents.VenueOpened), () => GameEvents.RaiseVenueOpened(new VenueArgs(area)));
        else Raise(nameof(GameEvents.VenueClosed), () => GameEvents.RaiseVenueClosed(new VenueArgs(area)));
    }
}
