using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Base converter that pulls dynamic dropdown items from the active <see cref="VanillaVocabulary"/>.
/// </summary>
public abstract class DynamicVocabularyConverter(Func<VanillaVocabulary, IEnumerable<string>> selector)
    : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    // Returning false makes it an editable dropdown (user can pick from list OR type custom keys)
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var vocab = VanillaVocabulary.Current;
        if (vocab is null) return new StandardValuesCollection(Array.Empty<string>());

        var values = selector(vocab)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return new StandardValuesCollection(values);
    }
}

// Visuals
public sealed class ClothingGfxConverter()
    : DynamicVocabularyConverter(v => v.Looks.Select(l => l.ClothingGfx));

public sealed class UnitGfxConverter()
    : DynamicVocabularyConverter(v => v.Looks.Select(l => l.UnitGfx));

public sealed class BuildingGfxConverter()
    : DynamicVocabularyConverter(v => v.Looks.Select(l => l.BuildingGfx));

public sealed class CoaGfxConverter()
    : DynamicVocabularyConverter(v => v.Looks.Select(l => l.CoaGfx));

// Pillars
public sealed class EthosConverter()
    : DynamicVocabularyConverter(v => v.Ethos);

public sealed class MartialCustomConverter()
    : DynamicVocabularyConverter(v => v.MartialCustoms);

public sealed class HeadDeterminationConverter()
    : DynamicVocabularyConverter(v => v.HeadDeterminations);

// Look Presets (e.g. "norse", "french", "byzantine")
public sealed class LookPresetConverter()
    : DynamicVocabularyConverter(v => v.Looks.Select(l => l.SourceCulture));

// Faith
public sealed class FaithIconConverter()
    : DynamicVocabularyConverter(v => v.FaithIcons);

/// <summary>
/// The vanilla ethnicities a human culture can be moved onto.
///
/// Not a <see cref="DynamicVocabularyConverter"/>, and exclusive rather than editable, which are
/// the same decision twice: a template CK3 does not recognise is not an error but a silent
/// fall-through to the look the culture already had, so a typed key that misses would look exactly
/// like a pick that did nothing. The list is the generator's own
/// <see cref="Ethnicities.HumanTemplates"/>, kept in family order rather than alphabetised so the
/// related looks sit together in the dropdown.
/// </summary>
public sealed class EthnicityTemplateConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        => new(Ethnicities.HumanTemplates.ToList());
}