This folder holds hand-kept files copied verbatim into the generated mod by Emit/StaticFileWriter.cs,
as the last step of generation.

It is NOT itself a mod root. Each immediate subfolder is one *file set*, and each set is a mod root:
a file's path below the set folder is its path in the mod.

    BaseFilesToCopy/
        Core/                                       always copied
            gfx/map/map_object_data/map_table_*.txt
        Wilderness/                                 copied only when MapConfig.EnableWilderness
            common/...
            events/...
            gfx/interface/icons/holding_types_tab/
            localization/english/...
        Fantasy/                                    copied only when MapConfig.EnableFantasyEthnicities
            common/...                              is on AND RaceMode is not HumanOnly
            events/...
            gfx/interface/icons/traits/
            localization/english/...

Two rules:
  - A file the pipeline generated always wins. The copy never overwrites, so putting a file here
    can add to the mod but cannot silently replace part of it.
  - This README is not copied.

--- Core ---

Vanilla files that replace_path deletes but nothing regenerates. Declaring gfx/map/map_object_data
drops all of vanilla's, including the map_table_* meshes, which are not keyed to the map and only
need to come back. (MapTableWriter reads these same files, rescales them and writes the result
*before* the copy runs, so its versions win and these stay as the fallback for anything it could
not parse.)

--- Wilderness ---

The static half of the wilderness and colonisation system: the government, holdings, buildings,
modifiers and the script that moves a county between them. Hand-written because none of it varies
per map — the generator's job is to decide *which* counties start wild, not to reinvent what
wilderness means.

What the generator emits alongside it, and which these files therefore assume exists:
  - a dummy holder character carrying the `wilderness` trait, in history/characters
  - county title history handing every wild county to that character with
    `government = wilderness_government`
  - province history setting `holding = wilderness_holding` on each wild county's capital barony

The four .dds under gfx/interface/{icons,illustrations} for the overseeing activity are likewise
placeholders, and copies of vanilla's hunt art. CK3 derives an activity's icon paths from its key
and has no field to point them elsewhere, so an activity that ships none of them renders as four
checkerboards in the planner. They want replacing, but the mechanic does not wait on the art.

The two .dds under gfx/ are placeholders: the county window's holding strip draws
`gfx/interface/icons/holding_types_tab/<holding key>.dds` by convention, with no way to point a
custom holding somewhere else, so a type that ships no file there renders as the missing-texture
checkerboard. Both are copies of vanilla's tribal_holding.dds and want replacing with real art.

Nothing in this set references a generated culture, faith or title key, and it must stay that way:
these files ship identically for every seed, so a reference to `culture:something_generated` would
be a dangling pointer on any map that did not happen to roll it.

--- Fantasy ---

The static half of the fantasy race system: the seven phenotype race traits (the six fantasy races
plus the visible Human trait), the scripted triggers/effects/on_actions that assign them at birth
and through the culture pulse, the long-lived races' world_weary fading (traits, events, death
reason, script values), the race trait icons, and the forced dwarf beards portrait modifier.

Gated so a realistic map ships none of it: no race chips in the ruler designer, no fading events,
no phenotype pulses. Two fantasy-adjacent things deliberately stay in Core because they are written
into every mod regardless of mode:
  - common/genes/gen_race_skin.txt — Emit/PortraitWriter.cs writes this gene into every persistent
    DNA record on every map, so the declaration must always exist (it is inert without the traits).
  - the gen_race_skin loc line in localization/english/gen_req_localization_l_english.yml.

What the generator emits alongside it, and which these files therefore assume exists:
  - phenotype traits stamped onto history characters by Emit/HistoryWriter.cs (humans get
    phenotype_human; the culture pulse spreads traits to engine-generated characters from there)
  - gfx/portraits/portrait_modifiers/99_gen_race_morphs.txt from Emit/RaceMorphWriter.cs, which
    forces each race's look by trait — and resets inherited skin shifts on mixed-line humans via
    the gen_phenotype_human character FLAG (narrow marker; not the same thing as the Human trait)
  - the marriage-reluctance patch from Emit/InteractionWriter.cs, which calls
    gen_is_different_race_than from this set's scripted triggers

The same no-generated-keys rule as Wilderness applies: this set ships identically for every seed.
