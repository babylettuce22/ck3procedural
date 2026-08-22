using System.ComponentModel;
using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// Everything editable about a living ruler — the fourth inspector, reached from a title's
/// "Ruler…" button, which is the same man the title's Held by names.
///
/// Two kinds of field, and the split is the one <see cref="Ruler"/> draws. What other files
/// reference — the character id, the seat, the house — is shown and not editable, because a change
/// there would dangle a reference somewhere else in the mod. The character's own values — name,
/// sex, birth year, schooling, traits, skills, purse — are editable and re-emitted in place by
/// <see cref="WorldOverwrite"/>. The profile fields write through <c>with</c> on the immutable
/// <see cref="RulerProfile"/>, so the generated one survives for Revert.
/// </summary>
public sealed class RulerInspector : InspectorForm
{
    private readonly Button _reroll = Theme.MakeButton("Reroll name", 100);
    private readonly Button _title = Theme.MakeButton("Title…", 70);
    private readonly Button _culture = Theme.MakeButton("Culture…", 84);
    private readonly Button _faith = Theme.MakeButton("Faith…", 76);

    public RulerInspector(WorldEdits edits) : base(edits, "Ruler", new Size(400, 560))
    {
        _reroll.Click += (_, _) => Reroll();
        _title.Click += (_, _) => { if (Single is { } r) GoTo(r.PrimaryTitle); };
        _culture.Click += (_, _) => { if (Single is { } r) GoTo(r.Culture); };
        _faith.Click += (_, _) => { if (Single is { } r) GoTo(r.Faith); };

        AddAction(_reroll);
        AddAction(_title);
        AddAction(_culture);
        AddAction(_faith);
    }

    /// <summary>
    /// The written world's de facto structure, set by the owning window on every visit, the same
    /// as <see cref="TitleInspector.Realm"/>. Needed to say how the game will style this ruler:
    /// the word comes from the <em>top liege's</em> culture, and the top liege is a walk up the
    /// graph.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RealmGraph? Realm { get; set; }

    private Ruler? Single => Selection.Count == 1 && Selection[0] is Ruler r ? r : null;

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Ruler>().Select(r => new Fields(r, Edits, Realm));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Ruler r
            ? $"{TitleInspector.TierName(r.PrimaryTitle)} {r.PrimaryTitle.Name} — {r.Id}"
            : $"{targets.Count} rulers selected";

    protected override string Title(object target) => target is Ruler r ? r.Name : "Ruler";

    protected override void Refreshed()
    {
        bool single = Edits.IsLoaded && Single is not null;
        _title.Enabled = single;
        _culture.Enabled = single;
        _faith.Enabled = single;
        _reroll.Enabled = Edits.IsLoaded && Selection.OfType<Ruler>().Any();
    }

    /// <summary>
    /// A new name from the ruler's own culture, drawn for the sex the ruler is now — the generated
    /// name always came from the male list, so this is also how a ruler made female gets a name
    /// that fits.
    /// </summary>
    private void Reroll()
    {
        foreach (var ruler in Selection.OfType<Ruler>().ToList())
        {
            var names = ruler.Female ? ruler.Culture.FemaleNames : ruler.Culture.MaleNames;
            if (names.Count == 0) continue;

            var rng = new Core.Rng(Random.Shared.Next(1, int.MaxValue));
            Edits.RenameRuler(ruler, rng.Pick(names));
        }

        Rebuild();
    }

    // --- Dropdowns ----------------------------------------------------------------------------

    /// <summary>The five lifestyles, and nothing else: the education trait key is built from it.</summary>
    public sealed class LifestyleConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(new[]
            {
                RulerProfile.DiplomacyLifestyle, RulerProfile.MartialLifestyle,
                RulerProfile.StewardshipLifestyle, RulerProfile.IntrigueLifestyle,
                RulerProfile.LearningLifestyle,
            });
    }

    /// <summary>The lifestyles plus "none", for the optional second tree.</summary>
    public sealed class OptionalLifestyleConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(new[]
            {
                "", RulerProfile.DiplomacyLifestyle, RulerProfile.MartialLifestyle,
                RulerProfile.StewardshipLifestyle, RulerProfile.IntrigueLifestyle,
                RulerProfile.LearningLifestyle,
            });
    }

    /// <summary>Vanilla's legitimacy script values. Editable: a modded install may have more.</summary>
    public sealed class LegitimacyConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(new[]
            {
                "", "legitimacy_level_1", "legitimacy_level_2", "legitimacy_level_3",
                "legitimacy_level_4", "legitimacy_level_5",
            });
    }

    /// <summary>
    /// The bynames the generator hands out. Editable, because vanilla has a few hundred more and
    /// any <c>nick_*</c> key the install knows is fine.
    /// </summary>
    public sealed class NicknameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
            => new(new[]
            {
                "", "nick_the_bold", "nick_the_strong", "nick_the_lionheart", "nick_the_victorious",
                "nick_the_hammer", "nick_the_ironside", "nick_the_fair", "nick_the_good",
                "nick_the_just", "nick_the_generous", "nick_the_magnificent", "nick_the_builder",
                "nick_the_lawgiver", "nick_the_fox", "nick_the_spider", "nick_the_shrewd",
                "nick_the_wise", "nick_the_pious", "nick_the_scholar", "nick_the_great",
                "nick_the_conqueror",
            });
    }

    // --- Fields -------------------------------------------------------------------------------

    /// <inheritdoc cref="TitleInspector.Fields"/>
    public sealed class Fields(Ruler ruler, WorldEdits edits, RealmGraph? realm)
    {
        private RulerProfile P => ruler.Profile;

        private void Profile(Func<RulerProfile, RulerProfile> change)
            => edits.EditRuler(ruler, r => r.Profile = change(r.Profile));

        private int StartYear => edits.Target?.Result.Config.StartYear ?? ruler.BirthYear + 16;

        private PrehistoryMap? Prehistory => edits.Target?.Written.Prehistory;

        // --- Seat (read-only) ---

        [Category("Seat")]
        [Description("The county this ruler sits in. Fixed — it is how every map of rulers is keyed.")]
        [ReadOnly(true)]
        public string Seat => ruler.Seat.Name;

        [Category("Seat")]
        [DisplayName("Primary title")]
        [Description("The highest title held, which the character was graded by.")]
        [ReadOnly(true)]
        public string PrimaryTitle
            => $"{TitleInspector.TierName(ruler.PrimaryTitle)} {ruler.PrimaryTitle.Name}";

        [Category("Seat")]
        [Description("Independent, or sworn to a liege.")]
        [ReadOnly(true)]
        public string Standing => ruler.Independent ? "(independent)" : "vassal";

        [Category("Seat")]
        [DisplayName("Styled as")]
        [Description("How the game will name the primary title and this ruler — the title's own "
                     + "word if it has one, else the top liege's culture's word for this government. "
                     + "Not a property of the character: change it on the culture (Culture…) or "
                     + "override it on the title (Title…).")]
        [ReadOnly(true)]
        public string StyledAs => RealmStyle.Describe(ruler.PrimaryTitle, realm, edits.Target?.Written);

        [Category("Seat")]
        [DisplayName("Character id")]
        [Description("The history id every other file references. Fixed.")]
        [ReadOnly(true)]
        public string Id => ruler.Id;

        [Category("Seat")]
        [Description("The house the character is written into. Fixed — the house file, the "
                     + "ancestors and the heirs all point at it.")]
        [ReadOnly(true)]
        public string House
            => Prehistory?.Houses.TryGetValue(ruler.HouseKey, out var h) == true
                ? $"{h.LocalizedName}  ({ruler.HouseKey})"
                : ruler.HouseKey;

        [Category("Seat")]
        [ReadOnly(true)]
        public string Dynasty
            => Prehistory?.Dynasties.TryGetValue(ruler.DynastyId, out var d) == true
                ? $"{d.LocalizedName}  ({ruler.DynastyId})"
                : ruler.DynastyId;

        // --- Identity ---

        [Category("Identity")]
        [Description("The given name. Renaming rewrites the character file and the bookmark "
                     + "screen; artifact and chronicle prose keeps the generated name.")]
        public string Name
        {
            get => ruler.Name;
            set => edits.RenameRuler(ruler, value);
        }

        [Category("Identity")]
        [Description("Generated rulers are all male. Making one female writes her as such "
                     + "everywhere the engine asks; use Reroll name for a name from the right "
                     + "list. The spouse prehistory married her to stays as written.")]
        public bool Female
        {
            get => ruler.Female;
            set => edits.EditRuler(ruler, r => r.Female = value);
        }

        [Category("Identity")]
        [DisplayName("Birth year")]
        [Description("Held inside the range below, which is what the family written around this "
                     + "ruler allows. Moving it does not re-roll anything age-dependent in the "
                     + "profile — those are yours to change.")]
        public int BirthYear
        {
            get => ruler.BirthYear;
            set => edits.SetRulerBirthYear(ruler, value);
        }

        [Category("Identity")]
        [DisplayName("Birth year range")]
        [Description("Sixteen years after the father, sixteen before the wedding and every child, "
                     + "and an adult at the start date.")]
        [ReadOnly(true)]
        public string BirthYearRange
        {
            get
            {
                var (min, max) = edits.RulerBirthYearBounds(ruler);
                return $"{min} – {max}";
            }
        }

        [Category("Identity")]
        [Description("At the start date.")]
        [ReadOnly(true)]
        public int Age => StartYear - ruler.BirthYear;

        // --- Education ---

        [Category("Education")]
        [TypeConverter(typeof(LifestyleConverter))]
        [Description("The tree the education trait belongs to, and so the one the perk points "
                     + "below are spendable in. Changing it rewrites the education trait.")]
        public string Lifestyle
        {
            get => P.Lifestyle;
            set => Profile(p => p with
            {
                Lifestyle = value,
                EducationTrait = $"education_{value}_{p.EducationLevel}",
            });
        }

        [Category("Education")]
        [DisplayName("Education level")]
        [Description("1 to 5, as vanilla grades it. Changing it rewrites the education trait.")]
        public int EducationLevel
        {
            get => P.EducationLevel;
            set
            {
                int level = Math.Clamp(value, 1, 5);
                Profile(p => p with
                {
                    EducationLevel = level,
                    EducationTrait = $"education_{p.Lifestyle}_{level}",
                });
            }
        }

        [Category("Education")]
        [DisplayName("Education trait")]
        [ReadOnly(true)]
        public string EducationTrait => P.EducationTrait;

        [Category("Education")]
        [DisplayName("Perk points")]
        [Description("Lifestyle perk points granted at game start in the education's tree, on "
                     + "top of what vanilla auto-assigns for age.")]
        public int PerkPoints
        {
            get => P.PerkPoints;
            set => Profile(p => p with { PerkPoints = Math.Clamp(value, 0, 12) });
        }

        [Category("Education")]
        [DisplayName("Second lifestyle")]
        [TypeConverter(typeof(OptionalLifestyleConverter))]
        [Description("A second tree the ruler has dabbled in, or none.")]
        public string SecondLifestyle
        {
            get => P.SecondLifestyle ?? "";
            set => Profile(p => p with { SecondLifestyle = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
        }

        [Category("Education")]
        [DisplayName("Second perk points")]
        public int SecondPerkPoints
        {
            get => P.SecondPerkPoints;
            set => Profile(p => p with { SecondPerkPoints = Math.Clamp(value, 0, 12) });
        }

        // --- Traits ---

        [Category("Traits")]
        [DisplayName("Personality")]
        [Description("Vanilla trait keys, one per line — brave, greedy, just. Three is what the "
                     + "generator writes and what the engine expects; opposites (brave and craven) "
                     + "cannot both be held.")]
        public string[] PersonalityTraits
        {
            get => [.. P.PersonalityTraits];
            set => Profile(p => p with { PersonalityTraits = Clean(value) });
        }

        [Category("Traits")]
        [DisplayName("Other traits")]
        [Description("Congenital, commander, lifestyle, scar and coping traits, one key per line. "
                     + "The phenotype trait is added by the writer and is not listed here.")]
        public string[] OtherTraits
        {
            get => [.. P.OtherTraits];
            set => Profile(p => p with { OtherTraits = Clean(value) });
        }

        [Category("Traits")]
        [TypeConverter(typeof(NicknameConverter))]
        [Description("A vanilla nick_* key, or blank for none.")]
        public string Nickname
        {
            get => P.Nickname ?? "";
            set => Profile(p => p with { Nickname = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
        }

        // --- Skills ---

        [Category("Skills")]
        public int Diplomacy { get => P.Diplomacy; set => Profile(p => p with { Diplomacy = Skill(value) }); }

        [Category("Skills")]
        public int Martial { get => P.Martial; set => Profile(p => p with { Martial = Skill(value) }); }

        [Category("Skills")]
        public int Stewardship { get => P.Stewardship; set => Profile(p => p with { Stewardship = Skill(value) }); }

        [Category("Skills")]
        public int Intrigue { get => P.Intrigue; set => Profile(p => p with { Intrigue = Skill(value) }); }

        [Category("Skills")]
        public int Learning { get => P.Learning; set => Profile(p => p with { Learning = Skill(value) }); }

        [Category("Skills")]
        public int Prowess { get => P.Prowess; set => Profile(p => p with { Prowess = Skill(value) }); }

        // --- Standing ---

        [Category("Standing")]
        [Description("Starting gold, already scaled for the government.")]
        public int Gold
        {
            get => ruler.Gold;
            set => edits.EditRuler(ruler, r => r.Gold = Math.Max(0, value));
        }

        [Category("Standing")]
        [Description("Starting prestige. Vanilla's levels sit at 1000, 2000, 5000, 10000, 25000, "
                     + "and the level is an opinion modifier on everyone.")]
        public int Prestige
        {
            get => ruler.Prestige;
            set => edits.EditRuler(ruler, r => r.Prestige = Math.Max(0, value));
        }

        [Category("Standing")]
        [Description("Starting dynasty prestige. Only paid out to an independent ruler.")]
        public int Renown
        {
            get => ruler.Renown;
            set => edits.EditRuler(ruler, r => r.Renown = Math.Max(0, value));
        }

        [Category("Standing")]
        [Description("Starting dread. Zero for rulers with nobody to frighten.")]
        public int Dread
        {
            get => P.Dread;
            set => Profile(p => p with { Dread = Math.Clamp(value, 0, 100) });
        }

        [Category("Standing")]
        [TypeConverter(typeof(LegitimacyConverter))]
        [Description("A legitimacy script value, or blank for a government that has none to gain.")]
        public string Legitimacy
        {
            get => P.Legitimacy ?? "";
            set => Profile(p => p with { Legitimacy = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
        }

        [Category("Standing")]
        [DisplayName("Stability years")]
        [Description("How long the early-reign stability modifier runs. Only written for "
                     + "independent rulers and for dukes and above.")]
        public int StabilityYears
        {
            get => P.StabilityYears;
            set => Profile(p => p with { StabilityYears = Math.Clamp(value, 0, 30) });
        }

        private static int Skill(int value) => Math.Clamp(value, 0, 50);

        private static List<string> Clean(string[]? values)
            => [.. (values ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)];

        public override string ToString() => ruler.Name;
    }
}
