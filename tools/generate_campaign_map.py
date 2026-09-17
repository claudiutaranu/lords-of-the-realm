"""Generates the Royal Crown vs. Northern Watch campaign map as a raster Voronoi diagram
instead of a painted illustration, so provinces are real, clickable, divided regions.

How it works: pick a landmass silhouette and a handful of seed points (one per stronghold),
then color every land pixel by whichever seed is nearest ("raster Voronoi" - simpler than
computing actual Voronoi polygons, and gives pixel-perfect province shapes for free). That
same per-pixel province assignment produces two images:

  - map-drawn.png: the visible art (shaded + textured so it doesn't look like flat
    vector fill), with a thin border between same-realm provinces and a thicker gold line on
    the frontier between realms.
  - map-ids.png: an unseen, lossless twin of the same layout where each province is
    a flat color encoding its index (R channel = index + 1, 0 = water). The game samples this
    pixel-for-pixel on click to know what was hit - the same technique Paradox's province
    bitmaps use, so hit-testing stays exact no matter how the visible art evolves.

Also prints the adjacency between provinces (which ones actually touch), computed as a side
effect of the same pixel scan.

To change the map, edit the campaign's data, not this file:

  data/campaigns/<CAMPAIGN>/map.json       - canvas size, the island's outline, and the terrain,
                                             island and forest knobs
  data/campaigns/<CAMPAIGN>/provinces.json - each province's name, seat (x, y in map pixels),
                                             region (north/south geography), owner and capital flag

Then run this script and reopen the project so Godot re-imports. provinces.json is the same file
the game reads, so there is no second copy to keep in step: move a seat here and it moves there.
Output goes to assets/campaigns/<CAMPAIGN>/ (images) and data/campaigns/<CAMPAIGN>/ (roads).

Run: pip install pillow numpy; python tools/generate_campaign_map.py

One import setting matters: map-ids.png must stay on Godot's "image" importer
(type=Image), not the default texture importer. Loading it as a texture and reading pixels off
it works in the editor but breaks in an exported build, because only the compressed texture
ships. Re-running this script keeps the existing .import file, so the setting survives; just
don't reset it if you ever delete the .import.
"""

import heapq
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

CAMPAIGN = "royal-crown"
PROJECT_DIR = Path(__file__).resolve().parent.parent
OUT_DIR = PROJECT_DIR / "assets" / "campaigns" / CAMPAIGN
DATA_DIR = PROJECT_DIR / "data" / "campaigns" / CAMPAIGN

# The map's shape, its terrain knobs and its provinces are the campaign's data, not this script's:
# edit data/campaigns/<CAMPAIGN>/map.json and provinces.json, re-run, and the images follow. The
# game reads the same provinces.json, so a seat moved here is a seat moved there — no second copy to
# keep in step.
MAP = json.loads((DATA_DIR / "map.json").read_text())
CAMPAIGN_DATA = json.loads((DATA_DIR / "provinces.json").read_text())


def required(data, section, *keys):
    """Reads a block of knobs, and says plainly what is missing instead of dying on a KeyError:
    these files are meant to be edited by hand, so a typo has to name itself."""
    if section not in data:
        raise SystemExit(f"map.json is missing the \"{section}\" block — it is required.")

    block = data[section]
    missing = [key for key in keys if key not in block]
    if missing:
        raise SystemExit(f"map.json: \"{section}\" is missing {', '.join(missing)}.")

    return block

W, H = MAP["size"]
LANDMASS = [tuple(point) for point in MAP["outline"]]

TERRAIN = required(MAP, "terrain", "sea_floor_byte", "ridge_height", "ridge_detail",
                   "inland_height", "north_lift", "seat_height", "noise_height")
SEA_FLOOR_BYTE = TERRAIN["sea_floor_byte"]
RIDGE_HEIGHT = TERRAIN["ridge_height"]
RIDGE_DETAIL = TERRAIN["ridge_detail"]
INLAND_HEIGHT = TERRAIN["inland_height"]
NORTH_LIFT = TERRAIN["north_lift"]
SEAT_HEIGHT = TERRAIN["seat_height"]
NOISE_HEIGHT = TERRAIN["noise_height"]

ISLANDS = required(MAP, "islands", "islet_height", "stack_height", "emergent_shape",
                   "base_reach", "stack_count", "islets_per_province")
ISLET_HEIGHT = ISLANDS["islet_height"]
STACK_HEIGHT = ISLANDS["stack_height"]
EMERGENT_SHAPE = ISLANDS["emergent_shape"]
BASE_REACH = ISLANDS["base_reach"]
STACK_COUNT = int(ISLANDS["stack_count"])
# Offshore islands per province. 0 here and 0 stacks leaves a clean coastline.
ISLETS_PER_PROVINCE = float(ISLANDS["islets_per_province"])

FOREST = required(MAP, "forest", "southern_density", "northern_density", "treeline_strength")

# Realms in the order the campaign lists them; that order is the owner code written into the ID
# map's green channel (+1), which is how the terrain shader knows whose frontier it is drawing.
REALM_ORDER = list(CAMPAIGN_DATA["realms"].keys())

# name, x, y, region (0=southern lowlands, 1=northern highlands), is_capital, owner
PROVINCES = [
    (province["name"], province["x"], province["y"],
     1 if province.get("region") == "north" else 0,
     bool(province.get("capital", False)),
     REALM_ORDER.index(province["realm"]))
    for province in CAMPAIGN_DATA["provinces"]
]

WATER_COLOR = np.array([18, 33, 54])
BORDER_COLOR = np.array([28, 22, 18])
FRONTIER_COLOR = np.array([196, 156, 64])
COAST_COLOR = np.array([12, 20, 34])

RC_BASE = np.array([70, 92, 54])     # forest green-brown
NW_BASE = np.array([170, 186, 198])  # pale icy blue-white


def province_color(index):
    _, _, _, realm, _, _ = PROVINCES[index]
    base = RC_BASE if realm == 0 else NW_BASE
    # deterministic per-province lightness variation so neighbors read distinctly
    factor = 0.82 + 0.09 * (index % 4)
    return np.clip(base * factor, 0, 255)


# --- 3D terrain -------------------------------------------------------------------------
# The campaign map is a displaced mesh, not a picture: map-height.png drives vertex
# height in the terrain shader AND the collision heightfield the click raycast hits, so both
# read the same bytes. 255 here equals TERRAIN_HEIGHT world units in CampaignMap3D.cs - change
# one and change the other, or clicks stop landing where the mountains are drawn.
# Sea level, in height-map bytes. Everything below is seabed: the map carries bathymetry, not just
# land, because a flat zero-floor sea makes every wave shader think the whole sea is shoreline.
# CampaignMap3D.SeaFloorByte must match.
SEA_FLOOR_BYTE = 46.0

RIDGE_HEIGHT = 190.0      # the frontier range, the map's spine
INLAND_HEIGHT = 44.0      # how far the interior lifts away from the shoreline
NORTH_LIFT = 26.0         # the Northern Watch plateau sits above the southern lowlands
SEAT_HEIGHT = 16.0        # each stronghold gets its own hill to stand on
NOISE_HEIGHT = 26.0
RIDGE_DETAIL = 96.0       # sharp crests and spurs riding on the main range

SAND_COLOR = np.array([206, 186, 140])
GRASS_COLOR = np.array([92, 104, 62])    # sage meadow; the grass texture supplies the green
FOREST_COLOR = np.array([44, 60, 40])
ROCK_COLOR = np.array([96, 100, 110])    # cold blue-grey stone
TUNDRA_COLOR = np.array([138, 152, 168])
SNOW_COLOR = np.array([246, 249, 252])


def blurred(mask, radius):
    """Gaussian blur of a boolean/float field, as 0..1 floats."""
    source = (np.clip(mask, 0, 1) * 255).astype(np.uint8)
    blurred_img = Image.fromarray(source, "L").filter(ImageFilter.GaussianBlur(radius))
    return np.array(blurred_img).astype(np.float32) / 255.0


def smooth_noise(rng, cells, height=H, width=W):
    """Low-frequency noise in -1..1, built by upscaling a small random grid."""
    grid = rng.uniform(0, 255, size=(cells, cells)).astype(np.uint8)
    upscaled = Image.fromarray(grid, "L").resize((width, height), Image.BICUBIC)
    return np.array(upscaled).astype(np.float32) / 127.5 - 1.0


# Height in height-map bytes. Keep these low against the radii below: the terrain mesh carries one
# vertex per 4 map pixels, so anything narrower than ~40px and taller than it is wide comes out as a
# shard rather than a rock.
ISLET_HEIGHT = 30.0
STACK_HEIGHT = 34.0
EMERGENT_SHAPE = 0.35  # below this the disc is the island's underwater base, not land
BASE_REACH = 3.2       # how far the submarine shelf spreads, in island radii


def build_islets(rng, land):
    """Offshore islets, one or two per province, plus bare sea stacks.

    Positions are found rather than typed in: walk out from a seat until the coast is behind you,
    keep going into open water, and drop the islet where there is room for it. That way they follow
    whatever shape the coastline generator produced instead of drifting inland when it changes."""
    owned, stacks = [], []
    for index, (_, seat_x, seat_y, _, _, _) in enumerate(PROVINCES):
        # A fractional setting alternates: 1.5 gives every other province the second islet.
        wanted = int(ISLETS_PER_PROVINCE) + (1 if index % 2 == 0 and ISLETS_PER_PROVINCE % 1 else 0)
        for attempt in range(60):
            if wanted == 0:
                break

            angle = rng.uniform(0, 2 * np.pi)
            direction = np.array([np.cos(angle), np.sin(angle)])
            point = np.array([seat_x, seat_y], dtype=float)

            # Out to the coast first...
            while _is_land(land, point) and _inside(point):
                point += direction * 6
            if not _inside(point):
                continue

            # ...then a stretch of open sea, so the islet reads as separate from the mainland.
            point += direction * rng.uniform(40, 96)
            radius = rng.uniform(30, 58)
            if _has_clearance(land, point, radius + 10):
                owned.append((point[0], point[1], radius, index))
                wanted -= 1

    for _ in range(300):
        if len(stacks) >= STACK_COUNT:
            break
        point = np.array([rng.uniform(0, W), rng.uniform(0, H)])
        radius = rng.uniform(12, 22)
        if _has_clearance(land, point, radius + 26) and _near_coast(land, point, 140):
            stacks.append((point[0], point[1], radius))

    return owned, stacks


def seabed_base(distance, radius):
    """The submarine platform an island stands on, spread over several times the island's own width.

    Islands do not rise from the seabed as pillars: they sit on a shelf that shoals for a long way
    out. Keeping the base inside the island's own radius made a four-unit wall barely a unit wide —
    a grey slab visible straight through the shallow water."""
    reach = radius * BASE_REACH
    return np.clip(1.0 - distance / reach, 0, 1) ** 0.85 * (SEA_FLOOR_BYTE - 1.0)


def _inside(point):
    return 0 <= point[0] < W and 0 <= point[1] < H


def _is_land(land, point):
    return _inside(point) and land[int(point[1]), int(point[0])]


def _disc(point, radius):
    ys, xs = np.mgrid[0:H, 0:W]
    return ((xs - point[0]) ** 2 + (ys - point[1]) ** 2) <= radius ** 2


def _has_clearance(land, point, radius):
    if not _inside(point):
        return False
    return not land[_disc(point, radius)].any()


def _near_coast(land, point, distance):
    return land[_disc(point, distance)].any()


def ridged_noise(rng, cells, octaves=4):
    """Sum of folded noise: |n| flipped, so every octave leaves a crease instead of a blob. This
    is what turns rolling hills into ridges with spurs and gullies running off them."""
    total = np.zeros((H, W), dtype=np.float32)
    amplitude, weight = 1.0, 0.0
    for octave in range(octaves):
        layer = 1.0 - np.abs(smooth_noise(rng, cells * (2 ** octave)))
        total += layer ** 2 * amplitude
        weight += amplitude
        amplitude *= 0.5
    return total / weight


def build_terrain(rng, land, province_id, dist_to_seed, islet_field, islet_base):
    """Height field in world units, and the biome albedo painted from it."""
    # Distance from the coast, cheaply: a heavy blur of the land mask is ~0 at the shoreline
    # and ~1 deep inland, already smooth, no distance transform needed.
    inland = np.clip(blurred(land, 130.0) * 1.15, 0, 1) ** 0.85

    north = np.zeros_like(land, dtype=np.float32)
    for i, (_, _, _, region, _, _) in enumerate(PROVINCES):
        if region == 1:
            north[province_id == i] = 1.0

    # 0.5 sits exactly on the realm frontier, so folding the field at 0.5 gives a ridge that
    # runs the border and falls away on both sides.
    north_field = blurred(north, 115.0)
    ridge = np.clip(1.0 - np.abs(north_field * 2.0 - 1.0), 0, 1) ** 1.7

    seats = np.zeros_like(land, dtype=np.float32)
    for _, sx, sy, _, _, _ in PROVINCES:
        seats = np.maximum(seats, np.exp(-(((np.arange(W)[None, :] - sx) ** 2 +
                                            (np.arange(H)[:, None] - sy) ** 2) / (2 * 70.0 ** 2))))

    noise = 0.45 * smooth_noise(rng, 24) + 0.35 * smooth_noise(rng, 64) + 0.2 * smooth_noise(rng, 160)
    crests = ridged_noise(rng, 26, octaves=5)

    # Cliffs, not beaches: the ground leaves the water fast and then levels off, which is what gives
    # a coastline a silhouette instead of a ramp.
    shore_rise = np.clip(inland * 5.5, 0, 1) ** 0.55

    height = (
        INLAND_HEIGHT * shore_rise
        + RIDGE_HEIGHT * ridge * np.clip(inland * 1.6, 0, 1) * (0.35 + 0.65 * crests)
        # Spurs and gullies riding on the range, and on the northern highlands.
        + RIDGE_DETAIL * crests * np.clip(inland * 2.2, 0, 1) * (0.3 + 0.9 * ridge + 0.45 * north_field)
        + NORTH_LIFT * north_field * inland
        + SEAT_HEIGHT * seats
        + NOISE_HEIGHT * noise * inland
        # Islets get their own relief: the inland field barely registers a rock a few pixels wide.
        + 255.0 * islet_field * (0.85 + 0.15 * crests)
    )
    # Land sits above sea level; the water keeps a shelf that falls away from the coast, so depth
    # is a real number everywhere and the surf knows where the beach is.
    # The shelf only climbs to the waterline in the last stretch before the beach; further out it
    # stays deep, which is what gives the surf a band to roll across instead of a flat pan.
    shelf = (SEA_FLOOR_BYTE - 1.0) * np.clip(blurred(land, 32.0) * 1.9, 0, 1) ** 1.6
    # Under water, an islet still has a base: the seabed rises toward it, which gives the shallow
    # ring of lighter water around every island instead of a wall dropping out of nowhere.
    height = np.where(land,
                      SEA_FLOOR_BYTE + np.clip(height, 0.0, 255.0 - SEA_FLOOR_BYTE) * 0.82,
                      np.maximum(shelf, islet_base))
    # Soften the quantisation steps and the seams the blurs leave behind.
    height = np.array(Image.fromarray(height.astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(1.1))).astype(np.float32)

    # Slope drives where rock shows through: steep faces never hold grass or snow.
    gradient_y, gradient_x = np.gradient(height)
    slope = np.clip(np.sqrt(gradient_x ** 2 + gradient_y ** 2) / 6.0, 0, 1)

    # Thresholds are read off the generated field itself (median ~75, peaks ~235), so retuning
    # the height constants doesn't silently turn the whole map to snow.
    altitude = np.clip(height / 235.0, 0, 1)
    warm = np.clip(1.0 - north_field * 1.4, 0, 1)  # the south is green, the north is not

    albedo = GRASS_COLOR * warm[:, :, None] + TUNDRA_COLOR * (1.0 - warm)[:, :, None]

    # Woodland in patches, not as a wash: below the treeline, wherever the noise says so.
    forest = np.clip((noise + 0.15) * 2.2, 0, 1) * warm * np.clip(1.0 - (altitude - 0.3) * 3.0, 0, 1)
    albedo = albedo * (1 - forest[:, :, None]) + FOREST_COLOR * forest[:, :, None]

    # Steep ground can hold neither trees nor snow.
    bare = np.clip((slope - 0.25) * 1.8, 0, 1)
    albedo = albedo * (1 - bare[:, :, None]) + ROCK_COLOR * bare[:, :, None]

    snowline = np.clip((altitude - 0.55) * 4.0, 0, 1) * (1.0 - 0.8 * bare)
    albedo = albedo * (1 - snowline[:, :, None]) + SNOW_COLOR * snowline[:, :, None]

    beach = np.clip(1.0 - inland * 26.0, 0, 1)
    albedo = albedo * (1 - beach[:, :, None]) + SAND_COLOR * beach[:, :, None]
    # Stronger regional variation: real ground is never one flat tone across a whole province.
    albedo *= 1.0 + 0.16 * noise[:, :, None]

    # The water itself is the water shader's job; what the terrain paints under it is seabed, so
    # shallows read as sand through the surface and the deeps as dark silt.
    seabed_depth = np.clip((SEA_FLOOR_BYTE - height) / SEA_FLOOR_BYTE, 0, 1)[:, :, None]
    seabed = SAND_COLOR * (1.0 - seabed_depth) + np.array([44, 54, 60]) * seabed_depth
    albedo = np.where(land[:, :, None], np.clip(albedo, 0, 255), seabed)
    return height, albedo.astype(np.uint8)


def build_props(rng, land, height, slope, north_field, inland, province_id, islet_field):
    """Where the map grows things. One image, three channels, all 0..255 densities:
    R = woodland, G = boulders and outcrops, B = open ground worth farming. CampaignMap3D
    scatters instances against it with a fixed seed, so the same map always grows the same trees."""
    altitude = np.clip(height / 235.0, 0, 1)
    warm = np.clip(1.0 - north_field * 1.4, 0, 1)
    noise = 0.65 * smooth_noise(rng, 30) + 0.35 * smooth_noise(rng, 90)

    # Province edges, thickened: a treeline along the border is how the map shows where one
    # lord's writ ends without drawing a line on the ground.
    edge = np.zeros_like(land, dtype=bool)
    for dx, dy in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
        edge |= land & (province_id != shifted(province_id, dx, dy))
    border_band = blurred(edge, 9.0)

    # Woods in stands with open ground between them, not a blanket: fields and roads need somewhere
    # to be, and a solid canopy hides the terrain the player is reading.
    southern_woods = np.clip((noise - 0.05) * 1.5, 0, 1) * warm * np.clip(1.0 - (altitude - 0.28) * 3.2, 0, 1)
    southern_woods *= FOREST["southern_density"]
    # The north is not bare rock: it is taiga. Sparser than the southern woods and stopping at the
    # snow line, but the Northern Watch has forests of its own.
    northern_woods = np.clip((noise - 0.2) * 1.7, 0, 1) * (1.0 - warm) * np.clip(1.0 - (altitude - 0.46) * 3.0, 0, 1)
    northern_woods *= FOREST["northern_density"]
    treeline = border_band / max(border_band.max(), 1e-6)
    forest = np.clip(southern_woods + northern_woods, 0, 1)
    # Trees mark borders in both realms, but nothing grows on the snowy crest itself.
    forest = np.clip(forest + treeline * FOREST["treeline_strength"] * np.clip(1.0 - (altitude - 0.5) * 3.0, 0, 1), 0, 1)
    forest *= (1.0 - np.clip(slope * 1.2, 0, 1))
    # Islets are too small for the inland field to register, so they would come out bare rock:
    # give them their own stand of trees, thinner than the mainland's.
    islet_land = np.clip(islet_field * 255.0 / ISLET_HEIGHT, 0, 1)
    forest = np.clip(forest + islet_land * 0.5 * warm * (1.0 - np.clip(slope * 1.4, 0, 1)), 0, 1)
    forest *= np.clip(inland * 6.0, 0, 1)  # nothing grows on the beach itself

    boulders = np.clip((slope - 0.32) * 2.2, 0, 1) * np.clip(0.35 + altitude, 0, 1)
    boulders = np.clip(boulders + np.clip((altitude - 0.5) * 1.5, 0, 1) * 0.5, 0, 1)

    farmland = warm * np.clip(1.0 - slope * 3.5, 0, 1) * np.clip(1.0 - altitude * 2.2, 0, 1)
    farmland = np.clip(farmland - forest * 0.8, 0, 1) * np.clip(inland * 5.0, 0, 1)

    props = np.stack([forest, boulders, farmland], axis=2) * 255.0
    return np.where(land[:, :, None], props, 0).astype(np.uint8)


def build_roads(height, land, province_id, adjacency):
    """A road per pair of neighbouring provinces, from seat to seat, taking the cheapest ground:
    climbing costs, so roads bend around the ridge and cross it at the lowest saddle - the same
    line an army would have to walk."""
    step = 4  # pathfind on a quarter-scale grid; the result is smoothed anyway
    small_h = height[::step, ::step]
    small_land = land[::step, ::step]
    rows, cols = small_h.shape

    def node(px, py):
        return (min(py // step, rows - 1), min(px // step, cols - 1))

    roads = []
    for i, j in sorted(adjacency):
        start, goal = node(PROVINCES[i][1], PROVINCES[i][2]), node(PROVINCES[j][1], PROVINCES[j][2])
        best = {start: 0.0}
        came = {}
        queue = [(0.0, start)]
        while queue:
            cost, current = heapq.heappop(queue)
            if current == goal:
                break
            if cost > best.get(current, float("inf")):
                continue
            row, col = current
            for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                nr, nc = row + dr, col + dc
                if not (0 <= nr < rows and 0 <= nc < cols) or not small_land[nr, nc]:
                    continue
                climb = abs(float(small_h[nr, nc]) - float(small_h[row, col]))
                move = 1.4 if dr and dc else 1.0
                nudge = cost + move + climb * 2.5   # slope is what makes a road expensive
                if nudge < best.get((nr, nc), float("inf")):
                    best[(nr, nc)] = nudge
                    came[(nr, nc)] = current
                    heapq.heappush(queue, (nudge, (nr, nc)))

        if goal not in came and goal != start:
            continue

        path = [goal]
        while path[-1] != start:
            path.append(came[path[-1]])
        path.reverse()

        points = [[col * step, row * step] for row, col in path]
        for _ in range(6):  # smooth the staircase the grid leaves behind
            points = [points[0]] + [
                [(points[k - 1][0] + points[k][0] * 2 + points[k + 1][0]) / 4,
                 (points[k - 1][1] + points[k][1] * 2 + points[k + 1][1]) / 4]
                for k in range(1, len(points) - 1)
            ] + [points[-1]]
        roads.append({"from": PROVINCES[i][0], "to": PROVINCES[j][0],
                      "points": [[round(x, 1), round(y, 1)] for x, y in points[::2]]})

    # The trunk road: the chain of routes that actually joins the two capitals. It gets paved in
    # the game, the rest stay dirt tracks, so the map shows at a glance how an army would march
    # from one realm's seat to the other.
    trunk = capital_route(adjacency)
    for road in roads:
        pair = tuple(sorted((road["from"], road["to"])))
        road["kind"] = "paved" if pair in trunk else "dirt"

    return roads


def capital_route(adjacency):
    """Province-to-province hops along the shortest chain between the two capitals, as name pairs."""
    capitals = [index for index, province in enumerate(PROVINCES) if province[4]]
    if len(capitals) < 2:
        return set()

    neighbours = {index: set() for index in range(len(PROVINCES))}
    for i, j in adjacency:
        neighbours[i].add(j)
        neighbours[j].add(i)

    start, goal = capitals[0], capitals[1]
    came, queue = {start: None}, [start]
    while queue:
        current = queue.pop(0)
        if current == goal:
            break
        for neighbour in sorted(neighbours[current]):
            if neighbour not in came:
                came[neighbour] = current
                queue.append(neighbour)

    if goal not in came:
        return set()

    hops, node = set(), goal
    while came[node] is not None:
        hops.add(tuple(sorted((PROVINCES[node][0], PROVINCES[came[node]][0]))))
        node = came[node]

    return hops


def shifted(arr, dx, dy):
    out = np.roll(arr, (dy, dx), axis=(0, 1))
    if dy > 0: out[:dy, :] = arr[:1, :]
    if dy < 0: out[dy:, :] = arr[-1:, :]
    if dx > 0: out[:, :dx] = arr[:, :1]
    if dx < 0: out[:, dx:] = arr[:, -1:]
    return out


def main():
    rng = np.random.default_rng(7)

    mask_img = Image.new("L", (W, H), 0)
    ImageDraw.Draw(mask_img).polygon(LANDMASS, fill=255)
    # The traced outline is a straight-edged polygon, which reads as cut cardboard once the
    # shore is a real 3D edge: soften it and push the boundary around with noise so the coast
    # gains bays and headlands. Everything downstream (provinces, ID map, terrain) uses this.
    coast_noise = 0.7 * smooth_noise(rng, 26) + 0.3 * smooth_noise(rng, 70)
    land = (blurred(np.array(mask_img) > 0, 14.0) + 0.3 * coast_noise) > 0.5

    islets, stacks = build_islets(rng, land)
    islet_field = np.zeros((H, W), dtype=np.float32)
    islet_base = np.zeros((H, W), dtype=np.float32)
    islet_owner = np.full((H, W), -1, dtype=np.int32)
    ys, xs = np.mgrid[0:H, 0:W]
    for x, y, radius, owner in islets:
        distance = np.sqrt((xs - x) ** 2 + (ys - y) ** 2)
        shape = np.clip(1.0 - distance / radius, 0, 1)
        land |= shape > EMERGENT_SHAPE
        islet_field = np.maximum(islet_field, shape ** 1.5 * (ISLET_HEIGHT / 255.0))
        islet_base = np.maximum(islet_base, seabed_base(distance, radius))
        islet_owner = np.where(shape > EMERGENT_SHAPE, owner, islet_owner)
    for x, y, radius in stacks:
        distance = np.sqrt((xs - x) ** 2 + (ys - y) ** 2)
        shape = np.clip(1.0 - distance / radius, 0, 1)
        land |= shape > EMERGENT_SHAPE
        islet_field = np.maximum(islet_field, shape ** 1.3 * (STACK_HEIGHT / 255.0))
        islet_base = np.maximum(islet_base, seabed_base(distance, radius))

    print(f"Islets: {len(islets)} owned, {len(stacks)} bare stacks")

    best_dist = np.full((H, W), np.inf)
    province_id = np.full((H, W), -1, dtype=np.int32)
    for i, (_, sx, sy, _, _, _) in enumerate(PROVINCES):
        d = (xs - sx) ** 2 + (ys - sy) ** 2
        closer = d < best_dist
        best_dist = np.where(closer, d, best_dist)
        province_id = np.where(closer, i, province_id)
    province_id = np.where(land, province_id, -1)
    province_id = np.where(islet_owner >= 0, islet_owner, province_id)

    fill = np.zeros((H, W, 3), dtype=np.uint8)
    for i in range(len(PROVINCES)):
        fill[province_id == i] = province_color(i)

    # Cheap "painted" shading so it doesn't read as flat vector fill: a radial glow toward
    # each province's seat, plus soft low-frequency noise for texture.
    dist_to_seed = np.sqrt(best_dist)
    radial = np.clip(1.22 - 0.4 * np.clip(dist_to_seed / 300.0, 0, 1), 0.75, 1.22)

    noise_small = rng.uniform(-1, 1, size=(48, 32)).astype(np.float32)
    noise = np.array(
        Image.fromarray(((noise_small + 1) * 127.5).astype(np.uint8)).resize((W, H), Image.BICUBIC)
    ).astype(np.float32) / 127.5 - 1.0

    shaded = fill.astype(np.float32) * radial[:, :, None] * (1.0 + 0.07 * noise[:, :, None])
    fill = np.clip(shaded, 0, 255).astype(np.uint8)
    water_shaded = np.clip(WATER_COLOR[None, None, :] * (1.0 + 0.1 * noise[:, :, None]), 0, 255).astype(np.uint8)
    fill = np.where(land[:, :, None], fill, water_shaded)

    fill_id = np.zeros((H, W, 3), dtype=np.uint8)
    for i in range(len(PROVINCES)):
        # R = index + 1 (0, untouched, decodes as "no province" in C#); G = realm + 1, which is
        # what the terrain shader compares to draw a gold frontier instead of a hairline border.
        fill_id[province_id == i] = (i + 1, PROVINCES[i][5] + 1, 0)

    drawn = fill.copy()
    adjacency = set()
    inner_seam = np.zeros((H, W), dtype=bool)
    frontier_seam = np.zeros((H, W), dtype=bool)

    for dx, dy in [(1, 0), (0, 1)]:
        nb_id = shifted(province_id, dx, dy)
        nb_land = shifted(land, dx, dy)

        diff_province = land & nb_land & (province_id != nb_id)
        same_realm = np.zeros((H, W), dtype=bool)
        diff_realm = np.zeros((H, W), dtype=bool)
        for i in range(len(PROVINCES)):
            for j in range(len(PROVINCES)):
                if i == j:
                    continue
                pair_mask = diff_province & (province_id == i) & (nb_id == j)
                if not pair_mask.any():
                    continue
                adjacency.add(tuple(sorted((i, j))))
                if PROVINCES[i][5] == PROVINCES[j][5]:
                    same_realm |= pair_mask
                else:
                    diff_realm |= pair_mask

        coast = land & ~nb_land

        drawn[coast] = COAST_COLOR
        drawn[same_realm] = BORDER_COLOR
        # Thicken the frontier a bit so it reads as a real border, not a hairline.
        frontier_thick = diff_realm.copy()
        for ddx, ddy in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
            frontier_thick |= shifted(diff_realm, ddx, ddy)
        drawn[frontier_thick & land] = FRONTIER_COLOR
        inner_seam |= same_realm
        frontier_seam |= frontier_thick

    height, albedo = build_terrain(rng, land, province_id, dist_to_seed, islet_field, islet_base)

    # No borders baked into the terrain colour: the shader draws them from the ID map, which keeps
    # them a clean line however close the camera gets (seams stay in the flat drawn map, which the
    # sidebar minimap still uses).

    print(f"Map: {W}x{H}, {len(LANDMASS)} outline points, {len(PROVINCES)} provinces")
    print(f"Terrain: ridge {TERRAIN['ridge_height']:.0f} (+{TERRAIN['ridge_detail']:.0f} detail), "
          f"inland {TERRAIN['inland_height']:.0f}, sea level byte {SEA_FLOOR_BYTE:.0f}")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    Image.fromarray(drawn, "RGB").save(OUT_DIR / "map-drawn.png")
    Image.fromarray(fill_id, "RGB").save(OUT_DIR / "map-ids.png")
    Image.fromarray(height.astype(np.uint8), "L").save(OUT_DIR / "map-height.png")
    Image.fromarray(albedo, "RGB").save(OUT_DIR / "map-albedo.png")

    gradient_y, gradient_x = np.gradient(height)
    slope = np.clip(np.sqrt(gradient_x ** 2 + gradient_y ** 2) / 6.0, 0, 1)
    inland = np.clip(blurred(land, 130.0) * 1.15, 0, 1) ** 0.85
    north = np.zeros_like(land, dtype=np.float32)
    for i, (_, _, _, region, _, _) in enumerate(PROVINCES):
        if region == 1:
            north[province_id == i] = 1.0
    north_field = blurred(north, 115.0)

    props = build_props(rng, land, height, slope, north_field, inland, province_id, islet_field)
    Image.fromarray(props, "RGB").save(OUT_DIR / "map-props.png")

    roads = build_roads(height, land, province_id, adjacency)
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    (DATA_DIR / "map-roads.json").write_text(json.dumps(roads, indent=1))
    print(f"\nRoads: {len(roads)} routes, {sum(len(r['points']) for r in roads)} points")

    print("Adjacency:")
    for i, j in sorted(adjacency):
        print(f"  {PROVINCES[i][0]} <-> {PROVINCES[j][0]}")


if __name__ == "__main__":
    main()
