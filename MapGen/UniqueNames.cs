namespace Ck3MapGen.MapGen;

/// <summary>
/// Keeps generated names distinct within one namespace — cultures, faiths, bodies of water — each
/// of which owns its own <c>used</c> set. Callers import it with <c>using static</c>, so the two
/// calls read as they did when every generator carried its own copy.
/// </summary>
internal static class UniqueNames
{
    /// <summary>
    /// Draws a name, redrawing up to 16 times while it is taken, then falls back on
    /// <see cref="Unique"/>. Redrawing first matters: a numeral is the generator showing through,
    /// so it is only the answer once fresh words have run out.
    /// </summary>
    public static string UniqueFrom(Func<string> draw, HashSet<string> used)
    {
        string name = draw();
        for (int attempt = 0; attempt < 16 && used.Contains(name); attempt++) name = draw();
        return Unique(name, used);
    }

    /// <summary>
    /// Claims <paramref name="name"/>, or the first free "name2" … "name99". Past that the name is
    /// returned taken, rather than looping forever on a namespace that has run out.
    /// </summary>
    public static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        for (int suffix = 2; suffix < 100; suffix++)
        {
            string candidate = $"{name}{suffix}";
            if (used.Add(candidate)) return candidate;
        }

        return name;
    }
}
