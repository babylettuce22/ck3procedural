using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class WarWriter
{
    public static void WriteAll(string modDir, PrehistoryMap prehistory, Config.MapConfig cfg)
    {
        // 1. Clean up old history/wars/ file so CK3-tiger doesn't complain about end_date
        string oldHistoryFile = Path.Combine(modDir, "history", "wars", "00_generated_wars.txt");
        if (File.Exists(oldHistoryFile))
        {
            File.Delete(oldHistoryFile);
        }

        // 2. Emit wars as a live on_game_start action — and with none, take away any an earlier write
        // left, since applying a history rewrites this layer over the mod rather than clearing it.
        string dir = Path.Combine(modDir, "common", "on_action");
        string file = Path.Combine(dir, "00_generated_starting_wars.txt");
        if (prehistory.ActiveWars.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }

        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Active Starting Wars initiated on Game Start");
        b.Blank();

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_start_active_wars");

        b.Blank();

        using (b.Block("gen_start_active_wars"))
        using (b.Block("effect"))
        using (StartGate.LatestOnly(b, cfg))
        {
            foreach (var war in prehistory.ActiveWars)
            {
                string attackerChar = HistoryWriter.CharacterId(war.AttackerCounty);
                string defenderChar = HistoryWriter.CharacterId(war.DefenderCounty);

                b.Comment(war.Description);

                using (b.Block($"character:{attackerChar}"))
                {
                    using (b.Block("start_war"))
                    {
                        b.Field("cb", war.CasusBelli);
                        b.Field("target", $"character:{defenderChar}");
                        b.Field("target_title", $"title:{war.TargetTitle.Key}");

                        // Emitted only for claim wars; Field skips a null value.
                        b.Field("claimant", war.ClaimantCounty is null
                            ? null
                            : $"character:{HistoryWriter.CharacterId(war.ClaimantCounty)}");
                    }

                    if (war.AttackingAllies.Count + war.DefendingAllies.Count > 0)
                        using (b.Block("every_character_war"))
                        {
                            using (b.Block("limit"))
                            {
                                b.Field("primary_attacker", $"character:{attackerChar}");
                                b.Field("primary_defender", $"character:{defenderChar}");
                            }
                            b.Field("save_scope_as", "target");
                            // Scope the actual war just started, rather than an arbitrary war of its
                            // ruler. Alliances may have expired while military participation continued.
                            foreach (var ally in war.AttackingAllies)
                                Join(ally, "add_attacker");
                            foreach (var ally in war.DefendingAllies)
                                Join(ally, "add_defender");

                            void Join(MapGen.Title ally, string effect)
                            {
                                string id = $"character:{HistoryWriter.CharacterId(ally)}";
                                using (b.Block("if"))
                                {
                                    using (b.Block("limit"))
                                    {
                                        using (b.Block(id))
                                        {
                                            b.Field("is_alive", "yes");
                                            b.Field("is_landed", "yes");
                                            b.Field("is_independent_ruler", "yes");
                                            using (b.Block("trigger_if"))
                                            {
                                                using (b.Block("limit")) b.Field("government_has_flag", "government_is_theocracy");
                                                b.Field("faith", effect == "add_attacker" ? $"character:{attackerChar}.faith" : $"character:{defenderChar}.faith");
                                            }
                                        }
                                        using (b.Block("NOT")) b.Field("is_participant", id);
                                        using (b.Block("can_join_war_liege_vassal_check_trigger"))
                                        {
                                            b.Field("WARRIOR", effect == "add_attacker" ? $"character:{attackerChar}" : $"character:{defenderChar}");
                                            b.Field("JOINER", id);
                                        }
                                        using (b.Block("joiner_not_already_in_another_war_with_any_target_war_participants_trigger"))
                                        {
                                            b.Field("WARRIOR", effect == "add_attacker" ? $"character:{attackerChar}" : $"character:{defenderChar}");
                                            b.Field("JOINER", id);
                                        }
                                    }
                                    b.Field(effect, id);
                                }
                            }
                        }
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(file, b.ToString());
    }
}
