using System.ComponentModel;

namespace Ck3MapGen.Config;

public enum SettingRole
{
    /// <summary>Applies however the terrain was obtained, including an imported heightmap.</summary>
    Always,

    /// <summary>Only consumed while generating terrain; inert when a heightmap is imported.</summary>
    GenerationOnly,
}

/// <summary>
/// Marks whether a setting still does anything once terrain comes from a file.
///
/// The GUI filters the property grid on this when a heightmap is loaded. The split is per-property
/// rather than per-category because the obvious category-level answer is wrong: importing a
/// heightmap leaves the whole of Provinces live, leaves Rivers and lakes live (they are re-derived
/// from the imported field by the same drainage code), and leaves the parts of Coast and Height
/// scale that the 16-bit write and its inverse read. Hiding those would take away exactly the
/// knobs worth touching on an imported map.
///
/// Implemented so <see cref="PropertyGrid.BrowsableAttributes"/> can do the filtering natively —
/// hence the <see cref="Match"/> override, which is what AttributeCollection uses.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingRoleAttribute(SettingRole role) : Attribute
{
    public SettingRole Role { get; } = role;

    public override object TypeId => typeof(SettingRoleAttribute);

    public override bool Match(object? obj) => obj is SettingRoleAttribute other && other.Role == Role;

    public override bool Equals(object? obj) => obj is SettingRoleAttribute other && other.Role == Role;

    public override int GetHashCode() => (int)Role;
}
