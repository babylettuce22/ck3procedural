using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Everything editable about a generated faith, plus its religion's name, virtues and sins.
///
/// A faith and the religion above it are edited from one window because a religion has nowhere
/// else to be reached from; anything here that belongs to the religion says so, and lands on every
/// faith under it.
///
/// Its doctrines are still left alone. They are a coherent set the generator assembled together
/// against CK3's own compatibility rules, and half-editing them through a property grid is how you
/// get a religion that contradicts itself. The tenet, virtue and sin slots are editable precisely
/// because each of them can state its constraint — see the <see cref="SlotConverter"/>s, which
/// drop from each dropdown whatever the rest of the faith has already ruled out.
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

    /// <summary>
    /// A dropdown for one slot of a fixed-length list, offering the install's own vocabulary minus
    /// whatever the slot's siblings rule out. Nested here rather than filed with the plain
    /// vocabulary dropdowns in <see cref="CultureConverters"/> for the reason
    /// <c>RealmWordsConverter</c> is nested in <see cref="CultureInspector"/> — these are the only
    /// ones that have to read the rest of the object they sit on.
    ///
    /// <paramref name="prefix"/> is the property-name stem the slot number follows, so "Tenet"
    /// reads the index off Tenet1, Tenet2, Tenet3.
    /// </summary>
    public abstract class SlotConverter(
        Func<VanillaVocabulary, IEnumerable<string>> selector, string prefix)
        : DynamicVocabularyConverter(selector)
    {
        /// <summary>What this slot may not offer, given everything else the object holds.</summary>
        protected abstract HashSet<string> Blocked(Fields fields, int index);

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            var pool = base.GetStandardValues(context).Cast<string>();

            // Multi-select hands the grid an object[] rather than one Fields, and there is no
            // single faith to read the siblings off then. Offering the whole pool is the honest
            // answer there; the setters still refuse to store a contradiction.
            if (context?.Instance is not Fields fields
                || context.PropertyDescriptor?.Name is not { } property
                || !property.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(property[prefix.Length..], out int slot)
                || slot < 1)
                return new StandardValuesCollection(pool.ToList());

            var blocked = Blocked(fields, slot - 1);
            return new StandardValuesCollection(pool.Where(v => !blocked.Contains(v)).ToList());
        }
    }

    /// <summary>
    /// A tenet slot. Leaves out the other two slots' tenets, anything CK3's <c>can_pick</c> rules
    /// will not seat beside them, and anything ruled out by the religion's own doctrines.
    /// </summary>
    public sealed class TenetConverter() : SlotConverter(v => v.Tenets, "Tenet")
    {
        protected override HashSet<string> Blocked(Fields fields, int index)
            => fields.TenetsBlockedFor(index);
    }

    /// <summary>A virtue slot. Leaves out the other virtues and every trait already a sin.</summary>
    public sealed class VirtueConverter() : SlotConverter(v => v.Virtues, "Virtue")
    {
        protected override HashSet<string> Blocked(Fields fields, int index)
            => fields.TraitsBlockedFor(index, virtues: true);
    }

    /// <summary>A sin slot. Leaves out the other sins and every trait already a virtue.</summary>
    public sealed class SinConverter() : SlotConverter(v => v.Sins, "Sin")
    {
        protected override HashSet<string> Blocked(Fields fields, int index)
            => fields.TraitsBlockedFor(index, virtues: false);
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
        [Description("The faith's icon, picked from the gfx keys harvested off this install.")]
        [TypeConverter(typeof(FaithIconConverter))]
        public string Icon
        {
            get => faith.Icon;
            set => edits.EditFaith(faith, f => f.Icon = value.Trim());
        }

        // CK3's doctrine_core_tenets group is number_of_picks = 3 and Faiths.CreateFaith draws
        // exactly that many, so three fixed slots cover every faith the generator makes — one
        // dropdown each, the way a culture's ethos and martial custom pillars are edited. The
        // picker dialog these replaced could add a fourth tenet the game would then ignore.
        private const string TenetHelp =
            "One of the faith's three tenets, from this install's own harvested tenet pool. The "
            + "dropdown leaves out whatever the other two slots already hold; a key the harvest "
            + "missed can still be typed in by hand. Clearing a slot leaves the faith with fewer "
            + "than three tenets, and closes the gap.";

        [Category("Doctrine")] [DisplayName("Tenet 1")] [Description(TenetHelp)]
        [TypeConverter(typeof(TenetConverter))]
        public string Tenet1 { get => Slot(0); set => SetSlot(0, value); }

        [Category("Doctrine")] [DisplayName("Tenet 2")] [Description(TenetHelp)]
        [TypeConverter(typeof(TenetConverter))]
        public string Tenet2 { get => Slot(1); set => SetSlot(1, value); }

        [Category("Doctrine")] [DisplayName("Tenet 3")] [Description(TenetHelp)]
        [TypeConverter(typeof(TenetConverter))]
        public string Tenet3 { get => Slot(2); set => SetSlot(2, value); }

        private string Slot(int index) => index < faith.Tenets.Count ? faith.Tenets[index] : "";

        private void SetSlot(int index, string? value)
        {
            string tenet = (value ?? "").Trim();

            edits.EditFaith(faith, f =>
            {
                var slots = new List<string>(f.Tenets);
                while (slots.Count <= index) slots.Add("");
                slots[index] = tenet;

                // Picking a tenet the faith already holds elsewhere moves it here rather than
                // doubling it: CK3 reads a repeated doctrine as one, so a duplicate would quietly
                // cost the faith a tenet instead of adding one. The dropdown hides the siblings so
                // this is only reachable by typing, or by editing several faiths at once.
                if (tenet.Length > 0)
                    for (int i = 0; i < slots.Count; i++)
                        if (i != index && string.Equals(slots[i], tenet, StringComparison.Ordinal))
                            slots[i] = "";

                // Compacted, because the list is written straight to script and a hole in it would
                // emit an empty doctrine line.
                f.Tenets = [.. slots.Where(t => t.Length > 0)];
            });
        }

        /// <summary>
        /// What this tenet slot's dropdown must not offer: the tenets the other slots hold,
        /// everything CK3 refuses to seat beside them, and everything ruled out by the religion's
        /// doctrines. The last is why a faith whose religion criminalises witchcraft is never
        /// offered natural primitivism.
        /// </summary>
        internal HashSet<string> TenetsBlockedFor(int index)
        {
            var siblings = faith.Tenets.Where((_, i) => i != index);
            var vocab = VanillaVocabulary.Current;

            return vocab is null
                ? [.. siblings]
                : vocab.IncompatibleWithAll(siblings.Concat(faith.Religion.Doctrines.Values));
        }

        // --- Religion virtues and sins ---------------------------------------------------------
        //
        // These belong to the religion, so editing one here lands on every faith under it, the way
        // renaming the religion does. The generator draws three to five of each, so five slots
        // cover it; the extras sit empty. Its doctrines stay unedited — they are a coherent set
        // assembled together, and half-editing them through a property grid is how you get a
        // religion that contradicts itself.

        private const string VirtueHelp =
            "One of the religion's virtues — the traits it rewards. Shared by every faith under "
            + "the religion. The dropdown leaves out its other virtues and anything already a sin.";

        private const string SinHelp =
            "One of the religion's sins — the traits it punishes. Shared by every faith under the "
            + "religion. The dropdown leaves out its other sins and anything already a virtue.";

        [Category("Religion traits")] [DisplayName("Virtue 1")] [Description(VirtueHelp)]
        [TypeConverter(typeof(VirtueConverter))]
        public string Virtue1 { get => Trait(0, true); set => SetTrait(0, value, true); }

        [Category("Religion traits")] [DisplayName("Virtue 2")] [Description(VirtueHelp)]
        [TypeConverter(typeof(VirtueConverter))]
        public string Virtue2 { get => Trait(1, true); set => SetTrait(1, value, true); }

        [Category("Religion traits")] [DisplayName("Virtue 3")] [Description(VirtueHelp)]
        [TypeConverter(typeof(VirtueConverter))]
        public string Virtue3 { get => Trait(2, true); set => SetTrait(2, value, true); }

        [Category("Religion traits")] [DisplayName("Virtue 4")] [Description(VirtueHelp)]
        [TypeConverter(typeof(VirtueConverter))]
        public string Virtue4 { get => Trait(3, true); set => SetTrait(3, value, true); }

        [Category("Religion traits")] [DisplayName("Virtue 5")] [Description(VirtueHelp)]
        [TypeConverter(typeof(VirtueConverter))]
        public string Virtue5 { get => Trait(4, true); set => SetTrait(4, value, true); }

        [Category("Religion traits")] [DisplayName("Sin 1")] [Description(SinHelp)]
        [TypeConverter(typeof(SinConverter))]
        public string Sin1 { get => Trait(0, false); set => SetTrait(0, value, false); }

        [Category("Religion traits")] [DisplayName("Sin 2")] [Description(SinHelp)]
        [TypeConverter(typeof(SinConverter))]
        public string Sin2 { get => Trait(1, false); set => SetTrait(1, value, false); }

        [Category("Religion traits")] [DisplayName("Sin 3")] [Description(SinHelp)]
        [TypeConverter(typeof(SinConverter))]
        public string Sin3 { get => Trait(2, false); set => SetTrait(2, value, false); }

        [Category("Religion traits")] [DisplayName("Sin 4")] [Description(SinHelp)]
        [TypeConverter(typeof(SinConverter))]
        public string Sin4 { get => Trait(3, false); set => SetTrait(3, value, false); }

        [Category("Religion traits")] [DisplayName("Sin 5")] [Description(SinHelp)]
        [TypeConverter(typeof(SinConverter))]
        public string Sin5 { get => Trait(4, false); set => SetTrait(4, value, false); }

        private List<string> Own(bool virtues) => virtues ? faith.Religion.Virtues : faith.Religion.Sins;

        private string Trait(int index, bool virtues)
        {
            var own = Own(virtues);
            return index < own.Count ? own[index] : "";
        }

        private void SetTrait(int index, string? value, bool virtues)
        {
            string trait = (value ?? "").Trim();

            edits.EditReligion(faith.Religion, r =>
            {
                var own = virtues ? r.Virtues : r.Sins;
                var opposite = virtues ? r.Sins : r.Virtues;

                var slots = new List<string>(own);
                while (slots.Count <= index) slots.Add("");
                slots[index] = trait;

                if (trait.Length > 0)
                {
                    for (int i = 0; i < slots.Count; i++)
                        if (i != index && string.Equals(slots[i], trait, StringComparison.Ordinal))
                            slots[i] = "";

                    // A trait cannot be a virtue and a sin at once. The vocabulary harvest already
                    // drops the traits vanilla lists on both sides for this reason; a religion that
                    // rewards and punishes the same behaviour reads as a bug in the faith window.
                    opposite.RemoveAll(t => string.Equals(t, trait, StringComparison.Ordinal));
                }

                // Held in place, not reassigned: the lists are init-only on Religion.
                var kept = slots.Where(t => t.Length > 0).ToList();
                own.Clear();
                own.AddRange(kept);
            });
        }

        /// <summary>
        /// What a virtue or sin slot's dropdown must not offer: the same list's other slots, and
        /// everything on the opposite list.
        /// </summary>
        internal HashSet<string> TraitsBlockedFor(int index, bool virtues)
        {
            var blocked = new HashSet<string>(Own(!virtues), StringComparer.Ordinal);
            blocked.UnionWith(Own(virtues).Where((_, i) => i != index));
            return blocked;
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
