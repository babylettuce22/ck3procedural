using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Starts All Under Heaven's Dynastic Cycle on the generated hegemony.
///
/// <code>
/// Related base files:
///   BaseFilesToCopy/Core/localization/english/zz_gen_dynastic_cycle_l_english.yml
///       vanilla's "China" strings, re-worded onto the title's own name
///   BaseFilesToCopy/Core/common/scripted_triggers/zz_gen_admin_conversion_triggers.txt
///       who may adopt the celestial government that goes with it
/// </code>
///
/// The situation is bound to <c>title:h_china</c> by name, ~220 times, and the generated hegemony
/// is written under that key (<see cref="Titles.HegemonyKey"/>) precisely so that all of it
/// resolves. What is left to do is start it, and vanilla does that from an oddly-placed history
/// file — <c>history/struggles/tgp_dynastic_cycle_history.txt</c>, gated on nothing but the DLC —
/// which the mod blanks with the rest of that folder. This is its replacement, under a name of
/// our own, the way SilkRoadWriter starts the Silk Road.
///
/// Vanilla's three hundred lines of <c>record_situation_special_event</c> are the Qin-to-Song
/// timeline, which this world did not have, and are not copied. Its dated <c>change_phase</c>
/// calls are what set the era per bookmark, and one of them is: 1066 opens Song China in
/// <c>situation_dynastic_cycle_phase_stability_advancement</c> because its emperor holds nearly
/// all of it, while 867 and 1178 open in instability with a dynasty already fading. A crowned
/// hegemon here is the Song case — <c>Realms.ExpandHegemonRealm</c> fills the border to nine
/// tenths — so a crowned start gets the 1066 line, verbatim in form, dated the day after the
/// situation starts. An unheld hegemony keeps the type's own <c>start_phase</c> (instability),
/// which is vanilla's 221 BC opening before the first emperor.
///
/// <c>MapConfig.DynasticCycle</c> off means this writer is not called at all (ContentWriter):
/// the situation is never started, and everything else on the hegemony — title, celestial
/// government, ministries — stands without it. See the setting's own doc for what that costs.
///
/// Instability is not a neutral opening. Its every future phase — expansion, advancement, and
/// instability's own chaos track — is <c>takeover_points = 1</c>, so the first year's drift
/// catalysts decide the era; a crowned hegemon that opened there was observed (2026-09-07,
/// 1050 start) with 142 of its sub-region phase records in chaos by 1067. The chaos phase in
/// particular is not a starting position: its <c>on_start</c> shatters the hegemon's realm.
///
/// The map footprint takes care of itself. The situation's <c>on_start</c> adds every de jure
/// county of the title to its one sub-region and its yearly pulse re-syncs to the same, so the
/// Dynastic Cycle covers exactly what the hegemony does — a contiguous group of empires, a
/// landmass where the map offers one (Titles.Crown), and never the whole world. It is a regional
/// cycle, as vanilla's is; it stopped being a world cycle when the crown stopped being the map.
///
/// Game start then finishes the job: vanilla's <c>on_game_start</c> block finds the situation
/// through <c>situation:dynastic_cycle ?=</c>, zeroes the examination clock on the title and
/// picks the ruling element. A hegemony nobody holds starts the cycle the way vanilla's own file
/// does at 221 BC, before the first recorded emperor: <c>title:h_china.holder</c> is null in the
/// situation's <c>on_start</c> and the ministry-participant loop does nothing.
///
/// <para><b>The land thresholds, when somebody is crowned.</b> Every year the cycle asks what
/// share of <c>h_china</c>'s de jure counties sit in the hegemon's realm
/// (<c>common/on_action/dynastic_cycle_on_actions.txt</c>): under
/// <c>catalyst_hegemony_too_few_lands_value</c> it is a steady push toward chaos, under
/// <c>catalyst_hegemony_far_too_few_lands_value</c> it is chaos on the spot — and the chaos phase's
/// first act is <c>tgp_chaos_shattering_effect</c>, which destroys the hegemony and the emperor's
/// empire and frees every vassal outside the emperor's remaining de jure. Vanilla's 50 % and 30 %
/// assume the Song holding nearly all of China, and a crowned hegemon here now starts in much the
/// same position — <c>Realms.ExpandHegemonRealm</c> fills the border to nine tenths. The wilderness
/// is what still separates the two: it counts in the denominator (the game does not know those
/// counties are empty) and can put the starting share under vanilla's warning line on its own. So
/// when the map starts with a hegemon, the two values are
/// rewritten in vanilla's own proportion to what the crown actually holds: half of the starting
/// share is the warning, three tenths of it is the loss. That keeps their meaning — "the hegemon
/// has lost most of what a hegemon should hold" — while making the yardstick this map's. Unheld,
/// the base file's constants stand (<c>BaseFilesToCopy/Core/common/script_values/</c>, same
/// name; StaticFileWriter never overwrites a generated file, so writing the same path is how
/// the crowned case takes precedence).</para>
/// </summary>
public static class DynasticCycleWriter
{
    /// <summary>Vanilla's thresholds as fractions of a full hold, kept as the proportion here.</summary>
    private const double TooFewOfShare = 0.5, FarTooFewOfShare = 0.3;

    public static void WriteAll(string modDir, List<Title> empires, double? hegemonShare = null)
    {
        if (Titles.HegemonyOf(empires) is not { } hegemony) return;

        if (hegemonShare is { } share) WriteLandThresholds(modDir, hegemony, share);

        var b = new JominiBuilder();
        b.Comment($"Starts the Dynastic Cycle on {hegemony.Key} (\"{hegemony.Name}\"); see Emit/DynasticCycleWriter.cs.");
        b.Comment("Vanilla starts it from history/struggles/tgp_dynastic_cycle_history.txt, which this mod blanks.");
        b.Blank();

        using (b.Block("1.1.1"))
        using (b.Block("effect"))
        using (b.Block("if"))
        {
            using (b.Block("limit")) b.Field("has_tgp_dlc_trigger", "yes");
            using (b.Block("start_situation")) b.Field("type", "dynastic_cycle");
        }

        // A crowned hegemon opens in the stable era, the way vanilla's 1066.1.1 entry opens Song
        // China. Same grammar as that entry; dated the day after start_situation so the situation
        // and its top sub-region exist when it runs. Unheld, the type's start_phase stands.
        if (hegemonShare is not null)
        {
            b.Blank();
            b.Comment("Crowned at the start date, so the cycle opens in the stable era, as vanilla's 1066 does.");
            using (b.Block("1.1.2"))
            using (b.Block("effect"))
            using (b.Block("if"))
            {
                using (b.Block("limit")) b.Field("has_tgp_dlc_trigger", "yes");
                using (b.Block("situation:dynastic_cycle"))
                using (b.Block("situation_top_sub_region"))
                    b.Inline("change_phase", "phase = situation_dynastic_cycle_phase_stability_advancement");
            }
        }

        string dir = Path.Combine(modDir, "history", "struggles");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_dynastic_cycle.txt"), b.ToString());

        Console.WriteLine($"  dynastic cycle: started on {hegemony.Key} ({hegemony.Name})");
    }

    private static void WriteLandThresholds(string modDir, Title hegemony, double share)
    {
        // Floor of one percent: a share of zero would make both catalysts fire on any map, and a
        // hegemon who genuinely holds nothing has bigger problems than a script value.
        string Fraction(double f) => Math.Max(0.01, f).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        var b = new JominiBuilder();
        b.Comment($"The Dynastic Cycle's land thresholds, tuned to what the crowned hegemon of {hegemony.Key}");
        b.Comment($"(\"{hegemony.Name}\") holds at the start date: {Fraction(share)} of the hegemony's de jure counties.");
        b.Comment("Vanilla's 0.5 / 0.3 assume a hegemon holding nearly all of a compact de jure China; this one");
        b.Comment("starts holding nearly all of the settled part, with wilderness counties in the denominator");
        b.Comment("that no ruler will ever hold. Same proportions, this map's yardstick. See Emit/DynasticCycleWriter.cs.");
        b.Blank();
        b.Field("catalyst_hegemony_too_few_lands_value", Fraction(share * TooFewOfShare));
        b.Field("catalyst_hegemony_far_too_few_lands_value", Fraction(share * FarTooFewOfShare));

        string dir = Path.Combine(modDir, "common", "script_values");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_dynastic_cycle_values.txt"), b.ToString());

        Console.WriteLine($"  dynastic cycle: land thresholds {Fraction(share * TooFewOfShare)} / "
                        + $"{Fraction(share * FarTooFewOfShare)} for a hegemon holding {share:P0} of the "
                        + "hegemony's de jure counties");
    }
}
