namespace Ck3MapGen.MapGen;

using Ck3MapGen.Emit;
using Ck3MapGen.Io;

/// <summary>
/// Proves that a weapon split into a shared base and a shared lead puts every vertex exactly where
/// merging the whole thing into one mesh would have.
///
/// **Why this exists rather than a look in-game.** Composition moves the pairing out of the geometry
/// and into an <c>attach</c> locator, and the failure mode is not a crash or a missing model — it is
/// a blade seated two units too deep in its guard, which reads as "the cut is slightly off" and
/// sends you back to Blender after a perfectly correct change. The arithmetic is checkable offline
/// against the merge path that is already known good, so it is checked offline.
///
/// It walks **every** lead/base pairing the libraries can make, not the pool the forge samples: the
/// whole point of composition is that any pairing is available, and a rule that holds for the
/// sixteen a pool happened to draw is not the claim being made.
///
/// Run with <c>--verify-compose</c>. Exits non-zero if any library disagrees.
/// </summary>
public static class WeaponComposeCheck
{
    /// <summary>
    /// How far a composed vertex may sit from its merged counterpart before it counts as a fault.
    ///
    /// Generous by two orders of magnitude, deliberately. Composition subtracts a socket from the
    /// geometry and adds it back through the locator, so in exact arithmetic the difference is zero
    /// and in float32 it is a rounding step on values of order 100 — call it 1e-4. A real fault is a
    /// part mated against the wrong socket, which lands whole units away. Nothing lives in between,
    /// so the threshold only has to separate the two.
    /// </summary>
    private const float Tolerance = 0.01f;

    /// <summary>Runs the check over every parts library this checkout has. True if all agree.</summary>
    public static bool Run()
    {
        bool ok = true;
        int libraries = 0;

        foreach (var (kind, relPath, _) in WeaponForgeStep.PartsLibraries)
        {
            string? path = WeaponForgeStep.Locate(relPath);

            if (path is null)
            {
                Console.WriteLine($"  {kind,-7} no library in this checkout - skipped");
                continue;
            }

            libraries++;
            ok &= CheckLibrary(kind, path);
        }

        if (libraries == 0)
        {
            Console.Error.WriteLine(
                "No parts libraries found. Nothing was verified, which is not the same as passing.");
            return false;
        }

        return ok;
    }

    private static bool CheckLibrary(string kind, string path)
    {
        var schema = WeaponSchema.For(kind);
        var parts = WeaponForge.LoadParts(path, schema);

        // Same rule the forge uses: a lead needs only its own slot, a base needs everything else.
        // Mirrored rather than shared because the forge draws a random pool from these and this
        // walks all of them, so the two want different things from the same classification.
        var nonLead = schema.Required.Where(s => s != schema.Lead).ToList();

        var families = parts
            .Where(p => p.HasTextures)
            .GroupBy(p => p.Family)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var leads = families.Where(g => g.Any(p => p.Slot == schema.Lead)).Select(g => g.Key).ToList();
        var bases = families.Where(g => nonLead.All(s => g.Any(p => p.Slot == s))).Select(g => g.Key).ToList();

        if (leads.Count == 0 || bases.Count == 0)
        {
            Console.WriteLine($"  {kind,-7} no forgeable pairing - skipped");
            return true;
        }

        int pairings = 0, unmountable = 0, unshareable = 0;
        var faults = new List<string>();

        foreach (string baseFamily in bases)
        {
            var baseParts = parts.Where(p => p.Family == baseFamily && p.Slot != schema.Lead).ToList();
            var built = WeaponForge.BuildBase($"check_base_{baseFamily}", baseParts, schema);

            if (!built.LeadMountable)
            {
                unmountable++;
                continue;
            }

            foreach (string leadFamily in leads)
            {
                var leadPart = parts.First(p => p.Family == leadFamily && p.Slot == schema.Lead);
                var lead = WeaponForge.BuildLead($"check_lead_{leadFamily}", leadPart, schema);

                if (lead is null)
                {
                    unshareable++;
                    continue;
                }

                // The reference: what merging this exact pairing would have produced. Original
                // anchoring, because that is the frame the base is built in and the one the hand
                // holds.
                var chosen = SelectParts(parts, schema, leadFamily, baseFamily);
                if (chosen is null) continue;

                pairings++;

                var placements = WeaponForge.Placements(chosen, schema);
                float drift = LeadDrift(lead, built.LeadLocator, leadPart, placements[leadPart]);

                if (drift > Tolerance)
                    faults.Add($"{leadFamily} on {baseFamily}: lead off by {drift:F3}");
            }
        }

        foreach (string fault in faults.Take(10)) Console.Error.WriteLine($"    {fault}");

        if (faults.Count > 10)
            Console.Error.WriteLine($"    ... and {faults.Count - 10} more");

        string verdict = faults.Count == 0 ? "OK" : $"{faults.Count} FAULTS";

        Console.WriteLine($"  {kind,-7} {pairings,4} pairings from "
            + $"{leads.Count} leads + {bases.Count} bases  {verdict}");

        // Both of these are the ways composition quietly stops being additive, so both are named
        // rather than counted silently: a base that cannot seat a lead, or a lead with no socket to
        // be normalised onto, drops out of the catalogue entirely.
        if (unmountable > 0)
            Console.WriteLine($"  {kind,-7} {unmountable} base(s) have no socket for the lead");

        if (unshareable > 0)
            Console.WriteLine($"  {kind,-7} {unshareable} lead(s) carry no socket toward their mount");

        return faults.Count == 0;
    }

    /// <summary>
    /// How far the composed lead sits from where merging would have put it.
    ///
    /// Compares <c>normalised mesh + locator</c> against <c>source mesh + shift</c> vertex by
    /// vertex and returns the worst single coordinate. Vertex order survives translation, so index i
    /// on one side is index i on the other; a length mismatch is itself a fault and is reported as
    /// infinite rather than by comparing whatever happens to overlap.
    /// </summary>
    private static float LeadDrift(
        WeaponPiece lead, float[] locator, WeaponPart source, float[] shift)
    {
        float[] composed = FirstMeshPositions(lead.Root);
        float[] original = source.Mesh.Floats("p");

        if (composed.Length != original.Length || composed.Length == 0) return float.PositiveInfinity;

        float worst = 0f;

        for (int i = 0; i < original.Length; i++)
        {
            float expected = original[i] + shift[i % 3];
            float actual = composed[i] + locator[i % 3];
            worst = Math.Max(worst, Math.Abs(expected - actual));
        }

        return worst;
    }

    /// <summary>
    /// The <c>p</c> stream of the first mesh node in a built piece.
    ///
    /// A lead is a single part and so a single material batch, which is why taking the first is
    /// enough here and would not be for a base.
    /// </summary>
    private static float[] FirstMeshPositions(PdxNode root)
    {
        foreach (var shape in root.Child("object").Children)
            foreach (var mesh in shape.Children)
                if (mesh.Name == "mesh") return mesh.Floats("p");

        return [];
    }

    /// <summary>One part per slot: the lead from its family, everything else from the base.</summary>
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
