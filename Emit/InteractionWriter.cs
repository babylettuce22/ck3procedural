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

    /// <summary>
    /// Keeps poets from sending poems to the wilderness and ruins dummies.
    ///
    /// Most interactions never reach the dummies because their target test includes
    /// <c>is_available</c>, which the dummy's <c>incapable</c> trait fails. <c>send_poem_interaction</c>
    /// has no such test — its recipient only has to be an adult and not imprisoned — and its AI
    /// target list includes <c>neighboring_rulers</c>, which the dummy is to almost every county on
    /// the map. It is <c>auto_accept</c>, so nothing on the receiving end refuses it either.
    ///
    /// The guard goes in <c>is_shown</c>: the engine evaluates it for each AI target candidate, so it
    /// drops the dummy from the AI's pick as well as taking the button off the player's menu.
    /// </summary>
    public static void PatchPoetryInteractions(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "poetry interactions",
            "common", "character_interactions", "00_poetry_interactions.txt");

        if (patch is null) return;

        string guard =
            "\n\t\tscope:recipient = {\n"
            + "\t\t\tNOT = { has_trait = wilderness }\n"
            + "\t\t\tNOT = { government_has_flag = government_is_wilderness }\n"
            + "\t\t}\n";

        patch.InsertAfter("send_poem is_shown", guard,
            "send_poem_interaction = {", "is_shown = {");

        patch.Ship(modDir);
    }
}