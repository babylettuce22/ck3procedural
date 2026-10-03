# Features in detail

This page holds the longer explanations behind the summary in the [README](../README.md).

- [Heightmaps and map size](#heightmaps-and-map-size)
- [Painting the climate](#painting-the-climate)
- [Finding the game and output folder](#finding-the-game-and-output-folder)
- [Editing worlds](#editing-worlds)
- [What is generated](#what-is-generated)
- [Azgaar imports](#azgaar-imports)
- [Vanilla cultures and faiths](#vanilla-cultures-and-faiths)
- [Languages, names and calendar](#languages-names-and-calendar)
- [Faith icons](#faith-icons)
- [Native rank titles](#native-rank-titles)
- [Coats of arms](#coats-of-arms)
- [How generation works](#how-generation-works)
- [Validation and current limits](#validation-and-current-limits)

## Heightmaps and map size

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

## Painting the climate

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

## Finding the game and output folder

`Core/GameLocator.cs` searches Steam libraries and common installation locations. It also
resolves the user's Documents folder for the CK3 launcher mod directory, including redirected
Documents folders. Use **Game folder…** or CLI `--game` if automatic detection finds the
wrong installation.

Export produces a mod directory and a sibling `.mod` launcher file. On the CLI, a bare
`--mod` name is placed in the detected launcher mod directory; a path selects a specific
output directory. `--mod` without a value uses the default `proceduralmap` folder.

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
| Peoples and religions | Cultures, heritages, languages, name lists, faiths, faith icons, doctrines, holy sites, and ethnicities. |
| Characters and history | Rulers, houses, dynasties and their coats of arms, ancestors, formation history, starting wars, bookmarks, portraits, and chronicles. |
| Military and artifacts | Generated men-at-arms and associated innovations, regalia, and composed weapon and armour assets. |
| Regional content | Centers of the World and wonders, regional struggles, routes, Silk Road and steppe content, and formation decisions. |
| Governments | Government assignment, administrative and nomadic content, hegemony support, and dynastic-cycle integration. |
| Optional systems | Wilderness and colonisation, county ruins, fantasy racial traits and morphology, and generated Restorationist and inversion-cult societies. |
| Integration | Localization, GUI changes, defines, and compatibility declarations and patches for the replacement world. |

Many systems combine generated files with hand-maintained file sets in `BaseFilesToCopy/`.
Wilderness, ruins, and fantasy content are gated by their settings; ruins also require
wilderness. Fantasy ethnicities are off by default. Societies combine generated world state
with the static `Societies` file set; they are off by default and hidden in the normal
settings, and `--societies` turns them on. The older `SocietyPrototype` set is a separate
reference implementation; `--society-prototype` ships it in place of the generated societies.
The presence of a magic setting does not represent a completed procedural magic system.

Adult heads of generated faiths begin office with at least 20 Learning. Spiritual heads receive
at least three-star Learning education; temporal heads keep their existing education specialty.
This applies at the starting bookmarks and to successors or newly created heads in play.
Higher skills and education are retained, and childhood education proceeds normally.

Generated faiths that already elect their spiritual head register new duchy-tier sees with a
permanent Synod Seat. Registration follows the founding title transfer by a day; a yearly check
by the Head of Faith repairs missing registrations, including sees in older saves. Existing seats
keep their succession and survival rules. Temple baronies and the Head of Faith receive no new
seats. A personal appointment marked `gen_synod_personal` is removed when its holder receives a
permanent seat or becomes Head of Faith. Personal titles are excluded from permanent-seat recovery.
Generated clerical-region faiths skip vanilla's automatic cardinal refills and seeding toward ten.
Their Head of Faith can instead use **Appoint Synod Member** to give an eligible same-faith cleric
a personal seat, and a ruler can petition the head to seat their court chaplain. Both routes share
one allowance (one personal seat per three permanent seats, minimum one), charge only when the
seat is granted, and recheck eligibility and capacity on acceptance; see
`docs/synod-personal-appointments.md`. Their see-founding, influence-vote, and Legation
eligibility systems remain in place.

Run `python tools/verify_synod_registration.py --game "<CK3 game directory>"` to check the scripts'
vanilla fallback bodies, references, persistent links, and localization encoding. This checks
source compatibility; [the runtime checklist](synod-registration-checks.md) covers actual elections,
title changes, and save/reload behavior in CK3.

## Azgaar imports

The importer uses the export's names and name bases, province and state domains, political
hierarchy, government forms, religions and their ancestry, cultures, climate, and biomes.
Imported climate is combined with the local model's seasonal and relief detail. Terrain
relief still comes from the heightmap. Race tags on imported cultures can inform fantasy
ethnicities when that feature is enabled.

The generator fills in content the export does not supply, including CK3 characters,
dynasties, and prehistory. Importing is a translation into CK3's hierarchy and systems;
settings that conflict with imported data are identified in the GUI.

## Vanilla cultures and faiths

`ContentSource` (`--content vanilla`) lays a region of CK3's real world onto the generated
map. The generator picks a window of vanilla's map holding about as many counties as the
generated map has, shaped like its land, and projects the map onto it. Several seeded windows
are tried, and the one whose peoples best suit the generated land wins. `VanillaRegion`
(`--vanilla-region world_europe`) confines the window to one or more of vanilla's geographical
regions; a region smaller than the map is taken whole with its nearest neighbours.

- **Titles.** Every empire, kingdom, duchy, county and barony is a vanilla title with its
  vanilla name, arms and culture-specific names. They are laid on top down: the map's empires
  take vanilla empires, their kingdoms that empire's kingdoms, and so on, so every title keeps
  the generated map's shape and sits inside its real parent.
- **Characters.** Each ruler is the character vanilla's history has holding that title at the
  start date, written with their vanilla traits, parents, marriages and dynasty, plus enough
  of their family (ancestors, siblings, children, spouses) to make a house. A title vanilla gives
  nobody, or a county vanilla keeps in a king's demesne, keeps a generated ruler.
- **Cultures and faiths.** Each county has the culture and faith its vanilla county had at the
  Advancement Year. Vanilla definitions, name lists, innovations, doctrines and localisation are
  used unchanged; the mod only references their keys.
- **Realms.** The independent realms and their vassal dukes are vanilla's own at that date. Each
  ruler sits in the realm's vanilla capital, or in a county of the real ruler's own people and
  faith, so the Byzantine emperor is Greek and Orthodox.
- **Holy sites and heads of faith.** Holy sites stand in their vanilla counties when the map has
  them, and heads of faith such as the Papacy are seated in their vanilla capitals.

With an Azgaar import the export's hierarchy and states are kept and vanilla titles are laid
onto them the same way. The mode cannot be combined with fantasy ethnicities.

To see what the installed game offers, or one culture or faith in full:

```powershell
dotnet run -- --vanilla-catalog
dotnet run -- --vanilla-catalog catholic
```

## Languages, names and calendar

Generated names use a phonology, lexicon, and language flavour. Heritages have related
language families and cultures have dialects, so related peoples can share recognizable
name elements. Faith names can use a liturgical register. Azgaar imports also use the
export's names and Markov name bases.

The world also gets its own calendar in the language of its most widespread people: twelve
month names, often built from that language's words for the season and for "moon", and an era
after every year ("12 Talvenmoon 900 TR"). Names can be typed on the Calendar tab (or with
`--calendar-era` and `--calendar-months`); anything left blank is generated, and an Azgaar
export's own era is used before a generated one. Turn it off with World Calendar in World State,
or `--no-calendar`. Worlds using vanilla cultures and faiths keep CK3's calendar.

To sample a language without generating a map:

```powershell
dotnet run -- --languages Norse 4242 family
```

Omit the flavour name to sample all flavours. The implementation lives in
`MapGen/Language/`: `Language.cs`, `Phonology.cs`, `Lexicon.cs`, and `LanguageFlavour.cs`.

## Faith icons

Every generated faith gets its own icon, rendered as a relief: a symbol raised from metal,
stone or wood, lit from the upper left, in vanilla's 100×100 format.

- **Religions share a symbol.** Each religion takes one motif family (suns, crescents, crosses,
  a world tree, knots, wheels, antlers and so on), chosen from its faiths' tenets and
  whether it is Abrahamic-shaped. No two religions share a family. Faiths within it vary the
  motif, frame and material, as vanilla's Christian faiths are all crosses.
- **Material follows standing.** Unreformed faiths are carved in wood, stone, bone or iron;
  reformed ones are cast in bronze, verdigris, silver or jade; Abrahamic-shaped faiths and faiths
  with a head are gold, silver, electrum, obsidian or enamel. Unreformed faiths also get the icon
  they will show once reformed.
- **The inlay is the faith's colour.** Gems, bosses and medallion fields are enamelled in the
  faith's map colour, and redrawn when the colour is edited.

Choosing a vanilla icon in the Faith inspector replaces the generated one. Turn the feature off
with Generated Faith Icons in Cultures and faiths. The designs live in `MapGen/Peoples/FaithIcons.cs`
and the renderer in `Emit/Relief/`.

## Native rank titles

Optional, and off by default. With Native rank titles on (Cultures and faiths, or the Quick
generator's People step), each generated culture styles its rulers in its own language. Its
counts, dukes, kings and emperors, and its chieftains, governors and priest-rulers, get words
built from its tongue's roots. A duke is war + lord and an emperor is great + king, so sister
cultures end up with related words. Native realm names does the same for Barony, County,
Duchy, Kingdom and Empire. The two options work independently.

- **Variants follow the ruler's situation.** A sovereign duke is a prince. March, palatinate
  and castellany contracts have their own titles. A ruler holding two or more duchies or
  kingdoms is a grand duke or high king. A people ruled under another religion takes its
  crown's words from that religion's holy tongue.
- **A realm uses its top liege's words**, as the English vocabularies do.
- **Hovering a native word in game shows its English equivalent** (King, Margrave, Duchy). Turn
  this off with Native rank tooltips.

To sample the words for a language, `--languages` prints them. The coining is in
`MapGen/Titles/NativeTitles.cs` and the game files in `Emit/Culture/NativeRankWriter.cs`. From the command
line: `--native-titles`, `--native-realms`, `--no-rank-tooltips`.

## Coats of arms

Dynasty and house arms are rolled with vanilla's own heraldry rules, read from the installed
game: its weighted templates, tincture lists and emblem lists, with the regional sets (kamon,
Byzantine rondels, steppe tamgas, Iberian bordures and so on) chosen by the same culture and
faith triggers the game uses. A main house bears its dynasty's arms; a cadet bears them in
another livery with a cadency mark. Everything is written out, so the bookmark screen and the
Ruler inspector show real shields. Without a game install, a small fixed set is used instead.
The rules are run in `Emit/Culture/VanillaHeraldry.cs`.

## How generation works

`Core/Generator.Generate` derives the map: terrain input, optional Azgaar binding, climate,
drainage, major-river carving, province partitioning, terrain classification, and title
hierarchy. Preview layers are published as these stages finish.

`Core/Generator.WriteMod` exports the map and calls `Emit/ContentWriter.cs` to build and
write the world content. Its first step, `ContentWriter.BuildWorld`
(`Emit/ContentWriter.World.cs`), decides the social layers (development, wilderness,
cultures, realms, governments, faiths, cultivation, routes, water names) into a `WorldModel`
without writing any files; the writers then emit from it. Some raster work runs alongside
content and history generation. The installed game's vocabulary and data influence cultures,
faiths, regiments, graphics, and compatibility output.

A seed is useful for repeatable comparisons, but **a seed and heightmap alone are not a
complete reproducibility record**. Keep the configuration, optional Azgaar/Forge input,
generator and Forge revisions, installed game data, and required assets as well. Exported
watermarks include a generation timestamp, so whole-folder byte identity is not promised.

The History simulation also maintains political alliances between nearby independent adult
rulers. Shared threats, house relations, and religious compatibility influence agreement;
each ruler has at most three allies. Agreements are reviewed at succession and periodically,
and direct allies may accept or refuse calls to war. Committed support influences warfare and
is divided across simultaneous wars. Losing independence ends an external agreement.
Independent theocrats can negotiate same-faith political agreements without marriage; their
successors are appointed adult clerics rather than child heirs. This does not simulate a full
marriage market or let vassals form independent military coalitions.

Applying a history carries its surviving alliances and allied war participants into the main
bookmark, rather than inventing a replacement external alliance network. The saved history
also keeps offer cooldowns, ongoing wars, truces, and claims for continuation. The History
workspace exposes an **Alliances** rule and lists each realm's allies in the map readout.

## Validation and current limits

The repository includes focused diagnostic checks:

```powershell
dotnet run -- --verify-world-editor
dotnet run -- --verify-compose

dotnet run -- --verify-history-alliances
```

The world-editor check exercises edits in a disposable fixture. An optional mod path also
checks that opening and saving an unchanged existing world preserves bytes and timestamps.
The composition check compares weapon attachment and merged geometry.
The alliance check covers formation, calls and refusals, shared military commitments,
clerical succession, neutrality, deterministic yearly runs, and diplomatic save/resume.

These checks do not establish that a generated mod works in CK3. Changes to emitted content
also need an export, validation with a matching **ck3-tiger**, and in-game inspection. Map
rendering, locators, portraits, and scripted gameplay require runtime checks; a successful
C# build cannot validate them. Generation time and memory use depend on map size and enabled
features.
