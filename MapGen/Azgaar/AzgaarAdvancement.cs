using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// How advanced an imported world is, read from what the export says about its peoples.
///
/// Azgaar has no notion of an era — its year is a calendar date, and a default export is dated
/// 15532 whatever it looks like — so the advancement a world is judged against (innovations, the
/// development baseline, the men-at-arms era; see <see cref="Config.MapConfig.EraAnchorYear"/>)
/// used to be whatever the world year had been before the import, 900 by default. That was an
/// answer from outside the map. The export does describe how its peoples live, in three ways that
/// separate an early world from a late one:
/// <list type="bullet">
/// <item><b>Settled against tribal</b> — the share of people in countries whose government
/// <see cref="AzgaarGovernments"/> reads as settled rather than tribal. Nomads count as neither:
/// vanilla has hordes in every era. People in no country are left out too; that land starts wild.
/// This is the same reading the governments come from, so the innovations a world is given agree
/// with the realms it is given.</item>
/// <item><b>Organised faiths</b> — the share following an Organized religion or a heresy of one,
/// against folk faiths and cults.</item>
/// <item><b>Walled towns</b> — the share of town-dwellers behind walls or a citadel.</item>
/// </list>
/// All three are weighted by population, cell by cell, so a vast empty tribal steppe does not
/// outweigh a crowded feudal heartland.
///
/// The score maps onto CK3's own starts: a third or less settled-organised-walled is 867, about four
/// fifths is 1066, and everything is 1178. Measured on the two real exports to hand, Ondrerol (a
/// third tribal, folk faiths three to one) reads as 911 and Fleunland (no tribes, organised faiths
/// the majority) as 1056.
/// </summary>
public static class AzgaarAdvancement
{
    /// <summary>What was read, and the year it comes to.</summary>
    public sealed record Reading(int Year, double Settled, double Organized, double Walled)
    {
        /// <summary>One line for the log and the page: the year and the three reasons for it.</summary>
        public string Describe()
            => $"as advanced as {Year}: {Percent(Settled)} of people settled rather than tribal, "
             + $"{Percent(Organized)} in organised faiths, {Percent(Walled)} of townsfolk behind walls";

        private static string Percent(double share) => $"{Math.Round(share * 100):F0}%";
    }

    private const double SettledWeight = 0.45, OrganizedWeight = 0.35, WalledWeight = 0.20;

    /// <summary>
    /// Reads the export. Null when it carries no people to read — a "Minimal" export with no cells,
    /// or one with every population at zero — and the caller keeps its old answer.
    /// </summary>
    public static Reading? Read(AzgaarWorld world)
    {
        if (!world.HasCells) return null;

        var governmentOf = world.RealStates.ToDictionary(s => s.I, AzgaarGovernments.For);

        double settled = 0, tribal = 0, organized = 0, faithful = 0, people = 0;
        foreach (var cell in world.Pack.Cells)
        {
            if (!cell.IsLand || cell.Pop <= 0) continue;
            double pop = cell.Pop;
            people += pop;

            if (governmentOf.TryGetValue(cell.State, out string? government))
            {
                if (government == GovernmentMap.Tribal) tribal += pop;
                else if (government != GovernmentMap.Nomad) settled += pop;
            }

            if (world.Religion(cell.Religion) is { } religion)
            {
                faithful += pop;
                if (religion.Type.Equals("Organized", StringComparison.OrdinalIgnoreCase)
                    || religion.Type.Equals("Heresy", StringComparison.OrdinalIgnoreCase))
                    organized += pop;
            }
        }
        if (people <= 0) return null;

        double townsfolk = 0, walled = 0;
        foreach (var burg in world.RealBurgs)
        {
            townsfolk += burg.Population;
            if (burg.Walls != 0 || burg.Citadel != 0) walled += burg.Population;
        }

        // A share with nothing to measure reads as the middle, so it neither drags nor lifts.
        double settledShare = settled + tribal > 0 ? settled / (settled + tribal) : 0.5;
        double organizedShare = faithful > 0 ? organized / faithful : 0.5;
        double walledShare = townsfolk > 0 ? walled / townsfolk : 0.5;

        double score = SettledWeight * settledShare + OrganizedWeight * organizedShare + WalledWeight * walledShare;
        return new Reading(YearFor(score), settledShare, organizedShare, walledShare);
    }

    /// <summary>The score on CK3's timeline: 867 up to a third, 1066 at four fifths, 1178 at all.</summary>
    public static int YearFor(double score)
    {
        score = Math.Clamp(score, 0, 1);
        if (score <= 0.35) return 867;
        if (score <= 0.80) return (int)Math.Round(867 + (score - 0.35) / 0.45 * (1066 - 867));
        return (int)Math.Round(1066 + (score - 0.80) / 0.20 * (1178 - 1066));
    }
}
