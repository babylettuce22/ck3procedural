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

    /// <summary>Port of getRandomDecimal(min, max) — two decimal places, as the JS does.</summary>
    public double Decimal(double min, double max)
        => Math.Round(NextDouble() * (max - min) + min, 2);

    /// <summary>Port of pickFrom(arr).</summary>
    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    /// <summary>True with probability <paramref name="p"/>.</summary>
    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Port of subsetOf(arr) — keeps each element ~1/3 of the time, never returns empty.</summary>
    public List<T> Subset<T>(IReadOnlyList<T> items)
    {
        var result = new List<T>();
        for (int i = 0; i < items.Count; i++)
            if (Int(0, 2) == 1) result.Add(items[i]);
        if (result.Count == 0 && items.Count > 0) result.Add(Pick(items));
        return result;
    }

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
