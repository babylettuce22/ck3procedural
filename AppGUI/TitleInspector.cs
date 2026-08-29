using System.ComponentModel;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>Everything editable about a de jure title, and the way through to what lives in it.</summary>
public sealed class TitleInspector : InspectorForm
{
    private readonly Button _reroll = Theme.MakeButton("Reroll name", 100);
    private readonly Button _recolorChildren = Theme.MakeButton("Recolour children", 130);
    private readonly Button _culture = Theme.MakeButton("Culture…", 84);
    private readonly Button _faith = Theme.MakeButton("Faith…", 76);
    private readonly Button _ruler = Theme.MakeButton("Ruler…", 76);
    private readonly Button _liege = Theme.MakeButton("Liege", 60);
    private readonly Button _vassals = Theme.MakeButton("Vassals…", 84);
    private readonly Button _focusMap = Theme.MakeButton("Focus map", 90);

    /// <summary>
    /// The written world's de facto structure, when there is one. Set by the owning window after
    /// every write; null before the first write, when the realm buttons stay dark and this window
    /// is a purely de jure affair.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RealmGraph? Realm { get; set; }

    /// <summary>Asks the owning window to focus the Realms map on this ruler's seat.</summary>
    public event Action<Title>? FocusRealm;

    public TitleInspector(WorldEdits edits) : base(edits, "Title", new Size(440, 520))
    {
        _reroll.Click += (_, _) => Reroll();
        _recolorChildren.Click += (_, _) => RecolorChildren();
        _culture.Click += (_, _) => GoToRelated(t => Edits.Target?.Written.Cultures.For(t));
        _faith.Click += (_, _) => GoToRelated(t => Edits.Target?.Written.Faiths.For(t));
        _ruler.Click += (_, _) => GoToRelated(RulerOf);
        _liege.Click += (_, _) => GoToLiege();
        _vassals.Click += (_, _) => ShowVassals();
        _focusMap.Click += (_, _) => { if (Seat() is { } seat) FocusRealm?.Invoke(seat); };

        AddAction(_reroll);
        AddAction(_recolorChildren);
        AddAction(_culture);
        AddAction(_faith);
        AddAction(_ruler);
        AddAction(_liege);
        AddAction(_vassals);
        AddAction(_focusMap);
    }

    private IEnumerable<Title> Titles_ => Selection.OfType<Title>();

    /// <summary>The seat of whoever holds the single selected title, when that means anything.</summary>
    private Title? Seat()
        => Realm is { } realm && Selection.Count == 1 && Selection[0] is Title title
            ? realm.SeatOf(title)
            : null;

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Title>().Select(t => new Fields(t, Edits, Realm));

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

        var seat = Edits.IsLoaded ? Seat() : null;
        _ruler.Enabled = single && Selection[0] is Title held && RulerOf(held) is not null;
        _liege.Enabled = seat is not null && Realm!.LiegeSeat(seat) is not null;
        _vassals.Enabled = seat is not null && Realm!.VassalSeats(seat).Count > 0;
        _focusMap.Enabled = seat is not null;
    }

    public static string TierName(Title title) => title.Tier switch
    {
        // Without this the tier above empire falls through to the barony default, and every empire
        // reports its de jure liege as "Barony <the world>".
        "h" => "Hegemony",
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
    /// The character holding this title at game start — the same man Held by names — or null
    /// before a write, for wilderness, or for a mod written with history skipped.
    /// </summary>
    private Ruler? RulerOf(Title title)
        => Realm?.SeatOf(title) is { } seat
           && Edits.Target?.Written.Rulers is { } rulers
           && rulers.TryGet(seat, out var ruler)
            ? ruler
            : null;

    private void GoToLiege()
    {
        if (Seat() is not { } seat || Realm!.LiegeSeat(seat) is not { } above) return;
        GoTo(Realm.Primary(above));
    }

    /// <summary>
    /// The way down a realm: a menu of the ruler's direct vassals, biggest first. Picking one both
    /// inspects it here and refocuses the map, so the window and the map descend together.
    /// </summary>
    private void ShowVassals()
    {
        if (Seat() is not { } seat || Realm is not { } realm) return;

        var vassals = realm.VassalSeats(seat);
        if (vassals.Count == 0) return;

        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);

        const int cap = 40;
        foreach (var vassal in vassals.Take(cap))
        {
            var primary = realm.Primary(vassal);
            var item = new ToolStripMenuItem(
                $"{TierName(primary)} {primary.Name} — {realm.RealmSize(vassal)} counties");

            item.Click += (_, _) =>
            {
                GoTo(primary);
                FocusRealm?.Invoke(vassal);
            };

            menu.Items.Add(item);
        }

        if (vassals.Count > cap)
            menu.Items.Add(new ToolStripMenuItem($"… and {vassals.Count - cap} more") { Enabled = false });

        menu.Show(_vassals, new Point(0, _vassals.Height));
    }

    /// <summary>
    /// The editable face of a title.
    ///
    /// Everything settable writes through <see cref="WorldEdits"/> rather than to the title
    /// directly, so the revert history and the pending-file tracking cannot be bypassed. Read-only
    /// properties are context — what this is and where it sits — which is most of what tells you
    /// whether you clicked the right thing.
    /// </summary>
    public sealed class Fields(Title title, WorldEdits edits, RealmGraph? realm)
    {
        private Title? Seat => realm?.SeatOf(title);

        [Category("Realm (de facto)")]
        [DisplayName("Held by")]
        [Description("The ruler holding this title at game start, named by their primary title. "
                     + "De facto, unlike Liege below — written with the mod, so empty until one is.")]
        [ReadOnly(true)]
        public string HeldBy
            => Seat is { } s ? $"{TierName(realm!.Primary(s))} {realm.Primary(s).Name}" : "—";

        [Category("Realm (de facto)")]
        [DisplayName("Answers to")]
        [Description("The liege this ruler is sworn to, or independent.")]
        [ReadOnly(true)]
        public string AnswersTo
            => Seat is not { } s ? "—"
                : realm!.LiegeSeat(s) is { } above
                    ? $"{TierName(realm.Primary(above))} {realm.Primary(above).Name}"
                    : "(independent)";

        [Category("Realm (de facto)")]
        [DisplayName("Direct vassals")]
        [ReadOnly(true)]
        public string DirectVassals => Seat is { } s ? realm!.VassalSeats(s).Count.ToString() : "—";

        [Category("Realm (de facto)")]
        [DisplayName("Realm counties")]
        [Description("Everything this ruler's realm contains, demesne and vassals together.")]
        [ReadOnly(true)]
        public string RealmCounties => Seat is { } s ? realm!.RealmSize(s).ToString() : "—";

        [Category("Realm (de facto)")]
        [DisplayName("Demesne")]
        [Description("Counties this ruler holds personally rather than through a vassal.")]
        [ReadOnly(true)]
        public string Demesne => Seat is { } s ? realm!.Demesne(s).Count.ToString() : "—";

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

        // --- Realm titles ---
        //
        // The two places a realm's word can come from, and what it comes out as. The culture's
        // vocabulary is edited on the culture (Culture…); this is the per-title override — the
        // priority-900 rule that names one specific title whoever holds it — which is what an
        // import uses for the countries that named themselves.

        private bool Ranked => title.Tier is "e" or "k" or "d";

        [Category("Realm titles")]
        [DisplayName("Realm word")]
        [Description("This title's own word for itself — Sultanate, League, United Provinces — in "
                     + "place of what its holder's culture calls a realm of this rank. Empires, "
                     + "kingdoms and duchies only; blank to take the culture's word. Setting it "
                     + "derives the ruler's style below, which you can then change.")]
        public string Form
        {
            get => Ranked ? title.Form ?? "" : "—";
            set
            {
                if (!Ranked) return;
                string form = value.Trim();
                edits.EditTitleWords(title, t =>
                {
                    if (form.Length == 0)
                    {
                        t.Form = t.Holder = t.HolderFemale = null;
                        return;
                    }

                    t.Form = form;
                    t.Holder = RulerWord.From(form);
                    t.HolderFemale = RulerWord.Feminine(t.Holder);
                });
            }
        }

        [Category("Realm titles")]
        [DisplayName("Ruler style (male)")]
        [Description("What a man holding this title is called. Only used when Realm word is set.")]
        public string Holder
        {
            get => Ranked ? title.Holder ?? "" : "—";
            set { if (Ranked) edits.EditTitleWords(title, t => t.Holder = Word(value)); }
        }

        [Category("Realm titles")]
        [DisplayName("Ruler style (female)")]
        [Description("What a woman holding this title is called. Only used when Realm word is set; "
                     + "blank falls back to the male style, which is what vanilla does where a "
                     + "language has no feminine.")]
        public string HolderFemale
        {
            get => Ranked ? title.HolderFemale ?? "" : "—";
            set { if (Ranked) edits.EditTitleWords(title, t => t.HolderFemale = Word(value)); }
        }

        [Category("Realm titles")]
        [DisplayName("Renders as")]
        [Description("What the game will call this title and its holder at game start, the way "
                     + "the engine decides it: the title's own word if set, else the top liege's "
                     + "culture's word for the holder's government, else vanilla's own rules.")]
        [ReadOnly(true)]
        public string RendersAs => RealmStyle.Describe(title, realm, edits.Target?.Written);

        private static string? Word(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
