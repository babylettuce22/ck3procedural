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
    public bool IsHeir { get; init; }
    public bool IsDeadAncestor { get; init; }
    public string? MarriageDate { get; set; }
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
    public Dictionary<Title, List<(Title TargetCounty, int Days)>> Truces { get; } = [];
    public Dictionary<Title, List<(Title TargetTitle, bool Pressed)>> Claims { get; } = [];

    public List<ActiveWar> ActiveWars { get; } = [];

    private const int MaxRivalsPerRuler = 2;
    private const int MaxFriendsPerRuler = 2;
    private const int MaxAlliancesPerRuler = 3;

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

        // 2. Build Multi-Generational Ancestry (Deceased Fathers & Sibling Bonds)
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
            if (map.CharacterDynastyMap.ContainsKey(county)) continue;

            var primary = HistoryWriter.Primary(county, realms);
            var liegeCounty = TopLiegeCounty(county, realms);

            var vRng = new Rng(county.Index ^ 0x48A1);
            bool isHighVassal = primary.Tier is "d" or "k";

            if (isHighVassal && map.CharacterDynastyMap.TryGetValue(liegeCounty, out var liegeDynastyId) && vRng.Chance(0.40))
            {
                map.CharacterDynastyMap[county] = liegeDynastyId;

                if (vRng.Chance(0.50))
                {
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
                    map.CharacterHouseMap[county] = map.CharacterHouseMap[liegeCounty];
                }
            }
            else
            {
                CreateDynastyAndMainHouse(map, county, cultures);
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
        foreach (var county in rulerCounties)
        {
            var primary = HistoryWriter.Primary(county, realms);
            bool isTopLiege = !realms.Liege.ContainsKey(primary);

            if (isTopLiege)
            {
                var culture = cultures.For(county);
                var faith = faiths.For(county);
                var fRng = new Rng(county.Index ^ 0x7E1B);

                string fatherName = culture.MaleNames.Count > 0
                    ? culture.MaleNames[fRng.Int(0, culture.MaleNames.Count - 1)]
                    : "Aldgisl";

                int birthYear = cfg.StartYear - fRng.Int(65, 85);
                int deathYear = cfg.StartYear - fRng.Int(5, 18);

                var father = new HistoricalCharacter
                {
                    Id = $"gen_char_father_{county.Index}",
                    Name = fatherName,
                    Female = false,
                    DynastyId = map.CharacterDynastyMap[county],
                    DynastyHouseKey = map.CharacterHouseMap[county],
                    CultureKey = culture.Key,
                    FaithKey = faith.Key,
                    BirthDate = $"{birthYear}.{fRng.Int(1, 12)}.{fRng.Int(1, 28)}",
                    DeathDate = $"{deathYear}.{fRng.Int(1, 12)}.{fRng.Int(1, 28)}",
                    AssociatedCounty = county,
                    IsDeadAncestor = true
                };

                map.DeceasedParents[county] = father;
                map.AllExtraCharacters.Add(father);
            }
        }

        foreach (var county in rulerCounties)
        {
            var liegeCounty = TopLiegeCounty(county, realms);
            if (county == liegeCounty) continue;

            if (map.CharacterDynastyMap.TryGetValue(county, out var dynId) &&
                map.CharacterDynastyMap.TryGetValue(liegeCounty, out var liegeDynId) &&
                dynId == liegeDynId &&
                map.DeceasedParents.TryGetValue(liegeCounty, out var sharedFather))
            {
                // consider re-enabling sharedFather (originally was causing too many siblings and lag when clicking on certain character's portraits)

                //map.DeceasedParents[county] = sharedFather;
                var liegePrimary = HistoryWriter.Primary(liegeCounty, realms);
                AddClaim(map, county, liegePrimary, pressed: false);
            }
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

        foreach (var ruler in sortedRulers)
        {
            if (marriedRulers.Contains(ruler)) continue;

            var rulerFaith = faiths.For(ruler);
            var rulerCulture = cultures.For(ruler);
            var mRng = new Rng(ruler.Index ^ 0x6E19);

            if (!mRng.Chance(0.88)) continue;

            int rulerBirthYear = HistoryWriter.GetRulerBirthYear(ruler.Index, cfg.StartYear);
            var topLiege = TopLiegeCounty(ruler, realms);
            bool isTopLiege = (ruler == topLiege);

            Title? brideOriginCounty = null;
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
                    brideOriginCounty = foreignEligible[mRng.Int(0, foreignEligible.Count - 1)];
                }
                else if (internalVassals.Count > 0 && mRng.Chance(0.65))
                {
                    brideOriginCounty = internalVassals[mRng.Int(0, internalVassals.Count - 1)];
                }
                else if (foreignEligible.Count > 0)
                {
                    brideOriginCounty = foreignEligible[mRng.Int(0, foreignEligible.Count - 1)];
                }
            }
            // === 2. VASSAL MARRIAGE SELECTION ===
            else
            {
                // A) Liege's Royal House (Liege-Vassal Alliance)
                bool canMarryLiege = map.CharacterHouseMap.GetValueOrDefault(topLiege) != map.CharacterHouseMap.GetValueOrDefault(ruler);

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
                    brideOriginCounty = topLiege;
                }
                else if (coVassals.Count > 0 && mRng.Chance(0.50))
                {
                    brideOriginCounty = coVassals[mRng.Int(0, coVassals.Count - 1)];
                }
                else if (foreignBorder.Count > 0 && mRng.Chance(0.40))
                {
                    brideOriginCounty = foreignBorder[mRng.Int(0, foreignBorder.Count - 1)];
                }
                else if (canMarryLiege)
                {
                    brideOriginCounty = topLiege;
                }
                else if (coVassals.Count > 0)
                {
                    brideOriginCounty = coVassals[mRng.Int(0, coVassals.Count - 1)];
                }
            }

            // consider re-enabling brideFather

            // === 3. GENERATE SPOUSE CHARACTER ===
            string brideDynasty;
            string? brideHouse;
            string? brideFather;
            Culture brideCulture;
            Faith brideFaith;

            if (brideOriginCounty != null)
            {
                brideCulture = cultures.For(brideOriginCounty);
                brideFaith = faiths.For(brideOriginCounty);
                brideDynasty = map.CharacterDynastyMap.GetValueOrDefault(brideOriginCounty, map.CharacterDynastyMap[ruler]);
                brideHouse = map.CharacterHouseMap.GetValueOrDefault(brideOriginCounty);
                //brideFather = map.DeceasedParents.TryGetValue(brideOriginCounty, out var df) ? df.Id : HistoryWriter.CharacterId(brideOriginCounty);
            }
            else
            {
                // Fallback: Generate a distinct local noble house so rulers never marry their own dynasty
                brideCulture = rulerCulture;
                brideFaith = rulerFaith;

                string nobleDynName = brideCulture.DynastyNames.Count > 1
                    ? brideCulture.DynastyNames[(ruler.Index + 3) % brideCulture.DynastyNames.Count]
                    : $"{brideCulture.Name}court_{ruler.Index}";

                brideDynasty = $"gen_dynasty_noble_{ruler.Index}";
                brideHouse = $"house_gen_noble_{ruler.Index}";
                string nobleNameKey = $"dynn_gen_noble_{ruler.Index}";

                if (!map.Dynasties.ContainsKey(brideDynasty))
                {
                    map.Dynasties[brideDynasty] = new DynastyDef
                    {
                        Id = brideDynasty,
                        NameKey = nobleNameKey,
                        LocalizedName = nobleDynName,
                        CultureKey = brideCulture.Key
                    };
                    map.Houses[brideHouse] = new DynastyHouseDef
                    {
                        Key = brideHouse,
                        NameKey = nobleNameKey,
                        LocalizedName = nobleDynName,
                        DynastyId = brideDynasty,
                        Prefix = CulturePrefix(brideCulture.Key)
                    };
                }

                brideFather = null;
            }

            string femaleName = brideCulture.FemaleNames.Count > 0
                ? brideCulture.FemaleNames[mRng.Int(0, brideCulture.FemaleNames.Count - 1)]
                : $"{brideCulture.Name}a";



            int brideBirthYear = cfg.StartYear - mRng.Int(20, 44);
            int earliestMarriageYear = Math.Max(rulerBirthYear + 16, brideBirthYear + 16);
            int marriageYear = Math.Min(cfg.StartYear - 2, earliestMarriageYear + mRng.Int(0, 8));
            string weddingDate = $"{marriageYear}.{mRng.Int(1, 12)}.{mRng.Int(1, 28)}";

            var spouse = new HistoricalCharacter
            {
                Id = $"gen_char_spouse_{ruler.Index}",
                Name = femaleName,
                Female = true,
                DynastyId = brideDynasty,
                DynastyHouseKey = brideHouse,
                CultureKey = brideCulture.Key,
                FaithKey = brideFaith.Key,
                BirthDate = $"{brideBirthYear}.{mRng.Int(1, 12)}.{mRng.Int(1, 28)}",
                FatherId = null,
                AssociatedCounty = ruler,
                MarriageDate = weddingDate
            };

            map.Spouses[ruler] = spouse;
            map.AllExtraCharacters.Add(spouse);
            marriedRulers.Add(ruler);

            // Establish Alliance and House Amity if married into an existing ruler's house
            if (brideOriginCounty != null)
            {
                AddMarriageAlliance(map, ruler, brideOriginCounty, spouse.Id, HistoryWriter.CharacterId(brideOriginCounty), weddingDate);

                if (map.CharacterHouseMap.TryGetValue(ruler, out var hA) &&
                    map.CharacterHouseMap.TryGetValue(brideOriginCounty, out var hB) && hA != hB)
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
            var cRng = new Rng(ruler.Index ^ 0x51E3);

            int childCount = cRng.Int(1, 3);
            var childrenList = new List<HistoricalCharacter>();

            int weddingYear = spouse.MarriageDate != null
                ? int.Parse(spouse.MarriageDate.Split('.')[0])
                : cfg.StartYear - 10;

            for (int i = 0; i < childCount; i++)
            {
                bool isFemale = cRng.Chance(0.48);
                string childName = isFemale
                    ? (culture.FemaleNames.Count > 0 ? culture.FemaleNames[cRng.Int(0, culture.FemaleNames.Count - 1)] : "Asta")
                    : (culture.MaleNames.Count > 0 ? culture.MaleNames[cRng.Int(0, culture.MaleNames.Count - 1)] : "Ragnarr");

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
                    FatherId = HistoryWriter.CharacterId(ruler),
                    MotherId = spouse.Id,
                    AssociatedCounty = ruler,
                    IsHeir = false
                };

                childrenList.Add(child);
                map.AllExtraCharacters.Add(child);
            }

            // Assign Primary Heir based on faith gender doctrines (male-preference default)
            var designatedHeir = childrenList.FirstOrDefault(c => !c.Female) ?? childrenList.FirstOrDefault();
            if (designatedHeir != null)
            {
                int heirIndex = childrenList.IndexOf(designatedHeir);
                childrenList[heirIndex] = new HistoricalCharacter
                {
                    Id = designatedHeir.Id,
                    Name = designatedHeir.Name,
                    Female = designatedHeir.Female,
                    DynastyId = designatedHeir.DynastyId,
                    DynastyHouseKey = designatedHeir.DynastyHouseKey,
                    CultureKey = designatedHeir.CultureKey,
                    FaithKey = designatedHeir.FaithKey,
                    BirthDate = designatedHeir.BirthDate,
                    FatherId = designatedHeir.FatherId,
                    MotherId = designatedHeir.MotherId,
                    AssociatedCounty = designatedHeir.AssociatedCounty,
                    IsHeir = true
                };
            }

            map.Children[ruler] = childrenList;
        }
    }

    private static void AddMarriageAlliance(PrehistoryMap map, Title a, Title b, string brideId, string brideRelativeId, string weddingDate)
    {
        if (!map.Alliances.TryGetValue(a, out var listA)) map.Alliances[a] = listA = [];
        if (!map.Alliances.TryGetValue(b, out var listB)) map.Alliances[b] = listB = [];

        if (!listA.Any(al => al.PartnerCounty == b))
            listA.Add(new AllianceLink { PartnerCounty = b, ThroughSpouseId = brideId, ThroughPartnerId = brideRelativeId, FormationDate = weddingDate });
        if (!listB.Any(al => al.PartnerCounty == a))
            listB.Add(new AllianceLink { PartnerCounty = a, ThroughSpouseId = brideRelativeId, ThroughPartnerId = brideId, FormationDate = weddingDate });
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

    private static Title TopLiegeCounty(Title county, RealmMap realms)
    {
        var primary = HistoryWriter.Primary(county, realms);
        var current = primary;

        while (realms.Liege.TryGetValue(current, out var liege))
        {
            current = liege;
        }

        return realms.HolderCounty.TryGetValue(current, out var topHolder) ? topHolder : county;
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