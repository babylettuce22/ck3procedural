using System.Globalization;
using System.Text.RegularExpressions;
using Ck3MapGen.GameGui;
using Ck3MapGen.Io;

namespace Ck3MapGen.Core;

public sealed class WorldField
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required Func<string> Read { get; init; }
    public Action<string>? Write { get; init; }
    public Action? Reset { get; init; }
    public string Description { get; init; } = "";
    /// <summary>For a colour field: the top of its component range, 255 for RGB and 1 for HSV or faith colours.</summary>
    public double? ColorScale { get; init; }
    /// <summary>Whether the value is a whitespace-separated list rather than one token.</summary>
    public bool IsList { get; init; }
    /// <summary>Choices for a reference field, key and display name, or null for free text.</summary>
    public Func<IReadOnlyList<(string Key, string Label)>>? Options { get; init; }
}

public sealed class WorldEntry
{
    public required string Key { get; init; }
    public required string Kind { get; init; }
    public required string File { get; init; }
    public List<WorldField> Fields { get; } = [];
    public WorldEntry? Parent { get; init; }
    public int ProvinceId { get; init; }
    public string Name => Fields.FirstOrDefault(f => f.Name == "Name")?.Read() ?? Key;
    public string Value(string key) => Fields.FirstOrDefault(f => f.Name == key)?.Read() ?? "";
    public WorldField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
    public override string ToString() => $"{Name}  [{Key}]";
}

/// <summary>
/// Opens the mod as it exists, including worlds made by older builds. This never calls Generate,
/// WriteMod, a content writer, or the edit-overlay replay path. Unknown script stays on disk.
/// </summary>
public sealed class LoadedWorld
{
    private readonly Dictionary<string, EditableWorldFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (EditableWorldFile File, (int Start, int Length) Range)> _loc = [];
    private readonly Dictionary<string, List<string>> _bookmarkNames = [];
    private readonly Dictionary<string, List<(EditableWorldFile File, GuiNode Node)>> _bookmarkCharacters = [];
    public string DirectoryPath { get; }
    public List<WorldEntry> Entries { get; } = [];
    public Dictionary<string, WorldEntry> Titles { get; } = [];
    public Dictionary<int, WorldEntry> Baronies { get; } = [];
    public Dictionary<int, WorldEntry> Provinces { get; } = [];
    public Dictionary<int, int> ProvinceByRgb { get; } = [];
    public Dictionary<string, WorldEntry> Characters { get; } = [];
    public Dictionary<string, string> Holders { get; } = [];
    public Dictionary<string, string> Lieges { get; } = [];
    /// <summary>The government each title's history gives it at the start date.</summary>
    public Dictionary<string, string> Governments { get; } = [];
    public Dictionary<string, WorldEntry> Dynasties { get; } = [];
    public Dictionary<string, WorldEntry> Houses { get; } = [];
    public Dictionary<string, WorldEntry> HolySites { get; } = [];
    /// <summary>Generated arms by their owner's key — a dynasty id or a house key.</summary>
    public Dictionary<string, WorldEntry> Coats { get; } = [];
    private readonly Dictionary<string, (List<string> Male, List<string> Female)> _nameLists = [];
    public const string WildernessGovernment = "wilderness_government";
    /// <summary>
    /// The holding types a barony in an opened mod may be set to, and the ones its dropdown offers.
    ///
    /// The last two are the seats of governments no vanilla province history writes but this tool
    /// does: a horde's capital is a nomad holding and a mandala realm's is a temple citadel. They
    /// were missing here, so opening a generated mod that contained either and letting the grid
    /// round-trip that barony was rejected as an invalid holding — the value the file already held
    /// was not one the editor would accept back.
    /// </summary>
    public static readonly string[] Holdings =
    [
        "none", "castle_holding", "city_holding", "church_holding", "tribal_holding",
        "nomad_holding", "temple_citadel_holding",
    ];
    public string StartDate { get; private set; } = "9999.12.31";
    public List<string> Notes { get; } = [];
    public string? LastBackup { get; private set; }
    public IReadOnlyCollection<EditableWorldFile> Files => _files.Values;
    public IEnumerable<EditableWorldFile> ChangedFiles => Files.Where(f => f.Changed);
    public string ProvinceMapPath => Path.Combine(DirectoryPath, "map_data", "provinces.png");

    private LoadedWorld(string path) => DirectoryPath = Path.GetFullPath(path);

    public static LoadedWorld Open(string path)
    {
        if (File.Exists(path) && Path.GetFileName(path).Equals("descriptor.mod", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path)!;
        var world = new LoadedWorld(path);
        if (!File.Exists(Path.Combine(path, "descriptor.mod"))
            || !File.Exists(world.ProvinceMapPath)
            || !File.Exists(Path.Combine(path, "common", "landed_titles", "00_landed_titles.txt")))
            throw new IOException("Choose a generated mod folder containing descriptor.mod, map_data/provinces.png and common/landed_titles/00_landed_titles.txt.");

        world.ReadLocalisation();
        world.ReadFlavorization();
        world.ReadBookmarks();
        var titles = world.Read("common/landed_titles/00_landed_titles.txt");
        foreach (var root in titles.Script!.Roots) world.AddTitle(titles, root, null);
        world.ReadEntries("common/culture/cultures/00_generated_cultures.txt", "Culture");
        world.ReadEntries("common/religion/religion_types/00_generated_religions.txt", "Religion");
        world.ReadEntries("history/characters/00_generated_characters.txt", "Character");
        world.ReadEntries("history/provinces/00_generated_provinces.txt", "Province");
        world.ReadEntries("common/dynasties/00_generated_dynasties.txt", "Dynasty");
        world.ReadEntries("common/dynasty_houses/00_generated_houses.txt", "House");
        world.ReadEntries("common/religion/holy_site_types/01_generated_holy_sites.txt", "HolySite");
        world.ReadEntries("common/coat_of_arms/coat_of_arms/00_generated_coas.txt", "CoatOfArms");
        world.ReadNameLists();
        world.ReadTitleHistory();
        world.ReadDefinition();
        world.Notes.Add("This edits the mod's starting world, not a CK3 save. Regenerating this mod can replace these edits; keep the edited world as a separate mod or copy.");
        world.Notes.Add("Names embedded in historical prose and portrait DNA remain as written. Structural changes, government conversion and terrain regeneration use the normal generator workflow.");
        return world;
    }

    private EditableWorldFile Read(string relative, bool script = true)
    {
        string path = Path.GetFullPath(Path.Combine(DirectoryPath, relative));
        if (!_files.TryGetValue(path, out var file)) _files.Add(path, file = new(path, script));
        return file;
    }

    private void ReadLocalisation()
    {
        // Load ordinary English files first, then explicit replacement localizations. Duplicate
        // keys at the same precedence are ambiguous, so do not silently edit one of them.
        var seen = new Dictionary<string, bool>();
        foreach (string directory in new[] { "localization/english", "localization/english/replace", "localization/replace/english" })
        {
            string root = Path.Combine(DirectoryPath, directory);
            if (!Directory.Exists(root)) continue;
            bool replacement = directory.Contains("replace");
            foreach (string path in Directory.GetFiles(root, "*_l_english.yml").Order(StringComparer.Ordinal))
            {
                var file = Read(path, false);
                foreach (Match m in Regex.Matches(file.Original,
                             "(?m)^[ \\t]*([A-Za-z0-9_.-]+):[0-9]*[ \\t]+\"((?:\\\\.|[^\"\\\\\\r\\n])*)\""))
                {
                    string key = m.Groups[1].Value;
                    if (seen.TryGetValue(key, out bool wasReplacement) && wasReplacement == replacement)
                    {
                        _loc.Remove(key);
                        Notes.Add($"Duplicate English localization is read-only: {key}");
                        continue;
                    }
                    seen[key] = replacement;
                    _loc[key] = (file, (m.Groups[2].Index, m.Groups[2].Length));
                }
            }
        }
    }

    private void ReadBookmarks()
    {
        foreach (string directory in new[] { "common/bookmarks/bookmarks", "common/bookmarks/challenge_characters" })
        {
            string root = Path.Combine(DirectoryPath, directory);
            if (!Directory.Exists(root)) continue;
            foreach (string path in Directory.GetFiles(root, "*.txt"))
            {
                var file = Read(path);
                if (file.Script!.Roots.Select(n => n.Field("start_date")).FirstOrDefault(d => d is not null) is { } date
                    && DateNumber(date) < DateNumber(StartDate)) StartDate = date;
                foreach (var node in file.Script!.Roots.SelectMany(r => r.Descendants().Prepend(r)))
                {
                    string? id = node.Field("history_id");
                    if (id is null) continue;
                    if (!_bookmarkCharacters.TryGetValue(id, out var chars)) _bookmarkCharacters[id] = chars = [];
                    chars.Add((file, node));
                    if (node.Field("name") is not { } name) continue;
                    if (!_bookmarkNames.TryGetValue(id, out var names)) _bookmarkNames[id] = names = [];
                    names.Add(Unquote(name));
                }
            }
        }
    }

    private void AddTitle(EditableWorldFile file, GuiNode node, WorldEntry? parent)
    {
        if (!node.IsBlock || !Regex.IsMatch(node.Key, "^[bcdkeh]_")) return;
        int.TryParse(node.Field("province"), out int province);
        var entry = new WorldEntry { Key = node.Key, Kind = "Title", File = file.Path, Parent = parent, ProvinceId = province };
        if (!Titles.TryAdd(entry.Key, entry)) throw new FormatException($"Duplicate title: {entry.Key}");
        if (province > 0 && !Baronies.TryAdd(province, entry)) throw new FormatException($"Duplicate province: {province}");
        Entries.Add(entry);
        AddName(entry, node.Key);
        AddLocalisation(entry, node.Key + "_adj", "Adjective");
        AddFields(entry, file, node, "color");
        if (node.Key[0] is 'e' or 'k' or 'd' or 'h') AddFields(entry, file, node, "capital");
        if (node.Key[0] is 'e' or 'k' or 'd')
        {
            string tier = node.Key[0] switch { 'e' => "empire", 'k' => "kingdom", _ => "duchy" };
            const string applies = "This title's own word, outranking its holder's culture. Only present where the generator wrote one — an imported country that named itself.";
            AddLocalisation(entry, $"gen_flav_{node.Key}_{tier}", "Realm word", RealmWordsCategory, applies);
            AddLocalisation(entry, $"gen_flav_{node.Key}_{tier}_male", "Ruler style (male)", RealmWordsCategory, applies);
            AddLocalisation(entry, $"gen_flav_{node.Key}_{tier}_female", "Ruler style (female)", RealmWordsCategory, applies);
        }
        foreach (var child in node.Children) AddTitle(file, child, entry);
    }

    private void ReadEntries(string relative, string kind)
    {
        if (!File.Exists(Path.Combine(DirectoryPath, relative)))
        {
            Notes.Add($"Not present in this world: {relative}");
            return;
        }
        var file = Read(relative);
        foreach (var node in file.Script!.Roots.Where(n => n.IsBlock))
        {
            var entry = AddEntry(file, node, kind);
            if (kind == "Religion")
            {
                foreach (var faith in node.ChildrenNamed("faiths").SelectMany(n => n.Children).Where(n => n.IsBlock))
                    AddEntry(file, faith, "Faith");
            }
            if (kind == "Province" && int.TryParse(node.Key, out int id)) Provinces.Add(id, entry);
        }
    }

    private WorldEntry AddEntry(EditableWorldFile file, GuiNode node, string kind)
    {
        var entry = new WorldEntry { Key = node.Key, Kind = kind, File = file.Path };
        Entries.Add(entry);
        if (kind == "Character")
        {
            Characters.Add(node.Key, entry);
            if (node.Children.FirstOrDefault(n => n.Key == "name" && n.Value is not null) is { } name)
            {
                var range = file.ValueRange(name);
                entry.Fields.Add(new WorldField
                {
                    Name = "Name", Category = "Identity", Read = () => Unquote(file.Read(range)),
                    Write = value =>
                    {
                        ValidateName(value);
                        string old = Unquote(file.Read(range));
                        file.Set(range, Quote(value));
                        foreach (string key in _bookmarkNames.GetValueOrDefault(node.Key) ?? [])
                            if (_loc.TryGetValue(key, out var loc))
                            {
                                string text = loc.File.Read(loc.Range);
                                if (text == old || text.StartsWith(old + " ", StringComparison.Ordinal))
                                    loc.File.Set(loc.Range, value + text[old.Length..]);
                            }
                    },
                    Description = "Character and matching bookmark display names. Historical prose and portrait DNA are preserved."
                    ,Reset = () =>
                    {
                        file.RevertRange(range);
                        foreach (string key in _bookmarkNames.GetValueOrDefault(node.Key) ?? [])
                            if (_loc.TryGetValue(key, out var loc)) loc.File.RevertRange(loc.Range);
                    }
                });
            }
            AddFields(entry, file, node, "martial", "prowess", "diplomacy", "intrigue", "stewardship", "learning", "trait");
            // Religion and culture can also appear on bookmark companions; update those exact
            // references when a character is edited, without rebuilding the cast or its portraits.
            AddFields(entry, file, node, "culture", "religion");
            foreach (var date in node.Children.Where(n => n.IsBlock && (n.Field("birth") == "yes" || n.Field("death") == "yes")))
                entry.Fields.Add(new WorldField { Name = date.Field("birth") == "yes" ? "Birth date" : "Death date", Category = "History", Read = () => date.Key });
            AddStanding(entry, file, node);
        }
        else if (kind == "Province") AddFields(entry, file, node, "culture", "religion", "holding", "special_building_slot", "special_building");
        else if (kind is "Dynasty" or "House")
        {
            (kind == "Dynasty" ? Dynasties : Houses).Add(node.Key, entry);
            AddLocalisation(entry, Unquote(node.Field("name") ?? ""), "Name", description:
                "The family name shown in game — one English localization line, which a dynasty and its main house share.");
        }
        else if (kind == "HolySite")
        {
            HolySites.Add(node.Key, entry);
            AddFields(entry, file, node, "county");
        }
        else if (kind == "CoatOfArms")
        {
            Coats.Add(node.Key, entry);
            AddFields(entry, file, node, "pattern", "color1", "color2");
            var emblems = node.ChildrenNamed("colored_emblem").Where(n => n.IsBlock).ToList();
            for (int i = 0; i < emblems.Count; i++)
                AddPrefixedFields(entry, file, emblems[i], i == 0 ? "emblem " : $"emblem {i + 1} ", "texture", "color1");
        }
        else
        {
            AddName(entry, node.Key);
            AddLocalisation(entry, node.Key + "_adj", "Adjective");
            AddLocalisation(entry, node.Key + "_desc", "Description");
            AddFields(entry, file, node, "color", "ethos", "martial_custom", "head_determination", "traditions",
                "clothing_gfx", "unit_gfx", "building_gfx", "coa_gfx", "doctrine", "icon", "holy_site");
            if (kind == "Culture") AddCultureWords(entry);
        }
        // Read-only information keeps historical relations and identities inspectable without
        // pretending that changing one foreign key is a safe structural edit.
        foreach (var child in node.Children.Where(n => !n.IsBlock && n.Value is not null))
        {
            if (entry.Fields.Any(f => f.Name == child.Key || f.Name.StartsWith(child.Key + " ")) || child.Key == "name") continue;
            entry.Fields.Add(new WorldField { Name = child.Key, Category = "References", Read = () => Unquote(child.Value!) });
        }
        return entry;
    }

    private void ReadTitleHistory()
    {
        const string relative = "history/titles/00_generated_titles.txt";
        if (!File.Exists(Path.Combine(DirectoryPath, relative))) return;
        var file = Read(relative);
        foreach (var node in file.Script!.Roots)
        {
            if (!Titles.TryGetValue(node.Key, out var entry)) continue;
            var dates = node.Children.Where(n => n.IsBlock && DateNumber(n.Key) is > 0
                && DateNumber(n.Key) <= DateNumber(StartDate)).OrderBy(n => DateNumber(n.Key)).ToList();
            string? holder = dates.Select(n => n.Field("holder")).LastOrDefault(v => v is not null);
            if (holder is not null)
            {
                Holders[node.Key] = holder;
                entry.Fields.Add(new WorldField { Name = "Holder", Category = "References",
                    Read = () => Characters.TryGetValue(holder, out var character) ? character.ToString() : holder });
            }
            string? government = dates.Select(n => n.Field("government")).LastOrDefault(v => v is not null);
            if (government is not null)
            {
                Governments[node.Key] = government;
                entry.Fields.Add(new WorldField { Name = "Government", Category = "References", Read = () => government });
            }
            string? liege = dates.Select(n => n.Field("liege")).LastOrDefault(v => v is not null);
            if (liege is not null && liege != "0") Lieges[node.Key] = liege;
            if (dates.SelectMany(d => d.ChildrenNamed("change_development_level")).LastOrDefault(n => !n.IsBlock && n.Value is not null) is { } level)
                AddLeaf(entry, file, level, "Development", "Development", Range(0, 100),
                    "The county's development at the start date — the change_development_level line in its title history.");
        }
    }

    // --- Leaves inside date and effect blocks ------------------------------------------------------

    /// <summary>A validator for a whole number in a closed range; null means the value is fine.</summary>
    private static Func<string, string?> Range(int min, int max)
        => value => int.TryParse(value.Trim(), out int n) && n >= min && n <= max ? null : $"Use a whole number between {min} and {max}.";

    /// <summary>One editable leaf anywhere in a block, by exact source range, the way the flat fields are.</summary>
    private static void AddLeaf(WorldEntry entry, EditableWorldFile file, GuiNode leaf, string name, string category,
        Func<string, string?> validate, string description)
    {
        var range = file.ValueRange(leaf);
        entry.Fields.Add(new WorldField
        {
            Name = name, Category = category, Description = description,
            Read = () => file.Read(range),
            Write = value =>
            {
                if (validate(value) is { } problem) throw new ArgumentException(problem);
                file.Set(range, value.Trim());
            },
            Reset = () => file.RevertRange(range),
        });
    }

    /// <summary>
    /// The purse and standing the generator grants at the start date: ordinary leaves inside the
    /// dated <c>effect</c> block of the character — gold, prestige, dread, legitimacy, lifestyle
    /// perk points, and dynasty prestige one block further in. Edited in place like a skill.
    /// </summary>
    private static void AddStanding(WorldEntry entry, EditableWorldFile file, GuiNode character)
    {
        const string category = "Standing";
        foreach (var date in character.Children.Where(n => n.IsBlock && DateNumber(n.Key) > 0))
            foreach (var effect in date.ChildrenNamed("effect").Where(n => n.IsBlock))
            {
                foreach (var leaf in effect.Children.Where(n => !n.IsBlock && n.Value is not null))
                {
                    if (entry.Fields.Any(f => f.Category == category && f.Name == Label(leaf.Key))) continue;
                    switch (leaf.Key)
                    {
                        case "add_gold": AddLeaf(entry, file, leaf, "Gold", category, Range(0, 100000), "Starting gold, granted on the start date."); break;
                        case "add_prestige": AddLeaf(entry, file, leaf, "Prestige", category, Range(0, 1000000), "Starting prestige. Vanilla's levels sit at 1000, 2000, 5000, 10000, 25000."); break;
                        case "add_piety": AddLeaf(entry, file, leaf, "Piety", category, Range(0, 1000000), "Starting piety."); break;
                        case "add_dread": AddLeaf(entry, file, leaf, "Dread", category, Range(0, 100), "Starting dread."); break;
                        case "add_legitimacy":
                            AddLeaf(entry, file, leaf, "Legitimacy", category,
                                v => Regex.IsMatch(v.Trim(), "^legitimacy_level_[0-9]+$") ? null : "Use a legitimacy_level_N script value.",
                                "A legitimacy script value granted at the start date."); break;
                        default:
                            if (Regex.Match(leaf.Key, "^add_([a-z]+)_lifestyle_perk_points$") is { Success: true } m)
                                AddLeaf(entry, file, leaf, Label(leaf.Key), category, Range(0, 12),
                                    $"Lifestyle perk points granted at game start in the {m.Groups[1].Value} tree, on top of what vanilla auto-assigns for age.");
                            break;
                    }
                }
                foreach (var dynasty in effect.ChildrenNamed("dynasty").Where(n => n.IsBlock))
                    foreach (var leaf in dynasty.ChildrenNamed("add_dynasty_prestige").Where(n => !n.IsBlock && n.Value is not null))
                        if (!entry.Fields.Any(f => f.Name == "Dynasty prestige"))
                            AddLeaf(entry, file, leaf, "Dynasty prestige", category, Range(0, 1000000), "Starting dynasty prestige (renown). Only paid out to an independent ruler.");
            }

        static string Label(string key) => Regex.Match(key, "^add_([a-z]+)_lifestyle_perk_points$") is { Success: true } m
            ? $"Perk points ({m.Groups[1].Value})" : key;
    }

    // --- Name lists ---------------------------------------------------------------------------------

    private void ReadNameLists()
    {
        const string relative = "common/culture/name_lists/00_generated_name_lists.txt";
        if (!File.Exists(Path.Combine(DirectoryPath, relative))) return;
        var file = Read(relative);
        foreach (var node in file.Script!.Roots.Where(n => n.IsBlock))
            _nameLists[node.Key] = (Tokens(node, "male_names"), Tokens(node, "female_names"));

        List<string> Tokens(GuiNode list, string key)
            => list.ChildrenNamed(key).Where(n => n.IsBlock)
                .SelectMany(n => Regex.Replace(file.Read(file.ValueRange(n)), "#[^\\r\\n]*", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Select(Unquote).ToList();
    }

    /// <summary>The given names a culture draws from, localized, for one sex; empty when the mod carries no list for it.</summary>
    public IReadOnlyList<string> Names(string culture, bool female)
    {
        var entry = Entries.FirstOrDefault(e => e.Kind == "Culture" && e.Key == culture);
        if (entry is null || !_nameLists.TryGetValue(entry.Value("name_list"), out var lists)) return [];
        var keys = female ? lists.Female : lists.Male;
        return keys.Select(k => Localized(k)).Where(n => !n.StartsWith("cul_") && n.Length > 0).ToList();
    }

    private static int DateNumber(string date)
    {
        var parts = date.Split('.');
        return parts.Length == 3 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m)
            && int.TryParse(parts[2], out int d) && y > 0 && y < 10000 && m is >= 1 and <= 12 && d is >= 1 and <= 31
                ? y * 10000 + m * 100 + d : 0;
    }

    private void AddName(WorldEntry entry, string key) => AddLocalisation(entry, key, "Name");
    private void AddLocalisation(WorldEntry entry, string key, string label, string category = "Identity",
        string description = "English localization. Other text is preserved, including names embedded in historical prose.")
    {
        if (!_loc.TryGetValue(key, out var loc)) return;
        entry.Fields.Add(new WorldField
        {
            Name = label, Category = category, Read = () => loc.File.Read(loc.Range),
            Write = value => { ValidateName(value); loc.File.Set(loc.Range, value); },
            Reset = () => loc.File.RevertRange(loc.Range),
            Description = description,
        });
    }

    // --- Realm words ----------------------------------------------------------------------------
    //
    // TitleTierWriter gives a culture its own word for each rank of realm and for the holders, per
    // government, and an imported country its own word for its title. Both are flavorization
    // entries whose keys are localisation keys: gen_flav_<culture>_<variant>_<tier>[_male|_female]
    // and gen_flav_<title>_<tier>[_male|_female]. The words themselves are ordinary English loc,
    // so they edit like a name; the flavorization file is read only to say which governments a
    // variant applies to, and to resolve what a title renders as.

    private static readonly (string Tier, string Realm, string Male, string Female)[] Ranks =
    [
        ("empire", "Empire word", "Emperor", "Empress"),
        ("kingdom", "Kingdom word", "King", "Queen"),
        ("duchy", "Duchy word", "Duke", "Duchess"),
    ];
    private readonly Dictionary<string, List<string>> _flavorGovernments = [];
    public const string RealmWordsCategory = "Realm titles";

    private void ReadFlavorization()
    {
        const string relative = "common/flavorization/zz_generated_flavorization.txt";
        if (!File.Exists(Path.Combine(DirectoryPath, relative))) return;
        var file = Read(relative);
        foreach (var node in file.Script!.Roots.Where(n => n.IsBlock && n.Key.StartsWith("gen_flav_")))
        {
            string prefix = Regex.Replace(node.Key, "_(?:empire|kingdom|duchy)(?:_male|_female)?$", "");
            if (node.ChildrenNamed("governments").FirstOrDefault(g => g.IsBlock) is not { } governments) continue;
            _flavorGovernments[prefix] = Regex.Replace(file.Read(file.ValueRange(governments)), "#[^\\r\\n]*", "")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Replace("_government", "")).ToList();
        }
    }

    private void AddRealmWords(WorldEntry entry, string prefix, string? suffix, string applies)
    {
        foreach (var (tier, realm, male, female) in Ranks)
        {
            AddLocalisation(entry, $"{prefix}_{tier}", realm + suffix, RealmWordsCategory,
                $"This {tier}'s word for itself — Tsardom, League, Prelacy — in place of vanilla's. {applies}");
            AddLocalisation(entry, $"{prefix}_{tier}_male", male + suffix, RealmWordsCategory,
                $"What a man holding a {tier} is called. {applies}");
            AddLocalisation(entry, $"{prefix}_{tier}_female", female + suffix, RealmWordsCategory,
                $"What a woman holding a {tier} is called. {applies}");
        }
    }

    /// <summary>A culture's vocabularies, one per variant the writer emitted, with the governments each covers.</summary>
    private void AddCultureWords(WorldEntry culture)
    {
        for (int variant = 0; ; variant++)
        {
            string prefix = $"gen_flav_{culture.Key}_{variant}";
            if (!Ranks.Any(r => _loc.ContainsKey($"{prefix}_{r.Tier}") || _loc.ContainsKey($"{prefix}_{r.Tier}_male"))) break;
            var governments = _flavorGovernments.GetValueOrDefault(prefix);
            string applies = governments is { Count: > 0 }
                ? $"Applies to this culture's {string.Join(", ", governments)} realms, by the top liege's culture."
                : "Applies to this culture's realms, by the top liege's culture.";
            AddRealmWords(culture, prefix, variant == 0 ? null : $" (variant {variant + 1})", applies);
        }
    }

    /// <summary>The words a culture uses for realms of this government, or null for vanilla's own.</summary>
    public (string Realm, string Male, string Female)? CultureWords(string culture, string government, string tier)
    {
        string rank = tier switch { "e" => "empire", "k" => "kingdom", "d" => "duchy", _ => "" };
        if (rank.Length == 0) return null;
        for (int variant = 0; variant < 16; variant++)
        {
            string prefix = $"gen_flav_{culture}_{variant}";
            if (!_loc.ContainsKey($"{prefix}_{rank}") && !_loc.ContainsKey($"{prefix}_{rank}_male")) continue;
            if (_flavorGovernments.GetValueOrDefault(prefix) is { Count: > 0 } governments
                && !governments.Contains(government.Replace("_government", ""))) continue;
            return (Word($"{prefix}_{rank}"), Word($"{prefix}_{rank}_male"), Word($"{prefix}_{rank}_female"));
        }
        return null;
        string Word(string key) => _loc.TryGetValue(key, out var loc) ? loc.File.Read(loc.Range) : "";
    }

    /// <summary>A title's own word for itself, written for imported countries, or null when it takes its culture's.</summary>
    public (string Realm, string Male, string Female)? TitleWords(string title, string tier)
    {
        string rank = tier switch { "e" => "empire", "k" => "kingdom", "d" => "duchy", _ => "" };
        string prefix = $"gen_flav_{title}";
        if (rank.Length == 0 || !_loc.ContainsKey($"{prefix}_{rank}")) return null;
        return (Localized($"{prefix}_{rank}"), Word($"{prefix}_{rank}_male"), Word($"{prefix}_{rank}_female"));
        string Word(string key) => _loc.TryGetValue(key, out var loc) ? loc.File.Read(loc.Range) : "";
    }

    private void AddFields(WorldEntry entry, EditableWorldFile file, GuiNode parent, params string[] keys)
        => AddPrefixedFields(entry, file, parent, "", keys);

    // A distinct name on purpose: with the same name, a one-key call such as AddFields(…, "color")
    // bound to this overload with "color" as the prefix and no keys, and every such field vanished.
    private void AddPrefixedFields(WorldEntry entry, EditableWorldFile file, GuiNode parent, string prefix, params string[] keys)
    {
        foreach (string key in keys)
        {
            var nodes = parent.ChildrenNamed(key).ToList();
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!node.IsBlock && node.Value is null) continue;
                var range = file.ValueRange(node);
                bool quoted = !node.IsBlock && node.Value is { Length: >= 2 } v && v[0] == '"' && v[^1] == '"';
                string ReadValue() => node.IsBlock
                    ? string.Join(" ", Regex.Replace(file.Read(range), "#[^\\r\\n]*", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    : Unquote(file.Read(range));
                entry.Fields.Add(new WorldField
                {
                    Name = prefix + (nodes.Count == 1 ? key : $"{key} {i + 1}"), Category = "Properties",
                    ColorScale = key == "color" ? (entry.Kind == "Faith" || node.Head.Contains("hsv") ? 1 : 255) : null,
                    IsList = node.IsBlock,
                    Options = OptionsFor(entry, key, node),
                    Read = ReadValue,
                    Write = value =>
                    {
                        if (value == ReadValue()) return;
                        ValidateValue(entry, key, value, node);
                        file.Set(range, node.IsBlock ? " " + value.Trim() + " " : quoted ? Quote(value.Trim()) : value.Trim());
                        if (entry.Kind == "Character" && key is "culture" or "religion")
                            foreach (var bookmark in _bookmarkCharacters.GetValueOrDefault(entry.Key) ?? [])
                                if (bookmark.Node.Children.FirstOrDefault(n => n.Key == key && n.Value is not null) is { } field)
                                    bookmark.File.Set(bookmark.File.ValueRange(field), value.Trim());
                    },
                    Description = key == "color" ? "Three color components in the file's original RGB or HSV scale."
                        : "Existing CK3 keys; whitespace separates a list. Only this field is changed.",
                    Reset = () =>
                    {
                        file.RevertRange(range);
                        if (entry.Kind == "Character" && key is "culture" or "religion")
                            foreach (var bookmark in _bookmarkCharacters.GetValueOrDefault(entry.Key) ?? [])
                                if (bookmark.Node.Children.FirstOrDefault(n => n.Key == key && n.Value is not null) is { } field)
                                    bookmark.File.RevertRange(bookmark.File.ValueRange(field));
                    }
                });
            }
        }
    }

    /// <summary>
    /// The dropdown a field offers: this world's own keys for references into it, the generator's
    /// verified heraldry for arms, and the vanilla vocabulary — harvested from the game folder when
    /// the mod was opened — for the pillars, looks, doctrines and icons a culture or faith carries.
    /// Read at dropdown time, so a vocabulary harvested after the open still counts.
    /// </summary>
    private Func<IReadOnlyList<(string Key, string Label)>>? OptionsFor(WorldEntry entry, string key, GuiNode node)
    {
        static List<(string, string)> Plain(IEnumerable<string> keys) => keys.Select(k => (k, k)).ToList();
        List<(string, string)> Kind(string kind) => Entries.Where(e => e.Kind == kind).Select(e => (e.Key, e.Name)).ToList();
        List<(string, string)> Counties(WorldEntry? under) => Titles.Values
            .Where(t => t.Key.StartsWith("c_") && (under is null || IsUnder(t, under))).Select(t => (t.Key, t.Name)).ToList();
        var vocabulary = () => MapGen.VanillaVocabulary.Current;
        return key switch
        {
            "culture" => () => Kind("Culture"),
            "religion" => () => Kind("Faith"),
            "holding" => () => Plain(Holdings),
            "county" => () => Counties(null),
            "capital" => () => Counties(entry),
            "holy_site" => () => Kind("HolySite"),
            "pattern" => () => Plain(Emit.CoatOfArmsWriter.Patterns),
            "texture" => () => Plain(Emit.CoatOfArmsWriter.Emblems),
            "color1" or "color2" when entry.Kind == "CoatOfArms" => () => Plain(Emit.CoatOfArmsWriter.Colors),
            "ethos" => () => Plain(vocabulary()?.Ethos ?? []),
            "martial_custom" => () => Plain(vocabulary()?.MartialCustoms ?? []),
            "head_determination" => () => Plain(vocabulary()?.HeadDeterminations ?? []),
            "clothing_gfx" => () => Plain(Looks(l => l.ClothingGfx)),
            "unit_gfx" => () => Plain(Looks(l => l.UnitGfx)),
            "building_gfx" => () => Plain(Looks(l => l.BuildingGfx)),
            "coa_gfx" => () => Plain(Looks(l => l.CoaGfx)),
            "icon" => () => Plain(vocabulary()?.FaithIcons ?? []),
            "doctrine" => () => Plain(Unquote(node.Value ?? "").StartsWith("tenet_")
                ? vocabulary()?.Tenets ?? []
                : vocabulary()?.DoctrineGroups.Values.SelectMany(d => d) ?? []),
            _ => null,
        };
        IEnumerable<string> Looks(Func<MapGen.VanillaVocabulary.Look, string> pick)
            => (vocabulary()?.Looks ?? []).Select(pick).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Order(StringComparer.Ordinal);
    }

    private static bool IsUnder(WorldEntry title, WorldEntry ancestor)
    {
        for (var p = title.Parent; p is not null; p = p.Parent) if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    private void ValidateValue(WorldEntry entry, string key, string value, GuiNode node)
    {
        string kind = entry.Kind;
        if (key == "color")
        {
            var pieces = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            double max = kind == "Faith" || node.Head.Contains("hsv") ? 1 : 255;
            if (pieces.Length != 3 || pieces.Any(p => !double.TryParse(p, CultureInfo.InvariantCulture, out double d) || !double.IsFinite(d) || d < 0 || d > max))
                throw new ArgumentException($"Use three numbers between 0 and {max}.");
        }
        else if (key is "martial" or "prowess" or "diplomacy" or "intrigue" or "stewardship" or "learning")
        {
            if (!int.TryParse(value, out int skill) || skill < 0 || skill > 100)
                throw new ArgumentException("Use a skill between 0 and 100.");
        }
        else
        {
            if (!Regex.IsMatch(value.Trim(), node.IsBlock ? "^[A-Za-z0-9_.-]+(?:\\s+[A-Za-z0-9_.-]+)*$" : "^[A-Za-z0-9_.-]+$"))
                throw new ArgumentException("Use CK3 identifiers, without quotes or script punctuation.");
            if (key is "culture" or "religion")
            {
                string referencedKind = key == "culture" ? "Culture" : "Faith";
                if (!Entries.Any(e => e.Kind == referencedKind && e.Key == value.Trim()) && value.Trim() != Unquote(node.Value ?? ""))
                    throw new ArgumentException($"Choose a {referencedKind.ToLowerInvariant()} defined in this world.");
            }
            if (key == "holding" && !Holdings.Contains(value.Trim()))
                throw new ArgumentException(
                    $"Use one of: {string.Join(", ", Holdings)}. A holding has to match the "
                    + "government its county's ruler holds, which the generator converts as a set.");
            if (key is "county" or "capital")
            {
                if (!Titles.TryGetValue(value.Trim(), out var county) || !county.Key.StartsWith("c_"))
                    throw new ArgumentException("Choose a county defined in this world.");
                if (key == "capital" && !IsUnder(county, entry))
                    throw new ArgumentException($"The capital has to be a county inside {entry.Name}.");
            }
            if (key == "holy_site" && !HolySites.ContainsKey(value.Trim()))
                throw new ArgumentException("Choose a holy site defined in this world.");
            if (key is "pattern" or "texture" && !value.Trim().EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a .dds texture name from gfx/coat_of_arms.");
        }
    }

    private void ReadDefinition()
    {
        string path = Path.Combine(DirectoryPath, "map_data", "definition.csv");
        foreach (string line in File.ReadLines(path))
        {
            var p = line.TrimStart('\uFEFF').Split(';');
            if (p.Length < 4 || !int.TryParse(p[0], out int id) || id < 1
                || !byte.TryParse(p[1], out byte r) || !byte.TryParse(p[2], out byte g) || !byte.TryParse(p[3], out byte b)) continue;
            if (!ProvinceByRgb.TryAdd((r << 16) | (g << 8) | b, id))
                throw new FormatException($"Duplicate province color in definition.csv at province {id}.");
        }
    }

    public string Localized(string key) => _loc.TryGetValue(key, out var loc) ? loc.File.Read(loc.Range) : key;
    public void Revert() { foreach (var file in Files) file.Revert(); }

    /// <summary>All preflight checks precede writes. Backups live outside the mod. Each replacement
    /// is atomic; failure rolls already committed files back and leaves edits pending.</summary>
    public int Save(string? backupRoot = null)
    {
        var changes = ChangedFiles.Select(f => (File: f, Bytes: f.Bytes())).ToList();
        if (changes.Count == 0) return 0;
        foreach (var file in Files)
            if (!File.Exists(file.Path) || !File.ReadAllBytes(file.Path).AsSpan().SequenceEqual(file.SavedBytes))
                throw new IOException($"Changed outside the editor: {file.Path}. Reopen the world to load the current files; nothing was saved.");
        foreach (var change in changes.Where(c => c.File.Script is not null))
            _ = GuiParser.Parse(System.Text.Encoding.UTF8.GetString(change.Bytes), change.File.Path);

        string backup = Path.Combine(backupRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ck3MapGen", "WorldBackups"),
            Path.GetFileName(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar)), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        var staged = new List<(EditableWorldFile File, byte[] Bytes, string Temp)>();
        var committed = new List<EditableWorldFile>();
        try
        {
            foreach (var change in changes)
            {
                string copy = Path.Combine(backup, Path.GetRelativePath(DirectoryPath, change.File.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.WriteAllBytes(copy, change.File.SavedBytes);
                string temp = change.File.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((change.File, change.Bytes, temp));
                File.WriteAllBytes(temp, change.Bytes);
            }
            foreach (var change in staged)
            {
                // Recheck immediately before replacement as well as during preflight.
                if (!File.ReadAllBytes(change.File.Path).AsSpan().SequenceEqual(change.File.SavedBytes))
                    throw new IOException($"Changed while saving: {change.File.Path}");
                File.Replace(change.Temp, change.File.Path, null);
                committed.Add(change.File);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var file in committed)
                try { File.WriteAllBytes(file.Path, file.SavedBytes); }
                catch (Exception rollback) { errors.Add(rollback); }
            if (errors.Count > 1) throw new AggregateException($"Save failed; restore originals from {backup}", errors);
            throw;
        }
        finally
        {
            foreach (var change in staged)
                if (File.Exists(change.Temp)) File.Delete(change.Temp);
        }
        foreach (var change in changes) change.File.Accept(change.Bytes);
        LastBackup = backup;
        return changes.Count;
    }

    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    private static string Quote(string value) => "\"" + value + "\"";
    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['"', '\\', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("Enter text without quotes, backslashes or line breaks.");
    }
}
