using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NivalisModKit;

/// <summary>
/// Named commands that development tools can run in the game through the dev bridge
/// (<c>POST http://127.0.0.1:5710/cmd/name?arg=value</c>, or <c>tools/bridge.sh cmd name arg=value</c>).
/// For test mods and debugging: open a window, set up a situation, read back a result. Commands run on the main
/// thread, so they can use the game and the kit freely. Running them needs the kit's <c>[DevBridge] Enabled</c> and
/// <c>AllowCommands</c>; registering always works and costs nothing.
/// </summary>
/// <example><code>
/// DevCommands.Register(MyGuid, "give-money", "amount=N: add money (hundredths)", args =>
/// {
///     int amount = args.GetInt("amount", 10000);
///     AddMoney(amount);
///     return new { added = amount, now = Economy.PlayerMoney };   // sent back as JSON
/// });
/// </code></example>
public static class DevCommands
{
    /// <summary>A registered command.</summary>
    public sealed class Command
    {
        /// <summary>The name used in <c>/cmd/name</c>.</summary>
        public string Name { get; internal set; }
        /// <summary>The mod that registered it (its GUID).</summary>
        public string Owner { get; internal set; }
        /// <summary>One line: its arguments and what it does.</summary>
        public string Help { get; internal set; }
        internal Func<CommandArgs, object> Run;
    }

    static readonly Dictionary<string, Command> commands = new(StringComparer.OrdinalIgnoreCase);
    static readonly Regex ValidName = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.IgnoreCase);

    /// <summary>All registered commands, by name.</summary>
    public static IReadOnlyList<Command> All => commands.Values.OrderBy(c => c.Name).ToList();

    /// <summary>True if commands can be run now (bridge on and <c>[DevBridge] AllowCommands</c> set).</summary>
    public static bool Enabled => Bridge.Running && KitPlugin.BridgeCommands?.Value == true;

    /// <summary>
    /// Registers a command. <paramref name="run"/> gets the request's arguments and returns the reply (any object;
    /// sent as JSON) or null for <c>"ok"</c>. Names are letters, digits, '.', '-' and '_'; a name another mod already
    /// registered is refused (returns false, and the log names both mods).
    /// </summary>
    public static bool Register(string owner, string name, string help, Func<CommandArgs, object> run)
    {
        if (string.IsNullOrEmpty(owner) || run == null || name == null || !ValidName.IsMatch(name))
        {
            KitPlugin.L.LogWarning($"DevCommands: refused '{name}' from {owner}: names are letters, digits, '.', '-', '_'");
            return false;
        }
        if (commands.TryGetValue(name, out var existing) && existing.Owner != owner)
        {
            KitPlugin.L.LogWarning($"DevCommands: '{name}' from {owner} refused, already registered by {existing.Owner}");
            return false;
        }
        commands[name] = new Command { Name = name, Owner = owner, Help = help ?? "", Run = run };
        return true;
    }

    /// <inheritdoc cref="Register(string, string, string, Func{CommandArgs, object})"/>
    public static bool Register(string owner, string name, string help, Action<CommandArgs> run) =>
        run != null && Register(owner, name, help, a => { run(a); return null; });

    /// <summary>Removes your command.</summary>
    public static void Unregister(string owner, string name)
    {
        if (name != null && commands.TryGetValue(name, out var c) && c.Owner == owner) commands.Remove(name);
    }

    // Main thread (the bridge queues it there).
    internal static object Run(string name, Dictionary<string, string> query)
    {
        if (!commands.TryGetValue(name ?? "", out var c))
            throw new KeyNotFoundException($"no command '{name}' (GET /cmd lists them)");
        KitPlugin.L.LogInfo($"DevCommands: {c.Name} {string.Join(" ", query.Select(kv => $"{kv.Key}={kv.Value}"))}".TrimEnd());
        var reply = c.Run(new CommandArgs(query));
        return reply ?? "ok";
    }

    // ---------- the kit's own commands ----------

    static IDisposable devPause;

    static Nivalis.InventorySystem.PlayerInventory Inventory() =>
        Nivalis.Singleton<Nivalis.PlayerManager>.InstanceExist(out var pm) ? pm.LocalPlayer?.Inventory : null;

    internal static void RegisterBuiltIns()
    {
        const string kit = ModKit.Guid;
        Register(kit, "help", "List the commands", _ =>
            All.Select(c => new { name = c.Name, help = c.Help, owner = c.Owner }).ToArray());

        Register(kit, "notify", "text=... [header=...]: a notification in the game's feed", a =>
            new { shown = Ui.Notify(a.Get("header", "Dev bridge"), a.Get("text", "Hello")) });

        Register(kit, "open", "what=Map | Venue | <menu tab>: open a game screen (Ui.OpenMap / OpenVenue / OpenMenu)", a =>
        {
            string what = a.Get("what", "");
            bool ok = what.Equals("Map", StringComparison.OrdinalIgnoreCase) ? Ui.OpenMap()
                : what.Equals("Venue", StringComparison.OrdinalIgnoreCase) ? Ui.OpenVenue(Venues.PlayerOwned.FirstOrDefault())
                : Enum.TryParse<Nivalis.UI.InGameMenu.InGameMenuTab>(what, true, out var tab) && Ui.OpenMenu(tab);
            if (!ok) throw new ArgumentException($"couldn't open '{what}'. Tabs: {string.Join(", ", Enum.GetNames(typeof(Nivalis.UI.InGameMenu.InGameMenuTab)))}");
            return new { opened = what };
        });

        Register(kit, "money", "amount=N: add money in hundredths (negative takes it away; 10000 = 100.00)", a =>
        {
            if (!a.Has("amount")) throw new ArgumentException("amount=N needed (hundredths: 10000 = 100.00)");
            var inv = Inventory() ?? throw new InvalidOperationException("no player (load a save first)");
            int before = inv.Money;
            inv.ChangeMoneyWithoutReceipt(a.GetInt("amount"));   // the game's own call; raises its money event
            return new { before, now = inv.Money };
        });

        Register(kit, "give", "item=Name [amount=N]: put items in the player's inventory", a =>
        {
            var item = Items.ByName(a.Get("item", "")) ?? throw new ArgumentException($"no item '{a.Get("item", "")}' (bridge /items lists names)");
            var inv = Inventory() ?? throw new InvalidOperationException("no player (load a save first)");
            int amount = Math.Max(1, a.GetInt("amount", 1));
            inv.AddItem(item, amount);
            return new { gave = Items.NameOf(item), amount };
        });

        ConfigCommand.Register();

        Register(kit, "mods", "Open the Mods browser", _ => { ModMenu.Open(); return new { opened = ModMenu.BrowserOwner }; });

        Register(kit, "clock", "[speed=X] [sim=X] [pause=on|off]: clock and simulation speed (1 clears), shared pause", a =>
        {
            const string owner = "devbridge";
            if (a.Has("speed")) { float v = a.GetFloat("speed", 1f); if (v == 1f) GameClock.ClearClockSpeed(owner); else GameClock.SetClockSpeed(owner, v); }
            if (a.Has("sim")) { float v = a.GetFloat("sim", 1f); if (v == 1f) GameClock.ClearSimulationSpeed(owner); else GameClock.SetSimulationSpeed(owner, v); }
            if (a.Has("pause"))
            {
                if (a.GetBool("pause", false)) devPause ??= GameClock.Pause(owner);
                else { devPause?.Dispose(); devPause = null; }
            }
            return new { clockSpeed = GameClock.ClockSpeed, simulationSpeed = GameClock.SimulationSpeed, paused = GameClock.IsPaused };
        });
    }
}

/// <summary>A dev command's arguments (<c>?name=value&amp;flag</c>). Names ignore case.</summary>
public sealed class CommandArgs
{
    readonly Dictionary<string, string> values;

    internal CommandArgs(Dictionary<string, string> values) =>
        this.values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

    /// <summary>All arguments.</summary>
    public IReadOnlyDictionary<string, string> Values => values;

    /// <summary>True if the argument was given (even without a value, as in <c>?verbose</c>).</summary>
    public bool Has(string name) => values.ContainsKey(name);

    /// <summary>The argument, or <paramref name="fallback"/>.</summary>
    public string Get(string name, string fallback = null) => values.TryGetValue(name, out var v) ? v : fallback;

    /// <summary>The argument as a whole number, or <paramref name="fallback"/> if missing or not a number.</summary>
    public int GetInt(string name, int fallback = 0) =>
        int.TryParse(Get(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The argument as a number, or <paramref name="fallback"/> if missing or not a number.</summary>
    public float GetFloat(string name, float fallback = 0f) =>
        float.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>
    /// The argument as on/off: true for true, on, yes, 1, or a bare flag (<c>?verbose</c>); false for false, off,
    /// no, 0; otherwise <paramref name="fallback"/>.
    /// </summary>
    public bool GetBool(string name, bool fallback = false)
    {
        if (!values.TryGetValue(name, out var v)) return fallback;
        switch (v.Trim().ToLowerInvariant())
        {
            case "": case "true": case "on": case "yes": case "1": return true;
            case "false": case "off": case "no": case "0": return false;
            default: return fallback;
        }
    }
}
