using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// Everything editable about a generated culture.
///
/// Every pickable field draws its options from <see cref="VanillaVocabulary.Current"/> — the same
/// harvest of the install's own files the generator writes from — rather than a shipped list. That
/// is what keeps the dropdowns honest across DLC differences: nothing is offered that this install
/// lacks, and nothing the install has goes missing. The dropdowns stay editable and the list
/// pickers keep a custom-keys box, so a key the harvest missed can still be typed; a wrong key
/// costs one line in the error log rather than a broken mod.
/// </summary>
public sealed class CultureInspector : InspectorForm
{
    private readonly Button _heritage = Theme.MakeButton("Heritage…", 90);
    private readonly Button _rerollWords = Theme.MakeButton("Reroll words", 100);

    public CultureInspector(WorldEdits edits) : base(edits, "Culture", new Size(400, 520))
    {
        // Heritage is not editable — it owns the language every name in this culture is drawn from
        // — so this reports rather than navigates.
        _heritage.Click += (_, _) => ShowHeritage();
        _rerollWords.Click += (_, _) => RerollWords();
        AddAction(_heritage);
        AddAction(_rerollWords);
    }

    /// <summary>
    /// A fresh vocabulary for each selected culture's realms, the way the generator draws one —
    /// one of the shipped ladders, applied to every government it suits — so a people that rolled
    /// plain Kingdoms can be given a word without choosing it by hand.
    /// </summary>
    private void RerollWords()
    {
        var pool = Emit.TitleTierWriter.Vocabularies.Where(v => !v.IsPlain).ToList();
        if (pool.Count == 0) return;

        foreach (var culture in Selection.OfType<Culture>().ToList())
        {
            var rng = new Core.Rng(Random.Shared.Next(1, int.MaxValue));
            var words = rng.Pick(pool);
            Edits.EditCultureWords(culture, c => Fields.SetAll(c, words));
        }

        Rebuild();
    }

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Culture>().Select(c => new Fields(c, Edits));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Culture c
            ? $"Culture — {c.Key}"
            : $"{targets.Count} cultures selected";

    protected override string Title(object target) => target is Culture c ? c.Name : "Culture";

    protected override void Refreshed()
    {
        _heritage.Enabled = Selection.Count == 1;
        _rerollWords.Enabled = Edits.IsLoaded && Selection.OfType<Culture>().Any();
    }

    /// <summary>
    /// The shipped vocabularies, by label, plus vanilla's — filtered to the ones the generator would
    /// draw for the government the property is named after, so a theocracy is not offered a Horde.
    /// The all-governments row takes the property name <c>RealmWords</c> and gets everything.
    /// </summary>
    public sealed class RealmWordsConverter : StringConverter
    {
        public const string Vanilla = "(vanilla)";

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            string? government = context?.PropertyDescriptor?.Name is { } name
                                 && name != nameof(Fields.RealmWords)
                ? name.ToLowerInvariant()
                : null;

            var labels = new List<string> { Vanilla };
            labels.AddRange(Emit.TitleTierWriter.Vocabularies
                .Where(v => !v.IsPlain)
                .Where(v => government is null || Emit.TitleTierWriter.Suits(v, government))
                .Select(v => v.Label));

            return new StandardValuesCollection(labels);
        }
    }

    private void ShowHeritage()
    {
        if (Selection.Count != 1 || Selection[0] is not Culture culture) return;

        var h = culture.Heritage;
        MessageBox.Show(this,
            $"Heritage: {h.Name}  ({h.Key})\n\n"
            + $"Cultures sharing it: {string.Join(", ", h.Cultures.Select(c => c.Name))}\n\n"
            + "A heritage owns the language its cultures are named from, so it is fixed — moving a "
            + "culture to another one would leave every place name it generated speaking the wrong "
            + "tongue.",
            "Heritage", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <inheritdoc cref="TitleInspector.Fields"/>
    public sealed class Fields(Culture culture, WorldEdits edits)
    {
        [Category("Identity")]
        [Description("The name shown in game. The hybrid-culture prefix is derived from it, so "
                     + "renaming fixes that too.")]
        public string Name
        {
            get => culture.Name;
            set => edits.RenameCulture(culture, value);
        }

        [Category("Identity")]
        [Description("The script key every other file references. Fixed.")]
        [ReadOnly(true)]
        public string Key => culture.Key;

        [Category("Identity")]
        [Description("The culture group this belongs to, which owns its language. Fixed.")]
        [ReadOnly(true)]
        public string Heritage => culture.Heritage.Name;

        [Category("Identity")]
        [Description("The combining form CK3 splices into a hybrid culture's name. Derived from "
                     + "the name.")]
        [ReadOnly(true)]
        public string Prefix => culture.Prefix;
        [Category("Appearance")]
        [Description("The colour of this culture on the culture map mode.")]
        public Color Color
        {
            get => Color.FromArgb(culture.Color.R, culture.Color.G, culture.Color.B);
            set => edits.EditCulture(culture, c => c.Color = (value.R, value.G, value.B));
        }

        [Category("Visual Appearance")]
        [Description("Convenience preset: applies all 4 visual graphic sets (Clothing, Unit, Building, CoA) from a vanilla culture simultaneously.")]
        [TypeConverter(typeof(LookPresetConverter))]
        public string PresetLook
        {
            get => VanillaVocabulary.Current?.Looks.FirstOrDefault(l =>
                l.ClothingGfx == culture.ClothingGfx &&
                l.UnitGfx == culture.UnitGfx &&
                l.BuildingGfx == culture.BuildingGfx &&
                l.CoaGfx == culture.CoaGfx)?.SourceCulture ?? "(Custom)";
            set
            {
                var match = VanillaVocabulary.Current?.Looks.FirstOrDefault(l =>
                    string.Equals(l.SourceCulture, value.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null) return;

                edits.EditCulture(culture, c =>
                {
                    c.ClothingGfx = match.ClothingGfx;
                    c.UnitGfx = match.UnitGfx;
                    c.BuildingGfx = match.BuildingGfx;
                    c.CoaGfx = match.CoaGfx;
                });
            }
        }

        [Category("Visual Appearance")]
        [Description("The vanilla ethnicity this culture's people are built from — their complexion, "
                     + "and the hair and eye colours they draw from. Changing it regenerates this "
                     + "culture's look alone: the other cultures of its heritage keep theirs, even "
                     + "though they were generated from the same definition. Fantasy races show "
                     + "their race here and cannot be retemplated — their colouring is their own, "
                     + "not a vanilla ethnicity's.")]
        [TypeConverter(typeof(EthnicityTemplateConverter))]
        public string EthnicityTemplate
        {
            get
            {
                if (edits.Ethnicities?.For(culture) is not { } eth) return "(not written yet)";

                return eth.Archetype == RaceArchetype.Human
                    ? eth.BaseTemplate
                    : $"({Ethnicities.RaceName(eth.Archetype)})";
            }
            set => edits.EditCultureEthnicity(culture, value);
        }

        [Category("Visual Appearance")]
        [Description("The clothing, armor, and headgear graphic set for portraits.")]
        [TypeConverter(typeof(ClothingGfxConverter))]
        public string ClothingGfx
        {
            get => culture.ClothingGfx;
            set => edits.EditCulture(culture, c => c.ClothingGfx = value.Trim());
        }

        [Category("Visual Appearance")]
        [Description("The 3D map unit soldier, armor and weapon model set.")]
        [TypeConverter(typeof(UnitGfxConverter))]
        public string UnitGfx
        {
            get => culture.UnitGfx;
            set => edits.EditCulture(culture, c => c.UnitGfx = value.Trim());
        }

        [Category("Visual Appearance")]
        [Description("The 3D settlement and holding model set.")]
        [TypeConverter(typeof(BuildingGfxConverter))]
        public string BuildingGfx
        {
            get => culture.BuildingGfx;
            set => edits.EditCulture(culture, c => c.BuildingGfx = value.Trim());
        }

        [Category("Visual Appearance")]
        [Description("Coat of arms heraldic styling and charge palettes.")]
        [TypeConverter(typeof(CoaGfxConverter))]
        public string CoaGfx
        {
            get => culture.CoaGfx;
            set => edits.EditCulture(culture, c => c.CoaGfx = value.Trim());
        }

        [Category("Character")]
        [Description("The culture's ethos pillar — a CK3 script key such as ethos_bellicose.")]
        [TypeConverter(typeof(EthosConverter))]
        public string Ethos
        {
            get => culture.Ethos;
            set => edits.EditCulture(culture, c => c.Ethos = value.Trim());
        }

        [Category("Character")]
        [Description("The martial custom pillar — a CK3 script key such as martial_custom_male_only.")]
        [TypeConverter(typeof(MartialCustomConverter))]
        public string MartialCustom
        {
            get => culture.MartialCustom;
            set => edits.EditCulture(culture, c => c.MartialCustom = value.Trim());
        }

        [Category("Character")]
        [Description("How this culture picks a cultural head — a CK3 script key.")]
        [TypeConverter(typeof(HeadDeterminationConverter))]
        public string HeadDetermination
        {
            get => culture.HeadDetermination;
            set => edits.EditCulture(culture, c => c.HeadDetermination = value.Trim());
        }

        [Category("Character")]
        [Description("The culture's traditions. Opens a picker over the install's harvested "
                     + "tradition list — vanilla cultures carry three to five.")]
        [Editor(typeof(TraditionListEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string[] Traditions
        {
            get => [.. culture.Traditions];
            set => edits.EditCulture(culture,
                c => c.Traditions = [.. (value ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)]);
        }

        // --- Realm titles ---
        //
        // What this people calls its realms and their rulers, per government — the flavorization
        // TitleTierWriter writes. One row sets every government at once; the rows below it tune
        // one government each, and the dropdown on each only offers what the generator would have
        // drawn for it.

        [Category("Realm titles")]
        [DisplayName("All governments")]
        [TypeConverter(typeof(RealmWordsConverter))]
        [Description("What this people calls its realms and their rulers — empire, kingdom, duchy "
                     + "and the emperor, king and duke holding them — for every government the "
                     + "vocabulary suits; the rest keep vanilla's words. A realm takes its top "
                     + "liege's culture's words, so a vassal of another people is styled the way "
                     + "its liege is. Pick one here to set the whole culture, or tune a single "
                     + "government below.")]
        public string RealmWords
        {
            get => Summary(null);
            set => SetWords(null, value);
        }

        [Category("Realm titles")]
        [DisplayName("Rulers")]
        [Description("The holders' styles for the vocabulary above, top down.")]
        [ReadOnly(true)]
        public string RealmHolders
            => Uniform() is { } words ? words.Holders
             : culture.RealmWords.Count == 0 ? "Emperor · King · Duke (vanilla)"
             : "(varies by government)";

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Feudal { get => Summary("feudal"); set => SetWords("feudal", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Clan { get => Summary("clan"); set => SetWords("clan", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Tribal { get => Summary("tribal"); set => SetWords("tribal", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Republic { get => Summary("republic"); set => SetWords("republic", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Theocracy { get => Summary("theocracy"); set => SetWords("theocracy", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Administrative { get => Summary("administrative"); set => SetWords("administrative", value); }

        [Category("Realm titles")] [TypeConverter(typeof(RealmWordsConverter))]
        public string Nomad { get => Summary("nomad"); set => SetWords("nomad", value); }

        /// <summary>The one vocabulary every government with a word shares, or null if they differ or none has one.</summary>
        private Emit.TitleVocabulary? Uniform()
        {
            var distinct = culture.RealmWords.Values.Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : null;
        }

        private string Summary(string? government)
        {
            if (government is not null)
                return culture.RealmWords.TryGetValue(government, out var words)
                    ? words.Label
                    : RealmWordsConverter.Vanilla;

            if (culture.RealmWords.Count == 0) return RealmWordsConverter.Vanilla;
            return Uniform()?.Label ?? "(varies by government)";
        }

        private void SetWords(string? government, string value)
        {
            value = value.Trim();
            Emit.TitleVocabulary? words = null;

            if (!string.Equals(value, RealmWordsConverter.Vanilla, StringComparison.OrdinalIgnoreCase))
            {
                words = Emit.TitleTierWriter.Vocabularies.FirstOrDefault(v =>
                    string.Equals(v.Label, value, StringComparison.OrdinalIgnoreCase));

                // Not a vocabulary we know — a typo, or the mixed-state placeholder handed back.
                if (words is null) return;
                if (words.IsPlain) words = null;
            }

            edits.EditCultureWords(culture, c =>
            {
                if (government is null) SetAll(c, words);
                else if (words is null) c.RealmWords.Remove(government);
                else c.RealmWords[government] = words;
            });
        }

        /// <summary>
        /// Gives every government the same words, the way the generator's draw does: only the
        /// governments the vocabulary suits, the rest back to vanilla. Null clears the culture.
        /// </summary>
        internal static void SetAll(Culture c, Emit.TitleVocabulary? words)
        {
            c.RealmWords.Clear();
            if (words is null) return;

            foreach (string government in Emit.TitleTierWriter.Governments)
                if (Emit.TitleTierWriter.Suits(words, government))
                    c.RealmWords[government] = words;
        }

        [Category("Extent")]
        [Description("How many counties speak this culture at the start date.")]
        [ReadOnly(true)]
        public int Counties => culture.Counties.Count;

        [Category("Extent")]
        [Description("Mean development of the counties speaking it, which is what the generator "
                     + "grew this culture's character from.")]
        [ReadOnly(true)]
        public string Development => culture.MeanDevelopment.ToString("F1");

        public override string ToString() => culture.Name;
    }
}
