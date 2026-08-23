# Procedural Magic — a design skeleton

Status: draft for review. Nothing here is implemented. The goal is to agree on the *shape*
before any C# is written.

---

## 0. The actual problem

The easy version of this feature is a spell list with generated names. That fails, because after
two worlds the player has learned the list and every subsequent world is the same game with
different nouns. Dwarf Fortress's myth-and-magic work is interesting for the opposite reason: it
generates the *rules*, and the spell list is a consequence.

So the design target should be falsifiable. Call it the **Five Differences test**. Two worlds have
genuinely different magic if and only if they differ in at least three of:

1. **Acquisition** — how a character gets in at all. Changes the early game.
2. **Resource** — what you are spending and husbanding. Changes the moment-to-moment loop.
3. **Failure mode** — what you are afraid of. Changes what "playing well" means.
4. **Social position** — who you must hide from, serve, or outrank. Changes the diplomacy.
5. **World coupling** — what magic does to the map and to everyone not using it.

A generated system that differs only in effect magnitudes fails the test even if every string is
unique. A generated system that shares an effect palette but differs on those five axes passes,
and will feel like a different game.

This document is structured as five layers, each of which is a separate C# stage and a separate
emitter group, so they can be built and shipped independently.

```
  Layer 1  COSMOLOGY   what magic is                     (abstract, no CK3 vocabulary)
  Layer 2  LAWS        who, at what price, under whom    (the IR; the thing the GUI inspects)
  Layer 3  INSTRUMENTS spells, ranks, institutions       (the generated content, budget-balanced)
  Layer 4  PLACEMENT   magic on the map and in history   (ley field, sites, starting state)
  Layer 5  CONSEQUENCE the ledger, prophecy, world drift (the passive engine-driven half)
```

---

## Layer 1 — Cosmology

Ten axes. Sampled with weights, then repaired by a coherence pass. These are deliberately
*mechanical* categories, not thematic ones — theme is downstream naming.

### Axis 1 — SOURCE: what magic *is*

| Value | Consequence |
| --- | --- |
| `Force` | Impersonal law. No one to bargain with. Power comes from knowledge, so books and teachers are the bottleneck. |
| `Entities` | Beings grant it. Requires an entity roster, favour tracking, wrath. Negotiation replaces study. |
| `Substance` | A material/energy with a location. Extractable, tradeable, **exhaustible** — magic becomes a resource war. |
| `Inheritance` | It is in the blood. The marriage market becomes the magic economy. Deepest CK3-native coupling. |
| `Language` | Words and true names have power. Knowledge is an object: stealable, burnable, copyable. |
| `Wound` | Magic is damage left by a past event. Every use widens the tear. Ties directly to the chronicle. |

### Axis 2 — ACCESS: the acquisition graph

`Born` · `Taught` · `Bargained` · `Found` · `Suffered` · `Bought` · `Stolen`

`Stolen` deserves special note: a world where the only way to gain power is to take it from
another practitioner is zero-sum, and the AI will hunt the player without any bespoke AI work.
Worlds may have two access edges (e.g. `Born` OR `Bargained`), which creates factional structure
for free — two populations with the same power and incompatible politics.

### Axis 3 — FUEL: the resource you manage

| Value | The loop it creates |
| --- | --- |
| `Ambient` | A per-province scalar that depletes and regenerates. Land value = arcane value. Land war. |
| `Vital` | Health, lifespan, fertility of the caster. Every cast is a bite out of your own campaign. |
| `Sacrificial` | Other characters. Cheap in resource, ruinous in opinion and tyranny. |
| `Devotional` | Piety. The faith owns the tap and can turn it off. |
| `Material` | Gold and components. Boring alone, excellent combined with `Substance` scarcity. |
| `Temporal` | Only inside windows — seasons, conjunctions, situation phases. Turns play into planning. |
| `Debt` | Free at cast time. The ledger collects later, with interest, at a time you do not choose. |

### Axis 4 — PRICE: the failure mode

`Corruption` (a trait ladder ending in something that is no longer a person) ·
`Taint` (heritable — your children pay) ·
`Depletion` (the land pays; terrain and harvest degrade) ·
`Attention` (something notices and comes) ·
`Stigma` (purely social; secrets, opinion, witch-hunts) ·
`Instability` (a world meter; catastrophes hit everyone, including the innocent) ·
`Backlash` (immediate random misfire)

Worlds get one dominant price and optionally one minor one. The dominant price is the single
strongest determinant of how the world *feels*, and it must be legible to the player within the
first twenty years or the system reads as free power.

### Axis 5 — INSTITUTION: social position

`College` (open, ranked, chartered) · `Cult` (secret society) · `Church` (faith-integrated) ·
`Crown` (royal monopoly, illegal for vassals) · `Folk` (unregulated and common) ·
`Outlaw` (actively hunted) · `Solitary` (no institution; every practitioner a rival)

### Axis 6 — DOMAIN WEIGHTS: what it can do

A distribution over `Life`, `Death`, `War`, `Mind`, `Nature`, `Fate`, `Craft`. Worlds emphasise
two or three and **forbid** one or two outright. The forbidding matters more than the emphasis:
"this world has no healing" is a structural fact that changes every other decision the player
makes. A world that can do a bit of everything is a world with no identity.

### Axis 7 — CEILING: the largest thing magic can touch

`Personal` → `Court` → `Realm` → `World`. Gates which effect verbs are legal.

### Axis 8 — PREVALENCE

`Hidden` (<1% of characters) · `Rare` (~3%) · `Common` (~15%) · `Universal` (nobility-wide).
Feeds the existing runtime phenotype assignment hook, which already tags generated courtiers.

### Axis 9 — RELIABILITY

`Deterministic` (a tool) · `Probabilistic` (a gamble) · `Capricious` (an entity decides, and it
has opinions about you). Changes whether the player is engineering or supplicating.

### Axis 10 — COUNTERPLAY (must be non-empty)

`Wards` (buildings/null provinces) · `Hunters` (a faction with a CB and a scheme) ·
`Compensation` (non-practitioners get a structural advantage) · `Deterrence` (mages check mages) ·
`Scarcity` (the fuel simply runs out).

### The coherence pass

Independent sampling produces incoherent nonsense at a high rate, so the resolver runs hard
constraints and then soft repairs:

- `Fuel=Devotional` ⇒ `Institution ∈ {Church, Cult}`.
- `Source=Entities` ⇒ entity roster non-empty, `Reliability ≠ Deterministic`.
- `Price=Depletion` ⇒ `Fuel=Ambient` and a ley field must be placed.
- `Ceiling=World` ⇒ price must include `Instability` (otherwise the campaign ends by year 40).
- `Access={Born}` + `Institution=Outlaw` ⇒ legal and excellent; force a secrets/exposure loop.
- `Prevalence=Universal` ⇒ cap `Ceiling` at `Court` (everyone having realm-scale magic is soup).
- `Source=Inheritance` ⇒ raise the weight on dynasty-legacy and marriage-market emitters.

Constraints get checked rather than assumed: the resolver repairs and re-checks to a fixed point,
and fails loudly to the run log if it cannot converge, rather than emitting a broken world.

---

## Layer 2 — The Laws (the IR)

Derive produces one fully-resolved object. Emit only reads it. This matches the existing
`Generate` / `WriteMod` split and means the GUI can render a "magic" inspector page from the same
object the emitters consume — no second source of truth.

```csharp
public sealed record MagicSystem(
    Cosmology            Myth,          // the ten axes, resolved
    AcquisitionGraph     Access,        // nodes = states, edges = how you move between them
    FuelRule             Fuel,
    PriceRule            Price,
    InstitutionRule      Institution,
    DomainWeights        Domains,
    IReadOnlyList<Entity>    Entities,  // empty unless Source = Entities
    IReadOnlyList<Rank>      Ladder,    // progression states
    IReadOnlyList<Spell>     Spells,
    IReadOnlyList<Prophecy>  Prophecies,
    LeyField             Ley,           // per-province scalar, may be flat
    Counterplay          Counter,
    KeystoneLink         Keystone,      // the deliberate cross-system coupling, see Layer 5
    Lexicon              Names);        // drawn from the world's generated language
```

`AcquisitionGraph` is worth calling out as a real graph rather than an enum, because it is what
the tutorial text, the AI weights, and the reachability validator all read. Nodes are
`Mundane → Latent → Initiate → Rank N → Terminal`, edges carry a trigger, a cost, and a
probability, and the terminal node is often *not* good for you (`Corruption` ends in a monster;
`Debt` ends in collection).

---

## Layer 3 — Instruments

### The spell grammar

A spell is not written, it is assembled. Six slots:

```csharp
public sealed record Spell(
    string Key, string Name,
    Delivery Delivery,                    // how the player performs it
    TargetKind Target,                    // self, courtier, rival, vassal, province, title, army, dynasty
    IReadOnlyList<Precondition> Requires, // rank, fuel, place, time window, secrecy, entity favour
    CostVector Cost,                      // fuel type x amount
    IReadOnlyList<EffectAtom> Effects,    // verb x scope x magnitude x duration
    BacklashSpec Backlash,                // probability x severity x kind
    Exposure Exposure,                    // who learns you did it, and what that costs
    int Rank, double Power, double Price);
```

**Delivery** is the slot that does the most work, because CK3 gives us six genuinely different
interaction shapes and they produce completely different play:

| Delivery | CK3 vehicle | Play pattern |
| --- | --- | --- |
| `Decision` | `common/decisions` in a generated decision group | Instant, self-directed, cooldown-gated |
| `Interaction` | `common/character_interactions` | Targeted, the target may accept or refuse |
| `Scheme` | `common/schemes` | Contested over time, agents, secrecy, discovery |
| `Activity` | `common/activities` | A ritual with locales, guests, phases, and options |
| `Story` | `common/story_cycles` | A per-character arc that ticks on its own for years |
| `Passive` | `common/traits` + `common/modifiers` | Always-on; the price is paid continuously |

A world that delivers its magic as schemes plays nothing like a world that delivers it as
activities, even with identical effects. Delivery weights are drawn from the cosmology
(`Institution=Cult` biases hard toward `Scheme`; `Church` toward `Activity`; `Solitary` toward
`Decision` and `Story`).

**EffectAtom** verbs are a fixed palette gated by domain weights and ceiling — roughly forty
atoms across the seven domains, each with a known power weight and a known script emission. The
palette is fixed and hand-audited; the *selection and combination* is generated. This is the
single most important scoping decision in the whole design: generating script from atoms is
tractable, generating script from nothing is not.

### The budget

```
Power(s) = Σ  w(verb) · magnitude · scope_mult · duration_mult · reliability
Price(s) = fuel_price(Cost) + P(backlash) · severity + exposure_risk · stigma_weight

Constraint:  | Power(s) − λ · Price(s) |  <  ε        for every spell
```

`λ` is a per-world exchange rate. Low λ = cheap, nasty, ubiquitous magic. High λ = expensive,
safe, rare magic. It is one number and it retunes the entire world, which makes it the right
thing to expose as a settings slider.

Rank curve: `Power_max(r) = P0 · g^r`, with spell count per rank falling as power rises, so the
top of the ladder is two or three decisions and not twenty.

**Non-degeneracy** gets enforced, not hoped for: no spell may exceed 1.5× the median Power/Price
ratio, no single spell may satisfy a title's entire claim path, and any effect touching
succession or inheritance requires a price from the heritable set.

### The institution as generated content

Whatever `Institution` came out becomes real files: a `College` emits court position types,
buildings with rank requirements, and an education modifier; a `Cult` emits a secret type, an
exposure event chain, and a scheme; a `Church` emits doctrines on the generated faiths and a holy
order; `Crown` emits realm law and vassal contract obligations; `Outlaw` emits a casus belli, a
hunter court position, and a trait that must be hidden.

---

## Layer 4 — Placement

Magic has to be *somewhere*, or it is a menu rather than a world.

**The ley field** is a scalar over provinces, derived from fields that already exist rather than
from fresh noise: drainage extremes (headwaters and river mouths), elevation extremes, the
habitability minimum (magic likes places people do not), existing wonder sites, and — if
`Source=Wound` — distance from a generated epicentre. It emits as province flags and variables
set in province history, gates buildings and spell preconditions, and gets a thirteenth GUI
preview view alongside the twelve that exist.

**Historical seeding** puts the system into the world's past rather than bolting it onto the
present: chronicle entries for the founding event, some starting artifacts reclassified as
magical, a fraction of starting rulers holding the trait per `Prevalence`, one wonder site
designated the epicentre, and one extant institution with a real holder. The chronicle already
generates a past nobody can currently read; this gives it consequences.

**Portrait coupling** is free and worth taking: the race gene machinery already in the tool can
render a corruption ladder visibly on the portrait via HSV-shift genes, which is the same trick
already used for fantasy races. A world whose price is `Corruption` should show it on faces.

---

## Layer 5 — Consequence (the passive half)

This is the layer that makes it a system instead of a toybox, and it is the part the engine
drives without the player.

### The Ledger

One global variable. Every cast — player *and* AI — adds `Power(s)` to it. It decays annually at
a generated rate. Threshold crossings fire world-level consequences drawn from the price rule:
a situation advancing a phase, an epidemic outbreak seeded at the highest-ley province, terrain
degradation, an entity waking, a hunter faction forming.

The point is aggregation. Individual choices are small; the world moves because *everyone* is
choosing. It also means an AI-heavy world drifts on its own, which is exactly the passive
behaviour that makes generated content feel alive.

### The prophecy engine

Cheap, and possibly the most Dwarf-Fortress-feeling thing in the document.

Generate a predicate over world state — a title held by a given dynasty, N counties of a faith,
a character with a trait alive past a year, the ledger above a threshold — and pair it with a
consequence. Emit the predicate as an on-action pulse check, the foreshadowing as chronicle text
and a legend visible at game start, and the consequence as whatever the ceiling allows.

Then generate some prophecies that are **false**: the foreshadowing exists, the predicate is
unsatisfiable, nothing ever happens. A world where every prophecy comes true is a world where
prophecy is just a quest log.

### The keystone

Deliberately generate at least one **cross-system coupling** to another generated subsystem: a
spell that is cheap only during a particular phase of the generated situation; a curse that
interacts with the generated epidemic's susceptibility; a rank that can only be reached by a
member of a struggle's rival faction; a ley node inside a wonder. Emergence between subsystems is
what makes players talk about a world, and it is far more reliable to generate one on purpose
than to hope it falls out.

---

## The counterplay requirement

Every world must answer "what stops this". Unanswered, the player becomes a wizard-king by 1100
and the campaign is over. The generated counterplay is a first-class object with its own emitters,
and the validator refuses a world without one. Corollary: any world granting extended life must
also generate a succession counter-pressure, or the dynasty simulation — which is the actual game
— stops running.

---

## AI participation

Non-negotiable, and cheap if designed in: every generated decision, interaction and scheme gets
generated `ai_will_do` weights derived from the same axes; on-action pulses give AI practitioners
opportunities scoped to *landed and flagged* characters only; and a fraction of consequences are
authored to happen to AI characters visibly, so the player sees magic happening to other people
rather than only when they press a button.

---

## IR → CK3 mapping

| IR construct | Files written |
| --- | --- |
| Ranks, latency, corruption ladder | `common/traits`, `common/genes` + portrait modifiers |
| Fuel | `common/script_values`, character/province variables, `common/scripted_effects` |
| Spells (`Decision`) | `common/decisions` + a generated `decision_group_type` |
| Spells (`Interaction`) | `common/character_interactions`, `common/scripted_relations` |
| Spells (`Scheme`) | `common/schemes`, `common/secret_types` |
| Spells (`Activity`) | `common/activities/*` (types, intents, locales, pulse actions) |
| Spells (`Story`) | `common/story_cycles` |
| Backlash, price, wrath | `common/on_action`, `common/modifiers`, `common/deathreasons` |
| Institution | `common/court_positions/types`, `common/buildings`, `common/laws`, `common/holy_orders` |
| Entities | generated faith deities, `common/story_cycles` for pacts, favour variables |
| Ley field | `history/provinces` flags/variables, building gates, geographical regions |
| Ledger + world drift | `common/situations`, `common/epidemics`, global variables, on-action pulses |
| Prophecy | on-action predicate checks, `common/legends`, chronicle text |
| Counterplay | `common/casus_belli_types`, hunter court position, ward buildings |
| Artifacts, enchanting | existing artifact writer, `common/inspirations` |
| Summons | `common/men_at_arms_types` |
| All names and tooltips | `localization/english`, `common/customizable_localization` |

Every one of these is a plain-text format the tool already writes neighbours of. The exact
capabilities of the newer ones (schemes, situations, legends, story cycles) need verifying against
1.19 files before they are relied on, in the same style as the rest of the project.

---

## C# architecture

```
World/Magic/          Cosmology.cs        axes, sampler, coherence resolver
                      MagicSystem.cs      the IR
                      Acquisition.cs      the graph + reachability proof
                      SpellGrammar.cs     atoms, assembly, budget
                      Entities.cs         roster, favour, wrath tables
                      Prophecy.cs         predicate + consequence generation
                      LeyField.cs         placement off existing fields
                      Keystone.cs         cross-subsystem coupling selection
                      MagicValidator.cs   the invariants below

Emit/Magic/           TraitEmitter, DecisionEmitter, InteractionEmitter, SchemeEmitter,
                      ActivityEmitter, StoryEmitter, OnActionEmitter, InstitutionEmitter,
                      LedgerEmitter, ProphecyEmitter, CounterplayEmitter, MagicLocEmitter
                      + JominiBuilder.cs  shared indentation/quoting/block helper
```

Pipeline position: cosmology resolves **early** (before cultures and faiths, so both can be
written knowing the world's stance on magic); placement runs after provinces and wonders;
emitters run in the write half, after faiths, before compatibility.

---

## Validation invariants

The headless verify loop already runs a whole world into the scratchpad against a known error
baseline, so these are checkable rather than aspirational:

1. **Reachability** — a start character can reach Rank 1 by the acquisition graph within N years.
2. **Counterplay non-empty.**
3. **No degenerate spell** (Power/Price outlier bound).
4. **Referential closure** — every key referenced is emitted; every key emitted is localised.
5. **Ceiling/price coupling** — world-scale power implies world-scale price.
6. **Five Differences** — hash the loop-defining tuple; consecutive seeds must differ on ≥3 axes.
7. **Error budget** — zero new entries above the existing baseline.
8. **Performance** — no unbounded per-character annual pulse; pulses scoped to landed + flagged.

---

## Discoverability

A procedural system nobody can read is noise. Three cheap surfaces: a generated grimoire document
written into the mod folder alongside the mod; the bookmark and world description stating the
world's rules in plain language; and an in-game codex built from a decision group. Note the known
constraint that event text widgets silently store nothing without the right scope setup, and that
option text cannot display it — so the codex should lean on decision descriptions rather than
event boxes.

---

## Staging

| Tier | Adds | Worth on its own? |
| --- | --- | --- |
| T0 | Cosmology resolve, traits, prevalence, portrait morph, loc | Yes — worlds gain a stance, visibly, with no player agency |
| T1 | Spells as decisions, fuel, backlash, budget, validator | Yes — this is a playable magic system |
| T2 | Institution, ley field, buildings, court positions, secrets | Yes — magic gets a social and geographic address |
| T3 | Ledger, world drift, prophecy, entities | Yes — this is where it stops being a toybox |
| T4 | Schemes, activities, story cycles, incursions, keystone | Polish and depth |

T0+T1 is the minimum that passes the Five Differences test. T3 is where it earns the comparison
to what DF is doing.

---

## Two worked examples

Both are single seeds through the resolver, written out to show that the *structures* diverge and
not just the strings.

### Seed A — "the salt debt"

```
Source      Wound              Access     Suffered (survive the wasting) | Bought (rare)
Fuel        Debt               Price      Instability (dominant) + Taint (minor)
Institution Crown              Domains    Nature, Death   (Life forbidden, Mind forbidden)
Ceiling     Realm              Prevalence Rare        Reliability Deterministic
Counterplay Hunters + Scarcity Keystone   ledger threshold advances the generated drought situation
```

Play pattern: magic is free at the point of use and the crown licenses it, so the early game is
about getting *licensed* rather than getting powerful. Every cast raises a world meter that makes
the drought worse for everyone, so the interesting decision is political — you are drawing on a
commons, your vassals can see the meter, and the neighbours who did not cast will blame you when
their harvest fails. No healing exists, so the plague generated elsewhere in the world stays
lethal. Terminal state: the debt collects.

### Seed B — "the inherited tongue"

```
Source      Language           Access     Born | Taught (from a book you must physically hold)
Fuel        Vital              Price      Corruption (dominant, visible on portraits)
Institution Cult               Domains    Mind, Fate      (War forbidden)
Ceiling     Court              Prevalence Hidden      Reliability Probabilistic
Counterplay Deterrence         Keystone   rank 3 unreachable except inside a struggle faction
```

Play pattern: nothing here touches armies, so it never resolves a war — it resolves *courts*.
Delivery skews to schemes and secrets, so the loop is exposure management, not resource
management. Power costs your own health, which means the tension is between using it and living
long enough to matter. Books are objects, so they can be stolen, and other cultists want yours.
Terminal state: you become something your heirs must deal with.

Same tool, same map, same emitters. Different game.

---

## Risks

- **Performance and save size.** Per-character variables across thousands of characters, and
  on-action pulses, are the two ways to make a generated world unplayably slow. Scope everything
  to landed and flagged characters, and prefer flags over variables where a boolean will do.
- **AI incompetence.** If the AI cannot use the system, magic becomes a player-only cheat and the
  world feels dead. This is the most likely failure of the whole feature.
- **Opacity.** A system nobody can figure out is indistinguishable from a broken one.
- **Silent failure.** CK3's failure mode for script errors is often nothing at all, which is
  exactly why the validator and the error budget matter more here than in the map half.
- **Scope.** T0 through T2 is a large amount of work. T3/T4 should not be started until T1 has
  been played.

---

## Open questions for review

1. **Fixed atom palette — agreed?** Everything above rests on effect atoms being a hand-audited
   fixed set. If we want free-form generated script, most of the validation story collapses.
2. **How much may magic touch the map?** Terrain change and epidemic seeding are the most
   dramatic couplings and the most likely to break other generated systems.
3. **Should worlds be able to roll "no magic"?** A mundane world with false prophecies and
   superstition is a legitimate and cheap outcome — and it makes magical worlds feel rarer.
4. **One system per world, or two competing ones?** Two incompatible traditions is a much richer
   world and roughly double the work, plus a whole interaction surface between them.
5. **Player-facing complexity budget** — how many spells at rank 1 before it stops being legible?
6. **Where does this sit relative to the races work?** `Source=Inheritance` overlaps heavily with
   the existing race machinery, and they should probably share the trait and gene layer.
