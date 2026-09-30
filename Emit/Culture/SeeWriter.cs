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

        var sees = faiths.Faiths.SelectMany(f => f.Sees).ToList();
        if (sees.Count == 0)
        {
            if (!removeWhenNone) return;
            foreach (string path in new[] { titlesPath, regionsPath, flavorPath, riteNamesPath, locPath })
                if (File.Exists(path)) File.Delete(path);
            return;
        }

        var loc = new LocFile();
        WriteTitles(titlesPath, sees, loc);
        WriteRegions(regionsPath, sees);
        WriteFlavor(flavorPath, faiths, loc);
        WriteRiteNames(riteNamesPath);
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
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    private static void WriteRegions(string path, List<See> sees)
    {
        var b = new JominiBuilder();
        b.Comment("Generated clerical regions, bound to their sees by clerical_region in history/titles/01_generated_sees.txt.");
        b.Blank();

        foreach (var see in sees)
        {
            using (b.Block(see.RegionKey))
                b.Inline("counties", [.. see.Counties.Select(c => c.Key)]);
            b.Blank();
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
            var sees = religion.Faiths.SelectMany(f => f.Sees).ToList();
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
                loc.Add(key, text);
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
