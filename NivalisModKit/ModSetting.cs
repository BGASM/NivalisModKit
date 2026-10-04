using BepInEx.Configuration;

namespace NivalisModKit;

/// <summary>
/// How a setting appears in the in-game mod browser. Pass one as a tag when you bind the setting:
/// <code>
/// Config.Bind("General", "Verbose", false,
///     new ConfigDescription("Log every decision.", null, new ModSetting { IsAdvanced = true }));
/// </code>
/// Listing is opt-in: a setting appears when it has this tag, or when its mod called
/// <see cref="ModMenu.ListSettings"/> (then all of its settings appear, and these flags adjust them). The browser
/// also reads BepInEx ConfigurationManager's <c>ConfigurationManagerAttributes</c> tag to adjust listed settings,
/// but that tag alone doesn't list a setting.
/// </summary>
public sealed class ModSetting
{
    /// <summary>False hides the setting from the browser entirely.</summary>
    public bool? Browsable { get; set; }

    /// <summary>True shows the value but doesn't let the player change it.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>True shows it only when the player ticks "Show advanced".</summary>
    public bool? IsAdvanced { get; set; }

    /// <summary>
    /// True when a change only takes effect after restarting the game (the mod reads the value once). The browser
    /// says so next to it.
    /// </summary>
    public bool? RequiresRestart { get; set; }

    /// <summary>Position within its section; higher comes first. Settings without one keep file order.</summary>
    public int? Order { get; set; }

    /// <summary>Name to show instead of the key.</summary>
    public string DisplayName { get; set; }

    // True when the entry carried a ModSetting tag (its mod opted this setting in).
    internal bool Tagged { get; private set; }

    // The flags in effect for an entry, from a ModSetting or a ConfigurationManagerAttributes tag.
    internal static ModSetting Of(ConfigEntryBase entry)
    {
        var result = new ModSetting();
        var tags = entry?.Description?.Tags;
        if (tags == null) return result;
        foreach (var tag in tags)
        {
            if (tag is ModSetting m) { Merge(result, m); result.Tagged = true; continue; }
            if (tag == null || tag.GetType().Name != "ConfigurationManagerAttributes") continue;
            // ConfigurationManager's tag: a class each mod copies into itself, so read it by member name.
            Merge(result, new ModSetting
            {
                Browsable = Read<bool?>(tag, "Browsable"),
                ReadOnly = Read<bool?>(tag, "ReadOnly"),
                IsAdvanced = Read<bool?>(tag, "IsAdvanced"),
                Order = Read<int?>(tag, "Order"),
                DisplayName = Read<string>(tag, "DispName"),
            });
        }
        return result;
    }

    static void Merge(ModSetting into, ModSetting from)
    {
        into.Browsable = from.Browsable ?? into.Browsable;
        into.ReadOnly = from.ReadOnly ?? into.ReadOnly;
        into.IsAdvanced = from.IsAdvanced ?? into.IsAdvanced;
        into.RequiresRestart = from.RequiresRestart ?? into.RequiresRestart;
        into.Order = from.Order ?? into.Order;
        into.DisplayName = from.DisplayName ?? into.DisplayName;
    }

    static T Read<T>(object o, string name)
    {
        try
        {
            var t = o.GetType();
            object v = t.GetField(name)?.GetValue(o) ?? t.GetProperty(name)?.GetValue(o);
            return v is T typed ? typed : default;
        }
        catch { return default; }
    }
}
