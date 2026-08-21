using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

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