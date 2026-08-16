using System.Text;
using System.Text.RegularExpressions;
using Ck3MapGen.Core;
using Ck3MapGen.Io;
using Ck3MapGen.MapGen;

namespace Ck3MapGen.Emit;

/// <summary>
/// Selects culturally matching DNA templates from vanilla for each bookmark character,
/// writing the full format to common/bookmark_portraits and the proper portrait_info
/// format to common/dna_data so 3D models match seamlessly.
/// </summary>
public static class PortraitWriter
{
    private static readonly Regex IdentityRegex =
        new(@"^[ \t]*[A-Za-z_0-9]+[ \t]*=[ \t]*\{", RegexOptions.Multiline);

    private static readonly Regex MaleTypeRegex =
        new(@"type\s*=\s*male\b", RegexOptions.IgnoreCase);

    private static readonly Regex GenesRegex =
        new(@"genes\s*=\s*\{(?<content>(?>[^{}]+|\{(?<DEPTH>)|\}(?<-DEPTH>))*(?(DEPTH)(?!)))\}", RegexOptions.Singleline);

    public record CharacterPortraitRequest(
        string Key,
        Culture Culture
    );

    public static void WriteAll(string modDir, string gameDir,
        List<CharacterPortraitRequest> requests, int seed = 0)
    {
        string sourceDir = Path.Combine(gameDir, "common", "bookmark_portraits");
        string bmDestDir = Path.Combine(modDir, "common", "bookmark_portraits");
        string dnaDestDir = Path.Combine(modDir, "common", "dna_data");

        Directory.CreateDirectory(bmDestDir);
        Directory.CreateDirectory(dnaDestDir);

        var templates = LoadCategorizedMaleTemplates(sourceDir);
        if (templates.AllTemplates.Count == 0)
        {
            Console.WriteLine("  portraits: no vanilla male templates found, skipped");
            return;
        }

        var rng = new Rng(seed ^ 0x5087);
        var dnaFileBuilder = new StringBuilder();
        dnaFileBuilder.Append("# Generated DNA mappings for in-game characters\n\n");

        foreach (var req in requests)
        {
            string templatePath = PickMatchingTemplate(req.Culture, templates, rng);
            string body = File.ReadAllText(templatePath);
            string renamedBookmark = IdentityRegex.Replace(body, $"{req.Key} = {{", 1);

            // 1. Write for Bookmark Screen (common/bookmark_portraits/)
            ParadoxText.WriteBom(Path.Combine(bmDestDir, $"{req.Key}.txt"), renamedBookmark);

            // 2. Extract genes and wrap in proper portrait_info format for in-game DNA (common/dna_data/)
            var match = GenesRegex.Match(body);
            if (match.Success)
            {
                string genesContent = match.Groups["content"].Value.Trim();
                string dnaKey = $"dna_{req.Key}";

                dnaFileBuilder.Append($$"""
                {{dnaKey}} = {
                	portrait_info = {
                		genes = {
                {{genesContent}}
                		}
                	}
                	enabled = yes
                }


                """);
            }
        }

        // 3. Write for In-Game campaign load
        ParadoxText.WriteBom(Path.Combine(dnaDestDir, "00_generated_dna.txt"), dnaFileBuilder.ToString());

        Console.WriteLine($"  portraits: {requests.Count} culture-matched portraits written to bookmark_portraits and dna_data");
    }

    private record TemplatePool(
        List<string> Western,
        List<string> Norse,
        List<string> Mena,
        List<string> African,
        List<string> Asian,
        List<string> Byzantine,
        List<string> Steppe,
        List<string> AllTemplates
    );

    private static string PickMatchingTemplate(Culture culture, TemplatePool pool, Rng rng)
    {
        var look = culture.Heritage.Look;
        string clothing = (look.ClothingGfx ?? "").ToLowerInvariant();
        string eth = (look.Ethnicities ?? "").ToLowerInvariant();

        List<string> candidatePool;

        if (clothing.Contains("norse") || eth.Contains("northern") || eth.Contains("scandinavian"))
            candidatePool = pool.Norse;
        else if (clothing.Contains("mena") || clothing.Contains("arabic") || clothing.Contains("muslim") || eth.Contains("arab") || eth.Contains("persian"))
            candidatePool = pool.Mena;
        else if (clothing.Contains("african") || eth.Contains("african") || eth.Contains("sub_saharan"))
            candidatePool = pool.African;
        else if (clothing.Contains("indian") || clothing.Contains("asian") || eth.Contains("asian") || eth.Contains("indian"))
            candidatePool = pool.Asian;
        else if (clothing.Contains("byzantine") || clothing.Contains("greek") || eth.Contains("mediterranean"))
            candidatePool = pool.Byzantine;
        else if (clothing.Contains("steppe") || clothing.Contains("mongol") || clothing.Contains("turkic") || eth.Contains("turkic") || eth.Contains("steppe"))
            candidatePool = pool.Steppe;
        else
            candidatePool = pool.Western;

        if (candidatePool.Count == 0) candidatePool = pool.AllTemplates;
        return rng.Pick(candidatePool);
    }

    private static TemplatePool LoadCategorizedMaleTemplates(string sourceDir)
    {
        var western = new List<string>();
        var norse = new List<string>();
        var mena = new List<string>();
        var african = new List<string>();
        var asian = new List<string>();
        var byzantine = new List<string>();
        var steppe = new List<string>();
        var all = new List<string>();

        if (!Directory.Exists(sourceDir))
            return new TemplatePool(western, norse, mena, african, asian, byzantine, steppe, all);

        foreach (string path in Directory.GetFiles(sourceDir, "*.txt").OrderBy(p => p))
        {
            string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string text = File.ReadAllText(path);

            if (!MaleTypeRegex.IsMatch(text)) continue;

            all.Add(path);

            if (fileName.Contains("rurik") || fileName.Contains("bjorn") || fileName.Contains("haesteinn") || fileName.Contains("ivar") || fileName.Contains("harald") || fileName.Contains("sigurdr") || fileName.Contains("halfdan"))
                norse.Add(path);
            else if (fileName.Contains("mutamid") || fileName.Contains("yaqub") || fileName.Contains("tahirid") || fileName.Contains("hashimid") || fileName.Contains("tulunid") || text.Contains("mena_clothing"))
                mena.Add(path);
            else if (fileName.Contains("daura") || fileName.Contains("ghana") || text.Contains("african"))
                african.Add(path);
            else if (fileName.Contains("bhoja") || fileName.Contains("pala") || fileName.Contains("chola") || text.Contains("indian"))
                asian.Add(path);
            else if (fileName.Contains("basil") || fileName.Contains("byzantine") || text.Contains("byzantine"))
                byzantine.Add(path);
            else if (fileName.Contains("khazar") || fileName.Contains("cuman") || fileName.Contains("seljuk") || fileName.Contains("alp_arslan"))
                steppe.Add(path);
            else
                western.Add(path);
        }

        return new TemplatePool(western, norse, mena, african, asian, byzantine, steppe, all);
    }
}