using Ck3MapGen.Io;

namespace Ck3MapGen.MapGen;

/// <summary>
/// What kind of place a burg is, in CK3's terms.
///
/// Azgaar draws a settlement layer our generator has no way to derive: 1,600-odd burgs, each with
/// a population and a set of building flags saying whether it has a citadel, walls, a temple, a
/// market plaza, a shanty town and a harbour. Those flags are exactly the question
/// <see cref="Development.Holding"/> was answering with a die roll against terrain, so where a
/// burg stands the roll stands down and the export answers instead.
///
/// The reading is deliberately from the FLAGS, not from <see cref="AzgaarBurg.Group"/> alone.
/// Groups are a newer field, they are editable, and the author can rename or delete them — but
/// they are computed from the flags in the first place (<c>settings.options.burgs.groups</c> holds
/// rules like <c>monastery: {temple: true, walls: false, plaza: false, port: false}</c>), so
/// reading the flags reproduces the taxonomy on an export that has no groups at all and agrees
/// with it on one that does. The group is used only where it says something the flags cannot: how
/// big a place is relative to the rest of the map.
/// </summary>
public static class AzgaarSettlement
{
    /// <summary>
    /// The holding a burg makes, or null when the export has drawn a settlement without saying
    /// what kind of place it is.
    ///
    /// Ordered most specific first, because the flags overlap: a cathedral city has a temple AND a
    /// plaza AND walls, and it is a city. Only a temple standing on its own is a monastery.
    ///
    /// - A temple with no market and no garrison is a <c>church_holding</c> — Azgaar's "monastery".
    /// - A citadel or walls with no market and no harbour is a <c>castle_holding</c> — its "fort".
    /// - A plaza or a harbour is a <c>city_holding</c>: a burg with a plaza is a market town by
    ///   Azgaar's own rules ("trading_post", "caravanserai"), and a port trades whether or not a
    ///   plaza was drawn on it.
    ///
    /// **Null for a burg with no flags at all, which is most of them**, and that matters more than
    /// any of the mappings above. The flags are drawn for Azgaar's burg preview, not as a census:
    /// on a real export barely one burg in seventy carries a temple, so reading an unflagged town
    /// as a city — the obvious default — silently empties the settled half of the map of church
    /// holdings, and with them of bishops, while nothing in the export ever said so. Where the
    /// flags are silent the burg is still a holding, but which holding stays the generator's own
    /// question to answer from terrain and development.
    /// </summary>
    public static string? Holding(AzgaarBurg burg)
    {
        bool market = burg.Plaza != 0 || burg.IsPort;

        if (burg.Temple != 0 && !market && burg.Citadel == 0 && burg.Walls == 0)
            return "church_holding";

        if ((burg.Citadel != 0 || burg.Walls != 0) && !market)
            return "castle_holding";

        return market ? "city_holding" : null;
    }

    /// <summary>
    /// Whether this burg is a place with a holding in it, rather than a hamlet on the road.
    ///
    /// CK3 baronies are county-sized fractions and a map has far more of them than a realm has
    /// castles; a village of four hundred people is not a barony's worth of holding, and writing
    /// one for every burg would hand the player a continent of two-holding counties.
    ///
    /// A burg qualifies if it carries any building at all — a temple, a citadel, walls, a market
    /// or a harbour is a thing somebody built and garrisoned — or if it is large for this map.
    /// "Large for this map" is measured against the map's own median burg rather than an absolute
    /// head count, for the same reason development is ranked rather than scored: population points
    /// scale with <c>settings.populationRate</c> and with how densely the author drew his world,
    /// so an absolute floor reads every burg on one map as a city and every burg on the next as a
    /// hamlet.
    ///
    /// <see cref="AzgaarBurg.Group"/>, where the export has one, overrules on the two ends the
    /// flags cannot see: an author who has marked a place a hamlet has said it is not a holding
    /// whatever it was built with, and one who has marked it a city or a capital has said it is.
    /// </summary>
    public static bool IsHolding(AzgaarBurg burg, double medianPopulation)
    {
        switch (burg.Group?.ToLowerInvariant())
        {
            case "hamlet": return false;
            case "capital" or "city" or "town": return true;
        }

        if (burg.IsCapital) return true;
        if (burg.Citadel != 0 || burg.Walls != 0 || burg.Temple != 0 || burg.Plaza != 0 || burg.IsPort)
            return true;

        return burg.Population >= medianPopulation;
    }

    /// <summary>
    /// A one-word label for the log, so a run says what the export gave it rather than only how
    /// many holdings came out of it.
    /// </summary>
    public static string Describe(AzgaarBurg burg) => Holding(burg) switch
    {
        "church_holding" => "monastery",
        "castle_holding" => "fort",
        "city_holding" => burg.IsPort ? "port" : "market",
        _ => "unflagged town",
    };
}
