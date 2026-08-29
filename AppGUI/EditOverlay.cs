using System.Text.Json;
using System.Text.Json.Serialization;
using Ck3MapGen.Emit;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Every edit made to a world, as values keyed by what they were made to, so they can be laid
/// back over the next world generated — the thing that lets a user tune twenty cultures, nudge a
/// slider, and not start again.
///
/// Only <em>touched</em> fields are recorded. A whole-object snapshot would, on re-application, put
/// back every editable value including the ones the user never looked at, and a regenerate that
/// legitimately changed a culture's traditions would have them silently overwritten with the old
/// ones. Absent means untouched, everywhere here.
///
/// Matching is by key <em>and</em> generated name. Title keys carry the generated name already
/// (<c>k_gen_ryalos_12</c>), but culture, faith and ruler keys are plain indices
/// (<c>gen_culture_3</c>, <c>gen_char_117</c>) that exist in every world, so on a different seed
/// they would land on strangers. Requiring the fresh object to have been generated with the same
/// name makes re-application exact when the world is the same and inert when it is not — and a
/// setting that does not disturb naming still carries everything across.
///
/// Saved as <c>proctool_edits.json</c> in the mod folder, so a mod written again tomorrow under the
/// same name gets its edits back too. The heightmap identity is kept so a file from another world
/// is never applied.
/// </summary>
public sealed class EditOverlay
{
    public const string FileName = "proctool_edits.json";

    /// <summary>Which heightmap the edits were made against — a provider's <c>Detail</c>.</summary>
    public string? Heightmap { get; set; }

    public Dictionary<string, TitleEdit> Titles { get; set; } = [];
    public Dictionary<string, CultureEdit> Cultures { get; set; } = [];
    public Dictionary<string, FaithEdit> Faiths { get; set; } = [];
    public Dictionary<string, ReligionEdit> Religions { get; set; } = [];

    /// <summary>Keyed by the character's history id, which is the seat county's index.</summary>
    public Dictionary<string, RulerEdit> Rulers { get; set; } = [];

    [JsonIgnore]
    public int Count => Titles.Count + Cultures.Count + Faiths.Count + Religions.Count + Rulers.Count;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>The overlay at <paramref name="path"/>, or null if there is none or it is unreadable.</summary>
    public static EditOverlay? Load(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<EditOverlay>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A title's touched fields. The key already names the generated title, so no guard.</summary>
public sealed class TitleEdit
{
    public string? Name { get; set; }
    public int[]? Color { get; set; }

    /// <summary>Present when any of the three was touched; they are edited as one.</summary>
    public TitleWords? Words { get; set; }
}

public sealed record TitleWords(string? Form, string? Holder, string? HolderFemale);

public sealed class CultureEdit
{
    /// <summary>The name the culture was generated with — the guard against a stranger's key.</summary>
    public required string Generated { get; set; }

    public string? Name { get; set; }
    public int[]? Color { get; set; }
    public string? Ethos { get; set; }
    public string? MartialCustom { get; set; }
    public string? HeadDetermination { get; set; }
    public List<string>? Traditions { get; set; }
    public string? CoaGfx { get; set; }
    public string? BuildingGfx { get; set; }
    public string? ClothingGfx { get; set; }
    public string? UnitGfx { get; set; }
    public Dictionary<string, TitleVocabulary>? RealmWords { get; set; }

    /// <summary>
    /// The vanilla ethnicity this culture was moved onto, if it was — the choice, not the genes it
    /// produced. Replaying the choice redraws the hair and eye variants, which is what makes the
    /// edit portable; the genes themselves come off an Rng and could not be replayed.
    /// </summary>
    public string? Ethnicity { get; set; }
}

public sealed class FaithEdit
{
    public required string Generated { get; set; }

    public string? Name { get; set; }
    public double[]? Color { get; set; }
    public string? Icon { get; set; }
    public List<string>? Tenets { get; set; }
}

public sealed class ReligionEdit
{
    public required string Generated { get; set; }
    public string? Name { get; set; }
    public List<string>? Virtues { get; set; }
    public List<string>? Sins { get; set; }
}

public sealed class RulerEdit
{
    public required string Generated { get; set; }

    public string? Name { get; set; }
    public bool? Female { get; set; }
    public int? BirthYear { get; set; }

    /// <summary>The whole profile when any of it was touched: the inspector edits it by <c>with</c>.</summary>
    public RulerProfile? Profile { get; set; }

    public int? Gold { get; set; }
    public int? Prestige { get; set; }
    public int? Renown { get; set; }
}
