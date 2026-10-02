using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Land for spiritual Heads of Faith: a seat county at one of the faith's holy sites and a small
/// patrimony beside it, held by the head as an independent theocracy, as vanilla's Pope holds the
/// Papal States. Without it a generated head held only a landless duchy-tier title: measured on seed
/// 303 at 1150 (2026-10-01), ten heads sat on 31-276 gold for fifteen years and the ecclesiastical
/// ones on a treasury of 2-4, so none could ever pay for an Ecumenical Council (500), a bull or a
/// holy-site action. The user chose land over a tithe (2026-10-01).
///
/// Built on <see cref="PrinceBishops"/>' two safe routes, so no ruler is stripped of his only land:
/// a lone count (one county, no vassals) becomes the head himself, or a county is taken out of a
/// lord's demesne of three or more, never the lord's own seat. The patrimony only takes the second
/// way. Seats are scored (holy site, how inland, development; never a one-barony county). The head is
/// sworn to the same-faith king or emperor above the land, as vanilla's Ecumenical Patriarch answers to
/// the Basileus, and stands alone, as the Pope does, only where there is none (the user, 2026-10-02:
/// vassals by default, some independent heads for variety).
///
/// Keeping the seat matters more than usual: a landed head's title is not landless, so a head who
/// loses his last county loses the head title too. An independent head is never seated where a ruler
/// of another faith holds a de jure title above the seat (a de jure war he cannot win); rulers of his
/// own faith are kept off him at run time instead, by the AI war score in
/// BaseFilesToCopy/Core/common/script_values/zz_gen_head_of_faith_war_values.txt (2026-10-02).
///
/// Carved on the realm and government maps before the rulers are drawn (ContentWriter.BuildSees,
/// ahead of the prince-bishops, who need a feudal or clan lord and so pass these counties by), so
/// every writer after it sees an ordinary theocratic count; HistoryWriter gives that count the head
/// title (<see cref="RealmMap.HeadSeats"/>). Spiritual, generated heads only: a temporal head is
/// already a landed ruler, and an inherited (vanilla) head keeps vanilla's setup.
/// </summary>
public static class HeadSeats
{
    /// <summary>Counties beside the seat the head may also hold, the patrimony.</summary>
    public const int Patrimony = 2;


    /// <param name="preferred">Each faith's seat on a date already carved, which this one prefers;
    /// gains this date's.</param>
    /// <returns>Every county carved, seats and patrimony.</returns>
    public static List<Title> Carve(FaithMap faiths, RealmMap realms, GovernmentMap governments,
        WildernessMap wilderness, Dictionary<Title, int> development, Dictionary<Faith, Title> preferred,
        bool isStartDate = true)
    {
        var carved = new List<Title>();
        var seats = realms.HolderCounty.Values.ToHashSet();
        var demesne = realms.HolderCounty.Where(kv => kv.Key.Tier == "c")
            .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Count());
        var vassalLords = realms.Liege.Values
            .Select(t => realms.HolderCounty.GetValueOrDefault(t))
            .OfType<Title>()
            .ToHashSet();
        realms.HeadSeats.Clear();
        if (isStartDate)
            foreach (var f in faiths.Faiths)
                if (f.Head is { } h) { h.LandedSeat = null; h.LandedOnSomeDate = false; }

        foreach (var faith in faiths.Faiths)
        {
            if (faith is { Inherited: true } || faith.Head is not { Temporal: false, Inherited: false }) continue;

            // A lay-clergy faith allows theocracy only to cardinals, archbishops and LANDLESS clergy
            // (theocratic_lay_clergy_trigger), so a head seated on land there is turned secular by the
            // engine within the first year (feudal/clan/mandala on seed 303, 2026-10-02). Its head stays
            // a landless cleric, as before, which that rule allows; vanilla's lay-clergy faiths have no
            // landed theocrats either.
            if (faith.Religion.LayClergy) continue;

            // Another bookmark seats only a head landed on the start date. landed_titles is not dated:
            // a head landed on one date only had a non-landless title, which the engine destroyed under
            // the landless priest holding it on the start date (faith 38, seed 303, ~1161, never restored).
            if (!isStartDate && faith.Head.LandedSeat is null) continue;

            // Holy sites, then the faith's other counties. Every eligible one is scored rather than the
            // first taken: on seed 303 a first-eligible pick seated three of eight heads on border or
            // one-barony counties that neighbours took within fifteen years (2026-10-02).
            var holy = faith.HolySites.Select(h => h.County).ToHashSet();
            var candidates = holy.Concat(faiths.ByCounty.Where(kv => kv.Value == faith).Select(kv => kv.Key))
                .Distinct().ToList();

            Title? seat = null;
            double best = double.MinValue;
            int eligible = 0, refused = 0;
            foreach (var county in candidates)
            {
                if (county.Children.Count < 2 || !Settled(county, out var lord)) continue;
                bool lone = lord == county;
                if (lone ? demesne.GetValueOrDefault(county) != 1 || vassalLords.Contains(county)
                           || !ReferenceEquals(Emit.HistoryWriter.Primary(county, realms), county)
                         : demesne.GetValueOrDefault(lord) < 3) continue;
                eligible++;

                // Who could take the seat from the head. Over three thirty-year runs on seed 303
                // (2026-10-02, ck3run/loss_causes.py) all seven seats lost belonged to heads standing
                // alone, to de jure wars by an old duke and to conquest wars, and none under a
                // protector. Two answers: AI rulers of the head's own faith no longer make war on
                // their Head (zz_gen_head_of_faith_war_values.txt), and the seat is chosen here
                // against the rest: a ruler of ANOTHER faith holding a duchy, kingdom, empire or
                // hegemony the seat lies in de jure, who may take it by de_jure_cb (or its
                // individual county/duchy and nomadic forms). A head alone on one county loses that
                // war and the head title with it, so such a seat is refused on the start date.
                // Under a protector the war is the protector's to fight, so claimants only weigh.
                var protector = Protector(lord);
                var claims = Exposure(county, protector);
                bool doomed = protector is null && claims.OtherFaith > 0;
                if (doomed && isStartDate) { refused++; continue; }

                // Then a protector outweighs a holy site, and claimants weigh against both: another
                // faith's most, the same faith's outside the protector's realm less (they spare the
                // head but may still fight the protector over the seat), the protector's own
                // vassals least (crown authority may stop them, and they spare the head too).
                // Another date's seat must be found if at all possible: the head title is not
                // landless (the start-date head is landed), and a landless priest holding it would
                // see it destroyed at once (faith 38, seed 303), so an exposed seat is still better
                // than none there.
                double score = (doomed ? -1e5 : 0)
                    + (preferred.GetValueOrDefault(faith) == county ? 1e4 : 0)
                    + (protector is not null ? 200 : 0)
                    - 150 * claims.OtherFaith - 40 * claims.SameFaith - 20 * claims.Inside
                    + (holy.Contains(county) ? 100 : 0)
                    + 60 * Inland(county)
                    + development.GetValueOrDefault(county);
                if (score > best || (score == best && seat is not null && county.Index < seat.Index)) { best = score; seat = county; }
            }

            if (seat is not null)
            {
                var lord = realms.HolderCounty[seat];
                // Who protects the seat, decided before the carve changes the realm map: the overlord
                // of the land it comes from (as prince-bishops answer to theirs), king tier or above
                // because the head's own title is a duchy, and of the same faith.
                var protector = Protector(lord);
                var claims = Exposure(seat, protector);
                Console.WriteLine($"    head of {faith.Key}: {seat.Key} ({(lord == seat ? "lone count" : "from a demesne")}), "
                                  + $"{Inland(seat):P0} inland, "
                                  + (protector is null ? $"independent (no same-faith king or emperor above {Emit.HistoryWriter.Primary(lord, realms).Key})" : $"sworn to {protector.Key}")
                                  + $", de jure claimants: {claims.OtherFaith} of another faith, {claims.SameFaith} of this faith outside the protector's realm, {claims.Inside} inside it"
                                  + (protector is null && claims.OtherFaith > 0 ? " (EXPOSED: no safe seat on this date)" : ""));
                if (lord == seat)
                {
                    // A lone count becomes the head himself.
                    realms.Liege.Remove(seat);
                    realms.Origin.Remove(seat);
                }
                else
                {
                    // Out of a demesne of three or more; never the lord's own seat.
                    realms.HolderCounty[seat] = seat;
                    demesne[lord]--;
                    seats.Add(seat);
                }
                // Sworn to a protector where there is one, as vanilla's Ecumenical Patriarch answers to
                // the Basileus: a seat left independent inside its old realm still sits in that lord's
                // de jure duchy, and on seed 303 three of eight such heads lost it to a de jure claim
                // within fifteen years. Independent, as the Pope is, only with no protector above.
                if (protector is not null) realms.SetLiege(seat, protector, LiegeOrigin.Church);

                governments.Set(seat, GovernmentMap.Theocracy);
                carved.Add(seat);
            }

            if (seat is null)
            {
                Console.WriteLine($"    head of {faith.Key}: no seat, stays landless ("
                                  + (eligible == 0 ? $"none of {candidates.Count} counties is a lone count or out of a demesne of three"
                                                   : $"all {refused} eligible counties lie under another faith's de jure title, with no protector")
                                  + ")");
                continue;
            }
            realms.HeadSeats[faith] = seat;
            if (isStartDate) faith.Head.LandedSeat = seat;
            faith.Head.LandedOnSomeDate = true;
            preferred[faith] = seat;

            // The patrimony: neighbours of the faith, each out of a demesne of three or more.
            if (realms.CountyAdjacency?.GetValueOrDefault(seat) is not { } next) continue;
            int taken = 0;
            foreach (var county in next.OrderBy(c => c.Index))
            {
                if (taken == Patrimony) break;
                if (seats.Contains(county) || !Settled(county, out var lord) || lord == county) continue;
                if (demesne.GetValueOrDefault(lord) < 3) continue;

                realms.HolderCounty[county] = seat;
                demesne[lord]--;
                governments.Set(county, GovernmentMap.Theocracy);
                carved.Add(county);
                taken++;
            }

            // The share of a county's neighbours that follow this faith: how far inside its own world it sits.
            double Inland(Title county)
            {
                if (realms.CountyAdjacency?.GetValueOrDefault(county) is not { Count: > 0 } around) return 0;
                return around.Count(c => faiths.For(c) == faith) / (double)around.Count;
            }

            // The overlord above a lord, king tier or above and of this faith; null when there is none.
            Title? Protector(Title lord)
            {
                var current = Emit.HistoryWriter.Primary(lord, realms);
                var seen = new HashSet<Title>();
                while (seen.Add(current) && realms.Liege.TryGetValue(current, out var up) && up.Tier != "h") current = up;
                return Title.TierRank(current.Tier) >= Title.TierRank("k")
                       && current.Key is not (WildernessMap.TitleKey or WildernessMap.RuinsTitleKey)
                       && realms.HolderCounty.TryGetValue(current, out var protectorSeat)
                       && faiths.For(protectorSeat) == faith
                    ? current : null;
            }

            // The rulers who would hold a de jure claim on the county once the head holds it: the
            // holders of every duchy, kingdom, empire and hegemony above it, less the protector and
            // anyone the protector answers to, each counted once. Split three ways: rulers of
            // another faith (inside or outside the protector's realm: the head's own faith's AI
            // spares him, theirs does not), rulers of this faith outside the protector's realm, and
            // this faith's vassals of the protector. Wilderness and ruins dummies make no wars.
            (int OtherFaith, int SameFaith, int Inside) Exposure(Title county, Title? protector)
            {
                var above = new HashSet<Title>();
                for (var t = protector; t is not null && above.Add(t); t = realms.Liege.GetValueOrDefault(t)) { }
                var counted = new HashSet<Title>();
                int other = 0, same = 0, inside = 0;
                for (var a = county.Parent; a is not null; a = a.Parent)
                {
                    if (Title.TierRank(a.Tier) < Title.TierRank("d")) continue;
                    if (!realms.HolderCounty.TryGetValue(a, out var holderSeat) || holderSeat == county) continue;
                    var top = Emit.HistoryWriter.Primary(holderSeat, realms);
                    if (top.Key is WildernessMap.TitleKey or WildernessMap.RuinsTitleKey || wilderness.Contains(holderSeat)) continue;
                    if (above.Contains(top) || !counted.Add(holderSeat)) continue;
                    if (faiths.For(holderSeat) != faith) { other++; continue; }
                    bool under = false;
                    var seen = new HashSet<Title>();
                    for (var t = top; t is not null && !under && seen.Add(t); t = realms.Liege.GetValueOrDefault(t))
                        under = above.Contains(t);
                    if (under) inside++; else same++;
                }
                return (other, same, inside);
            }

            // Settled land of this faith held by a feudal or clan lord: what a head may be seated on.
            bool Settled(Title county, out Title lord)
            {
                lord = null!;
                if (wilderness.Contains(county) || faiths.For(county) != faith) return false;
                if (!realms.HolderCounty.TryGetValue(county, out var l) || wilderness.Contains(l)) return false;
                lord = l;
                return GovernmentMap.Family(governments.For(l)) is GovernmentMap.Feudal or GovernmentMap.Clan;
            }
        }

        return carved;
    }
}
