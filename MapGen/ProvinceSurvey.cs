namespace Ck3MapGen.MapGen;

/// <summary>
/// The per-barony facts that need a pass over the province raster and that more than one stage
/// wants: where each province is, whether it touches the sea or a major river, and how much
/// water flows through it. Taken once and shared, because the raster is tens of millions of
/// cells and every stage that walked it for itself was a second of run time.
/// </summary>
public sealed class ProvinceSurvey
{
    /// <summary>Seed position per barony province id, index 0 unused.</summary>
    public required (double X, double Y)[] Position { get; init; }

    /// <summary>Whether the province touches open sea.</summary>
    public required bool[] Coastal { get; init; }

    /// <summary>Whether the province touches a major river province.</summary>
    public required bool[] Riverside { get; init; }

    /// <summary>The strongest drainage flow through the province, 0 without drainage data.</summary>
    public required float[] PeakFlow { get; init; }

    /// <summary>
    /// The flow that counts as "a real river": the 90th percentile of provinces that have any,
    /// so a single great river does not make every other stream read as a trickle.
    /// </summary>
    public required float FlowReference { get; init; }

    /// <summary>How much of a river a province has, 0 to 1, with a major river bank counting as 1.</summary>
    public double River(int province)
        => Riverside[province] ? 1.0 : Math.Clamp(PeakFlow[province] / FlowReference, 0, 1);

    public static ProvinceSurvey Take(ProvinceMap provinces, int[] order, int baronyCount, Drainage? drainage)
    {
        var coastal = new bool[baronyCount + 1];
        var riverside = new bool[baronyCount + 1];
        var peakFlow = new float[baronyCount + 1];
        var position = new (double X, double Y)[baronyCount + 1];

        for (int label = 0; label < order.Length; label++)
        {
            int id = order[label];
            if (id >= 1 && id <= baronyCount && label < provinces.Seeds.Count)
                position[id] = (provinces.Seeds[label].X, provinces.Seeds[label].Y);
        }

        // Right and down neighbours are enough — every adjacent pair is seen once from one side
        // or the other.
        int w = provinces.Width, h = provinces.Height;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                int cell = row + x;
                int label = provinces.Label[cell];
                int id = order[label];
                bool land = id >= 1 && id <= baronyCount;

                if (land && drainage is not null && cell < drainage.Flow.Length && drainage.LandMask[cell] != 0)
                    peakFlow[id] = Math.Max(peakFlow[id], drainage.Flow[cell]);

                if (x + 1 < w) Touch(id, label, land, provinces.Label[cell + 1]);
                if (y + 1 < h) Touch(id, label, land, provinces.Label[cell + w]);
            }
        }

        void Touch(int id, int label, bool land, int otherLabel)
        {
            if (label == otherLabel) return;
            int other = order[otherLabel];
            bool otherLand = other >= 1 && other <= baronyCount;
            if (land == otherLand) return;

            // Exactly one side is a barony; the other is water, wasteland or an impassable. A
            // wasteland is land too, so only water counts, and a major river is a river bank
            // rather than a coast.
            int barony = land ? id : other;
            var water = provinces.Seeds[land ? otherLabel : label];
            if (water.IsLand) return;
            if (water.IsMajorRiver) riverside[barony] = true;
            else coastal[barony] = true;
        }

        var flows = new List<float>();
        for (int id = 1; id <= baronyCount; id++) if (peakFlow[id] > 0) flows.Add(peakFlow[id]);
        flows.Sort();
        float reference = flows.Count == 0 ? 1f : flows[(int)(0.9 * (flows.Count - 1))];
        if (reference <= 0) reference = 1f;

        return new ProvinceSurvey
        {
            Position = position,
            Coastal = coastal,
            Riverside = riverside,
            PeakFlow = peakFlow,
            FlowReference = reference,
        };
    }
}
