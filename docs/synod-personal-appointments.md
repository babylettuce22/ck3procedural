# Personal appointments to generated Synods

Design written October 3, 2026, and implemented the same day; in-game validation is still owed.
The implementation lives in `zz_gen_synod_appointment_interaction.txt` (direct appointment),
`zz_gen_synod_chaplain_interaction.txt` (chaplain petition), and the
`zz_gen_synod_personal_*` effects, triggers, and values. Existing behavior below was inspected in
the installed CK3 1.20.0.3 scripts and the generator's source.

Known differences from this design, pending the in-game run:

- The direct appointment does not explicitly fire a councillor; it relies on the engine to drop
  them on promotion (the chaplain petition fires explicitly).
- Candidate blockers (imprisonment, excommunication, war, clerical gender) share one tooltip.

## Purpose and player experience

A spiritual Head of Faith can invite an eligible cleric to join an existing Synod. The appointment
gives that person a voice in choosing the next head, alongside the permanent representatives of
the faith's Sees. It provides a way to reward service, elevate a scholar, or favor a trusted cleric
without founding a new See.

The interaction is **Appoint Synod Member**, in the Religion category of a character's right-click
menu. It targets one person, with the usual confirmation window showing the candidate, cost,
available personal seats, and consequences. It works across the faith regardless of diplomatic
range. The candidate may belong to another ruler's court or realm.

The actor is the faith's actual spiritual Head of Faith. Native controls that let a player direct
a puppet head should work through that head's authority and resources. Rite heads and challengers
do not independently appoint members in this proposal.

## Eligibility

The actor's faith must be a generated, organized faith with our clerical-region doctrine, a
spiritual head, and an established electing Synod: the head already has the clerical elective
succession law and the faith has at least one permanent elector seat. By God Alone remains
required. Dulia is not a prerequisite: venerating saints and choosing a religious successor
serve different purposes.

The actor and recipient must be adults, capable, free, and not hostages. The recipient must:

- Follow the actor's faith; another rite within that faith is acceptable.
- Be recognized as clergy by the native investiture-clergy check. This includes realm priests,
  ecclesiastical rulers, and See holders. Learning alone does not qualify a lay courtier.
- Be eligible for a clerical elector title: an unlanded cleric, a theocratic ruler, or an eligible
  holy-order ruler. Ordinary secular rulers cannot receive an appointment.
- Satisfy the appointing head's rite rules for clerical gender, as in the native interaction.
- Be someone other than the head, have no existing personal or permanent elector seat, and hold
  no inactive permanent generated seat that could produce a duplicate vote.
- Be neither excommunicated nor at war with the appointing head.

Virtue, Learning, piety, opinion, and family ties influence AI interest rather than imposing
additional hard thresholds on the player. A head may choose an unimpressive but loyal cleric.

A See holder eligible for permanent registration should receive their See's seat through that
system. Personal appointment must not charge for a temporary seat while permanent registration
is pending.

## Shared capacity

Use the existing faith-wide allowance:

`maximum personal seats = max(1, floor(permanent seats / 3))`

Direct appointments and accepted court-chaplain petitions consume the **same** allowance. Every
existing personal seat counts, regardless of which head or petitioner obtained it. A new head
inherits the occupied allowance.

| Permanent seats | Personal allowance | Total with a full allowance |
|---:|---:|---:|
| 3 | 1 | 4 |
| 6 | 2 | 8 |
| 9 | 3 | 12 |
| 15 | 5 | 20 |

Retain the native appointment guard against adding a member when there are already 70 electors,
matching the current chaplain petition. This is an appointment restriction; it does not change
the generator's permanent-seat registration policy. A Synod's personal allowance is a ceiling,
not a population target that must be filled.

If the allowance falls below the number already appointed, existing appointments remain and new
ones are blocked until capacity becomes available. Personal members cannot dismiss one another
or vacate a seat to supply extra capacity.

## Cost, timing, and acceptance

- **Cost:** the native scaled financial cost, `minor_treasury_value`, paid by the appointing
  authority. Preserve the native treasury/gold handling and puppet-payment conventions.
  Generated offers record their quote on send and charge on successful acceptance. Generated
  chaplain petitions likewise defer piety and hook use, so cancelled offers spend nothing.
- **Direct appointment cooldown:** two years per appointing head after a successful appointment.
  Retain the native ten-year cooldown against the same recipient for reappointment cases.
- **Acceptance:** eligible AI recipients accept the promotion. A human recipient receives the
  normal offer and may decline. Capacity and eligibility are checked again when it is accepted.
- **Political benefit:** the appointee gains +20 grateful opinion toward the appointing head,
  using the existing opinion modifier's normal decay. Their election choices continue to use
  the existing vote and candidate mechanics.

The rewards are membership and gratitude. The generated branch does not add the Papal branch's
piety award, Christian Church situation catalysts, monastic favor hook, or a new puppet link.
The existing chaplain petition retains its own political bargain and cooldown; its shared
resource with this interaction is personal-seat capacity.

## Promotion and lifetime

Each successful appointment grants one personal elector title using the existing personal-seat
effect. It represents one member, rather than a geographic region.

- An unlanded cleric becomes a landless ecclesiastical ruler through the title grant. Where
  promotion makes their current council position incompatible, they leave that position through
  the normal council rules. The confirmation must explain this consequence.
- An already eligible theocratic or holy-order ruler keeps their existing government and primary
  office. Receiving a personal seat must not displace a See or convert a holy-order government.
- The title is personal and ends on its holder's death or succession. It does not pass to a
  relative, get automatically refilled, or create a new permanent seat.
- Conversion away from the faith or loss of elector eligibility ends the appointment through
  the appropriate removal path. Government changes and yearly repair remove personal titles
  from holders who lose clergy status or an eligible government, including inactive titles.
  Temporary appointment blockers do not independently revoke an existing membership.
  A personal title cannot become a permanent seat through repair.
- Receiving a permanent Synod seat replaces the personal title, retaining one membership and
  releasing its personal-capacity slot.
- Election as Head of Faith removes the personal title through the existing cleanup path.

The title creates no See, region, land claim, or hereditary office. A change of head does not
revoke valid appointments held by other members.

## Feedback

Proposed confirmation text:

> Invite [Name] to join the Synod of [Faith]. Their membership is personal and lasts while they
> remain eligible. They will take part in choosing the next [Head of Faith title].

Display **Personal appointments: 1 / 2**, with the post-appointment count previewed. Explain the
allowance in its tooltip: one personal appointment for every three permanent seats, with a
minimum of one for an established Synod.

Show the interaction for relevant same-faith clergy and expose temporary blockers in the
confirmation: full allowance, cooldown, insufficient funds, imprisonment, incapacity,
excommunication, war with the head, or incompatible clerical-gender rules. Existing members and
secular candidates do not need an unusable appointment entry.

When applicable, show **[Name] will leave their council position and become an ecclesiastical
ruler**. The success notification names the appointee, faith, and personal Synod seat. Generated
messages use Synod language and the faith's own religious title rather than references to Rome,
red hats, or Cardinals. The native Papal branch keeps its existing presentation.

## AI behavior

Only qualifying heads evaluate direct appointments. Use bounded native recipient lists: clergy
among their courtiers, vassals, subordinates, family, existing relations, and holy-order leaders.
Apply eligibility filtering to every list; do not search all characters in the world.

Use restrained initial interest, rather than vanilla's bonuses for a College below ten or twenty
Cardinals. Starting tuning:

- Base willingness: 20.
- Favor Learning of at least 15 (+10), piety level of at least 2 (+10), and virtuous traits (+10
  each); reduce interest for sinful traits (-10 each).
- Favor friends (+20) and candidates with an opinion of at least 50 toward the head (+10).
- An ambitious or cynical head also favors eligible family members (+20). A zealous head gives
  further preference to virtuous candidates (+10).
- Do not appoint rivals. Consider affordability before evaluating candidates.
- Multiply willingness by 0.25 when only one personal slot remains. This preserves opportunities
  for ruler petitions without making a reserved slot permanently inaccessible to the head.

The two-year direct-appointment cooldown bounds successful AI appointments. These numbers are
initial tuning, not a promise about appointment frequency; observe their effect in a test world.
No automatic minimum membership or replacement appointment is introduced.

## Implementation boundary

Adapt the single native `appoint_cardinal_interaction` object with a narrowly gated generated
branch, following the existing chaplain override. Keep the Papal fallback's eligibility,
granting, costs, effects, notifications, and AI behavior intact. Do not broaden the shared
"leads College of Cardinals" or "follows Pope" triggers globally.

Reuse the existing personal-seat effect, capacity value, and cleanup paths. Before reuse:

1. Harden granting so it refuses a second vote, enforces the shared allowance, and uses the
   current faith/head at the moment of acceptance. Apply this contract to both appointment routes.
2. Preserve the government and primary title of an already eligible ruler. The current helper
   uses a theocratic government base and can change a non-theocratic government, so holy-order
   cases require particular review.
3. Revalidate pending requests. Two requests accepted close together must not exceed capacity.
   If an appointment cannot complete, explain the cancellation and return its paid financial or
   piety cost. A chaplain petition cancelled by capacity or eligibility changes must also leave
   its offered hook unspent. Check native pending-interaction handling before choosing the
   cancellation/refund mechanism.
4. Use `gen_personal_seat` in generated notifications; the native effect's `cardinalate` scope
   does not exist in this granting path.
5. Add faith-aware wording for the interaction, confirmation, notifications, and capacity
   failures. Keep the loaded vanilla Papal strings available through the fallback.
6. Extend the existing source checker to verify the native fallback and generated branch,
   then update the stale documentation that still says chaplain petitions are hidden.

Scope is the direct personal appointment and the shared safeguards it requires. Founding new
Sees, establishing a Synod after a faith reaches three Sees in play, patron saints, and tenet
integration remain separate features.

## Acceptance checks for implementation

- The interaction appears for an eligible generated head, including native puppet control,
  and targets eligible same-faith clergy beyond diplomatic range.
- A faith without an electing Synod, a temporal head, a rite head, or a challenger cannot use
  the generated branch. Without By God Alone, it is unavailable.
- An appointment grants exactly one active personal vote, charges the correct authority,
  applies the intended gratitude and cooldown, and displays the correct title in its toast.
- Direct appointments and chaplain petitions obey a single allowance in either order, including
  two pending requests competing for the last slot. A cancelled grant leaves no paid cost,
  consumed petition hook, or title.
- A permanent member, inactive permanent-seat holder, or pending permanent See registration
  cannot gain a paid duplicate personal membership.
- Court-priest promotion handles council departure correctly. Existing theocratic and
  holy-order recipients retain their government and primary office.
- Death, conversion, loss of eligibility, permanent-seat promotion, election as head, and
  save/reload preserve the intended lifetime and one-vote behavior.
- AI behavior is occasional, stays within the allowance, and avoids automatically filling
  every small Synod. Petition opportunities remain present during a multi-decade test.
- VanillaWorld Papal appointments retain their native behavior. Run the repository source,
  packaging, and validator checks before runtime testing.

## Source grounding

Inspected installed sources: `common/character_interactions/pam_interactions.txt`
(`appoint_cardinal_interaction`), `common/character_interactions/_character_interactions.info`,
`common/scripted_triggers/pam_scripted_triggers.txt`, and the native treasury script values.

Existing generator components:

- `BaseFilesToCopy/Core/common/character_interactions/zz_gen_synod_chaplain_interaction.txt`
- `BaseFilesToCopy/Core/common/scripted_effects/zz_gen_synod_personal_seat_effects.txt`
- `BaseFilesToCopy/Core/common/script_values/zz_gen_synod_personal_seat_values.txt`
- `BaseFilesToCopy/Core/common/scripted_triggers/zz_gen_synod_registration_triggers.txt`
- `BaseFilesToCopy/Core/common/on_action/zz_gen_synod_registration_on_actions.txt`
- `BaseFilesToCopy/Core/common/scripted_effects/zz_gen_pam_cardinal_title_effects.txt`
- `tools/verify_synod_registration.py`

The existing court-chaplain petition is adapted in code, and `docs/features.md` and
`docs/synod-registration-checks.md` describe both personal-appointment routes.
