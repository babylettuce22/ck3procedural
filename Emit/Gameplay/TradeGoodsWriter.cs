using System.Text;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// <c>common/trade/county_potential_trade_goods/county_potential_trade_goods.txt</c>, under
/// vanilla's own file name so it replaces vanilla's: that file names only vanilla counties, and a
/// generated county missing from every list has no potential goods, which shuts it out of nearly
/// every village and resource district building. Which goods, see <see cref="TradeGoods"/>.
/// </summary>
public static class TradeGoodsWriter
{
    public static void Write(string modDir, Dictionary<Title, List<string>> potential)
    {
        var byGood = new Dictionary<string, List<string>>();
        foreach (var good in TradeGoodTable.Goods) byGood[good.Key] = [];
        foreach (var (county, goods) in potential.OrderBy(kv => kv.Key.Index))
            foreach (string g in goods) byGood[g].Add(county.Key);

        var b = new StringBuilder();
        b.Append("# Generated: which trade goods each county could produce. See TradeGoods.cs.\n");
        foreach (var (good, counties) in byGood)
        {
            // An empty list is valid, but leaving the good out says the same thing more plainly.
            if (counties.Count == 0) continue;
            b.Append(good).Append(" = {\n\tcounties = {");
            for (int i = 0; i < counties.Count; i++)
                b.Append(i % 12 == 0 ? "\n\t\t" : " ").Append(counties[i]);
            b.Append("\n\t}\n}\n\n");
        }

        string dir = Path.Combine(modDir, "common", "trade", "county_potential_trade_goods");
        Directory.CreateDirectory(dir);
        ParadoxText.WriteBom(Path.Combine(dir, "county_potential_trade_goods.txt"), b.ToString());

        Report(potential, byGood);
    }

    private static void Report(Dictionary<Title, List<string>> potential, Dictionary<string, List<string>> byGood)
    {
        if (potential.Count == 0) return;
        var counts = potential.Values.GroupBy(g => g.Count).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        Console.WriteLine($"  trade goods: {potential.Count} counties, goods per county {string.Join(" ", counts)} " +
                          "(vanilla 2:5% 3:21% 4:46% 5:28%)");

        var shares = TradeGoodTable.Goods
            .Select(g => $"{g.Key["trade_good_".Length..]} {100.0 * byGood[g.Key].Count / potential.Count:F0}/{100 * g.Share:F0}");
        Console.WriteLine($"    share % generated/vanilla: {string.Join(", ", shares)}");
    }
}
