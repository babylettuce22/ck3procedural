# Starting saints

`Emit/Culture/StartingSaintWriter.cs` emits a roster of up to six founding saints per generated
religion. Sister faiths share the same historical characters and tombs. Inherited religions and
the unsettled placeholder religion retain their existing behavior.

Names come from the religion's language. The six epithets are Peacemaker, Teacher, Contemplative,
Steward, Defender and Healer. Characters have the native Saint trait, suitable education and a
virtue where available. They have no dynasty, relatives or titles, and their death dates precede
every exported bookmark, including very early start years.
Random personality traits are disabled so initialization cannot add sinful traits to them.

An appended `on_game_start_after_lobby` hook registers the saints after the selected bookmark's
holdings are established. It tries at most 24 distinct burial provinces per religion, prioritizing
holy sites and county capitals. A tomb requires an occupied holding, cannot displace another saint,
and is marked as a historical buried-saint travel point. Subsequent sibling-faith registrations
reuse the engine's burial link. Small religions or bookmarks with few occupied holdings may have
fewer than six registered saints. A saved global variable prevents repeated startup registration.

The writer owns three files: a character history, a startup on-action and English localization.
Re-exporting an empty or entirely inherited roster removes those files. Their identities and names
are deterministic by world seed and religion key; existing mod edits preserve the separate files.

## DLC boundary

This change seeds the Saints list. It adds no canonization or relic-extraction decision and does
not alter their DLC gates or Dulia's requirements.

## Patron saint decision

Vanilla's Select a Patron Saint (`pam_determine_patron_saint_decision`) is hidden from everyone
outside the Christianity religion; Dulia is only a requirement, so non-Christians never see it.
`StartingSaintWriter.WritePatronSaints` lifts the installed decision into
`common/decisions/dlc_decisions/zzz_generated/zz_gen_patron_saint_decision.txt`, which loads after
`pam/pam_decisions.txt` and wins the key. Its is_shown also admits generated-religion members whose
rite has Dulia at least permitted or who hold it as a personal tenet. Requirements, the PAM DLC gate
and AI are unchanged, and Christians see exactly vanilla.

For generated religions, the six apostle options get neutral names (the Rock, the Wayfarer, the
Beloved, the Reckoner, the Lion, the Physician) and grant `gen_patron_saint_*` modifiers, which are
stat-identical copies of vanilla's `pam_patron_saint_*` lifted from the installed file. Modifier text
cannot vary by religion, hence the copies. Vanilla's Christian holy-order checks read the `pam_`
modifiers only. A changed installed decision skips the feature with a console line.
`--verify-tenets` covers the gate, both modifier branches, stale-file cleanup and apostle-free text.
In-game check owed.

The installed CK3 1.20.0.3 Saint trait, historical `add_saint` calls and Saints-tab GUI definition
have no explicit DLC gate. The generated startup hook likewise has none. This supports making the
initial roster available without By God Alone, but script inspection and validators cannot prove
the absence of an engine-side ownership restriction. A new-campaign test with By God Alone disabled
is required to confirm that the Saints list and tomb registration work without the expansion.

## Game check

Build and the focused writer fixture passed. The fixture checks shared rosters, inherited religion
exclusion, virtues, dates before every bookmark, year-one starts, tiny religions, deterministic
re-export, localization BOM and owned-file cleanup. A complete world generated from Desktop/height.png
with seed 81947 emitted 18 founding saints. Tiger and ScriptIndex reported no findings in the new
saint files; EventFlow reported zero findings for the appended startup chain. The full mod still
has validator findings in other files, so this is not a clean full-mod validation or an engine test.

Regenerate a world and start a new campaign with debug mode, first with By God Alone disabled.
Open a generated faith's Saints tab: the founding figures should appear with generated names,
portraits and burial locations. A sibling faith should list the same figures. Inspect a tomb,
save and reload, and verify the roster and burial locations remain unchanged. Repeat on an
additional bookmark if exported, and with the DLC enabled. Read fresh `error.log` and
`database_conflicts.log` after these checks.
