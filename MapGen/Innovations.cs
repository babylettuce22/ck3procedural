using Ck3MapGen.Config;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One innovation this generator invented, as opposed to one it harvested off vanilla.
///
/// Deliberately a plain record of what CK3's innovation format accepts rather than anything about
/// war, because innovations are the engine's general answer to "a culture can do a thing others
/// cannot, and can come to do it later". Men-at-arms are only the first caller — a generated
/// building, decision or law wants exactly this object with a different field filled in, and the
/// alternative is each of those systems growing its own half-innovation emitter.
///
/// Everything here is optional except the identity and the era. An innovation with nothing
/// unlocked and no modifier is legal and simply reads as a piece of the culture's history.
/// </summary>
public sealed class Innovation
{
    /// <summary>Frozen. Also the localisation key, and the key <c>discover_innovation</c> names.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }
    public required string Description { get; set; }

    /// <summary>A <c>culture_era_*</c> key. Never later than the world's own era for anything a
    /// culture is meant to hold at the start date — see <see cref="Innovations.EraAt"/>.</summary>
    public required string Era { get; set; }

    /// <summary><c>culture_group_military</c>, <c>_civic</c> or <c>_regional</c>.</summary>
    public string Group { get; set; } = "culture_group_military";

    /// <summary>Which skill of the culture head speeds it along.</summary>
    public string Skill { get; set; } = "martial";

    /// <summary>A gfx path borrowed from vanilla, or null to let CK3 fall back on its default.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Culture-scope trigger lines deciding who may ever have this, written verbatim into
    /// <c>potential</c>. Empty means everyone, which is what a world-wide innovation wants.
    ///
    /// Lines rather than a structured trigger because the useful ones here are one-liners —
    /// <c>has_cultural_pillar = x</c>, <c>this = culture:y</c> — and a trigger tree would be a
    /// second script builder living inside a data class.
    /// </summary>
    public List<string> Potential { get; } = [];

    public List<string> UnlockMenAtArms { get; } = [];
    public List<string> UnlockBuildings { get; } = [];
    public List<string> UnlockDecisions { get; } = [];

    public Dictionary<string, string> Parameters { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CharacterModifier { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CultureModifier { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CountyModifier { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// <c>global_regional</c> by default, which is what vanilla flags an innovation that only some
    /// cultures can ever reach. The flag decides how <c>has_all_innovations</c> counts it, so a
    /// culture-locked innovation flagged <c>global_regular</c> would make every other culture's
    /// era permanently incomplete.
    /// </summary>
    public List<string> Flags { get; } = ["global_regional"];

    /// <summary>Cultures that already have this worked out on the start date.</summary>
    public List<Culture> KnownAtStart { get; } = [];
}

/// <summary>
/// Every innovation the run invented, and the lookup <see cref="Emit.CultureWriter"/> reads when it
/// writes a culture's history.
/// </summary>
public sealed class InnovationMap
{
    private readonly Dictionary<Culture, List<Innovation>> _byCulture = [];

    public List<Innovation> All { get; } = [];

    public Innovation Add(Innovation innovation)
    {
        All.Add(innovation);
        return innovation;
    }

    /// <summary>
    /// Records that a culture starts the game already holding an innovation. Called by whichever
    /// system invented it, because only that system knows whether the culture has earned it.
    /// </summary>
    public void GrantAtStart(Innovation innovation, Culture culture)
    {
        if (innovation.KnownAtStart.Contains(culture)) return;

        innovation.KnownAtStart.Add(culture);
        _byCulture.TryAdd(culture, []);
        _byCulture[culture].Add(innovation);
    }

    /// <summary>What a culture has already discovered when the game opens, in key order.</summary>
    public IReadOnlyList<Innovation> StartingFor(Culture culture)
        => _byCulture.TryGetValue(culture, out var list)
            ? [.. list.OrderBy(i => i.Key, StringComparer.Ordinal)]
            : [];
}

/// <summary>
/// The era ladder, and the one fact about it every generated innovation has to respect: a culture
/// cannot discover something from an era it has not reached.
/// </summary>
public static class Innovations
{
    /// <summary>
    /// CK3's four culture eras with the years they open on, oldest first.
    ///
    /// The years are vanilla's timeline, not the world's calendar. Everything asked of this table
    /// is a question about how advanced the world is, and <see cref="MapConfig.EraYear"/> exists
    /// precisely so that question survives a world that calls the year something else — see
    /// the calendar split in <see cref="MapConfig"/>.
    /// </summary>
    public static readonly (string Key, int StartYear)[] Eras =
    [
        ("culture_era_tribal", 0),
        ("culture_era_early_medieval", 900),
        ("culture_era_high_medieval", 1050),
        ("culture_era_late_medieval", 1200),
    ];

    /// <summary>The index into <see cref="Eras"/> the world has reached.</summary>
    public static int EraIndexAt(int eraYear)
    {
        int index = 0;
        for (int i = 0; i < Eras.Length; i++)
            if (eraYear >= Eras[i].StartYear) index = i;
        return index;
    }

    /// <summary>The era key the world has reached.</summary>
    public static string EraAt(int eraYear) => Eras[EraIndexAt(eraYear)].Key;

    /// <summary>Where an era key sits on the ladder, or 0 for one this build does not know.</summary>
    public static int IndexOf(string era)
    {
        for (int i = 0; i < Eras.Length; i++)
            if (Eras[i].Key == era) return i;
        return 0;
    }
}
