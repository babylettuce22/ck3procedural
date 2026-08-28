# Authoring new weapon parts — requirements

For the Blender side. The generator assembles weapons from these parts in C# at generation time,
with no Blender involved, so **naming is the entire contract**. Nothing needs changing on the code
side when you add parts to an existing `.mesh` — but a name that doesn't match is silently ignored
rather than flagged.

Libraries: `sword_parts.mesh`, `dagger_parts.mesh`, `axe_parts.mesh`, `mace_parts.mesh`.

---

## 1. Naming

**Shapes:** `<family>_<slot>Shape` — e.g. `ep1_western_sword_01_a_bladeShape`.

The family is *literally everything before* `_<slot>Shape`, so all parts of one weapon must share a
byte-identical prefix. Suffix matching is case-insensitive. A shape matching no slot is skipped
without comment.

**A shape's name comes from `obj.data.name` (the mesh datablock), not the object name.** Locators
use the object name. These are easy to get out of sync.

| kind | slots | anchor (held) | lead (business end) |
|---|---|---|---|
| sword, dagger | `blade` `guard` `grip` `pommel` | `grip` | `blade` |
| axe, mace | `head` `haft`, plus optional `cap` | `haft` | `head` |

**A family does not need every slot.** It is judged against the role it is drawn for:

* to donate the **lead** (`blade` / `head`) it needs only that one part — a blade cut from a weapon
  whose hilt was not worth keeping is a perfectly good donor on its own;
* to supply the **body** it needs every slot except the lead, including the anchor.

A family that can do neither contributes to nothing, and the generator names it. `cap` is genuinely
optional either way — half the vanilla axes have no butt cap.

**Locators:** `<family>_<slot>_socket_<target>` — the slot is the part it belongs to, the target is
what it mates with. `west01_grip_socket_guard` sits on the grip and mates onto the guard.

Required sockets, one per side of every join:

```
bladed   grip:   socket_guard, socket_pommel
         guard:  socket_grip,  socket_blade
         pommel: socket_grip
         blade:  socket_guard

hafted   haft:   socket_head,  socket_cap        (socket_cap only if the family has a cap)
         head:   socket_haft
         cap:    socket_haft
```

**Parenting does not survive export.** No parent/transform data is written, so the *name* is the only
thing binding a socket to its part. Renaming a part means renaming its locators.

---

## 2. Where a socket goes

A socket marks **where the two parts physically meet**, and mating pairs must be authored at the
**same point in space** — self-reassembly has to be a zero translation. Alignment uses all three
axes, so sideways placement matters as much as height.

For a part the anchor passes *through* — an axe head, a mace head — that point is **the anchor's axis
at the join height**, not the centroid of the part's cross-section. Those coincide on a sword guard,
which is why the centroid rule worked at first and then put axe head sockets up to 15.6 units
off-axis, leaving mixed axes floating.

Self-rebuild cannot catch this, because both sides of a pair are wrong by the same amount. The check
that does: **the lateral distance from a socket to the anchor's axis should be small.**

**Axis:** in *file* space the long axis is **Z**, tip at negative Z. Blender shows it as Y because
io_pdx_mesh swaps on import: `file = (blender.x, blender.z, blender.y)`.

---

## 3. Export settings

- **`chk_locs = True`** or no locators are written at all, and every part then fails validation.
- `chk_selected = True` filters locators too — easy to lose them this way.
- io_pdx_mesh **0.91.0** with the three local patches stashed at `C:\Users\caelo\ck3-modding-tools\`
  (see its README). Re-apply after any addon self-update.

---

## 4. Hard requirements

**Every part needs all three textures.** `diff`, `n` and `spec` must all be non-empty or the whole
family is dropped. The exporter's slot mapping is counter-intuitive:

```
Base Color -> diff      Roughness -> spec      Normal -> n
```

Only the basename is exported and CK3 resolves textures globally by filename, so nothing needs
copying next to the mesh. But a mesh naming a texture the importer couldn't find exports an **empty**
string, and an untextured weapon renders as a hole punched through the character.

**Every part needs UV2.** This one is worth care: the check is **batch-wide**, so a *single* part
missing its second UV map disables recolouring for all 32 generated weapons and their rendered
icons. It prints a line saying so, but the damage is far wider than the cause.

UV2 is a projection for tiling material swatches, not an atlas — even texel density matters, packing
does not. All four libraries use a world-space box projection at **20.0 world units per 1.0 UV**, so
they can share one layout. Keep new parts at that scale. (The exporter flips V: `file_v = 1 - blender_v`.)

**No degenerate parts.** A 2-face sliver posing as a guard passes every check and looks broken.
Exclude it when cutting.

---

## 5. Two things that have cost real time

**Don't let a haft flare where the head mounts.** `ep1_indian_mace_01_a` is a cone that widens toward
its head socket — 4.25 units of radius at the join against a library median of 1.68 — so any foreign
head, bored for a shaft half that thick, sits on it visibly floating. It's now restricted to
self-only in code. If a new haft measures much over twice the library median at its own
`socket_head`, expect the same and say so.

**Sockets carry an intrinsic offset if the part wasn't zeroed.** The axe and mace libraries were
re-exported once to fix exactly this; sockets moved 3–8 units. Worth a sanity check that a
family reassembles onto itself unchanged.

---

## 6. What "done" looks like

After exporting, the generator run prints one line per library. A new family is working when it
appears in forged weapons without any of these:

Only one thing still stops the run outright:

- `no <anchor> anywhere in the library` — *nothing* has the held part, so no weapon can be
  assembled. A single family without one is fine and no longer an error; it simply cannot supply a
  body.

The rest are dropped with a named message and the run continues:

- `dropped N part(s) with no sockets: <part>` — locators missing or misnamed. **This is the one to
  watch**: the part is silently absent from every weapon afterwards, so the line is the only sign.
- `<family> contribute(s) nothing` — neither a lead to donate nor a complete body
- `skipped ... no textures on <family>` — one of diff/n/spec is empty
- `parts library has no UV2` — batch-wide, kills all recolouring

A lead-only family that is working announces itself:

```
forged weapons: sword lead-only (blade donors, no body) — fp3_sassanian_sword_01_a
```

Ping the code side after exporting and the join-radius measurement can be re-run across all four
libraries in about a minute.
