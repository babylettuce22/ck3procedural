namespace Ck3MapGen.Magic;

/// <summary>
/// A front end for this folder alone, so the design can be judged before it is wired to anything.
///
/// Deliberately not part of the generation pipeline: it constructs no world, reads no heightmap
/// and writes no mod. <see cref="Program"/> hands off to it on the first argument and returns,
/// which keeps the whole feature removable by deleting a directory and two lines.
///
///   --magic [seed]                 one world, in full
///   --magic-sweep [count]          many worlds, and what varies between them
///
///   --from N                       first seed for a sweep (default 1)
///   --exchange X                   power per unit price; below 1 is expensive magic
///   --spells N                     spell budget per world
///   --ranks N                      ladder height (0 rolls it)
///   --prophecies N                 how many to seed
///   --presence X                   Absent | Hidden | Rare | Common | Universal
///   --ceiling X                    Personal | Court | Realm | World  (clamp)
///   --no-mundane                   never roll a world with no practice
///   --out PATH                     write to a file instead of the console
/// </summary>
public static class MagicCli
{
    public static bool Handles(string arg) => arg is "--magic" or "--magic-sweep";

    public static int Run(string[] args)
    {
        var options = new MagicOptions();
        bool sweep = args[0] == "--magic-sweep";

        int seed = 1;
        int count = 40;
        string? outPath = null;

        // A bare number straight after the verb is the seed, or the sweep size. Everything else is
        // a named flag, so the common cases stay short: `--magic 7`, `--magic-sweep 200`.
        int i = 1;
        if (args.Length > 1 && !args[1].StartsWith("--") && int.TryParse(args[1], out int bare))
        {
            if (sweep) count = bare; else seed = bare;
            i = 2;
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from" when i + 1 < args.Length:
                    seed = int.Parse(args[++i]);
                    break;

                case "--exchange" when i + 1 < args.Length:
                    options.Exchange = double.Parse(args[++i]);
                    break;

                case "--spells" when i + 1 < args.Length:
                    options.SpellBudget = int.Parse(args[++i]);
                    break;

                case "--ranks" when i + 1 < args.Length:
                    options.RankCount = int.Parse(args[++i]);
                    break;

                case "--prophecies" when i + 1 < args.Length:
                    options.ProphecyCount = int.Parse(args[++i]);
                    break;

                case "--presence" when i + 1 < args.Length:
                    if (!Enum.TryParse<MagicPrevalence>(args[++i], true, out var presence))
                    {
                        Console.Error.WriteLine($"--presence {args[i]}: expected one of "
                                                + string.Join(", ", Enum.GetNames<MagicPrevalence>()));
                        return 1;
                    }

                    options.Presence = presence;
                    break;

                case "--ceiling" when i + 1 < args.Length:
                    if (!Enum.TryParse<MagicCeiling>(args[++i], true, out var ceiling))
                    {
                        Console.Error.WriteLine($"--ceiling {args[i]}: expected one of "
                                                + string.Join(", ", Enum.GetNames<MagicCeiling>()));
                        return 1;
                    }

                    options.CeilingCap = ceiling;
                    break;

                case "--no-mundane":
                    options.AllowMundane = false;
                    break;

                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;

                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 1;
            }
        }

        string text = sweep
            ? MagicReport.Sweep(seed, Math.Max(1, count), options)
            : MagicReport.Render(MagicGenerator.Generate(seed, options), options);

        if (outPath is null)
        {
            Console.WriteLine(text);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

            // With a BOM. The report is full of em-dashes and the tools that will read it — an
            // editor, Get-Content, a diff — guess ANSI without one and render them as mojibake.
            File.WriteAllText(outPath, text, new System.Text.UTF8Encoding(true));
            Console.WriteLine($"wrote {outPath}");
        }

        return 0;
    }
}
