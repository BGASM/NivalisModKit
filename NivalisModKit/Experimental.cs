using System;

namespace NivalisModKit;

/// <summary>
/// Marks kit API that may change or be removed in a minor release, unlike the rest of the kit (see the
/// README's Versioning section). Use it, but expect to update your mod when the kit updates.
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class ExperimentalAttribute : Attribute
{
    /// <summary>What may change, or what will replace it.</summary>
    public string Note { get; }

    /// <param name="note">What may change, or what will replace it.</param>
    public ExperimentalAttribute(string note = null) => Note = note;
}
