"""Paints a lord's shield (assets/ui/icons/shield-<lord>.png) from the Margrave's: his gilt rim, a
field of the lord's colour shaded as the painted one is, and the lord's emblem
(assets/ui/emblems/<lord>.png) laid in the middle with a shadow under it. The Margrave's own and the
Crown's are painted by hand; this is for the lords who came with an emblem and no shield.

Run: tools/.venv/bin/python tools/paint_shields.py
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

PROJECT = Path(__file__).resolve().parent.parent
ICONS = PROJECT / 'assets' / 'ui' / 'icons'
EMBLEMS = PROJECT / 'assets' / 'ui' / 'emblems'
# Each lord's field, a shade under his map colour so the emblem stands out of it.
FIELDS = [('marshal', (128, 92, 30)), ('castellan', (46, 46, 52)), ('countess', (100, 26, 70))]

base = Image.open(ICONS / 'shield-margrave.png').convert('RGBA')
alpha = base.split()[-1]
inner = alpha.point(lambda a: 255 if a > 128 else 0).filter(ImageFilter.MinFilter(41)).filter(ImageFilter.MinFilter(41))
inner = inner.filter(ImageFilter.GaussianBlur(2))
L = np.asarray(base.convert('L'), dtype=np.float32) / 255.0
box = inner.getbbox()
for key, field in FIELDS:
    h, w = L.shape
    ys = np.linspace(0, 1, h)[:, None]
    xs = np.linspace(-1, 1, w)[None, :]
    shade = (1.1 - 0.3 * ys) * (1.0 - 0.35 * xs ** 2)
    grain = np.asarray(Image.effect_noise((w, h), 18).filter(ImageFilter.GaussianBlur(1.2)), dtype=np.float32) / 255.0
    rgb = np.clip(np.array(field, dtype=np.float32)[None, None, :] * (shade * (0.9 + 0.2 * grain))[:, :, None], 0, 255)
    fill = Image.fromarray(rgb.astype(np.uint8), 'RGB').convert('RGBA')
    shield = base.copy()
    shield.paste(fill, (0, 0), inner)
    emblem = Image.open(EMBLEMS / f'{key}.png').convert('RGBA')
    span = int((box[2] - box[0]) * 0.8)
    emblem.thumbnail((span, span), Image.LANCZOS)
    cx = (box[0] + box[2]) // 2 - emblem.width // 2
    cy = int(box[1] + (box[3] - box[1]) * 0.46) - emblem.height // 2
    shadow = Image.new('RGBA', emblem.size, (0, 0, 0, 0)); shadow.putalpha(emblem.split()[-1].point(lambda a: a * 0.6))
    shield.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(6)), (cx + 6, cy + 8))
    shield.alpha_composite(emblem, (cx, cy))
    shield.putalpha(alpha)
    shield.save(ICONS / f'shield-{key}.png')
    print(key)
