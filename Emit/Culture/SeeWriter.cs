using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// The declarations generated sees need beyond the religion and history files (MapGen/Peoples/Sees.cs):
/// <list type="bullet">
/// <item>the landless <c>d_et_gen_N</c> titles, shaped like vanilla's <c>d_et_*</c>
///   (07_pam_ecclesiastical_titles.txt);</item>
/// <item>their geographical regions, <c>et_gen_N_region = { counties = { … } }</c>, which the title
///   history binds with <c>clerical_region</c>;</item>
/// <item>the flavorization that styles the sees and their holders, and its localisation;</item>
/// <item>rite-name rules so a rite the engine founds later in a generated religion is named as the
///   generated ones are, by its founder's seat or its founder's name, rather than by vanilla's generic
///   "[Culture] [Faith]" fallback.</item>
/// </list>
/// Called from <see cref="ReligionWriter.WriteAll"/>, so every path that writes the religion files —
/// the generator and the editor's overwrite — writes these beside them. Every file is removed when
/// no faith has sees, so a re-emit never leaves a stale one behind.
/// </summary>
public static class SeeWriter
{
    private const string TitlesFile = "01_generated_sees.txt";
    private const string RegionsFile = "zz_gen_clerical_regions.txt";
    private const string FlavorFile = "zz_gen_sees.txt";
    private const string RiteNamesFile = "00_gen_rite_names.txt";   // before vanilla's 00_rite_names.txt: first match wins
    private const string LocFileName = "gen_sees_l_english.yml";

    /// <summary>Above vanilla's clerical-region entries (27-110) and below nothing of ours they could meet.</summary>
    private const int TitlePriority = 200;
    private const int HolderPriority = 200;

    /// <param name="removeWhenNone">
    /// Delete the files when no faith has sees. False from the editor's overwrite: a world loaded back
    /// from its files does not rebuild its sees yet (LoadedWorld reads no clerical regions), and the
    /// see history the generator wrote would otherwise be left naming titles and regions that no
    /// longer exist.
    /// </param>
    public static void WriteAll(string modDir, FaithMap faiths, bool removeWhenNone = true)
    {
        string titlesPath = Path.Combine(modDir, "common", "landed_titles", TitlesFile);
        string regionsPath = Path.Combine(modDir, "map_data", "geographical_regions", RegionsFile);
        string flavorPath = Path.Combine(modDir, "common", "flavorization", FlavorFile);
        string riteNamesPath = Path.Combine(modDir, "common", "religion", "rite_names", RiteNamesFile);
        string locPath = Path.Combine(modDir, "localization", "english", LocFileName);
        string electorsPath = Path.Combine(modDir, "common", "on_action", ElectorsFile);

        var sees = faiths.Faiths.SelectMany(f => f.Sees).Concat(faiths.Faiths.SelectMany(f => f.EraSees)).ToList();
        if (sees.Count == 0)
        {
            if (!removeWhenNone) return;
            foreach (string path in new[] { titlesPath, regionsPath, flavorPath, riteNamesPath, locPath, electorsPath })
                if (File.Exists(path)) File.Delete(path);
            return;
        }

        var loc = new LocFile();
        WriteTitles(titlesPath, sees, loc);
        WriteRegions(regionsPath, sees);
        WriteFlavor(flavorPath, faiths, loc);
        WriteRiteNames(riteNamesPath);
        WriteElectorLaw(electorsPath, faiths);
        Directory.CreateDirectory(Path.GetDirectoryName(locPath)!);
        loc.Write(locPath);
    }

    private static void WriteTitles(string path, List<See> sees, LocFile loc)
    {
        var b = new JominiBuilder(JominiStyle.Spaced);
        b.Comment("Generated sees: landless clerical-region titles, shaped like vanilla's d_et_* (07_pam_ecclesiastical_titles.txt).");
        b.Blank();

        foreach (var see in sees)
        {
            var (r, g, bl) = see.Seat.Color;
            using (b.Block(see.Key))
            {
                b.Inline("color", r.ToString(), g.ToString(), bl.ToString());
                b.Field("capital", see.Seat.Key);
                b.Blank();
                b.Field("landless", "yes");
                b.Field("always_follows_primary_heir", "yes");
                b.Field("no_automatic_claims", "yes");
                b.Blank();
                // Vanilla's @never_primary_score: an archbishop's see is never what the AI plays for.
                using (b.Block("ai_primary_priority")) b.Field("add", "-1000");
            }
            b.Blank();

            // Named for the seat, as vanilla's are (d_et_milano: "Milano"); the rank word is flavorization's.
            loc.Add(see.Key, see.Seat.Name);
            loc.Add($"{see.Key}_adj", see.Seat.Name);

            // The see's Synod Seat, when its faith elects: shaped like vanilla's cardinalates
            // (07_pam_ecclesiastical_titles.txt d_cd_*), but never destroyed on succession, since it
            // passes with the see (zz_gen_see_electors_on_actions.txt) rather than by appointment.
            if (!see.Faith.HasElectors) continue;
            using (b.Block(see.SynodSeatKey))
            {
                b.Inline("color", r.ToString(), g.ToString(), bl.ToString());
                b.Field("capital", see.Seat.Key);
                b.Blank();
                b.Field("landless", "yes");
                b.Field("allow_domicile", "no");
                b.Field("no_automatic_claims", "yes");
                b.Blank();
                using (b.Block("ai_primary_priority")) b.Field("add", "-1000");
            }
            b.Blank();

            loc.Add(see.SynodSeatKey, see.Seat.Name);
            loc.Add($"{see.SynodSeatKey}_adj", see.Seat.Name);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    private const string ElectorsFile = "zz_gen_see_electors_start_on_actions.txt";

    /// <summary>
    /// Vanilla's theocratic_elective law on the holder of each electing faith's head-of-faith title at
    /// game start, as game_start.txt gives it to the Pope; the on_title_gain re-grant and the rest of
    /// the election are static (BaseFilesToCopy/Core zz_gen_see_electors_on_actions.txt). Named titles,
    /// because script has no iterator over every faith. Removed when no faith elects.
    /// </summary>
    private static void WriteElectorLaw(string path, FaithMap faiths)
    {
        var heads = faiths.Faiths.Where(f => f.HasElectors && f.Head is { Temporal: false }).Select(f => f.Head!.TitleKey).ToList();
        if (heads.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var b = new JominiBuilder();
        b.Comment("The election law for the heads of faith whose archbishops elect them. See Emit/Culture/SeeWriter.cs.");
        b.Blank();
        using (b.Block("on_game_start_after_lobby"))
            b.Inline("on_actions", "gen_see_electors_game_start");
        b.Blank();
        using (b.Block("gen_see_electors_game_start"))
        using (b.Block("effect"))
            // Token, not Block: a block key is written with " = ", which would make "?= =".
            foreach (string head in heads)
                b.Token($"title:{head}.holder ?= {{ gen_see_electors_grant_law_effect = yes }}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    private static void WriteRegions(string path, List<See> sees)
    {
        var b = new JominiBuilder();
        b.Comment("Generated clerical regions, bound to their sees by clerical_region in history/titles/01_generated_sees.txt.");
        b.Blank();

        // The start date's region, then one per other bookmark the see stands on, as vanilla keeps
        // et_867_* and et_1066_* side by side.
        foreach (var see in sees)
        {
            if (see.Counties.Count > 0)
            {
                using (b.Block(see.RegionKey))
                    b.Inline("counties", [.. see.Counties.Select(c => c.Key)]);
                b.Blank();
            }

            foreach (var (year, counties) in see.Eras.OrderBy(kv => kv.Key))
            {
                using (b.Block(see.RegionKeyAt(year)))
                    b.Inline("counties", [.. counties.Select(c => c.Key)]);
                b.Blank();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    /// <summary>
    /// Per religion: the see, great see and primacy as rank words, and one holder style (flavorization
    /// cannot tell one clerical title from another for a character). In the religion's tongue when native
    /// rank titles coined its words, the holder then taking the religion's own "bishop" word; English otherwise.
    /// </summary>
    private static void WriteFlavor(string path, FaithMap faiths, LocFile loc)
    {
        var b = new JominiBuilder();
        b.Comment("Generated sees: rank words for clerical-region titles and their holders, per generated religion.");
        b.Blank();

        foreach (var religion in faiths.Religions.Where(r => r.HasSees))
        {
            var sees = religion.Faiths.SelectMany(f => f.AllSees).ToList();
            var words = religion.SeeWords;
            string stem = $"gen_see_{religion.Key}";

            Title($"{stem}_see", TitlePriority, null, words?.See ?? "See");

            var great = sees.Where(s => s.Rank == SeeRank.Great).Select(s => s.Key).ToList();
            if (great.Count > 0) Title($"{stem}_great", TitlePriority + 1, great, words?.GreatSee ?? "Great See");

            var primate = sees.Where(s => s.Rank == SeeRank.Primate).Select(s => s.Key).ToList();
            if (primate.Count > 0) Title($"{stem}_primacy", TitlePriority + 2, primate, words?.Primacy ?? "Primacy");

            string? bishop = words is null ? null : LocKey(religion, "BishopMale");
            string? bishopF = words is null ? null : LocKey(religion, "BishopFemale") ?? bishop;
            Holder($"{stem}_holder_male", "male", bishop is null ? "Hierarch" : $"${bishop}$");
            Holder($"{stem}_holder_female", "female", bishopF is null ? "Hierarch" : $"${bishopF}$");

            void Title(string key, int priority, List<string>? titles, string text)
            {
                using (b.Block(key))
                {
                    b.Field("type", "title");
                    b.Field("tier", "duchy");
                    b.Field("priority", priority);
                    b.Inline("governments", "ecclesiastical_government");
                    b.Inline("religions", religion.Key);
                    if (titles is not null) b.Inline("titles", [.. titles]);
                    using (b.Block("flavourization_rules"))
                    {
                        b.Field("target_character", "yes");
                        b.Field("top_liege", "no");
                        b.Field("only_holder", "yes");
                    }
                    b.Field("special_title", "clerical_region");
                }
                b.Blank();

                // A coined word ("Haligre") is glossed by vanilla's archdiocese concept, as the
                // native rank words are by theirs: the word as written, the concept's header and
                // text on hover. Procedural worlds relabel that concept "See" in neutral wording
                // (HierarchyFlavourWriter), so the gloss reads See there. English needs no gloss.
                // A word with an apostrophe ("Groo'ubuq") would end the quoted argument, so it
                // goes in its own key and the link reads it back with Localize, vanilla's pattern
                // (Concept('artifact_claim', Localize('game_concept_claimants'))), as
                // ConceptTooltips does for the rank and religion words.
                if (words is null)
                    loc.Add(key, text);
                else if (text.Contains('\''))
                {
                    loc.Add($"{key}_word", text);
                    loc.AddBuilt(key, $"[Concept('archdiocese',Localize('{key}_word'))|E]");
                }
                else
                    loc.AddBuilt(key, $"[Concept('archdiocese','{text}')|E]");
            }

            void Holder(string key, string gender, string text)
            {
                using (b.Block(key))
                {
                    b.Field("type", "character");
                    b.Field("gender", gender);
                    b.Field("special", "clerical_region_holder");
                    b.Field("tier", "duchy");
                    b.Field("priority", HolderPriority);
                    b.Inline("governments", "theocracy_government", "ecclesiastical_government");
                    b.Inline("religions", religion.Key);
                    using (b.Block("flavourization_rules"))
                    {
                        b.Field("top_liege", "no");
                        b.Field("spouse_takes_title", "no");
                    }
                }
                b.Blank();
                if (text.StartsWith('$')) loc.AddBuilt(key, text);
                else loc.Add(key, text);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    /// <summary>The loc key a religion's localization tag points at, when it has one.</summary>
    private static string? LocKey(Religion religion, string tag)
        => religion.Localization.FirstOrDefault(t => t.Tag == tag).Value is { Length: > 0 } key
           && religion.LocalizationText.ContainsKey(key) ? key : null;

    /// <summary>
    /// Rules for rites the engine founds in play inside a generated religion, tried before vanilla's
    /// (this file sorts first, and the first unused name whose trigger passes wins). Every generated
    /// religion carries gen_settled_doctrine, which is how they are told apart from vanilla's. Both
    /// reuse vanilla's own localisation: the founder's seat ("Kelian Varnism") or the founder's name
    /// ("Kelianism"), the two styles the generated rites use.
    /// </summary>
    private static void WriteRiteNames(string path)
    {
        string settled = MapGen.Faiths.SettledDoctrine;
        var b = new JominiBuilder();
        b.Comment("Rite names for rites founded in play inside generated religions. See Emit/Culture/SeeWriter.cs.");
        b.Blank();

        using (b.Block("gen_founder_name_rite"))
        {
            b.Field("name", "d_landless_founder_christian_rite_ian");
            using (b.Block("trigger"))
            {
                using (b.Block("scope:faith")) b.Field("has_doctrine", settled);
                b.Field("exists", "scope:founder");
                using (b.Block("scope:founder"))
                using (b.Block("static_group_filter"))
                {
                    b.Field("group", "culture_name_general_chance");
                    b.Field("match", "0.5");
                }
            }
        }
        b.Blank();

        using (b.Block("gen_placename_rite"))
        {
            b.Field("name", "d_placename_and_faith_rite");
            using (b.Block("trigger"))
            {
                using (b.Block("scope:faith")) b.Field("has_doctrine", settled);
                b.Field("exists", "scope:founder");
                using (b.Block("scope:founder")) b.Field("is_landed", "yes");
            }
        }
        b.Blank();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }
}
