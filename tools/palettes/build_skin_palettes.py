"""Builds the mod's custom gfx/portraits/skin_palette.dds and skin_properties_palette.dds.

WHY THIS EXISTS
---------------
CK3 resolves an ethnicity's `skin_color = { w = { x1 y1 x2 y2 } }` rectangle into a UV
lookup in gfx/portraits/skin_palette.dds, which stock CK3 fills with one continuous *human*
gradient. A green orc or an obsidian drow has nowhere to point, so the fantasy races need
pigment painted into that texture.

The catch is that overwriting the wrong pixels silently recolours stock ethnicities, since
they read the same texture. So this script does not guess at what is safe: it parses every
`skin_color` rect out of CK3's own ethnicity files (and any extra mods passed via
--also-scan), rasterises them into a claimed mask, and paints only where nothing claims.
Stock ethnicities keep working untouched, and generated *human* ethnicities inherit their
skin straight from a vanilla template rather than overriding it - see CreateEthnicity in
MapGen/Ethnicities.cs.

It turns out stock only claims about 46% of the palette. The largest free block is the
left-hand column below the midtones, which is what FANTASY_* carves up below.

Two invariants are asserted before anything is written, so a layout edit cannot quietly
break stock portraits:

    1. every pixel a stock rect can sample is byte-identical to the stock texture
    2. every pixel this script paints falls outside every claimed rect

LAYOUT (256x256 - same dimensions as stock, so it is a drop-in replacement)
--------------------------------------------------------------------------
Stacked in the free left-hand block, columns 0..FANTASY_COL1 starting at row FANTASY_ROW0:
eight races, each holding one TIER_ROWS-tall strip per entry in TIERS, race-major. Within a
strip, u interpolates that race's hue stops and v ramps light -> dark.
MapGen/Ethnicities.cs mirrors these constants in its SkinPalette class and derives every
coordinate it emits from them, so the texture and the ethnicities cannot drift apart.
Reordering BANDS or TIERS means reordering BandOf / TierOf there to match.

INTENSITY TIERS
---------------
TIERS is MapConfig's FantasyRaceMode: the same race is painted three times, from
believable to lurid, and an ethnicity samples whichever strip matches the map's setting.
Two knobs separate them.

`realism` blends each stop toward the human tone of *equal luminance*, read out of the
stock gradient itself rather than guessed at - so a low-fantasy orc lands on a real olive
complexion that happens to read green, not on a washed-out green. `chroma` then scales the
re-spread below. Low fantasy also damps how far the material properties drift from stock,
so subtle races keep subtle skin shading too.

MULTIPLY SEMANTICS
------------------
gfx/FX/court_scene.shader:911-921 blends the palette as
    lerp(Diffuse.rgb, Diffuse.rgb * PaletteColor, t)
so the palette *multiplies* the skin diffuse rather than replacing it. Two consequences:

  1. Nothing can be lighter than the bare diffuse - white is the palest skin, which is why
     the stock gradient tops out at pure white. Keep light stops below ~250.
  2. The diffuse is warm (R > G > B), so a merely green-ish RGB comes out olive-tan after
     the multiply. `chroma` below re-spreads a stop's channels away from its brightest one
     to punch back through that warm bias. Orc and Deepkin lean on this hard.

PROPERTIES PALETTE (512x512, 10 mips)
-------------------------------------
Stock skin_properties_palette.dds is uniformly (128,128,128,255) - fully neutral. Channel
meanings, from court_scene.shader:824,848 feeding cw/lighting_util.fxh:60
GetMaterialProperties(diffuse, normal, roughness, spec, metalness):

    R = subsurface scattering mask   G = specular   B = metalness   A = roughness

Neutral is NEUTRAL_PROPS, which is 128 on RGB but 255 on alpha - roughness therefore
deviates downward from 255, not around 128. Only the race strips deviate at all, so stock
ethnicities keep the stock material response exactly, and metalness stays at 128 everywhere
because metallic skin reads as broken rather than fantastical.

USAGE
-----
    python tools/palettes/build_skin_palettes.py

Reads the stock palette and ethnicity files straight from the CK3 install (--game to
override) and writes into BaseFilesToCopy/Core/gfx/portraits/, which StaticFileWriter copies
verbatim into the mod.
"""

from __future__ import annotations

import argparse
import re
import struct
from pathlib import Path

import numpy as np

PALETTE_SIZE = 256

# --------------------------------------------------------------------------------------
# Where the race bands live. Kept in whole pixels because MapGen/Ethnicities.cs mirrors
# these exact integers; floats would drift between the two.
# --------------------------------------------------------------------------------------

FANTASY_COL1 = 73    # last painted column; stock's papuan rect starts at column 76
FANTASY_ROW0 = 105   # first painted row; the rect above ends at row 102
TIER_ROWS = 6
BAND_COUNT = 8

# One strip per FantasyRaceMode, in MapConfig's order. realism = how far each stop is pulled
# toward the stock human tone of equal luminance; chroma = multiplier on a band's own chroma;
# props = how much of the band's material deviation from neutral survives.
TIERS = [
    {"name": "LowFantasy", "realism": 0.62, "chroma": 0.35, "props": 0.35},
    {"name": "HighFantasy", "realism": 0.25, "chroma": 1.00, "props": 1.00},
    {"name": "ExoticSurreal", "realism": 0.00, "chroma": 1.45, "props": 1.40},
]

# stops: (u position, lightest RGB, darkest RGB). u must ascend and span 0.0 .. 1.0.
# chroma: 0 = leave the RGB alone, 1 = maximum re-spread against the warm diffuse.
BANDS = [
    {
        "name": "HighElf",
        "chroma": 0.15,
        "stops": [
            (0.0, (248, 236, 228), (196, 178, 170)),  # moonpale ivory
            (0.5, (240, 240, 248), (186, 190, 202)),  # pearl blue-white
            (1.0, (250, 232, 214), (200, 180, 160)),  # warm alabaster
        ],
        # R sss, G spec, B metal, A rough. Neutral is NEUTRAL_PROPS per channel -
        # 128 on RGB but 255 on alpha, so roughness deviates downward from 255.
        "props": (150, 132, 128, 238),
    },
    {
        "name": "WoodElf",
        "chroma": 0.35,
        "stops": [
            (0.0, (232, 206, 178), (150, 126, 100)),  # birch tan
            (0.5, (206, 196, 158), (124, 120, 88)),   # olive bark
            (1.0, (196, 164, 130), (112, 88, 64)),    # umber loam
        ],
        "props": (138, 128, 128, 250),
    },
    {
        "name": "Dwarf",
        "chroma": 0.10,
        "stops": [
            (0.0, (236, 190, 166), (156, 110, 88)),   # forge-flushed ruddy
            (0.5, (214, 186, 164), (134, 112, 94)),   # granite tan
            (1.0, (196, 178, 166), (116, 102, 94)),   # iron-dust grey-brown
        ],
        "props": (108, 120, 128, 255),
    },
    {
        "name": "Orc",
        "chroma": 0.90,
        "stops": [
            (0.0, (150, 178, 110), (72, 96, 48)),     # moss green
            (0.5, (156, 170, 142), (76, 90, 70)),     # sage grey-green
            (1.0, (124, 144, 84), (52, 68, 32)),      # bog olive
        ],
        "props": (100, 116, 128, 255),
    },
    {
        "name": "Gnome",
        "chroma": 0.30,
        "stops": [
            (0.0, (226, 190, 132), (140, 112, 66)),   # ochre
            (0.5, (218, 174, 140), (134, 98, 76)),    # clay tan
            (1.0, (200, 150, 112), (116, 80, 56)),    # russet umber
        ],
        "props": (132, 128, 128, 252),
    },
    {
        "name": "Giantkin",
        "chroma": 0.45,
        "stops": [
            (0.0, (232, 240, 246), (168, 182, 196)),  # frostpale
            (0.5, (206, 226, 240), (140, 166, 190)),  # glacier blue
            (1.0, (206, 210, 214), (138, 144, 152)),  # storm grey
        ],
        "props": (118, 134, 128, 246),
    },
    {
        "name": "Deepkin",
        "chroma": 0.80,
        # Deepkin is the one race whose whole identity is being darker and cooler than any
        # human complexion, so it keeps almost none of the realism pull - see tier_stop. At
        # the stock 0.62 the low-fantasy strip landed on (78, 54, 52), a plain warm brown.
        "realism_scale": 0.12,
        # Every stop keeps R >= G. apply_chroma pins the brightest channel and pushes the
        # other two down by the same exponent, so it *preserves* a G-over-R ordering rather
        # than correcting it; the old slate stop was (96, 100, 108) and came out green.
        "stops": [
            (0.0, (46, 42, 58), (13, 12, 19)),        # obsidian
            (0.5, (54, 52, 68), (15, 14, 21)),        # slate graphite
            (1.0, (54, 41, 68), (15, 11, 21)),        # violet ash
        ],
        # Dark skin with a hard specular reads plasticky, so the sheen comes down alongside
        # the diffuse. Roughness still sits below stock for that damp underground look.
        "props": (92, 138, 128, 234),
    },
    {
        "name": "Exotic",
        "chroma": 0.70,
        "stops": [
            (0.00, (140, 206, 200), (48, 96, 94)),    # cyan-teal
            (0.33, (176, 146, 206), (72, 52, 100)),   # violet
            (0.66, (216, 142, 146), (104, 44, 50)),   # crimson-rose
            (1.00, (226, 190, 120), (116, 86, 32)),   # gold-amber
        ],
        "props": (140, 140, 128, 240),
    },
]

assert len(BANDS) == BAND_COUNT
STRIP_COUNT = BAND_COUNT * len(TIERS)
assert FANTASY_ROW0 + STRIP_COUNT * TIER_ROWS <= PALETTE_SIZE


def strip_row0(band: int, tier: int) -> int:
    """First row of one race's strip for one tier. Race-major, matching SkinPalette.Swatch."""
    return FANTASY_ROW0 + (band * len(TIERS) + tier) * TIER_ROWS

NEUTRAL_PROPS = (128, 128, 128, 255)


# --------------------------------------------------------------------------------------
# Which palette coordinates existing ethnicities already sample
# --------------------------------------------------------------------------------------


def _strip_comments(text: str) -> str:
    return "\n".join(line.split("#")[0] for line in text.splitlines())


def skin_rects(text: str) -> list[tuple[float, float, float, float]]:
    """Every `N = { x1 y1 x2 y2 }` inside every `skin_color = { ... }` block."""
    rects: list[tuple[float, float, float, float]] = []
    for match in re.finditer(r"skin_color\s*=\s*\{", text):
        i, depth = match.end(), 1
        while i < len(text) and depth:
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
            i += 1
        block = text[match.end() : i - 1]
        for r in re.finditer(
            r"\d+\s*=\s*\{\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\}", block
        ):
            rects.append(tuple(float(g) for g in r.groups()))  # type: ignore[arg-type]
    return rects


def claimed_mask(dirs: list[Path]) -> np.ndarray:
    """Rasterise every ethnicity skin rect found under `dirs` into a [v, u] mask."""
    mask = np.zeros((PALETTE_SIZE, PALETTE_SIZE), bool)
    total = 0
    for d in dirs:
        if not d.is_dir():
            print(f"  (skipped, not a directory: {d})")
            continue
        for f in sorted(d.glob("*.txt")):
            rects = skin_rects(_strip_comments(f.read_text(encoding="utf-8-sig", errors="replace")))
            total += len(rects)
            for x1, y1, x2, y2 in rects:
                u0, u1 = sorted((int(x1 * (PALETTE_SIZE - 1)), int(x2 * (PALETTE_SIZE - 1))))
                v0, v1 = sorted((int(y1 * (PALETTE_SIZE - 1)), int(y2 * (PALETTE_SIZE - 1))))
                mask[v0 : v1 + 1, u0 : u1 + 1] = True
    print(f"  {total} skin rect(s) claim {mask.mean() * 100:.1f}% of the palette")
    return mask


# --------------------------------------------------------------------------------------
# sRGB <-> linear. Ramps interpolated in linear light, otherwise the midtones go muddy.
# --------------------------------------------------------------------------------------


def srgb_to_linear(c: np.ndarray) -> np.ndarray:
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c: np.ndarray) -> np.ndarray:
    c = np.clip(c, 0.0, 1.0)
    s = np.where(c <= 0.0031308, c * 12.92, 1.055 * (c ** (1 / 2.4)) - 0.055)
    return np.clip(np.rint(s * 255.0), 0, 255).astype(np.uint8)


def relative_luminance(linear_rgb: np.ndarray) -> float:
    return float(0.2126 * linear_rgb[0] + 0.7152 * linear_rgb[1] + 0.0722 * linear_rgb[2])


def human_equivalent(linear_rgb: np.ndarray, stock_column: np.ndarray) -> np.ndarray:
    """The stock human tone of the same luminance, in linear light.

    Lets `realism` pull a fantasy stop toward a complexion that actually occurs on the stock
    gradient instead of toward flat grey, so a damped orc reads as an olive-skinned man
    rather than as a desaturated orc.
    """
    target = relative_luminance(linear_rgb)
    lums = 0.2126 * stock_column[:, 0] + 0.7152 * stock_column[:, 1] + 0.0722 * stock_column[:, 2]
    return stock_column[int(np.argmin(np.abs(lums - target)))]


def apply_chroma(rgb: tuple[float, float, float], chroma: float) -> tuple[float, float, float]:
    """Re-spread a colour's channels away from its brightest one.

    The palette multiplies a warm diffuse, so a nominally green RGB lands as olive. Raising
    each channel's ratio-to-max by an exponent widens the channel spread while pinning the
    brightest channel, which survives the multiply as actual saturation. chroma=0 is a
    no-op.
    """
    if chroma <= 0.0:
        return tuple(float(v) for v in rgb)  # type: ignore[return-value]
    m = max(rgb)
    if m == 0:
        return (0.0, 0.0, 0.0)
    exponent = 1.0 + 0.6 * chroma
    return tuple(m * (v / m) ** exponent for v in rgb)  # type: ignore[return-value]


# --------------------------------------------------------------------------------------
# DDS I/O. Both stock files are uncompressed BGRA8 with a plain 124-byte header.
# --------------------------------------------------------------------------------------

DDSD_CAPS, DDSD_HEIGHT, DDSD_WIDTH, DDSD_PITCH = 0x1, 0x2, 0x4, 0x8
DDSD_PIXELFORMAT, DDSD_MIPMAPCOUNT = 0x1000, 0x20000
DDPF_ALPHAPIXELS, DDPF_RGB = 0x1, 0x40
DDSCAPS_COMPLEX, DDSCAPS_TEXTURE, DDSCAPS_MIPMAP = 0x8, 0x1000, 0x400000


def read_dds_rgba(path: Path) -> np.ndarray:
    """Read an uncompressed BGRA8 DDS as an (h, w, 4) RGBA array (top mip only)."""
    blob = path.read_bytes()
    if blob[:4] != b"DDS ":
        raise ValueError(f"{path} is not a DDS file")
    _size, _flags, height, width, _pitch, _depth, _mips = struct.unpack_from("<7I", blob, 4)
    _pf_size, _pf_flags, four_cc, bits, r, g, b, a = struct.unpack_from("<8I", blob, 76)
    if four_cc != 0 or bits != 32 or (r, g, b, a) != (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000):
        raise ValueError(f"{path} is not uncompressed BGRA8 (fourCC={four_cc}, bits={bits})")
    bgra = np.frombuffer(blob, np.uint8, count=width * height * 4, offset=128)
    return bgra.reshape(height, width, 4)[:, :, [2, 1, 0, 3]].copy()


def write_dds_rgba(path: Path, mips: list[np.ndarray]) -> None:
    """Write RGBA mip levels (largest first) as an uncompressed BGRA8 DDS."""
    height, width = mips[0].shape[:2]

    flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PITCH | DDSD_PIXELFORMAT
    caps = DDSCAPS_TEXTURE
    if len(mips) > 1:
        flags |= DDSD_MIPMAPCOUNT
        caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP

    header = b"DDS " + struct.pack(
        "<7I", 124, flags, height, width, width * 4, 0, len(mips) if len(mips) > 1 else 0
    )
    header += b"\0" * 44  # dwReserved1[11]
    header += struct.pack(
        "<8I", 32, DDPF_ALPHAPIXELS | DDPF_RGB, 0, 32,
        0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000,
    )
    header += struct.pack("<5I", caps, 0, 0, 0, 0)
    assert len(header) == 128, len(header)

    path.write_bytes(header + b"".join(m[:, :, [2, 1, 0, 3]].tobytes() for m in mips))


def build_mip_chain(top: np.ndarray) -> list[np.ndarray]:
    """Box-filter down to 1x1, matching the 10 levels the stock properties palette ships."""
    chain, cur = [top], top
    while cur.shape[0] > 1 or cur.shape[1] > 1:
        h, w = max(1, cur.shape[0] // 2), max(1, cur.shape[1] // 2)
        src = cur[: h * 2, : w * 2].astype(np.uint16)
        if cur.shape[0] == 1:
            nxt = ((src[:, 0::2] + src[:, 1::2]) // 2).astype(np.uint8)
        elif cur.shape[1] == 1:
            nxt = ((src[0::2, :] + src[1::2, :]) // 2).astype(np.uint8)
        else:
            nxt = (
                (src[0::2, 0::2] + src[1::2, 0::2] + src[0::2, 1::2] + src[1::2, 1::2]) // 4
            ).astype(np.uint8)
        chain.append(nxt)
        cur = nxt
    return chain


# --------------------------------------------------------------------------------------
# Band rendering
# --------------------------------------------------------------------------------------


def tier_stop(rgb: tuple[int, int, int], band: dict, tier: dict, stock_column: np.ndarray) -> np.ndarray:
    """One stop colour, damped for a tier, in linear light.

    Blend toward the human equivalent first, then re-spread: doing it the other way round
    would boost the saturation this tier is meant to be giving up.

    A band may damp the pull with `realism_scale`. The equal-luminance human tone is always
    warm (R > G > B), so realism does not merely desaturate a stop - it drags it toward brown.
    For a race defined by *not* being a human complexion that is the wrong direction, and no
    amount of chroma afterwards can undo it.
    """
    realism = tier["realism"] * band.get("realism_scale", 1.0)
    lin = srgb_to_linear(np.array(rgb, dtype=np.float64))
    if realism > 0.0:
        lin = lin * (1.0 - realism) + human_equivalent(lin, stock_column) * realism
    srgb = linear_to_srgb(lin).astype(np.float64)
    return srgb_to_linear(np.array(apply_chroma(tuple(srgb), band["chroma"] * tier["chroma"])))


def render_band(width: int, rows: int, band: dict, tier: dict, stock_column: np.ndarray) -> np.ndarray:
    """Render one race strip: u interpolates the hue stops, v ramps light -> dark."""
    positions = np.array([s[0] for s in band["stops"]], dtype=np.float64)
    light = np.array([tier_stop(s[1], band, tier, stock_column) for s in band["stops"]])
    dark = np.array([tier_stop(s[2], band, tier, stock_column) for s in band["stops"]])

    u = np.linspace(0.0, 1.0, width)
    light_u = np.stack([np.interp(u, positions, light[:, c]) for c in range(3)], axis=-1)
    dark_u = np.stack([np.interp(u, positions, dark[:, c]) for c in range(3)], axis=-1)

    t = np.linspace(0.0, 1.0, rows)[:, None, None]
    lin = light_u[None, :, :] * (1.0 - t) + dark_u[None, :, :] * t

    out = np.empty((rows, width, 4), np.uint8)
    out[:, :, :3] = linear_to_srgb(lin)
    out[:, :, 3] = 255
    return out


def painted_mask() -> np.ndarray:
    """The [v, u] pixels the race strips occupy."""
    mask = np.zeros((PALETTE_SIZE, PALETTE_SIZE), bool)
    mask[FANTASY_ROW0 : FANTASY_ROW0 + STRIP_COUNT * TIER_ROWS, : FANTASY_COL1 + 1] = True
    return mask


def build_skin_palette(stock: np.ndarray, claimed: np.ndarray) -> np.ndarray:
    """Stock pixels everywhere, with the race strips painted into free space."""
    out = stock.copy()
    width = FANTASY_COL1 + 1
    # A fixed mid-undertone slice of the stock gradient is the reference for `realism`.
    stock_column = srgb_to_linear(stock[:, PALETTE_SIZE // 2, :3].astype(np.float64))
    for i, band in enumerate(BANDS):
        for t, tier in enumerate(TIERS):
            y0 = strip_row0(i, t)
            out[y0 : y0 + TIER_ROWS, :width] = render_band(width, TIER_ROWS, band, tier, stock_column)

    painted = painted_mask()
    overlap = painted & claimed
    if overlap.any():
        vs, us = np.nonzero(overlap)
        raise SystemExit(
            f"REFUSING TO WRITE: the race bands overlap {overlap.sum()} claimed pixel(s), "
            f"e.g. row {vs[0]} col {us[0]}. Adjust FANTASY_ROW0 / FANTASY_COL1 / BAND_ROWS."
        )

    changed = (out[:, :, :3] != stock[:, :, :3]).any(axis=2)
    stray = changed & ~painted
    if stray.any():
        raise SystemExit(f"REFUSING TO WRITE: {stray.sum()} pixel(s) changed outside the bands.")
    if (changed & claimed).any():
        raise SystemExit("REFUSING TO WRITE: a claimed pixel changed.")
    return out


def build_properties_palette(claimed: np.ndarray, size: int = 512) -> np.ndarray:
    """Neutral everywhere stock ethnicities read; a mild material tweak per race band."""
    out = np.empty((size, size, 4), np.uint8)
    out[:, :] = NEUTRAL_PROPS

    scale = size / PALETTE_SIZE
    col1 = int((FANTASY_COL1 + 1) * scale)
    for i, band in enumerate(BANDS):
        for t, tier in enumerate(TIERS):
            # Damp the deviation from neutral by the tier, so a subtle race also gets subtle
            # subsurface and roughness rather than full-strength material weirdness.
            props = tuple(
                int(round(neutral + (v - neutral) * tier["props"]))
                for v, neutral in zip(band["props"], NEUTRAL_PROPS)
            )
            y0 = int(strip_row0(i, t) * scale)
            out[y0 : y0 + int(TIER_ROWS * scale), :col1] = props

    # Same guarantee as the diffuse palette: nothing a stock rect samples may deviate.
    changed = (out != np.array(NEUTRAL_PROPS, np.uint8)).any(axis=2)
    claimed_hi = np.repeat(np.repeat(claimed, int(scale), 0), int(scale), 1)
    if (changed & claimed_hi).any():
        raise SystemExit("REFUSING TO WRITE: properties deviate under a claimed rect.")
    return out


# --------------------------------------------------------------------------------------


def main() -> None:
    repo = Path(__file__).resolve().parents[2]
    default_game = Path(r"C:/Program Files (x86)/Steam/steamapps/common/Crusader Kings III/game")

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--game", type=Path, default=default_game, help="CK3 game/ directory")
    ap.add_argument(
        "--also-scan",
        type=Path,
        nargs="*",
        default=[],
        metavar="DIR",
        help="extra common/ethnicities directories whose rects must also stay intact "
             "(point this at another mod you intend to run alongside)",
    )
    ap.add_argument(
        "--out",
        type=Path,
        default=repo / "BaseFilesToCopy" / "Core" / "gfx" / "portraits",
        help="destination directory for the generated .dds files",
    )
    ap.add_argument("--preview", type=Path, default=None, help="also write PNG previews here")
    args = ap.parse_args()

    src = args.game / "gfx" / "portraits" / "skin_palette.dds"
    if not src.is_file():
        raise SystemExit(f"stock skin_palette.dds not found at {src} (pass --game)")

    print("scanning ethnicities for claimed palette coordinates:")
    claimed = claimed_mask([args.game / "common" / "ethnicities", *args.also_scan])

    stock = read_dds_rgba(src)
    if stock.shape[:2] != (PALETTE_SIZE, PALETTE_SIZE):
        raise SystemExit(f"expected a {PALETTE_SIZE}x{PALETTE_SIZE} stock palette, got {stock.shape}")

    args.out.mkdir(parents=True, exist_ok=True)

    skin = build_skin_palette(stock, claimed)
    write_dds_rgba(args.out / "skin_palette.dds", [skin])
    print(f"\nwrote {args.out / 'skin_palette.dds'}  {skin.shape[1]}x{skin.shape[0]}  1 mip")

    props = build_properties_palette(claimed)
    props_mips = build_mip_chain(props)
    write_dds_rgba(args.out / "skin_properties_palette.dds", props_mips)
    print(
        f"wrote {args.out / 'skin_properties_palette.dds'}  "
        f"{props.shape[1]}x{props.shape[0]}  {len(props_mips)} mips"
    )

    n = PALETTE_SIZE - 1
    print(f"\nrace strips, all inside free space (u 0.0000..{FANTASY_COL1 / n:.4f}):")
    for i, band in enumerate(BANDS):
        for t, tier in enumerate(TIERS):
            v0 = strip_row0(i, t)
            v1 = v0 + TIER_ROWS - 1
            print(
                f"  {band['name']:9s} {tier['name']:14s} rows {v0}-{v1}  "
                f"v {v0 / n:.4f}..{v1 / n:.4f}"
            )
    print(f"\nstock pixels preserved: {(1 - painted_mask().mean()) * 100:.1f}% of the palette")

    if args.preview:
        from PIL import Image

        args.preview.mkdir(parents=True, exist_ok=True)
        Image.fromarray(skin, "RGBA").convert("RGB").save(args.preview / "skin_palette.png")
        Image.fromarray(props, "RGBA").convert("RGB").save(
            args.preview / "skin_properties_palette.png"
        )
        print(f"previews written to {args.preview}")


if __name__ == "__main__":
    main()
