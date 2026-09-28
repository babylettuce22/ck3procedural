using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps vanilla's real-world peoples and faiths from turning up in a procedural world.
///
/// The mod replaces vanilla's titles and history but not its cultures and faiths: those stay in
/// the database because thousands of vanilla lines name them. So an effect that writes
/// <c>culture:mongol</c> or <c>faith:vajrayana</c> as a literal still works on a generated map,
/// and reported from play (2026-09-27) three do it without asking anything about the map:
///
/// <list type="bullet">
/// <item><b>Herder migration.</b> Every Migrate fires <c>mpo_misc.0001</c>, which "leaves behind"
/// a heritage culture picked by vanilla region — Mongol in <c>world_steppe_east</c>, Kipchak in
/// <c>world_steppe_central</c> (both of which SteppeWriter fills with this map's steppe), and
/// Oghuz (<c>culture:turkish</c>) as the final <c>else</c>, i.e. anywhere at all. That is the
/// "base game cultures forming, mostly nomads" report.</item>
/// <item><b>Nomad flavour events.</b> <c>nomad_events.0140</c> (merchant) and <c>.0250</c>
/// (priest) look for a visitor in the pool, then — 0250 only — a neighbouring ruler's faith, and
/// failing that create one from a hard-coded list: Uyghur Vajrayana, Kerait Nestorian, Bolghar
/// Ashari and so on. 0250's first option converts the nomad, their domicile and every county they
/// hold to the visitor's faith. That is the "spawned Buddhism" report.</item>
/// <item><b>The Mongol invasion.</b> On the default rule it rolls yearly from 1180 to 1250, and
/// with no <c>character:125501</c> on this map it falls through to creating Temüjin and his court
/// from vanilla's Mongol templates. The Seljuk invasion (945–1066) is the same shape.</item>
/// </list>
///
/// Only for <see cref="MapConfig.ContentSourceMode.Procedural"/>: a VanillaWorld map is meant to
/// have these peoples, and on it all three behave as vanilla intends.
///
/// Not done here: a save that already has them keeps them. Nothing converts existing counties.
/// </summary>
public static class VanillaIdentityWriter
{
    public static void WriteAll(string modDir, string gameDir, MapConfig cfg)
    {
        if (cfg.ContentSource == MapConfig.ContentSourceMode.VanillaWorld)
        {
            Console.WriteLine("  vanilla identity guard: not needed (Content Source VanillaWorld)");
            return;
        }

        WriteMigrationHeritage(gameDir, modDir);
        WriteNomadVisitors(gameDir, modDir);
        WriteHistoricalInvasions(modDir);
    }

    /// <summary>
    /// Rewrites <c>leave_behind_heritage_culture_effect</c>, which every branch of
    /// <c>mpo_misc.0001</c>'s region chain calls, rather than the chain itself: one definition
    /// instead of ~30 region arms, and the arms can grow in a patch without reopening this.
    ///
    /// Vanilla's own order, minus the step that names a vanilla people. It prefers the culture the
    /// county had before the horde took it (<c>migration_previous_culture</c>, stamped whenever a
    /// nomad takes a county of another culture), then the horde's own — but only when either shares
    /// the region's vanilla heritage, and no generated culture ever does, so vanilla always reached
    /// its third step. Here the first two apply unconditionally. <c>$CULTURE$</c> is still read,
    /// because no vanilla call passes a parameter its effect does not use: it keeps a county whose
    /// "previous culture" is that same vanilla people (a save the bug already reached) from being
    /// handed back to it.
    ///
    /// The divergence rolls after the chain are left as they are; vanilla has them at chance 0.
    /// </summary>
    private static void WriteMigrationHeritage(string gameDir, string modDir)
    {
        var patch = VanillaPatch.Open(gameDir, "vanilla identity (migration)", "events", "mpo_misc.txt");
        if (patch is null) return;

        const string effect =
            "scripted_effect leave_behind_heritage_culture_effect = {\n"
            + "\t# Generated world: $CULTURE$ is a vanilla region's people, which this map does not have.\n"
            + "\t# The land goes back to whoever lived there before the horde, or keeps the horde's own.\n"
            + "\tif = {\n"
            + "\t\tlimit = {\n"
            + "\t\t\texists = scope:old_capital_county.var:migration_previous_culture\n"
            + "\t\t\tNOT = { scope:old_capital_county.var:migration_previous_culture = $CULTURE$ }\n"
            + "\t\t}\n"
            + "\t\tscope:old_capital_county.holder = {\n"
            + "\t\t\tset_culture = scope:old_capital_county.var:migration_previous_culture\n"
            + "\t\t}\n"
            + "\t}\n"
            + "\telse_if = {\n"
            + "\t\tlimit = { exists = scope:old_holder.domicile.domicile_culture }\n"
            + "\t\tscope:old_capital_county.holder = {\n"
            + "\t\t\tset_culture = scope:old_holder.domicile.domicile_culture\n"
            + "\t\t}\n"
            + "\t}\n"
            + "}";

        patch.ReplaceBlock("leave_behind_heritage_culture_effect",
            "scripted_effect leave_behind_heritage_culture_effect = {", effect);

        patch.Ship(modDir);
    }

    /// <summary>
    /// Lets the two visitor events fire only when the visitor can be someone from this world.
    ///
    /// A gate in <c>trigger</c> rather than a replacement for the fallback: the conditions are
    /// vanilla's own, copied from each event's <c>immediate</c>, so when the event fires it takes
    /// one of vanilla's non-hard-coded branches and the <c>else</c> that creates the Uyghur monk
    /// is never reached. 0140 has no neighbour branch, so it now needs a merchant already in the
    /// pool at the nomad's location. The events are rarer on a generated map; the alternative was
    /// inventing whom to send, and the neighbour branch vanilla already wrote is that answer.
    /// </summary>
    private static void WriteNomadVisitors(string gameDir, string modDir)
    {
        var patch = VanillaPatch.Open(gameDir, "vanilla identity (nomad visitors)",
            "events", "dlc", "mpo", "mpo_nomads_flavour_events.txt");
        if (patch is null) return;

        const string merchant =
            "\n\t\t# Generated world: only with a merchant already here. Vanilla's fallback creates one\n"
            + "\t\t# of a hard-coded vanilla faith and culture.\n"
            + "\t\tany_pool_character = {\n"
            + "\t\t\tprovince = root.location\n"
            + "\t\t\tnomad_events_0140_valid_merchant = yes\n"
            + "\t\t}\n";

        const string priest =
            "\n\t\t# Generated world: only when a priest can come from here or from a neighbour. Vanilla's\n"
            + "\t\t# fallback creates one of a hard-coded vanilla faith and culture, and option a converts to it.\n"
            + "\t\tOR = {\n"
            + "\t\t\tany_pool_character = {\n"
            + "\t\t\t\tprovince = root.location\n"
            + "\t\t\t\tnomad_events_0250_valid_priest = yes\n"
            + "\t\t\t}\n"
            + "\t\t\tany_neighboring_top_liege_realm_owner = {\n"
            + "\t\t\t\tNOR = {\n"
            + "\t\t\t\t\tfaith = root.faith\n"
            + "\t\t\t\t\tfaith = { has_doctrine_parameter = unreformed }\n"
            + "\t\t\t\t}\n"
            + "\t\t\t}\n"
            + "\t\t}\n";

        patch.InsertAfter("nomad_events.0140 trigger", merchant, "nomad_events.0140 = {", "trigger = {");
        patch.InsertAfter("nomad_events.0250 trigger", priest, "nomad_events.0250 = {", "trigger = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// Marks the Mongols and the Seljuks as already come, which is the flag each yearly roll
    /// (historical and random rule alike) tests before spawning.
    ///
    /// <list type="bullet">
    /// <item><c>mongols_have_appeared</c> — also tested by <c>ep3_roman_restoration.0520</c>,
    /// read nowhere else.</item>
    /// <item><c>seljuk_invasion_happened</c> — read nowhere else. The roll runs 945–1066, and its
    /// birth region <c>dlc_fp3_seljuk_birth_region</c> is a real county here (<c>c_lhasa</c> is
    /// one of the Silk Road's reused keys), so House Seljuk is created on it with a big army.
    /// Its faith is drawn from <c>world_steppe_west</c> with every unreformed faith weighted to
    /// zero, which on a pagan steppe leaves nothing to draw.</item>
    /// </list>
    ///
    /// The Almohads are left alone: they need <c>c_tinmallal</c> or <c>k_maghreb</c> to hold a
    /// county, find none, and log that they will not spawn.
    /// </summary>
    private static void WriteHistoricalInvasions(string modDir)
    {
        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("""
                  No Mongol or Seljuk invasion on a generated world: vanilla creates Temujin and
                  House Seljuk from nothing. Written by Emit/VanillaIdentityWriter.cs.
                  """);
        b.Blank();

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_no_historical_invasions");

        b.Blank();

        using (b.Block("gen_no_historical_invasions"))
        using (b.Block("effect"))
        {
            b.Inline("set_global_variable", "name = mongols_have_appeared", "value = yes");
            b.Inline("set_global_variable", "name = seljuk_invasion_happened", "value = yes");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_no_historical_invasions.txt"), b.ToString());

        Console.WriteLine("  vanilla identity guard: Mongol and Seljuk invasions off");
    }
}
