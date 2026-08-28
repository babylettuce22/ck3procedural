# Procedural artifacts — handoff

Written 2026-08-26. Everything here was measured against CK3 **1.19.0.6** on this machine, or is
implemented and verified in the repo. Where something is unverified it says so.

The short version: generated weapon artifacts are **assembled from parts at generation time**,
recoloured procedurally, given matching icons, and rendered in the character's hand on the portrait.
Four weapon kinds ship. Armour is the next frontier and is *more* tractable than it looks — see §7.

---

## 1. What exists

| kind | library | families | shapes | in pool |
|---|---|---|---|---|
| sword | `sword_parts.mesh` | 14 | 56 | 8 |
| dagger | `dagger_parts.mesh` | 9 | 36 | 8 |
| axe | `axe_parts.mesh` | 8 | 20 | 8 |
| mace | `mace_parts.mesh` | 10 | 27 | 8 |

Each world forges 8 weapons per kind, plus 12 fixed `FORGE TEST` diagnostics reachable from the
decisions panel. Generated weapon artifacts draw their look from the pool instead of the vanilla
catalogue.

`sword_parts_raw.mesh` and `sword_parts_socketed.mesh` are **superseded** and read by nothing.
Delete when convenient — they are untracked, so only the `.blend` can regenerate them.

### Source files (~1,900 lines total)

```
Io/PdxMesh.cs                            binary .mesh reader/writer      247
MapGen/WeaponForge.cs                    schema, parts, assembly         557
Emit/WeaponForge/WeaponForgeStep.cs      orchestration, pools, recipes   544
Emit/WeaponForge/ForgedWeaponRecolour.cs masks, palettes, variations     374
Emit/WeaponForge/ForgedWeaponWriter.cs   .mesh + .asset emission         130
Emit/WeaponForge/ForgedWeaponIcon.cs     icon tinting                     90
```

Plus two static files in `BaseFilesToCopy/Core/`: the idle-animation override
(`gfx/portraits/portrait_animations/zz_procedural_weapon_idle.txt`) and its gate trigger
(`common/scripted_triggers/zz_pmg_portrait_weapon_triggers.txt`).

Everything is called from **two lines** in `ContentWriter` — `ForgeWeaponPools` before
`ArtifactMap.Build`, and `WriteAll` for the diagnostics.

---

## 2. Running and verifying

```
dotnet build                       # ALWAYS before --no-build if assets/ changed (see §6)
dotnet run --no-build -- --heightmap <png> --mod <dir> --seed 4242
<tiger>/ck3-tiger.exe <mod>/descriptor.mod
```

**Baseline is 3 errors, 53 warnings.** All three errors are pre-existing `window_*.gui`
datafunction-arity issues. Any forge change must return to that baseline. Tiger writes ANSI colour
even when piped — filter with `sed -e 's/\x1b\[[0-9;]*m//g'`.

**The regression that matters** is self-rebuild: reassembling a weapon from its own parts must
reproduce its original bounds exactly. A throwaway harness lives in the session scratchpad; it takes
`<library> <kind>` and prints per-family drift. Every library currently passes at
**drift 0.000000000**. If you change placement, this is the test that catches it.

In-game: take a `FORGE TEST` decision. It mints the weapon, equips it (creation does **not** equip),
and forces it onto the portrait. That is one click versus hunting fifty artifacts.

---

## 3. The pipeline

```
Blender (offline)          cut real weapons into parts, add sockets + UV2, export one .mesh per kind
  |
LoadParts                  parse shapes + locators, validate anchor and sockets present
  |
SelectParts                lead family supplies the business end, base family the rest
  |
Assemble                   place parts by socket, merge vertex streams, offset triangle indices
  |
ForgedWeaponRecolour       per-weapon mask + palette + variation
ForgedWeaponWriter         .mesh + .asset (entity)
ForgedWeaponIcon           tinted inventory icon
  |
WeaponAssets rows -> ArtifactMap.Build -> create_artifact visuals = gen_forged_*
  |
idle animation item -> modifierpack -> game_entity_override = weapon -> drawn in hand
```

---

## 4. Facts worth not rediscovering

### The `.mesh` format
A flat stream of depth-tagged nodes — essentially binary XML. Header `@@b@`; objects are `[`×depth
then a null-terminated name; properties are `!`, u8 name length, name, type char (`i`/`f`/`s`),
i32 count, payload. **String length is written as `len+1`** to count its terminator. A 60-line
reader handles the whole thing. Full grammar in `Io/PdxMesh.cs`.

**Axis:** in *file* space the weapon's long axis is **Z**, tip at negative Z. Blender shows this as
Y because io_pdx_mesh swaps coordinate space on import. Work in file space and no swap is needed.
`file = (blender.x, blender.z, blender.y)`.

**One `mesh` node per material.** A shape may hold several — that is how a weapon built from two
source weapons stays renderable — and the `.asset` needs one `meshsettings` per material, sharing
the shape name with incrementing `index`.

### Assembly
Parts are rigid and unskinned, so placement is **translation only**: add a vector to `p`, leave `n`
and `ta` alone, concatenate streams, offset `tri` by the running vertex count. That is the whole
reason this can live in C# with no Blender at generation time. **The moment a part needs rotating
or non-uniform scaling, normals and tangents must be transformed too** — that boundary has not been
crossed and is where the cheapness ends.

### Sockets
Parts carry locator empties named `<family>_<slot>_socket_<target>`. For parts A and B, A's
`socket_B` aligns onto B's `socket_A`, and mating pairs are authored at the *same* point so
self-reassembly is a zero translation. Alignment uses all three axes.

**Locators behave differently from meshes — do not assume:**
* a locator's node name is the **object** name; a mesh shape takes `obj.data.name`
* `p` is always **baked world position**, axis-swapped like geometry; local space is never written
* **parenting does not survive** export — no `pa`/`tx` is written, so the *name* is the only thing
  binding a socket to its part
* `chk_locs=True` is required or none are written; `chk_selected=True` filters locators too

**The socket rule, corrected:** a socket marks **where two parts physically meet**. For a part the
anchor passes *through* — an axe head, a mace head — that is the **anchor's axis at the join
height**, not the centroid of the part's cross-section. The original centroid rule was right for
swords and caps (where the two coincide) and put axe head sockets up to **15.6 units off-axis**,
making mixed axes float. Self-rebuild cannot catch this: both sides of the pair are wrong by the
same amount. The check that does: *the lateral distance from a socket to the anchor's axis should
be small.*

### Weapon schemas
Kinds are **data**, not branches (`WeaponSchema`). Each declares an anchor (the held part), a lead
(the business end), required slots, optional slots, and a mount chain walked front-to-back.

```
Bladed  (sword, dagger)  anchor Grip  lead Blade  chain: guard→grip, pommel→grip, blade→guard
Hafted  (axe, mace)      anchor Haft  lead Head   chain: head→haft, cap→haft   (Cap optional)
```

**Optional slots are load-bearing.** Half the vanilla axes and three of ten maces have no butt cap
because vanilla never modelled one; requiring it would discard them. That is different from a
*degenerate* part — a 2-face sliver posing as a guard — which should be excluded when cutting.

`Lead` must be **explicit**. Deriving it as "last link in the chain" was right for swords by
accident and wrong for axes, where the chain ends on the optional cap — so every mixed axe silently
took its head from the wrong family and the two test axes came out identical.

**Caps never migrate.** `SelectParts` takes only the lead slot from the lead family; the anchor and
everything else come from the base family. So an odd cap (byzantine's wrist-strap, african's
1242-face ferrule) only ever appears on its own haft. No recipe exclusion needed.

### Recolouring
Read from the shader, not inferred — `<install>/jomini/gfx/FX/jomini/portrait_accessory_variation.fxh`
(note the **jomini/ root**, not `game/gfx/FX/`):

```
float4 Mask = PdxTex2D( PatternMask, Input.UV0 );   // which channel applies where -> map1
ApplyPattern( Input.UV1, ... );                     // the tiling swatch           -> map2
```

The **mask samples the model's existing atlas UV**; only the swatch tiles over UV2. This is the
reverse of the natural reading and was wrong in a brief before being caught by reading the source.

One `pattern_mask` per **entity**, never per `meshsettings`. Since a forged weapon mixes parts whose
map1 layouts overlap (53.9% of cross-family slot pairs share texels), parts cannot simply be handed
a channel each. The resolution: **two parts sharing a texel must share a channel**, so grouping
parts into connected components of the "overlaps" relation always yields a valid mask, and four
parts can never make more than four groups — exactly the channel count. Colour count degrades
gracefully rather than the weapon failing.

That is also why the pool picks **one lead family + one base family** rather than four independent
ones: a coherent base yields 2+ distinct colours 81% of the time against 42% for a free-for-all.

Palette is a 16×N uncompressed BGRA DDS, rows duplicated so the shader's random row pick is a no-op.
`Io/DdsWriter.WriteBgra` already emits the right format.

### Icons
Artifact icons are a **960×240 uncompressed BGRA strip of four 238×240 frames**
(`framesize = { 238 240 }`, `frame = N`) drawn at **30–60 pixels**. At that size a player reads
colour and rough silhouette and nothing else, so we tint the stock icon rather than render geometry.
If a real render ever replaces it, draw the **hilt** — a whole sword in a 238×240 cell is a thin
diagonal, while the hilt is nearly square and carries the identity.

### Getting a weapon onto a portrait
**The animation is the trigger, not the artifact.** Nothing shows a weapon because it is equipped.
The portrait plays a named animation → `animations.modifierpack` has a trigger-gated block for that
animation → it injects a prop → the prop declares `game_entity_override = weapon` → the engine
substitutes the artifact's own entity. Only **38** animation names have prop blocks and `idle` is
not one of them, which is why this looks broken until you hook it.

We hook it with an item inside `idle` in a re-declared `idle` key. **Never name that file
`animations.txt`** — a same-path file replaces vanilla's outright and drops every other animation.

**A pose and a pack must be a pairing vanilla actually authored.** This is stronger than the decal
rule and breaks visibly: pairing `throneRoom_twoHandedPassive1_entry` with the `two_handed_spear`
pack put the spear on `bn_r_prop` while that pose only animates the **left** hand, so it hung in
space. Check which prop node a pack uses — `_left` variants exist. To find valid pairings, grep
`animations.txt` for `portrait_modifier_pack = <pack>` and read the `animation = { head = ... }`
above each hit.

**Most weapon poses are male-only.** `hunting_knife_start`, `sword_yield_start`, `threatening`,
`chessCocky1`, `idle_spear`, `assassin`, `celebrateOneHanded*`, `oneHandedAggressive1`,
`sword_coup_degrace` — all have **zero** female `.anim` files. This has cost three debugging rounds.
Always check both bodies before choosing a pose.

Current pose table, all both-gender:

```
sword   council_marshal                    one_handed_sword
dagger  council_marshal                    one_handed_dagger
axe     throneRoom_oneHandedPassive1_entry one_handed_axe
mace    throneRoom_oneHandedPassive1_entry one_handed_mace
spear   throneRoom_twoHandedPassive1_entry throneRoom_two_handed_passive_1
hammer  throneRoom_oneHandedPassive1_entry two_handed_hammer
```

Pose height tracks weapon geometry: daggers are too short for a low hold and get raised; swords are
long but thin so raised is safe; axes and maces have bulky off-axis heads (21.9 units across against
a sword blade's 7.3) that cross the face at chest height, so they get lowered.

---

## 5. The Blender side

io_pdx_mesh **0.91.0** with **three local patches** (stashed at `C:\Users\caelo\ck3-modding-tools\`
with a README). Re-apply after any addon self-update:

1. `Material.shadow_method` / `blend_method` — removed in Blender 4.2; guard with `hasattr`
2. `from imp import reload` → `from importlib import reload` — `imp` removed in Python 3.12
3. `bpy.ops.object.join(ctx)` → `bpy.context.temp_override(...)` — context dicts removed in 3.2+;
   fires on **every multi-material mesh**, and its failure is misleading because the geometry
   imports fine and only the join throws

**The `.asset` is authoritative for textures, not the `.mesh`.** A mesh may name a texture living in
a different folder (importer can't find it, exports empty strings) or name none at all. An empty
`texture_diffuse` renders untextured, which on a portrait reads as **a hole punched through the
character**. `WeaponPart.HasTextures` requires all three of diff/n/spec and the forge skips any
family missing them, naming it.

Exporter slot mapping is counter-intuitive: Base Color→`diff`, **Roughness→`spec`**, Normal→`n`.
Only the basename is exported, and CK3 resolves textures **globally by filename**, so nothing needs
copying beside the mesh.

UV2 is a projection for tiling material swatches, not an atlas — even texel density matters, packing
does not. All four libraries use a world-space box projection at **SCALE = 20.0 world units per 1.0
UV**, so they share one `pattern_layout`. **The exporter flips V**: `file_v = 1 − blender_v`.

---

## 6. Traps that cost time

* **Stale assets in `bin/`.** The library probe checks `AppContext.BaseDirectory/assets` first. A
  library added since the last build is invisible and its kind silently doesn't forge; a library
  *deleted* from `assets/` keeps working from `bin/`. **`dotnet build` before `--no-build` whenever
  `assets/` changed.** This has bitten twice.
* **The armour gate.** Weapons were invisible on generated artifacts because the gate required
  `portrait_wear_armor_trigger`. Confusingly, `FORGE TEST` decisions set
  `pmg_always_show_equipped_weapon` **permanently**, so a test character then rendered every weapon
  forever — which reads exactly like a portrait-cache bug. It is not. Now defaults to `always = yes`.
* **Copying vanilla's `idle` block imports its character cameos**, which dangle because this mod
  `replace_path`s `history/characters`. Filter at *modifier* granularity, not by item — some are
  pure cameos, others are generic items that merely nudge one character.
* **Square brackets in localisation are datafunction syntax.** `[FORGE TEST]` is parsed as a call.
* **`PdxNode.Child(name)` creates the node when absent** — never use it in a property getter.

---

## 7. The armour path

This is the intended next step, and the research is further along than you might expect.

### What is genuinely different

**Armour is not a prop.** `game_entity_override` has exactly **one** value anywhere in vanilla —
`weapon`, 14 uses, all in `accessories/props.txt`. There is no `armor` override. The body slots are
`crown` (helmet), `regalia`, `armor`, `weapon`, and **vanilla contains zero artifact-gated portrait
modifiers**, so it never renders an equipped crown, armour or regalia at all.

AGOT builds them by hand: one portrait modifier + one accessory **per artifact**, in an ordinary
`usage = game` group. Worked example in `05_headgear_situational.txt`:

```
agot_headgear_daemonblackfyre_crown = {
    dna_modifiers = { accessory = {
        mode = add   gene = headgear   template = agot_crowns   accessory = daemonblackfyre_crown
    } }
    weight = { base = 0  modifier = { add = 300  OR = {
        agot_has_artifact_equipped = { ARTIFACT_VARIABLE = crown_daemonblackfyre_artifact }
        has_inactive_trait = equipped_crown_daemonblackfyre_artifact
    } } }
}
```

**The difficulty is inverted from weapons.** Armour needs **no animation hook at all** — `usage =
game` groups are evaluated on every portrait, so it shows in every context automatically. But it
costs one generated portrait modifier + one accessory **per artifact**, where a weapon costs
nothing per artifact because the engine substitutes the entity for free. Both are pure text, so both
are generatable; armour just emits more.

### The skinning question — answered

This decides whether part-assembly works at all, so it was worth measuring:

| mesh | skin data |
|---|---|
| `m_clothes_sec_byzantine_war_nob_01_artifact.mesh` (the *artifact display* piece) | **none** |
| `m_clothes_religious_african_hi_01.mesh` (a *worn* garment) | `bones`, `ix`, `w` |

So the artifact-clothing meshes are rigid display pieces, but what a character actually **wears** is
a skinned garment from `gfx/models/portraits/m_clothes/`. Assembling armour parts means assembling
**skinned** meshes.

The good news: `bones` is a single int — the number of **influences per vertex** (4) — and `ix`
holds indices into the shared body rig, 4 per vertex. Parts from different garments therefore live
in the **same index space**, so no bone remapping is needed. `MeshBuilder` would need to concatenate
`ix` and `w` alongside `p`/`n`/`ta`/`u0`/`u1`, and assert that `bones` matches across parts. That is
a modest addition, not a rewrite.

**Unverified:** the `sfs` property (4 floats) on worn garments. Unknown meaning. Find out before
assuming it can be dropped or copied from one part.

### What I would do first

1. **Prove the render path before touching geometry.** Emit one portrait modifier + accessory for a
   *single* hand-picked vanilla garment, gated on an equipped armour artifact, and confirm it
   appears. That is text only and isolates the mechanism from the assembly problem.
2. **Then check what "parts" even means for armour.** A cuirass, pauldrons and a skirt are plausible
   slots, but unlike a weapon they are not stacked along one axis, so the socket/chain model may not
   transfer. It may be simpler to *swap whole garments* and rely on recolouring for variety —
   worn garments already carry `map1` + `map2`, so palette recolouring should work with no new UVs.
3. **Only then consider assembly.** Skinned merging is tractable per above, but a seam between two
   armour pieces on a deforming body is a far harder visual problem than a seam on a rigid sword.

Honest assessment: **recolouring existing armour is probably 80% of the visible payoff for 20% of
the work**, because armour already has UV2 and the variation system is designed for exactly this.
Part-assembly is the ambitious version and should follow, not lead.

---

## 8. Open items

* **Spear and hammer have no parts library** — they fall back to the vanilla catalogue. Vanilla has
  4 spear and 2 hammer portrait entities, so both are thin.
* **`two_handed_hammer` pack carries no additives at all** — no fat, dwarf, or
  `female_prop_fix_additive`, unlike every other pack we use. Untested territory for prop placement
  on female, fat, and dwarf rulers. The spear was fixed by moving to a pack that has them; the
  hammer has no such alternative.
* **Output size**: 19 MB of meshes/masks + 39 MB of icons per world, because `DdsWriter` writes no
  block compression. Generating icons only for famed and illustrious artifacts would cut most of it
  and arguably reads better anyway.
* **`ep2_northern_mace_01_b`** is a byte-identical re-skin of `_01_a`, differing only in diffuse.
  Kept deliberately — with recolouring it is still a distinct look — but it is a candidate to prune.
* **Cross-kind mixing is not reachable** (an axe head on a sword grip). Mechanically possible;
  blocked because artifact `type` decides both the inventory slot and which idle pose fires.
