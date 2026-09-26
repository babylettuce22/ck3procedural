using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps a game-start effect that names the start date's people to the start date's own bookmark.
/// On an additional bookmark before it they are unborn; on one after it they are dead. A no-op with
/// a single start date, so the file comes out exactly as before.
/// </summary>
internal static class StartGate
{
    /// <summary>
    /// Opens <c>if = { limit = { current_date &gt;= start current_date &lt; next } }</c>, with
    /// whichever bounds apply, when any do; dispose to close it.
    /// </summary>
    public static IDisposable? LatestOnly(JominiBuilder b, MapConfig cfg)
    {
        var (from, until) = cfg.MainBookmarkWindow;
        if (from is null && until is null) return null;

        var gate = b.Block("if");
        using (b.Block("limit"))
        {
            if (from is not null) b.Token($"current_date >= {from}");
            if (until is not null) b.Token($"current_date < {until}");
        }
        return gate;
    }
}
