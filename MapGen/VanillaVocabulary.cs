using System.Globalization;
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
    ///
    /// Filtered against <see cref="NamedColors"/> before use — see there for the two vanilla
    /// pillars whose colour is a dangling reference in the base game itself.
    /// </summary>
    public List<string> LanguageColors { get; } = [];

    /// <summary>
    /// Every colour <c>common/named_colors</c> actually declares.
    ///
    /// Needed because two of vanilla's own are not declared even though they look it.
    /// <c>culture_colors.txt</c> writes <c>khitan { 0.00 0.00 0.00 }</c> and
    /// <c>tungusic { 0.00 0.00 0.00 }</c> — **missing the equals sign** — so neither name is
    /// defined, while <c>language_khitan</c> and <c>language_tungusic</c> go on referencing them.
    /// A generated pillar that borrowed either inherited the dangling reference and ck3-tiger
    /// reported it, which is how this was found; a hardcoded <c>!= "tungusic"</c> in the culture
    /// writer had already papered over one of the two and missed its twin one line below it.
    ///
    /// Parsed strictly, requiring the <c>=</c>, precisely so a malformed entry is *not* collected.
    /// </summary>
    public HashSet<string> NamedColors { get; } = new(StringComparer.Ordinal);

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

    /// <param name="UnlocksMaa">Whether the innovation carries an <c>unlock_maa</c>.</param>
    /// <param name="Regional">Whether it is flagged <c>global_regional</c> — vanilla's own marker
    /// for an innovation only some peoples can ever reach. The line between the two kinds of
    /// men-at-arms gate: <c>innovation_quilted_armor</c> is <c>global_regular</c> and gates
    /// <c>armored_footmen</c>, which every ruler on any map is meant to have, while
    /// <c>innovation_elephantry</c> is <c>global_regional</c> and gates war elephants.</param>
    public sealed record InnovationDef(string Key, string Era, string Group,
        bool UnlocksMaa = false, bool Regional = false);

    public Dictionary<string, InnovationDef> InnovationDefs { get; } = new(StringComparer.Ordinal);

    /// <summary>Icon paths vanilla's own innovations are drawn with, in declaration order. A
    /// generated innovation borrows one rather than shipping art.</summary>
    public List<string> InnovationIcons { get; } = [];

    /// <summary>Stores (discoveryYear, innovationKey) per vanilla culture history.</summary>
    public List<List<(int Year, string Innovation)>> CultureHistories { get; } = [];

    /// <summary>Doctrine group key to the doctrines that satisfy it.</summary>
    public Dictionary<string, List<string>> DoctrineGroups { get; } = [];

    /// <summary>The three-pick tenet pool, which is its own doctrine group.</summary>
    public List<string> Tenets { get; } = [];

    /// <summary>
    /// Which doctrines CK3 refuses to let one faith hold at once, read off the <c>can_pick</c>
    /// triggers its faith-creation screen enforces — human sacrifice beside pacifism, an
    /// anointment rite beside no head of faith. Symmetric, because vanilla states a pair from one
    /// side only about half the time and the game blocks the combination either way round.
    ///
    /// A faith written straight into script is never checked against these, so a generated one can
    /// carry a contradiction the game itself would not have let a player pick.
    /// </summary>
    public Dictionary<string, HashSet<string>> IncompatibleDoctrines { get; } = [];

    /// <summary>Whether <paramref name="doctrine"/> may sit beside every one of <paramref name="with"/>.</summary>
    public bool Compatible(string doctrine, IEnumerable<string> with)
        => !IncompatibleDoctrines.TryGetValue(doctrine, out var clashes) || !with.Any(clashes.Contains);

    /// <summary>Everything that may not sit beside any of <paramref name="held"/>, held included.</summary>
    public HashSet<string> IncompatibleWithAll(IEnumerable<string> held)
    {
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (string doctrine in held)
        {
            blocked.Add(doctrine);
            if (IncompatibleDoctrines.TryGetValue(doctrine, out var clashes)) blocked.UnionWith(clashes);
        }

        return blocked;
    }

    /// <summary>
    /// The religion-level localization block of a vanilla pagan religion, as (tag, value) pairs.
    ///
    /// We do not know the full tag set ourselves and must not guess it: a religion missing
    /// HighGodName renders broken text wherever an event mentions the faith's god. Copying the tag
    /// list off a real religion means the generated ones carry exactly the tags this version of the
    /// game expects, including any added by a patch after this was written.
    /// </summary>
    public List<(string Tag, string Value)> ReligionLocTemplate { get; } = [];

    /// <summary>
    /// What one men-at-arms archetype's numbers actually look like in this install.
    ///
    /// The envelope a generated regiment is built inside. Damage and toughness are the two stats
    /// CK3's own quality score is dominated by, and they are also the two that differ by an order
    /// of magnitude between archetypes — heavy cavalry averages 102 damage against skirmishers'
    /// 16 — so "a strong unit" cannot be a number written here. It has to be a number read off
    /// the archetype the unit belongs to, which is what this is.
    /// </summary>
    public sealed class MaaArchetype
    {
        public required string Key { get; init; }
        public int Count { get; set; }

        /// <summary>Mean over every vanilla regiment of this archetype.</summary>
        public double Damage { get; set; }

        public double Toughness { get; set; }
        public double Pursuit { get; set; }
        public double Screen { get; set; }

        /// <summary>The strongest vanilla regiment of this archetype, stat by stat. A generated
        /// one is clamped just above these rather than to an invented ceiling.</summary>
        public int MaxDamage { get; set; }

        public int MaxToughness { get; set; }
        public int MaxPursuit { get; set; }
        public int MaxScreen { get; set; }

        /// <summary>Mean siege contribution. Only siege engines carry a meaningful one.</summary>
        public double SiegeValue { get; set; }

        /// <summary>The commonest sub-regiment size — 100 for nearly everything, 10 for siege
        /// engines, 25 for elephants. Never invented: an off-size regiment reads as a bug.</summary>
        public int Stack { get; set; } = 100;

        /// <summary>
        /// What this archetype counters, and by how much, as vanilla's own regiments of it agree.
        ///
        /// Harvested rather than written down because the counter web is the one part of the
        /// combat model a generated regiment absolutely may not improvise. It is a closed system —
        /// pikes beat horse, horse beats bow, bow beats skirmisher — and a unit that counters the
        /// wrong thing does not read as exotic, it reads as broken, and it silently unbalances
        /// every battle the AI fights with it.
        /// </summary>
        public Dictionary<string, double> Counters { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Ground this archetype is *worse* on, as vanilla's own regiments of it agree: CK3 terrain
        /// id to the stats it loses there, always negative.
        ///
        /// The half of vanilla's balance that is easiest to miss and most expensive to. A war
        /// elephant is 250 damage and a mountain takes 100 of them away again; horse archers give
        /// up half their damage in a swamp. Read the base stats alone and an archetype's power
        /// looks like a flat number, when vanilla has actually spent it against a map. A generated
        /// regiment built from the means and given only the bonuses its doctrine earned is
        /// strictly better than the vanilla unit it was measured against, everywhere that unit is
        /// meant to be weak.
        ///
        /// Only terrains more than half the archetype declares are kept, so this is the
        /// archetype's own weakness rather than one regiment's flavour.
        /// </summary>
        public Dictionary<string, Dictionary<string, int>> TerrainPenalty { get; }
            = new(StringComparer.Ordinal);

        /// <summary>What this archetype loses in a normal winter, or empty for one that does not
        /// mind. Kept apart from <see cref="WinterHarshPenalty"/> because vanilla's penalties
        /// roughly double between the two while its bonuses are written identically.</summary>
        public Dictionary<string, int> WinterNormalPenalty { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> WinterHarshPenalty { get; } = new(StringComparer.Ordinal);

        /// <summary>Vanilla's base recruitment price for the archetype, in gold, from
        /// <c>common/script_values/00_men_at_arms_values.txt</c>. Zero when the install names no
        /// such value, in which case the price is fitted from power instead.</summary>
        public double BuyCost { get; set; }

        public double LowMaintenance { get; set; }

        /// <summary>
        /// The quality number CK3 itself would put on this archetype's average regiment, on the
        /// same weighting the generated ones are scored with. The unit of "as strong as vanilla".
        /// </summary>
        public double Power => Retinues.Power(Damage, Toughness, Pursuit, Screen);
    }

    /// <summary>Every men-at-arms archetype the install declares a regiment for, by <c>type</c>.</summary>
    public Dictionary<string, MaaArchetype> MaaArchetypes { get; } = new(StringComparer.Ordinal);

    /// <summary>Regiment icons on disk under <c>gfx/interface/icons/regimenttypes</c>, without the
    /// extension. Referenced rather than copied — the generated mod ships no art of its own here.</summary>
    public List<string> MaaIcons { get; } = [];

    /// <summary>Unit-card illustrations vanilla regiments actually reference, by archetype. A
    /// regiment with none renders a blank card, so every generated one is given one.</summary>
    public Dictionary<string, List<string>> MaaIllustrations { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Traditions that unlock a vanilla cultural regiment, via an <c>unlock_maa_*</c> parameter.
    ///
    /// Harvested so <see cref="Cultures"/> can keep them off generated cultures. A people invented
    /// for this map fielding Danish Huscarls is the one place where borrowing vanilla's vocabulary
    /// shows through as a borrowing, and it is avoidable: the generated regiments replace exactly
    /// what these traditions would have granted.
    /// </summary>
    public HashSet<string> TraditionsUnlockingMaa { get; } = new(StringComparer.Ordinal);

    /// <summary>Innovations some regiment's own <c>can_recruit</c> asks for by name. The second way
    /// an innovation gates a unit, and the one <c>unlock_maa</c> does not cover — war elephants are
    /// gated this way and nothing in the innovation says so.</summary>
    public HashSet<string> InnovationsGatingMaa { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether holding this innovation would put one of vanilla's *named* regiments in a generated
    /// culture's hands.
    ///
    /// Two routes, and the second needs the regional flag to stay off the first. Every innovation
    /// carrying an <c>unlock_maa</c> is one. So is any innovation a regiment's <c>can_recruit</c>
    /// names — but only the regional ones, because the generic roster is gated exactly the same
    /// way (<c>armored_footmen</c> asks for <c>innovation_quilted_armor</c>) and that roster is the
    /// floor every ruler on any map is meant to recruit from.
    /// </summary>
    public bool GrantsVanillaRegiment(InnovationDef def)
        => def.UnlocksMaa || (def.Regional && InnovationsGatingMaa.Contains(def.Key));

    /// <summary>What vanilla multiplies low maintenance by while a regiment is raised.</summary>
    public double HighMaintenanceMultiplier { get; private set; } = 3.0;

    public static VanillaVocabulary Read(string gameDir)
    {
        var v = new VanillaVocabulary();

        // Before the pillars, which are filtered against it.
        v.ReadNamedColors(Path.Combine(gameDir, "common", "named_colors"));

        v.ReadPillars(Path.Combine(gameDir, "common", "culture", "pillars"));
        v.ReadCultures(Path.Combine(gameDir, "common", "culture", "cultures"));
        v.ReadDoctrines(Path.Combine(gameDir, "common", "religion", "doctrine_group_types"));
        v.ReadDoctrineConflicts(Path.Combine(gameDir, "common", "religion", "doctrine_types"));
        v.ReadReligions(Path.Combine(gameDir, "common", "religion", "religion_types"));
        v.ReadInnovationDefs(Path.Combine(gameDir, "common", "culture", "innovations"));
        v.ReadInnovations(Path.Combine(gameDir, "history", "cultures"));
        v.ReadMenAtArms(gameDir);

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
        Console.WriteLine($"  men-at-arms harvested: {v.MaaArchetypes.Values.Sum(a => a.Count)} regiments across " +
                          $"{v.MaaArchetypes.Count} archetypes, {v.MaaIcons.Count} icons");

        Current = v;
        return v;
    }

    /// <summary>Whether enough was harvested to generate against. A stub install fails this.</summary>
    public bool IsUsable =>
        Ethos.Count > 0 && MartialCustoms.Count > 0 && HeadDeterminations.Count > 0
        && Traditions.Count > 0 && Looks.Count > 0 && FaithIcons.Count > 0 && Tenets.Count > 0
        && (CultureHistories.Count > 0 || InnovationDefs.Count > 0);

    /// <summary>
    /// The declared named colours, read out of the <c>colors</c> block each file wraps them in.
    ///
    /// The <c>=</c> is required by the pattern and that is the entire point of the method — see
    /// <see cref="NamedColors"/>. Four value forms are in play across the two vanilla files:
    /// a bare brace triple, <c>hsv {</c>, <c>hsv{</c> and <c>hsv360 {</c>.
    /// </summary>
    private void ReadNamedColors(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            string? block = Block(File.ReadAllText(path), "colors");
            if (block is null) continue;

            foreach (Match m in Regex.Matches(block,
                         @"^\s*(\w+)\s*=\s*(?:hsv360|hsv|rgb)?\s*\{", RegexOptions.Multiline))
                NamedColors.Add(m.Groups[1].Value);
        }
    }

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

                        // Dropped rather than carried when the name is not actually declared. A
                        // pillar written without a colour is legal and only loses its shading in
                        // the language map mode, which is a far better outcome than propagating
                        // vanilla's own dangling reference into every generated world.
                        if (color.Success
                            && NamedColors.Contains(color.Groups[1].Value)
                            && !LanguageColors.Contains(color.Groups[1].Value))
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

    /// <summary>
    /// Matches, in priority order: a comment, a `doctrine:x` reference, a `name = {` block opener,
    /// a bare brace. Comments come first so a doctrine named in one is not read as a reference, and
    /// the reference beats the block opener so `doctrine:x = {` is read as a reference and not as a
    /// block called "doctrine:x".
    /// </summary>
    private const string ConflictToken =
        @"#[^\n]*|doctrine:(?<ref>\w+)|(?<block>[A-Za-z_][\w.]*)\s*=\s*\{|\{|\}";

    /// <summary>
    /// The <c>can_pick</c> triggers, which are where CK3 keeps its doctrine incompatibilities.
    ///
    /// A `doctrine:x = { is_in_list = selected_doctrines }` reference only means "conflicts with"
    /// when an odd number of NOT/NOR/NAND encloses it. Read positively the identical reference is a
    /// *prerequisite* — tenet_divine_marriage requires doctrine_consanguinity_unrestricted, and
    /// tenet_fp3_fedayeen requires one of six militant tenets — so the polarity is not optional
    /// detail. Taking every reference as a conflict would forbid the pairs the game demands.
    ///
    /// A reference nested inside anything other than a negation, an OR or a custom_description is
    /// dropped rather than guessed at, because it is a conditional statement about something else.
    /// Every one in the install belongs to tenet_rite, whose can_pick is entirely about what the
    /// *head of faith's* faith believes and constrains this faith's own doctrines not at all.
    ///
    /// 51 doctrines and 65 pairs on a full install; a stub or a DLC-less one just harvests fewer.
    /// </summary>
    private void ReadDoctrineConflicts(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            foreach (var (key, body) in TopLevelBlocks(File.ReadAllText(path)))
            {
                string? pick = Block(body, "can_pick");
                if (pick is null) continue;

                var open = new List<string>();

                foreach (Match m in Regex.Matches(pick, ConflictToken))
                {
                    if (m.Groups["block"].Success) { open.Add(m.Groups["block"].Value); continue; }
                    if (m.Value == "{") { open.Add("{"); continue; }
                    if (m.Value == "}") { if (open.Count > 0) open.RemoveAt(open.Count - 1); continue; }
                    if (!m.Groups["ref"].Success) continue;   // a comment

                    string other = m.Groups["ref"].Value;
                    bool negated = open.Count(Negates) % 2 == 1;
                    bool conditional = open.Any(o => !Negates(o) && !Transparent(o));

                    if (!negated || conditional || string.Equals(other, key, StringComparison.Ordinal))
                        continue;

                    Link(key, other);
                    Link(other, key);
                }
            }
        }

        void Link(string a, string b)
        {
            if (!IncompatibleDoctrines.TryGetValue(a, out var set))
                IncompatibleDoctrines[a] = set = new HashSet<string>(StringComparer.Ordinal);

            set.Add(b);
        }

        static bool Negates(string block) => block is "NOT" or "NOR" or "NAND";

        // An OR only widens whatever is being negated around it, and a custom_description only
        // names the tooltip the refusal shows. Neither changes what the reference asserts.
        static bool Transparent(string block) => block is "OR" or "custom_description";
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

        // The winner is whichever pagan religion sorts last, which as of now is Zunism. That is
        // fine for the tag set and only the tag set: every *value* here is that one religion's own
        // localisation key, and copying one into a generated religion gives all of them the same
        // vanilla god. Faiths.BuildLocalization is what decides which values may pass through.
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

            // The `@name = "gfx/..."` defines at the head of each file. A generated innovation has
            // to point at art that exists, and these are every icon vanilla's own innovations use.
            foreach (Match m in Regex.Matches(text, "^@\\w+\\s*=\\s*\"(gfx/[^\"]+)\"", RegexOptions.Multiline))
                if (!InnovationIcons.Contains(m.Groups[1].Value)) InnovationIcons.Add(m.Groups[1].Value);

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

                bool unlocksMaa = Regex.IsMatch(body, @"^[ \t]?unlock_maa\s*=", RegexOptions.Multiline);
                bool regional = Regex.IsMatch(body, @"^[ \t]?flag\s*=\s*global_regional\s*$",
                    RegexOptions.Multiline);

                InnovationDefs[key] = new InnovationDef(key, era, group, unlocksMaa, regional);
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
    /// The men-at-arms envelope: what each archetype's regiments are worth, what vanilla charges
    /// for them, and what art they are drawn with — plus the traditions that hand a culture one.
    ///
    /// Harvested rather than tabulated for the usual reason, and for one more. The usual one is
    /// DLC: a stat table written here would describe whichever DLC the author owned, and the
    /// archetypes themselves arrive with them — elephants with one pack, camels with another. The
    /// extra one is *patching*. Paradox rebalances these numbers, and a generated regiment whose
    /// stats were tuned against 1.19 would quietly drift out of line with the game around it over
    /// a couple of updates. Read at run time, the generated roster is rebalanced by the same patch
    /// that rebalances vanilla's.
    /// </summary>
    private void ReadMenAtArms(string gameDir)
    {
        var samples = new Dictionary<string, List<(int Damage, int Toughness, int Pursuit,
            int Screen, double Siege, int Stack)>>(StringComparer.Ordinal);

        var counters = new Dictionary<string, Dictionary<string, List<double>>>(StringComparer.Ordinal);
        var prices = new Dictionary<string, List<(string Buy, string Maintenance)>>(StringComparer.Ordinal);

        // archetype -> slot ("forest", "normal_winter", ...) -> stat -> one value per regiment that
        // declared it. Kept as raw lists so the "more than half the archetype agrees" filter below
        // can be applied per slot, which is the only thing separating an archetype's own weakness
        // from a single regiment's flavour.
        var modifiers =
            new Dictionary<string, Dictionary<string, Dictionary<string, List<int>>>>(StringComparer.Ordinal);

        var values = ReadScriptValues(Path.Combine(gameDir, "common", "script_values"));

        if (values.TryGetValue("high_maint_mult", out double mult) && mult > 0)
            HighMaintenanceMultiplier = mult;

        string dir = Path.Combine(gameDir, "common", "men_at_arms_types");

        if (Directory.Exists(dir))
        {
            foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
            {
                foreach (var (_, body) in TopLevelBlocks(File.ReadAllText(path)))
                {
                    // At most one character of indent, because `type` is also the name of a field
                    // inside an accolade's `accolade_attribute_level` block three levels down, and
                    // an unanchored match reads `archer_attribute` as though it were an archetype.
                    // Vanilla writes a regiment's own fields at exactly one tab, without exception.
                    var type = Regex.Match(body, @"^[ \t]?type\s*=\s*(\w+)", RegexOptions.Multiline);
                    if (!type.Success) continue;

                    // Which innovations this regiment's own gate names, before any of the envelope
                    // exclusions below — the question is what an innovation puts in reach, which
                    // does not care whether the regiment belongs in an average.
                    if (Block(body, "can_recruit") is { } gate)
                        foreach (Match m in Regex.Matches(gate, @"\bhas_innovation\s*=\s*(\w+)"))
                            InnovationsGatingMaa.Add(m.Groups[1].Value);

                    // One-off regiments are not part of the envelope.
                    //
                    // Accolade troops, holy-order guards and the EP3 specials are capped at a
                    // single regiment or cannot be recruited at all, and they are balanced as
                    // rewards rather than as purchases — `accolade_maa_elephantiers` is 400 damage
                    // against the 250 of the strongest thing a ruler can actually buy. Averaged in,
                    // eleven of them dragged every archetype's mean and ceiling upwards, and the
                    // first generated elephant regiment came out at 317 damage as a result.
                    //
                    // Detected by the two fields that say so rather than by filename, so a future
                    // pack's specials are excluded on the day it ships.
                    if (Regex.IsMatch(body, @"^[ \t]?max_regiments\s*=\s*[0-9]", RegexOptions.Multiline)
                        || Regex.IsMatch(body, @"^[ \t]?special_recruit_only\s*=\s*yes", RegexOptions.Multiline))
                        continue;

                    string archetype = type.Groups[1].Value;

                    int Stat(string name)
                    {
                        var m = Regex.Match(body, $@"^[ \t]?{name}\s*=\s*(-?\d+)", RegexOptions.Multiline);
                        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    }

                    var siege = Regex.Match(body, @"^[ \t]?siege_value\s*=\s*([\d.]+)", RegexOptions.Multiline);
                    int stack = Stat("stack");

                    samples.TryAdd(archetype, []);
                    samples[archetype].Add((Stat("damage"), Stat("toughness"), Stat("pursuit"),
                        Stat("screen"),
                        siege.Success ? double.Parse(siege.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                        stack > 0 ? stack : 100));

                    // Every `reference` in the block, including the triggered variants. The writer
                    // takes whichever an archetype uses most often, which is the untriggered
                    // fallback — the one illustration guaranteed to render for any culture.
                    foreach (Match m in Regex.Matches(body, @"\breference\s*=\s*(\w+)"))
                    {
                        MaaIllustrations.TryAdd(archetype, []);
                        MaaIllustrations[archetype].Add(m.Groups[1].Value);
                    }

                    // What vanilla charges for this exact regiment. Nearly always a named script
                    // value rather than a literal — `gold = huscarls_recruitment_cost` — which is
                    // why the values file is read first.
                    var buy = Regex.Match(body, @"buy_cost\s*=\s*\{[^}]*?gold\s*=\s*([\w.]+)");
                    var maint = Regex.Match(body, @"low_maintenance_cost\s*=\s*\{[^}]*?gold\s*=\s*([\w.]+)");

                    if (buy.Success && maint.Success)
                    {
                        prices.TryAdd(archetype, []);
                        prices[archetype].Add((buy.Groups[1].Value, maint.Groups[1].Value));
                    }

                    // The terrain and winter tables, flattened into one slot map. Both are the same
                    // shape — `slot = { stat = n ... }` — and both are wanted for the same reason.
                    foreach (string table in new[] { "terrain_bonus", "winter_bonus" })
                    {
                        string? block = Block(body, table);
                        if (block is null) continue;

                        foreach (Match slot in Regex.Matches(block, @"(\w+)\s*=\s*\{([^}]*)\}"))
                        {
                            modifiers.TryAdd(archetype,
                                new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.Ordinal));

                            var slots = modifiers[archetype];
                            slots.TryAdd(slot.Groups[1].Value,
                                new Dictionary<string, List<int>>(StringComparer.Ordinal));

                            var stats = slots[slot.Groups[1].Value];

                            foreach (Match stat in Regex.Matches(slot.Groups[2].Value, @"(\w+)\s*=\s*(-?\d+)"))
                            {
                                stats.TryAdd(stat.Groups[1].Value, []);
                                stats[stat.Groups[1].Value].Add(
                                    int.Parse(stat.Groups[2].Value, CultureInfo.InvariantCulture));
                            }
                        }
                    }

                    string? counterBlock = Block(body, "counters");
                    if (counterBlock is null) continue;

                    counters.TryAdd(archetype, new Dictionary<string, List<double>>(StringComparer.Ordinal));
                    foreach (Match m in Regex.Matches(counterBlock, @"(\w+)\s*=\s*([\d.]+)"))
                    {
                        var against = counters[archetype];
                        against.TryAdd(m.Groups[1].Value, []);
                        against[m.Groups[1].Value].Add(
                            double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        foreach (var (archetype, rows) in samples)
        {
            var entry = MaaArchetypes[archetype] = new MaaArchetype
            {
                Key = archetype,
                Count = rows.Count,
                Damage = rows.Average(r => r.Damage),
                Toughness = rows.Average(r => r.Toughness),
                Pursuit = rows.Average(r => r.Pursuit),
                Screen = rows.Average(r => r.Screen),
                MaxDamage = rows.Max(r => r.Damage),
                MaxToughness = rows.Max(r => r.Toughness),
                MaxPursuit = rows.Max(r => r.Pursuit),
                MaxScreen = rows.Max(r => r.Screen),
                // Averaged over the regiments that actually declare one, and only kept when most
                // of the archetype does. Averaged over all of them instead, archers came out at
                // 0.01 — a tenth of the smallest number vanilla ever writes, and visible in the
                // unit card as a siege contribution nobody intended the archetype to have.
                SiegeValue = rows.Count(r => r.Siege > 0) * 2 > rows.Count
                    ? rows.Where(r => r.Siege > 0).Average(r => r.Siege)
                    : 0,
                Stack = rows.GroupBy(r => r.Stack)
                            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key,
            };

            // The terrain and winter penalties, on a stricter bar than the counters below: more
            // than half the archetype has to declare the slot at all, and the mean has to come out
            // negative. Bonuses are dropped on the floor here — what ground a generated regiment is
            // *good* on is its doctrine's business, and copying vanilla's would hand a shield-wall
            // the steppe bonus its archetype's horse archers earned.
            if (modifiers.TryGetValue(archetype, out var slots))
                foreach (var (slot, stats) in slots)
                {
                    var into = slot switch
                    {
                        "normal_winter" => entry.WinterNormalPenalty,
                        "harsh_winter" => entry.WinterHarshPenalty,
                        _ => entry.TerrainPenalty.TryGetValue(slot, out var existing)
                            ? existing
                            : entry.TerrainPenalty[slot] = new Dictionary<string, int>(StringComparer.Ordinal),
                    };

                    foreach (var (stat, samplesOfStat) in stats)
                    {
                        if (samplesOfStat.Count * 2 <= rows.Count) continue;

                        int mean = (int)Math.Round(samplesOfStat.Average());
                        if (mean < 0) into[stat] = mean;
                    }

                    // A terrain every regiment declares but only for a bonus leaves an empty slot.
                    if (into.Count == 0 && slot is not ("normal_winter" or "harsh_winter"))
                        entry.TerrainPenalty.Remove(slot);
                }

            if (!counters.TryGetValue(archetype, out var against)) continue;

            // Only what a third of the archetype agrees on. One outlier regiment countering
            // something odd is a designed exception, not the archetype's place in the web, and
            // copying it onto every generated unit of that type would multiply the exception.
            foreach (var (target, weights) in against)
                if (weights.Count * 3 >= rows.Count)
                    entry.Counters[target] = Math.Round(weights.Average() * 2) / 2;
        }

        ApplyMaaCosts(prices, values);
        ReadMaaTraditions(Path.Combine(gameDir, "common", "culture", "traditions"));

        // A regiment's `icon` is not only the small icon. CK3 resolves it against the illustration
        // paths as well, so a name present in `icons/regimenttypes` but missing from
        // `illustrations/men_at_arms_big` and `_small` is a broken unit card — armenian_archers,
        // heavy_cavalry_western, hippotoxotai and kheshig are all in that state in 1.19. Only the
        // intersection of the three folders is safe to point at.
        MaaIcons.AddRange(
            Names(Path.Combine(gameDir, "gfx", "interface", "icons", "regimenttypes"))
                .Intersect(Names(Path.Combine(gameDir, "gfx", "interface", "illustrations", "men_at_arms_big")),
                           StringComparer.Ordinal)
                .Intersect(Names(Path.Combine(gameDir, "gfx", "interface", "illustrations", "men_at_arms_small")),
                           StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal));

        static IEnumerable<string> Names(string dir)
            => !Directory.Exists(dir)
                ? []
                : Directory.GetFiles(dir, "*.dds")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => n is not null && !n.StartsWith('_') && !n.StartsWith("unit_stat_"))
                    .Select(n => n!);
    }

    /// <summary>
    /// What every regiment of an archetype costs on average, from vanilla's own price list.
    ///
    /// The prices are worth chasing through the indirection rather than approximating, because
    /// vanilla does not price purely on power and the places it departs are exactly the ones a
    /// generated regiment would get wrong. War elephants are the case that proved it: fitting a
    /// gold-per-point line across the priced archetypes and extrapolating gave an elephant
    /// regiment 2.4x vanilla's upkeep, because vanilla deliberately charges elephants far less
    /// maintenance than their damage would suggest. Reading the number vanilla wrote needs no
    /// theory about why it wrote it.
    ///
    /// The fitted line survives only as the fallback for an archetype whose prices do not resolve
    /// at all — a modded install, or a future file layout.
    /// </summary>
    private void ApplyMaaCosts(Dictionary<string, List<(string Buy, string Maintenance)>> prices,
        Dictionary<string, double> values)
    {
        foreach (var archetype in MaaArchetypes.Values)
        {
            if (!prices.TryGetValue(archetype.Key, out var rows)) continue;

            var resolved = rows
                .Select(r => (Buy: Resolve(r.Buy), Maintenance: Resolve(r.Maintenance)))
                .Where(r => r.Buy > 0)
                .ToList();

            if (resolved.Count == 0) continue;

            archetype.BuyCost = resolved.Average(r => r.Buy);
            archetype.LowMaintenance = resolved.Average(r => r.Maintenance);
        }

        // Archetypes whose prices did not resolve are put on the same gold-per-point line as the
        // ones that did, so they are at least priced against this install rather than against a
        // number written here.
        var priced = MaaArchetypes.Values.Where(a => a.BuyCost > 0 && a.Power > 0).ToList();
        if (priced.Count == 0) return;

        double goldPerPower = priced.Sum(a => a.BuyCost) / priced.Sum(a => a.Power);
        double maintPerPower = priced.Sum(a => a.LowMaintenance) / priced.Sum(a => a.Power);

        foreach (var archetype in MaaArchetypes.Values)
        {
            if (archetype.BuyCost <= 0) archetype.BuyCost = archetype.Power * goldPerPower;
            if (archetype.LowMaintenance <= 0) archetype.LowMaintenance = archetype.Power * maintPerPower;
        }

        double Resolve(string token)
            => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal)
                ? literal
                : values.GetValueOrDefault(token);
    }

    /// <summary>
    /// Every named number under <c>common/script_values</c>, with the arithmetic resolved.
    ///
    /// Two layers: <c>@name = 90</c> defines, and the script values that reference them through
    /// Paradox's inline arithmetic — <c>huscarls_recruitment_cost = @[heavy_infantry_recruitment_cost
    /// * 1.28]</c>. The expression grammar in play across all 274 of them is a chain of names and
    /// numbers joined by <c>*</c>, <c>+</c> and <c>-</c>, evaluated left to right with no
    /// precedence and no parentheses, which is what this evaluates. Anything outside that shape
    /// resolves to nothing and its caller falls back.
    /// </summary>
    private static Dictionary<string, double> ReadScriptValues(string dir)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!Directory.Exists(dir)) return values;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);

            foreach (Match m in Regex.Matches(text, @"^@(\w+)\s*=\s*([\d.]+)\s*$", RegexOptions.Multiline))
                values[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

            foreach (Match m in Regex.Matches(text, @"^(\w+)\s*=\s*([\d.]+)\s*$", RegexOptions.Multiline))
                values[m.Groups[1].Value] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

            foreach (Match m in Regex.Matches(text, @"^(\w+)\s*=\s*@\[([^\]]+)\]", RegexOptions.Multiline))
                if (Evaluate(m.Groups[2].Value, values) is { } result)
                    values[m.Groups[1].Value] = result;
        }

        return values;
    }

    private static double? Evaluate(string expression, Dictionary<string, double> values)
    {
        var tokens = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length % 2 == 0) return null;

        double? Term(string token)
            => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double literal)
                ? literal
                : values.TryGetValue(token, out double named) ? named : null;

        if (Term(tokens[0]) is not { } total) return null;

        for (int i = 1; i + 1 < tokens.Length; i += 2)
        {
            if (Term(tokens[i + 1]) is not { } operand) return null;

            total = tokens[i] switch
            {
                "*" => total * operand,
                "+" => total + operand,
                "-" => total - operand,
                "/" => operand == 0 ? total : total / operand,
                _ => double.NaN,
            };

            if (double.IsNaN(total)) return null;
        }

        return total;
    }

    /// <summary>
    /// Traditions whose <c>parameters</c> block hands out an <c>unlock_maa_*</c> — the vanilla
    /// route from a culture to a named vanilla regiment, and the one a generated culture must not
    /// take. See <see cref="TraditionsUnlockingMaa"/>.
    /// </summary>
    private void ReadMaaTraditions(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (string path in Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal))
            foreach (var (key, body) in TopLevelBlocks(File.ReadAllText(path)))
            {
                string? parameters = Block(body, "parameters");
                if (parameters is not null && parameters.Contains("unlock_maa_", StringComparison.Ordinal))
                    TraditionsUnlockingMaa.Add(key);
            }
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
