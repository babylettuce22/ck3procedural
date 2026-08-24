using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// A way of making war. Not a unit — a unit is what a <see cref="Doctrine"/> becomes once a
/// particular people on particular ground has adopted it.
///
/// The list is deliberately about *ideas* rather than about CK3 archetypes, and several doctrines
/// share an archetype: shield-walls, mountain wardens and household guards are all heavy infantry
/// and none of the three fights like the others. Going straight from terrain to archetype was the
/// obvious design and it produces nine indistinguishable heavy infantry regiments on any map with
/// a lot of forest, because the archetype is the *outcome* of a military idea and not the idea.
/// </summary>
public enum Doctrine
{
    /// <summary>Locked shields, held ground. Cold forest and hill country, tribal and clan.</summary>
    ShieldWall,

    /// <summary>A hedge of long spears. Broken upland ground, settled and communal.</summary>
    PikeHedge,

    /// <summary>Massed bows drilled to volley. Hills and forest, patient and ordered.</summary>
    MassedArchery,

    /// <summary>Loose order, javelins and knives, no line to break. Jungle, marsh, poor ground.</summary>
    Skirmish,

    /// <summary>Bow from the saddle. Steppe and dryland, herders and horse peoples.</summary>
    HorseArchery,

    /// <summary>Fast riders for the chase and the raid. Open plains and grass.</summary>
    Outriders,

    /// <summary>Armoured lance. Farmland and wealth, courts that can afford horses.</summary>
    Lancers,

    /// <summary>The camel, which the horse will not stand beside. Desert and drylands.</summary>
    CamelCorps,

    /// <summary>Wardens of the passes. Mountains, thin air, stone.</summary>
    MountainWardens,

    /// <summary>Fen-runners who fight where a column cannot form. Wetland and floodplain.</summary>
    MarshRunners,

    /// <summary>The household under arms, paid and permanent. Rich, courtly, settled.</summary>
    HouseGuard,

    /// <summary>Coast-raiders who fight off the beach. Shorelines and islands.</summary>
    Strandwardens,

    /// <summary>War beasts. Jungle, and enough wealth to feed them.</summary>
    WarBeasts,
}

/// <summary>
/// One generated men-at-arms regiment: everything <c>common/men_at_arms_types</c> needs, already
/// decided. <see cref="Emit.RetinueWriter"/> only punctuates it.
/// </summary>
public sealed class Regiment
{
    /// <summary>Frozen. The MaA key, and the localisation key its name is written under.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    /// <summary>The <c>_flavor</c> line — one sentence of prose, already assembled and escaped.</summary>
    public required string Flavor { get; set; }

    public required Doctrine Doctrine { get; init; }

    /// <summary>The CK3 <c>type</c>: heavy_infantry, archers, light_cavalry and so on.</summary>
    public required string Archetype { get; init; }

    /// <summary>The people it belongs to. For a heritage regiment this is the heritage's largest
    /// culture, which is whose language named it.</summary>
    public required Culture Culture { get; init; }

    /// <summary>Set on a heritage regiment, null on a culture's own elite.</summary>
    public Heritage? Heritage { get; init; }

    public bool IsElite => Heritage is null;

    public int Damage { get; set; }
    public int Toughness { get; set; }
    public int Pursuit { get; set; }
    public int Screen { get; set; }
    public double SiegeValue { get; set; }
    public int Stack { get; set; } = 100;

    public int BuyCost { get; set; }
    public double LowMaintenance { get; set; }
    public double HighMaintenance { get; set; }
    public int ProvisionCost { get; set; }
    public int AiQuality { get; set; }

    /// <summary>CK3 terrain id to the stats it adds there.</summary>
    public Dictionary<string, Dictionary<string, int>> TerrainBonus { get; } = new(StringComparer.Ordinal);

    /// <summary>Stats the regiment gains or loses in a normal winter, or empty for one the
    /// archetype and the doctrine both have nothing to say about.</summary>
    public Dictionary<string, int> WinterNormal { get; } = new(StringComparer.Ordinal);

    /// <summary>The same in a harsh one. Written separately because vanilla repeats its winter
    /// *bonuses* verbatim across the two and roughly doubles its winter *penalties*.</summary>
    public Dictionary<string, int> WinterHarsh { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> Counters { get; } = new(StringComparer.Ordinal);

    public string? Icon { get; set; }
    public string? Illustration { get; set; }

    /// <summary>The innovation that unlocks it, or null when a heritage pillar does instead.</summary>
    public Innovation? Unlock { get; set; }

    /// <summary>The ground this regiment was shaped by, for the report and the flavour line.</summary>
    public TerrainClass HomeTerrain { get; set; }
}

/// <summary>The finished military geography: who fields what, and what unlocks it.</summary>
public sealed class RetinueMap
{
    public List<Regiment> Regiments { get; } = [];

    /// <summary>The regiment every culture of a heritage may raise.</summary>
    public Dictionary<Heritage, Regiment> ByHeritage { get; } = [];

    /// <summary>The elite regiment a culture earned, for those that earned one.</summary>
    public Dictionary<Culture, Regiment> Elite { get; } = [];

    /// <summary>The innovations invented for this roster, for the writer and for culture history.</summary>
    public required InnovationMap Innovations { get; init; }

    /// <summary>What a culture can raise beyond vanilla's generic roster, best first.</summary>
    public IEnumerable<Regiment> For(Culture culture)
    {
        if (Elite.TryGetValue(culture, out var elite)) yield return elite;
        if (ByHeritage.TryGetValue(culture.Heritage, out var levy)) yield return levy;
    }
}

/// <summary>
/// Grows a men-at-arms roster out of the world the rest of the pipeline has already decided.
///
/// **Nothing here invents a number.** Every stat, price and counter is read off
/// <see cref="VanillaVocabulary.MaaArchetypes"/> — what the installed game's own regiments of that
/// archetype are worth — and then bent by a doctrine and renormalised back onto the same power
/// budget. So a generated regiment is, by construction, a rearrangement of a vanilla one rather
/// than a guess at what a vanilla one is like, and a balance patch to CK3 moves the generated
/// roster with it. See <see cref="Power"/> for the one thing that *is* decided here.
///
/// **Two tiers, for the same reason vanilla has two.** Every heritage fields one regiment its
/// cultures can all raise from the first day — that is the people's way of war, and it is gated on
/// the heritage pillar so a culture cannot lose it. On top of that, a culture that has earned one
/// — by temperament, by wealth, or by holding a martial tradition — gets an elite regiment behind
/// a generated innovation, so there is something in the military tree left to discover.
/// </summary>
public static class Retinues
{
    /// <summary>
    /// What a regiment is worth, on one scale, so that two builds of the same archetype can be
    /// held to the same budget.
    ///
    /// The one set of numbers in this file that is chosen rather than harvested, and it is chosen
    /// against vanilla's own price list: scoring its eight priced archetypes this way and dividing
    /// by their recruitment costs gives 0.94 to 1.35 gold per point across the whole roster, from
    /// skirmishers to heavy cavalry. A weighting that did not track price would let a doctrine
    /// trade a stat CK3 charges for against one it does not.
    /// </summary>
    public static double Power(double damage, double toughness, double pursuit, double screen)
        => damage + toughness * 1.2 + pursuit * 0.35 + screen * 0.35;

    /// <summary>
    /// Whether this run is writing a roster of its own, and so whether the routes from a generated
    /// culture to one of vanilla's *named* regiments should be closed.
    ///
    /// There are two such routes and they have to be shut together or not at all. A tradition can
    /// carry an <c>unlock_maa_*</c> parameter (see
    /// <see cref="VanillaVocabulary.TraditionsUnlockingMaa"/>, closed in <see cref="Cultures"/>),
    /// and an innovation can put a named regiment in reach (see
    /// <see cref="VanillaVocabulary.GrantsVanillaRegiment"/>, closed in
    /// <see cref="Emit.CultureWriter"/>). Closing only the first is what this generator did before,
    /// and it left the wider of the two open: culture histories sample sixteen vanilla innovations
    /// apiece, <c>innovation_war_camels</c> is one of them, and <c>camel_rider</c> has no
    /// <c>can_recruit</c> of its own — so a people with no camel tradition anywhere fielded
    /// vanilla's Camel Riders beside the camels this generator had just invented for them.
    ///
    /// Closing either is only safe because the generated roster replaces what it removes, which is
    /// why this is one predicate and not two conditions written out twice.
    /// </summary>
    public static bool ReplacesVanillaRosters(VanillaVocabulary vocab, MapConfig cfg)
        => cfg.EnableGeneratedRetinues && vocab.MaaArchetypes.Count > 0;

    /// <summary>
    /// A doctrine's whole definition: what it is made of, who reaches for it, and what to call it.
    /// </summary>
    /// <param name="Skew">Multipliers on the archetype's mean stat line, before renormalising back
    /// onto the power budget. This is the doctrine's whole mechanical identity — a shield-wall and
    /// a household guard are the same archetype and the same budget, spent differently.</param>
    /// <param name="Ground">Terrain that suggests it. Also what its terrain bonus is written for.</param>
    /// <param name="DevelopmentPull">-1 for a doctrine of poor country, +1 for one that needs a
    /// treasury. Scored against the culture's own mean development.</param>
    /// <param name="EliteOnly">Barred from the heritage tier.
    ///
    /// The heritage regiment is what a people can raise from the first day and never lose, so it
    /// has to be something a people can actually always raise. Barded destriers and war elephants
    /// are neither: vanilla puts both behind innovations, and a heritage-wide elephant regiment
    /// measured 255 damage available to every ruler of that people on turn one. Both are far
    /// better as the thing a rich culture has earned.
    ///
    /// Horse archery is here on the same measurement rather than on the same intuition. Scoring
    /// the install's archetypes on <see cref="Power"/> puts archer cavalry at 116 against heavy
    /// infantry's 78 and light cavalry's 71 — it belongs with heavy cavalry at 152, not with the
    /// free tier — and vanilla agrees by construction: every horse-archer regiment it ships is
    /// gated behind an innovation or a tradition, and the horse a people gets for nothing is
    /// light_horsemen. <see cref="Doctrine.Outriders"/> is that unit, and it is what a steppe
    /// people now fields for free while the bow from the saddle is what they work towards.</param>
    /// <param name="MinDevelopment">Mean development the culture must actually reach, as opposed to
    /// the soft pull of <paramref name="DevelopmentPull"/>.
    ///
    /// A pull alone was not enough. Terrain is worth up to three points of score and wealth at most
    /// two, so on a jungle-heavy map a dirt-poor tribe out-scored every other doctrine and fielded
    /// war elephants — on all four test seeds, which is a signature of the generator rather than of
    /// the world. Some doctrines are not a preference at all: you keep elephants or you do not.</param>
    private sealed record Profile(
        Doctrine Doctrine,
        string Archetype,
        (double Damage, double Toughness, double Pursuit, double Screen) Skew,
        TerrainClass[] Ground,
        string[] Ethos,
        string[] Governments,
        double DevelopmentPull,
        string[] Nouns,
        string Idea,
        string[] Icons,
        RaceArchetype[] Races,
        bool EliteOnly = false,
        double MinDevelopment = 0);

    /// <summary>
    /// The doctrines, and what pulls a people towards each.
    ///
    /// Icons are listed in preference order and filtered against what the install actually has, so
    /// a doctrine whose first choice shipped with a DLC the player does not own falls back rather
    /// than pointing at a missing texture. Every list ends in a base-game icon.
    /// </summary>
    private static readonly Profile[] Profiles =
    [
        new(Doctrine.ShieldWall, "heavy_infantry", (0.95, 1.25, 0.5, 1.9),
            [TerrainClass.Forest, TerrainClass.Taiga, TerrainClass.Hills, TerrainClass.Arctic],
            ["ethos_bellicose", "ethos_stoic"],
            [GovernmentMap.Tribal, GovernmentMap.Clan],
            -0.25,
            ["Shields", "Wall", "Shieldmen", "Housecarls", "Sworn"],
            "They fight shoulder to shoulder and do not give ground.",
            ["danish_huskarls", "heavy_infantry"],
            [RaceArchetype.Dwarf, RaceArchetype.Giantkin, RaceArchetype.Orc]),

        new(Doctrine.PikeHedge, "pikemen", (1.2, 1.15, 0.4, 0.9),
            [TerrainClass.Hills, TerrainClass.Mountains, TerrainClass.Plains, TerrainClass.Farmlands],
            ["ethos_communal", "ethos_egalitarian"],
            [GovernmentMap.Republic, GovernmentMap.Feudal],
            0.4,
            ["Spears", "Pikes", "Hedge", "Longspears"],
            "Their spear-hedge is drilled until it moves as one body.",
            ["pikemen", "pikemen_militia"],
            [RaceArchetype.Dwarf, RaceArchetype.Gnome]),

        new(Doctrine.MassedArchery, "archers", (1.25, 0.85, 0.6, 0.9),
            [TerrainClass.Hills, TerrainClass.Forest, TerrainClass.Taiga, TerrainClass.Drylands],
            ["ethos_stoic", "ethos_bureaucratic"],
            [GovernmentMap.Feudal, GovernmentMap.Administrative],
            0.15,
            ["Bows", "Archers", "Bowmen", "Volley"],
            "Every household owes the levy a bow and a season's practice with it.",
            ["armenian_archers", "bowmen"],
            [RaceArchetype.HighElf, RaceArchetype.WoodElf]),

        new(Doctrine.Skirmish, "skirmishers", (1.35, 0.75, 1.5, 0.9),
            [TerrainClass.Jungle, TerrainClass.Forest, TerrainClass.Drylands, TerrainClass.Steppe],
            ["ethos_bellicose", "ethos_egalitarian"],
            [GovernmentMap.Tribal, GovernmentMap.Clan],
            -0.5,
            ["Runners", "Raiders", "Javelins", "Hunters"],
            "They never form a line, and there is nothing to break.",
            ["skirmishers"],
            [RaceArchetype.Orc, RaceArchetype.WoodElf, RaceArchetype.Gnome]),

        new(Doctrine.HorseArchery, "archer_cavalry", (1.1, 0.8, 1.3, 1.0),
            [TerrainClass.Steppe, TerrainClass.Drylands, TerrainClass.Plains],
            ["ethos_bellicose", "ethos_stoic"],
            [GovernmentMap.Nomad, GovernmentMap.Clan],
            -0.1,
            ["Riders", "Horsebows", "Outriders", "Quivers"],
            "They shoot at the gallop and are gone before the answer comes.",
            ["horse_archers", "steppe_raiders", "light_cavalry"],
            [RaceArchetype.Orc], EliteOnly: true),

        new(Doctrine.Outriders, "light_cavalry", (1.0, 0.85, 1.35, 1.15),
            [TerrainClass.Plains, TerrainClass.Steppe, TerrainClass.Farmlands, TerrainClass.Floodplains],
            ["ethos_bellicose", "ethos_courtly"],
            [GovernmentMap.Clan, GovernmentMap.Nomad, GovernmentMap.Feudal],
            0.0,
            ["Horse", "Riders", "Lances", "Scouts"],
            "Light horse for the pursuit, the raid and the road.",
            ["nomadic_riders", "light_cavalry"],
            []),

        new(Doctrine.Lancers, "heavy_cavalry", (1.1, 1.2, 0.85, 0.8),
            [TerrainClass.Farmlands, TerrainClass.Plains, TerrainClass.Floodplains],
            ["ethos_courtly", "ethos_bellicose"],
            [GovernmentMap.Feudal, GovernmentMap.Administrative],
            0.75,
            ["Lances", "Horse", "Riders", "Barded"],
            "Armoured horse, and the estates it takes to keep them shod.",
            ["conrois", "heavy_cavalry_western", "heavy_cavalry"],
            [RaceArchetype.HighElf], EliteOnly: true, MinDevelopment: 9),

        new(Doctrine.CamelCorps, "camel_cavalry", (1.15, 1.0, 1.1, 1.05),
            [TerrainClass.Desert, TerrainClass.Drylands, TerrainClass.DesertMountains, TerrainClass.Oasis],
            ["ethos_bellicose", "ethos_communal"],
            [GovernmentMap.Clan, GovernmentMap.Nomad],
            -0.1,
            ["Camels", "Riders", "Sand", "Dromedars"],
            "Horses will not stand against camels, and the sand is no obstacle to them.",
            ["camel_riders", "light_cavalry"],
            []),

        new(Doctrine.MountainWardens, "heavy_infantry", (1.15, 1.4, 0.7, 0.85),
            [TerrainClass.Mountains, TerrainClass.DesertMountains, TerrainClass.Hills],
            ["ethos_stoic", "ethos_communal"],
            [GovernmentMap.Tribal, GovernmentMap.Feudal],
            -0.1,
            ["Wardens", "Stone", "Watch", "Highlanders"],
            "They hold passes a hundred men could not force.",
            ["mountaineer", "heavy_infantry"],
            [RaceArchetype.Dwarf, RaceArchetype.Deepkin, RaceArchetype.Giantkin]),

        new(Doctrine.MarshRunners, "skirmishers", (1.1, 0.95, 1.35, 1.25),
            [TerrainClass.Wetlands, TerrainClass.Floodplains, TerrainClass.Jungle],
            ["ethos_egalitarian", "ethos_communal"],
            [GovernmentMap.Tribal, GovernmentMap.Republic],
            -0.2,
            ["Fenmen", "Runners", "Reeds", "Waders"],
            "They know which ground will hold a man and which will swallow him.",
            ["skirmishers"],
            [RaceArchetype.WoodElf, RaceArchetype.Gnome]),

        new(Doctrine.HouseGuard, "heavy_infantry", (1.3, 1.15, 0.8, 1.0),
            [TerrainClass.Farmlands, TerrainClass.Plains, TerrainClass.Floodplains, TerrainClass.Oasis],
            ["ethos_courtly", "ethos_bureaucratic"],
            [GovernmentMap.Administrative, GovernmentMap.Feudal, GovernmentMap.Republic],
            0.9,
            ["Guard", "Household", "Watch", "Chosen"],
            "Paid in coin and kept under arms the whole year round.",
            ["palace_guards", "varangian_veterans", "heavy_infantry"],
            [RaceArchetype.HighElf, RaceArchetype.Human], MinDevelopment: 10),

        new(Doctrine.Strandwardens, "heavy_infantry", (1.25, 1.0, 1.1, 1.15),
            [TerrainClass.Beach, TerrainClass.Wetlands, TerrainClass.Taiga],
            ["ethos_bellicose", "ethos_egalitarian"],
            [GovernmentMap.Tribal, GovernmentMap.Clan, GovernmentMap.Republic],
            -0.15,
            ["Strand", "Oars", "Shorewards", "Tide"],
            "They come off the water already in line and take the beach before the alarm is up.",
            ["jomsviking_pirates", "bondi", "heavy_infantry"],
            [RaceArchetype.Orc, RaceArchetype.Human]),

        new(Doctrine.WarBeasts, "elephant_cavalry", (1.0, 1.1, 0.9, 1.0),
            [TerrainClass.Jungle, TerrainClass.Floodplains, TerrainClass.Wetlands],
            ["ethos_courtly", "ethos_spiritual"],
            [GovernmentMap.Feudal, GovernmentMap.Administrative],
            0.85,
            ["Tuskers", "Beasts", "Towers", "Trumpets"],
            "The beasts are worth a wing of horse and cost a county to feed.",
            ["ballista_elephant"],
            [RaceArchetype.Giantkin], EliteOnly: true, MinDevelopment: 13),
    ];

    /// <summary>Plain English for the flavour line. <see cref="TerrainClass.Name"/> gives CK3 ids.</summary>
    private static string Ground(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Beach => "the shoreline",
        TerrainClass.Plains => "open country",
        TerrainClass.Farmlands => "tilled country",
        TerrainClass.Steppe => "the grass",
        TerrainClass.Drylands => "dry country",
        TerrainClass.Desert => "the desert",
        TerrainClass.Jungle => "the deep forest",
        TerrainClass.Forest => "the woods",
        TerrainClass.Taiga => "the cold woods",
        TerrainClass.Wetlands => "the marshes",
        TerrainClass.Floodplains => "the river flats",
        TerrainClass.Hills => "broken hill country",
        TerrainClass.Mountains => "the high passes",
        TerrainClass.DesertMountains => "the bare ranges",
        TerrainClass.Arctic => "the frozen north",
        TerrainClass.Oasis => "the watered places",
        _ => "their own country",
    };

    /// <summary>Terrain that argues for a winter bonus rather than a terrain one.</summary>
    private static bool IsCold(TerrainClass terrain)
        => terrain is TerrainClass.Taiga or TerrainClass.Arctic;

    public static RetinueMap Build(CultureMap cultures, GovernmentMap governments,
        TerrainClass[] provinceTerrain, VanillaVocabulary vocab, MapConfig cfg, Rng rng)
    {
        var map = new RetinueMap { Innovations = new InnovationMap() };

        // Nothing to build against. Happens on a stub install and, more usefully, would happen on
        // a future patch that moved the files — better a roster of vanilla units than a crash.
        if (vocab.MaaArchetypes.Count == 0)
        {
            Console.WriteLine("  retinues: SKIPPED (no men-at-arms data in the game folder)");
            return map;
        }

        var usable = Profiles.Where(p => vocab.MaaArchetypes.ContainsKey(p.Archetype)).ToList();
        if (usable.Count == 0) return map;

        string era = Innovations.EraAt(cfg.EraYear);

        // Terrain per culture once, since both passes want it and a heritage's is the sum of its
        // cultures'. Counted in baronies rather than counties, which is the grain terrain is
        // actually painted at.
        var terrainByCulture = new Dictionary<Culture, Dictionary<TerrainClass, int>>();

        foreach (var culture in cultures.Cultures)
        {
            var counts = new Dictionary<TerrainClass, int>();

            foreach (var county in culture.Counties)
                foreach (var barony in county.Children)
                {
                    int id = barony.ProvinceId;
                    if (id <= 0 || id >= provinceTerrain.Length) continue;
                    counts[provinceTerrain[id]] = counts.GetValueOrDefault(provinceTerrain[id]) + 1;
                }

            terrainByCulture[culture] = counts;
        }

        // The government a culture mostly lives under. Read per county rather than per realm: a
        // culture split between a khanate and a kingdom should read as neither exclusively, and
        // the majority is what its way of war would follow.
        var governmentByCulture = new Dictionary<Culture, string>();

        foreach (var culture in cultures.Cultures)
        {
            var votes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var county in culture.Counties)
            {
                string g = governments.For(county);
                votes[g] = votes.GetValueOrDefault(g) + 1;
            }

            governmentByCulture[culture] = votes.Count == 0
                ? GovernmentMap.Feudal
                : votes.OrderByDescending(kv => kv.Value)
                       .ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
        }

        // What the map already fields, so the second heritage to sit on jungle does not arrive at
        // the first one's answer. Counted across both passes, since a heritage's regiment and a
        // cousin culture's elite are equally visible to a player reading the roster.
        var worldwide = (Doctrines: new Dictionary<Doctrine, int>(),
                         Archetypes: new Dictionary<string, int>(StringComparer.Ordinal));

        void Tally(Regiment regiment)
        {
            worldwide.Doctrines[regiment.Doctrine] =
                worldwide.Doctrines.GetValueOrDefault(regiment.Doctrine) + 1;
            worldwide.Archetypes[regiment.Archetype] =
                worldwide.Archetypes.GetValueOrDefault(regiment.Archetype) + 1;
        }

        // Pass one: the heritage regiment, one per people.
        for (int h = 0; h < cultures.Heritages.Count; h++)
        {
            var heritage = cultures.Heritages[h];
            var members = heritage.Cultures.Where(c => c.Key != Cultures.UnsettledKey).ToList();
            if (members.Count == 0) continue;

            var terrain = new Dictionary<TerrainClass, int>();
            foreach (var culture in members)
                foreach (var (t, n) in terrainByCulture[culture])
                    terrain[t] = terrain.GetValueOrDefault(t) + n;

            if (terrain.Count == 0) continue;

            // The heritage's largest culture speaks for it: the regiment is named in that
            // language, and its temperament is the one most of the people actually hold.
            var lead = members.OrderByDescending(c => c.Counties.Count)
                              .ThenBy(c => c.Key, StringComparer.Ordinal).First();

            // Weighted by counties, so a heritage's wealth is the wealth of most of its people
            // rather than the average of its cultures however small.
            int held = Math.Max(1, members.Sum(c => c.Counties.Count));
            double wealth = members.Sum(c => c.MeanDevelopment * c.Counties.Count) / held;

            var profile = Best(usable, terrain, lead, wealth, governmentByCulture[lead],
                               [], incumbent: null, floor: 0, elite: false, worldwide, vocab, rng);

            var regiment = Compose($"gen_maa_h{h}", profile, lead, heritage, terrain,
                                   vocab, rng, elite: false);

            map.Regiments.Add(regiment);
            map.ByHeritage[heritage] = regiment;
            Tally(regiment);
        }

        // Pass two: the elite, for the cultures that earned one.
        int earned = 0;

        for (int c = 0; c < cultures.Cultures.Count; c++)
        {
            var culture = cultures.Cultures[c];
            if (culture.Key == Cultures.UnsettledKey) continue;
            if (!Earns(culture, vocab)) continue;

            var terrain = terrainByCulture[culture];
            if (terrain.Count == 0) continue;

            // Never a doctrine a sibling culture has taken: two of a heritage's four cultures
            // fielding the same idea under two invented names is the failure mode this exists to
            // prevent. Its own heritage's doctrine is a preference rather than a bar — see the
            // `incumbent` note on Best for why the two cannot be the same rule.
            var taken = new HashSet<Doctrine>();
            foreach (var sibling in culture.Heritage.Cultures)
                if (map.Elite.TryGetValue(sibling, out var other)) taken.Add(other.Doctrine);

            map.ByHeritage.TryGetValue(culture.Heritage, out var levy);

            double floor = levy is null
                ? 0
                : Power(levy.Damage, levy.Toughness, levy.Pursuit, levy.Screen);

            var profile = Best(usable, terrain, culture, culture.MeanDevelopment,
                               governmentByCulture[culture], taken, levy?.Doctrine, floor,
                               elite: true, worldwide, vocab, rng);

            var regiment = Compose($"gen_maa_c{c}", profile, culture, null, terrain,
                                   vocab, rng, elite: true, floor);

            var innovation = map.Innovations.Add(new Innovation
            {
                Key = $"innovation_{regiment.Key}",
                Name = regiment.Name,
                Description = regiment.Flavor,
                Era = era,
                Group = "culture_group_military",
                Skill = "martial",
                Icon = InnovationIcon(regiment, vocab, rng),
            });

            // Vanilla's own idiom for a culture-locked innovation, parent clause included: a
            // culture that later hybridises or splits keeps what its ancestors worked out.
            innovation.Potential.Add("OR = {");
            innovation.Potential.Add($"\tthis = culture:{culture.Key}");
            innovation.Potential.Add($"\tany_parent_culture_or_above = {{ this = culture:{culture.Key} }}");
            innovation.Potential.Add("}");
            innovation.UnlockMenAtArms.Add(regiment.Key);

            regiment.Unlock = innovation;

            // Whether they have it *yet*. A martial or wealthy people has already worked it out;
            // for the rest it is the thing their culture head is still reaching for, which is the
            // whole reason the elite tier is an innovation and not a pillar check.
            if (culture.MeanDevelopment >= 8 || culture.Ethos == "ethos_bellicose")
                map.Innovations.GrantAtStart(innovation, culture);

            map.Regiments.Add(regiment);
            map.Elite[culture] = regiment;
            Tally(regiment);
            earned++;
        }

        Report(map, earned, cultures.Cultures.Count(c => c.Key != Cultures.UnsettledKey));
        return map;
    }

    /// <summary>
    /// Whether a culture has any business fielding an elite regiment of its own.
    ///
    /// Three ways in, because there are three reasons a real people ends up with a famous
    /// household troop: it is warlike, it is rich enough to keep one, or it already holds the
    /// martial tradition the unit would grow out of. A culture that is none of the three fights
    /// with its heritage's regiment and vanilla's generic roster, which is a complete answer.
    ///
    /// The <see cref="VanillaVocabulary.TraditionsUnlockingMaa"/> clause reads as the third of
    /// those and is not: this method only runs when
    /// <see cref="ReplacesVanillaRosters"/> holds, which is exactly when
    /// <see cref="Cultures"/> has kept those traditions off generated cultures, so the clause can
    /// only fire on a culture whose traditions were edited by hand in the inspector. It is kept
    /// for that case. The martial-tradition route that actually fires in a generated world is the
    /// name match below, which the filter leaves alone — highland warriors and winter warriors
    /// unlock nothing and survive it.
    /// </summary>
    private static bool Earns(Culture culture, VanillaVocabulary vocab)
        => culture.Ethos == "ethos_bellicose"
        || culture.MeanDevelopment >= 12
        || culture.Traditions.Any(vocab.TraditionsUnlockingMaa.Contains)
        || culture.Traditions.Any(t => t.Contains("warrior", StringComparison.Ordinal)
                                    || t.Contains("martial", StringComparison.Ordinal));

    /// <summary>
    /// Which doctrine this people would actually have arrived at.
    ///
    /// Ground dominates and is meant to: it is the only input measured over the whole territory
    /// rather than sampled once, and a people's weapons follow the country they fight in more
    /// closely than they follow its temperament. Everything else nudges, and the jitter is small
    /// enough that it only decides genuine near-ties.
    /// </summary>
    /// <param name="worldwide">How many regiments already made on this map hold each doctrine and
    /// each archetype. Terrain is regional, so without this a map whose settled ground is mostly
    /// jungle hands the same skirmisher idea to three unrelated peoples and the roster reads as
    /// one unit with three names. The penalty is smaller than the ground term on purpose: it
    /// diversifies genuine near-ties and does not put horse archers in a swamp.</param>
    /// <param name="development">Mean development of whoever will actually raise this — the
    /// culture for an elite, the whole heritage for a heritage regiment. Not the lead culture's
    /// own: a heritage regiment is recruitable by every culture under the pillar, so one rich
    /// culture speaking for the people must not put a costly doctrine within reach of its poor
    /// cousins.</param>
    /// <param name="taken">Doctrines a *sibling culture* of the same heritage already fields. A
    /// hard exclusion: two of a heritage's four cultures fielding the same idea under two invented
    /// names is the failure mode this exists to prevent.</param>
    /// <param name="incumbent">The doctrine this culture's own heritage regiment already holds, or
    /// null. A soft exclusion, and the difference matters. Held as hard as
    /// <paramref name="taken"/> it forced the elite off the people's own way of war and onto
    /// whatever else the ground allowed, which on a steppe people meant the elite they had to
    /// research was a third the strength of the regiment they already had for free. An elite that
    /// is the same idea done better is what vanilla ships — huscarls over armoured footmen — and
    /// is a far better outcome than a weaker unit with a different name.</param>
    /// <param name="floor">Power the regiment has to beat, or zero. Set to the heritage
    /// regiment's for an elite: an elite is defined against what the same people can already
    /// raise, not against its own archetype's mean, and those two are not the same question when
    /// archetype means run from 42 to 299 on the same scale.</param>
    private static Profile Best(List<Profile> profiles, Dictionary<TerrainClass, int> terrain,
        Culture culture, double development, string government, HashSet<Doctrine> taken,
        Doctrine? incumbent, double floor, bool elite,
        (Dictionary<Doctrine, int> Doctrines, Dictionary<string, int> Archetypes) worldwide,
        VanillaVocabulary vocab, Rng rng)
    {
        int total = Math.Max(1, terrain.Values.Sum());
        double wealth = Math.Clamp(development / 20.0, 0.0, 1.0);

        Profile? best = null;
        double bestScore = double.NegativeInfinity;

        bool Affordable(Profile p) => development >= p.MinDevelopment;
        bool Allowed(Profile p) => !taken.Contains(p.Doctrine) && (elite || !p.EliteOnly);

        // What Compose would actually build this doctrine at, which is the only honest way to ask
        // whether it clears the floor.
        bool Beats(Profile p) => Budget(vocab.MaaArchetypes[p.Archetype], elite) >= floor * 1.05;

        var eligible = profiles
            .Where(p => Allowed(p) && Affordable(p) && p.Doctrine != incumbent && Beats(p))
            .ToList();

        // Nobody else's ground can carry a regiment worth researching, so the people's own way of
        // war done properly it is.
        if (eligible.Count == 0)
            eligible = [.. profiles.Where(p => Allowed(p) && Affordable(p) && Beats(p))];

        // Not even that — the heritage regiment is already the strongest thing this culture can
        // reach. Compose is told the floor as well and will spend what headroom the archetype's
        // ceiling leaves, so the elite is at least not a downgrade it paid an innovation for.
        if (eligible.Count == 0)
            eligible = [.. profiles.Where(p => Allowed(p) && Affordable(p) && p.Doctrine != incumbent)];

        // Nothing left, on a heritage with more cultures than there are doctrines for its ground.
        // Every doctrine it can afford rather than nothing at all — a duplicate beats an unarmed
        // people, but a duplicate is still not a reason to hand a dirt-poor tribe elephants.
        if (eligible.Count == 0)
            eligible = [.. profiles.Where(p => (elite || !p.EliteOnly) && Affordable(p))];

        if (eligible.Count == 0)
            eligible = [.. profiles.Where(p => p.MinDevelopment <= 0 && (elite || !p.EliteOnly))];

        // A stub install with nothing this generator can build against. Callers have already
        // checked, so this is here to fail as a caught exception rather than as an index.
        if (eligible.Count == 0)
            throw new InvalidOperationException(
                "no men-at-arms doctrine is buildable against this game folder");

        foreach (var profile in eligible)
        {
            double share = profile.Ground.Sum(t => terrain.GetValueOrDefault(t)) / (double)total;

            double score = 3.0 * share
                         + (profile.Ethos.Contains(culture.Ethos) ? 1.1 : 0)
                         + (profile.Governments.Contains(government) ? 0.8 : 0)
                         + profile.DevelopmentPull * (wealth - 0.5) * 2.0
                         - 0.7 * worldwide.Doctrines.GetValueOrDefault(profile.Doctrine)
                         - 0.35 * worldwide.Archetypes.GetValueOrDefault(profile.Archetype)
                         + rng.NextDouble() * 0.35;

            // A fantasy race the export or the ethnicity pass tagged this people with. Worth as
            // much as the ethos: it is a statement about their bodies, and a race that cannot
            // ride is not going to have arrived at heavy cavalry whatever its grassland says.
            if (culture.ImportedArchetype is { } race && profile.Races.Contains(race)) score += 1.1;

            if (score <= bestScore) continue;

            bestScore = score;
            best = profile;
        }

        return best ?? eligible[0];
    }

    /// <summary>
    /// The power a regiment of this archetype is built to.
    ///
    /// Elites sit a fifth above their archetype's mean, which is roughly where vanilla's named
    /// cultural regiments sit above the generic one they are a variant of. Shared with
    /// <see cref="Best"/> so that the question "would this doctrine actually be an upgrade" is
    /// answered with the number <see cref="Compose"/> will really use.
    /// </summary>
    private static double Budget(VanillaVocabulary.MaaArchetype archetype, bool elite)
        => archetype.Power * (elite ? 1.2 : 1.0);

    /// <summary>
    /// Turns a doctrine and a people into an actual regiment: the stat line, the price, the
    /// terrain it is better on, and the name it is called by.
    /// </summary>
    /// <param name="floor">Power this regiment has to beat — the heritage regiment's, for an
    /// elite. Normally slack, because <see cref="Best"/> has already picked an archetype whose own
    /// budget clears it; it binds only when no archetype the culture could reach was strong enough,
    /// and there it spends whatever headroom <see cref="Clamp"/> leaves rather than shipping an
    /// innovation that hands the player a worse unit than the free one.</param>
    private static Regiment Compose(string key, Profile profile, Culture culture,
        Heritage? heritage, Dictionary<TerrainClass, int> terrain, VanillaVocabulary vocab,
        Rng rng, bool elite, double floor = 0)
    {
        var archetype = vocab.MaaArchetypes[profile.Archetype];

        // Start from the archetype's own average line, bend it with the doctrine, then pull the
        // whole thing back onto the budget. Skewing without renormalising would make every
        // doctrine with an aggressive multiplier simply better than the others.
        double damage = archetype.Damage * profile.Skew.Damage;
        double toughness = archetype.Toughness * profile.Skew.Toughness;
        double pursuit = archetype.Pursuit * profile.Skew.Pursuit;
        double screen = archetype.Screen * profile.Skew.Screen;

        double budget = Math.Max(Budget(archetype, elite), floor * 1.05);
        double raw = Power(damage, toughness, pursuit, screen);

        if (raw > 0)
        {
            double scale = budget / raw;
            damage *= scale; toughness *= scale; pursuit *= scale; screen *= scale;
        }

        // Enough jitter that two peoples who reached the same doctrine on the same ground do not
        // field the same numbers, not enough to move a unit out of its band.
        double Jitter(double value) => value * (0.94 + rng.NextDouble() * 0.12);

        var regiment = new Regiment
        {
            Key = key,
            Name = "",
            Flavor = "",
            Doctrine = profile.Doctrine,
            Archetype = profile.Archetype,
            Culture = culture,
            Heritage = heritage,
            Damage = Clamp(Jitter(damage), archetype.MaxDamage),
            Toughness = Clamp(Jitter(toughness), archetype.MaxToughness),
            Pursuit = Clamp(Jitter(pursuit), archetype.MaxPursuit),
            Screen = Clamp(Jitter(screen), archetype.MaxScreen),
            Stack = archetype.Stack,
            SiegeValue = Math.Round(archetype.SiegeValue, 2),
            AiQuality = elite ? 100 : 80,
        };

        foreach (var (target, weight) in archetype.Counters) regiment.Counters[target] = weight;

        // Priced by its own power against its archetype's, which is how vanilla prices a cultural
        // regiment — huscarls are heavy infantry at 1.28, longbowmen archers at 1.2.
        double ratio = archetype.Power <= 0
            ? 1.0
            : Power(regiment.Damage, regiment.Toughness, regiment.Pursuit, regiment.Screen) / archetype.Power;

        regiment.BuyCost = (int)Math.Round(archetype.BuyCost * ratio / 5) * 5;
        regiment.LowMaintenance = Math.Round(archetype.LowMaintenance * ratio, 2);
        regiment.HighMaintenance = Math.Round(regiment.LowMaintenance * vocab.HighMaintenanceMultiplier, 2);
        regiment.ProvisionCost = Provisions(profile.Archetype, ratio);

        // The two terrains this people actually holds most of, among the ones the doctrine is
        // about. Not simply their commonest terrain: a bonus on ground the doctrine has nothing to
        // do with reads as a stat handout rather than as a way of fighting.
        var home = profile.Ground
            .Where(t => terrain.ContainsKey(t))
            .OrderByDescending(t => terrain[t])
            .ThenBy(t => (int)t)
            .ToList();

        if (home.Count == 0) home.Add(profile.Ground[0]);
        regiment.HomeTerrain = home[0];

        // Which two stats the doctrine leans on, so the bonus reinforces the unit's character
        // rather than flattening it back towards the archetype's mean.
        var leading = new (string Stat, int Value, double Skew)[]
            {
                ("damage", regiment.Damage, profile.Skew.Damage),
                ("toughness", regiment.Toughness, profile.Skew.Toughness),
                ("pursuit", regiment.Pursuit, profile.Skew.Pursuit),
                ("screen", regiment.Screen, profile.Skew.Screen),
            }
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Skew).ThenBy(s => s.Stat, StringComparer.Ordinal)
            .Take(2).ToList();

        for (int i = 0; i < home.Count && i < 2; i++)
        {
            var t = home[i];
            double strength = i == 0 ? 0.28 : 0.14;

            // Cold ground pays out as a winter bonus instead. Taiga and arctic are where a people
            // learns to fight in snow, and CK3 has a slot that says exactly that. Written to both
            // winter slots identically, which is what vanilla does with a winter *bonus* — only
            // its penalties escalate between normal and harsh.
            bool cold = IsCold(t) && regiment.WinterNormal.Count == 0;

            var into = cold
                ? regiment.WinterNormal
                : (regiment.TerrainBonus.TryGetValue(TerrainClassifier.Name(t), out var existing)
                    ? existing
                    : regiment.TerrainBonus[TerrainClassifier.Name(t)] = new Dictionary<string, int>(StringComparer.Ordinal));

            foreach (var (stat, value, _) in leading)
            {
                int bonus = Math.Max(2, (int)Math.Round(value * strength));
                into[stat] = into.GetValueOrDefault(stat) + bonus;
            }

            if (cold)
                foreach (var (stat, value) in regiment.WinterNormal) regiment.WinterHarsh[stat] = value;
        }

        // And the ground the archetype is bad on, straight off vanilla's own regiments of it and
        // scaled by how much stronger than them this one is.
        //
        // Without this the generated roster was one-sided in a way that is invisible from the base
        // stats alone: a generated war elephant came out at 246 damage with a +69 jungle bonus and
        // nothing else, against vanilla's war_elephant at 250 with +50 in jungle, -100 in the
        // mountains, -150 in the wetlands and -60 in a harsh winter. Measured on the base line the
        // two look like the same unit. Measured anywhere a player actually fights, the generated
        // one was strictly better, because it had been given the half of the balance vanilla
        // spends on terrain and none of the half it recoups.
        //
        // Ground the doctrine already claimed is skipped: what this people learned to do in the
        // marshes outranks what their archetype normally does there, which is the whole reason the
        // doctrine picked that ground.
        foreach (var (terrainId, penalties) in
                 archetype.TerrainPenalty.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (regiment.TerrainBonus.ContainsKey(terrainId)) continue;

            var into = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (stat, value) in penalties)
            {
                int scaled = (int)Math.Round(value * ratio);
                if (scaled < 0) into[stat] = scaled;
            }

            if (into.Count > 0) regiment.TerrainBonus[terrainId] = into;
        }

        // Winter likewise, unless the doctrine has already said this is a people who fight in snow.
        if (regiment.WinterNormal.Count == 0 && regiment.WinterHarsh.Count == 0)
        {
            foreach (var (stat, value) in archetype.WinterNormalPenalty)
            {
                int scaled = (int)Math.Round(value * ratio);
                if (scaled < 0) regiment.WinterNormal[stat] = scaled;
            }

            foreach (var (stat, value) in archetype.WinterHarshPenalty)
            {
                int scaled = (int)Math.Round(value * ratio);
                if (scaled < 0) regiment.WinterHarsh[stat] = scaled;
            }
        }

        if (vocab.MaaIllustrations.TryGetValue(profile.Archetype, out var illustrations)
            && illustrations.Count > 0)
            regiment.Illustration = illustrations
                .GroupBy(r => r, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .First().Key;

        // The doctrine's own art if the install has it, then the archetype's generic icon, then
        // whatever there is. Never left unset: with no `icon` CK3 looks for a texture named after
        // the regiment's key, and there is no gen_maa_h0.dds on anybody's disk.
        regiment.Icon = profile.Icons.FirstOrDefault(vocab.MaaIcons.Contains)
            ?? new[] { profile.Archetype, regiment.Illustration ?? "" }
                .FirstOrDefault(vocab.MaaIcons.Contains)
            ?? (vocab.MaaIcons.Count > 0 ? vocab.MaaIcons[0] : null);

        Name(regiment, profile, culture, rng, elite);
        return regiment;
    }

    /// <summary>
    /// A name in the people's own language, and one sentence saying who they are.
    ///
    /// Mostly a bare invented word, because that is what vanilla's cultural regiments are —
    /// Huscarl, Landsknecht, Druzhina, Monaspa — and a compound reads as a translation rather than
    /// as the thing's name. The minority that do take an English noun are there so the roster does
    /// not become a wall of unglossed invented words.
    /// </summary>
    private static void Name(Regiment regiment, Profile profile, Culture culture, Rng rng, bool elite)
    {
        string word = culture.Language.Word(rng, 2, elite ? 3 : 2);
        word = char.ToUpperInvariant(word[0]) + word[1..];

        regiment.Name = rng.Chance(0.35) ? $"{word} {rng.Pick(profile.Nouns)}" : word;

        string[] standing = elite
            ? ["the sworn strength of", "the picked men of", "the standing companies of"]
            : ["raised across", "levied throughout", "mustered in every district of"];

        string[] closing =
        [
            $"and know {Ground(regiment.HomeTerrain)} as their own",
            $"and have never been beaten in {Ground(regiment.HomeTerrain)}",
            $"and were made by {Ground(regiment.HomeTerrain)} as much as by any captain",
        ];

        // Escaped on the way out by LocFile; the #F markers are CK3's own flavour colour, and
        // every vanilla regiment's flavour line is wrapped in them.
        regiment.Flavor =
            $"#F {profile.Idea} The {regiment.Name} are {rng.Pick(standing)} the {culture.Name}, " +
            $"{rng.Pick(closing)}.#!";
    }

    /// <summary>
    /// An innovation icon that looks like the thing it unlocks.
    ///
    /// <see cref="VanillaVocabulary.InnovationIcons"/> is the whole harvested set, civic art
    /// included, and picking from it blind put a windmill on a regiment of raiders. Matched on the
    /// filename because vanilla names these after their subject — <c>innovation_camel</c>,
    /// <c>innovation_knight</c>, <c>innovation_maa_01</c> — with a narrowing to anything martial
    /// and a last resort of whatever the install has, so a stripped-down install still gets art.
    /// </summary>
    private static string? InnovationIcon(Regiment regiment, VanillaVocabulary vocab, Rng rng)
    {
        if (vocab.InnovationIcons.Count == 0) return null;

        string[] wanted = regiment.Archetype switch
        {
            "camel_cavalry" => ["camel"],
            "elephant_cavalry" => ["elephant"],
            "heavy_cavalry" or "light_cavalry" or "archer_cavalry" => ["knight", "caballeros"],
            "siege_weapon" => ["siege"],
            _ => ["special_maa", "weapons_and_armor", "maa_0", "hird"],
        };

        var matches = vocab.InnovationIcons
            .Where(path => wanted.Any(w => path.Contains(w, StringComparison.Ordinal)))
            .ToList();

        if (matches.Count == 0)
            matches = [.. vocab.InnovationIcons.Where(p =>
                p.Contains("maa", StringComparison.Ordinal)
                || p.Contains("knight", StringComparison.Ordinal)
                || p.Contains("weapons", StringComparison.Ordinal))];

        return rng.Pick(matches.Count > 0 ? matches : vocab.InnovationIcons);
    }

    /// <summary>
    /// Provisioning weight, on vanilla's own three bands. Only Roads to Power reads it, and a
    /// missing value there means a free regiment for every nomad and adventurer on the map.
    /// </summary>
    private static int Provisions(string archetype, double ratio)
    {
        bool mounted = archetype.Contains("cavalry", StringComparison.Ordinal);
        int[] bands = mounted ? [7, 15, 21] : [3, 7, 12];
        return bands[ratio < 0.95 ? 0 : ratio < 1.15 ? 1 : 2];
    }

    /// <summary>
    /// Rounded, floored at zero, and allowed a little past the strongest vanilla regiment of the
    /// archetype but no further. The ceiling matters more than it looks: an elite budget spent
    /// almost entirely on one stat is exactly how a generated unit ends up outclassing everything
    /// in the game, and the strongest thing vanilla ships is a defensible place to stop.
    /// </summary>
    private static int Clamp(double value, int vanillaMax)
        => (int)Math.Round(Math.Clamp(value, 0, Math.Max(1, vanillaMax * 1.15)));

    private static void Report(RetinueMap map, int earned, int settledCultures)
    {
        if (map.Regiments.Count == 0) return;

        var archetypes = map.Regiments
            .GroupBy(r => r.Archetype, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");

        Console.WriteLine($"  retinues: {map.ByHeritage.Count} heritage regiments, " +
                          $"{earned} elites earned of {settledCultures} cultures");
        Console.WriteLine($"  retinue archetypes: {string.Join(", ", archetypes)}");

        int known = map.Innovations.All.Count(i => i.KnownAtStart.Count > 0);
        Console.WriteLine($"  retinue innovations: {map.Innovations.All.Count} written, " +
                          $"{known} already discovered at the start date");
    }
}
