This folder is a mod root. Every file under it is copied verbatim into the generated mod at the
same relative path, by Emit/StaticFileWriter.cs, as the last step of generation.

Two rules:
  - A file the pipeline generated always wins. The copy never overwrites, so putting a file here
    can add to the mod but cannot silently replace part of it.
  - This README is not copied.

What belongs here: vanilla files that replace_path deletes but nothing regenerates. Declaring
gfx/map/map_object_data drops all of vanilla's, including the map_table_* meshes, which are not
keyed to the map and only need to come back.
