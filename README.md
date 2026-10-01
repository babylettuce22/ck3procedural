# CK3 Procedural Tool

Generate and edit Crusader Kings III worlds from a heightmap. The tool builds provinces,
rivers, climate, terrain, titles, cultures, faiths, rulers and history, then exports a
playable total-conversion mod. It has a Windows desktop interface and a command-line interface.

<img width="1790" height="935" alt="CK3 Procedural Tool desktop interface and map preview" src="https://github.com/user-attachments/assets/bece6c17-74fb-4f13-a539-60a07c044c55" />

## Features

- **Terrain from any source.** Import a heightmap PNG, sculpt one in the embedded
  [CK3 Heightmap Forge](https://github.com/babylettuce22/ck3-heightmap-forge), or bring an
  [Azgaar](https://azgaar.github.io) Full JSON export for names, borders, cultures and religions.
- **A full map pipeline.** Climate (with an optional painted Köppen layer), drainage and
  navigable rivers, provinces, terrain, map graphics, flatmap, trees, animals and locators.
- **Generated peoples.** Cultures, heritages and languages with related dialects, faiths with
  doctrines, holy sites and their own relief-rendered icons, a world calendar, and optional
  native rank titles. Or settle the map with CK3's own cultures and faiths instead.
- **History.** A realm-formation simulation produces the de jure hierarchy, realms, rulers,
  houses, dynasties and heraldry, starting wars, chronicles and up to three bookmarks. The
  History workspace keeps simulating the world past the start date.
- **Gameplay content.** Governments (including administrative and nomadic), men-at-arms,
  artifacts, wonders, regional struggles, routes and formation decisions. Optional
  wilderness, ruins and fantasy races.
- **Editing.** Inspect and edit titles, cultures, faiths and rulers before export, or open an
  existing generated mod and edit it in place without regenerating it.

See [docs/features.md](docs/features.md) for details on each system.

## Requirements

- Windows
- Crusader Kings III, version 1.20. Exporting a mod reads the installed game's data.

## Getting started

Download a release, extract the whole folder, and run `Ck3MapGen.exe`. Keep the bundled
`assets` and `BaseFilesToCopy` folders beside the executable. Releases can lag behind the
source tree.

The start page offers three ways in:

- **Quick (recommended):** a few guided choices about map, world and people, then a generated
  mod. This path tends to give the most complete and best-looking worlds.
- **Complex:** the full settings. Choose a heightmap, **Preview** (F5), adjust settings,
  then **Write mod** (Ctrl+S).
- **Azgaar:** import an Azgaar export with its matching heightmap.

Then enable the generated mod in a playset in the CK3 launcher. **Help ▸ Getting started…**
walks through a first world.

## Command line

Replace `dotnet run --` with `.\Ck3MapGen.exe` when using a release.

```powershell
# Open the desktop interface.
dotnet run -- --gui

# Preview a map and write debug images, without exporting a mod.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --seed 4242 --out "C:\Maps\preview"

# Export a mod into the launcher's mod directory.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --seed 4242 --mod "My World"

# Use Azgaar data, or a Heightmap Forge preset.
dotnet run -- --heightmap "C:\Maps\heightmap.png" --azgaar "C:\Maps\world.json" --mod "Azgaar World"
dotnet run -- --forge "C:\Maps\terrain.json" --seed 4242 --mod "Forge World"

# Generate with saved settings.
dotnet run -- --settings "C:\Maps\preset.json" --heightmap "C:\Maps\heightmap.png" --mod "My World"

# Open an existing generated mod for editing.
dotnet run -- --edit-world "C:\Maps\My World"
```

| Option | Purpose |
| --- | --- |
| `--heightmap <png>` / `--forge <json>` | Terrain source. |
| `--azgaar <json>` | Optional Azgaar Full JSON world data. |
| `--mod [name-or-directory]` | Export a mod. A bare name goes into the launcher's mod folder. |
| `--game <directory>` | CK3's `game` directory, if detection picks the wrong one. |
| `--seed <integer>` | World-generation seed. |
| `--settings <preset>` | Load a saved settings preset. |
| `--out <directory>` | Write debug images here. |
| `--content procedural\|vanilla` | Generated cultures and faiths, or CK3's own. |
| `--county-scale <number>` | Barony size relative to vanilla; larger means fewer provinces. |
| `--start-year <year>` | Start date. |
| `--additional-bookmarks` | Add two more start dates. |
| `--races off\|low\|high\|exotic` | Fantasy-race preset. |
| `--no-history` | Skip characters and history for faster iteration. |

The desktop settings cover far more than the CLI. See `Program.cs` for every flag and
`Config/MapConfig.cs` for settings and defaults.

### Map size

CK3 renders terrain reliably only at certain sizes: **4096×2048, 5120×2560, 6144×3072,
8192×4096, 9216×4608 and 18432×9216**. Use `--fit-heightmap` to resample a PNG to one of
these, or `--allow-unverified-size` to try another size anyway.

## Building from source

Requires the **.NET 10 SDK** on Windows. The project references the Heightmap Forge source,
expected as a sibling folder named `noisetool`:

```powershell
git clone https://github.com/babylettuce22/ck3procedural.git
git clone https://github.com/babylettuce22/ck3-heightmap-forge.git noisetool
cd ck3procedural
dotnet build Ck3MapGen.csproj
dotnet run -- --gui
```

To keep the Forge elsewhere, pass `-p:NoiseToolDir="C:\Source\noisetool\"` to the build.

### Project layout

| Directory | Contents |
| --- | --- |
| `AppGUI/` | Windows Forms interface: start page, Quick generator, map previews, inspectors and editors. |
| `Config/` | Settings, defaults and descriptions. |
| `Core/` | Pipeline coordination, loaded-world model, game discovery. |
| `MapGen/` | Generation by subject: terrain, climate, rivers, provinces, titles, peoples, languages, history, Azgaar and vanilla import, artifacts, situations, societies. |
| `World/` | Coarse simulation grid. |
| `Emit/` | Writers that turn the generated world into mod files. |
| `Io/` | Image and text formats, source-preserving file edits. |
| `GameGUI/` | Paradox GUI parsing and preview. |
| `BaseFilesToCopy/` | Hand-written mod file sets: Core, Procedural, Wilderness, Ruins, Fantasy, Societies, SocietyPrototype. |
| `assets/` | Source assets used by the generator. |
| `tools/` | World-editor checks and asset-preparation scripts. |

## Status

Under active development. Societies and magic are unfinished and hidden by default; ruins are
marked work in progress. A successful build doesn't prove a generated mod works: changes to
emitted content should be checked with [ck3-tiger](https://github.com/amtep/ck3-tiger) and in
game. See [Validation and current limits](docs/features.md#validation-and-current-limits).

## License and credits

The source is released under the [MIT License](LICENSE).

Most bundled third-party assets are CC0. Three are **CC-BY-4.0**, which requires crediting the
author wherever the work is shared (commercial use is allowed). The full record of sources,
licences and modifications is in [`BaseFilesToCopy/Core/CREDITS.md`](BaseFilesToCopy/Core/CREDITS.md),
which is copied into every generated mod.

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
