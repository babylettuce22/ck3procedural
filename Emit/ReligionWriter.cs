using System.Globalization;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class ReligionWriter
{
    public static void WriteAll(string modDir, FaithMap faiths)
    {
        WriteHolySites(modDir, faiths);
        WriteReligions(modDir, faiths);
        WriteLocalisation(modDir, faiths);

        int sites = faiths.Faiths.Sum(f => f.HolySites.Count);
        Console.WriteLine($"  faiths written: {faiths.Faiths.Count} faiths in " +
                          $"{faiths.Religions.Count} religions, {sites} holy sites");
    }

    private static void WriteHolySites(string modDir, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "religion", "holy_site_types");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated holy sites, one per faith's richest counties.");
        b.Blank();

        // Deduplicate across shared holy sites
        var writtenSites = new HashSet<string>(StringComparer.Ordinal);

        foreach (var faith in faiths.Faiths)
        {
            foreach (var (key, county) in faith.HolySites)
            {
                if (!writtenSites.Add(key)) continue;

                using (b.Block(key))
                {
                    b.Field("county", county.Key);
                    b.Blank();

                    using (b.Block("character_modifier"))
                    {
                        b.Field("name", $"holy_site_{key}_effect_name");
                        b.Field("monthly_piety_gain_mult", "0.1");
                    }
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_holy_sites.txt"), b.ToString());
    }

    private static void WriteReligions(string modDir, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "religion", "religion_types");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        b.Comment("Generated religions and their faiths.");
        b.Blank();

        foreach (var religion in faiths.Religions)
        {
            using (b.Block(religion.Key))
            {
                // The family gates flavour only; the hostility doctrine in the list below is what
                // decides who this religion may holy-war. Pagan roots exist for the unreformed
                // doctrine's reform flow, which an Abrahamic-shaped religion never enters.
                b.Field("family", religion.Abrahamic ? MapGen.Faiths.AbrahamicFamily : MapGen.Faiths.Family);
                b.Field("graphical_faith", religion.GraphicalFaith);
                if (!religion.Abrahamic) b.Field("pagan_roots", "yes");
                b.Blank();

                foreach (var (_, doctrine) in religion.Doctrines) b.Field("doctrine", doctrine);
                b.Blank();

                using (b.Block("traits"))
                {
                    b.Inline("virtues", string.Join(' ', religion.Virtues));
                    b.Inline("sins", string.Join(' ', religion.Sins));
                }

                b.Blank();

                using (b.Block("localization"))
                    foreach (var (tag, value) in religion.Localization) b.Field(tag, value);

                b.Blank();

                using (b.Block("faiths"))
                {
                    foreach (var faith in religion.Faiths)
                    {
                        var (r, g, bl) = faith.Color;

                        using (b.Block(faith.Key))
                        {
                            b.Inline("color", F(r), F(g), F(bl));
                            b.Field("icon", faith.Icon);

                            // Unreformed faiths require reformed_icon, otherwise Reformation/Holy Site view causes CTD
                            if (!faith.IsOrganized)
                            {
                                b.Field("doctrine", "unreformed_faith_doctrine");
                                b.Field("reformed_icon", faith.Icon);
                            }

                            b.Blank();

                            if (faith.Head is not null && faith.IsOrganized)
                            {
                                // A temporal head is a landed ruler who is also the faith's head —
                                // see HistoryWriter, which hands the title to one — and vanilla
                                // pairs it with no anointment, so the rite below is left to the
                                // spiritual kind.
                                b.Field("doctrine", faith.Head.Temporal
                                    ? "doctrine_temporal_head"
                                    : "doctrine_spiritual_head");
                                b.Field("religious_head", faith.Head.TitleKey);

                                // The anointment rite belongs beside the head that performs it.
                                //
                                // Its doctrine group is filled at *religion* level, where
                                // doctrine_head_of_faith is pinned to doctrine_no_head — so the
                                // can_pick repair in Faiths.Build correctly rules the two anointment
                                // doctrines out and every religion lands on doctrine_no_anointment.
                                // That is right for the religion and wrong for the faiths overridden
                                // here: they have a head of faith and inherited a rite that assumes
                                // there is none, which left `crowned_emperor` unreachable on the
                                // whole map and the imperial branch of the coronation dead.
                                //
                                // A faith-level doctrine overrides its religion's for the same
                                // group, so one line here reconciles them. The dominant faith of a
                                // religion gets the imperial rite — it is the one whose head is
                                // expected to crown emperors, and it is what makes the anointment
                                // option default-on at empire tier — and the rest get the plain
                                // permission.
                                //
                                // Guarded on the religion having drawn a coronation doctrine at
                                // all, which is this generator's own record of whether the install
                                // it read defines the group: the doctrines live in a base-game file
                                // but only since the patch Coronations shipped with, and naming one
                                // an older install has never heard of is a hard script error rather
                                // than a feature quietly doing nothing.
                                if (!faith.Head.Temporal && religion.Doctrines.ContainsKey("doctrine_coronation"))
                                {
                                    b.Field("doctrine", faith.IsDominant
                                        ? "doctrine_imperial_anointment"
                                        : "doctrine_anointment_permitted");
                                }
                            }

                            // Ensure every faith has at least one holy site
                            if (faith.HolySites.Count > 0)
                            {
                                foreach (var (key, _) in faith.HolySites) b.Field("holy_site", key);
                            }
                            else if (faiths.Faiths.Any(f => f.HolySites.Count > 0))
                            {
                                // Fallback to avoid fatal error on empty dummy faiths
                                var fallbackSite = faiths.Faiths.First(f => f.HolySites.Count > 0).HolySites[0];
                                b.Field("holy_site", fallbackSite.Key);
                            }

                            b.Blank();

                            foreach (string tenet in faith.Tenets) b.Field("doctrine", tenet);
                        }
                    }
                }
            }

            b.Blank();
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_religions.txt"), b.ToString());
    }

    /// <summary>
    /// Not private: holy site names read <c>county.Name</c> off the live title, so renaming a
    /// county after the write means re-running exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteLocalisation(string modDir, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var religion in faiths.Religions)
        {
            entries[religion.Key] = religion.Name;
            entries[$"{religion.Key}_adj"] = religion.Name;
            entries[$"{religion.Key}_adherent"] = religion.Name;
            entries[$"{religion.Key}_adherent_plural"] = religion.Name + "s";
            entries[$"{religion.Key}_desc"] =
                $"The faiths gathered under {religion.Name} share their gods and their rites, " +
                $"and disagree about everything else.";

            foreach (var (key, value) in religion.LocalizationText) entries[key] = value;
        }

        foreach (var faith in faiths.Faiths)
        {
            entries[faith.Key] = faith.Name;
            entries[$"{faith.Key}_adj"] = faith.Name;
            entries[$"{faith.Key}_adherent"] = faith.Name;
            entries[$"{faith.Key}_adherent_plural"] = faith.Name + "s";
            entries[$"{faith.Key}_desc"] = $"The teachings of {faith.Name}.";

            // Localization required for unreformed faiths when reforming
            if (!faith.IsOrganized)
            {
                entries[$"{faith.Key}_old"] = $"Old {faith.Name}";
                entries[$"{faith.Key}_old_adj"] = $"Old {faith.Name}";
                entries[$"{faith.Key}_old_adherent"] = $"Old {faith.Name}";
                entries[$"{faith.Key}_old_adherent_plural"] = $"Old {faith.Name}s";
            }

            if (faith.Head is not null)
                entries[faith.Head.TitleKey] = faith.Head.Name;

            foreach (var (key, county) in faith.HolySites)
            {
                entries[$"holy_site_{key}_name"] = county.Name;
                entries[$"holy_site_{key}_effect_name"] = $"From [holy_site|E] #weak ($holy_site_{key}_name$)#!";
            }
        }

        var loc = new LocFile();
        foreach (var (key, value) in entries) loc.Add(key, value);

        loc.Write(Path.Combine(dir, "gen_faiths_l_english.yml"));
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}