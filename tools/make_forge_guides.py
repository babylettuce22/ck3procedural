"""Authors the coastline guide layers the layout-driven Forge presets ship with.

Run by hand when the shapes below change:

    python tools/make_forge_guides.py

Output goes to assets/forge-presets/<preset>.layers/, beside the preset JSON that names it.

WHY A GUIDE AND NOT A STAGE
---------------------------
Twin Continents and Inland Sea promise an arrangement of land and sea, and noise alone never
guarantees one. The Continents stage's coast paint channel already does what a "layout bias"
would: ContinentStage.GetCoastline adds `paint * spread` to the continent noise *before* the
land/sea threshold, where spread is the noise's own p1..p99 range. So a guide is a soft nudge,
not a stencil - the noise still draws every actual coastline, and a value of 0.25 shifts the
threshold by a quarter of the whole noise range. Keep guides smooth and well under 1; a hard
full-strength shape comes out as a brush-shaped coast.

Land fraction is measured on the unpainted noise on purpose, so a guide that paints sea
lowers the land share. The presets compensate with a higher landFraction.

Only ContinentStage is paintable. BaseNoiseStage has no layer, so these presets start there.

FORMAT
------
PaintLayer.Save: 16-bit greyscale PNG, value v in [-1, 1] stored as round(v * 32767) + 32768,
so 0 ("no opinion") is exactly 32768. Positive pushes toward land, negative toward sea.
Sampled bilinearly in normalised map space, so the authoring size only limits smoothness.
"""

import os

import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "forge-presets")
W, H = 1024, 512


def grid():
    x = (np.arange(W) + 0.5) / W
    y = (np.arange(H) + 0.5) / H
    return np.meshgrid(x, y)


def gauss(d):
    return np.exp(-d * d)


def twin_continents():
    """A sea strait down the middle, and a gentle land lobe centred in each half."""
    nx, ny = grid()
    strait = -0.45 * gauss((nx - 0.5) / 0.05)
    lobes = sum(0.15 * gauss(np.hypot((nx - cx) / 0.15, (ny - 0.5) / 0.26)) for cx in (0.26, 0.74))
    return strait + lobes


def inland_sea():
    """A sea basin in the centre, and a ring of land pushed up around it."""
    nx, ny = grid()
    # Elliptical radius in units of the basin's size. The map is 2:1, so 0.16 of the width is
    # 0.32 of the height: a basin about twice as long east-west as it is north-south.
    d = np.hypot((nx - 0.5) / 0.16, (ny - 0.5) / 0.15)
    basin = -0.45 * gauss(d / 0.9)
    ring = 0.2 * gauss((d - 1.4) / 0.6)
    return basin + ring


def write(preset, values):
    folder = os.path.join(ROOT, preset + ".layers")
    os.makedirs(folder, exist_ok=True)
    v = np.clip(values, -1.0, 1.0)
    pixels = np.clip(np.round(v * 32767.0) + 32768, 0, 65535).astype(np.uint16)
    path = os.path.join(folder, "00-coast.png")
    Image.fromarray(pixels).save(path)
    print(f"{path}: min {v.min():+.3f} max {v.max():+.3f}")


if __name__ == "__main__":
    write("twin-continents", twin_continents())
    write("inland-sea", inland_sea())
