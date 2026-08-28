namespace Ck3MapGen.MapGen;

using Ck3MapGen.Emit;
using System.IO;

/// <summary>
/// Proves that composing a weapon out of separate entities puts every vertex exactly where merging
/// it into one mesh would have.
///
/// **Why this exists rather than a look in-game.** The composition moves placement out of the
/// geometry and into <c>attach</c> locators, and the failure mode is not a crash or a missing model
/// — it is a hilt sitting two units inside a blade, which reads as "the cut is slightly off" and
/// sends you back to Blender after a perfectly correct change. The arithmetic is checkable offline
/// against the merge path that is already known good, so it is checked offline.
///
/// It walks **every** lead/base pairing the libraries can make, not a sampled pool: the whole point
/// of composition is that any pairing is available, and a rule that holds for the sixteen the pool
/// happened to draw is not the claim being made.
///
/// Run with <c>--verify-compose</c>. Exits non-zero on the first library that disagrees.
/// </summary>
public static class WeaponComposeCheck
{
    /// <summary>
    /// How far a composed vertex may sit from its merged counterpart before it counts as a fault.
    ///
    /// Generous by three orders of magnitude, deliberately. Composition subtracts a socket from the
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

        int checkedPairs = 0, unshared = 0;
        var faults = new List<string>();
        var anchorOffsets = new List<(string Lead, float Along)>();

        foreach (string lead in leads)
        {
            foreach (string baseFamily in bases)
            {
                var chosen = SelectParts(parts, schema, lead, baseFamily);
                if (chosen is null) continue;

                checkedPairs++;

                string name = $"check_{kind}_{lead}_{baseFamily}";

                // Reanchored, because that is the frame Compose builds in. Against the ORIGINAL
                // schema every part of a pairing comes out wrong by the same amount, which is not a
                // mating error at all -- it is the constant offset between anchoring on the grip and
                // anchoring on the blade, and it is measured separately below.
                var placements = WeaponForge.Placements(chosen, schema.Reanchored());
                var composed = WeaponForge.Compose(name, chosen, schema.Reanchored());

                // How far this pairing's whole weapon slides in the hand by anchoring on the lead
                // instead of the held part. Under the original schema the held part never moves;
                // under the reanchored one the lead never moves, so the lead's original shift IS the
                // displacement, and its spread across bases is what decides whether one mesh per
                // lead can serve every pairing.
                var original = WeaponForge.Placements(chosen, schema);
                var leadPart = chosen.First(p => p.Slot == schema.Lead);
                anchorOffsets.Add((lead, Along(original[leadPart])));

                foreach (var piece in composed.AllParts)
                {
                    if (!piece.Shared) unshared++;

                    float drift = Drift(piece, placements[piece.Part]);

                    if (drift > Tolerance)
                    {
                        faults.Add($"{lead} + {baseFamily}: {piece.Part.Slot} "
                            + $"({piece.Part.Name}) off by {drift:F3}");
                    }
                }
            }
        }

        foreach (string fault in faults.Take(10)) Console.Error.WriteLine($"    {fault}");

        if (faults.Count > 10)
            Console.Error.WriteLine($"    ... and {faults.Count - 10} more");

        string verdict = faults.Count == 0 ? "OK" : $"{faults.Count} FAULTS";

        Console.WriteLine($"  {kind,-7} {checkedPairs,4} pairings, "
            + $"{leads.Count} leads x {bases.Count} bases  {verdict}");

        // Said out loud because it is the one way composition silently reverts to costing a mesh per
        // pairing: a part whose join lacks a socket on one side keeps its original geometry and is
        // correct, but its mesh belongs to that pairing alone.
        if (unshared > 0)
        {
            Console.WriteLine($"  {kind,-7} {unshared} part placement(s) could not be shared - "
                + "a join is missing a socket on one side; check the library's locators");
        }

        ReportAnchorSpread(kind, anchorOffsets);
        return faults.Count == 0;
    }

    /// <summary>
    /// How far the weapon moves in the hand, per lead, when the lead becomes the anchor.
    ///
    /// **This is the number that decides whether one mesh per lead is enough.** Anchoring on the
    /// held part keeps the grip in the hand and lets the blade land where it may; anchoring on the
    /// lead does the opposite. For a given blade the displacement varies only with what it was
    /// paired with, so the *spread* across bases is the error a single shared mesh would have to
    /// absorb: centre each lead on its own mean and the worst pairing sits half the spread away.
    ///
    /// A spread of a unit or two is nothing on a weapon around 100 units long. A spread of tens
    /// means the hand closes on air for some pairings, and the lead cannot be the shared mesh.
    /// </summary>
    private static void ReportAnchorSpread(string kind, List<(string Lead, float Along)> offsets)
    {
        if (offsets.Count == 0) return;

        float worst = 0f;
        string worstLead = "";

        foreach (var group in offsets.GroupBy(o => o.Lead))
        {
            float spread = group.Max(o => o.Along) - group.Min(o => o.Along);

            if (spread > worst)
            {
                worst = spread;
                worstLead = group.Key;
            }
        }

        float half = worst / 2f;

        Console.WriteLine($"  {kind,-7} anchor spread: worst lead {worstLead} varies {worst:F2} "
            + $"units across bases (+/-{half:F2} if centred)");

        // What lead-anchoring would actually cost. Centre each lead on the median of its own
        // offsets -- median, not mean, so one freak base cannot drag the centre off the cluster the
        // rest of them form -- and count how many pairings then sit within a tolerance. This is the
        // yield of the "blade keeps its palette" design: pairings outside the band cannot use a
        // shared lead mesh and would have to be dropped, exactly as SelfOnlyFamilies already drops
        // pairings that cannot be mated cleanly.
        foreach (float tolerance in (float[])[1f, 2f, 4f])
        {
            int within = 0;

            foreach (var group in offsets.GroupBy(o => o.Lead))
            {
                var sorted = group.Select(o => o.Along).OrderBy(v => v).ToList();
                float centre = sorted[sorted.Count / 2];
                within += sorted.Count(v => Math.Abs(v - centre) <= tolerance);
            }

            Console.WriteLine($"  {kind,-7}   within +/-{tolerance,4:F1} units of its lead's median: "
                + $"{within,4}/{offsets.Count} pairings ({100.0 * within / offsets.Count:F0}%)");
        }
    }

    /// <summary>The component of a placement along the blade axis, which is where it matters.</summary>
    private static float Along(float[] shift) => shift[WeaponForge.AxisIndex];

    /// <summary>
    /// How far this part's composed position sits from where merging would have put it.
    ///
    /// Compares <c>composed mesh + locator</c> against <c>source mesh + shift</c> vertex by vertex,
    /// and returns the worst single coordinate. Vertex order is preserved through the translation,
    /// so index i on one side is index i on the other; a length mismatch is itself a fault and is
    /// reported as an infinite drift rather than by comparing what happens to overlap.
    /// </summary>
    private static float Drift(ComposedPart piece, float[] shift)
    {
        float[] composed = piece.Mesh.Floats("p");
        float[] source = piece.Part.Mesh.Floats("p");

        if (composed.Length != source.Length) return float.PositiveInfinity;

        float worst = 0f;

        for (int i = 0; i < source.Length; i++)
        {
            float expected = source[i] + shift[i % 3];
            float actual = composed[i] + piece.Locator[i % 3];
            worst = Math.Max(worst, Math.Abs(expected - actual));
        }

        return worst;
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
