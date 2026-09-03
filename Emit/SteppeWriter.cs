using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Binds vanilla's Great Steppe situation to the generated map.
///
/// The situation itself carries over untouched — its phases, catalysts, events, window and pulses
/// are all vanilla's and are read generically for any sub-region — so what has to be written is
/// only where it is. Three files do that:
///
/// <list type="bullet">
/// <item>the situation type, vanilla's file read from the installed game with only its
/// <c>sub_regions</c> block rewritten, so the situation declares exactly as many sub-regions as
/// this map's steppe earns and nothing else in the file drifts from the installed patch;</item>
/// <item>the history start, on day one of year one as vanilla does it, DLC-gated the same way and
/// rolling the same opening season per sub-region;</item>
/// <item>the region names, overriding "Western Steppe" and its siblings with names drawn from the
/// kingdoms the belt runs through.</item>
/// </list>
///
/// The geographical regions the sub-regions point at are written by
/// <see cref="CompatibilityWriter.WriteGeographicalRegions"/>, which re-declares every vanilla
/// region key anyway and takes the steppe's lists for the ones it fills.
///
/// Without the DLC nothing here does anything: the history file checks the same trigger vanilla's
/// does, and game_start.txt turns every nomad holding tribal on its own.
/// </summary>
public static class SteppeWriter
{
    private const string TypeKey = "the_great_steppe";

    private const string StartPhase = "situation_steppe_abundant_grazing_season";

    /// <summary>
    /// Vanilla's opening roll, verbatim: a bias toward the good season so nobody starts in a zud.
    /// </summary>
    private static readonly (int Weight, string? Phase)[] OpeningSeasons =
    [
        (4, null),
        (2, "situation_steppe_cold_zud_season"),
        (2, "situation_steppe_severe_drought_season"),
        (2, "situation_steppe_warm_nights_season"),
    ];

    public static void WriteAll(string modDir, string gameDir, SteppeMap steppe)
    {
        if (steppe.IsEmpty)
        {
            Console.WriteLine("  great steppe: none (no steppe belt worth a season)");
            return;
        }

        if (!WriteSituationType(modDir, gameDir, steppe))
        {
            Console.WriteLine("  great steppe: WARNING vanilla situation file not found under " +
                              $"'{gameDir}'; the situation is not started");
            return;
        }

        WriteHistory(modDir, steppe);
        WriteOwnRegions(modDir, steppe);
        WriteLocalisation(modDir, steppe);

        Console.WriteLine($"  great steppe: {steppe.SubRegions.Count} " +
                          (steppe.SubRegions.Count == 1 ? "region" : "regions") +
                          $", {steppe.Count} counties, {steppe.NomadCount} nomadic");
        foreach (var s in steppe.SubRegions)
        {
            var frontier = steppe.Expansions.Where(e => e.SubRegionKey == s.Key && !e.Parked).ToList();
            string expansions = frontier.Count == 0
                ? "no frontier to expand into"
                : $"can expand into {string.Join(", ", frontier.Select(e => $"{e.Name} ({e.Counties.Count})"))}";
            Console.WriteLine($"    {s.Key}: {s.Name} — {s.Counties.Count} counties; {expansions}");
        }
    }

    /// <summary>
    /// The regions behind the sub-regions on keys of our own — the outer two slots. The vanilla
    /// keys are re-declared by <see cref="CompatibilityWriter.WriteGeographicalRegions"/>, which
    /// walks vanilla's files and so never sees these; a filename vanilla never used keeps the
    /// two from colliding.
    /// </summary>
    private static void WriteOwnRegions(string modDir, SteppeMap steppe)
    {
        var own = steppe.SubRegions.Where(s => !s.VanillaRegion).ToList();
        if (own.Count == 0) return;

        var b = new JominiBuilder();
        b.Comment("""
                  Steppe sub-regions beyond vanilla's three. The keys are ours; the sub-regions
                  in common/situation/situations/the_great_steppe.txt point at them.
                  """);
        b.Blank();

        foreach (var s in own)
        {
            using (b.Block(s.RegionKey))
            using (b.Block("counties"))
                for (int i = 0; i < s.Counties.Count; i += 10)
                    b.Token(string.Join(' ', s.Counties.Skip(i).Take(10).Select(c => c.Key)));
            b.Blank();
        }

        string dir = Path.Combine(modDir, "map_data", "geographical_regions");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_steppe_regions.txt"), b.ToString());
    }

    /// <summary>
    /// Vanilla's situation type with its <c>sub_regions</c> block replaced.
    ///
    /// Same filename as vanilla's on purpose: situations are a database keyed by name, and a
    /// second definition of <c>the_great_steppe</c> in a differently named file would be a
    /// conflict rather than an override. Shadowing the file replaces the definition cleanly.
    ///
    /// Read from the installed game rather than shipped as a copy so that everything but the one
    /// block tracks the patch that is actually installed — the file is a thousand lines of phase
    /// script that Paradox tunes between versions.
    /// </summary>
    private static bool WriteSituationType(string modDir, string gameDir, SteppeMap steppe)
    {
        string source = Path.Combine(gameDir, "common", "situation", "situations", $"{TypeKey}.txt");
        if (!File.Exists(source)) return false;

        // Vanilla's file opens with a byte-order mark; WriteBom puts one back, so strip it here or
        // the shipped file carries two and the parser sees garbage before the first key.
        string text = File.ReadAllText(source).TrimStart((char)0xFEFF);

        string? original = SubRegionsBlock(text, out int start, out int end);
        if (original is null) return false;

        var block = new JominiBuilder(startDepth: 1);
        using (block.Block("sub_regions"))
        {
            foreach (var s in steppe.SubRegions)
            {
                using (block.Block(s.Key))
                {
                    block.Color("map_color", s.Color.R, s.Color.G, s.Color.B);
                    block.Inline("geographical_regions", s.RegionKey);
                }
            }
        }

        var b = new JominiBuilder();
        b.Comment("""
                  Vanilla's the_great_steppe situation, read from the installed game, with one
                  block changed: sub_regions names this map's steppe rather than Eurasia's.

                  Each sub-region is bound to one generated geographical region — see
                  map_data/geographical_regions — and there are as many of them as the belt earns,
                  ordered west to east under vanilla's own keys because base-game script hardcodes
                  situation_sub_region:steppe_west. Everything else here is vanilla's, verbatim.
                  """);
        b.Blank();
        // The span starts at the key, so the text before it ends with the line's own indent; the
        // builder writes its own, one level deep, so drop vanilla's or the block lands two deep.
        b.Raw(text[..start].TrimEnd(' ', '\t'));
        b.Raw(block.ToString().TrimEnd('\n'));
        b.Raw(text[end..]);

        string dir = Path.Combine(modDir, "common", "situation", "situations");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, $"{TypeKey}.txt"), b.ToString());
        return true;
    }

    /// <summary>
    /// The <c>sub_regions = { … }</c> block inside the type, brace-matched, with the span it
    /// occupies. The key is searched for at the start of a line so a sub-region *named*
    /// something-sub_regions could not be mistaken for it.
    /// </summary>
    private static string? SubRegionsBlock(string text, out int start, out int end)
    {
        start = end = -1;

        int at = 0;
        while (true)
        {
            at = text.IndexOf("sub_regions", at, StringComparison.Ordinal);
            if (at < 0) return null;

            bool lineStart = at == 0 || text[at - 1] is '\n' or '\t' or ' ';
            int after = at + "sub_regions".Length;
            bool assigned = after < text.Length && text[after..].TrimStart(' ', '\t').StartsWith('=');
            if (lineStart && assigned) break;
            at = after;
        }

        int open = text.IndexOf('{', at);
        if (open < 0) return null;

        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
            {
                start = at;
                end = i + 1;
                return text[start..end];
            }
        }

        return null;
    }

    /// <summary>
    /// Starts the situation.
    ///
    /// Year one rather than the bookmark date, which is where vanilla puts it and for a reason
    /// that matters here: a phase's <c>on_start</c> fires events at every nomad in the
    /// sub-region, and the variable that suppresses those on day one is set from
    /// <c>on_game_start</c>, which runs after history. Started on the bookmark date, the opening
    /// roll below would fire a season-change event at every khan before that guard exists.
    /// In year one there is nobody to fire it at.
    /// </summary>
    private static void WriteHistory(string modDir, SteppeMap steppe)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Starts the Great Steppe situation on this map. Mirrors vanilla's
                  mpo_the_great_steppe_history.txt, which the generator blanks, down to the
                  opening-season roll; only the sub-region keys differ, and only in number.
                  """);
        b.Blank();

        using (b.Block("1.1.1"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit")) b.Field("has_mpo_dlc_trigger", "yes");

            using (b.Block("start_situation"))
            {
                b.Field("type", TypeKey);
                b.Field("start_phase", StartPhase);
            }

            b.Comment("A bit skewed so nobody starts in an immediately bad season.");
            using (b.Block($"situation:{TypeKey}"))
            {
                foreach (var s in steppe.SubRegions)
                {
                    using (b.Block($"situation_sub_region:{s.Key}"))
                    using (b.Block("random_list"))
                    {
                        foreach (var (weight, phase) in OpeningSeasons)
                        {
                            using (b.Block(weight))
                            {
                                if (phase is null) continue;
                                using (b.Block("change_phase")) b.Field("phase", phase);
                            }
                        }
                    }
                }
            }
        }

        string dir = Path.Combine(modDir, "history", "situations");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_great_steppe.txt"), b.ToString());
    }

    /// <summary>
    /// The region names. Vanilla's <c>regions_l_english.yml</c> calls them Western, Central and
    /// Eastern Steppe; on a map where the one steppe is in the south-east, "Western Steppe" is a
    /// small lie told on every tooltip. The expansion regions likewise: the decision shows each
    /// item by its region's name, and "Northern Russia" over a generated duchy is the same lie.
    /// A parked key keeps vanilla's name, since nothing can show it. Written under
    /// <c>replace</c> so the key wins outright.
    /// </summary>
    private static void WriteLocalisation(string modDir, SteppeMap steppe)
    {
        var loc = new LocFile();

        // Two keys per sub-region: the situation window titles a sub-region through
        // <situation>_sub_region_<key> (vanilla's situation_sub_regions_l_english.yml), while
        // tooltips and script elsewhere name the geographical region behind it. Both say the
        // same thing so the player never sees two names for one place.
        foreach (var s in steppe.SubRegions)
        {
            loc.Add($"{TypeKey}_sub_region_{s.Key}", s.Name);
            loc.Add(s.RegionKey, s.Name);
        }
        loc.Blank();
        foreach (var e in steppe.Expansions.Where(e => !e.Parked)) loc.Add(e.Key, e.Name!);
        loc.Write(Path.Combine(modDir, "localization", "replace", "english", "zz_gen_steppe_l_english.yml"));
    }
}
