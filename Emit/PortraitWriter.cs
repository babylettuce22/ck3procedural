using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Selects culturally matching DNA templates from vanilla for each bookmark character,
/// writing the full format to common/bookmark_portraits and the proper portrait_info
/// format to common/dna_data so 3D models match seamlessly.
/// </summary>
public static class PortraitWriter
{
    private static readonly Regex IdentityRegex =
        new(@"^[ \t]*[A-Za-z_0-9]+[ \t]*=[ \t]*\{", RegexOptions.Multiline);

    private static readonly Regex MaleTypeRegex =
        new(@"type\s*=\s*male\b", RegexOptions.IgnoreCase);

    // Vanilla dumps its bookmark portraits in four flavours — 238 male, 40 female, 45 boy, 9 girl —
    // and the type written in the file is the one the screen renders. A wife given a male record
    // stands there as a man, so the pool a request draws from has to follow the character.
    private static readonly Regex FemaleTypeRegex =
        new(@"type\s*=\s*female\b", RegexOptions.IgnoreCase);

    private static readonly Regex BoyTypeRegex =
        new(@"type\s*=\s*boy\b", RegexOptions.IgnoreCase);

    private static readonly Regex GirlTypeRegex =
        new(@"type\s*=\s*girl\b", RegexOptions.IgnoreCase);

    private static readonly Regex GenesRegex =
        new(@"genes\s*=\s*\{(?<content>(?>[^{}]+|\{(?<DEPTH>)|\}(?<-DEPTH>))*(?(DEPTH)(?!)))\}", RegexOptions.Singleline);

    /// <summary>
    /// One portrait to write. <paramref name="Female"/> and <paramref name="Child"/> choose which
    /// of vanilla's four template pools the DNA is borrowed from.
    ///
    /// Every name a bookmark file mentions needs one of these, nested companions included: ck3-tiger
    /// grades a <c>character = { name = X }</c> with no <c>common/bookmark_portraits/X.txt</c> as
    /// fatal, and CK3 1.13 crashes on it.
    ///
    /// <paramref name="AliasOf"/> names an earlier request to copy the face from, for the case where
    /// one man appears twice on the same screen — a liege drawn small beside his vassal and large in
    /// his own slot needs two records under two names, and two independent draws would give him two
    /// faces. Requests are answered in order, so the key aliased must come first in the list.
    /// </summary>
    public record CharacterPortraitRequest(
        string Key,
        Culture Culture,
        bool Female = false,
        bool Child = false,
        string? AliasOf = null,
        string Tier = "c",
        IReadOnlyList<string>? Traits = null
    );

    /// <summary>One <c>key={ ... }</c> line inside a DNA <c>genes = { }</c> block.</summary>
    private static readonly Regex GeneLineRegex =
        new(@"(?<ind>^[ \t]*)(?<key>[a-z_0-9]+)=\{[^{}]*\}", RegexOptions.Multiline);

    public static void WriteAll(string modDir, string gameDir,
        List<CharacterPortraitRequest> requests, EthnicityMap ethnicities, int seed = 0)
    {
        string sourceDir = Path.Combine(gameDir, "common", "bookmark_portraits");
        string bmDestDir = Path.Combine(modDir, "common", "bookmark_portraits");
        string dnaDestDir = Path.Combine(modDir, "common", "dna_data");

        Directory.CreateDirectory(bmDestDir);
        Directory.CreateDirectory(dnaDestDir);

        var men = LoadCategorizedTemplates(sourceDir, MaleTypeRegex);
        if (men.AllTemplates.Count == 0)
        {
            Console.WriteLine("  portraits: no vanilla male templates found, skipped");
            return;
        }

        var women = LoadCategorizedTemplates(sourceDir, FemaleTypeRegex);
        var boys = LoadCategorizedTemplates(sourceDir, BoyTypeRegex);
        var girls = LoadCategorizedTemplates(sourceDir, GirlTypeRegex);

        // Each falls back to the nearest pool that exists rather than to nothing: a game install
        // missing the nine girl records should still draw a girl as a woman, not as her father.
        TemplatePool PoolFor(CharacterPortraitRequest r)
        {
            if (r.Child && r.Female && girls.AllTemplates.Count > 0) return girls;
            if (r.Child && !r.Female && boys.AllTemplates.Count > 0) return boys;
            if (r.Female && women.AllTemplates.Count > 0) return women;
            return men;
        }

        var rng = new Rng(seed ^ 0x5087);
        var dnaFileBuilder = new StringBuilder();
        dnaFileBuilder.Append("# Generated DNA mappings for in-game characters\n\n");

        var written = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var req in requests)
        {
            string body;

            if (req.AliasOf is { } twin && written.TryGetValue(twin, out var already))
            {
                body = already;
            }
            else
            {
                var face = PickMatchingTemplate(req.Culture, req.Tier, PoolFor(req), rng);
                body = File.ReadAllText(face.Path);

                // Sanitize invalid/obsolete modifiers that vanilla templates sometimes retain
                body = Regex.Replace(body, @"[ \t]*custom_headgear\s*=\s*male_empty\r?\n?", "");
                body = Regex.Replace(body, @"[ \t]*custom_headgear\s*=\s*female_empty\r?\n?", "");

                // The borrowed ruler's own misfortunes do not come with him. Six of vanilla's records
                // carry a `special_*` accessory — Ya'qub's face mask, Ivar's wooden leg, a blindfold,
                // an eye patch — and a persistent DNA pins them, so a generated count inherited a
                // disfigurement mask with no disfigurement behind it. 326 of the 332 records name no
                // special gene at all, so dropping the line is the normal shape rather than a hack.
                body = Regex.Replace(body, @"[ \t]*special_[a-z_]+=\{[^{}]*\}\r?\n?", "");

                // Where the sex's own pool had nobody dressed for this culture, borrow the outfit
                // from the pool that does. Men are the donor set: 238 records over 33 regions.
                body = Wardrobe(body, face, req, men, rng);
                body = Grooming(body, req, men, rng);

                // Repaint the borrowed DNA in the character's own ethnicity before anything is
                // written, so the bookmark screen and the in-game portrait agree and both match the
                // realm.
                body = ApplyEthnicity(body, ethnicities.For(req.Culture), rng);

                // After the ethnicity, not before: the ethnicity is what a character of this culture
                // looks like, and albinism is what overrides it. Painting it first would have the
                // repaint put the colour back.
                body = TraitLooks(body, req.Traits);
            }

            written[req.Key] = body;
            string renamedBookmark = IdentityRegex.Replace(body, $"{req.Key} = {{", 1);

            // 1. Write for Bookmark Screen (common/bookmark_portraits/)
            ParadoxText.WriteBom(Path.Combine(bmDestDir, $"{req.Key}.txt"), renamedBookmark);

            // 2. Extract genes and wrap in proper portrait_info format for in-game DNA (common/dna_data/)
            var match = GenesRegex.Match(body);
            if (match.Success)
            {
                string genesContent = match.Groups["content"].Value.Trim();
                string dnaKey = $"dna_{req.Key}";

                dnaFileBuilder.Append($$"""
                {{dnaKey}} = {
                	portrait_info = {
                		genes = {
                {{genesContent}}
                		}
                	}
                	enabled = yes
                }


                """);
            }
        }

        // 3. Write for In-Game campaign load
        ParadoxText.WriteBom(Path.Combine(dnaDestDir, "00_generated_dna.txt"), dnaFileBuilder.ToString());

        Console.WriteLine($"  portraits: {requests.Count} culture-matched portraits written to bookmark_portraits and dna_data");
    }

    /// <summary>
    /// Rewrites a borrowed vanilla DNA template's genes in terms of the character's own generated
    /// ethnicity.
    ///
    /// Without this a bookmark character wore whichever vanilla ruler's DNA the culture's clothing
    /// most resembled — skin, hair, eyes, bone structure and all. DNA overrides ethnicity outright,
    /// so the drow leading a drow realm came out an ordinary brown-skinned human on the bookmark
    /// screen and stayed one in the campaign. These are the handful of characters a player looks at
    /// hardest, which is why it read as "some drow are still human coloured".
    ///
    /// Only genes the ethnicity actually defines are touched, and only lines the template already
    /// carries are rewritten, so nothing invalid can be introduced. Generated humans define no
    /// <c>skin_color</c> at all — they inherit complexion from their vanilla template ethnicity —
    /// so their borrowed DNA keeps the skin it came with, which is the intended behaviour rather
    /// than an omission.
    /// </summary>
    private static string ApplyEthnicity(string body, EthnicityDef? eth, Rng rng)
    {
        if (eth is null) return body;

        var genes = GenesRegex.Match(body);
        if (!genes.Success) return body;

        var content = genes.Groups["content"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string indent = "\t\t\t";

        string rewritten = GeneLineRegex.Replace(content.Value, m =>
        {
            string key = m.Groups["key"].Value;
            string ind = m.Groups["ind"].Value;
            seen.Add(key);
            if (ind.Length > 0) indent = ind;

            if (eth.ColorGenes.TryGetValue(key, out var palettes) && palettes.Count > 0)
            {
                var p = PickWeighted(palettes, e => e.Weight, rng);
                int x = Byte255(rng.Float(p.X1, p.X2));
                int y = Byte255(rng.Float(p.Y1, p.Y2));
                return $"{ind}{key}={{ {x} {y} {x} {y} }}";
            }

            if (eth.MorphGenes.TryGetValue(key, out var entries) && entries.Count > 0)
            {
                var e = PickWeighted(entries, x => x.Weight, rng);
                int v = Byte255(rng.Float(e.Min, e.Max));
                return $"{ind}{key}={{ \"{e.SubGeneName}\" {v} \"{e.SubGeneName}\" {v} }}";
            }

            return m.Value;
        });

        // Genes the ethnicity defines that the borrowed template has never heard of have to be
        // appended rather than substituted. `gen_race_skin` is the case that matters: it is our own
        // gene, so no vanilla DNA record mentions it, and without this a bookmark drow would keep a
        // human hue no matter how the ethnicity was written.
        var added = new StringBuilder();
        foreach (var (key, entries) in eth.MorphGenes)
        {
            if (seen.Contains(key) || entries.Count == 0) continue;
            seen.Add(key);
            var e = PickWeighted(entries, x => x.Weight, rng);
            int v = Byte255(rng.Float(e.Min, e.Max));
            added.Append($"\n{indent}{key}={{ \"{e.SubGeneName}\" {v} \"{e.SubGeneName}\" {v} }}");
        }

        // A persistent DNA record must mention EVERY registered gene — the engine logs "Persistent
        // portrait info missing gene X" per record per missing gene otherwise (portraitcontext.cpp).
        // Human ethnicities deliberately define no gen_race_skin, so their records need the empty
        // index-0 template written out explicitly.
        if (!seen.Contains("gen_race_skin"))
            added.Append($"\n{indent}gen_race_skin={{ \"gen_skin_human\" 0 \"gen_skin_human\" 0 }}");

        return string.Concat(
            body.AsSpan(0, content.Index),
            rewritten,
            added.ToString(),
            body.AsSpan(content.Index + content.Length));
    }

    /// <summary>A DNA gene value, which the format stores as a byte rather than a 0..1 float.</summary>
    private static int Byte255(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static T PickWeighted<T>(List<T> items, Func<T, int> weight, Rng rng)
    {
        int total = 0;
        foreach (var i in items) total += Math.Max(0, weight(i));
        if (total <= 0) return items[rng.Int(0, items.Count - 1)];

        int roll = rng.Int(0, total - 1);
        foreach (var i in items)
        {
            roll -= Math.Max(0, weight(i));
            if (roll < 0) return i;
        }
        return items[^1];
    }

    /// <summary>
    /// One borrowed vanilla portrait, filed under the clothing region and rank it is actually
    /// wearing rather than under a guess made from its filename.
    /// </summary>
    private record Template(string Path, string Region, string Rank, string Beards);

    private record TemplatePool(Dictionary<string, List<Template>> ByRegion, List<Template> AllTemplates);

    /// <summary>The accessory value inside a record — <c>clothes={ "mena_high_nobility_clothes" … }</c>.</summary>
    private static readonly Regex ClothesGeneRegex =
        new(@"\bclothes=\{\s*""([a-z0-9_]+)""", RegexOptions.IgnoreCase);

    private static readonly Regex BeardsGeneRegex =
        new(@"\bbeards=\{\s*""([a-z0-9_]+)""", RegexOptions.IgnoreCase);

    /// <summary>
    /// Beard sets reserved for named historical characters. Borrowing one is the same mistake as
    /// borrowing Ya'qub's face mask: it is that man's beard, not a regional style.
    /// </summary>
    private static bool IsBorrowableBeard(string beard) =>
        beard.Length > 0
        && beard != "no_beard"
        && !beard.StartsWith("scripted_character", StringComparison.Ordinal);

    /// <summary>
    /// The region a beard style belongs to, or empty when it belongs to none.
    ///
    /// The wardrobe names beards the same way it names clothes — <c>mena_beards_curly</c>,
    /// <c>northern_beards_straight</c>, <c>tgp_chinese_beards</c> — but it also ships styles with no
    /// region in them at all (<c>all_beards</c>, <c>thin_beards_straight</c>, <c>ep2_beards</c>,
    /// <c>orthodox_beards</c>). Those are the safe fallback for a culture whose own region has no
    /// beard of its own: vanilla ships no indian or afr beard set, so a generic one is the honest
    /// answer rather than a beard from the wrong continent.
    /// </summary>
    private static string BeardRegion(string beard)
    {
        int cut = beard.IndexOf("beards", StringComparison.Ordinal);
        if (cut <= 0) return "";

        string token = beard[..cut].TrimEnd('_');
        return KnownRegions.Contains(token) ? token : "";
    }

    /// <summary>Rank words a clothes accessory name can end in, longest first so the greedy ones win.</summary>
    private static readonly string[] RankWords =
    [
        "high_nobility", "low_nobility", "war_nobility", "nobility", "royalty", "imperial",
        "ministers", "commoner", "common", "bedchamber", "children",
    ];

    /// <summary>
    /// What a culture's <c>clothing_gfx</c> means in terms of the clothing regions vanilla's portrait
    /// records actually wear, best first.
    ///
    /// The two vocabularies do not use the same words, which is the whole problem: a culture says
    /// <c>african_clothing_gfx</c> and the wardrobe says <c>afr_nobility_clothes</c>; a culture says
    /// <c>chinese_clothing_gfx</c> and the wardrobe says <c>tgp_chinese_…</c>. Matching is by prefix,
    /// so one entry covers a region's era and DLC variants (<c>ep2_western_era1</c>, <c>era2</c>, …).
    ///
    /// Read against the whole chain rather than one token: <c>clothing_gfx</c> is an ordered fallback
    /// chain (see the notes on it), so a narrow head like <c>east_slavic</c> that nothing here knows
    /// is answered by the broad <c>northern</c> behind it.
    /// </summary>
    private static readonly (string Gfx, string[] Regions)[] ClothingRegions =
    [
        ("dde_hre", ["dde_hre", "ep2_western", "western"]),
        ("dde_abbasid", ["dde_abbasid", "mena"]),
        ("fp1_norse", ["fp1", "northern"]),
        ("iberian_christian", ["fp2_christian", "western"]),
        ("iberian_muslim", ["fp2_muslim", "mena"]),
        ("afr_berber", ["afr", "mena"]),
        ("african", ["afr", "sub_saharan"]),
        ("sub_saharan", ["sub_saharan", "afr"]),
        ("byzantine", ["ep3_byzantine", "byzantine"]),
        ("iranian", ["fp3_iranian", "mena"]),
        ("mena", ["mena", "dde_abbasid", "fp2_muslim"]),
        ("indian", ["indian"]),
        ("southeast_asian", ["tgp_southeast"]),
        ("malay", ["tgp_southeast"]),
        ("viet", ["tgp_southeast"]),
        ("tai", ["tgp_southeast"]),
        ("papuan", ["tgp_southeast"]),
        ("japanese", ["tgp_japanese"]),
        ("korean", ["tgp_chinese", "tgp_japanese"]),
        ("chinese", ["tgp_chinese"]),
        ("dali", ["tgp_chinese"]),
        ("tangut", ["tgp_chinese", "steppe"]),
        ("khitan", ["steppe", "tgp_chinese"]),
        ("jurchen", ["steppe", "tgp_chinese"]),
        ("uyghur", ["steppe", "mpo_mongol"]),
        ("mongol", ["mpo_mongol", "steppe", "ep2_steppe"]),
        ("turkic", ["steppe", "mpo_mongol"]),
        ("northern", ["northern", "fp1"]),
        ("sami", ["northern"]),
        ("ugro_permian", ["northern"]),
        ("nivkh", ["northern"]),
        ("ainu", ["northern"]),
        ("emishi", ["northern"]),
        ("west_slavic", ["pol", "western", "northern"]),

        // Northern before pol: vanilla's own east slavic cultures write
        // `{ east_slavic_clothing_gfx northern_clothing_gfx }`, so northern is the fallback the game
        // itself nominates. Polish dress is a neighbour's, not their designated second choice.
        ("east_slavic", ["northern", "pol", "western"]),
        ("pommeranian", ["pol", "western"]),
        ("western", ["western", "ep2_western", "fp4_western", "sp3_western", "dde_hre"]),
    ];

    /// <summary>
    /// Every region name the table above knows, for testing a beard style against a culture.
    /// Declared after <see cref="ClothingRegions"/> and not before it: static initialisers run in
    /// declaration order, and reading the array from above it gets a null.
    /// </summary>
    private static readonly HashSet<string> KnownRegions =
        ClothingRegions.SelectMany(e => e.Regions).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Which rank of dress suits a title tier, best first. Vanilla dresses most cultures' dukes and
    /// above out of the same high-nobility wardrobe, so the lists overlap deliberately.
    /// </summary>
    private static string[] RanksFor(string tier) => tier switch
    {
        "e" => ["imperial", "royalty", "high_nobility", "nobility", "war_nobility"],
        "k" => ["royalty", "high_nobility", "nobility", "war_nobility"],
        "d" => ["high_nobility", "nobility", "war_nobility", "royalty"],
        _ => ["low_nobility", "nobility", "commoner", "common", "high_nobility"],
    };

    /// <summary>
    /// Picks a record whose wardrobe suits the character, rather than one whose filename mentions a
    /// vanilla ruler of roughly the right part of the world.
    ///
    /// This matters because the borrowed record is not just a face: <c>clothes</c>, <c>legwear</c>,
    /// <c>headgear</c>, <c>hairstyles</c> and <c>beards</c> are all genes, and a persistent DNA record
    /// pins them. Whatever the borrowed ruler was wearing is what the generated one wears, in the
    /// campaign as well as on the bookmark screen. The old filename buckets put an African-gfx ruler
    /// in MENA court dress because the African bucket was near enough empty and the fallback was the
    /// whole set, picked at random.
    ///
    /// Taking the whole record rather than rewriting the clothing genes is deliberate: a vanilla
    /// record's clothes, legwear, headgear and hair already agree with each other, and swapping one
    /// gene for a name from another region is how you get a MENA turban over Byzantine robes.
    /// </summary>
    private static Template PickMatchingTemplate(
        Culture culture, string tier, TemplatePool pool, Rng rng)
    {
        var chain = RegionsFor(culture).Concat(LastResortRegions).ToList();

        // Region first, but only where the region has something of the right station. A thin region
        // is worse than the next one along: `pol` ships two records, both commoners, and taking one
        // put a duke of an east slavic culture in a peasant's tunic when the northern wardrobe
        // behind it in his own chain had a high-nobility set waiting.
        foreach (string region in chain)
        {
            if (!pool.ByRegion.TryGetValue(region, out var inRegion)) continue;

            foreach (string rank in RanksFor(tier))
            {
                var fitted = inRegion.Where(t => t.Rank == rank).ToList();
                if (fitted.Count > 0) return rng.Pick(fitted);
            }
        }

        // Nothing in the whole chain dresses for his station; the right part of the world still beats
        // the right station, so take the best region at whatever rank it keeps.
        foreach (string region in chain)
        {
            if (pool.ByRegion.TryGetValue(region, out var inRegion) && inRegion.Count > 0)
                return rng.Pick(inRegion);
        }

        return rng.Pick(pool.AllTemplates);
    }

    /// <summary>
    /// The clothing regions this culture could believably wear, best first — its own gfx chain
    /// resolved through <see cref="ClothingRegions"/>, then western as the last resort.
    /// </summary>
    private static IEnumerable<string> RegionsFor(Culture culture)
    {
        // Line() hands back the value with its braces on — "{ east_slavic_clothing_gfx
        // northern_clothing_gfx }" — so the chain is read out token by token rather than compared
        // whole against anything.
        string chain = (culture.Heritage.Look.ClothingGfx ?? "").ToLowerInvariant();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(chain, @"([a-z0-9_]+)_clothing_gfx"))
        {
            string gfx = m.Groups[1].Value;

            foreach (var (key, regions) in ClothingRegions)
            {
                if (gfx != key) continue;

                foreach (string region in regions)
                    if (seen.Add(region)) yield return region;

                break;
            }
        }

    }

    /// <summary>
    /// Where a portrait comes from when the culture's own chain yields nothing — kept apart from
    /// <see cref="RegionsFor"/> so that "we found a genuine regional match" and "we gave up and
    /// dressed him as a Frank" stay distinguishable. See <see cref="Wardrobe"/>.
    /// </summary>
    private static readonly string[] LastResortRegions = ["western", "ep2_western"];

    /// <summary>
    /// The accessory genes that make up an outfit. Verified sex-aware — every clothing template
    /// declares male, female, boy and girl variants — so these are safe to lift across sexes.
    /// </summary>
    private static readonly string[] WardrobeGenes = ["clothes", "legwear", "headgear"];

    /// <summary>
    /// Hair and facial hair. Kept apart from <see cref="WardrobeGenes"/> because these are *not*
    /// reliably sex-aware: <c>afr_hairstyles</c> and <c>all_beards</c> declare no female or girl
    /// variant at all, so lifting one onto a woman's record could leave her bald. Only ever taken
    /// from a donor of the same sex as the target.
    /// </summary>
    private static readonly string[] GroomingGenes = ["hairstyles", "beards"];

    /// <summary>
    /// The outfit a culture should be wearing, lifted off a record from the right region even when
    /// that record is of the wrong sex.
    ///
    /// Needed because vanilla's dumps are not evenly spread: 238 male records cover 33 clothing
    /// regions, but only 40 female ones cover 17, and there is no female record at all wearing
    /// indian, afr or steppe dress. Matching a wife to her own culture from the female pool alone is
    /// therefore impossible for most of this generator's worlds, and she came out a Frankish
    /// noblewoman married to an Indian king.
    ///
    /// Safe because a clothing accessory is not sex-specific: <c>indian_high_nobility_clothes</c>
    /// declares <c>male</c>, <c>female</c>, <c>boy = male</c> and <c>girl = female</c> variants, so
    /// naming it on a female record renders the female Indian noble dress. Only the three wardrobe
    /// genes are taken, and all three from one donor, so the outfit still agrees with itself —
    /// swapping a single gene is how you get a MENA turban over Byzantine robes.
    /// </summary>
    private static string Wardrobe(string body, Template face, CharacterPortraitRequest req,
        TemplatePool donors, Rng rng)
    {
        var wanted = RegionsFor(req.Culture).ToList();

        // The face already came from a region the culture would recognise; leave it be.
        if (wanted.Contains(face.Region, StringComparer.Ordinal)) return body;
        if (wanted.Count == 0) return body;

        var donor = BestInRegion(wanted, req.Tier, donors, rng);
        if (donor is null) return body;

        string donorText = File.ReadAllText(donor.Path);

        // Hair and beard only when the donor pool is the target's own sex — see GroomingGenes.
        var genes = req.Female || req.Child
            ? WardrobeGenes
            : [.. WardrobeGenes, .. GroomingGenes];

        foreach (string gene in genes) body = CopyGene(body, donorText, gene);

        return body;
    }

    /// <summary>The best record in the first of <paramref name="wanted"/> that has one.</summary>
    private static Template? BestInRegion(
        List<string> wanted, string tier, TemplatePool donors, Rng rng)
    {
        foreach (string region in wanted)
        {
            if (!donors.ByRegion.TryGetValue(region, out var inRegion) || inRegion.Count == 0) continue;

            foreach (string rank in RanksFor(tier))
            {
                var fitted = inRegion.Where(t => t.Rank == rank).ToList();
                if (fitted.Count > 0) return rng.Pick(fitted);
            }

            return rng.Pick(inRegion);
        }

        return null;
    }

    /// <summary>Replaces one gene line in <paramref name="body"/> with the donor's, indentation kept.</summary>
    private static string CopyGene(string body, string donorText, string gene)
    {
        var from = Regex.Match(donorText, $@"\b{gene}=\{{[^{{}}]*\}}");
        if (!from.Success) return body;

        var into = Regex.Match(body, $@"(?<ind>^[ \t]*)\b{gene}=\{{[^{{}}]*\}}", RegexOptions.Multiline);
        return into.Success
            ? body.Remove(into.Index, into.Length).Insert(into.Index, into.Groups["ind"].Value + from.Value)
            : body;
    }

    /// <summary>
    /// Gives an adult man facial hair in his own culture's style.
    ///
    /// Two things went wrong without this. Most borrowed records name <c>no_beard</c> — five of the
    /// eight bookmarked men on one seed were clean-shaven — and because the record pins the gene, none
    /// of the culture-and-faith beard modifiers that would normally grow one can reach him. The rest
    /// carried a beard from the wrong side of the world, or one of the
    /// <c>scripted_character_beards_*</c> sets that belongs to a named historical character.
    ///
    /// Left alone for women and children: the beard gene on a female record is inert, and a boy with
    /// a full beard is worse than a boy without one.
    /// </summary>
    private static string Grooming(string body, CharacterPortraitRequest req, TemplatePool men, Rng rng)
    {
        if (req.Female || req.Child) return body;

        var wanted = RegionsFor(req.Culture).ToList();
        var current = BeardsGeneRegex.Match(body);
        string have = current.Success ? current.Groups[1].Value.ToLowerInvariant() : "";

        bool grow = !IsBorrowableBeard(have);
        string haveRegion = BeardRegion(have);
        bool wrongRegion = haveRegion.Length > 0 && !wanted.Contains(haveRegion, StringComparer.Ordinal);

        if (!grow && !wrongRegion) return body;

        // Not every man wore one — but a man who already has a beard in the wrong style keeps a
        // beard, he just gets his own culture's. Only growing one is left to chance.
        if (grow && !rng.Chance(0.75)) return body;

        var styles = men.AllTemplates.Select(t => t.Beards).Where(IsBorrowableBeard).Distinct().ToList();

        // Walked in chain order, so an east slavic culture takes a northern beard rather than the
        // western one further down its own fallback list.
        var pool = new List<string>();
        foreach (string region in wanted)
        {
            pool = styles.Where(b => BeardRegion(b) == region).ToList();
            if (pool.Count > 0) break;
        }

        // No beard set for this part of the world — vanilla ships none for several — so a style with
        // no region in its name at all, which is the honest answer.
        if (pool.Count == 0) pool = styles.Where(b => BeardRegion(b).Length == 0).ToList();
        if (pool.Count == 0) return body;

        string value = $@"beards={{ ""{rng.Pick(pool)}"" {rng.Int(40, 220)} ""no_beard"" 0 }}";
        var into = Regex.Match(body, @"(?<ind>^[ \t]*)\bbeards=\{[^{}]*\}", RegexOptions.Multiline);

        return into.Success
            ? body.Remove(into.Index, into.Length).Insert(into.Index, into.Groups["ind"].Value + value)
            : body;
    }

    /// <summary>
    /// Paints on the congenital traits the bookmark screen cannot work out for itself.
    ///
    /// A bookmark character is declared with a name, a dynasty, a culture, a faith, a government and
    /// a title — and no traits. So <c>gfx/portraits/trait_portrait_modifiers</c>, which is what turns
    /// an albino character white in the campaign, has nothing to fire on: the screen does not know he
    /// is albino. The trait is real, it is in <c>history/characters</c>, and the player sees it a
    /// second later on the character sheet; only the portrait he clicked to get there disagreed.
    ///
    /// Applied exactly as vanilla's own modifier does it — <c>skin_color</c> and <c>hair_color</c>
    /// shifted by -1.0 on both axes, <c>eye_color</c> by -1.0 on x alone — which on a palette
    /// coordinate stored as a byte means clamping to the pale corner. The eyebrow thinning vanilla
    /// pairs with it is a morph shift and is left to the campaign, where the real modifier runs.
    ///
    /// Only colour-driven traits are handled. <c>dwarf</c>, <c>giant</c>, <c>hunchbacked</c> and
    /// <c>spindly</c> shift morph genes by relative amounts whose meaning depends on the template
    /// each borrowed record happens to carry, so they stay invisible on this one screen rather than
    /// being guessed at.
    /// </summary>
    private static string TraitLooks(string body, IReadOnlyList<string>? traits)
    {
        if (traits is null || !traits.Contains("albino")) return body;

        body = ShiftColor(body, "skin_color", paleY: true);
        body = ShiftColor(body, "hair_color", paleY: true);
        body = ShiftColor(body, "eye_color", paleY: false);
        return body;
    }

    /// <summary>
    /// Drives one colour gene's palette coordinates to the low end. A colour gene reads
    /// <c>hair_color={ x y x y }</c> — dominant pair then recessive — and both have to move or the
    /// character comes out half albino.
    ///
    /// Deliberately not anchored to the start of a line. The dump format puts the first gene on the
    /// same line as <c>genes={</c>, and <c>hair_color</c> is usually that first gene — a line-anchored
    /// pattern silently skipped exactly the one an albino most needs changed, leaving white skin
    /// under dark hair.
    /// </summary>
    private static string ShiftColor(string body, string gene, bool paleY)
    {
        return Regex.Replace(
            body,
            $@"\b{gene}=\{{\s*\d+\s+(?<y1>\d+)\s+\d+\s+(?<y2>\d+)\s*\}}",
            m => paleY
                ? $"{gene}={{ 0 0 0 0 }}"
                : $"{gene}={{ 0 {m.Groups["y1"].Value} 0 {m.Groups["y2"].Value} }}");
    }

    private static TemplatePool LoadCategorizedTemplates(string sourceDir, Regex typeRegex)
    {
        var byRegion = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
        var all = new List<Template>();

        if (!Directory.Exists(sourceDir)) return new TemplatePool(byRegion, all);

        foreach (string path in Directory.GetFiles(sourceDir, "*.txt").OrderBy(p => p))
        {
            string text = File.ReadAllText(path);
            if (!typeRegex.IsMatch(text)) continue;

            var (region, rank) = WardrobeOf(text);
            var beards = BeardsGeneRegex.Match(text);
            var template = new Template(path, region, rank,
                beards.Success ? beards.Groups[1].Value.ToLowerInvariant() : "");
            all.Add(template);

            if (region.Length == 0) continue;

            if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = [];
            list.Add(template);
        }

        // Filed under every prefix of itself, so a request for "western" finds "western_era1" and a
        // request for "ep3_byzantine" finds "ep3_byzantine_era2" without the table having to name
        // every era vanilla has shipped or will ship.
        foreach (var template in all.Where(t => t.Region.Length > 0).ToList())
        {
            foreach (var (_, regions) in ClothingRegions)
            {
                foreach (string region in regions)
                {
                    if (region == template.Region || !template.Region.StartsWith(region, StringComparison.Ordinal))
                        continue;

                    if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = [];
                    if (!list.Contains(template)) list.Add(template);
                }
            }
        }

        return new TemplatePool(byRegion, all);
    }

    /// <summary>
    /// What a record is wearing, read off its own <c>clothes</c> gene:
    /// <c>mena_high_nobility_clothes</c> is the mena region at high-nobility rank.
    /// </summary>
    private static (string Region, string Rank) WardrobeOf(string text)
    {
        var match = ClothesGeneRegex.Match(text);
        if (!match.Success) return ("", "");

        string name = match.Groups[1].Value.ToLowerInvariant();
        if (name.EndsWith("_clothes", StringComparison.Ordinal)) name = name[..^"_clothes".Length];

        foreach (string rank in RankWords)
        {
            if (name.EndsWith("_" + rank, StringComparison.Ordinal))
                return (name[..^(rank.Length + 1)], rank);
        }

        return (name, "");
    }
}