# Handoff: mace_parts.mesh

Produced 2026-08-26. Measured on this machine except where marked **DECISION** — those are
choices for you.

Blend: `C:\Users\caelo\Documents\Blender\ck3\ck3_artifact_maces.blend`
Output: `assets/weaponparts/mace_parts.mesh` — 1,780,679 bytes

Nothing else touched. `sword_parts.mesh`, `dagger_parts.mesh`, `axe_parts.mesh` unchanged.

---

## 1. What's in the file

**27 shapes, 34 locators, 10 families.** Verified by re-parsing the binary.

Every shape has `map1` + `map2` and all three textures. Every family reassembles from its own
sockets with drift **exactly 0.0**, read back from the file.

Same `head` / `haft` / `cap` schema as axes, haft as anchor, cap optional. No new conventions.

| family | slots | head | haft | cap |
|---|---|---|---|---|
| ep1_african_mace_01_a | 3 | 1118 | 48 | 1242 |
| ep1_byzantine_mace_01_a | 3 | 544 | 232 | 216 |
| ep1_indian_mace_01_a | 3 | 508 | 360 | 108 |
| ep1_steppe_mace_01_a | 3 | 496 | 367 | 238 |
| ep1_western_mace_01_a | 3 | 624 | 336 | 108 |
| ep1_mena_hammer_01_a | 3 | 196 | 66 | 42 |
| ep1_western_hammer_01_a | 3 | 122 | 28 | 10 |
| ep1_mena_mace_01_a | **2** | 156 | 348 | — |
| ep2_northern_mace_01_a | **2** | 380 | 784 | — |
| ep2_northern_mace_01_b | **2** | 380 | 784 | — |

Three capless families, same situation as the axes (4 of 8 there). Whatever you decide for axes
applies here unchanged.

**Two of the ten are hammers**, not maces — `ep1_mena_hammer_01_a` and `ep1_western_hammer_01_a`.
Same three-slot structure, so they cut identically, but a recipe mixing a hammer head onto a mace
haft is a design choice you may or may not want reachable.

---

## 2. The socket correction was applied from the start, and it mattered

You warned this would bite harder on maces than axes. Applying the corrected rule up front —
cross-section from the **haft** (the axial member), never from the head — gives:

**Socket-axis check: worst off-axis 0.804, CLEAN. 13 of 17 joins are exactly 0.000.**

For comparison, all four libraries now:

    swords   worst 0.088   CLEAN
    daggers  worst 0.486   CLEAN
    axes     worst 1.113   CLEAN
    maces    worst 0.804   CLEAN

The two non-zero mace joins are both legitimate: `ep1_steppe_mace_01_a` cap↔haft at 0.804 and
`ep1_byzantine_mace_01_a` haft↔head at 0.455, in both cases because the haft is slightly offset
or tapered at that height, not because the socket is measuring bulk.

Socket positions (Blender space; **file Z = Blender Y**):

| family | haft↔head (x, y, z) | haft↔cap (x, y, z) |
|---|---|---|
| ep1_african_mace_01_a | −0.01586, −48.01735, −0.03450 | −0.00022, 22.15217, −0.06390 |
| ep1_byzantine_mace_01_a | −1.15519, −46.57393, −0.01539 | 0.73566, 10.46305, 0.01016 |
| ep1_indian_mace_01_a | 0, −64.42555, −0.02460 | 0, 4.93701, −0.02459 |
| ep1_steppe_mace_01_a | 0, −35.51023, −0.04319 | 0.80354, 5.92663, 0.27079 |
| ep1_western_mace_01_a | 0, −45.57272, 0 | 0, 8.13811, 0 |
| ep1_mena_hammer_01_a | 0.00204, −35.10454, −0.59956 | 0.00205, 11.92706, −0.59957 |
| ep1_western_hammer_01_a | 0, −37.69086, 0.00037 | 0, 9.93993, 0.00037 |
| ep1_mena_mace_01_a | 0, −52.28140, −0.37264 | — |
| ep2_northern_mace_01_a | 0, −51.10146, 0 | — |
| ep2_northern_mace_01_b | 0, −51.10146, 0 | — |

Note these are all near the axis — contrast the axe head sockets, which legitimately needed
correcting from up to 15.64 off. That is the corrected rule working.

---

## 3. A cut rule that had to be DISABLED for maces

The axe pass added an override: *a long, round-sectioned island is haft regardless of centroid*
(`y_extent > 20 and 0.7 < zw/xw < 1.5`). It was needed because `ep1_mena_axe_01_a`'s haft is
modelled in segments that interleave with head islands.

**That override is unsafe on maces and is off here.** An axe head is Z-wide (aspect 3.5–24.5), so
it could never be mistaken for a haft. A mace head is a *ball or fluted cylinder* at aspect ≈1.0 —
exactly what the override looks for. `ep1_mena_mace_01_a`'s head is 16.9 units long against a
20-unit threshold, i.e. one modelling decision away from being silently swallowed into the haft.

No mace needed it: every haft island's centroid already falls clear of head territory under plain
cut planes. If a future mace does need it, gate it on aspect ratio *of the head*, not just the
candidate island.

---

## 4. Rate of firing — both rules, as predicted

**Overlap cap: 6 of 17 joins.** Raw overlaps 2.07–9.20 against caps of 0.42–2.00. Heaviest on the
hammers and northern maces, where the haft runs well up into the head.

**Bevel-skip: 10 of 17 joins, 8 of 10 families.** Worst cross-section at the raw join plane
**4.8%** (`ep1_mena_hammer_01_a` haft↔cap), then 9.7% (`ep1_steppe_mace_01_a` haft↔cap).
Lower hit-rate than axes (12/12) but the worst cases are just as degenerate.

---

## 5. Per-family notes worth reading before writing recipes

* **`ep1_byzantine_mace_01_a`'s "cap" is a wrist-strap loop**, not a pommel. It is 216 faces
  spanning ~20 units and hangs past the end of the haft. Geometrically fine and it reassembles
  exactly, but bolted onto another mace it will read as a dangling strap rather than a butt cap.
* **`ep1_african_mace_01_a`'s cap is 1242 faces** — larger than its own head (1118) and 26× its
  haft (48). It is a finely ribbed pommel ferrule. Correct, but expensive: any weapon that picks
  it inherits 1242 faces for a small visual element.
* **`ep1_western_hammer_01_a` is very crude** — 160 tris total, parts of 122 / 28 / 10 faces, and
  several haft islands are **zero-width flat cards** (xw = 0.00). It works, but it will look poor
  beside the higher-poly families, and its 10-face cap contributes almost nothing.
* **`ep2_northern_mace_01_b` is a pure re-skin of `_01_a`** — byte-identical geometry (verified by
  vertex hash), *and* identical normal and properties maps. It differs only in
  `ep2_northern_mace_01_b_diffuse.dds`. **DECISION:** with procedural recolouring working, a
  diffuse-only variant may be redundant. Dropping it costs 3 shapes and loses one hand-authored
  colourway. I kept both; say the word and I'll prune.
* Three caps were **folded into their haft** because they were the flat facet closing the haft
  tube rather than a component: `ep1_mena_mace_01_a` (12 faces), `ep2_northern_mace_01_a` and
  `_01_b` (32 faces each, including a zero-thickness end disc). Same call as northern/steppe axes.

---

## 6. Textures

All 10 resolved from their `.asset`, each to its own set, all present on disk. Audit ALL OK for
27/27. All 10 `.asset` files specify `shader = "portrait_attachment"` and every mace mesh carries
it — no split, unlike swords (2 values) and daggers (3).

## 7. UV2

World-space box projection, **SCALE = 20.0**, `map2` second in the layer list — same constant as
the other three libraries. Measured peak density 0.04914–0.05000 against theoretical 0.05, so all
four share one `pattern_layout`.

Reminder: **the exporter flips V** (`file_v = 1 − blender_v`).

## 8. Sources rejected: none

All 10 portrait mace/hammer meshes in the game cut cleanly. Every
`weapons/*_mace_01_a_portrait.mesh` and both hammers are included, plus the two `props/fp4/`
northern variants.

---

## 9. Open questions

1. **Capless families** — still unresolved from the axe handoff; now affects 3 of 10 maces too.
2. **Hammers in the mace library** — same schema, different weapon. Reachable in recipes or not?
3. **`ep2_northern_mace_01_b`** — keep the diffuse-only variant, or prune?
4. **`ep1_byzantine_mace_01_a`'s strap-cap and `ep1_african_mace_01_a`'s 1242-face cap** — both
   valid, both odd when mixed. Worth a recipe-level exclusion rather than a data change.
