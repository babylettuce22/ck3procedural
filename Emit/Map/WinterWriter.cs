using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// How hard each province's winters are, written to the two places CK3 looks for it.
///
/// Winter is not scenery. It cuts an army's supply and bleeds it through attrition, it docks
/// men-at-arms (heavy cavalry fights at −20 damage in a harsh winter), and it is what the engine's
/// "game snow" texture is drawn from — the per-province snow the terrain shader lays on top of the
/// hemisphere gradient. Before this writer, a generated map had none of it and some of it wrong:
///
/// <list type="bullet">
/// <item><c>common/province_terrain/01_province_properties.txt</c>, where vanilla sets a
/// <c>winter_severity_bias</c> on all 12,632 provinces, was shipped empty, so every province took
/// the engine's default of 0.1. The mild level starts at 0.2, so nowhere had winter at all.</item>
/// <item><c>map_data/climate.txt</c> was never written, and map_data is deliberately not
/// replaced (see <see cref="ModWriter"/>), so vanilla's file loaded onto this map. Its 635 ids are
/// all ≤ ~1,450 and every one lands on a generated barony — about a tenth of a 6,000-barony map,
/// in whatever blocks the id order happens to make.</item>
/// </list>
///
/// Both now come from one number per province, so they cannot disagree. The bias is read off the
/// province's coldest month in the climate model, which already cools high ground by the lapse
/// rate; <see cref="Curve"/> is vanilla's own bias against the real coldest-month temperature of
/// fifty of its provinces, and dry country is softened the way vanilla softens its steppe.
/// </summary>
public static class WinterWriter
{
    /// <summary>
    /// Coldest-month mean (°C) to <c>winter_severity_bias</c>, linear between the points and flat
    /// beyond them. Read off vanilla: Cairo, Baghdad, Córdoba and Rome (≥ 8 °C) are 0;
    /// Marseille and Damascus (7 °C) 0.1; London, Paris, Dublin and York (4–5 °C) 0.40–0.45 —
    /// an oceanic winter counts, frost or not; Trier and Tbilisi (1 °C) 0.4; Prague and Kraków
    /// (−1 to −2 °C) 0.55–0.6; Kiev, Moscow and Novgorod (−5 to −9 °C) 0.6–0.65; Kazan and Perm
    /// (−11 to −14 °C) 0.8; Kholmogory (−13 °C) and Karakorum (−20 °C) 0.9.
    /// </summary>
    private static readonly (double ColdC, double Bias)[] Curve =
    [
        (-22, 1.00), (-16, 0.90), (-12, 0.80), (-8, 0.65), (-2, 0.60),
        (1, 0.45), (5, 0.40), (7, 0.10), (9, 0.00),
    ];

    /// <summary>
    /// What dry country keeps of its winter. Vanilla's steppe sits well under its temperature:
    /// Saray, Itil and Sarkel at −7 °C are 0.4 where Moscow at −9 °C is 0.65, and Otrar, Urgench
    /// and Khiva are 0.2–0.4. The Tarim oases are the exception (0.6), which is why this is a
    /// factor and not a zero.
    /// </summary>
    private const double DryFactor = 0.65;

    /// <summary>
    /// The engine's winter levels (<c>WINTER_LEVEL_THRESHOLD_*</c> in 00_defines): a province
    /// whose bias is under a level's line can never reach that level.
    /// </summary>
    private const double Mild = 0.2, Normal = 0.5, Harsh = 0.9;

    /// <summary>
    /// Every province's bias, indexed by province id: land from its climate, water at 0.0, as
    /// vanilla's file says seas and rivers should stay. Impassable provinces are land and are
    /// included — nobody campaigns there, but the game snow is drawn per province, and a wall
    /// with a winter is a range that whitens in January.
    /// </summary>
    public static double[] Severity(ClimateField field, ProvinceMap provinces, int[] order, int landCount)
    {
        int provinceCount = 0;
        foreach (int id in order) provinceCount = Math.Max(provinceCount, id);
        var bias = new double[provinceCount + 1];

        var cold = new double[landCount + 1];
        var warm = new double[landCount + 1];
        var mean = new double[landCount + 1];
        var annual = new double[landCount + 1];
        var summer = new double[landCount + 1];
        var winter = new double[landCount + 1];
        var count = new int[landCount + 1];

        int w = provinces.Width, h = provinces.Height;
        bool same = field.Width == w && field.Height == h;
        for (int y = 0; y < h; y++)
        {
            int fy = same ? y : (int)((long)y * field.Height / h);
            for (int x = 0; x < w; x++)
            {
                int id = order[provinces.Label[y * w + x]];
                if (id < 1 || id > landCount) continue;

                int i = fy * field.Width + (same ? x : (int)((long)x * field.Width / w));
                cold[id] += field.ColdC[i];
                warm[id] += field.WarmC[i];
                mean[id] += field.MeanC[i];
                annual[id] += field.AnnualMm[i];
                summer[id] += field.SummerMm[i];
                winter[id] += field.WinterMm[i];
                count[id]++;
            }
        }

        for (int id = 1; id <= landCount; id++)
        {
            int n = count[id];
            if (n == 0) continue;

            double coldC = cold[id] / n;
            double value = FromColdest(coldC);
            var zone = Koppen.Classify(warm[id] / n, coldC, mean[id] / n, annual[id] / n, summer[id] / n, winter[id] / n);
            if (Koppen.IsArid(zone)) value *= DryFactor;

            // Vanilla writes the bias in twentieths; so does this, which also keeps two provinces
            // a hundredth of a degree apart from reading as two different climates in the file.
            bias[id] = Math.Round(value * 20) / 20;
        }

        return bias;
    }

    private static double FromColdest(double coldC)
    {
        if (coldC <= Curve[0].ColdC) return Curve[0].Bias;
        for (int k = 1; k < Curve.Length; k++)
        {
            if (coldC > Curve[k].ColdC) continue;
            var (c0, b0) = Curve[k - 1];
            var (c1, b1) = Curve[k];
            return b0 + (b1 - b0) * (coldC - c0) / (c1 - c0);
        }
        return Curve[^1].Bias;
    }

    public static void WriteAll(string modDir, double[] bias, int baronyCount, int landCount)
    {
        WriteProperties(modDir, bias, landCount);
        WriteClimate(modDir, bias, landCount);
        Report(bias, baronyCount, landCount);
    }

    /// <summary>
    /// <c>01_province_properties.txt</c>, laid out as vanilla's is. Every province is written,
    /// water included: one left out falls back to the engine's 0.1, not to 0.
    /// </summary>
    private static void WriteProperties(string modDir, double[] bias, int landCount)
    {
        string dir = Path.Combine(modDir, "common", "province_terrain");
        Directory.CreateDirectory(dir);

        var b = new JominiBuilder(JominiStyle.Script);
        b.Comment("Winter severity per province, from the generated climate's coldest month. Seas and");
        b.Comment("rivers stay at 0.0, as vanilla's own file asks.");
        for (int id = 1; id < bias.Length; id++)
        {
            using (b.Block($"{id}"))
                b.Field("winter_severity_bias", id <= landCount ? bias[id] : 0.0, "0.0#");
        }

        ParadoxText.WriteBom(Path.Combine(dir, "01_province_properties.txt"), b.ToString());
    }

    /// <summary>
    /// <c>map_data/climate.txt</c>, the older per-province winter lists. Vanilla keeps only a
    /// twentieth of its provinces in them and sets the bias on all of them, so the bias is what
    /// counts; the lists are written from the same bias anyway, so whichever the engine reads, it
    /// reads the same answer, and vanilla's ids no longer land on this map. A province goes in the
    /// highest level its bias can reach.
    /// </summary>
    private static void WriteClimate(string modDir, double[] bias, int landCount)
    {
        string dir = Path.Combine(modDir, "map_data");
        Directory.CreateDirectory(dir);

        var text = new StringBuilder();
        List("mild_winter", v => v >= Mild && v < Normal);
        List("normal_winter", v => v >= Normal && v < Harsh);
        List("severe_winter", v => v >= Harsh);

        // Written beside map_data and moved in, not written in place: MapDataWriter may be running
        // on another thread, and it fails the run on any empty file at map_data's top level — which
        // a file being written by truncation is, for a moment. The move is atomic, so its scan sees
        // no climate.txt or a whole one.
        string staging = Path.Combine(modDir, "climate.txt.tmp");
        ParadoxText.WriteNoBom(staging, text.ToString());
        File.Move(staging, Path.Combine(dir, "climate.txt"), overwrite: true);

        void List(string key, Func<double, bool> member)
        {
            text.Append(key).Append(" = {\n");
            int onLine = 0;
            for (int id = 1; id <= landCount && id < bias.Length; id++)
            {
                if (!member(bias[id])) continue;
                text.Append(onLine == 0 ? "\t" : " ").Append(id);
                if (++onLine == 20) { text.Append('\n'); onLine = 0; }
            }
            if (onLine > 0) text.Append('\n');
            text.Append("}\n\n");
        }
    }

    private static void Report(double[] bias, int baronyCount, int landCount)
    {
        int none = 0, mild = 0, normal = 0, harsh = 0;
        double sum = 0;
        for (int id = 1; id <= baronyCount && id < bias.Length; id++)
        {
            double v = bias[id];
            sum += v;
            if (v < Mild) none++;
            else if (v < Normal) mild++;
            else if (v < Harsh) normal++;
            else harsh++;
        }
        if (baronyCount == 0) return;

        double walls = 0;
        int wallCount = 0;
        for (int id = baronyCount + 1; id <= landCount && id < bias.Length; id++) { walls += bias[id]; wallCount++; }

        string Share(int n) => $"{100.0 * n / baronyCount:F1}%";
        Console.WriteLine($"  winter: baronies with none {Share(none)}, mild at most {Share(mild)}, " +
                          $"normal at most {Share(normal)}, harsh possible {Share(harsh)}, mean bias {sum / baronyCount:F2}" +
                          (wallCount > 0 ? $"; impassable mean {walls / wallCount:F2}" : ""));
        Console.WriteLine("    vanilla for comparison (passable): none 41.8%, mild 12.9%, normal 35.5%, harsh 9.7%, mean 0.37");
    }
}
