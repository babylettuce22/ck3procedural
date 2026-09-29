using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One person the restoration needs and the world did not already have: a king of the fallen
/// crown, the heir who never reigned, the living pretender. Written by
/// <see cref="Emit.RestorationWriter"/> into a character file of its own.
/// </summary>
public sealed record RestorationPerson(
    string Id, string Name, bool Female, int Born, int? Died, string? FatherId,
    string? MotherId, bool Landless);

/// <summary>
/// A member sworn at the start date, and how far in. <see cref="Rank"/> is trait XP on the society's
/// rank track (0-100), not a rung number.
/// </summary>
public sealed record RestorationMember(string Id, int Rank);

/// <summary>
/// Everything a world's Restorationist society is made of: the crown that fell, when and how, the
/// house it fell from, who stands to inherit, and who is already sworn to put them back.
///
/// Derived, never invented where the world already says something. The crown is a de jure kingdom
/// nobody holds at the start date. When the formation's frames show one realm holding most of it
/// before it broke, that realm is the fallen dynasty: its fall year is the frame it stopped holding
/// the land, sharpened by the event log, and its old capital is the seat the society means to take
/// back. A realm that survived as a rump gives the society a landed pretender of its own house; one
/// that did not leaves its line to be written here, the last kings and all.
/// </summary>
public sealed class RestorationPlan
{
    public required Title Crown { get; init; }

    /// <summary>
    /// The crown is worn by an usurping house at the start (it must be taken back by war), rather than
    /// unheld (it must be re-made). An usurped crown's title history is HistoryWriter's, so the kings
    /// are written as people only.
    /// </summary>
    public required bool CrownHeld { get; init; }

    /// <summary>The county the crown was held from. What the society means by "the old seat".</summary>
    public required Title OldSeat { get; init; }

    public required int FellYear { get; init; }

    /// <summary>How it fell: "absorbed", "collapsed", "fragmented", or "unknown" when the world kept no record.</summary>
    public required string FallKind { get; init; }

    /// <summary>True when the formation actually recorded one realm holding this crown's land.</summary>
    public required bool FromHistory { get; init; }

    public required Culture Culture { get; init; }
    public required Faith Faith { get; init; }

    public required string DynastyId { get; init; }
    public required string HouseKey { get; init; }

    /// <summary>The house's name loc key, for <c>$key$</c> substitution in the society's prose.</summary>
    public required string HouseNameKey { get; init; }

    /// <summary>Set when this plan created the house; null when it belongs to a ruling house already written.</summary>
    public string? MintedHouseName { get; init; }
    public string? MintedPrefix { get; init; }

    public required string PretenderId { get; init; }
    public required bool PretenderLanded { get; init; }

    /// <summary>The ruler whose court shelters a landless pretender at the start, or null.</summary>
    public string? HostId { get; init; }

    /// <summary>The ruler who holds the most of the crown's land at the start. The society's enemy.</summary>
    public string? UsurperId { get; init; }

    /// <summary>People written for this plan alone, eldest first so every parent precedes its child.</summary>
    public List<RestorationPerson> Minted { get; } = [];

    /// <summary>The crown's past holders, oldest first: (character id, year they took it).</summary>
    public List<(string Id, int From)> Kings { get; } = [];

    public List<RestorationMember> Members { get; } = [];

    /// <summary>The member who runs the society day to day. Always also in <see cref="Members"/>.</summary>
    public required string KeeperId { get; init; }

    /// <summary>Which of the naming patterns the society's name uses. See RestorationWriter.</summary>
    public required int NamePattern { get; init; }

    /// <summary>Settled counties of the crown, for the run log.</summary>
    public required int CountyCount { get; init; }
}

public static class Restoration
{
    /// <summary>Below this many settled counties a kingdom is too small to have been worth mourning.</summary>
    private const int MinCounties = 4;

    /// <summary>
    /// A realm held a crown "as a unit" when it held at least this share of its settled land. Half:
    /// at 0.6, two of three seeds found no kingdom any realm had ever held that fully, and the story
    /// only needs "most of it was theirs".
    /// </summary>
    private const double HeldShare = 0.5;

    /// <summary>
    /// Picks the fallen crown and builds the society around it, or returns null when the world has no
    /// kingdom that fits — every kingdom held, or none large enough. Reads the world and adds nothing
    /// to it: minted people and houses live only on the returned plan.
    /// </summary>
    public static RestorationPlan? Build(
        List<Title> empires, RealmMap realms, CultureMap cultures, FaithMap faiths,
        WildernessMap wilderness, RulerMap rulers, PrehistoryMap prehistory, MapConfig cfg)
    {
        var rng = Rng.For(cfg.Seed, 0x5E57, 1, cfg.PeopleSalt);

        // Seat -> its independent overlord's seat, once, rather than walking lieges per county.
        var primaryOf = new Dictionary<Title, Title>();
        foreach (var (title, seat) in realms.HolderCounty)
            if (!primaryOf.TryGetValue(seat, out var best) || Title.TierRank(title.Tier) > Title.TierRank(best.Tier))
                primaryOf[seat] = title;

        Title TopSeat(Title seat)
        {
            var current = primaryOf.GetValueOrDefault(seat) ?? seat;
            for (int guard = 0; guard < 16 && realms.Liege.TryGetValue(current, out var lord); guard++) current = lord;
            return realms.HolderCounty.GetValueOrDefault(current) ?? seat;
        }

        // Two kinds of fallen crown. BROKEN: nobody holds the kingdom at the start, and the formation
        // shows a realm that once held most of it. USURPED: somebody holds it, and the formation
        // shows a DIFFERENT realm held it before them. A broken crown must be re-made; an usurped one
        // must be taken. The first is preferred, but on most worlds the crowns that really fell were
        // taken whole by a neighbour, and the kingdoms nobody holds are the ones nobody ever united.
        var candidates = new List<(Title Crown, List<Title> Land, Evidence? Ev, bool Held, double Score)>();
        foreach (var kingdom in Titles.Flatten(empires).Where(t => t.Tier == "k"))
        {
            bool held = realms.HolderCounty.TryGetValue(kingdom, out var crownSeat);

            var land = Titles.Flatten([kingdom])
                .Where(t => t.Tier == "c" && !wilderness.Contains(t) && realms.HolderCounty.ContainsKey(t))
                .OrderBy(t => t.Index)
                .ToList();
            if (land.Count < MinCounties) continue;

            // The realm holding the crown now, as the formation knows it — a past holder must differ.
            int? nowRoot = held ? realms.History?.Owner.GetValueOrDefault(crownSeat!)?.Root.Id : null;
            var ev = Evidence.Find(realms.History, land, cfg.StartYear, held, nowRoot);

            // A held kingdom with no record of anyone else holding it is just a kingdom.
            if (held && ev is null) continue;

            // Recorded history first, then a fall the living remember — a generation or two back,
            // not so recent it is still a war and not so old it is only a song — then size.
            double score = land.Count / 5.0 + rng.NextDouble();
            if (!held) score += 3;
            if (ev is not null)
            {
                score += 10;
                int ago = cfg.StartYear - ev.FellYear;
                if (ago is >= 20 and <= 160) score += 5;
            }

            candidates.Add((kingdom, land, ev, held, score));
        }

        if (candidates.Count == 0) return null;
        var pick = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Crown.Index).First();

        var crown = pick.Crown;
        var landSet = pick.Land.ToHashSet();
        var ev0 = pick.Ev;

        // For the run log: when no fall was recorded, how close the formation ever came to a realm
        // holding this crown, so a tuning question can be answered without a debugger.
        if (ev0 is null)
            Console.WriteLine($"  restoration society: no recorded fall for {crown.Key}; "
                + $"{realms.History?.Frames.Count ?? 0} frames, best past share {Evidence.BestShare(realms.History, pick.Land):P0}; "
                + $"{candidates.Count(c => c.Ev is not null)} of {candidates.Count} candidate kingdoms have one");

        // The society's enemy: the crown's holder if it has one; else whoever holds the most of the
        // crown's land, crowned or not.
        var usurperSeat = pick.Held ? realms.HolderCounty[crown]
            : pick.Land
                .GroupBy(c => TopSeat(realms.HolderCounty[c]))
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Index)
                .First().Key;
        rulers.TryGet(usurperSeat, out var usurper);

        var oldSeat = ev0?.Seat is { } s && landSet.Contains(s) ? s
            : crown.Capital is { } cap && Titles.Flatten([cap]).FirstOrDefault(t => t.Tier == "c") is { } capCounty
              && landSet.Contains(capCounty) ? capCounty
            : pick.Land[0];

        var culture = ev0?.Culture ?? cultures.For(oldSeat);
        var faith = faiths.For(oldSeat);

        int fellYear = ev0?.FellYear ?? cfg.StartYear - rng.Int(40, 120);
        string fallKind = ev0?.Kind ?? "unknown";

        // ---- The rightful house ----------------------------------------------------------------
        //
        // A rump of the fallen realm still standing is the best answer there is: its ruler IS the
        // old dynasty, reduced. Failing that, whoever sits in the old seat — the house that kept the
        // throne room when the kingdom around it went — provided they are not the usurper themself.
        Ruler? heir = null;
        if (ev0 is not null && realms.History is { } hist)
        {
            var rump = hist.Polities.FirstOrDefault(p => p.Id == ev0.PolityId && p.Alive);
            if (rump is not null && rulers.TryGet(rump.Capital, out var r) && Fits(r)) heir = r;
        }
        if (heir is null && rulers.TryGet(oldSeat, out var seated) && Fits(seated)
            && seated.Culture.Key == culture.Key)
            heir = seated;

        bool Fits(Ruler r) => r.HouseKey.Length > 0 && r.Id != usurper?.Id
            && r.HouseKey != usurper?.HouseKey
            && TopSeat(r.Seat) != TopSeat(usurperSeat);

        string dynastyId, houseKey, houseNameKey;
        string? mintedName = null, mintedPrefix = null;
        string pretenderId;
        bool landed;

        var minted = new List<RestorationPerson>();
        var kings = new List<(string, int)>();
        var mrng = Rng.For(cfg.Seed, 0x5E57, 2, cfg.PeopleSalt);

        int reignLen() => mrng.Int(12, 30);

        if (heir is not null)
        {
            dynastyId = heir.DynastyId;
            houseKey = heir.HouseKey;
            houseNameKey = prehistory.Houses.TryGetValue(houseKey, out var h) ? h.NameKey : $"dynn_{dynastyId}";
            pretenderId = heir.Id;
            landed = true;

            // The last king was of this house, and died with the crown. Written as a dead member
            // of it so the title history has somebody to name.
            int kBorn = fellYear - mrng.Int(35, 55);
            var last = new RestorationPerson("gen_restor_king_1",
                GivenName(culture, false, mrng), false, kBorn, fellYear, null, null, false);
            int kFrom = fellYear - reignLen();
            var before = new RestorationPerson("gen_restor_king_0",
                GivenName(culture, mrng.Chance(0.15), mrng), false, kBorn - mrng.Int(22, 34), kFrom, null, null, false);
            last = last with { FatherId = before.Female ? null : before.Id, MotherId = before.Female ? before.Id : null };
            minted.Add(before);
            minted.Add(last);
            kings.Add((before.Id, kFrom - reignLen()));
            kings.Add((last.Id, kFrom));
        }
        else
        {
            // No house survived to claim it, so the world never wrote one. Write it here: two kings,
            // the heir the fall disinherited, and that heir's child, who is the pretender now.
            mintedName = FreshHouseName(culture, prehistory, mrng);
            mintedPrefix = PrehistoryMap.CulturePrefix(culture.Key);
            dynastyId = "gen_dynasty_restor";
            houseKey = "house_gen_restor";
            houseNameKey = "dynn_gen_restor";

            int lastBorn = fellYear - mrng.Int(35, 55);
            int lastFrom = fellYear - reignLen();
            var first = new RestorationPerson("gen_restor_king_0",
                GivenName(culture, false, mrng), false, lastBorn - mrng.Int(22, 34), lastFrom, null, null, false);
            var last = new RestorationPerson("gen_restor_king_1",
                GivenName(culture, false, mrng), false, lastBorn, fellYear, first.Id, null, false);

            minted.AddRange([first, last]);

            // The line in exile: as many generations as the years since the fall need, each born to
            // the last and dead before the start date, down to a parent young enough to have the
            // pretender. A crown lost a century and a half ago is five or six fathers back.
            var parent = last;
            int n = 0;
            while (parent.Born + 30 < cfg.StartYear - 40)
            {
                int born = parent.Born + mrng.Int(22, 32);
                bool nextIsParent = born + 30 < cfg.StartYear - 40;
                // Dead before the start, and never before their own child is born.
                int childBy = nextIsParent ? born + 32 : cfg.StartYear - 17;
                int died = Math.Clamp(born + mrng.Int(42, 62), Math.Max(childBy + 1, fellYear + 1), cfg.StartYear - 1);
                var exile = new RestorationPerson($"gen_restor_exile_{n++}",
                    GivenName(culture, false, mrng), false, born, died, parent.Id, null, false);
                minted.Add(exile);
                parent = exile;
            }

            // The pretender: an adult, young enough that the cause has years in them, and born
            // while the last exile lived.
            int pLo = Math.Max(parent.Born + 18, cfg.StartYear - 40);
            int pHi = Math.Min(Math.Min(parent.Born + 45, (parent.Died ?? cfg.StartYear) - 1), cfg.StartYear - 17);
            int pBorn = pHi >= pLo ? mrng.Int(pLo, pHi) : Math.Min(pLo, cfg.StartYear - 17);
            bool pFemale = mrng.Chance(0.2);
            var pretender = new RestorationPerson("gen_restor_pretender",
                GivenName(culture, pFemale, mrng), pFemale, pBorn, null, parent.Id, null, true);

            minted.Add(pretender);

            // A sibling more often than not: the cause survives the pretender's death with one.
            if (mrng.Chance(0.6))
            {
                int sBorn = Math.Clamp(pBorn + mrng.Int(-6, 8), cfg.StartYear - 42, cfg.StartYear - 8);
                bool sFemale = mrng.Chance(0.5);
                sBorn = Math.Min(sBorn, (parent.Died ?? cfg.StartYear) - 1);
                minted.Add(new RestorationPerson("gen_restor_sibling",
                    GivenName(culture, sFemale, mrng), sFemale, sBorn, null, parent.Id, null, true));
            }

            kings.Add((first.Id, lastFrom - reignLen()));
            kings.Add((last.Id, lastFrom));
            pretenderId = pretender.Id;
            landed = false;
        }

        // ---- The sworn ---------------------------------------------------------------------------
        //
        // Lords on the crown's old land come first, and of its people more than strangers. A vassal
        // of the usurper is exactly the kind of man a conspiracy is made of, so he is not excluded —
        // only the usurper himself and the pretender, who is counted separately.
        var scored = new List<(Ruler R, double Score)>();
        foreach (var r in rulers.All)
        {
            if (r.Id == pretenderId || r.Id == usurper?.Id) continue;
            if (usurper is not null && r.HouseKey == usurper.HouseKey) continue;
            double sc = 0;
            if (landSet.Contains(r.Seat)) sc += 3;
            if (r.Culture.Key == culture.Key) sc += 2;
            if (r.HouseKey == houseKey) sc += 2;
            if (sc < 2) continue;
            if (TopSeat(r.Seat) == TopSeat(usurperSeat) && r.Seat != usurperSeat) sc += 0.5;
            sc += rng.NextDouble() * 1.5;
            scored.Add((r, sc));
        }

        int wanted = Math.Clamp(pick.Land.Count / 3, 4, 8);
        var sworn = scored.OrderByDescending(x => x.Score).ThenBy(x => x.R.Seat.Index).Take(wanted).ToList();

        var members = new List<RestorationMember>();
        string keeperId = pretenderId;
        for (int i = 0; i < sworn.Count; i++)
        {
            // The first is the Keeper; the rest spread across the lower rungs.
            int rank = i == 0 ? 80 : rng.Int(5, 60);
            members.Add(new RestorationMember(sworn[i].R.Id, rank));
            if (i == 0) keeperId = sworn[i].R.Id;
        }
        // (The pretender is not listed: restor_make_pretender_effect swears them in.)

        // A landless pretender needs a roof -- and a host who can give them land, because a seat is
        // the first thing the cause needs of them. Grant the Heir a Seat asks for Trusted standing and
        // two counties held directly (it will not give away the host's capital), so the host is the
        // best-placed sworn ruler outside the usurper's realm who holds two, raised to Trusted if
        // below it. Else anyone outside the usurper's realm, else the Keeper, else nobody.
        int CountiesHeld(Title seat) => realms.HolderCounty.Count(kv => kv.Value == seat && kv.Key.Tier == "c");
        string? hostId = null;
        if (!landed)
        {
            var outside = sworn.Where(x => TopSeat(x.R.Seat) != TopSeat(usurperSeat)).ToList();
            var host = outside.FirstOrDefault(x => CountiesHeld(x.R.Seat) >= 2).R
                       ?? outside.FirstOrDefault().R;
            hostId = host?.Id ?? (keeperId != pretenderId ? keeperId : null);

            int at = members.FindIndex(m => m.Id == hostId);
            if (at >= 0 && members[at].Rank < 30) members[at] = members[at] with { Rank = 30 };

            if (minted.FirstOrDefault(p => p.Id == "gen_restor_sibling") is { } sib)
                members.Add(new RestorationMember(sib.Id, rng.Int(10, 35)));
        }

        var plan = new RestorationPlan
        {
            Crown = crown,
            CrownHeld = pick.Held,
            OldSeat = oldSeat,
            FellYear = fellYear,
            FallKind = fallKind,
            FromHistory = ev0 is not null,
            Culture = culture,
            Faith = faith,
            DynastyId = dynastyId,
            HouseKey = houseKey,
            HouseNameKey = houseNameKey,
            MintedHouseName = mintedName,
            MintedPrefix = mintedPrefix,
            PretenderId = pretenderId,
            PretenderLanded = landed,
            HostId = hostId,
            UsurperId = usurper?.Id,
            KeeperId = keeperId,
            NamePattern = rng.Int(0, 4),
            CountyCount = pick.Land.Count,
        };
        plan.Minted.AddRange(minted);
        plan.Kings.AddRange(kings);
        plan.Members.AddRange(members);
        return plan;
    }

    private static string GivenName(Culture culture, bool female, Rng rng)
    {
        var names = female ? culture.FemaleNames : culture.MaleNames;
        return names.Count > 0 ? names[rng.Int(0, names.Count - 1)] : female ? "Nullberta" : "Nullbert";
    }

    /// <summary>A dynasty name from the culture's list that no house on the map already carries.</summary>
    private static string FreshHouseName(Culture culture, PrehistoryMap prehistory, Rng rng)
    {
        var taken = prehistory.Houses.Values.Select(h => h.LocalizedName)
            .Concat(prehistory.Dynasties.Values.Select(d => d.LocalizedName))
            .ToHashSet(StringComparer.Ordinal);
        var free = culture.DynastyNames.Where(n => !taken.Contains(n)).ToList();
        if (free.Count > 0) return free[rng.Int(0, free.Count - 1)];
        return culture.DynastyNames.Count > 0 ? culture.DynastyNames[rng.Int(0, culture.DynastyNames.Count - 1)] : culture.Name;
    }

    /// <summary>
    /// What the formation remembers of one realm holding a crown's land and losing it: which polity,
    /// from where, of what people, and when and how it stopped.
    /// </summary>
    private sealed record Evidence(int PolityId, Title Seat, Culture Culture, int FellYear, string Kind)
    {
        /// <summary>The largest share of this land any one independent realm held in any frame.</summary>
        public static double BestShare(FormationHistory? history, List<Title> land)
        {
            if (history is null) return 0;
            var landSet = land.ToHashSet();
            double best = 0;
            foreach (var frame in history.Frames.Values)
            {
                var byRoot = new Dictionary<Polity, int>();
                foreach (var p in frame.Polities)
                {
                    int n = p.Counties.Count(landSet.Contains);
                    if (n > 0) byRoot[p.Root] = byRoot.GetValueOrDefault(p.Root) + n;
                }
                if (byRoot.Count > 0) best = Math.Max(best, byRoot.Values.Max() / (double)land.Count);
            }
            return best;
        }

        /// <param name="crownHeld">The kingdom is held at the start; look for a DIFFERENT earlier holder.</param>
        /// <param name="nowRootId">The formation's id for the realm holding it now, when held.</param>
        public static Evidence? Find(FormationHistory? history, List<Title> land, int startYear,
            bool crownHeld = false, int? nowRootId = null)
        {
            if (history is null || history.Frames.Count < 2) return null;
            var landSet = land.ToHashSet();

            // Per frame: the independent realm that held the most of this land, and how much.
            (int Year, Polity? Root, double Share) Best(int year, FormationHistory frame)
            {
                var byRoot = new Dictionary<Polity, int>();
                foreach (var p in frame.Polities)
                {
                    int n = p.Counties.Count(landSet.Contains);
                    if (n == 0) continue;
                    var root = p.Root;
                    byRoot[root] = byRoot.GetValueOrDefault(root) + n;
                }
                if (byRoot.Count == 0) return (year, null, 0);
                var top = byRoot.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Id).First();
                return (year, top.Key, top.Value / (double)land.Count);
            }

            var frames = history.Frames.Select(kv => Best(kv.Key, kv.Value)).ToList();

            // A broken crown must be broken at the end: nobody holding it as a unit in the last frame.
            if (!crownHeld && frames[^1].Share >= HeldShare) return null;

            // The most recent frame in which one realm held it — for an usurped crown, a realm other
            // than today's holder — and the frame after, when that realm no longer did.
            for (int i = frames.Count - 2; i >= 0; i--)
            {
                if (frames[i].Share < HeldShare || frames[i].Root is not { } held) continue;
                if (crownHeld && held.Id == nowRootId) continue;
                if (frames[i + 1].Root?.Id == held.Id && frames[i + 1].Share >= HeldShare) continue;
                int heldYear = frames[i].Year, brokenYear = frames[i + 1].Year;

                // The seat as it stood while it held the land — capitals move, so read it off the frame.
                var seat = held.Capital;

                // The log names the moment when it has one: the realm's own collapse, its absorption,
                // or a block breaking away, in the window between the two frames.
                // Capitals move inside a realm, so any of its counties in that frame counts as it.
                var frame = history.Frames[heldYear];
                var realm = frame.Polities.Where(p => p.Root == held).SelectMany(p => p.Counties).ToHashSet();
                var hit = history.Events
                    .Where(e => e.Year > heldYear && e.Year <= brokenYear && e.Actor is { } a && realm.Contains(a)
                        && e.Kind is FormationKind.Absorbed or FormationKind.Collapsed or FormationKind.Fragmented)
                    // A collapse or a conquest is the fall; a block seceding is only its symptom.
                    .OrderBy(e => e.Kind == FormationKind.Fragmented ? 1 : 0)
                    .ThenBy(e => e.Year)
                    .FirstOrDefault();

                int year = hit?.Year ?? (heldYear + brokenYear) / 2;
                string kind = hit?.Kind switch
                {
                    FormationKind.Absorbed => "absorbed",
                    FormationKind.Collapsed => "collapsed",
                    FormationKind.Fragmented => "fragmented",
                    _ => "unknown",
                };

                // A fall at the very start date is a war, not a memory.
                if (year > startYear - 10) return null;
                return new Evidence(held.Id, seat, held.Culture, year, kind);
            }

            return null;
        }
    }
}
