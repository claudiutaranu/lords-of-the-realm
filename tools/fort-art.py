"""Lays one fortification onto the page's own frame, and cuts its card from the same drawing.

Every rung shares a footing, so one dissolves into the next without the picture shifting under it.
The counters cover the bottom quarter of the page, which is why a low timber wall is scaled harder
than a tall castle: otherwise all you see of a palisade is the tip of its watchtower.

A rung is measured by its height, or by its width where it is meant to run edge to edge and leave
no valley showing at the sides. Past 1.0 the frame simply crops it, which is the point.

The footing is shared inside a ladder, so one rung dissolves into the next without the picture
shifting. Timber and stone keep different footings on purpose: a timber wall that runs edge to edge
is very tall and would lose its roofs off the top of the frame at the height a castle wants to sit.

    python3 tools/fort-art.py <key> <source.png> <size 0..1+> [height|width] [ground 0..1]
"""
import sys
from PIL import Image

W, H = 1672, 941
CENTRE, WIDEST = 0.50, 0.96
CARD_W, CARD_H = 480, 360

def place(art, size, along, ground):
    art = art.crop(art.getchannel('A').getbbox())   # drop the empty margin the render left
    if along == 'width':
        wide = int(W * size)
        tall = int(art.height * wide / art.width)
    else:
        tall = int(H * size)
        wide = int(art.width * tall / art.height)
        if wide > W * WIDEST:
            wide = int(W * WIDEST)
            tall = int(art.height * wide / art.width)

    art = art.resize((wide, tall), Image.LANCZOS)

    frame = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    frame.paste(art, (int(W * CENTRE) - wide // 2, int(H * ground) - tall), art)
    return frame, wide, tall

def card(scene):
    vale = Image.open('assets/forts/landscape.png').convert('RGBA')
    crop = vale.crop((int(vale.width * 0.10), int(vale.height * 0.28),
                      int(vale.width * 0.62), int(vale.height * 0.95))).resize((CARD_W, CARD_H), Image.LANCZOS)

    art = scene.crop(scene.getchannel('A').getbbox())
    tall = int(CARD_H * 0.62)
    wide = int(art.width * tall / art.height)
    if wide > CARD_W * 0.92:
        wide = int(CARD_W * 0.92)
        tall = int(art.height * wide / art.width)
    art = art.resize((wide, tall), Image.LANCZOS)

    out = crop.copy()
    out.paste(art, (CARD_W // 2 - wide // 2, int(CARD_H * 0.88) - tall), art)
    return out.convert('RGB')

key, source, size = sys.argv[1], sys.argv[2], float(sys.argv[3])
along = sys.argv[4] if len(sys.argv) > 4 else 'height'
ground = float(sys.argv[5]) if len(sys.argv) > 5 else 0.99
scene, wide, tall = place(Image.open(source).convert('RGBA'), size, along, ground)
scene.save(f'assets/forts/scene/{key}.png')
card(scene).save(f'assets/forts/{key}.png')
print(f'{key:<15} latime={wide/W:.2f} inaltime={tall/H:.2f} varf_la={ground - tall/H:.2f}')
