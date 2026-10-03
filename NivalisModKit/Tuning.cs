using System;
using HarmonyLib;
using Nivalis;
using Nivalis.Apartment;
using Nivalis.Fishing;
using Nivalis.GhostSystem.CustomerLoop;
using Nivalis.InventorySystem;

namespace NivalisModKit;

/// <summary>
/// Change game rules: crop yield and growth speed, fish yield, property prices, security awareness
/// gain. Each is an event whose handlers adjust a value; nothing is patched until a mod subscribes
/// to that event. Handlers run in the order added, each seeing the previous one's value; one that
/// throws is logged and skipped. For vendor prices see <see cref="Pricing"/>.
/// </summary>
public static class Tuning
{
    static readonly Hook<CropYieldContext> cropYield = new(nameof(CropYield), () => Patch(
        typeof(GreenhouseAreaGhost), nameof(GreenhouseAreaGhost.CalculateYield), new[] { typeof(GreenhouseModuleGhost) },
        postfix: nameof(CropYieldPostfix)));
    static readonly Hook<CropGrowthContext> cropGrowth = new(nameof(CropGrowthSpeed), () => Patch(
        typeof(GreenhouseAreaGhost), nameof(GreenhouseAreaGhost.CalculateGrowthSpeed), new[] { typeof(GreenhouseModuleGhost) },
        postfix: nameof(CropGrowthPostfix)));
    static readonly Hook<FishYieldContext> fishYield = new(nameof(FishYield), () => Patch(
        typeof(FishingManager), "CalculateYield", new[] { typeof(ItemType), typeof(float) },
        postfix: nameof(FishYieldPostfix)));
    static readonly Hook<PropertyPriceContext> propertyPrice = new(nameof(PropertyPrice), () =>
    {
        Patch(typeof(PropertyManager), nameof(PropertyManager.GetBuyPrice), new[] { typeof(BaseProperty) },
            postfix: nameof(PropertyPricePostfix));
        Patch(typeof(ApartmentManager), nameof(ApartmentManager.GetPrice), new[] { typeof(BaseProperty) },
            postfix: nameof(PropertyPricePostfix));
    });
    static readonly Hook<AwarenessGainContext> awarenessGain = new(nameof(AwarenessGain), () => Patch(
        typeof(CurfewManager), nameof(CurfewManager.IncreaseAwarness), new[] { typeof(float), typeof(bool) },
        prefix: nameof(AwarenessPrefix)));

    static readonly Hook<CatchContext> catchHook = new(nameof(Catch), () => Patch(
        typeof(CurfewManager), nameof(CurfewManager.CatchPlayer), new[] { typeof(bool) },
        prefix: nameof(CatchPrefix)));

    /// <summary>
    /// The player is about to be caught (awareness reached 1). Set <c>Cancel</c> to let them off:
    /// the kit then drops awareness to <c>AwarenessAfterCancel</c> (0.5 by default), so furniture
    /// stays unlocked, and clears every camera's and drone's lock-on, so only a fresh sighting builds
    /// awareness up again before the next catch. Set it
    /// close to 1 and a drone overhead re-triggers the catch within a few steps. To stop awareness
    /// rising at all, use <see cref="AwarenessGain"/>.
    /// </summary>
    public static event Action<CatchContext> Catch { add => catchHook.Add(value); remove => catchHook.Remove(value); }

    static bool catchCancelled;

    // Read once by the PlayerCaught postfix, so a cancelled catch raises no event.
    internal static bool ConsumeCatchCancelled()
    {
        bool c = catchCancelled;
        catchCancelled = false;
        return c;
    }

    static bool CatchPrefix(CurfewManager __instance, bool byDrone)
    {
        var ctx = catchHook.Run(() => new CatchContext(byDrone));
        if (ctx == null || !ctx.Cancel) return true;
        float after = Math.Clamp(ctx.AwarenessAfterCancel, 0f, 0.99f);   // 1 would mean caught
        try { __instance._awarness = after; } catch { }
        ResetDetectors();
        catchCancelled = true;
        return false;   // skip CatchPlayer: no popup, no security rise, no furniture lock
    }

    // A camera or drone that has watched the player for detectionTime locks on: it adds awareness
    // every frame, seen or not, until the catch. With the catch skipped, clear the lock on all of
    // them so awareness only rises again on a fresh sighting.
    static void ResetDetectors()
    {
        try
        {
            var cameras = SecurityCamera.instances;
            if (cameras == null) return;
            foreach (var camera in cameras)
                if (camera != null) camera.playerVisibilityTime = 0f;
        }
        catch (Exception e) { KitPlugin.L.LogWarning($"Tuning.Catch: could not reset detectors: {e.Message}"); }
    }

    /// <summary>Default awareness the kit sets when a catch is cancelled.</summary>
    public const float DefaultAwarenessAfterCancel = 0.5f;

    /// <summary>How many crops a greenhouse module yields when harvested.</summary>
    public static event Action<CropYieldContext> CropYield { add => cropYield.Add(value); remove => cropYield.Remove(value); }

    /// <summary>A greenhouse module's growth speed (a multiplier; compost and upgrades are already in it).</summary>
    public static event Action<CropGrowthContext> CropGrowthSpeed { add => cropGrowth.Add(value); remove => cropGrowth.Remove(value); }

    /// <summary>How many items a catch gives.</summary>
    public static event Action<FishYieldContext> FishYield { add => fishYield.Add(value); remove => fishYield.Remove(value); }

    /// <summary>The purchase price of a property (venue, apartment, greenhouse), in hundredths.</summary>
    public static event Action<PropertyPriceContext> PropertyPrice { add => propertyPrice.Add(value); remove => propertyPrice.Remove(value); }

    /// <summary>How much security awareness rises (before it's applied). Set to 0 to ignore a rise.</summary>
    public static event Action<AwarenessGainContext> AwarenessGain { add => awarenessGain.Add(value); remove => awarenessGain.Remove(value); }

    // ---------- patches ----------

    static void CropYieldPostfix(GreenhouseAreaGhost __instance, GreenhouseModuleGhost module, ref int __result)
    {
        int game = __result;
        var ctx = cropYield.Run(() => new CropYieldContext(__instance, module, game));
        if (ctx != null) __result = Math.Max(ctx.Yield, 0);
    }

    static void CropGrowthPostfix(GreenhouseAreaGhost __instance, GreenhouseModuleGhost module, ref float __result)
    {
        float game = __result;
        var ctx = cropGrowth.Run(() => new CropGrowthContext(__instance, module, game));
        if (ctx != null) __result = Math.Max(ctx.Speed, 0f);
    }

    static void FishYieldPostfix(ItemType itemType, float size, ref int __result)
    {
        int game = __result;
        var ctx = fishYield.Run(() => new FishYieldContext(itemType, size, game));
        if (ctx != null) __result = Math.Max(ctx.Yield, 0);
    }

    static void PropertyPricePostfix(BaseProperty apartment, ref int __result)
    {
        int game = __result;
        var ctx = propertyPrice.Run(() => new PropertyPriceContext(apartment, game));
        if (ctx != null) __result = Math.Max(ctx.Price, 0);
    }

    static void AwarenessPrefix(ref float amount, bool byDrone)
    {
        float game = amount;
        var ctx = awarenessGain.Run(() => new AwarenessGainContext(game, byDrone));
        if (ctx != null) amount = Math.Max(ctx.Amount, 0f);
    }

    // ---------- item use order ----------

    static readonly Hook<UseOrderContext> useOrder = new(nameof(UseOrder), () =>
    {
        Patch(typeof(ItemStack), "PopItemsInto", new[] { typeof(int), typeof(Il2CppSystem.Collections.Generic.List<ItemInstanceData>) },
            prefix: nameof(PopIntoPrefix));
        Patch(typeof(ItemStack), "PopItems", new[] { typeof(int) }, prefix: nameof(PopPrefix));
    });

    /// <summary>
    /// Items are about to be taken from a stack (cooking, selling, moving, delivering). Call
    /// <c>Sort</c> to choose which go first. Since game patch 2 the game itself sorts stacks used for
    /// cooking by remaining freshness, least first, which puts spoiled items first; this runs after
    /// that, so your order wins.
    /// </summary>
    /// <example><code>
    /// // Never take spoiled items while fresh ones remain; otherwise least time left first.
    /// Tuning.UseOrder += ctx => ctx.Sort((a, b) =>
    /// {
    ///     bool sa = a.remainingDecayTime &lt;= 0, sb = b.remainingDecayTime &lt;= 0;
    ///     if (sa != sb) return sa ? 1 : -1;
    ///     return a.remainingDecayTime.CompareTo(b.remainingDecayTime);
    /// });
    /// </code></example>
    public static event Action<UseOrderContext> UseOrder { add => useOrder.Add(value); remove => useOrder.Remove(value); }

    static void PopIntoPrefix(ItemStack __instance, int count) => RunUseOrder(__instance, count);
    static void PopPrefix(ItemStack __instance, int count) => RunUseOrder(__instance, count);

    static void RunUseOrder(ItemStack stack, int count)
    {
        try
        {
            var list = stack?._instanceData;
            if (list == null || list.Count < 2 || count <= 0) return;
            var ctx = useOrder.Run(() => new UseOrderContext(stack, count));
            ctx?.Apply(list);
        }
        catch (Exception e) { KitPlugin.L.LogError($"Tuning.UseOrder: {e.Message}"); }
    }

    // ---------- plumbing ----------

    static Harmony harmony;

    static void Patch(Type type, string method, Type[] args, string prefix = null, string postfix = null)
    {
        var target = AccessTools.Method(type, method, args) ?? throw new Exception($"{type.Name}.{method} not found");
        harmony ??= new Harmony(ModKit.Guid + ".tuning");
        harmony.Patch(target,
            prefix: prefix == null ? null : new HarmonyMethod(typeof(Tuning), prefix),
            postfix: postfix == null ? null : new HarmonyMethod(typeof(Tuning), postfix));
    }

    // One tunable value: its handlers, and the patch installed on first subscription.
    sealed class Hook<TCtx> where TCtx : class
    {
        readonly string name;
        readonly Action install;
        Action<TCtx> handlers;
        bool attempted;

        public Hook(string name, Action install)
        {
            this.name = name;
            this.install = install;
        }

        public void Add(Action<TCtx> handler)
        {
            handlers += handler;
            string who = handler?.Method.DeclaringType?.Assembly.GetName().Name ?? "?";
            KitPlugin.L.LogInfo($"Tuning.{name}: handler added by {who}");
            if (attempted) return;
            attempted = true;
            try
            {
                install();
                KitPlugin.L.LogInfo($"Tuning.{name}: live");
            }
            catch (Exception e) { KitPlugin.L.LogError($"Tuning.{name}: missing ({e.Message})"); }
        }

        public void Remove(Action<TCtx> handler) => handlers -= handler;

        // Null when nobody's subscribed or the context couldn't be built: the game's value stands.
        public TCtx Run(Func<TCtx> make)
        {
            var h = handlers;
            if (h == null) return null;
            TCtx ctx;
            try { ctx = make(); } catch { return null; }
            long t = Perf.Start();
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<TCtx>)d)(ctx); }
                catch (Exception e) { GameEvents.LogFailure($"Tuning.{name}", d, e); }
            }
            Perf.Stop("Tuning." + name, t);
            return ctx;
        }
    }
}

/// <summary>Arguments for <see cref="Tuning.CropYield"/>.</summary>
public sealed class CropYieldContext
{
    /// <summary>The greenhouse.</summary>
    public GreenhouseAreaGhost Greenhouse { get; }
    /// <summary>The module being harvested.</summary>
    public GreenhouseModuleGhost Module { get; }
    /// <summary>The plant.</summary>
    public ItemType Plant { get; }
    /// <summary>The game's yield.</summary>
    public int GameYield { get; }
    /// <summary>The yield. Negative values become 0.</summary>
    public int Yield { get; set; }

    internal CropYieldContext(GreenhouseAreaGhost greenhouse, GreenhouseModuleGhost module, int yield)
    {
        Greenhouse = greenhouse;
        Module = module;
        try { Plant = module?.PlantType; } catch { }
        GameYield = Yield = yield;
    }
}

/// <summary>Arguments for <see cref="Tuning.CropGrowthSpeed"/>.</summary>
public sealed class CropGrowthContext
{
    /// <summary>The greenhouse.</summary>
    public GreenhouseAreaGhost Greenhouse { get; }
    /// <summary>The module.</summary>
    public GreenhouseModuleGhost Module { get; }
    /// <summary>The plant.</summary>
    public ItemType Plant { get; }
    /// <summary>The game's growth speed.</summary>
    public float GameSpeed { get; }
    /// <summary>The growth speed. Negative values become 0.</summary>
    public float Speed { get; set; }

    internal CropGrowthContext(GreenhouseAreaGhost greenhouse, GreenhouseModuleGhost module, float speed)
    {
        Greenhouse = greenhouse;
        Module = module;
        try { Plant = module?.PlantType; } catch { }
        GameSpeed = Speed = speed;
    }
}

/// <summary>Arguments for <see cref="Tuning.FishYield"/>.</summary>
public sealed class FishYieldContext
{
    /// <summary>The fish (or other catch).</summary>
    public ItemType Item { get; }
    /// <summary>The catch's size.</summary>
    public float Size { get; }
    /// <summary>The game's yield.</summary>
    public int GameYield { get; }
    /// <summary>How many items the catch gives. Negative values become 0.</summary>
    public int Yield { get; set; }

    internal FishYieldContext(ItemType item, float size, int yield)
    {
        Item = item;
        Size = size;
        GameYield = Yield = yield;
    }
}

/// <summary>Arguments for <see cref="Tuning.PropertyPrice"/>.</summary>
public sealed class PropertyPriceContext
{
    /// <summary>The property.</summary>
    public BaseProperty Property { get; }
    /// <summary>The game's price, in hundredths.</summary>
    public int GamePrice { get; }
    /// <summary>The price, in hundredths. Negative values become 0.</summary>
    public int Price { get; set; }

    internal PropertyPriceContext(BaseProperty property, int price)
    {
        Property = property;
        GamePrice = Price = price;
    }
}

/// <summary>Arguments for <see cref="Tuning.AwarenessGain"/>.</summary>
public sealed class AwarenessGainContext
{
    /// <summary>The game's awareness increase.</summary>
    public float GameAmount { get; }
    /// <summary>True if a drone saw the player (otherwise a camera or theft).</summary>
    public bool ByDrone { get; }
    /// <summary>The increase to apply. Negative values become 0.</summary>
    public float Amount { get; set; }

    internal AwarenessGainContext(float amount, bool byDrone)
    {
        GameAmount = Amount = amount;
        ByDrone = byDrone;
    }
}

/// <summary>Arguments for <see cref="Tuning.Catch"/>.</summary>
public sealed class CatchContext
{
    /// <summary>True if a drone is catching the player, false for a camera (or theft).</summary>
    public bool ByDrone { get; }

    /// <summary>Set to true to cancel the catch.</summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Awareness (0 to 0.99) to set when the catch is cancelled. Lower gives the player longer
    /// before the next catch. Default <see cref="Tuning.DefaultAwarenessAfterCancel"/>.
    /// </summary>
    public float AwarenessAfterCancel { get; set; } = Tuning.DefaultAwarenessAfterCancel;

    internal CatchContext(bool byDrone) => ByDrone = byDrone;
}

/// <summary>Arguments for <see cref="Tuning.UseOrder"/>.</summary>
public sealed class UseOrderContext
{
    readonly ItemStack stack;
    System.Collections.Generic.List<ItemInstanceData> order;   // use-first first; null = unchanged

    /// <summary>The item type in the stack.</summary>
    public ItemType Item { get; }

    /// <summary>How many items are being taken.</summary>
    public int Count { get; }

    /// <summary>The stack's items in the order they'll be used (first = used first).</summary>
    public System.Collections.Generic.IReadOnlyList<ItemInstanceData> Items
    {
        get
        {
            if (order != null) return order;
            var list = new System.Collections.Generic.List<ItemInstanceData>();
            var src = stack._instanceData;
            for (int i = src.Count - 1; i >= 0; i--) list.Add(src[i]);   // the game takes from the end
            return list;
        }
    }

    /// <summary>
    /// Orders the stack so items that compare lower are used first. Later handlers see this order and can
    /// sort again. Stable: items that compare equal keep their current order.
    /// </summary>
    public void Sort(Comparison<ItemInstanceData> useFirst)
    {
        if (useFirst == null) return;
        var items = new System.Collections.Generic.List<ItemInstanceData>(Items);
        var index = new System.Collections.Generic.Dictionary<ItemInstanceData, int>();
        for (int i = 0; i < items.Count; i++) index[items[i]] = i;
        items.Sort((a, b) =>
        {
            int c = useFirst(a, b);
            return c != 0 ? c : index[a].CompareTo(index[b]);   // stable
        });
        order = items;
    }

    internal UseOrderContext(ItemStack stack, int count)
    {
        this.stack = stack;
        Count = count;
        try { Item = stack._type; } catch { }
    }

    // Writes the chosen order back: use-first items at the end, where the game takes from.
    internal void Apply(Il2CppSystem.Collections.Generic.List<ItemInstanceData> list)
    {
        if (order == null || order.Count != list.Count) return;
        list.Clear();
        for (int i = order.Count - 1; i >= 0; i--) list.Add(order[i]);
    }
}
