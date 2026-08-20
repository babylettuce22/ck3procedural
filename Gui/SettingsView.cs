using System.ComponentModel;
using Ck3MapGen.Config;

namespace Ck3MapGen.Gui;

/// <summary>
/// The settings grid's window onto <see cref="MapConfig"/>: one section at a time, searchable,
/// with the display categories cleaned up.
///
/// A second descriptor layered over the config's own rather than a replacement for it. MapConfig's
/// descriptor already answers the questions that belong to the *data* — which settings are advanced,
/// which an Azgaar export has taken over — and this one answers the questions that belong to the
/// *pane*: which section the user is looking at, what they typed into the search box, and the fact
/// that "03 Provinces" is a sort key and not a name anyone should read. Composing keeps both filters
/// in one query path, so a row decorated read-only by the export stays decorated when it surfaces
/// through a search.
/// </summary>
public sealed class SettingsView : CustomTypeDescriptor
{
    private readonly MapConfig _config;

    /// <summary>Raw (numbered) category to show, or null for all of them.</summary>
    public string? Section { get; set; }

    public string Search { get; set; } = "";

    public SettingsView(MapConfig config)
        : base(TypeDescriptor.GetProvider(typeof(MapConfig)).GetTypeDescriptor(typeof(MapConfig)))
    {
        _config = config;
    }

    /// <summary>The sections in authoring order — the numeric prefixes are the pipeline order.</summary>
    public static IReadOnlyList<string> Sections { get; } =
        [.. typeof(MapConfig).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(CategoryAttribute), true)
                .OfType<CategoryAttribute>().FirstOrDefault()?.Category)
            .Where(c => c is not null)
            .Distinct()
            .OrderBy(c => int.TryParse(c!.Split(' ')[0], out int n) ? n : int.MaxValue)
            .Cast<string>()];

    /// <summary>
    /// "03 Provinces" → "Provinces". Ampersands become "and" because the grid's category header
    /// renders them as accelerator marks — "Fantasy &amp; Ethnicities" came out underlined and
    /// missing its &amp;.
    /// </summary>
    public static string DisplayName(string category)
    {
        int at = 0;
        while (at < category.Length && char.IsDigit(category[at])) at++;
        while (at < category.Length && category[at] == ' ') at++;

        string name = at > 0 && at < category.Length ? category[at..] : category;
        return name.Replace("&", "and");
    }

    public override object GetPropertyOwner(PropertyDescriptor? pd) => _config;

    public override PropertyDescriptorCollection GetProperties() => GetProperties(null);

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var shown = new List<PropertyDescriptor>();

        foreach (PropertyDescriptor property in _config.GetProperties(attributes))
        {
            // Two filters the grid used to apply for free when it owned the descriptor directly:
            // hidden rows (the grid checks IsBrowsable itself, this view has to), and the
            // descriptor plumbing MapConfig inherits — CustomTypeDescriptor grew a public
            // RequireRegisteredTypes property in .NET 9, which is a grid row nobody asked for.
            if (!property.IsBrowsable || property.ComponentType != typeof(MapConfig)) continue;

            // A search spans every section — the searcher doesn't know which section holds the
            // knob, that being the reason they are searching — and the section filter resumes
            // when the box empties.
            bool searching = !string.IsNullOrWhiteSpace(Search);
            if (!searching && Section is not null && property.Category != Section) continue;
            if (searching && !Matches(property)) continue;

            shown.Add(new Renamed(property, DisplayName(property.Category)));
        }

        return new PropertyDescriptorCollection([.. shown]);
    }

    private bool Matches(PropertyDescriptor property)
    {
        if (string.IsNullOrWhiteSpace(Search)) return true;

        return Has(property.Name) || Has(property.DisplayName)
            || Has(property.Category) || Has(property.Description);

        bool Has(string? text)
            => text is not null && text.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A property wearing its category's display name. Every answer the grid asks for is forwarded
    /// as a call, not copied from attributes — the inner descriptor may itself be a wrapper (the
    /// Azgaar-overridden rows override <see cref="PropertyDescriptor.Description"/> as a property),
    /// and the copying constructor would quietly flatten that back to the attribute's text.
    /// </summary>
    private sealed class Renamed(PropertyDescriptor inner, string category) : PropertyDescriptor(inner)
    {
        public override string Category => category;
        public override string Description => inner.Description;
        public override string DisplayName => inner.DisplayName;
        public override bool IsReadOnly => inner.IsReadOnly;

        public override Type ComponentType => inner.ComponentType;
        public override Type PropertyType => inner.PropertyType;
        public override bool CanResetValue(object component) => inner.CanResetValue(component);
        public override object? GetValue(object? component) => inner.GetValue(component);
        public override void ResetValue(object component) => inner.ResetValue(component);
        public override void SetValue(object? component, object? value) => inner.SetValue(component, value);
        public override bool ShouldSerializeValue(object component) => inner.ShouldSerializeValue(component);
        public override object? GetEditor(Type editorBaseType) => inner.GetEditor(editorBaseType);
        public override TypeConverter Converter => inner.Converter;
    }
}
