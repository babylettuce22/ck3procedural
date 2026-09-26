using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>What a generated faith's icon is made of. See <see cref="FaithIcons"/>.</summary>
public sealed record FaithIconRecipe(
    Faith Faith,
    string Family,
    string Motif,
    string Frame,
    FaithIcons.Tier Tier,
    string Material,
    string? ReformedMaterial,
    string? Engraving = null);

/// <summary>
/// Designs an icon for every generated faith, in the relief style rendered by
/// <see cref="Emit.FaithIconWriter"/>.
///
/// **Religions share a motif; faiths vary within it.** The religion picks a motif family — suns,
/// crescents, crosses, a tree — from the tenets its faiths hold and whether it is Abrahamic-shaped,
/// the way vanilla's Christian faiths are all crosses. No two religions share a family. Each faith
/// then takes a variant of the family, a frame, and a material, and no two faiths of one religion
/// end up with the same combination.
///
/// **Material follows standing.** An unreformed faith is carved in wood, stone, bone or iron; a
/// reformed one is cast in bronze, verdigris, silver or jade; an Abrahamic-shaped faith or one with
/// a head of faith is gold, silver, electrum, obsidian or enamel. The inlay (a boss, a gem, a
/// medallion's field) is enamel in the faith's own map colour, which is what ties the icon to the
/// faith on the map. An unreformed faith also gets a reformed icon: the same design in the
/// material it would be cast in after reformation.
///
/// Everything is keyed off faith and religion keys through <see cref="Rng.StableHash"/>, never off
/// the world seed's stream, so designing icons cannot shift anything else the generator draws.
/// </summary>
public static class FaithIcons
{
    public const string ReformedSuffix = "_reformed";

    public enum Tier { Unreformed, Reformed, Exalted }

    /// <summary>Motif families and the variants a religion's faiths cycle through.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Families = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["sun"] = ["sun", "sun12", "sun_plain"],
        ["estoile"] = ["estoile"],
        ["star"] = ["star8", "star7", "star5_pierced"],
        ["crescent"] = ["crescent_star", "crescent"],
        ["moon"] = ["triple_moon"],
        ["eye"] = ["eye"],
        ["knot"] = ["knot"],
        ["triskele"] = ["triskele"],
        ["tree"] = ["tree"],
        ["antlers"] = ["antlers"],
        ["flame"] = ["flame"],
        ["wheel"] = ["wheel8", "wheel6"],
        ["lotus"] = ["lotus"],
        ["rosette"] = ["rosette8", "rosette12"],
        ["hexagram"] = ["hexagram"],
        ["labrys"] = ["labrys"],
        ["hammer"] = ["hammer"],
        ["trident"] = ["trident"],
        ["key"] = ["key"],
        ["horned_disc"] = ["horned_disc"],
        ["mountain"] = ["mountain"],
        ["ouroboros"] = ["ouroboros"],
        ["cross"] = ["cross_patee", "ringed_cross", "looped_cross"],
    };

    /// <summary>Which families a tenet calls to mind, and how strongly.</summary>
    private static readonly Dictionary<string, (string Family, double Weight)[]> TenetPull = new(StringComparer.Ordinal)
    {
        ["tenet_sun_worship"] = [("sun", 6), ("estoile", 3), ("horned_disc", 3)],
        ["tenet_mountain_worship"] = [("mountain", 7)],
        ["tenet_sanctity_of_nature"] = [("tree", 5), ("antlers", 3), ("lotus", 1)],
        ["tenet_pastoral_isolation"] = [("tree", 2), ("antlers", 2), ("mountain", 1)],
        ["tenet_ancestor_worship"] = [("tree", 3), ("ouroboros", 2), ("knot", 1)],
        ["tenet_household_gods"] = [("flame", 3), ("key", 2)],
        ["tenet_astrology"] = [("star", 5), ("moon", 3), ("crescent", 2), ("estoile", 2)],
        ["tenet_esotericism"] = [("eye", 4), ("ouroboros", 3), ("hexagram", 2)],
        ["tenet_gnosticism"] = [("eye", 3), ("ouroboros", 3), ("star", 1)],
        ["tenet_alexandrian_catechism"] = [("key", 3), ("eye", 2), ("hexagram", 1)],
        ["tenet_sacred_shadows"] = [("moon", 5), ("crescent", 3), ("eye", 2)],
        ["tenet_warmonger"] = [("labrys", 3), ("hammer", 3), ("trident", 2)],
        ["tenet_armed_pilgrimages"] = [("cross", 3), ("labrys", 1), ("trident", 1)],
        ["tenet_fp3_fedayeen"] = [("labrys", 2), ("crescent", 2)],
        ["tenet_struggle_submission"] = [("crescent", 2), ("flame", 2)],
        ["tenet_pursuit_of_power"] = [("hammer", 2), ("labrys", 2), ("horned_disc", 1)],
        ["tenet_ritual_cannibalism"] = [("antlers", 3), ("flame", 2)],
        ["tenet_human_sacrifice"] = [("flame", 3), ("antlers", 2), ("labrys", 1)],
        ["tenet_cranial_trophies"] = [("antlers", 4)],
        ["tenet_sacrificial_ceremonies"] = [("flame", 4), ("labrys", 1)],
        ["tenet_exaltation_of_pain"] = [("flame", 2), ("knot", 1)],
        ["tenet_asceticism"] = [("lotus", 2), ("wheel", 2)],
        ["tenet_monasticism"] = [("lotus", 2), ("wheel", 2), ("key", 1)],
        ["tenet_vows_of_poverty"] = [("wheel", 2), ("lotus", 2)],
        ["tenet_no_mind"] = [("lotus", 3), ("wheel", 2)],
        ["tenet_inner_journey"] = [("lotus", 2), ("eye", 2), ("knot", 1)],
        ["tenet_pure_land"] = [("lotus", 4)],
        ["tenet_extinction_of_dharma"] = [("wheel", 4)],
        ["tenet_bhakti"] = [("lotus", 3), ("rosette", 2)],
        ["tenet_legalism"] = [("key", 3), ("wheel", 1)],
        ["tenet_literalism"] = [("key", 2), ("hexagram", 1)],
        ["tenet_religious_legal_pronouncements"] = [("key", 3)],
        ["tenet_rite"] = [("wheel", 2), ("rosette", 1)],
        ["tenet_benevolent_governance"] = [("wheel", 2), ("key", 1)],
        ["tenet_harmonious_society"] = [("wheel", 2), ("knot", 2), ("rosette", 1)],
        ["tenet_filial_piety"] = [("tree", 2), ("knot", 2)],
        ["tenet_ritual_celebrations"] = [("rosette", 3), ("sun", 1)],
        ["tenet_carnal_exaltation"] = [("rosette", 3), ("horned_disc", 1)],
        ["tenet_hedonistic"] = [("rosette", 2), ("horned_disc", 1)],
        ["tenet_gruesome_festivals"] = [("flame", 2), ("antlers", 1)],
        ["tenet_communal_possessions"] = [("knot", 2), ("key", 1)],
        ["tenet_mendicant_preachers"] = [("cross", 1), ("key", 1)],
        ["tenet_tax_nonbelievers"] = [("key", 1), ("crescent", 1)],
        ["tenet_aniconism"] = [("hexagram", 3), ("star", 2), ("knot", 2)],
        ["tenet_sacred_destruction"] = [("flame", 2), ("hammer", 2)],
        ["tenet_mystical_birthright"] = [("eye", 2), ("star", 1)],
        ["tenet_false_conversion_sanction"] = [("crescent", 1)],
        ["tenet_consolamentum"] = [("cross", 2), ("flame", 1)],
        ["tenet_adaptive"] = [("triskele", 2)],
        ["tenet_sacred_waters"] = [("trident", 4), ("crescent", 1)],
    };

    /// <summary>
    /// What an Azgaar tradition looks like. Only the five forms the Ondrerol export uses (Animism,
    /// Polytheism, Nature Worship, Ancestor Worship, Monotheism) have been seen in real data; the
    /// rest follow Azgaar's own generator vocabulary.
    /// </summary>
    private static readonly Dictionary<string, (string Family, double Weight)[]> FormPull = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Animism"] = [("tree", 3), ("antlers", 2), ("triskele", 2), ("moon", 1)],
        ["Shamanism"] = [("antlers", 3), ("eye", 2), ("moon", 2)],
        ["Totemism"] = [("antlers", 4), ("tree", 1)],
        ["Nature Worship"] = [("tree", 4), ("antlers", 2), ("lotus", 1), ("mountain", 1)],
        ["Ancestor Worship"] = [("tree", 3), ("ouroboros", 2), ("knot", 2)],
        ["Polytheism"] = [("horned_disc", 1.5), ("sun", 1), ("triskele", 1), ("trident", 1), ("rosette", 1)],
        ["Monotheism"] = [("sun", 1.5), ("eye", 1.5), ("star", 1), ("flame", 1)],
        ["Dualism"] = [("moon", 3), ("sun", 2), ("crescent", 2)],
        ["Pantheism"] = [("rosette", 2), ("wheel", 2), ("knot", 2)],
        ["Non-theism"] = [("wheel", 3), ("lotus", 3)],
        ["Cult"] = [("eye", 2), ("ouroboros", 2)],
        ["Dark Cult"] = [("ouroboros", 3), ("eye", 2), ("moon", 2)],
        ["Sect"] = [("key", 2), ("eye", 1)],
        ["Heresy"] = [("flame", 1)],
    };

    /// <summary>
    /// Words in an Azgaar deity's epithet ("The Sad Antelope", "The Secret Maker of Day") that
    /// name a symbol outright. Matched as whole words (see EpithetFamilies). The strongest
    /// pull there is: whoever made the map, or Azgaar for them, has already said what the god is.
    /// </summary>
    private static readonly (string Word, string Family)[] EpithetWords =
    [
        ("sun", "sun"), ("day", "sun"), ("dawn", "sun"), ("light", "sun"), ("bright", "sun"), ("golden", "sun"),
        ("radiant", "sun"), ("shining", "sun"),
        ("moon", "moon"), ("night", "moon"), ("dusk", "moon"), ("twilight", "moon"), ("shadow", "moon"),
        ("maiden", "moon"), ("crone", "moon"), ("crescent", "crescent"),
        ("star", "star"), ("celestial", "star"), ("sky", "estoile"), ("heaven", "estoile"), ("comet", "estoile"),
        ("serpent", "ouroboros"), ("snake", "ouroboros"), ("gorgon", "ouroboros"), ("wyrm", "ouroboros"),
        ("dragon", "ouroboros"), ("drake", "ouroboros"), ("viper", "ouroboros"), ("coil", "ouroboros"),
        ("fire", "flame"), ("flame", "flame"), ("burning", "flame"), ("ember", "flame"), ("ash", "flame"), ("blaze", "flame"),
        ("tree", "tree"), ("forest", "tree"), ("oak", "tree"), ("leaf", "tree"), ("root", "tree"), ("grove", "tree"), ("verdant", "tree"),
        ("stag", "antlers"), ("antelope", "antlers"), ("deer", "antlers"), ("elk", "antlers"), ("hart", "antlers"),
        ("horned", "antlers"), ("beast", "antlers"), ("hunt", "antlers"), ("hunter", "antlers"),
        ("war", "labrys"), ("warrior", "labrys"), ("battle", "labrys"), ("blood", "labrys"), ("conquer", "labrys"), ("slayer", "labrys"),
        ("destroyer", "labrys"), ("axe", "labrys"),
        ("hammer", "hammer"), ("smith", "hammer"), ("forge", "hammer"), ("maker", "hammer"), ("craft", "hammer"), ("thunder", "hammer"),
        ("sea", "trident"), ("water", "trident"), ("wave", "trident"), ("storm", "trident"), ("rain", "trident"),
        ("tide", "trident"), ("ocean", "trident"), ("river", "trident"), ("fog", "trident"), ("mist", "trident"),
        ("mountain", "mountain"), ("stone", "mountain"), ("earth", "mountain"), ("rock", "mountain"), ("peak", "mountain"),
        ("eye", "eye"), ("blind", "eye"), ("seer", "eye"), ("secret", "eye"), ("hidden", "eye"), ("wisdom", "eye"),
        ("watcher", "eye"), ("truth", "eye"),
        ("judge", "key"), ("law", "key"), ("keeper", "key"), ("gate", "key"),
        ("fate", "wheel"), ("time", "wheel"), ("wheel", "wheel"), ("cycle", "wheel"),
        ("bond", "knot"), ("weaver", "knot"), ("thread", "knot"),
        ("bull", "horned_disc"), ("mother", "horned_disc"), ("fertil", "horned_disc"),
        ("flower", "rosette"), ("bloom", "rosette"), ("rose", "rosette"), ("feast", "rosette"), ("joy", "rosette"),
        ("lotus", "lotus"), ("peace", "lotus"), ("serene", "lotus"), ("pure", "lotus"),
    ];

    private static readonly (string, double)[] AbrahamicBias =
        [("cross", 5), ("crescent", 4), ("hexagram", 3), ("star", 2), ("key", 1.5), ("eye", 1.5), ("flame", 1)];
    private static readonly (string, double)[] MonotheistPaganBias = [("sun", 1.5), ("eye", 1.5), ("flame", 1)];
    private static readonly (string, double)[] PolytheistPaganBias =
        [("tree", 1), ("triskele", 1.2), ("knot", 1), ("antlers", 1), ("trident", 1), ("moon", 1), ("horned_disc", 1)];

    private static readonly Dictionary<Tier, string[]> TierMaterials = new()
    {
        [Tier.Unreformed] = ["wood", "stone", "bone", "iron"],
        [Tier.Reformed] = ["bronze", "verdigris", "silver", "jade", "iron"],
        [Tier.Exalted] = ["gold", "silver", "enamel", "electrum", "obsidian"],
    };

    /// <summary>What a material looks like from across the faith list, so one religion does not show four greys.</summary>
    private static readonly Dictionary<string, string> ColourGroup = new(StringComparer.Ordinal)
    {
        ["wood"] = "brown", ["bronze"] = "brown", ["gold"] = "yellow", ["silver"] = "grey", ["iron"] = "grey",
        ["stone"] = "grey", ["bone"] = "cream", ["verdigris"] = "green", ["jade"] = "green", ["enamel"] = "colour",
        ["electrum"] = "pale", ["obsidian"] = "black",
    };

    /// <summary>
    /// The engravings cut into an enamelled frame field (see <see cref="Emit.Relief.ReliefMotifs.Engraving"/>),
    /// and the frames that have such a field.
    /// </summary>
    public static readonly string[] Engravings = ["Sunburst", "Diaper", "Spirograph"];
    private static readonly HashSet<string> FramesWithField = new(StringComparer.Ordinal) { "medallion", "lobed" };

    /// <summary>
    /// A framed faith's engraving: one its religion has not used yet where one is left, so sibling
    /// faiths in the same frame still tell apart. From a stream of its own, so adding it moved none
    /// of the material draws that come off the faith's icon stream.
    /// </summary>
    private static string PickEngraving(int seed, Faith faith, List<string> usedInReligion)
    {
        var order = Engravings.ToList();
        Seeded(seed, faith.Key, "engraving").Shuffle(order);
        string pick = order.FirstOrDefault(e => !usedInReligion.Contains(e)) ?? order[0];
        usedInReligion.Add(pick);
        return pick;
    }

    /// <summary>Half of all flagships go unframed; the rest of the faiths draw frames from this bag.</summary>
    private static readonly (string Frame, int Weight)[] FrameBag =
        [("none", 5), ("ring", 2), ("medallion", 2), ("lobed", 1), ("rayed", 1)];

    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Points every generated faith at its own icon. Vanilla faiths (Content Source VanillaWorld)
    /// keep theirs. The icon is only rendered while the key still points here, so choosing a
    /// vanilla icon in the Faith inspector simply wins.
    /// </summary>
    public static void Claim(FaithMap faiths)
    {
        foreach (var faith in faiths.Faiths)
        {
            if (faith.Inherited) continue;
            faith.Icon = faith.Key;
            faith.ReformedIcon = faith.Key + ReformedSuffix;
        }
    }

    public static bool HasGeneratedIcon(Faith faith) => !faith.Inherited && faith.Icon == faith.Key;

    /// <param name="seed">The world's seed, which every draw here is salted with.</param>
    public static List<FaithIconRecipe> Recipes(FaithMap faiths, int seed)
    {
        var religions = faiths.Religions
            .Where(r => !r.Inherited && r.Faiths.Any(f => !f.Inherited))
            .ToList();
        var family = AssignFamilies(religions, seed);

        var result = new List<FaithIconRecipe>();
        foreach (var religion in religions)
        {
            var members = religion.Faiths.Where(f => !f.Inherited).ToList();
            string fam = family[religion];
            var variants = Families[fam];

            var bag = FrameBag.SelectMany(f => Enumerable.Repeat(f.Frame, f.Weight)).ToList();
            Seeded(seed, religion.Key, "faiths").Shuffle(bag);

            var used = new HashSet<(string, string, string)>();
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var reformedGroups = new HashSet<string>(StringComparer.Ordinal);
            var engravingsUsed = new List<string>();

            // The faith with a head (else the first) carries the plain, unframed flagship design.
            var flagship = members.FirstOrDefault(HasHead) ?? members[0];

            for (int i = 0; i < members.Count; i++)
            {
                var faith = members[i];
                var rng = Seeded(seed, faith.Key, "icon");
                string variant = variants[i % variants.Length];
                string frame = faith == flagship ? "none" : bag[i % bag.Count];
                var tier = TierOf(religion, faith);
                bool pale = Saturation(faith.Color) < 0.25;

                string material = Pick(rng, Options(tier, pale), variant, frame, used, groups);
                string? reformed = tier == Tier.Unreformed
                    ? Pick(rng, Options(HasHeadOnceOrganised(faith) ? Tier.Exalted : Tier.Reformed, pale),
                        variant, frame, used, reformedGroups)
                    : null;

                string? engraving = FramesWithField.Contains(frame) ? PickEngraving(seed, faith, engravingsUsed) : null;

                result.Add(new FaithIconRecipe(faith, fam, variant, frame, tier, material, reformed, engraving));
            }
        }
        return result;
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>A stream keyed by name and the world's seed: keys like <c>gen_religion_0</c> repeat on every seed.</summary>
    private static Rng Seeded(int seed, params string[] parts) => Rng.For(seed, 0, Rng.StableHash(string.Join("|", parts)));

    private static bool HasHead(Faith f) => f.Head is not null && f.IsOrganized;

    /// <summary>A head is only written for an organised faith, but an unreformed one can already hold the title.</summary>
    private static bool HasHeadOnceOrganised(Faith f) => f.Head is not null;

    private static Tier TierOf(Religion religion, Faith faith)
    {
        if (!faith.IsOrganized) return Tier.Unreformed;
        return religion.Abrahamic || HasHead(faith) ? Tier.Exalted : Tier.Reformed;
    }

    private static List<string> Options(Tier tier, bool pale)
        => TierMaterials[tier].Where(m => !(pale && m == "enamel")).ToList();

    /// <summary>
    /// A material for one faith: prefer a colour group this religion has not shown yet, and never
    /// repeat a design (variant, frame and material all the same) within the religion.
    /// </summary>
    private static string Pick(Rng rng, List<string> options, string variant, string frame,
        HashSet<(string, string, string)> used, HashSet<string> groupsUsed)
    {
        rng.Shuffle(options);
        foreach (var m in options.OrderBy(m => groupsUsed.Contains(ColourGroup[m]) ? 1 : 0))
        {
            if (!used.Add((variant, frame, m))) continue;
            groupsUsed.Add(ColourGroup[m]);
            return m;
        }
        return options[0];
    }

    private static double Saturation((double R, double G, double B) c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max <= 0 ? 0 : (max - min) / max;
    }

    private static Dictionary<string, double> Scores(Religion religion)
    {
        var score = Families.Keys.ToDictionary(f => f, _ => 0.3, StringComparer.Ordinal);
        var bias = religion.Abrahamic ? AbrahamicBias : religion.Monotheist ? MonotheistPaganBias : PolytheistPaganBias;
        foreach (var (fam, w) in bias) score[fam] += w;

        var members = religion.Faiths.Where(f => !f.Inherited).ToList();
        int n = Math.Max(members.Count, 1);
        foreach (var faith in members)
            foreach (var tenet in faith.Tenets)
                if (TenetPull.TryGetValue(tenet, out var pulls))
                    foreach (var (fam, w) in pulls) score[fam] += w / n * 1.5;

        // An Azgaar religion's tradition, and above all its god's epithet.
        if (religion.SourceForm is { } form && FormPull.TryGetValue(form, out var formPulls))
            foreach (var (fam, w) in formPulls) score[fam] += w;
        // The epithet is a fact about the map; tenets are this generator's own dice. At 12 it
        // outranks any tenet pull (a single religion-wide tenet tops out near 10), so a god named
        // "The Sad Antelope" keeps its antlers against a neighbour whose rolled tenets want them.
        foreach (string fam in EpithetFamilies(religion.SourceDeity)) score[fam] += 12;

        // A cross on a pagan faith reads as Christianity whatever the tenets say.
        if (!religion.Abrahamic) score["cross"] *= 0.15;
        return score;
    }

    /// <summary>The families the epithet after the deity's name calls for, each at most once.</summary>
    private static IEnumerable<string> EpithetFamilies(string? deity)
    {
        if (deity is null) return [];
        int comma = deity.IndexOf(',');
        if (comma < 0) return [];

        // The epithet only: the proper name is generated syllables and can spell anything.
        var words = System.Text.RegularExpressions.Regex.Matches(deity[(comma + 1)..].ToLowerInvariant(), @"\p{L}+")
                                                      .Select(m => m.Value).ToList();
        return EpithetWords.Where(e => words.Any(w => Names(w, e.Word)))
                           .Select(e => e.Family)
                           .Distinct();

        // Short keys match whole words and plurals only ("sea" is not "season", "war" is not
        // "ward"); from five letters up a prefix is safe and catches "conqueror", "fertility".
        static bool Names(string word, string key)
            => word == key || word == key + "s" || word == key + "es"
               || (key.Length >= 5 && word.StartsWith(key, StringComparison.Ordinal));
    }

    /// <summary>
    /// The religion with the strongest single pull chooses first, among its three best remaining
    /// families weighted by the fourth power of their score; a family once taken is gone.
    ///
    /// The fourth power, not the square: close calls still vary, but a decisive signal wins. With
    /// the square an Azgaar god named "The Sad Antelope" drew a rosette about one time in four, and
    /// an early religion taking a family by chance left the one that clearly deserved it without.
    /// </summary>
    private static Dictionary<Religion, string> AssignFamilies(List<Religion> religions, int seed)
    {
        var scores = religions.ToDictionary(r => r, Scores);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<Religion, string>();

        foreach (var religion in religions.OrderByDescending(r => scores[r].Values.Max()))
        {
            var candidates = scores[religion]
                .Where(kv => !taken.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(3)
                .ToList();

            // More religions than families: reuse the best-scoring one rather than fail.
            if (candidates.Count == 0)
                candidates = scores[religion].OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(1).ToList();

            var rng = Seeded(seed, religion.Key, "family");
            static double Weight(double w) => w * w * w * w;
            double total = candidates.Sum(c => Weight(c.Value));
            double roll = rng.NextDouble() * total;
            string chosen = candidates[^1].Key;
            foreach (var (fam, w) in candidates)
            {
                roll -= Weight(w);
                if (roll < 0) { chosen = fam; break; }
            }

            taken.Add(chosen);
            result[religion] = chosen;
        }
        return result;
    }
}
