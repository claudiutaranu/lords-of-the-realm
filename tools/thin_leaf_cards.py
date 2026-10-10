"""Thins a leaf-card tree down to something a battlefield wood can stand by the hundred.

A game-artist's tree (a Sketchfab pack: bark meshes and foliage meshes, every leaf cluster its own
little triangle with an alpha-cut texture) is the opposite of a Meshy scan, and tools/decimate_meshy.py
is the wrong knife for it: shrink-wrapped, its crown would become a green blob. Here the crown is
already made of cards, so the cut is to keep a share of them, chosen at random, and grow each kept
card about its own middle so the crown stays as full as it was — the way a distant LOD is made by
hand. The bark is collapsed as a solid, with its UVs kept, since its texture tiles.

Every tree of the file (a root with its bark and foliage children, or a low-poly pack's single mesh) goes out as its own .glb, stood
on its base at the origin, Y up, its textures inside it. The exporter writes the leaves as
alpha-blended whatever Blender is told; the battlefield cuts them to alpha-scissor as it loads them
(Battlefield.GreatOaks).

    Blender -b --python tools/thin_leaf_cards.py -- <in.glb> <out-prefix> [keep] [bark-faces]

writes <out-prefix>-1.glb, -2.glb, ... keep is the share of leaf cards kept (0.15), bark-faces
the bark's triangles after the collapse (2000).
"""

import math
import random
import sys

import bmesh
import bpy

args = sys.argv[sys.argv.index("--") + 1:]
source, prefix = args[0], args[1]
keep = float(args[2]) if len(args) > 2 else 0.15
bark_faces = int(args[3]) if len(args) > 3 else 2000

# A card kept for every 1/keep is grown by this much in area; less than the full 1/keep, since the
# cards overlapped one another and a crown that keeps all its area reads as solid felt.
GROWTH = 0.4

random.seed(1215)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)


def thin_leaves(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    doomed = [f for f in bm.faces if random.random() > keep]
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    grow = math.sqrt(GROWTH / keep)
    for face in bm.faces:
        middle = face.calc_center_median()
        for vert in face.verts:
            vert.co = middle + (vert.co - middle) * grow
    bm.to_mesh(obj.data)
    bm.free()


def collapse_bark(obj):
    faces = len(obj.data.polygons)
    if faces > bark_faces:
        cut = obj.modifiers.new("collapse", "DECIMATE")
        cut.ratio = bark_faces / faces
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=cut.name)


# The pack's trees are its scene root's children: a bark group and a foliage group each, in turn —
# or, in a low-poly pack, one mesh a tree, which is light enough already and goes out whole.
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
trees = []
for obj in meshes:
    is_leaves = "foliage" in obj.parent.name
    if not is_leaves:
        trees.append([])
    trees[-1].append(obj)
    (thin_leaves if is_leaves else collapse_bark)(obj)

for number, parts in enumerate(trees, 1):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
        bpy.context.view_layer.objects.active = part
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.join()
    tree = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # Its foot at the origin: the middle of its trunk's base, wherever the pack stood it.
    low = min(v.co.z for v in tree.data.vertices)
    foot = [v.co for v in tree.data.vertices if v.co.z < low + 1.0]
    x = sum(c.x for c in foot) / len(foot)
    y = sum(c.y for c in foot) / len(foot)
    for v in tree.data.vertices:
        v.co.x -= x
        v.co.y -= y
        v.co.z -= low
    tris = sum(len(p.vertices) - 2 for p in tree.data.polygons)
    out = f"{prefix}-{number}.glb"
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=False,
                              export_yup=True)
    print(f"thinned {out}: {tris} triangles")
