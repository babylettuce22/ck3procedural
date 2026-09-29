using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the native rank titles <see cref="NativeTitles"/> coined: the flavorization that makes
/// the game use them, their localisation, the hidden game concepts that gloss each one in English
/// on hover, and the small runtime script the contract and titles-held variants need.
///
/// <code>
/// Related:
///   MapGen/Titles/NativeTitles.cs     the words, the ranks they gloss, families and variants
///   Emit/Culture/TitleTierWriter.cs   the English ladders underneath, and the caller of this
///   game/common/flavorization/_flavourization.info   what every condition below means
///   game/common/scripted_effects/00_flavorization_effects.txt   vanilla's own flag bridge
/// </code>
///
/// ---- Culture from the top liege, always ----
///
/// A realm reads as one realm: a vassal of another people is styled in the words of whoever
/// sits at the top, exactly as the English ladders are. Three kinds of entry get there by
/// different roads, because flavorization tests some conditions on the top liege and some on
/// the holder, and cannot mix the two:
///
/// * <b>By government</b> — <c>top_liege = yes</c> with <c>ignore_top_liege_government</c>: the
///   liege's culture, the holder's own government. The same rule the English ladders use.
/// * <b>Sovereigns</b> — <c>top_liege = no</c> with <c>only_independent</c>. Testing the holder is
///   safe here because an independent holder <em>is</em> their own top liege, so the culture
///   tested is the liege's anyway.
/// * <b>Contracts and titles held</b> — these are facts about the holder, and flavorization reads
///   a <c>flag</c> off the context character, which <c>top_liege = yes</c> would make the liege.
///   So the entries test the holder (<c>top_liege = no</c>), and the culture travels inside the
///   flag instead: <c>gen_native_rank_refresh_effect</c> sets <c>gen_rank_march_&lt;culture&gt;</c>
///   on a march lord, naming his top liege's culture's word for a margrave (one flag per distinct
///   word, so sister cultures that spell it alike share it). That script is the only runtime part
///   of the feature, and it is vanilla's own pattern (<c>additional_flavor_check_effect</c>) with
///   the culture added.
///
/// ---- Priorities ----
///
/// All above <see cref="TitleTierWriter"/>'s culture ladders (700) and below its per-title words
/// for imported countries (900), which name one specific country and outrank a people's habits.
/// Within the band: government 750, a convert's borrowed crown 760, sovereign 770, titles held
/// 780, contracts 790 — so a march lord who also holds two duchies is styled by his contract.
///
/// Pure, like <see cref="TitleTierWriter.WriteAll"/>: nothing is drawn here, and with nothing to
/// say every file is removed, so a world regenerated with the options off loses them.
/// </summary>
public static class NativeRankWriter
{
    private const int GovernmentPriority = 750;
    private const int SacredPriority = 760;
    private const int CoRulerPriority = 800;

    private static readonly string[] Genders = ["male", "female"];

    /// <summary>How an entry picks the character it tests; see the class remarks.</summary>
    private enum Rules { TopLiege, Sovereign, Holder, CoRuler }

    private sealed record Entry(
        string Key, string Type, string Tier, string Text, int Priority, Rules Rules,
        string? Gender = null, IReadOnlyList<string>? Governments = null,
        IReadOnlyList<string>? NameLists = null, IReadOnlyList<string>? Religions = null,
        string? Flag = null);

    public static void WriteAll(string modDir, CultureMap cultures, FaithMap faiths, bool tooltips)
    {
        string entriesPath = Path.Combine(modDir, "common", "flavorization", "zz_gen_native_ranks.txt");
        string locPath = Path.Combine(modDir, "localization", "english", "gen_native_ranks_l_english.yml");
        string conceptsPath = Path.Combine(modDir, "common", "game_concepts", "zz_gen_native_rank_concepts.txt");
        string conceptLocPath = Path.Combine(modDir, "localization", "english", "gen_native_rank_concepts_l_english.yml");
        string effectsPath = Path.Combine(modDir, "common", "scripted_effects", "zz_gen_native_rank_effects.txt");
        string onActionsPath = Path.Combine(modDir, "common", "on_action", "zz_gen_native_rank_on_actions.txt");

        var natives = cultures.Cultures.Where(c => c.NativeRanks is { IsEmpty: false }).ToList();

        var built = new List<Entry>();
        var concepts = new ConceptTooltips();
        var flags = new Flags();

        foreach (var culture in natives) Culture(culture, built, concepts, tooltips, flags);
        foreach (var religion in faiths.Religions) Sacred(religion, natives, built, concepts, tooltips);
        if (built.Any(e => e.Type == "character")) CoRulers(built);

        var entries = Merge(built);

        if (entries.Count == 0)
        {
            foreach (string path in new[] { entriesPath, locPath, conceptsPath, conceptLocPath, effectsPath, onActionsPath })
                if (File.Exists(path)) File.Delete(path);
            return;
        }

        WriteEntries(entriesPath, entries);

        var loc = new LocFile();
        foreach (var entry in entries) loc.AddBuilt(entry.Key, entry.Text);
        loc.Write(locPath);

        if (tooltips) concepts.Write(conceptsPath, conceptLocPath, ConceptsComment);
        else
        {
            if (File.Exists(conceptsPath)) File.Delete(conceptsPath);
            if (File.Exists(conceptLocPath)) File.Delete(conceptLocPath);
        }

        if (flags.Of.Count > 0)
        {
            WriteFlagScript(effectsPath, natives, flags);
            WriteOnActions(onActionsPath);
        }
        else
        {
            if (File.Exists(effectsPath)) File.Delete(effectsPath);
            if (File.Exists(onActionsPath)) File.Delete(onActionsPath);
        }

        int holders = entries.Count(e => e.Type == "character"), realms = entries.Count - holders;
        Console.WriteLine($"  native ranks: {holders} ruler styles and {realms} realm words for " +
                          $"{natives.Count} cultures ({built.Count} before sharing between kin)" +
                          (tooltips ? $", {concepts.Count} tooltips" : ""));
    }

    // --- Entries -------------------------------------------------------------------------------

    /// <summary>
    /// The runtime flags the flagged variants read, one per variant and <em>word</em> rather than
    /// per culture, so that sister cultures whose dialects spell a margrave the same way share one
    /// flag and so one entry. <see cref="Of"/> is what the script sets for each culture.
    /// </summary>
    private sealed class Flags
    {
        /// <summary>(variant, words) to the flag the first culture with those words named.</summary>
        public Dictionary<(string Variant, string Words), string> ByWords { get; } = [];

        /// <summary>(variant, culture key) to the flag a ruler under that culture's crown carries.</summary>
        public Dictionary<(string Variant, string Culture), string> Of { get; } = [];

        public string For(string variant, string culture, string words)
        {
            if (!ByWords.TryGetValue((variant, words), out string? flag))
                ByWords[(variant, words)] = flag = FlagName(variant, culture);
            Of[(variant, culture)] = flag;
            return flag;
        }
    }

    /// <summary>
    /// Folds entries that differ only in the culture they name into one entry naming them all.
    ///
    /// Sister cultures speak dialects of one language and most of their words come out the same —
    /// a sound shift moves only the roots that hold that sound — so a culture-by-culture file
    /// repeated most of itself. Measured on a 62-culture world before this: 6,353 entries, five
    /// times vanilla's whole flavorization. Flagged entries fold the same way because their flags
    /// are per word (<see cref="Flags"/>), and first occurrence keeps its key and its place.
    /// </summary>
    private static List<Entry> Merge(List<Entry> entries)
    {
        var merged = new List<Entry>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            string key = string.Join('\u001f', entry.Type, entry.Tier, entry.Gender ?? "", entry.Text,
                entry.Priority, entry.Rules, string.Join(' ', entry.Governments ?? []),
                string.Join(' ', entry.Religions ?? []), entry.Flag ?? "", entry.NameLists is null);

            if (!index.TryGetValue(key, out int at))
            {
                index[key] = merged.Count;
                merged.Add(entry);
                continue;
            }

            if (entry.NameLists is null) continue;   // an identical flagged entry: already there
            var lists = merged[at].NameLists!;
            merged[at] = merged[at] with { NameLists = [.. lists, .. entry.NameLists.Where(n => !lists.Contains(n))] };
        }

        return merged;
    }

    /// <summary>Every entry one culture's words need: by government, then each variant.</summary>
    private static void Culture(Culture culture, List<Entry> into, ConceptTooltips concepts,
        bool tooltips, Flags flags)
    {
        var ranks = culture.NativeRanks!;
        string c = culture.Key;
        string[] nameList = [culture.NameListKey];

        // By government. Families that land on the same rank at a tier share one entry — a feudal
        // and an administrative baron are both the people's hall-lord — so the conditions carry a
        // list of governments rather than the file carrying the entry twice.
        var holderGroups = new Dictionary<(string Tier, string Rank), List<string>>();
        var realmGroups = new Dictionary<(string Tier, string Rank), List<string>>();

        foreach (var family in NativeTitles.Families)
            for (int i = 0; i < NativeTitles.Tiers.Length; i++)
            {
                string tier = NativeTitles.Tiers[i];
                Group(holderGroups, (tier, family.Holders[i]), family.Governments);
                Group(realmGroups, (tier, family.Realms[i]), family.Governments);
            }

        if (ranks.Holders.Count > 0)
            foreach (var ((tier, rankId), governments) in holderGroups)
            {
                var rank = NativeTitles.HolderRanks[rankId];
                foreach (string gender in Genders)
                    into.Add(new Entry($"gen_nr_{c}_{NativeTitles.CkTier(tier)}_{rank.Id}_{gender}", "character",
                        NativeTitles.CkTier(tier), Holder(ranks, rank, gender, tooltips, false, concepts),
                        GovernmentPriority, Rules.TopLiege, gender, governments, nameList));
            }

        if (ranks.Realms.Count > 0)
            foreach (var ((tier, rankId), governments) in realmGroups)
            {
                var rank = NativeTitles.RealmRanks[rankId];
                into.Add(new Entry($"gen_nr_{c}_{NativeTitles.CkTier(tier)}_{rank.Id}", "title",
                    NativeTitles.CkTier(tier), Realm(ranks, rank, tooltips, false, concepts),
                    GovernmentPriority, Rules.TopLiege, Governments: governments, NameLists: nameList));
            }

        // Variants: the same people's words for a ruler's own situation.
        foreach (var variant in NativeTitles.Variants)
        {
            string tier = NativeTitles.CkTier(variant.Tier);
            var governments = variant.Families.SelectMany(f => NativeTitles.FamilyById(f).Governments).ToList();
            var rules = variant.Flag ? Rules.Holder : Rules.Sovereign;

            // Everything this variant would write for this culture, as the key its flag is shared by.
            string words = string.Join('|',
                ranks.Holders.TryGetValue(NativeTitles.HolderRanks[variant.Holder].Word, out var style)
                    ? $"{style.Male}|{style.Female}" : "",
                variant.Realm is { } r && ranks.Realms.TryGetValue(NativeTitles.RealmRanks[r].Word, out var place)
                    ? place : "");
            string? flag = variant.Flag ? flags.For(variant.Id, c, words) : null;
            string stem = variant.Flag ? $"gen_nr_{c}_{variant.Id}" : $"gen_nr_{c}_{variant.Id}_{tier}";

            // A flagged entry needs no name list: the flag already names the culture.
            var names = variant.Flag ? null : nameList;

            if (ranks.Holders.Count > 0)
            {
                var rank = NativeTitles.HolderRanks[variant.Holder];
                foreach (string gender in Genders)
                    into.Add(new Entry($"{stem}_{gender}", "character", tier,
                        Holder(ranks, rank, gender, tooltips, false, concepts), variant.Priority, rules,
                        gender, governments, names, Flag: flag));
            }

            if (ranks.Realms.Count > 0 && variant.Realm is { } realmId)
            {
                var rank = NativeTitles.RealmRanks[realmId];
                into.Add(new Entry($"{stem}_realm", "title", tier, Realm(ranks, rank, tooltips, false, concepts),
                    variant.Priority, rules, Governments: governments, NameLists: names, Flag: flag));
            }
        }

        static void Group(Dictionary<(string, string), List<string>> groups, (string, string) key, string[] governments)
        {
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.AddRange(governments.Where(g => !list.Contains(g)));
        }
    }

    /// <summary>
    /// A religion's crown words, for every people with native words that does not already call
    /// it home. A people whose own tongue is the religion's — or a sister of it — is skipped: the
    /// borrowed word would only be its own again with a sound changed.
    /// </summary>
    private static void Sacred(Religion religion, List<Culture> natives, List<Entry> into,
        ConceptTooltips concepts, bool tooltips)
    {
        if (religion.SacredRanks is not { IsEmpty: false } sacred) return;

        var converts = natives
            .Where(c => c.NativeRanks!.HomeReligion != religion.Key && c.NativeRanks.Family != sacred.Family)
            .Select(c => c.NameListKey)
            .ToList();
        if (converts.Count == 0) return;

        var crown = NativeTitles.FamilyById("crown");
        string[] religions = [religion.Key];

        foreach (string tier in NativeTitles.SacredTiers)
        {
            int i = Array.IndexOf(NativeTitles.Tiers, tier);
            string ckTier = NativeTitles.CkTier(tier);

            if (sacred.Holders.Count > 0)
            {
                var rank = NativeTitles.HolderRanks[crown.Holders[i]];
                foreach (string gender in Genders)
                    into.Add(new Entry($"gen_nr_sacred_{religion.Key}_{ckTier}_{gender}", "character", ckTier,
                        Holder(sacred, rank, gender, tooltips, true, concepts), SacredPriority, Rules.TopLiege,
                        gender, crown.Governments, converts, religions));
            }

            if (sacred.Realms.Count > 0)
            {
                var rank = NativeTitles.RealmRanks[crown.Realms[i]];
                into.Add(new Entry($"gen_nr_sacred_{religion.Key}_{ckTier}_realm", "title", ckTier,
                    Realm(sacred, rank, tooltips, true, concepts), SacredPriority, Rules.TopLiege,
                    Governments: crown.Governments, NameLists: converts, Religions: religions));
            }
        }
    }

    /// <summary>
    /// Vanilla's diarchy co-ruler style, restated above every entry here.
    ///
    /// A co-ruler is styled "Co-" and their liege's title by an untiered vanilla entry at priority
    /// 107 — above every ordinary vanilla rank, which is how it wins there — and every entry in
    /// this file outranks it, so without this a co-ruler would read as a plain native duke. Same
    /// conditions and the same text (<c>$co_ruler_male$</c>, which names the liege's title and so
    /// picks up the native word through it); only the priority moves.
    /// </summary>
    private static void CoRulers(List<Entry> into)
    {
        foreach (string gender in Genders)
            into.Add(new Entry($"gen_nr_co_ruler_{gender}", "character", Tier: "", Text: $"$co_ruler_{gender}$",
                Priority: CoRulerPriority, Rules: Rules.CoRuler, Gender: gender, Flag: "use_co_ruler_title"));
    }

    /// <summary>The flag a ruler carries for one variant, naming their top liege's culture.</summary>
    private static string FlagName(string variant, string culture) => $"gen_rank_{variant}_{culture}";

    // --- Text ----------------------------------------------------------------------------------

    private static string Holder(NativeRanks ranks, NativeTitles.HolderRank rank, string gender, bool tooltips,
        bool sacred, ConceptTooltips concepts)
    {
        var word = ranks.Holders[rank.Word];
        bool female = gender == "female";
        string text = female ? word.Female : word.Male;
        string english = female ? rank.Female : rank.Male;
        if (!tooltips) return text;

        // One concept per English style, so a Queen's tooltip says Queen; the feminine shares the
        // masculine's where English uses one word for both.
        string key = $"gen_rank_{rank.Id}{(female && rank.Female != rank.Male ? "_female" : "")}{(sacred ? "_sacred" : "")}";
        return concepts.Link(key, english, Describe(rank.Explanation, sacred), text);
    }

    private static string Realm(NativeRanks ranks, NativeTitles.RealmRank rank, bool tooltips, bool sacred,
        ConceptTooltips concepts)
    {
        string text = ranks.Realms[rank.Word];
        if (!tooltips) return text;

        string key = $"gen_realm_{rank.Id}{(sacred ? "_sacred" : "")}";
        return concepts.Link(key, rank.English, Describe(rank.Explanation, sacred), text);
    }

    /// <summary>
    /// The line under the English header. The header is the gloss — a native word is obviously
    /// native, so saying so again, and naming the English a second time, only made the tooltip three
    /// paragraphs tall. Every rank carries a one-liner: an empty description does not collapse, it
    /// leaves a blank line under the header (see <see cref="ConceptTooltips"/>).
    /// </summary>
    private static string Describe(string explanation, bool sacred)
        => !sacred ? explanation
         : explanation.Length > 0 ? $"{explanation} Borrowed from the faith's holy tongue."
         : "Borrowed from the faith's holy tongue.";

    // --- Files ---------------------------------------------------------------------------------

    private static void WriteEntries(string path, List<Entry> entries)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Generated flavorization: each culture's own words for its rulers and realms,
                  coined from its language (MapGen/NativeTitles.cs).

                  The entry key is the localisation key — see gen_native_ranks_l_english.yml.
                  Flagged entries are set by gen_native_rank_refresh_effect, which names the top
                  liege's culture in the flag; see zz_gen_native_rank_effects.txt.
                  """);
        b.Blank();

        foreach (var entry in entries)
        {
            using (b.Block(entry.Key))
            {
                b.Field("type", entry.Type);
                b.Field("gender", entry.Gender);
                if (entry.Type == "character") b.Field("special", "holder");
                if (entry.Tier.Length > 0) b.Field("tier", entry.Tier);
                b.Field("priority", entry.Priority);
                b.Field("flag", entry.Flag);

                if (entry.Governments is { Count: > 0 }) b.Inline("governments", [.. entry.Governments]);
                if (entry.NameLists is { Count: > 0 }) b.Inline("name_lists", [.. entry.NameLists]);
                if (entry.Religions is { Count: > 0 }) b.Inline("religions", [.. entry.Religions]);

                using (b.Block("flavourization_rules"))
                {
                    switch (entry.Rules)
                    {
                        case Rules.TopLiege:
                            b.Field("top_liege", "yes");
                            b.Field("ignore_top_liege_government", "yes");
                            break;
                        case Rules.Sovereign:
                            b.Field("top_liege", "no");
                            b.Field("only_independent", "yes");
                            break;
                        case Rules.Holder:
                            b.Field("top_liege", "no");
                            break;
                        case Rules.CoRuler:   // vanilla's co_ruler_male, word for word
                            b.Field("spouse_takes_title", "no");
                            b.Field("top_liege", "no");
                            b.Field("only_holder", "yes");
                            b.Field("only_vassals", "yes");
                            break;
                    }
                }
            }

            b.Blank();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    /// <summary>
    /// The tooltips' file header: one hidden concept per English rank, written by
    /// <see cref="ConceptTooltips"/>. Hidden because they are glosses, not rules — an encyclopedia
    /// page for "Duke" in a list of game mechanics would be noise.
    /// </summary>
    private const string ConceptsComment = """
                  Native rank tooltips: a word in a culture's own language, glossed with the
                  English rank it stands for. Referenced from gen_native_ranks_l_english.yml as
                  [Concept('key','word')|E]. Hidden from the encyclopedia.
                  """;

    /// <summary>
    /// The runtime half: which contract or titles-held variant a ruler is entitled to, set as a
    /// character flag that names the top liege's culture.
    ///
    /// The contract tests are vanilla's own, from <c>additional_flavor_check_effect</c> — including
    /// the title variable vanilla leaves for one day on a dying march lord's primary title, so the
    /// heir keeps the style through the succession. They are repeated here rather than read off
    /// vanilla's <c>margrave_flag</c> because both effects run from the same on_actions and nothing
    /// fixes which runs first.
    /// </summary>
    private static void WriteFlagScript(string path, List<Culture> natives, Flags flags)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Native rank titles: the variants flavorization cannot test by itself.

                  A march lord, a palatine, a castellan, a grand duke or a high king is styled in
                  their TOP LIEGE's language, but those are facts about the holder, and a
                  flavorization flag is read off the holder once top_liege = no. So the flag
                  carries the liege's culture's word in its name: gen_rank_<variant>_<culture>,
                  one flag per distinct word, named after the first culture to use it — sister
                  cultures that say it the same way share it.
                  Run on every title and liege change, yearly, and once at game start.
                  """);
        b.Blank();

        using (b.Block("gen_native_rank_refresh_effect"))
        {
            using (b.Block("if"))
            {
                using (b.Block("limit")) b.Field("has_character_flag", "gen_native_rank_flagged");
                b.Field("remove_character_flag", "gen_native_rank_flagged");
                b.Field("gen_native_rank_clear_effect", "yes");
            }

            using (b.Block("if"))
            {
                using (b.Block("limit"))
                {
                    b.Field("is_alive", "yes");
                    b.Field("is_landed", "yes");
                }

                Branch(b, "if", "march", "has_march_contract", "tier_duchy", "margrave_flag");
                Branch(b, "else_if", "palatine", "has_palatinate_contract", "tier_duchy", "duchy_palatinate_flag");
                Branch(b, "else_if", "palatine_county", "has_palatinate_contract", "tier_county", "county_palatinate_flag");
                Branch(b, "else_if", "castellan", "has_castellan_contract", "tier_county", "castellan_flag");
                Held(b, "high", "tier_kingdom");
                Held(b, "grand", "tier_duchy");
            }
        }

        b.Blank();

        // One tagging effect per variant: which flag a ruler gets depends on the variant as well as
        // on the liege's culture, since two cultures can share a margrave and not a castellan.
        foreach (string variant in NativeTitles.Variants.Where(v => v.Flag).Select(v => v.Id).Distinct())
        {
            var byFlag = natives
                .Where(c => flags.Of.ContainsKey((variant, c.Key)))
                .GroupBy(c => flags.Of[(variant, c.Key)])
                .ToList();

            using (b.Block($"gen_native_rank_tag_{variant}_effect"))
            {
                b.Field("add_character_flag", "gen_native_rank_flagged");
                b.Field("save_scope_as", "gen_native_rank_holder");

                using (b.Block("top_liege"))
                {
                    for (int i = 0; i < byFlag.Count; i++)
                    {
                        using (b.Block(i == 0 ? "if" : "else_if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("OR"))
                                foreach (var culture in byFlag[i])
                                    b.Field("has_culture", $"culture:{culture.Key}");

                            using (b.Block("scope:gen_native_rank_holder"))
                                b.Field("add_character_flag", byFlag[i].Key);
                        }
                    }
                }
            }

            b.Blank();
        }

        using (b.Block("gen_native_rank_clear_effect"))
            foreach (string flag in flags.Of.Values.Distinct().Order(StringComparer.Ordinal))
                b.Field("remove_character_flag", flag);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());

        static void Branch(JominiBuilder b, string keyword, string variant, string contract, string tier, string carried)
        {
            using (b.Block(keyword))
            {
                using (b.Block("limit"))
                using (b.Block("OR"))
                {
                    using (b.Block("AND"))
                    {
                        b.Field("vassal_contract_has_flag", contract);
                        b.Field("highest_held_title_tier", tier);
                    }

                    // Token, not Block: a block key is written with " = ", which makes "?= =".
                    b.Token($"primary_title ?= {{ has_variable = {carried} }}");
                }

                Tag(b, variant);
            }
        }

        static void Held(JominiBuilder b, string variant, string tier)
        {
            using (b.Block("else_if"))
            {
                using (b.Block("limit"))
                {
                    b.Field("highest_held_title_tier", tier);
                    using (b.Block("any_held_title"))
                    {
                        b.Field("tier", tier);
                        b.Token("count >= 2");   // a comparison, so not a Field

                    }
                }

                Tag(b, variant);
            }
        }

        static void Tag(JominiBuilder b, string variant) => b.Field($"gen_native_rank_tag_{variant}_effect", "yes");
    }

    private static void WriteOnActions(string path)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Native rank titles: keep the variant flags current. Appended to vanilla's
                  on_actions (each declaration adds to the list, none replaces it). There is no
                  on_action for a contract change, so the yearly pulse is the backstop for one.
                  """);
        b.Blank();

        foreach (string hook in new[] { "on_title_gain", "on_title_lost", "on_rank_up", "on_rank_down",
                                        "on_vassal_change", "yearly_playable_pulse" })
        {
            using (b.Block(hook))
            using (b.Block("on_actions"))
                b.Token("gen_native_rank_on_change");
            b.Blank();
        }

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_native_rank_game_start");
        b.Blank();

        using (b.Block("gen_native_rank_on_change"))
        using (b.Block("effect"))
            b.Field("gen_native_rank_refresh_effect", "yes");
        b.Blank();

        using (b.Block("gen_native_rank_game_start"))
        using (b.Block("effect"))
        using (b.Block("every_ruler"))
            b.Field("gen_native_rank_refresh_effect", "yes");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }
}
