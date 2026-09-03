using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The <c>--languages</c> command: prints what each flavour sounds like, so a change to the
/// phonology or a flavour table can be judged in a second instead of after a two-minute world.
/// </summary>
public static class LanguageProbe
{
    public static bool Run(string? flavourName, int seed, bool family)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var flavours = flavourName is null || flavourName.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? LanguageFlavour.All
            : LanguageFlavour.ByName(flavourName) is { } one ? [one] : [];

        if (flavours.Length == 0)
        {
            Console.Error.WriteLine($"No language flavour called \"{flavourName}\". Known: "
                + string.Join(", ", LanguageFlavour.All.Select(f => f.Name)));
            return false;
        }

        foreach (var flavour in flavours)
        {
            var rng = new Rng(seed ^ StableHash(flavour.Name));
            var language = Language.Create($"lang_{flavour.Name.ToLowerInvariant()}", rng, flavour);
            Show(language, flavour.Name, seed);

            if (!family) continue;
            Show(language.Derive(language.Key + "_sister", rng, 0.5), flavour.Name + " / sister language", seed);
            Show(language.Dialect(language.Key + "_dialect", rng), flavour.Name + " / dialect", seed);
        }

        return true;
    }

    private static void Show(Language lang, string label, int seed)
    {
        var rng = new Rng(seed);
        string folk = lang.FolkName(rng);

        Console.WriteLine($"== {label}: language \"{lang.Name}\"; a people called the {folk} speak {lang.LanguageNameFor(folk, rng)}");
        Console.WriteLine($"   patronym: {(lang.PatronymMale.Length == 0 ? "none" : (lang.PatronymIsPrefix ? "prefix " : "suffix ") + lang.PatronymMale + " / " + lang.PatronymFemale)}; of: \"{lang.Particle}\"");
        Line("male", 14, () => lang.MaleName(rng));
        Line("female", 10, () => lang.FemaleName(rng));
        Line("dynasty", 8, () => lang.DynastyName(rng));
        Line("barony", 14, () => lang.PlaceName(rng, 'b'));
        Line("county", 8, () => lang.PlaceName(rng, 'c'));
        Line("duchy", 6, () => lang.PlaceName(rng, 'd'));
        Line("kingdom", 5, () => lang.RealmName(rng, folk, 'k'));
        Line("empire", 3, () => lang.RealmName(rng, folk, 'e'));
        Line("words", 8, () => lang.Word(rng));
        Console.WriteLine($"   place-words: {string.Join(" ", lang.BaronyAffixes)} | {string.Join(" ", lang.CountyAffixes)} | {string.Join(" ", lang.DuchyAffixes)} | {string.Join(" ", lang.KingdomAffixes)}");
        Console.WriteLine();

        static void Line(string label, int count, Func<string> draw)
            => Console.WriteLine($"   {label,-8} {string.Join(" ", Enumerable.Range(0, count).Select(_ => draw()))}");
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }
    }
}
