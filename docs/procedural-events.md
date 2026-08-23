# Procedural events — research, not a proposal

Status: research. Nothing here is implemented and nothing here should be built until the open
questions at the end are answered.

The prompt was "is a well-derived procedural event system worth it, as a general capability rather
than a procedural-history feature". The short answer is **yes, but not as an `EventBuilder`** — the
useful thing decomposes into three separable pieces with very different costs and very different
payoffs, and only one of them is about events at all.

---

## 0. The actual question

Not "can we generate CK3 events". We already do: `Emit/StruggleWriter.cs:641` writes
`events/gen_struggle_events.txt`, and it works.

The question is **where the per-world variation lives**. That single question decides whether a
piece of content should be generated, hand-written, or hand-written with holes in it — and getting
it wrong in either direction is expensive. Generating something that never varies buys nothing and
costs you comments, diffs and readability. Hand-writing something that must vary means either N
copies or a feature that does not respond to the world.

Everything below answers that question per system rather than in general.

---

## 1. What exists today

An audit, because the answer depends on it.

**Generated script that is event-shaped — one file, 20 lines.**
`StruggleWriter.WriteDriftEvent` emits a hidden, self-requeueing, zero-option ticker. No title, no
desc, no portrait, no localisation. It is the minimum possible event and it is the only one the
pipeline generates.

**Generated on_actions — four writers.**
`ArtifactWriter.cs:53`, `CompatibilityWriter.cs:591`, `HistoryWriter.cs:544`, `WarWriter.cs:21`.
All the same shape: an `on_action` block with an `effect` and no window. This is the pipeline's real
event vocabulary today, and it is notable that it is entirely *windowless* — the generator has never
needed to show the player anything.

**Hand-written events — 3,890 lines of script, 703 lines of localisation.**
`BaseFilesToCopy/Wilderness/events/` (3,672 lines) and
`BaseFilesToCopy/Fantasy/events/gen_aging_events.txt` (218). Shipped by `Emit/StaticFileWriter.cs`,
gated per set on a `MapConfig` bool, copied last, never overwriting anything generated. That
mechanism is good and none of what follows should replace it.

**A structured history that stops one step short of being an event system.**
`MapGen/Chronicle.cs` (659 lines) already produces exactly the thing an event generator would need:
a `ChronicleEvent` with `Kind`, `Year`, `Subject`, `Counterpart`, `Culture`, `CounterpartCulture`,
`Faith`, `CounterpartFaith` and a 0–3 `Tension` weight, indexed by title. Its own doc comment says
why the structured half exists separately from the prose:

> parsing generated English back into facts is the failure mode this type exists to prevent

Today it has exactly one consumer, `Emit/ChronicleWriter.cs`, which turns it into at most 9 lines of
localisation per title and nothing else. **The chronicle is a fully generated event stream that
currently emits only flavour text.** That is the most important fact in this document.

**Two coexisting Jomini emission styles.**
Thirteen writers build script with `StringBuilder` and literal `\t` (heaviest: `HistoryWriter` 178
tab-strings, `StruggleWriter` 129, `ArtifactWriter` 102). Seven use C# raw string literals
(`$$"""`). Nobody chose this split deliberately, but it is not arbitrary: **raw literals win where
the shape is fixed and only names vary; StringBuilder wins where the number of blocks is decided at
runtime.** That distinction reappears below as the whole answer.

---

## 2. An event has two halves, and they generate very differently

This is the load-bearing distinction.

**The wiring** — `type`, `theme`, `trigger`, scope plumbing, `immediate`, per-option effects,
cooldowns, the on_action or scripted_effect that fires it, the loc keys it promises to have.
Mechanical, verifiable, and ruthlessly punished by CK3 for small errors: a missing key is a silent
no-op, a bad scope is a log line nobody reads. Generation is strictly better than a human here. A
generator does not typo a key and, more importantly, a generator can be made to *prove* that every
key it referenced was also emitted.

**The prose** — `title`, `desc`, option names. Here is the actual bar, from
`wilderness_colonization.0010.desc`:

> ...younger sons with no land coming to them, families a bad harvest turned out, a few men who did
> not say why they were going. ... Someone has to be in charge of that arithmetic.

Nothing in this repo generates that, and nothing will. What the repo *does* generate is the
chronicle bank line — one sentence, from a bank of six, with a place name and a year filled in by
`Chronicle.Fill` (`MapGen/Chronicle.cs:548`). That works, and it works because a chronicle line is
one sentence read in a list of nine other sentences.

**So the prose ceiling is a real design constraint, not a hedge: a generated event must be short, or
it must be authored.** A generated event that wants three paragraphs and four distinctly-motivated
options will read as filler, and CK3 players are unusually good at spotting filler because vanilla
has so much of it.

The corollary is the useful one. Design generated events *to the ceiling*: one or two sentences, two
options, both legible from the structured data — who, against whom, at what price. Do not try to
generate the wilderness colonisation event. Generate the event that says "the ward at X failed and
the thing beneath it stirred; pay, or run".

---

## 3. Three regimes, and the rule for placing a system in one

**Regime A — fixed structure, fixed prose.**
The event is identical in every world. It may be *absent* in some worlds (gated on a setting), but
when present it is byte-identical. Aging and fading. Colonisation. The phenotype pulses.

*Verdict: `BaseFilesToCopy`, forever.* Building a C# DSL to emit a constant file is pure loss — you
give up inline comments (and this repo's events are half comment, deliberately), git diffs on the
thing that actually changed, and the ability to hand a `.txt` to somebody who knows CK3 but not C#.
There is no version of an EventBuilder that improves on `gen_aging_events.txt`.

**Regime B — fixed structure, generated references.**
The event's shape is authored once, but it names keys only this run knows: struggle and phase keys,
generated culture/faith/trait/region/wonder keys, a count decided by the map. `WriteDriftEvent` is
already this. So is most of `HistoryWriter`.

*Verdict: templating, not generation.* Either a raw string literal with `{{...}}` holes (the style
seven writers already use) or — better, see §5.2 — an authored `.txt` in `BaseFilesToCopy` with
slots that `StaticFileWriter` fills on copy. The variation is in the *names*, and names are exactly
what a template is for.

**Regime C — generated structure.**
The event's *shape* varies with the world: which options exist, what they cost, what they chain to,
and how many events there are at all. A Cult world needs an exposure chain a Church world does not.
A ladder with five ranks needs five rank-up events; one with two needs two. A high-backlash world's
events have a death option and a low-backlash world's do not.

*Verdict: this is the only regime that justifies new machinery,* and it is the regime the magic
system lives in almost entirely.

**The rule.** Ask: *if I reroll the seed, does the shape of this event's option list change?*
No, and no names change → A. No, but names change → B. Yes → C.

---

## 4. Where the systems in flight actually land

| System | Regime | Why | Vehicle |
| --- | --- | --- | --- |
| Aging / fading | A | Same in every world it ships in | Static set (already correct) |
| Wilderness colonisation | A | Deliberately world-agnostic; its value is authored design | Static set (already correct) |
| Phenotype assignment | A | Mechanism, not content | Static set (already correct) |
| Struggle drift ticker | B | One key, one file, fixed shape | Raw literal (already correct) |
| Struggle phase flavour | B | Per-struggle names into a fixed frame | Slot-filled static, or literal |
| Chronicle → gameplay hooks | **C** | Count and parties decided by the map | **Event IR** |
| Prophecy (magic Layer 5) | **C** | Predicate *and* consequence both generated | **Event IR** |
| Magic exposure / backlash | **C** | Chain shape follows the Institution and Price axes | **Event IR** |
| Magic rank-up | **C** | One per generated rank | **Event IR** |
| Ledger threshold consequences | **C** | Thresholds and consequences both generated | **Event IR** |
| Spell delivery (Decision/Scheme/…) | B/C | Frames are fixed; *which* frame is generated | Emission layer + IR |
| Counterplay (hunter, ward, CB) | B | Shapes are fixed, keys are not | Slot-filled static |
| Keystone coupling | **C** | Its whole point is that it is unforeseeable at author time | **Event IR** |

Two readings fall out of this table.

**First: Regime C is real and it is not small.** Seven of the magic doc's own deliverables sit in it.
That is a genuine answer to "is this worth building" — it is not one feature's worth of need, which
is the usual reason a generic system is a mistake.

**Second, and less comfortable: every Regime C event in that table is structurally simple.** One or
two options. No branching. A `trigger`, an `immediate`, an effect, maybe a follow-up via
`trigger_event`. The deep branching trees — the ones an "event tree editor" would be built for — are
all in Regime A, where they should stay hand-written. **The systems that need generated events do
not need generated event *trees*.** That is the single most important finding here. It deflates most
of the ambition in the original framing while leaving the useful core intact.

---

## 5. What to build instead of an "EventBuilder"

Three pieces. They are independently useful, they ship in this order, and each is worth doing even
if the next never happens. That property is the main argument for this decomposition over one big
class.

### 5.1 A Jomini emission layer — worth building regardless of events

The magic doc already names this (`Emit/Magic/JominiBuilder.cs`) but scopes it too narrowly. It is
not a magic concern; it is a *pipeline* concern that 13 writers currently solve by hand with `\t`
strings.

Three parts, roughly:

- **A block builder.** Indentation, quoting, `key = { … }` nesting, list blocks. Removes ~600
  hand-written `\t` strings and, more usefully, makes emission of a *runtime-decided number* of
  blocks read like the raw-literal style rather than like string surgery.
- **A key registry.** Every generated key is minted through one place that records what kind of
  thing it is and which file declared it.
- **A localisation registry.** Every emitted key that needs loc registers its English there, and the
  loc files are written once at the end instead of by 13 writers each opening `l_english:`.

The prize is not tidiness — it is **referential closure as a checkable invariant**. Magic doc
validation invariant #4 ("every key referenced is emitted; every key emitted is localised") has no
machinery behind it today and cannot be checked at all. With a key registry and a loc registry it is
a set difference, and it runs in the headless verify loop next to the existing 8-error baseline.
That is the difference between "generated content that mostly works" and "generated content that
cannot ship a dangling reference".

This is the highest-confidence recommendation in the document. It is not speculative, it has a
concrete existing cost it removes, and everything else depends on it.

### 5.2 Slot-filled static files — the cheapest real win

Extend `StaticFileWriter` to substitute named slots while copying, and give it the world's key
tables as the substitution source.

This turns Regime B from a C# problem into an authoring problem. A struggle flavour event, a
counterplay casus belli, a ward building — all get written as ordinary CK3 `.txt` files, with
comments, syntax-highlighted, diffable, testable by dropping them in a mod by hand — but with
`$STRUGGLE_KEY$` where a generated name goes. The gating machinery (per-set, per-`MapConfig` bool),
the never-overwrite rule, and the ignore-file support already exist and do not change.

Cost is small: substitution on copy, plus a strict-mode failure when a slot has no value (silent
empty substitution is how you get a mod that loads and does nothing). Payoff is that a large class
of "we need this to name a generated thing" stops requiring a new C# writer at all.

The one thing to be careful about: this must not become a general macro language. Slots are name
substitution. The moment somebody wants a conditional or a loop in a `.txt`, that content has
crossed into Regime C and belongs in the IR.

### 5.3 An event IR — the actual new capability

The right model is already in the repo, and it is `Chronicle`, not any emitter. Its shape is: a
structured record that a *generator* writes and *several* consumers read, with prose carried
alongside as data rather than derived from it. Copy that relationship exactly.

Roughly:

```csharp
public sealed record GenEvent(
    string Namespace, string Id,
    EventShape Shape,          // character / letter / hidden / activity …
    string? Theme,
    IReadOnlyList<Condition> Trigger,
    IReadOnlyList<EffectAtom> Immediate,
    IReadOnlyList<GenOption> Options,   // 0..3; see the prose ceiling
    ProsePick Prose,           // bank + slots, resolved late, exactly like Chronicle.Fill
    FireRule Fired);           // on_action / pulse / trigger_event from another GenEvent
```

Three constraints matter more than the record layout:

1. **`EffectAtom` is a fixed, hand-audited palette.** The magic doc already states this and it is
   correct: *"generating script from atoms is tractable, generating script from nothing is not."*
   The atom palette is the contract with CK3 and it should be shared with the spell grammar rather
   than duplicated — a spell's effect and an event option's effect are the same kind of thing.
2. **Options are capped low and prose is banked, not composed.** Three options maximum, and every
   option's text comes from a bank keyed on what the option *does*, filled the way `Chronicle.Fill`
   fills. This is the prose ceiling from §2, encoded as a type constraint so it cannot be quietly
   violated later.
3. **`FireRule` is part of the IR, not the emitter.** The most common way generated CK3 content
   silently does nothing is that it exists and is never fired. If the firing rule is in the record,
   "every event is reachable" is a validator pass, not a hope.

The emitter reading this is small — a few hundred lines — precisely because §5.1 did the hard part.

**The proof-of-concept should be the chronicle, not magic.** The data already exists, is already
structured, and is currently inert. A `Feud` with `Tension = 3` between two named houses, or a
`Frontier` between two heritages, can become a real starting hook — an opinion modifier, a scripted
relation, a claim, or a small fireable event naming both parties — using prose banks that are
*already written*. It proves the IR against real generated data with no new derivation work, and it
converts the chronicle from a lore panel into a load-bearing system, which was its stated design
intent all along.

---

## 6. When an event is the wrong vehicle entirely

Worth stating because a general event capability creates pressure to use it.

CK3 gives cheaper mechanisms for most of what tempts you toward an event:

| Want | Cheaper than an event |
| --- | --- |
| A persistent consequence | `common/modifiers` on character/county/province |
| A reusable effect block | `common/scripted_effects` |
| A silent world reaction | `on_action` with `effect` and no window |
| A state that progresses | trait with a track (the aging system already does this) |
| A number the player manages | `common/script_values` + variables |
| A visible ongoing choice | decision, not an event |

An event costs a window, a title, a desc, N option strings, and — the expensive one — a slice of the
player's attention budget. **Generated content that fires many low-value events is worse than
generated content that fires none**, because event spam is the specific failure mode CK3 players
already complain about in vanilla.

A useful discipline for the IR: give every generated event an explicit reason it must be a window
rather than an on_action effect. If the reason is not "the player makes a choice here" or "the
player must be told something they could not otherwise learn", it should not be an event. This is
cheap to enforce (a required field) and it is the main thing that would stop a working event
generator from making worlds worse.

---

## 7. The GUI question

Three separable ideas were bundled in "UI to edit and see how event trees flow", and they have
different verdicts.

**A viewer: yes, and it is worth building early.** Not for polish — for debugging. The magic doc's
validation story (reachability within N years, Five Differences across seeds, no degenerate spell)
is unreadable as log lines and obvious as a picture. The machinery is largely present:
`Gui/InspectorForm.cs` (194 lines) plus four subclasses is an established pattern, and
`Gui/RealmGraph.cs` (136 lines) already draws a node graph. A "what did this world generate" page
showing the acquisition graph, the spell ladder, the event set and the prophecy predicates would pay
for itself the first time a world generates an unreachable rank.

**A visual event *editor*: no.** It competes with a text editor at the thing text editors are best
at, and it loses on this repo's own evidence — the hand-written events here are roughly half
comment, with twenty-line blocks explaining *why* three options exist. A node graph cannot hold
that, and the reasoning is the part worth keeping. Editing generated event script by hand also
breaks the property the README states as central — that a seed and a heightmap fully determine a
world — because the edit lives nowhere the next run can see.

**Editing the IR: yes, and that is the real answer.** `WorldEdits` + the inspectors already
establish the right shape: you edit the *derived object* (a `Culture`), not the emitted text
(`landed_titles`), and re-emission follows. The same applies here — the editable surface is this
world's laws, thresholds, ladder and spell list, not the `.txt`. That keeps determinism, keeps the
edit meaningful across a re-run, and is a much smaller build than an event graph editor.

There is also a "flow" view worth having that is *not* an editor and is not per-event: **the world's
firing graph**. Nodes are on_actions, pulses, decisions and events; edges are "fires". Its purpose
is to answer "what can actually happen in this world, and is anything orphaned" — which is a
validator with a picture attached, not an authoring tool.

---

## 8. Staging

Each stage is useful alone, which is the point.

1. **Emission layer** (§5.1). No new content. Retires the `\t` duplication, and lands referential
   closure and loc closure as checks in the headless verify loop.
2. **Slot substitution in `StaticFileWriter`** (§5.2). Small. Unlocks Regime B authoring without any
   new C# per feature.
3. **Chronicle → hooks** (§5.3 proof-of-concept). First real generated event content, on data that
   already exists, with prose that already exists.
4. **Event IR generalised**, once the chronicle case has shown what the record actually needs. Not
   before — designing the IR against a hypothetical magic system rather than a real consumer is how
   this becomes over-general.
5. **Inspector page**, alongside 4 rather than after it, because the IR is unreviewable without one.
6. **Magic consumes the IR** as its Layer 3/5 emitters.

---

## 9. Risks and open questions

**Risks**

- *Over-generalisation.* The IR is only worth its weight if Regime C is genuinely populated. If
  magic slips, this becomes a framework with one three-event consumer. Mitigation is stage 3: make
  the chronicle a real consumer first, so the IR is never speculative.
- *Prose regression to filler.* The prose ceiling is easy to state and easy to erode one option at a
  time. Encode it in the type (option cap, banked-only text) rather than in a comment.
- *Event spam.* See §6. The required-justification field is cheap; adding it later is not.
- *Silent CK3 failure.* Generated events fail the same way generated everything fails here — no log
  line, nothing happens. This is an argument for §5.1 first, not an argument against the feature.

**Open questions**

1. Does the chronicle hook produce content anyone wants, or is a generated feud better expressed as
   a starting opinion modifier with no window at all? Stage 3 is partly an experiment about this,
   and the honest answer may be "no window", which would still be a useful finding.
2. Is the `EffectAtom` palette shared with the spell grammar, or does the event IR keep its own?
   Shared is obviously right in principle; it also couples two unbuilt systems, so the decision
   should probably wait for stage 4.
3. How does slot substitution interact with the never-overwrite rule? A slot-filled static file is
   generated content wearing a static file's clothes, and the ordering guarantee in
   `StaticFileWriter`'s doc comment was written before that was possible.
4. Do generated events need `ai_will_do` weights from day one? The magic doc says AI participation is
   non-negotiable; if that holds, the IR needs an AI weight field from the start rather than bolted
   on, because retrofitting it means touching every generator that produces an event.

---

## 10. Bottom line

A general procedural event capability is worth building, and it is worth building *sooner* than
magic needs it, because its first two stages are pipeline hygiene that pay off immediately and
independently.

But the thing to build is not an EventBuilder. It is **an emission layer with closure checking, slot
substitution for hand-authored files, and a small Chronicle-shaped IR for the genuinely
shape-varying cases** — with hand-written events remaining the right answer for everything whose
value is authored design, which is most of what currently exists and all of what is best about it.

The single most actionable observation: **the chronicle is already a generated event stream that
only emits flavour text.** Whatever gets built first should give it a second consumer.
