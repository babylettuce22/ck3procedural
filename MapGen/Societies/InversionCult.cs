using Ck3MapGen.Config;
using Ck3MapGen.Core;

namespace Ck3MapGen.MapGen;

/// <summary>A cult member sworn at the start date. <see cref="Rank"/> is trait XP on the rank track (0-100).</summary>
public sealed record CultMember(string Id, int Rank);

/// <summary>
/// One doctrine the Unveiling turns inside out: the group, what the host faith holds there, and what
/// it holds after. Read off the host faith at generation time, so the inversion is of THIS faith.
/// </summary>
public sealed record CultInversion(string Group, string From, string To);

/// <summary>
/// Everything a world's inversion cult is made of. The principle, agreed for the society system on
/// 2026-08-24 and kept: the cult is derived by INVERTING its host, and the generator already made
/// everything it inverts — the host faith's devil, its sins, its doctrines, its holy tongue.
///
/// <list type="bullet">
/// <item>host: the largest generated faith that names a devil, preferring one with a head of faith, then a monotheist one</item>
/// <item>what it serves: that faith's own <c>DevilName</c>, by loc key — never invented separately</item>
/// <item>who it wants: the host religion's sins, as trait keys (a sinner is a likely recruit)</item>
/// <item>what it will do if it wins: the host's sin doctrines inverted, one group at a time</item>
/// <item>its name: the devil's name, or a word of the host's liturgical language</item>
/// </list>
/// </summary>
public sealed class CultPlan
{
    public required Faith Host { get; init; }

    /// <summary>The loc key the host religion names its devil with, for <c>$key$</c> substitution.</summary>
    public required string DevilKey { get; init; }

    /// <summary>The devil's name as written, for the run log only — prose uses <see cref="DevilKey"/>.</summary>
    public required string DevilName { get; init; }

    /// <summary>The host religion's sins: trait keys. The cult's virtues.</summary>
    public required List<string> Sins { get; init; }

    public required List<CultInversion> Inversions { get; init; }

    /// <summary>A word of the host's holy tongue, for the pattern that names the cult in it.</summary>
    public required string HolyWord { get; init; }

    public required int NamePattern { get; init; }

    public List<CultMember> Members { get; } = [];

    /// <summary>The cult's leader. Always also in <see cref="Members"/>.</summary>
    public required string HierophantId { get; init; }
}

public static class InversionCult
{
    /// <summary>A faith smaller than this is a sect, not a church worth hollowing out.</summary>
    private const int MinCounties = 8;

    /// <summary>
    /// The doctrine groups the Unveiling inverts, and what each becomes. Sins against the host's
    /// morality become its virtues where CK3 has a virtuous form (witchcraft, deviancy), and are
    /// otherwise simply allowed. Homosexuality is deliberately absent: vanilla treats it as a matter of
    /// a faith's tolerance, not of wickedness, and folding it into devil-worship would say otherwise.
    /// </summary>
    internal static readonly (string Group, string To, string[] Invert)[] InversionTable =
    [
        ("doctrine_witchcraft", "doctrine_witchcraft_virtuous",
            ["doctrine_witchcraft_crime", "doctrine_witchcraft_shunned", "doctrine_witchcraft_accepted"]),
        ("doctrine_deviancy", "doctrine_deviancy_virtuous",
            ["doctrine_deviancy_crime", "doctrine_deviancy_shunned", "doctrine_deviancy_accepted"]),
        ("doctrine_kinslaying", "doctrine_kinslaying_accepted",
            ["doctrine_kinslaying_any_dynasty_member_crime", "doctrine_kinslaying_extended_family_crime",
             "doctrine_kinslaying_close_kin_crime", "doctrine_kinslaying_shunned"]),
        ("doctrine_adultery_men", "doctrine_adultery_men_accepted",
            ["doctrine_adultery_men_crime", "doctrine_adultery_men_shunned"]),
        ("doctrine_adultery_women", "doctrine_adultery_women_accepted",
            ["doctrine_adultery_women_crime", "doctrine_adultery_women_shunned"]),
        ("doctrine_consanguinity", "doctrine_consanguinity_unrestricted",
            ["doctrine_consanguinity_restricted", "doctrine_consanguinity_cousins"]),
    ];

    /// <summary>
    /// Picks the host and builds the cult, or returns null when no faith fits. <paramref name="taken"/>
    /// is everyone already sworn to another society (one society per character, as in CK2).
    /// </summary>
    public static CultPlan? Build(FaithMap faiths, RulerMap rulers, IReadOnlySet<string> taken, MapConfig cfg,
        RealmMap realms, WildernessMap wilderness)
    {
        var rng = Rng.For(cfg.Seed, 0xC017, 1, cfg.PeopleSalt);

        static string? DevilKeyOf(Religion r)
            => r.Localization.FirstOrDefault(t => t.Tag == "DevilName").Value is { Length: > 0 } key
               && r.LocalizationText.ContainsKey(key) ? key : null;

        // A faith with a head first: the cult's middle phase IS its head of faith sworn to the devil
        // ("hollow"), so a headless host skips the heart of the story -- the rot can still carry it
        // to the Unveiling, but only as the fallback when no faith that names a devil has a head.
        // Heads are drawn at random (Faiths.OrganizeAndMintHeads), so a world may have none.
        // Then monotheist: a single god with a single adversary is the shape an inversion needs. A
        // pagan religion that still names a devil will do when there is no monotheist one.
        var host = faiths.Faiths
            .Where(f => !f.Inherited && !f.Religion.Inherited && f.Counties.Count >= MinCounties
                        && DevilKeyOf(f.Religion) is not null)
            .OrderByDescending(f => f.Head is not null && f.IsOrganized)
            .ThenByDescending(f => f.Religion.Monotheist)
            .ThenByDescending(f => f.Counties.Count)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .FirstOrDefault();
        if (host is null) return null;

        string devilKey = DevilKeyOf(host.Religion)!;
        var sins = host.Religion.Sins.ToList();

        var inversions = new List<CultInversion>();
        foreach (var (group, to, invert) in InversionTable)
        {
            string held = host.DoctrineOf(group);
            if (invert.Contains(held)) inversions.Add(new CultInversion(group, held, to));
        }

        // ---- The first of the faithless ------------------------------------------------------
        //
        // Rulers of the host faith whose own natures already lean the cult's way: the religion's sins
        // among their traits, a cynic's indifference, a liar's ease. Never the zealous, never the head
        // of the faith, never someone sworn elsewhere.
        // The head of faith is the prize, never a founder: a spiritual head holds the head title as
        // their primary; a temporal one holds it beside their own crown, and HistoryWriter decides who
        // that is -- ask it rather than re-derive it, or the two could disagree.
        string? temporalHead = Emit.HistoryWriter.TemporalHeadHolder(host, realms, faiths, wilderness);

        var scored = new List<(Ruler R, double Score)>();
        foreach (var r in rulers.All)
        {
            if (r.Faith.Key != host.Key || taken.Contains(r.Id)) continue;
            if (host.Head is { } head && r.PrimaryTitle.Key == head.TitleKey) continue;
            if (r.Id == temporalHead) continue;
            var traits = r.Profile.PersonalityTraits;
            if (traits.Contains("zealous")) continue;

            double sc = 2.0 * traits.Count(sins.Contains);
            if (traits.Contains("cynical")) sc += 1.5;
            if (traits.Contains("deceitful")) sc += 1;
            if (traits.Contains("ambitious")) sc += 0.5;
            if (sc <= 0) continue;
            sc += r.Profile.Intrigue / 10.0 + rng.NextDouble();
            scored.Add((r, sc));
        }

        if (scored.Count == 0) return null;

        int wanted = Math.Clamp(host.Counties.Count / 10, 4, 8);
        var sworn = scored.OrderByDescending(x => x.Score).ThenBy(x => x.R.Seat.Index).Take(wanted).ToList();

        // The Hierophant is the cleverest of them, not the most sinful: somebody has to keep the rest
        // alive.
        var hierophant = sworn.OrderByDescending(x => x.R.Profile.Intrigue + x.R.Profile.Learning)
            .ThenBy(x => x.R.Seat.Index).First().R;

        var plan = new CultPlan
        {
            Host = host,
            DevilKey = devilKey,
            DevilName = host.Religion.LocalizationText[devilKey],
            Sins = sins,
            Inversions = inversions,
            HolyWord = host.Religion.Language.Word(rng, 2, 3),
            NamePattern = rng.Int(0, 4),
            HierophantId = hierophant.Id,
        };

        foreach (var (r, _) in sworn)
            plan.Members.Add(new CultMember(r.Id, r.Id == hierophant.Id ? 80 : rng.Int(5, 55)));

        return plan;
    }
}
