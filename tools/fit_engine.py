"""Fits a bought siege engine to the battlefield, the way the castle kit's own engines come.

A store model (Fab's medieval mangonel) arrives as several meshes sharing one material, its rope
modelled strand by strand and its paint at 4K a map — more than three engines a side, seen from a
hill, will ever show. Here the meshes become one, the rope is collapsed, the maps are shrunk, and
the engine is stood on its wheels at the origin facing +x (the way BattlefieldWalls.Engine turns
the kit's), in metres.

    Blender -b --python tools/fit_engine.py -- <in.glb> <out.glb> <forward: +y|-y|+x|-x> [map-side]

forward is the way the engine throws or drives in the source; map-side is the maps' width (1024).
"""

import math
import sys

import bpy
import mathutils

args = sys.argv[sys.argv.index("--") + 1:]
source, out, forward = args[0], args[1], args[2]
map_side = int(args[3]) if len(args) > 3 else 1024

# The rope's strands are two thirds of the engine's triangles and a few pixels on screen.
ROPE_FACES = 1500
TURNS = {"+x": 0.0, "+y": -math.pi / 2, "-x": math.pi, "-y": math.pi / 2}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for obj in meshes:
    faces = len(obj.data.polygons)
    if "rope" in obj.name and faces > ROPE_FACES:
        cut = obj.modifiers.new("collapse", "DECIMATE")
        cut.ratio = ROPE_FACES / faces
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=cut.name)

bpy.ops.object.select_all(action="DESELECT")
for obj in meshes:
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.join()
engine = bpy.context.view_layer.objects.active
# The import's own turn to Z-up is on the object; set down first, so the turn below is about the vertical.
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
engine.data.transform(mathutils.Matrix.Rotation(TURNS[forward], 4, "Z"))

low = min(v.co.z for v in engine.data.vertices)
xs = [v.co.x for v in engine.data.vertices]
ys = [v.co.y for v in engine.data.vertices]
middle = ((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2)
for v in engine.data.vertices:
    v.co.x -= middle[0]
    v.co.y -= middle[1]
    v.co.z -= low

for image in bpy.data.images:
    if image.size[0] > map_side:
        image.scale(map_side, map_side)

tris = sum(len(p.vertices) - 2 for p in engine.data.polygons)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=False,
                          export_yup=True, export_image_format="JPEG")
print(f"fitted {out}: {tris} triangles, {max(xs) - min(xs):.2f} m long")
