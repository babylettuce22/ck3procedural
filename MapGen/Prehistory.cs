using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public sealed class ActiveWar
{
    public required string StartDate { get; init; }
    public required Title TargetTitle { get; init; }
    public required string CasusBelli { get; init; }
    public required Title AttackerCounty { get; init; }
    public required Title DefenderCounty { get; init; }
    public Title? ClaimantCounty { get; init; }
    public required string Description { get; init; }
}

public sealed class DynastyDef
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public required string LocalizedName { get; init; }
    public required string CultureKey { get; init; }
}

public sealed class DynastyHouseDef
{
    public required string Key { get; init; }
    public required string NameKey { get; init; }
    public required string LocalizedName { get; init; }
    public required string DynastyId { get; init; }
    public string? Prefix { get; init; }
}

public sealed class HistoricalCharacter
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Female { get; init; }
    public required string DynastyId { get; init; }
    public string? DynastyHouseKey { get; init; }
    public required string CultureKey { get; init; }
    public required string FaithKey { get; init; }
    public required string BirthDate { get; init; }
    public string? DeathDate { get; init; }
    public string? FatherId { get; set; }
    public string? MotherId { get; set; }
    public Title? AssociatedCounty { get; init; }
    /// <summary>Set after the children are drawn, once the faith's succession is known.</summary>
    public bool IsHeir { get; set; }

    public bool IsDeadAncestor { get; init; }
    public string? MarriageDate { get; set; }

    /// <summary>
    /// The <c>dna</c> key of a bookmark portrait, stamped on by the bookmark writer when this
    /// character is drawn beside one of its rulers; null for everyone else, who is rolled from
    /// their ethnicity as usual. Same arrangement as <see cref="Ruler.DnaKey"/>, and for the same
    /// reason: without it the wife on the bookmark screen and the wife in the campaign are two
    /// different faces.
    /// </summary>
    public string? DnaKey { get; set; }
}

public sealed class AllianceLink
{
    public required Title PartnerCounty { get; init; }
    public string? ThroughSpouseId { get; init; }
    public string? ThroughPartnerId { get; init; }
    public required string FormationDate { get; init; }
}

public sealed class DatedRelation
{
    public required Title TargetCounty { get; init; }
    public required string Date { get; init; }
}

public sealed class HouseRelationDef
{
    public required string HouseA { get; init; }
    public required string HouseB { get; init; }
    public required string Level { get; init; } // "feud", "rivalry", "quarrel", "cordial", "friendly", "amity"
    public string? StartDate { get; init; }
    public string? DescriptionKey { get; set; }
}

public sealed class PrehistoryMap
{
    public Dictionary<Title, HistoricalCharacter> Spouses { get; } = [];
    public Dictionary<Title, List<HistoricalCharacter>> Children { get; } = [];
    public Dictionary<Title, HistoricalCharacter> DeceasedParents { get; } = [];
    public Dictionary<Title, string> CharacterHouseMap { get; } = [];
    public Dictionary<Title, string> CharacterDynastyMap { get; } = [];

    public Dictionary<string, DynastyDef> Dynasties { get; } = [];
    public Dictionary<string, DynastyHouseDef> Houses { get; } = [];
    public List<HistoricalCharacter> AllExtraCharacters { get; } = [];
    public List<HouseRelationDef> HouseRelations { get; } = [];

    public Dictionary<Title, List<AllianceLink>> Alliances { get; } = [];
    public Dictionary<Title, List<DatedRelation>> Rivals { get; } = [];
    public Dictionary<Title, List<DatedRelation>> Friends { get; } = [];
    public Dictionary<Title, List<DatedRelation>> Nemeses { get; } = [];
    public Dictionary<Title, List<DatedRelation>> BloodBrothers { get; } = [];
    public Dictionary<Title, List<(Title TargetCounty, int Days)>> Truces { get; } = [];
    public Dictionary<Title, List<(Title TargetTitle, bool Pressed)>> Claims { get; } = [];

    public List<ActiveWar> ActiveWars { get; } = [];

    private const int MaxRivalsPerRuler = 2;
    private const int MaxFriendsPerRuler = 2;
    private const int MaxAlliancesPerRuler = 3;
    private const int MaxLiegeHouseMarriages = 4;

    public static PrehistoryMap Build(
        List<Title> counties,
        ProvinceMap provinces,
        int[] order,
        int landCount,
        RealmMap realms,
        CultureMap cultures,
        FaithMap faiths,
        GovernmentMap governments,
        WorldCenterMap? worldCenters,
        WildernessMap wilderness,
        MapConfig cfg,
        Rng rng)
    {
        var map = new PrehistoryMap();
        if (counties.Count == 0) return map;

        var settledCounties = counties.Where(c => !wilderness.Contains(c)).ToList();
        var countyNeighbors = BuildCountyAdjacency(settledCounties, provinces, order, landCount);
        var rulerCounties = realms.HolderCounty.Values.Distinct().Where(c => !wilderness.Contains(c)).ToList();

        // 1. Build Dynasties and Cadet Houses
        BuildDynastiesAndHouses(map, rulerCounties, realms, cultures, rng);

        // 2. Build Multi-Generational Ancestry (Deceased Parents & Sibling Bonds)
        BuildAncestryAndBrothers(map, rulerCounties, realms, cultures, faiths, cfg, rng);

        // 3. Build Adjacencies (Ruler-to-Ruler and TopLiege-to-TopLiege)
        var rulerNeighbors = BuildRulerNeighbors(rulerCounties, countyNeighbors, realms);
        var topLiegeNeighbors = BuildTopLiegeNeighbors(rulerNeighbors, realms);

        // 4. Inter-Dynastic & Intra-Realm Marriages & Children (Vassal + Liege Network)
        BuildMarriagesAndChildren(map, rulerCounties, rulerNeighbors, realms, cultures, faiths, cfg, rng);

        // 5. Border Friction, Nuanced House Relations, Truces, Claims, and Alliances
        BuildInterDynasticRelations(map, topLiegeNeighbors, realms, faiths, cultures, cfg, rng);

        // 6. Internal Realm Drama & Sibling Cadet Branches
        BuildInternalDrama(map, rulerCounties, realms, faiths, cultures, cfg, rng);

        // 6b. The Khan's Sworn Men
        BuildNomadCompanions(map, rulerCounties, realms, governments, cfg);

        // 7. Contested World Centers
        if (worldCenters is not null)
        {
            foreach (var center in worldCenters.Centers)
            {
                if (!countyNeighbors.TryGetValue(center.County, out var neighbors)) continue;

                foreach (var neighborCounty in neighbors)
                {
                    var topNeighbor = TopLiegeCounty(neighborCounty, realms);
                    var topCenter = TopLiegeCounty(center.County, realms);

                    if (topNeighbor != topCenter)
                    {
                        var covetRng = new Rng(topNeighbor.Index ^ center.County.Index);
                        if (covetRng.Chance(0.50))
                        {
                            AddClaim(map, topNeighbor, center.County, pressed: false);
                        }
                    }
                }
            }
        }

        // 8. Active Starting Wars
        if (cfg.EnableStartingWars && topLiegeNeighbors.Count > 0)
        {
            GenerateActiveWars(map, topLiegeNeighbors, realms, faiths, cultures, worldCenters, cfg, rng);
        }

        int totalDynasties = map.Dynasties.Count;
        int totalHouses = map.Houses.Count;
        int totalFeuds = map.HouseRelations.Count(r => r.Level is "feud");
        int totalRivalries = map.HouseRelations.Count(r => r.Level is "rivalry" or "quarrel");
        int totalAmities = map.HouseRelations.Count(r => r.Level is "amity" or "friendly" or "cordial");
        int totalChildren = map.Children.Values.Sum(c => c.Count);

        Console.WriteLine($"  pre-history: {totalDynasties} dynasties, {totalHouses} houses ({totalHouses - totalDynasties} cadet branches), " +
                          $"{map.Spouses.Count} marriages, {totalChildren} heirs/children, {totalFeuds} blood feuds, {totalRivalries} rivalries/quarrels, " +
                          $"{totalAmities} amities/friendships (remaining dynasties indifferent), {map.ActiveWars.Count} active wars");

        return map;
    }

    private static void BuildDynastiesAndHouses(
        PrehistoryMap map,
        List<Title> rulerCounties,
        RealmMap realms,
        CultureMap cultures,
        Rng rng)
    {
        foreach (var county in rulerCounties)
        {
            var primary = HistoryWriter.Primary(county, realms);
            bool isTopLiege = !realms.Liege.ContainsKey(primary);

            if (isTopLiege)
            {
                CreateDynastyAndMainHouse(map, county, cultures);
            }
        }

        foreach (var county in rulerCounties)
        {
            if (!map.CharacterDynastyMap.ContainsKey(county))
            {
            var primary = HistoryWriter.Primary(county, realms);
            var liegeCounty = TopLiegeCounty(county, realms);

            var vRng = new Rng(county.Index ^ 0x48A1);
            bool isHighVassal = primary.Tier is "d" or "k";

            // Cadet branches are rare on purpose. At two in five, most of the map's dukes turned out
            // to be relatives of their king, so nearly every war was a family quarrel and the
            // dynasty screen was one tree; at one in eight they are the exception they should be.
            //
            // A cadet always founds its own house — sharing the liege's outright made the branch
            // invisible, which is the one thing a cadet branch is for.
            if (isHighVassal && map.CharacterDynastyMap.TryGetValue(liegeCounty, out var liegeDynastyId)
                && vRng.Chance(0.12))
            {
                map.CharacterDynastyMap[county] = liegeDynastyId;

                var culture = cultures.For(county);
                string houseName = culture.DynastyNames.Count > 0
                    ? culture.DynastyNames[vRng.Int(0, culture.DynastyNames.Count - 1)]
                    : $"{culture.Name}_{county.Index}";

                string houseKey = $"house_gen_{county.Index}";
                string houseNameKey = $"dynn_gen_house_{county.Index}";
                string? prefix = CulturePrefix(culture.Key);

                map.Houses[houseKey] = new DynastyHouseDef
                {
                    Key = houseKey,
                    NameKey = houseNameKey,
                    LocalizedName = houseName,
                    DynastyId = liegeDynastyId,
                    Prefix = prefix
                };
                map.CharacterHouseMap[county] = houseKey;
            }
            else
            {
                CreateDynastyAndMainHouse(map, county, cultures);
            }
            }
        }
    }

    private static void CreateDynastyAndMainHouse(PrehistoryMap map, Title county, CultureMap cultures)
    {
        var culture = cultures.For(county);
        var cRng = new Rng(county.Index ^ 0x33A9);

        string dynName = culture.DynastyNames.Count > 0
            ? culture.DynastyNames[cRng.Int(0, culture.DynastyNames.Count - 1)]
            : $"{culture.Name}_{county.Index}";

        string dynId = $"gen_dynasty_{county.Index}";
        string dynNameKey = $"dynn_gen_{county.Index}";

        map.Dynasties[dynId] = new DynastyDef
        {
            Id = dynId,
            NameKey = dynNameKey,
            LocalizedName = dynName,
            CultureKey = culture.Key
        };

        string houseKey = $"house_gen_{county.Index}";
        string? prefix = CulturePrefix(culture.Key);

        map.Houses[houseKey] = new DynastyHouseDef
        {
            Key = houseKey,
            NameKey = dynNameKey,
            LocalizedName = dynName,
            DynastyId = dynId,
            Prefix = prefix
        };

        map.CharacterDynastyMap[county] = dynId;
        map.CharacterHouseMap[county] = houseKey;
    }

    /// <summary>
    /// One given name from the culture's own list for that sex.
    ///
    /// One draw whichever list it reads, so a world whose rulers are women walks the same streams
    /// in the same order as one whose rulers are men and differs only in the names that come out.
    /// </summary>
    private static string GivenName(Culture culture, bool female, Rng rng)
    {
        var names = female ? culture.FemaleNames : culture.MaleNames;
        return names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : female ? "Nullberta" : "Nullbert";
    }

    private static string? CulturePrefix(string cultureKey)
    {
        if (cultureKey.Contains("french") || cultureKey.Contains("norman") || cultureKey.Contains("breton") || cultureKey.Contains("occitan"))
            return "dynnp_de";
        if (cultureKey.Contains("german") || cultureKey.Contains("saxon") || cultureKey.Contains("bavarian") || cultureKey.Contains("franconian"))
            return "dynnp_von";
        if (cultureKey.Contains("dutch") || cultureKey.Contains("frisian"))
            return "dynnp_van";
        if (cultureKey.Contains("italian") || cultureKey.Contains("cisalpine"))
            return "dynnp_da";
        if (cultureKey.Contains("bedouin") || cultureKey.Contains("levantine") || cultureKey.Contains("yemeni"))
            return "dynnp_al-";
        return null;
    }

    private static void BuildAncestryAndBrothers(
        PrehistoryMap map,
        List<Title> rulerCounties,
        RealmMap realms,
        CultureMap cultures,
        FaithMap faiths,
        MapConfig cfg,
        Rng rng)
    {
        // Grouped by realm rather than walked flat, because a shared parent is a fact about a realm:
        // whether two rulers are brothers depends on who they both answer to.
        var byTopLiege = new Dictionary<Title, List<Title>>();

        foreach (var county in rulerCounties)
        {
            var top = TopLiegeCounty(county, realms);
            if (!byTopLiege.TryGetValue(top, out var members)) byTopLiege[top] = members = [];
            members.Add(county);
        }

        foreach (var (topLiege, realmCounties) in byTopLiege)
        {
            var culture = cultures.For(topLiege);
            var faith = faiths.For(topLiege);
            var topTitle = HistoryWriter.Primary(topLiege, realms);

            var topRng = new Rng(topLiege.Index ^ 0x7E1B);
            int topBirthYear = HistoryWriter.GetRulerBirthYear(topLiege.Index, cfg.StartYear);

            // The line runs through the parent of the ruler's own sex: a countess is her mother's
            // daughter, and the house descends the way the world's laws say land does.
            bool topFemale = HistoryWriter.RulerIsFemale(topLiege, faith);
            string topParentName = GivenName(culture, topFemale, topRng);

            int topParentBirth = topBirthYear - topRng.Int(22, 35);
            int topParentDeath = cfg.StartYear - topRng.Int(2, 12);

            var topParent = new HistoricalCharacter
            {
                Id = $"gen_char_parent_{topLiege.Index}",
                Name = topParentName,
                Female = topFemale,
                DynastyId = map.CharacterDynastyMap[topLiege],
                DynastyHouseKey = map.CharacterHouseMap[topLiege],
                CultureKey = culture.Key,
                FaithKey = faith.Key,
                BirthDate = $"{topParentBirth}.{topRng.Int(1, 12)}.{topRng.Int(1, 28)}",
                DeathDate = $"{topParentDeath}.{topRng.Int(1, 12)}.{topRng.Int(1, 28)}",
                AssociatedCounty = topLiege,
                IsDeadAncestor = true
            };

            map.DeceasedParents[topLiege] = topParent;
            map.AllExtraCharacters.Add(topParent);

            // Only vassals of the same dynasty can be the liege's brother, highest tier first so the
            // brother is the realm's second man rather than whichever county came up first.
            var kin = realmCounties
                .Where(c => c != topLiege
                            && map.CharacterDynastyMap.GetValueOrDefault(c) == map.CharacterDynastyMap[topLiege])
                .OrderByDescending(c => HistoryWriter.Rank(HistoryWriter.Primary(c, realms)))
                .ToList();

            // At most one. Sharing a parent across a whole realm produced courts of a dozen
            // siblings, which is both wrong and slow to draw.
            bool brotherTaken = false;

            foreach (var kinCounty in kin)
            {
                int kinBirthYear = HistoryWriter.GetRulerBirthYear(kinCounty.Index, cfg.StartYear);
                int ageGap = Math.Abs(topBirthYear - kinBirthYear);
                var kinTitle = HistoryWriter.Primary(kinCounty, realms);
                var kinRng = new Rng(kinCounty.Index ^ 0x481A);

                if (!brotherTaken && ageGap <= 10 && kinRng.Chance(0.4))
                {
                    map.DeceasedParents[kinCounty] = topParent;
                    brotherTaken = true;

                    // Brothers of consequence hold claims on each other. Only from a duchy or a
                    // kingdom: a count with a claim on his brother's county is a border squabble,
                    // while a duke with one on the realm is a succession crisis.
                    if (kinTitle.Tier is "d" or "k")
                    {
                        AddClaim(map, kinCounty, topTitle, pressed: false);

                        // And sometimes the elder brother wants what the younger holds.
                        if (kinRng.Chance(0.5)) AddClaim(map, topLiege, kinTitle, pressed: false);
                    }

                    continue;
                }

                // Belt and braces, matching the sweep at the end of this method. Nothing should
                // reach here twice now that TopLiegeCounty climbs by holder — a county is a member
                // of exactly one realm and is never also another realm's key — but a second parent
                // for one county does not fail, it writes two characters under one id and CK3 loads
                // whichever it read last.
                if (map.DeceasedParents.ContainsKey(kinCounty)) continue;

                var kinCulture = cultures.For(kinCounty);
                var kinFaith = faiths.For(kinCounty);

                bool kinFemale = HistoryWriter.RulerIsFemale(kinCounty, kinFaith);
                string kinParentName = GivenName(kinCulture, kinFemale, kinRng);

                int kinParentBirth = kinBirthYear - kinRng.Int(22, 35);
                int kinParentDeath = cfg.StartYear - kinRng.Int(2, 16);

                var kinParent = new HistoricalCharacter
                {
                    Id = $"gen_char_parent_{kinCounty.Index}",
                    Name = kinParentName,
                    Female = kinFemale,
                    DynastyId = map.CharacterDynastyMap[kinCounty],
                    DynastyHouseKey = map.CharacterHouseMap[kinCounty],
                    CultureKey = kinCulture.Key,
                    FaithKey = kinFaith.Key,
                    BirthDate = $"{kinParentBirth}.{kinRng.Int(1, 12)}.{kinRng.Int(1, 28)}",
                    DeathDate = $"{kinParentDeath}.{kinRng.Int(1, 12)}.{kinRng.Int(1, 28)}",
                    AssociatedCounty = kinCounty,
                    IsDeadAncestor = true
                };

                map.DeceasedParents[kinCounty] = kinParent;
                map.AllExtraCharacters.Add(kinParent);
            }
        }

        // Everyone the grouping missed — rulers of another dynasty, and any county whose realm was
        // not walked — still needs a parent, or their house begins with them and nothing inherits.
        foreach (var county in rulerCounties)
        {
            if (map.DeceasedParents.ContainsKey(county)) continue;

            var culture = cultures.For(county);
            var faith = faiths.For(county);
            var fRng = new Rng(county.Index ^ 0x981C);

            int birthYear = HistoryWriter.GetRulerBirthYear(county.Index, cfg.StartYear);
            int parentBirth = birthYear - fRng.Int(22, 35);
            int parentDeath = cfg.StartYear - fRng.Int(2, 15);

            bool female = HistoryWriter.RulerIsFemale(county, faith);
            string parentName = GivenName(culture, female, fRng);

            var parent = new HistoricalCharacter
            {
                Id = $"gen_char_parent_{county.Index}",
                Name = parentName,
                Female = female,
                DynastyId = map.CharacterDynastyMap[county],
                DynastyHouseKey = map.CharacterHouseMap[county],
                CultureKey = culture.Key,
                FaithKey = faith.Key,
                BirthDate = $"{parentBirth}.{fRng.Int(1, 12)}.{fRng.Int(1, 28)}",
                DeathDate = $"{parentDeath}.{fRng.Int(1, 12)}.{fRng.Int(1, 28)}",
                AssociatedCounty = county,
                IsDeadAncestor = true
            };

            map.DeceasedParents[county] = parent;
            map.AllExtraCharacters.Add(parent);
        }
    }

    private static void BuildMarriagesAndChildren(
        PrehistoryMap map,
        List<Title> rulerCounties,
        Dictionary<Title, HashSet<Title>> rulerNeighbors,
        RealmMap realms,
        CultureMap cultures,
        FaithMap faiths,
        MapConfig cfg,
        Rng rng)
    {
        // Group vassals by their top liege
        var vassalsByLiege = new Dictionary<Title, List<Title>>();
        foreach (var county in rulerCounties)
        {
            var liege = TopLiegeCounty(county, realms);
            if (!vassalsByLiege.TryGetValue(liege, out var list))
                vassalsByLiege[liege] = list = [];
            if (county != liege) list.Add(county);
        }

        // Sort rulers: Top Lieges and Higher Tier Rulers marry first
        var sortedRulers = rulerCounties
            .OrderByDescending(r => HistoryWriter.Rank(HistoryWriter.Primary(r, realms)))
            .ThenBy(r => r.Index)
            .ToList();

        var marriedRulers = new HashSet<Title>();

        // Each marriage into the top liege's house is an alliance the liege must carry; without a
        // cap a large empire handed its emperor one alliance per vassal (50+ observed).
        var liegeHouseMarriages = new Dictionary<Title, int>();

        foreach (var ruler in sortedRulers)
        {
            if (marriedRulers.Contains(ruler)) continue;

            var rulerFaith = faiths.For(ruler);
            var rulerCulture = cultures.For(ruler);
            bool rulerFemale = HistoryWriter.RulerIsFemale(ruler, rulerFaith);
            var mRng = new Rng(ruler.Index ^ 0x6E19);

            if (!mRng.Chance(0.88)) continue;

            int rulerBirthYear = HistoryWriter.GetRulerBirthYear(ruler.Index, cfg.StartYear);
            var topLiege = TopLiegeCounty(ruler, realms);
            bool isTopLiege = (ruler == topLiege);

            Title? spouseOriginCounty = null;
            var neighbors = rulerNeighbors.GetValueOrDefault(ruler, []);

            // === 1. TOP LIEGE MARRIAGE SELECTION ===
            if (isTopLiege)
            {
                // A) Foreign Sovereign Neighbor (Inter-Realm Alliance)
                var foreignEligible = neighbors
                    .Where(n => TopLiegeCounty(n, realms) != topLiege &&
                                faiths.For(n).Religion == rulerFaith.Religion &&
                                map.CharacterHouseMap.GetValueOrDefault(n) != map.CharacterHouseMap.GetValueOrDefault(ruler))
                    .ToList();

                // B) Powerful Internal Vassal House (Internal Realm Stability)
                var internalVassals = vassalsByLiege.GetValueOrDefault(ruler, [])
                    .Where(v => map.CharacterHouseMap.GetValueOrDefault(v) != map.CharacterHouseMap.GetValueOrDefault(ruler))
                    .ToList();

                if (foreignEligible.Count > 0 && mRng.Chance(0.55))
                {
                    spouseOriginCounty = foreignEligible[mRng.Int(0, foreignEligible.Count - 1)];
                }
                else if (internalVassals.Count > 0 && mRng.Chance(0.65))
                {
                    spouseOriginCounty = internalVassals[mRng.Int(0, internalVassals.Count - 1)];
                }
                else if (foreignEligible.Count > 0)
                {
                    spouseOriginCounty = foreignEligible[mRng.Int(0, foreignEligible.Count - 1)];
                }
            }
            // === 2. VASSAL MARRIAGE SELECTION ===
            else
            {
                // A) Liege's Royal House (Liege-Vassal Alliance)
                bool canMarryLiege = map.CharacterHouseMap.GetValueOrDefault(topLiege) != map.CharacterHouseMap.GetValueOrDefault(ruler) &&
                                     liegeHouseMarriages.GetValueOrDefault(topLiege) < MaxLiegeHouseMarriages;

                // B) Fellow Co-Vassals (Intra-Realm Alliance)
                var coVassals = vassalsByLiege.GetValueOrDefault(topLiege, [])
                    .Where(v => v != ruler && map.CharacterHouseMap.GetValueOrDefault(v) != map.CharacterHouseMap.GetValueOrDefault(ruler))
                    .ToList();

                // C) External Border Neighbor
                var foreignBorder = neighbors
                    .Where(n => TopLiegeCounty(n, realms) != topLiege &&
                                faiths.For(n).Religion == rulerFaith.Religion &&
                                map.CharacterHouseMap.GetValueOrDefault(n) != map.CharacterHouseMap.GetValueOrDefault(ruler))
                    .ToList();

                if (canMarryLiege && mRng.Chance(0.35))
                {
                    spouseOriginCounty = topLiege;
                }
                else if (coVassals.Count > 0 && mRng.Chance(0.50))
                {
                    spouseOriginCounty = coVassals[mRng.Int(0, coVassals.Count - 1)];
                }
                else if (foreignBorder.Count > 0 && mRng.Chance(0.40))
                {
                    spouseOriginCounty = foreignBorder[mRng.Int(0, foreignBorder.Count - 1)];
                }
                else if (canMarryLiege)
                {
                    spouseOriginCounty = topLiege;
                }
                else if (coVassals.Count > 0)
                {
                    spouseOriginCounty = coVassals[mRng.Int(0, coVassals.Count - 1)];
                }

                if (spouseOriginCounty == topLiege)
                    liegeHouseMarriages[topLiege] = liegeHouseMarriages.GetValueOrDefault(topLiege) + 1;
            }

            // === 3. GENERATE SPOUSE CHARACTER ===
            string spouseDynasty;
            string? spouseHouse;
            string? spouseParent = null;
            bool spouseParentIsMother = false;
            Culture spouseCulture;
            Faith spouseFaith;
            int spouseBirthYear = cfg.StartYear - mRng.Int(20, 44);

            if (spouseOriginCounty != null)
            {
                spouseCulture = cultures.For(spouseOriginCounty);
                spouseFaith = faiths.For(spouseOriginCounty);
                spouseDynasty = map.CharacterDynastyMap.GetValueOrDefault(spouseOriginCounty, map.CharacterDynastyMap[ruler]);
                spouseHouse = map.CharacterHouseMap.GetValueOrDefault(spouseOriginCounty);

                // The match must be close kin of the origin ruler or the engine dissolves the
                // marriage alliance on the first tick — house membership alone is not kinship.
                // Sharing that ruler's deceased parent makes them siblings.
                if (map.DeceasedParents.TryGetValue(spouseOriginCounty, out var df))
                {
                    spouseParent = df.Id;
                    spouseParentIsMother = df.Female;
                    int parentBirthYear = int.Parse(df.BirthDate.Split('.')[0]);
                    spouseBirthYear = Math.Max(spouseBirthYear, parentBirthYear + 17);
                }
            }
            else
            {
                // Fallback: Generate a distinct local noble house so rulers never marry their own dynasty
                spouseCulture = rulerCulture;
                spouseFaith = rulerFaith;

                string nobleDynName = spouseCulture.DynastyNames.Count > 1
                    ? spouseCulture.DynastyNames[(ruler.Index + 3) % spouseCulture.DynastyNames.Count]
                    : $"{spouseCulture.Name}court_{ruler.Index}";

                spouseDynasty = $"gen_dynasty_noble_{ruler.Index}";
                spouseHouse = $"house_gen_noble_{ruler.Index}";
                string nobleNameKey = $"dynn_gen_noble_{ruler.Index}";

                if (!map.Dynasties.ContainsKey(spouseDynasty))
                {
                    map.Dynasties[spouseDynasty] = new DynastyDef
                    {
                        Id = spouseDynasty,
                        NameKey = nobleNameKey,
                        LocalizedName = nobleDynName,
                        CultureKey = spouseCulture.Key
                    };
                    map.Houses[spouseHouse] = new DynastyHouseDef
                    {
                        Key = spouseHouse,
                        NameKey = nobleNameKey,
                        LocalizedName = nobleDynName,
                        DynastyId = spouseDynasty,
                        Prefix = CulturePrefix(spouseCulture.Key)
                    };
                }
            }

            // CK3 has no doctrine on a generated faith that permits a same-sex marriage, so the
            // consort is whatever the ruler is not.
            bool spouseFemale = !rulerFemale;
            string spouseName = GivenName(spouseCulture, spouseFemale, mRng);

            int earliestMarriageYear = Math.Max(rulerBirthYear + 16, spouseBirthYear + 16);
            int marriageYear = Math.Min(cfg.StartYear - 2, earliestMarriageYear + mRng.Int(0, 8));
            string weddingDate = $"{marriageYear}.{mRng.Int(1, 12)}.{mRng.Int(1, 28)}";

            var spouse = new HistoricalCharacter
            {
                Id = $"gen_char_spouse_{ruler.Index}",
                Name = spouseName,
                Female = spouseFemale,
                DynastyId = spouseDynasty,
                DynastyHouseKey = spouseHouse,
                CultureKey = spouseCulture.Key,
                FaithKey = spouseFaith.Key,
                BirthDate = $"{spouseBirthYear}.{mRng.Int(1, 12)}.{mRng.Int(1, 28)}",

                // Which side of the parentage this hangs on is the dead parent's sex, not the
                // ruler's: under a matriarchy the sibling this consort was drawn from descends from
                // a mother, and hanging it on `father` would point them at a woman.
                FatherId = spouseParentIsMother ? null : spouseParent,
                MotherId = spouseParentIsMother ? spouseParent : null,
                AssociatedCounty = ruler,
                MarriageDate = weddingDate
            };

            map.Spouses[ruler] = spouse;
            map.AllExtraCharacters.Add(spouse);
            marriedRulers.Add(ruler);

            // Establish Alliance and House Amity if married into an existing ruler's house.
            // No kinship link means no alliance: the engine would only dissolve it again.
            if (spouseOriginCounty != null && spouseParent != null)
            {
                AddMarriageAlliance(map, ruler, spouseOriginCounty, spouse.Id, weddingDate);

                if (map.CharacterHouseMap.TryGetValue(ruler, out var hA) &&
                    map.CharacterHouseMap.TryGetValue(spouseOriginCounty, out var hB) && hA != hB)
                {
                    map.HouseRelations.Add(new HouseRelationDef
                    {
                        HouseA = hA,
                        HouseB = hB,
                        Level = "amity",
                        StartDate = weddingDate
                    });
                }
            }
        }

        // === 4. GENERATE CHILDREN & HEIRS ===
        foreach (var (ruler, spouse) in map.Spouses)
        {
            var culture = cultures.For(ruler);
            var faith = faiths.For(ruler);
            bool rulerFemale = HistoryWriter.RulerIsFemale(ruler, faith);
            var cRng = new Rng(ruler.Index ^ 0x51E3);

            int childCount = cRng.Int(1, 3);
            var childrenList = new List<HistoricalCharacter>();

            int weddingYear = spouse.MarriageDate != null
                ? int.Parse(spouse.MarriageDate.Split('.')[0])
                : cfg.StartYear - 10;

            for (int i = 0; i < childCount; i++)
            {
                bool isFemale = cRng.Chance(0.48);
                string childName = GivenName(culture, isFemale, cRng);

                int birthYear = Math.Min(cfg.StartYear, weddingYear + 1 + (i * cRng.Int(2, 4)) + cRng.Int(0, 2));

                var child = new HistoricalCharacter
                {
                    Id = $"gen_char_child_{ruler.Index}_{i}",
                    Name = childName,
                    Female = isFemale,
                    DynastyId = map.CharacterDynastyMap[ruler],
                    DynastyHouseKey = map.CharacterHouseMap[ruler],
                    CultureKey = culture.Key,
                    FaithKey = faith.Key,
                    BirthDate = $"{birthYear}.{cRng.Int(1, 12)}.{cRng.Int(1, 28)}",
                    FatherId = rulerFemale ? spouse.Id : HistoryWriter.CharacterId(ruler),
                    MotherId = rulerFemale ? HistoryWriter.CharacterId(ruler) : spouse.Id,
                    AssociatedCounty = ruler,
                    IsHeir = false
                };

                childrenList.Add(child);
                map.AllExtraCharacters.Add(child);
            }

            // Assign Primary Heir by what the family's faith says succession is.
            //
            // The list is in birth order, so "first that qualifies" is the eldest that qualifies —
            // which is what preference means. A house with no child of the favoured sex falls back
            // to the eldest of any, exactly as the game would when the preferred line runs out.
            //
            // Marked in place rather than replaced with a copy carrying the flag. The copy went into
            // Children while AllExtraCharacters kept the original, so one child in the world existed
            // as two objects that agreed about everything except this flag — and anything stamped
            // onto the one in Children (a portrait key, say) never reached the one the character
            // file writes.
            var designatedHeir = Faiths.GenderOf(faith) switch
            {
                "doctrine_gender_female_dominated" => childrenList.FirstOrDefault(c => c.Female),
                "doctrine_gender_equal" => childrenList.FirstOrDefault(),
                _ => childrenList.FirstOrDefault(c => !c.Female),
            } ?? childrenList.FirstOrDefault();
            if (designatedHeir != null) designatedHeir.IsHeir = true;

            map.Children[ruler] = childrenList;
        }
    }

    private static void AddMarriageAlliance(PrehistoryMap map, Title rulerCounty, Title spouseOriginCounty, string spouseId, string weddingDate)
    {
        if (!map.Alliances.TryGetValue(rulerCounty, out var listA)) map.Alliances[rulerCounty] = listA = [];
        if (!map.Alliances.TryGetValue(spouseOriginCounty, out var listB)) map.Alliances[spouseOriginCounty] = listB = [];

        // The through-characters must be the wedded pair itself: the ruler on their own side, and
        // the consort — the ruler's spouse and the origin ruler's sibling — on their family's side.
        // Anything else (e.g. the origin ruler directly) fails the engine's marriage-alliance check
        // and the alliance is dissolved on the first tick after game start.
        string rulerId = HistoryWriter.CharacterId(rulerCounty);

        if (!listA.Any(al => al.PartnerCounty == spouseOriginCounty))
            listA.Add(new AllianceLink { PartnerCounty = spouseOriginCounty, ThroughSpouseId = rulerId, ThroughPartnerId = spouseId, FormationDate = weddingDate });
        if (!listB.Any(al => al.PartnerCounty == rulerCounty))
            listB.Add(new AllianceLink { PartnerCounty = rulerCounty, ThroughSpouseId = spouseId, ThroughPartnerId = rulerId, FormationDate = weddingDate });
    }

    private static void AddDirectAlliance(PrehistoryMap map, Title a, Title b, string allianceDate)
    {
        if (!map.Alliances.TryGetValue(a, out var listA)) map.Alliances[a] = listA = [];
        if (!map.Alliances.TryGetValue(b, out var listB)) map.Alliances[b] = listB = [];

        string charA = HistoryWriter.CharacterId(a);
        string charB = HistoryWriter.CharacterId(b);

        if (!listA.Any(al => al.PartnerCounty == b))
            listA.Add(new AllianceLink { PartnerCounty = b, ThroughSpouseId = charA, ThroughPartnerId = charB, FormationDate = allianceDate });
        if (!listB.Any(al => al.PartnerCounty == a))
            listB.Add(new AllianceLink { PartnerCounty = a, ThroughSpouseId = charB, ThroughPartnerId = charA, FormationDate = allianceDate });
    }

    private static void BuildInterDynasticRelations(
        PrehistoryMap map,
        Dictionary<Title, HashSet<Title>> topLiegeNeighbors,
        RealmMap realms,
        FaithMap faiths,
        CultureMap cultures,
        MapConfig cfg,
        Rng rng)
    {
        foreach (var (ruler, neighbors) in topLiegeNeighbors)
        {
            var primaryTitle = HistoryWriter.Primary(ruler, realms);
            var rulerFaith = faiths.For(ruler);
            var rulerRng = new Rng(ruler.Index ^ 0x7B2F);

            foreach (var otherRuler in neighbors)
            {
                if (ruler.Index >= otherRuler.Index) continue;

                var otherPrimary = HistoryWriter.Primary(otherRuler, realms);
                var otherFaith = faiths.For(otherRuler);

                bool sameFaith = rulerFaith == otherFaith;
                bool sameReligion = rulerFaith.Religion == otherFaith.Religion;

                // Hostile Religions -> 25% rivalry/quarrel, only 5% blood feud
                if (!sameReligion)
                {
                    int disputeYear = Math.Max(1, cfg.StartYear - rulerRng.Int(4, 15));
                    string disputeDate = $"{disputeYear}.{rulerRng.Int(1, 12)}.{rulerRng.Int(1, 28)}";

                    if (CanAddRival(map, ruler, otherRuler) && rulerRng.Chance(0.25))
                    {
                        AddRivalry(map, ruler, otherRuler, disputeDate);

                        if (map.CharacterHouseMap.TryGetValue(ruler, out var hA) &&
                            map.CharacterHouseMap.TryGetValue(otherRuler, out var hB) && hA != hB)
                        {
                            string level = rulerRng.Chance(0.20) ? "feud" : rulerRng.Chance(0.50) ? "rivalry" : "quarrel";
                            map.HouseRelations.Add(new HouseRelationDef
                            {
                                HouseA = hA,
                                HouseB = hB,
                                Level = level,
                                StartDate = disputeDate
                            });
                        }
                    }

                    if (rulerRng.Chance(0.18))
                    {
                        int truceDays = rulerRng.Int(365, 1825);
                        AddTruce(map, ruler, otherRuler, truceDays);
                        AddClaim(map, ruler, otherPrimary, pressed: true);
                    }
                }
                // Same Faith -> Cordial, Alliances, Friendships, or Indifferent
                else if (sameFaith)
                {
                    if (CanAddAlliance(map, ruler, otherRuler) && rulerRng.Chance(0.25))
                    {
                        int allianceYear = Math.Max(1, cfg.StartYear - rulerRng.Int(2, 10));
                        string allianceDate = $"{allianceYear}.{rulerRng.Int(1, 12)}.{rulerRng.Int(1, 28)}";
                        AddDirectAlliance(map, ruler, otherRuler, allianceDate);
                    }
                    if (CanAddFriend(map, ruler, otherRuler) && rulerRng.Chance(0.20))
                    {
                        int friendYear = Math.Max(1, cfg.StartYear - rulerRng.Int(3, 12));
                        string friendDate = $"{friendYear}.{rulerRng.Int(1, 12)}.{rulerRng.Int(1, 28)}";
                        AddFriendship(map, ruler, otherRuler, friendDate);
                    }

                    if (rulerRng.Chance(0.15) &&
                        map.CharacterHouseMap.TryGetValue(ruler, out var hA) &&
                        map.CharacterHouseMap.TryGetValue(otherRuler, out var hB) && hA != hB)
                    {
                        int cordialYear = Math.Max(1, cfg.StartYear - rulerRng.Int(2, 10));
                        string cordialDate = $"{cordialYear}.{rulerRng.Int(1, 12)}.{rulerRng.Int(1, 28)}";
                        map.HouseRelations.Add(new HouseRelationDef
                        {
                            HouseA = hA,
                            HouseB = hB,
                            Level = "cordial",
                            StartDate = cordialDate
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Binds a khan to the strongest of his vassals as sworn friends and blood brothers.
    ///
    /// Nomad realms are the one place CK3 scores a liege's personal ties directly:
    /// <c>obedience_value</c> pays +100 for a friend and +1250 for a blood brother, against a
    /// threshold of 100 for a king. A vassal with no tie to his khan instead takes -50 simply for
    /// having no good relationship, which — with the kurultai, dread and legitimacy penalties on top
    /// — is why every generated horde started with a court of Disobedient strangers.
    ///
    /// Deliberately partial. Roughly the top half of each horde is sworn to its khan and the rest is
    /// left to be won over, which is the mechanic doing its job rather than being switched off.
    /// </summary>
    private static void BuildNomadCompanions(
        PrehistoryMap map,
        List<Title> rulerCounties,
        RealmMap realms,
        GovernmentMap governments,
        MapConfig cfg)
    {
        const int MaxBloodBrothers = 2;
        const int MaxSwornFriends = 4;

        // Grouped by DIRECT liege, not by top liege: obedience is scored against whoever a
        // character actually answers to, so a count under a duke under a khan is the duke's problem
        // and binding him to the khan would buy nothing. Any nomad with vassals qualifies, whatever
        // his tier.
        var vassalsByLiege = new Dictionary<Title, List<Title>>();
        foreach (var county in rulerCounties)
        {
            var primary = HistoryWriter.Primary(county, realms);
            if (!realms.Liege.TryGetValue(primary, out var liegeTitle)) continue;
            if (!realms.HolderCounty.TryGetValue(liegeTitle, out var liegeCounty)) continue;
            if (liegeCounty == county) continue;
            if (!governments.IsNomad(liegeCounty)) continue;

            if (!vassalsByLiege.TryGetValue(liegeCounty, out var list))
                vassalsByLiege[liegeCounty] = list = [];
            list.Add(county);
        }

        int sworn = 0, anda = 0;

        foreach (var (khan, vassals) in vassalsByLiege.OrderBy(kv => kv.Key.Index))
        {
            var draw = new Rng(khan.Index ^ 0x6D0A);

            // Highest tier first: the vassals big enough to out-muster the khan are the ones whose
            // obedience actually decides whether the horde holds together.
            var ranked = vassals
                // blood_brother lists friend and rival among its opposites, so anyone already tied
                // to the khan is left as he is rather than given a second, contradictory relation.
                .Where(v => !HasRelation(map.Rivals, khan, v)
                            && !HasRelation(map.Nemeses, khan, v)
                            && !HasRelation(map.Friends, khan, v))
                .OrderByDescending(v => TierRank(HistoryWriter.Primary(v, realms).Tier))
                .ThenBy(v => v.Index)
                .ToList();

            int brothers = 0, friends = 0;

            foreach (var vassal in ranked)
            {
                int year = Math.Max(1, cfg.StartYear - draw.Int(4, 20));
                string date = $"{year}.{draw.Int(1, 12)}.{draw.Int(1, 28)}";

                // An anda is sworn young and rarely — two at most, and only to the men who could
                // otherwise stand against him.
                if (brothers < MaxBloodBrothers && draw.Chance(0.45))
                {
                    AddRelation(map.BloodBrothers, khan, vassal, date);
                    brothers++;
                    anda++;
                }
                else if (friends < MaxSwornFriends && draw.Chance(0.55))
                {
                    AddFriendship(map, khan, vassal, date);
                    friends++;
                    sworn++;
                }

                if (brothers >= MaxBloodBrothers && friends >= MaxSwornFriends) break;
            }
        }

        if (sworn + anda > 0)
            Console.WriteLine($"  pre-history: {anda} blood brothers and {sworn} sworn friends bound to their khans");
    }

    private static bool HasRelation(Dictionary<Title, List<DatedRelation>> table, Title a, Title b)
        => table.TryGetValue(a, out var list) && list.Any(r => r.TargetCounty == b);

    private static void AddRelation(
        Dictionary<Title, List<DatedRelation>> table, Title a, Title b, string date)
    {
        if (!table.TryGetValue(a, out var listA)) table[a] = listA = [];
        if (!table.TryGetValue(b, out var listB)) table[b] = listB = [];

        if (!listA.Any(r => r.TargetCounty == b)) listA.Add(new DatedRelation { TargetCounty = b, Date = date });
        if (!listB.Any(r => r.TargetCounty == a)) listB.Add(new DatedRelation { TargetCounty = a, Date = date });
    }

    private static int TierRank(string tier) => tier switch
    {
        "e" => 4, "k" => 3, "d" => 2, "c" => 1, _ => 0,
    };

    private static void BuildInternalDrama(
        PrehistoryMap map,
        List<Title> rulerCounties,
        RealmMap realms,
        FaithMap faiths,
        CultureMap cultures,
        MapConfig cfg,
        Rng rng)
    {
        var vassalsByLiege = new Dictionary<Title, List<Title>>();
        foreach (var vassalCounty in rulerCounties)
        {
            var primaryTitle = HistoryWriter.Primary(vassalCounty, realms);
            if (realms.Liege.TryGetValue(primaryTitle, out var liegeTitle) &&
                realms.HolderCounty.TryGetValue(liegeTitle, out var liegeCounty))
            {
                if (!vassalsByLiege.TryGetValue(liegeCounty, out var list))
                    vassalsByLiege[liegeCounty] = list = [];
                list.Add(vassalCounty);
            }
        }

        foreach (var (liegeCounty, vassals) in vassalsByLiege)
        {
            var liegeRng = new Rng(liegeCounty.Index ^ 0x91F3);

            var ambitiousVassals = vassals
                .OrderByDescending(v => (map.Claims.TryGetValue(v, out var cl) && cl.Any(c => c.TargetTitle == HistoryWriter.Primary(liegeCounty, realms)) ? 3 : 0) +
                                        (faiths.For(v) != faiths.For(liegeCounty) ? 2 : 0) +
                                        (HistoryWriter.Primary(v, realms).Tier == "d" ? 2 : 0))
                .ToList();

            if (ambitiousVassals.Count > 0 && liegeRng.Chance(0.30))
            {
                var vassal = ambitiousVassals[0];
                if (CanAddRival(map, liegeCounty, vassal))
                {
                    int dramaYear = Math.Max(1, cfg.StartYear - liegeRng.Int(2, 8));
                    string dramaDate = $"{dramaYear}.{liegeRng.Int(1, 12)}.{liegeRng.Int(1, 28)}";

                    AddRivalry(map, vassal, liegeCounty, dramaDate);
                    var liegePrimary = HistoryWriter.Primary(liegeCounty, realms);
                    AddClaim(map, vassal, liegePrimary, pressed: true);

                    if (map.CharacterHouseMap.TryGetValue(liegeCounty, out var hLiege) &&
                        map.CharacterHouseMap.TryGetValue(vassal, out var hVassal) && hLiege != hVassal)
                    {
                        map.HouseRelations.Add(new HouseRelationDef
                        {
                            HouseA = hLiege,
                            HouseB = hVassal,
                            Level = "rivalry",
                            StartDate = dramaDate
                        });
                    }
                }
            }
        }
    }

    private static void GenerateActiveWars(
        PrehistoryMap map,
        Dictionary<Title, HashSet<Title>> topLiegeNeighbors,
        RealmMap realms,
        FaithMap faiths,
        CultureMap cultures,
        WorldCenterMap? worldCenters,
        MapConfig cfg,
        Rng rng)
    {
        int targetWars = Math.Max(1, Math.Min(cfg.StartingWarsCount, topLiegeNeighbors.Count / 3));
        var busyRulers = new HashSet<Title>();

        var candidates = new List<(Title Attacker, Title Defender, string CB, Title TargetTitle, string Desc)>();

        foreach (var (ruler, neighbors) in topLiegeNeighbors)
        {
            var rulerFaith = faiths.For(ruler);
            var rulerPrimary = HistoryWriter.Primary(ruler, realms);

            foreach (var other in neighbors)
            {
                var otherFaith = faiths.For(other);
                var otherPrimary = HistoryWriter.Primary(other, realms);

                if (map.Alliances.TryGetValue(ruler, out var allies) && allies.Any(al => al.PartnerCounty == other)) continue;
                if (map.Truces.TryGetValue(ruler, out var truces) && truces.Any(t => t.TargetCounty == other)) continue;

                if (rulerFaith.Religion != otherFaith.Religion)
                {
                    candidates.Add((ruler, other, "minor_religious_war", other, "Holy War for Borderlands"));
                }
                else if (map.Claims.TryGetValue(ruler, out var claims) && claims.Any(c => c.TargetTitle == otherPrimary && c.Pressed))
                {
                    candidates.Add((ruler, other, "claim_cb", otherPrimary, "Claim War for Sovereignty"));
                }
                else if (map.Rivals.TryGetValue(ruler, out var rivals) && rivals.Any(r => r.TargetCounty == other))
                {
                    candidates.Add((ruler, other, "county_conquest_cb", other, "Feud & Border Conquest"));
                }
            }
        }

        rng.Shuffle(candidates);

        foreach (var (attacker, defender, cb, targetTitle, desc) in candidates)
        {
            if (map.ActiveWars.Count >= targetWars) break;
            if (busyRulers.Contains(attacker) || busyRulers.Contains(defender)) continue;

            int warStartMonth = rng.Int(1, 10);
            int warStartDay = rng.Int(1, 28);
            string warStartDate = $"{cfg.StartYear - rng.Int(1, 2)}.{warStartMonth}.{warStartDay}";

            var war = new ActiveWar
            {
                StartDate = warStartDate,
                TargetTitle = targetTitle,
                CasusBelli = cb,
                AttackerCounty = attacker,
                DefenderCounty = defender,
                ClaimantCounty = cb == "claim_cb" ? attacker : null,
                Description = desc
            };

            map.ActiveWars.Add(war);
            busyRulers.Add(attacker);
            busyRulers.Add(defender);
        }
    }

    /// <summary>
    /// The county of the independent ruler at the top of this county's chain of allegiance.
    ///
    /// <b>Climbs by holder, not only by title.</b> <see cref="RealmMap.Liege"/> records the
    /// relationship on a ruler's *primary* title, so a ruler's secondary titles have no liege entry
    /// of their own even when that ruler is somebody's vassal. Walking title-to-liege alone
    /// therefore stops dead at the first such title and reports a mid-tier vassal as independent:
    /// measured on the Fleunland export, four counties held a duchy under their own kingdom and had
    /// their sub-vassals' walks terminate on the duchy, so four dukes came back as top lieges while
    /// their own walks correctly reported the emperor above them.
    ///
    /// That inconsistency is not cosmetic. Everything that groups the world into realms reads this,
    /// and a county that is a group *key* by one route and a group *member* by another is counted
    /// twice — which is how two different dead parents came to be written under one character id.
    /// So each round climbs the title chain, and then re-enters at the holder it lands on rather
    /// than stopping there, until a ruler is reached who is genuinely above no one but themselves.
    ///
    /// Cycle-guarded: a liege loop is something the realm layer can produce and an unguarded walk
    /// would hang on it rather than fail.
    /// </summary>
    private static Title TopLiegeCounty(Title county, RealmMap realms)
    {
        var current = county;
        var seen = new HashSet<Title>();

        while (seen.Add(current))
        {
            var top = HistoryWriter.Primary(current, realms);
            while (realms.Liege.TryGetValue(top, out var liege)) top = liege;

            // No holder above, or the chain came back to the ruler it started from: this is the top.
            if (!realms.HolderCounty.TryGetValue(top, out var holder)
                || ReferenceEquals(holder, current)) return current;

            current = holder;
        }

        return current;
    }

    private static void AddRivalry(PrehistoryMap map, Title a, Title b, string date)
    {
        if (!map.Rivals.TryGetValue(a, out var listA)) map.Rivals[a] = listA = [];
        if (!map.Rivals.TryGetValue(b, out var listB)) map.Rivals[b] = listB = [];

        if (!listA.Any(r => r.TargetCounty == b)) listA.Add(new DatedRelation { TargetCounty = b, Date = date });
        if (!listB.Any(r => r.TargetCounty == a)) listB.Add(new DatedRelation { TargetCounty = a, Date = date });
    }

    private static void AddFriendship(PrehistoryMap map, Title a, Title b, string date)
    {
        if (!map.Friends.TryGetValue(a, out var listA)) map.Friends[a] = listA = [];
        if (!map.Friends.TryGetValue(b, out var listB)) map.Friends[b] = listB = [];

        if (!listA.Any(r => r.TargetCounty == b)) listA.Add(new DatedRelation { TargetCounty = b, Date = date });
        if (!listB.Any(r => r.TargetCounty == a)) listB.Add(new DatedRelation { TargetCounty = a, Date = date });
    }

    private static void AddTruce(PrehistoryMap map, Title a, Title b, int days)
    {
        if (!map.Truces.TryGetValue(a, out var listA)) map.Truces[a] = listA = [];
        if (!map.Truces.TryGetValue(b, out var listB)) map.Truces[b] = listB = [];
        listA.Add((b, days));
        listB.Add((a, days));
    }

    private static void AddClaim(PrehistoryMap map, Title ruler, Title targetTitle, bool pressed)
    {
        if (!map.Claims.TryGetValue(ruler, out var list)) map.Claims[ruler] = list = [];
        if (!list.Any(c => c.TargetTitle == targetTitle))
        {
            list.Add((targetTitle, pressed));
        }
    }

    private static bool CanAddRival(PrehistoryMap map, Title a, Title b)
    {
        int countA = map.Rivals.TryGetValue(a, out var sA) ? sA.Count : 0;
        int countB = map.Rivals.TryGetValue(b, out var sB) ? sB.Count : 0;
        bool alreadyRival = map.Rivals.TryGetValue(a, out var listA) && listA.Any(r => r.TargetCounty == b);
        return !alreadyRival && countA < MaxRivalsPerRuler && countB < MaxRivalsPerRuler;
    }

    private static bool CanAddFriend(PrehistoryMap map, Title a, Title b)
    {
        int countA = map.Friends.TryGetValue(a, out var sA) ? sA.Count : 0;
        int countB = map.Friends.TryGetValue(b, out var sB) ? sB.Count : 0;
        bool alreadyFriend = map.Friends.TryGetValue(a, out var listA) && listA.Any(r => r.TargetCounty == b);
        return !alreadyFriend && countA < MaxFriendsPerRuler && countB < MaxFriendsPerRuler;
    }

    private static bool CanAddAlliance(PrehistoryMap map, Title a, Title b)
    {
        int countA = map.Alliances.TryGetValue(a, out var sA) ? sA.Count : 0;
        int countB = map.Alliances.TryGetValue(b, out var sB) ? sB.Count : 0;
        bool alreadyAllied = map.Alliances.TryGetValue(a, out var listA) && listA.Any(al => al.PartnerCounty == b);
        return !alreadyAllied && countA < MaxAlliancesPerRuler && countB < MaxAlliancesPerRuler;
    }

    private static Dictionary<Title, HashSet<Title>> BuildCountyAdjacency(
        List<Title> counties, ProvinceMap provinces, int[] order, int landCount)
    {
        var countyOfProvince = new Dictionary<int, Title>();
        foreach (var c in counties)
            foreach (var b in c.Children)
                if (b.ProvinceId > 0) countyOfProvince[b.ProvinceId] = c;

        var adjacency = new Dictionary<Title, HashSet<Title>>();
        foreach (var c in counties) adjacency[c] = [];

        foreach (var (province, others) in Titles.BuildAdjacency(provinces, landCount, order))
        {
            if (!countyOfProvince.TryGetValue(province, out var c1)) continue;
            foreach (int other in others)
            {
                if (!countyOfProvince.TryGetValue(other, out var c2) || c1 == c2) continue;
                adjacency[c1].Add(c2);
                adjacency[c2].Add(c1);
            }
        }

        return adjacency;
    }

    private static Dictionary<Title, HashSet<Title>> BuildRulerNeighbors(
        List<Title> rulerCounties,
        Dictionary<Title, HashSet<Title>> countyNeighbors,
        RealmMap realms)
    {
        var rulerNeighbors = new Dictionary<Title, HashSet<Title>>();
        foreach (var c in rulerCounties) rulerNeighbors[c] = [];

        foreach (var (c1, neighbors) in countyNeighbors)
        {
            if (!realms.HolderCounty.TryGetValue(c1, out var holder1)) holder1 = c1;

            foreach (var c2 in neighbors)
            {
                if (!realms.HolderCounty.TryGetValue(c2, out var holder2)) holder2 = c2;

                if (holder1 != holder2 && rulerNeighbors.ContainsKey(holder1) && rulerNeighbors.ContainsKey(holder2))
                {
                    rulerNeighbors[holder1].Add(holder2);
                    rulerNeighbors[holder2].Add(holder1);
                }
            }
        }

        return rulerNeighbors;
    }

    private static Dictionary<Title, HashSet<Title>> BuildTopLiegeNeighbors(
        Dictionary<Title, HashSet<Title>> rulerNeighbors,
        RealmMap realms)
    {
        var topLiegeNeighbors = new Dictionary<Title, HashSet<Title>>();

        foreach (var (ruler, neighbors) in rulerNeighbors)
        {
            var top1 = TopLiegeCounty(ruler, realms);
            if (!topLiegeNeighbors.TryGetValue(top1, out var set1))
                topLiegeNeighbors[top1] = set1 = [];

            foreach (var neighbor in neighbors)
            {
                var top2 = TopLiegeCounty(neighbor, realms);
                if (top1 != top2)
                {
                    if (!topLiegeNeighbors.TryGetValue(top2, out var set2))
                        topLiegeNeighbors[top2] = set2 = [];

                    set1.Add(top2);
                    set2.Add(top1);
                }
            }
        }

        return topLiegeNeighbors;
    }

    public static int GetRulerBirthYear(int countyIndex, int startYear)
    {
        var rng = new Rng(countyIndex ^ 0x3E2D);
        return startYear - rng.Int(24, 50);
    }
}