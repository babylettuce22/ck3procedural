using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Settles a generated world with the base game's own peoples and faiths
/// (<see cref="MapConfig.ContentSourceMode.VanillaWorld"/>).
///
/// <b>Geography stays ours.</b> <see cref="Cultures.Build"/> and <see cref="Faiths.Build"/> run
/// exactly as they do on a procedural map, and this pass only swaps <em>who</em> lives in the
/// regions they grew: each generated heritage becomes a vanilla heritage, each culture a vanilla
/// culture of that heritage, each religion a vanilla religion and each faith one of its faiths.
/// Everything contiguity, kinship and neighbourhood bought on the generated map carries over for
/// free, because the regions are the same regions — sister cultures stay neighbours because they
/// were neighbours before they had names. It is the Azgaar import's seam turned around: there the
/// identities come from an export and the geography from the export; here the identities come from
/// the game and the geography from us.
///
/// <b>Choosing, by the map's own evidence.</b> A generated culture already carries what its ground
/// made it — terrain traditions, dress picked by climate (<see cref="ClothingClimate"/>), building
/// style — and so does every vanilla culture, so the fit is an overlap of those rather than a table
/// anyone wrote: a steppe people gets horse-breeder traditions and steppe dress, and so do the
/// vanilla cultures it can become. A faith is then chosen by vanilla's own province history, which
/// says how many counties of each culture follow each faith. Seeded noise keeps two seeds from
/// settling the same map the same way.
///
/// <b>Inherited means read-only.</b> Every object built here is flagged Inherited, and the writers
/// are handed <see cref="CultureMap.Declared"/> / <see cref="FaithMap.Declared"/>, so nothing
/// vanilla defines is written again. What the mod still writes is what it always owned: province
/// and character history that references these keys, holy sites rebound onto our counties, and the
/// head-of-faith titles, which common/landed_titles being replaced means we must declare.
/// </summary>
public static class VanillaIdentities
{
    /// <summary>Fewest place names a language is built from; below it the heritage's is used.</summary>
    private const int MinCorpus = 40;

    /// <summary>Fewest given names of each sex a vanilla culture needs to be placed at all.</summary>
    private const int MinGivenNames = 8;

    // ---------------------------------------------------------------------------------------------
    // Cultures
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// How well a vanilla culture suits the ground a generated one grew on, 0 to about 1: the
    /// terrain traditions it shares, then dress (which ClothingClimate chose by climate), then
    /// building style. Evidence the map already produced, compared with what vanilla wrote.
    /// </summary>
    internal static double Fit(Culture g, VanillaCatalog.CultureDef v)
        => (2.0 * g.Traditions.Count(v.Traditions.Contains)
          + 3.0 * (SharesToken(g.ClothingGfx, v.ClothingGfx) ? 1 : 0)
          + 1.5 * (SharesToken(g.BuildingGfx, v.BuildingGfx) ? 1 : 0)) / 10.0;

    /// <summary>
    /// The vanilla cultures a county may be given: seated in vanilla history (a template culture
    /// nobody lives in is not a people), with a known heritage, and with enough names to name a court.
    /// </summary>
    public static Dictionary<string, VanillaCatalog.CultureDef> Placeable(VanillaCatalog catalog)
        => catalog.Cultures.Values
            .Where(c => c.Counties > 0 && catalog.PillarNames.ContainsKey(c.Heritage))
            .Where(c => Given(catalog, c, female: false).Count >= MinGivenNames
                     && Given(catalog, c, female: true).Count >= MinGivenNames)
            .ToDictionary(c => c.Key, StringComparer.Ordinal);

    /// <param name="position">Where a county sits on this map, in pixels; null for one with no
    /// land province.</param>
    /// <param name="width">Map width in the same units, to lay the map onto a vanilla window.</param>
    /// <param name="height">Map height, likewise.</param>
    public static CultureMap SettleCultures(CultureMap generated, VanillaCatalog catalog,
        Func<Title, (double X, double Y)?> position, double width, double height, Rng rng)
    {
        var candidates = Placeable(catalog).Values
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "No vanilla culture could be read from the game directory, so the world cannot be settled " +
                "with vanilla cultures. Check the game path, or use ContentSource = Procedural.");

        var byHeritage = candidates.GroupBy(c => c.Heritage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Counties)
                                             .ThenBy(c => c.Key, StringComparer.Ordinal).ToList(),
                          StringComparer.Ordinal);

        double HeritageFit(Heritage h, List<VanillaCatalog.CultureDef> pool)
        {
            double weight = Math.Max(1, h.Cultures.Sum(c => c.Counties.Count));
            return h.Cultures.Sum(g => Math.Max(1, g.Counties.Count) * pool.Max(v => Fit(g, v))) / weight;
        }

        (double X, double Y)? Centre(IEnumerable<Title> counties)
        {
            double x = 0, y = 0;
            int n = 0;
            foreach (var county in counties)
                if (position(county) is { } p) { x += p.X; y += p.Y; n++; }
            return n == 0 ? null : (x / n, y / n);
        }

        // Biggest peoples first: they claim first in every greedy step, and a small heritage
        // settling for its second choice costs the map less.
        var generatedHeritages = generated.Heritages
            .OrderByDescending(h => h.Cultures.Sum(c => c.Counties.Count))
            .ThenBy(h => h.Key, StringComparer.Ordinal).ToList();
        var weightOf = generatedHeritages.ToDictionary(h => h, h => (double)Math.Max(1, h.Cultures.Sum(c => c.Counties.Count)));
        var generatedHome = generatedHeritages.ToDictionary(h => h,
            h => Centre(h.Cultures.SelectMany(c => c.Counties)) is { } p ? (p.X / width, p.Y / height) : (0.5, 0.5));

        var vanillaHome = byHeritage.ToDictionary(kv => kv.Key, kv => Home(kv.Value), StringComparer.Ordinal);
        var located = byHeritage.Keys.Where(k => vanillaHome[k] is not null).Order(StringComparer.Ordinal).ToList();

        // A window on the vanilla world, then the generated heritages laid onto the peoples inside
        // it by where each sits: the map's north-west people becomes the window's north-west
        // people, so neighbours here are neighbours there too, and a world reads as one region of
        // Earth rather than a sample of all of it. A few seeded windows are tried and the one whose
        // peoples also best suit this map's ground wins, so the seed picks the region and the land
        // settles ties. With no vanilla geography to read, suitability alone decides.
        Dictionary<Heritage, string> heritageFor;
        string region;
        int n = generatedHeritages.Count;

        if (located.Count >= n && n > 0)
        {
            var centres = located.OrderBy(k => -Math.Pow(rng.Double(), 1.0 / Math.Sqrt(byHeritage[k].Sum(c => c.Counties))))
                                 .ThenBy(k => k, StringComparer.Ordinal)
                                 .Take(Math.Min(8, located.Count)).ToList();

            (Dictionary<Heritage, string> Assignment, double Cost, string Centre)? best = null;
            foreach (string centre in centres)
            {
                var here = vanillaHome[centre]!.Value;
                var pool = located.OrderBy(k => Distance(vanillaHome[k]!.Value, here)).ThenBy(k => k, StringComparer.Ordinal)
                                  .Take(Math.Min(located.Count, n + Math.Max(2, n / 2))).ToList();
                var laid = Normalise(pool.ToDictionary(k => k, k => vanillaHome[k]!.Value));

                double Cost(Heritage h, string k)
                    => weightOf[h] * (Distance(generatedHome[h], laid[k])
                                      - 0.5 * HeritageFit(h, byHeritage[k])
                                      + 0.15 * Math.Max(0, h.Cultures.Count - byHeritage[k].Count));

                var assignment = Assign(generatedHeritages, pool, Cost);
                double total = assignment.Sum(kv => Cost(kv.Key, kv.Value));
                if (best is null || total < best.Value.Cost) best = (assignment, total, centre);
            }

            heritageFor = best!.Value.Assignment;
            region = $"around {catalog.PillarNames.GetValueOrDefault(best.Value.Centre, best.Value.Centre)}";
        }
        else
        {
            heritageFor = Assign(generatedHeritages, byHeritage.Keys.Order(StringComparer.Ordinal).ToList(),
                (h, k) => -weightOf[h] * HeritageFit(h, byHeritage[k]));
            region = "by fit alone (vanilla map unreadable)";
        }

        // Within each chosen heritage, generated cultures take vanilla cultures of it, largest first.
        // One the heritage has run out for joins whichever already-placed sibling fits it best: two
        // regions of one people rather than a culture from outside the family.
        var cultureFor = new Dictionary<Culture, VanillaCatalog.CultureDef>();
        var usedCultures = new HashSet<string>(StringComparer.Ordinal);

        // The same laying-out one level down: a heritage's cultures take its vanilla cultures by
        // where each sits within the heritage, so Norman stays north of Occitan. When the vanilla
        // heritage has fewer cultures than the generated one, the smallest generated regions join
        // whichever placed sibling sits nearest — two regions of one people rather than a culture
        // from outside the family.
        foreach (var heritage in generatedHeritages)
        {
            var pool = byHeritage[heritageFor[heritage]];
            var members = heritage.Cultures.OrderByDescending(c => c.Counties.Count)
                                           .ThenBy(c => c.Key, StringComparer.Ordinal).ToList();

            var mine = Normalise(members.ToDictionary(c => c, c => Centre(c.Counties) ?? (0.5, 0.5)));
            var theirs = Normalise(pool.ToDictionary(v => v, v => v.Home ?? (0.5, 0.5)));

            double Cost(Culture g, VanillaCatalog.CultureDef v)
                => Distance(mine[g], theirs[v]) - 0.5 * Fit(g, v) - 0.03 * Math.Log(1 + v.Counties);

            var leading = members.Take(pool.Count).ToList();
            foreach (var (g, v) in Assign(leading, pool, Cost)) cultureFor[g] = v;
            foreach (var g in members.Skip(pool.Count))
                cultureFor[g] = leading.Select(l => cultureFor[l]).Distinct()
                                       .OrderBy(v => Cost(g, v)).ThenBy(v => v.Key, StringComparer.Ordinal).First();

            foreach (var g in members) usedCultures.Add(cultureFor[g].Key);
        }

        // Build the inherited objects, one per vanilla key, in the order the generated ones stood.
        var heritages = new Dictionary<string, Heritage>(StringComparer.Ordinal);
        var cultures = new Dictionary<string, Culture>(StringComparer.Ordinal);
        var sources = new Dictionary<Culture, List<Culture>>();

        foreach (var g in generated.Cultures.Where(cultureFor.ContainsKey))
        {
            var v = cultureFor[g];

            if (!heritages.TryGetValue(v.Heritage, out var heritage))
                heritages[v.Heritage] = heritage = BuildHeritage(v.Heritage, byHeritage[v.Heritage], usedCultures,
                    catalog, g.Heritage, rng);

            if (!cultures.TryGetValue(v.Key, out var culture))
            {
                culture = BuildCulture(v, heritage, g, g.MeanDevelopment, catalog, rng);
                cultures[v.Key] = culture;
                sources[culture] = [];
                heritage.Cultures.Add(culture);
            }

            sources[culture].Add(g);
            culture.Counties.AddRange(g.Counties);
        }

        // A merged culture's development is its regions' county-weighted mean.
        foreach (var (culture, merged) in sources.Where(kv => kv.Value.Count > 1))
        {
            double total = merged.Sum(m => m.MeanDevelopment * Math.Max(1, m.Counties.Count));
            int weight = merged.Sum(m => Math.Max(1, m.Counties.Count));
            var replacement = CopyWithDevelopment(culture, total / weight);
            cultures[culture.Key] = replacement;
            int at = replacement.Heritage.Cultures.IndexOf(culture);
            replacement.Heritage.Cultures[at] = replacement;
        }

        var byCounty = new Dictionary<Title, Culture>();
        foreach (var (county, g) in generated.ByCounty)
            if (cultureFor.TryGetValue(g, out var v)) byCounty[county] = cultures[v.Key];

        int merges = generated.Cultures.Count - cultures.Count;
        Console.WriteLine($"  vanilla cultures: {cultures.Count} cultures in {heritages.Count} heritages settled {region} " +
                          $"({string.Join(", ", cultures.Values.Take(12).Select(c => c.Name))}" +
                          (cultures.Count > 12 ? ", ..." : "") + ")" +
                          (merges > 0 ? $"; {merges} generated regions joined a sibling" : ""));

        return new CultureMap
        {
            Heritages = [.. heritages.Values],
            Cultures = [.. cultures.Values],
            ByCounty = byCounty,
        };
    }

    /// <summary>
    /// A vanilla heritage as an inherited <see cref="Heritage"/>: vanilla's pillar key and name, a
    /// place-name language built from every culture of the heritage (keyed by the language pillar
    /// of the lead culture — the first of <paramref name="kin"/> in use), and the lead's look.
    /// </summary>
    private static Heritage BuildHeritage(string key, List<VanillaCatalog.CultureDef> kin, IReadOnlySet<string> used,
        VanillaCatalog catalog, Heritage fallback, Rng rng)
    {
        var lead = kin.Where(k => used.Contains(k.Key)).DefaultIfEmpty(kin[0]).First();
        var language = PlaceLanguage(lead.Language, kin.SelectMany(k => k.PlaceNames).Distinct().ToList(),
                           catalog.PillarNames.GetValueOrDefault(lead.Language, lead.Language), rng)
                       ?? fallback.Language;

        return new Heritage
        {
            Key = key,
            Name = catalog.PillarNames[key],
            Language = language,
            Look = new VanillaVocabulary.Look(lead.Key, lead.CoaGfx, lead.BuildingGfx,
                lead.ClothingGfx, lead.UnitGfx, lead.Ethnicities),
            ImportedArchetype = fallback.ImportedArchetype,
            Inherited = true,
        };
    }

    /// <summary>
    /// A vanilla culture as an inherited <see cref="Culture"/>: vanilla's own identity, a tongue
    /// built from its place names, and its name lists. <paramref name="source"/> is the culture
    /// that stood on this ground before, for the few fallbacks vanilla cannot supply.
    /// </summary>
    private static Culture BuildCulture(VanillaCatalog.CultureDef v, Heritage heritage, Culture source,
        double meanDevelopment, VanillaCatalog catalog, Rng rng)
    {
        var tongue = PlaceLanguage(v.Language, v.PlaceNames,
                         catalog.PillarNames.GetValueOrDefault(v.Language, v.Language), rng)
                     ?? heritage.Language;

        var dynasties = Dynasties(catalog, v);
        return new Culture
        {
            Key = v.Key,
            Name = v.Name,
            Heritage = heritage,
            Tongue = tongue,
            MeanDevelopment = meanDevelopment,
            Color = v.Color ?? source.Color,
            Ethos = v.Ethos,
            MartialCustom = v.MartialCustom,
            HeadDetermination = v.HeadDetermination,
            Traditions = [.. v.Traditions],
            CoaGfx = v.CoaGfx,
            BuildingGfx = v.BuildingGfx,
            ClothingGfx = v.ClothingGfx,
            UnitGfx = v.UnitGfx,
            MaleNames = Given(catalog, v, female: false),
            FemaleNames = Given(catalog, v, female: true),
            // Vanilla's houses first; a list too short for one per seat is topped up from the
            // culture it replaces, so two seats still never share a house name.
            DynastyNames = dynasties.Count >= 20 ? dynasties : [.. dynasties, .. source.DynastyNames],
            PatronymSuffixMale = tongue.PatronymMale,
            PatronymSuffixFemale = tongue.PatronymFemale,
            PatronymIsPrefix = tongue.PatronymIsPrefix,
            LocationPrefix = tongue.Particle,
            AlwaysUsePatronym = false,
            ImportedArchetype = source.ImportedArchetype,
            Inherited = true,
            NameKeys = NameKeysOf(catalog, v),
        };
    }

    private static Dictionary<string, string> NameKeysOf(VanillaCatalog catalog, VanillaCatalog.CultureDef culture)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string list in culture.NameLists)
            if (catalog.NameLists.TryGetValue(list, out var def))
                foreach (var (name, key) in def.Keys) keys.TryAdd(name, key);
        return keys;
    }

    /// <summary>
    /// The culture map rebuilt from each county's own vanilla culture, on a world of vanilla titles:
    /// c_paris speaks French because vanilla's Paris did, not because a region grew there. A county
    /// whose vanilla culture cannot be placed (too few names to name a court) takes the culture of
    /// the nearest county that can; one with no vanilla county keeps the culture it had.
    /// </summary>
    /// <param name="cultureOf">Each county's vanilla culture key at the start date.</param>
    /// <param name="projected">Where each county lands on vanilla's map, for the nearest-county fallback.</param>
    /// <remarks>The wilderness is recast like any other ground. Its counties take the unsettled
    /// culture later, but the culture they held before that is still listed, and a region the
    /// heritage window settled elsewhere on Earth would otherwise linger in the list.</remarks>
    public static CultureMap RecastCultures(CultureMap current, IReadOnlyDictionary<Title, string> cultureOf,
        IReadOnlyDictionary<Title, (double X, double Y)> projected, Dictionary<Title, int> development,
        VanillaCatalog catalog, Rng rng)
    {
        var placeable = Placeable(catalog);
        var good = cultureOf.Where(kv => placeable.ContainsKey(kv.Value) && projected.ContainsKey(kv.Key)).ToList();

        string KeyOf(Title county, Culture now)
        {
            if (cultureOf.TryGetValue(county, out string? own) && placeable.ContainsKey(own)) return own;
            if (!projected.TryGetValue(county, out var p) || good.Count == 0) return now.Key;
            return good.OrderBy(kv => Distance(projected[kv.Key], p)).ThenBy(kv => kv.Key.Index).First().Value;
        }

        var keyOf = new Dictionary<Title, string>();
        foreach (var (county, now) in current.ByCounty) keyOf[county] = KeyOf(county, now);

        var used = keyOf.Values.ToHashSet(StringComparer.Ordinal);
        var byHeritage = placeable.Values.GroupBy(c => c.Heritage, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Counties).ThenBy(c => c.Key, StringComparer.Ordinal).ToList(),
                          StringComparer.Ordinal);

        var heritages = new Dictionary<string, Heritage>(StringComparer.Ordinal);
        var cultures = new Dictionary<string, Culture>(StringComparer.Ordinal);
        var byCounty = new Dictionary<Title, Culture>();

        foreach (var (county, now) in current.ByCounty)
        {
            string key = keyOf[county];

            if (!cultures.TryGetValue(key, out var culture))
            {
                if (!placeable.TryGetValue(key, out var v))
                {
                    // Kept as it was: a culture this pass did not choose (the unsettled one, or a
                    // county vanilla never mapped). Reused whole; its county list is rebuilt below.
                    culture = now;
                    culture.Counties.Clear();
                }
                else
                {
                    if (!heritages.TryGetValue(v.Heritage, out var heritage))
                        heritages[v.Heritage] = heritage = BuildHeritage(v.Heritage, byHeritage[v.Heritage], used,
                            catalog, now.Heritage, rng);

                    var mine = keyOf.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList();
                    double mean = mine.Count == 0 ? now.MeanDevelopment : mine.Average(c => development.GetValueOrDefault(c));
                    culture = BuildCulture(v, heritage, now, mean, catalog, rng);
                    heritage.Cultures.Add(culture);
                }

                cultures[key] = culture;
            }

            culture.Counties.Add(county);
            byCounty[county] = culture;
        }

        Console.WriteLine($"  vanilla cultures: recast county by county — {cultures.Count} cultures in {heritages.Count} heritages " +
                          $"({string.Join(", ", cultures.Values.OrderByDescending(c => c.Counties.Count).Take(12).Select(c => c.Name))}" +
                          (cultures.Count > 12 ? ", ..." : "") + ")");

        return new CultureMap
        {
            Heritages = [.. heritages.Values, .. cultures.Values.Select(c => c.Heritage).Where(h => !heritages.ContainsValue(h)).Distinct()],
            Cultures = [.. cultures.Values],
            ByCounty = byCounty,
        };
    }

    /// <summary>Culture's MeanDevelopment is init-only; a merged culture is rebuilt with the mean.</summary>
    private static Culture CopyWithDevelopment(Culture c, double mean)
    {
        var copy = new Culture
        {
            Key = c.Key, Name = c.Name, Heritage = c.Heritage, Tongue = c.Tongue, MeanDevelopment = mean,
            Color = c.Color, Ethos = c.Ethos, MartialCustom = c.MartialCustom,
            HeadDetermination = c.HeadDetermination, Traditions = c.Traditions,
            CoaGfx = c.CoaGfx, BuildingGfx = c.BuildingGfx, ClothingGfx = c.ClothingGfx, UnitGfx = c.UnitGfx,
            MaleNames = c.MaleNames, FemaleNames = c.FemaleNames, DynastyNames = c.DynastyNames,
            PatronymSuffixMale = c.PatronymSuffixMale, PatronymSuffixFemale = c.PatronymSuffixFemale,
            PatronymIsPrefix = c.PatronymIsPrefix, LocationPrefix = c.LocationPrefix,
            AlwaysUsePatronym = c.AlwaysUsePatronym, ImportedArchetype = c.ImportedArchetype,
            Inherited = c.Inherited, NameKeys = c.NameKeys,
        };
        copy.Counties.AddRange(c.Counties);
        return copy;
    }

    private static List<string> Given(VanillaCatalog catalog, VanillaCatalog.CultureDef culture, bool female)
    {
        var names = new List<string>();
        foreach (string list in culture.NameLists)
            if (catalog.NameLists.TryGetValue(list, out var def))
                foreach (string n in female ? def.Female : def.Male)
                    if (!names.Contains(n)) names.Add(n);
        return names;
    }

    private static List<string> Dynasties(VanillaCatalog catalog, VanillaCatalog.CultureDef culture)
    {
        var names = new List<string>();
        foreach (string list in culture.NameLists)
            if (catalog.NameLists.TryGetValue(list, out var def))
                foreach (string n in def.Dynasties)
                    if (!names.Contains(n)) names.Add(n);
        return names;
    }

    /// <summary>A heritage's vanilla home: its cultures' homes, weighted by the counties behind each.</summary>
    private static (double X, double Y)? Home(List<VanillaCatalog.CultureDef> cultures)
    {
        double x = 0, y = 0, w = 0;
        foreach (var c in cultures)
            if (c.Home is { } h) { x += h.X * c.HomeCounties; y += h.Y * c.HomeCounties; w += c.HomeCounties; }
        return w > 0 ? (x / w, y / w) : null;
    }

    internal static double Distance((double X, double Y) a, (double X, double Y) b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// Positions rescaled into the unit square their bounding box spans, each axis on its own, so
    /// two sets of places on maps of different sizes and shapes can be compared by arrangement —
    /// who is north-west of whom — rather than by coordinates. A single place, or an axis with no
    /// spread, sits at the middle.
    /// </summary>
    internal static Dictionary<T, (double X, double Y)> Normalise<T>(Dictionary<T, (double X, double Y)> at)
        where T : notnull
    {
        if (at.Count == 0) return [];
        double minX = at.Values.Min(p => p.X), maxX = at.Values.Max(p => p.X);
        double minY = at.Values.Min(p => p.Y), maxY = at.Values.Max(p => p.Y);
        double Scale(double v, double lo, double hi) => hi - lo < 1e-9 ? 0.5 : (v - lo) / (hi - lo);
        return at.ToDictionary(kv => kv.Key, kv => (Scale(kv.Value.X, minX, maxX), Scale(kv.Value.Y, minY, maxY)));
    }

    /// <summary>
    /// Gives each item a slot at low total cost: greedily in the items' own order (callers put the
    /// biggest first), then pairwise swaps and moves into unused slots while any lowers the total.
    /// Slots are used once each until every one is taken, after which they are shared. Exact
    /// optimality is not the point — the inputs are a handful of places and a heuristic fit — but
    /// the swap pass stops a greedy early pick from stranding a later item on the far side of the map.
    /// </summary>
    internal static Dictionary<TItem, TSlot> Assign<TItem, TSlot>(IReadOnlyList<TItem> items, IReadOnlyList<TSlot> slots,
        Func<TItem, TSlot, double> cost) where TItem : notnull where TSlot : notnull
    {
        var result = new Dictionary<TItem, TSlot>();
        if (slots.Count == 0) return result;
        var used = new HashSet<TSlot>();

        foreach (var item in items)
        {
            var open = slots.Where(s => !used.Contains(s)).ToList();
            var pick = (open.Count > 0 ? open : slots).OrderBy(s => cost(item, s)).First();
            result[item] = pick;
            used.Add(pick);
        }

        for (int pass = 0; pass < 25; pass++)
        {
            bool improved = false;

            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    var (a, b) = (items[i], items[j]);
                    var (sa, sb) = (result[a], result[b]);
                    if (cost(a, sb) + cost(b, sa) < cost(a, sa) + cost(b, sb) - 1e-9)
                    {
                        result[a] = sb;
                        result[b] = sa;
                        improved = true;
                    }
                }

            foreach (var item in items)
                foreach (var slot in slots.Where(s => !result.ContainsValue(s)))
                    if (cost(item, slot) < cost(item, result[item]) - 1e-9)
                    {
                        result[item] = slot;
                        improved = true;
                    }

            if (!improved) break;
        }

        return result;
    }

    private static bool SharesToken(string a, string b)
    {
        var mine = a.Split([' ', '{', '}'], StringSplitOptions.RemoveEmptyEntries);
        var theirs = b.Split([' ', '{', '}'], StringSplitOptions.RemoveEmptyEntries);
        return mine.Any(theirs.Contains);
    }

    /// <summary>
    /// A language whose words sound like <paramref name="corpus"/> — vanilla's own names for the
    /// counties and baronies a culture holds — built by the same Markov chain an Azgaar name base
    /// drives (<see cref="Language.FromNameBase"/>). Null when the corpus is too thin to carry one.
    /// Keyed by the vanilla language pillar, which is what a culture written against it names.
    /// </summary>
    private static Language? PlaceLanguage(string key, List<string> corpus, string name, Rng rng)
    {
        var words = corpus.Select(n => n.Replace(",", "").Trim()).Where(n => n.Length >= 3).Distinct().ToList();
        if (words.Count < MinCorpus) return null;

        var lengths = words.Select(w => w.Length).Order().ToList();
        int min = Math.Max(3, lengths[lengths.Count / 10]);
        int max = Math.Max(min + 2, lengths[lengths.Count * 9 / 10]);

        // Letters the corpus doubles often enough to be the language's habit rather than an accident.
        var doubled = words.SelectMany(w => w.ToLowerInvariant().Zip(w.ToLowerInvariant().Skip(1))
                                             .Where(p => p.First == p.Second && char.IsLetter(p.First))
                                             .Select(p => p.First).Distinct())
                           .GroupBy(c => c)
                           .Where(g => g.Count() >= Math.Max(2, words.Count / 33))
                           .Select(g => g.Key).Order();

        var markov = AzgaarNames.FromBase(new AzgaarNameBase
        {
            Name = name,
            Min = min,
            Max = max,
            D = string.Concat(doubled),
            B = string.Join(",", words),
        });

        return markov is null ? null : Language.FromNameBase(key, markov, rng, name);
    }

    // ---------------------------------------------------------------------------------------------
    // Faiths
    // ---------------------------------------------------------------------------------------------

    /// <param name="faithOf">Each county's own vanilla faith key, on a world of vanilla titles
    /// (<see cref="VanillaTitles.Plan.State"/>); null to settle by region alone.</param>
    public static FaithMap SettleFaiths(FaithMap generated, CultureMap cultures, Dictionary<Title, int> development,
        VanillaCatalog catalog, VanillaVocabulary vocab, Rng rng, IReadOnlyDictionary<Title, string>? faithOf = null)
    {
        var live = catalog.Faiths.Values.Where(f => f.Counties > 0).ToList();
        if (live.Count == 0)
            throw new InvalidOperationException(
                "No vanilla faith could be read from the game directory, so the world cannot be settled " +
                "with vanilla faiths. Check the game path, or use ContentSource = Procedural.");

        var religions = live.Select(f => f.Religion).Distinct().OrderBy(r => r.Key, StringComparer.Ordinal).ToList();

        // How each culture prays in vanilla, as a share of its counties per faith.
        var cultureTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ((culture, _), n) in catalog.CultureFaith)
            cultureTotals[culture] = cultureTotals.GetValueOrDefault(culture) + n;

        double Share(string culture, string faith)
            => cultureTotals.TryGetValue(culture, out int total) && total > 0
                ? catalog.CultureFaith.GetValueOrDefault((culture, faith)) / (double)total
                : 0;

        double Affinity(IReadOnlyList<Title> counties, IEnumerable<VanillaCatalog.FaithDef> faiths)
        {
            if (counties.Count == 0) return 0;
            var keys = faiths.Select(f => f.Key).ToList();
            return counties.Sum(c => keys.Sum(f => Share(cultures.For(c).Key, f))) / counties.Count;
        }

        // Where the peoples on some counties come from in vanilla, and how near a faith's own
        // heartland is to that. Only a tie-break: affinity decides wherever vanilla's history has
        // an opinion, and this decides where it has none — the second faith of a Catholic region is
        // then a heresy of the region (Insularism, Mozarabism) rather than the largest faith left
        // anywhere (Nestorianism).
        (double X, double Y)? PeopleHome(IEnumerable<Title> counties)
        {
            double x = 0, y = 0;
            int n = 0;
            foreach (var county in counties)
                if (catalog.Cultures.TryGetValue(cultures.For(county).Key, out var def) && def.Home is { } h)
                { x += h.X; y += h.Y; n++; }
            return n == 0 ? null : (x / n, y / n);
        }

        static double Near((double X, double Y)? a, (double X, double Y)? b)
            => a is { } p && b is { } q ? 1.0 / (1.0 + Distance(p, q) / 600.0) : 0;

        static (double X, double Y)? ReligionHome(VanillaCatalog.ReligionDef religion)
        {
            double x = 0, y = 0, w = 0;
            foreach (var f in religion.Faiths)
                if (f.Home is { } h) { x += h.X * f.Counties; y += h.Y * f.Counties; w += f.Counties; }
            return w > 0 ? (x / w, y / w) : null;
        }

        var religionNoise = religions.ToDictionary(r => r.Key, _ => rng.Double(0, 0.25), StringComparer.Ordinal);
        var faithNoise = live.OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToDictionary(f => f.Key, _ => rng.Double(0, 0.1), StringComparer.Ordinal);

        var faithFor = new Dictionary<Faith, VanillaCatalog.FaithDef>();
        var religionUses = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedFaiths = new HashSet<string>(StringComparer.Ordinal);

        // Biggest religions first; a religion two generated regions both want is shared between
        // them (two Christian regions, not a Christian one and an Islamic one to keep the count),
        // with a penalty so a map of five religions is not five shades of one.
        foreach (var religion in generated.Religions
                     .OrderByDescending(r => r.Faiths.Sum(f => f.Counties.Count))
                     .ThenBy(r => r.Key, StringComparer.Ordinal))
        {
            var counties = religion.Faiths.SelectMany(f => f.Counties).ToList();
            var from = PeopleHome(counties);

            var chosen = religions
                .Select(r => (Religion: r,
                              Score: Affinity(counties, r.Faiths.Where(f => f.Counties > 0))
                                     + 0.15 * Near(ReligionHome(r), from)
                                     + religionNoise[r.Key]
                                     - 0.35 * religionUses.GetValueOrDefault(r.Key)))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Religion.Key, StringComparer.Ordinal)
                .First().Religion;

            religionUses[chosen.Key] = religionUses.GetValueOrDefault(chosen.Key) + 1;

            var pool = chosen.Faiths.Where(f => f.Counties > 0).ToList();
            foreach (var faith in religion.Faiths.OrderByDescending(f => f.Counties.Count)
                                                 .ThenBy(f => f.Key, StringComparer.Ordinal))
            {
                var fresh = pool.Where(f => !usedFaiths.Contains(f.Key)).ToList();
                var options = fresh.Count > 0
                    ? fresh
                    : pool.Where(f => usedFaiths.Contains(f.Key)).ToList() is { Count: > 0 } placed ? placed : pool;
                var people = PeopleHome(faith.Counties);

                var pick = options
                    .OrderByDescending(f => Affinity(faith.Counties, [f]) + 0.5 * Near(f.Home, people)
                                            + faithNoise[f.Key] + 0.02 * Math.Log(1 + f.Counties))
                    .ThenBy(f => f.Key, StringComparer.Ordinal).First();

                faithFor[faith] = pick;
                usedFaiths.Add(pick.Key);
            }
        }

        // Doctrine group of every doctrine, so a vanilla faith's doctrines can be read the way
        // generated ones are (by group). One outside every group keys itself.
        var outReligions = new Dictionary<string, Religion>(StringComparer.Ordinal);
        var outFaiths = new Dictionary<string, Faith>(StringComparer.Ordinal);
        var mergedFrom = new Dictionary<Faith, List<Faith>>();

        // A county's own vanilla faith, on a world of vanilla titles, outranks its region's: vanilla's
        // Paris was Catholic whatever the region around it was settled as.
        VanillaCatalog.FaithDef Pick(Title county, Faith g)
            => faithOf is not null && faithOf.TryGetValue(county, out string? own)
               && catalog.Faiths.TryGetValue(own, out var def) ? def : faithFor[g];

        Faith Ensure(VanillaCatalog.FaithDef v, Faith g, Title? first)
        {
            if (!outReligions.TryGetValue(v.Religion.Key, out var religion))
            {
                religion = NewReligion(v.Religion,
                    first is not null ? cultures.For(first).Heritage.Language : g.Religion.Language,
                    g.Religion.GraphicalFaith, catalog, vocab);
                outReligions[religion.Key] = religion;
            }

            if (!outFaiths.TryGetValue(v.Key, out var faith))
            {
                faith = NewFaith(v, religion, vocab);
                outFaiths[v.Key] = faith;
                religion.Faiths.Add(faith);
                mergedFrom[faith] = [];
            }

            if (!mergedFrom[faith].Contains(g)) mergedFrom[faith].Add(g);
            return faith;
        }

        foreach (var g in generated.Faiths.Where(faithFor.ContainsKey))
            foreach (var county in g.Counties)
                Ensure(Pick(county, g), g, county).Counties.Add(county);

        var byCounty = new Dictionary<Title, Faith>();
        foreach (var (county, g) in generated.ByCounty)
            if (faithFor.ContainsKey(g)) byCounty[county] = Ensure(Pick(county, g), g, county);

        var map = new FaithMap
        {
            Religions = [.. outReligions.Values],
            Faiths = [.. outFaiths.Values],
            ByCounty = byCounty,
            ImportedStructure = generated.ImportedStructure,
        };

        int sites = PlaceHolySites(map, mergedFrom, development, catalog);
        int heads = SeatHeads(map, catalog);

        Console.WriteLine($"  vanilla faiths: {outFaiths.Count} faiths in {outReligions.Count} religions settled " +
                          $"({string.Join(", ", outFaiths.Values.Take(10).Select(f => f.Name))}" +
                          (outFaiths.Count > 10 ? ", ..." : "") + $"), {sites} holy sites, {heads} heads of faith");

        return map;
    }

    /// <summary>Doctrines by group — the shape generated religions keep them in — tenets left out.
    /// A doctrine in no group keys itself.</summary>
    private static Dictionary<string, string> Grouped(IEnumerable<string> doctrines, VanillaVocabulary vocab)
    {
        var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (group, members) in vocab.DoctrineGroups)
            foreach (string doctrine in members) groupOf.TryAdd(doctrine, group);
        var tenets = vocab.Tenets.ToHashSet(StringComparer.Ordinal);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string d in doctrines.Where(d => !tenets.Contains(d)))
            map[groupOf.GetValueOrDefault(d, d)] = d;
        return map;
    }

    /// <summary>A vanilla religion as an inherited <see cref="Religion"/>.</summary>
    private static Religion NewReligion(VanillaCatalog.ReligionDef v, Language language, string fallbackGfx,
        VanillaCatalog catalog, VanillaVocabulary vocab)
    {
        var doctrines = v.Doctrines;
        return new Religion
        {
            Key = v.Key,
            Name = v.Name,
            Language = language,
            GraphicalFaith = v.GraphicalFaith ?? fallbackGfx,
            Monotheist = doctrines.Contains("doctrine_monotheist"),
            Abrahamic = v.Family == Faiths.AbrahamicFamily,
            LayClergy = doctrines.Contains("doctrine_theocracy_lay_clergy"),
            Doctrines = Grouped(doctrines, vocab),
            Virtues = [.. v.Virtues],
            Sins = [.. v.Sins],
            CoronationCrown = catalog.CrownReligions.Contains(v.Key),
            Localization = [],
            LocalizationText = [],
            Inherited = true,
        };
    }

    /// <summary>A vanilla faith as an inherited <see cref="Faith"/> of <paramref name="religion"/>.</summary>
    private static Faith NewFaith(VanillaCatalog.FaithDef v, Religion religion, VanillaVocabulary vocab)
    {
        var tenets = vocab.Tenets.ToHashSet(StringComparer.Ordinal);
        return new Faith
        {
            Key = v.Key,
            Name = v.Name,
            Religion = religion,
            Color = v.Color,
            Icon = v.Icon,
            Tenets = v.Doctrines.Where(tenets.Contains).ToList(),
            DoctrineOverrides = Grouped(v.Doctrines, vocab),
            IsOrganized = !v.Doctrines.Contains("unreformed_faith_doctrine")
                          && !v.Religion.Doctrines.Contains("unreformed_faith_doctrine"),
            Inherited = true,
        };
    }

    /// <summary>
    /// A vanilla culture added to the map with no land — the people of a historical ruler whose
    /// own kind holds no county here. Its heritage is the map's when present, else built.
    /// </summary>
    public static Culture Landless(VanillaCatalog.CultureDef v, CultureMap cultures, VanillaCatalog catalog, Rng rng)
    {
        var heritage = cultures.Heritages.FirstOrDefault(h => h.Key == v.Heritage);
        if (heritage is null)
        {
            var kin = Placeable(catalog).Values.Where(c => c.Heritage == v.Heritage)
                .OrderByDescending(c => c.Counties).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
            heritage = BuildHeritage(v.Heritage, kin, new HashSet<string>([v.Key], StringComparer.Ordinal),
                catalog, cultures.Heritages[0], rng);
            cultures.Heritages.Add(heritage);
        }

        var culture = BuildCulture(v, heritage, cultures.Cultures[0], 0, catalog, rng);
        heritage.Cultures.Add(culture);
        cultures.Cultures.Add(culture);
        return culture;
    }

    /// <summary>A vanilla faith added to the map with no land; see <see cref="Landless"/>.</summary>
    public static Faith? LandlessFaith(string key, FaithMap faiths, CultureMap cultures, VanillaCatalog catalog)
    {
        if (!catalog.Faiths.TryGetValue(key, out var v) || VanillaVocabulary.Current is not { } vocab) return null;

        var religion = faiths.Religions.FirstOrDefault(r => r.Key == v.Religion.Key);
        if (religion is null)
        {
            var sample = faiths.Religions.FirstOrDefault();
            religion = NewReligion(v.Religion, sample?.Language ?? cultures.Heritages[0].Language,
                sample?.GraphicalFaith ?? "", catalog, vocab);
            faiths.Religions.Add(religion);
        }

        var faith = NewFaith(v, religion, vocab);
        religion.Faiths.Add(faith);
        faiths.Faiths.Add(faith);
        return faith;
    }

    /// <summary>
    /// Binds each placed faith's vanilla holy sites to counties of this map.
    ///
    /// The counties the procedural pass already chose for the generated faiths come first — they
    /// were picked for development and spread, and are the best places this map has — then the
    /// faith's own richest counties, then its religion's. A site two faiths share is bound once, by
    /// whichever reaches it first, and the other faith finds it already there: one Jerusalem,
    /// contested, exactly as in vanilla. No county carries two sites.
    /// </summary>
    private static int PlaceHolySites(FaithMap map, Dictionary<Faith, List<Faith>> mergedFrom,
        Dictionary<Title, int> development, VanillaCatalog catalog)
    {
        var bound = new Dictionary<string, Title>(StringComparer.Ordinal);
        var taken = new HashSet<Title>();
        var everywhere = map.Faiths.SelectMany(f => f.Counties).Distinct()
            .OrderByDescending(c => development.GetValueOrDefault(c)).ThenBy(c => c.Index).ToList();

        // On a world of vanilla titles the site's own county may be on the map — c_jerusalem is
        // Jerusalem — and then that is where it goes, whichever faith holds the ground.
        var byKey = map.ByCounty.Keys.GroupBy(c => c.Key, StringComparer.Ordinal)
                                     .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var (site, county) in catalog.HolySiteCounty)
            if (byKey.TryGetValue(county, out var exact) && !taken.Contains(exact)
                && map.Faiths.Any(f => catalog.Faiths.TryGetValue(f.Key, out var d) && d.HolySites.Contains(site)))
            {
                bound[site] = exact;
                taken.Add(exact);
            }

        foreach (var faith in map.Faiths.OrderByDescending(f => f.Counties.Count).ThenBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!catalog.Faiths.TryGetValue(faith.Key, out var def)) continue;

            var candidates = mergedFrom[faith].SelectMany(g => g.HolySites.Select(s => s.County))
                .Concat(faith.Counties.OrderByDescending(c => development.GetValueOrDefault(c)).ThenBy(c => c.Index))
                .Concat(faith.Religion.Faiths.SelectMany(f => f.Counties)
                             .OrderByDescending(c => development.GetValueOrDefault(c)).ThenBy(c => c.Index))
                .Concat(everywhere)
                .Distinct()
                .ToList();

            foreach (string site in def.HolySites)
            {
                if (!catalog.HolySiteCounty.ContainsKey(site)) continue;

                if (!bound.TryGetValue(site, out var county))
                {
                    county = candidates.FirstOrDefault(c => !taken.Contains(c));
                    if (county is null) continue;
                    bound[site] = county;
                    taken.Add(county);
                }

                faith.HolySites.Add((site, county));
            }
        }

        return bound.Count;
    }

    /// <summary>
    /// Gives each placed faith its vanilla head of faith, seated on the holy site whose vanilla
    /// county was the title's capital (the Papacy on the site that was Rome) or failing that its
    /// first. A title several faiths name is held once, by the largest of them.
    /// </summary>
    private static int SeatHeads(FaithMap map, VanillaCatalog catalog)
    {
        var seated = new HashSet<string>(StringComparer.Ordinal);

        foreach (var faith in map.Faiths.OrderByDescending(f => f.Counties.Count).ThenBy(f => f.Key, StringComparer.Ordinal))
        {
            if (!catalog.Faiths.TryGetValue(faith.Key, out var def) || def.Head is not { } key) continue;
            if (!catalog.LandlessTitles.TryGetValue(key, out var title) || faith.HolySites.Count == 0) continue;
            if (!seated.Add(key)) continue;

            // The title's own vanilla capital when the map has it (the Papacy in c_roma), else the
            // holy site that stands where that capital was, else the faith's first site.
            var seat = map.ByCounty.Keys.FirstOrDefault(c => c.Key == title.Capital)
                ?? faith.HolySites
                    .Where(s => catalog.HolySiteCounty.GetValueOrDefault(s.Key) == title.Capital)
                    .Select(s => s.County)
                    .FirstOrDefault() ?? faith.HolySites[0].County;

            faith.Head = new HeadOfFaith
            {
                TitleKey = key,
                Name = title.Name,
                Seat = seat,
                Temporal = def.Doctrines.Contains("doctrine_temporal_head")
                           || (def.Religion.Doctrines.Contains("doctrine_temporal_head")
                               && !def.Doctrines.Contains("doctrine_spiritual_head")),
                Inherited = true,
                InheritedFields = title.Fields,
            };
        }

        return seated.Count;
    }
}
