namespace Ck3MapGen.MapGen;

/// <summary>How two races regard a marriage between them, from the closest to the furthest.</summary>
public enum RacePairTier
{
    Same,
    Friendly,
    Mixed,
    Hostile
}

/// <summary>
/// The race matrix as generation reads it, for the consorts <see cref="PrehistoryMap"/> marries
/// its rulers to. The same tiers the AI's marriage reluctance reads in game
/// (<c>gen_is_different_race_than</c>, <c>gen_is_friendly_race_pair_trigger</c> and
/// <c>gen_is_hostile_race_pair_trigger</c> in BaseFilesToCopy/Fantasy/common/scripted_triggers/
/// 00_phenotype_triggers.txt), so a starting marriage is one the game itself would have made.
/// Kept in step with that file by hand, as the triggers are with the traits' compatibility blocks.
///
/// The two elves are one people for marriage, as they are there. Symmetric: argument order never matters.
/// </summary>
public static class RacePairs
{
    public static RacePairTier Tier(RaceArchetype a, RaceArchetype b)
    {
        if (a == b || IsElf(a) && IsElf(b)) return RacePairTier.Same;
        if (Either(RaceArchetype.Human, RaceArchetype.Gnome)
            || Either(RaceArchetype.Dwarf, RaceArchetype.Gnome)
            || Either(RaceArchetype.Orc, RaceArchetype.Giantkin))
            return RacePairTier.Friendly;
        if (Either(RaceArchetype.Orc, RaceArchetype.HighElf)
            || Either(RaceArchetype.Orc, RaceArchetype.WoodElf)
            || Either(RaceArchetype.Orc, RaceArchetype.Dwarf)
            || Either(RaceArchetype.HighElf, RaceArchetype.DuskElf))
            return RacePairTier.Hostile;
        return RacePairTier.Mixed;

        bool Either(RaceArchetype x, RaceArchetype y) => a == x && b == y || a == y && b == x;
    }

    private static bool IsElf(RaceArchetype r) => r is RaceArchetype.HighElf or RaceArchetype.WoodElf;
}
