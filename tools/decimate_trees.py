"""Cuts the Meshy tree models down to something a campaign map can scatter by the thousand.

A Meshy export is a scanned-looking surface of three to eight million triangles with its look baked
into a texture. On the map a tree is thirty to sixty pixels tall and there are thousands of them, so
each one can afford a few hundred triangles.

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

Also, per model: stood on its own origin, base at y = 0 and centred, the way the scatter expects a
prop to stand.

Needs, in tools/.venv on top of what rebuild-map.sh installs: pip install pymeshlab trimesh scipy

Run: tools/.venv/bin/python tools/decimate_trees.py [path-to.glb ...]
     (with no arguments it takes every Meshy tree in ~/Downloads)
"""

import sys
import time
from pathlib import Path

import numpy as np
import pymeshlab
import trimesh
from PIL import Image
from scipy.spatial import cKDTree

PROJECT_DIR = Path(__file__).resolve().parent.parent
OUT_DIR = PROJECT_DIR / "assets" / "models" / "trees"

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


def reduce(path):
    started = time.time()
    scene = trimesh.load(path, force="scene")
    mesh = trimesh.util.concatenate([g for g in scene.dump() if isinstance(g, trimesh.Trimesh)])
    source_texture = mesh.visual.material.baseColorTexture
    source_uv = np.asarray(mesh.visual.uv, dtype=np.float64)
    source_points = np.asarray(mesh.vertices, dtype=np.float64)

    name = path.stem.replace("Meshy_AI_", "").rsplit("_", 2)[0].lower().replace(" ", "-").replace("_", "-")
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


def main():
    paths = [Path(p) for p in sys.argv[1:]] or sorted(
        p for p in Path.home().joinpath("Downloads").glob("Meshy_AI_*.glb")
        if any(word in p.name.lower() for word in ("tree", "pine", "oak", "guardian", "cypress")))
    print(f"Reducing {len(paths)} trees into {OUT_DIR.relative_to(PROJECT_DIR)}:")
    for path in paths:
        reduce(path)


if __name__ == "__main__":
    main()
