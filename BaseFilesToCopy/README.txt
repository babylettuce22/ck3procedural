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

Nothing in this set references a generated culture, faith or title key, and it must stay that way:
these files ship identically for every seed, so a reference to `culture:something_generated` would
be a dangling pointer on any map that did not happen to roll it.
