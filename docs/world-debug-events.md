# World debug events

These manual character events ship in `BaseFilesToCopy/Core`. A newly generated
world lists them automatically in the debug panel's Events tab. They have no
on-action, pulse, or AI decision hookup. Every menu includes Cancel and a tooltip
describing each action.

With debug mode enabled, use `event gen_world_debug_events.0001` on the player,
substituting the desired ID below. Conversion uses the receiving character's
faith/culture. World-wide actions include the player unless explicitly AI-only.
Ordinary ruler actions exclude the wilderness owner and landless/special rulers.

Every choice is also a button under **World tools** on the debug panel's Tools
tab (the round "Gen" button in the debug map-mode column), which runs the
choice's effect directly with no menu in between.

## Observe mode

In global observe (Observe from the lobby, no player character) the menu events
cannot be used: an event sent to an AI is answered by the AI at once and never
shows a window. The "Gen" button still appears there and opens an observer copy
of the panel ("Generated World (observing)") with the World, Live and Tools tabs.
Its World tools work the same way, except conversion, which needs a player's faith
or culture and is left out. The observer window keeps its live counts on one
generated county (the panel's anchor county) and has no Events tab.

| ID suffix | Menu | Choices |
| --- | --- | --- |
| 0001 | Realm collapse | Kingdom, duchy, or county ceiling; independent counties |
| 0002 | Neighbor wars | One random AI attacker; all eligible AI; peaceful AI; weaker targets |
| 0003 | Global white peace | End every war, including landless participants' wars |
| 0004 | Release all vassals | Make ordinary landed count-tier and higher vassals independent |
| 0005 | De jure consolidation | Restore duchies or kingdoms to capital holders and attach lower-ranked counts |
| 0006 | Resources | 5,000 gold per ruler; refill holding levies/garrisons; both |
| 0007 | Conversion | Receiving character's faith, culture, or both |
| 0008 | Unrest | Zero/full control; five years of -50 popular opinion; remove debug unrest |
| 0009 | Succession | Kill one independent AI ruler, all independent AI, or all ordinary AI rulers |
| 0010 | Epidemics | Minor/major/apocalyptic smallpox; reduce all existing outbreaks to minor |

## Behavior and limits

- Collapse destroys held non-titular titles above the chosen tier, including
  higher ranks introduced by DLC. It preserves de jure structure, titular and
  special titles, and personal counties. Independent counties does not mean one
  county per ruler. Baron-tier vassals remain attached.
- Neighbor wars require debug mode and a legal `debug_war` target. Each declaration
  rechecks eligibility; rulers without a valid target are skipped. Target selection
  uses neighboring top-liege realms. Victory transfers the targeted primary title
  and its vassals. Several attackers can choose the same defender. After reshaping
  realms, advance several days before using this menu so neighbor caches update.
- Consolidation preserves personal holdings. Counts holding land across de jure
  borders and rulers of equal/higher rank can prevent exact borders. A title whose
  preferred capital has no eligible holder is skipped. It does not collapse higher
  titles first; use the collapse menu separately if desired.
- Reinforcements refill holding levies and garrisons; they do not refill men-at-arms
  or special troops.
- Succession snapshots the affected rulers before killing them; their successors
  are not added to the same wave. Player characters are excluded.
- Epidemic outbreak options are unavailable when epidemics are disabled by game
  rule. Reducing severity leaves infections in place: the installed engine exposes
  no script effect to delete an epidemic outright.
- Temporary lists exist only for the effect chain. No recurring scans or permanent
  global debug flags are created.

## In-game verification

Use a disposable save of a freshly generated world. Check each menu's Cancel
option first, then exercise its choices on separate reloads. For collapse, inspect
the ranks and independence of rulers while checking that de jure titles remain.
For wars, check declarations and victory outcomes, including rulers with no legal
neighbors. For succession, verify that heirs survive the wave. Check the resource,
conversion, control, popular-opinion, and epidemic changes in their respective
character/county views. Inspect the fresh CK3 `error.log` after exercising all menus.

Static validation does not replace these runtime checks.
