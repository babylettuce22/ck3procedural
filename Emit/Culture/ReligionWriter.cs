using System.Globalization;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

public static class ReligionWriter
{
    /// <param name="seed">The world's seed, which the descriptions are drawn with. See <see cref="FaithDescriptions"/>.</param>
    /// <param name="tooltips">Gloss the religions' gods and clergy words on hover. See <see cref="ReligionGlossary"/>.</param>
    /// <param name="fromGeneration">False for the editor's overwrite, whose world may have been loaded
    /// from files that carry no sees in memory; see <see cref="SeeWriter.WriteAll"/>.</param>
    public static void WriteAll(string modDir, FaithMap faiths, int seed, bool tooltips = true,
        bool fromGeneration = true)
    {
        WriteHolySites(modDir, faiths);
        WriteReligions(modDir, faiths);
        WriteFaithHistory(modDir, faiths);
        WriteLocalisation(modDir, faiths, seed, tooltips);
        SeeWriter.WriteAll(modDir, faiths, removeWhenNone: fromGeneration);

        // Here rather than beside the call site, so an editor save that rewrites the faiths
        // redraws the icons from the same edited colours and tenets.
        FaithIconWriter.WriteAll(modDir, faiths, seed);

        int sites = faiths.Faiths.Sum(f => f.HolySites.Count);
        Console.WriteLine($"  faiths written: {faiths.Faiths.Count} faiths in " +
                          $"{faiths.Religions.Count} religions, {sites} holy sites");
    }

    /// <summary>
    /// How many of a faith's holy sites are eminent: vanilla's own default,
    /// <c>FAITH_EMINENT_HOLY_SITES_MAX_DEFAULT</c>. The rest are ordinary sites.
    /// </summary>
    private const int EminentHolySites = 3;

    /// <summary>
    /// A faith's holy sites split the way CK3 1.20 splits them: the first
    /// <see cref="EminentHolySites"/> are eminent, whose faith-wide modifier reaches every follower,
    /// and the rest are ordinary, which reward only whoever holds the county. Sites are placed
    /// best first (<c>Faiths.PlaceAllHolySites</c>), so the eminent ones are the faith's greatest.
    /// </summary>
    internal static (List<string> Eminent, List<string> Ordinary) SplitHolySites(Faith faith)
    {
        var keys = faith.HolySites.Select(s => s.Key).Distinct(StringComparer.Ordinal).ToList();
        return (keys.Take(EminentHolySites).ToList(), keys.Skip(EminentHolySites).ToList());
    }

    /// <summary>
    /// Up to 1.19 a holy site had one <c>character_modifier</c>, applied to every follower of the
    /// faith holding it; ours was +10% monthly piety. 1.20 replaced it with a faith-wide modifier
    /// that only an eminent site applies and a modifier for the county's holder that any site
    /// applies. The faith-wide half keeps the old +10%; the holder's is vanilla's usual +5%.
    /// </summary>
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

                    using (b.Block("county_holder_character_modifier"))
                        b.Field("monthly_piety_gain_mult", "0.05");

                    using (b.Block("faith_character_modifier"))
                        b.Field("monthly_piety_gain_mult", "0.1");
                }

                b.Blank();
            }
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_generated_holy_sites.txt"), b.ToString());
    }

    /// <summary>
    /// Religions, faiths and rites in CK3 1.20's layout: one database each, in
    /// <c>common/religion/religion_types</c>, <c>faith_types</c> and <c>rite_types</c>.
    ///
    /// Every faith gets exactly one rite, its main rite, keyed like the faith and carrying a copy
    /// of its colour, tenets and doctrines. That is what vanilla does for each of its own
    /// single-rite faiths (<c>02_mainline_rite_types.txt</c>), and for two reasons it spells out:
    /// a faith left to the engine gets a dynamic rite with a positional key script and history
    /// cannot name, and a rite with an empty <c>tenets</c> block falls back to the religion's core
    /// tenets rather than the faith's, which would silently drop the faith's own. Sharing the key
    /// also means the rite reads the faith's name, adjective and adherent localisation.
    /// </summary>
    private static void WriteReligions(string modDir, FaithMap faiths)
    {
        string root = Path.Combine(modDir, "common", "religion");
        string dir = Path.Combine(root, "religion_types");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder();
        var faithFile = new JominiBuilder();
        var riteFile = new JominiBuilder();
        b.Comment("Generated religions. Their faiths are in faith_types, each faith's main rite in rite_types.");
        b.Blank();
        faithFile.Comment("Generated faiths. Each has one scripted main rite of the same key in rite_types.");
        faithFile.Blank();
        riteFile.Comment("Generated main rites, one per faith and keyed like it, carrying the faith's colour,");
        riteFile.Comment("tenets and doctrines as vanilla's 02_mainline_rite_types.txt does for its own.");
        riteFile.Blank();

        foreach (var religion in faiths.Religions)
        {
            using (b.Block(religion.Key))
            {
                // The family gates flavour only; the hostility doctrine in the list below is what
                // decides who this religion may holy-war. Pagan roots exist for the unreformed
                // doctrine's reform flow, which an Abrahamic-shaped religion never enters.
                using (b.Block("religion_details"))
                {
                    b.Field("family", religion.Abrahamic ? MapGen.Faiths.AbrahamicFamily : MapGen.Faiths.Family);
                    b.Field("graphical_faith", religion.GraphicalFaith);

                    // A religion with an institutional clergy names ecclesiastical government for its
                    // theocrats, as vanilla's Christianity does: grants and Adopt Theocratic Rule hand
                    // out whatever this names, and without it a see's successor falls back to plain
                    // theocracy_government, which has no treasury, domicile or lease hierarchy. Every
                    // such religion, not only those with sees today: a faith of it that reforms in
                    // play founds sees (zz_gen_clerical_regions_on_actions.txt), and its prince-bishops
                    // are written ecclesiastical (HistoryWriter.TitleGovernment).
                    if (religion.Ecclesiastical || religion.HasSees)
                    {
                        b.Field("theocracy_government_type", "ecclesiastical_government");
                        b.Field("theocracy_lease_contract_type", "ecclesiastical_lease");
                    }
                }

                if (!religion.Abrahamic) b.Field("pagan_roots", "yes");
                b.Blank();

                foreach (var (_, doctrine) in religion.Doctrines) b.Field("doctrine", doctrine);

                // Righteous both ways between the wilderness and everyone else: a doctrine can
                // only fix its own faith's view of others, so the marker sits on the unsettled
                // religion and the override on every settled one. See Faiths.SettledDoctrine.
                b.Field("doctrine", religion.Key == MapGen.Faiths.UnsettledReligionKey
                    ? MapGen.Faiths.UnsettledMarkerDoctrine
                    : MapGen.Faiths.SettledDoctrine);
                b.Blank();

                // Weighted as vanilla weights every religion's since 1.20: the weight is what a
                // virtue adds to, or a sin takes from, a holder's Spiritual Fulfillment, and an
                // unweighted trait counts 1 against vanilla's 20-30. The first of each list carries
                // the most, which is the commonest shape vanilla's own lists take.
                using (b.Block("traits"))
                {
                    WeightedTraits(b, "virtues", religion.Virtues);
                    WeightedTraits(b, "sins", religion.Sins);
                }

                b.Blank();
                b.Inline("custom_faith_icons", string.Join(' ', CustomFaithIcons(religion)));
                b.Blank();

                using (b.Block("localization"))
                    foreach (var (tag, value) in religion.Localization) b.Field(tag, value);
            }

            b.Blank();

            foreach (var faith in religion.Faiths)
                WriteFaith(faithFile, riteFile, religion, faith, faiths);
        }

        ParadoxText.WriteBom(Path.Combine(dir, "00_generated_religions.txt"), b.ToString());

        string faithDir = Path.Combine(root, "faith_types");
        string riteDir = Path.Combine(root, "rite_types");
        Directory.CreateDirectory(faithDir);
        Directory.CreateDirectory(riteDir);
        ParadoxText.WriteBom(Path.Combine(faithDir, "00_generated_faiths.txt"), faithFile.ToString());
        ParadoxText.WriteBom(Path.Combine(riteDir, "00_generated_rites.txt"), riteFile.ToString());
    }

    /// <summary>
    /// What an applied history's re-emit has to rewrite after <see cref="MapGen.Sees"/> were rebuilt
    /// on its world (ContentWriter.ApplyRealms): the religion, faith and rite files (the rites'
    /// founders and the ecclesiastical fields depend on the sees), their localisation, and every see
    /// declaration. Holy sites and icons are the generated world's and a history does not move them.
    /// Without this the title history named sees the landed titles no longer declared.
    /// </summary>
    internal static void WriteSeeDependent(string modDir, FaithMap faiths, int seed, bool tooltips)
    {
        WriteReligions(modDir, faiths);
        WriteFaithHistory(modDir, faiths);
        WriteLocalisation(modDir, faiths, seed, tooltips);
        SeeWriter.WriteAll(modDir, faiths);
    }

    private const string FaithHistoryFile = "00_generated_faiths.txt";

    /// <summary>
    /// <c>history/faiths</c>: each generated faith marked created, with its starting known, permitted
    /// and prohibited tenets (<see cref="TenetStatuses"/>). Dated 1.1.1 so it stands on every bookmark.
    /// A faith's own cores are left out even if an editor change made one of them a status here (the
    /// game ignores those anyway, with a warning). When no faith carries statuses — a world reopened
    /// from its files — the file on disk is left as it is rather than emptied.
    /// </summary>
    private static void WriteFaithHistory(string modDir, FaithMap faiths)
    {
        var decided = faiths.Faiths.Where(f => f.TenetStatus is not null).ToList();
        if (decided.Count == 0) return;

        var b = new JominiBuilder();
        b.Comment("Generated faiths' starting tenet statuses (CK3 1.20). See MapGen/Peoples/TenetStatuses.cs.");
        foreach (var faith in decided)
        {
            var s = faith.TenetStatus!;
            List<string> Clean(List<string> list) => list.Where(t => !faith.Tenets.Contains(t)).Distinct().ToList();
            var known = Clean(s.Known);
            var permitted = Clean(s.Permitted);
            var prohibited = Clean(s.Prohibited);

            b.Blank();
            using (b.Block(faith.Key))
            using (b.Block("1.1.1"))
            {
                b.Field("created", "yes");
                if (known.Count > 0) b.Inline("known", known.ToArray());
                if (permitted.Count > 0) b.Inline("permitted", permitted.ToArray());
                if (prohibited.Count > 0) b.Inline("prohibited", prohibited.ToArray());
            }
        }

        string dir = Path.Combine(modDir, "history", "faiths");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, FaithHistoryFile), b.ToString());
    }

    private static void WeightedTraits(JominiBuilder b, string field, IReadOnlyList<string> traits)
    {
        using (b.Block(field))
            for (int i = 0; i < traits.Count; i++)
                b.Inline(traits[i], "weight", "=", i == 0 ? "30" : "20", "scale", "=", "1");
    }

    /// <summary>
    /// One faith in <c>faith_types</c> and its main rite in <c>rite_types</c>.
    ///
    /// What the faith and its rite each hold follows the guidance in vanilla's <c>_faith_types.info</c>
    /// and <c>02_mainline_rite_types.txt</c>: the faith carries its details (religion, colour, icon,
    /// head), its holy sites and the doctrines intrinsic to it; the rite carries a copy of the
    /// colour, the core tenets and the same doctrines, so the effective faith is identical to the
    /// one 1.19 read from a single block.
    /// </summary>
    private static void WriteFaith(JominiBuilder b, JominiBuilder rites, Religion religion, Faith faith,
        FaithMap faiths)
    {
        var (r, g, bl) = faith.Color;
        var doctrines = new List<string>();
        string? reformedIcon = null;

        // Unreformed faiths require reformed_icon, otherwise Reformation/Holy Site view causes CTD
        if (!faith.IsOrganized)
        {
            doctrines.Add("unreformed_faith_doctrine");
            reformedIcon = FaithIcons.HasGeneratedIcon(faith) && faith.ReformedIcon is { } reformed
                ? reformed
                : faith.Icon;
        }

        string? head = null;
        if (faith.Head is not null && faith.IsOrganized)
        {
            // A temporal head is a landed ruler who is also the faith's head — see HistoryWriter,
            // which hands the title to one — and vanilla pairs it with no anointment, so the rite
            // below is left to the spiritual kind.
            doctrines.Add(faith.Head.Temporal ? "doctrine_temporal_head" : "doctrine_spiritual_head");
            head = faith.Head.TitleKey;

            // The anointment rite belongs beside the head that performs it.
            //
            // Its doctrine group is filled at *religion* level, where doctrine_head_of_faith is
            // pinned to doctrine_no_head — so the can_pick repair in Faiths.Build correctly rules the
            // two anointment doctrines out and every religion lands on doctrine_no_anointment. That
            // is right for the religion and wrong for the faiths overridden here: they have a head
            // of faith and inherited a rite that assumes there is none, which left
            // `crowned_emperor` unreachable on the whole map and the imperial branch of the
            // coronation dead.
            //
            // A faith- or rite-level doctrine overrides its religion's for the same group, so one
            // entry here reconciles them. The dominant faith of a religion gets the imperial rite —
            // it is the one whose head is expected to crown emperors, and it is what makes the
            // anointment option default-on at empire tier — and the rest get the plain permission.
            //
            // Guarded on the religion having drawn a coronation doctrine at all, which is this
            // generator's own record of whether the install it read defines the group: naming one
            // an older install has never heard of is a hard script error rather than a feature
            // quietly doing nothing.
            if (!faith.Head.Temporal && religion.Doctrines.ContainsKey("doctrine_coronation"))
                doctrines.Add(faith.IsDominant ? "doctrine_imperial_anointment" : "doctrine_anointment_permitted");
        }

        // Clerical regions: the parameter every clerical-region interaction tests. On the faith, and
        // restated on its rites below, since a rite's doctrines replace the faith's group by group.
        // Every faith that could hold sees, not only those that do on the start date: one whose
        // land is still tribal founds its first sees in play, as its lords settle, through vanilla's
        // Request Ecclesiastical Title Creation, and one whose sees come on a later bookmark needs
        // the doctrine on that bookmark too.
        if (faith.HasClericalRegions || faith.Sees.Count > 0 || faith.EraSees.Count > 0) doctrines.Add(ClericalRegionsDoctrine);

        // Electors: vanilla's own hidden doctrine, which is all vanilla's theocratic_elective law asks
        // of a faith besides its head holding the head-of-faith title. The sees are the elector
        // titles (HistoryWriter.WriteSees), so its archbishops elect the head. See the see-electors plan.
        if (faith.HasElectors) doctrines.Add(ElectorsDoctrine);

        // Sacraments follow the hierarchy, as in vanilla, where the one religion with sacraments
        // central (Christianity) is also the one with sees. Central is what lets its archbishops and
        // spiritual head excommunicate (excommunicate_interaction: clergy ruler of duke tier or more,
        // faith_has_sacraments_central_trigger) and its faithful seek indulgences. A faith-level
        // override of the religion's draw, like the anointment rite above; guarded on the religion
        // having drawn the group, which is this install declaring it.
        if (faith.HasClericalRegions && religion.Doctrines.TryGetValue("sacraments_group", out string? sacraments)
            && sacraments != SacramentsCentral)
            doctrines.Add(SacramentsCentral);

        var (eminent, ordinary) = SplitHolySites(faith);

        // Ensure every faith has at least one holy site. Fallback to avoid fatal error on empty
        // dummy faiths. From the whole map: in a world of vanilla faiths the only sites are theirs.
        if (eminent.Count == 0 && (faiths.Whole ?? faiths).Faiths.FirstOrDefault(f => f.HolySites.Count > 0) is { } donor)
            eminent.Add(donor.HolySites[0].Key);

        using (b.Block(faith.Key))
        {
            b.Field("main_rite", faith.Key);

            using (b.Block("faith_details"))
            {
                b.Field("religion", religion.Key);
                b.Inline("color", F(r), F(g), F(bl));
                b.Field("icon", faith.Icon);
                if (reformedIcon is not null) b.Field("reformed_icon", reformedIcon);
                if (head is not null) b.Field("religious_head", head);
            }

            b.Blank();
            if (eminent.Count > 0) b.Inline("eminent_holy_sites", string.Join(' ', eminent));
            if (ordinary.Count > 0) b.Inline("holy_sites", string.Join(' ', ordinary));
            b.Blank();

            b.Inline("tenets", string.Join(' ', faith.Tenets));
            if (doctrines.Count > 0) b.Inline("doctrines", string.Join(' ', doctrines));
        }

        b.Blank();

        var primate = faith.Sees.FirstOrDefault(s => s.Rank == SeeRank.Primate);

        using (rites.Block(faith.Key))
        {
            // Beside regional rites the main rite needs a name of its own, as Roman Rite is not
            // Chalcedonian Christianity; alone it keeps reading the faith's name by sharing its key.
            if (faith.Rites.Count > 0) rites.Field("name", MainRiteName(faith));
            rites.Field("faith", faith.Key);
            if (primate is not null) rites.Field("founder", primate.Key);
            rites.Blank();
            rites.Inline("color", F(r), F(g), F(bl));
            rites.Blank();
            rites.Inline("tenets", string.Join(' ', faith.Tenets));
            if (doctrines.Count > 0) rites.Inline("doctrines", string.Join(' ', doctrines));
        }

        rites.Blank();

        // Regional rites (MapGen/Peoples/Sees.cs): the faith's core with one tenet and one devotional
        // doctrine of their own, founded by a great see as vanilla's Ambrosian rite is by Milan.
        foreach (var rite in faith.Rites)
        {
            var (rr, rg, rb) = rite.Color;
            var riteDoctrines = doctrines.Concat(rite.DoctrineOverrides.Values).ToList();

            using (rites.Block(rite.Key))
            {
                rites.Field("faith", faith.Key);
                rites.Field("founder", rite.Founder.Key);
                rites.Blank();
                rites.Inline("color", F(rr), F(rg), F(rb));
                rites.Blank();
                rites.Inline("tenets", string.Join(' ', rite.Tenets));
                if (riteDoctrines.Count > 0) rites.Inline("doctrines", string.Join(' ', riteDoctrines));
            }

            rites.Blank();
        }
    }

    /// <summary>The hidden doctrine that carries <c>has_clerical_regions</c>; see BaseFilesToCopy/Core.</summary>
    internal const string ClericalRegionsDoctrine = "special_doctrine_gen_clerical_regions";

    private const string SacramentsCentral = "doctrine_sacraments_central";

    /// <summary>Vanilla's hidden doctrine carrying <c>has_clerical_electors</c> (40_doctrines_special.txt).</summary>
    internal const string ElectorsDoctrine = "special_doctrine_has_clerical_electors";

    /// <summary>The loc key a faith's main rite is named by once the faith has regional rites.</summary>
    private static string MainRiteName(Faith faith) => $"{faith.Key}_main_rite";

    /// <summary>
    /// The icons the game offers when a faith of this religion reforms or a new faith is founded
    /// from it — the picker lists this and nothing else, so without it the list is empty. The
    /// religion's own icons first, each faith's and the reformed one drawn for an unreformed
    /// faith, then vanilla's shared list, which every vanilla religion carries.
    ///
    /// <see cref="VanillaVocabulary.Current"/> for the same reason as
    /// <see cref="CultureWriter.HouseFrameFor"/>: the editor's rewrite has no vocabulary in hand.
    /// </summary>
    private static IEnumerable<string> CustomFaithIcons(Religion religion)
    {
        var icons = new List<string>();
        foreach (var faith in religion.Faiths)
        {
            icons.Add(faith.Icon);
            if (!faith.IsOrganized && FaithIcons.HasGeneratedIcon(faith) && faith.ReformedIcon is { } reformed)
                icons.Add(reformed);
        }

        if (VanillaVocabulary.Current is { } vocab) icons.AddRange(vocab.CustomFaithIcons);
        return icons.Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Not private: holy site names read <c>county.Name</c> off the live title, so renaming a
    /// county after the write means re-running exactly this. See <see cref="WorldOverwrite"/>.
    /// </summary>
    internal static void WriteLocalisation(string modDir, FaithMap faiths, int seed, bool tooltips = true)
    {
        string dir = Path.Combine(modDir, "localization", "english");
        Directory.CreateDirectory(dir);

        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

        // The gods, the devil and the clergy words, as links to their glosses. Written through
        // AddBuilt below: the values are concept markup, not text for ParadoxText.Loc to escape.
        var concepts = tooltips ? new ConceptTooltips() : null;
        var glossed = concepts is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ReligionGlossary.Gloss(faiths, concepts);
        ReligionGlossary.Write(modDir, concepts);

        foreach (var religion in faiths.Religions)
        {
            entries[religion.Key] = religion.Name;
            entries[$"{religion.Key}_adj"] = religion.Name;
            entries[$"{religion.Key}_adherent"] = religion.Name;
            entries[$"{religion.Key}_adherent_plural"] = Language.Plural(religion.Name);
            entries[$"{religion.Key}_desc"] = FaithDescriptions.For(religion, seed);

            foreach (var (key, value) in religion.LocalizationText) entries[key] = value;
        }

        foreach (var faith in faiths.Faiths)
        {
            entries[faith.Key] = faith.Name;
            entries[$"{faith.Key}_adj"] = faith.Name;
            entries[$"{faith.Key}_adherent"] = faith.Name;
            entries[$"{faith.Key}_adherent_plural"] = Language.Plural(faith.Name);
            entries[$"{faith.Key}_desc"] = FaithDescriptions.For(faith, seed);

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

            // Rites follow vanilla's pattern (roman_rite: "Roman Rite", adjective "Roman Rite",
            // adherent "Roman Christian"): the adherent is the rite's adjective and the faith's word.
            if (faith.MainRiteAdjective is { } mainAdj && faith.Sees.FirstOrDefault(s => s.Rank == SeeRank.Primate) is { } primate)
                RiteEntries(entries, MainRiteName(faith), $"{mainAdj} Rite", mainAdj, faith,
                    $"The {mainAdj} Rite is the {faith.Name} faith as kept at {primate.Seat.Name}, the see of its "
                    + "head, and the measure the faith's other rites are held against.");

            foreach (var rite in faith.Rites)
                RiteEntries(entries, rite.Key, rite.Name, rite.Adjective, faith, rite.FromSaint
                    ? $"{rite.Name} keeps the teaching of a holy founder of the see of {rite.Founder.Seat.Name}, "
                      + $"and differs from the {faith.Name} of the head's see in the practices it holds most dear."
                    : $"The {rite.Name} is the {faith.Name} faith as kept in the see of {rite.Founder.Seat.Name} "
                      + "and the lands around it, with customs of its own.");

            foreach (var (key, county) in faith.HolySites)
            {
                entries[$"holy_site_{key}_name"] = county.Name;
                entries[$"holy_site_{key}_effect_name"] = $"From [holy_site|E] #weak ($holy_site_{key}_name$)#!";
            }
        }

        var loc = new LocFile();
        foreach (var (key, value) in entries)
        {
            if (glossed.TryGetValue(key, out var link)) loc.AddBuilt(key, link);
            else loc.Add(key, value);
        }

        loc.Write(Path.Combine(dir, "gen_faiths_l_english.yml"));
    }

    private static void RiteEntries(IDictionary<string, string> entries, string key, string name, string adjective,
        Faith faith, string description)
    {
        entries[key] = name;
        entries[$"{key}_adj"] = adjective;
        entries[$"{key}_adherent"] = $"{adjective} {faith.Name}";
        entries[$"{key}_adherent_plural"] = $"{adjective} {Language.Plural(faith.Name)}";
        entries[$"{key}_desc"] = description;
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}