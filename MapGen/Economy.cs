namespace Ck3MapGen.MapGen;

/// <summary>
/// What a county earns its holder every month, by the game's own arithmetic.
///
/// This exists because CK3 has no "wealth" map mode to copy. The nearest thing vanilla ships is
/// <c>economy_buildings</c>, whose tooltip (<c>ECONOMY_MAP_TOOLTIP_ENTRY</c>) reads a holding's
/// <c>GetIncome</c> and prints it as gold per month — so the honest analogue for a generated world
/// is that same number, computed from the holdings <see cref="Emit.ContentWriter"/> actually wrote
/// and the development it actually assigned.
///
/// Every constant below is read out of the game files rather than invented, so a patch that
/// rebalances holdings makes this wrong in a way that is at least findable. The chain for each
/// holding is <c>common/holdings/00_holdings.txt</c> → its <c>primary_building</c> →
/// that building's <c>monthly_income</c> → <c>common/script_values/00_building_values.txt</c>.
/// </summary>
public static class Economy
{
    /// <summary>
    /// The primary building's <c>monthly_income</c> for a holding at the level every generated
    /// barony starts at, in gold per month.
    ///
    /// Resolved from vanilla 1.19:
    /// <list type="bullet">
    /// <item>castle_holding → castle_01 → poor_building_tax_tier_2 → 0.25 + 0.15</item>
    /// <item>city_holding → city_01 → good_building_tax_tier_2 → 0.5 + 0.3</item>
    /// <item>church_holding → temple_01 → normal_building_tax_tier_3 → 0.35 + 0.2 + 0.2</item>
    /// <item>tribal_holding → tribe_01 → poor_building_tax_tier_1 → 0.25, and only under
    /// <c>government_is_tribal_excluding_wanua</c>, which every generated tribal county satisfies</item>
    /// </list>
    ///
    /// Nomads and wilderness earn nothing here, and that is not an omission: nomadic_camp_01
    /// declares no <c>monthly_income</c> at all — a horde's purse comes from its herds — and our own
    /// wilderness_01 declares none either, because unsettled ground has nobody to tax.
    /// </summary>
    public static double HoldingIncome(string holding) => holding switch
    {
        "castle_holding" => 0.40,
        "city_holding" => 0.80,
        "church_holding" => 0.75,
        "tribal_holding" => 0.25,
        _ => 0.0,
    };

    /// <summary>
    /// The development multiplier CK3 applies to a county's taxes.
    ///
    /// <c>TAX_AT_MAX_COUNTY_DEVELOPMENT = 0.5</c> in <c>common/defines/00_defines.txt</c>, and the
    /// define's own comment says it is "interpolated between this value and 0% when between 0 and
    /// 100" — so each point of development is worth half a percent.
    /// </summary>
    public static double DevelopmentTaxMultiplier(int development)
        => 1.0 + 0.005 * Math.Clamp(development, 0, 100);

    /// <summary>
    /// A county's gold per month: every barony's holding income, lifted by the county's development.
    /// </summary>
    /// <param name="holdings">Holding key by barony province id, as the province history wrote it.
    /// A barony missing from it — or written <c>none</c> — contributes nothing, which is the point:
    /// an empty holding slot is empty.</param>
    public static double CountyIncome(Title county, IReadOnlyDictionary<int, string> holdings,
        int development)
    {
        double gross = 0;

        foreach (var barony in county.Children)
        {
            if (barony.ProvinceId >= 1 && holdings.TryGetValue(barony.ProvinceId, out var holding))
                gross += HoldingIncome(holding);
        }

        return gross * DevelopmentTaxMultiplier(development);
    }

    /// <summary>The holdings a county actually got, for a hover readout. Empty slots left out.</summary>
    public static IEnumerable<string> CountyHoldings(Title county,
        IReadOnlyDictionary<int, string> holdings)
    {
        foreach (var barony in county.Children)
        {
            if (barony.ProvinceId >= 1
                && holdings.TryGetValue(barony.ProvinceId, out var holding)
                && holding != "none")
            {
                yield return holding;
            }
        }
    }
}
