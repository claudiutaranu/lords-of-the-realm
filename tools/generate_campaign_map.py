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


def knobs(data, section, **defaults):
    """Reads a block of hand-edited settings. A missing key falls back to its default and says so,
    rather than stopping the run: these files are meant to be typed in by hand, and losing a whole
    rebuild over one absent line is not worth the strictness."""
    block = dict(defaults)
    block.update(data.get(section, {}))

    filled = [key for key in defaults if key not in data.get(section, {})]
    if filled:
        print(f"map.json: \"{section}\" has no {', '.join(filled)} — using defaults")

    unknown = [key for key in block if key not in defaults]
    if unknown:
        print(f"map.json: \"{section}\" has settings this tool does not know: {', '.join(unknown)}")

    return block

W, H = MAP["size"]
LANDMASS = [tuple(point) for point in MAP["outline"]]

TERRAIN = knobs(MAP, "terrain", sea_floor_byte=46.0, ridge_height=190.0, ridge_detail=96.0,
                inland_height=44.0, north_lift=26.0, seat_height=16.0, noise_height=26.0,
                lowland_detail=0.08, outcrop_height=0.0, outcrop_cover=0.0, border_warp=0.0,
                coast_rock_height=0.0, coast_rock_width=3.0, coast_bays=0.0)
SEA_FLOOR_BYTE = TERRAIN["sea_floor_byte"]
RIDGE_HEIGHT = TERRAIN["ridge_height"]
RIDGE_DETAIL = TERRAIN["ridge_detail"]
INLAND_HEIGHT = TERRAIN["inland_height"]
NORTH_LIFT = TERRAIN["north_lift"]
SEAT_HEIGHT = TERRAIN["seat_height"]
NOISE_HEIGHT = TERRAIN["noise_height"]
# How much of the ridge detail reaches ground that is neither mountain nor northern highland. Crests
# and gullies belong to the mountains; spread across the lowlands they only crumple the fields the
# player is trying to read.
LOWLAND_DETAIL = TERRAIN["lowland_detail"]
# Crags standing out of the grass, as the reference island has them: how tall they stand in height
# bytes, and what fraction of the land they cover. Broad, not spiky — see the note on mesh
# resolution by the island heights.
OUTCROP_HEIGHT = TERRAIN["outcrop_height"]
OUTCROP_COVER = TERRAIN["outcrop_cover"]
# How far a province border wanders off the straight line between two seats, in map pixels. A raster
# Voronoi draws borders as perfectly straight bisectors, which no country has.
BORDER_WARP = TERRAIN["border_warp"]
# The rocky rim. A cliff alone is a wall, and a wall seen from a camera above it is only its top
# edge — a dark line round the island. What reads from above is a band of broken rock between the
# grass and the sea. Height is how rough that band is; width is how far in it reaches, as a divisor
# of the coast field (larger is narrower).
COAST_ROCK_HEIGHT = TERRAIN["coast_rock_height"]
# How deep the coves bite, in map pixels.
COAST_BAYS = TERRAIN["coast_bays"]
# The ditch dug along every border between counties, and the crossings left in it. In height bytes
# (a tenth of a world unit each) and map pixels (eight to a world unit).
DITCH_DEPTH = 14.0         # how deep it is dug at its middle
DITCH_WIDTH = 4.5          # the blur that shapes its cross-section; wider is a broader, softer trench
# The earth thrown up either side. Seen from above a trench alone is a thin dark line; the lit bank
# beside the shadowed cut is what makes it read as dug.
BANK_HEIGHT = 5.0
DITCH_BLOCKS = 11          # how far either side of the line an army cannot cross, in pixels — wider
                           # than the march grid's twelve-pixel cell, so no row of cells slips through
CROSSING_REACH = 24.0      # the gap left at a crossing, in pixels from its middle
FORD_SPACING = 260.0       # no stretch of border longer than this goes without a crossing

# How far the land stands clear of the water at the shore, and how deep the sea is right against it,
# in height bytes (a tenth of a world unit each). Together they are the step the surf breaks on.
FREEBOARD = 4.0
DRAFT = 3.0
# A wall, for the surface map: this many bytes of height lost within WALL_REACH pixels. Every shore
# steps FREEBOARD + DRAFT (seven) at the waterline, so a beach must stay well under it.
WALL_DROP = 14.0
WALL_REACH = 3
# The steepest the land may rise out of the sea, in bytes of height per pixel inland: about 73
# degrees at this map's scale, a cliff, but one spread over enough vertices to have a face.
COAST_SLOPE = 4.0
# A road in the surface map, in pixels: solid track for ROAD_HALF_WIDTH either side of the centre
# line, then worn into the grass over ROAD_FEATHER. Terrain3D has a vertex every two pixels, so
# anything narrower than this is lost between them.
ROAD_HALF_WIDTH = 2.0
ROAD_FEATHER = 2.5
# Below this the road is not painted at all.
ROAD_TRACE = 0.04
# The colour map under a road: neutral, so the dirt keeps its own colour (it only sets the hue).
ROAD_TINT = (140.0, 140.0, 138.0)
# How far above the water a beach may climb, in bytes, before it has gone back to turf.
BEACH_RISE = 7.0
# How far in from the edge of the image the land has to have given way to sea, in map pixels. The
# shelf round the island needs room too, or it meets the edge of the map as a straight line.
MAP_MARGIN = 64.0
COAST_ROCK_WIDTH = TERRAIN["coast_rock_width"]

ISLANDS = knobs(MAP, "islands", islet_height=30.0, stack_height=34.0, emergent_shape=0.35,
                base_reach=3.2, stack_count=26, islets_per_province=1.5)
ISLET_HEIGHT = ISLANDS["islet_height"]
STACK_HEIGHT = ISLANDS["stack_height"]
EMERGENT_SHAPE = ISLANDS["emergent_shape"]
BASE_REACH = ISLANDS["base_reach"]

# How a route is cut into the ground it crosses, in map pixels of half-width, and how strongly.
CARVE_RADIUS_DIRT = 12
CARVE_RADIUS_PAVED = 17
CARVE_STRENGTH = 0.85
CARVE_VERGE = 44        # how far the cutting's bank is graded out into the country it crosses
CARVE_VERGE_FULL = 24   # and how far of that is graded completely, before it starts to fade
CARVE_VERGE_BLUR = 11.0
GRADE_PASSES = 3
# Shortcuts kept on top of the minimum network that reaches every seat.
EXTRA_ROUTES = 2

# Map pixels of clearing either side of a route, before the edges are softened.
ROAD_CLEAR_DIRT = 13
ROAD_CLEAR_PAVED = 19
STACK_COUNT = int(ISLANDS["stack_count"])
# Offshore islands per province. 0 here and 0 stacks leaves a clean coastline.
ISLETS_PER_PROVINCE = float(ISLANDS["islets_per_province"])

FOREST = knobs(MAP, "forest", southern_density=0.72, northern_density=1.0, treeline_strength=0.8)

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
GRASS_COLOR = np.array([78, 108, 56])    # meadow; the grass texture supplies the green
FOREST_COLOR = np.array([44, 60, 40])
ROCK_COLOR = np.array([96, 100, 110])    # cold blue-grey stone
TUNDRA_COLOR = np.array([116, 124, 112])   # cold drab ground, not a second snow line
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


# The island heights live in map.json's "islands" block, read above. They used to be re-declared
# here, which quietly threw that block away: editing the campaign's own file changed nothing.
#
# Keep them low against the radii: the terrain mesh carries one vertex per 4 map pixels, so anything
# narrower than ~40px and taller than it is wide comes out as a shard rather than a rock.


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


# How much level ground each seat keeps for its fields, in map pixels. The fields are laid out on a
# lattice round the seat (MapDecoration.PlotSites: seven rings of cells, outside the town's own
# ring), so this is the reach of that lattice with a margin. The ground inside is eased down to the broad lie of the land — its crags
# and hummocks taken out — because a field is only laid where the ground is level enough, and a
# county whose seat stands in broken country had almost nowhere to put one.
FIELD_GROUND = 168.0
FIELD_GROUND_EDGE = 38.0


def masked_blur(values, mask, sigma):
    """A blur of `values` that only listens to pixels inside `mask`. Near a coast an ordinary blur
    pulls the seabed up into the fields; this averages the land alone, so a seat by the sea keeps its
    ground at the height of the land around it."""
    inside = mask.astype(np.float32)
    total = blurred_height(np.clip(values * inside, 0, 255), sigma)
    weight = blurred_height(inside * 255.0, sigma) / 255.0
    return np.where(weight > 0.02, total / np.maximum(weight, 1e-6), values)


def level_seats(height, land):
    """Terraces the ground round every seat for its fields.

    Smoothing alone was not enough: it takes the crags out, but a seat on a mountainside keeps the
    mountainside, and a field cannot be laid across one. So the ground is brought most of the way to
    one level — the lie of the land at the seat itself — with a little of the slope left in it so it
    does not read as a disc stamped flat, and eased back into the country round it at the edge. On
    a slope that is a terrace cut into the hill, which is what farmed hillsides are.
    """
    ys, xs = np.mgrid[0:H, 0:W]
    calm = masked_blur(height, land, 26.0)
    target = height.copy()
    level = np.zeros((H, W), dtype=np.float32)
    for _, sx, sy, _, _, _ in PROVINCES:
        distance = np.sqrt((xs - sx) ** 2 + (ys - sy) ** 2)
        weight = np.clip((FIELD_GROUND - distance) / FIELD_GROUND_EDGE, 0, 1)
        weight = weight * weight * (3.0 - 2.0 * weight)
        plateau = calm[int(sy), int(sx)]
        nearer = weight > level
        target = np.where(nearer, plateau * 0.9 + calm * 0.1, target)
        level = np.maximum(level, weight)
    level[~land] = 0.0
    return height * (1.0 - level) + target * level


def crag_field(rng, land):
    """Where bare rock pushes up through the grass, as 0..1. Ridged noise with its top slice kept:
    that leaves isolated crags with open ground between them rather than a crumpled plain.

    Two sizes, because an outcrop field has two: the broad shoulders of rock a hillside is built
    on, and the individual crags standing out of the grass between them. They were one size and a
    hundred pixels across, which at this map's scale is not a crag but a rise.

    The mesh carries a vertex every two map pixels, so anything down to about eight of them is
    drawn; these run from roughly fifty down to twenty. The threshold is read off the field itself,
    so OUTCROP_COVER means the share of the land that ends up rock whatever the noise happens to
    look like this run."""
    if OUTCROP_COVER <= 0.0:
        return np.zeros((H, W), dtype=np.float32)

    broad = ridged_noise(rng, 16, octaves=2)
    sharp = ridged_noise(rng, 34, octaves=2)
    field = np.maximum(broad, sharp * 0.92)
    cut = float(np.quantile(field[land], 1.0 - min(OUTCROP_COVER, 0.95)))
    # A short ramp is what makes a crag a crag: over a long one it comes out as a swell of ground
    # with no face on it anywhere, and the rock the control map paints needs a face to sit on.
    return np.clip((field - cut) / 0.075, 0, 1) ** 0.75


def build_terrain(rng, land, province_id, dist_to_seed, islet_field, islet_base):
    """The height field, plus the fields the albedo pass needs to paint it afterwards.

    Height comes first on its own because the roads are routed over it and then cut into it: a
    mountain road belongs in a pass it has carved, not draped over the cliffs it happens to cross.
    The colour is painted last, from the carved ground, so scree and snow follow the cutting."""
    # Distance from the coast, cheaply: a heavy blur of the land mask, recentred. A blur alone is
    # not it — over a straight shoreline it reads 0.5, not 0, so everything keyed to this field
    # (the cliffs, the beach, the treeline, where the range is allowed to stand) came out saturated
    # at 1 and did nothing at all. Halved and doubled, it is 0 on the waterline and 1 well inland.
    inland = np.clip((blurred(land, 130.0) - 0.5) * 2.0, 0, 1) ** 0.85

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
    # Crags, kept off the beach so none of them stands in the surf.
    crags = crag_field(rng, land) * np.clip(inland * 6.0, 0, 1)
    # A ring just inside the coast, broken up by the same creased noise the mountains are made of,
    # so the rim is a jumble of rock rather than a smooth band.
    coast_band = np.clip(1.0 - inland * COAST_ROCK_WIDTH, 0, 1) * np.clip(inland * 30.0, 0, 1)

    # Cliffs, not beaches: the ground leaves the water fast and then levels off, which is what gives
    # a coastline a silhouette instead of a ramp. Full height within about fifteen pixels of the
    # waterline, which at this map's scale is steep enough that the shader paints it as the rock it
    # is cut from and leaves sand only in the bays.
    # How far in the cliff runs before the land levels off. It has to span several of the terrain's
    # vertices at the coarse LOD the far coasts are drawn at, or it is drawn as a row of saw teeth
    # where it meets the water: at a vertex a unit apart, a cliff fifteen pixels wide is two
    # vertices. Twice that is drawn clean, and seen from a camera above it reads better too — a
    # sheer wall shows only its top edge, a steep rocky slope shows its rock.
    # And the curve is an S, not a power: flat where it leaves the water, steep through the middle,
    # flat again on top. A power curve is at its STEEPEST at the waterline, and a steep foot on a
    # coarse vertex grid is exactly what aliases into teeth; an S meets the sea at no slope at all.
    rise = np.clip(inland * 6.0, 0, 1)
    shore_rise = rise * rise * (3.0 - 2.0 * rise)

    height = (
        INLAND_HEIGHT * shore_rise
        + RIDGE_HEIGHT * ridge * np.clip(inland * 4.0, 0, 1) * (0.35 + 0.65 * crests)
        # Spurs and gullies riding on the range, and on the northern highlands.
        + RIDGE_DETAIL * crests * np.clip(inland * 4.0, 0, 1) * (LOWLAND_DETAIL + 1.0 * ridge + 0.5 * north_field)
        + OUTCROP_HEIGHT * crags
        + COAST_ROCK_HEIGHT * crests * coast_band
        + NORTH_LIFT * north_field * inland
        + SEAT_HEIGHT * seats
        + NOISE_HEIGHT * noise * inland
    )

    # An islet is not a piece of the mainland: applying the mainland's relief to it added the whole
    # inland lift on top of the islet's own few metres, so every rock stood on a plateau whose sides
    # dropped as a sheer wall to the seabed. Offshore rock gets its own height and nothing else.
    islet_relief = 255.0 * islet_field * (0.85 + 0.15 * crests)
    is_islet = islet_field > 0
    height = np.where(is_islet, islet_relief + NOISE_HEIGHT * 0.3 * noise, height)
    # Land sits above sea level; the water keeps a shelf that falls away from the coast, so depth
    # is a real number everywhere and the surf knows where the beach is.
    # The shelf only climbs to the waterline in the last stretch before the beach; further out it
    # stays deep, which is what gives the surf a band to roll across instead of a flat pan.
    # A wide shallow shelf, not a step. At a 32-pixel blur the sea went from ankle deep to four
    # units in forty pixels, which is half a degree of screen: there was nowhere for the band of
    # turquoise a coast is read by to be drawn. This carries it a good hundred and fifty pixels out.
    shelf = (SEA_FLOOR_BYTE - 1.0) * np.clip(blurred(land, 90.0) * 2.4, 0, 1) ** 1.3
    # Under water, an islet still has a base: the seabed rises toward it, which gives the shallow
    # ring of lighter water around every island instead of a wall dropping out of nowhere.
    # The summits used to be cut off flat against the top of the byte — four per cent of the land
    # came out as one level plateau, which the snow then painted as a sheet of paper laid over the
    # range. Below three quarters of the range nothing is touched; above it the ground eases into
    # the ceiling instead of hitting it, so a peak keeps its shape however tall the knobs are set.
    room = 255.0 - SEA_FLOOR_BYTE
    knee = room * 0.72
    over = np.maximum(height - knee, 0.0)
    height = np.minimum(np.maximum(height, 0.0), knee) + (room - knee) * (1.0 - np.exp(-over / (room - knee)))

    height = np.where(land,
                      SEA_FLOOR_BYTE + height * 0.82,
                      np.maximum(shelf, islet_base))
    # Soften the quantisation steps and the seams the blurs leave behind.
    height = np.array(Image.fromarray(height.astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(1.1))).astype(np.float32)

    # And level ground for the fields, last of all so nothing added after it can put a crag back.
    height = level_seats(height, land)

    return height, {"north_field": north_field, "inland": inland, "noise": noise,
                    "coast_band": coast_band}


def carve_roads(height, roads, land):
    """Cuts every route into the ground: a graded profile along the road, blended into the terrain.

    Each route's own elevation is smoothed along its length, then painted back into the map over a
    corridor. Where it crosses a ridge that leaves a pass; where it crosses a dip, an embankment.
    Without this a road in the mountains is a ribbon lying across cliffs, which is exactly what it
    looked like."""
    target = np.zeros((H, W), dtype=np.float32)
    weight = np.zeros((H, W), dtype=np.float32)
    ys, xs = np.mgrid[0:H, 0:W]

    for road in roads:
        points = [(float(x), float(y)) for x, y in road["points"]]
        elevations = [float(height[min(int(y), H - 1), min(int(x), W - 1)]) for x, y in points]

        # A road climbs at a steady grade rather than following every hump under it.
        smoothed = list(elevations)
        for _ in range(GRADE_PASSES):
            smoothed = [
                sum(smoothed[max(i - 3, 0):min(i + 4, len(smoothed))]) / len(smoothed[max(i - 3, 0):min(i + 4, len(smoothed))])
                for i in range(len(smoothed))
            ]

        radius = CARVE_RADIUS_PAVED if road["kind"] == "paved" else CARVE_RADIUS_DIRT
        for (x, y), elevation in zip(points, smoothed):
            left, right = max(int(x - radius), 0), min(int(x + radius) + 1, W)
            top, bottom = max(int(y - radius), 0), min(int(y + radius) + 1, H)
            if left >= right or top >= bottom:
                continue

            distance = np.sqrt((xs[top:bottom, left:right] - x) ** 2 + (ys[top:bottom, left:right] - y) ** 2)
            local = np.clip(1.0 - distance / radius, 0, 1) ** 1.5
            target[top:bottom, left:right] += local * elevation
            weight[top:bottom, left:right] += local

    cut = np.divide(target, np.maximum(weight, 1e-6))
    # The corridor's influence, softened so the cutting has shoulders instead of vertical sides.
    blend = np.clip(weight, 0, 1)
    blend = np.array(Image.fromarray((blend * 255).astype(np.uint8), "L")
                     .filter(ImageFilter.GaussianBlur(6.0))).astype(np.float32) / 255.0
    blend = np.clip(blend * 1.5, 0, 1) * CARVE_STRENGTH

    carved = height * (1.0 - blend) + cut * blend

    # The cutting's own edge is the steepest ground for a long way either side, and paint_albedo
    # answers steep ground with bare rock: every lane came out running between two raw scars.
    # So the bank is graded away — the road keeps its level bed and rises to the country it crosses
    # over a shoulder wide enough that nothing on it reads as a slope.
    # Fully graded across the edge of the cut and only then eased off. A verge that was already
    # fading where the cut ends left part of the step standing — at the relief this map has now,
    # enough of one to be a ditch down both sides of every road.
    verge = np.clip((CARVE_VERGE - distance_to_roads(roads)) / (CARVE_VERGE - CARVE_VERGE_FULL), 0, 1)
    verge = verge * verge * (3.0 - 2.0 * verge)
    # A blur that reaches the water pulls the seabed up into the shore. No road is graded near it.
    verge[blurred_height((~land) * 255.0, CARVE_VERGE) > 2.0] = 0.0
    return carved * (1.0 - verge) + blurred_height(carved, CARVE_VERGE_BLUR) * verge


def dig_borders(height, land, province_id, roads, adjacency):
    """Digs a ditch along every border between counties and leaves crossings in it.

    Where a road crosses a border there is a crossing, because that is what the road is for. Where
    two counties touch with no road between them, there is a ford at the point of their border
    nearest the line between their seats, so no neighbour is ever walled off from another. And on a
    long border, more fords, spaced so that no stretch longer than FORD_SPACING is all ditch.

    Not in the mountains: above the treeline a border is already a wall of rock, and a trench dug
    across the peaks reads as a scar rather than a boundary.

    Returns the height with the ditch dug, the ditch's visible shape (0..1) for the surface map, the
    ground an army cannot cross — which is what the game reads, so what is dug and what blocks can
    never disagree — and the crossings themselves.
    """
    ys, xs = np.mgrid[0:H, 0:W]
    line = np.zeros((H, W), dtype=bool)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        other = shifted(province_id, dx, dy)
        line |= land & (other >= 0) & (other != province_id)

    crossings = []

    def add(x, y, a, b, kind):
        crossings.append({"x": round(float(x), 1), "y": round(float(y), 1),
                          "between": sorted([PROVINCES[a][0], PROVINCES[b][0]]), "kind": kind})

    # Every road, where it passes from one county into the next.
    for road in roads:
        points = road["points"]
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            a = province_id[min(int(y0), H - 1), min(int(x0), W - 1)]
            b = province_id[min(int(y1), H - 1), min(int(x1), W - 1)]
            if a >= 0 and b >= 0 and a != b:
                add((x0 + x1) / 2, (y0 + y1) / 2, a, b, "road")

    # A ford on every border, and more along the long ones.
    for a, b in sorted(adjacency):
        pair = line & (((province_id == a) & np.logical_or.reduce(
            [shifted(province_id, dx, dy) == b for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))])))
        py, px = np.nonzero(pair)
        if len(px) == 0:
            continue
        points = np.stack([px, py], axis=1).astype(np.float32)
        existing = [np.array([c["x"], c["y"]]) for c in crossings
                    if c["between"] == sorted([PROVINCES[a][0], PROVINCES[b][0]])]
        if not existing:
            middle = np.array([(PROVINCES[a][1] + PROVINCES[b][1]) / 2, (PROVINCES[a][2] + PROVINCES[b][2]) / 2])
            pick = points[np.argmin(((points - middle) ** 2).sum(axis=1))]
            add(pick[0], pick[1], a, b, "ford")
            existing.append(pick)
        while True:
            far = np.min(np.stack([np.sqrt(((points - e) ** 2).sum(axis=1)) for e in existing]), axis=0)
            if far.max() <= FORD_SPACING:
                break
            pick = points[np.argmax(far)]
            add(pick[0], pick[1], a, b, "ford")
            existing.append(pick)

    gap = np.full((H, W), 1e9, dtype=np.float32)
    for c in crossings:
        np.minimum(gap, np.sqrt((xs - c["x"]) ** 2 + (ys - c["y"]) ** 2), out=gap)
    open_ground = np.clip((gap - CROSSING_REACH * 0.6) / (CROSSING_REACH * 0.4), 0, 1)

    altitude = np.clip(height / 235.0, 0, 1)
    lowland = np.clip(1.0 - (altitude - 0.55) * 6.0, 0, 1)

    # The trench itself: the border line blurred into a soft U, scaled so its middle is full depth.
    shape = blurred(line, DITCH_WIDTH)
    peak = float(np.percentile(shape[line], 90)) if line.any() else 1.0
    ditch = np.clip(shape / max(peak, 1e-6), 0, 1) ** 1.4 * open_ground * lowland * land
    # The banks: a wider, softer version of the same line with the trench taken out of its middle,
    # which leaves a ridge running either side.
    wide = blurred(line, DITCH_WIDTH * 2.6)
    wide_peak = float(np.percentile(wide[line], 90)) if line.any() else 1.0
    bank = np.clip(np.clip(wide / max(wide_peak, 1e-6), 0, 1) - ditch * 1.3, 0, 1) * open_ground * lowland * land
    dug = height - DITCH_DEPTH * ditch + BANK_HEIGHT * bank

    # Where an army may not cross: a band either side of the line, wider than a march cell, except at
    # the crossings — and only where the ditch was actually dug.
    band = line.copy()
    for _ in range(DITCH_BLOCKS):
        grown = band.copy()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            grown |= shifted(band, dx, dy)
        band = grown
    blocked = band & land & (gap > CROSSING_REACH) & (lowland > 0.5)

    return dug, np.clip(ditch + bank * 0.45, 0, 1), blocked, crossings


def road_strength(roads):
    """How much of each pixel is road: 1 along the track, falling to 0 over ROAD_FEATHER past its
    edge. Measured to the segments, not to the points — they are nine pixels apart, and a track
    narrower than that measured to its points is a string of beads."""
    ys, xs = np.mgrid[0:H, 0:W]
    reach = ROAD_HALF_WIDTH + ROAD_FEATHER
    distance = np.full((H, W), reach, dtype=np.float32)
    for road in roads:
        points = [(float(x), float(y)) for x, y in road["points"]]
        for (ax, ay), (bx, by) in zip(points, points[1:]):
            left, right = max(int(min(ax, bx) - reach), 0), min(int(max(ax, bx) + reach) + 2, W)
            top, bottom = max(int(min(ay, by) - reach), 0), min(int(max(ay, by) + reach) + 2, H)
            px, py = xs[top:bottom, left:right], ys[top:bottom, left:right]
            dx, dy = bx - ax, by - ay
            along = np.clip(((px - ax) * dx + (py - ay) * dy) / max(dx * dx + dy * dy, 1e-6), 0, 1)
            local = np.hypot(px - (ax + along * dx), py - (ay + along * dy))
            np.minimum(distance[top:bottom, left:right], local, out=distance[top:bottom, left:right])
    return np.clip((reach - distance) / ROAD_FEATHER, 0, 1)


def distance_to_roads(roads):
    """Pixels from each point of the map to the nearest road centre line, out to CARVE_VERGE."""
    ys, xs = np.mgrid[0:H, 0:W]
    distance = np.full((H, W), float(CARVE_VERGE), dtype=np.float32)
    for road in roads:
        for x, y in road["points"]:
            left, right = max(int(x - CARVE_VERGE), 0), min(int(x + CARVE_VERGE) + 1, W)
            top, bottom = max(int(y - CARVE_VERGE), 0), min(int(y + CARVE_VERGE) + 1, H)
            local = np.sqrt((xs[top:bottom, left:right] - x) ** 2 + (ys[top:bottom, left:right] - y) ** 2)
            np.minimum(distance[top:bottom, left:right], local, out=distance[top:bottom, left:right])

    return distance


def blurred_height(height, sigma):
    return np.array(Image.fromarray(np.clip(height, 0, 255).astype(np.uint8), "L")
                    .filter(ImageFilter.GaussianBlur(sigma))).astype(np.float32)


def paint_albedo(height, fields, land, rng):
    """The biome colour, painted from the finished ground."""
    north_field, inland, noise = fields["north_field"], fields["inland"], fields["noise"]

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
    bare = np.clip((slope - 0.16) * 2.2, 0, 1)
    albedo = albedo * (1 - bare[:, :, None]) + ROCK_COLOR * bare[:, :, None]

    snowline = np.clip((altitude - 0.72) * 5.0, 0, 1) * (1.0 - 0.8 * bare)
    albedo = albedo * (1 - snowline[:, :, None]) + SNOW_COLOR * snowline[:, :, None]

    # Sand collects in the bays, not on the headlands: where the ground leaves the water steeply
    # the coast is the rock it is cut from, which is what gives an island a silhouette.
    beach = np.clip(1.0 - inland * 26.0, 0, 1) * (1.0 - bare)
    albedo = albedo * (1 - beach[:, :, None]) + SAND_COLOR * beach[:, :, None]
    # Stronger regional variation: real ground is never one flat tone across a whole province.
    albedo *= 1.0 + 0.16 * noise[:, :, None]

    # The water itself is the water shader's job; what the terrain paints under it is seabed, so
    # shallows read as sand through the surface and the deeps as dark silt.
    seabed_depth = np.clip((SEA_FLOOR_BYTE - height) / SEA_FLOOR_BYTE, 0, 1)[:, :, None]
    # Wet sand, not dry: the beach colour seen through a metre of water was a white glare once the
    # sea was given a shelf wide enough to see the bottom of.
    seabed = SAND_COLOR * 0.74 * (1.0 - seabed_depth) + np.array([38, 50, 58]) * seabed_depth
    albedo = np.where(land[:, :, None], np.clip(albedo, 0, 255), seabed)
    return albedo.astype(np.uint8)


def build_surface(height, slope, fields, land, road):
    """Which ground texture covers every pixel, and how much of the one under it shows through.

    This is what Terrain3D calls a control map, written as an ordinary image so it can be looked at:
    R = the texture underneath, G = the one laid over it, B = how much of the overlay there is. The
    game packs those three into the bit field the plugin wants — see Terrain3DGround.AsControl.

    Before this the terrain chose its own surfaces by slope alone and everything else was painted on
    afterwards as flat colour in the shader: snow was a white wash and the beach a tan one, neither
    of them a texture at all, and rock above the treeline was the cliff sampled by hand and mixed
    in. All of that is decided here now, from the same fields the albedo is painted from, and the
    plugin blends them by their own height the way it blends everything else.

    Slots, matching Terrain3DGround: 0 grass, 1 cliff, 2 snow, 3 sand, 4 path.
    """
    inland = fields["inland"]
    altitude = np.clip(height / 235.0, 0, 1)

    # Rock: on anything with a face to it, and on everything above the treeline whatever its face.
    # The second half is the point — a mountain's broad shoulders are stone, and judging them on
    # slope alone drew them as meadow.
    steep = np.clip((slope - 0.20) * 2.6, 0, 1)
    high = np.clip((altitude - 0.56) * 4.5, 0, 1)
    # The sea cliff, on its own terms. By slope alone it came out three-quarters rock and a quarter
    # turf, and the quarter of turf is what showed on the cliff face — green streaked down a wall of
    # stone. Anything steep in the last stretch before the water is the rock the coast is cut from.
    sea_cliff = np.clip(1.0 - inland * 4.0, 0, 1) * np.clip((slope - 0.10) * 6.0, 0, 1)
    # And the rim above it: the whole band the generator roughened, whatever its slope, since from
    # above it is the rock you see and not the cliff face.
    rim = fields["coast_band"] ** 0.7
    # The ditch: turned earth and stone along its bed, so a border reads as something dug.
    trench = fields.get("ditch", np.zeros_like(slope)) * 0.95
    rock = np.clip(np.maximum(np.maximum(np.maximum(steep, high), np.maximum(sea_cliff, rim * 0.9)), trench), 0, 1)

    # Snow on the crest, thinning on the steepest faces, which do not hold it.
    snow = np.clip((altitude - 0.78) * 6.0, 0, 1) * (1.0 - 0.55 * steep)

    # Sand in the last stretch before the water, and only where the shore is gentle: a cliff is the
    # rock it is cut from, not a beach standing on end. And only down near the water: the coast rises
    # straight out of the sea in places, and judged by distance alone the flat top of a cliff fifty
    # units up came out as beach.
    near_sea_level = np.clip(1.0 - (height - SEA_FLOOR_BYTE - BEACH_RISE) / BEACH_RISE, 0, 1)
    sand = np.clip(1.0 - inland * 5.5, 0, 1) * (1.0 - steep) * near_sea_level

    # A pixel names the ground it is, and the ground lying on top of it. What matters is which way
    # round: snow LIES ON rock, and writing grass as the thing underneath left the peaks showing
    # green through every thin drift. So the pair is chosen by what is actually there.
    base = np.zeros((H, W), dtype=np.uint8)      # turf
    overlay = np.ones((H, W), dtype=np.uint8)    # with stone coming through it
    blend = rock.copy()

    # The beach: shingle underneath, with what rock the headland has on top of it.
    shingle = sand > 0.12
    base[shingle] = 3
    overlay[shingle] = 1
    blend[shingle] = rock[shingle] * 0.5

    # The crest: stone underneath, snow lying on it.
    drift = snow > 0.12
    base[drift] = 1
    overlay[drift] = 2
    blend[drift] = snow[drift]

    # Softened before it is written. The masks are built out of a gradient of an eight-bit height
    # map, so their edges arrive in steps, and a step in the blend is a step you can see on the
    # hillside. Which texture is which must NOT be softened — half way between grass and snow is a
    # texture that does not exist — so only this channel is touched.
    blend = blurred(np.clip(blend, 0, 1), 2.2)

    # The seabed is sand. It used to be written as nothing, which is slot 0, turf: the sea hides it,
    # but Terrain3D blends a surface between neighbouring vertices, so every face running down into
    # the water was drawn half in grass, and the waterline pulled the grass down to meet it.
    sea = ~land
    base[sea] = 3
    overlay[sea] = 1
    blend[sea] = 0.0

    # Walls: wherever the ground falls away sharply, stone on both sides of the edge — the lip at
    # the top and the foot under the water. The slope alone could not do it: on a wall that drops a
    # cliff's height in one pixel it lands on a single row, the blur above smears it away, and the
    # row below it was the seabed. Between the two the face was stretched grass. After the blur on
    # purpose: a wall is rock, not a mix.
    wall = sheer_faces(height)
    base[wall] = 1
    overlay[wall] = 1
    blend[wall] = 0.0

    # The roads, laid over whatever they cross. They were a strip of mesh floating over the ground
    # with its own flat texture, which read as tarmac: painted in here they take the terrain's light
    # and meet the grass along the stones' own height, the way a worn track does.
    track = road > ROAD_TRACE
    overlay[track] = 4
    blend[track] = road[track]

    return np.stack([base, overlay, np.clip(blend * 255.0, 0, 255).astype(np.uint8)], axis=2)


def cliffs_not_walls(height, land):
    """Holds the land down near the water: no higher than COAST_SLOPE bytes per pixel of distance
    from the sea. Everything the relief is built from fades out toward the coast, but the ground
    levelled round a seat does not, and where a seat stands near the sea its plateau ran straight
    to the water and stopped as a wall a cliff high and one pixel thick. Terrain3D cannot texture
    that — a face one vertex wide has no slope of its own to project the rock along — so it came
    out as grass smeared down the wall. Spread over a dozen pixels it is a cliff, and draws as one."""
    distance = np.zeros(height.shape, dtype=np.float32)
    inside = land.copy()
    reach = int(np.ceil(255.0 / COAST_SLOPE))
    for step in range(1, reach + 1):
        distance[inside] = step
        inside = (inside & np.roll(inside, 1, 0) & np.roll(inside, -1, 0)
                  & np.roll(inside, 1, 1) & np.roll(inside, -1, 1))
        if not inside.any():
            break
    ceiling = SEA_FLOOR_BYTE + FREEBOARD + COAST_SLOPE * (distance - 1.0)
    return np.where(land & inside, height, np.where(land, np.minimum(height, ceiling), height))


def sheer_faces(height):
    """Every pixel within WALL_REACH of a drop steeper than WALL_DROP, above it or below it."""
    lowest = height.copy()
    highest = height.copy()
    for dy in range(-WALL_REACH, WALL_REACH + 1):
        for dx in range(-WALL_REACH, WALL_REACH + 1):
            shifted = np.roll(np.roll(height, dy, 0), dx, 1)
            lowest = np.minimum(lowest, shifted)
            highest = np.maximum(highest, shifted)
    return (height - lowest > WALL_DROP) | (highest - height > WALL_DROP)


def build_props(rng, land, height, slope, fields, province_id, islet_field):
    """Where the map grows things. One image, all 0..255 densities: R = woodland, B = open ground
    worth farming. G is empty — it was the boulder field, and the map has no boulders any more.
    MapDecoration scatters against it with a fixed seed, so the same map always grows the same
    trees."""
    north_field, inland = fields["north_field"], fields["inland"]
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
    # Where the wood gives out. Read against this map's own spread, not guessed: the lowland sits
    # at about 0.44 of the range and the crest at 0.91, so a treeline set by eye at 0.20 cleared the
    # farmland of trees as well as the mountain. It belongs just above the plain — which leaves bare
    # stone between the forest and the snow, the one thing a mountain is for showing.
    southern_woods = np.clip((noise - 0.05) * 1.5, 0, 1) * warm * np.clip(1.0 - (altitude - 0.52) * 7.0, 0, 1)
    southern_woods *= FOREST["southern_density"]
    # The north is not bare rock: it is taiga. Sparser than the southern woods and stopping at the
    # snow line, but the Northern Watch has forests of its own.
    northern_woods = np.clip((noise - 0.2) * 1.7, 0, 1) * (1.0 - warm) * np.clip(1.0 - (altitude - 0.56) * 6.0, 0, 1)
    northern_woods *= FOREST["northern_density"]
    treeline = border_band / max(border_band.max(), 1e-6)
    forest = np.clip(southern_woods + northern_woods, 0, 1)
    # Trees mark borders in both realms, but nothing grows on the snowy crest itself.
    forest = np.clip(forest + treeline * FOREST["treeline_strength"] * np.clip(1.0 - (altitude - 0.54) * 6.0, 0, 1), 0, 1)
    forest *= (1.0 - np.clip(slope * 1.2, 0, 1))
    # Islets are too small for the inland field to register, so they would come out bare rock:
    # give them their own stand of trees, thinner than the mainland's.
    islet_land = np.clip(islet_field * 255.0 / ISLET_HEIGHT, 0, 1)
    forest = np.clip(forest + islet_land * 0.5 * warm * (1.0 - np.clip(slope * 1.4, 0, 1)), 0, 1)
    forest *= np.clip(inland * 6.0, 0, 1)  # nothing grows on the beach itself

    farmland = warm * np.clip(1.0 - slope * 3.5, 0, 1) * np.clip(1.0 - altitude * 2.2, 0, 1)
    farmland = np.clip(farmland - forest * 0.8, 0, 1) * np.clip(inland * 5.0, 0, 1)

    props = np.stack([forest, np.zeros_like(forest), farmland], axis=2) * 255.0
    return np.where(land[:, :, None], props, 0).astype(np.uint8)


def clear_along_roads(props, roads):
    """Wipes woodland from a corridor along every route, and widens the corridor for
    the paved trunk road, which is a built thing rather than a track worn through the trees."""
    corridor = Image.new("L", (W, H), 0)
    draw = ImageDraw.Draw(corridor)
    for road in roads:
        points = [(x, y) for x, y in road["points"]]
        draw.line(points, fill=255, width=ROAD_CLEAR_PAVED if road["kind"] == "paved" else ROAD_CLEAR_DIRT,
                  joint="curve")

    # Softened, so the treeline thins toward the road instead of stopping on a line.
    cleared = np.array(corridor.filter(ImageFilter.GaussianBlur(4.0))).astype(np.float32) / 255.0
    keep = np.clip(1.0 - cleared * 1.6, 0, 1)

    props = props.astype(np.float32)
    props[:, :, 0] *= keep   # woodland
    return props.astype(np.uint8)


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
        roads.append({"from": PROVINCES[i][0], "to": PROVINCES[j][0], "pair": (i, j),
                      "cost": best.get(goal, float("inf")),
                      "points": [[round(x, 1), round(y, 1)] for x, y in points[::2]]})

    # One road per pair of neighbours turns every well-connected province into a junction of five
    # tracks. What a realm actually builds is the cheapest network that still reaches every seat —
    # a minimum spanning tree over the routes, by what each one costs to travel — plus however many
    # shortcuts the campaign wants back on top.
    roads = cheapest_network(roads, len(PROVINCES))

    # The trunk road: the chain of kept routes that joins the two capitals. It gets paved in the
    # game, the rest stay dirt tracks, so the map shows how an army would march between realms.
    kept = {road["pair"] for road in roads}
    trunk = capital_route(kept)
    for road in roads:
        pair = tuple(sorted((road["from"], road["to"])))
        road["kind"] = "paved" if pair in trunk else "dirt"
        del road["pair"], road["cost"]

    return roads


def cheapest_network(roads, province_count):
    """Kruskal: take routes cheapest first, keeping one only if it joins two parts not yet linked.
    That leaves every seat reachable over the fewest roads; EXTRA_ROUTES then puts back the cheapest
    of the rejected ones, as the shortcuts a realm would eventually pave."""
    parent = list(range(province_count))

    def root(node):
        while parent[node] != node:
            parent[node] = parent[parent[node]]
            node = parent[node]
        return node

    kept, spare = [], []
    for road in sorted(roads, key=lambda r: r["cost"]):
        i, j = road["pair"]
        if root(i) != root(j):
            parent[root(i)] = root(j)
            kept.append(road)
        else:
            spare.append(road)

    return kept + spare[:EXTRA_ROUTES]


def capital_route(edges):
    """Province-to-province hops along the shortest chain between the two capitals, as name pairs,
    following only the roads that were actually kept."""
    capitals = [index for index, province in enumerate(PROVINCES) if province[4]]
    if len(capitals) < 2:
        return set()

    neighbours = {index: set() for index in range(len(PROVINCES))}
    for i, j in edges:
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
    # Drawn from the main generator so every later draw from it stays exactly where it was: the
    # mountains, the crags and the borders are all downstream of this and should not move.
    coast_noise = 0.7 * smooth_noise(rng, 26) + 0.3 * smooth_noise(rng, 70)

    # Then the outline is WARPED, not merely nudged. Pushing a softened edge back and forth by a
    # threshold moved it a dozen pixels either way along sides hundreds long, and the long straight
    # sides of the traced polygon still read as a coast cut with a knife. Sampling the outline
    # through a displaced grid bends the sides themselves: coves a few dozen pixels deep, headlands
    # between them, and a rocky jaggedness riding on both. Its own generator, so the one above is
    # left in step.
    shore = np.random.default_rng(MAP.get("coast_seed", 311))
    ys_c, xs_c = np.mgrid[0:H, 0:W]
    bend_x = (smooth_noise(shore, 8) * COAST_BAYS + smooth_noise(shore, 21) * COAST_BAYS * 0.34
              + smooth_noise(shore, 55) * COAST_BAYS * 0.11)
    bend_y = (smooth_noise(shore, 8) * COAST_BAYS + smooth_noise(shore, 21) * COAST_BAYS * 0.34
              + smooth_noise(shore, 55) * COAST_BAYS * 0.11)
    outline = np.array(mask_img) > 0
    sample_x = np.clip(np.rint(xs_c + bend_x), 0, W - 1).astype(np.int32)
    sample_y = np.clip(np.rint(ys_c + bend_y), 0, H - 1).astype(np.int32)
    warped = outline[sample_y, sample_x]
    # The traced outline runs within a dozen pixels of the top of the image, and the warp pushed that
    # coast into the edge itself, where it was cut off in a straight line with a rectangle of shelf
    # water above it. So the land is made to give way as it nears the edge of the map — pulled back
    # through the same threshold, which lets the coast curve away instead of being trimmed.
    to_edge = np.minimum(np.minimum(xs_c, W - 1 - xs_c), np.minimum(ys_c, H - 1 - ys_c)).astype(np.float32)
    edge_fall = np.clip((MAP_MARGIN - to_edge) / MAP_MARGIN, 0, 1) ** 1.5 * 0.9
    land = (blurred(warped, 3.0) + 0.12 * coast_noise - edge_fall) > 0.5

    for name, sx, sy, _, _, _ in PROVINCES:
        assert land[int(sy), int(sx)], f"the coast warp put {name}'s seat in the sea"


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

    # A raster Voronoi bisects every pair of seats with a ruler. Warping the ground the distance is
    # measured on bends those bisectors into borders that follow the country, and because the whole
    # plane is warped together the regions stay a clean partition — no holes, no orphaned patches.
    warp_x = xs + smooth_noise(rng, 7) * BORDER_WARP
    warp_y = ys + smooth_noise(rng, 7) * BORDER_WARP

    best_dist = np.full((H, W), np.inf)
    province_id = np.full((H, W), -1, dtype=np.int32)
    for i, (_, sx, sy, _, _, _) in enumerate(PROVINCES):
        d = (warp_x - sx) ** 2 + (warp_y - sy) ** 2
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

    height, terrain_fields = build_terrain(rng, land, province_id, dist_to_seed, islet_field, islet_base)

    roads = build_roads(height, land, province_id, adjacency)
    height = carve_roads(height, roads, land)
    height, ditch, blocked, crossings = dig_borders(height, land, province_id, roads, adjacency)
    terrain_fields["ditch"] = ditch

    # A clean step at the waterline. Measured, the land along the coast stood one to three bytes
    # above the water and the sea one byte below it — the two surfaces within a tenth of a unit of
    # each other, so every ripple and every shimmer of the reflections crossed back and forth over
    # the shore, and a low headland came out as a slab the sea lapped over and under as it moved.
    # So the land is held a little clear of the water and the sea a little deep of it, and the
    # height's own waterline is made to be exactly the land's outline, which the surf is laid along.
    height = cliffs_not_walls(height, land)
    height = np.where(land, np.maximum(height, SEA_FLOOR_BYTE + FREEBOARD),
                      np.minimum(height, SEA_FLOOR_BYTE - DRAFT))

    albedo = paint_albedo(height, terrain_fields, land, rng)
    # The colour map tints every texture on the ground, and under a road it was the meadow's green:
    # dirt dyed green is moss. Neutral there, so the track shows its own colour.
    road = road_strength(roads)
    albedo = (albedo * (1.0 - road[:, :, None]) + np.array(ROAD_TINT) * road[:, :, None]).astype(np.uint8)

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

    Image.fromarray(build_surface(height, slope, terrain_fields, land, road), "RGB").save(
        OUT_DIR / "map-surface.png")

    # How near the land every stretch of sea is — 1 at the water's edge, fading to nothing a few
    # dozen pixels out, zero on land itself. The water shader lays its surf along this, not along
    # the depth: the shelf round the island is wide and shallow and even has a crest out on it, so
    # surf keyed to "shallow water" came out as a thick white band standing offshore with open
    # water between it and the cliffs. Surf belongs where the sea meets the rock.
    # Measured off the FINISHED height, not the island's outline: the terrain is drawn from the
    # height, so that is where the waterline actually is. Off the outline the surf stood a few pixels
    # out to sea with a strip of open water between it and the rock.
    shore = height > SEA_FLOOR_BYTE
    reach = blurred(shore, 5.0)
    coast = np.where(shore, 0.0, np.clip(reach * 2.4, 0, 1))
    Image.fromarray((coast * 255.0).astype(np.uint8), "L").save(OUT_DIR / "map-coast.png")

    props = build_props(rng, land, height, slope, terrain_fields, province_id, islet_field)
    # A road through a wood is a road through a clearing: nothing grows where it runs, or the trees
    # simply stand on top of it and there is no path to see.
    props = clear_along_roads(props, roads)
    Image.fromarray(props, "RGB").save(OUT_DIR / "map-props.png")
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    (DATA_DIR / "map-roads.json").write_text(json.dumps(roads, indent=1))
    (DATA_DIR / "map-crossings.json").write_text(json.dumps(crossings, indent=1))
    # What an army cannot cross, as the game reads it: white is ditch, black is open ground.
    Image.fromarray((blocked * 255).astype(np.uint8), "L").save(OUT_DIR / "map-ditch.png")
    print(f"Borders: {sum(1 for c in crossings if c['kind'] == 'road')} road crossings, "
          f"{sum(1 for c in crossings if c['kind'] == 'ford')} fords")
    print(f"\nRoads: {len(roads)} routes, {sum(len(r['points']) for r in roads)} points")

    print("Adjacency:")
    for i, j in sorted(adjacency):
        print(f"  {PROVINCES[i][0]} <-> {PROVINCES[j][0]}")


if __name__ == "__main__":
    main()
