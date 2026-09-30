using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One people's own words for its ranks, coined in its tongue: what it calls a count, a duke, a
/// king, and — when realm names are on — a county, a duchy, a kingdom. Keyed by the coinage ids in
/// <see cref="NativeTitles"/> ("king", "grand_duke", "march"), so the writer can look any rank up
/// without knowing how it was spelled.
///
/// Decided once, by <see cref="NativeTitles.Assign"/>, and only read after that, which is what
/// keeps a re-emit from the editor from reshuffling anyone's words.
/// </summary>
public sealed class NativeRanks
{
    /// <summary>Ruler styles by coinage id, in both genders. Empty when native titles are off.</summary>
    public Dictionary<string, RankWord> Holders { get; } = new(StringComparer.Ordinal);

    /// <summary>Realm words by coinage id. Empty when native realm names are off.</summary>
    public Dictionary<string, string> Realms { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The religion most of this people's own counties followed at generation. A realm of this
    /// culture under any other religion takes its crown's words from that religion's holy tongue.
    /// Null for a people with no generated religion under it.
    /// </summary>
    public string? HomeReligion { get; init; }

    /// <summary>The key of the language family the words came from, so a loan is skipped between kin.</summary>
    public required string Family { get; init; }

    public bool IsEmpty => Holders.Count == 0 && Realms.Count == 0;
}

/// <summary>A ruler's style in both genders; the same word twice where the language has no feminine.</summary>
public sealed record RankWord(string Male, string Female);

/// <summary>
/// Native rank titles: every generated culture's words for its rulers and realms, coined from its
/// own language, with the English rank each one stands for kept beside it for the in-game tooltip.
///
/// <code>
/// Related:
///   MapGen/Language/Language.cs       RankWord, FeminineRank — the compounding itself
///   Emit/Culture/NativeRankWriter.cs  the flavorization, loc, tooltips and flag script built from this
///   Emit/Culture/TitleTierWriter.cs   the English ladders this sits above when both are on
/// </code>
///
/// ---- Words are compounds of roots, not draws ----
///
/// Every language already has a root for "king", "lord", "great", "high", "war", "folk", "priest"
/// and a hundred more concepts (<see cref="Lexicon"/>). A rank is a recipe over them — a duke is
/// war + lord, which is what Herzog means; an emperor is great + king — so a people's titles share
/// roots with each other the way real ones do, and a sister culture, whose roots are its parent's
/// with a sound shifted, comes out with cognates rather than strangers. Each rank lists a few
/// recipes in order; the first one that spells a legal, unblocked word not already taken by
/// another rank of the same people wins.
///
/// The few decisions that are draws — whether the language marks a feminine, which feminine
/// ending, which realm ending — are drawn once per language <em>family</em>, seeded from the
/// root language's key, so a whole family agrees on its grammar.
///
/// ---- Never on the main stream ----
///
/// Every draw here comes off its own <see cref="Rng.For(int, int, ulong, int)"/> stream, and the
/// lexicon was already built. Turning native titles on or off leaves every other name in the
/// world exactly as it was.
/// </summary>
public static class NativeTitles
{
    private const int GrammarStream = 0x4A7E;
    private const int WordStream = 0x4A7F;

    // --- What is coined -------------------------------------------------------------------------

    /// <summary>One word to coin, with the recipes to try in order.</summary>
    private sealed record Coinage(string Id, string[][] Recipes);

    private const string R = Language.RealmEnding;

    /// <summary>
    /// Ruler styles. Coined before the realm words, and in this order, so that the realm option
    /// cannot move a single title: uniqueness is first come, and the titles always come first.
    /// </summary>
    private static readonly Coinage[] HolderCoinages =
    [
        new("baron", [["hall", "lord"], ["home", "lord"], ["fort", "lord"]]),
        new("count", [["lord"], ["fair", "lord"], ["old", "lord"]]),
        new("duke", [["war", "lord"], ["warrior", "lord"], ["strength", "lord"]]),
        new("king", [["king"], ["high", "king"]]),
        new("emperor", [["great", "king"], ["glory", "king"], ["high", "king"]]),
        new("hegemon", [["sun", "king"], ["star", "king"], ["victory", "king"], ["glory", "king"]]),
        new("prince", [["glory", "lord"], ["bright", "lord"], ["honour", "lord"], ["high", "lord"]]),
        new("margrave", [["wall", "lord"], ["gate", "lord"], ["guard", "lord"]]),
        new("palatine", [["king", "lord"], ["tower", "lord"], ["hall", "king"]]),
        new("castellan", [["fort", "lord"], ["tower", "lord"], ["wall", "lord"]]),
        new("grand_duke", [["great", "lord"], ["broad", "lord"], ["high", "lord"]]),
        new("high_king", [["high", "king"], ["great", "king"], ["glory", "king"], ["victory", "king"]]),
        new("magistrate", [["town", "guard"], ["market", "guard"], ["town", "lord"]]),
        new("governor", [["high", "guard"], ["guard", "lord"], ["wisdom", "lord"]]),
        new("viceroy", [["king", "guard"], ["king", "friend"], ["guard", "king"]]),
        new("headman", [["home", "lord"], ["farm", "lord"], ["hall", "folk"]]),
        new("chieftain", [["folk", "lord"], ["folk", "king"], ["folk"]]),
        new("high_chieftain", [["folk", "king"], ["high", "folk"], ["old", "folk"]]),
        new("patriarch", [["old", "lord"], ["old", "king"], ["old", "folk"]]),
        new("grand_patriarch", [["old", "king"], ["great", "folk"], ["great", "old"]]),
        new("priest", [["priest"], ["holy", "lord"]]),
        new("high_priest", [["high", "priest"], ["old", "priest"]]),
        new("archpriest", [["great", "priest"], ["old", "priest"], ["wisdom", "priest"]]),
        new("grand_priest", [["holy", "priest"], ["king", "priest"], ["glory", "priest"]]),
        new("divine_emperor", [["holy", "king"], ["holy", "lord"], ["blessing", "king"]]),
        new("mayor", [["town", "friend"], ["market", "friend"], ["town", "lord"]]),
        new("lord_mayor", [["old", "friend"], ["wisdom", "friend"], ["harbour", "lord"]]),
        new("grand_mayor", [["great", "friend"], ["broad", "friend"], ["high", "friend"]]),
        new("high_prince", [["high", "friend"], ["bright", "friend"], ["glory", "friend"]]),
        new("grand_prince", [["glory", "friend"], ["honour", "friend"], ["victory", "friend"]]),
    ];

    /// <summary>Realm words: most are a root and the language's realm ending, as Kingdom is king + dom.</summary>
    private static readonly Coinage[] RealmCoinages =
    [
        new("barony", [["hall", R], ["home", R], ["fort"]]),
        new("county", [["lord", R], ["fair", R], ["old", R]]),
        new("duchy", [["war", R], ["warrior", R], ["strength", R]]),
        new("kingdom", [["king", R], ["high", R]]),
        new("empire", [["great", R], ["glory", R], ["high", R]]),
        new("hegemony", [["sun", R], ["star", R], ["victory", R]]),
        new("principality", [["glory", R], ["bright", R], ["honour", R]]),
        new("march", [["wall", R], ["gate", R], ["guard", R]]),
        new("palatinate", [["king", "hall"], ["tower", R], ["hall", R]]),
        new("castellany", [["fort", R], ["tower", R], ["wall", R]]),
        new("magistracy", [["town", R], ["market", R]]),
        new("province", [["wisdom", R], ["guard", R], ["road", R]]),
        new("viceroyalty", [["guard", R], ["peace", R], ["high", R]]),
        new("tribe", [["folk"], ["folk", R]]),
        new("chiefdom", [["folk", R], ["home", R]]),
        new("high_chiefdom", [["high", "folk"], ["great", "folk"], ["old", "folk"]]),
        new("clan", [["old", "folk"], ["friend", "folk"], ["old", R]]),
        new("grand_clan", [["great", "folk"], ["great", "old"], ["broad", "folk"]]),
        new("high_kingdom", [["high", R], ["glory", R], ["victory", R]]),
        new("temple", [["temple"], ["holy", R]]),
        new("great_temple", [["great", "temple"], ["high", "temple"], ["holy", "temple"]]),
        new("theocracy", [["priest", R], ["holy", R]]),
        new("grand_theocracy", [["holy", R], ["great", "priest"], ["blessing", R]]),
        new("holy_empire", [["blessing", R], ["holy", "king"], ["glory", R]]),
        new("city", [["town"], ["market"]]),
        new("grand_city", [["great", "town"], ["broad", "town"], ["high", "town"]]),
        new("republic", [["friend", R], ["peace", R]]),
        new("serene_republic", [["peace", R], ["bright", R], ["fair", R]]),
        new("grand_principality", [["honour", R], ["great", "friend"], ["broad", R]]),
    ];

    /// <summary>What a religion lends a converted people: only the crown's own words.</summary>
    private static readonly HashSet<string> SacredCoinages =
        ["king", "emperor", "hegemon", "kingdom", "empire", "hegemony"];

    // --- What each word means -------------------------------------------------------------------

    /// <summary>
    /// A rank as a player reads it: which coined word it uses, and the English it is glossed with in
    /// the tooltip. Two ranks can share a word — a palatine duke and a count palatine are both the
    /// people's "king's lord" — while keeping their own gloss.
    ///
    /// The explanation is the tooltip's only line under the English header, so it is empty
    /// wherever the header already says it all: "Kingdom" needs no "A kingdom." under it. Kept to
    /// one short sentence where it earns its place — a tooltip that says "Margrave" still owes the
    /// reader what a march is.
    /// </summary>
    public sealed record HolderRank(string Id, string Word, string Male, string Female, string Explanation);

    /// <inheritdoc cref="HolderRank"/>
    public sealed record RealmRank(string Id, string Word, string English, string Explanation);

    public static readonly IReadOnlyDictionary<string, HolderRank> HolderRanks = new[]
    {
        new HolderRank("baron", "baron", "Baron", "Baroness", "Holds a barony."),
        new HolderRank("count", "count", "Count", "Countess", "Rules a county."),
        new HolderRank("duke", "duke", "Duke", "Duchess", "Rules a duchy."),
        new HolderRank("king", "king", "King", "Queen", "Rules a kingdom."),
        new HolderRank("emperor", "emperor", "Emperor", "Empress", "Rules an empire."),
        new HolderRank("hegemon", "hegemon", "Hegemon", "Hegemoness", "Overlord of emperors."),
        new HolderRank("prince", "prince", "Prince", "Princess", "A sovereign duke."),
        new HolderRank("margrave", "margrave", "Margrave", "Margravine", "Holds a border march."),
        new HolderRank("palatine", "palatine", "Palatine", "Palatine", "Rules a duchy with the crown's powers."),
        new HolderRank("count_palatine", "palatine", "Count Palatine", "Countess Palatine", "Rules a county with the crown's powers."),
        new HolderRank("castellan", "castellan", "Castellan", "Castellan", "Keeps a county as the crown's fortress."),
        new HolderRank("grand_duke", "grand_duke", "Grand Duke", "Grand Duchess", "Holds two or more duchies."),
        new HolderRank("high_king", "high_king", "High King", "High Queen", "A king above kings."),
        new HolderRank("magistrate", "magistrate", "Magistrate", "Magistrate", "Administers a county for the state."),
        new HolderRank("governor", "governor", "Governor", "Governess", "Governs a province for the state."),
        new HolderRank("viceroy", "viceroy", "Viceroy", "Vicereine", "Governs a kingdom for the emperor."),
        new HolderRank("headman", "headman", "Headman", "Headwoman", "Leads a tribal holding."),
        new HolderRank("chieftain", "chieftain", "Chieftain", "Chieftess", "Leads a tribal county."),
        new HolderRank("high_chieftain", "high_chieftain", "High Chieftain", "High Chieftess", "Leads a tribal duchy."),
        new HolderRank("patriarch", "patriarch", "Patriarch", "Matriarch", "Head of a great clan."),
        new HolderRank("grand_patriarch", "grand_patriarch", "Grand Patriarch", "Grand Matriarch", "Head of a union of clans."),
        new HolderRank("priest", "priest", "Priest", "Priestess", "Priest-ruler of a temple."),
        new HolderRank("high_priest", "high_priest", "High Priest", "High Priestess", "Priest-ruler of a county."),
        new HolderRank("archpriest", "archpriest", "Archpriest", "Archpriestess", "Priest-ruler of a duchy."),
        new HolderRank("grand_priest", "grand_priest", "Grand Priest", "Grand Priestess", "Priest-ruler of a kingdom."),
        new HolderRank("divine_emperor", "divine_emperor", "Divine Emperor", "Divine Empress", "Priest-ruler of an empire."),
        new HolderRank("mayor", "mayor", "Mayor", "Mayor", "Heads a self-governing city."),
        new HolderRank("lord_mayor", "lord_mayor", "Lord-Mayor", "Lady-Mayor", "Heads a county-rank republic."),
        new HolderRank("grand_mayor", "grand_mayor", "Grand Mayor", "Grand Mayor", "Heads a duchy-rank republic."),
        new HolderRank("high_prince", "high_prince", "High Prince", "High Princess", "Heads a kingdom-rank republic."),
        new HolderRank("grand_prince", "grand_prince", "Grand Prince", "Grand Princess", "Heads an imperial republic."),
    }.ToDictionary(r => r.Id, StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, RealmRank> RealmRanks = new[]
    {
        new RealmRank("barony", "barony", "Barony", "A single holding and its lands."),
        new RealmRank("county", "county", "County", "A count's lands."),
        new RealmRank("duchy", "duchy", "Duchy", "A duke's lands."),
        new RealmRank("kingdom", "kingdom", "Kingdom", "A king's realm."),
        new RealmRank("empire", "empire", "Empire", "An emperor's realm."),
        new RealmRank("hegemony", "hegemony", "Hegemony", "Above empires."),
        new RealmRank("principality", "principality", "Principality", "A sovereign duchy."),
        new RealmRank("march", "march", "March", "A border duchy."),
        new RealmRank("palatinate", "palatinate", "Palatinate", "A duchy with the crown's powers."),
        new RealmRank("county_palatine", "palatinate", "County Palatine", "A county with the crown's powers."),
        new RealmRank("castellany", "castellany", "Castellany", "A county kept as the crown's fortress."),
        new RealmRank("magistracy", "magistracy", "Magistracy", "A county administered for the state."),
        new RealmRank("province", "province", "Province", "A duchy governed for the state."),
        new RealmRank("viceroyalty", "viceroyalty", "Viceroyalty", "A kingdom governed for the emperor."),
        new RealmRank("tribe", "tribe", "Tribe", "A tribal holding."),
        new RealmRank("chiefdom", "chiefdom", "Chiefdom", "A tribal county."),
        new RealmRank("high_chiefdom", "high_chiefdom", "High Chiefdom", "A tribal duchy."),
        new RealmRank("clan", "clan", "Clan", "A great clan's realm."),
        new RealmRank("grand_clan", "grand_clan", "Grand Clan", "A union of clans."),
        new RealmRank("high_kingdom", "high_kingdom", "High Kingdom", "A realm of kings."),
        new RealmRank("temple", "temple", "Temple", "A holding ruled by priests."),
        new RealmRank("great_temple", "great_temple", "Great Temple", "A county ruled by priests."),
        new RealmRank("theocracy", "theocracy", "Theocracy", "A duchy ruled by priests."),
        new RealmRank("grand_theocracy", "grand_theocracy", "Grand Theocracy", "A kingdom ruled by priests."),
        new RealmRank("holy_empire", "holy_empire", "Holy Empire", "An empire ruled by priests."),
        new RealmRank("city", "city", "City", "A self-governing city."),
        new RealmRank("grand_city", "grand_city", "Grand City", "A county-rank republic."),
        new RealmRank("republic", "republic", "Republic", "A duchy-rank republic."),
        new RealmRank("serene_republic", "serene_republic", "Most Serene Republic", "A kingdom-rank republic."),
        new RealmRank("grand_principality", "grand_principality", "Grand Principality", "An imperial republic."),
    }.ToDictionary(r => r.Id, StringComparer.Ordinal);

    // --- Who uses which -------------------------------------------------------------------------

    /// <summary>Our tier letters, bottom up, and the ladders below are indexed the same way.</summary>
    public static readonly string[] Tiers = ["b", "c", "d", "k", "e", "h"];

    /// <summary>CK3's word for a tier letter, as flavorization's <c>tier</c> field wants it.</summary>
    public static string CkTier(string tier) => tier switch
    {
        "b" => "barony", "c" => "county", "d" => "duchy", "k" => "kingdom", "e" => "empire", _ => "hegemony",
    };

    /// <summary>
    /// Governments that share one ladder, and the ladder, as vanilla styles them: a feudal duke, an
    /// administrative governor, a tribal high chieftain. Nomads take the tribal ladder, as they do
    /// in vanilla; All Under Heaven's governments join the family whose vassals they resemble.
    /// </summary>
    public sealed record Family(string Id, string[] Governments, string[] Holders, string[] Realms);

    public static readonly Family[] Families =
    [
        new("crown", [GovernmentMap.Feudal, GovernmentMap.JapanFeudal, GovernmentMap.Mandala],
            ["baron", "count", "duke", "king", "emperor", "hegemon"],
            ["barony", "county", "duchy", "kingdom", "empire", "hegemony"]),
        new("admin", [GovernmentMap.Administrative, GovernmentMap.Meritocratic, GovernmentMap.SteppeAdmin,
                      GovernmentMap.Celestial, GovernmentMap.JapanAdministrative],
            ["baron", "magistrate", "governor", "viceroy", "emperor", "hegemon"],
            ["barony", "magistracy", "province", "viceroyalty", "empire", "hegemony"]),
        new("clan", [GovernmentMap.Clan],
            ["headman", "chieftain", "high_chieftain", "patriarch", "grand_patriarch", "hegemon"],
            ["tribe", "chiefdom", "high_chiefdom", "clan", "grand_clan", "hegemony"]),
        new("tribal", [GovernmentMap.Tribal, GovernmentMap.Wanua, GovernmentMap.Nomad],
            ["headman", "chieftain", "high_chieftain", "king", "high_king", "hegemon"],
            ["tribe", "chiefdom", "high_chiefdom", "kingdom", "high_kingdom", "hegemony"]),
        new("theocracy", [GovernmentMap.Theocracy],
            ["priest", "high_priest", "archpriest", "grand_priest", "divine_emperor", "hegemon"],
            ["temple", "great_temple", "theocracy", "grand_theocracy", "holy_empire", "hegemony"]),
        new("republic", [GovernmentMap.Republic],
            ["mayor", "lord_mayor", "grand_mayor", "high_prince", "grand_prince", "hegemon"],
            ["city", "grand_city", "republic", "serene_republic", "grand_principality", "hegemony"]),
    ];

    public static Family FamilyById(string id) => Families.First(f => f.Id == id);

    /// <summary>
    /// A rank a ruler holds because of their own situation rather than their government: sovereign,
    /// holding a special contract, or holding more than one title of their rank.
    ///
    /// <see cref="Flag"/> variants are decided at runtime by the generated script, which sets a
    /// character flag carrying the top liege's culture; see <c>NativeRankWriter</c>. The others are
    /// independence, which flavorization can test on its own.
    /// </summary>
    public sealed record Variant(string Id, string Tier, string[] Families, string Holder, string? Realm, bool Flag,
        int Priority);

    public static readonly Variant[] Variants =
    [
        // Sovereigns. A crown duke answering to no one is a prince, and an administrative ruler
        // with no emperor over them is no one's governor; vanilla restyles those the same way.
        new("independent", "d", ["crown", "admin"], "prince", "principality", Flag: false, Priority: 770),
        new("independent", "c", ["admin"], "count", "county", Flag: false, Priority: 770),
        new("independent", "k", ["admin"], "king", "kingdom", Flag: false, Priority: 770),

        // Titles held: more than one of your own rank.
        new("grand", "d", ["crown"], "grand_duke", null, Flag: true, Priority: 780),
        new("high", "k", ["crown", "tribal"], "high_king", null, Flag: true, Priority: 780),

        // Special contracts, vanilla's own three, read the way vanilla reads them.
        new("march", "d", ["crown"], "margrave", "march", Flag: true, Priority: 790),
        new("palatine", "d", ["crown"], "palatine", "palatinate", Flag: true, Priority: 790),
        new("palatine_county", "c", ["crown"], "count_palatine", "county_palatine", Flag: true, Priority: 790),
        new("castellan", "c", ["crown"], "castellan", "castellany", Flag: true, Priority: 790),
    ];

    /// <summary>The tiers a converted people's crown borrows from its religion's holy tongue.</summary>
    public static readonly string[] SacredTiers = ["k", "e", "h"];

    // --- Deciding ------------------------------------------------------------------------------

    /// <summary>
    /// Coins every generated culture's words, and every generated religion's words for the crown,
    /// or clears them all when both options are off. Runs once every faith exists, because a
    /// people's home religion is part of what it is decided from.
    /// </summary>
    public static void Assign(CultureMap cultures, FaithMap faiths, MapConfig cfg)
    {
        bool holders = cfg.NativeRankTitles, realms = cfg.NativeRealmNames;

        foreach (var culture in cultures.Cultures) culture.NativeRanks = null;
        foreach (var religion in faiths.Religions) religion.SacredRanks = null;
        if (!holders && !realms) return;

        int coined = 0;
        foreach (var culture in cultures.Cultures)
        {
            if (culture.Inherited || culture.Key == Cultures.UnsettledKey) continue;
            culture.NativeRanks = Coin(culture.Tongue, cfg.Seed, holders, realms, HomeReligion(culture, faiths));
            coined++;
        }

        int lent = 0;
        foreach (var religion in faiths.Religions)
        {
            if (religion.Inherited || religion.Key == Faiths.UnsettledReligionKey) continue;
            religion.SacredRanks = Coin(religion.Language, cfg.Seed, holders, realms, null, SacredCoinages);
            lent++;
        }

        string what = holders && realms ? "titles and realm names" : holders ? "titles" : "realm names";
        Console.WriteLine($"  native ranks: {coined} cultures coined their own {what}; {lent} religions lend theirs to converts");
    }

    /// <summary>The religion most of a culture's own counties follow, ties to the lower key.</summary>
    private static string? HomeReligion(Culture culture, FaithMap faiths)
    {
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var county in culture.Counties)
            if (faiths.ByCounty.TryGetValue(county, out var faith) && !faith.Religion.Inherited)
                votes[faith.Religion.Key] = votes.GetValueOrDefault(faith.Religion.Key) + 1;

        return votes.Count == 0
            ? null
            : votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal).First().Key;
    }

    /// <summary>
    /// The draws a language family's grammar makes, as <see cref="Coin"/> makes them: whether it marks
    /// a feminine, which feminine marker, and which realm ending. For coining further words
    /// (<see cref="Sees.CoinWords"/>) that must agree with the ranks.
    /// </summary>
    public static (bool Gendered, int Marker, int Ending) GrammarOf(Language tongue, int seed)
    {
        var root = tongue;
        while (root.Parent is not null) root = root.Parent;

        var grammar = Rng.For(seed, GrammarStream, Rng.StableHash(root.Key));
        bool gendered = grammar.Chance(0.75);
        int marker = grammar.Int(0, 7);
        int ending = grammar.Int(0, 7);
        return (gendered, marker, ending);
    }

    /// <summary>
    /// One language's words. Titles are coined whether or not they are kept, and before the realm
    /// words, so that neither option can change what the other one produces.
    /// </summary>
    public static NativeRanks Coin(Language tongue, int seed, bool holders, bool realms, string? homeReligion,
        IReadOnlySet<string>? only = null)
    {
        var root = tongue;
        while (root.Parent is not null) root = root.Parent;

        var grammar = Rng.For(seed, GrammarStream, Rng.StableHash(root.Key));
        bool gendered = grammar.Chance(0.75);
        int marker = grammar.Int(0, 7);
        int ending = grammar.Int(0, 7);

        var ranks = new NativeRanks { HomeReligion = homeReligion, Family = root.Key };
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var coinage in HolderCoinages)
        {
            if (only is not null && !only.Contains(coinage.Id)) continue;

            string male = Spend(coinage, minLength: 3);

            // The feminine is spoken for as well, or a later rank could spell the same word — a
            // Finnic duchy came out as its own duchess before it was.
            string female = gendered
                ? tongue.FeminineRank(male, marker, Rng.For(seed, WordStream, Rng.StableHash($"{root.Key}/{coinage.Id}/f")),
                    free: w => !taken.Contains(w))
                : male;
            taken.Add(female);

            if (holders) ranks.Holders[coinage.Id] = new RankWord(male, female);
        }

        foreach (var coinage in RealmCoinages)
        {
            if (only is not null && !only.Contains(coinage.Id)) continue;

            // Four letters at least: a realm ending on a one-syllable root made "Kia" and "Mia".
            string word = Spend(coinage, minLength: 4);
            if (realms) ranks.Realms[coinage.Id] = word;
        }

        return ranks;

        // The first recipe that spells a free word; then any fresh two-syllable word, which is what
        // a language with no usable roots for a rank would borrow or invent anyway.
        string Spend(Coinage coinage, int minLength)
        {
            var rng = Rng.For(seed, WordStream, Rng.StableHash($"{root.Key}/{coinage.Id}"));

            foreach (var recipe in coinage.Recipes)
                if (tongue.RankWord(recipe, ending, rng, minLength: minLength) is { } word && taken.Add(word))
                    return word;

            string fresh = tongue.Word(rng, 2, 2);
            for (int attempt = 0; attempt < 8 && !taken.Add(fresh); attempt++) fresh = tongue.Word(rng, 2, 2);
            return fresh;
        }
    }
}
