namespace Ck3MapGen.Emit;

using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;
using System.IO;

/// <summary>
/// Forges weapons out of harvested parts and puts them in the mod behind a test decision.
///
/// **Deliberately a cul-de-sac.** Procedural mesh assembly is new and may not survive, so nothing
/// else in the pipeline depends on it: forged weapons are *not* mixed into the artifact pool that
/// <see cref="MapGen.ArtifactMap"/> hands out, and <see cref="WeaponAssets"/> does not know they
/// exist. Everything this feature emits comes from <c>Emit/ArtifactForge/</c>, so removing it means
/// deleting that folder and its two calls in <see cref="ContentWriter"/>. What is left behind is
/// <c>Io/PdxMesh.cs</c> and <c>MapGen/WeaponForge.cs</c>, which stay put deliberately: the first is
/// the <c>.mesh</c> format itself and will be wanted for map objects, the second is mesh assembly
/// with no weapon-specific knowledge in it. Both are libraries, not part of this feature.
///
/// Keeping them out of the artifact pool is also what makes them testable. A forged weapon dropped
/// into the general pool would land on one ruler somewhere among fifty artifacts, and confirming it
/// rendered would mean hunting for it. Instead each one gets a decision that mints it, equips it,
/// and forces it to show — so the loop is: take decision, look at portrait.
///
/// The step is a no-op when the parts library is absent, so a checkout without
/// <c>assets/weaponparts/</c> still generates a complete map.
/// </summary>
public static class WeaponForgeStep
{
    /// <summary>
    /// The parts library, one file per weapon type.
    ///
    /// Deliberately not a fallback chain any more. Earlier libraries were kept as fallbacks so a
    /// checkout with an older cut still forged, but that turned into a trap once recolouring
    /// existed: a library without UV2 would still assemble weapons and then pattern them against a
    /// UV set that is not there, which is a silent visual fault rather than a missing feature.
    /// One canonical file, and a capability check on what it contains, is the honest arrangement.
    /// </summary>
    public static readonly string[] PartsRelPaths =
    [
        "weaponparts/sword_parts.mesh",
    ];

    /// <summary>
    /// One parts library per weapon kind. The **file** declares the kind, which is why families
    /// inside it need no type prefix in their names.
    ///
    /// A kind listed here whose file is absent is simply skipped; a kind whose file exists but
    /// yields no usable family is reported, because that means the cut and the code disagree and
    /// silence would read as "no library".
    /// </summary>
    public static readonly (string Kind, string RelPath, string Icon)[] PartsLibraries =
    [
        ("sword",  "weaponparts/sword_parts.mesh",  "artifact_sword.dds"),
        ("dagger", "weaponparts/dagger_parts.mesh", "artifact_dagger.dds"),
        ("axe",    "weaponparts/axe_parts.mesh",    "artifact_axe.dds"),
        ("mace",   "weaponparts/mace_parts.mesh",   "artifact_mace.dds"),
        ("spear",  "weaponparts/spear_parts.mesh",  "artifact_spear.dds"),
    ];

    /// <summary>
    /// Families whose parts may only ever be combined with their own, in either role.
    ///
    /// This is the escape hatch for a part that is *correct on its own terms* and simply cannot host
    /// a foreign neighbour — not for one that merely looks unusual, and not a substitute for fixing
    /// a bad cut in Blender.
    ///
    /// <c>ep1_indian_mace_01_a</c> earns it: its haft is a cone that widens toward the head socket,
    /// measuring <b>4.25</b> units of radius at the join against a library median of <b>1.68</b>. Any
    /// foreign head is bored for a shaft half that thick, so it sits on the cone visibly floating
    /// rather than seated. Its own head is cut to match, so the family stays in the pool and simply
    /// only ever forges as a pure weapon.
    ///
    /// The measurement that finds these: haft radius sampled within 3 units of its own
    /// <c>socket_head</c>, compared against the rest of the library. A family more than about twice
    /// the median is worth looking at in Blender before it is listed here.
    /// </summary>
    private static readonly HashSet<string> SelfOnlyFamilies =
        new(StringComparer.OrdinalIgnoreCase) { "ep1_indian_mace_01_a" };

    /// <summary>
    /// Lead families too ostentatious for an ordinary weapon, reserved for the rarest bands.
    ///
    /// Deliberately **empty for now**. The mechanism is here because the judgement it encodes is
    /// about art rather than code — a blade either looks like a king's or it does not — and the
    /// answer will arrive with the blades, not with a rewrite. Adding a name is the whole change.
    ///
    /// It narrows where a family appears without costing a pairing: <see cref="BuildCatalogue"/>
    /// sorts these to the back of their kind, and bands are handed out in ascending order, so a
    /// fancy blade takes the top bands first and spills down through famed if there are more of them
    /// than top-band slots. Nothing is ever dropped.
    ///
    /// Distinct from <see cref="SelfOnlyFamilies"/>, which is about geometry that cannot mate. This
    /// is about geometry that mates perfectly well and should simply be rarer.
    /// </summary>
    private static readonly HashSet<string> FancyLeadFamilies =
        new(StringComparer.OrdinalIgnoreCase) { };

    /// <summary>
    /// Whether a lead/base pair may be forged. A self-only family is allowed opposite itself and
    /// nothing else, which is symmetric: it can neither donate to nor borrow from a stranger.
    /// </summary>
    private static bool MayCombine(string lead, string baseFamily)
        => string.Equals(lead, baseFamily, StringComparison.OrdinalIgnoreCase)
            || (!SelfOnlyFamilies.Contains(lead) && !SelfOnlyFamilies.Contains(baseFamily));

    /// <summary>
    /// The families from <paramref name="bases"/> that may supply the body for
    /// <paramref name="lead"/>.
    ///
    /// Can be empty, and the caller must handle that: a self-only family is allowed opposite itself
    /// alone, so one that donates a lead but has no body of its own — a blade cut from a weapon
    /// whose hilt was not kept — has nothing it may pair with. Returning the lead unconditionally
    /// would hand back a family that cannot fill the remaining slots.
    /// </summary>
    private static List<string> Compatible(IReadOnlyList<string> bases, string lead)
        => SelfOnlyFamilies.Contains(lead)
            ? [.. bases.Where(f => string.Equals(f, lead, StringComparison.OrdinalIgnoreCase))]
            : [.. bases.Where(f => !SelfOnlyFamilies.Contains(f))];

    /// <summary>
    /// How many icons are rendered at once.
    ///
    /// Capped well below the core count because each render is memory-hungry rather than long: a
    /// 960x960 supersampled pass holds depth, diffuse, specular and rim buffers plus the composite,
    /// which is roughly 65 MB while it runs. Letting sixteen of those go at once would ask for a
    /// gigabyte to save a second or two on work that is already a small share of generation.
    ///
    /// Results are collected into a pre-sized array by index, so parallelism cannot reorder the
    /// catalogue rows or the decision list — both are read back in order afterwards.
    /// </summary>
    private static readonly ParallelOptions IconParallel =
        new() { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 6) };

    /// <summary>
    /// Fewest looks a kind may be asked for, whatever the config says.
    ///
    /// A pool of zero would forge nothing and leave the artifact map pointing at looks that do not
    /// exist, so the setting is clamped rather than trusted.
    /// </summary>
    private const int MinPoolSizePerKind = 1;

    /// <summary>
    /// How a pool is split across the four rarity bands, weighted by how many artifacts of each
    /// band a world actually contains.
    ///
    /// Measured on seed 4242 over a 4096x2048 map: <b>26 common, 15 masterwork, 6 famed, 2
    /// illustrious</b> out of 49 artifacts. Weighting by that rather than splitting evenly puts the
    /// looks where they will be seen — but the more useful measurement is the other one from that
    /// run: the whole world held **20 weapon artifacts across five kinds** (3 swords, 3 daggers,
    /// 4 axes, 4 maces, 6 spears). A pool of eight per kind was never close to being exhausted, so
    /// reserving five of those eight for the upper bands costs a world nothing in repetition and
    /// buys every band a look of its own.
    /// </summary>
    private static readonly int[] BandWeights = [26, 15, 6, 2];

    /// <summary>
    /// Which band each look in a pool of <paramref name="count"/> is forged for, in band order.
    ///
    /// Scales with the pool size rather than assuming one: every band gets at least one look as
    /// soon as there are bands to go round, and the surplus is shared out by
    /// <see cref="BandWeights"/> using largest-remainder, so 8 becomes 3/2/2/1 and 16 becomes
    /// 7/5/2/2 without either being written down anywhere.
    ///
    /// Below four looks the bands cannot all be covered, and which ones to drop is a judgement:
    /// common is kept because it is most of every world, illustrious because it is the one a player
    /// stops to look at, and the middle two are given up first. The bands left uncovered are not
    /// left unserved — <see cref="WeaponAssets.AtTier"/> walks outward to the nearest one that
    /// exists.
    /// </summary>
    private static ArtifactRarity[] TierPlan(int count)
    {
        if (count <= 0) return [];

        int bands = WeaponAssets.BandCount;

        if (count < bands)
        {
            ArtifactRarity[] priority =
            [
                ArtifactRarity.Common,
                ArtifactRarity.Illustrious,
                ArtifactRarity.Famed,
                ArtifactRarity.Masterwork,
            ];

            return [.. priority.Take(count).OrderBy(t => (int)t)];
        }

        var counts = new int[bands];
        var fraction = new double[bands];
        int surplus = count - bands;
        int totalWeight = BandWeights.Sum();

        for (int i = 0; i < bands; i++)
        {
            double share = surplus * (double)BandWeights[i] / totalWeight;
            counts[i] = 1 + (int)share;
            fraction[i] = share - (int)share;
        }

        for (int placed = counts.Sum(); placed < count; placed++)
        {
            int best = 0;
            for (int i = 1; i < bands; i++)
            {
                if (fraction[i] > fraction[best]) best = i;
            }

            counts[best]++;
            fraction[best] = -1;
        }

        var plan = new List<ArtifactRarity>(count);

        for (int i = 0; i < bands; i++)
        {
            for (int n = 0; n < counts[i]; n++) plan.Add((ArtifactRarity)i);
        }

        return [.. plan];
    }

    /// <summary>
    /// Forges the world's pool of sword looks and returns catalogue rows for them.
    ///
    /// This is the step that takes procedural weapons out of the test cul-de-sac: the rows returned
    /// here replace the vanilla sword entries in <see cref="WeaponAssets"/> for this world, so an
    /// ordinary generated sword artifact wears a mesh that has never existed before. The meshes and
    /// their <c>.asset</c> files are written here too; only the artifact visual is left to
    /// <see cref="ArtifactWriter.WriteVisuals"/>, which owns that file.
    ///
    /// Returns an empty list when there is no parts library, or when no family in it has a full set
    /// of textured parts — and an empty list is a *supported* answer, not a failure. The caller
    /// falls back to the vanilla catalogue, so a checkout with no <c>assets/weaponparts/</c> still
    /// generates a complete world with ordinary swords in it.
    /// </summary>
    /// <summary>
    /// Builds every pairing the libraries can make, as shared pieces plus one entity each.
    ///
    /// **This is the composed replacement for <see cref="ForgeWeaponPools"/>**, and the difference
    /// is what a pairing costs. The pool path merges each weapon into its own <c>.mesh</c>, so the
    /// catalogue can only be as large as the binary art you are willing to write; here the base
    /// assembly and the lead are each written once and a pairing is a few lines of text, so the
    /// catalogue is every combination the libraries admit — 861 of them on the current cut, from 115
    /// meshes.
    ///
    /// **Colour is not applied here yet.** Every piece goes out on the plain shader with its source
    /// textures, deliberately: composed geometry and procedural recolour failed together during the
    /// attach probes in ways that looked identical, and separating them is the only way a fault in
    /// one is not read as a fault in the other. The recolour belongs on the base entity, which is
    /// the root and the only part that can carry it.
    ///
    /// Returns an empty list when there is no parts library, which is a supported answer: the caller
    /// falls back to the vanilla catalogue.
    /// </summary>
    /// <summary>
    /// The composed catalogue, plus what a later pass needs to draw icons for part of it.
    ///
    /// Icons cannot be rendered while the catalogue is built, because which pairings deserve one
    /// depends on which the world actually hands out and at what rarity — and that is decided by
    /// <see cref="MapGen.ArtifactMap"/>, which runs afterwards and needs the catalogue to run at
    /// all. So the pieces are carried forward rather than rebuilt.
    /// </summary>
    public sealed record ComposedCatalogue(
        IReadOnlyList<WeaponAsset> Looks,
        IReadOnlyList<ComposedKind> Kinds,
        IReadOnlyList<ComposedLook> Pairings,
        ForgedRecolour? Recolour);

    /// <summary>
    /// The pseudo-weapon a base family's finish is worked out under, one per rarity band.
    ///
    /// The recolour machinery takes a set of parts and returns a mask plus a palette, and a base
    /// assembly is a set of parts — so it transfers whole, with the base standing in for a weapon.
    /// One entry per band because the finish is *keyed on* the band: an illustrious weapon's
    /// fittings come out gilded and a common one's plain, which is the thematic payoff of the
    /// palette landing on the fittings rather than the blade.
    ///
    /// The mask that comes back is identical across the four bands of one family — same parts, same
    /// UV layout — so three of every four are redundant. They are small, and deduplicating them
    /// would mean teaching the recolour to separate mask from palette, which is a change to working
    /// code for a few hundred kilobytes.
    /// </summary>
    public static string BaseLookName(string family, ArtifactRarity tier)
        => $"{ComposedWeaponWriter.BaseMeshName(family)}_{tier.ToString().ToLowerInvariant()}";

    public static ComposedCatalogue ComposeWeaponCatalogue(
        string modDir, string gameDir, Rng rng)
    {
        var kinds = new List<ComposedKind>();
        var partsDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materials = new List<ForgedMaterial>();
        var allParts = new List<WeaponPart>();
        var icons = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (kind, relPath, icon) in PartsLibraries)
        {
            string? path = Locate(relPath);
            if (path is null) continue;

            if (Path.GetDirectoryName(path) is { } dir) partsDirs.Add(dir);

            icons[kind] = icon;
            var schema = WeaponSchema.For(kind);
            var parts = WeaponForge.LoadParts(path, schema);
            allParts.AddRange(parts);

            // Same classification the pool path uses: a lead needs only its own slot, since a blade
            // cut from a weapon whose hilt was not kept is still a perfectly good blade; a base needs
            // every other slot, because it supplies all of them.
            var nonLead = schema.Required.Where(s => s != schema.Lead).ToList();

            var families = parts
                .Where(p => p.HasTextures)
                .GroupBy(p => p.Family)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var bases = new List<(string, WeaponBase)>();
            var leads = new List<(string, WeaponPiece, WeaponPart)>();

            foreach (var family in families)
            {
                if (nonLead.All(s => family.Any(p => p.Slot == s)))
                {
                    var built = WeaponForge.BuildBase(
                        ComposedWeaponWriter.BaseMeshName(family.Key),
                        [.. family.Where(p => p.Slot != schema.Lead)], schema);

                    // A base that cannot seat a lead would emit a locator at the origin and hang the
                    // blade through the grip, so it is dropped and named rather than shipped.
                    if (built.LeadMountable) bases.Add((family.Key, built));
                    else Console.WriteLine($"  composed weapons: {family.Key} has no socket for a "
                        + $"{schema.Lead.ToString().ToLowerInvariant()} - dropped as a base");
                }

                if (family.FirstOrDefault(p => p.Slot == schema.Lead) is { } leadPart)
                {
                    var built = WeaponForge.BuildLead(
                        ComposedWeaponWriter.LeadMeshName(family.Key), leadPart, schema);

                    if (built is not null) leads.Add((family.Key, built, leadPart));
                    else Console.WriteLine($"  composed weapons: {family.Key} "
                        + $"{schema.Lead.ToString().ToLowerInvariant()} carries no socket toward its "
                        + "mount - dropped as a lead");
                }
            }

            if (bases.Count == 0 || leads.Count == 0)
            {
                Console.WriteLine($"  composed weapons: {kind} has no forgeable pairing - skipped");
                continue;
            }

            materials.AddRange(bases.SelectMany(b => b.Item2.Piece.Materials));
            materials.AddRange(leads.SelectMany(l => l.Item2.Materials));
            kinds.Add(new ComposedKind(kind, bases, leads));
        }

        if (kinds.Count == 0) return new ComposedCatalogue([], [], [], null);

        // Pairings first, because the tier plan is what decides which finish each one wears, and the
        // finishes cannot be written until the bands are known.
        var pairings = ComposedWeaponWriter.Plan(kinds, MayCombine);
        var catalogue = BuildCatalogue(pairings, icons);
        var tierOf = catalogue.ToDictionary(
            r => r.VisualKey[..^"_visuals".Length], r => r.Tier ?? ArtifactRarity.Common,
            StringComparer.Ordinal);

        var recolour = Recolour(modDir, kinds, rng);
        var looks = ComposedWeaponWriter.WriteAll(
            modDir, kinds, MayCombine, tierOf, recolour, BaseLookName);
        ForgedWeaponTextures.Ship(modDir, gameDir, partsDirs, materials, allParts);

        int meshes = kinds.Sum(k => k.Bases.Count + k.Leads.Count);

        Console.WriteLine($"  composed weapons: {looks.Count} pairings from {meshes} shared meshes "
            + $"across {kinds.Count} kind(s)");

        return new ComposedCatalogue(catalogue, kinds, pairings, recolour);
    }

    /// <summary>
    /// Finishes the handful of weapons a world actually hands out: a merged, fully recoloured mesh
    /// for the rarest, and a rendered icon for a slightly wider set. Returns the catalogue with
    /// those rows repointed.
    ///
    /// **Why the rarest get a different kind of geometry.** A composed weapon's blade is an attached
    /// child, and an attached child never receives a <c>portrait_accessory</c> binding — so the
    /// palette reaches the fittings and stops. Merging the whole weapon into one mesh makes it a
    /// single root, and the recolour then covers the blade too. That is the old pool path, and it is
    /// affordable here for exactly the reason the icons are: a world places 23 forged weapons and
    /// only a couple at famed or better, so a handful of extra meshes buys every hero weapon in the
    /// world a finish the other 843 pairings cannot have.
    ///
    /// Both passes share the assembly, because both need the same thing — the whole weapon in one
    /// piece — and assembling it twice would be the only cost of keeping them apart.
    /// </summary>
    /// <param name="merge">Rarity at which a weapon earns a merged, fully recoloured mesh.</param>
    /// <param name="draw">Rarity at which it earns a rendered icon. Expected to be the looser of the
    /// two, so every merged weapon also gets an icon drawn from its own colours.</param>
    public static IReadOnlyList<WeaponAsset> FinishTopArtifacts(
        string modDir, string gameDir, ComposedCatalogue catalogue,
        IEnumerable<(string Visuals, ArtifactRarity Rarity)> placed,
        ArtifactRarity merge, ArtifactRarity draw, Rng rng)
    {
        if (catalogue.Recolour is not { } recolour || catalogue.Looks.Count == 0)
            return catalogue.Looks;

        var rarest = new Dictionary<string, ArtifactRarity>(StringComparer.Ordinal);

        foreach (var (visuals, rarity) in placed)
        {
            if (rarity < draw) continue;

            // A visual can be handed out more than once, so the best band it ever reached is what
            // decides its treatment. Otherwise a look that is illustrious on one ruler could be
            // demoted by also being masterwork on another.
            rarest[visuals] = rarest.TryGetValue(visuals, out var seen) && seen > rarity
                ? seen
                : rarity;
        }

        if (rarest.Count == 0) return catalogue.Looks;

        var byKey = catalogue.Pairings.ToDictionary(p => p.VisualKey, StringComparer.Ordinal);
        var tierOf = catalogue.Looks.ToDictionary(
            r => r.VisualKey, r => r.Tier ?? ArtifactRarity.Common, StringComparer.Ordinal);
        var byKind = catalogue.Kinds.ToDictionary(k => k.Kind, k => k, StringComparer.Ordinal);

        // Assembled once and used by both passes, because both want the same thing: the whole
        // weapon in one piece.
        var built = new List<(string Key, ComposedLook Look, ForgedWeapon Weapon, WeaponBase Base)>();

        foreach (string key in rarest.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!byKey.TryGetValue(key, out var look)) continue;
            if (!byKind.TryGetValue(look.Kind, out var kind)) continue;

            var schema = WeaponSchema.For(look.Kind);
            var baseBuilt = kind.Bases.FirstOrDefault(b => b.Family == look.BaseFamily).Built;
            var lead = FindLead(kind, look.LeadFamily);
            if (baseBuilt is null || lead is null) continue;

            var parts = new List<WeaponPart>(baseBuilt.Parts) { lead };
            string name = HeroName(look.Kind, built.Count);

            Console.WriteLine($"  composed weapons: {name} <- {look.LeadFamily} on {look.BaseFamily} "
                + $"({rarest[key].ToString().ToLowerInvariant()})");

            built.Add((key, look, WeaponForge.Assemble(name, parts, schema), baseBuilt));
        }

        // ---- merged heroes ------------------------------------------------------------------
        var heroes = built.Where(x => rarest[x.Key] >= merge).ToList();
        ForgedRecolour? heroColour = null;

        if (heroes.Count > 0)
        {
            var made = heroes.Select(h => h.Weapon).ToList();
            var tiers = heroes.ToDictionary(
                h => h.Weapon.Name, h => rarest[h.Key], StringComparer.Ordinal);

            heroColour = ForgedWeaponRecolour.Write(modDir, made, rng, "hero", tiers);

            if (heroColour is not null) ForgedWeaponWriter.WriteAll(modDir, made, heroColour);
        }

        var entity = new Dictionary<string, string>(StringComparer.Ordinal);

        if (heroColour is not null)
        {
            foreach (var hero in heroes)
                entity[hero.Key] = ForgedWeaponWriter.EntityName(hero.Weapon.Name);
        }

        // ---- icons --------------------------------------------------------------------------
        var drawn = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, look, weapon, baseBuilt) in built)
        {
            // A hero's colours cover every part including the blade, which is the whole point of
            // merging it. Anything else falls back to the base's finish plus a sampled lead.
            var colours = heroColour is not null
                    && heroColour.PartColour.TryGetValue(weapon.Name, out var full)
                ? [.. full]
                : ColoursFor(weapon, baseBuilt, recolour,
                    BaseLookName(look.BaseFamily, tierOf.GetValueOrDefault(key, ArtifactRarity.Common)),
                    gameDir);

            if (ForgedWeaponRender.Write(
                    modDir, gameDir, weapon, WeaponSchema.For(look.Kind), look.Kind, colours)
                is { } file)
            {
                drawn[key] = file;
            }
        }

        Console.WriteLine($"  composed weapons: {entity.Count} merged and fully recoloured at "
            + $"{merge.ToString().ToLowerInvariant()}+, {drawn.Count} icon(s) at "
            + $"{draw.ToString().ToLowerInvariant()}+, {catalogue.Looks.Count - drawn.Count} on stock art");

        return [.. catalogue.Looks.Select(r => r with
        {
            Icon = drawn.GetValueOrDefault(r.VisualKey, r.Icon),
            Entity = entity.GetValueOrDefault(r.VisualKey, r.Entity),
        })];
    }

    /// <summary>
    /// Name for a merged hero weapon: short, and numbered rather than descriptive.
    ///
    /// Distinct from its pairing so both can exist side by side — the composed pairing stays in the
    /// catalogue for anything the override path rolls onto an ordinary weapon, and only the artifact
    /// that earned it points at the merged mesh.
    ///
    /// **Numbered because a <c>.mesh</c> node name may not reach 64 characters** (<c>PdxMesh</c>
    /// line 202), and a pairing name is two family names joined. The first cut appended
    /// <c>_hero</c> to the pairing and produced <c>gen_wpn_ep2_northern_mace_01_b__
    /// ep2_northern_mace_01_b_heroShape</c> — exactly 64, and the write threw. The index is stable
    /// because the pairings are walked in sorted key order, and the mapping is printed as it is
    /// assigned so a file on disk can still be traced back to the families it came from.
    /// </summary>
    private static string HeroName(string kind, int index) => $"gen_hero_{kind}_{index:00}";

    /// <summary>
    /// The lead part of one family, or null when this kind's library has none.
    ///
    /// Read off <see cref="ComposedKind.Leads"/> and not off a base's parts: a base is built by
    /// excluding the lead, so searching there can only ever return nothing. It did, silently, and
    /// the first cut of the icon pass rendered zero without failing.
    /// </summary>
    private static WeaponPart? FindLead(ComposedKind kind, string family)
    {
        foreach (var (name, _, part) in kind.Leads)
            if (string.Equals(name, family, StringComparison.Ordinal)) return part;

        return null;
    }

    /// <summary>
    /// A colour per part, in the order <see cref="ForgedWeaponRender"/> walks them.
    ///
    /// The base's parts take the finish they were given. The lead has none — it is an attached child
    /// and keeps the textures it was cut with — so its colour is sampled from its own diffuse
    /// instead, which is what the model will actually look like. Guessing a neutral steel here would
    /// draw a bronze axe head silver.
    /// </summary>
    private static List<(byte R, byte G, byte B)> ColoursFor(
        ForgedWeapon weapon, WeaponBase built, ForgedRecolour recolour, string look, string gameDir)
    {
        var baseColours = recolour.PartColour.TryGetValue(look, out var list) ? list : [];
        var colours = new List<(byte, byte, byte)>(weapon.Parts.Count);

        foreach (var part in weapon.Parts)
        {
            int index = -1;

            for (int i = 0; i < built.Parts.Count; i++)
                if (ReferenceEquals(built.Parts[i], part)) index = i;

            colours.Add(index >= 0 && index < baseColours.Count
                ? baseColours[index]
                : ForgedWeaponTextures.AverageColour(gameDir, part.Diffuse)
                    ?? ((byte)150, (byte)150, (byte)155));
        }

        return colours;
    }

    /// <summary>
    /// Works out a finish for every base family in every rarity band, or null when the library
    /// cannot be patterned at all.
    ///
    /// **Only the base is recoloured, and that is forced rather than chosen.** The pairing root is
    /// the base assembly, and an attached child receives no <c>portrait_accessory</c> binding, so
    /// the lead cannot carry a palette however it is declared. It keeps the textures it was cut
    /// with — which for a blade or an axe head means steel, and reads correctly.
    /// </summary>
    private static ForgedRecolour? Recolour(
        string modDir, IReadOnlyList<ComposedKind> kinds, Rng rng)
    {
        var stand = new List<ForgedWeapon>();
        var tiers = new Dictionary<string, ArtifactRarity>(StringComparer.Ordinal);

        foreach (var kind in kinds)
        {
            var schema = WeaponSchema.For(kind.Kind);

            foreach (var (family, built) in kind.Bases)
            {
                foreach (var tier in Enum.GetValues<ArtifactRarity>())
                {
                    string look = BaseLookName(family, tier);

                    stand.Add(new ForgedWeapon(
                        look, built.Piece.ShapeName, built.Piece.Root, built.Piece.Materials,
                        built.Parts));

                    tiers[look] = tier;
                }
            }
        }

        if (ForgedWeaponRecolour.Write(modDir, stand, rng, "composed", tiers) is not { } raw)
            return null;

        return ShareMasks(modDir, kinds, raw);
    }

    /// <summary>
    /// Points all four bands of a base family at one mask file and removes the duplicates.
    ///
    /// A mask is derived from geometry — which parts overlap in UV0 — so the four bands of one
    /// family produce **byte-identical** files. Measured before this existed: 232 masks at 256 KB
    /// each, 58 MB, of which three quarters were copies. Sharing takes it to 14.5 MB and changes
    /// nothing on screen, because only the variation differs between bands.
    ///
    /// The duplicates are written and then deleted rather than never written. The recolour owns mask
    /// generation and emits one per entry; teaching it to share a mask between entries means
    /// separating mask from palette inside 800 lines of working code, for a saving this gets
    /// directly. The write is a few hundred milliseconds of a ten-second run.
    /// </summary>
    private static ForgedRecolour ShareMasks(
        string modDir, IReadOnlyList<ComposedKind> kinds, ForgedRecolour raw)
    {
        var shared = new Dictionary<string, string>(StringComparer.Ordinal);
        int removed = 0;

        foreach (var kind in kinds)
        {
            foreach (var (family, _) in kind.Bases)
            {
                string keep = raw.MaskFor(BaseLookName(family, ArtifactRarity.Common));

                foreach (var tier in Enum.GetValues<ArtifactRarity>())
                {
                    string look = BaseLookName(family, tier);
                    string own = raw.MaskFor(look);
                    shared[look] = keep;

                    if (string.Equals(own, keep, StringComparison.Ordinal)) continue;

                    string path = Path.Combine(modDir, own.Replace('/', Path.DirectorySeparatorChar));

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed++;
                    }
                }
            }
        }

        Console.WriteLine($"  composed weapons: {shared.Count - removed} mask(s) shared across "
            + $"{WeaponAssets.BandCount} bands, {removed} duplicate(s) removed");

        return raw with { MaskByWeapon = shared };
    }

    /// <summary>
    /// Catalogue rows for every pairing, tier-banded per kind.
    ///
    /// Banding is per kind rather than across the whole catalogue so each kind keeps a full spread —
    /// otherwise a kind with many pairings would monopolise the rare bands and a small one would sit
    /// entirely in the common band, which is what <see cref="WeaponAssets.AtTier"/>'s outward walk
    /// then has to paper over.
    ///
    /// The icon is the kind's stock one for now. That is the piece composition does not shrink: an
    /// icon belongs to a pairing, so rendering them is the one cost that still scales with the
    /// product. Gating the icon on the lead alone would cut it to one per blade, and a visual's
    /// <c>icon</c> is a separate trigger list from its <c>asset</c>, so that is available whenever
    /// the render cost is worth paying.
    /// </summary>
    private static List<WeaponAsset> BuildCatalogue(
        IReadOnlyList<ComposedLook> looks, IReadOnlyDictionary<string, string> icons)
    {
        var rows = new List<WeaponAsset>(looks.Count);

        foreach (var byKind in looks.GroupBy(l => l.Kind))
        {
            // Fancy leads to the back of the queue, because TierPlan hands out bands in ascending
            // order and the last entries are therefore the rarest. A blade too ostentatious for a
            // common weapon lands on an illustrious one instead, and the surplus spills down through
            // famed rather than being dropped — so marking a family fancy narrows where it appears
            // without ever costing a pairing.
            //
            // OrderBy is stable, so within each group the pairings keep the order Plan produced and
            // a base family's run stays contiguous.
            var ordered = byKind
                .OrderBy(l => FancyLeadFamilies.Contains(l.LeadFamily) ? 1 : 0)
                .ToList();

            var plan = TierPlan(ordered.Count);

            for (int i = 0; i < ordered.Count; i++)
            {
                var look = ordered[i];

                rows.Add(new WeaponAsset(
                    look.VisualKey, look.Kind, look.EntityName,
                    icons.GetValueOrDefault(look.Kind, "artifact_sword.dds"), plan[i]));
            }
        }

        return rows;
    }

    public static IReadOnlyList<WeaponAsset> ForgeWeaponPools(
        string modDir, string gameDir, Rng rng, int poolSizePerKind)
    {
        int poolSize = Math.Max(MinPoolSizePerKind, poolSizePerKind);
        var built = new List<(ForgedWeapon Weapon, string Kind, string StockIcon, ArtifactRarity Tier)>();

        // Where each library was actually resolved from, so ForgedWeaponTextures probes the same
        // checkout rather than guessing at it a second time.
        var partsDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (kind, relPath, icon) in PartsLibraries)
        {
            string? path = Locate(relPath);
            if (path is null) continue;

            if (Path.GetDirectoryName(path) is { } dir) partsDirs.Add(dir);

            var schema = WeaponSchema.For(kind);
            built.AddRange(ForgeOneKind(kind, icon, WeaponForge.LoadParts(path, schema), schema, rng, poolSize));
        }

        if (built.Count == 0) return [];

        // Order matters: colours are decided first, because a weapon's icon is a tinted copy of the
        // stock one and cannot be written until its colour is known.
        var forged = built.Select(b => b.Weapon).ToList();
        var tiers = built.ToDictionary(b => b.Weapon.Name, b => b.Tier);
        var recolour = ForgedWeaponRecolour.Write(modDir, forged, rng, "pool", tiers);
        ForgedWeaponWriter.WriteAll(modDir, forged, recolour);

        // Any texture the game does not already provide has to travel with the mod. Detected by
        // absence from the game's own index rather than by a naming convention, and fatal when a
        // texture is found nowhere - see ForgedWeaponTextures.
        ForgedWeaponTextures.Ship(modDir, gameDir, partsDirs,
            [.. forged.SelectMany(w => w.Materials)], [.. forged.SelectMany(w => w.Parts)]);

        // Falls back to the stock icon whenever there is no colour to apply or the source cannot be
        // read — an icon that does not match the model is far better than none.
        var icons = new string[built.Count];

        Parallel.For(0, built.Count, IconParallel, i =>
            icons[i] = IconFor(modDir, gameDir, built[i].Weapon, built[i].Kind, built[i].StockIcon, recolour));

        var rows = new List<WeaponAsset>();

        for (int i = 0; i < built.Count; i++)
        {
            rows.Add(new WeaponAsset($"{built[i].Weapon.Name}_visuals", built[i].Kind,
                ForgedWeaponWriter.EntityName(built[i].Weapon.Name), icons[i], built[i].Tier));
        }

        return rows;
    }

    private static List<(ForgedWeapon Weapon, string Kind, string StockIcon, ArtifactRarity Tier)> ForgeOneKind(
        string kind, string icon, IReadOnlyList<WeaponPart> parts, WeaponSchema schema, Rng rng,
        int poolSize)
    {

        // Only families whose parts are fully textured can be drawn at all; an untextured one
        // renders as a hole in the portrait. Filtering here rather than at assembly keeps every
        // combination below valid by construction.
        var textured = parts
            .Where(p => p.HasTextures)
            .GroupBy(p => p.Family)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        // A family is judged against the role it is being drawn for, not against the whole schema.
        //
        // The two roles need different things. A lead supplies exactly one part - the blade, the
        // head - so a family cut from a weapon whose hilt was not worth keeping is a perfectly good
        // lead with nothing else. A base supplies everything the lead does not, so it does need the
        // full set. Requiring all four slots of both roles was the old rule, and it discarded any
        // partial family outright and without a word: a blade-only cut simply never appeared.
        var nonLead = schema.Required.Where(s => s != schema.Lead).ToList();

        var leadFamilies = textured
            .Where(g => g.Any(p => p.Slot == schema.Lead))
            .Select(g => g.Key)
            .ToList();

        var baseFamilies = textured
            .Where(g => nonLead.All(s => g.Any(p => p.Slot == s)))
            .Select(g => g.Key)
            .ToList();

        if (leadFamilies.Count == 0 || baseFamilies.Count == 0)
        {
            Console.WriteLine($"  forged weapons: {kind} library has no family that can supply a "
                + $"{(leadFamilies.Count == 0 ? schema.Lead.ToString().ToLowerInvariant() : "body")}"
                + " from textured parts - none forged");
            return [];
        }

        // Worth naming, because a partial family is invisible in every other way: it never forges a
        // weapon on its own, only ever lends its lead to someone else's body.
        var leadOnly = leadFamilies.Where(f => !baseFamilies.Contains(f)).ToList();

        if (leadOnly.Count > 0)
        {
            Console.WriteLine($"  forged weapons: {kind} lead-only "
                + $"({schema.Lead.ToString().ToLowerInvariant()} donors, no body) — "
                + string.Join(", ", leadOnly));
        }

        // Said out loud because it silently shrinks the combination space: a family restricted here
        // contributes one pure weapon instead of pairing with the other nine, and a pool that looks
        // thin is otherwise very hard to trace back to this list.
        var restricted = leadFamilies.Concat(baseFamilies).Distinct()
            .Where(SelfOnlyFamilies.Contains).ToList();

        if (restricted.Count > 0)
        {
            Console.WriteLine($"  forged weapons: {kind} restricted to self-only — "
                + string.Join(", ", restricted));
        }

        var recipes = new List<List<WeaponPart>>();

        // Combinations are deduplicated so a small library cannot hand the same weapon out twice
        // under two names. With one family that means a pool of one, which is correct.
        var seen = new HashSet<string>();

        for (int attempt = 0; attempt < poolSize * 8 && recipes.Count < poolSize; attempt++)
        {
            // One family supplies the anchor and its neighbours, another supplies the business
            // end -- a blade on a foreign hilt, a head on a foreign haft. Two reasons, and the
            // second is the surprising one: a hilt whose parts came off the same weapon looks
            // deliberate, and because parts of one family never share atlas texels, keeping it
            // coherent is also what lets the recolour step tint the parts separately. Measured on
            // the sword library, a coherent hilt yields 2+ distinct colours 81% of the time
            // against 42% for a free-for-all.
            string leadFamily = leadFamilies[rng.Int(0, leadFamilies.Count - 1)];

            // Drawn from the families this lead may actually pair with, rather than drawn freely and
            // then rejected: a rejected pair would still consume one of the attempt budget's slots,
            // so a library with several self-only families could quietly under-fill its pool.
            var bases = Compatible(baseFamilies, leadFamily);
            if (bases.Count == 0) continue;

            string baseFamily = bases[rng.Int(0, bases.Count - 1)];

            var chosen = SelectParts(parts, schema, leadFamily, baseFamily);
            if (chosen is null) continue;
            string signature = string.Join("|", chosen.Select(p => p.Name));
            if (!seen.Add(signature)) continue;

            recipes.Add(chosen);
        }

        // Bands are handed out after the loop rather than inside it, because the pool can
        // under-fill: a small or heavily self-restricted library runs out of distinct combinations
        // long before the attempt budget does. Planning against the *requested* size and taking
        // bands off the front would then drop whichever bands fell past the end — and those are the
        // rare ones, so a thin library would lose its illustrious look and keep every common.
        // Planning against what was actually forged degrades in the right direction instead.
        var plan = TierPlan(recipes.Count);
        var made = new List<(ForgedWeapon, string, string, ArtifactRarity)>(recipes.Count);
        var band = new Dictionary<ArtifactRarity, int>();

        for (int i = 0; i < recipes.Count; i++)
        {
            var tier = plan[i];
            band[tier] = band.GetValueOrDefault(tier) + 1;

            // The band is in the name so every file this weapon owns -- mesh, asset, mask, palette,
            // icon, visual key -- says which band it was forged for. Numbering restarts per band,
            // and the pair is unique because a look holds exactly one band.
            string name = $"gen_forged_{kind}_{tier.ToString().ToLowerInvariant()}_{band[tier]:00}";
            made.Add((WeaponForge.Assemble(name, recipes[i], schema), kind, icon, tier));
        }

        return made;
    }

    private static WeaponPart PickPart(IEnumerable<WeaponPart> parts, string family, WeaponPartSlot slot)
        => parts.First(p => p.Family == family && p.Slot == slot && p.HasTextures);

    /// <summary>
    /// The icon for a forged weapon: a render of its own geometry where that is proven, and a tint
    /// of the stock icon everywhere else.
    ///
    /// **Every kind with a parts library renders.** <see cref="ForgedWeaponRender"/> follows
    /// vanilla's own two compositions: a bladed weapon is framed on the hilt with the blade cropped
    /// off the bottom, and a hafted one is the mirror — head at the top, haft cropped — which is
    /// what <c>artifact_axe.dds</c> and <c>artifact_mace.dds</c> do. Spear and hammer have no parts
    /// library and fall back to the vanilla catalogue before ever reaching here, so the tint path
    /// below now only covers a weapon whose render genuinely failed.
    /// </summary>
    private static string IconFor(
        string modDir, string gameDir, ForgedWeapon weapon, string kind, string stock,
        ForgedRecolour? recolour)
    {
        if (recolour is not { } r) return stock;

        if (r.PartColour.TryGetValue(weapon.Name, out var partColours)
            && ForgedWeaponRender.Write(modDir, gameDir, weapon, WeaponSchema.For(kind), kind, partColours) is { } drawn)
        {
            return drawn;
        }

        return r.PrimaryColour.TryGetValue(weapon.Name, out var colour)
            ? ForgedWeaponIcon.Write(modDir, gameDir, weapon.Name, stock, colour) ?? stock
            : stock;
    }

    /// <summary>
    /// Locates the parts library, or null if this checkout has none.
    ///
    /// Probed the same way <see cref="FlatmapWriter"/> finds its parchment: the assets folder is
    /// copied beside the built exe, but a <c>dotnet run</c> from the repo resolves it from the
    /// working directory instead.
    /// </summary>
    private static string? FindParts()
    {
        foreach (string relPath in PartsRelPaths)
        {
            if (Locate(relPath) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// Resolves one library path, or null if this checkout has none.
    ///
    /// Probed the same way <see cref="FlatmapWriter"/> finds its parchment: the assets folder is
    /// copied beside the built exe, but a <c>dotnet run</c> from the repo resolves it from the
    /// working directory instead.
    /// </summary>
    public static string? Locate(string relPath)
    {
        string rel = relPath.Replace('/', Path.DirectorySeparatorChar);

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "assets", rel),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", rel),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets", rel),
        ];

        foreach (string c in candidates)
        {
            string full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }

        return null;
    }


    /// <summary>
    /// Picks one part per slot: <paramref name="lead"/> supplies the business end (the last link in
    /// the schema's chain — a blade, an axe head), <paramref name="baseFamily"/> supplies the anchor
    /// and everything else.
    ///
    /// Two families rather than one per slot, and the second reason is the surprising one: a hilt
    /// whose parts came off the same weapon looks deliberate, and because parts of one family never
    /// share atlas texels, keeping it coherent is also what lets the recolour step tint the parts
    /// separately — 2+ distinct colours 81% of the time against 42% for a free-for-all.
    ///
    /// Returns null when a **required** slot is missing. An **optional** slot that is missing is
    /// simply skipped: half the vanilla axes have no butt cap, and a capless axe is a whole axe.
    /// </summary>
    private static List<WeaponPart>? SelectParts(
        IReadOnlyList<WeaponPart> parts, WeaponSchema schema, string lead, string baseFamily)
    {
        var chosen = new List<WeaponPart>();

        foreach (var slot in schema.AllSlots)
        {
            string from = slot == schema.Lead ? lead : baseFamily;
            var part = parts.FirstOrDefault(p => p.Family == from && p.Slot == slot);

            if (part is not null) chosen.Add(part);
            else if (!schema.Optional.Contains(slot)) return null;
        }

        return chosen;
    }
}
