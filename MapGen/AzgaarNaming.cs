using Ck3MapGen.Emit;
using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Decides which imported name belongs on which of our objects.
///
/// Separate from <see cref="AzgaarImport"/> on purpose. That class answers questions about the map —
/// which state covers this county, which burg sits in it — and this one turns those answers into
/// policy: that a kingdom is named after a state and a duchy after a province, that a county would
/// rather be named after its own market town than after the province it sits in, and that no name
/// is ever used twice. The policy is the part most likely to want changing; the measurements are
/// not.
///
/// Every method here is best-effort. Azgaar names a few hundred things and our generator wants
/// names for several thousand, so most titles will still be named by the generator — with, by this
/// point, a language built from Azgaar's own corpus, which is what stops the two halves sounding
/// like different maps. Nothing here is allowed to fail a run: a name that cannot be borrowed is
/// simply not borrowed.
/// </summary>
public static class AzgaarNaming
{
    /// <summary>
    /// How much of a title an Azgaar object has to cover before its name goes on it.
    ///
    /// Low, and deliberately so. Each object has already been matched to the title it covers *most*
    /// by the time this applies, so the threshold is not choosing between candidates — it is only
    /// throwing out matches where the winner covers so little that the name would be actively
    /// misleading. Set it high and the map keeps its invented names while Azgaar's go unused.
    /// </summary>
    private const double MinimumShare = 0.25;

    /// <summary>
    /// Drops a leading article from an imported name.
    ///
    /// Azgaar writes plenty of them — "the Sunlit Path", "the Sundering Sea" — and CK3 does not.
    /// The game builds its own phrases around a title or faith name, so an article baked into the
    /// name itself resurfaces as "the Sunlit Paths" in the adherent plural and "Eastern the
    /// Sundering Sea" once a qualifier goes in front of it.
    /// </summary>
    internal static string StripArticle(string name)
        => name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? name[4..].Trim() : name;

    /// <summary>
    /// Drops parenthetical qualifiers from an export name — "Rohand (Human) Beliefs" is how a
    /// fantasy preset tags a culture's race in Azgaar's own UI, and the tag reads as debug text in
    /// the middle of a CK3 tooltip.
    /// </summary>
    internal static string StripParenthetical(string name)
    {
        int open;
        while ((open = name.IndexOf('(')) >= 0)
        {
            int close = name.IndexOf(')', open);
            if (close < 0) break;
            name = name[..open] + name[(close + 1)..];
        }

        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The race a fantasy-preset export tags a culture with, read from the parenthetical before
    /// <see cref="StripParenthetical"/> throws it away — "Dunirr (Dwarven)" is the export telling
    /// us these are dwarves, and it beats guessing the same fact back from terrain affinity.
    ///
    /// Matched against the parenthetical's content only, never the whole name: a culture is free
    /// to be *called* Orkadal without being orcs. "Dark elf" is tested before "elf" because it
    /// contains it. A tag with no counterpart in our roster ("Arachnid", "Serpents", "Draconic")
    /// returns null rather than inventing a race for it — the ethnicity builder then falls back to
    /// guessing from terrain, which is a better answer than a random look. Null is also what an
    /// untagged culture returns, which is every culture on a non-fantasy export.
    /// </summary>
    internal static RaceArchetype? ParseRace(string name)
    {
        int open = name.IndexOf('(');
        if (open < 0) return null;

        int close = name.IndexOf(')', open);
        if (close < 0) return null;

        string tag = name[(open + 1)..close].Trim().ToLowerInvariant();
        if (tag.Length == 0) return null;

        if (tag.Contains("human")) return RaceArchetype.Human;
        if (tag.Contains("dark elf") || tag.Contains("drow")) return RaceArchetype.Deepkin;
        if (tag.Contains("elf") || tag.Contains("elv")) return RaceArchetype.HighElf;
        if (tag.Contains("dwarf") || tag.Contains("dwarv")) return RaceArchetype.Dwarf;
        if (tag.Contains("orc") || tag.Contains("ork") || tag.Contains("goblin")) return RaceArchetype.Orc;
        if (tag.Contains("gnom") || tag.Contains("halfling") || tag.Contains("hobbit")) return RaceArchetype.Gnome;
        if (tag.Contains("giant")) return RaceArchetype.Giantkin;
        return null;
    }

    /// <summary>
    /// Drops the form word from a full name when CK3 is going to say it anyway.
    ///
    /// The game renders a kingdom called "Kingdom of Fortimia" as "Kingdom of Kingdom of Fortimia".
    /// Only an exact match on the tier's own word is stripped, though — a Thearchy, a League, a
    /// Council, a Principality and a Grand Duchy are all kingdoms or duchies as far as CK3 is
    /// concerned, and those words are the whole reason the full name is worth carrying over.
    /// </summary>
    private static string Deduplicate(string full, string tier, string bare)
    {
        string word = tier switch
        {
            "e" => "Empire", "k" => "Kingdom", "d" => "Duchy", "c" => "County", _ => "",
        };

        if (word.Length == 0) return full;

        return full.StartsWith($"{word} of ", StringComparison.OrdinalIgnoreCase)
            || full.Equals($"{bare} {word}", StringComparison.OrdinalIgnoreCase)
                ? bare
                : full;
    }

    /// <summary>
    /// Works out a name for every title Azgaar has something to say about.
    ///
    /// Assignment is greedy across each tier at once rather than title by title as the hierarchy is
    /// walked. That matters because names are exclusive: walking depth-first hands a state's name to
    /// whichever of its kingdoms happens to be visited first, which is an accident of clustering,
    /// where sorting by coverage hands it to the kingdom that actually contains most of that state.
    ///
    /// The tiers are done in the order they compete for the same objects — states are consumed by
    /// empires and kingdoms, provinces by duchies and counties, burgs by counties and then by
    /// baronies — so the larger title gets first refusal and the smaller ones take what is left.
    /// </summary>
    public static Dictionary<Title, string> TitleNames(AzgaarImport azgaar, List<Title> empires,
        Dictionary<(string Culture, string Government), string>? tierForms = null,
        CultureMap? cultures = null, Dictionary<int, string>? stateGovernments = null)
    {
        var names = new Dictionary<Title, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claimedBurgs = new HashSet<int>();

        var all = Titles.Flatten(empires).ToList();
        var byTier = all.GroupBy(t => t.Tier).ToDictionary(g => g.Key, g => g.ToList());

        // The titles whose form word the tier will say for them. Same list the flavorization rules
        // are written from, so a state cannot end up with the word in both places or in neither.
        var formOwners = TitleTierWriter.StateForms(azgaar).Select(f => f.Title).ToHashSet();

        // The title each state actually became takes that state's *full* name — "Moskor Theocracy",
        // "Thearchy of Mever", "Alorenil League" — rather than the bare "Moskor".
        //
        // Azgaar's full name is the country's name as its own map draws it, and the form word in it
        // carries meaning our tiers cannot: a Thearchy, a League and a Grand Duchy are all kingdoms
        // to CK3. Reading it off the state-to-title map rather than by coverage is what makes this
        // exact — the share-based passes below only know which state covers a title *most*, which is
        // right for everything else and one degree too loose for the country's own name.
        foreach (var (id, title) in azgaar.StateTitles.OrderBy(kv => kv.Key))
        {
            if (azgaar.World.State(id) is not { } state) continue;

            // The tier already says the form, so the name must not say it too — CK3 would render
            // "United Provinces of United Provinces of Ryalos". Emit/TitleTierWriter writes one
            // flavorization rule per state title, so the word is carried there for every state that
            // has one, and reading the same list is what keeps the two from disagreeing.
            //
            // The tier is the better of the two places for it: it declines properly, it appears
            // everywhere the game names the title, and it applies to whoever holds the title rather
            // than being baked into a string.
            bool tierCarriesForm = formOwners.Contains(title);

            string full = tierCarriesForm
                ? StripArticle(state.Name)
                : StripArticle(state.FullName is { Length: > 0 } f ? f : state.Name);

            full = Deduplicate(full, title.Tier, state.Name);
            if (full.Length == 0 || !used.Add(full)) continue;

            names[title] = full;
        }

        // Every title that *is* a country has already been named above, so anything reaching these
        // passes is a title our own grouping invented — a synthetic empire over several states, or a
        // duchy cut out of a state. Those take the bare name of whatever covers them most, never the
        // ceremonial one: an empire spanning four countries called "Principality of Kehl" claims to
        // be one of them.
        AssignByShare(Tier("e"), b => b.State, id => azgaar.World.State(id)?.Name);
        AssignByShare(Tier("k"), b => b.State, id => azgaar.World.State(id)?.Name);
        AssignByShare(Tier("d"), b => b.Province, id => azgaar.World.Province(id)?.Name);

        // A county is a place people live in, so it would rather be named after the town in it than
        // after the administrative division it belongs to. The province's ceremonial name is the
        // fallback for counties with no town.
        AssignBurgs(Tier("c"));
        AssignByShare(Tier("c"), b => b.Province, id => azgaar.World.Province(id)?.FullName);

        // Baronies take whatever burgs the counties did not.
        AssignBurgs(Tier("b"));

        return names;

        List<Title> Tier(string tier) => byTier.TryGetValue(tier, out var list) ? list : [];

        // Sorts every (title, object) pairing in a tier by how much of the title the object covers,
        // then walks the list handing each object to the best title still without a name.
        void AssignByShare(List<Title> titles, Func<AzgaarBinding, AzgaarShare> pick,
                           Func<int, string?> nameOf)
        {
            var candidates = new List<(Title Title, int Id, double Share)>();

            foreach (var title in titles)
            {
                if (names.ContainsKey(title)) continue;
                if (azgaar.For(title) is not { } binding) continue;

                var share = pick(binding);
                if (!share.Exists || share.Share < MinimumShare) continue;

                candidates.Add((title, share.Id, share.Share));
            }

            var taken = new HashSet<int>();
            foreach (var (title, id, _) in candidates
                         .OrderByDescending(c => c.Share)
                         .ThenBy(c => c.Title.Index))
            {
                if (names.ContainsKey(title) || !taken.Add(id)) continue;

                string? name = nameOf(id);
                if (string.IsNullOrWhiteSpace(name)) continue;

                name = StripArticle(name);
                if (!used.Add(name)) continue;

                names[title] = name;
            }
        }

        // Burgs are points, not areas, so there is nothing to weigh — a burg is either inside a
        // title or it is not. Capitals and large towns come first within each title, which is what
        // makes a county take its name from its market town rather than from a hamlet.
        void AssignBurgs(List<Title> titles)
        {
            foreach (var title in titles.OrderBy(t => t.Index))
            {
                if (names.ContainsKey(title)) continue;
                if (azgaar.For(title) is not { } binding) continue;

                foreach (var burg in binding.Burgs)
                {
                    if (string.IsNullOrWhiteSpace(burg.Name)) continue;
                    if (!claimedBurgs.Add(burg.I)) continue;

                    string burgName = StripArticle(burg.Name);
                    if (!used.Add(burgName)) continue;

                    names[title] = burgName;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Renames generated cultures after the Azgaar cultures whose ground they occupy.
    ///
    /// Only the display name moves. <see cref="Culture.Key"/> is what every other file in the mod
    /// points at — name lists, history, localisation, the culture's own definition — and rewriting
    /// it here would mean rewriting all of them, so it stays exactly as generated.
    ///
    /// Which counties belong to which culture is still ours to decide in this tier; this only
    /// relabels the result. The two disagree wherever our region growth split a culture Azgaar kept
    /// whole, which is why the share is checked and the greedy pass gives each Azgaar culture to the
    /// one generated culture that covers most of it.
    /// </summary>
    public static int RenameCultures(AzgaarImport azgaar, CultureMap cultures)
    {
        var used = new HashSet<string>(
            cultures.Cultures.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        var candidates = cultures.Cultures
            .Select(c => (Culture: c, Share: azgaar.Across(c.Counties, b => b.Culture)))
            .Where(c => c.Share.Exists && c.Share.Share >= MinimumShare)
            .OrderByDescending(c => c.Share.Share)
            .ToList();

        var taken = new HashSet<int>();
        int renamed = 0;

        foreach (var (culture, share) in candidates)
        {
            if (!taken.Add(share.Id)) continue;
            if (azgaar.World.Culture(share.Id) is not { } source) continue;
            if (string.IsNullOrWhiteSpace(source.Name)) continue;

            // The race tag is read before the parenthetical carrying it is stripped for display —
            // the tag is data (these people are dwarves), the parenthetical is Azgaar's UI showing
            // that data, and only the second has no place in a CK3 tooltip.
            culture.ImportedArchetype = ParseRace(source.Name) ?? culture.ImportedArchetype;

            string name = StripParenthetical(StripArticle(source.Name));
            if (name.Length == 0) continue;

            // The generated name is freed as the imported one is claimed, so a later culture can
            // still be called what this one used to be.
            used.Remove(culture.Name);
            if (!used.Add(name)) continue;

            culture.Name = name;
            renamed++;
        }

        return renamed;
    }

    /// <summary>
    /// Renames generated faiths after the Azgaar religions covering the same ground, and the
    /// religions above them after those religions' <em>form</em>.
    ///
    /// The split is what makes this read correctly in game. Azgaar's <c>name</c> is a specific
    /// belief — "Ilmarism", "the Sunlit Path" — which is a CK3 faith; its <c>form</c> is the
    /// tradition that belief belongs to — "Shamanism", "Monotheism" — which is a CK3 religion.
    /// Putting the specific name on both levels produces the "Ilmarism faith of the Ilmarism
    /// religion" that every hand-made pantheon mod eventually stops doing.
    /// </summary>
    public static (int Faiths, int Religions) RenameFaiths(AzgaarImport azgaar, FaithMap faiths)
    {
        var used = new HashSet<string>(
            faiths.Faiths.Select(f => f.Name).Concat(faiths.Religions.Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);

        var candidates = faiths.Faiths
            .Select(f => (Faith: f, Share: azgaar.Across(f.Counties, b => b.Religion)))
            .Where(f => f.Share.Exists && f.Share.Share >= MinimumShare)
            .OrderByDescending(f => f.Share.Share)
            .ToList();

        var taken = new HashSet<int>();
        var sourceOf = new Dictionary<Faith, AzgaarReligion>();
        int renamedFaiths = 0;

        foreach (var (faith, share) in candidates)
        {
            if (!taken.Add(share.Id)) continue;
            if (azgaar.World.Religion(share.Id) is not { } source) continue;
            if (string.IsNullOrWhiteSpace(source.Name)) continue;

            sourceOf[faith] = source;

            string name = StripArticle(source.Name);

            used.Remove(faith.Name);
            if (!used.Add(name)) continue;

            faith.Name = name;
            renamedFaiths++;
        }

        int renamedReligions = 0;
        foreach (var religion in faiths.Religions)
        {
            // The religion takes its name from whichever of its faiths brought the most ground with
            // it, since that is the tradition the rest of them are variants of.
            var lead = religion.Faiths
                .Where(sourceOf.ContainsKey)
                .OrderByDescending(f => f.Counties.Count)
                .FirstOrDefault();

            if (lead is null || sourceOf[lead].Form is not { Length: > 0 } form) continue;

            used.Remove(religion.Name);
            if (!used.Add(form)) continue;

            religion.Name = form;
            renamedReligions++;
        }

        return (renamedFaiths, renamedReligions);
    }
}
