using System.ComponentModel;
using System.Drawing.Design;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.AppGUI;

/// <summary>
/// A property-grid editor for the string-array fields whose values are CK3 script keys with a
/// known, harvested vocabulary. Replaces the stock StringArrayEditor, which was a bare multiline
/// textbox that only worked if you already knew the keys by heart.
///
/// This is for the fields whose length genuinely varies — a culture carries three to five
/// traditions. A field with a fixed number of slots is better as one dropdown per slot; see the
/// tenet slots on <see cref="FaithInspector.Fields"/>, which used to be a list here.
///
/// The list is a CheckedListBox of every key the install actually has, drawn from the same
/// <see cref="VanillaVocabulary"/> harvest the generator itself writes from — the same reason the
/// single-value dropdowns use <see cref="DynamicVocabularyConverter"/>: no shipped list to go
/// stale, and nothing offered that this install lacks. A custom-keys box below keeps the old
/// escape hatch open, and any current value the vocabulary does not recognise lands there rather
/// than being silently dropped — an install without a DLC must not eat keys written with it.
/// </summary>
public abstract class VocabularyListEditor(
    Func<VanillaVocabulary, IEnumerable<string>> selector, string noun, string countHint)
    : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        => UITypeEditorEditStyle.Modal;

    public override object? EditValue(
        ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        var current = value as string[] ?? [];
        var vocab = VanillaVocabulary.Current;

        var known = vocab is null
            ? []
            : selector(vocab)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

        // With nothing harvested (an inspector opened before any generation ran) there is no list
        // to offer, and a modal that could only echo the textbox back would be noise.
        if (known.Count == 0) return value;

        var custom = current.Where(c => !known.Contains(c, StringComparer.Ordinal)).ToList();

        using var dialog = new Form
        {
            Text = $"Pick {noun}",
            Size = new Size(420, 560),
            MinimumSize = new Size(340, 400),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
        };

        var count = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            ForeColor = Theme.TextDim,
        };

        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
        };
        foreach (var key in known)
            list.SetItemChecked(list.Items.Add(key), current.Contains(key, StringComparer.Ordinal));

        var customLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Text = "Custom keys (one per line — for keys this install's harvest missed):",
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(8, 0, 8, 2),
            ForeColor = Theme.TextDim,
        };

        var customBox = new TextBox
        {
            Dock = DockStyle.Bottom,
            Multiline = true,
            Height = 64,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Text = string.Join(Environment.NewLine, custom),
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40,
            Padding = new Padding(4),
        };
        var ok = Theme.MakeButton("OK", 80);
        var cancel = Theme.MakeButton("Cancel", 80);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        void UpdateCount()
        {
            int customCount = customBox.Lines.Count(l => !string.IsNullOrWhiteSpace(l));
            count.Text = $"{list.CheckedItems.Count + customCount} selected — {countHint}";
        }
        // ItemCheck fires BEFORE the check state changes, so the recount runs a beat later.
        list.ItemCheck += (_, _) => dialog.BeginInvoke(UpdateCount);
        customBox.TextChanged += (_, _) => UpdateCount();

        dialog.Controls.Add(list);
        dialog.Controls.Add(count);
        dialog.Controls.Add(customLabel);
        dialog.Controls.Add(customBox);
        dialog.Controls.Add(buttons);
        // Dock order quirk: last-added Bottom control sits highest, so re-add in display order.
        customLabel.SendToBack(); customBox.SendToBack(); buttons.SendToBack();
        list.BringToFront();
        UpdateCount();

        if (dialog.ShowDialog() != DialogResult.OK) return value;

        // Keys the user already had keep their original order — order is visible in game and a
        // reorder nobody asked for reads as churn — and newly picked ones follow in list order.
        var chosen = list.CheckedItems.Cast<string>().ToHashSet(StringComparer.Ordinal);
        var result = current.Where(chosen.Contains).ToList();
        result.AddRange(chosen.Where(k => !result.Contains(k, StringComparer.Ordinal)));
        result.AddRange(customBox.Lines.Select(l => l.Trim())
            .Where(l => l.Length > 0 && !result.Contains(l, StringComparer.Ordinal)));

        return result.ToArray();
    }
}

/// <summary>Culture traditions, from the install's harvested tradition list.</summary>
public sealed class TraditionListEditor()
    : VocabularyListEditor(v => v.Traditions, "traditions", "vanilla cultures carry 3 to 5");
