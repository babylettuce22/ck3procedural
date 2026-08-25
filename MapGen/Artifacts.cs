using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public enum ArtifactCategory
{
    SovereignJewels,
    MartialRelics,
    SacredScriptures,
    ScholarlyWorks
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
}

public sealed class ArtifactMap
{
    public Dictionary<Title, List<GeneratedArtifact>> ByCounty { get; } = new();
    public List<GeneratedArtifact> AllArtifacts { get; } = new();

    /// <summary>Every one-off modifier body that needs writing, in emission order.</summary>
    public IEnumerable<GeneratedArtifact> Signatures
        => AllArtifacts.Where(a => a.ModifierFields is not null);

    public static ArtifactMap Build(
                List<Title> counties, CultureMap cultures, FaithMap faiths,
                RealmMap realms, WildernessMap wilderness, PrehistoryMap prehistory,
                MapConfig cfg, Rng rng)
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

        // Draw a fated bearer among settled counties only
        var fatedCounty = rng.Pick(settledCounties);

        // Every name this world has handed out. Two artifacts sharing a name is wrong on any of
        // them and absurd on a template that declares `unique = yes`.
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var county in settledCounties)
        {
            var culture = cultures.For(county);
            var faith = faiths.For(county);
            var primaryTitle = HistoryWriter.Primary(county, realms);

            var countyRng = new Rng(county.Index ^ 0x7E1A);
            var (firstName, _) = HistoryWriter.RulerNames(county, culture);

            var list = new List<GeneratedArtifact>();

            bool isEmperor = primaryTitle.Tier == "e";
            bool isKing = primaryTitle.Tier == "k";
            bool isDuke = primaryTitle.Tier == "d";

            int targetCount = 0;
            int roll = countyRng.Int(0, 100);

            if (isEmperor) targetCount = roll > 50 ? 4 : 3;
            else if (isKing) targetCount = roll > 40 ? 3 : 2;
            else if (isDuke) targetCount = roll > 70 ? 2 : (roll > 15 ? 1 : 0);
            else targetCount = roll > 60 ? 1 : 0;

            if (county == fatedCounty && targetCount == 0)
            {
                targetCount = 1;
            }

            for (int i = 0; i < targetCount; i++)
            {
                var artRng = new Rng((int)(county.Index ^ 0x3D7F ^ (i * 7177)));
                bool isLegendary = (county == fatedCounty && i == 0) || (artRng.Int(0, 100) < 2);

                // Rarity first, then the numbers that produce it. The reverse — which is what this
                // used to do — leaves the tier to chance, and the tier is the part of an artifact
                // the player reacts to.
                var rarity = isLegendary
                    ? ArtifactRarity.Illustrious
                    : RarityFor(primaryTitle.Tier, i, artRng);

                var (quality, wealth) = Roll(rarity, artRng);

                ArtifactCategory category;
                if (i == 0 && (isEmperor || isKing))
                {
                    category = ArtifactCategory.SovereignJewels;
                }
                else
                {
                    category = DrawCategory(headline: i == 0, artRng);
                }

                var look = Compose(
                    category, rarity, culture, faith, primaryTitle, firstName, taken, artRng);

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
    // Rarity
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What a ruler of this tier keeps, before the fated one is drawn.
    ///
    /// Each artifact after the first steps down a band: the crown is the heirloom and the rest of
    /// the strongbox is the rest of the strongbox. Without the step an emperor's four items were
    /// four equals, which is not how a treasury reads.
    /// </summary>
    private static ArtifactRarity RarityFor(string tier, int index, Rng rng)
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
        if (rng.Int(0, 11) == 0) band++;

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
    private static ArtifactCategory DrawCategory(bool headline, Rng rng)
    {
        int roll = rng.Int(0, 99);

        if (headline)
        {
            return roll < 65 ? ArtifactCategory.MartialRelics
                 : roll < 83 ? ArtifactCategory.SacredScriptures
                 : ArtifactCategory.ScholarlyWorks;
        }

        return roll < 45 ? ArtifactCategory.MartialRelics
             : roll < 73 ? ArtifactCategory.SacredScriptures
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
        Title primaryTitle, string firstName, HashSet<string> taken, Rng rng)
    {
        bool legendary = rarity == ArtifactRarity.Illustrious;

        switch (category)
        {
            case ArtifactCategory.SovereignJewels:
                return Sovereign(legendary, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.MartialRelics:
                return Martial(legendary, culture, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.SacredScriptures:
                return Sacred(legendary, faith, primaryTitle, firstName, taken, rng);

            case ArtifactCategory.ScholarlyWorks:
            default:
                return Scholarly(legendary, primaryTitle, firstName, taken, rng);
        }
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

    /// <summary>Draws from a bank, preferring names the world has not used yet.</summary>
    private static string PickFree(List<string> bank, HashSet<string> taken, Rng rng)
    {
        var free = bank.Where(n => !taken.Contains(n)).ToList();
        return rng.Pick(free.Count > 0 ? free : bank);
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

    private static ArtifactLook Sovereign(
        bool legendary, Title primaryTitle, string firstName, HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SovereignFlourishes, SovereignBase, rng) : (null, "");
        string place = primaryTitle.Name;

        if (rng.Int(0, 100) < 30)
        {
            var bank = legendary
                ? new List<string> { "The Scepter of Supreme Dominion", "The Rod of Heaven", $"The Sovereign Star of {place}", "The Unbroken Staff", $"The Writ-Rod of {place}" }
                : new List<string> { $"Scepter of {place}", $"The Rod of {firstName}", $"The Regalia of {place}", $"The Staff of {firstName}", $"The Elder Scepter of {place}" };

            return new ArtifactLook(
                "regalia", "regalia", "sovereign", "grandeur_positive",
                Claim(taken, PickFree(bank, taken, rng), place, firstName),
                legendary
                    ? $"The ultimate symbol of earthly power over {place}. Those who stand before its bearer are filled with uncontrollable awe and absolute obedience. {clause}"
                    : $"A ceremonial scepter crafted from precious metals, symbolizing de jure lordship over {place}.",
                fields);
        }

        var crowns = legendary
            ? new List<string> { "The Crown of Eternity", "The Solar Diadem", $"The Imperial Diadem of {place}", "The Unfallen Crown", $"The Starlit Crown of {place}" }
            : new List<string> { $"Crown of {place}", $"The Diadem of {firstName}", $"The {place} Circlet", $"The Old Crown of {place}", $"The Coronet of {firstName}" };

        return new ArtifactLook(
            "helmet", "crown", "sovereign", "grandeur_positive",
            Claim(taken, PickFree(crowns, taken, rng), place, firstName),
            legendary
                ? $"An awe-inspiring masterpiece, rumored to have been crafted by angelic hands. It radiates an ethereal glow, asserting the divine right to rule over {place}. {clause}"
                : $"The majestic ceremonial crown of {place}, worn by {firstName} to project dynastic authority.",
            fields);
    }

    private static ArtifactLook Martial(
        bool legendary, Culture culture, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(MartialFlourishes, MartialBase, rng) : (null, "");
        string place = primaryTitle.Name;

        if (rng.Int(0, 100) < 20)
        {
            string[] armors = { "armor_plate", "armor_mail", "armor_scale", "armor_lamellar", "armor_laminar", "armor_brigandine" };
            string armorType = armors[rng.Int(0, armors.Length - 1)];

            var bank = legendary
                ? new List<string> { $"The Aegis of {place}", "The Impervious Plate", $"The Sun-Forged Mail of {firstName}", "The Unpierced Coat", $"The Iron Vigil of {place}" }
                : new List<string> { $"The Guard of {place}", $"The Armor of {firstName}", $"The {place} Harness", $"{firstName}'s War-Harness", $"The Mail of {place}" };

            return new ArtifactLook(
                armorType, "armor", "martial", "prowess_positive",
                Claim(taken, PickFree(bank, taken, rng), place, firstName),
                legendary
                    ? $"A legendary suit of armor that seems completely untouched by blade or arrow. It was forged in secret fires and bears the eternal protection of the {culture.Name} deities. {clause}"
                    : $"A fine suit of protective mail designed in the traditional {culture.Name} pattern, bearing the heraldry of {place}.",
                fields);
        }

        string[] weapons = { "sword", "axe", "mace", "spear", "dagger" };
        string weaponKind = weapons[rng.Int(0, weapons.Length - 1)];

        string weaponName = weaponKind switch
        {
            "sword" => "Blade",
            "axe" => "Cleaver",
            "mace" => "Mace",
            "spear" => "Lance",
            _ => "Dagger"
        };

        var weaponBank = legendary
            ? weaponKind switch
            {
                "sword" => new List<string> { "The Sunslayer", "Eternity's Edge", $"The Holy Sword of {firstName}", "The Widowing", $"The Oathblade of {place}" },
                "axe" => new List<string> { "The Earthsplitter", "The Doomcleaver", "Famine", "The Reaping", $"The Red Axe of {place}" },
                "mace" => new List<string> { "The Worldcrusher", "The Skull-Render", "The Starfall Mace", "The Judgement", $"The Iron Word of {place}" },
                "spear" => new List<string> { "The Sky-Piercer", "The Last Watch", "The Thousandth Wound", "The Long Silence", $"The Standing Spear of {place}" },
                _ => new List<string> { "The Whisperer", "Death's Kiss", "The Nightfall Dagger", "The Quiet Argument", $"The Blackthorn of {place}" },
            }
            : new List<string>
            {
                $"{firstName}'s Trusty {weaponName}",
                $"The {weaponName} of {place}",
                $"The {place} {weaponName}",
                $"{firstName}'s {weaponName}",
                $"The Old {weaponName} of {place}",
                $"The Hearth {weaponName} of {place}",
            };

        return new ArtifactLook(
            weaponKind, weaponKind, "martial", "prowess_positive",
            Claim(taken, PickFree(weaponBank, taken, rng), place, firstName),
            legendary
                ? $"A mythical {weaponKind} of incomparable balance and terrifying power. The weapon itself hums with the memory of a thousand battlefields. {clause}"
                : $"A balanced steel {weaponKind} made for combat, decorated in classic {culture.Name} style.",
            fields);
    }

    /// <summary>
    /// Scripture and scholarship, carried rather than displayed.
    ///
    /// The type is <c>miscellaneous</c>, not <c>book</c>. A <c>book</c> sits in slot <c>book</c>,
    /// and the only slots of that type are <c>lectern_1</c> and <c>lectern_2</c> — both
    /// <c>category = court</c>, so a count or a duke has nowhere to put one and gets no modifier
    /// from it at all. Since two of the four categories here are written matter, that was most of
    /// the generated treasure doing nothing. <c>miscellaneous</c> goes to the trinket slots, which
    /// every character has.
    ///
    /// It also settles the art: <c>pocket_book</c> and <c>artifact_scroll</c> live in
    /// <c>00_personal_misc.txt</c> and declare no <c>pedestal</c>, so a court slot has no stand to
    /// draw them on and drops the model on the floor.
    /// </summary>
    private static ArtifactLook Sacred(
        bool legendary, Faith faith, Title primaryTitle, string firstName,
        HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(SacredFlourishes, SacredBase, rng) : (null, "");
        string place = primaryTitle.Name;
        string creed = faith.Name;

        // Ten forms rather than one. This category alone put fifty artifacts into a single string
        // — thirteen copies of "A Study of the Deesi Faith" in one world — because the name did not
        // vary at all once the faith was fixed.
        var bank = legendary
            ? new List<string>
            {
                "The Codex of Revelation",
                $"The Celestial Scrolls of the {creed} Faith",
                "The Words of the Primeval Creator",
                $"The First Testament of the {creed} Faith",
                "The Unwritten Gospel",
            }
            : new List<string>
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
            "miscellaneous",
            rng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll",
            "sacred", "piety_positive",
            Claim(taken, PickFree(bank, taken, rng), place, firstName),
            legendary
                ? $"The pristine, original manuscript containing direct divine revelations. Its holy verses inspire unmatched devotion, and a single page is worth more than a kingdom. {clause}"
                : $"A hand-bound volume outlining the holy customs, teachings, and heritage of {place}.",
            fields);
    }

    /// <summary>Learning, on the same trinket slot and for the same reason as <see cref="Sacred"/>.</summary>
    private static ArtifactLook Scholarly(
        bool legendary, Title primaryTitle, string firstName, HashSet<string> taken, Rng rng)
    {
        var (fields, clause) = legendary ? Signature(ScholarFlourishes, ScholarBase, rng) : (null, "");
        string place = primaryTitle.Name;

        var bank = legendary
            ? new List<string>
            {
                "The Opus of the Universe",
                "The Grand Compendium of the Stars",
                "The Chronicles of the First Age",
                "The Book of Every River",
                $"The Great Survey of {place}",
            }
            : new List<string>
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
                $"The Reckoning of Years, kept at {place}",
            };

        return new ArtifactLook(
            "miscellaneous",
            rng.Int(0, 1) == 0 ? "pocket_book" : "artifact_scroll",
            "scholar", "learning_positive",
            Claim(taken, PickFree(bank, taken, rng), place, firstName),
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
