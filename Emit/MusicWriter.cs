// Emit/MusicWriter.cs
namespace Ck3MapGen.Emit;

using Ck3MapGen.Io;

/// <summary>
/// Lets a generated culture's clothing choose its mood music, and stops the Dynastic Cycle from
/// choosing it for everyone.
///
/// <code>
/// Related:
///   MapGen/ClothingClimate.cs     why a culture wears what it wears (climate fit, kin first)
///   Emit/HegemonyFlavourWriter    the same token-rewrite technique, on the hegemony's art
///   Emit/DynasticCycleWriter      the situation whose music branch this removes
/// </code>
///
/// ---- How CK3 picks music ----
///
/// Two layers. <b>Cues</b> are fired by script — <c>play_music_cue</c> on a death, a war, an
/// activity's locale — and this writer leaves them alone. <b>Mood tracks</b> are the long pieces
/// the engine picks by itself, and nearly every one sits in a <c>group_*</c> block in
/// <c>music/in_game/*.txt</c> whose <c>is_valid</c> gates the whole pool. Those gates are the
/// scripted triggers in <c>common/scripted_triggers/music_triggers.txt</c>, which this overrides.
///
/// ---- What was wrong on a generated map ----
///
/// Every pool trigger keys on vanilla heritage pillars, religions and <c>world_*</c> regions. A
/// generated culture carries <c>heritage_gen_N</c> and a generated faith, so almost every test
/// was false and almost every ruler heard the base pool — except where one of two accidents fired:
///
/// * <b>The Dynastic Cycle.</b> The Chinese and Asian triggers both accept
///   <c>any_character_situation = { situation_type = dynastic_cycle }</c>, and the Chinese one is
///   "bespoke": <c>should_not_use_bespoke_music_trigger</c> then shuts off the base, Kingdom Come,
///   and four expansion pools. Our cycle's core sub-region is h_china's de jure extent — the whole
///   map — and its <c>other_rulers</c> group takes every independent ruler (everyone at all, once
///   h_china has no holder). So an independent anywhere heard only Chinese and Asian music, a count
///   under a non-hegemon king heard only the base pool, and independence flipped between the two.
/// * <b>Building gfx.</b> The Asian trigger also accepts five <c>has_building_gfx</c> tests. A
///   culture keeps its heritage's buildings when <see cref="MapGen.ClothingClimate"/> re-dresses it
///   for its climate, so a cold-country cousin of a Chinese-looking heritage heard Asian music
///   while dressed for the north.
///
/// ---- The fix ----
///
/// Both accidents are rewritten to <c>always = no</c>. The cycle itself is untouched — this only
/// ends its say over the music. Then each pool's top-level <c>OR</c> gains one arm asking what the
/// culture wears (<see cref="Pools"/>). <c>has_clothing_gfx</c> is satisfied by membership anywhere
/// in the culture's fallback chain, which is why several arms carry a <c>NOR</c>: vanilla dresses
/// East Slavs as <c>{ east_slavic northern }</c> and Koreans as <c>{ korean chinese }</c>, and
/// without the exclusion a Rus culture would hear Norse music and a Korean one would be locked
/// into China's. Each exclusion mirrors a heritage vanilla's own trigger leaves out.
///
/// Every vanilla arm stays as written, so a pool still opens for anything it opened for before.
/// The additions are purely additive except for the two rewrites above.
///
/// ---- What is left as it was ----
///
/// * <c>should_use_european_christian_music_trigger</c> requires <c>christianity_religion</c> as a
///   hard AND. No generated faith is that religion, and the pool is sacred music, so it stays shut
///   rather than being opened by dress.
/// * The <c>steppe_building_gfx</c> branch of the Asian trigger. It is paired with a heritage or a
///   north-east-Asian capital, and <c>world_steppe_east</c> is a region Steppe.cs deliberately
///   emits under vanilla's key, so it is a designed hook rather than an accident.
/// * Cues. The court, tournament and wedding cues pick by <c>has_graphical_*_culture_group_trigger</c>,
///   which tests building gfx — so a re-dressed culture's court cue can still come from its
///   heritage's region while its mood music follows its dress.
///
/// One consumer outside the music system reads a trigger this touches: the grand hunt's horn in
/// <c>hunt_events.txt</c> picks its All Under Heaven variant by <c>should_use_asian_music_trigger</c>,
/// and now follows dress with the rest.
///
/// The file is re-cut from the installed game on every generate, like HegemonyFlavourWriter's, and
/// <see cref="VanillaPatch"/> ships nothing if any anchor has moved.
/// </summary>
public static class MusicWriter
{
    /// <summary>One way to be dressed for a pool: any of these chains, and none of those.</summary>
    private sealed record Dress(string[] AnyOf, string[] NoneOf);

    private static Dress Wears(params string[] anyOf) => new(anyOf, []);

    /// <summary>
    /// Cultures whose dress falls back to <c>northern</c> without being Norse. Vanilla's Norse
    /// trigger takes North Germanic heritage only, and its broad European one leaves out East Slavs,
    /// Finnic and Ugro-Permian peoples alike.
    /// </summary>
    private static readonly string[] NorthernButNotNorse =
        ["east_slavic_clothing_gfx", "ugro_permian_clothing_gfx", "sami_clothing_gfx"];

    /// <summary>
    /// Each pool trigger, the expansion whose pool it opens (for the log only), and the dress that
    /// now opens it too. Derived from the chains vanilla's own cultures wear, against the heritages
    /// each trigger names.
    /// </summary>
    private static readonly (string Trigger, string Pool, Dress[] Arms)[] Pools =
    [
        ("should_use_norse_music_trigger", "Northern Lords",
            [new(["northern_clothing_gfx"], NorthernButNotNorse)]),

        ("should_use_iberian_music_trigger", "Fate of Iberia",
            [Wears("iberian_christian_clothing_gfx", "iberian_muslim_clothing_gfx")]),

        ("should_use_iranian_music_trigger", "Legacy of Persia",
            [Wears("iranian_clothing_gfx")]),

        ("should_use_byzantine_music_trigger", "Roads to Power",
            [Wears("byzantine_clothing_gfx")]),

        // Mongol dress is also the fallback under Tangut and Nivkh (and Jurchen, whose chain runs
        // through Nivkh). Vanilla makes those peoples Asian rather than nomadic, so they are sent
        // to the Asian arm below instead. Khitan, Uyghur and Turkic dress stay nomadic, as their
        // Mongolic and Turkic heritages are in vanilla.
        ("should_use_nomadic_music_trigger", "Khans of the Steppe",
            [new(["mongol_clothing_gfx"], ["tangut_clothing_gfx", "nivkh_clothing_gfx"])]),

        // Bespoke: this pool shuts the generic ones off. Korean and Viet dress fall back to
        // Chinese; vanilla gives those heritages the Asian pool, not the exclusive Chinese one.
        ("should_use_chinese_music_trigger", "All Under Heaven (Chinese)",
            [new(["chinese_clothing_gfx"], ["korean_clothing_gfx", "viet_clothing_gfx"])]),

        // The dress-side twin of the five building tests rewritten away below: every chain vanilla
        // puts on a culture with Chinese, Japanese, Indian, South-East Asian or Tibetan buildings,
        // plus the two taken out of the nomadic arm. Chains cover their narrow heads — chinese
        // covers korean, viet, dali; southeast_asian covers malay, tai, papuan; ainu covers emishi.
        ("should_use_asian_music_trigger", "All Under Heaven",
            [Wears("chinese_clothing_gfx", "japanese_clothing_gfx", "ainu_clothing_gfx",
                   "indian_clothing_gfx", "southeast_asian_clothing_gfx",
                   "tangut_clothing_gfx", "nivkh_clothing_gfx")]),

        // Western dress covers every Frankish, English, German, Iberian-Christian and West Slavic
        // chain. Plain northern dress is the Norse and Gaelic look, both on vanilla's list.
        ("should_use_broadly_european_music_trigger", "Kingdom Come",
            [Wears("western_clothing_gfx"), new(["northern_clothing_gfx"], NorthernButNotNorse)]),
    ];

    /// <summary>
    /// Tests in the vanilla file that answered the music question for reasons other than dress.
    /// Each is rewritten to <c>always = no</c>, which inside an <c>OR</c> simply removes the arm.
    /// </summary>
    private static readonly (string Name, string Test)[] Retired =
    [
        ("dynastic cycle branch", "any_character_situation = { situation_type = dynastic_cycle }"),
        ("chinese buildings", "has_building_gfx = chinese_building_gfx"),
        ("indian buildings", "has_building_gfx = indian_building_gfx"),
        ("south-east asian buildings", "has_building_gfx = southeast_asian_building_gfx"),
        ("tibetan buildings", "has_building_gfx = tibetan_building_gfx"),
        ("japanese buildings", "has_building_gfx = japanese_building_gfx"),
    ];

    public static void WriteAll(string modDir, string gameDir)
    {
        var patch = VanillaPatch.Open(gameDir, "music pools", "common", "scripted_triggers", "music_triggers.txt");
        if (patch is null) return;

        foreach ((string name, string test) in Retired)
            patch.ReplaceEvery(name, test, "always = no");

        // Anchored on the definition (`= {`, never the `= yes` references in the bespoke and
        // Asian guards), then its first OR — which in every one of these triggers is the
        // top-level one, the broad European trigger's leading NOT included.
        foreach ((string trigger, string pool, Dress[] arms) in Pools)
            patch.InsertAfter($"{trigger} OR", "\n" + DressArms(pool, arms).TrimEnd('\n'),
                $"{trigger} = {{", "OR = {");

        patch.Ship(modDir);
    }

    /// <summary>
    /// The arms for one pool, written at the depth of the <c>OR</c> they are spliced into.
    /// </summary>
    private static string DressArms(string pool, Dress[] arms)
    {
        var b = new JominiBuilder(startDepth: 2);
        b.Comment($"Generated map: dressed for the {pool} pool (see Emit/MusicWriter.cs).");

        foreach (Dress arm in arms)
        {
            using (b.Block("culture"))
            {
                if (arm.AnyOf.Length == 1)
                    b.Field("has_clothing_gfx", arm.AnyOf[0]);
                else
                    using (b.Block("OR"))
                        foreach (string gfx in arm.AnyOf) b.Field("has_clothing_gfx", gfx);

                if (arm.NoneOf.Length > 0)
                    using (b.Block("NOR"))
                        foreach (string gfx in arm.NoneOf) b.Field("has_clothing_gfx", gfx);
            }
        }

        return b.ToString();
    }
}
