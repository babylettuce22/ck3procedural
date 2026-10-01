# CK3 Procedural Worlds: Landed-Family Seats (Gentry) Design Notes

Working design record — October 1, 2026. **Proposal only; no implementation.** Nothing below has
been tested in game. Statements about engine behaviour are marked **verified** (already measured or
relied on by this repository), **vanilla** (read from vanilla's design, not tested here), or
**unverified** (an assumption that needs an in-game test before anything is built on it).

## 1. Goal

A house can hold a hereditary family seat that is its own title. The house head stays playable
while holding it, even after losing every county, so losing your land doesn't end the game. A house
can climb from that seat to a kingdom, the way many real families rose from a manor.

Non-goals: replacing feudal succession, making every courtier playable, or adding a new map layer.

## 2. What already exists

| Piece | Status | Source |
|---|---|---|
| Landless `noble_family` titles (`landless = yes`, `noble_family = yes`, `always_follows_primary_heir`, `destroy_if_invalid_heir`) | verified — the generator already writes them | `Emit/ContentWriter.cs` `WriteNobleFamilyTitles`; `MapGen/History/Prehistory.cs` `RebuildNobleFamilies` |
| `estate` domicile created for a landed owner holding a noble family title | verified (as a control) | notes in `BaseFilesToCopy/Wilderness/common/governments/00_colony_government.txt` |
| Our own custom domicile type (`camp` on a landed owner) | verified to **fail**: engine never created it | same file |
| `landless_playable = yes`: administrative family heads stay playable without land | vanilla | Roads to Power governments |
| `noble_families = yes` on a feudal government (Sōryō) | vanilla | `Governments.cs` `AllowsNobleFamilies` comment |
| Generator already re-declares vanilla governments | verified | `BaseFilesToCopy/Core/common/governments/zz_gen_japan_government_types.txt` |
| Landless adventurers (camps) as a playable landless state | vanilla | `landless_adventurer` mechanic |

So we should reuse the noble family title and the `estate`. A new kind of domicile title would
probably fail the way the colony camp did.

## 3. Recommendation: an earned seat, plus a landless gentry government

The options considered:

- **All feudal houses get a seat.** Rejected. Landless AI families would build up without limit.
  Administrative games limit them through appointments; feudal has nothing comparable.
- **Gentry as a government that landed realms use.** Rejected as a separate feature. It would be
  Sōryō minus the Japanese layers, and it doesn't address losing your land.
- **Recommended: the two together, in different roles.**
  - The **seat is earned** (a decision, a grant or an event), not given by government. Whether a
    house has one is about the house, not the realm's laws.
  - **Gentry is a landless-only government** that a seat holder moves to when their last county is
    lost, the way `landless_adventurer` is the government of a camp. It carries
    `landless_playable = yes`, `noble_families = yes` and `domicile_type = estate`. Gaining a
    county moves the holder back to the landed government that fits them.

That keeps vanilla feudal untouched, limits how many families exist, and gives the gentry
government a job Sōryō doesn't already do.

**Biggest risk (unverified):** whether a government with `landless_playable` works outside
`mechanic_type = administrative`. If it doesn't, the fallback is to use the administrative
mechanic type for the landless state only. That brings influence and appointment machinery, which
may or may not suit the feature. See §9, test 1.

## 4. Lifecycle

### 4.1 Earning a seat

Possible ways to get one (first release could ship just the first two):

1. **Decision: "Establish a family seat."** House head currently holds a county or more. Costs
   prestige and gold, scaled by tier. One per house; cadet branches earn their own.
2. **Liege grant.** A liege can grant a seat to a loyal vassal house as a reward without land, the
   way rulers historically ennobled families. Costs the liege prestige. Gives a hook (gratitude,
   obligation).
3. **Later: ennobled commoner.** A knight, commander or councillor with high prestige is granted a
   seat with no land at all: the bottom rung. This is the one that creates new playable
   characters, so it needs the tightest limit.
4. **Start-date seeding** by the generator (§7).

### 4.2 Holding a seat while landed

The seat sits beside the house head's land like an administrative family title does. The `estate`
is the family's home: a home base, family buildings, and perhaps a small income or prestige bonus.
Styling stays with the land (`ruler_uses_title_name = no`, as the generator already writes).

### 4.3 Losing the last county

- **Hoped-for behaviour (unverified):** the holder keeps the seat and is moved to the gentry
  government by an `on_title_lost`-style hook, so they stay playable with the estate intact.
- **Alternatives if the engine does otherwise:** a demotion to courtier we have to catch and
  reverse, or a forced switch to adventurer. Test 2 in §9 decides which applies.

### 4.4 Life as landless gentry

What a gentry head can do:
- Keep the house and estate going, arrange marriages, use schemes, take court positions or
  council seats, serve as a knight or commander.
- Collect a small estate income, enough to survive but not enough to fund a war.
- Keep (and possibly gain) claims, but not press them alone (§5).

### 4.5 Climbing back

See §5. Gaining any county returns the head to a landed government; the seat stays with them.

### 4.6 Decline and extinction

- Line ends → seat destroyed (`destroy_if_invalid_heir`, already written).
- **Landless-generations counter:** after N successive landless heads (say 3), the seat lapses
  and the family becomes ordinary courtiers. An event warns one generation ahead.
- Optional upkeep: a gentry head below a prestige threshold for some years loses the seat.

## 5. Ways to climb

Without at least one of these, families survive but can't rise. Ranked by how well each would
probably work:

| Path | Notes | Status |
|---|---|---|
| **Service to a liege** | Commanders and councillors get granted counties; vanilla interactions already exist. AI weighting may need a nudge toward gentry heads. | vanilla interactions; weighting unverified |
| **Marriage and inheritance** | Matrilineal or heir marriages into landed houses. Needs no new script. | vanilla |
| **Buying a lordship** | New interaction: a gentry head offers gold for a county the liege holds directly. Historically sound (lordships were bought and sold). | design |
| **Sponsored claim** | A gentry head with a claim persuades an ally or liege to press it for them, possibly through vanilla's existing claim-pressing behaviour. | unverified |
| **Taking up arms** | Decision to become an adventurer temporarily, keeping the seat if the engine allows, and conquer. Only if gentry and adventurer can coexist. | unverified |

Possible overlap with Societies: §3A of the Societies notes wants a landless Restorationist
claimant who can still act. A pretender who heads a gentry family would get a playable,
estate-based exile almost for free.

## 6. Limits and AI

- Seats are earned, so the count is bounded by the earning costs and the landless-generations
  limit, not by the number of houses.
- Possible hard limit: no more than X% of a realm's houses hold a seat, checked by the decision
  and the grant.
- AI: modest weights for establishing a seat (rich, prestigious, at risk of losing land), and for
  climbing (seek service, marriages into land, buy lordships when rich).
- Watch character count in long games; landless families are what drive it up.

## 7. Generator integration

- **Setting:** `GentryFamilies: off | earned | earned + seeded`, default off, alongside World State.
- **Start-date seeding:** houses the history sim saw fall (`SimMemory` "seized", "collapsed",
  "divided") start as gentry heads with a seat named for their old capital. This connects the
  chronicle to living families at game start.
- **Seat naming:** "of [old capital]" in the house's culture; the title's `capital` already points
  at the holder's seat, which reads correctly as "from somewhere".
- **Interactions to check:** `RebuildNobleFamilies` already owns family titles for administrative
  realms. Gentry seats should be a separate list, so a government edit in the editor doesn't remove
  a gentry seat, or the other way round.
- **Coats of arms:** reuse the house arms. `noble_family_title_realm_setup_effect` already sets the
  CoA at game start (`CoatOfArmsWriter.cs` comment).

## 8. DLC fallback

`landless_playable` needs Roads to Power (vanilla). Without it: still write the seats, use plain
feudal, and gate the decision behind the DLC trigger. Landless gentry then can't happen, and seats
exist only as an honorific. Alternatively, don't write the feature at all. Decide before building.

## 9. Open questions and the tests that answer them

Ordered so each test decides whether the next one is worth running.

1. **Does `landless_playable` work on a non-administrative government?** Re-declare a test
   government with it plus `noble_families` and `domicile_type = estate`. Give a character a seat,
   strip their land in the console, and check whether they stay playable. Also read Sōryō's
   definition in the installed game to see whether it already carries `landless_playable`.
2. **What happens when a feudal seat holder loses their last county?** Do they stay on the seat,
   get demoted to courtier, or become an adventurer? Does the estate survive?
3. **Can a seat holder be granted a county back** through vanilla interactions, and does the
   government switch cleanly to the landed one?
4. **Can an adventurer hold a noble family title?** Only matters for the "taking up arms" path.
5. **Does granting a seat to a courtier** (§4.1.3) make them playable?
6. Save/reload at every state; behaviour with societies, wilderness colonies and administrative
   realms in the same world.

## 10. Not yet decided

- Earning costs, the landless-generations limit N, and the per-realm cap.
- Whether the estate gets its own building set or reuses vanilla's estate buildings (they're tied
  to domicile type by key, as the colony notes found for camps).
- Whether "taking up arms" belongs in a first release.
- The name: *gentry*, *family seat*, *manor*, or something taken from the generated language.
