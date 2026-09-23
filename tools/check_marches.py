"""Can an army get there? Every county's village, walked to from every other, over the generated map.

The game decides where men can march in CampaignMapPage.LayGround and MarchGrid, off the height, ID and
ditch images this campaign's generator writes. A map can come out of the generator looking right and
still have a county nobody can reach: a road that climbs one cell too steeply is not a road to the
march grid, and a whole region went unreachable that way without anybody noticing. So the same rules
are applied here, to the images on disk, and the run fails if any county cannot be reached.

Kept in step with the C# by hand. The numbers below are the game's own (CampaignMapPage, CampaignMap3D,
MarchGrid, game-balance.tres); change one there and it has to change here.

    tools/.venv/bin/python tools/check_marches.py [campaign] [--rise 0.9]
"""
import argparse
import json
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image

PROJECT_DIR = Path(__file__).resolve().parent.parent

CELL_SIZE = 12                            # MarchGrid.CellSize
HEIGHT_SCALE = 26.0                       # CampaignMap3D.HeightScale
SEA_LEVEL = HEIGHT_SCALE * 46.0 / 255.0   # CampaignMap3D.SeaLevel, off SeaFloorByte
SHORE_CLEARANCE = 0.35                    # CampaignMapPage.ShoreClearance
MARCHABLE_RISE = 0.9                      # CampaignMapPage.MarchableRise


def load(campaign):
    assets = PROJECT_DIR / "assets" / "campaigns" / campaign
    data = PROJECT_DIR / "data" / "campaigns" / campaign
    height = np.asarray(Image.open(assets / "map-height.png").convert("L"), dtype=np.float32) / 255.0 * HEIGHT_SCALE
    ids = np.asarray(Image.open(assets / "map-ids.png").convert("RGB"))[:, :, 0].astype(np.int32) - 1
    ditch = np.asarray(Image.open(assets / "map-ditch.png").convert("L"), dtype=np.float32) / 255.0
    roads = json.loads((data / "map-roads.json").read_text())
    yards = {y["province"]: (y["x"], y["y"]) for y in json.loads((data / "map-yards.json").read_text())}
    provinces = json.loads((data / "provinces.json").read_text())["provinces"]
    return height, ids, ditch, roads, yards, provinces


def height_at(height, x, y):
    """CampaignMap3D.HeightAt: nothing off the edge of the map, the pixel under the point inside it."""
    rows, cols = height.shape
    if x < 0 or y < 0 or x > cols or y > rows:
        return 0.0
    return float(height[min(int(y), rows - 1), min(int(x), cols - 1)])


def lay_ground(height, ditch, roads, rise_limit):
    """CampaignMapPage.LayGround into MarchGrid: which cells an army can stand on, and why not."""
    rows, cols = height.shape
    across, down = cols // CELL_SIZE, rows // CELL_SIZE
    walkable = np.zeros((down, across), dtype=bool)
    why = {}
    for cy in range(down):
        for cx in range(across):
            x, y = (cx + 0.5) * CELL_SIZE, (cy + 0.5) * CELL_SIZE
            here = height_at(height, x, y)
            if here <= SEA_LEVEL + SHORE_CLEARANCE:
                why[(cx, cy)] = "sea"
                continue
            if ditch[min(max(int(y), 0), rows - 1), min(max(int(x), 0), cols - 1)] > 0.5:
                why[(cx, cy)] = "ditch"
                continue
            rise = max(abs(height_at(height, x + CELL_SIZE, y) - here), abs(height_at(height, x, y + CELL_SIZE) - here))
            if rise > rise_limit:
                why[(cx, cy)] = f"steep {rise:.2f}"
                continue
            walkable[cy, cx] = True

    # A road only makes ground that could already be walked cheaper (MarchGrid.Mark); it never
    # makes a cliff passable. Nothing to do for reachability, which is all this asks.
    return walkable, why


def cell_of(point):
    return int(np.floor(point[0] / CELL_SIZE)), int(np.floor(point[1] / CELL_SIZE))


def reach(walkable, start):
    """Every cell reachable from start: eight ways round, never squeezing between two corners."""
    down, across = walkable.shape
    seen = np.zeros_like(walkable)
    if not (0 <= start[0] < across and 0 <= start[1] < down) or not walkable[start[1], start[0]]:
        return seen
    seen[start[1], start[0]] = True
    queue = deque([start])
    while queue:
        cx, cy = queue.popleft()
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                x, y = cx + dx, cy + dy
                if (dx == 0 and dy == 0) or not (0 <= x < across and 0 <= y < down):
                    continue
                if seen[y, x] or not walkable[y, x]:
                    continue
                if dx and dy and (not walkable[cy, x] or not walkable[y, cx]):
                    continue
                seen[y, x] = True
                queue.append((x, y))
    return seen


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("campaign", nargs="?", default="royal-crown")
    parser.add_argument("--rise", type=float, default=MARCHABLE_RISE)
    args = parser.parse_args()

    height, ids, ditch, roads, yards, provinces = load(args.campaign)
    walkable, why = lay_ground(height, ditch, roads, args.rise)

    capital = next(p for p in provinces if p.get("capital") and p["realm"] == json.loads(
        (PROJECT_DIR / "data" / "campaigns" / args.campaign / "provinces.json").read_text())["player"])
    start = cell_of(yards.get(capital["name"], (capital["x"], capital["y"])))
    seen = reach(walkable, start)

    unreachable = []
    for province in provinces:
        cell = cell_of(yards.get(province["name"], (province["x"], province["y"])))
        ok = bool(seen[cell[1], cell[0]])
        print(f"  {capital['name']} -> {province['name']}: {'reachable' if ok else 'UNREACHABLE'}")
        if not ok:
            unreachable.append(province["name"])

    # Where a road stops being ground: the first cells along each line that an army cannot stand on.
    for road in roads:
        broken = []
        for (x0, y0), (x1, y1) in zip(road["points"], road["points"][1:]):
            span = max(np.hypot(x1 - x0, y1 - y0), 1e-6)
            for along in np.arange(0.0, span + 1e-6, CELL_SIZE * 0.5):
                cell = cell_of((x0 + (x1 - x0) * along / span, y0 + (y1 - y0) * along / span))
                if cell in why and cell not in [b[0] for b in broken]:
                    broken.append((cell, why[cell]))
        if broken:
            shown = ", ".join(f"{c}:{w}" for c, w in broken[:4])
            print(f"  road {road['from']} -> {road['to']}: {len(broken)} blocked cells ({shown}{', ...' if len(broken) > 4 else ''})")

    print(f"\nmarches ({args.campaign}, rise {args.rise}): "
          + ("every county can be reached" if not unreachable else f"{len(unreachable)} UNREACHABLE: {', '.join(unreachable)}"))
    raise SystemExit(1 if unreachable else 0)


if __name__ == "__main__":
    main()
