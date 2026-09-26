namespace Ck3MapGen.MapGen;

/// <summary>
/// An applied history's chronicle: what the simulation remembered (<see cref="AppliedHistory.Chronicle"/>),
/// written out as <see cref="ChronicleEvent"/>s for the lore panel and the struggles, in the same
/// plain voice as the rest of the chronicle.
///
/// The words are written here, when the history is applied, from the names the world has then —
/// not when it was captured — so a title renamed in the editor is remembered by its new name. A
/// realm is named for the seat it was ruled from, "the lords of X": the realms of the history are
/// mostly gone by the start date, and the seat is the one name the map still has for them.
/// </summary>
public static class HistoryChronicle
{
    /// <summary>How much bad blood a remembered thing leaves, as <see cref="ChronicleEvent.Tension"/> counts it.</summary>
    public static int TensionOf(AppliedHistory.Remembered r)
    {
        bool foreign = r.ActorCulture is not null && r.CounterpartCulture is not null && r.ActorCulture != r.CounterpartCulture;
        return r.What switch
        {
            "won" => foreign ? 3 : 2,
            "held" => foreign ? 2 : 1,
            "seized" or "freed" => 2,
            "divided" or "swore" or "collapsed" or "drifted" => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// The remembered history as chronicle events, on the titles of the world being written. One
    /// whose title this world lacks, or that would hang on wilderness, is left out.
    /// </summary>
    public static List<ChronicleEvent> Events(IEnumerable<AppliedHistory.Remembered> remembered, List<Title> empires,
        CultureMap cultures, WildernessMap wilderness)
    {
        var all = Titles.Flatten(Titles.Roots(empires)).ToList();
        var byKey = new Dictionary<string, Title>(StringComparer.Ordinal);
        foreach (var t in all) byKey.TryAdd(t.Key, t);
        var byIndex = all.Where(t => t.Tier == "c").GroupBy(t => t.Index).ToDictionary(g => g.Key, g => g.First());
        var cultureByKey = cultures.Cultures.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());

        var events = new List<ChronicleEvent>();
        foreach (var r in remembered)
        {
            if (!byKey.TryGetValue(r.Subject, out var subject)) continue;
            if (subject.Tier == "c" && wilderness.Contains(subject)) continue;
            if (Text(r, subject, byKey, byIndex, cultureByKey, wilderness) is not { } text) continue;

            events.Add(new ChronicleEvent
            {
                Kind = r.What switch
                {
                    "won" or "held" => ChronicleKind.War,
                    "drifted" => ChronicleKind.Frontier,
                    "settled" or "resettled" or "ruined" => ChronicleKind.Settlement,
                    _ => ChronicleKind.Seat,
                },
                Year = r.Year,
                Subject = subject,
                Counterpart = byIndex.GetValueOrDefault(r.Counterpart),
                Culture = r.ActorCulture is { } a ? cultureByKey.GetValueOrDefault(a) : null,
                CounterpartCulture = r.CounterpartCulture is { } b ? cultureByKey.GetValueOrDefault(b) : null,
                Tension = TensionOf(r),
                Text = Io.ParadoxText.Loc(text),
            });
        }
        return events;
    }

    private static string? Text(AppliedHistory.Remembered r, Title subject, Dictionary<string, Title> byKey,
        Dictionary<int, Title> byIndex, Dictionary<string, Culture> cultures, WildernessMap wilderness)
    {
        string Lords(int seat) => byIndex.TryGetValue(seat, out var c) ? $"the lords of {c.Name}" : "a realm long gone";
        string Named(Title t) => t.Tier switch
        {
            "d" => $"the duchy of {t.Name}",
            "k" => $"the kingdom of {t.Name}",
            "e" => $"the empire of {t.Name}",
            _ => t.Name,
        };
        string Counties(int[] indices)
        {
            var names = indices.Where(byIndex.ContainsKey).Select(i => byIndex[i].Name).ToList();
            return names.Count switch
            {
                0 => Named(subject),
                1 => names[0],
                <= 3 => string.Join(", ", names[..^1]) + " and " + names[^1],
                _ => $"{string.Join(", ", names.Take(2))} and {names.Count - 2} more",
            };
        }

        // The ground fought over: the whole title when all of it changed hands, else the counties,
        // and where they lie.
        string Ground()
        {
            if (r.Counties is not { Length: > 0 } taken || subject.Tier == "c") return Named(subject);
            int whole = subject.Children.Count(c => c.Tier == "c" && !wilderness.Contains(c));
            return taken.Length >= whole ? Named(subject) : $"{Counties(taken)} in {Named(subject)}";
        }

        string pronoun = r.Female ? "her" : "his";
        string? text = r.What switch
        {
            // A loser whose own seat was taken is still named for it: say so, or it reads as a slip.
            "won" => $"In {r.Year} {Lords(r.Actor)} took {Ground()} from {Lords(r.Counterpart)}"
                     + (r.Counties?.Contains(r.Counterpart) == true ? ", seat and all." : "."),
            "held" => $"In {r.Year} {Lords(r.Actor)} held {Ground()} against {Lords(r.Counterpart)}.",
            "divided" when r.Person is { } heir => $"When {r.Other ?? "the old ruler"} died in {r.Year}, {heir} took "
                                                  + $"{Named(subject)} as {pronoun} share of the realm.",
            "seized" when r.Person is { } who => $"In {r.Year}, after {r.Other ?? "the old ruler"} died, {who} seized the seat at {subject.Name}.",
            "chosen" when r.Person is { } who => $"In {r.Year}, after {r.Other ?? "the old ruler"} died, {who} was chosen to rule from {subject.Name}.",
            "drifted" when r.Into is { } into && byKey.TryGetValue(into, out var parent)
                => $"{Named(subject)} has answered to {Named(parent)} since {r.Year}.",
            "settled" => $"{Settlers(r.ActorCulture, cultures)} from {Lords(r.Actor)} cleared {subject.Name} in {r.Year}.",
            "resettled" => $"{Settlers(r.ActorCulture, cultures)} from {Lords(r.Actor)} rebuilt the ruins of {subject.Name} in {r.Year}.",
            "ruined" when r.Counties is { Length: > 0 } lost => $"{Counties(lost)} was abandoned in {r.Year} and left to ruin.",
            "swore" => $"In {r.Year} {Lords(r.Actor)} swore fealty to {Lords(r.Counterpart)}.",
            "freed" => $"In {r.Year} {Lords(r.Actor)} threw off the rule of {Lords(r.Counterpart)}.",
            "collapsed" => $"In {r.Year} the vassals of {Lords(r.Actor)} walked out, and the realm came apart.",
            _ => null,
        };
        return text is null ? null : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Settlers(string? culture, Dictionary<string, Culture> cultures)
        => culture is not null && cultures.TryGetValue(culture, out var c) ? $"{c.Name} settlers" : "Settlers";
}
