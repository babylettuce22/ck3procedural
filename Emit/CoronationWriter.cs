using System.Text.RegularExpressions;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// What the Coronations DLC needs from a world whose religions and realms it has never heard of.
///
/// The DLC itself needs no help to *run* here — its realm law, its activity and its oaths are all
/// world-agnostic, and nothing this generator writes overrides them. What it cannot do on its own is
/// answer three questions about a generated map, because vanilla answers them from hardcoded lists
/// of its own religions and provinces:
///
/// <list type="number">
/// <item>Does this faith crown its rulers, or hand them a sceptre?</item>
/// <item>Which places have crowned kings long enough for it to be a tradition?</item>
/// <item>Who already owns their realm's regalia on the start date?</item>
/// </list>
///
/// The shape is borrowed from A Game of Thrones, which solves the first with a two-line override of
/// the same triggers and the second with a game-start on_action gated on
/// <c>has_dlc_feature = coronations</c>. The difference is that AGOT is one culture-sphere and can
/// answer "crown" for the whole map; a generated world has to answer per religion, which is what
/// vanilla's own structure does and what <see cref="Religion.CoronationCrown"/> carries.
/// </summary>
public static class CoronationWriter
{
    /// <summary>
    /// County opinion is the reward, so these are the two rungs of vanilla's own 5/10/15 ladder
    /// worth starting a world on: an imperial seat has crowned rulers since before anyone counted,
    /// a kingdom's seat is merely where it is done.
    ///
    /// Values rather than modifiers because the activity re-derives the modifier from the variable
    /// every time a ceremony ends there — writing one without the other leaves a province that is
    /// ancient until its next coronation and then abruptly is not.
    /// </summary>
    private const int EmpireTradition = 20;
    private const int KingdomTradition = 5;

    public static void WriteAll(string modDir, string gameDir, List<Title> empires, FaithMap faiths)
    {
        WriteArtifactTriggers(modDir, gameDir, faiths);
        WriteGameStart(modDir, empires);
    }

    /// <summary>
    /// Re-declares CK3's crown and regalia triggers so they have an answer for generated religions.
    ///
    /// Vanilla's pair name twelve religions that crown and seven that use regalia, and send
    /// everything else to a <c>trigger_else</c> where both slots count. That sounds permissive and
    /// is the worst of the three answers: <c>coronation_being_crowned_trigger</c> — the ceremony
    /// where the officiator actually sets the crown on the ruler's head, event 6100 — is reachable
    /// only when the crown list says yes or the ruler can be anointed, so a whole scene of the
    /// activity was unreachable on every generated map.
    ///
    /// The vanilla lists are read back out of the game files rather than copied in here, so a
    /// religion Paradox adds later keeps its rite. Nothing on a generated map holds a vanilla
    /// religion, but the console and the odd converted save do, and the read costs one regex.
    /// </summary>
    private static void WriteArtifactTriggers(string modDir, string gameDir, FaithMap faiths)
    {
        string source = Path.Combine(
            gameDir, "common", "scripted_triggers", "10_ach_scripted_triggers.txt");

        // No DLC files, no triggers to re-declare — and declaring them from nothing would invent
        // content the rest of the install has never heard of.
        if (!File.Exists(source)) return;

        string text = File.ReadAllText(source);

        var crown = VanillaReligions(text, "coronation_proper_artifact_crown_trigger");
        var regalia = VanillaReligions(text, "coronation_proper_artifact_regalia_trigger");

        crown.AddRange(faiths.Religions.Where(r => r.CoronationCrown).Select(r => r.Key));
        regalia.AddRange(faiths.Religions.Where(r => !r.CoronationCrown).Select(r => r.Key));

        var b = new JominiBuilder();
        b.Comment("""
                  Which rite a religion uses to make a ruler: the crown, or the regalia.

                  These two triggers are vanilla's, re-declared. Vanilla names only its own
                  religions and sends anything else to a trigger_else where both slots count, which
                  reads as permissive and is not: the crowning scene of the coronation
                  (coronation_being_crowned_trigger, event 6100) is gated on the crown list saying
                  yes, so "either" means "never crowned by anyone".

                  Vanilla's own entries are preserved. The generated religions are appended, and
                  each one's answer here is the same one that chose the slot of the sovereign
                  artifact its kings start with.
                  """);
        b.Blank();

        WriteReligionTrigger(b, "coronation_proper_artifact_crown_trigger", crown);
        b.Blank();
        WriteReligionTrigger(b, "coronation_proper_artifact_regalia_trigger", regalia);

        string dir = Path.Combine(modDir, "common", "scripted_triggers");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_coronation_triggers.txt"), b.ToString());

        int crowned = faiths.Religions.Count(r => r.CoronationCrown);
        Console.WriteLine($"  coronations: {crowned} religions crown, " +
                          $"{faiths.Religions.Count - crowned} use regalia");
    }

    private static void WriteReligionTrigger(JominiBuilder b, string key, List<string> religions)
    {
        using (b.Block(key))
        {
            // An OR with nothing in it is not "no", it is a parse the engine has to guess at.
            // Only reachable if the game files stop listing religions and every generated one
            // rolled the other way, but the failure would be silent and permanent.
            if (religions.Count == 0) { b.Field("always", "no"); return; }

            using (b.Block("religion"))
            using (b.Block("OR"))
                foreach (string religion in religions) b.Field("this", $"religion:{religion}");
        }
    }

    /// <summary>The <c>religion:x</c> references inside one trigger, in the order they are written.</summary>
    private static List<string> VanillaReligions(string text, string key)
    {
        string? body = GovernmentWriter.Block(text, key);
        if (body is null) return [];

        return Regex.Matches(body, @"religion:([A-Za-z_0-9]+)")
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
    }

    /// <summary>
    /// Game start: where crowning rulers is already customary.
    ///
    /// Deliberately does <em>not</em> also hand a crown to any king who lacks one, which is what
    /// vanilla's suppressed <c>coronation_events.0302</c> did. There is no such king:
    /// <c>Artifacts.Build</c> gives every ruler of kingdom tier or above a target of two to four
    /// pieces — never zero — and forces the first of them to be
    /// <see cref="MapGen.ArtifactCategory.SovereignJewels"/> with the redraw and the give-up branch
    /// both skipped for it. A guard for that case would be unreachable, and it would not be free:
    /// asking "does this ruler own their regalia" from this file only answers correctly if
    /// 00_generated_artifacts_on_action.txt has already run, which holds because the two files sort
    /// that way and for no stronger reason. Getting that wrong would not lose a crown, it would
    /// mint a second one for every realm on the map — the precise failure being fixed here.
    ///
    /// The genuine loss of a crown mid-game is vanilla's problem and vanilla handles it: the AI
    /// forges a replacement from <c>yearly_playable_pulse</c>, and a player is offered one by
    /// <c>realm_maintenance_events</c>.
    /// </summary>
    private static void WriteGameStart(string modDir, List<Title> empires)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Coronation setup: traditional crowning places, and a regalia backstop.

                  Gated on the DLC rather than on script existence. Every identifier below ships in
                  the base game — the DLC only turns the system on — so an ungated version would
                  quietly hand out county opinion in a game with no coronations to earn it.
                  """);
        b.Blank();

        using (b.Block("on_game_start_after_lobby"))
        using (b.Block("on_actions"))
            b.Token("gen_coronation_setup");

        b.Blank();

        using (b.Block("gen_coronation_setup"))
        {
            using (b.Block("trigger")) b.Field("has_dlc_feature", "coronations");

            using (b.Block("effect"))
            {
                b.Comment("""
                          The seats of the realms that were already old when the map opens.

                          Vanilla seeds nine historical sites and AGOT eight, one per constituent
                          kingdom; the analogue here is one per empire, with every kingdom's seat a
                          rung lower. Both are the realm capital rather than a holy site, because a
                          holy site is where a faith gathers and a capital is where a crown is put
                          on — vanilla's own list is Reims and Aachen, not Rome.
                          """);

                foreach (var empire in empires)
                {
                    if (SeatCounty(empire) is not { } county) continue;
                    WriteTradition(b, county, "coronation_ancient_tradition_modifier", EmpireTradition);
                }

                foreach (var kingdom in Titles.Flatten(empires).Where(t => t.Tier == "k"))
                {
                    // The empire's own seat kingdom already has the ancient rung through the loop
                    // above — they share a county — and adding the lesser modifier on top would
                    // stack two tradition modifiers on one province and then contradict the
                    // variable, which the activity re-derives from on the next ceremony there.
                    if (SeatCounty(kingdom) is not { } county) continue;
                    if (empires.Any(e => ReferenceEquals(SeatCounty(e), county))) continue;

                    WriteTradition(b, county, "coronation_tradition_modifier", KingdomTradition);
                }

            }
        }

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_coronation_on_actions.txt"), b.ToString());

        WriteGiveawayOverride(modDir);
    }

    private static void WriteTradition(JominiBuilder b, Title county, string modifier, int value)
    {
        b.Blank();

        using (b.Block($"title:{county.Key}"))
        using (b.Block("title_province"))
        {
            b.Field("add_province_modifier", modifier);

            using (b.Block("set_variable"))
            {
                b.Field("name", "coronation_tradition_location");
                b.Field("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>
    /// Suppresses vanilla's game-start crown giveaway, which on this map only ever duplicates.
    ///
    /// Same directory and a later-sorting name, which is what it takes to win a database key: see
    /// the load-order rule in the notes on <c>database_conflicts.log</c>. The replacement keeps the
    /// event's shape so that anything that triggers it by id still finds an event, and logs a line
    /// so the override can be confirmed from game.log rather than assumed.
    /// </summary>
    private static void WriteGiveawayOverride(string modDir)
    {
        var b = new JominiBuilder();
        b.Comment("""
                  Vanilla's coronation_events.0302 hands every independent king and emperor a crown
                  at game start, for worlds whose rulers have none. This world's rulers have one —
                  every king's and emperor's first artifact is their sovereign piece — but 0302 runs
                  on on_game_start and the generated treasure arrives on on_game_start_after_lobby,
                  so it can only ever see a crownless king and mint a duplicate beside the real one.

                  Replaced with nothing, because the case it covers cannot arise: every ruler of
                  kingdom tier or above is given two to four artifacts and the first is forced to be
                  their crown or their regalia. A ruler who loses it later is vanilla's own problem,
                  and vanilla still solves it — the AI forges a replacement on the yearly pulse and
                  a player is offered one by realm_maintenance_events.
                  """);
        b.Blank();
        b.Field("namespace", "coronation_events");
        b.Blank();

        using (b.Block("coronation_events.0302"))
        {
            b.Field("scope", "none");
            b.Field("hidden", "yes");
            b.Blank();

            using (b.Block("immediate"))
                b.Quoted("debug_log", "gen: vanilla coronation crown giveaway suppressed");
        }

        string dir = Path.Combine(modDir, "events", "activities", "coronation_activity");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "zz_gen_coronation_events.txt"), b.ToString());
    }

    /// <summary>
    /// The county a realm is crowned in: its capital, followed down every tier rather than one.
    /// <see cref="Title.Capital"/> steps a single level, so an empire's is a kingdom.
    /// </summary>
    private static Title? SeatCounty(Title title)
    {
        var at = title;

        while (at is not null && at.Tier != "c") at = at.Capital;

        return at;
    }
}
