# CK3 Procedural Tool

Generate and edit Crusader Kings III worlds from a heightmap. The tool builds provinces,
rivers, terrain, titles, cultures, faiths, rulers, and history, then exports a total-conversion
mod for CK3. It includes a Windows desktop interface and a command-line interface.

You can import a heightmap PNG or create terrain with the embedded **CK3 Heightmap Forge**.
An optional **Azgaar Full JSON export** supplies names, borders, cultures, religions, and other
world data alongside the heightmap.

<img width="1790" height="935" alt="CK3 Procedural Tool desktop interface and map preview" src="https://github.com/user-attachments/assets/bece6c17-74fb-4f13-a539-60a07c044c55" />

## Getting started

The application runs on **Windows** and targets **CK3 1.19**. Mod export needs an installed
copy of the game: the generator reads its culture, religion, military, and graphics data.
This README describes the current source tree; packaged releases may contain an earlier feature set.

For a packaged build, extract the complete release folder and run `Ck3MapGen.exe`. Keep the
bundled assets and `BaseFilesToCopy` folders beside the executable. The release workflow
builds a self-contained Windows x64 package.

1. Choose a PNG through **Heightmap…**, or create terrain in the **Heightmap** tab.
2. Optionally select a matching Azgaar export through **Azgaar…**.
3. Adjust the seed and settings, then use **Preview** to inspect the map.
4. Check the detected installation through **Game folder…**.
5. Use **Write mod** to export, then enable the generated mod in a CK3 launcher playset.

The preview offers physical, climate, de jure, and world map layers, with zoom, pan, and
inspectors for titles, cultures, faiths, and rulers. World content becomes available as it is
generated. Settings can be saved and loaded as presets.

### Heightmaps and map size

The heightmap supplies the map dimensions, coastline, and relief. Generation can rescale its
heights and carve navigable rivers, so the exported heightmap is not necessarily identical
to the input. A Forge preset supplies terrain in memory without a separate PNG export.

The code's known-rendering size list is **4096×2048, 5120×2560, 6144×3072, 8192×4096,
9216×4608, and 18432×9216**. Other dimensions can produce missing terrain in CK3.
Use `--fit-heightmap` to resample a PNG to a size in that list, or
`--allow-unverified-size` to explicitly try its original dimensions. These options are
mutually exclusive; fitting applies to PNG inputs, not Forge presets.

For images drawn on a different height scale, `--normalize-heightmap` or `--shift-heightmap`
can adapt sea level. Each accepts an optional source sea level on a 0–255 scale. The GUI
exposes the corresponding settings. An Azgaar JSON file must align with the heightmap;
it does not replace it.

### Painting the climate

The **Climate** tab paints the kind of environment you want over the heightmap, in Köppen-map
colours: choose a climate from the palette (rainforest, monsoon, savanna, hot desert, steppe,
Mediterranean, oceanic, humid subtropical, continental, subarctic, tundra, with cold desert,
cold steppe and ice cap under *More climates*) and brush it over a region. Each brush is a
climate profile — a sea-level temperature, a seasonal swing, a yearly rainfall and its summer
share — rather than a finished biome, so the generator blends the paint into its own climate by
stroke weight, applies the lapse rate from the real relief, and only then classifies. A rainforest
brush over a range gives tropical lowland and cooler highland; overlapping soft strokes give a
transition rather than a border. Three views show what you asked for (*Paint*), what the model
now predicts (*Climate*), and an approximate landscape (*Landscape*); the prediction updates in
the background after each stroke.

An unvisited tab changes nothing, unpainted ground stays automatic, and **Use automatic climate**
bypasses the paint without deleting it. With an Azgaar export, its climate and biomes remain the
starting point and painted areas take precedence locally. The paint is saved beside a preset as
`<preset>.climate.png`, restored between sessions, and can be exported for `--climate-paint`.

### Finding the game and output folder

`Core/GameLocator.cs` searches Steam libraries and common installation locations. It also
resolves the user's Documents folder for the CK3 launcher mod directory, including redirected
Documents folders. Use **Game folder…** or CLI `--game` if automatic detection finds the
wrong installation.

Export produces a mod directory and a sibling `.mod` launcher file. On the CLI, a bare
`--mod` name is placed in the detected launcher mod directory; a path selects a specific
output directory. `--mod` without a value uses the default `proceduralmap` folder.

## Command-line use

These examples run from a source checkout. With a packaged build, replace `dotnet run --`
with `.\Ck3MapGen.exe`.

```powershell
# Open the desktop interface.
dotnet run -- --gui

# Derive a map and write debug images without exporting a mod.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --seed 4242 --out "C:\Maps\preview"

# Export a mod into the launcher's mod directory.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --seed 4242 --mod "My World"

# Add Azgaar world data to the matching heightmap.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --azgaar "C:\Maps\world.json" --mod "Azgaar World"

# Generate terrain directly from a Heightmap Forge preset.
dotnet run -- --forge "C:\Maps\terrain.json" --seed 4242 --mod "Forge World"

# Open an existing generated mod for editing.
dotnet run -- --edit-world "C:\Maps\My World"
```

Frequently used options:

| Option | Purpose |
| --- | --- |
| `--heightmap <png>` / `--forge <json>` | Choose one terrain source. |
| `--azgaar <json>` | Import optional Azgaar Full JSON world data. |
| `--climate-paint <png>` | Apply climate paint exported from the Climate tab (or saved beside a preset). |
| `--mod [name-or-directory]` | Export a mod; omitting this flag leaves the run as a preview/debug export. |
| `--game <directory>` | Select CK3's `game` directory. |
| `--seed <integer>` | Set the world-generation seed. |
| `--out <directory>` | Choose a debug-image directory and enable debug images. |
| `--debug-images` / `--no-debug-images` | Explicitly enable or disable debug PNGs. By default they are enabled only when no mod is written. |
| `--county-scale <number>` | Scale barony size relative to vanilla; larger values produce fewer provinces. |
| `--province-downscale <integer>` | Set the province-grid downscale factor. |
| `--start-year <year>` / `--era-anchor <year>` | Set the start year and era calibration. |
| `--gender historical\|mixed\|femaledominated` | Choose the world's gender-law profile. |
| `--races off\|low\|high\|exotic` | Choose a fantasy-race preset. |
| `--impassable-mask <png>` | Supply a painted impassable mask; `--impassable-mask-mode snap\|touch` controls how it applies. |
| `--starting-hegemony` | Enable a starting hegemony. |
| `--no-dynastic-cycle` / `--no-formation` | Disable the dynastic cycle or pre-start formation simulation. |
| `--no-history` | Skip character history and related output, including bookmarks, artifacts, and chronicles. Useful for iteration. |
| `--no-packed` | Skip packed heightmap output for diagnostic runs. |

The desktop settings cover more configuration than the CLI. See `Program.cs` for the
complete argument handling and `Config/MapConfig.cs` for defaults and setting descriptions.
Diagnostic flags such as `--no-history` and `--no-packed` omit parts of a normal full export.

## Editing worlds

There are two editing paths:

- **A world generated in the current session:** inspectors and edit overlays update the
  generated world; the overwrite path re-emits affected content.
- **An existing generated mod:** use **Open generated world… (WIP)** or
  `--edit-world <directory>` to open its exported files. Edits
  are applied to source ranges, preserving comments, whitespace, UTF-8 BOMs, and unknown
  fields rather than regenerating the world.

The existing-world editor exposes supported fields for titles, provinces, cultures, faiths,
characters, dynasties, houses, holy sites, and coats of arms. Matching bookmark display names
are updated when a character is renamed. **Save edits** writes changed files and backs up
the originals under `%LOCALAPPDATA%\Ck3MapGen\WorldBackups`. It refuses to save if a loaded
file has changed externally; reopen the world to load those changes.

This is an editor for the generator's output layout, not an arbitrary CK3 mod or save-game
editor. Opening a world requires `descriptor.mod`, `map_data/provinces.png`, and
`common/landed_titles/00_landed_titles.txt`. Only map layers recoverable from the exported
mod are available; unsaved simulation layers are not reconstructed. Editing does not
regenerate historical prose or portrait DNA.

## What is generated

Output depends on the settings, available game data, and generated world. The main groups are:

| Area | Content |
| --- | --- |
| Map data | Heightmap, packed/indirection atlases, province and river rasters, province definitions, adjacencies, terrain, and seasons. |
| Map graphics | Terrain textures and masks, water and snow textures, flatmap, holding locators, trees, animals, bridges, and map objects. |
| Titles and settlements | De jure hierarchy, realm borders, capitals, holdings, development, and title names and colours. |
| Peoples and religions | Cultures, heritages, languages, name lists, faiths, doctrines, holy sites, and ethnicities. |
| Characters and history | Rulers, houses, dynasties, ancestors, formation history, starting wars, bookmarks, portraits, and chronicles. |
| Military and artifacts | Generated men-at-arms and associated innovations, regalia, and composed weapon and armour assets. |
| Regional content | Centers of the World and wonders, regional struggles, routes, Silk Road and steppe content, and formation decisions. |
| Governments | Government assignment, administrative and nomadic content, hegemony support, and dynastic-cycle integration. |
| Optional systems | Wilderness and colonisation, county ruins, fantasy racial traits and morphology, and the society prototype. |
| Integration | Localization, GUI changes, defines, and compatibility declarations and patches for the replacement world. |

Many systems combine generated files with hand-maintained file sets in `BaseFilesToCopy/`.
Wilderness, ruins, and fantasy content are gated by their settings; ruins also require
wilderness. Fantasy ethnicities are off by default. Societies are a hand-written prototype,
off by default and hidden in the normal settings; `--societies` enables it. The presence of
a magic setting does not represent a completed procedural magic system.

### Azgaar imports

The importer uses the export's names and name bases, province and state domains, political
hierarchy, government forms, religions and their ancestry, cultures, climate, and biomes.
Imported climate is combined with the local model's seasonal and relief detail. Terrain
relief still comes from the heightmap. Race tags on imported cultures can inform fantasy
ethnicities when that feature is enabled.

The generator fills in content the export does not supply, including CK3 characters,
dynasties, and prehistory. Importing is a translation into CK3's hierarchy and systems;
settings that conflict with imported data are identified in the GUI.

### Languages and names

Generated names use a phonology, lexicon, and language flavour. Heritages have related
language families and cultures have dialects, so related peoples can share recognizable
name elements. Faith names can use a liturgical register. Azgaar imports also use the
export's names and Markov name bases.

To sample a language without generating a map:

```powershell
dotnet run -- --languages Norse 4242 family
```

Omit the flavour name to sample all flavours. The implementation lives in
`MapGen/Language.cs`, `Phonology.cs`, `Lexicon.cs`, and `LanguageFlavour.cs`.

## Building from source

Use Windows with the **.NET 10 SDK**. The project references the Heightmap Forge source
projects and expects this sibling layout by default:

```text
parent/
  ck3procedural/      # this repository
  noisetool/         # babylettuce22/ck3-heightmap-forge
    NoiseTool.Core/
    NoiseTool.Ui/
```

From the parent directory:

```powershell
git clone https://github.com/babylettuce22/ck3procedural.git
git clone https://github.com/babylettuce22/ck3-heightmap-forge.git noisetool
cd ck3procedural
dotnet build Ck3MapGen.csproj
dotnet run -- --gui
```

For another Forge location, pass `-p:NoiseToolDir="C:\Source\noisetool\"` to the build.
The main project targets `net10.0-windows`, uses Windows Forms, and references ImageSharp
4.0.0. Its project file also configures a local `sixlabors.lic` path. CK3-specific image
and text writers live in `Io/` and `Emit/`.

## How generation works

`Core/Generator.Generate` derives the map: terrain input, optional Azgaar binding, climate,
drainage, major-river carving, province partitioning, terrain classification, and title
hierarchy. Preview layers are published as these stages finish.

`Core/Generator.WriteMod` exports the map and calls `Emit/ContentWriter.cs` to build and
write the world content. Some raster work runs alongside content and history generation.
The installed game's vocabulary and data influence cultures, faiths, regiments, graphics,
and compatibility output.

A seed is useful for repeatable comparisons, but **a seed and heightmap alone are not a
complete reproducibility record**. Keep the configuration, optional Azgaar/Forge input,
generator and Forge revisions, installed game data, and required assets as well. Exported
watermarks include a generation timestamp, so whole-folder byte identity is not promised.

### Repository layout

| Directory | Responsibility |
| --- | --- |
| `AppGUI/` | Windows Forms interface, map previews, inspectors, and editing. |
| `Config/` | Settings, defaults, descriptions, and property-grid behavior. |
| `Core/` | Pipeline coordination, loaded-world model, RNG, timing, and game discovery. |
| `MapGen/` | Geography, society and history generation, imports, names, and asset composition. |
| `World/` | Coarse simulation grid. |
| `Emit/` | Mod writers and compatibility patches. |
| `Io/` | Image/text formats and source-preserving file edits. |
| `GameGUI/` | Paradox GUI parsing and preview support. |
| `BaseFilesToCopy/` | Bundled mod file sets: Core, Wilderness, Ruins, Fantasy, and Societies. |
| `assets/` | Source assets used by the generator. |
| `tools/` | World-editor checks and asset preparation scripts. |
| `.github/workflows/` | Release build and packaging workflow. |

### Validation and current limits

The repository includes focused diagnostic checks:

```powershell
dotnet run -- --verify-world-editor
dotnet run -- --verify-compose
```

The world-editor check exercises edits in a disposable fixture. An optional mod path also
checks that opening and saving an unchanged existing world preserves bytes and timestamps.
The composition check compares weapon attachment and merged geometry.

These checks do not establish that a generated mod works in CK3. Changes to emitted content
also need an export, validation with a matching **ck3-tiger**, and in-game inspection. Map
rendering, locators, portraits, and scripted gameplay require runtime checks; a successful
C# build cannot validate them. Generation time and memory use depend on map size and enabled
features.

## Credits & Attributions

Most third-party assets bundled with this tool are CC0 and oblige nothing. Four are **CC-BY-4.0**,
which does: the author must be credited wherever the work is shared. Commercial use is allowed for
all four. The full record — sources, licences, what each was used for and how it was modified —
is in [`BaseFilesToCopy/Core/CREDITS.md`](BaseFilesToCopy/Core/CREDITS.md), which is copied into
every mod this tool generates so the credit travels with the work rather than staying in the repo.

> This work is based on "5 Piece Platemail - MetaHuman (rigged)"
> (https://sketchfab.com/3d-models/5-piece-platemail-metahuman-rigged-d5a36fd5d69640b29bb7e92f72f61608)
> by DevonLux (https://sketchfab.com/DevonLux) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

> This work is based on "Executioner Sword"
> (https://sketchfab.com/3d-models/executioner-sword-de8e451fa6014d7e9ea3d5386f4893a0)
> by Leon Steiner (https://sketchfab.com/Leon.Steiner) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

> This work is based on "Shoulder Armor"
> (https://sketchfab.com/3d-models/shoulder-armor-053d84b1034c429ab476778022d64ff5)
> by ilyaballz (https://sketchfab.com/ilyaballz) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

> This work is based on "Medieval Shoulder Pad"
> (https://sketchfab.com/3d-models/medieval-shoulder-pad-5376ef05f3d3448889517d9bd0ff8421)
> by ViniciusMello (https://sketchfab.com/ViniciusMello) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)
