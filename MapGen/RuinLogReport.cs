using System.Globalization;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The <c>--ruinlog</c> command: reads the <c>RUINLOG</c> lines the Ruins mod writes into CK3's
/// <c>debug.log</c> and answers the question the running game cannot — why counties are ruining.
/// </summary>
/// <remarks>
/// <para>
/// The mechanic destroys its own evidence. A good year zeroes the decay counter rather than
/// decrementing it, <c>gen_ruin_forget_county_effect</c> deletes every variable behind it, and the
/// five reasons a year can be bad are read as a single boolean — so a ruined county carries no
/// record of what happened to it. The mod therefore writes one line at each of four moments and
/// this reads them back. See <c>BaseFilesToCopy/Ruins/common/scripted_effects/00_ruins_effects.txt</c>.
/// </para>
/// <para>
/// ---- What the four kinds are for ----
/// </para>
/// <para>
/// <c>fall</c> alone is a biased sample: it is the output of a filter, and reading only it tells you
/// what the threshold caught while saying nothing about what it nearly caught. <c>reset</c> is the
/// counterfactual — a streak that ended without a fall, logged with the count it had reached — and
/// the two together are what make the threshold judgeable. If resets cluster at one year and falls
/// at fifteen, the bar is doing real work; if resets cluster at fourteen, most of the map is one
/// unlucky year from ruin and the number is a coin toss.
/// </para>
/// <para>
/// ---- Reading the cause columns ----
/// </para>
/// <para>
/// They are counts of YEARS in the streak in which each arm was true, so they sum to more than the
/// decay count whenever causes overlap. The useful reading is per-fall dominance: which single arm
/// was true for the most years of the run that killed this county. A world where <c>control</c>
/// dominates every fall is a world where ruination is measuring administration; one where
/// <c>devdrop</c> dominates is measuring economics; one where <c>contested</c> dominates is measuring
/// war, which is the failure mode this mechanic was rewritten to stop having.
/// </para>
/// <para>
/// ---- Robustness ----
/// </para>
/// <para>
/// Fields are read BY NAME, never by position, and an unknown field is ignored rather than fatal.
/// That is deliberate: the log format is a localisation string in a .yml that somebody will add a
/// field to, and a parser that broke on the addition would be a parser nobody updates. A line that
/// yields no recognisable fields is counted as malformed and reported, so a format change is
/// visible rather than silent.
/// </para>
/// </remarks>
public static class RuinLogReport
{
    private const string Marker = "RUINLOG;";

    /// <summary>One parsed log line. Every count defaults to zero, so a missing field reads as none.</summary>
    private sealed class Entry
    {
        public string Kind = "";
        public string County = "";
        public int Year, Decay, Since, Control, Opinion, Development;
        public int Contested, NoControl, Hated, Plague, Emptying;
        public int Occupied, Besieged, Looted;

        /// <summary>Years from the streak's first bad year to this line. Zero when unknown.</summary>
        public int Length => Since > 0 && Year >= Since ? Year - Since : 0;

        /// <summary>
        /// The arm that was true for the most years of this streak, or "none" when nothing was
        /// counted. Ties go to the earlier name in the list, which is stable rather than meaningful —
        /// a genuinely tied fall is a compound cause and the per-arm totals are where to read it.
        /// </summary>
        public string Dominant
        {
            get
            {
                (string Name, int N)[] arms =
                [
                    ("contested", Contested), ("no-control", NoControl), ("hated", Hated),
                    ("plague", Plague), ("emptying", Emptying),
                ];
                var best = arms.OrderByDescending(a => a.N).First();
                return best.N == 0 ? "none" : best.Name;
            }
        }

        /// <summary>Whether any violence was recorded against this county during the streak.</summary>
        public bool Violent => Occupied + Besieged + Looted > 0;
    }

    public static bool Run(string path)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"No such file: {path}");
            Console.Error.WriteLine("Point --ruinlog at CK3's debug.log, usually");
            Console.Error.WriteLine(@"  %USERPROFILE%\Documents\Paradox Interactive\Crusader Kings III\logs\debug.log");
            return false;
        }

        var entries = new List<Entry>();
        int malformed = 0, unresolved = 0;
        bool probeSeen = false, probeOk = false;

        foreach (string raw in ReadSharedLines(path))
        {
            int at = raw.IndexOf(Marker, StringComparison.Ordinal);
            if (at < 0) continue;

            // From the marker to end of line. debug.log prefixes its own timestamp and category,
            // and neither is worth parsing when the line already carries its own year.
            string line = raw[at..].TrimEnd();

            // The probe is checked rather than collected. Its whole job is to prove the log's two
            // reading mechanisms resolved at all — a variable off THIS and a name off THIS — so a
            // `probe` field reading anything but the stamped 1234 means the rest is fiction.
            //
            // `ERROR:` is what the game actually prints for a bracket it cannot resolve; the first
            // version of this format printed it in every field. Testing for it as well as for the
            // constant means a partial failure — the number resolving and the name not — is caught
            // too, which the constant alone would sail past.
            if (line.StartsWith("RUINLOG;probe", StringComparison.Ordinal))
            {
                probeSeen = true;
                probeOk = line.Contains("probe=1234", StringComparison.Ordinal)
                    && !line.Contains("ERROR:", StringComparison.Ordinal);
                continue;
            }

            // A field the game could not resolve is printed as `ERROR:[name|`, which parses as no
            // number at all — so such a line would otherwise be counted as merely malformed and the
            // real cause left to guess at. Counted separately because it has one specific meaning
            // and one specific fix: a variable named in the .yml that is not on the county.
            if (line.Contains("ERROR:", StringComparison.Ordinal)) unresolved++;

            var entry = Parse(line);
            if (entry is null) malformed++;
            else entries.Add(entry);
        }

        if (unresolved > 0)
        {
            Console.WriteLine($"!! {unresolved} line(s) contain unresolved fields (`ERROR:[...`).");
            Console.WriteLine("!! A datafunction in the RUINLOG localisation did not resolve. Every field must");
            Console.WriteLine("!! hang off THIS or off a global — `debug_log` cannot see saved scopes — and every");
            Console.WriteLine("!! variable it names must be stamped before the line is written.");
            Console.WriteLine();
        }

        if (probeSeen && !probeOk)
        {
            // Loud, and before anything else. Every figure in the report is drawn from the same
            // localisation machinery the probe just failed, so a summary printed under this banner
            // would be a page of confident zeroes.
            Console.WriteLine("!! The interpolation probe FAILED.");
            Console.WriteLine("!! A RUINLOG;probe line was written, but its `probe` field did not come back as the");
            Console.WriteLine("!! 1234 the effect stamped, or the line carried an unresolved field. The log's own");
            Console.WriteLine("!! reading mechanism is broken, so every number below it is meaningless.");
            Console.WriteLine("!! Fix the logging before reading anything into the report.");
            Console.WriteLine();
        }
        else if (!probeSeen && entries.Count > 0)
        {
            Console.WriteLine("(No probe line in this log — it is written once at game start, so this log");
            Console.WriteLine(" probably begins mid-session. The figures are unverified but usually fine.)");
            Console.WriteLine();
        }

        if (entries.Count == 0)
        {
            Console.WriteLine($"No RUINLOG entries in {path}.");
            Console.WriteLine("The mod writes them only under -debug_mode; check the launch flag first.");
            if (malformed > 0) Console.WriteLine($"{malformed} line(s) carried the marker but parsed as nothing.");
            return true;
        }

        var falls = entries.Where(e => e.Kind == "fall").ToList();
        var resets = entries.Where(e => e.Kind == "reset").ToList();
        var warns = entries.Where(e => e.Kind == "warn").ToList();
        var years = entries.Where(e => e.Kind == "year").ToList();

        Console.WriteLine($"=== {path}");
        Console.WriteLine($"{entries.Count} entries: {falls.Count} falls, {warns.Count} warnings, "
            + $"{resets.Count} recoveries, {years.Count} bad years"
            + (malformed > 0 ? $", {malformed} malformed" : ""));

        int lo = entries.Min(e => e.Year), hi = entries.Max(e => e.Year);
        int span = Math.Max(1, hi - lo + 1);
        Console.WriteLine($"Years {lo}-{hi} ({span}), "
            + $"{falls.Count / (double)span:0.00} falls a year, "
            // Across every kind, not just `year`. Counting bad-year lines alone undercounted this
            // badly once the suffering arms were tightened: a county shocked into the watchlist and
            // reset before it ever had a bad year has no `year` line at all, and in one measured
            // world that was 96% of them — the figure read 34 counties where the true answer was
            // 826, which made the mechanic look narrowly targeted when it was touching the map.
            + $"{entries.Select(e => e.County).Distinct().Count()} counties touched, "
            + $"{falls.Select(e => e.County).Distinct().Count()} lost.");
        Console.WriteLine();

        Section("WHY THEY FELL", falls, "fall");
        Section("WHY THEY RECOVERED INSTEAD", resets, "reset");

        // The two distributions side by side are the tuning instrument. Falls say what the
        // threshold caught; resets say how close everything else came to it.
        if (falls.Count > 0 || resets.Count > 0)
        {
            Console.WriteLine("STREAK LENGTH AT THE END OF THE RUN");
            Console.WriteLine("  How many unbroken bad years each county had accumulated when it either fell");
            Console.WriteLine("  or recovered. If the two overlap heavily the threshold is close to a coin toss.");
            Histogram("  falls  ", falls.Select(e => e.Decay));
            Histogram("  resets ", resets.Select(e => e.Decay));
            Console.WriteLine();
        }

        if (falls.Count > 0)
        {
            // How much of the threshold each fall bought with violence rather than with bad years.
            //
            // This used to report the share of falls with NO violence in them and call that the
            // headline for whether ruination measures neglect. That reading was wrong, and wrong in
            // a way worth recording: `gen_ruin_shock_effect` is the only door onto the watchlist, so
            // a county that is never attacked is never even looked at. The figure was therefore
            // pinned at 0% by construction and could never have said anything else.
            //
            // What the shock SHARE says is answerable: a fall that took 4 points from one war and 11
            // from eleven bad years is mostly neglect; one that took 12 from four wars is not.
            int shockPts = falls.Sum(f => f.Occupied + f.Besieged + 2 * f.Looted);
            int allPts = Math.Max(1, falls.Sum(f => f.Decay));
            Console.WriteLine("HOW THE FALLS WERE PAID FOR");
            Console.WriteLine($"  {shockPts} of {allPts} points ({shockPts * 100.0 / allPts:0}%) came from violence;"
                + $" the rest from bad years.");
            Console.WriteLine("  Every county on the watchlist got there by being attacked — the shock hooks are the");
            Console.WriteLine("  only way on — so a fall with no violence in it cannot happen and its absence means");
            Console.WriteLine("  nothing. This share is the answerable question: a high one means the wars are doing");
            Console.WriteLine("  the work, a low one means the years afterwards are.");
            Console.WriteLine();

            Console.WriteLine("THE TWENTY MOST RECENT FALLS");
            Console.WriteLine("  year  county                          run  dev  ctl   op  dominant cause");
            foreach (var f in falls.OrderByDescending(e => e.Year).Take(20))
            {
                string name = f.County.Length > 30 ? f.County[..29] + "…" : f.County;
                Console.WriteLine($"  {f.Year,4}  {name,-30}  {f.Decay,3}  {f.Development,3}  "
                    + $"{f.Control,3}  {f.Opinion,3}  {f.Dominant}"
                    + (f.Violent ? $"  (occ {f.Occupied}, siege {f.Besieged}, loot {f.Looted})" : ""));
            }
            Console.WriteLine();
        }

        return true;
    }

    /// <summary>
    /// The per-cause breakdown for one kind of line: how often each arm dominated a run, and how
    /// many years each arm was true across all of them.
    /// </summary>
    /// <remarks>
    /// Both columns are printed because they answer different questions and disagree in exactly the
    /// interesting case. "Dominated" counts counties and says what the usual story is; "years"
    /// counts years and says what is most present overall. An arm that is never dominant but always
    /// second is invisible in the first column and obvious in the second — plague behaves like this
    /// by construction, since it is short and rarely the longest-running cause of anything.
    /// </remarks>
    private static void Section(string title, List<Entry> set, string kind)
    {
        Console.WriteLine(title);
        if (set.Count == 0)
        {
            Console.WriteLine($"  No {kind} lines in this log.");
            Console.WriteLine();
            return;
        }

        (string Name, Func<Entry, int> Get)[] arms =
        [
            ("contested (somebody else holds it)", e => e.Contested),
            ("no control", e => e.NoControl),
            ("hated", e => e.Hated),
            ("plague", e => e.Plague),
            ("emptying (development falling)", e => e.Emptying),
        ];

        var dominant = set.GroupBy(e => e.Dominant).ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine("  cause                                 dominated     total years");
        foreach (var (name, get) in arms)
        {
            string key = name.Split(' ')[0] switch
            {
                "contested" => "contested",
                "no" => "no-control",
                "hated" => "hated",
                "plague" => "plague",
                _ => "emptying",
            };
            dominant.TryGetValue(key, out int dom);
            Console.WriteLine($"  {name,-36}  {dom,5} ({dom * 100.0 / set.Count,3:0}%)  {set.Sum(get),11}");
        }
        if (dominant.TryGetValue("none", out int none) && none > 0)
            Console.WriteLine($"  {"(no cause recorded)",-36}  {none,5} ({none * 100.0 / set.Count,3:0}%)");
        Console.WriteLine();
    }

    /// <summary>A one-line text histogram over small integers.</summary>
    /// <remarks>
    /// Bucketed by exact value rather than by range because the values being counted are decay
    /// points, which top out in the teens — a range bucket would hide the one thing worth seeing,
    /// which is whether resets pile up just short of the threshold.
    /// </remarks>
    private static void Histogram(string label, IEnumerable<int> values)
    {
        var list = values.ToList();
        if (list.Count == 0) { Console.WriteLine($"{label}(none)"); return; }

        int max = list.Max();
        var counts = new int[max + 1];
        foreach (int v in list) if (v >= 0) counts[v]++;
        int peak = counts.Max();

        Console.Write(label);
        for (int v = 0; v <= max; v++)
        {
            if (counts[v] == 0) continue;
            // Scaled to the tallest bar rather than to the count, so the two rows above and below
            // each other are comparable in shape even when one has ten times the entries.
            int width = Math.Max(1, counts[v] * 20 / peak);
            Console.Write($"{v}:{new string('#', width)}({counts[v]}) ");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Reads a file CK3 currently has open.
    /// </summary>
    /// <remarks>
    /// <c>File.ReadLines</c> throws IOException on <c>debug.log</c> whenever the game is running,
    /// because CK3 keeps its own write handle and .NET asks for exclusive read by default. That is
    /// precisely the moment this tool is most wanted — the interesting question is usually "what has
    /// it logged so far", asked with the game still up — so sharing the handle is not a nicety.
    /// <c>FileShare.Delete</c> is in there because the game rotates its logs at startup.
    /// </remarks>
    private static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    /// <summary>
    /// Splits one RUINLOG line into an entry, or null if nothing recognisable came out.
    /// </summary>
    /// <remarks>
    /// The county name is taken from the raw text after <c>county=</c> rather than from the split,
    /// so a generated name containing a semicolon survives. That is why the field is last in the
    /// format: everything numeric has already been read to its left.
    /// </remarks>
    private static Entry? Parse(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 3) return null;

        var e = new Entry { Kind = parts[1] };
        bool any = false;

        int nameAt = line.IndexOf(";county=", StringComparison.Ordinal);
        if (nameAt >= 0) e.County = line[(nameAt + 8)..].Trim();

        foreach (string field in parts)
        {
            int eq = field.IndexOf('=');
            if (eq <= 0) continue;
            string key = field[..eq];
            string value = field[(eq + 1)..];

            if (key == "county") continue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;

            switch (key)
            {
                case "y": e.Year = n; break;
                case "decay": e.Decay = n; break;
                case "since": e.Since = n; break;
                case "ctl": e.Control = n; break;
                case "op": e.Opinion = n; break;
                case "dev": e.Development = n; break;
                case "c_contested": e.Contested = n; break;
                case "c_control": e.NoControl = n; break;
                case "c_opinion": e.Hated = n; break;
                case "c_plague": e.Plague = n; break;
                case "c_devdrop": e.Emptying = n; break;
                case "s_occupied": e.Occupied = n; break;
                case "s_siege": e.Besieged = n; break;
                case "s_looted": e.Looted = n; break;
                default: continue;   // A field added to the .yml and not here. Ignored on purpose.
            }
            any = true;
        }

        return any ? e : null;
    }
}
