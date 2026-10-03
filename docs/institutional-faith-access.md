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
their mechanics; it does not rewrite every event's religious flavor.

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

By God Alone remains required. Inspect `error.log` and `database_conflicts.log` after the game
test to confirm engine acceptance and the winning overrides.
