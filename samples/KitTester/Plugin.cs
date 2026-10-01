using System;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Nivalis;
using Nivalis.Economy;
using Nivalis.InventorySystem;
using NivalisModKit;

namespace KitTester;

// Logs every kit event. Doubles as the regression test after game updates:
// every kit feature should be exercised here.
[BepInPlugin("bgasm.nivalis.kittester", "Kit Tester", "0.1.0")]
[BepInDependency(ModKit.Guid)]
public class Plugin : BasePlugin
{
    internal static ManualLogSource L;

    static readonly string[] Events =
    {
        nameof(GameEvents.BuyIngredientsStarting),
        nameof(GameEvents.BuyIngredientsFinished),
        nameof(GameEvents.VenueHour),
        nameof(GameEvents.IngredientsPurchased),
        nameof(GameEvents.EquipmentPurchased),
        nameof(GameEvents.DayStarted),
        nameof(GameEvents.HourStarted),
        nameof(GameEvents.NewGameStarted),
        nameof(GameEvents.GameLoaded),
        nameof(GameEvents.GameSaved),
        nameof(GameEvents.DistrictEntered),
        nameof(GameEvents.DishCooked),
        nameof(GameEvents.SaleMade),
        nameof(GameEvents.DeliveryCompleted),
        nameof(GameEvents.ShopOpened),
        nameof(GameEvents.ShopClosed),
        nameof(GameEvents.PlayerBought),
        nameof(GameEvents.PlayerSold),
        nameof(GameEvents.MoneyChanged),
        nameof(GameEvents.QuestStarted),
        nameof(GameEvents.QuestCompleted),
        nameof(GameEvents.QuestFailed),
        nameof(GameEvents.QuestObjectiveStarted),
        nameof(GameEvents.QuestObjectiveCompleted),
        nameof(GameEvents.QuestPinnedChanged),
        nameof(GameEvents.QuestMarkerAdded),
        nameof(GameEvents.QuestMarkerRemoved),
        nameof(GameEvents.VenueSetupQuestUpdated),
    };

    // City-wide counts since the last HourStarted; the player's venues are logged line by line.
    static int dishes, sales, deliveries, salesTotal;

    static int hourUpdates;
    static bool worldChecked;
    static bool pipelineChecked;
    static int bought, skipped, failed;

    public override void Load()
    {
        L = Log;
        L.LogInfo($"Kit Tester loaded against {ModKit.Name} {ModKit.Version}");

        foreach (string ev in Events)
            L.LogInfo($"IsAvailable({ev}) = {GameEvents.IsAvailable(ev)}");

        CheckStructLayout();
        CheckNativeHook();

        GameEvents.BuyIngredientsStarting += a =>
        {
            if (!pipelineChecked) { pipelineChecked = true; L.LogInfo($"Purchasing.IsAvailable = {Purchasing.IsAvailable}"); }
            bought = skipped = failed = 0;
            L.LogInfo($"BuyIngredientsStarting: {NameOf(a.Area?.Venue)} / {RecipeName(a)}");
        };
        GameEvents.BuyIngredientsFinished += a =>
            L.LogInfo($"BuyIngredientsFinished: {NameOf(a.Area?.Venue)} / {RecipeName(a)} bought={a.Bought} " +
                      $"(pipeline: {bought} bought, {skipped} skipped, {failed} failed)");

        // Read-only; doesn't turn the pipeline on. Counts per recipe, shown on the Finished line.
        Purchasing.Decision += d =>
        {
            if (d.Result == PurchaseResult.Bought) bought++;
            else if (d.Result == PurchaseResult.Skipped) skipped++;
            else failed++;
        };
        GameEvents.IngredientsPurchased += a =>
            L.LogInfo($"IngredientsPurchased: {NameOf(a.Area?.Venue)} / {NameOf(a.Item)} x{a.Count} for {a.TotalPrice}");
        GameEvents.VenueHour += OnVenueHour;
        GameEvents.EquipmentPurchased += a =>
            L.LogInfo($"EquipmentPurchased: {NameOf(a.Area?.Venue)} / {NameOf(a.Item)} x{a.Count} for {a.TotalPrice}");
        GameEvents.DayStarted += a => L.LogInfo($"DayStarted: day {a.Day}, {a.DayOfWeek}");
        GameEvents.HourStarted += a =>
        {
            L.LogInfo($"HourStarted: day {a.Day}, {a.Hour:00}:00 (last hour, city: {dishes} dishes, " +
                      $"{sales} sales for {salesTotal}, {deliveries} deliveries)");
            dishes = sales = deliveries = salesTotal = 0;
        };

        GameEvents.NewGameStarted += () => L.LogInfo("NewGameStarted");
        GameEvents.GameLoaded += a => L.LogInfo($"GameLoaded: {a.SaveName ?? "?"} in {World.NameOf(a.District)}");
        GameEvents.GameSaved += a => L.LogInfo($"GameSaved: {a.SaveName} autosave={a.IsAutoSave}");
        GameEvents.DistrictEntered += a => L.LogInfo($"DistrictEntered: {World.NameOf(a.District)}");

        GameEvents.DishCooked += a =>
        {
            dishes++;
            if (a.Area != null && a.Area.PlayerOwned)
                L.LogInfo($"DishCooked: {NameOf(a.Area.Venue)} / {NameOf(a.Meal?.Type)} failed={a.Meal?.IsFailed}");
        };
        GameEvents.SaleMade += a =>
        {
            sales++;
            salesTotal += a.Price;
            if (a.Area == null || a.Area.PlayerOwned)
                L.LogInfo($"SaleMade: {(a.Area == null ? "vending machine" : NameOf(a.Area.Venue))} / " +
                          $"{NameOf(a.Meal)} for {a.Price} to {NameOf(a.Customer)}");
        };
        GameEvents.QuestStarted += a => L.LogInfo($"QuestStarted: {a.Title} [{a.Id}]");
        GameEvents.QuestCompleted += a => L.LogInfo($"QuestCompleted: {a.Title} [{a.Id}]");
        GameEvents.QuestFailed += a => L.LogInfo($"QuestFailed: {a.Title} [{a.Id}]");
        GameEvents.QuestObjectiveStarted += a => L.LogInfo($"QuestObjectiveStarted: {a.Quest.Title} / {a.Text} [{a.Id}]");
        GameEvents.QuestObjectiveCompleted += a => L.LogInfo($"QuestObjectiveCompleted: {a.Quest.Title} / {a.Text} [{a.Id}]");
        GameEvents.QuestPinnedChanged += a => L.LogInfo($"QuestPinnedChanged: {a.Quest.Title} pinned={a.Pinned}");
        GameEvents.QuestMarkerAdded += a => L.LogInfo($"QuestMarkerAdded: {a.PointId} scene {a.SceneIndex} for {a.Quest.Title}");
        GameEvents.QuestMarkerRemoved += a => L.LogInfo($"QuestMarkerRemoved: {a.PointId} scene {a.SceneIndex} for {a.Quest.Title}");
        GameEvents.VenueSetupQuestUpdated += a => L.LogInfo($"VenueSetupQuestUpdated: {a.Title} state={a.State}");
        GameEvents.MoneyChanged += a => L.LogInfo($"MoneyChanged: {a.Old} -> {a.New} ({a.Delta:+#;-#;0})");
        GameEvents.ShopOpened += a => L.LogInfo($"ShopOpened: {NameOf(a.Vendor)}");
        GameEvents.ShopClosed += a => L.LogInfo($"ShopClosed: {NameOf(a.Vendor)}");
        GameEvents.PlayerBought += a =>
            L.LogInfo($"PlayerBought: {NameOf(a.Item)} x{a.Count} for {a.TotalPrice} at {NameOf(a.Vendor)}");
        GameEvents.PlayerSold += a =>
            L.LogInfo($"PlayerSold: {NameOf(a.Item)} x{a.Count} for {a.TotalPrice} at {NameOf(a.Vendor)}");

        GameEvents.DeliveryCompleted += a =>
        {
            deliveries++;
            if (a.Area != null && a.Area.PlayerOwned)
                L.LogInfo($"DeliveryCompleted: {NameOf(a.Area.Venue)} by {NameOf(a.Staff)}: " +
                          string.Join(", ", a.Items.Select(kv => $"{NameOf(kv.Key)} x{kv.Value}")));
        };
    }

    // VenueHour fires for every venue in the world, so only the player's are logged,
    // with a count of all updates to show the rest are firing.
    static void OnVenueHour(VenueHourArgs a)
    {
        hourUpdates++;
        if (!worldChecked) { worldChecked = true; CheckWorld(); }
        if (a.Area == null || !a.Area.PlayerOwned) return;

        L.LogInfo($"VenueHour: {NameOf(a.Area.Venue)} ({hourUpdates} venue updates so far)");
    }

    // ---------- helper checks ----------

    // Offsets should match Order Fix 1.0's verbose "Hooked BuyItem" line.
    static void CheckStructLayout()
    {
        try
        {
            L.LogInfo("StructLayout ShopTradeRequest: " +
                      $"customer={StructLayout.FieldOffset<ShopTradeRequest>("customer")} " +
                      $"itemType={StructLayout.FieldOffset<ShopTradeRequest>("itemType")} " +
                      $"freshness={StructLayout.FieldOffset<ShopTradeRequest>("freshness")} " +
                      $"amount={StructLayout.FieldOffset<ShopTradeRequest>("amount")} " +
                      $"size={StructLayout.Size<ShopTradeRequest>()} " +
                      $"valueType={StructLayout.IsValueType<ShopTradeRequest>()}");
            L.LogInfo("StructLayout BasicTemp: " +
                      $"StackCount={StructLayout.FieldOffset<ItemStack.BasicTemp>("StackCount")} " +
                      $"size={StructLayout.Size<ItemStack.BasicTemp>()}");
        }
        catch (Exception e) { L.LogError($"StructLayout check failed: {e.Message}"); }

        try
        {
            StructLayout.FieldOffset<ShopTradeRequest>("noSuchField");
            L.LogError("StructLayout: missing field did not throw");
        }
        catch (MissingFieldException) { L.LogInfo("StructLayout: missing field throws as expected"); }
    }

    static void CheckNativeHook()
    {
        try
        {
            IntPtr p = NativeHook.MethodPointer<Vendor>(
                "NativeMethodInfoPtr_BuyItem_Public_Void_byref_ShopTradeRequest_Single_byref_BasicTemp_0");
            L.LogInfo($"NativeHook.MethodPointer(Vendor.BuyItem) = 0x{p.ToInt64():X}");
        }
        catch (Exception e) { L.LogError($"NativeHook full-name lookup failed: {e.Message}"); }

        try
        {
            NativeHook.MethodPointer<Vendor>("BuyItem");
            L.LogError("NativeHook: overloaded short name did not throw");
        }
        catch (System.Reflection.AmbiguousMatchException e)
        {
            L.LogInfo($"NativeHook: short name is ambiguous as expected ({e.Message.Split(',')[0]})");
        }
        catch (Exception e) { L.LogError($"NativeHook short-name lookup: unexpected {e.GetType().Name}: {e.Message}"); }
    }

    // Expected from Meridian Market: Docks 1, Calypso Island 5.
    static void CheckWorld()
    {
        try
        {
            var from = World.Find("Meridian Market");
            L.LogInfo($"World: {World.Locations.Count} districts, Find(\"Meridian Market\") = {World.NameOf(from)}");
            if (from == null) return;

            var hops = World.Locations
                .Select(l => (name: World.NameOf(l), hops: World.Hops(from, l)))
                .OrderBy(x => x.hops).ThenBy(x => x.name)
                .Select(x => $"{x.name} {x.hops}");
            L.LogInfo($"World.Hops from Meridian Market: {string.Join(", ", hops)}");
        }
        catch (Exception e) { L.LogError($"World check failed: {e}"); }
    }

    // ---------- names ----------

    static string RecipeName(BuyIngredientsArgs a)
    {
        try { return NameOf(a.Recipe?.Output.type); }
        catch { return "?"; }
    }

    // Unity objects print as "name (Type)"; keep the name.
    static string NameOf(Il2CppSystem.Object o)
    {
        if (o == null) return "?";
        try
        {
            string s = o.ToString();
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }
        catch { return "?"; }
    }
}
