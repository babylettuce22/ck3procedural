# World-state events

Static world-state one-offs live in `BaseFilesToCopy/Core/events/gen_world_events.txt`.
Their supporting files share the `gen_world_events` prefix. Core copies these into
every generated world; no generator changes or generated IDs are required.

## A Season of Swords — gen_world_events.0001

Each January 1, an appended `yearly_global_pulse` on_action runs one global census.
An eligible realm is an independent landed ruler of county rank or above. A ruler
counts once if `is_at_war = yes`, including participation as an ally or in an
internal war. Wars fought only by their vassals do not count for that ruler.
Landless adventurers and independent barons are excluded from both counts.

At least 10 eligible realms and a war share of at least 50% dispatch the event to
all currently available adult landed players of county rank or above, including
vassals. This is one occurrence per world campaign, preserved across succession
by `gen_world_season_of_swords_seen`. The first actual delivery marks it seen;
an observer campaign or a year without eligible players does not consume it.
There is no lower-threshold fallback or random chance after passing the gates.

Buying supplies costs vanilla income-scaled `minor_gold_value` and gives +5%
levy reinforcement for two years. Hospitality costs `tiny_gold_value` and grants
`minor_prestige_gain`. Both paid options require enough gold. Continuing ordinary
business is always available and has an explicit tooltip describing the outcome.
The supplies option has alternate wording for a player already at war.

Tune the minimum realm count and percentage in
`common/script_values/gen_world_events_values.txt`.
Annual `GEN_WORLD_WAR` entries in debug.log record year, realm count, warring
realm count and percentage, even after the event has occurred. These measurements
are needed before making claims about how often a generated campaign reaches 50%.

## In-game checks

Use a freshly generated world in debug mode. In a landed adult player's console:

1. `effect gen_world_war_census_effect = yes` takes a current census; inspect
   debug.log for `GEN_WORLD_WAR`.
2. To test the event window without waiting for widespread wars, run
   `effect set_global_variable = { name = gen_world_realm_count value = 10 }`, then
   `effect set_global_variable = { name = gen_world_war_percent value = 50 }`, then
   `event gen_world_events.0001`. Use a disposable save: this marks the event seen.
3. Check all options and the supplies wording at peace and at war. Check that
   insufficient funds hide the paid options and that the free option stays usable.
4. For trigger boundaries, test the scripted trigger with a census of 10 realms
   and war percentages below/at 50, then a census of 9 realms at 100. Only the
   10-realms/50% case should pass. A census with zero realms must stay at 0%.
5. Test dispatch with `effect trigger_event = { on_action = gen_world_events_yearly }`:
   it recomputes the real census. It must not recur after delivery or succession.
   In multiplayer, each eligible player must receive the same first occurrence.

Static checks: mod-verify's `verify.sh --static`, then EventFlow `chain
gen_world_events` and `check` against the verified world. Runtime trigger timing,
multiplayer delivery and natural incidence still require the in-game checks above.
