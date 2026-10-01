using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Gives the court chaplain of a generated religion that religion's own clergy word.
///
/// Vanilla names the chaplain through the custom localisation <c>GetActualBishopTitle</c>
/// (<c>common/customizable_localization/00_divinity_custom_loc.txt</c>), which the council position
/// asks for whenever the faith is of the pagan family or holds temporal theocracy. It is a chain of
/// vanilla religions by name, and a generated religion matches none of them. So it fell to the
/// last two branches: a faith with pagan hostility got Paganism's "Court Shaman" / "Shaman" /
/// "High Shaman", and anything else got "Suffragan Bishop" / "Bishop" / "Archbishop".
///
/// Every generated religion already declares its own priest and bishop words (coined when native
/// titles are on, English otherwise; see ReligionWriter and ReligionGlossary). This puts them at
/// the head of the chain, straight after vanilla's head-of-faith early-outs, which still apply:
/// the priest word for a court below duchy tier, the bishop word from duchy up, each in the
/// chaplain's gender. Same split as vanilla's Paganism branch, one word fewer.
///
/// Both custom localisations are patched, the name and its possessive, because each is its own
/// chain. The trigger listing the generated religions is emitted beside them, so the patch is the
/// same text in every world.
///
/// Not touched: faiths outside those two groups keep vanilla's plain "Court Chaplain", which the
/// council position picks before ever asking this chain.
/// </summary>
public static class ChaplainTitleWriter
{
    private const string Trigger = "gen_is_generated_religion_trigger";

    public static void WriteAll(string modDir, string gameDir, FaithMap declared)
    {
        if (declared.Religions.Count == 0)
        {
            Console.WriteLine("  chaplain titles: SKIPPED (no generated religions)");
            return;
        }

        var patch = VanillaPatch.Open(gameDir, "chaplain titles",
            "common", "customizable_localization", "00_divinity_custom_loc.txt");
        if (patch is null) return;

        patch.InsertAfter("GetActualBishopTitle head-of-faith early-out", Entries(""),
            "GetActualBishopTitle = {", "localization_key = religious_head_name\n\t}\n");
        patch.InsertAfter("GetActualBishopTitlePossessive head-of-faith early-out", Entries("_possessive"),
            "GetActualBishopTitlePossessive = {", "localization_key = religious_head_name_possessive\n\t}\n");

        // Only alongside the patch: the trigger and the keys mean nothing without it, and the patch
        // names both.
        if (!patch.Ship(modDir)) return;

        WriteTrigger(modDir, declared);
        WriteLocalisation(modDir);
        Console.WriteLine($"  chaplain titles: {declared.Religions.Count} generated religion(s) name their own chaplains");
    }

    /// <summary>
    /// The four branches, in first-match order. A vacant seat reads as male, as vanilla's own
    /// gender tests do (<c>cp:councillor_court_chaplain ?= { is_female = yes }</c> is false when
    /// nobody holds it).
    /// </summary>
    private static string Entries(string suffix) => $$"""

        	# Generated religions: the faith's own clergy words. See Emit/Culture/ChaplainTitleWriter.cs.
        	text = {
        		trigger = {
        			{{Trigger}} = yes
        			highest_held_title_tier >= tier_duchy
        			cp:councillor_court_chaplain ?= { is_female = yes }
        		}
        		localization_key = gen_chaplain_title_bishop_female{{suffix}}
        	}
        	text = {
        		trigger = {
        			{{Trigger}} = yes
        			highest_held_title_tier >= tier_duchy
        		}
        		localization_key = gen_chaplain_title_bishop{{suffix}}
        	}
        	text = {
        		trigger = {
        			{{Trigger}} = yes
        			cp:councillor_court_chaplain ?= { is_female = yes }
        		}
        		localization_key = gen_chaplain_title_priest_female{{suffix}}
        	}
        	text = {
        		trigger = { {{Trigger}} = yes }
        		localization_key = gen_chaplain_title_priest{{suffix}}
        	}

        """.Replace("\r\n", "\n");

    private static void WriteTrigger(string modDir, FaithMap declared)
    {
        var b = new JominiBuilder();
        b.Comment("Whether the scoped character follows a religion this world generated.");
        b.Comment("Used by the chaplain titles; see Emit/Culture/ChaplainTitleWriter.cs.");
        b.Blank();

        using (b.Block(Trigger))
        using (b.Block("OR"))
            foreach (var religion in declared.Religions)
                b.Field("religion", $"religion:{religion.Key}");

        ParadoxText.WriteBom(
            Path.Combine(modDir, "common", "scripted_triggers", "zz_gen_chaplain_title_triggers.txt"), b.ToString());
    }

    private static void WriteLocalisation(string modDir)
    {
        const string text = """
            l_english:
             # The court chaplain of a generated religion; see Emit/Culture/ChaplainTitleWriter.cs.
             gen_chaplain_title_priest:0 "[ROOT.Char.GetFaith.PriestMale]"
             gen_chaplain_title_priest_female:0 "[ROOT.Char.GetFaith.PriestFemale]"
             gen_chaplain_title_bishop:0 "[ROOT.Char.GetFaith.BishopMale]"
             gen_chaplain_title_bishop_female:0 "[ROOT.Char.GetFaith.BishopFemale]"
             gen_chaplain_title_priest_possessive:0 "[ROOT.Char.GetFaith.PriestMale]'s"
             gen_chaplain_title_priest_female_possessive:0 "[ROOT.Char.GetFaith.PriestFemale]'s"
             gen_chaplain_title_bishop_possessive:0 "[ROOT.Char.GetFaith.BishopMale]'s"
             gen_chaplain_title_bishop_female_possessive:0 "[ROOT.Char.GetFaith.BishopFemale]'s"

            """;

        ParadoxText.WriteBom(
            Path.Combine(modDir, "localization", "english", "gen_chaplain_titles_l_english.yml"),
            text.Replace("\r\n", "\n"));
    }
}
