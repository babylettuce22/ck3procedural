# Synod registration checks

Implementation: `BaseFilesToCopy/Core/common/*/zz_gen_synod_*` plus the existing see-elector
handover and recovery scripts. Regenerate the mod with the current build before testing; these
files are copied on export. The source checker compares vanilla fallback bodies against the
installed game, so a patch changing them requires review rather than silently drifting.

## Headless checks

- [x] `dotnet build --no-restore` (zero warnings and errors, 2026-10-02).
- [x] `python tools/verify_synod_registration.py --game "<CK3 game directory>"`.
- [x] Fresh world export and source/packaging check with `--mod obj/synod-world`.
- [x] Run Tiger against the exported mod and compare with its baseline. Tiger 1.19 cannot
  validate the 1.20 clerical-region/elector/rite API; record that limitation, rather than treating
  its unknown-token findings as evidence of a successful runtime check.

The local experimental 1.20 fork was also run on the baseline, changed scripts and exported
world. It reported no errors in the new registration triggers/effect/on-actions/events. It is
not a clean full-mod validation: the retained vanilla seed body reports a missing `c_roma`
fallback in the generated world, and the copied vanilla destruction/interaction bodies have
scope warnings. The Synod exclusion prevents its automatic paths from executing that seed
body. Other full-mod findings remain outside this change. Reports are in
`obj/synod-tiger120*.txt` and `obj/synod-world-tiger120.txt`; the fork remains experimental.

The export fixture had no starting sees, so it verifies packaging rather than actual Synod
behavior. The in-game checks below remain pending and require a regenerated world with a Synod.

## In-game checks

Launch the regenerated mod with `-debug_mode`. Use a generated faith that already has a Synod,
an eligible secular ruler, and a capital outside existing sees. Keep a separate test save.

- [ ] Record the current Synod seats and their holders. Use the existing see-creation petition
  and get it accepted. After two days, the new duchy-tier see should have exactly one permanent
  seat, held by its eligible hierarch; the see remains their primary title and their government
  remains ecclesiastical. Other Synods should have unchanged counts.
- [ ] Run the repair effect twice while playing a member of that faith:
  `effect faith = { every_clerical_region_title = { gen_synod_register_see_effect = yes } }`.
  Neither run should add another seat to a registered see or to a temple barony.
- [ ] Found a see, save immediately before the delayed registration, reload, and advance two
  days. Verify exactly one seat. Repeat save/reload after registration and run repair again.
- [ ] For an older save containing an unregistered see, advance through the Head of Faith's
  next yearly pulse or run the repair effect. An eligible holder with no permanent vote gains
  one seat. A holder already owning a different permanent seat gains no second one.
- [ ] Kill a seat holder, change a see holder, and convert one to another faith. Existing
  permanent seats retain the established succession/recovery behavior and never vote for a
  foreign-faith holder. New registration must not create an extra seat for the same see.
- [ ] Split a region: a genuinely new eligible see gets one seat. Merge regions: existing
  permanent seats survive according to the current policy; a holder must not retain two votes.
  A freed seat can have its own replacement hierarch rather than being destroyed.
- [ ] Give a cleric a personal seat (Appoint Synod Member or a chaplain petition), then
  appoint them to a see. Their personal title is destroyed when they
  take the permanent seat. Repeat with a personal-seat holder becoming Head of Faith; no
  personal title remains. A personal title is never repaired into a permanent seat on conversion.
- [ ] Found/reform/diverge a generated faith carrying clerical electors. Confirm no automatic
  minting of ten clerics or personal cardinalates. Registration extends faiths already eligible
  for Synods; it does not automatically turn every new spiritual-headed faith into an election.
- [ ] In VanillaWorld mode with the real Papacy, verify that ordinary seeding/refills and the
  chaplain-cardinalate request retain vanilla behavior. The shared Papacy trigger is unchanged.
- [ ] Let the test world run for at least twenty years. Check that growth corresponds to new
  eligible sees, not an automatic floor of ten or repeated registrations.

After running these cases, Codex can inspect `error.log`, `debug.log`, and
`database_conflicts.log` directly under the CK3 logs directory. Source/Tiger checks alone do not
confirm the engine's handling of dynamic titles, delayed events, or saved scope references.

## Personal-appointment integration contract

- The personal title carries both `gen_synod_personal` and `gen_synod_faith`.
- Permanent seats carry `gen_synod_see` and `gen_synod_faith`; their sees point back through
  `gen_synod_seat`. Registration does not use `roma_cardinalates` or `pam_dynamic_cardinalate`.
- `gen_synod_remove_personal_seats_effect` runs in character scope and destroys all their
  personal titles through the normal destruction path. Permanent seat handovers call it before
  granting their vote. A delayed title-gain cleanup and yearly Head of Faith check cover duplicates.
- The preservation override and conversion recovery exclude personal titles explicitly.
- Government-change and yearly callbacks remove personal titles when their holder no longer
  qualifies as clergy or has an eligible government, using held-title markers rather than the
  active elector pointer. The vanilla callbacks' own effects are not replaced.
- Both personal-appointment routes are single-object overrides of the native interactions
  (`appoint_cardinal_interaction` and `request_court_chaplain_cardinalate_interaction`), gated on
  `special_doctrine_gen_clerical_regions`; other faiths keep the unchanged vanilla bodies. Both
  grant through `gen_synod_grant_personal_seat_effect`, which rechecks eligibility and the shared
  allowance (`gen_synod_personal_seat_room_value`) at acceptance. See
  `docs/synod-personal-appointments.md`.

## Appointment implementation validation (2026-10-03)

- [x] Source checks compare both interactions' projected native branches to CK3 1.20.0.3,
  including costs, granting, AI, conditional text and native localization fallbacks.
- [x] Capacity arithmetic is evaluated from the actual script values over empty, small, full
  and overflow elector fixtures. Final grant guards, success-only payment, and office preservation
  are checked separately, as is inactive personal-seat cleanup on eligibility loss.
- [x] The local verify loop generated a fresh seed-823103 world containing an electing generated
  faith. Subsequent static export comparisons use that same world and its captured baseline.
- [x] The local Tiger fork's five new errors in the copied appointment definition reproduce in
  the unmodified vanilla interaction: four unsupported 1.20 AI recipient lists and its situation
  phase parameter. The isolated native control also reports two native puppet-scope initialization
  errors. Control output: `obj/synod-appointments-native-control-tiger.txt`.

- [x] Final Release build succeeded with zero warnings/errors. Static re-export changed only the
  ten intended shipped text files; all 1,376 binary assets stayed identical. No new generator
  warnings or ScriptIndex findings were introduced.
- [x] `verify_synod_registration.py --mod` passed against the final exported fixture, including
  interaction overrides, references, cleanup, and packaged localization.
- [x] EventFlow found no missing targets or saved-scope failures in the generated appointment
  helpers or cleanup callbacks. The unchanged native petition's engine-provided `scope:hook`
  remains a reported scope warning, alongside unrelated existing mod findings. Final report:
  `obj/synod-appointments-eventflow-final.txt`.

Source tools cannot confirm pending-interaction scope persistence or the engine's promotion
behavior; the following runtime cases remain pending.

## Personal appointment runtime checks

- [ ] Regenerate, enable By God Alone, and use a faith with an existing electing Synod. Direct an
  appointment as its actual head, including through native puppet controls. Eligible same-faith
  clergy beyond diplomatic range should appear; temporal heads, rite heads, and challengers cannot
  appoint through the generated branch.
- [ ] Check the quota preview, financial quote, council-departure notice, +20 gratitude, and
  two-year direct cooldown. AI recipients accept; human recipients receive an offer.
- [ ] Appoint directly then petition for a chaplain, and reverse the order. Both consume the same
  allowance, including after a change of head. Full allowance should explain the blocker.
- [ ] Send competing offers for the last slot. Accept in either order: the second must cancel
  without payment or hook use. Change faith, head, chaplain, available funds, or permanent membership
  while an offer is pending and check the same cancellation behavior.
- [ ] Save/reload a pending offer. Its original head, faith, candidate, fee, and selected hook must
  persist. Declined generated offers spend no currency or hook.
- [ ] Promote an unlanded chaplain and check council departure, ecclesiastical government, and
  the petition's existing clerical-agent bargain. Existing theocratic/holy-order recipients must
  retain their government and primary office.
- [ ] Test death, conversion, secular government change, permanent-seat promotion, and election
  as head. Each appointee retains at most one active vote; personal seats are not preserved/refilled.
- [ ] Run AI heads for twenty years, checking appointment cadence, the shared allowance, and
  petition opportunities. Check ordinary Papal appointments in VanillaWorld mode.
