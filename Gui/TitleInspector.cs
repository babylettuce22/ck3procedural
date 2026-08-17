using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>Everything editable about a de jure title, and the way through to what lives in it.</summary>
public sealed class TitleInspector : InspectorForm
{
    private readonly Button _reroll = Theme.MakeButton("Reroll name", 100);
    private readonly Button _recolorChildren = Theme.MakeButton("Recolour children", 130);
    private readonly Button _culture = Theme.MakeButton("Culture…", 84);
    private readonly Button _faith = Theme.MakeButton("Faith…", 76);

    public TitleInspector(WorldEdits edits) : base(edits, "Title", new Size(380, 500))
    {
        _reroll.Click += (_, _) => Reroll();
        _recolorChildren.Click += (_, _) => RecolorChildren();
        _culture.Click += (_, _) => GoToRelated(t => Edits.Target?.Written.Cultures.For(t));
        _faith.Click += (_, _) => GoToRelated(t => Edits.Target?.Written.Faiths.For(t));

        AddAction(_reroll);
        AddAction(_recolorChildren);
        AddAction(_culture);
        AddAction(_faith);
    }

    private IEnumerable<Title> Titles_ => Selection.OfType<Title>();

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Title>().Select(t => new Fields(t, Edits));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Title t
            ? $"{TierName(t)} — {t.Key}"
            : $"{targets.Count} titles selected";

    protected override string Title(object target) => target is Title t ? t.Name : "Title";

    protected override void Refreshed()
    {
        _recolorChildren.Enabled = Edits.IsLoaded && Titles_.Any(t => t.Children.Count > 0);

        // One title at a time for these: they lead somewhere, and a button that leads to four
        // different cultures has nowhere to go.
        bool single = Edits.IsLoaded && Selection.Count == 1;
        _culture.Enabled = single;
        _faith.Enabled = single;
    }

    public static string TierName(Title title) => title.Tier switch
    {
        "e" => "Empire",
        "k" => "Kingdom",
        "d" => "Duchy",
        "c" => "County",
        _ => "Barony",
    };

    // --- Actions ------------------------------------------------------------------------------

    private void Reroll()
    {
        if (Edits.Target is not { } target) return;

        foreach (var title in Titles_.ToList())
        {
            var rng = new Core.Rng(Random.Shared.Next(1, int.MaxValue));
            Edits.Rename(title, MapGen.Titles.GenerateName(title, target.Written.Cultures, rng));
        }

        Rebuild();
    }

    private void RecolorChildren()
    {
        foreach (var title in Titles_.ToList()) Edits.RecolorChildren(title);
        Rebuild();
    }

    private void GoToRelated(Func<Title, object?> resolve)
    {
        if (Selection.Count != 1 || Selection[0] is not Title title) return;
        if (resolve(title) is { } related) GoTo(related);
    }

    /// <summary>
    /// The editable face of a title.
    ///
    /// Everything settable writes through <see cref="WorldEdits"/> rather than to the title
    /// directly, so the revert history and the pending-file tracking cannot be bypassed. Read-only
    /// properties are context — what this is and where it sits — which is most of what tells you
    /// whether you clicked the right thing.
    /// </summary>
    public sealed class Fields(Title title, WorldEdits edits)
    {
        [Category("Identity")]
        [Description("The name shown in game. Renaming rewrites the localisation; the title's key "
                     + "is left alone, so nothing that references this title breaks.")]
        public string Name
        {
            get => title.Name;
            set => edits.Rename(title, value);
        }

        [Category("Identity")]
        [Description("The script key every other file references. Fixed — changing it would dangle "
                     + "every reference to this title.")]
        [ReadOnly(true)]
        public string Key => title.Key;

        [Category("Identity")]
        [Description("Which rung of the de jure hierarchy this title sits on.")]
        [ReadOnly(true)]
        public string Tier => TierName(title);

        [Category("Appearance")]
        [Description("The colour of this title on the map. Children are not re-derived from it — "
                     + "use Recolour children for that.")]
        public Color Color
        {
            get => Color.FromArgb(title.Color.R, title.Color.G, title.Color.B);
            set => edits.Recolor(title, (value.R, value.G, value.B));
        }

        [Category("Place")]
        [Description("The province this barony is, or -1 for every tier above one.")]
        [ReadOnly(true)]
        public int ProvinceId => title.ProvinceId;

        [Category("Place")]
        [Description("The title this one belongs to de jure.")]
        [ReadOnly(true)]
        public string Liege => title.Parent is { } p ? $"{TierName(p)} {p.Name}" : "(independent)";

        [Category("Place")]
        [Description("How many titles sit directly beneath this one.")]
        [ReadOnly(true)]
        public int Children => title.Children.Count;

        public override string ToString() => title.Name;
    }
}
