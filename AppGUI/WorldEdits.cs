using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Every pending change to a written mod, and what it would take to publish them.
///
/// Owned by the window and shared by every surface that touches it — the tree, the map, and each
/// inspector. They are all views of one edit session, and giving each its own state was the
/// alternative worth avoiding: a title renamed in one place and a culture recoloured in another
/// have to end up in the same overwrite.
///
/// The edits themselves live on the generated objects. This does not model them a second time; it
/// records what each one *was* so a revert has somewhere to go, and tracks which files have fallen
/// behind the objects.
///
/// Adding an editable type is a snapshot record and a mutate call. Everything else — reverting,
/// counting, dirty tracking, telling the views to refresh — is already type-agnostic.
/// </summary>
public sealed class WorldEdits
{
    /// <summary>
    /// What an object looked like before it was first touched.
    ///
    /// Typed per kind rather than a bag of reflected values: reverting has to put real fields back,
    /// and comparing has to know which fields count. Implementations are records, so the comparison
    /// is the compiler's.
    /// </summary>
    private interface ISnapshot
    {
        bool Differs();
        void Restore();

        /// <summary>Records the fields that differ from the snapshot into the overlay, keyed for re-application.</summary>
        void Capture(EditOverlay into);
    }

    private sealed record TitleSnapshot(Title Target, string Name, (byte R, byte G, byte B) Color,
        string? Form, string? Holder, string? HolderFemale) : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal)
            || Target.Color != Color
            || Target.Form != Form
            || Target.Holder != Holder
            || Target.HolderFemale != HolderFemale;

        public void Restore()
        {
            Target.Name = Name;
            Target.Color = Color;
            Target.Form = Form;
            Target.Holder = Holder;
            Target.HolderFemale = HolderFemale;
        }

        public void Capture(EditOverlay into)
        {
            var t = Target;
            into.Titles[t.Key] = new TitleEdit
            {
                Name = !string.Equals(t.Name, Name, StringComparison.Ordinal) ? t.Name : null,
                Color = t.Color != Color ? [t.Color.R, t.Color.G, t.Color.B] : null,
                Words = t.Form != Form || t.Holder != Holder || t.HolderFemale != HolderFemale
                    ? new TitleWords(t.Form, t.Holder, t.HolderFemale)
                    : null,
            };
        }
    }

    private sealed record CultureSnapshot(Culture Target, string Name, (byte R, byte G, byte B) Color,
            string Ethos, string MartialCustom, string HeadDetermination, List<string> Traditions,
            string CoaGfx, string BuildingGfx, string ClothingGfx, string UnitGfx,
            Dictionary<string, TitleVocabulary> RealmWords)
            : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal)
            || Target.Color != Color
            || Target.Ethos != Ethos
            || Target.MartialCustom != MartialCustom
            || Target.HeadDetermination != HeadDetermination
            || !Target.Traditions.SequenceEqual(Traditions)
            || Target.CoaGfx != CoaGfx
            || Target.BuildingGfx != BuildingGfx
            || Target.ClothingGfx != ClothingGfx
            || Target.UnitGfx != UnitGfx
            || !SameWords(Target.RealmWords, RealmWords);

        public void Restore()
        {
            Target.Name = Name;
            Target.Color = Color;
            Target.Ethos = Ethos;
            Target.MartialCustom = MartialCustom;
            Target.HeadDetermination = HeadDetermination;
            Target.Traditions = [.. Traditions];
            Target.CoaGfx = CoaGfx;
            Target.BuildingGfx = BuildingGfx;
            Target.ClothingGfx = ClothingGfx;
            Target.UnitGfx = UnitGfx;
            Target.RealmWords = new(RealmWords);
        }

        public void Capture(EditOverlay into)
        {
            var c = Target;

            // Carried across the replacement below. The look is a separate snapshot on the same
            // culture (see EthnicitySnapshot) and may already have written its field here.
            string? ethnicity = into.Cultures.TryGetValue(c.Key, out var existing) ? existing.Ethnicity : null;

            into.Cultures[c.Key] = new CultureEdit
            {
                Generated = Name,
                Ethnicity = ethnicity,
                Name = !string.Equals(c.Name, Name, StringComparison.Ordinal) ? c.Name : null,
                Color = c.Color != Color ? [c.Color.R, c.Color.G, c.Color.B] : null,
                Ethos = c.Ethos != Ethos ? c.Ethos : null,
                MartialCustom = c.MartialCustom != MartialCustom ? c.MartialCustom : null,
                HeadDetermination = c.HeadDetermination != HeadDetermination ? c.HeadDetermination : null,
                Traditions = !c.Traditions.SequenceEqual(Traditions) ? [.. c.Traditions] : null,
                CoaGfx = c.CoaGfx != CoaGfx ? c.CoaGfx : null,
                BuildingGfx = c.BuildingGfx != BuildingGfx ? c.BuildingGfx : null,
                ClothingGfx = c.ClothingGfx != ClothingGfx ? c.ClothingGfx : null,
                UnitGfx = c.UnitGfx != UnitGfx ? c.UnitGfx : null,
                RealmWords = !SameWords(c.RealmWords, RealmWords) ? new(c.RealmWords) : null,
            };
        }

        /// <summary>Same governments carrying the same words; the vocabulary is a record, so equal by value.</summary>
        private static bool SameWords(Dictionary<string, TitleVocabulary> a, Dictionary<string, TitleVocabulary> b)
            => a.Count == b.Count
            && a.All(kv => b.TryGetValue(kv.Key, out var words) && words == kv.Value);
    }

    /// <summary>
    /// A culture's look, which is a second independent edit on the same object.
    ///
    /// It cannot be folded into <see cref="CultureSnapshot"/>, because what changes is not a field
    /// on the culture — it is which <see cref="EthnicityDef"/> the map points at, and the culture
    /// itself is untouched. It therefore needs its own entry in <c>_originals</c>, and it cannot be
    /// keyed on the culture: that key is already the culture snapshot's, and the second write would
    /// silently evict the first, taking every pending rename and recolour with it. Hence
    /// <see cref="EthnicityKey"/>.
    /// </summary>
    private sealed record EthnicitySnapshot(
        Culture Target, EthnicityMap Map, EthnicityDef Def, List<(string Key, int Weight)> Variants)
        : ISnapshot
    {
        // By reference, not by template name: a retemplate always mints a fresh definition, and two
        // definitions naming the same vanilla template are still different looks once their hair
        // and eye variants have been redrawn.
        public bool Differs() => !ReferenceEquals(Map.For(Target), Def);

        public void Restore() => MapGen.Ethnicities.Assign(Map, Target, Def, Variants);

        /// <summary>
        /// Merges into the culture's overlay entry rather than replacing it, so this and
        /// <see cref="CultureSnapshot.Capture"/> can both run in either order without one
        /// discarding the other's fields. <c>Generated</c> is left alone when an entry already
        /// exists — the culture snapshot is the only one that knows the pre-rename name.
        /// </summary>
        public void Capture(EditOverlay into)
        {
            if (!into.Cultures.TryGetValue(Target.Key, out var edit))
                into.Cultures[Target.Key] = edit = new CultureEdit { Generated = Target.Name };

            // The chosen template, never the resulting gene blocks: the variants are redrawn from
            // an Rng, so a captured output would not replay. Replaying the choice regenerates them.
            edit.Ethnicity = Map.For(Target).BaseTemplate;
        }
    }

    /// <summary>
    /// The <c>_originals</c> key for a culture's look, distinct from the culture itself so the two
    /// snapshots coexist. A record, so a fresh instance looks up the one already stored.
    /// </summary>
    private sealed record EthnicityKey(Culture Culture);

    private sealed record FaithSnapshot(Faith Target, string Name, (double R, double G, double B) Color,
        string Icon, List<string> Tenets) : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal)
            || Target.Color != Color
            || Target.Icon != Icon
            || !Target.Tenets.SequenceEqual(Tenets);

        public void Restore()
        {
            Target.Name = Name;
            Target.Color = Color;
            Target.Icon = Icon;
            Target.Tenets = [.. Tenets];
        }

        public void Capture(EditOverlay into)
        {
            var f = Target;
            into.Faiths[f.Key] = new FaithEdit
            {
                Generated = Name,
                Name = !string.Equals(f.Name, Name, StringComparison.Ordinal) ? f.Name : null,
                Color = f.Color != Color ? [f.Color.R, f.Color.G, f.Color.B] : null,
                Icon = f.Icon != Icon ? f.Icon : null,
                Tenets = !f.Tenets.SequenceEqual(Tenets) ? [.. f.Tenets] : null,
            };
        }
    }

    /// <summary>
    /// Virtues and sins are held as copies and put back by mutating in place, because
    /// <see cref="Religion.Virtues"/> is init-only — the religion hands out the same list object
    /// for its whole life, and every reader holds that reference.
    /// </summary>
    private sealed record ReligionSnapshot(
        Religion Target, string Name, List<string> Virtues, List<string> Sins) : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal)
               || !Target.Virtues.SequenceEqual(Virtues)
               || !Target.Sins.SequenceEqual(Sins);

        public void Restore()
        {
            Target.Name = Name;
            Target.Virtues.Clear(); Target.Virtues.AddRange(Virtues);
            Target.Sins.Clear(); Target.Sins.AddRange(Sins);
        }

        public void Capture(EditOverlay into)
            => into.Religions[Target.Key] = new ReligionEdit
            {
                Generated = Name,
                Name = !string.Equals(Target.Name, Name, StringComparison.Ordinal) ? Target.Name : null,
                Virtues = !Target.Virtues.SequenceEqual(Virtues) ? [.. Target.Virtues] : null,
                Sins = !Target.Sins.SequenceEqual(Sins) ? [.. Target.Sins] : null,
            };
    }

    /// <summary>
    /// The profile is held whole rather than field by field: it is an immutable record, so every
    /// edit to it is a <c>with</c> that leaves the generated one untouched, and restoring is handing
    /// it back.
    /// </summary>
    private sealed record RulerSnapshot(Ruler Target, string Name, bool Female,
        int BirthYear, int BirthMonth, int BirthDay, RulerProfile Profile,
        int Gold, int Prestige, int Renown) : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal)
            || Target.Female != Female
            || Target.BirthYear != BirthYear
            || Target.BirthMonth != BirthMonth
            || Target.BirthDay != BirthDay
            || !ReferenceEquals(Target.Profile, Profile)
            || Target.Gold != Gold
            || Target.Prestige != Prestige
            || Target.Renown != Renown;

        public void Restore()
        {
            Target.Name = Name;
            Target.Female = Female;
            Target.BirthYear = BirthYear;
            Target.BirthMonth = BirthMonth;
            Target.BirthDay = BirthDay;
            Target.Profile = Profile;
            Target.Gold = Gold;
            Target.Prestige = Prestige;
            Target.Renown = Renown;
        }

        public void Capture(EditOverlay into)
        {
            var r = Target;
            into.Rulers[r.Id] = new RulerEdit
            {
                Generated = Name,
                Name = !string.Equals(r.Name, Name, StringComparison.Ordinal) ? r.Name : null,
                Female = r.Female != Female ? r.Female : null,
                BirthYear = r.BirthYear != BirthYear ? r.BirthYear : null,
                Profile = !ReferenceEquals(r.Profile, Profile) ? r.Profile : null,
                Gold = r.Gold != Gold ? r.Gold : null,
                Prestige = r.Prestige != Prestige ? r.Prestige : null,
                Renown = r.Renown != Renown ? r.Renown : null,
            };
        }
    }

    /// <summary>
    /// A realm's government: which government each of its counties is on, and the holdings that
    /// seat their rulers.
    ///
    /// Both halves, because they are one change. Each government names exactly one
    /// <c>primary_holding</c>, so moving a realm onto another one moves every capital holding with
    /// it, and putting the government back without putting the holdings back would leave a world
    /// the edit had quietly rearranged.
    ///
    /// The counties are held individually rather than as one word for the realm because they were
    /// never uniform: a coastal city and a steppe march are decided county by county on top of
    /// whatever their sovereign got, and restoring them all to the realm's word would erase that.
    /// </summary>
    private sealed record GovernmentSnapshot(
        GovernmentMap Map, Title Seat, Title Primary,
        Dictionary<Title, string> Counties,
        Dictionary<int, string> Holdings, Dictionary<int, string> Live,
        bool WasAdministrative, bool WasNomad) : ISnapshot
    {
        public bool Differs()
            => Counties.Any(kv => !string.Equals(Map.For(kv.Key), kv.Value, StringComparison.Ordinal));

        public void Restore()
        {
            foreach (var (county, government) in Counties) Map.Set(county, government);
            foreach (var (province, holding) in Holdings) Live[province] = holding;
            Map.MarkRealm(Primary, WasAdministrative, WasNomad);
        }

        // The seat's government stands for the realm's: SetGovernment writes one word across the
        // whole realm, and where a vassal has since been moved off it, the vassal's own entry is
        // what carries that.
        public void Capture(EditOverlay into) => into.Governments[Seat.Key] = Map.For(Seat);
    }

    /// <summary>
    /// The <c>_originals</c> key for a realm's government, distinct from the seat county itself so
    /// that it and the county's own <see cref="TitleSnapshot"/> coexist — the same arrangement, and
    /// for the same reason, as <see cref="EthnicityKey"/>.
    /// </summary>
    private sealed record GovernmentKey(Title Seat);

    /// <summary>
    /// Keyed by the object itself. Every generated type here is a class with reference equality,
    /// which is exactly the identity wanted — two cultures with the same name are still two
    /// cultures.
    /// </summary>
    private readonly Dictionary<object, ISnapshot> _originals = [];

    private GenerationResult? _result;
    private WrittenContent? _written;
    private string? _modDir;

    /// <summary>
    /// Which files are behind the objects, rather than which objects differ from their generated
    /// values.
    ///
    /// The two are different questions and only this one decides what an overwrite has to do.
    /// Reverting an already-published rename takes the changed count back to zero while leaving the
    /// file on disk still holding the edit, so gating on the count would strand that revert.
    /// </summary>
    private WorldAspect _pending;

    /// <summary>
    /// Raised on anything that changes what the views should be showing, carrying what was touched.
    ///
    /// The argument is what lets the window redraw a map only when something it paints has moved.
    /// Every map view is a full-map pass, and re-rendering on each keystroke of a rename would make
    /// typing feel like the tool had hung.
    /// </summary>
    public event Action<WorldAspect>? Changed;

    public bool IsLoaded => _result is not null && _written is not null && _modDir is not null;

    public (GenerationResult Result, WrittenContent Written, string ModDir)? Target
        => _result is not null && _written is not null && _modDir is not null
            ? (_result, _written, _modDir)
            : null;

    public WorldAspect Pending => IsLoaded ? _pending : WorldAspect.None;
    public bool HasPending => Pending != WorldAspect.None;

    public int EditedCount => _originals.Values.Count(s => s.Differs());

    // A culture carries two independent snapshots — its own fields, and the look the map points at
    // — so both of these have to ask about the second one as well, or a culture whose only change
    // is its ethnicity reads as unedited and offers no revert. A title does the same for the
    // government of the realm it belongs to.
    public bool WasEdited(object target)
        => (_originals.TryGetValue(target, out var snapshot) && snapshot.Differs())
        || (target is Culture c && _originals.TryGetValue(new EthnicityKey(c), out var eth) && eth.Differs())
        || (GovernmentKeyOf(target) is { } gov && _originals.TryGetValue(gov, out var rule) && rule.Differs());

    public bool CanRevert(object target)
        => _originals.ContainsKey(target)
        || (target is Culture c && _originals.ContainsKey(new EthnicityKey(c)))
        || (GovernmentKeyOf(target) is { } gov && _originals.ContainsKey(gov));

    /// <summary>
    /// Where a title's government edit is filed: under the seat of whoever holds it, so that every
    /// title of one realm — the empire, the duchy inside it, the county the man actually sits in —
    /// reaches the same entry, whichever of them was inspected when the change was made.
    ///
    /// Null for a title nobody holds, which is what a de-jure-only duchy is, and before a write.
    /// </summary>
    private GovernmentKey? GovernmentKeyOf(object target)
    {
        if (target is not Title title || _written?.Realms is not { } realms) return null;

        return realms.HolderCounty.TryGetValue(title, out var seat) ? new GovernmentKey(seat)
            : title.Tier == "c" ? new GovernmentKey(title)
            : null;
    }

    public void Attach(GenerationResult result, WrittenContent written, string modDir)
    {
        _result = result;
        _written = written;
        _modDir = modDir;

        _originals.Clear();

        // The write that just finished used exactly these values, so disk and memory agree.
        _pending = WorldAspect.None;
        Changed?.Invoke(WorldAspect.None);
    }

    public void Detach()
    {
        _result = null;
        _written = null;
        _modDir = null;

        _originals.Clear();
        _pending = WorldAspect.None;
        Changed?.Invoke(WorldAspect.None);
    }

    public void MarkWritten()
    {
        _pending = WorldAspect.None;
        Changed?.Invoke(WorldAspect.None);
    }

    // --- Names --------------------------------------------------------------------------------

    /// <summary>
    /// Characters that would break a localisation file.
    ///
    /// <see cref="Io.ParadoxText.Loc"/> escapes quotes and folds line breaks on the way out, so
    /// none of these can actually corrupt it. They are refused anyway because a name silently
    /// rewritten between typing it and reading it in game is worse than one that was not accepted —
    /// and a backslash reaches the file untouched, so a trailing one would escape the closing quote.
    /// </summary>
    private static readonly char[] Forbidden = ['"', '\\', '\r', '\n'];

    /// <summary>The reason a name cannot be used, or null if it can.</summary>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "This needs a name.";
        if (name.Trim().IndexOfAny(Forbidden) >= 0)
            return "A name cannot contain a quote or a backslash.";
        return null;
    }

    private static string Checked(string name)
    {
        name = name.Trim();
        return Validate(name) is { } problem ? throw new ArgumentException(problem, nameof(name)) : name;
    }

    // --- Editing ------------------------------------------------------------------------------
    //
    // Each of these snapshots the object on first touch, mutates it, and marks the files that have
    // fallen behind. Snapshotting once rather than per edit is what makes a revert go back to the
    // generated value rather than to the previous edit.

    public void Edit(Title title, Action<Title> change)
        => Apply(title, () => Snapshot(title), () => change(title),
            WorldAspect.TitleNames | WorldAspect.TitleColors);

    public void Rename(Title title, string name)
    {
        string checkedName = Checked(name);
        Apply(title, () => Snapshot(title),
            () => title.Name = checkedName, WorldAspect.TitleNames);
    }

    public void Recolor(Title title, (byte R, byte G, byte B) color)
        => Apply(title, () => Snapshot(title),
            () => title.Color = color, WorldAspect.TitleColors);

    public void EditCulture(Culture culture, Action<Culture> change)
        => Apply(culture, () => Snapshot(culture), () => change(culture), WorldAspect.Cultures);

    public void RenameCulture(Culture culture, string name)
    {
        string checkedName = Checked(name);
        Apply(culture, () => Snapshot(culture), () => culture.Name = checkedName, WorldAspect.Cultures);
    }

    /// <summary>The generated looks, for the inspector's dropdown. Null before a world is attached.</summary>
    public EthnicityMap? Ethnicities => _written?.Ethnicities;

    /// <summary>
    /// Moves one culture onto a different vanilla look.
    ///
    /// Only this culture: <see cref="MapGen.Ethnicities.Retemplate"/> forks a fresh definition
    /// rather than editing the shared one, so the heritage siblings that pointed at the same object
    /// keep the look they were generated with.
    ///
    /// A fresh seed each time, like <see cref="RecolorChildren"/> — the hair and eye variants are
    /// drawn inside the template's own palette, so reusing one seed would hand back the same
    /// distribution and make a second pick on the same template look like it had done nothing.
    ///
    /// Silently does nothing for a non-human culture or a template CK3 lacks; the inspector does
    /// not offer either, and this is the backstop for the overlay replay, which can carry a
    /// template into a world whose cultures came out differently.
    /// </summary>
    public void EditCultureEthnicity(Culture culture, string template)
    {
        if (Ethnicities is not { } map) return;
        if (map.For(culture).Archetype != RaceArchetype.Human) return;

        // Snapshotted before the change and only if it lands, so a refused retemplate leaves no
        // revert entry claiming an edit that never happened.
        var before = map.For(culture);
        var beforeVariants = new List<(string Key, int Weight)>(map.VariantsFor(culture));

        var rng = new Rng(Random.Shared.Next(1, int.MaxValue));
        var mode = _result?.Config.RaceMode ?? Config.MapConfig.FantasyRaceMode.HumanOnly;
        if (!MapGen.Ethnicities.Retemplate(map, culture, template, mode, rng)) return;

        var key = new EthnicityKey(culture);
        if (!_originals.ContainsKey(key))
            _originals[key] = new EthnicitySnapshot(culture, map, before, beforeVariants);

        _pending |= WorldAspect.Ethnicities;
        Changed?.Invoke(WorldAspect.Ethnicities);
    }

    public void EditFaith(Faith faith, Action<Faith> change)
        => Apply(faith, () => Snapshot(faith), () => change(faith), WorldAspect.Faiths);

    public void RenameFaith(Faith faith, string name)
    {
        string checkedName = Checked(name);
        Apply(faith, () => Snapshot(faith), () => faith.Name = checkedName, WorldAspect.Faiths);
    }

    /// <summary>
    /// A religion's virtues and sins, edited from the faith window because that is the only place a
    /// religion is reachable. It is shared, so this lands on every faith under it — the same as
    /// renaming one.
    /// </summary>
    public void EditReligion(Religion religion, Action<Religion> change)
        => Apply(religion, () => Snapshot(religion), () => change(religion), WorldAspect.Faiths);

    public void RenameReligion(Religion religion, string name)
    {
        string checkedName = Checked(name);
        Apply(religion, () => Snapshot(religion),
            () => religion.Name = checkedName, WorldAspect.Faiths);
    }

    /// <summary>
    /// The governments the written world is on. Null before a write and for a mod written with
    /// history skipped, which is when there is no realm to have one.
    /// </summary>
    public GovernmentMap? Governments => _written?.Governments;

    /// <summary>
    /// Moves a realm onto a different government.
    ///
    /// The whole realm — the ruler's own counties and every vassal's beneath them — because that is
    /// the unit <see cref="MapGen.Governments.Build"/> decides in: a government is chosen once per
    /// independent top liege and laid over everything inside it. A liege whose vassals kept the old
    /// one is a shape the generator never produces, and under a horde it is one the engine comes
    /// apart on: a nomad holding declares <c>required_heir_government_types</c>, so settled counts
    /// under a khan misinherit at the first succession. Changing one vassal alone is still
    /// possible — drill into them and change their realm, which is a smaller span of the same
    /// edit.
    ///
    /// The capital holding of every county follows, for the reason
    /// <see cref="GovernmentSnapshot"/> gives. A county's second holding is left as it was — a city
    /// under a new liege is still a city — except under a horde, whose counties are the camp and
    /// nothing else, the way the generator writes the steppe.
    ///
    /// Snapshotted once per realm, first touch wins, so a revert goes back to what was generated
    /// rather than to the previous edit. Two overlapping edits — an empire, then a duke inside it —
    /// are two entries, and reverting the empire's takes the duke's counties back with it.
    /// </summary>
    public void SetGovernment(Title seat, Title primary, IReadOnlyList<Title> realmCounties,
        string government)
    {
        if (_written is not { } written || written.Governments is not { } map) return;

        // The wilderness is held by its own immortal placeholder under a government of its own, and
        // it is not part of anybody's realm; this only guards against a caller that thinks it is.
        var counties = realmCounties.Where(c => !written.Wilderness.Contains(c)).ToList();
        if (counties.Count == 0) return;

        var key = new GovernmentKey(seat);
        if (!_originals.ContainsKey(key))
        {
            _originals[key] = new GovernmentSnapshot(map, seat, primary,
                counties.ToDictionary(c => c, map.For),
                Baronies(counties).ToDictionary(id => id, id => written.Holdings[id]),
                written.Holdings,
                map.IsAdminEmpire(primary), map.IsNomadRealm(primary));
        }

        string capital = GovernmentMap.CapitalHolding(government);

        // Baronies carrying a wonder or a Silk Road bazaar. A horde's counties are otherwise
        // emptied of their second holding, the way the generator writes the steppe — but never
        // these: the province history writer upgrades a bazaar's barony to a city precisely so a
        // special building is never left standing on ground with no holding under it, and the rest
        // of the mod points at both kinds from elsewhere. Measured, not assumed: without this a
        // realm turned nomadic stranded a changan_market on a holding-less barony.
        var special = written.ProvinceHistory
            .Where(r => r.SpecialSlot is not null)
            .Select(r => r.ProvinceId)
            .ToHashSet();

        foreach (var county in counties)
        {
            map.Set(county, government);

            // Seat first, matching the province history: index zero is the capital.
            var baronies = county.SeatFirst().ToList();
            for (int i = 0; i < baronies.Count; i++)
            {
                int province = baronies[i].ProvinceId;
                if (!written.Holdings.ContainsKey(province)) continue;

                if (i == 0) written.Holdings[province] = capital;
                else if (government == GovernmentMap.Nomad && !special.Contains(province))
                    written.Holdings[province] = "none";
            }
        }

        map.MarkRealm(primary,
            government == GovernmentMap.Administrative, government == GovernmentMap.Nomad);

        _pending |= WorldAspect.Governments;
        Changed?.Invoke(WorldAspect.Governments);

        // Only the baronies this map actually wrote a holding for; a barony absent from the table
        // is one no province history line covers, and inventing one for it would put a holding on
        // ground the mod says nothing about.
        IEnumerable<int> Baronies(IEnumerable<Title> of)
            => of.SelectMany(c => c.Children)
                 .Select(b => b.ProvinceId)
                 .Where(written.Holdings.ContainsKey);
    }

    public void EditRuler(Ruler ruler, Action<Ruler> change)
        => Apply(ruler, () => Snapshot(ruler), () => change(ruler), WorldAspect.Rulers);

    public void RenameRuler(Ruler ruler, string name)
    {
        string checkedName = Checked(name);
        Apply(ruler, () => Snapshot(ruler), () => ruler.Name = checkedName, WorldAspect.Rulers);
    }

    /// <summary>
    /// Moves a ruler's birth year, held inside <see cref="RulerBirthYearBounds"/>. The month and
    /// day are left alone — they were never anything but noise.
    /// </summary>
    public void SetRulerBirthYear(Ruler ruler, int year)
    {
        var (min, max) = RulerBirthYearBounds(ruler);
        int clamped = Math.Clamp(year, min, max);
        Apply(ruler, () => Snapshot(ruler), () => ruler.BirthYear = clamped, WorldAspect.Rulers);
    }

    /// <summary>
    /// The years a ruler can have been born in without contradicting the family prehistory built
    /// around the generated year: at least sixteen years after the father, at least sixteen before
    /// the wedding and before every child, and an adult at the start date. The engine would load a
    /// man younger than his son, but it would log it and the court would read as nonsense, so the
    /// editor refuses it rather than leaving the error for the game to find.
    /// </summary>
    public (int Min, int Max) RulerBirthYearBounds(Ruler ruler)
    {
        int start = _result?.Config.StartYear ?? ruler.BirthYear + 16;
        int min = start - 90;
        int max = start - 16;

        if (_written?.Prehistory is { } prehistory)
        {
            if (prehistory.DeceasedParents.TryGetValue(ruler.Seat, out var father))
                min = Math.Max(min, YearOf(father.BirthDate) + 16);

            if (prehistory.Spouses.TryGetValue(ruler.Seat, out var spouse) && spouse.MarriageDate is { } wedding)
                max = Math.Min(max, YearOf(wedding) - 16);

            if (prehistory.Children.TryGetValue(ruler.Seat, out var children))
                foreach (var child in children) max = Math.Min(max, YearOf(child.BirthDate) - 16);
        }

        // The generated year always satisfies all of these, so the range cannot actually be empty;
        // this only guards against a prehistory the rules above were not written for.
        if (min > max) min = max;
        return (min, max);

        static int YearOf(string date) => int.Parse(date.Split('.')[0]);
    }

    private static RulerSnapshot Snapshot(Ruler r)
        => new(r, r.Name, r.Female, r.BirthYear, r.BirthMonth, r.BirthDay, r.Profile,
               r.Gold, r.Prestige, r.Renown);

    /// <summary>
    /// A title's own word for itself and its holder's style. Its own aspect: only the
    /// flavorization files carry it, and a recolour should not drag them along.
    /// </summary>
    public void EditTitleWords(Title title, Action<Title> change)
        => Apply(title, () => Snapshot(title), () => change(title), WorldAspect.TitleWords);

    /// <summary>A culture's words for its realms, per government. See <see cref="EditTitleWords"/>.</summary>
    public void EditCultureWords(Culture culture, Action<Culture> change)
        => Apply(culture, () => Snapshot(culture), () => change(culture), WorldAspect.TitleWords);

    private static TitleSnapshot Snapshot(Title t)
        => new(t, t.Name, t.Color, t.Form, t.Holder, t.HolderFemale);

    private static CultureSnapshot Snapshot(Culture c)
        => new(c, c.Name, c.Color, c.Ethos, c.MartialCustom, c.HeadDetermination, [.. c.Traditions],
               c.CoaGfx, c.BuildingGfx, c.ClothingGfx, c.UnitGfx, new(c.RealmWords));
    private static FaithSnapshot Snapshot(Faith f) => new(f, f.Name, f.Color, f.Icon, [.. f.Tenets]);

    private static ReligionSnapshot Snapshot(Religion r)
        => new(r, r.Name, [.. r.Virtues], [.. r.Sins]);

    /// <summary>
    /// Re-derives every descendant title's colour from this one's current colour.
    ///
    /// Snapshots each of them first, so this stays as revertable as a single edit — it can touch
    /// thousands of titles at once and being unable to undo it would make it unusable.
    /// </summary>
    public void RecolorChildren(Title title)
    {
        var descendants = Titles.Flatten([title]).Where(t => t != title).ToList();
        if (descendants.Count == 0) return;

        foreach (var child in descendants)
            _originals.TryAdd(child, Snapshot(child));

        // A fresh seed each time: the spread is random within its tolerances, so reusing one would
        // hand back the same shades and the button would look like it had done nothing.
        Titles.RecolorChildren(title, new Rng(Random.Shared.Next(1, int.MaxValue)));

        _pending |= WorldAspect.TitleColors;
        Changed?.Invoke(WorldAspect.TitleColors);
    }

    private void Apply(object target, Func<ISnapshot> snapshot, Action change, WorldAspect aspects)
    {
        if (!_originals.ContainsKey(target)) _originals[target] = snapshot();

        change();

        _pending |= aspects;
        Changed?.Invoke(aspects);
    }

    // --- Carrying edits across worlds --------------------------------------------------------

    /// <summary>
    /// Every edit that currently differs from its generated value, as an overlay keyed for
    /// re-application — see <see cref="EditOverlay"/> for why only touched fields and why the
    /// generated name rides along.
    /// </summary>
    public EditOverlay Export(string? heightmap)
    {
        var overlay = new EditOverlay { Heightmap = heightmap };
        foreach (var snapshot in _originals.Values)
            if (snapshot.Differs()) snapshot.Capture(overlay);
        return overlay;
    }

    /// <summary>
    /// Lays an overlay over the attached world. Every edit whose object is generated again under
    /// the same key and name is made again through the ordinary edit methods, so it is snapshotted,
    /// revertable and pending like one typed in — the caller decides whether to push it to disk.
    /// Returns how many edits landed and how many had nothing to land on.
    /// </summary>
    public (int Applied, int Missed) Import(EditOverlay overlay)
    {
        if (!IsLoaded) return (0, overlay.Count);

        int applied = 0, missed = 0;

        var titles = Titles.Flatten(_result!.Titles).GroupBy(t => t.Key).ToDictionary(g => g.Key, g => g.First());
        foreach (var (key, edit) in overlay.Titles)
        {
            if (!titles.TryGetValue(key, out var title)) { missed++; continue; }
            if (!Try(() =>
            {
                if (edit.Name is { } name) Rename(title, name);
                if (edit.Color is { Length: 3 } c) Recolor(title, ((byte)c[0], (byte)c[1], (byte)c[2]));
                if (edit.Words is { } w)
                    EditTitleWords(title, t => { t.Form = w.Form; t.Holder = w.Holder; t.HolderFemale = w.HolderFemale; });
            })) { missed++; continue; }
            applied++;
        }

        var cultures = _written!.Cultures.Cultures.ToDictionary(c => c.Key);
        foreach (var (key, edit) in overlay.Cultures)
        {
            if (!cultures.TryGetValue(key, out var culture) || culture.Name != edit.Generated) { missed++; continue; }
            if (!Try(() =>
            {
                if (edit.Name is { } name) RenameCulture(culture, name);

                if (edit.Color is not null || edit.Ethos is not null || edit.MartialCustom is not null
                    || edit.HeadDetermination is not null || edit.Traditions is not null
                    || edit.CoaGfx is not null || edit.BuildingGfx is not null
                    || edit.ClothingGfx is not null || edit.UnitGfx is not null)
                {
                    EditCulture(culture, c =>
                    {
                        if (edit.Color is { Length: 3 } col) c.Color = ((byte)col[0], (byte)col[1], (byte)col[2]);
                        if (edit.Ethos is { } v1) c.Ethos = v1;
                        if (edit.MartialCustom is { } v2) c.MartialCustom = v2;
                        if (edit.HeadDetermination is { } v3) c.HeadDetermination = v3;
                        if (edit.Traditions is { } v4) c.Traditions = [.. v4];
                        if (edit.CoaGfx is { } v5) c.CoaGfx = v5;
                        if (edit.BuildingGfx is { } v6) c.BuildingGfx = v6;
                        if (edit.ClothingGfx is { } v7) c.ClothingGfx = v7;
                        if (edit.UnitGfx is { } v8) c.UnitGfx = v8;
                    });
                }

                if (edit.RealmWords is { } words) EditCultureWords(culture, c => c.RealmWords = new(words));

                // Last, and deliberately after the rename: EditCultureEthnicity names the new
                // definition after the culture's current name. It no-ops if this world made the
                // culture non-human, which is the right outcome — the race is the world's to
                // decide and an overlay may not override it.
                if (edit.Ethnicity is { } ethnicity) EditCultureEthnicity(culture, ethnicity);
            })) { missed++; continue; }
            applied++;
        }

        var faiths = _written.Faiths.Faiths.ToDictionary(f => f.Key);
        foreach (var (key, edit) in overlay.Faiths)
        {
            if (!faiths.TryGetValue(key, out var faith) || faith.Name != edit.Generated) { missed++; continue; }
            if (!Try(() =>
            {
                if (edit.Name is { } name) RenameFaith(faith, name);
                if (edit.Color is not null || edit.Icon is not null || edit.Tenets is not null)
                {
                    EditFaith(faith, f =>
                    {
                        if (edit.Color is { Length: 3 } col) f.Color = (col[0], col[1], col[2]);
                        if (edit.Icon is { } icon) f.Icon = icon;
                        if (edit.Tenets is { } tenets) f.Tenets = [.. tenets];
                    });
                }
            })) { missed++; continue; }
            applied++;
        }

        var religions = _written.Faiths.Religions.ToDictionary(r => r.Key);
        foreach (var (key, edit) in overlay.Religions)
        {
            if (!religions.TryGetValue(key, out var religion) || religion.Name != edit.Generated) { missed++; continue; }
            if (edit.Name is { } name && !Try(() => RenameReligion(religion, name))) { missed++; continue; }

            if (edit.Virtues is not null || edit.Sins is not null)
            {
                if (!Try(() => EditReligion(religion, r =>
                {
                    if (edit.Virtues is { } virtues) { r.Virtues.Clear(); r.Virtues.AddRange(virtues); }
                    if (edit.Sins is { } sins) { r.Sins.Clear(); r.Sins.AddRange(sins); }
                }))) { missed++; continue; }
            }

            applied++;
        }

        var rulers = (_written.Rulers?.All ?? []).ToDictionary(r => r.Id);
        foreach (var (key, edit) in overlay.Rulers)
        {
            if (!rulers.TryGetValue(key, out var ruler) || ruler.Name != edit.Generated) { missed++; continue; }
            if (!Try(() =>
            {
                if (edit.Name is { } name) RenameRuler(ruler, name);
                if (edit.Female is not null || edit.Profile is not null || edit.Gold is not null
                    || edit.Prestige is not null || edit.Renown is not null)
                {
                    EditRuler(ruler, r =>
                    {
                        if (edit.Female is { } female) r.Female = female;
                        if (edit.Profile is { } profile) r.Profile = profile;
                        if (edit.Gold is { } gold) r.Gold = gold;
                        if (edit.Prestige is { } prestige) r.Prestige = prestige;
                        if (edit.Renown is { } renown) r.Renown = renown;
                    });
                }
                if (edit.BirthYear is { } year) SetRulerBirthYear(ruler, year);
            })) { missed++; continue; }
            applied++;
        }

        // Last, because it reads the realm graph rather than one object, and the graph is built from
        // the world as attached — nothing above changes who holds what. A seat whose realm this
        // world did not grow, or a government this build does not offer, is a miss rather than a
        // half-applied change.
        if (overlay.Governments.Count > 0 && RealmGraph.Build(_written, _result) is { } graph)
        {
            foreach (var (key, government) in overlay.Governments)
            {
                if (!titles.TryGetValue(key, out var seat) || seat.Tier != "c"
                    || !GovernmentMap.Assignable.Contains(government))
                {
                    missed++;
                    continue;
                }

                var counties = graph.RealmCounties(seat);
                if (counties.Count == 0) { missed++; continue; }

                SetGovernment(seat, graph.Primary(seat), counties, government);
                applied++;
            }
        }
        else missed += overlay.Governments.Count;

        return (applied, missed);

        // A name that fails validation is the only way an edit can refuse; it was valid when typed,
        // so this guards against a hand-edited overlay file rather than anything the tool wrote.
        static bool Try(Action apply)
        {
            try { apply(); return true; }
            catch (ArgumentException) { return false; }
        }
    }

    // --- Reverting ----------------------------------------------------------------------------

    public void Revert(object target)
    {
        // Reverting a culture takes its look with it. The two are separate entries so that neither
        // evicts the other, but to the person clicking Revert on a culture they are one edit. A
        // title and its realm's government are the same arrangement.
        if (target is Culture culture) RevertOne(new EthnicityKey(culture));
        if (GovernmentKeyOf(target) is { } government) RevertOne(government);

        RevertOne(target);
    }

    private void RevertOne(object target)
    {
        if (!_originals.TryGetValue(target, out var snapshot)) return;

        var aspects = AspectsOf(target);
        if (snapshot.Differs()) _pending |= aspects;

        snapshot.Restore();
        _originals.Remove(target);
        Changed?.Invoke(aspects);
    }

    public void RevertAll()
    {
        var touched = WorldAspect.None;

        foreach (var (target, snapshot) in _originals)
        {
            if (!snapshot.Differs()) continue;
            touched |= AspectsOf(target);
            snapshot.Restore();
        }

        _pending |= touched;
        _originals.Clear();
        Changed?.Invoke(touched);
    }

    /// <summary>Which files an object of this kind can dirty.</summary>
    private static WorldAspect AspectsOf(object target) => target switch
    {
        Title => WorldAspect.TitleNames | WorldAspect.TitleColors | WorldAspect.TitleWords,
        Culture => WorldAspect.Cultures | WorldAspect.TitleWords,
        EthnicityKey => WorldAspect.Ethnicities,
        GovernmentKey => WorldAspect.Governments,
        Faith or Religion => WorldAspect.Faiths,
        Ruler => WorldAspect.Rulers,
        _ => WorldAspect.None,
    };
}
