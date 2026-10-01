using System;
using Il2CppInterop.Runtime.InteropTypes;
using Nivalis;
using Nivalis.Apartment;
using Nivalis.Fishing;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

// Phase 9 batch (a): events the game raises itself, so the kit only subscribes; nothing is patched.
// Source: research/hook-catalog.md. Instance events re-attach after each load (see reattach).
static partial class EventPatches
{
    static void InstallBreadth()
    {
        // ---------- curfew and detection (CurfewManager) ----------
        Subscribe(nameof(GameEvents.AwarenessIncreased), () => ViaT<float>(() => CurfewManager.OnAwarenessIncrease,
            delta => Raise(nameof(GameEvents.AwarenessIncreased), () => GameEvents.RaiseAwarenessIncreased(new AwarenessArgs(delta, CurrentAwareness())))));
        Subscribe(nameof(GameEvents.SecurityLevelChanged), () => ViaT2<WorldLocation, int>(() => CurfewManager.OnSecurityLevelChanged,
            (district, level) => Raise(nameof(GameEvents.SecurityLevelChanged), () => GameEvents.RaiseSecurityLevelChanged(new SecurityLevelArgs(district, level)))));
        Subscribe(nameof(GameEvents.CurfewStarted), () => ViaCurfewEvent(cm => cm.OnCurfewStart, GameEvents.RaiseCurfewStarted));
        Subscribe(nameof(GameEvents.CurfewEnded), () => ViaCurfewEvent(cm => cm.OnCurfewEnd, GameEvents.RaiseCurfewEnded));
        Subscribe(nameof(GameEvents.CurfewWarning), () => ViaCurfewEvent(cm => cm.OnCurfewWarningEnabled, GameEvents.RaiseCurfewWarning));

        // Day end: curfew start (02:00) or sleeping closes the day. CurfewManager, SleepManager and
        // RentManager share one DayEndEvent asset. Its base class takes object listeners, which the
        // game calls on every invoke (InvokeBaseEvent), so no struct crosses a delegate.
        Subscribe(nameof(GameEvents.DayEnded), () =>
        {
            Il2CppSystem.Action<Il2CppSystem.Object> action = (Action<Il2CppSystem.Object>)(_ =>
                Raise(nameof(GameEvents.DayEnded), () => GameEvents.RaiseDayEnded(new DayEndedArgs(GameTime.Day))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<CurfewManager>(), o =>
            {
                var ev = ((CurfewManager)o).dayEndEvent ?? throw new Exception("dayEndEvent not set on CurfewManager");
                ev.Subscribe(0, action);
            });
        });

        // ---------- fishing ----------
        Subscribe(nameof(GameEvents.FishCaught), () =>
        {
            Il2CppSystem.Action<ItemType> action = (Action<ItemType>)(item =>
                Raise(nameof(GameEvents.FishCaught), () => GameEvents.RaiseFishCaught(new ItemArgs(item))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<FishingManager>(), o => ((FishingManager)o).add_OnItemCollected(action));
        });
        Subscribe(nameof(GameEvents.FishDiscovered), () =>
        {
            Il2CppSystem.Action<ItemType> action = (Action<ItemType>)(item =>
                Raise(nameof(GameEvents.FishDiscovered), () => GameEvents.RaiseFishDiscovered(new ItemArgs(item))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<FishingManager>()?.Database, o => ((FishDatabase)o).add_onFishUnlocked(action));
        });

        // ---------- farming ----------
        Subscribe(nameof(GameEvents.CropPlanted), () =>
        {
            Il2CppSystem.Action<ItemContainer, ItemType> action = (Action<ItemContainer, ItemType>)((_, plant) =>
                Raise(nameof(GameEvents.CropPlanted), () => GameEvents.RaiseCropPlanted(new ItemArgs(plant))));
            keepAlive.Add(action);
            GreenhouseModuleGhost.add_OnPlanted(action);   // static event: once is enough
            return () => true;
        });
        Subscribe(nameof(GameEvents.CropHarvested), () =>
        {
            Il2CppSystem.Action<ItemType, bool> action = (Action<ItemType, bool>)((plant, firstTime) =>
                Raise(nameof(GameEvents.CropHarvested), () => GameEvents.RaiseCropHarvested(new CropHarvestedArgs(plant, firstTime))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<GreenhouseManager>(), o => ((GreenhouseManager)o).add_onPlantHarvested(action));
        });

        // ---------- property, rent, furniture, apartment ----------
        Subscribe(nameof(GameEvents.PropertyOwnerChanged), () =>
        {
            Il2CppSystem.Action<BaseProperty> action = (Action<BaseProperty>)(p =>
                Raise(nameof(GameEvents.PropertyOwnerChanged), () => GameEvents.RaisePropertyOwnerChanged(new PropertyArgs(p))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<PropertyManager>(), o => ((PropertyManager)o).OnPropertyOwnerChanged.Value.Add(action));
        });
        Subscribe(nameof(GameEvents.RentStarted), () => ViaRentField(true));
        Subscribe(nameof(GameEvents.RentStopped), () => ViaRentField(false));
        Subscribe(nameof(GameEvents.FurniturePlaced), () => ViaT<HoldableEntity>(() => PlacementSystem.OnObjectPlace,
            e => Raise(nameof(GameEvents.FurniturePlaced), () => GameEvents.RaiseFurniturePlaced(new FurnitureArgs(e)))));
        Subscribe(nameof(GameEvents.FurnitureStored), () => ViaT<HoldableEntity>(() => PlacementSystem.OnObjectStore,
            e => Raise(nameof(GameEvents.FurnitureStored), () => GameEvents.RaiseFurnitureStored(new FurnitureArgs(e)))));
        Subscribe(nameof(GameEvents.ApartmentEntered), () => ViaApartment(true));
        Subscribe(nameof(GameEvents.ApartmentLeft), () => ViaApartment(false));

        // ---------- venues ----------
        Subscribe(nameof(GameEvents.VenueOwnerChanged), () =>
        {
            Il2CppSystem.Action<Venue> action = (Action<Venue>)(v =>
                Raise(nameof(GameEvents.VenueOwnerChanged), () => GameEvents.RaiseVenueOwnerChanged(new VenueOwnerArgs(v))));
            keepAlive.Add(action);
            return ViaInstance(() => Single<VenueManager>(), o => ((VenueManager)o).OnVenueOwnerChanged.Value.Add(action));
        });
    }

    // ---------- helpers ----------

    // Every handler body runs through here: never throw into game code.
    static void Raise(string what, Action raise)
    {
        try { raise(); }
        catch (Exception e) { KitPlugin.L.LogError($"{what}: {e}"); }
    }

    static T Single<T>() where T : UnityEngine.MonoBehaviour =>
        Singleton<T>.InstanceExist(out var instance) ? instance : null;

    static float CurrentAwareness()
    {
        try { return Single<CurfewManager>()?.Awarness ?? 0f; } catch { return 0f; }
    }

    static Func<bool> ViaT<T>(Func<ActionNonAlloc<T>> source, Action<T> handler)
    {
        Il2CppSystem.Action<T> action = handler;
        keepAlive.Add(action);
        return () =>
        {
            var target = source();
            if (target == null) return false;
            target.Add(action);
            return true;
        };
    }

    static Func<bool> ViaT2<T1, T2>(Func<ActionNonAlloc<T1, T2>> source, Action<T1, T2> handler)
    {
        Il2CppSystem.Action<T1, T2> action = handler;
        keepAlive.Add(action);
        return () =>
        {
            var target = source();
            if (target == null) return false;
            target.Add(action);
            return true;
        };
    }

    // Attach to whatever owner() currently returns; skips an owner it already holds. Registered
    // for re-attachment after loads, since managers may be rebuilt.
    static Func<bool> ViaInstance(Func<Il2CppObjectBase> owner, Action<Il2CppObjectBase> attach)
    {
        IntPtr attachedTo = IntPtr.Zero;
        Func<bool> f = () =>
        {
            var o = owner();
            if (o == null) return false;
            if (o.Pointer == attachedTo) return true;
            attach(o);
            attachedTo = o.Pointer;
            return true;
        };
        reattach.Add(f);
        return f;
    }

    // CurfewManager's start/end/warning are ScriptableObjectEvents on the instance.
    static Func<bool> ViaCurfewEvent(Func<CurfewManager, ScriptableObjectEvent> source, Action raise)
    {
        Il2CppSystem.Action action = (Action)(() => Raise("Curfew event", raise));
        keepAlive.Add(action);
        return ViaInstance(() => Single<CurfewManager>(), o =>
        {
            var ev = source((CurfewManager)o) ?? throw new Exception("event not set on CurfewManager");
            ev.Subscribe(action);
        });
    }

    // RentManager.OnPropertyRented / OnPropertyStoppedRenting are plain delegate fields: chain onto them.
    static Func<bool> ViaRentField(bool started)
    {
        Il2CppSystem.Action<BaseProperty> action = (Action<BaseProperty>)(p => Raise(started ? "RentStarted" : "RentStopped", () =>
        {
            if (started) GameEvents.RaiseRentStarted(new PropertyArgs(p));
            else GameEvents.RaiseRentStopped(new PropertyArgs(p));
        }));
        keepAlive.Add(action);
        return ViaInstance(() => Single<RentManager>(), o =>
        {
            var rm = (RentManager)o;
            var current = started ? rm.OnPropertyRented : rm.OnPropertyStoppedRenting;
            var combined = current == null ? action
                : Il2CppSystem.Delegate.Combine(current, action).Cast<Il2CppSystem.Action<BaseProperty>>();
            if (started) rm.OnPropertyRented = combined;
            else rm.OnPropertyStoppedRenting = combined;
        });
    }

    // One static game event (controller, entered) feeds both kit events; each subscription filters.
    static Func<bool> ViaApartment(bool entered) =>
        ViaT2<ApartmentController, bool>(() => ApartmentController.OnPlayerEnterExit, (controller, isEnter) =>
        {
            if (isEnter != entered) return;
            Raise(entered ? "ApartmentEntered" : "ApartmentLeft", () =>
            {
                if (entered) GameEvents.RaiseApartmentEntered(new ApartmentArgs(controller));
                else GameEvents.RaiseApartmentLeft(new ApartmentArgs(controller));
            });
        });
}
