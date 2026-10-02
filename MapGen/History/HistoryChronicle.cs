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
            "won" or "fell" => foreign ? 3 : 2,
            "held" => foreign ? 2 : 1,
            "feud" => 3,
            "seized" or "freed" or "brokeaway" or "rivals" => 2,
            "divided" or "swore" or "collapsed" or "drifted" or "fallen" => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// How much a remembered thing belongs among the world's headlines: what kind of thing it was,
    /// weighed by how big the powers in it were (<see cref="AppliedHistory.Remembered.Scale"/>, 0 in
    /// a file saved before it was measured, which leaves the kind alone to decide). The end of a
    /// realm outranks a war, a war outranks a settlement, and a war between empires outranks one
    /// between counts. A drift is weighed by what it moved instead: a kingdom changing empire is
    /// news, a duchy changing kingdom less so.
    /// </summary>
    public static double Notability(AppliedHistory.Remembered r)
    {
        double kind = r.What switch
        {
            "fell" or "collapsed" or "greatest" => 5,
            "fallen" or "feud" => 4,
            "seized" => 3.5,
            "won" or "freed" => 3,
            "brokeaway" => 2.5,
            "swore" or "divided" or "rivals" => 2,
            // A duchy turning is news; one county, much less so.
            "assimilated" or "converted" => r.Subject.StartsWith("c_") ? 0.75 : 1.5,
            "held" => 1.5,
            "chosen" or "settled" or "resettled" or "ruined" => 1,
            "drifted" => r.Into?.StartsWith("e_") == true ? 4 : 2.5,
            _ => 0,
        };
        if (kind == 0 || r.What == "drifted") return kind;

        // Land taken counts for something of its own, and a seat taken with it more.
        if (r.What == "won")
            kind += Math.Min(2, 0.25 * (r.Counties?.Length ?? 0)) + (r.Counties?.Contains(r.Counterpart) == true ? 1 : 0);

        return kind * (1 + Math.Log2(1 + r.Scale) / 2);
    }

    /// <summary>
    /// The <paramref name="max"/> most notable remembered things, oldest first, as the world's
    /// headlines. See <see cref="ChronicleMap.PickHeadlines{T}"/> for how a list of the most notable
    /// is kept from being one kind of thing over and over; here, besides, a war that ended a realm
    /// is told once, as the fall, and not again as the war won — and the vassals a collapse or a
    /// fall set loose are that collapse or fall, not a string of risings of their own.
    /// </summary>
    public static List<AppliedHistory.Remembered> Headlines(IEnumerable<AppliedHistory.Remembered> remembered, int max)
    {
        var all = remembered.ToList();
        var fell = all.Where(r => r.What == "fell").Select(r => (r.Year, r.Actor, r.Counterpart)).ToHashSet();
        var ended = all.Where(r => r.What is "fell" or "collapsed").Select(r => (r.Year, r.Actor)).ToHashSet();
        var pool = all.Where(r => !(r.What == "won" && fell.Contains((r.Year, r.Counterpart, r.Actor)))
                               && !(r.What == "freed" && ended.Contains((r.Year, r.Counterpart))));

        return ChronicleMap.PickHeadlines(pool, Notability, r => r.What, r => r.Subject, r => r.Year,
            r => $"{r.What}|{r.Subject}|{r.Actor}|{r.Counterpart}|{r.Person}", max);
    }

    /// <summary>
    /// The remembered history as chronicle events, on the titles of the world being written. One
    /// whose title this world lacks, or that would hang on wilderness, is left out.
    /// </summary>
    public static List<ChronicleEvent> Events(IEnumerable<AppliedHistory.Remembered> remembered, List<Title> empires,
        CultureMap cultures, WildernessMap wilderness, FaithMap? faiths = null)
    {
        var all = Titles.Flatten(Titles.Roots(empires)).ToList();
        var byKey = new Dictionary<string, Title>(StringComparer.Ordinal);
        foreach (var t in all) byKey.TryAdd(t.Key, t);
        var byIndex = all.Where(t => t.Tier == "c").GroupBy(t => t.Index).ToDictionary(g => g.Key, g => g.First());
        var cultureByKey = cultures.Cultures.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First());
        var faithByKey = faiths?.Faiths.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => ChronicleMap.FaithLink(g.First())) ?? [];

        var events = new List<ChronicleEvent>();
        foreach (var r in remembered)
        {
            if (!byKey.TryGetValue(r.Subject, out var subject)) continue;
            if (subject.Tier == "c" && wilderness.Contains(subject)) continue;
            if (Text(r, subject, byKey, byIndex, cultureByKey, wilderness, faithByKey) is not { } text) continue;

            events.Add(new ChronicleEvent
            {
                Kind = r.What switch
                {
                    "won" or "held" or "fell" => ChronicleKind.War,
                    "feud" or "rivals" => ChronicleKind.Feud,
                    "drifted" => ChronicleKind.Frontier,
                    "settled" or "resettled" or "ruined" or "assimilated" => ChronicleKind.Settlement,
                    "converted" => ChronicleKind.Faith,
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
        Dictionary<int, Title> byIndex, Dictionary<string, Culture> cultures, WildernessMap wilderness,
        Dictionary<string, string> faiths)
    {
        string Lords(int seat) => byIndex.TryGetValue(seat, out var c) ? $"the lords of {ChronicleMap.TitleLink(c)}" : "a realm long gone";
        string Named(Title t) => t.Tier switch
        {
            "d" => $"the duchy of {ChronicleMap.TitleLink(t)}",
            "k" => $"the kingdom of {ChronicleMap.TitleLink(t)}",
            "e" => $"the empire of {ChronicleMap.TitleLink(t)}",
            _ => ChronicleMap.TitleLink(t),
        };
        string Counties(int[] indices)
        {
            var names = indices.Where(byIndex.ContainsKey).Select(i => ChronicleMap.TitleLink(byIndex[i])).ToList();
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

        string PeopleOf(Title t) => t.Tier == "c" ? $"the people of {ChronicleMap.TitleLink(t)}" : $"most of {Named(t)}";

        string pronoun = r.Female ? "her" : "his";
        string? text = r.What switch
        {
            // A loser whose own seat was taken is still named for it: say so, or it reads as a slip.
            "won" => $"In {r.Year} {Lords(r.Actor)} took {Ground()} from {Lords(r.Counterpart)}"
                     + (r.Counties?.Contains(r.Counterpart) == true ? ", seat and all." : "."),
            "held" => $"In {r.Year} {Lords(r.Actor)} held {Ground()} against {Lords(r.Counterpart)}.",
            "divided" when r.Person is { } heir => $"When {r.Other ?? "the old ruler"} died in {r.Year}, {heir} took "
                                                  + $"{Named(subject)} as {pronoun} share of the realm.",
            "seized" when r.Person is { } who => $"In {r.Year}, after {r.Other ?? "the old ruler"} died, {who} seized the seat at {ChronicleMap.TitleLink(subject)}.",
            "chosen" when r.Person is { } who => $"In {r.Year}, after {r.Other ?? "the old ruler"} died, {who} was chosen to rule from {ChronicleMap.TitleLink(subject)}.",
            "drifted" when r.Into is { } into && byKey.TryGetValue(into, out var parent)
                => $"{Named(subject)} has answered to {Named(parent)} since {r.Year}.",
            "settled" => $"{Settlers(r.ActorCulture, cultures)} from {Lords(r.Actor)} cleared {ChronicleMap.TitleLink(subject)} in {r.Year}.",
            "resettled" => $"{Settlers(r.ActorCulture, cultures)} from {Lords(r.Actor)} rebuilt the ruins of {ChronicleMap.TitleLink(subject)} in {r.Year}.",
            "ruined" when r.Counties is { Length: > 0 } lost => $"{Counties(lost)} was abandoned in {r.Year} and left to ruin.",
            "assimilated" when r.Other is { } key && cultures.TryGetValue(key, out var people)
                => $"By {r.Year} {PeopleOf(subject)} had taken up {ChronicleMap.CultureLink(people)} ways under {Lords(r.Actor)}.",
            "converted" when r.Other is { } key && faiths.TryGetValue(key, out var faith)
                => $"By {r.Year} {PeopleOf(subject)} had turned to {faith} under {Lords(r.Actor)}.",
            "swore" => $"In {r.Year} {Lords(r.Actor)} swore fealty to {Lords(r.Counterpart)}.",
            "freed" => $"In {r.Year} {Lords(r.Actor)} threw off the rule of {Lords(r.Counterpart)}.",
            "collapsed" => $"In {r.Year} the vassals of {Lords(r.Actor)} walked out, and the realm came apart.",
            "brokeaway" => $"In {r.Year} {Lords(r.Actor)} broke away from {Lords(r.Counterpart)}, which had grown too wide to hold them.",
            "fell" => $"In {r.Year} {Lords(r.Counterpart)} took the last lands of {Lords(r.Actor)}, and their realm was no more.",
            "feud" when r.Person is { } a && r.Other is { } b => $"In {r.Year} the houses of {a} and {b} fell into open feud over {ChronicleMap.TitleLink(subject)}.",
            "rivals" when r.Person is { } a && r.Other is { } b => $"In {r.Year} the houses of {a} and {b} became rivals over {ChronicleMap.TitleLink(subject)}.",
            "greatest" when r.Person is { } house => $"By {r.Year} the house of {house}, ruling from {ChronicleMap.TitleLink(subject)}, was the greatest in the world.",
            "fallen" when r.Person is { } house => $"In {r.Year} the house of {house}, once among the greatest, ruled nowhere any more. "
                                                  + $"Its last seat had been {ChronicleMap.TitleLink(subject)}.",
            _ => null,
        };
        return text is null ? null : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Settlers(string? culture, Dictionary<string, Culture> cultures)
        => culture is not null && cultures.TryGetValue(culture, out var c) ? $"{ChronicleMap.CultureLink(c)} settlers" : "Settlers";
}
