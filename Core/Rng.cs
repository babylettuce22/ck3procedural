namespace Ck3MapGen.Core;

/// <summary>
/// Seeded replacement for ck2rpg's Math.random. xoshiro256** — fast, good quality, and
/// reproducible, so a given seed always yields the same world. The helper names mirror the
/// JS utilities (getRandomInt, pickFrom, ...) to keep ported call sites recognisable.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(int seed) : this((ulong)seed) { }

    public Rng(ulong seed)
    {
        // SplitMix64 to expand the seed into the full state.
        ulong z = seed + 0x9E3779B97F4A7C15UL;
        _s0 = SplitMix(ref z);
        _s1 = SplitMix(ref z);
        _s2 = SplitMix(ref z);
        _s3 = SplitMix(ref z);
    }

    private static ulong SplitMix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong r = z;
        r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
        r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
        return r ^ (r >> 31);
    }

    /// <summary>
    /// A hash of a string that is the same in every process. <c>string.GetHashCode()</c> is not:
    /// .NET randomises string hashing per process, so an Rng seeded from one produces a different
    /// result on every run of the same seed — against the whole point of seeding it. Anything that
    /// keys an Rng off a generated name (a dynasty id, a faith key) has to come through here.
    ///
    /// FNV-1a over the UTF-16 code units. The quality bar is only "spreads keys apart", because
    /// SplitMix64 in the constructor does the actual mixing; what matters is that it is fixed.
    /// </summary>
    public static ulong StableHash(string text)
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL; // FNV-1a 64-bit offset basis

        foreach (char c in text)
        {
            hash = (hash ^ (byte)c) * prime;
            hash = (hash ^ (byte)(c >> 8)) * prime;
        }

        return hash;
    }

    /// <summary>
    /// The stream for one thing in one world: <paramref name="seed"/> is the world's
    /// (<c>MapConfig.Seed</c>), <paramref name="stream"/> names the purpose (the hex constant each
    /// call site already had), <paramref name="key"/> is the thing — a county index — and
    /// <paramref name="salt"/> tells apart draws of the same thing, such as a seat's ruler in an
    /// earlier bookmark.
    ///
    /// Per-thing streams used to be <c>new Rng(county.Index ^ 0x6E19)</c>: independent of every
    /// other draw, which is the point, but also of the world seed, so county 5 got the same family
    /// on every seed — measured, every child count and every child's birthday matched between seeds
    /// 4242 and 991. The parts are also mixed rather than XORed, so two purposes cannot collide on
    /// the county pair whose indices differ by the XOR of their constants.
    /// </summary>
    public static Rng For(int seed, int stream, int key, int salt = 0)
        => For(seed, stream, (ulong)(uint)key, salt);

    /// <inheritdoc cref="For(int, int, int, int)"/>
    /// <remarks>For a thing keyed by name: pass <see cref="StableHash"/> of its key.</remarks>
    public static Rng For(int seed, int stream, ulong key, int salt = 0)
    {
        ulong h = Finalise((ulong)(uint)seed + 0x9E3779B97F4A7C15UL);
        h = Finalise(h ^ (uint)stream);
        h = Finalise(h ^ key);
        h = Finalise(h ^ (uint)salt);
        return new Rng(h);
    }

    private static ulong Finalise(ulong r)
    {
        r = (r ^ (r >> 30)) * 0xBF58476D1CE4E5B9UL;
        r = (r ^ (r >> 27)) * 0x94D049BB133111EBUL;
        return r ^ (r >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0, 1). Equivalent to Math.random().</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Port of getRandomInt(min, max) — inclusive on both ends.</summary>
    public int Int(int min, int max)
    {
        if (max < min) (min, max) = (max, min);
        ulong span = (ulong)((long)max - min + 1);
        return min + (int)(NextUInt64() % span);
    }

    /// <summary>Uniform double in [0, 1). Alias for NextDouble().</summary>
    public double Double() => NextDouble();

    /// <summary>Uniform double in [min, max).</summary>
    public double Double(double min, double max) => NextDouble() * (max - min) + min;

    /// <summary>Uniform float in [0, 1).</summary>
    public float Float() => (float)NextDouble();

    /// <summary>Uniform float in [min, max).</summary>
    public float Float(float min, float max) => (float)NextDouble() * (max - min) + min;

    /// <summary>Port of getRandomDecimal(min, max) — two decimal places, as the JS does.</summary>
    public double Decimal(double min, double max)
        => Math.Round(NextDouble() * (max - min) + min, 2);

    /// <summary>Port of pickFrom(arr).</summary>
    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    /// <summary>
    /// Index of a pick proportional to <paramref name="weight"/>, using one <see cref="Int"/> draw.
    /// Negative weights count as zero. When no weight is positive, returns -1 *without drawing*, so
    /// each caller keeps its own fallback — and the stream stays where that fallback expects it.
    /// </summary>
    public int WeightedIndex<T>(IReadOnlyList<T> items, Func<T, int> weight)
    {
        int total = 0;
        foreach (var item in items) total += Math.Max(0, weight(item));
        if (total <= 0) return -1;

        int roll = Int(0, total - 1);
        for (int i = 0; i < items.Count; i++)
        {
            roll -= Math.Max(0, weight(items[i]));
            if (roll < 0) return i;
        }

        return items.Count - 1;
    }

    /// <summary>True with probability <paramref name="p"/>.</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>In-place Fisher-Yates shuffle.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Int(0, i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}