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

    /// <summary>
    /// Adds the cross-race reluctance modifier to the <c>ai_accept</c> of both marriage
    /// interactions.
    ///
    /// Both, or neither: patching one leaves the AI happy to marry across races in whichever
    /// direction the other interaction covers, which reads as the feature being broken rather than
    /// off. The old code warned only when *zero* of the two landed and shipped the file on one.
    /// </summary>
    public static void PatchMarriageInteractions(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "interactions",
            "common", "character_interactions", "00_marriage_interactions.txt");

        if (patch is null) return;

        string[] interactions = ["arrange_marriage_interaction", "marry_off_interaction"];

        foreach (var interaction in interactions)
            patch.InsertAfter($"{interaction} ai_accept",
                "\n" + RaceMarriageAcceptanceModifier + "\n",
                $"{interaction} = {{", "ai_accept = {");

        patch.Ship(modDir);
    }
}