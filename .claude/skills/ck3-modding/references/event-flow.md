# EventFlow: reading and checking an event chain without launching the game

`C:\Users\caelo\Desktop\ck3devtools\EventFlow\`, whose README has the full model. It prints event
chains as plain text and checks them. Run it for anything that adds or changes events, story
cycles, decisions or interactions: before writing (to see the chain you're joining) and after (to
catch what you broke).

```
EventFlow.exe chain <name|namespace|file-fragment> [--mod DIR]   # outline + findings for that set
EventFlow.exe why <event or definition>                          # every route from an entry point
EventFlow.exe check [--only <check>]                             # all findings, the generator's files only
EventFlow.exe var <name>                                         # every set/read of a variable or flag
```

The exe is `bin\Release\net10.0\EventFlow.exe`. Build it with `dotnet build -c Release`. `--mod`
defaults to the newest generated mod. **Don't read a verify-runs world while another session
regenerates it**; copy its script folders first (the README says which).

## What it knows that tiger and ScriptIndex don't

It follows flow **across definition kinds**:
- events fired from scripted effects, which are inlined into their callers;
- on_action lists, including merged vanilla ones; the chain view shows only the mod's own blocks;
- `create_story` and story `effect_group` cadence (`[every 4–7 years]`);
- decisions, interactions and scripted GUIs as entry points;
- events handed to vanilla code to fire later, like `OUTPUT_EVENT = my.0100` into single combat or
  schemes, shown as `[passed as OUTPUT_EVENT]`.

## Reading a chain outline

```
EVENT gen_blood_challenge.0011 "The High Seat"  [events/…:137]
  in: gen_blood_challenge.0010 (immediate)
  scopes: handed {actor, gen_bc_challenger, …}  saves {…}  uses {gen_bc_challenger, gen_bc_title!}
  option 3 "Watch how [gen_bc_scene_opponent…" [has trigger]: shows gen_bc_apply_cunning_stance_effect(), stress_impact  calls …
```

- **handed** is what *every* caller guarantees. A use marked **`!`** is not guaranteed on some
  route, and `check` names the caller.
- **shows** is what the option's tooltip can show the player. **SILENT** means nothing at all.
  An option that only fires the next event is allowed; `check` flags only silent options in
  events that offer a real choice.

## Checks, and what to do about each

| Check | Usually means | Fix |
|---|---|---|
| `scope-not-passed` | loc or script names a scope a caller never saved: a blank name or a failed effect in game | `save_scope_as` it in that caller before `trigger_event`, or guard it with `exists`/`?=` |
| `silent-option` | a choice whose consequence the player can't see; the project rule is that options show their trade-offs | add the effect visibly, or `custom_tooltip` / `show_as_tooltip` |
| `odds-without-outcome` | a visible `random_list` where a branch does nothing visible | give every branch an outcome, or hide the roll and describe it |
| `read-never-set` / `set-never-read` | a dead branch or a dead write, often a typo in one of the two names | `var NAME` shows every site |
| `unreachable` | nothing fires it: a missing hookup, or a leftover | `why NAME`; wire it up or delete it |
| `missing-target` | fires an id nothing defines | fix the id |

A finding that is deliberate goes in `EventFlow\accepted.txt` with a reason. Findings cover only
the generator's own files, not patched vanilla copies.

## Limits

- Static reading: `[conditional]` means "under an if/random", not "might never run".
- The scope model is conservative:
  - engine on_actions, activities and schemes hand unknown scopes and don't restrict;
  - it assumes saved scopes carry over through `trigger_event` and temporary ones don't;
  - order inside an event is ignored (a scope saved in option 2 counts for the desc).
- It doesn't evaluate triggers, so it can't tell you an event never passes its `trigger`.
  error.log and an in-game run still settle that.
- Verified by `selftest.sh`, which plants one bug of each kind (all caught, no extras), and by
  calibration on the seed-4242 world (2026-09-30).
