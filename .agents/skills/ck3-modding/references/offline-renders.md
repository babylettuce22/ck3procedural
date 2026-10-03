# Offline renders: ground and portraits without launching the game

Two local tools in `C:\Users\caelo\Desktop\ck3devtools\` rebuild what the engine draws from the
same game and mod files. Use them to judge a visual change (terrain palette, race genes, portrait
modifiers) before asking the user for a game launch. Each tool has a README with every option.

Both layer a mod folder over `<game>` the way the engine does. Point them at a generated mod,
usually verify.sh's last world `..\verify-runs\<config>\mod` (see the `mod-verify` skill), or at
`<mods>\proceduralmap`.

**What they are not:** an oracle. Lighting and shaders are simplified. They answer "did the shape
or pattern change the way I meant", not "does it look right in game". Once a change looks right
offline, the user still confirms it in game.

## TerrainRender: map ground

`ck3devtools\TerrainRender\terrain_render.py`, about 15 s a panel.

```
python terrain_render.py vanilla <mod> [<mod> ...] [--biome drylands] [--at X,Y] [--size 256x192]
```

- **What it reproduces.** The low-spec detail blend of `pdxterrain.shader`: 2×2 index-matched
  mask accumulation, the `smoothstep(0, 0.1)` presence cut, height blend on the diffuse alpha,
  per-material `tile_factor`, and colormap soft-light. It reads `detail_index.tga`,
  `detail_intensity.tga`, `colormap.dds` and `heightmap.png`, with the game's own detail textures.
- **When to use it.** For any change to `TerrainPalette` or `TerrainTextureWriter`, render
  vanilla, before and after side by side. The README has the before/after recipe using
  `verify.sh --snap`.
- **Limits.** The lighting is a plain sun, so judge pattern and colour only. It matched the
  in-game drylands screenshot (2026-09-29).
- **In the app.** The generator has the same blend as the "CK3 ground" map mode
  (`AppGUI/Map/GroundPreview.cs`).

## portrait_render: character heads

`ck3devtools\portrait_render\portrait.py`, about 1 s for a sheet of a dozen heads once the parse
cache is warm. The first run after the game, mod or tool changes takes 2–4 s.

```
python portrait.py --mod <mod> --race giantkin --vanilla-row          # women + men rows + neutral heads
python portrait.py --mod <mod> --race giantkin --sex female --set gene_jaw_width=jaw_width_pos:0.75
python portrait.py --mod <mod> --character <history id>               # culture -> ethnicity, sex, traits, age
python portrait.py --dna 163112_halfdan_whiteshirt --date 867          # exact vanilla DNA (common/dna_data)
python portrait.py --mod <mod> --list                                  # races, their traits and ethnicities
```

The sheet is written to `out/sheet.png` unless you pass `--out`. Faces are seeded (`--seed`), so
the same command always draws the same heads. `--set` overrides a gene after the portrait
modifiers, which lets you try a tuning value without regenerating.

**The chain it replicates.** Any of these steps is worth knowing when debugging a portrait:

1. **DNA.** Comes from one of three places:
   - an ethnicity: the `template =` chain resolved (the child's gene block replaces the
     parent's), then a weighted entry per gene, with the value uniform in its `range`;
   - `common/dna_data`: the first allele of each pair is the one shown, stored as 0–255;
   - a history character, whose culture's `ethnicities` pick the ethnicity.
2. **Portrait modifiers.** Groups apply in `priority` order, one entry per group
   (`selection_behavior = max` picks the highest weight, otherwise it's weighted random).
   - A `mode = replace` morph sets the gene's template and value. A `mode = add` morph stacks a
     second copy of the gene.
   - Modifiers any higher priority than a `replace` override it.
   - The tool evaluates `has_trait`, `is_female`, `exists`, AND/OR/NOT/NOR. Any other condition
     never fires, even under NOT.
   - `modify` morphs driven by game state (weight, muscularity) are not modelled.
3. **Gene to attribute.** The template's `male`/`female` block is used (`female = male` is an
   alias). Each `setting` gives an attribute value:
   - `value = { min max }` is lerped by the gene value, `curve` is piecewise, and a bare
     `value = N` is constant.
   - Then it is multiplied by (or has added) its `age` preset's curve at x = age/100.
   - Settings with `required_tags` are skipped, since they need an accessory tag such as an earring.
   - The same attribute from several genes adds up.
4. **Attribute to geometry.** The head `.asset`'s **`entity`** block (not the `pdxmesh` block)
   maps each attribute to one of two things:
   - A **blend shape**: a full-position `.mesh`, aligned vertex for vertex with the head. The
     attribute value is its weight, with 0 as neutral.
   - An **additive animation**: an `.anim` with 11 samples spanning attribute 0..1, `default = 0.5`.
     This is how the slider genes work (jaw_width, chin_width, head_width, neck_width, …): they
     move bones, not blend shapes. Each bone's offset from the default sample is applied in its
     parent's space, then the head is linear-blend skinned. The mesh's skeleton `tx` is the
     inverse bind matrix.
   - `.mesh` and `.anim` share the `@@b@` binary container, and quaternions are stored xyzw.

**Not modelled:** hair, beards, clothes, horns and other accessories, eyes and teeth, skin
colour and textures, decals (wrinkles, complexion), and the portrait shader and lighting.
Judge face shape only.

**Worth remembering:** vanilla `gene_jaw_width` gives women the full male range ("Female uses
full range"), so a race that pushes the jaw looks worst on its women. Render women and men
separately before and after any change to jaw, chin or brow.

**Fidelity vs the game: unconfirmed.** The planned check is to render
`--dna 163112_halfdan_whiteshirt --date 867` and compare it with Halfdan Whiteshirt's portrait on
the 867 bookmark. Until the user has done that comparison, say "offline render" rather than
"this is how it looks".
