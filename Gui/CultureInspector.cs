using System.ComponentModel;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// Everything editable about a generated culture.
///
/// Traditions, ethos and martial custom are free text rather than dropdowns. Every one of them is a
/// CK3 script key, and the set that exists depends on which DLC the install has — the generator
/// harvests them from the game's own files at write time rather than shipping a list. Offering a
/// closed dropdown would mean either duplicating that harvest or presenting keys the install does
/// not have, and a wrong key here costs one line in the error log rather than a broken mod.
/// </summary>
public sealed class CultureInspector : InspectorForm
{
    private readonly Button _heritage = Theme.MakeButton("Heritage…", 90);

    public CultureInspector(WorldEdits edits) : base(edits, "Culture", new Size(400, 520))
    {
        // Heritage is not editable — it owns the language every name in this culture is drawn from
        // — so this reports rather than navigates.
        _heritage.Click += (_, _) => ShowHeritage();
        AddAction(_heritage);
    }

    protected override IEnumerable<object> Wrap(IReadOnlyList<object> targets)
        => targets.OfType<Culture>().Select(c => new Fields(c, Edits));

    protected override string Describe(IReadOnlyList<object> targets)
        => targets.Count == 1 && targets[0] is Culture c
            ? $"Culture — {c.Key}"
            : $"{targets.Count} cultures selected";

    protected override string Title(object target) => target is Culture c ? c.Name : "Culture";

    protected override void Refreshed() => _heritage.Enabled = Selection.Count == 1;

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
        [Description("The culture's traditions, one CK3 script key per line. Vanilla cultures carry "
                     + "three to five.")]
        [Editor("System.Windows.Forms.Design.StringArrayEditor, System.Design",
                typeof(System.Drawing.Design.UITypeEditor))]
        public string[] Traditions
        {
            get => [.. culture.Traditions];
            set => edits.EditCulture(culture,
                c => c.Traditions = [.. (value ?? []).Select(t => t.Trim()).Where(t => t.Length > 0)]);
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
