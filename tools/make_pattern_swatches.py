"""Authors the flat pattern swatches the weapon forge tints against.

Run once, by hand, when the numbers below change:

    python tools/make_pattern_swatches.py

Output goes to BaseFilesToCopy, not to the generated mod, because these are fixed assets
rather than per-world ones - there is no reason to rewrite nine identical files on every
generation run. CK3 resolves textures globally by filename, so they need no registration
beyond the pattern_textures block ForgedWeaponRecolour emits.

WHY AUTHOR THEM AT ALL
----------------------
A pattern swatch is three textures and nothing else - colormask, normal, properties - so
this needs no mesh, no Blender, and no addon. The properties texture is the whole point:
per cw/lighting_util.fxh the engine reads it as

    GetMaterialProperties( diffuse, normal,
        roughness = Properties.a,  spec = Properties.g,  metalness = Properties.b )

and portrait_accessory_variation.fxh additionally does `Diffuse.rgb *= Properties.rrr`,
so Properties.r is ambient occlusion. Vanilla's nearest metal swatch, statue/gold_plain_01,
measures AO 1.00 / metal 1.00 / roughness 0.40. Authoring our own lets us pull metalness off
1.0, which matters because a fully metallic surface has NO diffuse response at all - it is
visible only as reflected light, which is why forged blades read as near-black in dim portrait
scenes. It also lets metalness VARY, which turned out to be the axis the eye actually reads:
see the note on gen_iron_rough.

FORMAT
------
DXT5, matching every vanilla swatch, because these are loaded into a texture array and a
lone uncompressed entry is a risk not worth taking for files that are one flat colour.
Encoding a constant colour to DXT5 is exact and needs no compressor: both endpoints get
the same value and every index is zero.

NORMALS ARE NOT RGB
-------------------
texture_decals_base.fxh unpacks them as `Normal.xy = NormalSample.ga * 2 - 1`, i.e. X in
GREEN and Y in ALPHA - the DXT5nm convention. A flat normal is (128, 128, 255, 128), not
the (128, 128, 255, 255) you would write for an ordinary tangent-space map. Blue is unused
here except as opacity when a layout asks for it, so it is left at 255 = fully opaque.

Note the swatch normal REPLACES the model's own normal wherever the mask applies - the
shader assigns rather than blends. Flat is therefore a deliberate choice: it gives a
polished surface. A grain pattern here would read as brushed metal instead.
"""

import os
import struct

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(
    HERE, '..', 'BaseFilesToCopy', 'Core', 'gfx', 'portraits',
    'accessory_variations', 'textures', 'patterns', 'gen')

SIZE = 512

# Colormask fires red only, exactly as every vanilla swatch does. The palette column the
# shader reads is MaskIndex * 4 + <colormask channel>, so a red-only swatch means our four
# groups land on columns 0, 4, 8 and 12.
COLORMASK = (255, 0, 0, 0)

# X in green, Y in alpha, both neutral at 128. See NORMALS ARE NOT RGB above.
FLAT_NORMAL = (128, 128, 255, 128)

# (AO, spec, metalness, roughness) as bytes -> written as R, G, B, A, and how much the
# roughness varies across the surface.
#
# JITTER IS WHY THINGS STOP LOOKING LIKE PLASTIC. A single flat roughness value is not a
# material, it is a mathematical ideal: nothing real is uniformly glossy, and the eye reads
# perfect uniformity as cheap. It matters most on the SHINY surfaces, which is the opposite of
# what you would guess - a matte cloth hides its own uniformity, a mirror advertises it. It
# matters more again here because these swatches land on cloth-shaped meshes, where a flawless
# specular over a garment silhouette is the exact thing that looks wrong.
SWATCHES = {
    # The middle rung of the metal ladder, and the weapon forge's only metal.
    #
    # Metalness 0.72 rather than near-1: a fully metallic surface has NO diffuse response and is
    # visible only as what it reflects, which is why the first forged blades read as near-black
    # in dim scenes. Roughness 0.40 matches vanilla's own metal swatches exactly - the earlier
    # 0.20 was twice as glossy as anything the game ships and looked it.
    'gen_steel': (255, 0, 140, 92, 0.12),

    # AO 242 rather than vanilla leather's 191: that swatch was quietly costing every grip a
    # quarter of its light, on top of an already dark palette tint. Spec and roughness are
    # kept near vanilla's, which looked right for leather; metalness stays 0.
    'gen_leather': (242, 122, 0, 122, 0.16),

    # ---- surfaces for armour ------------------------------------------------------------
    # The palette says what colour a region is; the swatch says what it is MADE of, because
    # roughness and metalness live here rather than in the palette. Two regions can share a
    # colour and still read as different substances, which is the whole point: a brigandine is
    # dark leather with bright rivets, and the rivets are not merely a lighter brown.
    #
    # Roughness is the axis that carries most of it. Vanilla's own metal swatches sit at 0.40;
    # these span 0.22 to 0.82, which is the difference between a polished helm and felt.
    #
    # CALIBRATED AGAINST VANILLA, not chosen freely. Vanilla's metal swatches all sit at 0.40.
    # The first pass here put steel at 0.20 - twice as glossy as anything the game ships - and
    # it read as too shiny in exactly the place it would: over cloth-shaped garment meshes,
    # where a flawless specular on a fabric silhouette is the giveaway. Everything moved toward
    # 0.40 while keeping the ORDER intact, so the surfaces still separate; the shiniest is now a
    # polished helm rather than a mirror.

    # Mirror-bright plate. The lowest roughness here, and near-full metalness - a tight
    # specular that throws the portrait's key light straight back.
    'gen_steel_polished': (255, 0, 166, 46, 0.14),

    # Dark, worked iron: mail, and anything meant to look forged rather than finished.
    #
    # METALNESS CARRIES THE LADDER, NOT ROUGHNESS. The first attempt moved only roughness
    # across the three metal rungs and held metalness at 0.86-0.94 throughout, and in game the
    # rarities were indistinguishable while the TYPES read clearly. The reason is that the type
    # differences cross the metal/non-metal boundary - cloth at metalness 0 against polished at
    # 0.94 - whereas the rarity rungs did not move it at all. A near-fully-metallic surface has
    # almost no diffuse response, so it is lit entirely by what it reflects, and under a soft
    # portrait light a roughness change on a dark garment is a weak signal.
    #
    # But metalness has a CEILING in this renderer, and 0.95 is well past it. A metal has no
    # diffuse response at all - it shows only what it reflects - and CK3's portrait lighting
    # gives it very little to reflect. Pushed to 0.74 and 0.97 the middle and upper rungs came
    # out DARKER and flatter than the rough one below them, which inverted the ladder: common
    # read as bright, masterwork as flat, and only the palette ramp rescued illustrious.
    #
    # So metalness now spans a narrow 0.45 to 0.65 - enough to separate worked iron from
    # finished steel, not enough to drop the surface into "black mirror" - and ROUGHNESS does
    # the work, falling 0.62 to 0.18 across the rungs. Brightness climbs with rarity instead of
    # dipping in the middle, which is what the eye was actually reading all along.
    'gen_iron_rough': (250, 0, 115, 158, 0.20),

    # Padded cloth - gambesons, surcoats, the quilted layer under everything. No metalness at
    # all, very high roughness. This is the one that stops fabric reading as painted metal.
    'gen_cloth': (240, 96, 0, 209, 0.12),

    # Lacquered and glossy, but NOT metal: eastern lamellar, painted scale. Low roughness with
    # zero metalness is a combination that has no vanilla equivalent among the statue swatches.
    'gen_lacquer': (250, 150, 0, 66, 0.13),
}


def rgb565(r, g, b):
    return ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3)


def dxt5_block(r, g, b, a):
    """One 4x4 DXT5 block of a single constant RGBA value."""
    c = rgb565(r, g, b)
    block = struct.pack('<BB6s', a, a, b'\x00' * 6) + struct.pack('<HH4s', c, c, b'\x00' * 4)
    assert len(block) == 16
    return block


def alpha_block(values):
    """A DXT5 alpha block carrying 16 real per-texel values.

    This is the only part of the format that can hold detail cheaply. The colour block has two
    RGB565 endpoints shared by all 16 texels, but the ALPHA block has its own pair of 8-bit
    endpoints plus a 3-bit index per texel - eight interpolated levels, encoded exactly.

    Roughness lives in alpha, which is the useful coincidence here: roughness is precisely the
    channel that needs to vary. A single flat value across a whole surface is what makes a
    material read as plastic, because nothing real is uniformly glossy.
    """
    lo, hi = int(min(values)), int(max(values))

    if hi == lo:
        return struct.pack('<BB6s', hi, lo, b'\x00' * 6)

    # Code order for a0 > a1: 0 -> a0, 1 -> a1, then 2..7 walking a0 down to a1.
    table = [hi, lo] + [((6 - i) * hi + (1 + i) * lo) // 7 for i in range(6)]
    bits = 0
    for t, v in enumerate(values):
        best = min(range(8), key=lambda c: (v - table[c]) ** 2)
        bits |= best << (t * 3)

    return struct.pack('<BB', hi, lo) + bits.to_bytes(6, 'little')


def tileable_noise(size, octaves=3, seed=7):
    """Value noise that wraps, so the swatch tiles without a seam.

    Built by upsampling small random lattices with np.roll-free wrapping - each octave is a
    coarse grid resampled to full size with periodic edges, so opposite edges agree by
    construction rather than by blending.
    """
    rng = np.random.default_rng(seed)
    out = np.zeros((size, size), dtype=np.float64)
    amp, total = 1.0, 0.0

    for o in range(octaves):
        n = 4 << o                                  # 4, 8, 16 ... lattice points, divides size
        g = rng.random((n, n))
        # bilinear upsample with wraparound
        ys = np.arange(size) * n / size
        y0 = np.floor(ys).astype(int) % n
        y1 = (y0 + 1) % n
        fy = (ys - np.floor(ys))[:, None]
        a = g[y0][:, y0] * (1 - fy) + g[y1][:, y0] * fy
        b = g[y0][:, y1] * (1 - fy) + g[y1][:, y1] * fy
        fx = (ys - np.floor(ys))[None, :]
        out += amp * (a * (1 - fx) + b * fx)
        total += amp
        amp *= 0.5

    out /= total
    return (out - out.min()) / max(out.max() - out.min(), 1e-9)


def dxt5_with_mips(r, g, b, a, width, height, jitter=0):
    """The full mip chain. Colour is constant; roughness (alpha) may carry noise.

    MIPS ARE NOT OPTIONAL. These swatches are sampled as a Texture2DArray and every slice must
    agree on dimensions AND mip count. Every vanilla pattern texture is 512x512 DXT5 with 10
    levels; a single-level slice makes the engine read past the end of the buffer looking for
    level 1, which crashes to desktop during asset load with no crash reporter at all.
    """
    colour = dxt5_block(r, g, b, a)[8:]             # the colour half, constant for every block
    field = None

    if jitter > 0:
        field = tileable_noise(width)
        field = np.clip(a + (field - 0.5) * 2.0 * jitter * 255.0, 0, 255)

    data = bytearray()
    w, h = width, height
    level = field

    while True:
        bw, bh = max(1, (w + 3) // 4), max(1, (h + 3) // 4)

        if level is None:
            data += (struct.pack('<BB6s', a, a, b'\x00' * 6) + colour) * (bw * bh)
        else:
            for by in range(bh):
                for bx in range(bw):
                    tile = level[by * 4:by * 4 + 4, bx * 4:bx * 4 + 4]
                    vals = [int(tile[min(y, tile.shape[0] - 1), min(x, tile.shape[1] - 1)])
                            for y in range(4) for x in range(4)]
                    data += alpha_block(vals) + colour

        if w == 1 and h == 1:
            break

        w, h = max(1, w // 2), max(1, h // 2)
        if level is not None:
            # box-downsample the roughness so each mip stays the average of what it replaced
            cut = level[:level.shape[0] // 2 * 2, :level.shape[1] // 2 * 2]
            level = cut.reshape(max(1, cut.shape[0] // 2), 2, max(1, cut.shape[1] // 2), 2).mean(axis=(1, 3))                 if cut.size else level

    return bytes(data), mip_count(width, height)


def mip_count(width, height):
    n, w, h = 1, width, height
    while w > 1 or h > 1:
        w = max(1, w // 2)
        h = max(1, h // 2)
        n += 1
    return n


def dds_header(width, height, top_level_size, mips):
    """The fixed 128-byte DDS header: 4 magic + 124 of DDS_HEADER.

    Every field is matched to what vanilla's pattern swatches carry, verified by dumping theirs:
    flags 0xa1007, caps1 0x401008, mips 10, pitch = the TOP level's byte size only.

    Laid out explicitly because the tail is easy to get wrong - after the 44-byte reserved block
    come the pixel format's 8 dwords AND five more (caps1-4 plus reserved2). Stopping at caps2
    leaves the file 8 bytes short, which readers report as a truncated image rather than a bad
    header.
    """
    header = struct.pack(
        '<4sIIIIIII44sIIIIIIIIIIIII',
        b'DDS ',
        124,                                                    # dwSize
        0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000,           # + MIPMAPCOUNT | LINEARSIZE
        height, width,
        top_level_size,                                         # dwPitchOrLinearSize: top level only
        0,                                                      # dwDepth
        mips,                                                   # dwMipMapCount
        b'\x00' * 44,                                           # dwReserved1[11]
        32,                                                     # ddspf.dwSize
        0x4,                                                    # ddspf.dwFlags = DDPF_FOURCC
        int.from_bytes(b'DXT5', 'little'),
        0, 0, 0, 0, 0,                                          # bit count and masks, unused
        0x8 | 0x1000 | 0x400000,                                # COMPLEX | TEXTURE | MIPMAP
        0, 0, 0,                                                # dwCaps2..4
        0)                                                      # dwReserved2
    assert len(header) == 128, len(header)
    return header


def write(path, rgba, jitter=0):
    data, mips = dxt5_with_mips(*rgba, SIZE, SIZE, jitter)
    top = max(1, SIZE // 4) * max(1, SIZE // 4) * 16
    with open(path, 'wb') as f:
        f.write(dds_header(SIZE, SIZE, top, mips))
        f.write(data)
    return len(data) + 128, mips


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, (*props, jitter) in SWATCHES.items():
        for suffix, rgba in (('masks', COLORMASK), ('normal', FLAT_NORMAL), ('properties', tuple(props))):
            path = os.path.join(OUT, f'{name}_{suffix}.dds')
            # Only the properties map carries roughness, so only it gets the variation.
            size, mips = write(path, rgba, jitter if suffix == 'properties' else 0)
            # 349680 is what every vanilla 512x512 DXT5 pattern swatch weighs. Anything else means
            # the mip chain is wrong, and a wrong mip chain here is a crash, not a visual bug.
            ok = 'ok' if jitter == 0 or suffix != 'properties' else f'ok, jitter {jitter}'
            flag = ok if size == 349680 else '!! expected 349680'
            print(f'  {os.path.basename(path):34s} {SIZE}x{SIZE} DXT5 mips={mips} '
                  f'{size:>7} bytes {flag}  rgba={rgba}')


if __name__ == '__main__':
    main()
