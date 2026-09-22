"""Packs a loose albedo/normal pair into the two images Terrain3D reads.

Terrain3D wants each ground surface as two RGBA textures:

    <name>_alb_ht.png   RGB = albedo, A = the surface's own HEIGHT
    <name>_nrm_rgh.png  RGB = normal, A = its ROUGHNESS

Those two alpha channels are not decoration. The height is what lets two surfaces meet along their
own relief instead of cross-fading into a smear, and the roughness is what lets wet rock and dry
turf catch the light differently. The ambientCG sets ship them; the older loose pairs in
assets/terrain do not, so what they lack is derived here rather than left flat:

  - height comes from the albedo's luminance, contrast-stretched. It is not the real displacement
    map, but for blending it only has to say which parts of the surface stand proud, and for snow
    and sand — drifts and ripples — luminance says exactly that.
  - roughness is the surface's own figure with a little of that same variation on it, so it is not
    one number across a whole beach.

Run: tools/.venv/bin/python tools/pack_terrain_textures.py
"""

import numpy as np
from PIL import Image
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent
LOOSE_DIR = PROJECT_DIR / "assets" / "terrain"
PACKED_DIR = PROJECT_DIR / "assets" / "terrain" / "pbr"

# name, roughness, how much the derived height varies (snow drifts more than wet sand)
SURFACES = [
    ("snow", 0.82, 0.55),
    ("sand", 0.68, 0.70),
    # The roads: Poly Haven's rocky_trail_02, packed dirt with the stones worn out of it.
    ("path", 0.86, 0.60),
]


def stretched(grey):
    """Luminance pulled out to the full 0..1 range, so a flat-looking texture still has a height
    map with something in it."""
    low, high = np.percentile(grey, 2), np.percentile(grey, 98)
    return np.clip((grey - low) / max(high - low, 1e-6), 0.0, 1.0)


def pack(name, roughness, relief):
    albedo = np.asarray(Image.open(LOOSE_DIR / f"{name}-diffuse.jpg").convert("RGB")).astype(np.float32)
    normal = np.asarray(Image.open(LOOSE_DIR / f"{name}-normal.jpg").convert("RGB")).astype(np.float32)

    grey = albedo @ np.array([0.299, 0.587, 0.114], dtype=np.float32) / 255.0
    height = 0.5 + (stretched(grey) - 0.5) * relief
    rough = np.clip(roughness + (0.5 - stretched(grey)) * 0.18, 0.0, 1.0)

    Image.fromarray(np.dstack([albedo, height * 255.0]).astype(np.uint8), "RGBA").save(
        PACKED_DIR / f"{name}_alb_ht.png")
    Image.fromarray(np.dstack([normal, rough * 255.0]).astype(np.uint8), "RGBA").save(
        PACKED_DIR / f"{name}_nrm_rgh.png")
    print(f"  {name}: albedo+height and normal+roughness, {albedo.shape[1]}x{albedo.shape[0]}, "
          f"roughness {rough.mean():.2f}")


def main():
    print(f"Packing into {PACKED_DIR.relative_to(PROJECT_DIR)}:")
    for name, roughness, relief in SURFACES:
        pack(name, roughness, relief)


if __name__ == "__main__":
    main()
