using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Gives each culture its own word for the realms its rulers hold <em>and for the rulers
/// themselves</em>, and gives each imported country its own word for itself — so the map is not
/// wall-to-wall Kingdoms and Dukes, and the United Provinces of Ryalos is not a Kingdom.
///
/// CK3 does not hardcode "Kingdom of" or "King". Both come out of <c>common/flavorization</c>: a
/// database of rules, each of which names a localisation key of its own name and applies when its
/// conditions match the holder.
///
/// <code>
/// kingdom_feudal = { type = title  tier = kingdom  priority = 46
///                    governments = { feudal_government } }
/// king_clan_male = { type = character  gender = male  special = holder  tier = kingdom
///                    priority = 46  governments = { clan_government } }
/// duchy_feudal_ban = { type = title  tier = duchy  priority = 28
///                      governments = { feudal_government administrative_government }
///                      name_lists = { name_list_croatian name_list_bosnian } }
/// </code>
///
/// That last one is the thing to understand before reading the rest, because getting it wrong made
/// this whole file inert for its entire life. <c>duchy_feudal_ban</c> <em>looks</em> like a pattern
/// the engine parses — tier, government, culture — and it is not. It is simply the name somebody
/// gave a rule. There is no key composed from a culture that the game will find on its own, so
/// writing <c>kingdom_feudal_gen_culture_12: "United Provinces"</c> into a .yml put a string in the
/// dictionary that nothing ever looked up, and every realm on every generated map kept vanilla's
/// words while the console cheerfully reported the vocabulary it had chosen.
///
/// Two kinds of rule are written. Per *culture and government*, matched on the culture's name list,
/// which is how a people comes to call all of its realms Tsardoms — the right shape for a generated
/// map, where a culture wanting its own vocabulary is the whole goal. And per *title*, matched on
/// one specific title id, which is what an import needs: Azgaar assigns a form per country, our
/// cultures are finer-grained than its countries, and only naming the title itself survives a state
/// whose capital belongs to a culture that mostly lives next door.
///
/// Written for every map, imported or not. Cultures that draw the ordinary vocabulary emit nothing
/// and fall through to vanilla's own rules, which is what keeps a tribal count a Chieftain.
/// </summary>
public static class TitleTierWriter
{
    private static readonly string Feudal = Token(GovernmentMap.Feudal);
    private static readonly string Clan = Token(GovernmentMap.Clan);
    private static readonly string Tribal = Token(GovernmentMap.Tribal);
    private static readonly string Republic = Token(GovernmentMap.Republic);
    private static readonly string Theocracy = Token(GovernmentMap.Theocracy);
    private static readonly string Administrative = Token(GovernmentMap.Administrative);
    private static readonly string Nomad = Token(GovernmentMap.Nomad);

    /// <summary>
    /// The governments a generated ruler can hold, as the short token the rest of this file keys
    /// vocabularies by — the government's name with its <c>_government</c> suffix removed.
    ///
    /// Only the suffix is dropped here; the entries themselves are written with the full
    /// <c>_government</c> name, which is what the <c>governments</c> condition matches on.
    /// </summary>
    private static readonly string[] Governments =
    [
        Token(GovernmentMap.Feudal), Token(GovernmentMap.Clan), Token(GovernmentMap.Tribal),
        Token(GovernmentMap.Republic), Token(GovernmentMap.Theocracy),
        Token(GovernmentMap.Administrative), Token(GovernmentMap.Nomad),
    ];

    /// <summary>Strips the <c>_government</c> suffix CK3's localisation keys leave off.</summary>
    public static string Token(string government)
        => government.EndsWith("_government", StringComparison.Ordinal)
            ? government[..^"_government".Length]
            : government;

    /// <summary>
    /// One culture's whole vocabulary for a rank: what the realm is called and what its holder is
    /// called, in both genders.
    ///
    /// <see cref="Suits"/> is which governments the vocabulary is drawn for. A Thearchy ruled by a
    /// Thearch is a fine word for a theocracy and a nonsense one for a horde, and since the keys are
    /// written per government anyway there is no reason to hand a feudal king a priest's title.
    /// Null suits anything.
    /// </summary>
    private sealed record Vocabulary(
        string Empire, string Kingdom, string Duchy,
        string Emperor, string Empress,
        string King, string Queen,
        string Duke, string Duchess,
        string[]? Suits = null)
    {
        public bool Fits(string government) => Suits is null || Suits.Contains(government);

        /// <summary>True for the vocabulary that needs no keys because it is already vanilla's.</summary>
        public bool IsPlain => Kingdom == "Kingdom" && Duchy == "Duchy" && Empire == "Empire";
    }

    /// <summary>
    /// Alternative vocabularies, each a full ladder from empire down to duchy.
    ///
    /// Chosen to be readable as English titles of the period rather than invented words: the culture
    /// name in front of them is already generated, and a generated word for the rank as well makes a
    /// title nobody can parse. Every one of these is a real historical style for a realm of roughly
    /// the right size, with the ruler's own style beside it.
    /// </summary>
    private static readonly Vocabulary[] Ladders =
    [
        new("Empire", "Kingdom", "Duchy",
            "Emperor", "Empress", "King", "Queen", "Duke", "Duchess"),

        new("Imperium", "Principality", "March",
            "Imperator", "Imperatrix", "Prince", "Princess", "Margrave", "Margravine",
            [Feudal, Clan, Administrative]),

        new("Dominion", "Realm", "Marches",
            "Overlord", "Overlady", "High Lord", "High Lady", "Warden", "Wardeness"),

        new("Suzerainty", "Protectorate", "Wardenry",
            "Suzerain", "Suzeraine", "Protector", "Protectress", "Warden", "Wardeness"),

        new("Autocracy", "Tsardom", "Voivodeship",
            "Autocrat", "Autocratrix", "Tsar", "Tsaritsa", "Voivode", "Voivodess",
            [Feudal, Clan, Administrative]),

        new("Grand Realm", "Grand Duchy", "Duchy",
            "Grand Prince", "Grand Princess", "Grand Duke", "Grand Duchess", "Duke", "Duchess",
            [Feudal, Administrative]),

        new("Great Khanate", "Khanate", "Horde",
            "Great Khan", "Great Khatun", "Khan", "Khatun", "Beg", "Begum",
            [Nomad, Clan, Tribal]),

        new("Hegemony", "Thearchy", "Prelacy",
            "Hegemon", "Hegemoness", "Thearch", "Thearchess", "Prelate", "Prelatess",
            [Theocracy]),

        new("Confederation", "League", "Commune",
            "Grand Doge", "Grand Dogaressa", "Doge", "Dogaressa", "Consul", "Consul",
            [Republic]),

        new("Grand Council", "Council", "Assembly",
            "Grand Speaker", "Grand Speaker", "Speaker", "Speaker", "Elder", "Elder",
            [Republic, Tribal]),
    ];

    /// <summary>
    /// Picks a vocabulary per culture and government, and per imported state, and writes both the
    /// flavorization entries and the localisation they resolve to.
    ///
    /// Imported maps take their words from the export where they can. Azgaar's <c>formName</c> is the
    /// country's own word for itself — Thearchy, League, Council, Principality, Grand Duchy — and it
    /// is a better answer than anything drawn from a pool, so a state keeps its own form on its own
    /// title and its people take the form their largest state uses for everything else.
    /// </summary>
    public static void WriteAll(string modDir, CultureMap cultures, Rng rng,
        Dictionary<(string Culture, string Government), string>? forms = null,
        AzgaarImport? azgaar = null)
    {
        var imported = forms ?? [];

        var entries = new List<Flavor>();

        // Drawn without replacement, reshuffling only once every ladder has been spent. Picking
        // independently each time meant two neighbouring peoples calling their realms Protectorates
        // on a sixteen-culture map, which reads as a bug rather than as a coincidence; a map with
        // more cultures than ladders does repeat, but only after using all of them.
        var undrawn = new List<Vocabulary>();

        int borrowed = 0, drawn = 0, plain = 0;

        foreach (var culture in cultures.Cultures)
        {
            // One draw per culture, reused for every government it has no imported form for, so a
            // people's realms read as one vocabulary rather than as seven unrelated ones. The
            // per-government filter is applied when the draw is spent, not when it is made.
            var drawnLadder = rng.Chance(0.55) ? DrawLadder() : null;
            bool tookAnything = false, tookImported = false;

            // Governments that ended up with the same words share one set of entries. The condition
            // takes a list, so a culture whose feudal, clan and administrative realms are all
            // Tsardoms needs three entries rather than nine.
            var byVocabulary = new Dictionary<Vocabulary, List<string>>();

            foreach (string government in Governments)
            {
                Vocabulary? vocabulary = null;

                if (imported.TryGetValue((culture.Key, government), out var form))
                {
                    vocabulary = Borrow(form, government, rng);
                    tookImported = true;
                }
                else if (drawnLadder is not null && drawnLadder.Fits(government))
                {
                    vocabulary = drawnLadder;
                }

                if (vocabulary is null || vocabulary.IsPlain) continue;
                tookAnything = true;

                if (!byVocabulary.TryGetValue(vocabulary, out var list))
                    byVocabulary[vocabulary] = list = [];
                list.Add(government);
            }

            int variant = 0;
            foreach (var (vocabulary, governments) in byVocabulary)
                Emit(entries, $"gen_flav_{culture.Key}_{variant++}", vocabulary,
                     nameList: culture.NameListKey, governments: governments,
                     titles: null, priority: CulturePriority);

            if (tookImported) borrowed++;
            else if (tookAnything) drawn++;
            else plain++;
        }

        // The country's own word, on the country's own title.
        //
        // This is the part a per-culture rule cannot express. Flavorization matches on the holder's
        // culture, and our cultures are finer-grained than Azgaar's states — the United Provinces of
        // Ryalos had its form written against the culture that held most of its counties, while its
        // capital, and so its ruler, was a different culture entirely, so nothing matched and the
        // game fell back to "Kingdom of Ryalos". Naming the title outranks all of it: whoever holds
        // it, and whatever they are, the realm is called what the export calls it.
        int state = 0;
        foreach (var (form, title) in StateForms(azgaar))
        {
            string ruler = Ruler.From(form);
            var vocabulary = Single(title.Tier, form, ruler, Ruler.Feminine(ruler));
            if (vocabulary is null) continue;

            Emit(entries, $"gen_flav_state_{state++}", vocabulary, nameList: null,
                 governments: null, titles: [title.Key], priority: TitlePriority);
        }

        if (entries.Count == 0) return;

        WriteEntries(modDir, entries);
        WriteLocalization(modDir, entries);

        Console.WriteLine($"  title tiers: {borrowed} cultures took the export's own words, " +
                          $"{drawn} drew one, {plain} kept Kingdom and Duchy; " +
                          $"{state} states named their own title");

        Vocabulary DrawLadder()
        {
            if (undrawn.Count == 0)
            {
                undrawn.AddRange(Ladders.Where(l => !l.IsPlain));
                rng.Shuffle(undrawn);
            }

            var picked = undrawn[^1];
            undrawn.RemoveAt(undrawn.Count - 1);
            return picked;
        }
    }

    /// <summary>
    /// Every imported state that has a word for itself, paired with the title it became.
    ///
    /// Shared with <see cref="MapGen.AzgaarNaming"/>, which has to strip that same word out of the
    /// state's name — the game says it once from here, and a name carrying it too renders "United
    /// Provinces of United Provinces of Ryalos". One list, read by both, so the two cannot disagree.
    /// </summary>
    public static IEnumerable<(string Form, Title Title)> StateForms(AzgaarImport? azgaar)
    {
        if (azgaar is null) yield break;

        foreach (var (id, title) in azgaar.StateTitles.OrderBy(kv => kv.Key))
        {
            if (azgaar.World.State(id)?.FormName is not { Length: > 0 } form) continue;
            if (Tier(title.Tier) is null) continue;

            yield return (form.Trim(), title);
        }
    }

    /// <summary>A vocabulary carrying one word, at the one rung the title actually sits on.</summary>
    private static Vocabulary? Single(string tier, string realm, string male, string female)
        => tier switch
        {
            "e" => new Vocabulary(realm, "", "", male, female, "", "", "", ""),
            "k" => new Vocabulary("", realm, "", "", "", male, female, "", ""),
            "d" => new Vocabulary("", "", realm, "", "", "", "", male, female),
            _ => null,
        };

    /// <summary>One flavorization entry: its key, what it matches on, and what it renders as.</summary>
    private sealed record Flavor(
        string Key, string Type, string Tier, string Text,
        string? Gender = null, string? NameList = null,
        IReadOnlyList<string>? Governments = null, IReadOnlyList<string>? Titles = null,
        int Priority = CulturePriority);

    /// <summary>
    /// Beats every vanilla entry that could match a generated culture.
    ///
    /// Vanilla's own culture rules sit at 28 to 47 and its special ones at 300ish; nothing there can
    /// match a generated culture anyway, since the conditions are on vanilla name lists and
    /// heritages. Sitting well above them costs nothing and means a future vanilla rule with an
    /// unexpectedly broad condition cannot quietly take a word back.
    /// </summary>
    private const int CulturePriority = 700;

    /// <summary>Above <see cref="CulturePriority"/>: a named country outranks its people's habits.</summary>
    private const int TitlePriority = 900;

    /// <summary>CK3's word for each of our tier letters, or null for tiers with no vocabulary.</summary>
    private static string? Tier(string tier) => tier switch
    {
        "e" => "empire", "k" => "kingdom", "d" => "duchy", _ => null,
    };

    /// <summary>
    /// Turns one vocabulary into the entries that express it: the realm's word for each rank, and
    /// the holder's word in each gender.
    ///
    /// Empty words are skipped, which is what lets a per-title vocabulary carry only the one rung
    /// its title sits on.
    /// </summary>
    private static void Emit(List<Flavor> into, string key, Vocabulary vocabulary,
        string? nameList, IReadOnlyList<string>? governments, IReadOnlyList<string>? titles,
        int priority)
    {
        Rank("empire", vocabulary.Empire, vocabulary.Emperor, vocabulary.Empress);
        Rank("kingdom", vocabulary.Kingdom, vocabulary.King, vocabulary.Queen);
        Rank("duchy", vocabulary.Duchy, vocabulary.Duke, vocabulary.Duchess);

        void Rank(string tier, string realm, string male, string female)
        {
            if (realm.Length > 0)
                into.Add(new Flavor($"{key}_{tier}", "title", tier, realm,
                    NameList: nameList, Governments: governments, Titles: titles,
                    Priority: priority));

            if (male.Length > 0)
                into.Add(new Flavor($"{key}_{tier}_male", "character", tier, male, Gender: "male",
                    NameList: nameList, Governments: governments, Titles: titles,
                    Priority: priority));

            if (female.Length > 0)
                into.Add(new Flavor($"{key}_{tier}_female", "character", tier, female,
                    Gender: "female", NameList: nameList, Governments: governments, Titles: titles,
                    Priority: priority));
        }
    }

    /// <summary>
    /// Writes <c>common/flavorization</c>.
    ///
    /// This file is the half that was missing, and its absence is why none of this ever reached the
    /// game. A key like <c>kingdom_feudal_gen_culture_12</c> looks like a pattern the engine parses —
    /// tier, government, culture — and it is not: vanilla's <c>duchy_feudal_roman</c> is simply the
    /// *name someone gave a flavorization entry*, and the engine localises the entry's own key once
    /// the entry matches. Writing the localisation alone put a string in the game's dictionary that
    /// nothing ever looked up.
    /// </summary>
    private static void WriteEntries(string modDir, List<Flavor> entries)
    {
        var sb = new StringBuilder();
        sb.Append("# Generated flavorization: each culture's word for its realms and their holders,\n");
        sb.Append("# and each imported country's word for itself.\n");
        sb.Append("#\n");
        sb.Append("# The entry key is the localisation key — see gen_title_tiers_l_english.yml. An\n");
        sb.Append("# entry here with no matching line there renders as its own key on screen.\n\n");

        foreach (var entry in entries)
        {
            sb.Append($"{entry.Key} = {{\n");
            sb.Append($"\ttype = {entry.Type}\n");
            if (entry.Gender is not null) sb.Append($"\tgender = {entry.Gender}\n");
            if (entry.Type == "character") sb.Append("\tspecial = holder\n");
            sb.Append($"\ttier = {entry.Tier}\n");
            sb.Append($"\tpriority = {entry.Priority}\n");

            if (entry.Governments is { Count: > 0 })
                sb.Append($"\tgovernments = {{ {string.Join(' ', entry.Governments
                    .Select(g => $"{g}_government"))} }}\n");

            if (entry.NameList is not null)
                sb.Append($"\tname_lists = {{ {entry.NameList} }}\n");

            if (entry.Titles is { Count: > 0 })
            {
                sb.Append($"\ttitles = {{ {string.Join(' ', entry.Titles)} }}\n");

                // The title is the country, so the test has to be against whoever holds *it*, not
                // against their suzerain. Left at the default, a state that ended up someone's
                // vassal would be checked against the overlord's title and never match its own.
                sb.Append("\tflavourization_rules = { top_liege = no }\n");
            }
            else if (entry.Governments is { Count: > 0 })
            {
                // Culture from the top liege, government from the character.
                //
                // Both halves are deliberate. Culture stays on the liege — that is the default, and
                // it is what makes a realm read as one realm rather than as a patchwork, since our
                // cultures are finer-grained than the countries they sit in and a minority-culture
                // vassal taking his own people's word for a duchy would be the odd one out among his
                // neighbours. Government does not: the export states one per country, so a feudal
                // Grand Duchy that Azgaar made a horde's vassal is still a feudal Grand Duchy, and
                // leaving the check on the liege styled its counties as part of the horde.
                //
                // Vanilla does exactly this, and for the same reason — see duchy_administrative in
                // common/flavorization/00_flavorization.txt, where the comment is that only the
                // governors should take the top liege's titles, "not also feudal vassals,
                // republican vassals, etc."
                sb.Append("\tflavourization_rules = {\n");
                sb.Append("\t\ttop_liege = yes\n");
                sb.Append("\t\tignore_top_liege_government = yes\n");
                sb.Append("\t}\n");
            }

            sb.Append("}\n\n");
        }

        string dir = Path.Combine(modDir, "common", "flavorization");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_generated_flavorization.txt"), sb.ToString());
    }

    private static void WriteLocalization(string modDir, List<Flavor> entries)
    {
        // No BOM in the string. ParadoxText.WriteBom encodes with one, and seeding the builder with
        // a second left U+FEFF in front of "l_english:" once the encoder's own was stripped — which
        // is not a header CK3 recognises, so the game skipped this whole file and every tier word in
        // it. No other loc writer here does it; this one did, silently, from the start.
        var sb = new StringBuilder("l_english:\n");
        foreach (var entry in entries) sb.Append($" {entry.Key}:0 \"{entry.Text}\"\n");

        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "gen_title_tiers_l_english.yml"), sb.ToString());
    }

    /// <summary>
    /// The form each culture's states mostly use, for each government those states hold.
    ///
    /// Keyed by government as well as by culture because a form word is a statement about the
    /// *state*, not about the people in it, and smearing one across a culture's every government
    /// produced keys like <c>kingdom_theocracy_gen_culture_4 "Grand Duchy"</c> — a theocracy called
    /// a Grand Duchy, written because one duchy-shaped monarchy happened to share the culture.
    /// Splitting by government keeps each word with the kind of realm that chose it.
    ///
    /// By majority within each bucket rather than by first match: a culture spanning a Thearchy and
    /// four Principalities is a culture of principalities, and taking whichever state happened to be
    /// enumerated first would make that a coin toss between runs.
    /// </summary>
    public static Dictionary<(string Culture, string Government), string> FormsByCulture(
        AzgaarImport azgaar, CultureMap cultures, Dictionary<int, string> stateGovernments)
    {
        var votes = new Dictionary<(string, string), Dictionary<string, double>>();

        foreach (var (id, title) in azgaar.StateTitles)
        {
            if (azgaar.World.State(id) is not { } state) continue;
            if (state.FormName is not { Length: > 0 } form) continue;

            string government = Token(stateGovernments.GetValueOrDefault(id, GovernmentMap.Feudal));
            var key = (cultures.For(title).Key, government);

            if (!votes.TryGetValue(key, out var tally)) votes[key] = tally = [];

            // Weighted by how much ground the state covers, not one vote each. Two states of one
            // culture is the common case and a head count ties, which then falls to an alphabetical
            // tie-break — so a Kingdom the size of the map lost its word to a Grand Duchy in the
            // corner of it purely because G sorts before K. Size is the honest tie-break: the
            // culture's word for a realm is the word its largest realm uses.
            double weight = state.Area > 0 ? state.Area : Math.Max(state.Cells, 1);
            tally[form] = tally.GetValueOrDefault(form) + weight;
        }

        return votes.ToDictionary(
            v => v.Key,
            v => v.Value.OrderByDescending(f => f.Value).ThenBy(f => f.Key, StringComparer.Ordinal)
                        .First().Key);
    }

    /// <summary>
    /// A full vocabulary built around one imported form, which sits at whichever rung it belongs on.
    ///
    /// Azgaar's forms are already ranked by area — Duchy, Grand Duchy, Principality, Kingdom, Empire
    /// in ascending order — but a theocracy or a republic draws from a different vocabulary, so the
    /// rung is decided by matching against the ladders below and falling back to the kingdom rung,
    /// which is where most states land.
    /// </summary>
    private static Vocabulary Borrow(string form, string government, Rng rng)
    {
        foreach (var ladder in Ladders)
        {
            if (Same(ladder.Empire, form) || Same(ladder.Kingdom, form) || Same(ladder.Duchy, form))
                return ladder;
        }

        // An unfamiliar form still becomes this culture's word for a kingdom, and its holder is
        // styled from it — a Sultanate has a Sultan, an Exarchate an Exarch. The ranks around it are
        // drawn so the ladder stays complete, from a pool that suits the government so a Caliphate's
        // duchies are not Communes.
        var basis = Fitting(government, rng);
        string ruler = Ruler.From(form);

        return basis with
        {
            Kingdom = form,
            King = ruler,
            Queen = Ruler.Feminine(ruler),
        };

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A drawn ladder that suits the given government, for the rungs a borrowed form leaves empty.
    ///
    /// The plain ladder is excluded even though it fits everything. This is only ever called to
    /// complete a form the export supplied, and completing "United Republic of Taisia" with plain
    /// Duchies under a plain Duke is the one outcome that adds nothing — the borrowed word is
    /// evidence the people has its own vocabulary, so the rungs around it should have one too.
    /// </summary>
    private static Vocabulary Fitting(string government, Rng rng)
    {
        var pool = Ladders.Where(l => !l.IsPlain && l.Fits(government)).ToList();
        return pool.Count == 0 ? Ladders[0] : rng.Pick(pool);
    }
}

/// <summary>
/// Turns the name of a realm into the style of whoever holds it.
///
/// English builds most of these by suffix and the suffixes are regular enough to be worth the
/// twenty lines: a Khaganate has a Khagan, a Sheikhdom a Sheikh, an Archbishopric an Archbishop, an
/// Oligarchy an Oligarch. Azgaar's form vocabulary is open — the states editor lets a user type
/// anything — so a table alone would leave holes, and a hole here is what puts a Patriarch back on
/// the throne of a Khanate.
/// </summary>
internal static class Ruler
{
    /// <summary>Forms whose ruler is not the form word with a suffix filed off.</summary>
    private static readonly Dictionary<string, string> Irregular =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Empire"] = "Emperor",
            ["Duchy"] = "Duke",
            ["Grand Duchy"] = "Grand Duke",
            ["Archduchy"] = "Archduke",
            ["County"] = "Count",
            ["Barony"] = "Baron",
            ["Principality"] = "Prince",
            ["Marquisate"] = "Marquis",
            ["March"] = "Margrave",
            ["Marches"] = "Margrave",
            ["Republic"] = "Consul",
            ["Most Serene Republic"] = "Doge",
            ["Trade Company"] = "Director",
            ["Federation"] = "Chancellor",
            ["Union"] = "Chancellor",
            ["United Kingdom"] = "King",
            ["United Provinces"] = "Stadtholder",
            ["United Republic"] = "Consul",
            ["Commonwealth"] = "Lord Protector",
            ["Confederation"] = "Grand Consul",
            ["League"] = "Doge",
            ["Commune"] = "Consul",
            ["Council"] = "Speaker",
            ["Community"] = "Elder",
            ["Junta"] = "Marshal",
            ["Heptarchy"] = "Bretwalda",
            ["Horde"] = "Khan",
            ["Ulus"] = "Khan",
            ["Orda"] = "Khan",
            ["Tribe"] = "Chieftain",
            ["Tribes"] = "High Chieftain",
            ["Brotherhood"] = "Grand Master",
            ["See"] = "Pontiff",
            ["Holy See"] = "Pontiff",
            ["Holy State"] = "Hierarch",
            ["Papacy"] = "Pope",
            ["Realm"] = "High Lord",
            ["Dominion"] = "Overlord",
            ["Prelacy"] = "Prelate",
            ["Wardenry"] = "Warden",
            ["Assembly"] = "Elder",
            ["Voivodeship"] = "Voivode",
        };

    /// <summary>Suffix rules, longest first so "-archy" wins over "-y" and "-ric" over "-c".</summary>
    private static readonly (string Suffix, string Replacement)[] Rules =
    [
        ("archy", "arch"),      // Thearchy, Oligarchy, Tetrarchy, Monarchy
        ("cracy", "crat"),      // Theocracy, Autocracy, Aristocracy
        ("ship", ""),           // Lordship, Stewardship
        ("dom", ""),            // Kingdom, Sheikhdom, Chiefdom, Tsardom, Earldom
        ("ric", ""),            // Bishopric, Archbishopric
        ("ate", ""),            // Khanate, Sultanate, Caliphate, Emirate, Despotate, Exarchate
    ];

    private static readonly Dictionary<string, string> Feminines =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Khan"] = "Khatun",
            ["Khagan"] = "Khatun",
            ["Great Khan"] = "Great Khatun",
            ["Sultan"] = "Sultana",
            ["Emir"] = "Emira",
            ["Shah"] = "Shahbanu",
            ["Tsar"] = "Tsaritsa",
            ["King"] = "Queen",
            ["Duke"] = "Duchess",
            ["Grand Duke"] = "Grand Duchess",
            ["Archduke"] = "Archduchess",
            ["Count"] = "Countess",
            ["Baron"] = "Baroness",
            ["Prince"] = "Princess",
            ["Emperor"] = "Empress",
            ["Doge"] = "Dogaressa",
            ["Chieftain"] = "Chieftess",
            ["High Chieftain"] = "High Chieftess",
            ["Pope"] = "Popess",
            ["Overlord"] = "Overlady",
            ["High Lord"] = "High Lady",
        };

    /// <summary>The style of whoever holds a realm of this name.</summary>
    public static string From(string form)
    {
        form = form.Trim();
        if (Irregular.TryGetValue(form, out string? irregular)) return irregular;

        foreach (var (suffix, replacement) in Rules)
        {
            if (!form.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

            string stem = form[..^suffix.Length] + replacement;

            // "Ate" off "Ulus-ate" is fine; off a three-letter word it is not. A stem too short to
            // be a word means the rule matched something that was never a suffix.
            if (stem.Length >= 3) return stem;
        }

        // Nothing recognisable. The rank's own plain word is still a better answer than the
        // government's, which is where "Patriarch" came from.
        return "King";
    }

    /// <summary>
    /// The feminine style, or the same word again.
    ///
    /// Same-word is what vanilla itself does wherever a language has no feminine — see
    /// <c>duke_nomad_female_turkish: "$duke_nomad_male_turkish$"</c> — and it is much better than
    /// inventing one, which is how you get a Thearchess nobody asked for.
    /// </summary>
    public static string Feminine(string ruler) => Feminines.GetValueOrDefault(ruler, ruler);
}
