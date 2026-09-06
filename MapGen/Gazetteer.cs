using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>
/// The real-world place names a generated name must not land on. A county called "Yemen" reads
/// as a mistake however good the rest of the map is, and the generator has no idea it said it:
/// short words in particular fall onto real ones by chance, and the Azgaar Markov chains are
/// trained on real place names to begin with.
///
/// The list is <c>assets/realworld_places.txt</c>, built by <c>tools/make_gazetteer.py</c> from
/// every title, cultural title name, region and culture in the vanilla CK3 localisation plus a
/// hand-kept supplement for the world the game's map stops short of. It is embedded, so the
/// generator never needs the game install for this.
/// </summary>
public static class Gazetteer
{
    private const string Resource = "realworld_places.txt";

    /// <summary>A real name of this many letters or more still reads as itself with a short tail
    /// on it — "Spainus", "Norseaxe", "Tongaka" — so those are caught too. Shorter stems are
    /// left alone: "Kent" begins too many honest names.</summary>
    private const int StemFloor = 5;
    private const int TailMax = 3;

    private static readonly Lazy<HashSet<string>> Names = new(Load);

    public static int Count => Names.Value.Count;

    /// <summary>
    /// Whether a name folded by <see cref="Ascii.Fold(string, bool)"/> is a real place, or a real
    /// place of <see cref="StemFloor"/> letters or more wearing a tail of up to
    /// <see cref="TailMax"/> letters.
    /// </summary>
    public static bool IsRealPlace(string plain)
    {
        var names = Names.Value;
        if (names.Contains(plain)) return true;

        for (int length = Math.Max(StemFloor, plain.Length - TailMax); length < plain.Length; length++)
            if (names.Contains(plain[..length])) return true;

        return false;
    }

    private static HashSet<string> Load()
    {
        using var stream = typeof(Gazetteer).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"Embedded resource {Resource} is missing; check the EmbeddedResource item in the csproj.");
        using var reader = new StreamReader(stream);

        var names = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            string plain = Ascii.Fold(line);
            if (plain.Length >= 3) names.Add(plain);
        }
        return names;
    }
}
