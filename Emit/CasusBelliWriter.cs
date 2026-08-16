using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Prevents players and AI from declaring war on the wilderness dummy holder
/// by gating declare_war_interaction in common/character_interactions/.
/// </summary>
public static class CasusBelliWriter
{
    public static void WriteAll(string modDir, string gameDir, Config.MapConfig cfg)
    {
        if (!cfg.EnableWilderness)
        {
            Console.WriteLine("  war rules: SKIPPED (wilderness disabled)");
            return;
        }

        string sourceFile = Path.Combine(gameDir, "common", "character_interactions", "00_war.txt");
        if (!File.Exists(sourceFile))
        {
            Console.WriteLine("  war rules: SKIPPED (00_war.txt not found in game folder)");
            return;
        }

        string targetDir = Path.Combine(modDir, "common", "character_interactions");
        Directory.CreateDirectory(targetDir);

        string originalText = File.ReadAllText(sourceFile);

        // Inject wilderness blocker into declare_war_interaction is_shown / is_valid
        string condition = "\n\t\tscope:recipient = {\n\t\t\tNOT = { government_has_flag = government_is_wilderness }\n\t\t\tNOT = { has_trait = wilderness }\n\t\t}\n";

        string patched = originalText;
        int isShownIndex = patched.IndexOf("is_shown = {", StringComparison.Ordinal);
        if (isShownIndex != -1)
        {
            int insertPos = isShownIndex + "is_shown = {".Length;
            patched = patched.Insert(insertPos, condition);
        }

        ParadoxText.WriteBom(Path.Combine(targetDir, "00_war.txt"), patched);
        Console.WriteLine("  war rules: wilderness protected from war declarations via declare_war_interaction");
    }
}