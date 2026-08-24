using System.Text;

namespace Ck3MapGen.Magic;

/// <summary>
/// Renders a generated system as text, and sweeps a range of seeds.
///
/// This is the whole point of the folder being runnable on its own. The question that has to be
/// answered before a single emitter is written is not "does it compile" but "is what comes out
/// compelling", and that is a question you answer by reading fifty of them, not by playing one.
/// The sweep exists because the failure mode that actually kills a procedural system is invisible
/// in any single sample: every world reads fine, and they are all the same world.
/// </summary>
public static class MagicReport
{
    // ------------------------------------------------------------------ single world

    public static string Render(MagicSystem s, MagicOptions options)
    {
        var sb = new StringBuilder();
        var myth = s.Myth;

        Rule(sb, $"SEED {s.Seed}  —  {s.Naming.Tradition}");

        if (s.IsMundane)
        {
            sb.AppendLine();
            sb.AppendLine("  A world with no practice in it. What follows is what people believe anyway.");
            sb.AppendLine();
            Prophecies(sb, s);
            Findings(sb, s, options);
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine($"  {Pitch(s)}");
        sb.AppendLine();

        // ---------------------------------------------------------------- axes
        Head(sb, "COSMOLOGY");
        Pair(sb, "source", myth.Source.ToString());
        Pair(sb, "access", string.Join(" or ", myth.Access));
        Pair(sb, "fuel", $"{myth.Fuel} ({SpellGrammar.FuelUnit(myth.Fuel)})");
        Pair(sb, "price", myth.MinorPrice is null
            ? myth.Price.ToString()
            : $"{myth.Price}, and {myth.MinorPrice} besides");
        Pair(sb, "institution", $"{myth.Institution} — {s.Naming.Institution}");
        Pair(sb, "ceiling", myth.Ceiling.ToString());
        Pair(sb, "prevalence", myth.Prevalence.ToString());
        Pair(sb, "reliability", myth.Reliability.ToString());
        Pair(sb, "domains", string.Join(", ", myth.Domains.Ranked.Select(r => $"{r.Domain} {r.Weight:0.0}")));
        Pair(sb, "forbidden", myth.Domains.Forbidden.Count == 0
            ? "nothing"
            : string.Join(", ", myth.Domains.Forbidden) + "  <- the structural fact of this world");

        if (s.CoherenceTrace.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  repaired by coherence:");
            foreach (var line in s.CoherenceTrace) sb.AppendLine($"    - {line}");
        }

        // ---------------------------------------------------------------- entry
        Head(sb, "GETTING IN");
        sb.AppendLine(s.Access.OpenToOutsiders
            ? $"  Open. A motivated outsider is in after roughly {s.Access.ExpectedYearsToInitiate:0.#} years."
            : "  Closed. There is no route in for someone not already carrying it.");
        sb.AppendLine();

        foreach (var edge in s.Access.Edges)
        {
            string mark = edge.AvailableAtStart ? " " : "x";
            sb.AppendLine($"  {mark} {edge.From,-9} -> {edge.To,-9} [{edge.Trigger}] {edge.Gate}");
        }

        sb.AppendLine();
        sb.AppendLine($"  end of the road: {s.Access.TerminalNote}");

        // ---------------------------------------------------------------- ladder
        Head(sb, "LADDER");
        foreach (var rank in s.Ladder)
            sb.AppendLine($"  {rank.Index}. {rank.Title,-16} ceiling {rank.PowerCeiling,6:0.0}   {rank.Gate}");

        // ---------------------------------------------------------------- spells
        Head(sb, $"SPELLS ({s.Spells.Count})");

        foreach (var rank in s.Ladder)
        {
            var atRank = s.Spells.Where(x => x.Rank == rank.Index).ToList();
            if (atRank.Count == 0) continue;

            sb.AppendLine();
            sb.AppendLine($"  -- {rank.Title} --");

            foreach (var spell in atRank)
            {
                sb.AppendLine();
                sb.AppendLine($"  {spell.Name}");
                sb.AppendLine($"    {spell.Delivery} on {spell.Target}   "
                              + $"cost {spell.Cost}   power {spell.Power:0.0} / price {spell.Price:0.0}");

                foreach (string line in spell.Describe())
                    sb.AppendLine($"    - {line}");

                if (spell.Requires.Count > 0)
                    sb.AppendLine($"    needs: {string.Join("; ", spell.Requires.Select(r => r.Note))}");

                if (spell.Backlash.Probability > 0.01)
                    sb.AppendLine($"    {spell.Backlash.Probability:P0} of the time: {spell.Backlash.Note}");

                sb.AppendLine($"    seen: {spell.Exposure.Visibility:P0} — {spell.Exposure.Consequence}");
            }
        }

        // ---------------------------------------------------------------- the rest
        if (s.Entities.Count > 0)
        {
            Head(sb, "WHAT ANSWERS");
            foreach (var e in s.Entities)
            {
                sb.AppendLine();
                sb.AppendLine($"  {e.Name}   [{e.Sphere}, {e.Temperament}]");
                sb.AppendLine($"    asks: {e.Price}");
                sb.AppendLine($"    gives: {string.Join("; ", e.Boons)}");
                sb.AppendLine($"    if crossed: {string.Join("; ", e.Wraths)}");
            }
        }

        Head(sb, "THE LEDGER");
        if (s.Ledger.Enabled)
        {
            sb.AppendLine($"  {s.Ledger.Note}");
            sb.AppendLine($"  decay {s.Ledger.DecayPerYear:P1} a year.");
            sb.AppendLine();
            foreach (var (threshold, consequence) in s.Ledger.Thresholds)
                sb.AppendLine($"    at {threshold,4:0}  {consequence}");
        }
        else sb.AppendLine($"  {s.Ledger.Note}");

        Head(sb, "WHAT STOPS IT");
        sb.AppendLine($"  {s.Counter.Description}");

        Head(sb, "KEYSTONE");
        sb.AppendLine($"  [{s.Keystone.Subsystem}] {s.Keystone.Description}");

        Prophecies(sb, s);
        Findings(sb, s, options);

        return sb.ToString();
    }

    /// <summary>
    /// The world's loop in one sentence. Worth more than the axis table when skimming fifty
    /// worlds, because it is written in the terms a player would use rather than the terms the
    /// generator thinks in.
    /// </summary>
    public static string Pitch(MagicSystem s)
    {
        var myth = s.Myth;

        string entry = myth.Access[0] switch
        {
            MagicAccess.Born => "You are born to it or you are not",
            MagicAccess.Taught => "Someone who has it has to agree to teach you",
            MagicAccess.Bargained => "Something has to answer when you ask",
            MagicAccess.Found => "You have to go and find it",
            MagicAccess.Suffered => "You have to survive something first",
            MagicAccess.Bought => "You have to be rich enough to be sold it",
            _ => "You have to take it off someone who already has it",
        };

        string spend = myth.Fuel switch
        {
            MagicFuel.Ambient => "you spend the ground you stand on",
            MagicFuel.Vital => "you spend your own life",
            MagicFuel.Sacrificial => "you spend other people",
            MagicFuel.Devotional => "you spend standing with your faith",
            MagicFuel.Material => "you spend gold",
            MagicFuel.Temporal => "you spend a window that will not wait",
            _ => "you pay nothing until the ledger asks",
        };

        string fear = myth.Price switch
        {
            MagicPrice.Corruption => "you are afraid of what you are turning into",
            MagicPrice.Taint => "you are afraid for your children",
            MagicPrice.Depletion => "you are afraid of running the land dry",
            MagicPrice.Attention => "you are afraid of being noticed",
            MagicPrice.Stigma => "you are afraid of being seen",
            MagicPrice.Instability => "you are afraid of what everyone will owe",
            _ => "you are afraid it goes wrong",
        };

        string reach = myth.Ceiling switch
        {
            MagicCeiling.Personal => "It reaches no further than your own skin.",
            MagicCeiling.Court => "It reaches the people in the room.",
            MagicCeiling.Realm => "It reaches provinces, armies and titles.",
            _ => "It reaches the map itself.",
        };

        return $"{entry}, {spend}, and {fear}. {reach}";
    }

    private static void Prophecies(StringBuilder sb, MagicSystem s)
    {
        if (s.Prophecies.Count == 0) return;

        Head(sb, "WHAT IS SAID");
        foreach (var p in s.Prophecies)
        {
            sb.AppendLine();
            sb.AppendLine($"  \"{p.Text}\"");
            sb.AppendLine($"    {(p.CanEverFire ? "checks" : "NEVER FIRES")}: {p.PredicateNote}");
            if (p.CanEverFire) sb.AppendLine($"    then: {p.Consequence}");
        }
    }

    private static void Findings(StringBuilder sb, MagicSystem s, MagicOptions options)
    {
        var findings = MagicValidator.Check(s, options);
        if (findings.Count == 0) return;

        Head(sb, "VALIDATOR");
        foreach (var f in findings)
        {
            string tag = f.Severity switch
            {
                FindingSeverity.Error => "ERROR",
                FindingSeverity.Warning => "warn ",
                _ => "note ",
            };

            sb.AppendLine($"  [{tag}] {f.Rule}: {f.Detail}");
        }
    }

    // ------------------------------------------------------------------ sweep

    /// <summary>
    /// Generates a range of worlds and reports on the population rather than on any one of them.
    ///
    /// Three questions, in order of how badly a "no" would hurt: are they different from each
    /// other, is the axis sampler actually reaching its whole range, and how often does the
    /// validator find something. A generator that scores well on the first and badly on the second
    /// is producing variety out of one narrow corner of the design space, which will run out.
    /// </summary>
    public static string Sweep(int firstSeed, int count, MagicOptions options)
    {
        var worlds = MagicGenerator.GenerateMany(firstSeed, count, options);
        var sb = new StringBuilder();

        Rule(sb, $"SWEEP — {count} worlds from seed {firstSeed}");
        sb.AppendLine();

        foreach (var w in worlds)
            sb.AppendLine($"  {w.Seed,6}  {Summarise(w)}");

        // ---------------------------------------------------------------- five differences
        Head(sb, "FIVE DIFFERENCES");
        sb.AppendLine("  Two worlds count as genuinely different when at least three of the five");
        sb.AppendLine("  loop-defining facts differ. Measured over every pair, not just neighbours,");
        sb.AppendLine("  because neighbouring seeds differing is the easy case.");
        sb.AppendLine();

        var live = worlds.Where(w => !w.IsMundane).ToList();
        var histogram = new int[6];
        int pairs = 0;

        for (int i = 0; i < live.Count; i++)
        for (int j = i + 1; j < live.Count; j++)
        {
            var a = live[i].Myth.LoopSignature();
            var b = live[j].Myth.LoopSignature();
            int differences = a.Zip(b).Count(p => p.First != p.Second);
            histogram[differences]++;
            pairs++;
        }

        if (pairs > 0)
        {
            for (int d = 0; d <= 5; d++)
                sb.AppendLine($"    {d} of 5 differ   {Bar(histogram[d], pairs)}  {histogram[d] * 100.0 / pairs,5:0.0}%");

            double passing = (histogram[3] + histogram[4] + histogram[5]) * 100.0 / pairs;
            sb.AppendLine();
            sb.AppendLine($"  {passing:0.0}% of pairs pass. Below about 70% and the worlds are variations");
            sb.AppendLine("  on a theme rather than different systems.");
        }

        // ---------------------------------------------------------------- axis coverage
        Head(sb, "AXIS COVERAGE");
        Histogram(sb, "source", worlds.Select(w => w.Myth.Source.ToString()));
        Histogram(sb, "fuel", worlds.Select(w => w.Myth.Fuel.ToString()));
        Histogram(sb, "price", worlds.Select(w => w.Myth.Price.ToString()));
        Histogram(sb, "institution", worlds.Select(w => w.Myth.Institution.ToString()));
        Histogram(sb, "ceiling", worlds.Select(w => w.Myth.Ceiling.ToString()));
        Histogram(sb, "prevalence", worlds.Select(w => w.Myth.Prevalence.ToString()));
        Histogram(sb, "forbidden", worlds.SelectMany(w => w.Myth.Domains.Forbidden.Select(f => f.ToString())));
        Histogram(sb, "delivery", worlds.SelectMany(w => w.Spells.Select(s => s.Delivery.ToString())));
        Histogram(sb, "keystone", worlds.Select(w => w.Keystone.Subsystem));

        // ---------------------------------------------------------------- validator
        Head(sb, "VALIDATOR ACROSS THE SWEEP");
        var all = worlds.SelectMany(w => MagicValidator.Check(w, options).Select(f => (w.Seed, f))).ToList();

        int errors = all.Count(x => x.f.Severity == FindingSeverity.Error);
        int warnings = all.Count(x => x.f.Severity == FindingSeverity.Warning);

        sb.AppendLine($"  {errors} errors, {warnings} warnings across {count} worlds.");
        sb.AppendLine();

        foreach (var group in all.GroupBy(x => x.f.Rule).OrderByDescending(g => g.Count()))
        {
            var worst = group.Max(x => x.f.Severity);
            sb.AppendLine($"    {group.Count(),4}x  [{worst}] {group.Key}");
        }

        if (errors > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Seeds with errors (a coherence rule is wrong, not a seed):");
            foreach (var seed in all.Where(x => x.f.Severity == FindingSeverity.Error)
                                    .Select(x => x.Seed).Distinct().Take(20))
                sb.AppendLine($"    {seed}");
        }

        // ---------------------------------------------------------------- shape
        Head(sb, "SHAPE");
        var spellCounts = live.Select(w => w.Spells.Count).ToList();
        if (spellCounts.Count > 0)
            sb.AppendLine($"  spells per world: min {spellCounts.Min()}, "
                          + $"mean {spellCounts.Average():0.0}, max {spellCounts.Max()}");

        sb.AppendLine($"  mundane worlds: {worlds.Count(w => w.IsMundane)} of {count}");
        sb.AppendLine($"  closed to outsiders: {live.Count(w => !w.Access.OpenToOutsiders)} of {live.Count}");
        sb.AppendLine($"  with a ledger: {live.Count(w => w.Ledger.Enabled)} of {live.Count}");
        sb.AppendLine($"  with entities: {live.Count(w => w.Entities.Count > 0)} of {live.Count}");

        return sb.ToString();
    }

    /// <summary>One scannable line per world.</summary>
    public static string Summarise(MagicSystem s)
    {
        if (s.IsMundane) return $"{"(mundane)",-26}  no practice";

        string axes = $"{s.Myth.Access[0]}/{s.Myth.Fuel}/{s.Myth.Price}/{s.Myth.Institution}";
        return $"{Trim(s.Naming.Tradition, 26),-26}  {axes,-44}  "
               + $"{s.Myth.Ceiling,-8} {s.Spells.Count,2} spells";
    }

    // ------------------------------------------------------------------ formatting

    private static void Rule(StringBuilder sb, string title)
    {
        sb.AppendLine(new string('=', 92));
        sb.AppendLine(title);
        sb.AppendLine(new string('=', 92));
    }

    private static void Head(StringBuilder sb, string title)
    {
        sb.AppendLine();
        sb.AppendLine($"-- {title} " + new string('-', Math.Max(0, 88 - title.Length)));
    }

    private static void Pair(StringBuilder sb, string key, string value) =>
        sb.AppendLine($"  {key,-13} {value}");

    private static string Bar(int value, int total)
    {
        int width = total == 0 ? 0 : (int)Math.Round(value * 40.0 / total);
        return new string('#', width).PadRight(40, '.');
    }

    private static void Histogram(StringBuilder sb, string label, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"  {label}");

        foreach (var group in list.GroupBy(v => v).OrderByDescending(g => g.Count()))
            sb.AppendLine($"    {group.Key,-16} {Bar(group.Count(), list.Count)} {group.Count(),4}");
    }

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "-";
}
