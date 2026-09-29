// Emit/HegemonyFlavourWriter.cs
namespace Ck3MapGen.Emit;

using System.IO;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

/// <summary>
/// Takes China's appearance off the generated hegemony while leaving China's machinery under it.
///
/// <code>
/// Related:
///   MapGen/Titles.cs           HegemonyKey — why the title is keyed h_china at all
///   Emit/DynasticCycleWriter   the situation that key exists to satisfy
///   Emit/CoatOfArmsWriter      WriteHegemonyArms, the shield this writer ships
/// </code>
///
/// ---- The bargain, and what it was quietly costing ----
///
/// The hegemony is written under vanilla's <c>h_china</c> key so that All Under Heaven's roughly
/// 220 hardcoded <c>title:h_china</c> references — the Dynastic Cycle, the nine ministries, the
/// Mandate casus belli, tribute missions — resolve to it for free. Its NAME, adjective, colour and
/// extent are all generated, and the loc keys are replaced, so in text the title is whatever this
/// world called it. In pictures it was still China.
///
/// Five things keyed off that title rather than off culture, and every one of them is art:
///
/// * <b>The arms.</b> Vanilla's h_china shield is the imperial dragon over black, yellow and red.
///   Handled by <see cref="CoatOfArmsWriter.WriteHegemonyArms"/>, not here.
/// * <b>Clothes.</b> <c>01_clothes_base.txt</c> weights Tang and Song imperial dragon robes at
///   120 for whoever holds the hegemony as their primary title, and for their spouse. Culture's
///   own high-noble clothes score 100-110, so the hegemon of a Norse or Malian world dressed as a
///   Chinese emperor.
/// * <b>Headgear.</b> Same shape in <c>01_headgear_base.txt</c>: the mianguan at 110.
/// * <b>The throne room.</b> The <c>chinese</c> court scene lists the hegemony as primary title
///   alongside its culture tests, and six other scenes carry a matching "and you are not the Son
///   of Heaven" guard.
/// * <b>Event illustrations.</b> <c>east_asian_governor_trigger</c> and its close-family sibling
///   route every administrative vassal under the hegemony into East Asian event art.
///
/// ---- Why the token is rewritten rather than the entries removed ----
///
/// Every one of these tests is a literal <c>primary_title ?= title:h_china</c> inside a weight or
/// a trigger. Rewriting that token to <c>always = no</c> makes the Chinese entry unreachable and
/// leaves everything around it — every other culture's weights, the whole rest of the file —
/// exactly as vanilla wrote it. It also gets the negative form right for free: a guard reading
/// <c>NOT = { primary_title ?= title:h_china }</c> becomes <c>NOT = { always = no }</c>, which is
/// true, which is what "there is no Son of Heaven here" should mean for the six court scenes that
/// ask.
///
/// Deleting the entries instead would need brace matching, and lifting only the entries we want
/// into a higher-priority portrait group would mean re-deriving, per graphical culture group and
/// per DLC era, what the hegemon should wear — nine thousand lines of judgement to reproduce an
/// answer vanilla already computes correctly the moment the Chinese entry stops outbidding it.
///
/// The copies are re-cut from the player's own installation on every generate, so they track
/// whatever CK3 version is installed rather than a version this repo was written against. That is
/// the same reason they are safe: a patch that renames the token makes <c>ReplaceEvery</c> miss,
/// and <see cref="VanillaPatch"/> then ships nothing rather than shipping vanilla under our name.
///
/// ---- What is deliberately left Chinese ----
///
/// The <c>idle_emperor</c> and <c>frontEnd_emperor</c> portrait animations, which pose the hegemon
/// holding a hu tablet. It reads as imperial rather than as specifically Chinese, and it is the
/// only thing in the set that flatters the title. Left by choice, not oversight.
///
/// Two more are out of reach of any of this because they never touched the title key:
/// <c>heritage_chinese</c> gates <c>is_valid_celestial_dynasty</c> (who may claim the Mandate) and
/// <c>religion:confucianism_religion</c> gates the imperial examination pipeline.
/// </summary>
public static class HegemonyFlavourWriter
{
    /// <summary>
    /// One vanilla file, and the tests in it that name the hegemony.
    ///
    /// Ordered longest-first within each file. <c>primary_title = title:h_china</c> is a substring
    /// of <c>primary_spouse.primary_title = title:h_china</c>, and rewriting the short form first
    /// would leave <c>primary_spouse.always = no</c> — script that parses, scopes into nothing, and
    /// leaves the spouse in imperial robes with no error anywhere to say so.
    /// </summary>
    private static readonly (string Label, string[] Path, string[] Tests)[] Targets =
    [
        ("hegemon clothes", ["gfx", "portraits", "portrait_modifiers", "01_clothes_base.txt"],
            ["primary_spouse.primary_title ?= title:h_china", "primary_title ?= title:h_china"]),

        ("hegemon headgear", ["gfx", "portraits", "portrait_modifiers", "01_headgear_base.txt"],
            ["primary_spouse.primary_title ?= title:h_china", "primary_title ?= title:h_china"]),

        ("hegemon court scene", ["gfx", "court_scene", "scene_cultures", "00_default_cultures.txt"],
            ["primary_title ?= title:h_china"]),

        // The two governor triggers keep their third arm, which asks whether the top liege's
        // culture has the East Asian heritage pillar. That one is correct as it stands: a
        // generated culture carries a generated heritage, so it is false here, and it would be
        // right to leave true on a map that did have such cultures.
        ("hegemon event art", ["common", "scripted_triggers", "00_illustration_triggers.txt"],
            ["primary_title.de_jure_liege = title:h_china", "primary_title = title:h_china"]),
    ];

    /// <summary>The test's replacement: false where it was asserted, true where it was denied.</summary>
    private const string Never = "always = no";

    public static void WriteAll(string modDir, string gameDir, List<Title> empires)
    {
        // No hegemony means h_china is one of CompatibilityWriter's landless shims, held by
        // nobody. Every test below is about who holds it as a primary title, so none of them can
        // fire and there is nothing to strip — and no arms to write, since an unheld titular is
        // never drawn.
        if (Titles.HegemonyOf(empires) is not { } hegemony)
        {
            Console.WriteLine("  hegemony flavour: SKIPPED (no hegemony on this map)");
            return;
        }

        CoatOfArmsWriter.WriteHegemonyArms(modDir, hegemony);
        WriteArmsAssignment(modDir, hegemony);

        foreach ((string label, string[] path, string[] tests) in Targets)
        {
            var patch = VanillaPatch.Open(gameDir, label, path);
            if (patch is null) continue;

            foreach (string test in tests) patch.ReplaceEvery(test, test, Never);

            patch.Ship(modDir);
        }
    }

    /// <summary>
    /// The load-order-proof half of the arms; see <see cref="CoatOfArmsWriter.WriteHegemonyArms"/>
    /// for why there are two halves.
    ///
    /// Appended to <c>on_game_start_after_lobby</c> rather than declared as a new key: an
    /// on_action redeclared adds to vanilla's list instead of replacing it. Written from here
    /// rather than shipped as a base file because the coat of arms key it names exists only when
    /// a hegemony does, and <c>set_coa</c> pointing at a key nothing defines is an error in the
    /// log on every launch.
    /// </summary>
    private static void WriteArmsAssignment(string modDir, Title hegemony)
    {
        var b = new JominiBuilder();
        b.Comment($"Puts the generated arms on {hegemony.Key} (\"{hegemony.Name}\") once the world is loaded,");
        b.Comment("so the shield holds whatever order the coat of arms folder is read in.");
        b.Comment("See Emit/HegemonyFlavourWriter.cs and Emit/CoatOfArmsWriter.cs.");
        b.Blank();

        using (b.Block("on_game_start_after_lobby"))
        using (b.Block("on_actions"))
            b.Token("gen_hegemony_arms");

        b.Blank();

        // Plain `title:x = { … }`, the form vanilla's own game_start.txt uses for the Norse
        // Scandinavia arms. The optional `?=` would buy nothing: this file is written only when
        // the hegemony exists, and the key resolves either way — held or as a vanilla shim.
        using (b.Block("gen_hegemony_arms"))
        using (b.Block("effect"))
        using (b.Block($"title:{hegemony.Key}"))
            b.Field("set_coa", CoatOfArmsWriter.HegemonyCoaKey);

        ParadoxText.WriteBom(
            Path.Combine(modDir, "common", "on_action", "zz_gen_hegemony_arms.txt"), b.ToString());
    }
}
