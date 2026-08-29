using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// A decision per generated empire: unite its de jure kingdoms and the empire exists.
///
/// Vanilla ships about a dozen of these — Restore the Roman Empire, Found the Empire of Hindustan,
/// Revive Greater Armenia — and they are the only reason its unformed empire titles are anything
/// but grey lines. A generated map draws the same lines and, until now, gave a player no way at
/// all to sit on one: <c>found_empire_decision</c> creates a *custom* empire out of whatever the
/// founder happens to hold, so the de jure empires this generator spends real effort shaping were
/// unreachable except by conquering into them from a start that already had one.
///
/// Everything here is a plain function of the de jure tree and the wilderness map. There is no
/// random draw, so the same world produces the same decisions on every run and re-emitting after
/// an edit is safe.
///
/// **Names are never baked in.** Every string refers to a title through <c>$key$</c> loc
/// substitution, which resolves against <c>gen_titles_l_english.yml</c> — the file
/// <see cref="WorldOverwrite"/> rewrites when a title is renamed in the editor. So a renamed
/// empire renames its own decision for free, and this file is not on the list of prose that keeps
/// the name it was born with.
///
/// Written by <see cref="DecisionsWriter"/>, which owns the schema; this file owns only the
/// content and the requirements.
/// </summary>
public static class FormationDecisions
{
    /// <summary>
    /// The share of an empire's settled counties a founder's realm has to contain.
    ///
    /// Not <c>completely_controls</c>, which is what vanilla's regional decisions use. That works
    /// for vanilla because its custom regions are hand-drawn and small — Cumbria is four counties.
    /// A generated empire is three to five kingdoms, which is sixty to two hundred counties, and
    /// demanding every one of them is demanding a world conquest before the empire may be declared.
    /// A supermajority is the same idea at the right scale: the empire is unarguably yours, and the
    /// last few holdouts are what the imperial title is *for*.
    /// </summary>
    private const double CountyShare = 0.6;

    /// <summary>
    /// Empires with fewer settled counties than this get no decision.
    ///
    /// The wilderness pass can leave an empire almost entirely unpeopled, and a decision to unite
    /// six counties into an empire is a decision to break the tier system. It also guards the
    /// degenerate case the share above cannot: an empire of one settled county would ask its
    /// founder to hold one county.
    /// </summary>
    private const int MinSettledCounties = 12;

    /// <summary>
    /// Openings for the description, chosen by the empire's ordinal so the set reads as written
    /// rather than stamped, and so the choice is stable across runs. <c>{0}</c> is the empire.
    /// </summary>
    private static readonly string[] Openings =
    [
        "$0$ is a line on old maps and nothing more.",
        "There has been no crown in $0$ within living memory.",
        "$0$ exists in the chronicles, in the songs, and nowhere else.",
        "Older hands drew $0$ whole. Their successors could not hold it.",
    ];

    /// <summary>
    /// One decision per empire worth forming.
    ///
    /// Empires that already have a holder are **not** filtered out here. Whether a title is held is
    /// a runtime question — the founder may be dead by the time anyone reads this, the empire may
    /// be destroyed and rebuilt — so it is asked at runtime, in <c>is_shown</c>, and this list
    /// stays a property of the map rather than of the start date.
    /// </summary>
    public static List<DecisionSpec> Build(List<Title> empires, WildernessMap wilderness)
    {
        var specs = new List<DecisionSpec>();

        foreach (var empire in empires)
        {
            var kingdoms = empire.Children.Where(k => k.Tier == "k").ToList();
            if (kingdoms.Count == 0) continue;

            var settled = Titles.Flatten([empire])
                                .Where(t => t.Tier == "c" && !wilderness.Contains(t))
                                .ToList();

            if (settled.Count < MinSettledCounties) continue;

            specs.Add(Formation(empire, kingdoms, settled.Count));
        }

        // The world's own formation decision, when the map is large enough to have crowned one. The
        // same floor as an empire's: the real gate is the empire count below, and a small world that
        // can be united at all should be able to say so.
        if (Titles.HegemonyOf(empires) is { } hegemony)
        {
            int settled = Titles.Flatten([hegemony])
                                .Count(t => t.Tier == "c" && !wilderness.Contains(t));

            if (settled >= MinSettledCounties) specs.Add(Hegemony(hegemony, empires, settled));
        }

        return specs;
    }

    /// <summary>
    /// The share of the world's settled counties a claimant's realm has to contain.
    ///
    /// Lower than an empire's supermajority because the empire titles carry most of the weight —
    /// each one already cost its holder sixty per cent of its own de jure land — and requiring a
    /// supermajority again on top of that would be charging twice for the same conquest. This is a
    /// floor rather than the test: it stops the world being claimed by someone who inherited a
    /// collection of crowns and rules very little of the ground under them.
    /// </summary>
    private const double HegemonyCountyShare = 0.45;

    /// <summary>The decision key an empire's formation decision would have, formed or not.</summary>
    private static string KeyFor(Title empire) => $"gen_form_{empire.Key}_decision";

    /// <summary>
    /// Whether <see cref="Build"/> gave this empire a formation decision.
    ///
    /// For the caller's log line, and asked of the built list rather than recomputed, so the answer
    /// cannot disagree with what was written.
    /// </summary>
    public static bool HasFormation(IEnumerable<DecisionSpec> decisions, Title empire)
        => decisions.Any(d => d.Key == KeyFor(empire));

    private static DecisionSpec Formation(Title empire, List<Title> kingdoms, int settled)
    {
        string key = KeyFor(empire);
        string title = $"title:{empire.Key}";

        int countyGoal = Math.Max(1, (int)Math.Ceiling(settled * CountyShare));

        // Half its kingdoms, at least two, and never more than it has. Held rather than merely
        // controlled: a kingdom title that nobody has created is a kingdom nobody has united, and
        // an emperor of nothing but counties is what found_empire_decision is for.
        int kingdomGoal = Math.Min(kingdoms.Count, Math.Max(2, (kingdoms.Count + 1) / 2));

        string countyTooltip = $"{key}_counties_tt";
        string kingdomTooltip = $"{key}_kingdoms_tt";
        string deJureTooltip = $"{key}_de_jure_tt";

        return new DecisionSpec
        {
            Key = key,
            Name = $"Found the {Word(empire)} of ${empire.Key}$",
            Description = Description(empire, kingdoms, settled),
            SelectionTooltip = $"${empire.Key}$ becomes a [de_jure|E] [empire|E] under your rule.",
            ConfirmText = "Let it be one realm.",
            Pictures = [new(DecisionsWriter.RealmPicture)],
            Group = "major",
            Cost = Cost(settled),

            // Vanilla's own cadence for found_empire_decision: only a king is ever a candidate, so
            // only the kingdom tier pays for the check, and it pays every five years rather than
            // constantly — the county test below walks the whole de jure empire.
            Ai = AiCheck.PerTier(barony: 0, county: 0, duchy: 0, kingdom: 60, empire: 0, hegemony: 0),
            AiPotential = DecisionsWriter.AlwaysYes,
            AiWillDo = DecisionsWriter.Weight(100),

            IsShown = b =>
            {
                b.Field("is_playable_character", "yes");
                b.Field("is_landed_or_landless_administrative", "yes");

                // The whole gate, and the reason no start-date filtering happens at build time.
                using (b.Block("NOT")) b.Field("exists", $"{title}.holder");

                // Kings only, as vanilla's own empire decisions have it. A duke has not yet earned
                // the question, and an emperor is already answering it somewhere else.
                b.Field("highest_held_title_tier", "tier_kingdom");

                // Whose empire this is. Capital rather than "holds any county in it", because the
                // second offers every neighbouring king a claim on the same title and the decisions
                // panel fills up with other people's countries.
                using (b.Block("capital_county"))
                    b.Field("target_is_de_jure_liege_or_above", title);
            },

            IsValid = b =>
            {
                // Token, not Field: a comparison is a whole line, and `key = >= n` is what a
                // Field call produces. CK3 parses it as a syntax error and drops the rest of the
                // file, so the tell is every decision in the file going missing at once.
                b.Token("prestige_level >= 4");

                using (b.Block("custom_tooltip"))
                {
                    b.Field("text", kingdomTooltip);

                    using (b.Block("any_held_title"))
                    {
                        b.Token($"count >= {kingdomGoal}");
                        b.Field("title_tier", "kingdom");
                        b.Field("target_is_de_jure_liege_or_above", title);
                    }
                }

                using (b.Block("custom_tooltip"))
                {
                    b.Field("text", countyTooltip);

                    using (b.Block(title))
                    using (b.Block("any_de_jure_county"))
                    {
                        b.Token($"count >= {countyGoal}");

                        // Realm, not demesne. An emperor rules through his vassals, and requiring
                        // the founder to hold the counties personally would require him to break
                        // every domain limit in the game first.
                        b.Field("holder.top_liege", "root");
                    }
                }
            },

            IsValidShowingFailuresOnly = b =>
            {
                b.Field("top_liege", "this");
                b.Field("is_available_adult", "yes");
                b.Field("is_at_war", "no");
            },

            Effect = b =>
            {
                using (b.Block("create_title_and_vassal_change"))
                {
                    b.Field("type", "created");
                    b.Field("save_scope_as", "title_change");
                    b.Field("add_claim_on_loss", "no");
                }

                using (b.Block(title))
                using (b.Block("change_title_holder"))
                {
                    b.Field("holder", "root");
                    b.Field("change", "scope:title_change");
                }

                b.Field("resolve_title_and_vassal_change", "scope:title_change");
                b.Field("set_primary_title_to", title);
                b.Blank();

                // De jure drift can move a kingdom out of the empire over a long game, and an
                // empire founded with holes in it is not the empire the map drew. Vanilla's
                // Armenian decision repairs the same way. The repair is silent, so it is announced
                // by hand — a hidden_effect writes no tooltip of its own.
                b.Field("custom_tooltip", deJureTooltip);

                using (b.Block("hidden_effect"))
                    foreach (var kingdom in kingdoms)
                        using (b.Block($"title:{kingdom.Key}"))
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("NOT"))
                                b.Field("target_is_de_jure_liege_or_above", title);

                            b.Field("set_de_jure_liege_title", title);
                        }
            },

            ExtraLocalisation =
            [
                (kingdomTooltip, $"You hold at least #V {kingdomGoal}#! of the [de_jure|E] "
                               + $"[kingdoms|E] of ${empire.Key}$"),
                (countyTooltip, $"Your realm holds at least #V {countyGoal}#! of the "
                              + $"#V {settled}#! settled [counties|E] of ${empire.Key}$"),
                (deJureTooltip, $"Every [de_jure|E] [kingdom|E] of ${empire.Key}$ that has drifted "
                              + "away returns to it"),
            ],
        };
    }

    /// <summary>
    /// The decision that makes one throne out of every empire on the map.
    ///
    /// Shaped like <see cref="Formation"/> one rung up, and gated on empire *titles* rather than on
    /// a share of the world's ground. That is deliberate: an empire title already cost its holder a
    /// supermajority of its own de jure counties, so counting empires counts conquest that has
    /// already been paid for, and asks the claimant for something legible — most of the crowns —
    /// instead of a number only the tooltip can explain.
    /// </summary>
    private static DecisionSpec Hegemony(Title hegemony, List<Title> empires, int settled)
    {
        string key = KeyFor(hegemony);
        string title = $"title:{hegemony.Key}";

        // Most of them, never fewer than two. Two is the floor because a hegemony over one empire
        // is the same realm drawn twice — the same reason Titles.Crown refuses to build one.
        int empireGoal = Math.Max(2, empires.Count / 2 + 1);
        int countyGoal = Math.Max(1, (int)Math.Ceiling(settled * HegemonyCountyShare));

        string empireTooltip = $"{key}_empires_tt";
        string countyTooltip = $"{key}_counties_tt";
        string deJureTooltip = $"{key}_de_jure_tt";

        return new DecisionSpec
        {
            Key = key,
            Name = $"Proclaim the {Word(hegemony)} of ${hegemony.Key}$",
            Description = HegemonyDescription(hegemony, empires, settled),
            SelectionTooltip = $"${hegemony.Key}$ becomes a [de_jure|E] [hegemony|E] under your rule.",
            ConfirmText = "Let there be one throne above the rest.",
            Pictures = [new(DecisionsWriter.RealmPicture)],
            Group = "major",
            Cost = HegemonyCost(settled),

            // Only an emperor is ever a candidate, so only that tier pays for the check — and it is
            // a check that walks every settled county on the map, so it pays rarely.
            Ai = AiCheck.PerTier(barony: 0, county: 0, duchy: 0, kingdom: 0, empire: 120, hegemony: 0),
            AiPotential = DecisionsWriter.AlwaysYes,
            AiWillDo = DecisionsWriter.Weight(100),

            IsShown = b =>
            {
                b.Field("is_playable_character", "yes");
                b.Field("is_landed_or_landless_administrative", "yes");

                using (b.Block("NOT")) b.Field("exists", $"{title}.holder");

                // Emperors only, and no capital test beside it. The empire decisions ask whose
                // empire this is so that a title does not appear in every neighbour's panel; a
                // hegemony is de jure liege of the whole map, so that question has no answer here.
                b.Field("highest_held_title_tier", "tier_empire");
            },

            IsValid = b =>
            {
                // Token, not Field, for the same reason as Formation: a comparison is a whole line,
                // and `key = >= n` is a syntax error that eats the rest of the file.
                b.Token("prestige_level >= 5");

                using (b.Block("custom_tooltip"))
                {
                    b.Field("text", empireTooltip);

                    using (b.Block("any_held_title"))
                    {
                        b.Token($"count >= {empireGoal}");
                        b.Field("title_tier", "empire");
                    }
                }

                using (b.Block("custom_tooltip"))
                {
                    b.Field("text", countyTooltip);

                    using (b.Block(title))
                    using (b.Block("any_de_jure_county"))
                    {
                        b.Token($"count >= {countyGoal}");
                        b.Field("holder.top_liege", "root");
                    }
                }
            },

            IsValidShowingFailuresOnly = b =>
            {
                b.Field("top_liege", "this");
                b.Field("is_available_adult", "yes");
                b.Field("is_at_war", "no");
            },

            Effect = b =>
            {
                using (b.Block("create_title_and_vassal_change"))
                {
                    b.Field("type", "created");
                    b.Field("save_scope_as", "title_change");
                    b.Field("add_claim_on_loss", "no");
                }

                using (b.Block(title))
                using (b.Block("change_title_holder"))
                {
                    b.Field("holder", "root");
                    b.Field("change", "scope:title_change");
                }

                b.Field("resolve_title_and_vassal_change", "scope:title_change");
                b.Field("set_primary_title_to", title);
                b.Blank();

                // Same repair as the empire decision, one tier up: de jure drift can walk an empire
                // out from under the hegemony over a long game, and the title the map drew should
                // be the title that gets proclaimed.
                b.Field("custom_tooltip", deJureTooltip);

                using (b.Block("hidden_effect"))
                    foreach (var empire in empires)
                        using (b.Block($"title:{empire.Key}"))
                        using (b.Block("if"))
                        {
                            using (b.Block("limit"))
                            using (b.Block("NOT"))
                                b.Field("target_is_de_jure_liege_or_above", title);

                            b.Field("set_de_jure_liege_title", title);
                        }
            },

            ExtraLocalisation =
            [
                (empireTooltip, $"You hold at least #V {empireGoal}#! of the #V {empires.Count}#! "
                              + $"[empires|E] of ${hegemony.Key}$"),
                (countyTooltip, $"Your realm holds at least #V {countyGoal}#! of the "
                              + $"#V {settled}#! settled [counties|E] of ${hegemony.Key}$"),
                (deJureTooltip, $"Every [de_jure|E] [empire|E] of ${hegemony.Key}$ that has drifted "
                              + "away returns to it"),
            ],
        };
    }

    /// <summary>What proclaiming the world costs. Vanilla prices a hegemony at 2400 gold.</summary>
    private static DecisionCost HegemonyCost(int settled) => new(
        Gold: Round50(Math.Clamp(1000 + settled * 6, 1000, 2500)),
        Prestige: Round50(Math.Clamp(3000 + settled * 12, 3000, 6000)));

    /// <summary>
    /// The paragraph in the detail view, built from the empires the hegemony actually contains.
    /// </summary>
    private static string HegemonyDescription(Title hegemony, List<Title> empires, int settled)
    {
        string span = empires.Count >= 2
            ? $"{empires.Count} [empires|E], from ${empires[0].Key}$ to ${empires[^1].Key}$, have "
            + "never bowed to a single throne"
            : $"${empires[0].Key}$ has never bowed to a throne above its own";

        return $"No one has ever worn it. {span} — {settled} settled [counties|E] and no ruler "
             + $"above the rest. Hold enough of that, and ${hegemony.Key}$ stops being a word for "
             + "the world and becomes a [de_jure|E] title: one crown above all crowns.";
    }

    /// <summary>
    /// The word this title uses for itself.
    ///
    /// <see cref="Title.Form"/> is set for an imported country that named its own form of state, and
    /// the flavorization written by <see cref="TitleTierWriter"/> makes the game say that word
    /// everywhere else, so the decision saying "Empire" over a Khaganate would be the one place the
    /// map disagreed with itself. Everything without a form of its own takes the plain word for its
    /// rank, which is what vanilla's rules will render it as.
    /// </summary>
    private static string Word(Title title)
        => string.IsNullOrWhiteSpace(title.Form)
            ? title.Tier == "h" ? "Hegemony" : "Empire"
            : title.Form.Trim();

    /// <summary>
    /// What it costs to declare. Scaled by the empire's settled counties so that a sprawling one is
    /// not the same price as a compact one, and clamped at both ends so no seed produces a bargain
    /// or an impossibility.
    /// </summary>
    private static DecisionCost Cost(int settled) => new(
        Gold: Round50(Math.Clamp(200 + settled * 8, 200, 1000)),
        Prestige: Round50(Math.Clamp(1000 + settled * 15, 1000, 3000)));

    private static int Round50(int value) => (value + 25) / 50 * 50;

    /// <summary>
    /// The paragraph in the detail view.
    ///
    /// Composed from the empire's own kingdoms rather than written per map, and every name in it is
    /// a <c>$key$</c> reference, so the prose survives a rename and says something true about *this*
    /// empire — which is the only kind of flavour a generator can honestly write.
    /// </summary>
    private static string Description(Title empire, List<Title> kingdoms, int settled)
    {
        string opening = Openings[Math.Abs(empire.Index) % Openings.Length]
            .Replace("$0$", $"${empire.Key}$");

        string span = kingdoms.Count >= 2
            ? $"{kingdoms.Count} [kingdoms|E], from ${kingdoms[0].Key}$ to ${kingdoms[^1].Key}$, "
            + "have never answered to one crown"
            : $"${kingdoms[0].Key}$ has never answered to a crown above its own";

        return $"{opening} {span} — {settled} settled "
             + $"[counties|E] and no emperor. Hold enough of that ground, and the [empire|E] can be "
             + "made real: not a claim, not a memory, but a [de_jure|E] title with your name on it.";
    }
}
