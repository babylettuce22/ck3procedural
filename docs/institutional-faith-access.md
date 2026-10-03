# Generated institutional faith access (CK3 1.20.0.3)

`Emit/Culture/InstitutionalFaithWriter.cs` runs after faith generation. It lifts individual
definitions from the installed game and changes their institutional eligibility. Missing
definitions or changed source patterns fail generation rather than silently shipping a partial
adaptation. Costs, DLC checks, project sites, contribution rules and native Christian behavior
remain in the lifted definitions.

## Adapted systems

| System | Change |
|---|---|
| Spiritual fulfillment | Add generated religions with clerical regions or historical/current sees to the native fulfillment type. This also opens fulfillment-gated confession, devotion, mentor, last rites and endow-masses decisions when their other requirements are met. |
| Promulgate Word of Pious Deeds | Actual generated Synod electors can take the decision without a Christian chancery domicile. Uses a faith-aware event: +30 native election candidacy score and prestige, or the native extra-piety Populist shortlist boost. Keeps the native initial cost, once-per-lifetime flag and other restrictions. |
| Cathedral great projects | All three tiers become eligible through fulfillment; bell rewards accept generated institutional faiths. Native cathedral building effects retain their ecclesiastical-government requirement. |
| Cathedral stories (procedural content only) | Frescoes, stained glass, construction and completion use the faith's deity and sacred traditions. Frescoes/glass draw a saint from the actor's faith registry, with generic wording if it is empty. The west-portal sermon admits institutional faiths and keeps its six native reward choices without Bible quotations. Vanilla content mode retains the installed stories. |
| Clerical arbitration | Enforce succession, request white peace and request victory accept generated institutional actors. The shared enemy eligibility check also accepts them; common faith/rite/head and war restrictions remain. |
| Senior clergy and monastic courts | Shared checks accept generated institutional characters, retaining their existing government, monasticism and relationship conditions. |
| Clergy dynasty legacy | An institutional dynast qualifies; existing DLC, unrestricted-legacies and already-unlocked-perk behavior remains. |
| Tenet popularity | Religion-based eligibility includes the same generated religions admitted to fulfillment, allowing the popularity system used by Synod candidates. The native trigger works when called on either a faith or a character. |

The engine assigns fulfillment **per religion**, not per faith. Sibling faiths of an admitted
generated religion therefore share that fulfillment type. Character/faith-specific adaptations
use `special_doctrine_gen_clerical_regions`; religions are never relabeled as Christianity.
Inherited religions are excluded from enrollment. Output reused for a world with no eligible
religions has the writer's five owned override files removed.

## Gates reviewed and retained

The audit checked the installed PAM decisions, great projects, buildings, scripted triggers,
interactions and activities for Christianity and Christian-fulfillment restrictions.

- Ecumenical councils already have a generated-faith adaptation in the Procedural file set.
- Great Schism, Kingdom of Heaven, antipope/Christian Church situation machinery, patron saints
  and Papal historical decisions require a broader feature adaptation, including their effects
  and referenced historical objects. Their gates remain.
- Christian situation catalysts and East/West church background choices remain tied to the
  Christian Church. Many Christianity checks in cardinal appointments, conversion and doctrine
  interactions affect those catalysts or AI weighting rather than player availability.
- Mass rite conversion's Christianity checks choose description text, rather than hiding the
  project. The clergy-court development decision also has Christian-specific descriptive text.
- Incite holy war has an additional Christianity check for AI actors; human visibility is not
  blocked by that particular check. It is retained in this pass.

Other fulfillment-gated vanilla events can still contain Christian wording. This change opens
their mechanics; it does not rewrite every event's religious flavor. Cathedral stories are adapted
separately by `Emit/Culture/CathedralFlavourWriter.cs`, called through the procedural-only
`HierarchyFlavourWriter` path. It overrides the installed event file at its matching path, changes
its theme to the generic faith theme, replaces Mary's historical character lookup, and broadens
the sermon gate. It preserves the installed option blocks, construction cadence and rewards.
Only the affected localization keys are replaced; shared Christian glossaries and Bible quote
providers remain available to other events. The saint-name custom localization checks for its
saved scope before using it, so older saves and faiths without registered saints have a fallback.

## Validation and game checks

Build and a fixture containing eligible, noninstitutional and inherited religions verify
enrollment, preservation of the native decision branch/cost/lifetime flag, all cathedral tiers,
and arbitration edits. EventFlow finds no problems in the new decision/event chain.
Unmodified installed definitions were lifted into a parallel fixture for Tiger comparison;
the local validator still has gaps in native 1.20 interaction/project schemas. Both fixtures
produce the same 45 errors and 17 warnings, with no new diagnostic messages in the adaptation. A complete
generated world was also tested with seed 81947 on Desktop/height.png. This does not establish
in-game UI or engine behavior.

The final full-world run completed. Its additional errors came from separately changed council
activity files (`ecumenical_council.txt` and `pam_investiture_council.txt`), outside this writer;
the inherited white-peace trigger also produces a native tooltip-complexity warning when lifted.
Full-repository validation is therefore not clean, despite the adaptation fixture having no
new diagnostics against its vanilla counterpart.

After regenerating and restarting CK3 with the updated mod:

1. As an actual Synod elector, open religious decisions. Promulgation should appear when the
   faith has a religious head and the character otherwise qualifies. A shortlisted candidate
   without an actual elector title does not qualify. Check both the score and extra-piety options.
2. As a duke-or-higher ruler of an admitted religion, inspect cathedral great projects. The
   native valid see/theocracy-capital site rules still apply. Upgrades require the previous tier.
3. Complete a cathedral in an ecclesiastical holding and verify building effects and bell rewards.
4. Inspect clergy arbitration between qualifying rulers of the same institutional faith and the
   clergy dynasty legacy for an institutional dynast.
5. In a procedural world, fund frescoes and stained glass: the choices should reference local
   saints/traditions, not Christian figures. Check a faith without saints for readable fallback
   wording. Inspect vaulting, bell-casting and completion, and the west-portal sermon with its six
   unchanged rewards. A world generated with vanilla content should still show vanilla stories.

`--verify-cathedral-flavour [fixture-directory]` checks both content modes against the installed
game, compares every event option block (including rewards, costs and AI weights), checks saint
selection/fallback and sermon eligibility, and verifies UTF-8 BOMs. An explicit directory keeps
adapted and vanilla-control fixtures for validator comparison.

Cathedral validation (2026-10-03, installed 1.20.0.3): the build and fixture checks passed;
EventFlow reported no findings. Adapted and unmodified event fixtures produced identical Tiger
diagnostic messages (44 errors from native great-project schema gaps; fixture encoding/logic
warnings). Full generation with seed 924031 completed and included the new stories. The full
world still has unrelated validation findings, and Tiger flags the completion story's native
`[province.GetName]` binding because its datafunction tables are incomplete. In-game rendering
and campaign behavior remain to be checked.

These expansion mechanics retain their By God Alone DLC gates; the project itself does not
require the expansion. Inspect `error.log` and `database_conflicts.log` after the game
test to confirm engine acceptance and the winning overrides.
