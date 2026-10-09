using System;

namespace NivalisModKit;

/// <summary>
/// The standard BepInEx settings tag, read by settings menus such as Mod Settings Menu and Configuration
/// Manager as well as the kit's own Mods browser. Pass one as a tag when you bind a setting:
/// <code>
/// Config.Bind("Debug", "Verbose", false,
///     new ConfigDescription("Log every decision.", null, new ConfigurationManagerAttributes { IsAdvanced = true }));
/// </code>
/// Menus find it by its class name and read its fields, so this is the same tag mods usually copy into
/// themselves; with the kit you don't need a copy. In the kit's browser it also opts the setting in, like
/// <see cref="ModSetting"/>. New in 0.6.2.
/// </summary>
public sealed class ConfigurationManagerAttributes
{
    /// <summary>True hides the setting unless the player asks for advanced settings.</summary>
    public bool? IsAdvanced;

    /// <summary>False hides the setting entirely.</summary>
    public bool? Browsable;

    /// <summary>True shows the value but doesn't let the player change it.</summary>
    public bool? ReadOnly;

    /// <summary>Position within its section; higher comes first.</summary>
    public int? Order;

    /// <summary>Name to show instead of the key.</summary>
    public string DispName;

    /// <summary>Category to show it under, instead of its section.</summary>
    public string Category;

    /// <summary>Description to show instead of the config description.</summary>
    public string Description;

    /// <summary>Default value to show, when it differs from the bound default.</summary>
    public object DefaultValue;

    /// <summary>True hides the "reset to default" button.</summary>
    public bool? HideDefaultButton;

    /// <summary>True hides the setting's name (for custom drawers in other menus).</summary>
    public bool? HideSettingName;

    /// <summary>True shows a 0 to 1 range as a percentage.</summary>
    public bool? ShowRangeAsPercent;

    /// <summary>Converts the value to the text shown.</summary>
    public Func<object, string> ObjToStr;

    /// <summary>Converts entered text back to a value.</summary>
    public Func<string, object> StrToObj;

    /// <summary>
    /// Kit extension: true when a change only takes effect after restarting the game. The kit's browser says so
    /// next to the setting; other menus ignore it.
    /// </summary>
    public bool? RequiresRestart;
}
