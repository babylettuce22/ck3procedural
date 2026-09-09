using System.ComponentModel;
using System.Globalization;
using Ck3MapGen.Core;
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
        _culture.Click += (_, _) => GoToRelated(t => Loaded is { } w ? w.CultureOf(t) : Edits.Target?.Written.Cultures.For(t));
        _faith.Click += (_, _) => GoToRelated(t => Loaded is { } w ? w.FaithOf(t) : Edits.Target?.Written.Faiths.For(t));
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

    /// <summary>The selected titles — generated objects, or an opened mod's shells for its entries.</summary>
    private IEnumerable<Title> Titles_ => Loaded is { } w
        ? Selection.OfType<WorldEntry>().Where(e => e.Kind == "Title").Select(w.Shell)
        : Selection.OfType<Title>();

    private Title? One => Selection.Count == 1 ? Titles_.FirstOrDefault() : null;

    /// <summary>The seat of whoever holds the single selected title, when that means anything.</summary>
    private Title? Seat()
        => Realm is { } realm && One is { } title ? realm.SeatOf(title) : null;

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Title>().Select(t => new Fields(t, Edits, Realm));

    protected override IEnumerable<object> WrapLoaded(IReadOnlyList<WorldEntry> entries)
        => entries.Where(e => e.Kind == "Title").Select(e => new LoadedFields(e, Loaded!, Realm));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Title t
            ? $"{TierName(t)} — {t.Key}"
            : $"{targets.Count} titles selected";

    protected override string Title(object target) => target is Title t ? t.Name : "Title";

    protected override void Refreshed()
    {
        _recolorChildren.Enabled = Live && Titles_.Any(t => t.Children.Count > 0);

        // Names are rerolled from the generated cultures' name lists, which an opened mod does not
        // carry; its names are typed.
        _reroll.Enabled = Edits.IsLoaded && Titles_.Any();

        // One title at a time for these: they lead somewhere, and a button that leads to four
        // different cultures has nowhere to go.
        bool single = Live && One is not null;
        _culture.Enabled = single && (Loaded is null || Loaded.CultureOf(One!) is not null);
        _faith.Enabled = single && (Loaded is null || Loaded.FaithOf(One!) is not null);

        var seat = Live ? Seat() : null;
        _ruler.Enabled = single && RulerOf(One!) is not null;
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

    /// <summary>
    /// The governments a realm can be put on. Exclusive, unlike the nickname and legitimacy
    /// dropdowns, because this is not a key the engine merely looks up: the capital holding of
    /// every county in the realm is derived from it, and a government the tool has no
    /// <c>primary_holding</c> for would leave every ruler in the realm unable to hold his own seat.
    /// </summary>
    /// <remarks>
    /// The property holds the game's key and the grid shows the name, which is what the map legend
    /// shows too. Fourteen raw keys was a list nobody could read at a glance — the choice between
    /// <c>japan_administrative_government</c> and <c>japan_feudal_government</c> is Ritsuryō or
    /// Sōryō, which is how the game itself names them and nothing in the keys says.
    /// </remarks>
    public sealed class GovernmentConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(GovernmentMap.DisplayOrder);

        public override object? ConvertTo(ITypeDescriptorContext? context,
            CultureInfo? culture, object? value, Type destinationType)
            => destinationType == typeof(string) && value is string key
                ? GovernmentMap.DisplayName(key)
                : base.ConvertTo(context, culture, value, destinationType);

        // Back to the key the property stores. A name the list does not hold is handed through
        // untouched rather than guessed at, which is what keeps the blank the getter shows for the
        // wilderness and for an unwritten world round-tripping as itself.
        public override object? ConvertFrom(ITypeDescriptorContext? context,
            CultureInfo? culture, object value)
            => value is string name
                ? GovernmentMap.DisplayOrder.FirstOrDefault(
                    g => GovernmentMap.DisplayName(g) == name, name)
                : base.ConvertFrom(context, culture, value);
    }

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
        if (Loaded is { } world)
        {
            foreach (var title in Titles_.ToList()) world.RecolorChildren(title);
            LoadedChanged();
            return;
        }
        foreach (var title in Titles_.ToList()) Edits.RecolorChildren(title);
        Rebuild();
    }

    private void GoToRelated(Func<Title, object?> resolve)
    {
        if (One is not { } title) return;
        if (resolve(title) is { } related) GoTo(related);
    }

    /// <summary>
    /// The character holding this title at game start — the same man Held by names — or null
    /// before a write, for wilderness, or for a mod written with history skipped. For an opened
    /// mod it is the character entry the title history names.
    /// </summary>
    private object? RulerOf(Title title)
    {
        if (Loaded is { } world) return world.HolderOf(title);
        return Realm?.SeatOf(title) is { } seat
           && Edits.Target?.Written.Rulers is { } rulers
           && rulers.TryGet(seat, out var ruler)
            ? ruler
            : null;
    }

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
    public sealed class Fields(Title title, WorldEdits edits, RealmGraph? realm) : ILoadedChoices
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
        [TypeConverter(typeof(GovernmentConverter))]
        [Description("What this title's holder rules as. Changing it moves the whole realm — this "
                     + "ruler's counties and every vassal's beneath them — because a government is "
                     + "decided per realm, and it takes each county's capital holding with it, "
                     + "since each government seats its ruler in one kind of holding only — a "
                     + "horde takes nomad holdings and empties the rest of each county, and a "
                     + "mandala rebuilds every capital as a temple citadel. To "
                     + "change a single vassal instead, open them from Vassals… and change theirs. "
                     + "Empty until the mod is written.")]
        public string Government
        {
            // Blank for the wilderness as well as for a title nobody holds: an unsettled county is
            // held by its own immortal placeholder under wilderness_government, which is not one of
            // ours to change, and showing a settable value that quietly did nothing would be worse
            // than showing none.
            get => !Unsettled && Seat is { } s && edits.Governments is { } map ? map.For(s) : "—";
            set
            {
                if (Unsettled || Seat is not { } s || realm is null) return;
                edits.SetGovernment(s, realm.Primary(s), realm.RealmCounties(s), value);
            }
        }

        private bool Unsettled => edits.Target?.Written.Wilderness.Contains(title) == true;

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

        [Category("Place")]
        [TypeConverter(typeof(ChoiceConverter))]
        [Description("The de jure capital: one of the counties beneath a duchy, kingdom or empire. "
                     + "Written first in landed_titles, which is how the engine reads a capital. "
                     + "A county's seat barony is fixed — the holdings were built around it.")]
        public string Capital
        {
            get => title.Tier is "d" or "k" or "e" or "h" ? CapitalCounty(title)?.Key ?? "" : "—";
            set
            {
                if (title.Tier is not ("d" or "k" or "e" or "h") || string.IsNullOrWhiteSpace(value) || value == "—") return;
                var county = Titles.Flatten([title]).FirstOrDefault(t => t.Tier == "c" && t.Key == value.Trim());
                if (county is null) throw new ArgumentException($"Choose a county inside {title.Name}.");
                edits.SetCapital(title, county);
            }
        }

        private static Title? CapitalCounty(Title of)
        {
            var seat = of.Capital;
            while (seat is not null && seat.Tier != "c") seat = seat.Capital;
            return seat;
        }

        [Category("Development")]
        [Description("The county's development at the start date, as the title history writes it. "
                     + "Counties only; a duchy's shows a dash.")]
        public int Development
        {
            get => title.Tier == "c" ? edits.Development(title) : 0;
            set { if (title.Tier == "c") edits.SetDevelopment(title, value); }
        }

        [Category("Buildings")]
        [DisplayName("Special building slot")]
        [Description("The special building slot this barony's province carries — a wonder's or a "
                     + "bazaar's — or blank for none. Baronies only. A slot the game does not "
                     + "define is ignored at load.")]
        public string SpecialSlot
        {
            get => title.Tier == "b" ? edits.ProvinceRow(title)?.SpecialSlot ?? "" : "—";
            set { if (title.Tier == "b") edits.SetSpecialBuilding(title, value == "—" ? null : value, null); }
        }

        [Category("Buildings")]
        [DisplayName("Special building")]
        [Description("The special building already standing in the slot at the start date, or blank.")]
        public string SpecialBuilding
        {
            get => title.Tier == "b" ? edits.ProvinceRow(title)?.SpecialBuilding ?? "" : "—";
            set { if (title.Tier == "b") edits.SetSpecialBuilding(title, null, value == "—" ? null : value); }
        }

        public IReadOnlyList<(string Key, string Label)> Choices(string property) => property switch
        {
            nameof(Capital) => Titles.Flatten([title]).Where(t => t.Tier == "c").Select(t => (t.Key, t.Name)).ToList(),
            _ => [],
        };

        public override string ToString() => title.Name;
    }

    /// <summary>
    /// The editable face of an opened mod's title: the same realm facts <see cref="Fields"/> shows,
    /// read back from the title history, and the values that can be changed in place — the
    /// localised name and adjective, the colour, and for the land beneath it the culture, faith
    /// and holding of its provinces. Structural facts stay read-only for the reason
    /// <see cref="LoadedWorld"/> gives: a changed key would dangle every file that names it.
    /// </summary>
    public sealed class LoadedFields(WorldEntry entry, LoadedWorldView world, RealmGraph? realm) : ILoadedChoices
    {
        private Title Shell => world.Shell(entry);
        private Title? Seat => realm?.SeatOf(Shell);
        private string Styled(Title seat) => $"{TierName(realm!.Primary(seat))} {realm.Primary(seat).Name}";

        [Category("Realm (de facto)")]
        [DisplayName("Held by")]
        [Description("The character holding this title at the start date, and their primary title, from the title history.")]
        [ReadOnly(true)]
        public string HeldBy => Seat is { } s
            ? world.HolderOf(realm!.Primary(s)) is { } holder ? $"{holder.Name} — {Styled(s)}" : Styled(s)
            : "—";

        [Category("Realm (de facto)")]
        [DisplayName("Answers to")]
        [Description("The liege this ruler is sworn to, or independent.")]
        [ReadOnly(true)]
        public string AnswersTo => Seat is not { } s ? "—"
            : realm!.LiegeSeat(s) is { } above ? Styled(above) : "(independent)";

        [Category("Realm (de facto)")]
        [Description("The government the title history gives this holder. Changing a government "
                     + "reseats every ruler in the realm, which the generator does as a whole; it "
                     + "is not edited here.")]
        [ReadOnly(true)]
        public string Government => world.GovernmentOf(Shell) ?? "—";

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
        [Description("The name shown in game. Renaming rewrites the English localisation; the "
                     + "title's key is left alone, so nothing that references this title breaks.")]
        public string Name
        {
            get => entry.Name;
            set => entry.Field("Name")?.Write?.Invoke(value);
        }

        [Category("Identity")]
        [Description("The adjective form, where the localisation has one.")]
        public string Adjective
        {
            get => entry.Value("Adjective");
            set => entry.Field("Adjective")?.Write?.Invoke(value);
        }

        [Category("Identity")]
        [Description("The script key every other file references. Fixed.")]
        [ReadOnly(true)]
        public string Key => entry.Key;

        [Category("Identity")]
        [ReadOnly(true)]
        public string Tier => TierName(Shell);

        [Category("Appearance")]
        [Description("The colour of this title on the map, written back in the file's own scale. "
                     + "Children are not re-derived from it — use Recolour children for that.")]
        public Color Color
        {
            get => entry.Field("color") is { } colour ? LoadedWorldView.ParseColor(colour) ?? Color.Black : Color.Black;
            set { if (entry.Field("color") is { Write: { } write } colour) write(LoadedWorldView.FormatColor(colour, value)); }
        }

        // --- Realm titles ---
        //
        // The per-title override the generator writes for an imported country that named itself.
        // Its three words are English loc lines and edit in place; a title without them takes its
        // culture's words, which are edited on the culture (Culture…), and there is no line here
        // to write into — the editor changes lines, it does not add them.

        private string Word(string field) => entry.Field(field) is not null ? entry.Value(field) : "—";
        private void SetWord(string field, string value)
        {
            if (entry.Field(field) is { Write: { } write }) write(value.Trim());
            else if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "—")
                throw new ArgumentException("This title has no word of its own in the mod; its realm is named by its holder's culture. Edit the words on the culture (Culture…), or regenerate with the title named.");
        }

        [Category("Realm titles")]
        [DisplayName("Realm word")]
        [Description("This title's own word for itself — Sultanate, League, United Provinces — in "
                     + "place of what its holder's culture calls a realm of this rank. Present only "
                     + "where the generator wrote one; otherwise the culture's word applies.")]
        public string Form { get => Word("Realm word"); set => SetWord("Realm word", value); }

        [Category("Realm titles")]
        [DisplayName("Ruler style (male)")]
        [Description("What a man holding this title is called, when the title has its own word.")]
        public string Holder { get => Word("Ruler style (male)"); set => SetWord("Ruler style (male)", value); }

        [Category("Realm titles")]
        [DisplayName("Ruler style (female)")]
        [Description("What a woman holding this title is called, when the title has its own word.")]
        public string HolderFemale { get => Word("Ruler style (female)"); set => SetWord("Ruler style (female)", value); }

        [Category("Realm titles")]
        [DisplayName("Renders as")]
        [Description("What the game will call this title and its holder at game start, the way "
                     + "the engine decides it: the title's own word if set, else the top liege's "
                     + "culture's word for the holder's government, else vanilla's own rules.")]
        [ReadOnly(true)]
        public string RendersAs => world.RendersAs(Shell);

        [Category("Place")]
        [TypeConverter(typeof(ChoiceConverter))]
        [Description("The de jure capital, from this title's capital line — one of the counties "
                     + "beneath it. Present only where the file has the line.")]
        public string Capital
        {
            get => entry.Field("capital") is not null ? entry.Value("capital") : "—";
            set
            {
                if (entry.Field("capital") is { Write: { } write }) write(value.Trim());
                else if (!string.IsNullOrWhiteSpace(value) && value.Trim() != "—")
                    throw new ArgumentException("This title has no capital line in landed_titles; the engine takes its first county.");
            }
        }

        [Category("Development")]
        [Description("The county's development at the start date — its change_development_level line in the title history.")]
        public int Development
        {
            get => world.DevelopmentOf(Shell);
            set
            {
                if (entry.Field("Development") is { Write: { } write }) write(value.ToString());
                else if (Shell.Tier == "c") throw new ArgumentException("This county's title history has no development line to change.");
            }
        }

        [Category("Buildings")]
        [DisplayName("Special building slot")]
        [Description("The special_building_slot line of this barony's province, where it has one.")]
        public string SpecialSlot
        {
            get => Shell.Tier == "b" ? world.ProvinceOf(Shell)?.Value("special_building_slot") ?? "" : "—";
            set => SetProvinceLeaf("special_building_slot", value);
        }

        [Category("Buildings")]
        [DisplayName("Special building")]
        [Description("The special_building line of this barony's province, where it has one.")]
        public string SpecialBuilding
        {
            get => Shell.Tier == "b" ? world.ProvinceOf(Shell)?.Value("special_building") ?? "" : "—";
            set => SetProvinceLeaf("special_building", value);
        }

        private void SetProvinceLeaf(string key, string value)
        {
            if (Shell.Tier != "b" || value == "—") return;
            if (world.ProvinceOf(Shell)?.Field(key) is { Write: { } write }) write(value);
            else throw new ArgumentException($"This province has no {key} line; the editor changes lines rather than adding them.");
        }

        [Category("People")]
        [TypeConverter(typeof(ChoiceConverter))]
        [Description("The culture of the provinces under this title — the capital's when they "
                     + "differ. Setting it writes every barony beneath, so a duchy converts whole.")]
        public string Culture
        {
            get => world.CultureOf(Shell)?.Key ?? "";
            set => world.SetProvinceField(Shell, "culture", value);
        }

        [Category("People")]
        [TypeConverter(typeof(ChoiceConverter))]
        [Description("The faith of the provinces under this title — the capital's when they "
                     + "differ. Setting it writes every barony beneath.")]
        public string Faith
        {
            get => world.FaithOf(Shell)?.Key ?? "";
            set => world.SetProvinceField(Shell, "religion", value);
        }

        [Category("People")]
        [TypeConverter(typeof(ChoiceConverter))]
        [Description("A barony's holding type. Blank above barony tier. A holding has to match the "
                     + "government its county's ruler holds — a horde's capital is a nomad holding "
                     + "and a mandala realm's a temple citadel — so changing one here changes only "
                     + "the holding, and moving a realm between governments is a conversion the "
                     + "generator does as a set.")]
        public string Holding
        {
            get => Shell.Tier == "b" ? world.ProvinceOf(Shell)?.Value("holding") ?? "" : "";
            set { if (Shell.Tier == "b") world.SetProvinceField(Shell, "holding", value); }
        }

        [Category("Place")]
        [Description("The province this barony is, or -1 for every tier above one.")]
        [ReadOnly(true)]
        public int ProvinceId => Shell.ProvinceId;

        [Category("Place")]
        [Description("The title this one belongs to de jure.")]
        [ReadOnly(true)]
        public string Liege => Shell.Parent is { } p ? $"{TierName(p)} {p.Name}" : "(independent)";

        [Category("Place")]
        [Description("How many titles sit directly beneath this one.")]
        [ReadOnly(true)]
        public int Children => Shell.Children.Count;

        public IReadOnlyList<(string Key, string Label)> Choices(string property) => property switch
        {
            nameof(Culture) => world.World.Entries.Where(e => e.Kind == "Culture").Select(e => (e.Key, e.Name)).ToList(),
            nameof(Faith) => world.World.Entries.Where(e => e.Kind == "Faith").Select(e => (e.Key, e.Name)).ToList(),
            nameof(Holding) => LoadedWorld.Holdings.Select(h => (h, h)).ToList(),
            nameof(Capital) => MapGen.Titles.Flatten([Shell]).Where(t => t.Tier == "c").Select(t => (t.Key, t.Name)).ToList(),
            _ => [],
        };

        public override string ToString() => entry.Name;
    }
}
