# CK3 ground shading investigation

Inspected the local CK3 1.20.0.3 installation on 2026-10-04. This is a design note;
the GPU conversion's presentation has not been changed by this investigation.

## What the preview currently misses

`AppGUI/Map/GroundPreview.cs` produces a baked, unlit RGB image. It reads diffuse
textures and the red property channel for colormap blending, but discards detail
normals and the remaining material properties. It downsamples texture tiles and
caps the image width at 4096. Zooming therefore magnifies that baked image instead
of sampling the original materials at the current screen resolution.

`AppGUI/Map/HeightfieldGpu.hlsl` lights that image with a fixed Lambert term and
adds a simple distance haze. Water retains the drape's flat RGB color. Its output
is quantized to bytes before the supersampling resolve.

## Installed sources and assets

Paths below are relative to the CK3 installation root, which contains `game`,
`clausewitz`, and `jomini`. Shader includes must resolve across all three trees,
with the mod and game overrides taking precedence over engine defaults.

| Source | What it supplies |
| --- | --- |
| `clausewitz/gfx/FX/cw/pdxterrain.fxh` | Terrain height normals, four-material detail blending, material UVs and offsets, packed detail-normal decoding. |
| `game/gfx/FX/pdxterrain.shader` | Combines height and detail normals; blends colormap; feeds material alpha/green/blue into roughness/specular/metalness. |
| `game/gfx/FX/cw/lighting_util.fxh` | Material-property conversion and sun-direction helpers. |
| `game/gfx/FX/jomini/map_lighting.fxh` | Separate sunny/overcast lighting, diffuse and specular illumination, environment-map lighting. |
| `game/gfx/map/environment/environment.txt` | Sun configuration, cubemaps, fog, exposure, contrast, tone mapper, bloom and depth-of-field settings. |
| `game/gfx/FX/jomini/jomini_water_default.fxh` | Three wave-normal layers plus flow normals, foam, sun highlights, refraction, environment reflection, Fresnel and shoreline waves. |
| `jomini/gfx/FX/jomini/jomini_water.fxh` | Engine water helpers included by the game's water shader. |
| `game/gfx/map/water/water.settings` | Water texture paths, colors, gloss, foam, Fresnel and wave settings. |
| `game/gfx/FX/jomini/posteffect_base.fxh` | Exposure/color pipeline and tone-mapping functions. |
| `jomini/gfx/FX/jomini/post_effect/tony_mc_mapface_2d.dds` | The actual tone-mapping lookup texture: 2304 × 48, legacy DDS format 113 (half-float RGBA). |

The inspected terrain normal texture is BC3/DXT5. Terrain and water environment
cubemaps are BC1/DXT1, with six faces. Our DDS reader currently decodes only the
top mip of ordinary BC1/BC3/32-bit textures into bytes; it needs explicit cubemap,
mipmap and floating-point support, or direct DDS uploads to Direct3D textures.

The generator already emits world-specific water color, foam and flow maps in
`Emit/Map/MapGraphicsWriter.cs`. These should override vanilla's maps in the
preview, because vanilla maps describe a different coastline.

## What the local shader cache can contribute

`Documents/Paradox Interactive/Crusader Kings III/shadercache/dx11` contains both
pixel and vertex shader caches. Inspected `.bin` files begin with a standard DXBC
header and retain an RDEF reflection chunk containing resource/constant names.

A bounded scan of 165 pixel shader binaries found water and terrain candidates:

- `00000000016A7C78.bin`: water color, ambient normals and flow-normal textures.
- `00000000028B1666.bin` and `0000000006DD3F36.bin`: detail index/mask/diffuse/normal
  textures, sunny/shadow terrain cubemaps and shadow maps.

These names identify candidates, not their complete effect/define combinations.
Shader reflection and disassembly can cross-check register bindings and shader
arithmetic. Cache entries may reflect previous patches, mods, or graphics options;
readable installed source is the better starting point.

The cached shaders are vertex/pixel programs, while our terrain is a compute
program. They cannot be substituted directly. Executing them would require the
appropriate graphics pipeline, matching constant buffers, textures, samplers,
input geometry and auxiliary render passes. Porting their relevant source
functions into our compute path avoids reproducing the entire game pipeline.

## Recommended implementation order

1. Load a terrain material set alongside the existing RGB drape: detail indices,
   masks, original diffuse/normal/property textures, colormap and terrain settings.
   Evaluate materials in the GPU at the current zoom, with filtering and mipmaps.
2. Port CK3's detail-normal decoding/blending and combine it with the height normal.
   Use the written world's normal scale, step size, tile factors and offsets.
3. Port sunny terrain lighting and environment illumination. Start from CK3's
   defaults; offer sun azimuth/elevation overrides with a reset to those defaults.
4. Keep lit color in linear floating-point HDR through lighting, fog, exposure,
   contrast and CK3's actual tone-mapping lookup. Convert to display bytes at the
   end. Adding only a stronger sun to the current byte RGB would miss this pipeline.
5. Port water shading using the generated color/foam/flow maps and installed wave
   normals/cubemap. Start at a fixed animation time for reproducible comparisons;
   add animation later. Refraction needs an underwater render or a documented
   approximation; full terrain shadows need an additional shadow calculation.
6. Add shadows, clouds and seasonal snow after the clear-weather material/water
   baseline is verified. Match the same game camera, weather, season and graphics
   settings when comparing against CK3 screenshots.

Normal maps, material lighting, cubemap water reflection and tone mapping can all
be added to the current compute renderer without replacing the camera or slab
geometry. Exact shadows and refraction require more work. Preserve the existing
CPU renderer and current shading path as fallbacks when enhanced assets or GPU
features are unavailable.

Validate intermediate outputs separately (base color, height/detail normals,
roughness, lighting before tone mapping, final color). Compare fixed views of a
written world, including coasts, drylands, forest and snow-covered mountains.
Offline comparison checks consistency; an in-game comparison is needed to claim
visual fidelity.
