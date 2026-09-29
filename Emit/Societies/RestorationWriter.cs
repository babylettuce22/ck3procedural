using Ck3MapGen.Config;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the per-world half of the Restorationist society: which crown, which house, who is sworn.
/// The other half — every event, interaction, effect and window — is the static set in
/// <c>BaseFilesToCopy/Societies</c>, and it reads everything this file decides through global
/// variables set at game start, so no static file names a generated key.
///
/// Files, all of them ours alone (nothing here is merged into a file another writer owns):
/// <list type="bullet">
/// <item><c>common/dynasties</c> and <c>common/dynasty_houses</c> <c>zz_gen_restor_*</c> — only when
/// the plan had to create the fallen house.</item>
/// <item><c>history/characters/zz_gen_restor_characters.txt</c> — the crown's last kings, and a created
/// house's exiled line.</item>
/// <item><c>history/titles/zz_gen_restor_titles.txt</c> — the crown's own history, so the title's
/// former holders panel names the kings the society mourns. HistoryWriter writes only held titles,
/// and the crown is unheld by construction, so the two never both write it.</item>
/// <item><c>common/scripted_effects/zz_gen_restor_setup_effects.txt</c> and its on_action — the
/// game-start setup.</item>
/// <item><c>localization/english/gen_restor_l_english.yml</c> — the society's nouns.</item>
/// </list>
///
/// Runs inside <see cref="ContentWriter.WriteHistoryLayer"/>, so an applied-history re-emit redoes
/// it; every file is deleted first, so a re-emit whose world has no fallen crown leaves none behind.
/// </summary>
internal static class RestorationWriter
{
    private static readonly string[] Owned =
    [
        Path.Combine("common", "dynasties", "zz_gen_restor_dynasties.txt"),
        Path.Combine("common", "dynasty_houses", "zz_gen_restor_houses.txt"),
        Path.Combine("history", "characters", "zz_gen_restor_characters.txt"),
        Path.Combine("history", "titles", "zz_gen_restor_titles.txt"),
        Path.Combine("common", "scripted_effects", "zz_gen_restor_setup_effects.txt"),
        Path.Combine("common", "on_action", "zz_gen_restor_on_actions.txt"),
        Path.Combine("localization", "english", "gen_restor_l_english.yml"),
    ];

    public static void WriteAll(string modDir, MapConfig cfg, RestorationPlan? plan,
        CultureMap cultures, EthnicityMap ethnicities)
    {
        foreach (var rel in Owned)
        {
            var path = Path.Combine(modDir, rel);
            if (File.Exists(path)) File.Delete(path);
        }

        // The on_action and the setup effect are written even without a plan, so the static set's
        // game-start hook always has something to call. An empty setup leaves global_var:restor_crown
        // unset, which is the one condition every static file gates on.
        WriteSetup(modDir, cfg, plan);
        WriteLocalisation(modDir, plan);
        if (plan is null) return;

        var rng = Rng.For(cfg.Seed, 0x5E57, 3, cfg.PeopleSalt);
        string Date(int year) => $"{year}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";

        var births = plan.Minted.ToDictionary(p => p.Id, p => Date(p.Born));
        var deaths = plan.Minted.Where(p => p.Died is not null).ToDictionary(p => p.Id, p => Date(p.Died!.Value));

        if (plan.MintedHouseName is not null) WriteHouse(modDir, plan);
        WriteCharacters(modDir, cfg, plan, HistoryWriter.NameTokens(cultures), ethnicities, births, deaths);

        // Additional bookmarks write their own holders for titles on their dates, and an usurped
        // crown's history is HistoryWriter's (it has a holder today); a second file naming the same
        // title would race either. The kings still exist as people either way.
        if (!cfg.UsesAdditionalBookmarks && !plan.CrownHeld) WriteTitleHistory(modDir, plan, deaths, rng);
    }

    private static void WriteHouse(string modDir, RestorationPlan plan)
    {
        var b = new JominiBuilder();
        b.Comment("The fallen house of the Restorationist society. Written by Emit/Societies/RestorationWriter.cs.");
        b.Blank();
        using (b.Block(plan.DynastyId))
        {
            b.Quoted("name", plan.HouseNameKey);
            b.Quoted("culture", plan.Culture.Key);
        }
        Write(modDir, Owned[0], b.ToString());

        var h = new JominiBuilder();
        h.Comment("The fallen house of the Restorationist society. Written by Emit/Societies/RestorationWriter.cs.");
        h.Blank();
        using (h.Block(plan.HouseKey))
        {
            if (plan.MintedPrefix is not null) h.Quoted("prefix", plan.MintedPrefix);
            h.Quoted("name", plan.HouseNameKey);
            h.Field("dynasty", plan.DynastyId);
        }
        Write(modDir, Owned[1], h.ToString());
    }

    /// <param name="nameToken">
    /// HistoryWriter's name-to-loc-key map: a history <c>name</c> is a loc key, and a raw name logs
    /// "Missing loc for name" per character. The minted names are drawn from the culture's own lists,
    /// so every one has a key.
    /// </param>
    private static void WriteCharacters(string modDir, MapConfig cfg, RestorationPlan plan,
        Func<string, string> nameToken, EthnicityMap ethnicities,
        Dictionary<string, string> births, Dictionary<string, string> deaths)
    {
        var b = new JominiBuilder();
        b.Comment("The fallen crown's kings and, where the house had to be written, its exiled line.\n"
            + "Written by Emit/Societies/RestorationWriter.cs; eldest first, so a parent always precedes its child.");
        b.Blank();

        string? phenotype = HistoryWriter.GetPhenotypeTrait(plan.Culture, ethnicities, cfg);
        foreach (var p in plan.Minted)
        {
            using (b.Block(p.Id))
            {
                b.Quoted("name", nameToken(p.Name));
                if (p.Female) b.Field("female", "yes");
                b.Field("dynasty_house", plan.HouseKey);
                b.Field("religion", plan.Faith.Key);
                b.Field("culture", plan.Culture.Key);
                b.Field("trait", phenotype);
                b.Field("father", p.FatherId);
                b.Field("mother", p.MotherId);

                // The pretender was raised to it: a claimant who could not read a letter or sit a
                // horse would be no rallying point, so the living line gets a lettered upbringing.
                if (p.Landless && p.Died is null)
                {
                    b.Field("diplomacy", 8 + (int)(Rng.StableHash(p.Id) % 6));
                    b.Field("martial", 6 + (int)(Rng.StableHash(p.Id + "m") % 6));
                    b.Field("stewardship", 5 + (int)(Rng.StableHash(p.Id + "s") % 5));
                    b.Field("intrigue", 5 + (int)(Rng.StableHash(p.Id + "i") % 6));
                    b.Field("learning", 5 + (int)(Rng.StableHash(p.Id + "l") % 5));
                }

                b.Inline(births[p.Id], "birth = yes");
                if (deaths.TryGetValue(p.Id, out var died)) b.Inline(died, "death = yes");
            }
            b.Blank();
        }
        Write(modDir, Owned[2], b.ToString());
    }

    /// <summary>
    /// The crown's holders up to the fall and nobody after. The last king's reign ends on the day he
    /// died, which is the day the crown fell — the title panel then reads as a line cut off.
    /// </summary>
    private static void WriteTitleHistory(string modDir, RestorationPlan plan,
        Dictionary<string, string> deaths, Rng rng)
    {
        var b = new JominiBuilder();
        b.Comment("The fallen crown's last kings. Written by Emit/Societies/RestorationWriter.cs.");
        b.Blank();
        using (b.Block(plan.Crown.Key))
        {
            // Each reign opens on the day the one before it closed; the first on a date of its own.
            string? previous = null;
            foreach (var (id, from) in plan.Kings)
            {
                string start = previous is not null && deaths.TryGetValue(previous, out var handover)
                    ? handover
                    : $"{from}.{rng.Int(1, 12)}.{rng.Int(1, 28)}";
                using (b.Block(start)) b.Field("holder", id);
                previous = id;
            }

            var last = plan.Kings[^1].Id;
            var fell = deaths.TryGetValue(last, out var d) ? d : $"{plan.FellYear}.6.1";
            using (b.Block(fell)) b.Field("holder", "0");
        }
        Write(modDir, Owned[3], b.ToString());
    }

    private static void WriteSetup(string modDir, MapConfig cfg, RestorationPlan? plan)
    {
        var b = new JominiBuilder();
        b.Comment("The Restorationist society's starting state, for this world only.\n"
            + "Written by Emit/Societies/RestorationWriter.cs. Everything it sets is read by the static\n"
            + "set in BaseFilesToCopy/Societies through global variables; see its README.");
        b.Blank();

        using (b.Block("restor_world_setup_effect"))
        {
            if (plan is null)
            {
                b.Comment("No kingdom in this world fits: every one is held, or none is large enough.\n"
                    + "Leaving global_var:restor_crown unset switches the whole society off.");
                b.Field("set_global_variable", "restor_absent");
            }
            else
            {
                using (StartGate.LatestOnly(b, cfg))
                {
                    void Global(string name, string value)
                    {
                        using (b.Block("set_global_variable"))
                        {
                            b.Field("name", name);
                            b.Field("value", value);
                        }
                    }

                    Global("restor_crown", $"title:{plan.Crown.Key}");
                    Global("restor_seat", $"title:{plan.OldSeat.Key}");
                    Global("restor_house", $"house:{plan.HouseKey}");
                    Global("restor_fell_year", plan.FellYear.ToString());
                    Global("restor_phase", "1");

                    using (b.Block($"character:{plan.PretenderId}"))
                        b.Field("restor_make_pretender_effect", "yes");

                    // Sheltered: a landless pretender at the court of a friend, where the engine
                    // would otherwise seat them with nobody in particular.
                    if (plan.HostId is not null)
                        using (b.Block($"character:{plan.HostId}"))
                            b.Field("add_courtier", $"character:{plan.PretenderId}");

                    foreach (var m in plan.Members)
                        using (b.Block($"character:{m.Id}"))
                        using (b.Block("restor_swear_in_effect"))
                            b.Field("RANK", m.Rank);

                    using (b.Block($"character:{plan.KeeperId}"))
                        b.Field("restor_make_keeper_effect", "yes");

                    // The enemy at the start: whoever holds the crown, else the most of its land.
                    // The runtime keeps a first usurper while they stay a fair one, so seeding it here
                    // is what makes the largest holder the enemy rather than whoever sits in the seat.
                    if (plan.UsurperId is not null)
                        Global("restor_usurper", $"character:{plan.UsurperId}");

                    b.Field("restor_world_started_effect", "yes");
                }
            }
        }
        Write(modDir, Owned[4], b.ToString());

        var o = new JominiBuilder();
        o.Comment("Seeds the Restorationist society. Written by Emit/Societies/RestorationWriter.cs.\n"
            + "on_game_start, not after the lobby: the Sworn trait is then already on the characters the\n"
            + "player picks between. The player's own introduction fires after the lobby, from the static set.");
        o.Blank();
        using (o.Block("on_game_start"))
        using (o.Block("on_actions"))
            o.Token("gen_restor_game_start");
        o.Blank();
        using (o.Block("gen_restor_game_start"))
        using (o.Block("effect"))
            o.Field("restor_world_setup_effect", "yes");
        Write(modDir, Owned[5], o.ToString());
    }

    /// <summary>
    /// The society's nouns. Every name is a <c>$key$</c> into the world's own localisation where one
    /// exists, so an editor rename of the crown or the old seat renames the society's prose with it.
    /// </summary>
    private static void WriteLocalisation(string modDir, RestorationPlan? plan)
    {
        var loc = new LocFile();
        if (plan is null)
        {
            loc.Add("restor_society_name", "The Sworn");
            loc.Write(Path.Combine(modDir, Owned[6]));
            return;
        }

        string crown = $"${plan.Crown.Key}$";
        string seat = $"${plan.OldSeat.Key}$";
        string house = $"${plan.HouseNameKey}$";

        string name = plan.NamePattern switch
        {
            0 => $"The Sworn of {crown}",
            1 => $"The Oath of {crown}",
            2 => $"The Keepers of {seat}",
            3 => $"The Friends of House {house}",
            _ => "The Company of the Empty Throne",
        };

        loc.AddBuilt("restor_society_name", name);
        loc.AddBuilt("restor_society_name_public", $"The Order of the Crown of {crown}");
        loc.AddBuilt("restor_crown_name", crown);
        loc.AddBuilt("restor_seat_name", seat);
        loc.AddBuilt("restor_house_name", house);
        loc.Add("restor_fell_year", plan.FellYear.ToString());

        string how = (plan.FallKind, plan.CrownHeld) switch
        {
            ("absorbed", false) => "when it was conquered and its land divided among the victors",
            ("absorbed", true) => "when it was conquered and its crown taken by another house",
            ("collapsed", _) => "when its great lords broke away and would no longer answer to the crown",
            ("fragmented", _) => "when it broke apart in war",
            _ => "in wars that few now living remember clearly",
        };
        loc.Add("restor_fall_how", how);

        // The opening sentence every introduction starts from. Whole, because a broken crown and an
        // usurped one are different facts: one has no king, the other has the wrong one.
        loc.AddBuilt("restor_fall_sentence", plan.CrownHeld
            ? $"{crown} has not had a ruler of House {house} since {plan.FellYear}. It was lost {how}, and another house wears its crown now."
            : $"{crown} has had no ruler since {plan.FellYear}. It fell {how}, and its last king was of House {house}.");

        if (plan.MintedHouseName is not null)
            loc.AddUnversioned(plan.HouseNameKey, plan.MintedHouseName);

        loc.Write(Path.Combine(modDir, Owned[6]));
    }

    private static void Write(string modDir, string rel, string text)
    {
        var path = Path.Combine(modDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, text);
    }
}
