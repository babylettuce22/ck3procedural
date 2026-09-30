namespace Ck3MapGen.MapGen;

/// <summary>
/// Which vanilla headgear clashes with horns, and what a horned head does about it. Measured offline
/// 2026-09-30 with ck3devtools/portrait_render (hats_vs_horns sweep): every headgear mesh rendered with
/// full ibex, ram and forward horns, front and three-quarter, worst case kept. A hat "clashes" when the
/// horns keep under 35% of their visible area (buried) or overdraw 8% or more of the hat (clipping
/// through it). The full table, with the numbers, is ck3devtools/portrait_render/out/horned_headgear.tsv.
///
/// Applied by <see cref="Emit.RaceHeadWriter"/> (WriteHornHeadgear): each listed accessory is copied from
/// the installed game with one tag added, and the horn and ornament accessories read those tags before
/// their own headgear rules. Crowns are not listed — they already swap to a band. Keyed by accessory
/// name, so a patch that adds hats leaves them on the ordinary rules until the sweep is rerun.
/// </summary>
public static class HornHeadgear
{
    /// <summary>The tag that gives full horns whatever else the hat's tags say.</summary>
    public const string FullTag = "gen_horns_full";

    /// <summary>The tag that gives filed stumps.</summary>
    public const string StumpTag = "gen_horns_stump";

    /// <summary>
    /// Clashing hats that leave the hair alone: on a horned head the hat is not drawn, so the horns show
    /// in full over the character's own hair (195).
    /// </summary>
    public static readonly HashSet<string> Bare = new(StringComparer.Ordinal)
    {
        "f_headgear_ccp5_french_war_nob_01_com_coif", "f_headgear_ccp5_french_war_nob_02_com_coif",
        "f_headgear_ccp5_french_war_nob_03_com_coif", "f_headgear_ccp5_french_war_nob_04_com_coif",
        "f_headgear_ccp5_western_war_nob_crestEra3_01", "f_headgear_ccp5_western_war_nob_crestEra3_02",
        "f_headgear_ccp5_western_war_nob_crest_01", "f_headgear_ccp5_western_war_nob_crest_02",
        "f_headgear_rel_ccp4_siberian_com_01", "f_headgear_rel_tgp_shinto_priest",
        "f_headgear_sec_afr_common_01", "f_headgear_sec_afr_common_02", "f_headgear_sec_ccp4_sami_com_01",
        "f_headgear_sec_ccp_nivkh_com_01", "f_headgear_sec_crusades_mena_war_nob_01_hi",
        "f_headgear_sec_crusades_mena_war_nob_01_lo", "f_headgear_sec_dde_hre_war_nob_01_hi",
        "f_headgear_sec_dde_hre_war_nob_01_lo", "f_headgear_sec_dde_hre_war_nob_01_roy",
        "f_headgear_sec_ep2_western_era1_war_nob_01", "f_headgear_sec_ep3_byzantine_era1_hi_nob_01",
        "f_headgear_sec_ep3_byzantine_era1_lo_nob_01", "f_headgear_sec_ep3_byzantine_era1_war_nob_01",
        "f_headgear_sec_ep3_byzantine_era2_hi_nob_01", "f_headgear_sec_ep3_byzantine_era2_nob_01",
        "f_headgear_sec_fp3_iranian_imp_01", "f_headgear_sec_fp3_iranian_nob_01",
        "f_headgear_sec_fp3_iranian_nob_02", "f_headgear_sec_fp4_western_era3_com_01",
        "f_headgear_sec_fp4_western_era3_com_02", "f_headgear_sec_fp4_western_nob_01_com",
        "f_headgear_sec_fp4_western_nob_01_lo", "f_headgear_sec_mena_war_nob_01_hi",
        "f_headgear_sec_mena_war_nob_01_lo", "f_headgear_sec_mpo_mongol_com_01",
        "f_headgear_sec_northern_war_nob_01_hi", "f_headgear_sec_northern_war_nob_01_lo",
        "f_headgear_sec_pol_era3_war_crest_01", "f_headgear_sec_pol_era4_war_crown_01",
        "f_headgear_sec_pol_high_nob_01", "f_headgear_sec_pol_low_nob_01",
        "f_headgear_sec_sp3_western_hi_nob_01", "f_headgear_sec_sp5_byzantine_roy_01",
        "f_headgear_sec_tgp_chinese_hi_nob_02", "f_headgear_sec_tgp_japanese_war_nob_01",
        "f_headgear_sec_tgp_japanese_war_nob_02_hi", "f_headgear_sec_tgp_japanese_war_nob_02_lo",
        "f_headgear_sp4_western_roy_01", "f_headgear_spec_ep2_mena_wedding",
        "f_headgear_spec_ep2_western_wedding", "female_headgear_religious_african_high_01",
        "female_headgear_religious_catholic_high_01", "female_headgear_religious_zoroastrian_high_01",
        "female_headgear_secular_byzantine_royalty_01", "female_headgear_secular_ep1_diamond_crown",
        "female_headgear_secular_ep1_indian_royalty_01", "female_headgear_secular_ep1_jester_01",
        "female_headgear_secular_ep1_mena_royalty_01", "female_headgear_secular_ep1_mena_royalty_02",
        "female_headgear_secular_ep1_sassanid_crown", "female_headgear_secular_fp1_common_01",
        "female_headgear_secular_fp1_common_02", "female_headgear_secular_fp1_nobility_01",
        "female_headgear_secular_fp1_nobility_03", "female_headgear_secular_fp1_war_nobility_01",
        "female_headgear_secular_fp2_iberian_christian_high_nobility_01",
        "female_headgear_secular_fp2_iberian_christian_nobility_01_high",
        "female_headgear_secular_fp2_iberian_christian_nobility_01_low",
        "female_headgear_secular_fp2_iberian_muslim_common_01",
        "female_headgear_secular_fp2_iberian_muslim_nobility_01",
        "female_headgear_secular_indian_high_nobility_01", "female_headgear_secular_indian_nobility_01",
        "female_headgear_secular_jester_01", "female_headgear_secular_mena_common_02",
        "female_headgear_secular_northern_common_01", "female_headgear_secular_western_nobility_03",
        "m_headgear_ccp5_french_war_nob_01_com_coif", "m_headgear_ccp5_french_war_nob_02_com_coif",
        "m_headgear_ccp5_french_war_nob_03_com_coif", "m_headgear_ccp5_french_war_nob_04_com_coif",
        "m_headgear_ccp5_western_war_nob_crestEra3_01", "m_headgear_ccp5_western_war_nob_crestEra3_02",
        "m_headgear_ccp5_western_war_nob_crest_01", "m_headgear_ccp5_western_war_nob_crest_02",
        "m_headgear_rel_ccp4_siberian_com_01", "m_headgear_sec_ccp4_sami_com_01",
        "m_headgear_sec_ccp4_sami_hi_nob_01", "m_headgear_sec_ccp4_sami_hi_nob_02",
        "m_headgear_sec_ccp4_sami_hi_nob_03", "m_headgear_sec_ccp4_sami_lo_nob_01",
        "m_headgear_sec_ccp4_sami_lo_nob_02", "m_headgear_sec_ccp4_sami_lo_nob_03",
        "m_headgear_sec_ccp_nivkh_com_01", "m_headgear_sec_dde_hre_war_nob_01_hi",
        "m_headgear_sec_dde_hre_war_nob_01_lo", "m_headgear_sec_dde_hre_war_nob_01_roy",
        "m_headgear_sec_ep2_western_era1_com_01", "m_headgear_sec_ep2_western_era1_imp_01",
        "m_headgear_sec_ep2_western_era1_nob_01", "m_headgear_sec_ep2_western_era1_war_nob_01",
        "m_headgear_sec_ep3_byzantine_era1_com_01", "m_headgear_sec_ep3_byzantine_era1_war_nob_01",
        "m_headgear_sec_ep3_byzantine_era2_imp_01", "m_headgear_sec_ep3_byzantine_era2_nob_01",
        "m_headgear_sec_ep3_byzantine_era2_nob_02", "m_headgear_sec_ep3_byzantine_era2_roy_01",
        "m_headgear_sec_fp3_iranian_imp_01", "m_headgear_sec_fp3_iranian_roy_01",
        "m_headgear_sec_fp3_turkic_nob_01", "m_headgear_sec_fp3_turkic_nob_02",
        "m_headgear_sec_fp4_western_era3_com_01", "m_headgear_sec_fp4_western_era3_com_02",
        "m_headgear_sec_fp4_western_nob_01_com", "m_headgear_sec_fp4_western_nob_01_lo",
        "m_headgear_sec_fp4_western_nob_02_com", "m_headgear_sec_fp4_western_nob_02_lo",
        "m_headgear_sec_mena_war_nob_01_hi", "m_headgear_sec_mena_war_nob_01_lo",
        "m_headgear_sec_mpo_mongol_com_01", "m_headgear_sec_mpo_mongol_imp_01",
        "m_headgear_sec_mpo_mongol_nob_01", "m_headgear_sec_pol_common_01", "m_headgear_sec_pol_common_02",
        "m_headgear_sec_pol_era3_war_crest_01", "m_headgear_sec_pol_era4_war_crown_01",
        "m_headgear_sec_pol_high_nob_01", "m_headgear_sec_pol_low_nob_01",
        "m_headgear_sec_sp2_western_hi_nob_01", "m_headgear_sec_sp3_western_hi_nob_01",
        "m_headgear_sec_tgp_chinese_com_01", "m_headgear_sec_tgp_chinese_com_02",
        "m_headgear_sec_tgp_chinese_hi_nob_01", "m_headgear_sec_tgp_chinese_imp_02",
        "m_headgear_sec_tgp_chinese_nob_01", "m_headgear_sec_tgp_chinese_nob_02",
        "m_headgear_sec_tgp_chinese_roy_01", "m_headgear_sec_tgp_japanese_com_01",
        "m_headgear_sec_tgp_japanese_imp_01", "m_headgear_sec_tgp_japanese_imp_02",
        "m_headgear_sec_tgp_japanese_roy_01", "m_headgear_sec_tgp_japanese_war_nob_01",
        "m_headgear_sec_tgp_japanese_war_nob_02_hi", "m_headgear_sec_tgp_japanese_war_nob_02_lo",
        "m_headgear_sec_tgp_korean_com_01", "m_headgear_sec_tgp_korean_imp_01",
        "m_headgear_sec_tgp_korean_nob_01", "m_headgear_sec_tgp_korean_nob_02",
        "m_headgear_sec_tgp_southeast_nob_01", "m_headgear_sec_tgp_southeast_roy_01_a",
        "m_headgear_sec_tgp_southeast_roy_01_b", "m_headgear_sp4_mena_roy_01", "m_headgear_sp4_rus_roy_01",
        "m_headgear_spec_ep2_western_wedding", "male_headgear_religious_catholic_head_01",
        "male_headgear_religious_catholic_high_01", "male_headgear_religious_jewish_high_01",
        "male_headgear_religious_zorastrian_high_01", "male_headgear_secular_byzantine_common_01",
        "male_headgear_secular_byzantine_high_nobility_01", "male_headgear_secular_byzantine_nobility_01",
        "male_headgear_secular_byzantine_royalty_01", "male_headgear_secular_dde_abbasid_imperial_01",
        "male_headgear_secular_dde_abbasid_royalty_02", "male_headgear_secular_dde_hre_common_01",
        "male_headgear_secular_dde_hre_common_02", "male_headgear_secular_dde_hre_imperial_01",
        "male_headgear_secular_dde_hre_nobility_02", "male_headgear_secular_ep1_diamond_crown",
        "male_headgear_secular_ep1_indian_royalty_01", "male_headgear_secular_ep1_mena_royalty_02",
        "male_headgear_secular_ep1_sassanid_crown", "male_headgear_secular_fp1_common_01",
        "male_headgear_secular_fp1_common_02", "male_headgear_secular_fp1_nobility_01",
        "male_headgear_secular_fp1_war_nobility_01", "male_headgear_secular_fp2_iberian_christian_common_01",
        "male_headgear_secular_fp2_iberian_christian_high_nobility_01_high",
        "male_headgear_secular_fp2_iberian_christian_high_nobility_01_low",
        "male_headgear_secular_fp2_iberian_christian_nobility_01_high",
        "male_headgear_secular_fp2_iberian_christian_nobility_01_low",
        "male_headgear_secular_fp2_iberian_muslim_common_01_high",
        "male_headgear_secular_fp2_iberian_muslim_common_01_low",
        "male_headgear_secular_fp2_iberian_muslim_high_nobility_01", "male_headgear_secular_indian_common_01",
        "male_headgear_secular_indian_high_nobility_01", "male_headgear_secular_mena_common_01",
        "male_headgear_secular_mena_common_01_low", "male_headgear_secular_mena_imperial_01",
        "male_headgear_secular_mena_nobility_01", "male_headgear_secular_mena_royalty_01",
        "male_headgear_secular_northern_common_01", "male_headgear_secular_rtt_nobility_01_high",
        "male_headgear_secular_rtt_nobility_01_low", "male_headgear_secular_steppe_common_01",
        "male_headgear_secular_western_common_01",
    };

    /// <summary>
    /// Clashing hats that hide the hair (hoods, turbans, mitres) and show full horns today: the hat stays
    /// and the horns become filed stumps — removing it would leave the head bald (8).
    /// </summary>
    public static readonly HashSet<string> Stump = new(StringComparer.Ordinal)
    {
        "f_headgear_sec_ccp_emishi_war_nob_01", "f_headgear_sec_tgp_southeast_roy_01",
        "female_headgear_secular_dde_abbasid_high_nobility_02", "female_headgear_special_head_bandage_01",
        "m_headgear_sec_ccp_emishi_war_nob_01", "m_headgear_sec_dde_abbasid_war_nob_01_lo",
        "m_headgear_sec_ep3_byzantine_era1_hi_nob_01", "male_headgear_special_head_bandage_01",
    };

    /// <summary>
    /// Hats tagged snug or enclosed, so stumped or hidden today, that measured clear of full horns: they
    /// get full horns (95).
    /// </summary>
    public static readonly HashSet<string> Full = new(StringComparer.Ordinal)
    {
        "f_headgear_ccp5_english_war_nob_04", "f_headgear_ccp5_english_war_nob_04_coa",
        "f_headgear_ccp5_english_war_nob_04_hi", "f_headgear_ccp5_french_war_nob_03",
        "f_headgear_ccp5_french_war_nob_03_coa", "f_headgear_ccp5_french_war_nob_03_hi",
        "f_headgear_ccp5_german_war_nob_01", "f_headgear_ccp5_german_war_nob_01_coa",
        "f_headgear_ccp5_german_war_nob_01_hi", "f_headgear_ccp5_swabian_war_nob_01",
        "f_headgear_ccp5_swabian_war_nob_01_coa", "f_headgear_ccp5_swabian_war_nob_02",
        "f_headgear_ccp5_swabian_war_nob_02_coa", "f_headgear_ccp5_swabian_war_nob_02_hi",
        "f_headgear_ccp5_western_war_nob_01", "f_headgear_sec_afr_high_nob_01",
        "f_headgear_sec_afr_imperial_01", "f_headgear_sec_afr_royalty_01", "f_headgear_sec_afr_war_nob_01",
        "f_headgear_sec_ep2_byzantine_war_nob_01", "f_headgear_sec_ep2_indian_war_nob_01",
        "f_headgear_sec_ep2_western_era4_war_nob_02", "f_headgear_sec_ep2_western_era4_war_nob_02_01",
        "f_headgear_sec_ep2_western_war_nob_01_com", "f_headgear_sec_ep2_western_war_nob_01_hi",
        "f_headgear_sec_ep2_western_war_nob_01_lo", "f_headgear_sec_ep2_western_war_nob_crest_01",
        "f_headgear_sec_ep3_byzantine_era2_war_nob_01", "f_headgear_sec_pol_era3_war_nob_01",
        "f_headgear_sec_steppe_war_nob_01_hi", "f_headgear_sec_steppe_war_nob_01_lo",
        "f_headgear_spec_ep2_byzantine_wedding", "female_headgear_religious_catholic_devoted_01",
        "female_headgear_secular_byzantine_common_01", "female_headgear_secular_byzantine_nobility_01",
        "female_headgear_secular_fp2_iberian_christian_common_01", "female_headgear_secular_mena_common_01",
        "female_headgear_secular_mena_royalty_01", "female_headgear_secular_sub_saharan_common_01",
        "m_headgear_ccp5_english_war_nob_03", "m_headgear_ccp5_english_war_nob_03_coa",
        "m_headgear_ccp5_english_war_nob_03_hi", "m_headgear_ccp5_english_war_nob_04",
        "m_headgear_ccp5_english_war_nob_04_coa", "m_headgear_ccp5_english_war_nob_04_hi",
        "m_headgear_ccp5_german_war_nob_01", "m_headgear_ccp5_german_war_nob_01_coa",
        "m_headgear_ccp5_german_war_nob_01_hi", "m_headgear_ccp5_german_war_nob_02",
        "m_headgear_ccp5_german_war_nob_02_coa", "m_headgear_ccp5_swabian_war_nob_01",
        "m_headgear_ccp5_swabian_war_nob_01_coa", "m_headgear_ccp5_swabian_war_nob_02",
        "m_headgear_ccp5_swabian_war_nob_02_coa", "m_headgear_ccp5_swabian_war_nob_02_hi",
        "m_headgear_ccp5_western_war_nob_01", "m_headgear_fp3_iranian_face_mask_01",
        "m_headgear_sec_afr_war_nob_01", "m_headgear_sec_byzantine_war_nob_01_hi",
        "m_headgear_sec_byzantine_war_nob_01_lo", "m_headgear_sec_byzantine_war_nob_01_roy",
        "m_headgear_sec_ccp_emishi_com_02", "m_headgear_sec_ccp_emishi_com_03",
        "m_headgear_sec_crusades_mena_war_nob_01_hi", "m_headgear_sec_crusades_mena_war_nob_01_lo",
        "m_headgear_sec_ep2_byzantine_war_nob_01", "m_headgear_sec_ep2_steppe_war_nob_01",
        "m_headgear_sec_ep2_western_era4_war_nob_01", "m_headgear_sec_ep2_western_era4_war_nob_02",
        "m_headgear_sec_ep2_western_era4_war_nob_02_01", "m_headgear_sec_ep2_western_war_nob_01_com",
        "m_headgear_sec_ep2_western_war_nob_01_hi", "m_headgear_sec_ep2_western_war_nob_01_lo",
        "m_headgear_sec_ep2_western_war_nob_crest_01", "m_headgear_sec_fp2_iberian_muslim_war_nob_01_hi",
        "m_headgear_sec_fp2_iberian_muslim_war_nob_01_lo", "m_headgear_sec_indian_war_nob_01_hi",
        "m_headgear_sec_indian_war_nob_01_lo", "m_headgear_sec_indian_war_nob_01_roy",
        "m_headgear_sec_northern_war_nob_01_hi", "m_headgear_sec_northern_war_nob_01_lo",
        "m_headgear_sec_pol_era3_war_nob_01", "m_headgear_sec_pol_era4_war_nob_01",
        "m_headgear_sec_steppe_war_nob_01_hi", "m_headgear_sec_steppe_war_nob_01_lo",
        "m_headgear_sec_tgp_chinese_face_mask", "m_headgear_sec_tgp_japanese_face_mask",
        "male_headgear_religious_african_high_01", "male_headgear_religious_jewish_head_01",
        "male_headgear_religious_steppe_high_01", "male_headgear_secular_dde_abbasid_common_01",
        "male_headgear_secular_fp1_war_nobility_02", "male_headgear_secular_indian_royalty_01",
        "male_headgear_secular_sub_saharan_high_nobility_01", "male_headgear_secular_western_common_02",
    };
}
