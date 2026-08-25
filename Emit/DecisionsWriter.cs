using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// A trigger or effect body, written into a block the writer has already opened.
///
/// A delegate rather than a pre-rendered string on purpose. Script fragments assembled as text
/// have to be indented by whoever built them, against a depth they cannot see, and spliced in
/// through <see cref="JominiBuilder.Raw"/> — which is invisible to the builder's brace checking.
/// Handing the open builder to the caller instead means the depth is always the real one and a
/// block that is opened is closed by the <c>using</c> that opened it, whichever file it ends up in.
/// </summary>
public delegate void ScriptBody(JominiBuilder b);

/// <summary>What a decision charges when it is taken. All three are optional and default to nothing.</summary>
public readonly record struct DecisionCost(int Gold = 0, int Prestige = 0, int Piety = 0)
{
    public bool IsFree => Gold == 0 && Prestige == 0 && Piety == 0;
}

/// <summary>How long before the same character may take the decision again.</summary>
public readonly record struct DecisionCooldown(int Years = 0, int Months = 0, int Days = 0);

/// <summary>One <c>picture</c> entry. The first whose trigger passes is drawn; a null trigger always does.</summary>
public readonly record struct DecisionPicture(string Reference, ScriptBody? Trigger = null);

/// <summary>
/// How often the AI reconsiders a decision, in months.
///
/// CK3 accepts <c>ai_check_interval</c> or <c>ai_check_interval_by_tier</c> and requires *all six*
/// tiers when the second is used, so the per-tier factory takes six arguments rather than a
/// dictionary: a missing tier is a silent load failure in the game and a compile error here.
/// </summary>
public sealed record AiCheck
{
    private AiCheck() { }

    public int? Interval { get; private init; }
    public (int Barony, int County, int Duchy, int Kingdom, int Empire, int Hegemony)? ByTier { get; private init; }

    /// <summary>The AI never looks at this decision.</summary>
    public static readonly AiCheck Never = new() { Interval = 0 };

    /// <summary>One interval for every tier.</summary>
    public static AiCheck Every(int months) => new() { Interval = months };

    /// <summary>An interval per tier. Zero means the AI at that tier never checks.</summary>
    public static AiCheck PerTier(int barony, int county, int duchy, int kingdom, int empire, int hegemony)
        => new() { ByTier = (barony, county, duchy, kingdom, empire, hegemony) };
}

/// <summary>
/// One decision, as the schema in <c>common/decisions/_decisions.info</c> describes it.
///
/// Everything the game reads is a property here and everything a *player* reads is a string here,
/// so a decision is described once and its script and its localisation cannot drift apart — the
/// bug this shape exists to prevent is a key written into <c>is_valid</c> as a
/// <c>custom_tooltip</c> and never written into the .yml, which the game renders as the raw key
/// and no validator on this machine catches.
///
/// The four loc values follow CK3's own defaults — <c>&lt;key&gt;</c>, <c>&lt;key&gt;_desc</c>,
/// <c>&lt;key&gt;_tooltip</c>, <c>&lt;key&gt;_confirm</c> — so nothing has to name them.
/// <see cref="ExtraLocalisation"/> carries the keys a body invents for itself.
///
/// Loc values are written **unescaped**, because the useful ones are not plain text: <c>$key$</c>
/// substitution is what lets a decision say a title's name without baking it in (see
/// <see cref="FormationDecisions"/>), and <c>[concept|E]</c> markup is what makes it read like
/// vanilla. A caller that interpolates a name a human typed has to escape the quotes itself.
/// </summary>
public sealed record DecisionSpec
{
    /// <summary>The decision id, and the stem of all four default localisation keys.</summary>
    public required string Key { get; init; }

    /// <summary>Its name in the decisions panel — loc <c>&lt;key&gt;</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The paragraph in the detail view — loc <c>&lt;key&gt;_desc</c>.</summary>
    public required string Description { get; init; }

    /// <summary>Hover text in the list — loc <c>&lt;key&gt;_tooltip</c>. Omitted when null.</summary>
    public string? SelectionTooltip { get; init; }

    /// <summary>The confirm button — loc <c>&lt;key&gt;_confirm</c>. Omitted when null.</summary>
    public string? ConfirmText { get; init; }

    /// <summary>Illustrations, first matching trigger wins. Never empty: ck3-tiger treats it as required.</summary>
    public IReadOnlyList<DecisionPicture> Pictures { get; init; } = [new(DecisionsWriter.MiscPicture)];

    /// <summary>A key from <c>common/decision_group_types</c>, or null for the default group.</summary>
    public string? Group { get; init; } = "major";

    /// <summary>Higher sorts first within the group. Null leaves it to definition order.</summary>
    public int? SortOrder { get; init; }

    /// <summary>Never listed in the panel — for a decision some other window renders its own button for.</summary>
    public bool Invisible { get; init; }

    public DecisionCost Cost { get; init; }

    /// <summary>Checked but not deducted, for a decision that charges later from a widget choice.</summary>
    public DecisionCost MinimumCost { get; init; }

    public DecisionCooldown? Cooldown { get; init; }

    /// <summary>Whether the decision is listed at all. Character scope.</summary>
    public ScriptBody? IsShown { get; init; }

    /// <summary>Requirements, shown in the detail view under Requirements. Character scope.</summary>
    public ScriptBody? IsValid { get; init; }

    /// <summary>Requirements shown only when they fail — age, war, prison. Character scope.</summary>
    public ScriptBody? IsValidShowingFailuresOnly { get; init; }

    /// <summary>Extra gate on the alert, on top of availability.</summary>
    public ScriptBody? ShouldCreateAlert { get; init; }

    public ScriptBody? Effect { get; init; }

    /// <summary>Whether the AI evaluates it at all. Defaults to never.</summary>
    public ScriptBody AiPotential { get; init; } = DecisionsWriter.AlwaysNo;

    /// <summary>The AI's percentage chance once it does. Defaults to zero.</summary>
    public ScriptBody AiWillDo { get; init; } = DecisionsWriter.Weight(0);

    public AiCheck Ai { get; init; } = AiCheck.Never;

    /// <summary>
    /// Loc keys the bodies name for themselves — a <c>custom_tooltip</c> in a trigger, a
    /// <c>custom_tooltip</c> around a hidden effect. Written beside the four defaults.
    /// </summary>
    public IReadOnlyList<(string Key, string Value)> ExtraLocalisation { get; init; } = [];
}

/// <summary>
/// Writes generated decisions, and knows nothing about what any of them are for.
///
/// The split is the point. CK3's decision schema has about twenty fields, four localisation keys
/// with derived names, a load-order rule and a validator that treats <c>picture</c> as required —
/// and none of that has anything to do with *which* decisions a given map wants. So the schema
/// lives here, once, and every source of decisions hands over <see cref="DecisionSpec"/> values.
/// <see cref="FormationDecisions"/> is the first such source; a second one costs a builder and a
/// line in <see cref="Emit.ContentWriter"/>, and no new understanding of the file format.
///
/// One file per group rather than one file for everything, because decisions are listed in
/// **script order** — file order, then definition order within a file — so a group that wants to
/// sit somewhere particular in the panel needs a filename it controls. <see cref="SortOrder"/>
/// overrides that, which is why the default stem sorts early and does not fight anybody.
///
/// New keys only, so no override rule applies: nothing here re-declares a vanilla decision.
/// Hiding vanilla ones is a different job with a load-order trick of its own — see
/// <see cref="CompatibilityWriter.WriteDecisionBlocks"/>.
///
/// <see cref="StruggleWriter"/> predates this and still emits its four ending decisions itself.
/// That is not a second opinion about the format — it is simply older, and it writes a file whose
/// contents are already correct. A conversion belongs with the next change to those decisions,
/// not with a refactor for its own sake.
/// </summary>
public static class DecisionsWriter
{
    /// <summary>The generic vanilla illustration, for a decision with no art of its own.</summary>
    public const string MiscPicture = "gfx/interface/illustrations/decisions/decision_misc.dds";

    /// <summary>Vanilla's realm-scale illustration, used by its own empire-founding decisions.</summary>
    public const string RealmPicture = "gfx/interface/illustrations/decisions/decision_realm.dds";

    /// <summary>Vanilla's title-founding illustration.</summary>
    public const string FoundTitlePicture = "gfx/interface/illustrations/decisions/decision_found_kingdom.dds";

    public static readonly ScriptBody AlwaysYes = b => b.Field("always", "yes");
    public static readonly ScriptBody AlwaysNo = b => b.Field("always", "no");

    /// <summary>A flat <c>ai_will_do</c> weight.</summary>
    public static ScriptBody Weight(int value) => b => b.Field("base", value);

    /// <summary>
    /// Writes one group of decisions and the localisation they need.
    ///
    /// <paramref name="stem"/> names both files, so a group is greppable from either half and the
    /// two can never be matched up wrongly. An empty list deletes both rather than leaving a stale
    /// pair behind — a re-run that produces no decisions must not ship the previous run's.
    /// </summary>
    /// <returns>How many decisions were written.</returns>
    public static int WriteAll(string modDir, IReadOnlyList<DecisionSpec> decisions,
        string stem = "00_gen_decisions", string? comment = null)
    {
        string scriptPath = Path.Combine(modDir, "common", "decisions", $"{stem}.txt");
        string locPath = Path.Combine(modDir, "localization", "english", $"{stem}_l_english.yml");

        if (decisions.Count == 0)
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
            if (File.Exists(locPath)) File.Delete(locPath);
            return 0;
        }

        var duplicate = decisions.GroupBy(d => d.Key, StringComparer.Ordinal)
                                 .FirstOrDefault(g => g.Count() > 1);

        // Not defensive padding: two decisions sharing a key means the later one silently replaces
        // the earlier one in the game's database, and the only symptom is a decision that never
        // appears. Cheaper to fail here than to find it in a save.
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate generated decision key '{duplicate.Key}'.");

        WriteScript(scriptPath, decisions, comment);
        WriteLocalisation(locPath, decisions);

        return decisions.Count;
    }

    private static void WriteScript(string path, IReadOnlyList<DecisionSpec> decisions, string? comment)
    {
        var b = new JominiBuilder();

        b.Comment(comment ?? "Generated decisions.");
        b.Blank();

        foreach (var d in decisions)
        {
            using (b.Block(d.Key))
            {
                // Every picture entry, in order. The schema takes repeated `picture` blocks rather
                // than a list, and the first whose trigger passes is the one drawn.
                foreach (var picture in d.Pictures)
                    using (b.Block("picture"))
                    {
                        if (picture.Trigger is { } trigger)
                        {
                            using (b.Block("trigger")) trigger(b);
                        }

                        b.Quoted("reference", picture.Reference);
                    }

                b.Field("decision_group_type", d.Group);
                b.Field("desc", $"{d.Key}_desc");
                if (d.SelectionTooltip is not null) b.Field("selection_tooltip", $"{d.Key}_tooltip");
                if (d.ConfirmText is not null) b.Field("confirm_text", $"{d.Key}_confirm");
                if (d.Invisible) b.Field("is_invisible", "yes");
                if (d.SortOrder is { } order) b.Field("sort_order", order);

                if (d.Cooldown is { } cooldown)
                {
                    var parts = new List<string>();
                    if (cooldown.Years > 0) parts.Add($"years = {cooldown.Years}");
                    if (cooldown.Months > 0) parts.Add($"months = {cooldown.Months}");
                    if (cooldown.Days > 0) parts.Add($"days = {cooldown.Days}");
                    if (parts.Count > 0) b.Inline("cooldown", [.. parts]);
                }

                Gate(b, "is_shown", d.IsShown);
                Gate(b, "is_valid", d.IsValid);
                Gate(b, "is_valid_showing_failures_only", d.IsValidShowingFailuresOnly);
                Gate(b, "should_create_alert", d.ShouldCreateAlert);

                WriteCost(b, "cost", d.Cost);
                WriteCost(b, "minimum_cost", d.MinimumCost);

                Gate(b, "effect", d.Effect);

                b.Blank();

                // Required unless ai_goal is set, and a decision with neither is a load error.
                if (d.Ai.Interval is { } interval) b.Field("ai_check_interval", interval);
                else if (d.Ai.ByTier is { } tiers)
                    using (b.Block("ai_check_interval_by_tier"))
                    {
                        b.Field("barony", tiers.Barony);
                        b.Field("county", tiers.County);
                        b.Field("duchy", tiers.Duchy);
                        b.Field("kingdom", tiers.Kingdom);
                        b.Field("empire", tiers.Empire);
                        b.Field("hegemony", tiers.Hegemony);
                    }

                using (b.Block("ai_potential")) d.AiPotential(b);
                using (b.Block("ai_will_do")) d.AiWillDo(b);
            }

            b.Blank();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ParadoxText.WriteBom(path, b.ToString());
    }

    /// <summary>A trigger or effect block, written only when the caller supplied one.</summary>
    private static void Gate(JominiBuilder b, string name, ScriptBody? body)
    {
        if (body is null) return;

        b.Blank();
        using (b.Block(name)) body(b);
    }

    private static void WriteCost(JominiBuilder b, string name, DecisionCost cost)
    {
        if (cost.IsFree) return;

        b.Blank();
        using (b.Block(name))
        {
            if (cost.Gold > 0) b.Field("gold", cost.Gold);
            if (cost.Prestige > 0) b.Field("prestige", cost.Prestige);
            if (cost.Piety > 0) b.Field("piety", cost.Piety);
        }
    }

    private static void WriteLocalisation(string path, IReadOnlyList<DecisionSpec> decisions)
    {
        var loc = new LocFile();

        foreach (var d in decisions)
        {
            // AddBuilt, not Add: these values carry $key$ substitutions and [concept|E] markup that
            // the escaper would leave alone but the *intent* is that they arrive verbatim, and a
            // future change to Loc() must not silently rewrite them.
            loc.AddBuilt(d.Key, d.Name);
            loc.AddBuilt($"{d.Key}_desc", d.Description);

            if (d.SelectionTooltip is { } tooltip) loc.AddBuilt($"{d.Key}_tooltip", tooltip);
            if (d.ConfirmText is { } confirm) loc.AddBuilt($"{d.Key}_confirm", confirm);

            foreach (var (key, value) in d.ExtraLocalisation) loc.AddBuilt(key, value);

            loc.Blank();
        }

        loc.Write(path);
    }
}
