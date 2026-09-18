"""Cuts each building out of a sheet of them, and keys its backdrop away.

The sheets are painted, not rendered: the backdrop is a smooth brown vignette with no alpha, and it
is the same family of colours as the sunlit stonework. Matching on colour therefore cannot separate
them — a threshold loose enough to clear the backdrop eats the towers, and one tight enough to spare
the towers leaves a halo.

What does separate them is detail. The backdrop is a soft gradient with almost nothing in it; every
building is drawn with mortar lines, shadow and edge. So the cut grows out from the border through
QUIET pixels only — ones close to their own blur — and stops at the first painted edge it meets,
which is the building's own silhouette.

    python3 tools/cut-tiles.py
"""
from collections import deque
from PIL import Image, ImageChops, ImageFilter

SHEETS = {
    'A': '/Users/clauzi/Downloads/ChatGPT Image Sep 18, 2026, 09_41_58 PM (3).png',
    'B': '/Users/clauzi/Downloads/ChatGPT Image Sep 18, 2026, 09_41_58 PM (2).png',
}

# One window per building, read off the sheets, with a margin of backdrop all round and clear of
# whatever is drawn in the next cell.
TILES = {
    'keep':       ('A', (450,   24,  782,  366)),
    'market':     ('A', (776,  380, 1148,  638)),
    'grain':      ('A', (1158, 380, 1532,  638)),
    'lumber':     ('A', (392,  646,  764,  850)),
    'mine':       ('A', (776,  646, 1148,  850)),
    'blacksmith': ('A', (1158, 646, 1532,  850)),
    'cattle':     ('A', (392,  858,  764, 1018)),
    'barracks':   ('A', (1158, 858, 1532, 1018)),
    'quarry':     ('B', (314,  656,  610,  850)),
}

QUIET = 7   # how flat a pixel has to be, against its own blur, to count as backdrop

def carve(tile):
    W, H = tile.size
    detail = ImageChops.difference(tile, tile.filter(ImageFilter.GaussianBlur(2))).convert('L')
    flat = detail.load()

    out = tile.convert('RGBA')
    px = out.load()
    seen = bytearray(W * H)
    queue = deque()

    def push(x, y):
        if not seen[y * W + x] and flat[x, y] <= QUIET:
            seen[y * W + x] = 1
            queue.append((x, y))

    for x in range(W):
        push(x, 0)
        push(x, H - 1)
    for y in range(H):
        push(0, y)
        push(W - 1, y)

    while queue:
        x, y = queue.popleft()
        px[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < W and 0 <= ny < H:
                push(nx, ny)

    return out

def largest(art):
    """Only the building. A window that reaches into the next cell picks up a rock or the tip of a
    steeple with it, and one loose rock in the corner is what sets the picture's size."""
    W, H = art.size
    px = art.load()
    seen = bytearray(W * H)
    best, best_size = None, 0
    for sy in range(H):
        for sx in range(W):
            if seen[sy * W + sx] or px[sx, sy][3] <= 8:
                continue

            blob, queue, size = [], deque([(sx, sy)]), 0
            seen[sy * W + sx] = 1
            while queue:
                x, y = queue.popleft()
                blob.append((x, y))
                size += 1
                for nx, ny in ((x+1, y), (x-1, y), (x, y+1), (x, y-1)):
                    if 0 <= nx < W and 0 <= ny < H and not seen[ny * W + nx] \
                            and px[nx, ny][3] > 8:
                        seen[ny * W + nx] = 1
                        queue.append((nx, ny))

            if size > best_size:
                best, best_size = blob, size

    kept = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    kp = kept.load()
    for x, y in best:
        kp[x, y] = px[x, y]
    return kept

if __name__ == '__main__':
    sheets = {k: Image.open(v).convert('RGB') for k, v in SHEETS.items()}
    for key, (sheet, box) in TILES.items():
        art = largest(carve(sheets[sheet].crop(box)))
        art = art.crop(art.getchannel('A').getbbox())
        art.save(f'assets/city/buildings/{key}.png')
        print(f'{key:<11} {art.size}')
