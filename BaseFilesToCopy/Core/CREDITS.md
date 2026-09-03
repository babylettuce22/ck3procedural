# Third-party asset credits

Every asset under `assets/` that did not originate in this project is listed here, with the licence
it arrived under and what that licence obliges.

**Why this file exists.** Everything here was CC0 until 2026-08-27, and CC0 obliges nothing — so
there was nothing to record. CC-BY does oblige something: the author must be credited wherever the
work is shared. That includes a shipped mod. Anything added below under CC-BY must have its credit
line reproduced in the mod's own public description, not only in this file.

**Why this file lives in `BaseFilesToCopy/Core` rather than in `assets/`.** So that it ships.
Everything under `BaseFilesToCopy/Core` is copied into every generated mod, so this file lands
beside `descriptor.mod` in the mod a player actually receives — which is what "wherever you share
it" means. It sat in `assets/` until 2026-08-28 and therefore reached nobody outside this repo,
which left the obligation unmet for every build shipped up to that point. The same lines are also
reproduced at the bottom of the repo's public `README.md`.

---

## CC-BY-4.0 — attribution REQUIRED

### 5 Piece Platemail - MetaHuman (rigged)
- author: DevonLux — https://sketchfab.com/DevonLux
- source: https://sketchfab.com/3d-models/5-piece-platemail-metahuman-rigged-d5a36fd5d69640b29bb7e92f72f61608
- licence: CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/
- commercial use: allowed
- used for: `armors/plate01_parts.mesh` — cuirass, vambraces, leg harness, sabatons, gauntlets — and
  `attachments/pauldron2_shoulder_l.mesh`, the `pauldron2` set, whose geometry is the cuirass's
  shoulder isolated and split at the midline. Its right side is reflected from the left at
  generation time rather than shipped separately.
  The helm ships with the set but is not used yet: it is unskinned, and CK3 attaches head gear to a
  separate skeleton from the body (see `for_future_armors.txt` 5H).

Credit line to reproduce verbatim wherever this is shared:

> This work is based on "5 Piece Platemail - MetaHuman (rigged)"
> (https://sketchfab.com/3d-models/5-piece-platemail-metahuman-rigged-d5a36fd5d69640b29bb7e92f72f61608)
> by DevonLux (https://sketchfab.com/DevonLux) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

### Medieval Shoulder Pad
- author: ViniciusMello — https://sketchfab.com/ViniciusMello
- source: https://sketchfab.com/3d-models/medieval-shoulder-pad-5376ef05f3d3448889517d9bd0ff8421
- licence: CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/
- commercial use: allowed
- used for: `attachments/pauldron3_shoulder_l.mesh` — the left pauldron of the `pauldron3` set, at
  398 vertices the lightest of the three. The right side is reflected from the left at generation
  time rather than shipped separately.
  Textures converted from the supplied 1024x1024 baseColor/normal/metallicRoughness maps into CK3's
  layouts by `PieceTextures`, from the sources kept in `attachments/textures/pauldron3_*`.

Credit line to reproduce verbatim wherever this is shared:

> This work is based on "Medieval Shoulder Pad"
> (https://sketchfab.com/3d-models/medieval-shoulder-pad-5376ef05f3d3448889517d9bd0ff8421)
> by ViniciusMello (https://sketchfab.com/ViniciusMello) licensed under CC-BY-4.0
> (http://creativecommons.org/licenses/by/4.0/)

### Shoulder Armor
- author: ilyaballz — https://sketchfab.com/ilyaballz
- source: https://sketchfab.com/3d-models/shoulder-armor-053d84b1034c429ab476778022d64ff5
- licence: CC-BY-4.0 — http://creativecommons.org/licenses/by/4.0/
- commercial use: allowed
- used for: `attachments/pauldron1_shoulder_l.mesh` — the left pauldron of the `pauldron1` set. The
  right is not a second asset: `BonePieceStep` reflects the left across the body's midline at
  generation time, so both shoulders come from this one mesh.
  Textures converted from the supplied 1024x1024 baseColor/normal/metallicRoughness maps into CK3's
  own layouts — DXT5nm normals, and (AO, spec, metalness, roughness) properties — by
  `PieceTextures`, from the sources kept in `attachments/textures/pauldron1_*`.

Credit line to reproduce verbatim wherever this is shared:

> This work is based on "Shoulder Armor"
> (https://sketchfab.com/3d-models/shoulder-armor-053d84b1034c429ab476778022d64ff5)
> by ilyaballz (https://sketchfab.com/ilyaballz) licensed under CC-BY-4.0
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
