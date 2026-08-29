namespace Ck3MapGen.Emit;

using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>One vanilla war garment, and everything needed to re-dress it as our own.</summary>
/// <param name="Accessory">Vanilla accessory key, e.g. <c>m_clothes_sec_norse_war_nob_01_hi</c>.</param>
/// <param name="Family">Culture stem taken from the name — <c>norse</c>, <c>ep2_steppe</c>.</param>
/// <param name="EntityBody">
/// The vanilla entity's body, verbatim minus its own <c>name</c> line. Copied rather than rebuilt
/// because it carries the blend-shape attributes — fat, gaunt, muscular, old, dwarf, infant — that
/// let a garment follow the body it is on. A hand-written entity without them renders one rigid
/// shape that clips through any character who is not the default build.
/// </param>
/// <param name="MeshPath">
/// The garment's <c>.mesh</c> on disk, or null when it could not be resolved.
///
/// Carried because a pattern tiles over UV1, and UV1 is NOT normalised across vanilla's garments:
/// <c>ep2_steppe</c> spans exactly 1.00, while <c>ep2_western_era2</c> spans 3.05 and starts at
/// -1.61. One shared layout scale therefore tiles three times denser on the western garment than on
/// the steppe one, which is visible as a pattern that is right on some cultures and wrong on others.
/// <see cref="ArmorForgeStep"/> reads the span from here and scales each layout by it.
/// </param>
public sealed record ArmorGarment(
    string Accessory, bool Female, string Family, string SetTags, string EntityBody, string? PatternMask,
    string? MeshPath);

/// <summary>
/// Finds the war garments vanilla already ships, so armour can be recoloured without modelling
/// anything.
///
/// **Why this is cheap.** A worn garment declares exactly what a forged weapon does — a
/// <c>pattern_mask</c> and a <c>variation</c> under <c>portrait_accessory</c> — so the recolour
/// system needs no new geometry, no new UVs and no mask of our own: vanilla's mask already marks
/// the garment's regions, and we supply a different palette against it.
///
/// **What is scarce is silhouettes, not colours.** There are 38 male and 37 female war garments and
/// they are organised by culture and era, never by material: no name contains mail, plate or scale,
/// and vanilla itself collapses all six armour artifact types onto two visuals. So the garment
/// answers "whose armour is this", and the palette answers "what is it made of".
/// </summary>
public static class ArmorCatalogue
{
    /// <summary>Accessory names that look like war dress rather than court dress.</summary>
    private static readonly Regex WarLike = new(@"_war_|_war\d|_armor", RegexOptions.Compiled);

    /// <summary>Pulls the culture stem out of an accessory name.</summary>
    private static readonly Regex Stem =
        new(@"^[mf]_clothes_sec(?:ular)?_(?<fam>.+?)_war", RegexOptions.Compiled);

    /// <summary>
    /// Reads the accessories, then the entities they point at. Returns empty rather than throwing
    /// when the game directory is not where we expect — armour is an enhancement, and a map that
    /// generates without it is a better outcome than one that refuses to generate at all.
    /// </summary>
    public static List<ArmorGarment> Read(string gameDir)
    {
        string accDir = Path.Combine(gameDir, "gfx", "portraits", "accessories");
        string modelDir = Path.Combine(gameDir, "gfx", "models", "portraits");

        if (!Directory.Exists(accDir) || !Directory.Exists(modelDir)) return [];

        var wanted = ReadAccessories(accDir);
        if (wanted.Count == 0) return [];

        var entities = ReadEntities(modelDir, [.. wanted.Values.Select(v => v.Entity)]);
        var found = new List<ArmorGarment>();

        foreach (var (name, info) in wanted)
        {
            if (!entities.TryGetValue(info.Entity, out var found2)) continue;

            var stem = Stem.Match(name);

            found.Add(new ArmorGarment(
                name,
                name.StartsWith("f_", StringComparison.Ordinal),
                stem.Success ? stem.Groups["fam"].Value : "western",
                info.Tags,
                found2.Body,
                MaskOf(found2.Body),
                found2.MeshPath));
        }

        return [.. found.OrderBy(g => g.Accessory, StringComparer.Ordinal)];
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Top-level accessory blocks, keyed by name. Line-based rather than a real parser, in the same
    /// spirit as <see cref="MapGen.VanillaVocabulary"/>: these files are machine-formatted and only
    /// three fields are wanted from each block.
    /// </summary>
    private static Dictionary<string, (string Entity, string Tags)> ReadAccessories(string dir)
    {
        var wanted = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(dir, "*.txt"))
        {
            string? open = null;
            string tags = "", entity = "";

            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();

                if (open is null)
                {
                    int eq = line.IndexOf(" = {", StringComparison.Ordinal);

                    // A top-level block is unindented; anything nested belongs to one already open.
                    if (eq <= 0 || raw.Length == 0 || char.IsWhiteSpace(raw[0])) continue;

                    string name = line[..eq];
                    if (!WarLike.IsMatch(name) || !name.StartsWith("m_clothes", StringComparison.Ordinal)
                        && !name.StartsWith("f_clothes", StringComparison.Ordinal)) continue;

                    open = name;
                    tags = entity = "";
                    continue;
                }

                if (line.StartsWith("set_tags", StringComparison.Ordinal))
                    tags = Between(line, '"', '"') ?? "";

                // `entity = { required_tags = "" shared_pose_entity = torso entity = X }` - the
                // inner assignment is the one that names the entity, so take the last match.
                if (line.StartsWith("entity = {", StringComparison.Ordinal))
                {
                    int last = line.LastIndexOf("entity = ", StringComparison.Ordinal);
                    entity = line[(last + 9)..].Trim().TrimEnd('}').Trim();
                }

                if (line != "}") continue;

                if (entity.Length > 0) wanted[open] = (entity, tags);
                open = null;
            }
        }

        return wanted;
    }

    /// <summary>
    /// The body of every wanted entity, found by scanning the model assets once.
    ///
    /// Brace-counted rather than line-matched: an entity block contains nested <c>meshsettings</c>
    /// and <c>game_data</c> blocks, so stopping at the first closing brace would truncate it and
    /// take the mesh settings with it.
    /// </summary>
    private static Dictionary<string, (string Body, string? MeshPath)> ReadEntities(
        string dir, HashSet<string> names)
    {
        var found = new Dictionary<string, (string, string?)>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(dir, "*.asset", SearchOption.AllDirectories))
        {
            string text;

            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }

            foreach (string name in names)
            {
                if (found.ContainsKey(name)) continue;

                int at = text.IndexOf($"name = \"{name}\"", StringComparison.Ordinal);
                if (at < 0) continue;

                int open = text.LastIndexOf('{', at);
                if (open < 0) continue;

                int depth = 0, i = open;

                for (; i < text.Length; i++)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}' && --depth == 0) break;
                }

                if (i >= text.Length) continue;

                // Everything inside the braces except the entity's own name line, which we replace.
                string body = text[(open + 1)..i];
                var kept = new StringBuilder();

                foreach (string line in body.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("name = \"", StringComparison.Ordinal)
                        && line.Contains(name, StringComparison.Ordinal)) continue;

                    kept.Append(line.TrimEnd()).Append('\n');
                }

                found[name] = (kept.ToString(), MeshBeside(path, text, kept.ToString()));
            }

            if (found.Count == names.Count) break;
        }

        return found;
    }

    /// <summary>
    /// The <c>.mesh</c> an entity draws, resolved next to the <c>.asset</c> that declares it.
    ///
    /// Two hops, because the entity names a <c>pdxmesh</c> by KEY and only the <c>pdxmesh</c> block
    /// knows the filename — they routinely differ (<c>..._01_mesh</c> against <c>..._01.mesh</c>),
    /// so building the filename from the key by string surgery would work on most garments and
    /// silently miss the rest.
    /// </summary>
    private static string? MeshBeside(string assetPath, string assetText, string entityBody)
    {
        string? key = Between(entityBody[Math.Max(0, entityBody.IndexOf("pdxmesh", StringComparison.Ordinal))..], '"', '"');
        if (string.IsNullOrEmpty(key)) return null;

        int at = assetText.IndexOf($"name = \"{key}\"", StringComparison.Ordinal);
        if (at < 0) return null;

        int file = assetText.IndexOf("file", at, StringComparison.Ordinal);
        if (file < 0) return null;

        string? name = Between(assetText[file..], '"', '"');
        if (string.IsNullOrEmpty(name)) return null;

        string path = Path.Combine(Path.GetDirectoryName(assetPath) ?? "", name);
        return File.Exists(path) ? path : null;
    }

    private static string? MaskOf(string body)
    {
        int at = body.IndexOf("pattern_mask", StringComparison.Ordinal);
        return at < 0 ? null : Between(body[at..], '"', '"');
    }

    private static string? Between(string s, char a, char b)
    {
        int i = s.IndexOf(a);
        if (i < 0) return null;
        int j = s.IndexOf(b, i + 1);
        return j < 0 ? null : s[(i + 1)..j];
    }
}
