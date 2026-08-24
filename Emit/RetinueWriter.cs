using System.Globalization;
using Ck3MapGen.Config;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Writes the generated men-at-arms: the regiment definitions, their names and flavour, and the
/// standing troops the world's rulers already have on the start date.
///
/// **Additive, and deliberately only additive.** Vanilla's generic roster — light footmen, bowmen,
/// pikemen, horsemen, siege engines — is left exactly as it is, because it is the floor every
/// ruler on any map recruits from and a generated world needs that floor as much as a historical
/// one. What the generated regiments replace is the *cultural* layer above it, and they replace it
/// by displacement rather than by deletion: <see cref="MapGen.Cultures"/> keeps the traditions
/// that unlock vanilla's named regiments off generated cultures, so nobody on this map can field
/// Huscarls, and everybody can field something of their own instead.
///
/// The innovations that unlock the elite regiments are not written here — see
/// <see cref="InnovationWriter"/>, which owns that shape for every system that wants one.
/// </summary>
public static class RetinueWriter
{
    public static void WriteAll(string modDir, RetinueMap retinues)
    {
        if (retinues.Regiments.Count == 0) return;

        WriteTypes(modDir, retinues);
        WriteLocalisation(modDir, retinues);
    }

    private static void WriteTypes(string modDir, RetinueMap retinues)
    {
        string dir = Path.Combine(modDir, "common", "men_at_arms_types");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated men-at-arms. Vanilla's generic roster is untouched and still recruitable;\n" +
                  "these are the cultural layer, one per heritage plus an elite for the cultures that\n" +
                  "earned one. Every stat is a rearrangement of the archetype's own vanilla average —\n" +
                  "see MapGen/Retinues.cs for the budget the rearrangement is held to.");
        b.Blank();

        foreach (var regiment in retinues.Regiments)
        {
            b.Comment($"{regiment.Name} — {regiment.Doctrine}, {regiment.Culture.Name}" +
                      (regiment.IsElite ? " (elite)" : " (heritage)"));

            using (b.Block(regiment.Key))
            {
                b.Field("type", regiment.Archetype);
                b.Blank();

                b.Field("damage", regiment.Damage);
                b.Field("toughness", regiment.Toughness);
                b.Field("pursuit", regiment.Pursuit);
                b.Field("screen", regiment.Screen);

                if (regiment.SiegeValue > 0)
                    b.Field("siege_value", regiment.SiegeValue, "0.##");

                if (regiment.TerrainBonus.Count > 0)
                {
                    b.Blank();
                    using (b.Block("terrain_bonus"))
                        foreach (var (terrain, stats) in regiment.TerrainBonus)
                            b.Inline(terrain, [.. Pairs(stats)]);
                }

                if (regiment.WinterNormal.Count > 0 || regiment.WinterHarsh.Count > 0)
                {
                    b.Blank();
                    using (b.Block("winter_bonus"))
                    {
                        if (regiment.WinterNormal.Count > 0)
                            b.Inline("normal_winter", [.. Pairs(regiment.WinterNormal)]);

                        if (regiment.WinterHarsh.Count > 0)
                            b.Inline("harsh_winter", [.. Pairs(regiment.WinterHarsh)]);
                    }
                }

                if (regiment.Counters.Count > 0)
                {
                    b.Blank();
                    using (b.Block("counters"))
                        foreach (var (target, weight) in regiment.Counters)
                            b.Field(target, weight, "0.##");
                }

                // A heritage regiment is gated on the pillar, an elite on its innovation — and the
                // two gates are mutually exclusive in CK3's own format, which is why the elite has
                // no can_recruit at all rather than a redundant one.
                if (!regiment.IsElite && regiment.Heritage is not null)
                {
                    b.Blank();
                    using (b.Block("can_recruit"))
                    using (b.Block("culture"))
                        b.Field("has_cultural_pillar", regiment.Heritage.Key);
                }

                b.Blank();
                b.Inline("buy_cost", "gold = " + regiment.BuyCost.ToString(CultureInfo.InvariantCulture));
                b.Inline("low_maintenance_cost", "gold = " + Number(regiment.LowMaintenance));
                b.Inline("high_maintenance_cost", "gold = " + Number(regiment.HighMaintenance));
                b.Field("provision_cost", regiment.ProvisionCost);

                b.Blank();
                b.Field("stack", regiment.Stack);
                b.Inline("ai_quality", "value = " + regiment.AiQuality.ToString(CultureInfo.InvariantCulture));

                if (regiment.Icon is not null) b.Field("icon", regiment.Icon);

                // Without one the unit card renders empty. The archetype's commonest reference is
                // used, which is always its untriggered fallback — the illustration that shows for
                // any culture rather than one behind an Asian-graphics trigger.
                if (regiment.Illustration is not null)
                {
                    using (b.Block("illustration"))
                        b.Field("reference", regiment.Illustration);
                }
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_maa_types.txt"), b.ToString());

        static IEnumerable<string> Pairs(Dictionary<string, int> stats)
            => stats.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => $"{kv.Key} = {kv.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>Two decimals at most, and never a trailing <c>.0</c> — vanilla writes 0.4, not 0.40.</summary>
    private static string Number(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// The regiment's name and its flavour line.
    ///
    /// Both keyed on the regiment's own key, which is CK3's convention here rather than ours: a
    /// men-at-arms type is looked up by key with <c>_flavor</c> appended, and a missing name prints
    /// the raw <c>gen_maa_h3</c> into the recruitment window.
    /// </summary>
    private static void WriteLocalisation(string modDir, RetinueMap retinues)
    {
        var loc = new LocFile();

        foreach (var regiment in retinues.Regiments)
        {
            loc.Add(regiment.Key, regiment.Name);
            loc.AddBuilt($"{regiment.Key}_flavor", regiment.Flavor);
        }

        loc.Write(Path.Combine(modDir, "localization", "english", "gen_maa_l_english.yml"));
    }

    /// <summary>
    /// What every independent ruler already has under arms when the game opens.
    ///
    /// An addition to what the engine already does, not a replacement for it. CK3 buys every
    /// start-date ruler a roster of its own — <c>MAA_STARTING_EXPENSE_MIN</c> is 0.2 and
    /// <c>_MAX</c> is 0.35 of monthly balance, spent at game start — and the generated regiments'
    /// <c>ai_quality</c> of 80 to 100 means that spending lands on them by preference. What this
    /// adds is a *guarantee*: the culture's own regiment specifically, on the ruler who represents
    /// that culture, rather than whatever the budget reached. It is off by default for exactly
    /// that reason — see <see cref="MapConfig.EnableStartingRetinues"/>.
    ///
    /// Only realm heads, and only one regiment scaled by rank, because the cost of getting this
    /// wrong is an AI running a deficit from the first month. A count's single sub-regiment of a
    /// heritage unit costs him well under a gold a month.
    /// </summary>
    public static void WriteStartingRegiments(string modDir, MapConfig cfg, RetinueMap retinues,
        RulerMap rulers)
    {
        if (!cfg.EnableStartingRetinues || retinues.Regiments.Count == 0) return;

        string dir = Path.Combine(modDir, "common", "on_action");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Standing regiments for the rulers who hold the world on the start date.");
        b.Blank();

        using (b.Block("on_game_start"))
        using (b.Block("on_actions"))
            b.Token("gen_grant_starting_retinues");

        b.Blank();

        int granted = 0;

        using (b.Block("gen_grant_starting_retinues"))
        using (b.Block("effect"))
        {
            foreach (var ruler in rulers.All)
            {
                if (!ruler.Independent) continue;

                // The elite when the culture already knows how to raise it, the heritage regiment
                // otherwise. Never both: one regiment per ruler keeps the upkeep predictable.
                var regiment = retinues.Elite.TryGetValue(ruler.Culture, out var elite)
                               && elite.Unlock?.KnownAtStart.Contains(ruler.Culture) == true
                    ? elite
                    : retinues.ByHeritage.GetValueOrDefault(ruler.Culture.Heritage);

                if (regiment is null) continue;

                int size = ruler.Tier switch { "e" => 3, "k" => 2, _ => 1 };

                using (b.Block($"character:{ruler.Id}"))
                using (b.Block("create_maa_regiment"))
                {
                    b.Field("type", regiment.Key);

                    // The ruler is being handed his own people's regiment, so the recruitment
                    // trigger would pass — but an elite is gated on an innovation whose discovery
                    // date is the same instant this fires, and the ordering between the two is not
                    // ours to depend on.
                    b.Field("check_can_recruit", "no");
                    b.Field("size", size);
                }

                granted++;
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_gen_starting_retinues.txt"), b.ToString());
        Console.WriteLine($"  starting retinues: {granted} independent rulers armed at the start date");
    }
}
