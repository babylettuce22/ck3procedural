using System.Globalization;
using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Declares the generated religions, their faiths, and the holy sites those faiths fight over.
///
/// Additive for the same reason <see cref="CultureWriter"/> is: vanilla script names faiths 2,135
/// times and religions 2,373 times, none of it map-bound, so blanking would cost thousands of
/// errors to buy nothing.
///
/// Holy sites are the exception and need care. They are the one part of a faith that reaches back
/// into the map — a site names a county — so they cannot be additive in the same careless way, and
/// they have to be written into the same directory <see cref="CompatibilityWriter.WriteHolySites"/>
/// rewrites, after it has run.
/// </summary>
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

    /// <summary>
    /// Holy sites, each pinned to a generated county.
    ///
    /// Written as a separate file in the holy_site_types directory rather than merged into the
    /// rewritten vanilla one, so the two stay independently readable — and so the ordering
    /// requirement is visible: this must run *after* the vanilla rewrite, which recreates the whole
    /// directory and would otherwise delete this file.
    ///
    /// The character modifier is not decoration. A holy site with no modifier is a site worth
    /// nothing to hold, and CK3 shows its effect in the faith interface, so an empty one reads as
    /// a bug to a player looking at it.
    /// </summary>
    private static void WriteHolySites(string modDir, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "religion", "holy_site_types");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated holy sites, one per faith's richest counties.\n\n");

        foreach (var faith in faiths.Faiths)
        {
            foreach (var (key, county) in faith.HolySites)
            {
                sb.Append($"{key} = {{\n");
                sb.Append($"\tcounty = {county.Key}\n\n");
                sb.Append("\tcharacter_modifier = {\n");
                sb.Append($"\t\tname = {key}_effect_name\n");
                sb.Append("\t\tmonthly_piety_gain_mult = 0.1\n");
                sb.Append("\t}\n");
                sb.Append("}\n\n");
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_holy_sites.txt"), sb.ToString());
    }

    private static void WriteReligions(string modDir, FaithMap faiths)
    {
        string dir = Path.Combine(modDir, "common", "religion", "religion_types");
        Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.Append("# Generated religions and their faiths.\n\n");

        foreach (var religion in faiths.Religions)
        {
            sb.Append($"{religion.Key} = {{\n");
            sb.Append($"\tfamily = {MapGen.Faiths.Family}\n");
            sb.Append($"\tgraphical_faith = {religion.GraphicalFaith}\n");
            sb.Append("\tpagan_roots = yes\n\n");

            // Hostility included: it is pinned to the pagan one for the whole religion (see
            // MapGen.Faiths.ForcedDoctrines), which is where vanilla declares it too, so there is
            // nothing left for a faith to override.
            foreach (var (_, doctrine) in religion.Doctrines)
                sb.Append($"\tdoctrine = {doctrine}\n");
            sb.Append('\n');

            sb.Append("\ttraits = {\n");
            sb.Append($"\t\tvirtues = {{ {string.Join(' ', religion.Virtues)} }}\n");
            sb.Append($"\t\tsins = {{ {string.Join(' ', religion.Sins)} }}\n");
            sb.Append("\t}\n\n");

            sb.Append("\tlocalization = {\n");
            foreach (var (tag, value) in religion.Localization)
                sb.Append($"\t\t{tag} = {value}\n");
            sb.Append("\t}\n\n");

            sb.Append("\tfaiths = {\n");
            foreach (var faith in religion.Faiths)
            {
                var (r, g, b) = faith.Color;
                sb.Append($"\t\t{faith.Key} = {{\n");
                sb.Append($"\t\t\tcolor = {{ {F(r)} {F(g)} {F(b)} }}\n");
                sb.Append($"\t\t\ticon = {faith.Icon}\n\n");

                // There is no "organized" doctrine to name: vanilla's `unreformed_faith` group holds
                // only unreformed_faith_doctrine and its West African variant, and a faith that
                // names neither *is* the organized case. Writing an invented opposite silently
                // dropped the whole distinction, so unorganized faiths came out reformed.
                if (!faith.IsOrganized)
                    sb.Append("\t\t\tdoctrine = unreformed_faith_doctrine\n");

                if (faith.ParentFaith is not null)
                {
                    sb.Append($"\t\t\tparent_faith = {faith.ParentFaith.Key}\n");
                }

                if (faith.Head is not null && faith.IsOrganized)
                {
                    sb.Append("\t\t\tdoctrine = doctrine_spiritual_head\n");
                    sb.Append($"\t\t\treligious_head = {faith.Head.TitleKey}\n");
                }

                foreach (var (key, _) in faith.HolySites)
                    sb.Append($"\t\t\tholy_site = {key}\n");
                sb.Append('\n');

                foreach (string tenet in faith.Tenets)
                    sb.Append($"\t\t\tdoctrine = {tenet}\n");

                sb.Append("\t\t}\n");
            }
            sb.Append("\t}\n");

            sb.Append("}\n\n");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_religions.txt"), sb.ToString());
    }
    private static void WriteLocalisation(string modDir, FaithMap faiths)
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

            if (faith.Head is not null)
                entries[faith.Head.TitleKey] = faith.Head.Name;

            foreach (var (key, county) in faith.HolySites)
                entries[$"{key}_effect_name"] = $"Holy Site of {county.Name}";
        }

        var sb = new StringBuilder();
        sb.Append("l_english:\n");
        foreach (var (key, value) in entries) sb.Append($" {key}:0 \"{value}\"\n");

        ParadoxText.WriteBom(Path.Combine(dir, "gen_faiths_l_english.yml"), sb.ToString());
    }
    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

}
