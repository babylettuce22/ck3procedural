using System.Text;
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

        var sb = new StringBuilder();
        sb.Append("# Active Starting Wars initiated on Game Start\n\n");

        sb.Append("on_game_start = {\n");
        sb.Append("\ton_actions = {\n");
        sb.Append("\t\tgen_start_active_wars\n");
        sb.Append("\t}\n");
        sb.Append("}\n\n");

        sb.Append("gen_start_active_wars = {\n");
        sb.Append("\teffect = {\n");

        for (int i = 0; i < prehistory.ActiveWars.Count; i++)
        {
            var war = prehistory.ActiveWars[i];

            string attackerChar = HistoryWriter.CharacterId(war.AttackerCounty);
            string defenderChar = HistoryWriter.CharacterId(war.DefenderCounty);

            sb.Append($"\t\t# {war.Description}\n");
            sb.Append($"\t\tcharacter:{attackerChar} = {{\n");
            sb.Append("\t\t\tstart_war = {\n");
            sb.Append($"\t\t\t\tcb = {war.CasusBelli}\n");
            sb.Append($"\t\t\t\ttarget = character:{defenderChar}\n");
            sb.Append($"\t\t\t\ttarget_title = title:{war.TargetTitle.Key}\n");

            if (war.ClaimantCounty is not null)
            {
                sb.Append($"\t\t\t\tclaimant = character:{HistoryWriter.CharacterId(war.ClaimantCounty)}\n");
            }

            sb.Append("\t\t\t}\n");
            sb.Append("\t\t}\n\n");
        }

        sb.Append("\t}\n");
        sb.Append("}\n");

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_starting_wars.txt"), sb.ToString());
    }
}