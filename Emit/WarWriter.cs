using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class WarWriter
{
    public static void WriteAll(string modDir, PrehistoryMap prehistory)
    {
        // 1. Clean up old history/wars/ file so CK3-tiger doesn't complain about end_date
        string oldHistoryFile = Path.Combine(modDir, "history", "wars", "00_generated_wars.txt");
        if (File.Exists(oldHistoryFile))
        {
            File.Delete(oldHistoryFile);
        }

        if (prehistory.ActiveWars.Count == 0) return;

        // 2. Emit wars as a live on_game_start action
        string dir = Path.Combine(modDir, "common", "on_action");
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
        {
            foreach (var war in prehistory.ActiveWars)
            {
                string attackerChar = HistoryWriter.CharacterId(war.AttackerCounty);
                string defenderChar = HistoryWriter.CharacterId(war.DefenderCounty);

                b.Comment(war.Description);

                using (b.Block($"character:{attackerChar}"))
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

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_starting_wars.txt"), b.ToString());
    }
}