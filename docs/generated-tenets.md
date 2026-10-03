# Generated tenets and DLC ownership

The generator includes the installed game's visible tenets, including all 26 By God Alone
tenets on CK3 1.20.0.3. It does not assume the player owns an expansion because its script
files are installed.

`VanillaVocabulary` retains each tenet's definition and `requires_dlc_flag`. `TenetWriter`
emits ungated tenets in a rite's ordinary `tenets` list and gated ones in native
`tenet_selection_pair` blocks. Each pair has a distinct, compatible fallback requiring no
DLC. CK3 chooses the branch using the player's enabled DLC, including mixed ownership
across expansions. Main and regional rites both use this mechanism. Faith definitions
delegate core tenets to their scripted main rites, as the vanilla schema recommends.

At generation time, `TenetWriter` also emits `zz_gen_bga_tenets.txt` from the installed
definitions. It widens explicit Christianity religion checks to admit this world's generated
religions, including checks for personal tenets. It preserves Christian eligibility and all
other fields, including DLC requirements, visibility, costs, effects, and Christology
restrictions. A subsequent generation picks up changes in installed definitions and newly
added BGA tenets; no hand-maintained copies of the 26 definitions are stored in the repo.
The output is removed when there are no generated religions to admit.

The generator reads the native `unique_christology_trigger` to prevent mutually exclusive
Christologies from being selected together. The world editor reads intended tenets from
selection pairs and regenerates ownership gates and fallbacks when their selection changes.

Generated religions also use names and descriptions from
`BaseFilesToCopy/Core/localization/english/gen_tenets_l_english.yml`: 26 new tenets and ten
older tenets whose expansion descriptions introduce Christian wording. Esoteric theological
names such as Monophysitism and Miaphysitism remain. Sacred Soldiery, Divine Mother,
Sacred Succession, Sacred Stelae, and Mystical Catechism replace overt references in names.
The descriptions retain a solemn religious tone while omitting named Christian figures,
Bible quotations, and historical glossary links.

`zzz_gen_tenet_text.txt` prepends a generated-religion choice to the effective tenet's
`name` and `desc` first-valid lists. Existing vanilla choices remain in their original order
as fallbacks for other religions. The writer reads existing mod overrides first, preserving
the holy-war adaptations rather than restoring vanilla restrictions. Missing implicit text
fields receive their native localization-key fallback; unexpected structures raise a named
error. Both text files use UTF-8 with BOM, and localization uses new keys rather than
overriding vanilla quotations or shared history text.

Religion restrictions and Christian wording elsewhere in vanilla events and activities
may still need separate adaptations; possessing a tenet does not promise access to every
Christian mechanic.

Validation: `dotnet run -- --verify-tenets` checks installed BGA coverage, Christology
conflicts, compatible ownership branches, unchanged native fields, stale-output cleanup,
editor save/reset/reopen behavior, generated text coverage, localization encoding, vanilla
text fallbacks, and preservation of existing mod gameplay overrides. The full local `verify.sh` loop validates a generated
world with ck3-tiger and ScriptIndex. In-game verification of ownership branches is still needed.
