using Ck3MapGen.Emit;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Gui;

/// <summary>
/// What the game will call a title and its holder, for the inspectors' read-only previews.
///
/// One resolver shared by the Title and Ruler inspectors so the two cannot disagree. It follows the
/// same steps the engine does against the flavorization <see cref="TitleTierWriter"/> writes: the
/// title's own word wins, else the <em>top liege's</em> culture's word for the holder's own
/// government, else vanilla's rules — which depend on the government and are not guessed at here.
/// </summary>
public static class RealmStyle
{
    public static string Describe(Title title, RealmGraph? realm, WrittenContent? written)
    {
        if (title.Tier is not ("e" or "k" or "d")) return "—";

        // Before a write, or with history skipped, nobody holds the title and there is no liege to
        // take a culture from; the title's own word is still known.
        if (realm is null || written is null || realm.SeatOf(title) is not { } seat)
        {
            return string.IsNullOrWhiteSpace(title.Form)
                ? "(vanilla words — no holder written yet)"
                : $"{title.Form.Trim()} of {title.Name}";
        }

        var top = realm.PathFromTop(seat)[0];
        var liegeCulture = written.Cultures.For(top);
        string government = written.Governments?.For(seat) ?? GovernmentMap.Feudal;
        bool female = written.Rulers is { } rulers && rulers.TryGet(seat, out var ruler) && ruler.Female;

        return TitleTierWriter.Resolve(title, liegeCulture, government, female) is { } words
            ? $"{words.Realm} of {title.Name} — {words.Holder}"
            : "(vanilla words, by government)";
    }
}
