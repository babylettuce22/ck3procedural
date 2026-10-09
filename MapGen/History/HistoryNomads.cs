namespace Ck3MapGen.MapGen;

/// <summary>
/// Nomad realms' own pace of coming apart, set by the <see cref="SimSettings.Nomads"/> dial.
///
/// A horde is held together by its khan rather than by its land, so the three ways a realm breaks
/// up — strain (<see cref="Formation.Sim.Fragility"/>), vassals walking out (HistoryIndependence)
/// and a realm divided among heirs (HistoryPeople) — each take a nomad realm's own multiplier.
/// By default it is a little under a settled realm's (<see cref="NomadSteadiness"/>), so a horde
/// lasts somewhat longer than a kingdom of its size. Turned up, the khanates break into feuding
/// hordes on every death, as players asked for. A dial of 1/<see cref="NomadSteadiness"/> (about
/// 1.3) is the history as it ran before nomads had a rule of their own.
///
/// Only probabilities move, never the number of draws, so the dial shifts no one else's dice.
/// </summary>
public sealed partial class HistorySim
{
    /// <summary>A nomad realm's breakup rates against a settled realm's, at the dial's default of 1.</summary>
    internal const double NomadSteadiness = 0.75;

    /// <summary>How much more readily this realm comes apart than a settled one: 1 for any but a nomad.</summary>
    internal double Fragility(Polity p)
        => GovernmentOf(p) == GovernmentMap.Nomad ? NomadSteadiness * _settings.Nomads : 1.0;

    /// <summary>
    /// How the nomads stand, for the headless run's log: independent hordes, their counties and the
    /// largest, and their vassals. Empty for a world without nomads.
    /// </summary>
    public string NomadSummary()
    {
        var nomads = _sim.Polities.Where(p => p.Alive && GovernmentOf(p) == GovernmentMap.Nomad).ToList();
        if (nomads.Count == 0) return "";
        var free = nomads.Where(p => p.Suzerain is null).ToList();
        return $"; nomads: {free.Count} independent of {nomads.Count}, {nomads.Sum(p => p.Counties.Count)} counties, "
               + $"largest independent {(free.Count > 0 ? free.Max(p => p.Counties.Count + Vassals(p)) : 0)} with vassals";

        int Vassals(Polity root) => _sim.Polities.Where(v => v.Alive && v != root && v.Root == root).Sum(v => v.Counties.Count);
    }
}
