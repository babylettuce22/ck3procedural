using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// One war of the History workspace: an independent realm against another, over a goal of
/// counties — a de jure duchy, or the attacker's claims — fought out over years.
/// </summary>
public sealed class SimWar
{
    public required int Id { get; init; }

    /// <summary>The independent realm that declared it.</summary>
    public required Polity Attacker { get; init; }

    /// <summary>
    /// Who gets the goal on a win: the attacker, or the vassal of it whose border the war was
    /// fought from — the goal has to join onto somebody's land.
    /// </summary>
    public required Polity Beneficiary { get; init; }

    /// <summary>The independent realm defending, for itself or a vassal.</summary>
    public required Polity Defender { get; init; }

    /// <summary>The de jure duchy fought over, or null for a war over claims.</summary>
    public Title? Duchy { get; init; }

    /// <summary>The county whose conquest the dice granted, which the war was declared from.</summary>
    public required Title Target { get; init; }

    /// <summary>The counties the attacker wants, as they were declared; narrowed to what the defender still holds as it goes.</summary>
    public required HashSet<Title> Goal { get; init; }

    public required int Started { get; init; }

    /// <summary>-100 to 100: at 100 the attacker has won, at -100 the defender.</summary>
    public double Score { get; set; }

    /// <summary>What the war is called in the chronicle.</summary>
    public string Name => Duchy is { } duchy ? $"the war for {duchy.Name}" : $"the war for {Attacker.Capital.Name}'s claims";
}

/// <summary>
/// Wars, truces and claims: conquest as CK3 fights it. A realm the dice say would take a county
/// declares a war instead — over the county's de jure duchy, or over its own claims on the
/// defender's land — and the war runs on its own dice, a battle a year, until one side's score
/// reaches 100 or it has gone on ten years. A peace hands over as much of the goal as joins onto
/// the winner's land, all at once; the loser keeps claims on what it lost for a generation; and
/// the two are held to vanilla's five-year truce.
///
/// Only independent realms go to war, as in CK3. A vassal whose border the dice picked calls its
/// liege into the war and is the one given the land, so the goal always joins onto someone.
///
/// Rides on the formation's own conquest roll through <see cref="Formation.Sim.Wage"/>, so how
/// often realms fight is still the aggression dial; the wars' own dice are a separate stream, and
/// with the rule off the history is county-by-county conquest exactly as before.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>Vanilla's standard truce: <c>standard_truce_duration_days = 1825</c>.</summary>
    private const int TruceYears = 5;

    /// <summary>How long a realm keeps a claim on land taken from it in a war: about a generation.</summary>
    private const int ClaimYears = 50;

    /// <summary>A war still undecided after this long ends on its score: a clear lead wins, anything else is a white peace.</summary>
    private const int MaxWarYears = 10;

    /// <summary>The war score a year's decisive battle is worth, and a battle the formation's dice add.</summary>
    private const double BattleScore = 30, SkirmishScore = 15;

    private readonly List<SimWar> _wars = [];
    private readonly Dictionary<(int, int), int> _truces = [];
    private readonly Dictionary<Title, (Polity Claimant, int Until)> _claims = [];
    private int _nextWar = 1;

    /// <summary>The wars under way, oldest first.</summary>
    public IReadOnlyList<SimWar> Wars => _wars;

    /// <summary>The war a county is being fought over, or null.</summary>
    public SimWar? WarOver(Title county) => _wars.FirstOrDefault(w => w.Goal.Contains(county));

    /// <summary>The realm with a claim on a county, and until when, or null.</summary>
    public (Polity Claimant, int Until)? ClaimOn(Title county)
        => _claims.TryGetValue(county, out var claim) && claim.Claimant.Alive ? claim : null;

    /// <summary>Whether two realms are under a truce, and until when.</summary>
    public int? TruceUntil(Polity a, Polity b)
        => _truces.TryGetValue(Pair(a, b), out int until) && until > _sim.Year ? until : null;

    private static (int, int) Pair(Polity a, Polity b) => a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id);

    /// <summary>Every truce still running, by realm id, and the year it ends. Carried by <see cref="AppliedHistory"/>.</summary>
    public IEnumerable<(int A, int B, int Until)> Truces
        => _truces.Where(kv => kv.Value > _sim.Year).OrderBy(kv => kv.Key).Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value));

    /// <summary>Every claim still standing, and the year it lapses. Carried by <see cref="AppliedHistory"/>.</summary>
    public IEnumerable<(Title County, Polity Claimant, int Until)> Claims
        => _claims.Where(kv => kv.Value.Claimant.Alive && kv.Value.Until > _sim.Year)
                  .OrderBy(kv => kv.Key.Index).Select(kv => (kv.Key, kv.Value.Claimant, kv.Value.Until));

    private void SeatWars() => _sim.Wage = Declare;

    /// <summary>
    /// The formation's dice have given <paramref name="p"/> a county of <paramref name="defender"/>'s.
    /// With wars in force, that is a war — or a battle in one already under way, or nothing under a
    /// truce — and never the county on the spot.
    /// </summary>
    private bool Declare(Polity p, Polity defender, Title target)
    {
        if (!_settings.Rules.HasFlag(RealmRules.Wars)) return false;

        var attacker = p.Root;
        var enemy = defender.Root;

        // Already fighting: the dice's win is a battle won in that war, for whichever side rolled it.
        if (_wars.FirstOrDefault(w => (w.Attacker == attacker && w.Defender == enemy)
                                      || (w.Attacker == enemy && w.Defender == attacker)) is { } war)
        {
            war.Score = Math.Clamp(war.Score + (war.Attacker == attacker ? SkirmishScore : -SkirmishScore), -100, 100);
            return true;
        }

        // A truce holds, and a realm fights one war of its own choosing at a time.
        if (TruceUntil(attacker, enemy) is not null) return true;
        if (_wars.Any(w => w.Attacker == attacker)) return true;

        // Its own claims first — the land it lost — and otherwise the duchy the county lies in.
        var claimed = _claims
            .Where(kv => kv.Value.Claimant == attacker && kv.Value.Until > _sim.Year
                         && _sim.Owner.TryGetValue(kv.Key, out var holder) && holder.Root == enemy)
            .Select(kv => kv.Key)
            .ToHashSet();

        Title? duchy = null;
        HashSet<Title> goal;
        if (claimed.Count > 0) goal = claimed;
        else
        {
            duchy = DeJureOf(target, "d") ?? target.Parent;
            goal = duchy is null
                ? [target]
                : [.. duchy.Children.Where(c => c.Tier == "c" && _sim.Owner.TryGetValue(c, out var holder) && holder.Root == enemy)];
            if (goal.Count == 0) goal = [target];
        }

        var declared = new SimWar
        {
            Id = _nextWar++, Attacker = attacker, Beneficiary = p, Defender = enemy, Duchy = duchy,
            Target = target, Goal = goal, Started = _sim.Year,
        };
        _wars.Add(declared);

        string behalf = p == attacker ? "" : $", for its vassal {p.Capital.Name}";
        string over = duchy is not null
            ? $"for the duchy of {duchy.Name}"
            : $"to take back {Names(goal)}";
        _sim.Log(FormationKind.WarDeclared, target, attacker, enemy, attacker.Culture == enemy.Culture ? 1 : 3,
            $"The realm of {attacker.Capital.Name} declared war on the realm of {enemy.Capital.Name} {over}{behalf}");
        return true;
    }

    /// <summary>The year's fighting and peaces, oldest war first, on the wars' own stream.</summary>
    private void WarsYear()
    {
        foreach (var (county, claim) in _claims.ToList())
            if (claim.Until <= _sim.Year || !claim.Claimant.Alive
                || _sim.Owner.GetValueOrDefault(county)?.Root == claim.Claimant.Root)
                _claims.Remove(county);

        if (_wars.Count == 0) return;
        var rng = new Rng(_seed ^ 0x3A77 ^ unchecked((int)((uint)_sim.Year * 0x9E3779B1u)));

        foreach (var war in _wars.ToList())
        {
            // Overtaken by events: a side gone, or no longer its own master, or nothing left to take.
            string? moot = !war.Attacker.Alive || !war.Defender.Alive ? "one side no longer stands"
                : war.Attacker.Suzerain is not null ? $"{war.Attacker.Capital.Name} bent the knee"
                : war.Defender.Suzerain is not null ? $"{war.Defender.Capital.Name} bent the knee"
                : null;
            war.Goal.RemoveWhere(c => _sim.Owner.GetValueOrDefault(c)?.Root != war.Defender);
            if (moot is null && war.Goal.Count == 0) moot = "nothing was left to fight over";
            if (moot is not null)
            {
                End(war, $"{Capitalise(war.Name)} was abandoned: {moot}");
                continue;
            }

            // The year's battle, weighed by what each side can bring to bear.
            double atk = Formation.Strength(_sim, war.Attacker), def = Formation.Strength(_sim, war.Defender);
            bool won = rng.Chance(atk / Math.Max(1e-9, atk + def));
            war.Score = Math.Clamp(war.Score + (won ? BattleScore : -BattleScore), -100, 100);

            int years = _sim.Year - war.Started;
            if (war.Score >= 100 || (years >= MaxWarYears && war.Score >= 50)) Win(war);
            else if (war.Score <= -100 || (years >= MaxWarYears && war.Score <= -50))
            {
                // A claim fought for and lost is given up.
                foreach (var county in war.Goal) if (_claims.TryGetValue(county, out var c) && c.Claimant == war.Attacker) _claims.Remove(county);
                End(war, $"The realm of {war.Defender.Capital.Name} won {war.Name} against {war.Attacker.Capital.Name}");
            }
            else if (years >= MaxWarYears)
                End(war, $"{Capitalise(war.Name)} between {war.Attacker.Capital.Name} and {war.Defender.Capital.Name} ended in a white peace");
        }
    }

    /// <summary>
    /// The attacker's peace: every county of the goal that joins onto the winner's land changes
    /// hands at once, the realms that lost them keep claims, and anything the peace cut off from
    /// its capital goes its own way, as a conquest's would.
    /// </summary>
    private void Win(SimWar war)
    {
        var winner = war.Beneficiary.Alive && war.Beneficiary.Root == war.Attacker ? war.Beneficiary : war.Attacker;

        // The part of the goal reachable from the winner's land through the goal itself.
        var taken = new List<Title>();
        var queue = new Queue<Title>(war.Goal
            .Where(c => _sim.Adjacent.TryGetValue(c, out var near) && near.Any(winner.Counties.Contains))
            .OrderBy(c => c.Index));
        var seen = queue.ToHashSet();
        while (queue.Count > 0)
        {
            var county = queue.Dequeue();
            taken.Add(county);
            if (!_sim.Adjacent.TryGetValue(county, out var near)) continue;
            foreach (var n in near.OrderBy(n => n.Index))
                if (war.Goal.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }

        var losers = new HashSet<Polity>();
        foreach (var county in taken.OrderBy(c => c.Index))
        {
            var holder = _sim.Owner[county];
            losers.Add(holder);
            Formation.Take(_sim, county, holder, winner, log: false);
            if (war.Defender.Alive) _claims[county] = (war.Defender, _sim.Year + ClaimYears);
        }
        foreach (var loser in losers.Where(l => l.Alive).OrderBy(l => l.Capital.Index)) Formation.ShedIslands(_sim, loser);

        End(war, taken.Count == 0
            ? $"The realm of {war.Attacker.Capital.Name} won {war.Name}, but none of it joined onto its land"
            : $"The realm of {war.Attacker.Capital.Name} won {war.Name}: {war.Defender.Capital.Name} gave up {Names(taken)}");
    }

    private void End(SimWar war, string note)
    {
        _wars.Remove(war);
        if (war.Attacker.Alive && war.Defender.Alive) _truces[Pair(war.Attacker, war.Defender)] = _sim.Year + TruceYears;
        _sim.Log(FormationKind.WarEnded, war.Duchy?.Capital ?? war.Goal.FirstOrDefault() ?? war.Attacker.Capital,
            war.Attacker.Alive ? war.Attacker : null, war.Defender.Alive ? war.Defender : null, 0, note);
    }

    private static string Names(IEnumerable<Title> counties)
    {
        var names = counties.OrderBy(c => c.Index).Select(c => c.Name).ToList();
        return names.Count switch
        {
            0 => "nothing",
            1 => names[0],
            <= 4 => string.Join(", ", names[..^1]) + " and " + names[^1],
            _ => $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more",
        };
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private void CheckWars(List<string> problems)
    {
        // A side can die after the year's peaces — a partition or a ruin later in the tick — and the
        // war is closed next year, so only what can never be true is checked.
        foreach (var war in _wars)
            if (war.Attacker == war.Defender) problems.Add($"{war.Name}: a realm at war with itself");
    }
}
