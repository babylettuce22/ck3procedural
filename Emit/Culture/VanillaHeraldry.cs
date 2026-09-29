using System.Globalization;
using Ck3MapGen.Core;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Who a coat of arms is being rolled for, as vanilla's heraldry triggers see them: the culture's
/// <c>coa_gfx</c> list, and the faith's religion, family, doctrines and icon. There is no title and
/// no character — dynasty and house arms are rolled for the family, not for anything it holds.
/// </summary>
public sealed record HeraldryScope(
    IReadOnlySet<string> CoaGfx,
    string? ReligionKey,
    string? ReligionFamily,
    IReadOnlySet<string> Doctrines,
    string? FaithIcon)
{
    public static readonly HeraldryScope None = new(new HashSet<string>(), null, null, new HashSet<string>(), null);

    /// <summary>A culture's <c>coa_gfx</c> as written, <c>{ a b c }</c> or a bare key, as a set.</summary>
    public static IReadOnlySet<string> ParseGfx(string? coaGfx)
        => (coaGfx ?? "").Split([' ', '\t', '{', '}'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Vanilla's own random heraldry, run here so its result can be written down.
///
/// CK3 rolls arms for anything without them from <c>common/coat_of_arms</c>: a weighted list of
/// templates (<c>template_lists/coa_templates.txt</c>, list <c>all</c>), each laying out a field
/// and its charges with tinctures and textures drawn from further weighted lists — colours, field
/// patterns, colored emblems — that grow extra entries when a trigger on the culture or faith holds.
/// That is where the metal-on-colour rule, the regional charges (Iberian bordures, Norse knots,
/// kamon, Byzantine rondels) and all the tuning of weights live.
///
/// The engine's roll cannot be used directly for two reasons, which is why this exists. The bookmark
/// and challenge screens read arms before anything at game start runs, so an unrolled house shows a
/// blank shield there; and a cadet has to wear its parent's arms with a difference, which only a
/// static definition can say — the engine can copy arms whole or roll new ones, never modify them.
///
/// Everything is read from the installed game, so a patch that retunes vanilla's heraldry retunes
/// ours. What the engine knows and this does not — the character, the title, the dynasty's own
/// name — evaluates false, which is also the truth for a generated family.
///
/// <para><b>special_selection is additive.</b> Its entries join the list when its trigger holds
/// rather than replacing it. The engine does not document this; the weights prove it — Japanese
/// kamon templates are listed at 360-1260 against the base templates' 3, magnitudes that only mean
/// anything if the two pools are drawn from together.</para>
/// </summary>
public sealed class VanillaHeraldry
{
    private readonly Dictionary<string, ScriptNode> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptNode> _templateLists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptNode> _colorLists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptNode> _patternLists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptNode> _emblemLists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ScriptNode> _scriptedTriggers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emblemFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _patternFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every colored emblem and field pattern on disk, for the inspectors' dropdowns.</summary>
    public IReadOnlyList<string> Emblems { get; private set; } = [];
    public IReadOnlyList<string> Patterns { get; private set; } = [];

    /// <summary>
    /// Every tincture vanilla's metal and colour lists can yield, for any culture — zero-weight and
    /// culture-gated entries included (purple is weighted 0 in the base list). A cadet's livery is
    /// drawn within these two classes, so they must cover everything a roll can produce.
    /// </summary>
    public IReadOnlyList<string> AllMetals { get; private set; } = [];
    public IReadOnlyList<string> AllColours { get; private set; } = [];

    public bool HasEmblem(string texture) => _emblemFiles.Contains(texture);

    public int TemplateCount => _templates.Count;

    /// <summary>
    /// Loaded alongside <see cref="MapGen.VanillaVocabulary"/>, so writers that run without a
    /// game folder in hand — the editor's re-emit — still roll against the same rules.
    /// </summary>
    public static VanillaHeraldry? Current { get; private set; }

    public static VanillaHeraldry? Load(string gameDir)
    {
        string root = Path.Combine(gameDir, "common", "coat_of_arms");
        if (!Directory.Exists(root)) return null;

        var h = new VanillaHeraldry();
        foreach (string file in Files(Path.Combine(root, "coat_of_arms")))
        {
            var doc = ScriptTree.ParseFile(file);

            // Script variables (`@cross_offset_left = 0.3`) that templates use for positions.
            foreach (var v in doc.Children!.Where(c => c.Key.StartsWith('@') && c.Value is not null))
                h._variables[v.Key] = v.Value!;

            foreach (var block in doc.Named("template").Where(b => b.IsBlock))
                foreach (var t in block.Children!.Where(c => c.IsBlock))
                    h._templates[t.Key] = t;
        }

        foreach (string file in Files(Path.Combine(root, "template_lists")))
        {
            var doc = ScriptTree.ParseFile(file);
            Collect(doc, "coat_of_arms_template_lists", h._templateLists);
            Collect(doc, "color_lists", h._colorLists);
            Collect(doc, "pattern_texture_lists", h._patternLists);
            Collect(doc, "colored_emblem_texture_lists", h._emblemLists);
        }

        foreach (string file in Files(Path.Combine(gameDir, "common", "scripted_triggers")))
            foreach (var t in ScriptTree.ParseFile(file).Children!.Where(c => c.IsBlock))
                h._scriptedTriggers[t.Key] = t;

        string gfx = Path.Combine(gameDir, "gfx", "coat_of_arms");
        foreach (string f in Files(Path.Combine(gfx, "colored_emblems"), "*.dds")) h._emblemFiles.Add(Path.GetFileName(f));
        foreach (string f in Files(Path.Combine(gfx, "patterns"), "*.dds")) h._patternFiles.Add(Path.GetFileName(f));
        h.Emblems = h._emblemFiles.Order(StringComparer.Ordinal).ToList();
        h.Patterns = h._patternFiles.Order(StringComparer.Ordinal).ToList();

        h.AllMetals = EveryValue(h._colorLists, "metal_colors");
        h.AllColours = EveryValue(h._colorLists, "normal_colors").Where(c => !h.AllMetals.Contains(c)).ToList();

        if (h._templates.Count == 0 || !h._templateLists.ContainsKey("all") || h.AllMetals.Count == 0 || h.AllColours.Count == 0)
            return null;

        Console.WriteLine($"  heraldry: {h._templates.Count} templates, {h._emblemLists.Count} emblem lists, " +
                          $"{h._emblemFiles.Count} emblems, {h._patternFiles.Count} patterns from the installed game");
        return h;

        static IEnumerable<string> Files(string dir, string pattern = "*.txt")
            => Directory.Exists(dir) ? Directory.GetFiles(dir, pattern).Order(StringComparer.Ordinal) : [];

        static void Collect(ScriptNode doc, string section, Dictionary<string, ScriptNode> into)
        {
            foreach (var s in doc.Named(section).Where(s => s.IsBlock))
                foreach (var list in s.Children!.Where(c => c.IsBlock))
                    into[list.Key] = list;
        }
    }

    /// <summary>Loads the rules from <paramref name="gameDir"/> and makes them <see cref="Current"/>.</summary>
    public static VanillaHeraldry? LoadCurrent(string gameDir) => Current = Load(gameDir);

    /// <summary><see cref="Current"/>, loading it from the located game the first time it is asked for.</summary>
    public static VanillaHeraldry? CurrentOrLocate()
    {
        if (Current is not null) return Current;
        return GameLocator.FindGameDir() is { } dir ? LoadCurrent(dir) : null;
    }

    // =========================================================================================
    // Rolling a coat
    // =========================================================================================

    /// <summary>
    /// A coat rolled the way the engine rolls one for a family of this culture and faith. Every draw
    /// comes off <paramref name="rng"/> in a fixed order, so a seed always yields the same arms.
    /// Null only if no template in the list can be resolved at all.
    /// </summary>
    public CoatOfArmsWriter.Coat? Compose(Rng rng, HeraldryScope scope)
    {
        var candidates = Weighted(_templateLists["all"], scope).Where(e => _templates.ContainsKey(e.Value)).ToList();

        // A template naming a list or texture that is not there is skipped and the roll repeated,
        // rather than written half-resolved.
        for (int attempt = 0; attempt < 8 && candidates.Count > 0; attempt++)
        {
            string name = Pick(rng, candidates);
            if (TryResolve(_templates[name], rng, scope) is { } coat) return coat with { Template = name };
            candidates.RemoveAll(e => e.Value == name);
        }
        return null;
    }

    private CoatOfArmsWriter.Coat? TryResolve(ScriptNode t, Rng rng, HeraldryScope scope)
    {
        // A template defining its own variables, or using one no file defines, is skipped whole.
        if (t.Children!.Any(c => c.Key.StartsWith('@'))) return null;
        try
        {
            return Resolve(t, rng, scope);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private CoatOfArmsWriter.Coat? Resolve(ScriptNode t, Rng rng, HeraldryScope scope)
    {
        var colours = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            if (t.First("color" + i) is not { } node) break;
            string? c = ResolveColour(node, rng, scope, colours);

            // Two lists can draw one tincture twice. On a divided field that is an undivided field,
            // so the second half re-rolls a few times before accepting it.
            for (int retry = 0; retry < 4 && c is not null && i == 2 && node.ListRef is not null && c == colours[0]; retry++)
                c = ResolveColour(node, rng, scope, colours);
            if (c is null) return null;
            colours.Add(c);
        }
        if (colours.Count == 0) colours.Add("red");
        if (colours.Count == 1) colours.Add(colours[0]);

        string? pattern = t.First("pattern") is { } p ? ResolveTexture(p, _patternLists, _patternFiles, rng, scope) : "pattern_solid.dds";
        if (pattern is null) return null;

        var charges = new List<CoatOfArmsWriter.Charge>();
        foreach (var ce in t.Named("colored_emblem").Where(c => c.IsBlock))
        {
            if (ce.First("texture") is not { } tex) continue;
            string? texture = ResolveTexture(tex, _emblemLists, _emblemFiles, rng, scope);
            if (texture is null) return null;

            var chargeColours = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                if (ce.First("color" + i) is not { } node) break;
                string? c = ResolveColour(node, rng, scope, colours);
                if (c is null) return null;
                chargeColours.Add(c);
            }
            if (chargeColours.Count == 0) chargeColours.Add(colours.Count > 2 ? colours[2] : "yellow");

            var instances = ce.Named("instance").Where(i => i.IsBlock).Select(ReadInstance).ToList();
            if (instances.Count == 0) instances.Add(new CoatOfArmsWriter.Instance(0.5, 0.5, 1, 1, 0, null));

            var mask = ce.First("mask") is { IsBlock: true } m ? m.Bare.Select(int.Parse).ToList() : null;
            charges.Add(new CoatOfArmsWriter.Charge(texture, chargeColours, instances, mask));
        }

        return new CoatOfArmsWriter.Coat(pattern, colours, charges);
    }

    private CoatOfArmsWriter.Instance ReadInstance(ScriptNode i)
    {
        var pos = Pair(i.First("position"), 0.5);
        var scale = Pair(i.First("scale"), 1.0);
        double rot = i.Get("rotation") is { } r ? Num(r) : 0;
        double? depth = i.Get("depth") is { } d ? Num(d) : null;
        return new CoatOfArmsWriter.Instance(pos.X, pos.Y, scale.X, scale.Y, rot, depth);

        (double X, double Y) Pair(ScriptNode? n, double fallback)
        {
            var v = n?.Bare ?? [];
            return v.Count >= 2 ? (Num(v[0]), Num(v[1])) : (fallback, fallback);
        }
    }

    /// <summary>
    /// A number, a script variable standing for one, or an inline expression
    /// (<c>@[0.5 - cross_from_center_x]</c>, whose names are variables without their <c>@</c>).
    /// Throws FormatException for anything else, which skips the template.
    /// </summary>
    private double Num(string s, int depth = 0)
    {
        if (depth > 16) throw new FormatException("variable cycle: " + s);
        if (s.StartsWith("@[", StringComparison.Ordinal)) return Expression(s[2..^1], depth);
        if (s.StartsWith('@')) return _variables.TryGetValue(s, out var v) ? Num(v, depth + 1) : throw new FormatException(s);
        return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>+, -, *, / and parentheses over numbers and variable names; what vanilla's templates use.</summary>
    private double Expression(string text, int depth)
    {
        var tokens = System.Text.RegularExpressions.Regex.Matches(text, @"\d+(\.\d+)?|\.\d+|[A-Za-z_][A-Za-z0-9_]*|[-+*/()]")
                                                           .Select(m => m.Value).ToList();
        int pos = 0;
        double value = Sum();
        if (pos != tokens.Count) throw new FormatException(text);
        return value;

        double Sum()
        {
            double v = Product();
            while (pos < tokens.Count && tokens[pos] is "+" or "-")
                v = tokens[pos++] == "+" ? v + Product() : v - Product();
            return v;
        }

        double Product()
        {
            double v = Unary();
            while (pos < tokens.Count && tokens[pos] is "*" or "/")
                v = tokens[pos++] == "*" ? v * Unary() : v / Unary();
            return v;
        }

        double Unary()
        {
            if (pos < tokens.Count && tokens[pos] == "-") { pos++; return -Unary(); }
            if (pos >= tokens.Count) throw new FormatException(text);
            string t = tokens[pos++];
            if (t == "(")
            {
                double v = Sum();
                if (pos >= tokens.Count || tokens[pos++] != ")") throw new FormatException(text);
                return v;
            }
            return char.IsLetter(t[0]) || t[0] == '_' ? Num("@" + t, depth + 1) : Num(t, depth + 1);
        }
    }

    /// <summary>A tincture: a literal, a reference to one of the template's colours, or a list.</summary>
    private string? ResolveColour(ScriptNode node, Rng rng, HeraldryScope scope, List<string> templateColours)
    {
        if (node.ListRef is { } list)
            return _colorLists.TryGetValue(list, out var body) && Weighted(body, scope) is { Count: > 0 } e ? Pick(rng, e) : null;
        if (node.Value is not { } v) return null;
        if (v.StartsWith("color", StringComparison.Ordinal) && int.TryParse(v.AsSpan(5), out int k))
            return k >= 1 && k <= templateColours.Count ? templateColours[k - 1] : (templateColours.Count > 0 ? templateColours[^1] : null);
        return v;
    }

    private string? ResolveTexture(ScriptNode node, Dictionary<string, ScriptNode> lists, HashSet<string> onDisk, Rng rng, HeraldryScope scope)
    {
        if (node.ListRef is { } list)
        {
            if (!lists.TryGetValue(list, out var body)) return null;
            var entries = Weighted(body, scope).Where(e => onDisk.Contains(e.Value)).ToList();
            return entries.Count > 0 ? Pick(rng, entries) : null;
        }
        return node.Value is { } v && onDisk.Contains(v) ? v : null;
    }

    // =========================================================================================
    // Weighted lists
    // =========================================================================================

    private readonly record struct Entry(string Value, double Weight);

    /// <summary>
    /// A list's entries with positive weight, plus those of every special_selection (at any depth)
    /// whose trigger holds for <paramref name="scope"/>.
    /// </summary>
    private List<Entry> Weighted(ScriptNode list, HeraldryScope scope)
    {
        var entries = new List<Entry>();
        Add(list);
        return entries;

        void Add(ScriptNode block)
        {
            foreach (var c in block.Children!)
            {
                if (c.Key == "special_selection" && c.IsBlock)
                {
                    if (c.First("trigger") is not { IsBlock: true } trig || Holds(trig, scope)) Add(c);
                    continue;
                }
                if (c.IsBlock || c.Value is null || c.Key == "trigger") continue;
                if (double.TryParse(c.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) && w > 0)
                    entries.Add(new Entry(c.Value, w));
            }
        }
    }

    /// <summary>Every value a list names, under any trigger and at any weight, in first-seen order.</summary>
    private static List<string> EveryValue(Dictionary<string, ScriptNode> lists, string name)
    {
        var seen = new List<string>();
        if (lists.TryGetValue(name, out var body)) Walk(body);
        return seen;

        void Walk(ScriptNode block)
        {
            foreach (var c in block.Children!)
            {
                if (c.Key == "special_selection" && c.IsBlock) Walk(c);
                else if (!c.IsBlock && c.Value is { } v && double.TryParse(c.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                         && !seen.Contains(v))
                    seen.Add(v);
            }
        }
    }

    private static string Pick(Rng rng, List<Entry> entries)
    {
        double total = entries.Sum(e => e.Weight);
        double roll = rng.NextDouble() * total;
        foreach (var e in entries)
        {
            roll -= e.Weight;
            if (roll < 0) return e.Value;
        }
        return entries[^1].Value;
    }

    // =========================================================================================
    // Triggers
    // =========================================================================================

    private enum Scope { Character, Culture, Faith, Religion, Unknown }

    /// <summary>Scopes saved with save_temporary_scope_as while a trigger runs.</summary>
    private sealed class Saved : Dictionary<string, Scope>
    {
        public Saved() : base(StringComparer.Ordinal) { }
    }

    private bool Holds(ScriptNode trigger, HeraldryScope ctx) => All(trigger, ctx, Scope.Character, new Saved(), 0);

    private bool All(ScriptNode block, HeraldryScope ctx, Scope at, Saved saved, int depth)
        => block.Children!.Where(c => !c.IsBare).All(c => Item(c, ctx, at, saved, depth));

    private bool Item(ScriptNode n, HeraldryScope ctx, Scope at, Saved saved, int depth)
    {
        if (depth > 32) return false;
        string key = n.Key;

        switch (key)
        {
            case "AND": return n.IsBlock && All(n, ctx, at, saved, depth + 1);
            case "OR": return n.IsBlock && n.Children!.Where(c => !c.IsBare).Any(c => Item(c, ctx, at, saved, depth + 1));
            case "NOT":
            case "NOR": return n.IsBlock && !n.Children!.Where(c => !c.IsBare).Any(c => Item(c, ctx, at, saved, depth + 1));
            case "NAND": return n.IsBlock && !All(n, ctx, at, saved, depth + 1);
            case "always": return n.Value == "yes";
            case "trigger_if":
            {
                if (!n.IsBlock) return false;
                bool limit = n.First("limit") is { IsBlock: true } l && All(l, ctx, at, saved, depth + 1);
                return !limit || n.Children!.Where(c => !c.IsBare && c.Key != "limit").All(c => Item(c, ctx, at, saved, depth + 1));
            }
            case "save_temporary_scope_as":
            case "save_scope_as":
                if (n.Value is { } name) saved[name] = at;
                return true;
            case "exists":
                return n.Value is "scope:culture" or "scope:faith";
        }

        // Scope changes.
        Scope? target = key switch
        {
            "scope:culture" => Scope.Culture,
            "scope:faith" => Scope.Faith,
            "scope:faith.religion" => Scope.Religion,
            "faith" when at == Scope.Character => Scope.Faith,
            "religion" or "faith.religion" when at is Scope.Faith or Scope.Character => Scope.Religion,
            _ when key.StartsWith("scope:", StringComparison.Ordinal) && saved.TryGetValue(key[6..], out var s) => s,
            _ => null,
        };
        if (target is { } to)
        {
            if (n.IsBlock) return All(n, ctx, to, saved, depth + 1);
            // `scope:faith.religion = religion:x`: a comparison with the scope itself.
            return n.Value is { } v && Is(to, v, ctx);
        }
        if (key == "this") return n.Value is { } tv && Is(at, tv, ctx);

        // Scripted triggers, evaluated in place; `= no` negates.
        if (!n.IsBlock && _scriptedTriggers.TryGetValue(key, out var def) && n.Value is "yes" or "no")
            return All(def, ctx, at, saved, depth + 1) == (n.Value == "yes");

        return (key, at) switch
        {
            ("has_coa_gfx", Scope.Culture) => n.Value is { } g && ctx.CoaGfx.Contains(g),
            ("is_in_family", Scope.Religion) => n.Value is { } f && f == ctx.ReligionFamily,
            ("has_doctrine", Scope.Faith) => n.Value is { } d && ctx.Doctrines.Contains(d),
            ("has_icon", Scope.Faith) => n.Value is { } i && i == ctx.FaithIcon,

            // The character, the title, the dynasty's name, liege chains, eras and regions: none of
            // them exist for a family's arms, which is also the right answer for a generated one.
            _ => false,
        };
    }

    /// <summary>Whether the scope <paramref name="at"/> is the database object <paramref name="reference"/>.</summary>
    private static bool Is(Scope at, string reference, HeraldryScope ctx) => at switch
    {
        Scope.Religion => reference == "religion:" + ctx.ReligionKey,
        _ => false,
    };
}
