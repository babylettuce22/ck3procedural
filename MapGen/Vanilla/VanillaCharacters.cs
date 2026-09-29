using System.Text;
using Ck3MapGen.Core;
using Ck3MapGen.GameGui;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Seats vanilla's own historical characters on a world of vanilla titles: whoever held France in
/// vanilla's history on the start date rules France here, with their real name, house, traits,
/// parents and family. The second half of <see cref="VanillaTitles"/> — that pass decided which
/// titles and realms exist; this one decides who holds them.
///
/// <b>Who.</b> For each ruler the realm pass seated, the highest title they hold is looked up in
/// vanilla's title history on the start date; its holder, if alive and not already seated
/// elsewhere, becomes this ruler. Higher tiers choose first, so the king of France is Charles
/// before any of his counties can claim him. A ruler whose titles vanilla gives nobody (or only
/// someone already seated — a king's demesne county realised here as a separate count) stays a
/// generated character.
///
/// <b>Under the same id.</b> A historical ruler is written as <c>gen_char_N</c>, the id every other
/// file already uses for that seat's ruler — title history, bookmarks, wars, artifacts — so none of
/// them change. Only their own history block does, and their relatives, imported under their
/// vanilla ids, point back at the new one.
///
/// <b>Family.</b> Enough of the house to make it a house: ancestors up the male and female lines,
/// spouses, and everyone descended from the ruler's parents (siblings, nephews, children,
/// grandchildren) who is born by the start date. Vanilla's dynasty and house definitions are not
/// replaced by this mod, so their ids resolve and their arms and names come free.
///
/// <b>Clean.</b> Vanilla history speaks of a world this map is not. Every reference is checked:
/// a relative outside the import, a title this map lacks, any province id (vanilla's ids mean
/// nothing here) — the line goes, or the whole effect block it sits in. The generated family,
/// relations, claims and wars of a seat that becomes historical are removed, so a real king does
/// not arrive with an invented wife.
/// </summary>
public static class VanillaCharacters
{
    /// <summary>Generations of ancestors imported above each historical ruler.</summary>
    private const int AncestorDepth = 8;

    /// <summary>Generations imported below a ruler's parents: siblings, nephews, grand-nephews —
    /// and so the ruler's own children and grandchildren.</summary>
    private const int FamilyDepth = 3;

    /// <summary>
    /// History fields whose value is a character id. Measured, not guessed: every key vanilla's
    /// character history gives a numeric id to, less the ones that are amounts (gold, levies) —
    /// add_concubine was the one a guessed list missed.
    /// </summary>
    private static readonly HashSet<string> CharacterFields = new(StringComparer.Ordinal)
    {
        "father", "mother", "employer", "add_spouse", "add_matrilineal_spouse", "remove_spouse",
        "killer", "add_concubine", "remove_concubine", "real_father", "set_father", "set_mother",
    };

    /// <param name="cfgEraOffset">Years this world's calendar sits off vanilla's (<see cref="Config.MapConfig.EraOffset"/>).</param>
    public static int Import(VanillaTitles.Plan plan, VanillaCatalog catalog, RealmMap realms, RulerMap rulers,
        PrehistoryMap prehistory, CultureMap cultures, FaithMap faiths, List<Title> empires, int cfgEraOffset, Rng rng)
    {
        string date = plan.Date;
        var (holder, _) = VanillaTitles.HoldersAt(catalog, date);

        bool Alive(VanillaCatalog.CharacterDef c)
            => (c.Birth is null || VanillaCatalog.OnOrBefore(c.Birth, date))
               && (c.Death is null || !VanillaCatalog.OnOrBefore(c.Death, date));

        bool Born(VanillaCatalog.CharacterDef c) => c.Birth is not null && VanillaCatalog.OnOrBefore(c.Birth, date);

        // Which titles each seat's ruler holds, highest first.
        var heldBySeat = realms.HolderCounty
            .GroupBy(kv => kv.Value)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key)
                                            .OrderByDescending(Realms.Rank).ThenBy(t => t.Index).ToList());

        var chosen = new Dictionary<Ruler, VanillaCatalog.CharacterDef>();
        var seated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ruler in rulers.All.OrderByDescending(r => Realms.Rank(r.PrimaryTitle)).ThenBy(r => r.Seat.Index))
        {
            foreach (var title in heldBySeat.GetValueOrDefault(ruler.Seat) ?? [ruler.PrimaryTitle])
            {
                if (!title.Inherited || !holder.TryGetValue(title.Key, out string? id) || seated.Contains(id)) continue;
                if (!catalog.CharacterDefs.TryGetValue(id, out var def) || !Alive(def)) continue;
                chosen[ruler] = def;
                seated.Add(id);
                break;
            }
        }

        if (chosen.Count == 0)
        {
            Console.WriteLine("  vanilla characters: none of this map's titles had a living holder in vanilla's history on " + date);
            return 0;
        }

        // --- The family to import ---------------------------------------------------------------
        var include = new HashSet<string>(chosen.Values.Select(d => d.Id), StringComparer.Ordinal);
        var defs = catalog.CharacterDefs;

        var frontier = include.ToList();
        for (int depth = 0; depth < AncestorDepth && frontier.Count > 0; depth++)
        {
            var next = new List<string>();
            foreach (string id in frontier)
                foreach (string? parent in new[] { defs[id].Father, defs[id].Mother })
                    if (parent is not null && defs.ContainsKey(parent) && include.Add(parent)) next.Add(parent);
            frontier = next;
        }

        foreach (var ruler in chosen.Values)
        {
            var generation = new List<string>();
            foreach (string? parent in new[] { ruler.Father, ruler.Mother })
                if (parent is not null) generation.Add(parent);
            if (generation.Count == 0) generation.Add(ruler.Id);

            for (int depth = 0; depth < FamilyDepth && generation.Count > 0; depth++)
            {
                var next = new List<string>();
                foreach (string id in generation)
                    foreach (string child in catalog.ChildrenOf.GetValueOrDefault(id) ?? [])
                        if (defs.TryGetValue(child, out var c) && Born(c) && (include.Add(child) || depth == 0))
                            next.Add(child);
                generation = next;
            }

            foreach (string spouse in ruler.Spouses)
                if (defs.TryGetValue(spouse, out var s) && Born(s)) include.Add(spouse);
        }

        // Spouses of the ruler's children, so a married heir is not written a bachelor.
        foreach (var ruler in chosen.Values)
            foreach (string child in catalog.ChildrenOf.GetValueOrDefault(ruler.Id) ?? [])
                if (defs.TryGetValue(child, out var c))
                    foreach (string spouse in c.Spouses)
                        if (defs.TryGetValue(spouse, out var s) && Born(s)) include.Add(spouse);

        // --- Writing it out -----------------------------------------------------------------------
        var asRuler = chosen.ToDictionary(kv => kv.Value.Id, kv => kv.Key.Id, StringComparer.Ordinal);
        var titleKeys = Titles.Flatten(empires).Select(t => t.Key)
            .Concat(Titles.HegemonyOf(empires) is { } crown ? [crown.Key] : [])
            .Concat(faiths.Faiths.Where(f => f.Head is not null).Select(f => f.Head!.TitleKey))
            .ToHashSet(StringComparer.Ordinal);

        // The dynasties and houses they belong to, which the mod would otherwise have blanked with
        // the rest of vanilla's — and the dynasty of every house, which a house names. A house whose
        // dynasty cannot be shipped is not shipped either, and a reference to anything unshippable
        // is dropped from the character rather than left dangling (they become lowborn).
        var dynasties = include.Select(i => defs[i].Dynasty).OfType<string>()
                               .Where(catalog.DynastySource.ContainsKey)
                               .ToHashSet(StringComparer.Ordinal);
        var houses = include.Select(i => defs[i].House).OfType<string>()
                            .Where(h => catalog.HouseSource.ContainsKey(h)
                                        && catalog.HouseDynasty.GetValueOrDefault(h) is { } d && catalog.DynastySource.ContainsKey(d))
                            .ToHashSet(StringComparer.Ordinal);
        foreach (string h in houses) dynasties.Add(catalog.HouseDynasty[h]);

        // Vanilla's dates are on vanilla's calendar; this world's may be slid off it (an Azgaar
        // export's own year). Every imported date moves by the same amount, so a king born sixty
        // years before vanilla's start is born sixty years before this one.
        var cleaner = new Cleaner(include, asRuler, titleKeys, dynasties, houses, cfgEraOffset);

        foreach (string id in include.Where(i => !asRuler.ContainsKey(i)).OrderBy(i => i, StringComparer.Ordinal))
            prehistory.HistoricalCharacters.Add(cleaner.Block(defs[id], id, depth: 0));
        foreach (string d in dynasties.Order(StringComparer.Ordinal)) prehistory.HistoricalDynasties.Add(catalog.DynastySource[d]);
        foreach (string h in houses.Order(StringComparer.Ordinal)) prehistory.HistoricalHouses.Add(catalog.HouseSource[h]);

        // Arms for the rulers' own houses and dynasties when vanilla defines none — the bookmark
        // screen shows them, and a shield vanilla means to roll at game start is blank there.
        foreach (var def in chosen.Values)
            foreach (string? key in new[] { def.House, def.Dynasty })
                if (key is not null && (houses.Contains(key) || dynasties.Contains(key))
                    && !catalog.CoaKeys.Contains(key) && !prehistory.HistoricalCoaKeys.Contains(key))
                    prehistory.HistoricalCoaKeys.Add(key);

        // --- The rulers themselves ------------------------------------------------------------------
        var loc = catalog.Loc;
        var removedSeats = new HashSet<Title>();
        foreach (var (ruler, def) in chosen)
        {
            string body = cleaner.Body(def, depth: 1, ruler: true);
            string name = def.NameKey is { } key ? (loc?.Text(key) ?? key) : ruler.Name;
            var (culture, faith) = catalog.CharacterAt(def.Id, date) ?? (null, null);

            var born = BirthOf(def, ruler);
            if (def.Birth is not null) born = (born.Item1 + cfgEraOffset, born.Item2, born.Item3);
            string dynasty = def.Dynasty is { } d && dynasties.Contains(d) ? d : "";
            string house = def.House is { } h && houses.Contains(h) ? h : "";

            var replacement = ruler.AsHistorical(def.Id, body, name, def.NameKey, def.Female, born,
                EnsureCulture(culture, cultures, catalog, rng) ?? ruler.Culture,
                EnsureFaith(faith, faiths, cultures, catalog) ?? ruler.Faith,
                dynasty, house);
            rulers.Replace(ruler, replacement);
            removedSeats.Add(ruler.Seat);

            if (house.Length > 0) prehistory.CharacterHouseMap[ruler.Seat] = house;
            else prehistory.CharacterHouseMap.Remove(ruler.Seat);
            if (dynasty.Length > 0) prehistory.CharacterDynastyMap[ruler.Seat] = dynasty;
            else prehistory.CharacterDynastyMap.Remove(ruler.Seat);
        }

        int dropped = Unwind(prehistory, rulers, removedSeats, chosen.Keys.Select(r => r.Id).ToHashSet(StringComparer.Ordinal));

        Console.WriteLine($"  vanilla characters: {chosen.Count} of {rulers.All.Count} rulers are vanilla's own as of {date} " +
                          $"({string.Join(", ", chosen.OrderByDescending(kv => Realms.Rank(kv.Key.PrimaryTitle)).Take(8).Select(kv => $"{rulers.For(kv.Key.Seat).Name} of {kv.Key.PrimaryTitle.Name}"))}" +
                          (chosen.Count > 8 ? ", ..." : "") + $"), {prehistory.HistoricalCharacters.Count} relatives imported, " +
                          $"{dropped} generated relatives and relations removed; {cleaner.Dropped} references this world cannot resolve dropped");
        return chosen.Count;
    }

    /// <summary>
    /// Takes the generated family and relations off the seats that became historical: their
    /// spouses, children and deceased parents, everyone descended from those, every alliance,
    /// rivalry, friendship, truce, claim and war that names the seat, the noble-family titles held
    /// there, and any generated dynasty or house nobody is left in.
    /// </summary>
    private static int Unwind(PrehistoryMap prehistory, RulerMap rulers, HashSet<Title> seats, HashSet<string> rulerIds)
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seat in seats)
        {
            if (prehistory.Spouses.Remove(seat, out var spouse)) gone.Add(spouse.Id);
            if (prehistory.Children.Remove(seat, out var children)) foreach (var c in children) gone.Add(c.Id);
            if (prehistory.DeceasedParents.Remove(seat, out var parent)) gone.Add(parent.Id);
        }

        // Anyone whose father or mother is gone, or is a seat's replaced ruler, goes too — to a
        // fixed point, so a generated grandchild does not outlive the invented son between.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var c in prehistory.AllExtraCharacters)
                if (!gone.Contains(c.Id) && (c.FatherId is { } f && (gone.Contains(f) || rulerIds.Contains(f))
                                             || c.MotherId is { } m && (gone.Contains(m) || rulerIds.Contains(m))))
                {
                    gone.Add(c.Id);
                    changed = true;
                }
        }

        // But nobody a generated ruler still stands on: brothers ruling neighbouring seats share one
        // dead father, and taking him with the historical seat left the other brother's `father =`
        // pointing at nothing. Kept with every ancestor above them.
        var byId = prehistory.AllExtraCharacters.GroupBy(c => c.Id, StringComparer.Ordinal)
                             .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var keep = new Stack<string>(rulers.All.Where(r => !r.IsHistorical).Select(r => r.ParentId).OfType<string>()
            .Concat(prehistory.DeceasedParents.Values.Select(p => p.Id))
            .Concat(prehistory.Spouses.Where(kv => !gone.Contains(kv.Value.Id)).Select(kv => kv.Value.Id)));
        while (keep.Count > 0)
        {
            string id = keep.Pop();
            if (!gone.Remove(id) && !byId.ContainsKey(id)) continue;
            if (byId.TryGetValue(id, out var c))
                foreach (string? up in new[] { c.FatherId, c.MotherId })
                    if (up is not null && gone.Contains(up)) keep.Push(up);
        }

        int removed = prehistory.AllExtraCharacters.RemoveAll(c => gone.Contains(c.Id));

        // A generated ruler whose wife came from a replaced house keeps her; one whose wife is gone
        // loses the marriage rather than keep a dangling one.
        foreach (var seat in prehistory.Spouses.Where(kv => gone.Contains(kv.Value.Id)).Select(kv => kv.Key).ToList())
            prehistory.Spouses.Remove(seat);
        foreach (var (seat, list) in prehistory.Children.ToList())
            list.RemoveAll(c => gone.Contains(c.Id));

        bool Touches(Title county) => seats.Contains(county);

        foreach (var table in new[] { prehistory.Rivals, prehistory.Friends, prehistory.Nemeses, prehistory.BloodBrothers })
        {
            foreach (var seat in seats) removed += table.Remove(seat) ? 1 : 0;
            foreach (var list in table.Values) removed += list.RemoveAll(r => Touches(r.TargetCounty));
        }

        foreach (var seat in seats) removed += prehistory.Alliances.Remove(seat) ? 1 : 0;
        foreach (var list in prehistory.Alliances.Values)
            removed += list.RemoveAll(a => Touches(a.PartnerCounty)
                                           || a.ThroughSpouseId is { } s && gone.Contains(s)
                                           || a.ThroughPartnerId is { } p && gone.Contains(p));

        foreach (var seat in seats) removed += prehistory.Truces.Remove(seat) ? 1 : 0;
        foreach (var list in prehistory.Truces.Values) removed += list.RemoveAll(t => Touches(t.TargetCounty));

        foreach (var seat in seats) removed += prehistory.Claims.Remove(seat) ? 1 : 0;

        removed += prehistory.ActiveWars.RemoveAll(w => Touches(w.AttackerCounty) || Touches(w.DefenderCounty)
                                                        || w.ClaimantCounty is { } c && Touches(c));
        removed += prehistory.NobleFamilies.RemoveAll(f => Touches(f.HolderCounty));

        // Generated dynasties and houses nobody is left in. Rulers are counted directly: a
        // generated ruler's house is not always in the county-to-house map, which falls back to a
        // default key when prehistory had no entry.
        var houses = prehistory.AllExtraCharacters.Select(c => c.DynastyHouseKey).OfType<string>()
            .Concat(prehistory.CharacterHouseMap.Values)
            .Concat(prehistory.NobleFamilies.Select(f => f.HouseKey))
            .Concat(rulers.All.Select(r => r.HouseKey))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in prehistory.Houses.Keys.Where(k => !houses.Contains(k)).ToList()) prehistory.Houses.Remove(key);
        prehistory.HouseRelations.RemoveAll(r => !prehistory.Houses.ContainsKey(r.HouseA) || !prehistory.Houses.ContainsKey(r.HouseB));

        var dynasties = prehistory.AllExtraCharacters.Select(c => c.DynastyId)
            .Concat(prehistory.CharacterDynastyMap.Values)
            .Concat(rulers.All.Select(r => r.DynastyId))
            .Concat(prehistory.Houses.Values.Select(h => h.DynastyId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in prehistory.Dynasties.Keys.Where(k => !dynasties.Contains(k)).ToList()) prehistory.Dynasties.Remove(key);

        return removed;
    }

    private static (int, int, int) BirthOf(VanillaCatalog.CharacterDef def, Ruler fallback)
    {
        if (def.Birth?.Split('.') is [var y, var m, var d]
            && int.TryParse(y, out int year) && int.TryParse(m, out int month) && int.TryParse(d, out int day))
            return (year, Math.Clamp(month, 1, 12), Math.Clamp(day, 1, 28));
        return (fallback.BirthYear, fallback.BirthMonth, fallback.BirthDay);
    }

    /// <summary>
    /// The culture object for a historical ruler whose people hold no county here — a Norman king
    /// of English counties. Added to the map inherited and landless, so the ruler's own records
    /// and the bookmark name the culture vanilla gives them.
    /// </summary>
    private static Culture? EnsureCulture(string? key, CultureMap cultures, VanillaCatalog catalog, Rng rng)
    {
        if (key is null) return null;
        if (cultures.Cultures.FirstOrDefault(c => c.Key == key) is { } existing) return existing;
        if (!VanillaIdentities.Placeable(catalog).TryGetValue(key, out var def)) return null;
        var added = VanillaIdentities.Landless(def, cultures, catalog, rng);
        return added;
    }

    private static Faith? EnsureFaith(string? key, FaithMap faiths, CultureMap cultures, VanillaCatalog catalog)
    {
        if (key is null) return null;
        if (faiths.Faiths.FirstOrDefault(f => f.Key == key) is { } existing) return existing;
        return VanillaIdentities.LandlessFaith(key, faiths, cultures, catalog);
    }

    /// <summary>Rewrites vanilla history blocks for this world. See the class remarks.</summary>
    private sealed class Cleaner(HashSet<string> include, Dictionary<string, string> asRuler, HashSet<string> titleKeys,
        HashSet<string> dynasties, HashSet<string> houses, int yearShift)
    {
        public int Dropped { get; private set; }

        private static readonly System.Text.RegularExpressions.Regex TitleShaped =
            new(@"^[hekdcb]_[A-Za-z0-9_\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private string Id(string vanilla) => asRuler.GetValueOrDefault(vanilla, vanilla);

        /// <summary>A <c>Y.M.D</c> date moved onto this world's calendar; anything else unchanged.
        /// Quotes are kept as written.</summary>
        private string Shift(string token)
        {
            string bare = GuiNode.Unquote(token);
            if (yearShift == 0 || bare.Split('.') is not [var y, var m, var d] || !int.TryParse(y, out int year)
                || !int.TryParse(m, out _) || !int.TryParse(d, out _))
                return token;
            string moved = $"{year + yearShift}.{m}.{d}";
            return token.StartsWith('"') ? GuiNode.Quote(moved) : moved;
        }

        /// <summary>A whole character block under <paramref name="id"/>.</summary>
        public string Block(VanillaCatalog.CharacterDef def, string id, int depth)
        {
            var sb = new StringBuilder();
            sb.Append('\t', depth).Append(Id(id)).Append(" = {\n");
            sb.Append(Body(def, depth + 1, ruler: false));
            sb.Append('\t', depth).Append("}\n\n");
            return sb.ToString();
        }

        /// <summary>
        /// A block's contents without its braces. DNA always goes: the mod blanks vanilla's DNA
        /// records, so a reference to one would dangle. For a <paramref name="ruler"/>, whose block
        /// the character writer opens itself, the name and sex it writes on its own go too.
        /// </summary>
        public string Body(VanillaCatalog.CharacterDef def, int depth, bool ruler)
        {
            var root = GuiParser.Parse(def.Source).Roots.First(r => r.IsBlock);
            var sb = new StringBuilder();
            foreach (var child in root.Children)
            {
                if (child.Key == "dna" || ruler && child.Key is "name" or "female") continue;
                Emit(child, sb, depth, atom: false);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes one node. Inside an <paramref name="atom"/> (an effect, or any block below a date)
        /// nothing is fixed up line by line: a block that names something this world lacks is
        /// dropped whole, because half an effect is a different effect.
        /// </summary>
        private void Emit(GuiNode node, StringBuilder sb, int depth, bool atom)
        {
            if (node.IsBlock)
            {
                bool dated = node.Key.Length > 0 && char.IsDigit(node.Key[0]) && node.Key.Contains('.');
                bool open = dated || node.Key == "death";
                if (!open && !atom && !Resolvable(node)) { Dropped++; return; }

                var inner = new StringBuilder();
                foreach (var child in node.Children) Emit(child, inner, depth + 1, atom: !open);
                if (inner.Length == 0 && open && node.Children.Count > 0) return;

                var head = node.Head.Select(Rewrite).ToList();
                if (dated) head[0] = Shift(head[0]);
                sb.Append('\t', depth).Append(string.Join(' ', head));
                if (node.Head.Count > 0) sb.Append(' ');
                sb.Append("{\n").Append(inner).Append('\t', depth).Append("}\n");
                return;
            }

            if (node.Value is not null)
            {
                string value = GuiNode.Unquote(node.Value);

                // A house or dynasty this world does not ship would dangle; without it the
                // character is lowborn, which the game accepts.
                if (node.Key == "dynasty" && !dynasties.Contains(value) || node.Key == "dynasty_house" && !houses.Contains(value))
                {
                    Dropped++;
                    return;
                }

                // `birth = "871.1.1"`, `death = "900.8.13"`: dates, onto this world's calendar.
                // Vanilla has the odd `death = yrd`, neither a date nor a reason; it is a death.
                if (node.Key is "birth" or "death")
                {
                    string shifted = Shift(node.Value);
                    bool date = shifted != node.Value || GuiNode.Unquote(node.Value).Count(ch => ch == '.') == 2;
                    sb.Append('\t', depth).Append(node.Key).Append(" = ")
                      .Append(date || value == "yes" ? shifted : "yes").Append('\n');
                    return;
                }

                if (CharacterFields.Contains(node.Key))
                {
                    if (!include.Contains(value)) { Dropped++; return; }
                    sb.Append('\t', depth).Append(node.Key).Append(" = ").Append(Id(value)).Append('\n');
                    return;
                }
                if (!Resolvable(node)) { Dropped++; return; }
                sb.Append('\t', depth).Append(string.Join(' ', node.Head)).Append(' ').Append(Rewrite(node.Value)).Append('\n');
                return;
            }

            if (!Resolvable(node)) { Dropped++; return; }
            sb.Append('\t', depth).Append(string.Join(' ', node.Head.Select(Rewrite))).Append('\n');
        }

        /// <summary>Whether every reference anywhere in <paramref name="node"/> exists in this world.</summary>
        private bool Resolvable(GuiNode node)
        {
            foreach (string token in Tokens(node))
            {
                string t = GuiNode.Unquote(token);
                if (t.StartsWith("province:", StringComparison.Ordinal)) return false;

                // A dotted id (`character:menendez.12`) cannot be scoped to at all.
                if (t.StartsWith("character:", StringComparison.Ordinal) && (!include.Contains(t[10..]) || t.Contains('.'))) return false;
                if (t.StartsWith("title:", StringComparison.Ordinal) && !titleKeys.Contains(t[6..])) return false;

                // A title named bare — `capital = c_vingulmork` — is a title all the same.
                if (TitleShaped.IsMatch(t) && !titleKeys.Contains(t)) return false;
            }

            // A block that names a character or a place by a bare field this world cannot vouch for.
            if (node.IsBlock)
                foreach (var d in Descendants(node).Where(d => !d.IsBlock && d.Value is not null))
                    if (CharacterFields.Contains(d.Key) && !include.Contains(GuiNode.Unquote(d.Value!))) return false;
            return true;
        }

        private string Rewrite(string token)
        {
            int at = token.IndexOf("character:", StringComparison.Ordinal);
            if (at < 0) return token;
            int end = at + 10;
            while (end < token.Length && (char.IsLetterOrDigit(token[end]) || token[end] == '_')) end++;
            string id = token[(at + 10)..end];
            return token[..(at + 10)] + Id(id) + token[end..];
        }

        private static IEnumerable<string> Tokens(GuiNode node)
        {
            foreach (string h in node.Head) yield return h;
            if (node.Value is not null) yield return node.Value;
            foreach (var child in node.Children)
                foreach (string t in Tokens(child)) yield return t;
        }

        private static IEnumerable<GuiNode> Descendants(GuiNode node)
        {
            foreach (var child in node.Children)
            {
                yield return child;
                foreach (var d in Descendants(child)) yield return d;
            }
        }
    }
}
