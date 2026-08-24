<img width="1790" height="935" alt="genguipic" src="https://github.com/user-attachments/assets/bece6c17-74fb-4f13-a539-60a07c044c55" />

# CK3 Procedural Tool

A generator that turns a single heightmap into a complete, playable Crusader Kings III total
conversion — the map rasters, the de jure title hierarchy, the cultures and faiths that name it, the
rivers and terrain it is painted with, the rulers who hold it on the start date, and the history that
put them there.

It does not generate terrain. The heightmap is an input, drawn wherever you like — by hand, by
Azgaar's Fantasy Map Generator, by another tool — and it is authoritative: it decides the map's size,
its coastline and its relief, and everything the mod ships is derived from it rather than negotiated
with it. What this program does is *interpret* that image, and then write the several hundred files
CK3 needs in order to believe in it.

A seed and a heightmap fully determine a world.

---

## What it produces

A mod folder, plus the sibling `.mod` file the launcher needs to see it. Inside:

| Area | What is written |
| --- | --- |
| `map_data/` | `heightmap.png` and the packed/indirection atlas pair, `provinces.png`, `rivers.png`, `definition.csv`, `default.map`, `adjacencies.csv`, `island_region.txt`, `seasons.txt` |
| `common/landed_titles/` | The full empire-to-barony de jure tree, named and coloured |
| `common/culture/`, `common/religion/` | Generated cultures, heritages, name lists and languages; generated religions, faiths and holy sites |
| `common/province_terrain/`, `history/provinces/` | Per-province terrain, holdings and development |
| `history/titles/`, `history/characters/`, `common/dynasties/` | Who holds what on the start date, the dynasties they belong to, and generations of ancestors behind them |
| `common/men_at_arms_types/`, `common/culture/innovations/` | A men-at-arms roster the world invented: one regiment per heritage, an elite for the cultures that earned one, and the innovations that unlock them |
| `common/struggle/`, `common/decisions/` | A generated regional struggle where the map is genuinely contested, with its four phases, catalysts and three endings |
| `common/artifacts/`, `common/coat_of_arms/` | Regalia carried by the rulers who inherited it, and arms for every house and title |
| `common/buildings/` | Wonders — special buildings raised at the world's landmarks, with the meshes and locators to stand them on |
| `common/bookmarks/`, `common/bookmark_portraits/` | A bookmark on the generated start date, with portraits |
| `common/ethnicities/`, `gfx/portraits/` | Ethnicities per culture — and, optionally, fantasy races with their own morphology |
| `gfx/map/terrain/` | Detail index and intensity textures, per-material masks (including `masks_gen`), colormap, flatmap |
| `gfx/map/water/`, `gfx/map/textures/` | Foam, water colour and snow masks repainted for the new coastline |
| `gfx/map/map_object_data/` | Locators for every holding, army and siege; trees, animals, weather effects, bridges and the map table |
| `common/defines/`, `map_data/geographical_regions/` | The engine's world size, and re-declarations that keep vanilla and DLC script from erroring |
| `localization/english/` | Names for everything generated, and a chronicle of the history behind it |

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
4. **Rivers** — the largest courses are carved into the heightmap as navigable major-river provinces;
   the rest are drawn into `rivers.png` as CK3's palette of widths, sources and tributary joins.
5. **Provinces** — a terrain-weighted, Lloyd-relaxed partition. Province *size* follows habitability
   (coasts, flat ground, river valleys, kind latitudes) rather than noise, because that is what makes
   a map read as settled rather than as patterned.
6. **Terrain classification** — climate × landform, which is also the matrix CK3's own `gen_*`
   material family is organised along.
7. **Titles** — baronies clustered up into counties, duchies, kingdoms and empires, allowed to reach
   across straits at the top two tiers only.

**Write** (`Core/Generator.WriteMod`)

Development, then cultures, then ethnicities, then realms, then governments, then faiths — an order
that is forced rather than chosen, since a title is named in the language of whoever lives there, and
whether a faith starts unreformed depends on how tribal its counties are. Then the world's way of
war — a men-at-arms roster grown from the ground each people holds, its temperament and its
government — and then the rasters, the textures, the locators, the scattered map objects, and last
the history: ancestors, rulers, wars, artifacts, the chronicle that ties them together, and any
struggle the map has earned.

Nothing in that roster is a number this program invented. Every stat, price and counter is read out
of the installed game's own regiments of the same archetype and rearranged inside their budget, so a
generated unit is a variant of a vanilla one rather than a guess at one — and a balance patch to CK3
moves the generated roster with it.

## Importing an Azgaar map

An Azgaar Fantasy Map Generator export (`--azgaar`, or the Azgaar tab) is an **adjunct, never a
requirement** — with no export given the generator behaves exactly as it did before. Given one, it
stops inventing the things Azgaar already decided:

- **Names** — cultures, faiths, states, provinces, burgs and water bodies, with a port of Azgaar's
  own Markov name generator for everything the export did not name itself.
- **Borders** — the province partition is grown *inside* Azgaar's own province and state domains, so
  realm borders are pixel-accurate rather than approximate to within a barony, and every Azgaar
  province gets at least one barony.
- **Hierarchy** — Azgaar ranks its own states by area; a state at its tier T becomes a CK3 title at
  tier T, subdivided below and grouped above by suzerainty, then culture, then landmass. States keep
  their own colours, and the tier shading below them stays ours.
- **Governments** — each state's form and formal name are read into a CK3 government type.
- **Religions** — organised religions and folk faiths become CK3 religions with a founding faith;
  heresies and cults become faiths inside the nearest ancestor, following the export's `origins`.
- **Climate** — a *reanchoring* rather than a substitution: the export says where it is hot and where
  it rains, and our model keeps the seasonal swing and the sub-grid relief detail it has none of.
- **Vegetation** — the export's biome map paints what grows on the ground, broken up by the same
  mosaic noise a generated map uses so its cell polygons do not show. Relief stays ours throughout:
  beach, hills, mountains and the snow line are altitude facts our heightmap resolves far finer than
  Azgaar's cells do.
- **Peoples** — one CK3 culture per culture the export drew, over the ground it drew them on. The
  heritages above them come from the export's ancestry where it drew one; where every culture
  descends from Wildlands — which is what Azgaar's own generator writes — from shared name bases, and
  then from geography, never merging two peoples the export tagged as different races.

Azgaar generates no characters or dynasties, so prehistory still builds those on top.

## Fantasy races

Off by default. Turned on (`--races low|high|exotic`), heritages and cultures are given races —
including from an Azgaar fantasy preset, which tags its cultures "Dunirr (Dwarven)" and the like;
the parenthetical is stripped from every display name and consumed as data. Races get their own
morphology through CK3's gene system, a trait that is assigned at runtime to generated courtiers and
adventurers as well as to the characters written into history, and the vocabulary to go with them.

## Two front ends

Both drive the same pipeline, so there is one definition of it rather than a console one and a window
one that drift apart.

The **GUI** (`Ck3MapGen.exe`, or `--gui`) is one window: every setting in a searchable grid, a
zoomable preview with twenty-one map modes in four families — Physical, Climate, De Jure and World —
and a log. Zoom and pan survive a rebuild and a view switch, because tuning is a loop of nudge a
setting, rebuild, look at the same place. Progress and time remaining are learned from the previous
run rather than hardcoded. Beyond looking:

- **Click anything to inspect it.** Titles, cultures, faiths and rulers each open an inspector, and
  a written mod can be edited through them — rename a culture, retune a faith, change a ruler — with
  only the affected files re-emitted.
- **A heightmap tab** hosting the CK3 Heightmap Forge, so a map can be drawn, eroded and shipped
  without leaving the tool.

The **CLI** covers the same ground for scripting and for measurement. `--heightmap`/`--forge`,
`--azgaar`, `--mod`, `--game`, `--seed`, `--scale`, `--county-scale`, `--start-year`, `--era-anchor`,
`--races`, `--impassable-mask`, `--normalize-heightmap`/`--shift-heightmap`, `--no-history`,
`--no-packed` and a set of flags that exist purely so two ways of doing something can be generated
from one binary on one seed and diffed. It also dumps debug PNGs — elevation, terrain, provinces,
terrain classes, Köppen climate, drainage, rivers, rainfall and temperature — which are how a change
gets eyeballed. `--mod` takes a bare name as well as a path, and puts a folder of that name in the
launcher's mod folder.

## Finding the game

Neither directory the tool needs is hardcoded. `Core/GameLocator` looks for the CK3 install through
Steam's registry keys and its `libraryfolders.vdf` — which is what knows about a library on a second
drive — and then through the usual Steam, GOG, Epic and Paradox paths on every fixed drive. The
launcher's mod folder is found the same way, following a Documents folder that has been redirected
onto OneDrive or off C: entirely. The whole search is registry reads and a fixed list of existence
checks, so it runs on every launch for about ten milliseconds.

What it found is printed in the log at startup and carried on the *Game folder…* button, which is
also how a wrong or missing answer is corrected — by hand, remembered for next time, and re-searched
if the install later moves. A write refuses to start without a real game folder rather than failing
several minutes in, because the mod is generated *against* vanilla's own culture and religion data.

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
- **Measure the engine; do not trust what it says about itself.** Vanilla's own comment on the water
  level asserts two values that are not equal, and the false one had been believed here for months.
  Where a number matters, it is read out of the shipped files or out of a frame capture, and the
  measurement is written down beside the code that depends on it.
- **Settings are calibrated against vanilla, and say so.** Barony density, impassable share, the
  867 development curve, the ratio of cultures to heritages — the defaults aim at measured vanilla
  numbers, and the reasoning is written down beside each one.
- **Prove a change end to end.** The generator has no unit tests, because "it compiles" proves
  nothing about the Paradox script it writes. A full headless run plus ck3-tiger validation takes
  about two minutes, and a refactor that is meant to change nothing is expected to produce a
  byte-identical mod.

## Layout

```
Config/   MapConfig — every knob, with the vanilla figure it was calibrated against
Core/     the pipeline, the seeded RNG, noise, phase timing, game location
MapGen/   climate, drainage, rivers, provinces, terrain, habitability, titles, cultures,
          faiths, governments, development, language, prehistory, rulers, chronicle,
          struggles, and the Azgaar importer
World/    the coarse simulation grid the climate model runs on
Emit/     one writer per part of the mod
Io/       PNG, DDS and Paradox-text encoders written by hand, for exact pixel formats
Gui/      the WinForms front end, its inspectors and the embedded Heightmap Forge
BaseFilesToCopy/  hand-kept files copied into the mod verbatim, in named sets
docs/     design notes for mechanics beyond the map
```

Built on **.NET 10** (Windows, WinForms), with ImageSharp as the only package dependency — used for
*reading* images; everything written is encoded by hand, because CK3 demands an exact pixel format
per file and a general imaging library will not guarantee one. The heightmap pipeline is referenced
as source from the CK3 Heightmap Forge repo beside this one (override `NoiseToolDir` if it lives
elsewhere). Targets CK3 **1.19**, and reads the installed game for its culture and religion
vocabulary.

## Where it is unfinished

- **Azgaar's history is parsed and unused.** Every export carries dated, named, located events —
  campaigns with start and end years, zones for invasions and crusades and plagues, battlefield
  markers whose legends name the war and the day. All of it is loaded and nothing reads it yet.
- **Locators face due north.** Every holding and wonder is written with identity rotation; vanilla
  varies yaw per instance.
- **Editing a ruler does not re-emit the bookmark.** Rename a ruler in the inspector and the
  character history updates while `00_bookmarks.txt` and its localisation go stale.
- **Terrain wobbles in the last four rows.** Two runs on one seed and one build differ by a few
  hundred pixels at the very bottom edge of the map, which quietly weakens the byte-identical
  diffing everything else is verified with.
- **Mechanics.** Everything generated so far is geography, people, titles and the history between
  them. `docs/` sketches what else CK3's script layer would let a generator invent — men-at-arms,
  buildings, innovations, dynasty legacies, succession laws, and a procedural magic system with its
  own rules per world — none of which is implemented.
