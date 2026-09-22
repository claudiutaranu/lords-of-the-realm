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
  2. that surface is collapsed to TARGET_FACES, which a closed surface survives gracefully.

BUILDINGS — anything whose name is not a tree — are the other way round. They are solid, with hard
edges that a shrink-wrap would round off, so:

  1. the surface is collapsed directly, with its UV seams welded first so the atlas does not pin
     every island's border in place;
  2. it is shaded smooth only within BUILDING_CREASE degrees, so a roof ridge and a wall corner
     stay sharp.

BOTH are then unwrapped and baked the same way:

  3. a proper unwrap (xatlas), its charts sized by their area — a grid layout, which is what the
     trees had, gives a whole crown as much texture as one twig;
  4. every texel is traced to the nearest point on the ORIGINAL's surface (not to its nearest
     vertex, which painted broad flat pieces in a mosaic of cells) and reads the original texture
     there, from a vertex facing the same way so a wall does not take the colour of its other side;
  5. the original's own normal at that point is baked beside the colour, in the model's own space,
     so a few thousand triangles are shaded with the relief of the eight million they came from —
     bark, thatch, roof tiles, the shape of a leaf cluster. tree-foliage.gdshader and
     settlement.gdshader read it (see NORMAL_MAP_SUFFIX);
  6. for a building, the flag is found — the blue cloth flying above the banners, grown across the mesh onto its
     trim, not onto its pole — and how far each of its vertices lies from the pole, 0 at the pole
     and 1 at the fly end, is baked into the colour texture's alpha. settlement.gdshader waves the
     cloth by it, so the flag flutters and stays fastened to its pole. Flags are painted blue on
     these models for the same reason the shader can recolour them: nothing else on them is.

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

# Triangles per tree, and the texture it carries. A pine is a simpler silhouette than a spreading
# oak and gets fewer. Thousands of trees stand on the map at once, so this is the one budget on the
# map that is really paid by the frame — measure before raising it.
TARGET_FACES = 3200
TARGET_FACES_CONIFER = 2400
TREE_TEXTURE = 2048
CONIFERS = ("pine", "cypress", "evergreen")

# The wrap: how small a gap it may follow into, and how far outside the leaves it sits, both as a
# percentage of the model's diagonal. Smaller follows the branches more closely and costs more faces
# to hold; larger is a lollipop. A trunk is a few per cent of a tree's height, so this keeps one.
WRAP_ALPHA = 0.8
WRAP_OFFSET = 0.35

# How far the shading normals lean out from the middle of the crown, 0..1, under the baked normals.
NORMAL_BEND = 0.55

# Texels of padding round each chart in the atlas, filled by dilation, so mipmapping never pulls
# the colour of the next chart over into this one.
GUTTER = 3
# The baked normals ride in their own file beside the model: <name>-normal.png, in the model's own
# space (not tangent space), which is what the shaders read.
NORMAL_MAP_SUFFIX = "-normal.png"
# How far the baked charts are grown into the atlas's empty space, in texels.
DILATION = 20
# The trees' normals are saved at half their colour's size: the relief they carry is leaf-sized and
# there are five of them in the repository.
TREE_NORMAL_TEXTURE = TREE_TEXTURE // 2

# A building: its triangles, its texture, the angle past which two faces meet at an edge rather than
# round a curve, and how close two vertices must be (per cent of the diagonal) to be welded across
# an atlas seam before the collapse.
BUILDING_FACES = 40000
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
    mesh = trimesh.load(path, force="mesh", process=False)
    surface = OriginalSurface(mesh)
    name = model_name(path)
    target = TARGET_FACES_CONIFER if any(word in name for word in CONIFERS) else TARGET_FACES

    ms = pymeshlab.MeshSet()
    ms.add_mesh(pymeshlab.Mesh(vertex_matrix=surface.points, face_matrix=surface.faces.astype(np.int32)))
    ms.generate_alpha_wrap(alpha=pymeshlab.PercentageValue(WRAP_ALPHA),
                           offset=pymeshlab.PercentageValue(WRAP_OFFSET))
    wrapped = ms.current_mesh().face_number()
    ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, preservetopology=True,
                                                planarquadric=True, optimalplacement=True,
                                                preservenormal=True)
    reduced = ms.current_mesh()
    remap, faces, uv = unwrap(reduced.vertex_matrix(), reduced.face_matrix(), TREE_TEXTURE)
    vertices = reduced.vertex_matrix()[remap]

    # Shading normals bent outward from the middle of the crown. The baked normals carry the leaves'
    # own relief on top of these; under them, a canopy shaded by its true facets is a pile of dark
    # shards wherever a face tips away from the light, and bent out of the crown it reads as one
    # soft mass, which is what a tree looks like from any distance at all.
    welded = trimesh.Trimesh(vertices=vertices, faces=faces, process=False)
    centre = np.array([(vertices[:, 0].min() + vertices[:, 0].max()) / 2,
                       vertices[:, 1].min() + (vertices[:, 1].max() - vertices[:, 1].min()) * 0.55,
                       (vertices[:, 2].min() + vertices[:, 2].max()) / 2])
    outward = vertices - centre
    outward /= np.maximum(np.linalg.norm(outward, axis=1, keepdims=True), 1e-9)
    normals = welded.vertex_normals * (1.0 - NORMAL_BEND) + outward * NORMAL_BEND
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-9)

    diagonal = float(np.linalg.norm(surface.points.max(axis=0) - surface.points.min(axis=0)))
    colour, relief, _ = bake(vertices, faces, uv, surface, TREE_TEXTURE,
                             inset=diagonal * WRAP_OFFSET / 100.0 * 1.6)
    model, height = export(name, OUT_DIR, vertices, faces, uv, normals,
                           Image.fromarray(np.clip(colour, 0, 255).astype(np.uint8), "RGB"),
                           Image.fromarray(np.clip(relief, 0, 255).astype(np.uint8), "RGB"), roughness=0.92,
                           relief_size=TREE_NORMAL_TEXTURE)
    print(f"  {name:20s} {len(surface.faces):>10,} -> wrap {wrapped:>6,} -> {len(faces):>5,} triangles   "
          f"{path.stat().st_size / 1048576:7.1f} MB -> {model.stat().st_size / 1024:5.0f} KB   "
          f"height {height:.2f}   ({time.time() - started:.0f}s)")


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

    def shade(self, points, facing):
        """The original's colour AND its own normal at the surface nearest each point, looked for
        from a vertex facing `facing` — one direction for them all, or one per point."""
        _, candidates = self.tree.query(points, k=FACING_CANDIDATES)
        facing = np.broadcast_to(facing, points.shape)
        agree = (self.normals[candidates] * facing[:, None, :]).sum(-1) > 0.2
        pick = np.where(agree.any(1), np.argmax(agree, 1), 0)
        vertex = candidates[np.arange(len(points)), pick]
        face, weights = self._nearest(points, vertex)
        corners = self.faces[face]
        uv = (self.uv[corners[:, 0]] * weights[:, 0, None] + self.uv[corners[:, 1]] * weights[:, 1, None]
              + self.uv[corners[:, 2]] * weights[:, 2, None])
        normal = (self.normals[corners[:, 0]] * weights[:, 0, None]
                  + self.normals[corners[:, 1]] * weights[:, 1, None]
                  + self.normals[corners[:, 2]] * weights[:, 2, None])
        normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-12)
        return self._sample(uv), normal

    def _nearest(self, points, vertex):
        """The face nearest each point among those round its vertex, and where on it the point lies."""
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
        return faces[rows, best], np.stack([bu[rows, best], bv[rows, best], bw[rows, best]], axis=1)

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
    """Grown out well past the charts' padding, so a mipmapped lookup at a chart's edge — or a
    vertex shader reading exactly on a corner — finds the chart's own value and not the empty
    atlas. Far more than the padding, because the coarse mip levels average whole neighbourhoods
    together: unfilled gutters read as black, and a wood seen from across the map darkened."""
    for _ in range(DILATION):
        grown, grown_filled = image.copy(), filled.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(image, dy, 0), dx, 1)
            shifted_filled = np.roll(np.roll(filled, dy, 0), dx, 1)
            take = ~grown_filled & shifted_filled
            grown[take] = shifted[take]
            grown_filled |= take
        image, filled = grown, grown_filled
    return image


def bake(vertices, faces, uv, surface, size, inset=0.0):
    """Every texel of the new atlas, taking the colour AND the normal the original has at the point
    of its surface nearest to it.

    `inset` pulls the sampled point back along the low surface's own normal before the lookup. A
    wrapped tree sits a little outside the leaves it was fitted round, and the nearest thing to it
    out there is whatever sticks out furthest — round a trunk, the needles hanging beside it, and
    the trunk came out green."""
    colour = np.zeros((size, size, 3), np.float32)
    relief = np.zeros((size, size, 3), np.float32)
    filled = np.zeros((size, size), bool)
    facets = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    facets /= np.maximum(np.linalg.norm(facets, axis=1, keepdims=True), 1e-12)
    for face, tx, ty, weights in texels(faces, uv, size):
        points = weights @ vertices[faces[face]] - facets[face] * inset
        painted, normal = surface.shade(points, facets[face])
        colour[ty, tx] = painted
        relief[ty, tx] = (normal * 0.5 + 0.5) * 255.0
        filled[ty, tx] = True

    return dilated(colour, filled), dilated(relief, filled), filled


def sway_alpha(vertices, faces, uv, colour, size, filled):
    """The flag's sway, in the colour texture's alpha — see flag_weights."""
    sway = flag_weights(vertices, faces, uv, colour)
    alpha = np.zeros((size, size, 1), np.float32)
    for face, tx, ty, weights in texels(faces, uv, size):
        alpha[ty, tx, 0] = 255.0 * (weights @ sway[faces[face]])
    return dilated(alpha, filled), sway


def unwrap(vertices, faces, size):
    """A UV layout whose charts are sized by their area, packed into one `size` square."""
    atlas = xatlas.Atlas()
    atlas.add_mesh(vertices.astype(np.float32), faces.astype(np.uint32))
    packing = xatlas.PackOptions()
    packing.resolution = size
    packing.padding = GUTTER
    packing.bilinear = True
    atlas.generate(pack_options=packing)
    return atlas[0]


def export(name, out_dir, vertices, faces, uv, normals, colour, relief, roughness, relief_size=None):
    """The model, stood on its own base and centred, with its baked colour inside it and its baked
    normals in a file beside it."""
    low, high = vertices.min(axis=0), vertices.max(axis=0)
    vertices = vertices - np.array([(low[0] + high[0]) / 2, low[1], (low[2] + high[2]) / 2])
    material = trimesh.visual.material.PBRMaterial(name=name, baseColorTexture=colour,
                                                   metallicFactor=0.0, roughnessFactor=roughness)
    # xatlas's V runs down the image, the way it was baked; trimesh holds V up and flips it on export.
    out = trimesh.Trimesh(vertices=vertices, faces=faces, vertex_normals=normals, process=False,
                          visual=trimesh.visual.TextureVisuals(uv=np.column_stack([uv[:, 0], 1.0 - uv[:, 1]]),
                                                               material=material))
    out_dir.mkdir(parents=True, exist_ok=True)
    model = out_dir / f"{name}.glb"
    out.export(model)
    if relief_size is not None and relief_size != relief.width:
        relief = relief.resize((relief_size, relief_size), Image.LANCZOS)

    relief.save(out_dir / f"{name}{NORMAL_MAP_SUFFIX}")
    return model, high[1] - low[1]


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
    # Smooth within a face's own plane, sharp across a ridge or a corner.
    shaded = trimesh.graph.smooth_shade(
        trimesh.Trimesh(vertices=reduced.vertex_matrix(), faces=reduced.face_matrix(), process=False),
        angle=np.radians(BUILDING_CREASE))
    remap, faces, uv = unwrap(np.asarray(shaded.vertices), np.asarray(shaded.faces), BUILDING_TEXTURE)
    vertices = np.asarray(shaded.vertices)[remap]
    normals = np.asarray(shaded.vertex_normals)[remap]

    colour, relief, filled = bake(vertices, faces, uv, surface, BUILDING_TEXTURE)
    alpha, sway = sway_alpha(vertices, faces, uv, colour, BUILDING_TEXTURE, filled)
    painted = Image.fromarray(np.clip(np.concatenate([colour, alpha], axis=2), 0, 255).astype(np.uint8), "RGBA")
    model, height = export(model_name(path), BUILDING_DIR, vertices, faces, uv, normals, painted,
                           Image.fromarray(np.clip(relief, 0, 255).astype(np.uint8), "RGB"), roughness=0.9)
    print(f"  {model.stem:20s} {len(surface.faces):>10,} -> {len(faces):>6,} triangles   "
          f"{path.stat().st_size / 1048576:7.1f} MB -> {model.stat().st_size / 1048576:5.1f} MB   "
          f"flag {int((sway > 0).sum())} vertices   height {height:.2f}   ({time.time() - started:.0f}s)")


def main():
    paths = [Path(p) for p in sys.argv[1:]] or sorted(
        p for p in Path.home().joinpath("Downloads").glob("Meshy_AI_*.glb")
        if any(word in p.name.lower() for word in TREE_WORDS))
    print(f"Reducing {len(paths)} models into {OUT_DIR.parent.relative_to(PROJECT_DIR)}:")
    for path in paths:
        reduce(path)


if __name__ == "__main__":
    main()
