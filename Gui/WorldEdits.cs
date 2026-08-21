using Ck3MapGen.Core;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

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
    }

    private sealed record TitleSnapshot(Title Target, string Name, (byte R, byte G, byte B) Color)
        : ISnapshot
    {
        public bool Differs()
            => !string.Equals(Target.Name, Name, StringComparison.Ordinal) || Target.Color != Color;

        public void Restore() { Target.Name = Name; Target.Color = Color; }
    }

    private sealed record CultureSnapshot(Culture Target, string Name, (byte R, byte G, byte B) Color,
            string Ethos, string MartialCustom, string HeadDetermination, List<string> Traditions,
            string CoaGfx, string BuildingGfx, string ClothingGfx, string UnitGfx)
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
            || Target.UnitGfx != UnitGfx;

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
        }
    }

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
    }

    private sealed record ReligionSnapshot(Religion Target, string Name) : ISnapshot
    {
        public bool Differs() => !string.Equals(Target.Name, Name, StringComparison.Ordinal);
        public void Restore() => Target.Name = Name;
    }

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

    public bool WasEdited(object target)
        => _originals.TryGetValue(target, out var snapshot) && snapshot.Differs();

    public bool CanRevert(object target) => _originals.ContainsKey(target);

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
        => Apply(title, () => new TitleSnapshot(title, title.Name, title.Color), () => change(title),
            WorldAspect.TitleNames | WorldAspect.TitleColors);

    public void Rename(Title title, string name)
    {
        string checkedName = Checked(name);
        Apply(title, () => new TitleSnapshot(title, title.Name, title.Color),
            () => title.Name = checkedName, WorldAspect.TitleNames);
    }

    public void Recolor(Title title, (byte R, byte G, byte B) color)
        => Apply(title, () => new TitleSnapshot(title, title.Name, title.Color),
            () => title.Color = color, WorldAspect.TitleColors);

    public void EditCulture(Culture culture, Action<Culture> change)
        => Apply(culture, () => Snapshot(culture), () => change(culture), WorldAspect.Cultures);

    public void RenameCulture(Culture culture, string name)
    {
        string checkedName = Checked(name);
        Apply(culture, () => Snapshot(culture), () => culture.Name = checkedName, WorldAspect.Cultures);
    }

    public void EditFaith(Faith faith, Action<Faith> change)
        => Apply(faith, () => Snapshot(faith), () => change(faith), WorldAspect.Faiths);

    public void RenameFaith(Faith faith, string name)
    {
        string checkedName = Checked(name);
        Apply(faith, () => Snapshot(faith), () => faith.Name = checkedName, WorldAspect.Faiths);
    }

    public void RenameReligion(Religion religion, string name)
    {
        string checkedName = Checked(name);
        Apply(religion, () => new ReligionSnapshot(religion, religion.Name),
            () => religion.Name = checkedName, WorldAspect.Faiths);
    }

    private static CultureSnapshot Snapshot(Culture c)
        => new(c, c.Name, c.Color, c.Ethos, c.MartialCustom, c.HeadDetermination, [.. c.Traditions],
               c.CoaGfx, c.BuildingGfx, c.ClothingGfx, c.UnitGfx);
    private static FaithSnapshot Snapshot(Faith f) => new(f, f.Name, f.Color, f.Icon, [.. f.Tenets]);

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
            _originals.TryAdd(child, new TitleSnapshot(child, child.Name, child.Color));

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

    // --- Reverting ----------------------------------------------------------------------------

    public void Revert(object target)
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
        Title => WorldAspect.TitleNames | WorldAspect.TitleColors,
        Culture => WorldAspect.Cultures,
        Faith or Religion => WorldAspect.Faiths,
        _ => WorldAspect.None,
    };
}
