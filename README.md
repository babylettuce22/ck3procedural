# CK3 Procedural Tool

[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20development-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/babylettuce22)

**A world generator for Crusader Kings III.** Start from nothing, or from your own heightmap
or Azgaar map, and end with a complete, playable total-conversion mod: a new world with its own
coasts, climate and rivers, peoples speaking related languages, faiths with their own symbols,
and a simulated history that decides who holds what when the game begins.

Make a few choices in the Quick generator and watch the world take shape, or open every setting
and tune it by hand. Then inspect and edit any title, culture, faith or ruler before you play.

<img width="996" height="696" alt="image" src="https://github.com/user-attachments/assets/e0b6d5f4-c9fd-4bad-bf9a-c12aac38018f" />
<img width="1765" height="1035" alt="image" src="https://github.com/user-attachments/assets/9c49d08c-1ee9-4dd9-a0e4-73e1bf850e0c" />
<img width="1012" height="781" alt="Recording 2026-10-02 at 20 11 06(1)" src="https://github.com/user-attachments/assets/d5eb20e0-517b-4e9a-9808-145cab927fc3" />

## Features

- **Terrain from any source.** Let the Quick generator build it, sculpt your own in the embedded
  [CK3 Heightmap Forge](https://github.com/babylettuce22/ck3-heightmap-forge), import a
  heightmap PNG, or bring an
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

The 3D previews use a Direct3D 11 compute renderer when compatible hardware is available,
and automatically fall back to the CPU renderer if initialization or rendering fails. Both
paths keep the same camera controls, terrain shading and presentation.

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

### Map size

CK3 renders terrain reliably only at certain sizes: **4096×2048, 5120×2560, 6144×3072,
8192×4096, 9216×4608 and 18432×9216**. Heightmaps at other sizes may show missing terrain
in game.

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

To compare hardware rendering with the CPU reference and exercise fallback handling, run
`dotnet run -c Release -- --verify-gpu-renderer`. An optional generated mod directory adds
comparisons using its written CK3 ground. The check saves comparison PNGs beside the build
and reports frame timings. Set the environment variable `CK3MAPGEN_RENDERER=cpu` to force
the software path when troubleshooting the app.

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

## Support

The tool and the mods it makes are free. If you enjoy it, you can support development on
[Ko-fi](https://ko-fi.com/babylettuce22).

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
