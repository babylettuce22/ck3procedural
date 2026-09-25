using Ck3MapGen.Config;
using Ck3MapGen.Io;

namespace Ck3MapGen.Emit;

/// <summary>
/// Keeps a game-start effect that names the start-date rulers out of the earlier bookmarks, where
/// those rulers are children, courtiers or not yet born. A no-op with a single start date, so the
/// file comes out exactly as before.
/// </summary>
internal static class StartGate
{
    /// <summary>Opens <c>if = { limit = { current_date &gt;= start } }</c> when needed; dispose to close it.</summary>
    public static IDisposable? LatestOnly(JominiBuilder b, MapConfig cfg)
    {
        if (cfg.LatestBookmarkGate is not { } date) return null;

        var gate = b.Block("if");
        using (b.Block("limit")) b.Token($"current_date >= {date}");
        return gate;
    }
}
