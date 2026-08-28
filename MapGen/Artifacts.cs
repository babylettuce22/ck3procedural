using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public enum ArtifactCategory
{
    SovereignJewels,
    MartialRelics,
    SacredScriptures,
    ScholarlyWorks,

    // ---- Court artifacts. Everything below needs a royal court to exist in. ----
    //
    // Separated from the four above by more than theme. The four above go in inventory slots every
    // character has; these go in court slots, which require Royal Court (EP1) AND a holder of at
    // least kingdom tier — MIN_ROYAL_COURT_TIER in 00_defines.txt, and every government in the game
    // declares royal_court = any, so tier is the real gate. A world's court treasure therefore
    // reaches a handful of rulers where its inventory treasure reaches every one of them.
    //
    // They are drawn IN ADDITION to a ruler's inventory artifacts rather than instead of them,
    // because they compete for different slots and a king should not own fewer swords for owning a
    // throne.

    /// <summary>A faith's relic, on a pedestal. Vanilla's commonest court artifact by far.</summary>
    CourtRelic,

    /// <summary>The seat itself. One slot in the room, so one per court and only for the grandest.</summary>
    CourtThrone,

    // There is deliberately no banner here, though vanilla's game-start pass makes one the first
    // thing every court gets. That pass is the reason: `historical_artifacts.0023` fires from
    // vanilla's own game_start.txt, which this generator does not blank, so every royal court in
    // every generated world ALREADY receives a house banner — and a dynasty banner too, if its
    // holder is the dynast.
    //
    // Generating our own put a third banner on the wall, and a worse one. Vanilla's
    // create_artifact_wall_banner_effect takes a TARGET of a title, house or dynasty and renders
    // the banner with that entity's actual coat of arms; ours could only ask for `visuals = banner`
    // and get the generic cloth. Two of the three wall slots were being spent to duplicate, badly,
    // something the base game already does correctly.
}

/// <summary>
/// The four bands CK3 draws an artifact's frame in, and the thing a player actually reads off the
/// inventory screen.
///
/// It is not a field on the artifact. The engine derives it from quality + wealth against
/// <c>ARTIFACT_THRESHOLD_MASTERWORK = 50</c>, <c>_FAMED = 130</c> and <c>_ILLUSTRIOUS = 180</c> in
/// <c>00_defines.txt</c>, so the only way to *decide* a rarity is to roll the two numbers inside a
/// band that cannot cross a threshold — see <see cref="Roll"/>. Rolling them freely, which is what
/// this file used to do, means the tier is whatever the dice said: the old 30..95 range on each
/// could not produce a common artifact at all and could hand a random count the same purple frame
/// as the world's one legendary.
/// </summary>
public enum ArtifactRarity
{
    Common,
    Masterwork,
    Famed,
    Illustrious
}

/// <summary>
/// One line of an artifact's history panel: who had it, when, and how they came by it.
///
/// The vocabulary is the engine's — <c>created</c>, <c>created_before_history</c>,
/// <c>inherited</c>, <c>given</c>, <c>conquest</c>, <c>stolen</c>, <c>discovered</c> — and the
/// panel renders each entry with the named character's portrait and coat of arms. An artifact with
/// one <c>created_before_history</c> line and nothing else shows an empty panel, which is what
/// every generated artifact used to be.
/// </summary>
public sealed record ArtifactProvenance
{
    public required string Type { get; init; }
    public required string Date { get; init; }

    /// <summary>The party the object came *from*: the giver, or the loser of the battle.</summary>
    public string? ActorId { get; init; }

    /// <summary>The party that ends the entry holding it.</summary>
    public string? RecipientId { get; init; }

    /// <summary>A province id for the entries the engine will render a place into.</summary>
    public int LocationProvinceId { get; init; } = -1;
}

public sealed class GeneratedArtifact
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public required string DescriptionKey { get; init; }
    public required string Type { get; init; }
    public required string Visuals { get; init; }
    public required string Template { get; init; }
    public required int Wealth { get; init; }
    public required int Quality { get; init; }
    public required string Modifier { get; init; }
    public required ArtifactCategory Category { get; init; }
    public required ArtifactRarity Rarity { get; init; }
    public required string LocalizedName { get; init; }
    public required string LocalizedDescription { get; init; }

    /// <summary>The year the first history entry claims. The chronicle reads this rather than
    /// rolling a year of its own, so the lore panel and the artifact panel agree.</summary>
    public required int CreatedYear { get; init; }

    /// <summary>
    /// The history chain, oldest first. The first entry is written into <c>create_artifact</c>'s
    /// own <c>history = { }</c> block; the rest become <c>add_artifact_history</c> calls.
    /// </summary>
    public required IReadOnlyList<ArtifactProvenance> Provenance { get; init; }

    /// <summary>The realm this object is an heirloom *of*, for
    /// <c>add_artifact_title_history</c>. Null for anything that is only a person's possession.</summary>
    public (string TitleKey, string Date)? TitleHistory { get; init; }

    /// <summary>
    /// The modifier body for a one-off artifact, written under <see cref="Modifier"/> as its own
    /// block. Null for the ordinary ones, which share a rarity-tiered key from a fixed pool.
    /// </summary>
    public IReadOnlyList<(string Key, string Value)>? ModifierFields { get; init; }

    public string ModifierIcon { get; init; } = "prowess_positive";

    /// <summary>
    /// Whether this artifact needs a royal court to exist in, and so has to be created behind a
    /// runtime check rather than unconditionally.
    ///
    /// Court slots come from Royal Court and belong to kingdom-tier holders. Creating one for a
    /// player who has neither leaves an artifact with nowhere to sit — so the spawn is guarded and
    /// such a world simply gets the inventory treasure, with nothing missing that it could have
    /// used. See <see cref="Emit.ArtifactWriter.WriteOnGameStart"/>.
    /// </summary>
    public bool NeedsRoyalCourt { get; init; }
}

public sealed class ArtifactMap
{
    public Dictionary<Title, List<GeneratedArtifact>> ByCounty { get; } = new();
    public List<GeneratedArtifact> AllArtifacts { get; } = new();

    /// <summary>Every one-off modifier body that needs writing, in emission order.</summary>
    public IEnumerable<GeneratedArtifact> Signatures
        => AllArtifacts.Where(a => a.ModifierFields is not null);

    /// <param name="worldCenters">Optional, and optional in the strong sense: a world may have no
    /// wonders at all, now or because a later map generates them some other way. Placement reads it
    /// for weighting only and never for structure.</param>
    /// <param name="development">Optional for the same reason.</param>
    public static ArtifactMap Build(
                List<Title> counties, CultureMap cultures, FaithMap faiths,
                RealmMap realms, WildernessMap wilderness, PrehistoryMap prehistory,
                WorldCenterMap? worldCenters, Dictionary<Title, int>? development,
                MapConfig cfg, Rng rng, IReadOnlyList<WeaponAsset>? forgedWeapons = null)
    {
        var map = new ArtifactMap();
        var legendaryLogs = new List<string>();

        if (counties.Count == 0) return map;

        // Ruler counties only. A county inside a liege's personal demesne has no character of its
        // own to hand a treasure to, and the spawn effect would scope to one that was never written.
        var rulers = realms.HolderCounty.Values.ToHashSet();
        //
        // Index order, not tree order. The names are now drawn against a world-level registry, so
        // what a county is allowed to call its sword depends on what earlier counties took — and
        // the tree is rebuilt from scratch every run with an iteration order that is stable only by
        // accident. The index is assigned once and never moves. Same reasoning, and the same fix,
        // as ChronicleMap.Build.
        var settledCounties = counties
            .Where(c => !wilderness.Contains(c) && rulers.Contains(c))
            .OrderBy(c => c.Index)
            .ToList();
        if (settledCounties.Count == 0) return map;

        // Reverse of prehistory's county-to-house map, so a feud can be told which county the other
        // house sits in and an artifact can name the line it was taken from. Same construction the
        // chronicle uses; houses are one-per-ruler, so it does not collide.
        var houseSeat = new Dictionary<string, Title>();
        foreach (var (county, house) in prehistory.CharacterHouseMap)
            houseSeat.TryAdd(house, county);

        // Where a famous thing would plausibly be, and how strongly.
        var (prominence, sacred) = Prominence(settledCounties, worldCenters, faiths, development);

        // The fated bearer, drawn against that rather than uniformly. Weighted rather than
        // forced: the great relic of the age usually sits somewhere the world already cares
        // about, and occasionally turns up in a county nobody has heard of, which is the more
        // interesting outcome precisely because it is not the rule.
        var fatedCounty = WeightedPick(settledCounties, prominence, rng);

        // Every name this world has handed out. Two artifacts sharing a name is wrong on any of
        // them and absurd on a template that declares `unique = yes`.
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var county in settledCounties)
        {
            var culture = cultures.For(county);
            var faith = faiths.For(county);
            var primaryTitle = HistoryWriter.Primary(county, realms);

            // Seeded from the county AND the world.
            //
            // The county half is the original point of these: a county's treasure should not move
            // because something unrelated changed elsewhere on the map. But the world half was
            // missing, and without it county 159 rolled the same stream in every world ever
            // generated — which is why the same legendary names kept coming back run after run, no
            // matter how wide the name banks got. Wider banks cannot help a generator that is
            // asking them the same question every time.
            var countyRng = new Rng(cfg.Seed ^ county.Index ^ 0x7E1A);
            var (firstName, _) = HistoryWriter.RulerNames(county, culture);

            var list = new List<GeneratedArtifact>();

            // Which of this ruler's one-of-a-kind slots are already spoken for.
            var usedSlots = new HashSet<string>(StringComparer.Ordinal);

            bool isEmperor = primaryTitle.Tier == "e";
            bool isKing = primaryTitle.Tier == "k";
            bool isDuke = primaryTitle.Tier == "d";

            int targetCount = 0;
            int roll = countyRng.Int(0, 100);

            if (isEmperor) targetCount = roll > 50 ? 4 : 3;
            else if (isKing) targetCount = roll > 40 ? 3 : 2;
            else if (isDuke) targetCount = roll > 70 ? 2 : (roll > 15 ? 1 : 0);
            else targetCount = roll > 60 ? 1 : 0;

            int standing = prominence.GetValueOrDefault(county);

            // A wonder or a great shrine draws treasure the way it draws pilgrims: things get
            // given to it, left at it, and not moved again. One extra piece, not a hoard — the
            // tier of the ruler still decides how much of a treasury there is.
            if (standing >= WonderScore) targetCount++;

            if (county == fatedCounty && targetCount == 0)
            {
                targetCount = 1;
            }

            for (int i = 0; i < targetCount; i++)
            {
                var artRng = new Rng((int)(cfg.Seed ^ county.Index ^ 0x3D7F ^ (i * 7177)));
                // Two ways to be legendary: being the world's fated bearer, or rolling it.
                //
                // The roll is scaled by standing for the same reason the fated draw is weighted.
                // It used to be a flat 2%, which on a large map produces more legendaries than the
                // fated one does — so the single relic this generator placed deliberately was
                // outnumbered by ones scattered at random, and the geography never showed up in the
                // tier anybody looks at. A plain county is now well under one percent; a county
                // with a wonder and wealth behind it is nearer ten.
                bool isLegendary = (county == fatedCounty && i == 0)
                                || artRng.Int(0, 999) < 6 + standing * 9;

                // Rarity first, then the numbers that produce it. The reverse — which is what this
                // used to do — leaves the tier to chance, and the tier is the part of an artifact
                // the player reacts to.
                var rarity = isLegendary
                    ? ArtifactRarity.Illustrious
                    : RarityFor(primaryTitle.Tier, i, standing, artRng);

                var (quality, wealth) = Roll(rarity, artRng);

                ArtifactCategory category;
                if (i == 0 && (isEmperor || isKing))
                {
                    category = ArtifactCategory.SovereignJewels;
                }
                else
                {
                    category = DrawCategory(
                        headline: i == 0, holy: sacred.Contains(county), artRng);
                }

                var look = Compose(
                    category, rarity, culture, faith, primaryTitle, firstName, taken, artRng, forgedWeapons);

                // Redraw the category rather than fill a slot twice. The draw is per-artifact and
                // knows nothing about its siblings, so a ruler with three or four pieces can roll
                // MartialRelics twice — and martial is a weapon four times in five, so two martials
                // is usually two swords.
                //
                // The sovereign piece is exempt: a king's first artifact is his crown by
                // construction, and there is only ever one of those.
                bool forced = i == 0 && (isEmperor || isKing);

                for (int attempt = 0; !forced && attempt < 5 && !SlotFree(usedSlots, look.Type); attempt++)
                {
                    category = DrawCategory(
                        headline: i == 0, holy: sacred.Contains(county), artRng);

                    look = Compose(
                        category, rarity, culture, faith, primaryTitle, firstName, taken, artRng, forgedWeapons);
                }

                // Still nowhere to put it, so this ruler simply owns less. An emperor draws four
                // pieces but the non-sovereign categories only reach three slots — weapon, armour
                // and journal, since scripture and scholarship both go to the journal — so a full
                // strongbox genuinely runs out of places, and no number of redraws invents one.
                //
                // Stopping is the honest answer, and stopping the whole loop rather than skipping
                // one index is right for the same reason: if the slots are full at i, they are full
                // at i+1 too. Three artifacts a ruler can use beats four with one that does nothing.
                if (!forced && !SlotFree(usedSlots, look.Type)) break;

                usedSlots.Add(SlotOf(look.Type));

                var (chain, titleHistory) = BuildProvenance(
                    county, primaryTitle, rarity, category, prehistory, houseSeat, cfg, artRng);

                // The description is finished from the chain, not written blind. An object whose
                // panel shows three owners and a theft should say so in its own text; one that was
                // made last decade should not claim otherwise.
                string description = look.Description + ProvenanceClause(rarity, chain, artRng);

                string id = $"gen_art_{county.Index}_{i}";

                var art = new GeneratedArtifact
                {
                    Id = id,
                    NameKey = $"gen_art_name_{county.Index}_{i}",
                    DescriptionKey = $"gen_art_desc_{county.Index}_{i}",
                    Type = look.Type,
                    Visuals = look.Visuals,
                    Template = rarity == ArtifactRarity.Illustrious
                        ? "gen_legendary_template"
                        : "gen_artifact_template",
                    Wealth = wealth,
                    Quality = quality,
                    Modifier = rarity == ArtifactRarity.Illustrious
                        ? $"gen_legend_{county.Index}_{i}_modifier"
                        : $"gen_{look.Family}_modifier_{rarity.ToString().ToLowerInvariant()}",
                    ModifierFields = look.Signature,
                    ModifierIcon = look.Icon,
                    Category = category,
                    Rarity = rarity,
                    LocalizedName = look.Name,
                    LocalizedDescription = description,
                    CreatedYear = YearOf(chain[0].Date),
                    Provenance = chain,
                    TitleHistory = titleHistory,
                };

                if (isLegendary)
                {
                    string charId = HistoryWriter.CharacterId(county);
                    legendaryLogs.Add($"'{look.Name}' ({category}) -> Holder: {firstName} of {primaryTitle.Name} "
                        + $"(Primary Title: {primaryTitle.Key}, Character: {charId}, "
                        + $"{art.Provenance.Count} history entries)");
                }

                list.Add(art);
                map.AllArtifacts.Add(art);
            }

            // --- Court artifacts, for the rulers who have a room to put them in ---------------
            //
            // Appended rather than mixed into the draw above: they occupy court slots rather than
            // inventory ones, so a king owning a throne should not mean owning one sword fewer.
            // Vanilla's own game-start pass has the same shape — every court gets its house banner,
            // then a scattering of faith relics on top.
            if (isEmperor || isKing)
            {
                var courtRng = new Rng((int)(county.Index ^ 0x2B91));
                var court = new List<ArtifactCategory>();

                // A relic is the commonest court piece in vanilla and the one that says which faith
                // the hall belongs to. Emperors keep one as a matter of course; kings often enough.
                if (isEmperor || courtRng.Int(0, 99) < 55) court.Add(ArtifactCategory.CourtRelic);

                // One throne slot in the room, so this is the piece a court either has or does not.
                if (isEmperor || courtRng.Int(0, 99) < 25) court.Add(ArtifactCategory.CourtThrone);

                for (int c = 0; c < court.Count; c++)
                {
                    var artRng = new Rng((int)(cfg.Seed ^ county.Index ^ 0x6C07 ^ (c * 4409)));
                    var category = court[c];

                    // A hall's treasure is drawn at the same standing as its owner's strongbox: a
                    // court piece is not automatically grander, it is just kept somewhere else.
                    var rarity = RarityFor(primaryTitle.Tier, 0, standing, artRng);

                    var (quality, wealth) = Roll(rarity, artRng);

                    var look = Compose(
                        category, rarity, culture, faith, primaryTitle, firstName, taken, artRng, forgedWeapons);

                    // A hall has one throne. The relic sits on a pedestal, of which there are four,
                    // so only the throne can collide here — but it is checked the same way rather
                    // than by knowing which of the two this is.
                    if (!SlotFree(usedSlots, look.Type)) continue;

                    usedSlots.Add(SlotOf(look.Type));

                    var (chain, titleHistory) = BuildProvenance(
                        county, primaryTitle, rarity, category, prehistory, houseSeat, cfg, artRng);

                    string description = look.Description + ProvenanceClause(rarity, chain, artRng);
                    int index = targetCount + c;

                    var art = new GeneratedArtifact
                    {
                        Id = $"gen_art_{county.Index}_{index}",
                        NameKey = $"gen_art_name_{county.Index}_{index}",
                        DescriptionKey = $"gen_art_desc_{county.Index}_{index}",
                        Type = look.Type,
                        Visuals = look.Visuals,
                        Template = rarity == ArtifactRarity.Illustrious
                            ? "gen_legendary_template"
                            : "gen_artifact_template",
                        Wealth = wealth,
                        Quality = quality,
                        Modifier = rarity == ArtifactRarity.Illustrious
                            ? $"gen_legend_{county.Index}_{index}_modifier"
                            : $"gen_{look.Family}_modifier_{rarity.ToString().ToLowerInvariant()}",
                        ModifierFields = look.Signature,
                        ModifierIcon = look.Icon,
                        Category = category,
                        Rarity = rarity,
                        LocalizedName = look.Name,
                        LocalizedDescription = description,
                        CreatedYear = YearOf(chain[0].Date),
                        Provenance = chain,
                        TitleHistory = titleHistory,
                        NeedsRoyalCourt = true,
                    };

                    list.Add(art);
                    map.AllArtifacts.Add(art);
                }
            }

            if (list.Count > 0)
            {
                map.ByCounty[county] = list;
            }
        }

        var spread = map.AllArtifacts
            .GroupBy(a => a.Rarity)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");

        Console.WriteLine($"  artifacts generated: {map.AllArtifacts.Count} procedural items across "
            + $"{map.ByCounty.Count} rulers ({string.Join(", ", spread)})");

        if (legendaryLogs.Count > 0)
        {
            Console.WriteLine("  legendary artifacts generated:");
            foreach (var log in legendaryLogs)
            {
                Console.WriteLine($"    {log}");
            }
        }

        return map;
    }

    // ---------------------------------------------------------------------------------------
    // Placement
    //
    // Artifacts used to be drawn per county in isolation, and the world's one legendary was put
    // wherever `rng.Pick` landed. That produces treasure with no geography: nothing about where a
    // relic is says anything about the world, and the Chronicle of Treasures — which lists a dozen
    // of them side by side — reads as a loot table rather than as a place.
    //
    // What follows is a weighting, never a rule. Every input is optional and every term is
    // additive, so a world with no wonders, no holy sites and no development data scores every
    // county zero and the draws below reduce exactly to the uniform ones they replace. That
    // matters beyond tidiness: wonders are not guaranteed to exist at world start, now or later.
    // ---------------------------------------------------------------------------------------

    private const int WonderScore = 6;
    private const int HolySiteScore = 3;
    private const int PrincipalSiteBonus = 2;

    /// <summary>
    /// How much of a reason each county gives for something famous to be there, and which counties
    /// are sacred ground.
    /// </summary>
    private static (Dictionary<Title, int> Score, HashSet<Title> Sacred) Prominence(
        List<Title> counties, WorldCenterMap? worldCenters, FaithMap faiths,
        Dictionary<Title, int>? development)
    {
        var score = new Dictionary<Title, int>();
        var sacred = new HashSet<Title>();

        void Add(Title county, int amount)
            => score[county] = score.GetValueOrDefault(county) + amount;

        // A wonder is the strongest single claim a county can have, and the only one that can push
        // an ordinary count's holdings past his station on its own.
        if (worldCenters is not null)
        {
            foreach (var center in worldCenters.Centers)
                Add(center.County, WonderScore);
        }

        // Holy sites, but only each faith's PRINCIPAL one — the seat its head sits at.
        //
        // Every site scored at first, and that was wrong by a factor of five: HolySitesPerFaith is
        // 5 by default, so a measured world had 60 holy-site counties against 156 rulers. Scoring
        // them all made prominence the common case, which is the one thing it cannot be and still
        // mean anything. Only the principal sites are scarce — one per faith, about a dozen.
        //
        // Principal sites also carry the scripture bias, and ONLY they do. An earlier pass let every
        // holy site steer what kind of thing its county held, on the reasoning that being worth a
        // relic and being worth a famous relic are different claims. Measured, that was too
        // expensive: sixty holy counties against a hundred and fifty rulers meant the bias fired on
        // a third of the world and pushed written works to half of all treasure. Sharpened to the
        // dozen seats a faith actually centres on, it says something instead of colouring
        // everything.
        foreach (var faith in faiths.Faiths)
        {
            if (faith.HolySites.Count == 0) continue;

            var principal = faith.HolySites[0].County;
            Add(principal, HolySiteScore + PrincipalSiteBonus);
            sacred.Add(principal);
        }

        // And wealth — but only genuinely exceptional wealth. The thresholds here were 6/10/15
        // against a world whose development runs median 9 and p90 20, so half the map scored and
        // the five wonder counties were drowned out by it. Measure before picking a number.
        if (development is not null)
        {
            foreach (var county in counties)
            {
                int dev = development.GetValueOrDefault(county);
                if (dev >= 30) Add(county, 3);
                else if (dev >= 20) Add(county, 2);
            }
        }

        return (score, sacred);
    }

    /// <summary>
    /// A county drawn in proportion to its standing, with every county keeping a floor of one.
    ///
    /// The floor is what keeps this a weighting. With nothing prominent anywhere every weight is
    /// 1 and this is a uniform draw; with a wonder somewhere that county is seven times likelier
    /// than a plain one and still not certain.
    /// </summary>
    private static Title WeightedPick(
        List<Title> counties, Dictionary<Title, int> prominence, Rng rng)
    {
        int total = 0;
        foreach (var county in counties) total += 1 + prominence.GetValueOrDefault(county);

        int roll = rng.Int(0, total - 1);

        foreach (var county in counties)
        {
            roll -= 1 + prominence.GetValueOrDefault(county);
            if (roll < 0) return county;
        }

        // Unreachable while the weights above are positive. Answering with a real county rather
        // than throwing keeps a rounding mistake from taking the whole generation down.
        return counties[^1];
    }

    // ---------------------------------------------------------------------------------------
    // Rarity
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What a ruler of this tier keeps, before the fated one is drawn.
    ///
    /// Each artifact after the first steps down a band: the crown is the heirloom and the rest of
    /// the strongbox is the rest of the strongbox. Without the step an emperor's four items were
    /// four equals, which is not how a treasury reads.
    /// </summary>
    private static ArtifactRarity RarityFor(string tier, int index, int standing, Rng rng)
    {
        var top = tier switch
        {
            "e" => ArtifactRarity.Famed,
            "k" => ArtifactRarity.Famed,
            "d" => ArtifactRarity.Masterwork,
            _ => ArtifactRarity.Common,
        };

        int band = (int)top - index;

        // One in twelve is better than its owner's station: a poor count with his
        // great-grandfather's sword is the reason artifacts are worth stealing.
        //
        // Standing shortens those odds rather than removing them — at a wonder or a principal
        // shrine it is closer to one in four. This is the lever that lets a minor lord holding
        // famous ground keep something out of proportion to his rank, which is a far better
        // reason for a famed relic to be somewhere than "the dice said so".
        int odds = Math.Max(4, 12 - Math.Min(standing, 8));

        if (rng.Int(0, odds - 1) == 0) band++;

        return (ArtifactRarity)Math.Clamp(band, (int)ArtifactRarity.Common, (int)ArtifactRarity.Famed);
    }

    /// <summary>
    /// What kind of thing a ruler who is not a king keeps.
    ///
    /// This was a flat draw over the three non-sovereign categories, and since two of them are
    /// written matter it put roughly two thirds of every world's treasure into books and scrolls —
    /// measured at 62% of all artifacts on seed 991. A world whose commonest heirloom is a pocket
    /// codex does not read as a world with a martial history. The weighting leans toward arms
    /// without removing the scholarship, and leans harder for the first piece a ruler owns, which
    /// is the one the chronicle will name.
    /// </summary>
    private static ArtifactCategory DrawCategory(bool headline, bool holy, Rng rng)
    {
        int roll = rng.Int(0, 99);

        // Sacred ground keeps scripture. Not exclusively — a shrine county still has lords with
        // swords — but the relic of a holy site being a psalter rather than a mace is the whole
        // reason to know the site is there.
        //
        // Held at forty rather than the fifty-five it started on. Principal shrines also carry the
        // most standing, so they win most of the legendaries; at fifty-five a measured world's
        // three greatest treasures were three books, which is coherent and dull. The bias should
        // colour a shrine's holdings, not monopolise the top of the world.
        if (holy && roll < 30) return ArtifactCategory.SacredScriptures;

        if (headline)
        {
            return roll < 72 ? ArtifactCategory.MartialRelics
                 : roll < 86 ? ArtifactCategory.SacredScriptures
                 : ArtifactCategory.ScholarlyWorks;
        }

        return roll < 62 ? ArtifactCategory.MartialRelics
             : roll < 81 ? ArtifactCategory.SacredScriptures
             : ArtifactCategory.ScholarlyWorks;
    }

    /// <summary>
    /// Quality and wealth inside a band that cannot cross a rarity threshold at either end.
    ///
    /// The bands are deliberately narrower than the gaps between the thresholds (50 / 130 / 180 on
    /// the sum): the maximum sum of a band sits below the next threshold and its minimum sits above
    /// its own, so two independent rolls can vary the numbers on the tooltip without ever moving
    /// the frame. Vanilla's <c>set_artifact_rarity_*</c> effects pin both to a single value each,
    /// which is safe but makes every artifact of a tier read identically.
    /// </summary>
    private static (int Quality, int Wealth) Roll(ArtifactRarity rarity, Rng rng) => rarity switch
    {
        ArtifactRarity.Common => (rng.Int(10, 22), rng.Int(10, 22)),        // sum 20..44   (< 50)
        ArtifactRarity.Masterwork => (rng.Int(28, 62), rng.Int(28, 62)),    // sum 56..124  (< 130)
        ArtifactRarity.Famed => (rng.Int(66, 88), rng.Int(66, 88)),         // sum 132..176 (< 180)
        _ => (rng.Int(92, 100), rng.Int(92, 100)),                          // sum 184..200
    };

    // ---------------------------------------------------------------------------------------
    // Provenance
    // ---------------------------------------------------------------------------------------

    private static int YearOf(string date)
        => int.TryParse(date.Split('.')[0], out int y) ? y : 0;

    private static string DateIn(int year, Rng rng)
        => $"{Math.Max(1, year)}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";

    /// <summary>
    /// The history chain, built from the ancestry prehistory already generated.
    ///
    /// How deep it goes is a function of rarity, which is the whole point: a common trinket that
    /// claims three centuries of owners is not more evocative, it is noise. So a common item gets a
    /// bare undated origin, a masterwork was made for the ruler's father and inherited from him,
    /// and a famed or illustrious one predates the line entirely and arrives through it — the shape
    /// AGOT gives its Valyrian steel, which opens <c>created_before_history</c> and then walks a
    /// chain of <c>inherited</c> entries down to the character holding it on the start date.
    ///
    /// Every character named here is a deceased parent from <see cref="PrehistoryMap"/>, because
    /// those are the only characters whose birth and death dates this generator knows. Naming the
    /// living ruler as a recipient of a dated entry would risk asserting he received it before he
    /// was born.
    /// </summary>
    private static (List<ArtifactProvenance> Chain, (string TitleKey, string Date)? TitleHistory)
        BuildProvenance(
            Title county, Title primaryTitle, ArtifactRarity rarity, ArtifactCategory category,
            PrehistoryMap prehistory, Dictionary<string, Title> houseSeat, MapConfig cfg, Rng rng)
    {
        var chain = new List<ArtifactProvenance>();
        string rulerId = HistoryWriter.CharacterId(county);

        prehistory.DeceasedParents.TryGetValue(county, out var parent);
        int parentBirth = parent is null ? 0 : YearOf(parent.BirthDate);
        int parentDeath = parent?.DeathDate is null ? 0 : YearOf(parent.DeathDate);
        bool hasLine = parent is not null && parentDeath > parentBirth + 20;

        int capital = county.Children.FirstOrDefault(b => b.Tier == "b")?.ProvinceId ?? -1;

        if (rarity <= ArtifactRarity.Common || !hasLine)
        {
            // Nothing is claimed about who made it or for whom. A dated origin and no more is
            // exactly what `created_before_history` means, and it is honest here.
            int year = cfg.StartYear - rng.Int(20, 90);
            chain.Add(new ArtifactProvenance { Type = "created_before_history", Date = DateIn(year, rng) });
            return (chain, null);
        }

        if (rarity == ArtifactRarity.Masterwork)
        {
            // Made inside the father's lifetime, after he was old enough to commission it.
            int year = rng.Int(parentBirth + 18, parentDeath - 1);

            chain.Add(new ArtifactProvenance
            {
                Type = "created",
                Date = DateIn(year, rng),
                RecipientId = parent!.Id,
                LocationProvinceId = capital,
            });

            chain.Add(new ArtifactProvenance
            {
                Type = "inherited",
                Date = parent.DeathDate!,
                RecipientId = rulerId,
            });

            return (chain, null);
        }

        // Famed and illustrious: older than the house that holds it.
        int made = cfg.StartYear - rng.Int(180, 460);
        chain.Add(new ArtifactProvenance { Type = "created_before_history", Date = DateIn(made, rng) });

        // If this house is feuding with another, the quarrel gets an object attached to it. That
        // is the cheapest way a generated world explains why two families hate each other, and it
        // reuses a feud the chronicle is already telling the player about.
        var rivalParent = FeudingRival(county, prehistory, houseSeat);
        int handover = rng.Int(parentBirth + 16, parentDeath - 1);

        if (rivalParent is not null && rng.Chance(0.5))
        {
            // Taken from the rival's line, in the generation before the current one. Actor is the
            // party it came from, recipient the one who ends up holding it — the same reading as
            // vanilla's `given` entries.
            chain.Add(new ArtifactProvenance
            {
                Type = rng.Chance(0.5) ? "conquest" : "stolen",
                Date = DateIn(rng.Int(Math.Max(made + 1, parentBirth), handover), rng),
                ActorId = rivalParent,
                RecipientId = parent!.Id,
            });
        }
        else
        {
            chain.Add(new ArtifactProvenance
            {
                Type = "inherited",
                Date = DateIn(handover, rng),
                RecipientId = parent!.Id,
            });
        }

        chain.Add(new ArtifactProvenance
        {
            Type = "inherited",
            Date = parent.DeathDate!,
            RecipientId = rulerId,
        });

        // Crowns and regalia belong to the realm rather than to the man wearing them, and the
        // artifact panel says so in its own line — this is what makes a vanilla crown read as a
        // crown instead of as an expensive hat.
        (string, string)? titleHistory = category == ArtifactCategory.SovereignJewels
            ? (primaryTitle.Key, chain[0].Date)
            : null;

        return (chain, titleHistory);
    }

    /// <summary>
    /// The sentence that reports the history panel back to the reader.
    ///
    /// Without it a famed crown of an empire — three owners, a title history, four centuries older
    /// than the man wearing it — read with exactly the prose of a count's ceremonial stick, because
    /// the description branched only on whether the artifact was the world's one legendary. This
    /// reads the chain that was actually built, so the text cannot claim a past the panel does not
    /// show.
    /// </summary>
    private static string ProvenanceClause(
        ArtifactRarity rarity, List<ArtifactProvenance> chain, Rng rng)
    {
        if (chain.Any(e => e.Type is "conquest" or "stolen"))
            return " " + rng.Pick(Taken);

        if (rarity >= ArtifactRarity.Famed)
            return " " + rng.Pick(Ancient);

        return string.Empty;
    }

    private static readonly List<string> Taken =
    [
        "It did not come into this house by inheritance, and the house it came from has not forgotten which one it is.",
        "It changed hands in a bad year, and the family it left has counted the years since.",
        "Its present owners did not commission it. They took it, and they display it deliberately.",
    ];

    private static readonly List<string> Ancient =
    [
        "It is older than the line that holds it, and has outlasted every hand it has passed through.",
        "No one now living remembers it being made; the earliest owner anyone can name inherited it too.",
        "It has been handed down long enough that the family treats holding it as a qualification for rule.",
    ];

    /// <summary>The deceased head of a house this one has fallen out with, or null.</summary>
    private static string? FeudingRival(
        Title county, PrehistoryMap prehistory, Dictionary<string, Title> houseSeat)
    {
        if (!prehistory.CharacterHouseMap.TryGetValue(county, out var mine)) return null;

        foreach (var rel in prehistory.HouseRelations)
        {
            if (rel.Level is not ("feud" or "rivalry")) continue;

            string? other = rel.HouseA == mine ? rel.HouseB
                          : rel.HouseB == mine ? rel.HouseA
                          : null;

            if (other is null) continue;
            if (!houseSeat.TryGetValue(other, out var seat)) continue;
            if (!prehistory.DeceasedParents.TryGetValue(seat, out var theirs)) continue;

            return theirs.Id;
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------
    // Appearance, name and modifier
    // ---------------------------------------------------------------------------------------

    private readonly record struct ArtifactLook(
        string Type,
        string Visuals,
        string Family,
        string Icon,
        string Name,
        string Description,
        IReadOnlyList<(string Key, string Value)>? Signature);

    private static ArtifactLook Compose(
        ArtifactCategory category, ArtifactRarity rarity, Culture culture, Faith faith,
        Title primaryTitle, string firstName, HashSet<string> taken, Rng rng,
        IReadOnlyList<WeaponAsset>? forgedWeapons)
    {
        bool legendary = rarity == ArtifactRarity.Illustrious;

        switch (category)
        {
            case ArtifactCategory.SovereignJewels:
                return Sovereign(legendary, culture, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.MartialRelics:
                return Martial(rarity, culture, primaryTitle, firstName, taken, rng, forgedWeapons);

            case ArtifactCategory.SacredScriptures:
                return Sacred(legendary, culture, faith, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.CourtRelic:
                return CourtRelic(legendary, culture, faith, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.CourtThrone:
                return CourtThrone(legendary, culture, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.ScholarlyWorks:
            default:
                return Scholarly(legendary, culture, primaryTitle, firstName, taken, rng);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Court artifacts
    //
    // Visuals are picked from 00_court_artifacts.txt and paired with the type that file declares
    // as their own `default_type`, which matters more here than anywhere else in this writer: a
    // court visual carries a `pedestal` field naming the stand the engine draws under it, and a
    // model used in a court slot without one is rendered sitting on the floor. That is exactly what
    // the old book-on-a-lectern did, using a personal-inventory visual in a court slot.
    //
    // So every visual below is one 00_court_artifacts.txt lists with a pedestal — reliquary,
    // cross, urn, scroll, skull, riches — or one whose slot needs no stand at all, which is the
    // wall and throne pieces.
    // ---------------------------------------------------------------------------------------

    /// <summary>Pedestal visuals that declare a stand, so the model does not sit on the floor.</summary>
    private static readonly List<string> RelicVisuals =
        ["reliquary", "cross", "urn", "scroll", "human_skull", "rock", "riches", "diamond"];

    private static readonly List<string> RelicNouns =
        ["Reliquary", "Relic", "Remnant", "Vessel", "Offering", "Token"];

    private static readonly List<string> ThroneNouns = ["Throne", "Seat", "Chair", "High Seat"];

    private static ArtifactLook CourtRelic(
        bool legendary, Culture culture, Faith faith, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SacredFlourishes, SacredBase, rng) : (null, "");
        string place = primaryTitle.Name;
        string creed = faith.Name;

        var bank = new List<string>
        {
            $"The {creed} Reliquary", $"The Relic of {place}", $"The {creed} Vessel",
            $"The Holy Remnant of {place}", $"The {creed} Offering",
        };

        return new ArtifactLook(
            "pedestal", rng.Pick(RelicVisuals), "courtrelic", "piety_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, RelicNouns, false, false, taken, rng), place, firstName)
                : Claim(taken, PickFree(bank, taken, rng), place, firstName),
            legendary
                // Both written about the object rather than about where it currently sits. The
                // earlier drafts said "in the hall at {place}", which stops being true the moment
                // the relic changes hands — and a relic is among the likelier things to.
                ? $"A relic of the {creed} faith whose authenticity no one has ever been permitted to question aloud. Whichever hall holds it treats its presence as the closest thing to a promise from its god. {clause}"
                : $"A venerated {creed} relic out of {place}, displayed where petitioners can see it and be reminded whose house they are standing in.",
            fields);
    }

    private static ArtifactLook CourtThrone(
        bool legendary, Culture culture, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SovereignFlourishes, SovereignBase, rng) : (null, "");
        string place = primaryTitle.Name;

        var bank = new List<string>
        {
            $"The Throne of {place}", $"The High Seat of {place}", $"The Seat of {firstName}",
            $"The Old Throne of {place}", $"The Great Chair of {place}",
        };

        return new ArtifactLook(
            "throne", "throne", "courtthrone", "grandeur_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, ThroneNouns, false, false, taken, rng), place, firstName)
                : Claim(taken, PickFree(bank, taken, rng), place, firstName),
            legendary
                ? $"The seat of {place}, and the argument that settles every other one. Sitting in it has decided more successions than any law written down. {clause}"
                : $"The seat from which {place} is ruled — not comfortable, and not meant to be. Its occupant is meant to be looked at rather than rested.",
            fields);
    }

    // ---------------------------------------------------------------------------------------
    // Naming
    //
    // A 239-artifact world produced 201 distinct names: two legendary daggers both called "The
    // Nightfall Dagger" on a template that declares `unique = yes`, and — the real cause — 90 of
    // those 239 drawing from a *single* template each, so every ordinary scripture in the world
    // was "A Study of the <faith> Faith" and every treatise was "The Chronicles of <place>".
    //
    // Two fixes, and both are needed. Banks wide enough that collisions are rare, and a world-level
    // registry that makes them impossible. The registry is the one place this file gives up
    // per-county independence, which is why Build now iterates in index order.
    // ---------------------------------------------------------------------------------------

    // ---------------------------------------------------------------------------------------
    // Slots
    //
    // A character has exactly one weapon slot, one armour, one crown, one regalia, one journal and
    // one throne. Handing them a second artifact for a slot they have already filled produces an
    // object that can never be equipped and never does anything — and worse, it reads as an
    // oversight: a king with a famed blade should not also be carrying a plain one he will never
    // draw. Trinkets and pedestals have four apiece and are exempt.
    //
    // This was measured, not guessed: before the redraw below, seven rulers in a hundred and fifty
    // held two weapons and five held two written works.
    // ---------------------------------------------------------------------------------------

    private static readonly HashSet<string> SingleSlots =
        ["weapon", "armor", "crown", "regalia", "journal", "throne"];

    private static string SlotOf(string type) => type switch
    {
        "sword" or "axe" or "mace" or "spear" or "dagger" => "weapon",
        "helmet" => "crown",
        _ when type.StartsWith("armor_", StringComparison.Ordinal) => "armor",
        _ => type,
    };

    /// <summary>Whether an artifact of this type would go somewhere this ruler has not filled.</summary>
    private static bool SlotFree(HashSet<string> used, string type)
    {
        string slot = SlotOf(type);
        return !SingleSlots.Contains(slot) || !used.Contains(slot);
    }

    /// <summary>Draws from a bank, preferring names the world has not used yet.</summary>
    private static string PickFree(List<string> bank, HashSet<string> taken, Rng rng)
    {
        var free = bank.Where(n => !taken.Contains(n)).ToList();
        return rng.Pick(free.Count > 0 ? free : bank);
    }

    // ---------------------------------------------------------------------------------------
    // Legendary names
    //
    // The world registry made names unique WITHIN a world; it could do nothing about the same five
    // strings turning up in world after world, because the legendary banks were five fixed English
    // literals per weapon kind and a world only has three to five legendaries to spend them on.
    // "The Nightfall Dagger" was arriving in most runs.
    //
    // Three sources instead, mixed per draw:
    //
    //   Compounds, built from parts. This is how the names people actually remember are made —
    //   Oathkeeper, Widow's Wail, Heartsbane, Orphan-Maker, Longclaw, Brightroar — and a few dozen
    //   parts multiply into thousands of results rather than adding five.
    //
    //   Epithet-and-noun, for the register compounds cannot reach: "The Patient Edge" is a
    //   different kind of name from "Frostfang" and a world wants both.
    //
    //   And the culture's own invented language, which is the only source that is *structurally*
    //   incapable of repeating across worlds, because every world invents its phonology from
    //   scratch. Weighted heavily for that reason. It is the same generator the wonders and the
    //   faiths name themselves from.
    //
    // Concrete and abstract parts are kept apart on purpose. Free crossing produces "Oathtooth"
    // and "Bonemercy" at the same rate as the good ones, and a name bank that has to be read
    // before it can be trusted is not doing its job.
    // ---------------------------------------------------------------------------------------

    private static readonly List<string> ConcreteRoots =
    [
        "Iron", "Blood", "Bone", "Frost", "Ember", "Star", "Moon", "Wolf", "Raven", "Thorn",
        "Storm", "Night", "Shadow", "Sun", "Serpent", "Briar", "Salt", "Stone", "Sea", "Winter",
    ];

    private static readonly List<string> ConcreteTails =
    [
        "fang", "claw", "biter", "brand", "thorn", "reaver", "render", "drinker", "tooth", "hook",
    ];

    private static readonly List<string> AbstractRoots =
    [
        "Oath", "Sorrow", "Wrath", "Doom", "Silence", "Dawn", "Dusk", "Mercy", "Ruin", "Vigil",
        "Memory", "Exile", "Judgement", "Reckoning", "Lament", "Fury", "Patience", "Hunger",
        "Grief", "Truth",
    ];

    private static readonly List<string> AbstractTails =
    [
        "keeper", "bane", "breaker", "song", "cry", "wail", "call", "light", "fall", "ward",
        "seeker", "bringer",
    ];

    private static readonly List<string> Epithets =
    [
        "Silent", "Patient", "Unbroken", "Last", "First", "Sleepless", "Nameless", "Hollow",
        "Weeping", "Faithful", "Unyielding", "Forgotten", "Crowned", "Bitter", "Radiant", "Quiet",
        "Waking", "Drowned", "Kindly", "Wintering",
    ];

    /// <summary>A compound in the Oathkeeper / Frostfang mould, kept to one register.</summary>
    private static string Compound(Rng rng)
    {
        var (root, tail) = rng.Int(0, 1) == 0
            ? (rng.Pick(ConcreteRoots), rng.Pick(ConcreteTails))
            : (rng.Pick(AbstractRoots), rng.Pick(AbstractTails));

        // Elide a doubled letter at the join: Serpent + thorn is Serpenthorn, not Serpentthorn.
        // English compounds do this and the eye notices when they do not.
        if (char.ToLowerInvariant(root[^1]) == tail[0]) tail = tail[1..];

        return root + tail;
    }

    /// <summary>
    /// A legendary name, from whichever source this draw calls for.
    ///
    /// <paramref name="nouns"/> is what the thing IS in the categories that need saying — an Edge, a
    /// Codex, a Diadem. Weapons often do without one entirely, which is why the bare forms are here:
    /// Ice and Blackfyre do not say "sword".
    /// </summary>
    /// <param name="bare">Whether the name may stand with no noun at all. True only for weapons:
    /// Ice and Blackfyre never say "sword", but a crown that does not say crown is just a word, and
    /// an invented word on its own is the weightless case — "Sceeprardto" as the name of a
    /// legendary diadem tells a reader nothing.</param>
    /// <param name="compounds">Whether Frostfang-style compounds fit. They belong to arms and, at a
    /// stretch, regalia. On scripture they produce "The Bonefang Gospel", which is not a holy book
    /// anyone would write.</param>
    private static string LegendaryName(
        Culture culture, Title primaryTitle, List<string> nouns, bool bare, bool compounds,
        HashSet<string> taken, Rng rng)
    {
        // Two attempts before falling through to a qualified form. The registry below still
        // guarantees uniqueness; this just avoids reaching for the qualifier when a re-roll of a
        // combinatorial source would do.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // The invented word appears in about two names in five, but nearly always ATTACHED to
            // an English noun. A name has to land the first time it is read, and an invented word
            // standing alone carries no meaning to land with — "Zvaretheth" is a sound, where
            // "Zvaretheth's Edge" is a story about somebody. Bare is kept for the rare case that
            // works (Ice, Blackfyre) and kept rare.
            int roll = rng.Int(0, 99);
            string noun = rng.Pick(nouns);

            // A form this category cannot use falls through to the next one rather than being
            // re-rolled, so disallowing compounds simply moves that weight onto the epithet forms
            // instead of skewing the whole distribution toward whatever comes first.
            string candidate =
                  roll < 8 && bare      ? culture.Language.Word(rng, 2, 3)
                : roll < 26             ? $"The {culture.Language.Word(rng, 2, 3)} {noun}"

                // Reads as the possession of a figure the world remembers, which is where most
                // real legendary names come from — and it carries an English noun, so it lands
                // even when the invented half means nothing to the reader.
                : roll < 38             ? $"{culture.Language.Word(rng, 2, 3)}'s {noun}"

                : roll < 60 && compounds ? (bare ? Compound(rng) : $"The {Compound(rng)} {noun}")
                : roll < 78             ? $"The {rng.Pick(Epithets)} {noun}"
                : roll < 90             ? $"The {noun} of {rng.Pick(AbstractRoots)}"
                : $"The {rng.Pick(Epithets)} {noun} of {primaryTitle.Name}";

            if (!taken.Contains(candidate)) return candidate;
        }

        return $"The {rng.Pick(Epithets)} {rng.Pick(nouns)} of {primaryTitle.Name}";
    }

    /// <summary>
    /// Reserves a name, re-attributing it if the world already has one.
    ///
    /// The qualifier replaces an existing "of …" tail rather than stacking on it, so a second
    /// "The Lance of Itklios" becomes "The Lance of Gruftdurd" and not "The Lance of Itklios of
    /// Gruftdurd". The numbered terminator exists so the method is total; with the banks below it
    /// should never be reached.
    /// </summary>
    private static string Claim(HashSet<string> taken, string preferred, string place, string person)
    {
        if (taken.Add(preferred)) return preferred;

        foreach (var alt in new[] { Qualify(preferred, place), Qualify(preferred, person) })
            if (taken.Add(alt)) return alt;

        for (int n = 2; ; n++)
        {
            string numbered = $"{Qualify(preferred, place)} ({n})";
            if (taken.Add(numbered)) return numbered;
        }
    }

    private static string Qualify(string name, string with)
    {
        int at = name.LastIndexOf(" of ", StringComparison.Ordinal);
        return at < 0 ? $"{name} of {with}" : $"{name[..at]} of {with}";
    }

    private static readonly List<string> RegaliaNouns = ["Scepter", "Rod", "Staff", "Seal", "Standard"];
    private static readonly List<string> CrownNouns = ["Crown", "Diadem", "Circlet", "Coronet", "Wreath"];
    private static readonly List<string> BladeNouns = ["Edge", "Blade", "Fang", "Point", "Answer"];
    private static readonly List<string> ArmourNouns = ["Aegis", "Harness", "Mail", "Guard", "Shell"];
    private static readonly List<string> ScriptureNouns =
        ["Codex", "Testament", "Gospel", "Scripture", "Revelation", "Psalter", "Canon"];
    private static readonly List<string> TomeNouns =
        ["Compendium", "Almanac", "Chronicle", "Treatise", "Survey", "Commentary", "Register"];

    private static ArtifactLook Sovereign(
        bool legendary, Culture culture, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SovereignFlourishes, SovereignBase, rng) : (null, "");
        string place = primaryTitle.Name;

        if (rng.Int(0, 100) < 30)
        {
            var bank = new List<string>
            {
                $"Scepter of {place}", $"The Rod of {firstName}", $"The Regalia of {place}",
                $"The Staff of {firstName}", $"The Elder Scepter of {place}",
            };

            return new ArtifactLook(
                "regalia", "regalia", "sovereign", "grandeur_positive",
                legendary
                    ? Claim(taken, LegendaryName(culture, primaryTitle, RegaliaNouns, false, true, taken, rng), place, firstName)
                    : Claim(taken, PickFree(bank, taken, rng), place, firstName),
                legendary
                    ? $"The ultimate symbol of earthly power over {place}. Those who stand before its bearer are filled with uncontrollable awe and absolute obedience. {clause}"
                    : $"A ceremonial scepter crafted from precious metals, symbolizing de jure lordship over {place}.",
                fields);
        }

        var crowns = new List<string>
        {
            $"Crown of {place}", $"The Diadem of {firstName}", $"The {place} Circlet",
            $"The Old Crown of {place}", $"The Coronet of {firstName}",
        };

        return new ArtifactLook(
            "helmet", "crown", "sovereign", "grandeur_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, CrownNouns, false, true, taken, rng), place, firstName)
                : Claim(taken, PickFree(crowns, taken, rng), place, firstName),
            legendary
                ? $"An awe-inspiring masterpiece, rumored to have been crafted by angelic hands. It radiates an ethereal glow, asserting the divine right to rule over {place}. {clause}"
                // "worn by {firstName}" was a present-tense claim about a man who will be dead
                // within a generation and whose heir wears it next. Past tense keeps it true: the
                // crown was made for him, and remains the crown of the realm either way.
                : $"The majestic ceremonial crown of {place}, made for {firstName} to project dynastic authority.",
            fields);
    }

    private static ArtifactLook Martial(
        ArtifactRarity rarity, Culture culture, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng, IReadOnlyList<WeaponAsset>? forgedWeapons)
    {
        bool legendary = rarity == ArtifactRarity.Illustrious;

        var (fields, clause) = legendary ? Signature(MartialFlourishes, MartialBase, rng) : (null, "");
        string place = primaryTitle.Name;

        if (rng.Int(0, 100) < 20)
        {
            string[] armors = { "armor_plate", "armor_mail", "armor_scale", "armor_lamellar", "armor_laminar", "armor_brigandine" };
            string armorType = armors[rng.Int(0, armors.Length - 1)];

            var bank = new List<string>
            {
                $"The Guard of {place}", $"The Armor of {firstName}", $"The {place} Harness",
                $"{firstName}'s War-Harness", $"The Mail of {place}",
            };

            return new ArtifactLook(
                armorType, "armor", "martial", "prowess_positive",
                legendary
                    ? Claim(taken, LegendaryName(culture, primaryTitle, ArmourNouns, false, true, taken, rng), place, firstName)
                    : Claim(taken, PickFree(bank, taken, rng), place, firstName),
                legendary
                    ? $"A legendary suit of armor that seems completely untouched by blade or arrow. It was forged in secret fires and bears the eternal protection of the {culture.Name} deities. {clause}"
                    : $"A fine suit of protective mail designed in the traditional {culture.Name} pattern, bearing the heraldry of {place}.",
                fields);
        }

        // Kinds come from the asset catalogue rather than a literal list, so a kind can never be
        // rolled that has no look to wear. Every kind there resolves to at least one entity.
        var weapons = WeaponAssets.Kinds;
        string weaponKind = weapons[rng.Int(0, weapons.Count - 1)];

        // The concrete look: a specific icon and a specific 3D entity, which is what the portrait
        // draws once the artifact is equipped. See WeaponAssets for how to point one at custom art.
        // A kind with forged meshes in this world wears those instead of the catalogue: the pool
        // replaces the vanilla rows rather than joining them, so a generated blade is a shape that
        // has never existed before rather than one of eight stock models. Kinds with no parts
        // library, and every kind on a checkout with none at all, still come from the catalogue.
        var forgedOfKind = forgedWeapons?.Where(a => a.Kind == weaponKind).ToList();

        var pool = forgedOfKind is { Count: > 0 }
            ? forgedOfKind
            : WeaponAssets.ForKind(weaponKind);

        // The forged pool is split across the rarity bands, so a common sword draws only from the
        // looks forged as common ones and a legendary one draws only from the top of the pool.
        // This is the seam the whole tiering exists for: everything a band is given — its finish
        // today, decals and glow later — reaches the game through this one line. The vanilla
        // catalogue carries no bands and comes back whole, so a checkout with no parts library is
        // unaffected.
        var looks = WeaponAssets.AtTier(pool, rarity);

        var look = looks[rng.Int(0, looks.Count - 1)];

        string weaponName = weaponKind switch
        {
            "sword" => "Blade",
            "axe" => "Cleaver",
            "mace" => "Mace",
            "spear" => "Lance",
            _ => "Dagger"
        };

        var weaponBank = new List<string>
        {
            $"{firstName}'s Trusty {weaponName}",
            $"The {weaponName} of {place}",
            $"The {place} {weaponName}",
            $"{firstName}'s {weaponName}",
            $"The Old {weaponName} of {place}",
            $"The Hearth {weaponName} of {place}",
        };

        // The one category that takes a bare name. A great sword is allowed to be called Frostfang
        // and nothing else — Ice and Blackfyre never say "sword" — where a crown that does not say
        // crown is just a word.
        var blades = new List<string>(BladeNouns) { weaponName };

        // Type stays the bare kind — it decides the inventory slot and which idle animation item
        // fires. Only the visual is specialised.
        return new ArtifactLook(
            weaponKind, look.VisualKey, "martial", "prowess_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, blades, true, true, taken, rng), place, firstName)
                : Claim(taken, PickFree(weaponBank, taken, rng), place, firstName),
            legendary
                ? $"A mythical {weaponKind} of incomparable balance and terrifying power. The weapon itself hums with the memory of a thousand battlefields. {clause}"
                : $"A balanced steel {weaponKind} made for combat, decorated in classic {culture.Name} style.",
            fields);
    }

    /// <summary>
    /// Scripture and scholarship, carried rather than displayed.
    ///
    /// The type is <c>journal</c>. Not <c>book</c>, which sits in slot <c>book</c> whose only slots
    /// are <c>lectern_1</c> and <c>lectern_2</c> — both <c>category = court</c>, so a count or a
    /// duke has nowhere to put one and gets no modifier from it at all. Since two of the four
    /// categories here are written matter, that was most of the generated treasure doing nothing.
    ///
    /// <c>journal</c> is the ninth INVENTORY slot, sitting beside crown, regalia, armour, weapon
    /// and the four trinkets. Nothing gates it: the slot carries no trigger, the type carries no
    /// trigger, and <c>window_inventory.gui</c> draws <c>journal_slot</c> with no DLC condition on
    /// it. Vanilla only ever fills it from Royal Court content, which is where vanilla happens to
    /// make books rather than a restriction on the slot.
    ///
    /// The cost of preferring it over the trinket slots is that there is exactly one, so a ruler
    /// who owns two written works can only benefit from the better of them. That is the right
    /// trade: a dedicated book slot with a book icon says what the thing is, where a scripture
    /// competing with jewellery for one of four trinket slots does not, and most rulers hold at
    /// most one.
    ///
    /// It also settles the art: <c>pocket_book</c> and <c>artifact_scroll</c> live in
    /// <c>00_personal_misc.txt</c> and declare no <c>pedestal</c>, so a court slot has no stand to
    /// draw them on and drops the model on the floor.
    /// </summary>
    private static ArtifactLook Sacred(
        bool legendary, Culture culture, Faith faith, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SacredFlourishes, SacredBase, rng) : (null, "");
        string place = primaryTitle.Name;
        string creed = faith.Name;

        // Ten forms rather than one. This category alone put fifty artifacts into a single string
        // — thirteen copies of "A Study of the Deesi Faith" in one world — because the name did not
        // vary at all once the faith was fixed.
        var bank = new List<string>
            {
                $"A Study of the {creed} Faith",
                $"The {creed} Psalter",
                $"Commentaries on the {creed} Rite",
                $"The Book of Hours of {place}",
                $"The {place} Lectionary",
                $"The Lesser Canon of the {creed} Faith",
                $"Homilies for the People of {place}",
                $"The {creed} Book of Days",
                $"An Account of the {creed} Mysteries",
                $"The Devotional of {place}",
            };

        return new ArtifactLook(
            "journal",
            rng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll",
            "sacred", "piety_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, ScriptureNouns, false, false, taken, rng), place, firstName)
                : Claim(taken, PickFree(bank, taken, rng), place, firstName),
            legendary
                ? $"The pristine, original manuscript containing direct divine revelations. Its holy verses inspire unmatched devotion, and a single page is worth more than a kingdom. {clause}"
                : $"A hand-bound volume outlining the holy customs, teachings, and heritage of {place}.",
            fields);
    }

    /// <summary>Learning, on the same trinket slot and for the same reason as <see cref="Sacred"/>.</summary>
    private static ArtifactLook Scholarly(
        bool legendary, Culture culture, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(ScholarFlourishes, ScholarBase, rng) : (null, "");
        string place = primaryTitle.Name;

        var bank = new List<string>
            {
                $"The Chronicles of {place}",
                $"A History of {place}",
                $"The {place} Almanac",
                $"On the Governance of {place}",
                $"The Ledger of {firstName}",
                $"The Commonplace Book of {firstName}",
                $"A Survey of the Lands of {place}",
                $"Notes on Law and Custom in {place}",
                $"The {place} Herbal",
                // Not "kept at {place}". A name is baked into localisation once and never revisited,
                // so any claim about where the thing currently IS becomes false the first time it
                // is inherited, stolen or taken in war — and artifacts are built to move. Naming
                // the place it came FROM stays true forever, which is also how real objects are
                // named: the Book of Kells is not a statement about Dublin's present holdings.
                $"The {place} Reckoning of Years",
            };

        return new ArtifactLook(
            "journal",
            rng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll",
            "scholar", "learning_positive",
            legendary
                ? Claim(taken, LegendaryName(culture, primaryTitle, TomeNouns, false, false, taken, rng), place, firstName)
                : Claim(taken, PickFree(bank, taken, rng), place, firstName),
            legendary
                ? $"An exhaustive library of universal secrets, ancient lineages, and advanced geometries compiled by legendary scholars. Its pages contain the blueprints of civilization itself. {clause}"
                : $"A compilation of local wisdom, records, and philosophical notes commissioned during the reign of {firstName}.",
            fields);
    }

    // ---------------------------------------------------------------------------------------
    // Legendary signatures
    //
    // Modelled on how AGOT writes Valyrian steel: every named sword shares one base — prowess 9,
    // monthly_dynasty_prestige_mult 0.05, monthly_prestige 0.2 — and then carries exactly one line
    // of its own. Blackfyre is the base plus vassal_limit; Longclaw is the base plus a forest
    // advantage; Ice is the base plus a domain slot. That single differing line is what makes
    // twenty swords with identical stat blocks read as twenty *different* swords.
    //
    // Before this, every legendary in a generated world drew from one of four fixed modifier keys,
    // so the world's great treasures were four objects wearing different names. Numbers are held
    // inside the envelope vanilla's own top-tier historical artifacts use: the Reichskrone is
    // vassal_limit 25, the Ark of the Covenant is monthly_piety 2 and clergy_opinion 10, the Spear
    // of the Prophet is knight_effectiveness_mult 0.2.
    // ---------------------------------------------------------------------------------------

    private static readonly (string Key, string Value)[] MartialBase =
    [
        ("prowess_no_portrait", "9"),
        ("monthly_dynasty_prestige_mult", "0.05"),
        ("monthly_prestige", "0.2"),
    ];

    private static readonly (string Key, string Value)[] SovereignBase =
    [
        ("vassal_opinion", "10"),
        ("monthly_dynasty_prestige_mult", "0.05"),
        ("monthly_prestige", "0.2"),
    ];

    private static readonly (string Key, string Value)[] SacredBase =
    [
        ("monthly_piety", "1.0"),
        ("same_faith_opinion", "10"),
        ("monthly_dynasty_prestige_mult", "0.05"),
    ];

    private static readonly (string Key, string Value)[] ScholarBase =
    [
        ("learning", "4"),
        ("monthly_dynasty_prestige_mult", "0.05"),
        ("monthly_prestige", "0.2"),
    ];

    /// <summary>A flourish and the sentence that explains it. The prose is not decoration: an
    /// artifact whose description says nothing about what it does is a stat block with a name.</summary>
    private readonly record struct Flourish(string Clause, params (string Key, string Value)[] Fields);

    private static readonly Flourish[] MartialFlourishes =
    [
        new("Knights who ride beneath it fight as though watched.",
            ("knight_effectiveness_mult", "0.15")),
        new("Its reputation arrives some days ahead of its bearer.",
            ("dread_baseline_add", "10")),
        new("No one who has carried it has ever been called an indifferent soldier.",
            ("martial", "2")),
        new("Armies holding ground they have already taken do not give it back.",
            ("controlled_province_advantage", "5"), ("same_heritage_county_advantage_add", "3")),
        new("What its bearer takes, he keeps, and there is always more of it than expected.",
            ("max_loot_mult", "0.5"), ("raid_speed", "0.25")),
        new("It has never in its history been carried slowly.",
            ("movement_speed", "0.10")),
        new("Lands taken with it in hand have a way of staying taken.",
            ("domain_limit", "1"), ("short_reign_duration_mult", "-0.2")),
        new("A household guard forms around it without anyone giving the order.",
            ("knight_limit", "2"), ("men_at_arms_maintenance", "-0.1")),
    ];

    private static readonly Flourish[] SovereignFlourishes =
    [
        new("More lords can be held to an oath sworn before it than any chancellor can explain.",
            ("vassal_limit", "15")),
        new("A reign that begins under it is never treated as a new one.",
            ("short_reign_duration_mult", "-0.3"), ("legitimacy_gain_mult", "0.15")),
        new("The great families quarrel over precedence in its presence, which is its own kind of loyalty.",
            ("powerful_vassal_opinion", "10"), ("happy_powerful_vassal_levy_contribution_mult", "0.2")),
        new("Guests who have seen it once ask to be received again.",
            ("courtier_and_guest_opinion", "10"), ("dynasty_opinion", "8")),
        new("Envoys arrive better prepared than they intended to be.",
            ("diplomacy_per_prestige_level", "1")),
        new("Its wearer's name outlives the reign by several generations.",
            ("monthly_dynasty_prestige", "0.1"), ("monthly_prestige_gain_per_happy_powerful_vassal_mult", "0.02")),
    ];

    private static readonly Flourish[] SacredFlourishes =
    [
        new("The clergy treat its keeper as one of their own, and build cheaply for him.",
            ("clergy_opinion", "15"), ("church_holding_build_gold_cost", "-0.2")),
        new("Tithes from the faithful arrive without being asked for twice.",
            ("domain_tax_same_faith_mult", "0.15")),
        new("Devotion accumulates around it the way dust does around anything else.",
            ("monthly_piety_gain_mult", "0.25")),
        new("Levies raised in its name refill faster than the recruiters can account for.",
            ("levy_reinforcement_rate_same_faith", "0.5")),
        new("Every knight sworn beside it is worth a small prayer, and the prayers add up.",
            ("monthly_piety_gain_per_knight_mult", "0.02"), ("learning", "3")),
    ];

    private static readonly Flourish[] ScholarFlourishes =
    [
        new("Anyone who studies it properly finds the rest of their education accelerating.",
            ("learning_lifestyle_xp_gain_mult", "0.3")),
        new("The capital grows around it, though its builders work slowly and argue often.",
            ("character_capital_county_monthly_development_growth_add", "0.05"), ("build_speed", "-0.2")),
        new("Its tables of accounts and precedence have never been improved upon.",
            ("stewardship_per_prestige_level", "1"), ("diplomacy_per_prestige_level", "1")),
        new("Its chapters on regimen and physic are copied out far more often than its philosophy.",
            ("health", "0.1"), ("life_expectancy", "5")),
        new("Those who have read it are notoriously difficult to deceive.",
            ("intrigue", "3"), ("enemy_hostile_scheme_success_chance_add", "-15")),
    ];

    private static (IReadOnlyList<(string Key, string Value)> Fields, string Clause) Signature(
        Flourish[] bank, (string Key, string Value)[] shared, Rng rng)
    {
        var pick = bank[rng.Int(0, bank.Length - 1)];
        var fields = new List<(string, string)>(shared);
        fields.AddRange(pick.Fields);
        return (fields, pick.Clause);
    }
}
