using System.Text.RegularExpressions;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The set of identifiers a generated culture or faith is allowed to name, read out of the
/// installed game rather than hardcoded here.
///
/// The rule this file exists to enforce: **never invent an identifier that has to already exist.**
/// A culture may invent its own name, its own language and its own words, because we also emit
/// those. It may not invent an ethos, a tradition, a doctrine or a clothing set, because those are
/// declared by the base game and by DLC the player may or may not own. Hardcoding a list of
/// tradition keys works until someone runs the tool without Fate of Iberia and every generated
/// culture references a tradition that is not there.
///
/// So the pattern throughout is harvest-then-recombine: read what vanilla actually uses in each
/// slot, and choose from that. A missing key is skipped rather than emitted, which degrades to a
/// slightly less varied world instead of to a broken one.
/// </summary>
public sealed class VanillaVocabulary
{
    /// <summary>
    /// A complete visual identity lifted verbatim off one vanilla culture.
    ///
    /// Harvested as a unit rather than field by field, because the four gfx sets and the
    /// ethnicity weights have to agree with each other: mixing Norse buildings with Japanese
    /// clothing and West African ethnicities produces a culture that looks like a bug. One vanilla
    /// culture's look is known-good by construction, so we borrow whole looks and let a generated
    /// heritage wear one.
    /// </summary>
    /// 
    public static VanillaVocabulary? Current { get; private set; }
    public sealed record Look(
        string SourceCulture, string CoaGfx, string BuildingGfx, string ClothingGfx, string UnitGfx, string Ethnicities);
    public List<string> Ethos { get; } = [];
    public List<string> MartialCustoms { get; } = [];
    public List<string> HeadDeterminations { get; } = [];
    public List<string> Traditions { get; } = [];
    public List<Look> Looks { get; } = [];
    public List<string> FaithIcons { get; } = [];

    /// <summary>Temple model sets a generated religion may point its faiths at.</summary>
    public List<string> GraphicalFaiths { get; } = [];

    /// <summary>Traits vanilla religions treat as virtues and sins, kept apart so we do not make a
    /// religion that considers cravenness both.</summary>
    public List<string> Virtues { get; } = [];

    public List<string> Sins { get; } = [];

    /// <summary>
    /// Named colours vanilla gives its language pillars. Language colour is a named-colour
    /// reference rather than an RGB triple, so it has to be borrowed rather than invented.
    /// </summary>
    public List<string> LanguageColors { get; } = [];

    /// <summary>
    /// Innovations already discovered at the 867 start, and the share of vanilla cultures that
    /// have each one.
    ///
    /// Stored as frequencies rather than split into "core" and "optional" because the measured
    /// distribution has no such split in it: over the 133 vanilla culture histories the commonest
    /// innovation is held by 75% of them and the tenth-commonest by 34%, sloping the whole way
    /// down. Sampling each innovation at its own frequency reproduces both the mix and the count —
    /// about seven per culture, which is what vanilla cultures actually start with — and needs no
    /// threshold anyone has to justify.
    /// </summary>
    public Dictionary<string, double> InnovationFrequency { get; } = [];

    public sealed record InnovationDef(string Key, string Era, string Group);

    public Dictionary<string, InnovationDef> InnovationDefs { get; } = new(StringComparer.Ordinal);

    /// <summary>Stores (discoveryYear, innovationKey) per vanilla culture history.</summary>
    public List<List<(int Year, string Innovation)>> CultureHistories { get; } = [];

    /// <summary>Doctrine group key to the doctrines that satisfy it.</summary>
    public Dictionary<string, List<string>> DoctrineGroups { get; } = [];

    /// <summary>The three-pick tenet pool, which is its own doctrine group.</summary>
    public List<string> Tenets { get; } = [];

    /// <summary>
    /// The religion-level localization block of a vanilla pagan religion, as (tag, value) pairs.
    ///
    /// We do not know the full tag set ourselves and must not guess it: a religion missing
    /// HighGodName renders broken text wherever an event mentions the faith's god. Copying the tag
    /// list off a real religion means the generated ones carry exactly the tags this version of the
    /// game expects, including any added by a patch after this was written.
    /// </summary>
    public List<(string Tag, string Value)> ReligionLocTemplate { get; } = [];

    public static VanillaVocabulary Read(string gameDir)
    {
        var v = new VanillaVocabulary();

        v.ReadPillars(Path.Combine(gameDir, "common", "culture", "pillars"));
        v.ReadCultures(Path.Combine(gameDir, "common", "culture", "cultures"));
        v.ReadDoctrines(Path.Combine(gameDir, "common", "religion", "doctrine_group_types"));
        v.ReadReligions(Path.Combine(gameDir, "common", "religion", "religion_types"));
        v.ReadInnovationDefs(Path.Combine(gameDir, "common", "culture", "innovations"));
        v.ReadInnovations(Path.Combine(gameDir, "history", "cultures"));

        v.Ethos.Sort(StringComparer.Ordinal);
        v.MartialCustoms.Sort(StringComparer.Ordinal);
        v.HeadDeterminations.Sort(StringComparer.Ordinal);
        v.LanguageColors.Sort(StringComparer.Ordinal);

        int tribal = v.InnovationDefs.Values.Count(d => d.Era == "culture_era_tribal");
        int early = v.InnovationDefs.Values.Count(d => d.Era == "culture_era_early_medieval");
        int high = v.InnovationDefs.Values.Count(d => d.Era == "culture_era_high_medieval");
        int late = v.InnovationDefs.Values.Count(d => d.Era == "culture_era_late_medieval");

        Console.WriteLine($"  vocabulary: {v.Ethos.Count} ethos, {v.Traditions.Count} traditions, {v.Looks.Count} looks");
        Console.WriteLine($"  innovations harvested: {tribal} tribal, {early} early medieval, {high} high medieval, {late} late medieval");

        Current = v;
        return v;
    }

    /// <summary>Whether enough was harvested to generate against. A stub install fails this.</summary>
    public bool IsUsable =>
        Ethos.Count > 0 && MartialCustoms.Count > 0 && HeadDeterminations.Count > 0
        && Traditions.Count > 0 && Looks.Count > 0 && FaithIcons.Count > 0 && Tenets.Count > 0
        && (CultureHistories.Count > 0 || InnovationDefs.Count > 0);

    private void ReadPillars(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var (key, body) in TopLevelBlocks(File.ReadAllText(path)))
            {
                // Sorted by the declared type rather than by filename, because a pillar's file is
                // convention and its `type` is what the culture slot actually checks.
                var type = Regex.Match(body, @"\btype\s*=\s*(\w+)");
                if (!type.Success) continue;

                switch (type.Groups[1].Value)
                {
                    case "ethos": Ethos.Add(key); break;
                    case "martial_custom": MartialCustoms.Add(key); break;
                    case "head_determination": HeadDeterminations.Add(key); break;

                    case "language":
                        var color = Regex.Match(body, @"^\s*color\s*=\s*(\w+)\s*$",
                            RegexOptions.Multiline);
                        if (color.Success && !LanguageColors.Contains(color.Groups[1].Value))
                            LanguageColors.Add(color.Groups[1].Value);
                        break;
                }
            }
        }
    }

    private void ReadCultures(string dir)
    {
        if (!Directory.Exists(dir)) return;

        var traditions = new HashSet<string>(StringComparer.Ordinal);
        var looks = new HashSet<Look>();

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var (key, body) in TopLevelBlocks(File.ReadAllText(path)))
            {
                // Only the plain `traditions = { }` block. Traditions that reach a culture through
                // dlc_tradition are gated on a flag we cannot evaluate, so they are not safe to
                // assign unconditionally.
                string? traditionBlock = Block(body, "traditions");
                if (traditionBlock is not null)
                    foreach (Match m in Regex.Matches(traditionBlock, @"\btradition_\w+"))
                        traditions.Add(m.Value);

                string? coa = Line(body, "coa_gfx");
                string? building = Line(body, "building_gfx");
                string? clothing = Line(body, "clothing_gfx");
                string? unit = Line(body, "unit_gfx");
                string? ethnicities = Block(body, "ethnicities");

                if (coa is not null && building is not null && clothing is not null
                    && unit is not null && ethnicities is not null)
                    looks.Add(new Look(key, coa, building, clothing, unit, ethnicities.Trim()));
            }
        }

        Traditions.AddRange(traditions.OrderBy(t => t, StringComparer.Ordinal));
        Looks.AddRange(looks.OrderBy(l => l.ClothingGfx, StringComparer.Ordinal));
    }

    private void ReadDoctrines(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var (key, body) in TopLevelBlocks(File.ReadAllText(path)))
            {
                string? list = Block(body, "doctrine_types");
                if (list is null) continue;

                var members = new List<string>();
                foreach (string raw in list.Split('\n'))
                {
                    string line = raw;
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line[..hash];

                    line = line.Trim();
                    if (line.Length > 0 && Regex.IsMatch(line, @"^\w+$")) members.Add(line);
                }

                if (members.Count > 0) DoctrineGroups[key] = members;
            }
        }

        if (DoctrineGroups.TryGetValue("doctrine_core_tenets", out var tenets)) Tenets.AddRange(tenets);
    }

    private void ReadReligions(string dir)
    {
        if (!Directory.Exists(dir)) return;

        var icons = new HashSet<string>(StringComparer.Ordinal);
        var graphical = new HashSet<string>(StringComparer.Ordinal);
        var virtues = new HashSet<string>(StringComparer.Ordinal);
        var sins = new HashSet<string>(StringComparer.Ordinal);
        string? bestTemplate = null;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);

            foreach (Match m in Regex.Matches(text, @"^\s*icon\s*=\s*(\w+)", RegexOptions.Multiline))
                icons.Add(m.Groups[1].Value);

            foreach (Match m in Regex.Matches(text, @"\bgraphical_faith\s*=\s*(\w+)"))
                graphical.Add(m.Groups[1].Value);

            // Prefer a pagan religion's tag set: it is the archetype the generated ones follow, so
            // its tags are the ones they will actually have values for.
            foreach (var (_, body) in TopLevelBlocks(text))
            {
                string? traits = Block(body, "traits");
                if (traits is not null)
                {
                    Collect(Block(traits, "virtues"), virtues);
                    Collect(Block(traits, "sins"), sins);
                }

                string? loc = Block(body, "localization");
                if (loc is null) continue;

                bool pagan = Regex.IsMatch(body, @"\bfamily\s*=\s*rf_pagan\b");
                if (bestTemplate is null || pagan) bestTemplate = loc;
            }
        }

        FaithIcons.AddRange(icons.OrderBy(i => i, StringComparer.Ordinal));
        GraphicalFaiths.AddRange(graphical.OrderBy(g => g, StringComparer.Ordinal));

        // A trait vanilla lists on both sides is ambiguous for us, so it is dropped from both.
        Virtues.AddRange(virtues.Except(sins).OrderBy(t => t, StringComparer.Ordinal));
        Sins.AddRange(sins.Except(virtues).OrderBy(t => t, StringComparer.Ordinal));

        if (bestTemplate is null) return;
        foreach (Match m in Regex.Matches(bestTemplate, @"^\s*(\w+)\s*=\s*([^\r\n{]+|\{[^}]*\})",
                     RegexOptions.Multiline))
            ReligionLocTemplate.Add((m.Groups[1].Value, m.Groups[2].Value.Trim()));
    }

    private void ReadInnovationDefs(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories))
        {
            string filename = Path.GetFileName(path).ToLowerInvariant();

            string defaultEra = "culture_era_tribal";
            if (filename.Contains("late_medieval")) defaultEra = "culture_era_late_medieval";
            else if (filename.Contains("high_medieval")) defaultEra = "culture_era_high_medieval";
            else if (filename.Contains("early_medieval")) defaultEra = "culture_era_early_medieval";
            else if (filename.Contains("tribal")) defaultEra = "culture_era_tribal";

            string text = File.ReadAllText(path);
            foreach (var (key, body) in TopLevelBlocks(text))
            {
                if (key.StartsWith('@')) continue;

                // Skip innovations with hardcoded heritage/culture restrictions (e.g. Longboats, Mubarizun)
                // as generated cultures will fail the engine's potential triggers.
                if (body.Contains("potential =")) continue;

                var eraMatch = Regex.Match(body, @"\bculture_era\s*=\s*(\w+)");
                var groupMatch = Regex.Match(body, @"\bgroup\s*=\s*(\w+)");

                if (!groupMatch.Success && !eraMatch.Success && !filename.Contains("innovation"))
                    continue;

                string era = eraMatch.Success ? eraMatch.Groups[1].Value : defaultEra;
                string group = groupMatch.Success ? groupMatch.Groups[1].Value : "culture_group_civic";

                InnovationDefs[key] = new InnovationDef(key, era, group);
            }
        }
    }

    private void ReadInnovations(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            var cultureHistory = new List<(int Year, string Innovation)>();

            // Match dated blocks, e.g. "867.1.1 = { ... }" or "1066.9.15 = { ... }"
            var dateBlocks = Regex.Matches(text, @"(?:^|\s)(\d{3,4})\.\d+\.\d+\s*=\s*\{");
            for (int i = 0; i < dateBlocks.Count; i++)
            {
                int year = int.Parse(dateBlocks[i].Groups[1].Value);
                int start = dateBlocks[i].Index;
                int end = (i + 1 < dateBlocks.Count) ? dateBlocks[i + 1].Index : text.Length;
                string block = text[start..end];

                foreach (Match m in Regex.Matches(block, @"discover_innovation\s*=\s*([a-zA-Z0-9_]+)"))
                {
                    cultureHistory.Add((year, m.Groups[1].Value));
                }
            }

            // Fallback for undated history declarations
            if (dateBlocks.Count == 0)
            {
                foreach (Match m in Regex.Matches(text, @"discover_innovation\s*=\s*([a-zA-Z0-9_]+)"))
                {
                    cultureHistory.Add((867, m.Groups[1].Value));
                }
            }

            if (cultureHistory.Count > 0)
            {
                CultureHistories.Add(cultureHistory);
            }
        }
    }

    public (Dictionary<string, double> Frequencies, double AverageCount) GetFrequenciesAtYear(int targetYear)
    {
        if (CultureHistories.Count == 0) return ([], 0);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalDiscovered = 0;
        int validCultures = 0;

        foreach (var history in CultureHistories)
        {
            var discovered = history
                .Where(h => h.Year <= targetYear)
                .Select(h => h.Innovation)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (discovered.Count == 0) continue;

            validCultures++;
            totalDiscovered += discovered.Count;
            foreach (string inn in discovered)
            {
                counts[inn] = counts.GetValueOrDefault(inn) + 1;
            }
        }

        if (validCultures == 0) return ([], 0);

        var freqs = counts.ToDictionary(
            kv => kv.Key,
            kv => (double)kv.Value / validCultures,
            StringComparer.Ordinal);

        return (freqs, (double)totalDiscovered / validCultures);
    }

    /// <summary>Where the 867-and-earlier part of a culture history file stops.</summary>
    private static int IndexOfLaterDate(string text)
    {
        var later = Regex.Match(text, @"\b(8[7-9]\d|9\d\d|1[0-9]{3})\.\d+\.\d+\s*=\s*\{");
        return later.Success ? later.Index : text.Length;
    }

    /// <summary>
    /// Bare trait names out of a virtues or sins list, ignoring the `trait = { scale = 2 }` form
    /// whose weights are not ours to reuse.
    /// </summary>
    private static void Collect(string? list, HashSet<string> into)
    {
        if (list is null) return;

        // Strip the weighting syntax first, so `stubborn = { scale = 2 }` and `brave = 0.5` both
        // reduce to the bare trait and the leftovers are trait names and nothing else.
        string cleaned = Regex.Replace(list, @"=\s*\{[^}]*\}", " ");
        cleaned = Regex.Replace(cleaned, @"=\s*[\d.]+", " ");

        foreach (Match m in Regex.Matches(cleaned, @"[a-z][a-z0-9_]*")) into.Add(m.Value);
    }

    /// <summary>
    /// A whole `name = { ... }` block, matched by counting braces rather than by regex — Paradox
    /// blocks nest arbitrarily and a non-greedy `\{.*?\}` stops at the first inner close.
    /// </summary>
    private static string? Block(string text, string name)
    {
        var open = Regex.Match(text, $@"(^|\n)\s*{Regex.Escape(name)}\s*=\s*\{{");
        if (!open.Success) return null;

        int start = text.IndexOf('{', open.Index) + 1;
        int depth = 1;

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text[start..i];
        }

        return null;
    }

    /// <summary>A single-line `name = { a b c }` assignment, returned verbatim after the `=`.</summary>
    private static string? Line(string text, string name)
    {
        var m = Regex.Match(text, $@"(^|\n)\s*{Regex.Escape(name)}\s*=\s*(\{{[^}}\r\n]*\}}|\S+)");
        return m.Success ? m.Groups[2].Value.Trim() : null;
    }

    /// <summary>
    /// Every `key = { ... }` declared at column 0, with its body. Top-level position is what
    /// distinguishes a declaration from the many nested blocks that share its shape.
    /// </summary>
    private static IEnumerable<(string Key, string Body)> TopLevelBlocks(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;

            int equals = line.IndexOf('=');
            if (equals <= 0 || !line.Contains('{')) continue;

            string key = line[..equals].Trim().TrimStart('﻿');
            if (key.Length == 0 || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) continue;

            int depth = 0;
            int start = i;
            for (int j = i; j < lines.Length; j++)
            {
                string body = lines[j];
                int hash = body.IndexOf('#');
                if (hash >= 0) body = body[..hash];

                depth += body.Count(c => c == '{') - body.Count(c => c == '}');
                if (depth > 0) continue;

                yield return (key, string.Join('\n', lines[start..(j + 1)]));
                i = j;
                break;
            }
        }
    }
}
