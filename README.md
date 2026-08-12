<img width="1790" height="935" alt="genguipic" src="https://github.com/user-attachments/assets/bece6c17-74fb-4f13-a539-60a07c044c55" />

# CK3 Procedural Map

A generator that turns a single heightmap into a complete, playable Crusader Kings III total
conversion — the map rasters, the de jure title hierarchy, the cultures and faiths that name it, the
terrain it is painted with, and the rulers who hold it on the start date.

It does not generate terrain. The heightmap is an input, drawn wherever you like — by hand, by
Azgaar, by another tool — and it is authoritative: it decides the map's size, its coastline and its
relief, and everything the mod ships is derived from it rather than negotiated with it. What this
program does is *interpret* that image, and then write the several hundred files CK3 needs in order
to believe in it.

---

## What it produces

A mod folder, plus the sibling `.mod` file the launcher needs to see it. Inside:

| Area | What is written |
| --- | --- |
| `map_data/` | `heightmap.png` and the packed/indirection atlas pair, `provinces.png`, `rivers.png`, `definition.csv`, `default.map`, `adjacencies.csv`, `continent.txt`, `island_region.txt`, `seasons.txt` |
| `common/landed_titles/` | The full empire-to-barony de jure tree, named and coloured |
| `common/culture/`, `common/religion/` | Generated cultures, heritages, name lists and languages; generated religions, faiths and holy sites |
| `common/province_terrain/`, `history/provinces/` | Per-province terrain and holdings |
| `history/titles/`, `history/characters/`, `common/dynasties/` | Who holds what at the start date, and the dynasties they belong to |
| `common/bookmarks/`, `common/bookmark_portraits/` | A bookmark on the generated start date, with portraits |
| `gfx/map/terrain/` | Detail index and intensity textures, per-material masks (including `masks_gen`), colormap, flatmap |
| `gfx/map/water/`, `gfx/map/textures/` | Foam, water colour and snow masks repainted for the new coastline |
| `gfx/map/map_object_data/` | Locators for every holding, army and siege, plus foliage and the map table |
| `common/defines/`, `map_data/geographical_regions/` | The engine's world size, and re-declarations that keep vanilla and DLC script from erroring |
| `localization/english/` | Names for everything generated |

Formats were verified byte-for-byte against vanilla 1.19 rather than taken from documentation,
because CK3 fails opaquely on nearly all of them: a wrong pixel format, an out-of-range locator or a
missing pathfinding graph produces no log line at all, just a load that stops with a core spinning.

## The pipeline

Generation splits in two, because reading a heightmap and writing a mod are separately useful — the
GUI re-derives constantly while a setting is being tuned, and writing is by far the slower half.

**Derive** (`Core/Generator.Generate`)

1. **Heightmap** — decoded, and optionally rescaled onto CK3's height scale for maps drawn on
   somebody else's (an Azgaar export puts sea level at the equivalent of 51/255 against CK3's 19,
   and reads as almost entirely land without this).
2. **Climate** — not latitude bands. Moisture is advected along the surface winds of a three-cell
   circulation, and the temperature and rainfall that come out are classified by Köppen. Its
   parameters are in real units — degrees Celsius, millimetres, degrees of latitude — so they can be
   checked against a real climate atlas instead of tuned blind.
3. **Drainage** — a depression-filled surface with a downslope receiver for every land cell and
   discharge accumulated along it, weighted by rainfall so the great rivers avoid the deserts.
4. **Provinces** — a terrain-weighted, Lloyd-relaxed partition. Province *size* follows habitability
   (coasts, flat ground, river valleys, kind latitudes) rather than noise, because that is what makes
   a map read as settled rather than as patterned.
5. **Terrain classification** — climate × landform, which is also the matrix CK3's own `gen_*`
   material family is organised along.
6. **Titles** — baronies clustered up into counties, duchies, kingdoms and empires, allowed to reach
   across straits at the top two tiers only.

**Write** (`Core/Generator.WriteMod`)

Development, then cultures, then title names, then governments, then faiths — an order that is
forced rather than chosen, since a title is named in the language of whoever lives there, and whether
a faith starts unreformed depends on how tribal its counties are. Then the rasters, the textures,
the locators, the history and the compatibility overrides.

Every random decision comes from a seed, so a seed and a heightmap fully determine a world.

## Two front ends

Both drive the same pipeline, so there is one definition of it rather than a console one and a window
one that drift apart.

The **GUI** (`Ck3MapGen.exe`, or `--gui`) is one window: a `PropertyGrid` over every setting, a
zoomable preview with twelve views — height, heightmap, terrain, climate, drainage, rivers,
provinces, counties, duchies, kingdoms, empires, government — and a log. Zoom and pan survive a
rebuild and a view switch, because tuning is a loop of nudge a setting, rebuild, look at the same
place. Progress and time remaining are learned from the previous run rather than hardcoded.

The **CLI** takes `--heightmap`, `--mod`, `--seed`, `--out`, `--scale`, `--county-scale`,
`--normalize-heightmap`, `--land-top`, `--land-top-percentile`, `--no-history` and `--no-packed`. It
also dumps debug PNGs — elevation, terrain, provinces, terrain classes, Köppen climate, drainage,
rivers, rainfall and temperature — which are how a change gets eyeballed.

## Rules the code holds itself to

- **Never invent an identifier that has to already exist.** A generated culture may invent its name,
  its language and its words, because those are emitted too. It may not invent an ethos, a tradition,
  a doctrine or a clothing set — those come from the base game and from DLC the player may not own.
  So the installed game is read and its actual vocabulary recombined.
- **Do not blank vanilla data — re-declare it.** A missing key is a hard script error, not a warning,
  and base-game and DLC script hardcodes region and title keys everywhere. Cultures and faiths are
  additive for the same reason; vanilla's stay declared and simply go unheld.
- **`replace_path` is a scalpel, not a broom.** Dropping `map_data` would take vanilla's 44 MB
  pathfinding graph with it. Dropping `gfx/map/map_object_data` is necessary, and everything under it
  then has to be rebuilt or hand-kept.
- **The BOM is not cosmetic.** Core `map_data` files carry none; script files under `common/`,
  `history/` and `gfx/` need UTF-8 with one.
- **Settings are calibrated against vanilla, and say so.** Barony density, impassable share, the
  867 development curve, the ratio of cultures to heritages — the defaults aim at measured vanilla
  numbers, and the reasoning is written down beside each one.

## Layout

```
Config/   MapConfig — every knob, with the vanilla figure it was calibrated against
Core/     the pipeline, the seeded RNG, noise, phase timing
MapGen/   climate, drainage, provinces, terrain, habitability, titles, cultures, faiths,
          governments, development, language
World/    the coarse simulation grid the climate model runs on
Emit/     one writer per part of the mod
Io/       PNG, DDS and Paradox-text encoders written by hand, for exact pixel formats
Gui/      the WinForms front end
VanillaFilesToCopy/  vanilla files that replace_path deletes and nothing regenerates
docs/     notes on procedural mechanics beyond the map
```

Built on .NET 9 (Windows, WinForms), with ImageSharp as the only dependency — used for *reading*
images; everything written is encoded by hand, because CK3 demands an exact pixel format per file and
a general imaging library will not guarantee one. Targets CK3 **1.19.0.6**, and reads the installed
game for its culture and religion vocabulary.

## Where it is unfinished

- **Rivers.** The drainage network exists and every land cell provably drains to an outlet, but
  nothing selects courses from it yet: `rivers.png` currently ships land and water only, and
  `default.map` has no major rivers. The previous hydrology was removed rather than patched — its
  courses wandered without reaching the sea and came out different at every map resolution.
- **Mechanics.** Everything generated so far is geography, people and titles. `docs/` sketches what
  else CK3's script layer would let a generator invent — men-at-arms, buildings, innovations, dynasty
  legacies, regional struggles, traits, artifacts — none of which is implemented.
