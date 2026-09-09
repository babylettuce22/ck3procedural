using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Emit;

namespace Ck3MapGen.MapGen;

public sealed class GovernmentMap
{
    public const string Feudal = "feudal_government";
    public const string Tribal = "tribal_government";
    public const string Clan = "clan_government";
    public const string Republic = "republic_government";
    public const string Theocracy = "theocracy_government";
    public const string Administrative = "administrative_government";
    public const string Nomad = "nomad_government";

    /// <summary>
    /// All Under Heaven's two portable bureaucracies — the same shape as
    /// <see cref="Administrative"/> with a merit currency and county-tier appointments on top.
    ///
    /// Portable is the operative word, and it is why <c>celestial_government</c> is not here beside
    /// them. Both of these are assigned in vanilla history by a bare <c>government =</c> line and
    /// carry no hardcoded title anywhere; celestial's entire ministry layer hangs off
    /// <c>tgp_has_access_to_ministry_trigger</c>, which tests <c>has_title = title:h_china</c>
    /// literally, so on a generated map it would be a name with nothing behind it. Without the
    /// expansion, vanilla's game-start sweep turns all three into feudal, so nothing here needs a
    /// guard of its own.
    /// </summary>
    public const string Meritocratic = "meritocratic_government";

    /// <inheritdoc cref="Meritocratic"/>
    public const string SteppeAdmin = "steppe_admin_government";

    /// <summary>
    /// The Son of Heaven's government, and the one thing in All Under Heaven that is NOT portable:
    /// its ministry, its Mandate and its Dynastic Cycle all test <c>title:h_china</c> by name.
    /// Assignable since the generated hegemony took that key (<see cref="Titles.HegemonyKey"/>),
    /// and meant for its holder — on any other realm it is a bureaucracy with an empty ministry.
    /// Vanilla history assigns it bare, and the game-start sweep turns it feudal without the
    /// expansion, so nothing here needs a guard of its own.
    /// </summary>
    public const string Celestial = "celestial_government";

    /// <summary>
    /// Ritsuryō — the court bureaucracy of All Under Heaven's Japan, and the same admin family as
    /// the three above: castle seats, noble families, county-tier appointments, a manor for a
    /// domicile.
    ///
    /// Vanilla makes it Japanese twice over and the mod undoes both. The government declares
    /// <c>primary_heritages = { heritage_japonic }</c>, and its <c>can_get_government</c> asks to
    /// be inside a Japanese realm; both objects are re-declared without either in
    /// BaseFilesToCopy/Core/common/governments/zz_gen_japan_government_types.txt, which is what
    /// lets the Adopt-a-Bureaucracy decision offer it on a generated map. History assignment never
    /// consulted those anyway, so here it takes a bare <c>government =</c> line like the rest.
    ///
    /// What stays Japanese is flavour rather than function: the house blocs, the shogunate layer
    /// and the imperial branches all name <c>title:e_japan</c>, which no generated map has. The
    /// government itself loads and plays; those layers simply stay quiet.
    /// </summary>
    public const string JapanAdministrative = "japan_administrative_government";

    /// <summary>
    /// Sōryō — the Japanese feudal government, and the counterpart Ritsuryō's vassals sit on.
    /// Castle-seated, and re-declared beside Ritsuryō in the same file to drop the heritage.
    /// </summary>
    public const string JapanFeudal = "japan_feudal_government";

    /// <summary>
    /// The Southeast Asian mandala: tributary overlordship rather than a hierarchy of vassals, and
    /// the one government here whose seat is not a castle. It declares
    /// <c>primary_holding = temple_citadel_holding</c>, so <see cref="CapitalHolding"/> rebuilds
    /// every capital in the realm as a temple citadel — a holding vanilla itself writes into
    /// province history 114 times, and one feudal's <c>valid_holdings</c> accepts, so the counties
    /// still stand up when the game-start sweep turns the realm feudal without the expansion.
    /// </summary>
    public const string Mandala = "mandala_government";

    /// <summary>
    /// Wanua — the maritime tribal government: barter, cheap embarkation, safer seas. Tribal-seated
    /// like ordinary tribes, and swept to <see cref="Tribal"/> without the expansion.
    ///
    /// Vanilla prefers it for <c>heritage_austronesian</c> and no generated culture carries any
    /// vanilla heritage. That preference is not a gate — history assignment bypasses
    /// <c>can_get_government</c> and the realm loads — but it is the reason a courtier newly landed
    /// inside one may be given an ordinary tribal government rather than this.
    /// </summary>
    public const string Wanua = "wanua_government";

    /// <summary>
    /// Every government a realm can be put on by hand, for the inspector's dropdown.
    ///
    /// Assignment in title history bypasses <c>can_get_government</c> entirely, so what belongs
    /// here is not what a ruler could reach in play but what stands up on a generated map with
    /// nothing else authored for it: a bare <c>government =</c> line and a capital holding of the
    /// right type. That is the seven the cascade above already produces plus all seven of
    /// All Under Heaven's, which vanilla history likewise assigns bare and which its game-start
    /// sweep turns back into feudal or tribal for anyone without the expansion — so not one of them
    /// needs a per-title guard the way <see cref="Administrative"/> does.
    ///
    /// Each of the seven wants one thing beyond the line, and each is met: the two Japanese ones
    /// want <c>heritage_japonic</c>, dropped by the mod's own re-declaration of both objects;
    /// <see cref="Celestial"/> wants <c>title:h_china</c>, which the generated hegemony now is;
    /// <see cref="Mandala"/> wants temple capitals, which <see cref="CapitalHolding"/> builds; and
    /// <see cref="Wanua"/> wants <c>heritage_austronesian</c>, which is a preference rather than a
    /// gate. Celestial and wanua are the two that mean less off their intended realm — a ministry
    /// with nothing behind it on any ruler but the hegemon, and a landing preference no generated
    /// culture answers to.
    ///
    /// Still absent, and for a reason no editor choice can fix: <c>landless_adventurer</c>, which
    /// is not a realm at all, and the four government types the engine hands out itself —
    /// <c>mercenary</c> and <c>holy_order</c>, plus <c>herder</c>, which is a vassal government
    /// under a horde rather than one a realm is put on.
    ///
    /// Absent from generation is not the same as unreachable in play: meritocratic and Ritsuryō
    /// (and Sōryō through it) can also be ADOPTED on a generated map, because
    /// BaseFilesToCopy/Core/common/scripted_triggers/zz_gen_admin_conversion_triggers.txt
    /// re-points vanilla's decision gates at the generated hegemony and at the player.
    /// </summary>
    public static readonly string[] Assignable =
    [
        Feudal, Clan, Tribal, Republic, Theocracy, Administrative, Nomad, Meritocratic, SteppeAdmin,
        Celestial, JapanAdministrative, JapanFeudal, Mandala, Wanua,
    ];

    /// <summary>
    /// The same fourteen as <see cref="Assignable"/>, in the order to show a person rather than the
    /// order the cascade decides them in: by family, each All Under Heaven government beside the
    /// vanilla one it is a variety of. The editor's dropdown and the map legend both read this, so
    /// a colour sits in the same place in the key as its name does in the list.
    ///
    /// Kept as its own array rather than sorting <see cref="Assignable"/>, because that array's
    /// order is load-bearing in its own right — it is the cascade's, and its doc comment reasons
    /// about it. That the two hold the same fourteen is checked by <c>--verify-world-editor</c>.
    /// </summary>
    public static readonly string[] DisplayOrder =
    [
        Feudal, JapanFeudal, Administrative, Celestial, Meritocratic, SteppeAdmin,
        JapanAdministrative, Clan, Republic, Theocracy, Mandala, Nomad, Tribal, Wanua,
    ];

    /// <summary>
    /// What to call a government on screen — the editor's dropdown, the map legend and both hover
    /// readouts, so all four agree on the name beside a colour instead of one saying "Ritsuryō"
    /// and the next <c>japan_administrative_government</c>.
    ///
    /// Only the names the game's own text uses and a mechanical key would lose are spelled out:
    /// the two Japanese governments, which vanilla calls Ritsuryō and Sōryō rather than anything
    /// containing "Japan", and the wilderness, which is not a government anyone plays. Everything
    /// else reads correctly straight from its key, so it is de-suffixed and de-underscored rather
    /// than listed — which also means a government added later gets a serviceable name here
    /// without this switch being the thing that has to remember it.
    /// </summary>
    public static string DisplayName(string government) => government switch
    {
        JapanAdministrative => "Ritsuryō",
        JapanFeudal => "Sōryō",
        Wilderness => "Wilderness",
        "" => "—",
        _ => Sentence(government.Replace("_government", "").Replace('_', ' ')),
    };

    private static string Sentence(string words)
        => words.Length == 0 ? "—" : char.ToUpperInvariant(words[0]) + words[1..];

    private readonly Dictionary<Title, string> byCounty;
    private readonly HashSet<Title> _adminRealms;
    private readonly HashSet<Title> _nomadRealms;

    internal GovernmentMap(
        Dictionary<Title, string> byCounty,
        HashSet<Title>? adminRealms = null,
        HashSet<Title>? nomadRealms = null)
    {
        this.byCounty = byCounty;
        _adminRealms = adminRealms ?? [];
        _nomadRealms = nomadRealms ?? [];
    }

    public string For(Title county) => byCounty.GetValueOrDefault(county, Feudal);

    public bool IsTribal(Title county) => For(county) == Tribal;
    public bool IsAdministrative(Title county) => For(county) == Administrative;
    public bool IsNomad(Title county) => For(county) == Nomad;

    /// <summary>
    /// The bureaucracies that behave alike: one government across the whole realm, castle seats, and
    /// noble families rather than ordinary vassals.
    ///
    /// Ritsuryō belongs here on every count — <c>administrative = yes</c>, <c>noble_families</c>,
    /// a castle seat — though the cascade never produces it, so today this only answers for the
    /// editor. Sōryō does not: it is the feudal half of that pair, and its vassals are ordinary.
    /// </summary>
    public static bool IsAdminFamily(string government)
        => government is Administrative or Meritocratic or SteppeAdmin or Celestial
            or JapanAdministrative;

    /// <summary>
    /// Whether this government declares <c>noble_families = yes</c> — the six that hand their
    /// house heads a landless family title and run appointments off it.
    ///
    /// Deliberately NOT <see cref="IsAdminFamily"/>, though five of the six are the same. Sōryō
    /// allows noble families too (01_japan_government_types.txt) while being feudal in every other
    /// respect, and folding it into the bureaucracy predicate would also claim it has one
    /// government realm-wide and a castle seat, which it does not.
    /// </summary>
    public static bool AllowsNobleFamilies(string government)
        => government is Administrative or Meritocratic or SteppeAdmin or Celestial
            or JapanAdministrative or JapanFeudal;

    /// <summary>
    /// The tier a family title is minted at, which is the government's own
    /// <c>min_appointment_tier</c>: duchy for the two Byzantine-descended bureaucracies, county for
    /// the four that carry <c>government_has_county_tier_noble_families</c>.
    ///
    /// Returned as the title-key prefix rather than a tier name because that is the only thing the
    /// callers do with it.
    /// </summary>
    public static string NobleFamilyTier(string government)
        => government is Administrative or SteppeAdmin ? "d" : "c";

    public bool IsAdminEmpire(Title title) => _adminRealms.Contains(title);
    public bool IsNomadRealm(Title title) => _nomadRealms.Contains(title);

    /// <summary>
    /// Moves one county onto a different government after the map was built — the editor's
    /// override, and the only thing here that mutates.
    ///
    /// Per county rather than per realm because that is how the map is keyed and how a revert has
    /// to put it back: the counties of one realm do not all share a government even as generated,
    /// since a coastal city and a steppe march are decided county by county on top of whatever
    /// their sovereign got.
    /// </summary>
    public void Set(Title county, string government) => byCounty[county] = government;

    /// <summary>
    /// Whether a realm is listed as one of the two kinds that are tracked whole rather than county
    /// by county. Set alongside <see cref="Set"/> when an override changes what a realm is, so a
    /// realm that has stopped being nomadic stops being listed as one.
    /// </summary>
    public void MarkRealm(Title primary, bool administrative, bool nomad)
    {
        if (administrative) _adminRealms.Add(primary); else _adminRealms.Remove(primary);
        if (nomad) _nomadRealms.Add(primary); else _nomadRealms.Remove(primary);
    }

    public IEnumerable<Title> AdminTitles => _adminRealms;
    public IEnumerable<Title> NomadTitles => _nomadRealms;

    /// <summary>
    /// Whether any county on the map is a horde — a peripheral steppe march counts, which is why
    /// this reads the counties and not <see cref="NomadTitles"/>. What decides whether the nomad
    /// naming override is worth shipping.
    /// </summary>
    public bool AnyNomad => byCounty.Values.Any(g => g == Nomad);

    /// <inheritdoc cref="AnyNomad"/>
    public IEnumerable<Title> NomadCounties => byCounty.Where(kv => kv.Value == Nomad).Select(kv => kv.Key);

    public static string SafeFallback(string government, bool isClanEligible = false) => government switch
    {
        Administrative => isClanEligible ? Clan : Feudal,
        Nomad => isClanEligible ? Clan : Tribal,
        _ => government
    };

    public static string CapitalHolding(string government) => government switch
    {
        // nomad_government declares primary_holding = nomad_holding and requires one in every
        // county it holds; a tribal seat is not a seat it can sit in.
        Nomad => "nomad_holding",
        Tribal => "tribal_holding",
        Republic => "city_holding",
        Theocracy => "church_holding",

        // Wanua is a tribal government with a boat: primary_holding = tribal_holding, the same as
        // any other tribe, and the same holding the game-start sweep leaves it standing on when it
        // reverts the realm to tribal without the expansion.
        Wanua => "tribal_holding",

        // The one seat here that is neither a castle nor an ordinary tribe. Feudal's valid_holdings
        // include temple_citadel_holding, so a mandala realm swept to feudal keeps its capitals.
        Mandala => "temple_citadel_holding",

        // Feudal, clan and every bureaucracy — administrative, meritocratic, steppe-admin,
        // celestial and both Japanese ones all declare primary_holding = castle_holding, so the
        // default is the right answer for them rather than an unconsidered one.
        _ => "castle_holding",
    };

    /// <summary>
    /// The government the dummies' counties are seated under in title history. Never assigned by
    /// <see cref="Governments.Build"/> — the cascade does not know about the wilderness and gives
    /// those counties a terrain guess — so this is reported over it wherever a wild county is
    /// shown or counted, because the history line is what the game actually loads.
    /// </summary>
    public const string Wilderness = "wilderness_government";

    /// <summary>
    /// Counts what the written history will say, county by county. Wild and ruined counties count
    /// as <see cref="Wilderness"/> whatever the cascade assigned them, which is what the history
    /// writer seats them under; an unassigned settled county counts as feudal, the same default
    /// <see cref="For"/> returns.
    /// </summary>
    public IEnumerable<(string Government, int Count)> Tally(IEnumerable<Title> counties, WildernessMap? wilderness)
    {
        var counts = new Dictionary<string, int>();
        foreach (var county in counties)
        {
            string government = wilderness?.Contains(county) == true ? Wilderness : For(county);
            counts[government] = counts.GetValueOrDefault(government) + 1;
        }

        return counts.Where(kv => kv.Value > 0)
                     .OrderByDescending(kv => kv.Value)
                     .Select(kv => (kv.Key, kv.Value));
    }
}

public static class Governments
{
    private const double ClanAridity = 0.40;

    private static bool IsArid(TerrainClass t) => t is TerrainClass.Desert or TerrainClass.Drylands
        or TerrainClass.Steppe or TerrainClass.DesertMountains;

    private static double Aridity(Title county, TerrainClass[] provinceTerrain)
    {
        int arid = 0, total = 0;
        foreach (var barony in county.Children)
        {
            if (barony.ProvinceId < 0 || barony.ProvinceId >= provinceTerrain.Length) continue;
            total++;
            if (IsArid(provinceTerrain[barony.ProvinceId])) arid++;
        }

        return total == 0 ? 0 : arid / (double)total;
    }

    private static bool IsCoastal(Title county, TerrainClass[] provinceTerrain)
    {
        foreach (var barony in county.Children)
            if (barony.ProvinceId >= 0 && barony.ProvinceId < provinceTerrain.Length
                && provinceTerrain[barony.ProvinceId] == TerrainClass.Beach)
                return true;

        return false;
    }

    public static GovernmentMap Build(
        List<Title> empires,
        List<Title> counties,
        RealmMap realms,
        TerrainClass[] provinceTerrain,
        Dictionary<Title, int> development,
        CultureMap? cultures,
        WorldCenterMap? worldCenters,
        MapConfig cfg,
        Rng rng,
        AzgaarImport? azgaar = null,
        Dictionary<int, string>? stateGovernments = null)
    {
        var assigned = new Dictionary<Title, string>();
        var adminTitles = new HashSet<Title>();
        var nomadTitles = new HashSet<Title>();

        int salt = rng.Int(1, int.MaxValue - 1);

        // A country's government is Azgaar's to decide when it said one — its form is the whole
        // difference between a Kingdom and a Most Serene Republic, and the terrain-and-development
        // reasoning below has no way to recover it.
        //
        // Highest tier first so that an empire lays its government over its whole span before the
        // kingdoms inside it are considered; a county claimed by a larger state is left alone rather
        // than reassigned, which is what keeps a vassal kingdom from overwriting its suzerain.
        var claimed = new HashSet<Title>();

        if (azgaar is not null && stateGovernments is not null)
        {
            foreach (var (state, title) in azgaar.StateTitles
                         .OrderByDescending(kv => TierRank(kv.Value.Tier))
                         .ThenBy(kv => kv.Key))
            {
                if (!stateGovernments.TryGetValue(state, out string? government)) continue;

                foreach (var county in Titles.Flatten([title]).Where(t => t.Tier == "c"))
                {
                    assigned[county] = government;
                    claimed.Add(county);
                }

                if (government == GovernmentMap.Nomad) nomadTitles.Add(title);
                if (government == GovernmentMap.Administrative) adminTitles.Add(title);
            }

            counties = counties.Where(c => !claimed.Contains(c)).ToList();

            // An export that covers the whole map leaves the terrain reasoning below nothing to
            // reason about, and running it over an empty list only risks it inventing a default.
            if (counties.Count == 0) return new GovernmentMap(assigned, adminTitles, nomadTitles);
        }

        // --- 1. Identify Clan-leaning heritages ---
        var clanHeritage = new HashSet<Heritage>();
        if (cultures is not null)
        {
            var aridity = new Dictionary<Heritage, (double Sum, int Count)>();
            foreach (var county in counties)
            {
                var heritage = cultures.For(county).Heritage;
                var (sum, count) = aridity.GetValueOrDefault(heritage);
                aridity[heritage] = (sum + Aridity(county, provinceTerrain), count + 1);
            }

            foreach (var (heritage, (sum, count)) in aridity)
                if (count > 0 && sum / count >= ClanAridity) clanHeritage.Add(heritage);
        }

        // --- 2. Group counties by their independent top liege realm ---
        var topLiegeCounties = new Dictionary<Title, List<Title>>();
        foreach (var county in counties)
        {
            var topLiege = TopLiege(county, realms);
            if (!topLiegeCounties.TryGetValue(topLiege, out var list))
                topLiegeCounties[topLiege] = list = [];
            list.Add(county);
        }

        // --- 3. Pre-score and identify Administrative Empires (STRICT QUALITY CRITERIA) ---
        var eligibleAdminRealms = new HashSet<Title>();
        if (cfg.EnableAdministrativeEmpires && !cfg.ShatteredWorld && cfg.AdministrativeEmpireShare > 0)
        {
            var scoredEmpires = new List<(Title TopLiege, double Score)>();

            foreach (var (topLiege, realmCounties) in topLiegeCounties)
            {
                // Empire or above: a crowned hegemon's primary is the hegemony (HistoryWriter.Rank),
                // and his realm is the one the celestial branch below exists for.
                var primary = HistoryWriter.Primary(topLiege, realms);
                if (primary.Tier is not ("e" or "h")) continue;

                bool hasImperialWonder = worldCenters is not null && realmCounties.Any(c =>
                {
                    var center = worldCenters.Centers.FirstOrDefault(wc => wc.County == c);
                    return center != null && center.Wonder.Archetype is WonderArchetype.ImperialPalace or WonderArchetype.GreatLibrary;
                });

                // Temporal & Quality Gate:
                // Must be in 800+ AD (or hold an ancient wonder) AND have high development (avgDev >= 11) or wonder
                double avgDev = realmCounties.Average(c => (double)development.GetValueOrDefault(c));
                if (cfg.EraYear < cfg.AdministrativeMinStartYear && !hasImperialWonder) continue;
                if (avgDev < 11.0 && !hasImperialWonder) continue;

                var capitalCulture = cultures?.For(topLiege);
                double score = avgDev;

                if (capitalCulture is not null)
                {
                    if (capitalCulture.Ethos is "ethos_bureaucratic" or "ethos_courtly") score += 6.0;
                    if (capitalCulture.Traditions.Contains("tradition_city_keepers")) score += 3.0;
                }

                if (hasImperialWonder) score += 10.0;
                scoredEmpires.Add((topLiege, score));
            }

            scoredEmpires.Sort((a, b) => b.Score.CompareTo(a.Score));
            int targetAdmin = (int)Math.Round(scoredEmpires.Count * cfg.AdministrativeEmpireShare);
            if (targetAdmin < 1 && scoredEmpires.Count > 0 && cfg.AdministrativeEmpireShare > 0)
            {
                targetAdmin = 1;
            }

            for (int i = 0; i < Math.Min(targetAdmin, scoredEmpires.Count); i++)
            {
                eligibleAdminRealms.Add(scoredEmpires[i].TopLiege);
                adminTitles.Add(HistoryWriter.Primary(scoredEmpires[i].TopLiege, realms));
            }
        }

        // The seat of a hegemon crowned at the start date, if the world has one. Read here so the
        // cascade below can tell that realm apart; on every other map it stays null and nothing in
        // the loop behaves differently.
        Title? hegemonSeat = cfg.StartingHegemony && Titles.HegemonyOf(empires) is { } crown
            ? realms.HolderCounty.GetValueOrDefault(crown)
            : null;

        // --- 4. Assign Government Realm-by-Realm with Historical Calibration ---
        foreach (var (topLiege, realmCounties) in topLiegeCounties)
        {
            var draw = new Rng(topLiege.Index ^ salt);
            var capitalDomTerrain = Development.DominantTerrain(topLiege, provinceTerrain);
            var capitalCulture = cultures?.For(topLiege);
            double avgDev = realmCounties.Average(c => (double)development.GetValueOrDefault(c));
            double avgAridity = realmCounties.Average(c => Aridity(c, provinceTerrain));

            // Steppe & Arid composition
            int steppeCount = realmCounties.Count(c => Development.DominantTerrain(c, provinceTerrain) == TerrainClass.Steppe);
            int aridCount = realmCounties.Count(c => Development.DominantTerrain(c, provinceTerrain) is TerrainClass.Desert or TerrainClass.Drylands);
            double steppeShare = realmCounties.Count > 0 ? (double)steppeCount / realmCounties.Count : 0.0;
            double aridShare = realmCounties.Count > 0 ? (double)(steppeCount + aridCount) / realmCounties.Count : 0.0;

            // Earlier starts lean harder nomadic: +0.25 at 500, +0.05 at 900, -0.25 at 1250 and
            // later. The floor used to be 0.50, which meant NomadSteppeShare could not express
            // anything below "half of every qualifying realm" — the clamp, not the knob, was
            // setting the outcome. It is low enough now that the setting is honest across its
            // whole range.
            double timeNomadFactor = Math.Clamp((1000 - cfg.EraYear) / 2000.0, -0.25, 0.25);
            double effectiveNomadShare = Math.Clamp(cfg.NomadSteppeShare + timeNomadFactor, 0.0, 0.98);

            string realmGovernment;

            // A. Administrative Empire
            if (eligibleAdminRealms.Contains(topLiege))
            {
                realmGovernment = GovernmentMap.Administrative;
            }
            // B. Nomadic Horde.
            //
            // Steppe is the heartland and qualifies on a modest share. Merely dry ground has to be
            // *mostly* dry: aridShare counts Desert and Drylands as well, so at the old 0.35 nearly
            // every realm on an arid map cleared it and the other two clauses never mattered. The
            // bare "capital is Drylands" clause is gone with it — it carried no share requirement at
            // all, so one dry capital county made a whole settled realm roll for horde.
            else if (cfg.EnableNomadHordes
                     && (steppeShare >= 0.20 || aridShare >= 0.60 || capitalDomTerrain == TerrainClass.Steppe)
                     && draw.Chance(effectiveNomadShare))
            {
                realmGovernment = GovernmentMap.Nomad;
                nomadTitles.Add(HistoryWriter.Primary(topLiege, realms));
            }
            // C. Clan Realm (Arid heritages or dry terrain with decent settlement)
            else if ((capitalCulture is not null && clanHeritage.Contains(capitalCulture.Heritage)) || avgAridity >= ClanAridity)
            {
                realmGovernment = GovernmentMap.Clan;
            }
            // D. Tribal Realm vs Feudal Realm (Historical Calibration)
            //
            // Early and undeveloped is tribal unless the capital sits on fertile ground. Note the
            // escape hatch is narrower than it reads: cultivation makes Farmlands about 2% of
            // provinces by design and nothing assigns Floodplains at all, so at any start before 950
            // this clause — not the avgDev < 7 one below it — is what decides tribal-versus-feudal
            // for nearly every realm the nomad and clan clauses did not already take.
            else if (cfg.EraYear < 950 && avgDev < 12.0
                     && capitalDomTerrain is not (TerrainClass.Farmlands or TerrainClass.Floodplains))
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            else if (capitalDomTerrain is TerrainClass.Taiga or TerrainClass.Arctic or TerrainClass.Jungle or TerrainClass.Wetlands && cfg.EraYear < 1100)
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            else if (avgDev < 7.0)
            {
                realmGovernment = GovernmentMap.Tribal;
            }
            // E. Feudal Realm (Settled core, fertile farmlands, or High/Late Medieval start)
            else
            {
                realmGovernment = GovernmentMap.Feudal;
            }

            // A crowned hegemon does not settle for the ordinary bureaucracy. Administrative is what
            // the cascade above can reach; a realm that rules the world should read as something
            // stranger. The hegemony is written as h_china, so the Son of Heaven's own government
            // fits it exactly: the ministry, the Mandate and the Dynastic Cycle all answer to that
            // key. The steppe share keeps the one exception vanilla itself makes, for the same
            // reason the horde clause reads it: a bureaucracy grown out of the grasslands is the
            // Khitan one, and vanilla's 1178 Jin hold the Mandate as steppe-admin. Both degrade to
            // feudal without the expansion.
            if (hegemonSeat is not null
                && realmGovernment == GovernmentMap.Administrative
                && realmCounties.Contains(hegemonSeat))
            {
                realmGovernment = steppeShare >= 0.20
                    ? GovernmentMap.SteppeAdmin
                    : GovernmentMap.Celestial;
            }

            // Assign the unified government to all constituent counties
            foreach (var county in realmCounties)
            {
                var countyDraw = new Rng(county.Index ^ salt);
                var countyDomTerrain = Development.DominantTerrain(county, provinceTerrain);
                int cLevel = development.GetValueOrDefault(county);

                // 1. If realm is Nomadic or any bureaucracy, all counties stay unified
                if (realmGovernment == GovernmentMap.Nomad
                    || GovernmentMap.IsAdminFamily(realmGovernment))
                {
                    assigned[county] = realmGovernment;
                }
                // 2. Peripheral Steppe marches in feudal/clan/tribal realms become Nomadic hordes
                else if (cfg.EnableNomadHordes && countyDomTerrain == TerrainClass.Steppe && countyDraw.Chance(effectiveNomadShare))
                {
                    assigned[county] = GovernmentMap.Nomad;
                    nomadTitles.Add(county);
                }
                // 3. Merchant Republic ports
                else if (realmGovernment is GovernmentMap.Feudal or GovernmentMap.Clan && IsRepublic(county, cLevel, countyDraw))
                {
                    assigned[county] = GovernmentMap.Republic;
                }
                // 4. Default to sovereign's government
                else
                {
                    assigned[county] = realmGovernment;
                }
            }
        }

        return new GovernmentMap(assigned, adminTitles, nomadTitles);

        bool IsRepublic(Title county, int level, Rng draw)
            => cfg.RepublicShare > 0
               && IsCoastal(county, provinceTerrain)
               && level >= 10
               && draw.NextDouble() < cfg.RepublicShare;
    }

    /// <summary>Title tiers as a number, so a hierarchy can be walked biggest-first.</summary>
    private static int TierRank(string tier) => tier switch
    {
        "h" => 5, "e" => 4, "k" => 3, "d" => 2, "c" => 1, _ => 0,
    };

    private static Title TopLiege(Title county, RealmMap realms)
    {
        var primary = HistoryWriter.Primary(county, realms);
        var current = primary;

        while (realms.Liege.TryGetValue(current, out var liege))
        {
            current = liege;
        }

        return realms.HolderCounty.TryGetValue(current, out var topHolder) ? topHolder : county;
    }
}