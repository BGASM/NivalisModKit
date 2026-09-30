namespace NivalisModKit;

/// <summary>Identity of the kit, for <c>[BepInDependency]</c> and version checks.</summary>
public static class ModKit
{
    /// <summary>BepInEx plugin GUID. Use as <c>[BepInDependency(ModKit.Guid)]</c>.</summary>
    public const string Guid = "will.nivalis.modkit";

    /// <summary>Display name.</summary>
    public const string Name = "Nivalis ModKit";

    /// <summary>Kit version. Keep in step with the csproj Version.</summary>
    public const string Version = "0.1.0";
}
