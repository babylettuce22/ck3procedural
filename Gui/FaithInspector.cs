using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// Everything editable about a generated faith, plus its religion's name.
///
/// A faith and the religion above it are edited from one window because the religion has exactly
/// one editable field. Its doctrines, virtues and sins are left alone: they are a coherent set the
/// generator assembled together, and half-editing them through a property grid is how you get a
/// religion whose doctrines contradict each other.
/// </summary>
public sealed class FaithInspector : InspectorForm
{
    private readonly Button _holySites = Theme.MakeButton("Holy sites…", 96);
    private readonly Button _seat = Theme.MakeButton("Head's seat…", 104);

    public FaithInspector(WorldEdits edits) : base(edits, "Faith", new Size(400, 520))
    {
        _holySites.Click += (_, _) => ShowHolySites();
        _seat.Click += (_, _) => GoToSeat();

        AddAction(_holySites);
        AddAction(_seat);
    }

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Faith>().Select(f => new Fields(f, Edits));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Faith f
            ? $"Faith — {f.Key}"
            : $"{targets.Count} faiths selected";

    protected override string Title(object target) => target is Faith f ? f.Name : "Faith";

    protected override void Refreshed()
    {
        bool single = Selection.Count == 1;
        _holySites.Enabled = single && Selection[0] is Faith { HolySites.Count: > 0 };
        _seat.Enabled = single && Selection[0] is Faith { Head: not null };
    }

    private void ShowHolySites()
    {
        if (Selection.Count != 1 || Selection[0] is not Faith faith) return;

        MessageBox.Show(this,
            $"{faith.Name} holds {faith.HolySites.Count} holy sites:\n\n"
            + string.Join("\n", faith.HolySites.Select(s => $"  {s.County.Name}   ({s.Key})"))
            + "\n\nA holy site takes its name from the county it sits in, so renaming that county "
            + "renames the site.",
            "Holy sites", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void GoToSeat()
    {
        if (Selection.Count == 1 && Selection[0] is Faith { Head: { } head }) GoTo(head.Seat);
    }

    /// <inheritdoc cref="TitleInspector.Fields"/>
    public sealed class Fields(Faith faith, WorldEdits edits)
    {
        [Category("Identity")]
        [Description("The name shown in game. The adjective and adherent forms are derived from it.")]
        public string Name
        {
            get => faith.Name;
            set => edits.RenameFaith(faith, value);
        }

        [Category("Identity")]
        [Description("The script key every other file references. Fixed.")]
        [ReadOnly(true)]
        public string Key => faith.Key;

        [Category("Identity")]
        [Description("The religion this faith belongs to. Renaming it here renames it for every "
                     + "faith under it.")]
        public string Religion
        {
            get => faith.Religion.Name;
            set => edits.RenameReligion(faith.Religion, value);
        }

        [Category("Identity")]
        [Description("Whether the religion is monotheist, which the generator used to pick its "
                     + "doctrines. Fixed.")]
        [ReadOnly(true)]
        public bool Monotheist => faith.Religion.Monotheist;

        [Category("Appearance")]
        [Description("The colour of this faith on the religion map mode.")]
        public Color Color
        {
            get => Color.FromArgb(
                (int)Math.Clamp(faith.Color.R * 255, 0, 255),
                (int)Math.Clamp(faith.Color.G * 255, 0, 255),
                (int)Math.Clamp(faith.Color.B * 255, 0, 255));

            // Back to the 0..1 triple CK3 script uses. Stored that way rather than as bytes because
            // that is what landed_titles and the religion file both expect to read.
            set => edits.EditFaith(faith, f => f.Color = (value.R / 255.0, value.G / 255.0, value.B / 255.0));
        }

        [Category("Appearance")]
        [Description("The faith's icon, a CK3 gfx key harvested from the install.")]
        public string Icon
        {
            get => faith.Icon;
            set => edits.EditFaith(faith, f => f.Icon = value.Trim());
        }

        [Category("Doctrine")]
        [Description("The faith's tenets, one CK3 script key per line. Vanilla faiths carry three.")]
        [Editor("System.Windows.Forms.Design.StringArrayEditor, System.Design",
                typeof(System.Drawing.Design.UITypeEditor))]
        public string[] Tenets
        {
            get => [.. faith.Tenets];
            set => edits.EditFaith(faith,
                f => f.Tenets = [.. (value ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)]);
        }

        [Category("Extent")]
        [Description("Whether this faith has a head of faith, and what their title is called.")]
        [ReadOnly(true)]
        public string Head => faith.Head is { } h ? $"{h.Name} at {h.Seat.Name}" : "(none)";

        [Category("Extent")]
        [Description("How many counties hold this faith at the start date.")]
        [ReadOnly(true)]
        public int Counties => faith.Counties.Count;

        [Category("Extent")]
        [Description("Whether the generator treated this as a dominant faith when placing it.")]
        [ReadOnly(true)]
        public bool Dominant => faith.IsDominant;

        public override string ToString() => faith.Name;
    }
}
