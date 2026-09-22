"""Cuts Meshy models down to something a campaign map can draw: trees by the thousand, and the
villages that stand on every county seat.

A Meshy export is a scanned-looking surface of two to eight million triangles with its look baked
into a texture atlas of thousands of islands. On the map a tree is thirty to sixty pixels tall and
there are thousands of them, so each one can afford a few hundred triangles; a village is a few
hundred pixels across at the closest zoom and there is one per seat, so it gets tens of thousands.

TREES

Neither obvious reduction works on these. The canopy is millions of separate leaf fragments, so a
plain collapse shatters it into triangles floating in the air where the leaves were; and a collapse
that keeps the original texture has to respect the seams of a UV atlas cut into thousands of
islands, and stalls at tens of thousands of faces. So the tree is rebuilt the way a game artist
makes a distant LOD:

  1. the whole thing is SHRINK-WRAPPED — an alpha wrap, a closed surface fitted round the leaves
     and the trunk — which turns a cloud of fragments into one solid canopy;
  2. that surface is collapsed to TARGET_FACES, which a closed surface survives gracefully;
  3. it gets a fresh UV layout of its own — every triangle a half-cell of a grid;
  4. every texel of that new texture is traced back to the nearest point of the original
     multi-million-vertex surface, and takes the colour the original texture has there.

BUILDINGS — anything whose name is not a tree — are the other way round. They are solid, with hard
edges that a shrink-wrap would round off, so:

  1. the surface is collapsed directly, with its UV seams welded first so the atlas does not pin
     every island's border in place;
  2. it is shaded smooth only within BUILDING_CREASE degrees, so a roof ridge and a wall corner stay
     sharp;
  3. it gets a proper unwrap (xatlas), charts sized by their area — a grid would give the yard, a
     handful of large triangles after the collapse, as little texture as a fence post;
  4. every texel is traced to the nearest point on the original's surface (not its nearest vertex:
     a flat yard has few vertices, and taking theirs painted it in a mosaic of cells) and reads the
     original texture there, from a vertex facing the same way so a wall does not take the colour
     of its other side;
  5. the flag is found — the blue cloth flying above the banners, grown across the mesh onto its
     trim, not onto its pole — and how far each of its vertices lies from the pole, 0 at the pole
     and 1 at the fly end, is baked into the texture's alpha. settlement.gdshader waves the cloth
     by it, so the flag flutters and stays fastened to its pole. Flags are painted blue on these
     models for the same reason the shader can recolour them: nothing else on them is.

Also, per model: stood on its own origin, base at y = 0 and centred, the way the map expects a prop
to stand.

Needs, in tools/.venv on top of what rebuild-map.sh installs: pip install pymeshlab trimesh scipy xatlas

Run: tools/.venv/bin/python tools/decimate_meshy.py [path-to.glb ...]
     (with no arguments it takes every Meshy tree in ~/Downloads)
"""

import sys
import time
from pathlib import Path

import numpy as np
import pymeshlab
import trimesh
import xatlas
from PIL import Image
from scipy.spatial import cKDTree

PROJECT_DIR = Path(__file__).resolve().parent.parent
OUT_DIR = PROJECT_DIR / "assets" / "models" / "trees"
BUILDING_DIR = PROJECT_DIR / "assets" / "models" / "settlements"
TREE_WORDS = ("tree", "pine", "oak", "guardian", "cypress")

# Triangles per tree. A pine is a simpler silhouette than a spreading oak and gets fewer.
TARGET_FACES = 1800
TARGET_FACES_CONIFER = 1400
CONIFERS = ("pine", "cypress", "evergreen")

# The wrap: how small a gap it may follow into, and how far outside the leaves it sits, both as a
# percentage of the model's diagonal. Smaller follows the branches more closely and costs more faces
# to hold; larger is a lollipop. A trunk is a few per cent of a tree's height, so this keeps one.
WRAP_ALPHA = 1.2
WRAP_OFFSET = 0.5

# How far the shading normals lean out from the middle of the crown, 0..1. See reduce().
NORMAL_BEND = 0.55

# The baked colour texture. A tree is a few dozen pixels tall on the map; this is ample.
TEXTURE_SIZE = 1024
# Texels of padding round each triangle in the atlas, filled by dilation, so mipmapping never
# pulls the colour of the next triangle over into this one.
GUTTER = 2

# A building: its triangles, its texture, the angle past which two faces meet at an edge rather than
# round a curve, and how close two vertices must be (per cent of the diagonal) to be welded across
# an atlas seam before the collapse.
BUILDING_FACES = 24000
BUILDING_TEXTURE = 2048
BUILDING_CREASE = 40.0
BUILDING_WELD = 0.02
# How many faces round an original vertex are searched for the nearest point on its surface, and how
# many original vertices are weighed for facing the right way.
SURFACE_FACES = 8
FACING_CANDIDATES = 6
# The flag: blue cloth above this share of the model's height (the banners on the houses hang below
# it), grown this many rings across the mesh to take in its trim, never within POLE_RADIUS of the
# pole's axis. The pole is the model's highest point — its finial.
FLAG_FLOOR = 0.29
FLAG_RINGS = 3
POLE_RADIUS = 0.03


def atlas(faces_count):
    """A UV layout that gives every triangle half of one square cell of a grid. Crude next to a
    real unwrap, but it cannot overlap, it wastes little, and for a baked texture that is all a UV
    layout is for."""
    cells = int(np.ceil(np.sqrt(np.ceil(faces_count / 2))))
    cell = 1.0 / cells
    inset = GUTTER / TEXTURE_SIZE
    uv = np.zeros((faces_count, 3, 2))
    for face in range(faces_count):
        slot, upper = divmod(face, 2)
        cx, cy = (slot % cells) * cell, (slot // cells) * cell
        lo, hi = inset, cell - inset
        if upper:
            uv[face] = [(cx + hi, cy + lo), (cx + hi, cy + hi), (cx + lo, cy + hi)]
        else:
            uv[face] = [(cx + lo, cy + lo), (cx + hi, cy + lo), (cx + lo, cy + hi)]
    return uv


def bake(vertices, faces, face_uv, source_points, source_uv, source_texture):
    """Colours every texel of the new atlas from the nearest point of the original surface."""
    size = TEXTURE_SIZE
    texture = np.asarray(source_texture.convert("RGB"), dtype=np.float32)
    th, tw = texture.shape[:2]
    tree = cKDTree(source_points)

    out = np.zeros((size, size, 3), dtype=np.float32)
    filled = np.zeros((size, size), dtype=bool)

    ys, xs = np.mgrid[0:size, 0:size]
    centres = np.stack([(xs + 0.5) / size, (ys + 0.5) / size], axis=-1)

    for face in range(len(faces)):
        a, b, c = face_uv[face]
        x0, x1 = int(np.floor(min(a[0], b[0], c[0]) * size)), int(np.ceil(max(a[0], b[0], c[0]) * size))
        y0, y1 = int(np.floor(min(a[1], b[1], c[1]) * size)), int(np.ceil(max(a[1], b[1], c[1]) * size))
        block = centres[y0:y1, x0:x1].reshape(-1, 2)
        # Barycentric coordinates of each texel in the triangle.
        v0, v1 = b - a, c - a
        d = v0[0] * v1[1] - v1[0] * v0[1]
        if abs(d) < 1e-12:
            continue
        rel = block - a
        w1 = (rel[:, 0] * v1[1] - v1[0] * rel[:, 1]) / d
        w2 = (v0[0] * rel[:, 1] - rel[:, 0] * v0[1]) / d
        w0 = 1.0 - w1 - w2
        inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
        if not inside.any():
            continue
        corners = vertices[faces[face]]
        points = (w0[inside, None] * corners[0] + w1[inside, None] * corners[1]
                  + w2[inside, None] * corners[2])
        _, nearest = tree.query(points)
        suv = source_uv[nearest]
        px = np.clip((suv[:, 0] % 1.0) * tw, 0, tw - 1).astype(int)
        py = np.clip((1.0 - suv[:, 1] % 1.0) * th, 0, th - 1).astype(int)
        colours = texture[py, px]
        tx = (block[inside, 0] * size).astype(int)
        ty = (block[inside, 1] * size).astype(int)
        out[ty, tx] = colours
        filled[ty, tx] = True

    # Dilate into the gutters so a mipmapped lookup at a triangle's edge still finds its own colour.
    for _ in range(GUTTER + 2):
        grown = out.copy()
        grown_filled = filled.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(out, dy, 0), dx, 1)
            shifted_filled = np.roll(np.roll(filled, dy, 0), dx, 1)
            take = ~grown_filled & shifted_filled
            grown[take] = shifted[take]
            grown_filled |= take
        out, filled = grown, grown_filled

    # glTF's V runs top to bottom in the image.
    return Image.fromarray(np.clip(out[::-1], 0, 255).astype(np.uint8), "RGB")


def model_name(path):
    """Meshy_AI_Blue_Banner_Hamlet_0922095710_texture.glb -> blue-banner-hamlet"""
    return path.stem.replace("Meshy_AI_", "").rsplit("_", 2)[0].lower().replace(" ", "-").replace("_", "-")


def reduce(path):
    if any(word in path.name.lower() for word in TREE_WORDS):
        reduce_tree(path)
    else:
        reduce_building(path)


def reduce_tree(path):
    started = time.time()
    scene = trimesh.load(path, force="scene")
    mesh = trimesh.util.concatenate([g for g in scene.dump() if isinstance(g, trimesh.Trimesh)])
    source_texture = mesh.visual.material.baseColorTexture
    source_uv = np.asarray(mesh.visual.uv, dtype=np.float64)
    source_points = np.asarray(mesh.vertices, dtype=np.float64)

    name = model_name(path)
    target = TARGET_FACES_CONIFER if any(word in name for word in CONIFERS) else TARGET_FACES

    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=source_points, face_matrix=np.asarray(mesh.faces, dtype=np.int32)))
    ms.generate_alpha_wrap(alpha=pymeshlab.PercentageValue(WRAP_ALPHA),
                           offset=pymeshlab.PercentageValue(WRAP_OFFSET))
    wrapped = ms.current_mesh().face_number()
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, preservetopology=True,
                                                planarquadric=True, optimalplacement=True,
                                                preservenormal=True)
    reduced = ms.current_mesh()
    vertices = reduced.vertex_matrix()
    faces = reduced.face_matrix()

    face_uv = atlas(len(faces))
    # The wrap sits a little outside the real surface, so the nearest point of the original to it
    # is whatever sticks out furthest — round a trunk, that is the needles hanging beside it, and
    # the trunk came out green. So the colour is looked up from a point pulled back inside by the
    # wrap's own offset, along the surface normal, where the bark actually is.
    diagonal = float(np.linalg.norm(source_points.max(axis=0) - source_points.min(axis=0)))
    inward = trimesh.Trimesh(vertices=vertices, faces=faces, process=False).vertex_normals
    sample_at = vertices - inward * diagonal * WRAP_OFFSET / 100.0 * 1.6
    texture = bake(sample_at, faces, face_uv, source_points, source_uv, source_texture)

    # Smooth normals, bent outward from the middle of the tree. A canopy shaded by its true facets
    # comes out as a pile of dark shards wherever a face tips away from the light; bent toward the
    # direction out of the crown, it shades as one soft mass, which is how a tree reads from afar.
    welded = trimesh.Trimesh(vertices=vertices, faces=faces, process=False)
    smooth = welded.vertex_normals
    centre = np.array([(vertices[:, 0].min() + vertices[:, 0].max()) / 2,
                       vertices[:, 1].min() + (vertices[:, 1].max() - vertices[:, 1].min()) * 0.55,
                       (vertices[:, 2].min() + vertices[:, 2].max()) / 2])
    outward = vertices - centre
    outward /= np.maximum(np.linalg.norm(outward, axis=1, keepdims=True), 1e-9)
    bent = smooth * (1.0 - NORMAL_BEND) + outward * NORMAL_BEND
    bent /= np.maximum(np.linalg.norm(bent, axis=1, keepdims=True), 1e-9)

    # Every corner becomes its own vertex: each triangle has its own patch of the atlas.
    corners = vertices[faces.reshape(-1)]
    corner_normals = bent[faces.reshape(-1)]
    low = corners.min(axis=0)
    high = corners.max(axis=0)
    corners = corners - np.array([(low[0] + high[0]) / 2, low[1], (low[2] + high[2]) / 2])
    # trimesh holds UVs the OpenGL way, V up from the bottom, and flips them itself on the way out
    # to glTF. Flipping them here as well mirrored every triangle's lookup: the ones that landed in
    # the unused cells came out black, and the bark was drawn on the crown.
    uv = face_uv.reshape(-1, 2).copy()
    tri_faces = np.arange(len(corners)).reshape(-1, 3)

    material = trimesh.visual.material.PBRMaterial(name=name, baseColorTexture=texture,
                                                  metallicFactor=0.0, roughnessFactor=0.92)
    out = trimesh.Trimesh(vertices=corners, faces=tri_faces, vertex_normals=corner_normals,
                          visual=trimesh.visual.TextureVisuals(uv=uv, material=material), process=False)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    target_path = OUT_DIR / f"{name}.glb"
    out.export(target_path)
    print(f"  {name:20s} {len(mesh.faces):>10,} -> wrap {wrapped:>6,} -> {len(faces):>5,} triangles   "
          f"{path.stat().st_size / 1048576:7.1f} MB -> {target_path.stat().st_size / 1024:5.0f} KB   "
          f"height {high[1] - low[1]:.2f}   ({time.time() - started:.0f}s)")


class OriginalSurface:
    """The full-detail model, asked where its surface is and what colour it is there."""

    def __init__(self, mesh):
        self.points = np.asarray(mesh.vertices, dtype=np.float64)
        self.faces = np.asarray(mesh.faces, dtype=np.int64)
        self.uv = np.asarray(mesh.visual.uv, dtype=np.float64)
        self.normals = np.asarray(mesh.vertex_normals, dtype=np.float64)
        self.texture = np.asarray(mesh.visual.material.baseColorTexture.convert("RGB"), dtype=np.float32)
        self.tree = cKDTree(self.points)
        # The faces round every vertex, up to SURFACE_FACES of them, padded with -1.
        flat = self.faces.ravel()
        owner = np.argsort(flat, kind="stable") // 3
        counts = np.bincount(flat, minlength=len(self.points))
        starts = np.concatenate([[0], np.cumsum(counts)[:-1]])
        self.around = np.full((len(self.points), SURFACE_FACES), -1, np.int64)
        for k in range(SURFACE_FACES):
            has = counts > k
            self.around[has, k] = owner[starts[has] + k]

    def colour(self, points, facing):
        """The original's colour at the surface nearest each point, from a vertex facing `facing` —
        one direction for them all, or one per point."""
        _, candidates = self.tree.query(points, k=FACING_CANDIDATES)
        facing = np.broadcast_to(facing, points.shape)
        agree = (self.normals[candidates] * facing[:, None, :]).sum(-1) > 0.2
        pick = np.where(agree.any(1), np.argmax(agree, 1), 0)
        vertex = candidates[np.arange(len(points)), pick]
        return self._sample(self._uv_at(points, vertex))

    def _uv_at(self, points, vertex):
        """Texture coordinate at the nearest point of the faces round each vertex."""
        faces = self.around[vertex]
        valid = faces >= 0
        faces = np.where(valid, faces, 0)
        a = self.points[self.faces[faces, 0]]
        b = self.points[self.faces[faces, 1]]
        c = self.points[self.faces[faces, 2]]
        p = points[:, None, :]
        e0, e1, r = b - a, c - a, p - a
        d00, d01, d11 = (e0 * e0).sum(-1), (e0 * e1).sum(-1), (e1 * e1).sum(-1)
        d20, d21 = (r * e0).sum(-1), (r * e1).sum(-1)
        den = np.maximum(d00 * d11 - d01 * d01, 1e-18)
        bv = np.clip((d11 * d20 - d01 * d21) / den, 0, 1)
        bw = np.clip((d00 * d21 - d01 * d20) / den, 0, 1)
        total = np.maximum(bv + bw, 1.0)             # pulled back onto the far edge if past it
        bv, bw = bv / total, bw / total
        bu = 1.0 - bv - bw
        nearest = a * bu[..., None] + b * bv[..., None] + c * bw[..., None]
        distance = np.where(valid, np.linalg.norm(nearest - p, axis=-1), np.inf)
        best = np.argmin(distance, 1)
        rows = np.arange(len(points))
        face = faces[rows, best]
        return (self.uv[self.faces[face, 0]] * bu[rows, best, None]
                + self.uv[self.faces[face, 1]] * bv[rows, best, None]
                + self.uv[self.faces[face, 2]] * bw[rows, best, None])

    def _sample(self, uv):
        """Bilinear, V up from the bottom as glTF-through-trimesh holds it."""
        h, w = self.texture.shape[:2]
        x = (uv[:, 0] % 1.0) * w - 0.5
        y = ((1.0 - uv[:, 1]) % 1.0) * h - 0.5
        x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
        fx, fy = (x - x0)[:, None], (y - y0)[:, None]
        x0, y0 = x0 % w, y0 % h
        x1, y1 = (x0 + 1) % w, (y0 + 1) % h
        t = self.texture
        return (t[y0, x0] * (1 - fx) * (1 - fy) + t[y0, x1] * fx * (1 - fy)
                + t[y1, x0] * (1 - fx) * fy + t[y1, x1] * fx * fy)


def texels(faces, uv, size):
    """Every texel of the atlas that each triangle covers: yields the face, the texels' columns and
    rows, and their barycentric weights in it."""
    for face in range(len(faces)):
        a, b, c = uv[faces[face]] * size
        x0, x1 = int(np.floor(min(a[0], b[0], c[0]))), int(np.ceil(max(a[0], b[0], c[0])))
        y0, y1 = int(np.floor(min(a[1], b[1], c[1]))), int(np.ceil(max(a[1], b[1], c[1])))
        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        texel = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
        v0, v1 = b - a, c - a
        d = v0[0] * v1[1] - v1[0] * v0[1]
        if abs(d) < 1e-9:
            continue
        r = texel - a
        w1 = (r[:, 0] * v1[1] - v1[0] * r[:, 1]) / d
        w2 = (v0[0] * r[:, 1] - r[:, 0] * v0[1]) / d
        w0 = 1.0 - w1 - w2
        inside = (w0 >= -0.03) & (w1 >= -0.03) & (w2 >= -0.03)
        if not inside.any():
            continue
        tx = np.clip(texel[inside, 0].astype(int), 0, size - 1)
        ty = np.clip(texel[inside, 1].astype(int), 0, size - 1)
        yield face, tx, ty, np.stack([w0[inside], w1[inside], w2[inside]], 1)


def flag_weights(vertices, faces, uv, colour):
    """How far along the flag each vertex lies, 0 at the pole to 1 at the fly end; 0 off the flag.
    The cloth is read off the baked texture at each vertex — what the shader will read — and not
    off the original: on a flag two faces thin, the two can disagree about which side a vertex is."""
    size = colour.shape[0]
    # Measured up from the model's own base: it has not been stood on y = 0 yet.
    floor = vertices[:, 1].min() + FLAG_FLOOR * (vertices[:, 1].max() - vertices[:, 1].min())
    pole = vertices[vertices[:, 1].argmax()][[0, 2]]
    reach = np.hypot(vertices[:, 0] - pole[0], vertices[:, 2] - pole[1])
    at = colour[np.clip((uv[:, 1] * size).astype(int), 0, size - 1),
                np.clip((uv[:, 0] * size).astype(int), 0, size - 1)] / 255.0
    linear = np.where(at <= 0.04045, at / 12.92, ((at + 0.055) / 1.055) ** 2.4)
    cloth = linear[:, 2] - np.maximum(linear[:, 0], linear[:, 1]) > 0.05
    allowed = (vertices[:, 1] > floor) & (reach > POLE_RADIUS)

    # The mesh is split at every UV seam and hard edge, so it is joined back up by position to walk it.
    _, joined = np.unique(np.round(vertices, 6), axis=0, return_inverse=True)
    joined = joined.ravel()
    may = np.bincount(joined[allowed], minlength=joined.max() + 1).astype(bool)
    flag = np.zeros(joined.max() + 1, bool)
    flag[joined[cloth & allowed]] = True
    corners = joined[faces]
    for _ in range(FLAG_RINGS):
        grown = np.zeros_like(flag)
        grown[corners[flag[corners].any(axis=1)].ravel()] = True
        flag |= grown & may
    on_flag = flag[joined] & allowed
    if not on_flag.any():
        return np.zeros(len(vertices))
    return np.where(on_flag, np.clip(reach / reach[on_flag].max(), 0.0, 1.0), 0.0)


def dilated(image, filled):
    """Grown out past the charts' padding, so a mipmapped lookup at a chart's edge — or a vertex
    shader reading exactly on a corner — finds the chart's own value and not the empty atlas."""
    for _ in range(6):
        grown, grown_filled = image.copy(), filled.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(image, dy, 0), dx, 1)
            shifted_filled = np.roll(np.roll(filled, dy, 0), dx, 1)
            take = ~grown_filled & shifted_filled
            grown[take] = shifted[take]
            grown_filled |= take
        image, filled = grown, grown_filled
    return image


def bake_building(vertices, faces, uv, surface):
    """The new atlas: every texel coloured from the original's surface, and the flag's sway in the
    alpha."""
    size = BUILDING_TEXTURE
    colour = np.zeros((size, size, 3), np.float32)
    sway_map = np.zeros((size, size, 1), np.float32)
    filled = np.zeros((size, size), bool)
    normals = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)
    for face, tx, ty, weights in texels(faces, uv, size):
        colour[ty, tx] = surface.colour(weights @ vertices[faces[face]], normals[face])
        filled[ty, tx] = True
    colour = dilated(colour, filled)

    sway = flag_weights(vertices, faces, uv, colour)
    for face, tx, ty, weights in texels(faces, uv, size):
        sway_map[ty, tx, 0] = 255.0 * (weights @ sway[faces[face]])
    sway_map = dilated(sway_map, filled)

    rgba = np.concatenate([colour, sway_map], axis=2)
    return Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA"), sway


def reduce_building(path):
    started = time.time()
    mesh = trimesh.load(path, force="mesh", process=False)
    surface = OriginalSurface(mesh)

    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=surface.points, face_matrix=surface.faces.astype(np.int32)))
    ms.meshing_merge_close_vertices(threshold=pymeshlab.PercentageValue(BUILDING_WELD))
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=BUILDING_FACES, preservetopology=False,
                                                preserveboundary=True, boundaryweight=2.0,
                                                planarquadric=True, optimalplacement=True,
                                                preservenormal=True, autoclean=True)
    reduced = ms.current_mesh()
    shaded = trimesh.graph.smooth_shade(
        trimesh.Trimesh(vertices=reduced.vertex_matrix(), faces=reduced.face_matrix(), process=False),
        angle=np.radians(BUILDING_CREASE))
    vertices, faces, normals = (np.asarray(shaded.vertices), np.asarray(shaded.faces),
                                np.asarray(shaded.vertex_normals))

    atlas = xatlas.Atlas()
    atlas.add_mesh(vertices.astype(np.float32), faces.astype(np.uint32))
    packing = xatlas.PackOptions()
    packing.resolution = BUILDING_TEXTURE
    packing.padding = 3
    packing.bilinear = True
    atlas.generate(pack_options=packing)
    remap, faces, uv = atlas[0]
    vertices, normals = vertices[remap], normals[remap]

    texture, sway = bake_building(vertices, faces, uv, surface)

    low = vertices.min(axis=0)
    high = vertices.max(axis=0)
    vertices = vertices - np.array([(low[0] + high[0]) / 2, low[1], (low[2] + high[2]) / 2])
    name = model_name(path)
    material = trimesh.visual.material.PBRMaterial(name=name, baseColorTexture=texture,
                                                  metallicFactor=0.0, roughnessFactor=0.9)
    # xatlas's V runs down the image, the way it was baked; trimesh holds V up and flips it on export.
    out = trimesh.Trimesh(vertices=vertices, faces=faces, vertex_normals=normals, process=False,
                          visual=trimesh.visual.TextureVisuals(uv=np.column_stack([uv[:, 0], 1.0 - uv[:, 1]]),
                                                               material=material))
    BUILDING_DIR.mkdir(parents=True, exist_ok=True)
    target_path = BUILDING_DIR / f"{name}.glb"
    out.export(target_path)
    print(f"  {name:20s} {len(surface.faces):>10,} -> {len(faces):>6,} triangles   "
          f"{path.stat().st_size / 1048576:7.1f} MB -> {target_path.stat().st_size / 1048576:5.1f} MB   "
          f"flag {int((sway > 0).sum())} vertices   ({time.time() - started:.0f}s)")


def main():
    paths = [Path(p) for p in sys.argv[1:]] or sorted(
        p for p in Path.home().joinpath("Downloads").glob("Meshy_AI_*.glb")
        if any(word in p.name.lower() for word in TREE_WORDS))
    print(f"Reducing {len(paths)} models into {OUT_DIR.parent.relative_to(PROJECT_DIR)}:")
    for path in paths:
        reduce(path)


if __name__ == "__main__":
    main()
