using System.Text.RegularExpressions;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

public static class InteractionWriter
{
    private const string RaceMarriageAcceptanceModifier = """
		# [Generated Phenotype Marriage Reluctance]
		modifier = {
			desc = AI_DIFFERENT_RACE_MARRIAGE_PENALTY
			trigger = {
				exists = scope:secondary_actor
				exists = scope:secondary_recipient
				scope:secondary_actor = {
					gen_is_different_race_than = { TARGET = scope:secondary_recipient }
				}
			}
			add = -75
		}
""";

    public static void PatchMarriageInteractions(string modDir, string gameDir)
    {
        string sourcePath = Path.Combine(gameDir, "common", "character_interactions", "00_marriage_interactions.txt");
        if (!File.Exists(sourcePath))
        {
            Console.WriteLine("  interactions: SKIPPED (00_marriage_interactions.txt not found in game folder)");
            return;
        }

        string text = File.ReadAllText(sourcePath);

        // Targets the ai_accept = { block inside both arrange_marriage_interaction and marry_off_interaction
        string[] interactions = ["arrange_marriage_interaction", "marry_off_interaction"];
        int patchedCount = 0;

        foreach (var interaction in interactions)
        {
            // Find the interaction definition block
            var match = Regex.Match(text, $@"\b{interaction}\s*=\s*\{{[\s\S]*?ai_accept\s*=\s*\{{");
            if (match.Success)
            {
                int insertPos = match.Index + match.Length;
                text = text.Insert(insertPos, "\n" + RaceMarriageAcceptanceModifier + "\n");
                patchedCount++;
            }
        }

        if (patchedCount == 0)
        {
            Console.WriteLine("  interactions: WARNING - Could not find ai_accept blocks in 00_marriage_interactions.txt");
            return;
        }

        string destPath = Path.Combine(modDir, "common", "character_interactions", "00_marriage_interactions.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        ParadoxText.WriteBom(destPath, text);

        Console.WriteLine($"  interactions: 00_marriage_interactions.txt — patched {patchedCount} marriage interactions with race reluctance");
    }
}