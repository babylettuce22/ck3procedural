using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The world's own calendar: what it calls its era, and its twelve months.
///
/// CK3 counts the days itself and only asks localisation what to call them, so a calendar is two
/// sets of loc keys and nothing else: <c>GAME_DATE_STRING</c> and its short and long forms carry
/// the era after the year (see <see cref="Emit.CompatibilityWriter.WriteCalendarLocalisation"/>),
/// and the months are <c>CW_DATE_January</c>.. <c>CW_DATE_Dec</c>, which the engine ships in
/// <c>clausewitz/localization/cw_date_l_english.yml</c> — not under <c>game/</c>, which is why a
/// search of the game's own localisation never finds them.
///
/// The calendar is one world's, not one culture's: CK3 renders every date the same way whoever is
/// looking. So it speaks the language of the people holding the most counties, the nearest thing a
/// generated world has to a calendar everyone else ended up on.
/// </summary>
public sealed record WorldCalendar(
    string EraName,
    string EraShort,
    IReadOnlyList<string>? Months,
    IReadOnlyList<string>? MonthsShort)
{
    /// <summary>
    /// The calendar a run writes, or null when the world keeps vanilla's months and "AD".
    ///
    /// <list type="bullet">
    /// <item>A world of vanilla peoples keeps vanilla's months: Charlemagne's heirs do not count in
    ///   Talvenmoons. An Azgaar export's era still rides along, as it always has.</item>
    /// <item>With <see cref="MapConfig.CalendarEnabled"/> off, only the export's era, as before.</item>
    /// <item>Otherwise every name is, in order of preference: the one typed on the Calendar tab,
    ///   the export's (for the era — the export naming its own age beats anything invented here),
    ///   or a generated one.</item>
    /// </list>
    ///
    /// Everything is generated first and the typed names laid over it, so typing one month leaves
    /// the other eleven exactly as they were. Its own random stream, seeded off the world seed, so
    /// turning the calendar on or off moves no other name in the world.
    /// </summary>
    public static WorldCalendar? Build(MapConfig cfg, AzgaarImport? azgaar, CultureMap cultures)
    {
        string importedName = azgaar?.EraName.Trim() ?? "";
        string importedShort = azgaar?.EraShort.Trim() ?? "";
        if (importedShort.Length == 0) importedShort = importedName;

        if (!cfg.CalendarEnabled || cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
            return importedShort.Length > 0 ? new WorldCalendar(importedName, importedShort, null, null) : null;

        var language = CalendarLanguage(cultures);
        var rng = new Rng(cfg.Seed ^ 0xCA1E);

        // Generated, or vanilla's English where there is no generated language to draw from — a
        // world whose every culture is vanilla's, which only a typed name can reach here.
        string[] months = language?.MonthNames(rng) ?? [.. EnglishMonths];
        for (int m = 0; m < months.Length; m++)
            if (Typed(cfg.CalendarMonths, m) is { } typed) months[m] = typed;

        var generated = language is not null ? Era(language, rng) : ("", "");
        string typedName = cfg.CalendarEraName.Trim(), typedShort = cfg.CalendarEraShort.Trim();

        var (eraName, eraShort) =
            typedName.Length > 0 || typedShort.Length > 0 ? (typedName, typedShort.Length > 0 ? typedShort : Initials(typedName))
            : importedShort.Length > 0 ? (importedName, importedShort)
            : generated;

        return new WorldCalendar(eraName, eraShort, months, ShortForms(months));

        static string? Typed(string[]? names, int m)
            => names is not null && m < names.Length && names[m]?.Trim() is { Length: > 0 } name ? name : null;
    }

    /// <summary>CK3's own month names, January first.</summary>
    public static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    /// <summary>"Talvek Reckoning" to "TR": the first letter of every word, as real calendars abbreviate.</summary>
    public static string Initials(string name)
        => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => Ascii.Fold(w) is { Length: > 0 } f ? char.ToUpperInvariant(f[0]).ToString() : ""));

    /// <summary>
    /// The tongue of the culture holding the most counties; ties go to the lower key, so the choice
    /// does not wander with dictionary order. Declared cultures only — a vanilla culture has no
    /// generated language to lend.
    /// </summary>
    private static Language? CalendarLanguage(CultureMap cultures)
        => cultures.ByCounty.Values
            .Where(c => !c.Inherited)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Key, StringComparer.Ordinal)
            .Select(g => g.Key.Tongue)
            .FirstOrDefault();

    /// <summary>What an era counts from, and the letter each form abbreviates to.</summary>
    private static readonly (string Word, double Weight)[] EraKinds =
        [("Era", 0.45), ("Reckoning", 0.35), ("Age", 0.20)];

    /// <summary>
    /// "the Talvek Reckoning" and "TR": named for a founder or for a word of the language, and
    /// abbreviated the way Azgaar's exports and real calendars are, one initial per word. A short
    /// form that already means a real calendar — "CE" — is drawn again.
    /// </summary>
    private static (string Name, string Short) Era(Language language, Rng rng)
    {
        double roll = rng.Double();
        string kind = roll < EraKinds[0].Weight ? EraKinds[0].Word
            : roll < EraKinds[0].Weight + EraKinds[1].Weight ? EraKinds[1].Word
            : EraKinds[2].Word;

        string name = "", abbreviation = "";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            name = rng.Chance(0.5) ? language.MaleName(rng) : language.Word(rng, 2, 3);
            string initial = Ascii.Fold(name);
            if (initial.Length == 0) continue;
            abbreviation = $"{char.ToUpperInvariant(initial[0])}{kind[0]}";
            if (abbreviation is not ("CE" or "AD" or "BC")) break;
        }
        return ($"{name} {kind}", abbreviation);
    }

    /// <summary>
    /// Three letters each, like vanilla's Jan..Dec, lengthened only where two months would
    /// otherwise read the same — a calendar whose short dates cannot tell two months apart is worse
    /// than one with a four-letter month in it.
    /// </summary>
    private static string[] ShortForms(IReadOnlyList<string> months)
    {
        var shorts = new string[months.Count];
        for (int length = 3; length <= 6; length++)
        {
            for (int m = 0; m < months.Count; m++)
                if (shorts[m] is null || shorts.Count(s => s == shorts[m]) > 1)
                    shorts[m] = months[m].Length <= length ? months[m] : months[m][..length];

            if (shorts.Distinct(StringComparer.Ordinal).Count() == shorts.Length) break;
        }
        return shorts;
    }
}
