# Remaining Christian gates: investigation

Investigated against the installed CK3 1.20.0.3 scripts and the current generator on
2026-10-02. This report proposes adaptations; it changes no gameplay scripts or emitters.
Priority and effort estimates below are judgments based on the dependencies found, not
in-game implementation results.

## Recommended next candidates

### 1. Everyday religious and Hierarch events

The `pam_christian_yearly_events` pool requires Christianity before any of its events can
be selected. It contains 56 secular-faith entries, 14 theocratic-ruler entries, eight generic
religious entries and one religious-artifact entry. Forty events in the secular-faith event
file also require `pam_secular_faith_christian_actor_trigger` individually. Opening only that
helper would leave the yearly pool inaccessible.

Portable examples include pilgrimage sponsorship, clerical petitions, scholarly arguments,
family grief, vows, visions, religious-artifact sponsorship, noble bequests, monastic audiences
and disputes over religious taxes. Other entries explicitly invoke Christ, historical saints,
Adamites, or Christian ritual and should be rewritten or excluded. Some otherwise generic
events are missing solely because the dispatcher is Christian-only.

Recommendation: curated procedural event pool, retaining native pacing and cooldown behavior;
adapt individual eligibility and wording as needed. High benefit, moderate content work.

Sources: `common/on_action/yearly_groups_on_actions.txt:427`;
`common/scripted_triggers/pam_secular_faith_triggers.txt:21`;
`events/dlc/pam/pam_secular_faith_events.txt`.

### 2. Cathedral II/III construction events

`pam_cathedral_great_project_yearly_on_action` is invoked only by the Christian Church
situation's yearly hook. It scans projects of types `pam_cathedral_02` and `_03` and schedules
founder/owner event pools. Generated worlds without that situation lack this scheduler,
even though the preceding implementation now opens the projects themselves and their rewards.
Tier I has a separate schedule initiated by its construction start.

Recommendation: independent global scheduler for procedural projects, with duplicate-scheduling
protection when the Christian situation is present. This is the most immediate follow-up to
the cathedral adaptation; verify event scopes and event wording as part of the port.

Sources: `common/situation/situations/pam_christian_situation.txt:28`;
`common/on_action/dlc/pam/pam_on_actions.txt:20`.

### 3. Spread the Word activity intent

`spread_personal_tenet_intent` directly requires `is_christian_trigger`. Its purpose is to
preach a personal tenet to activity guests. Other requirements include the Zealous Proselytizer
perk, relevant DLC, adulthood and a personal tenet. It does not require the Church situation
in its visibility trigger.

Recommendation: admit eligible generated faiths, preserving those prerequisites and checking
the activity-specific event consumers. A comparatively small, useful candidate.

Source: `common/activities/intents/spread_tenet_intents.txt:3`.

### 4. Petition the Head of Faith

`petition_head_of_faith_faith_qualifies_trigger` requires Christianity plus a spiritual head
of faith. The associated decision also requires central sacraments. The reusable system
includes travel to the religious authority for repentance and requests for excommunication,
and resolves a challenger as the authority where appropriate.

Recommendation: a Synod audience/petition adaptation for eligible spiritual-head faiths,
retaining doctrine and target restrictions. More than a visibility edit: review the travel
chain, ongoing-recipient checks and Papal wording together.

Sources: `common/scripted_triggers/11_petition_head_of_faith_triggers.txt:2`;
`common/decisions/10_religious_decisions.txt:2371`.

### 5. Situation-gated religious administration

- **Send Legatine Mission:** spends treasury to improve county development growth and control.
  Requires a Reform-chapter participant parameter. The generator currently hides it when the
  situation does not exist, preventing an unusable decision from appearing.
- **Reform Monastic Endowment:** patron funding for monastic orders, giving piety, spiritual
  fulfillment and leader opinion. Requires situation participation and a Reform parameter.
- **Levy the Tithes:** extracts money from religious vassals. Requires situation membership and
  an instability-related participant parameter.

Recommendation: reuse their effects behind procedural governance conditions, costs and
cooldowns. Monastic patronage and county missions are especially good thematic fits. Simply
removing phase requirements would turn episodic opportunities into permanently available
actions and needs a balance review.

Sources: `common/decisions/dlc_decisions/pam/pam_decisions.txt:1237`, `:1333`, `:1481`;
`Emit/Culture/HierarchyFlavourWriter.cs:203`.

## Further candidates requiring more design

- **Holy Myron / artifact anointing:** the anointing decision, gifting interaction and related
  event eligibility contain explicit Christian checks. The reusable idea is consecrating an
  artifact with sacred oil. Preserve the actual tenet, artifact and charge requirements;
  adapt acquisition and terminology too. Sources: `pam_decisions.txt:5834`,
  `pam_interactions.txt:28525`, `events/dlc/pam/pam_holy_myron_events.txt:526`.
- **Patron saints:** truly Christian-only, with Dulia requirements and named saints such as
  Peter. A procedural patron/ancestor system needs generated choices rather than exposing
  historical Christian names. Source: `pam_decisions.txt:888`.
- **Investiture disputes:** conflicts over appointment authority are portable, but the native
  wars, stances, council timer and resolution depend on the unique situation and its stored
  main-power faith. A per-faith dispute system would be a substantial feature.
  Source: `common/scripted_triggers/pam_scripted_triggers.txt:569`.
- **Historical heresy outbreaks:** component 3 is Christian-only and creates named historical
  movements with dates/geographical rules. A procedural founding-preacher movement could reuse
  the concept, not the gate alone. Source: `common/scripted_triggers/pam_heresy_triggers.txt:24`.
- **Reconciliation between faith heads:** `seek_communion_interaction` explicitly targets the
  Catholic head and uses Christian ecumenical doctrines. General religious recognition is
  an interesting feature but requires redesigned outcomes.
  Source: `common/character_interactions/pam_interactions.txt:28644`.
- **Mass rite conversion against organized faiths:** the project is already visible to
  otherwise eligible non-Christian rulers, and unreformed targets can qualify. Organized
  targets require the founder to participate in the Christian situation during its conversion
  phase. A procedural missionary campaign against organized religions needs an equivalent
  opportunity condition. Source: `common/great_projects/types/01_pam_projects.txt:5595`.
- **AI religious-war encouragement:** `ecclesiastic_incite_holy_war_interaction` adds a Christian
  requirement for AI actors; human actors are not blocked by that particular check. This is
  an AI-behavior adaptation, not a newly exposed player action. Source: `pam_interactions.txt:12068`.

The situation also supplies bonuses to cathedral speed, clerical appointments, councils,
conversion, vows, claims and religious coercion. Those are phase-dependent balance modifiers,
not entire missing features. A procedural model of institutional stability could reuse selected
effects; enabling every phase's advantages at once would change their intended behavior.

## Important corrections and already-generic systems

- The direct Christianity checks in the **antipope decisions** select artwork. Their availability
  is primarily doctrinal and authority-based. They should not be classified as Christian-only
  based on those checks. Source: `pam_antipope_decisions.txt:4` and `:32`.
- **Ancestor canonization/veneration** already supports non-Christian faiths. The Christian
  branch adds Dulia restrictions; the general trigger does not exclude other religions.
  Candidate ancestry, virtue, faith, available burial sites and other conditions still matter.
  Sources: `common/scripted_triggers/pam_saint_triggers.txt:44`;
  `common/decisions/dlc_decisions/pam/pam_saint_decisions.txt:10`.
- **Light rite divergence and dangerous new rites** already use a generic low-fervor faith
  pulse. The Christian situation adjusts weights; it is not required for those two components.
  Sources: `common/on_action/religion_on_actions.txt:1613`;
  `events/dlc/pam/heresy/pam_heresy_pulse.txt:15`.
- **Papal bulls** are Catholic-only, but non-Papal religious heads already have separate decisions
  to declare rites heretical and permit/prohibit tenets. Porting the entire bull system would
  duplicate existing powers unless its presentation is a deliberate goal.
- Many remaining Christian checks choose art, names, descriptions, religious cost differences,
  historical targets or Church-situation catalysts. They do not hide the underlying feature.

Suggested order: cathedral event scheduler; Spread the Word; curated everyday/Hierarch events;
head-of-faith petitions; monastic patronage and county missions. Larger investiture, patron and
reconciliation systems can follow as separate designs.
