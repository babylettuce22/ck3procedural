# Third-party asset credits

Every asset under `assets/` that did not originate in this project is listed here, with the licence
it arrived under and what that licence obliges.

**Why this file exists.** Everything here was CC0 until 2026-08-27, and CC0 obliges nothing — so
there was nothing to record. CC-BY does oblige something: the author must be credited wherever the
work is shared. That includes a shipped mod. Anything added below under CC-BY must have its credit
line reproduced in the mod's own public description, not only in this file.

---

## CC-BY-4.0 — attribution REQUIRED

### 5 Piece Platemail - MetaHuman (rigged)
- author: DevonLux — https://sketchfab.com/DevonLux
- source: https://sketchfab.com/3d-models/5-piece-platemail-metahuman-rigged-d5a36fd5d69640b29bb7e92f72f61608
- licence: CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/
- commercial use: allowed
- used for: `armors/plate01_parts.mesh` — cuirass, vambraces, leg harness, sabatons, gauntlets.
  The helm ships with the set but is not used yet: it is unskinned, and CK3 attaches head gear to a
  separate skeleton from the body (see `for_future_armors.txt` 5H).

Credit line to reproduce verbatim wherever this is shared:

> This work is based on "5 Piece Platemail - MetaHuman (rigged)"
> (https://sketchfab.com/3d-models/5-piece-platemail-metahuman-rigged-d5a36fd5d69640b29bb7e92f72f61608)
> by DevonLux (https://sketchfab.com/DevonLux) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

### Executioner Sword
- author: Leon Steiner — https://sketchfab.com/Leon.Steiner
- source: https://sketchfab.com/3d-models/executioner-sword-de8e451fa6014d7e9ea3d5386f4893a0
- licence: CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/
- commercial use: allowed
- used for: `weaponparts/sword_parts.mesh` — family `cc_executioner_sword_01`, all four slots.
  Textures converted from the supplied 4096x8192 JPEG/PNG to 1024x2048 DXT5 in
  `weaponparts/textures/`. Geometry is unmodified apart from a uniform rescale (x0.49436)
  and a PCA rotation onto the library's +Y axis convention.

Credit line to reproduce verbatim wherever this is shared:

> This work is based on "Executioner Sword"
> (https://sketchfab.com/3d-models/executioner-sword-de8e451fa6014d7e9ea3d5386f4893a0)
> by Leon Steiner (https://sketchfab.com/Leon.Steiner) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

---

## CC0 — no obligation, recorded for provenance

| asset | source | note |
|---|---|---|
| `armors/custom_breastplate_01` | Krakow museum scan via Wirtualne Muzea Malopolski | decimated 261856 -> 6002 tris |
| `armors/gambeson_01` textures | ambientCG Fabric063 | box-projected, baked into map1 |

---

## Where the limb pieces come from, and why the licence changed

CC0 has no limb armour at all. Searched downloadable CC0 on Sketchfab 2026-08-27 for pauldron,
gauntlet, vambrace, greave, sabaton, tasset, cuisse, sallet, armet, barbute, backplate and
chainmail: **zero results for every one of them**. CC0 yields helmets and breastplates only —
museum scans from Wirtualne Muzea Malopolski and the Cleveland Museum of Art.

So a full harness is reachable under CC-BY or not at all. Other CC-BY sources worth returning to:

| uploader | holdings | note |
|---|---|---|
| `DevonLux` | 6 MetaHuman-rigged sets: platemail, heavy, scalemail, bandit 7pc, leather, cloth | pre-split into slots; one fitting pass generalises across all six |
| `ernst52` | German Munition half-armour, Pikeman's, Harquebusier's, 8 helmets | historically accurate, moderate poly |
| `Multipainkiller_Studio` | Barbuta 2848 tris, Visored Barbuta 6624, arm guards under 1k | already at game polycount, no decimation needed |
