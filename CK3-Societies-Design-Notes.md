# CK3 Procedural Worlds: Societies Design Notes

Working discussion record — September 28, 2026 (America/Los_Angeles)

Repository: [babylettuce22/ck3procedural](https://github.com/babylettuce22/ck3procedural)

Source baseline: `6ea12830bc98129160894e45ab688ddf529c9f11`, reviewed September 28. This is a static source review, not an in-game validation. No implementation changes were made. Proposals below remain proposals, not agreed specifications. The user proposed considering a CK3 Situation when the banners are raised and requested this consolidated record.

## Status after the source review

The sections below retain the original review and its proposals. Since that review, the ten
remaining purchase options received affordability gates and four localized requirement tooltips.
Their events retain free choices. Tiger produced identical diagnostics before and after the patch;
in-game validation remains outstanding. The currency-affordability row in section 6 is therefore
historical, not an open implementation task. Documentation now also describes the existing
enabled-by-default setting and both active societies; the default itself was not changed.

## 1. Overall assessment

There is enough mechanical breadth for a substantial society feature: membership, ranks, currencies, exposure, recruitment, missions, activities, powers, opposition, and collective objectives. Adding another generic meter or mission category is not the priority.

The strongest opportunity is connecting preparation to consequential political action. Members should need particular people, make commitments, disagree about strategy, and live with the results of victory or failure. Generated names and targets are useful; differing political circumstances should also change how a society plays.

The active `BaseFilesToCopy/Societies` set contains Restorationists and an inversion cult. `SocietyPrototype` is the older reference implementation, not the current feature backlog. A lightweight reference check found 233 event definitions and no missing definitions for literal `cult.*` and `restor.*` event calls in the active set. This does not establish that all event scopes, engine calls, or gameplay paths work.

## 2. Restorationists: current implementation

### World setup and claimant

The generator selects a fallen kingdom and an associated house. The kingdom can be broken (unheld) or usurped (held by another house). It uses a suitable surviving landed ruler as pretender when available; otherwise it creates an exiled family line and a living claimant.

The generated landless claimant is an ordinary exile placed at a supporter’s court, not a generated adventurer with a camp. The generator favors a host outside the usurper’s realm with spare directly held counties and raises their standing sufficiently to help grant a seat. The shelter and special county-grant interactions explicitly exclude landless adventurers.

### Member experience

Members recruit, carry the heir’s letters, shelter the heir, give gold, grant a county, forge claims, stir counties, attend gatherings and errands, and manage witnesses and exposure. The Keeper assigns charges: win over a lord, contribute money, stir a county, shelter the pretender, or recruit. Standing unlocks powers; favour pays for them. Exposure creates secrets and brings the usurper’s attention.

Support reflects the map: land in the pretender’s realm, land under sworn rulers, stirred counties, and membership. Its weights are 40/30/15/15, capped at 100. It is not a bank of points that every completed mission permanently adds to.

### What raising the standard actually does

| Requirement or effect | Current behavior |
|---|---|
| Eligibility | Landed pretender; adult, free, at peace; 40 support; once per underground phase |
| Cost | 200 prestige |
| Military benefit | Six years of +15% levy size and −10% army maintenance |
| Held kingdom | Pressed claim on the crown |
| Unheld kingdom | Claims on held de jure duchies outside the pretender’s realm |
| Supporter response | Form an alliance, give 100 gold, or decline |
| Military pledge eligibility | Responding member is landed and independent; claimant is landed; not already allied |
| Army spawning | None in the reviewed Restorationist scripts |
| Automatic war declaration | None in the reviewed Restorationist scripts |
| Automatic entry into a war | None from the “march with them” response; it creates an alliance |

The decisive military action is left to ordinary claim warfare and calls to allies. A landless exile must acquire land before using this decision. Preparation can therefore succeed without the scripts guaranteeing a restoration attempt.

### Restoration and aftermath

For a held crown, the claimant must take it through warfare. For an unheld crown, a special decision creates it with 25% of the de jure counties in the pretender’s realm, 40 support, 150 gold, and 250 prestige, subject to personal and liege requirements. It does not transfer the remaining counties. The crowning can release the claimant from an eligible lower-tier liege.

Restoration changes the society into a public order: secrets dissolve and exposure stops. Its objective becomes completing and maintaining the kingdom. Another loss can return it underground. Pretender and Keeper succession, the end of the claimant’s line, and a reunited kingdom have existing content.

## 3. Recommended Restorationist changes

### A. Allow a sponsored restoration for a landless claimant

Keep the protected exile role. A Keeper or major landed supporter should be able to sponsor the attempt; a landed claimant can lead their own. Granting a county remains a meaningful advantage rather than the mandatory gateway. Do not automatically turn every claimant into an adventurer.

Technical question: verify a supported war-leader/beneficiary arrangement for an unlanded claimant, including title delivery and independence. A sponsored claim war is the proposed design, not a verified script recipe.

### B. Secure commitments before launch

Start with one commitment per supporter:

| Commitment | Concrete contribution |
|---|---|
| Military backing | Enter the restoration war when called, subject to valid participation |
| Financing | Transfer money earmarked for the attempt |
| Sanctuary | Protect the claimant and offer a staging location |
| Local organizing | Prepare a county to support the rising |
| Conditional support | Help in exchange for a specific reward or concession |

Record promised versus delivered support. A supporter may betray a commitment, but that must be visible and consequential. Validate vassal/liege conflicts and other war-participation restrictions; a promise cannot override engine constraints.

### C. Make the rising a real war

For a usurped crown, launch a war to install the claimant. For a fragmented crown, choose one initial target, preferably the old seat or its duchy, to establish a foothold. Do not launch simultaneous wars against every successor state. A foothold victory is not the same thing as restoration of the crown.

Consider a limited loyalist host after the sponsored-war loop works. Stirred counties supply local recruits; rulers provide existing armies; funding sustains the attempt. Cap special troops relative to the target’s strength and prevent double counting existing forces. Disband them when the attempt ends. Exact numbers require balancing.

### D. Remember victory and defeat

Victorious supporters expect rewards reflecting what they actually contributed: land, office, concessions, or recognition. The new monarch cannot necessarily honor every promise. Accepting the monarch’s independence should be an explicit political cost where relevant.

Defeat may expose participants, cost property, scatter the coalition, or send the claimant back into exile. Surviving causes can rebuild for another attempt; a dead claimant may leave a successor. Avoid permanently disabling the society after one failed war.

### E. Give the AI a decision point

Once ready, periodically choose to launch, postpone for an intelligible reason, or abandon the current plan. Tell members why: an essential sponsor is at war, the claimant is imprisoned, or pledged strength is insufficient. Readiness should not produce indefinite silence.

### F. Deepen internal politics later

Potential disputes: an incompetent rightful heir versus a capable relative; a sponsor unwilling to give up the new king’s vassalage; competing reward promises; cautious organizers versus impatient militants. Tie these to actual characters and contributions. No separate faction interface is required for an initial event-based implementation.

## 4. Proposed Restoration Situation

### Recommendation and purpose

Yes: use a Situation for the public restoration crisis, starting when the restoration war successfully begins. Secret organizing stays in the society panel. The Situation makes the cause legible to the claimant, supporters, defenders, and affected rulers without revealing every covert member.

The Situation should add coalition and settlement information around the actual war. War score and the casus belli remain authoritative for military resolution; do not create an independent progress bar that can declare a contradictory victory.

### Lifecycle (proposed)

| Stage | Entry | Purpose and exit |
|---|---|---|
| Secret preparation | Existing society loop | Recruit, shelter, fund, obtain promises; no public Situation |
| Rising | Confirmed creation of the restoration war | Reveal declared participants, resolve commitments, display the war and its political stakes |
| Settlement | War ends in a relevant victory | Allocate promised rewards; handle sovereignty and grudges; distinguish foothold success from full restoration |
| Defeat/dispersion | Defeat, white peace, or invalidation, with separate outcomes | Resolve casualties, exposure, flight and broken commitments; preserve surviving society state |
| Closed | Aftermath resolved or bounded cleanup deadline reached | Remove temporary Situation state and special forces; persistent society continues |

These are design stages, not verified native phase definitions. Invalidation is not automatically a defeat. If the claimant dies, inspect whether a successor can continue; if the crown changes hands or is destroyed, resolve according to the actual war outcome and valid objective. Repeat attempts need fresh identities and must not reuse stale commitments.

### Intended interface

- Claimant, target crown, sponsor/war leader, and defender.
- Actual war status and a link to the war interface, subject to available GUI bindings.
- Declared supporters and opponents, with undecided local rulers distinguished from combatants.
- Promised and delivered gold, military participation, sanctuary, and organizing.
- Relevant locations: old seat, current objective, and prepared counties. Do not expose covert preparation to unauthorized viewers.
- Actions appropriate to the viewer: honor a pledge, contribute funds, negotiate terms, offer amnesty, or decide a settlement reward.
- A short attempt history: launch, major defections, claimant succession, outcome, and settlement.

Keep readiness, personal favour, military war score, and contribution records distinct. No new generic “restoration progress” currency is necessary.

### Region, participation, and secrecy

Use the target crown’s relevant territory as the geographic focus; permit outside sponsors as participants where supported. Situation membership must not itself imply military participation. Secret members are not automatically listed as supporters. Public notification and map visibility should depend on involvement and relevance, with separate handling for private intelligence.

### Technical fit and limits

The repo already integrates Situations through `Emit/FrontierWriter.cs` and patches the shared Situation window in `Emit/GuiWriter.cs`. That is a useful local precedent. The GUI code documents two existing pitfalls: a phase without a duration can display a bogus end date in the generic window, and phase artwork required an explicit GUI extension for the Wilds. Reuse the established integration carefully rather than assuming the default UI exposes all desired information.

Native runtime creation/destruction, war-reference persistence, custom columns/actions, external participants, information visibility, and current DLC/version requirements still need verification against the target installation’s game scripts and script docs. An official Situation dev diary was located but could not be retrieved during this review; it is not used to claim specific supported APIs.

Create the Situation only after war creation succeeds. Make creation and ending idempotent, retain a distinct attempt identity, reconcile actual war state after save/load, and guarantee cleanup on all endings. Avoid displaying a public crisis when a launch fails validation.

## 5. Inversion cult: assessment and proposed depth

The implemented loop includes host-faith-derived recruitment, dark power, soul corruption, exposure and a hunter, missions, activities, powers, hidden/hollow/unveiled phases, doctrine inversion, and purification. This is substantial existing content.

The main proposed improvement is a meaningful choice between remaining secretly in control and unveiling. A captured church should provide advantages worth preserving; unveiling should offer stronger public benefits with political costs. Members might disagree: an ambitious ruler wants covert institutional influence, while a devotee demands open worship. These are suggested incentives and disputes, not a statement that the full system already implements them.

Particular members should be valuable for their offices, wealth, custody of the claimant, or control of a holy site. A roster should generate dependencies on people rather than only adding to a meter.

## 6. Source-review backlog

| Item | Evidence/status | Next step |
|---|---|---|
| Currency affordability | Both exposure event `0300.b` options spend ten currency without checking affordability; spending clamps at zero | Gate purchased benefits; distinguish losses/penalties from costs |
| Exclusive membership | Recruitment excludes the other society, but Restorationist swearing-in does not centrally enforce it; house succession can bypass recruitment | Define and enforce behavior when a cultist becomes pretender |
| Panel consistency | Opening copies headline state; displayed support/rot can be computed live while decision gates use cached globals; action indicators omit requirements | Share eligibility checks and refresh policy |
| Membership secrecy | Ordinary fame traits identify membership; no concealment condition found in reviewed definitions | Verify actual visibility in game before choosing a fix |
| Leader departure cleanup | Leave effects do not themselves clear leader roles; yearly repair checks absent/dead leaders | Harden invariant; many normal departure paths already exclude leaders, so not a demonstrated normal-play failure |
| Documentation/settings | Old “missing” sections contradict implemented features; societies default true while UI-hidden and described as off | Reconcile defaults, descriptions, and README |
| Multiple societies | Singleton `restor_*` / `cult_*` global state | Per-instance state if multiple causes/cults become a goal |
| Additional bookmarks | Setup uses the main bookmark window | Generate suitable state for other dates if desired |
| AI charges | Some completion is abstracted through yearly rolls | Replace selectively with concrete actions where they add gameplay |

The displayed Restorationist war chest currently accumulates contributions while gold also goes to the pretender. If adding actual pooled expenditure, explicitly distinguish cumulative contributions from an unspent treasury.

## 7. Suggested implementation order and acceptance checks

1. Resolve currency, membership, secrecy, and panel inconsistencies; implement a minimal sponsored war with verified beneficiary behavior.
2. Track commitments and resolve legal participant entry when the war begins.
3. Add a minimal Situation displaying participants, the actual objective/war, and commitments; start it only on confirmed launch.
4. Implement victory, defeat, white-peace, and invalidation aftermath; close the Situation cleanly.
5. Balance AI launch decisions and optional loyalist troops.
6. Expand internal disputes, settlement promises, and cult strategic choices.

Verify: landless and landed claimants; usurped and broken crowns; sponsors inside/outside the defender’s realm; claimant/leader death; imprisonment; crown destruction or transfer; no members left; repeated attempts; reward double-payment prevention; no special troops surviving cleanup; save/reload during every stage; existing Wilds/Dynastic Cycle UI compatibility; and nonmembers not learning hidden membership from the new UI.

No claim is made that this matrix has been run.

## 8. Earlier world-generation ideas (parking lot)

The initial brainstorming proposed connected historical features: competing imperial successor traditions; boundaries inherited from vanished geography; unfinished conquests; displaced cultures with lost homelands; conditional crowns; neutral coronation cities; hereditary offices outside the royal house; seasonal authority; shared holy sites with conflicting meanings; politically actionable prophecies; changing religious geography; inherited dynastic debts; feuding branches of one ancestral house; concealed founding histories; corridor kingdoms; isolated refuges of an older civilization; and reopened settlement frontiers.

The common design principle was to generate connected clusters from shared history. A fallen empire might explain its exiles, holy sites, rival crowns, and frontier culture together. These remain unselected ideas, not society requirements.

## 9. Source map

All repository links below use the reviewed commit.

- [Active society files and README](https://github.com/babylettuce22/ck3procedural/tree/6ea1283/BaseFilesToCopy/Societies)
- [Restoration generator](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/MapGen/Societies/Restoration.cs)
- [Restoration emitter](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/Emit/Societies/RestorationWriter.cs)
- [Decisions and launch requirements](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/BaseFilesToCopy/Societies/common/decisions/00_restor_decisions.txt)
- [Rising and supporter responses](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/BaseFilesToCopy/Societies/events/restor_rising_events.txt)
- [Restorationist powers](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/BaseFilesToCopy/Societies/common/character_interactions/00_restor_powers.txt)
- [Core membership, succession, and phases](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/BaseFilesToCopy/Societies/common/scripted_effects/00_restor_core_effects.txt)
- [Existing Frontier Situation implementation](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/Emit/FrontierWriter.cs)
- [Shared Situation GUI integration](https://github.com/babylettuce22/ck3procedural/blob/6ea1283/Emit/GuiWriter.cs)

