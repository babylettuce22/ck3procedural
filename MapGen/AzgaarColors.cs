using System.Globalization;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// Repaints the title hierarchy in the export's own colours, so the realm map mode in game shows
/// the political map the export drew.
///
/// The generated palette in <see cref="Titles"/> exists to make an invented world legible: a golden
/// angle spreads the empires as far apart in hue as they will go, and each tier below shades away
/// from its parent so a duchy reads as part of its kingdom. That is the right scheme when the map is
/// ours to invent, and the wrong one when the export has already decided both the borders and the
/// colours — a player who drew a green Shathora next to an orange Vener and then opened the mod to
/// find them purple and teal would rightly call it a bug.
///
/// So the export wins at exactly one tier: the independent realm. Every Azgaar state's title takes
/// that state's colour, and everything below it is shaded from that colour by the ordinary
/// <see cref="Titles.RecolorChildren"/> cascade, which is what keeps a county recognisably part of
/// its kingdom. The shading was never the part that disagreed with the export — the hues were — and
/// dropping it would cost the tier legibility for nothing.
///
/// Ground the export never claimed keeps its generated colour, because there is no Azgaar state
/// there to borrow one from.
/// </summary>
public static class AzgaarColors
{
    /// <summary>
    /// How far an empire is darkened from the member state it takes its colour from.
    ///
    /// Titles above the state tier have no Azgaar object behind them — the export stops at
    /// countries, and the empires above them are ours, grouped by suzerainty and culture. Inventing
    /// a hue for them would put a colour on the map that appears nowhere in the export, so they
    /// borrow from the largest state they contain instead. Borrowing it unchanged would leave an
    /// empire and its dominant kingdom indistinguishable in the de jure map modes, hence the shade.
    /// </summary>
    private const double EmpireShade = 0.78;

    /// <summary>
    /// Paints the state tier from the export and reshades everything beneath it.
    /// </summary>
    public static void Apply(List<Title> roots, AzgaarImport azgaar, Rng rng)
    {
        // Shallowest first, so a state that swallowed another as a vassal cannot cascade its own
        // shading over the vassal's colour after the vassal has been painted. Depth then id keeps
        // the order a property of the tree rather than of dictionary insertion, which is what makes
        // the result the same on every run.
        var states = azgaar.StateTitles
            .Where(s => s.Key > 0)
            .OrderBy(s => Depth(s.Value))
            .ThenBy(s => s.Key)
            .ToList();

        int painted = 0, unpainted = 0;

        foreach (var (stateId, title) in states)
        {
            if (!TryParseColor(azgaar.World.State(stateId)?.Color, out var rgb)) { unpainted++; continue; }

            title.Color = rgb;
            Titles.RecolorChildren(title, rng);
            painted++;
        }

        var stateTitles = new HashSet<Title>(states.Select(s => s.Value));

        foreach (var root in roots)
        {
            if (stateTitles.Contains(root)) continue;
            if (Dominant(root, stateTitles) is not { } member) continue;

            root.Color = Shade(member.Color, EmpireShade);
        }

        if (painted > 0)
            Console.WriteLine($"    painted {painted} realms in azgaar's colours" +
                              (unpainted > 0 ? $" ({unpainted} had none and kept a generated one)" : ""));
    }

    /// <summary>
    /// The state title holding the most baronies anywhere under <paramref name="root"/>.
    ///
    /// Most baronies rather than nearest or first: an empire reads as the country that dominates it,
    /// and on a grouping built from suzerainty that is reliably the suzerain.
    /// </summary>
    private static Title? Dominant(Title root, HashSet<Title> stateTitles)
    {
        Title? best = null;
        int bestSize = -1;

        Walk(root);
        return best;

        void Walk(Title title)
        {
            if (stateTitles.Contains(title))
            {
                int size = Baronies(title);

                // Ties on the key, so two equal-sized states always resolve the same way.
                if (size > bestSize || (size == bestSize && best is not null &&
                                        string.CompareOrdinal(title.Key, best.Key) < 0))
                {
                    best = title;
                    bestSize = size;
                }

                return;
            }

            foreach (var child in title.Children) Walk(child);
        }
    }

    private static int Baronies(Title title)
    {
        if (title.Tier == "b") return 1;

        int total = 0;
        foreach (var child in title.Children) total += Baronies(child);
        return total;
    }

    private static int Depth(Title title)
    {
        int depth = 0;
        for (var parent = title.Parent; parent is not null; parent = parent.Parent) depth++;
        return depth;
    }

    private static (byte R, byte G, byte B) Shade((byte R, byte G, byte B) rgb, double factor)
        => ((byte)Math.Clamp(rgb.R * factor, 0, 255),
            (byte)Math.Clamp(rgb.G * factor, 0, 255),
            (byte)Math.Clamp(rgb.B * factor, 0, 255));

    /// <summary>
    /// Reads one of Azgaar's colour strings.
    ///
    /// Every export measured so far writes plain <c>#rrggbb</c>, but the field holds whatever SVG
    /// fill the state was given, and a user who applies a pattern gets <c>url(#hatch3)</c> there
    /// instead. That is not a colour and cannot be turned into one, so it is refused rather than
    /// guessed at, and the title keeps the generated colour it already had.
    /// </summary>
    internal static bool TryParseColor(string? raw, out (byte R, byte G, byte B) rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var text = raw.Trim();

        if (text.StartsWith('#'))
        {
            var digits = text[1..];

            // #rgb is shorthand for #rrggbb; each digit doubles.
            if (digits.Length == 3)
            {
                if (!Nibble(digits[0], out int r3) || !Nibble(digits[1], out int g3) || !Nibble(digits[2], out int b3))
                    return false;

                rgb = ((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
                return true;
            }

            if (digits.Length != 6) return false;

            if (!byte.TryParse(digits[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
                !byte.TryParse(digits[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
                !byte.TryParse(digits[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return false;

            rgb = (r, g, b);
            return true;
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
        {
            var parts = text[4..^1].Split(',');
            if (parts.Length != 3) return false;

            var channels = new byte[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    return false;

                channels[i] = (byte)Math.Clamp(value, 0, 255);
            }

            rgb = (channels[0], channels[1], channels[2]);
            return true;
        }

        return false;

        static bool Nibble(char c, out int value)
        {
            value = 0;
            if (c >= '0' && c <= '9') { value = c - '0'; return true; }
            if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
            if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
            return false;
        }
    }
}
