using System;
using System.Collections.Generic;
using Nivalis;
using Nivalis.Boat;
using Nivalis.GhostSystem;
using Nivalis.GhostSystem.Ai;
using Nivalis.GhostSystem.CustomerLoop;
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
        Install(nameof(GameEvents.StaffPaid), () => typeof(VenueAreaGhost), "PayStaff",
            postfix: nameof(StaffPaidPostfix), args: () => Args(typeof(Person)));
        Install(nameof(GameEvents.StaffSkillGained), () => typeof(Person), nameof(Person.AddExperience),
            postfix: nameof(SkillGainedPostfix), args: () => Args(typeof(SkillDefinition), typeof(float)));
        Install(nameof(GameEvents.StaffRolesChanged), () => typeof(VenueManager), nameof(VenueManager.ChangeStaffTasks),
            postfix: nameof(RolesChangedPostfix), args: () => Args(typeof(Venue), typeof(RuntimePersonData), typeof(VenueTasks)));
        Install(nameof(GameEvents.StaffHoursChanged), () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.ReportStaffWorkingHoursChanged),
            postfix: nameof(StaffHoursPostfix), args: () => Type.EmptyTypes);

        // ---------- theft and security ----------
        Install(nameof(GameEvents.TheftCommitted), () => typeof(VenueAreaGhost), nameof(VenueAreaGhost.CommitCrime),
            postfix: nameof(TheftPostfix), args: () => Args(typeof(Ghost)));
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

    static void StaffPaidPostfix(VenueAreaGhost __instance, Person staff) =>
        Raise(nameof(GameEvents.StaffPaid), () =>
        {
            float wage = 0;
            try { wage = staff?.RuntimeData?.LastPaidWage ?? 0; } catch { }
            GameEvents.RaiseStaffPaid(new StaffPaidArgs(__instance, staff, wage));
        });

    static void SkillGainedPostfix(Person __instance, SkillDefinition skill, float value) =>
        Raise(nameof(GameEvents.StaffSkillGained), () => GameEvents.RaiseStaffSkillGained(new StaffSkillArgs(__instance, skill, value)));

    static void RolesChangedPostfix(Venue forVenue, RuntimePersonData person, VenueTasks venueTasks) =>
        Raise(nameof(GameEvents.StaffRolesChanged), () => GameEvents.RaiseStaffRolesChanged(new StaffRolesArgs(forVenue, person, venueTasks)));

    static void StaffHoursPostfix(VenueAreaGhost __instance) =>
        Raise(nameof(GameEvents.StaffHoursChanged), () => GameEvents.RaiseStaffHoursChanged(new VenueArgs(__instance)));

    // ---------- theft and security ----------

    static void TheftPostfix(VenueAreaGhost __instance, Ghost ghost) =>
        Raise(nameof(GameEvents.TheftCommitted), () => GameEvents.RaiseTheftCommitted(new TheftArgs(__instance, ghost)));

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
