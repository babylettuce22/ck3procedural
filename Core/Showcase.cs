namespace Ck3MapGen.Core;

/// <summary>
/// One thing the generator just made, as a finished picture of it for someone watching a run: a
/// culture, a faith, a regiment, a treasure. Plain values only — no reference back into the world
/// being generated — so it can be handed to another thread and kept after the run ends.
/// </summary>
public sealed record ShowcaseItem
{
    /// <summary>What sort of thing this is, shown as a small label: "Culture", "Men-at-arms".</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Body { get; init; }

    /// <summary>Short facts shown as chips: a tradition, a tenet, a stat.</summary>
    public IReadOnlyList<string> Chips { get; init; } = [];

    /// <summary>A colour the thing is known by — a culture's, a faith's — for a swatch.</summary>
    public (byte R, byte G, byte B)? Color { get; init; }

    /// <summary>
    /// Image files to try, in order, as absolute paths (.dds or .png): the first that exists and
    /// decodes is used. Usually the mod's own file first and the game's second.
    /// </summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    /// <summary>A wide illustration shown across the top, rather than an icon beside the text.</summary>
    public bool Illustration { get; init; }

    /// <summary>
    /// For an image that is a horizontal strip of equal frames — CK3's artifact icons hold one per
    /// rarity side by side — how many frames it has, and which one to show.
    /// </summary>
    public int ImageFrames { get; init; } = 1;
    public int ImageFrame { get; init; }
}

/// <summary>
/// A window onto a run as it happens, for a screen that wants something to show while it waits.
///
/// The generator publishes finished things here straight after the stage that made them. It is
/// a spectator's channel and nothing more, held to three rules so it can never change a world:
/// <list type="bullet">
/// <item><b>Read only.</b> Items are built from what a stage already decided. Nothing here draws
/// from a generator's <see cref="Rng"/> or calls anything that could, so a run with a listener is
/// byte-identical to one without.</item>
/// <item><b>Free when nobody watches.</b> Items are built lazily, and only while
/// <see cref="Listening"/>; the command line never pays for them.</item>
/// <item><b>Never fatal.</b> A mistake in building an item, or in whoever receives it, is
/// swallowed. A picture that could not be made must not cost the user their mod.</item>
/// </list>
/// Handlers are called on the generator's thread and must hand the item over to their own.
/// </summary>
public static class Showcase
{
    public static event Action<ShowcaseItem>? Published;

    public static bool Listening => Published is not null;

    /// <summary>Builds and publishes items, but only if someone is listening.</summary>
    public static void Publish(Func<IEnumerable<ShowcaseItem>> build)
    {
        var handler = Published;
        if (handler is null) return;

        List<ShowcaseItem> items;
        try
        {
            items = build().ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (showcase: {ex.GetType().Name}: {ex.Message})");
            return;
        }

        foreach (var item in items)
        {
            try { handler(item); }
            catch (Exception) { }
        }
    }
}
