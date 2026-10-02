"""Turns a rigged soldier saved as a .blend into the .glb tools/bake_figure.py reads.

The baker takes one skinned mesh and the clips on it, and a rig made in Blender is rarely that: the
peasant's pitchfork was its own mesh hung from his hand bone, and the file kept spare takes of his
run beside the real one. So: only the clips named are kept, and every mesh hung from a bone is
skinned wholly to that bone, at the rest pose, and joined into the body — cut down first to
PROP_TRIANGLES: the pitchfork came at sixteen thousand, and four hundred and eighty men each drew it.

Run: /Applications/Blender.app/Contents/MacOS/Blender -b rig.blend --python tools/export_blend.py -- \\
         out.glb idle_loop walk_loop run_loop attack
"""

import sys

import bpy

# The most triangles a prop keeps. A haft and its tines need no more; the body is left as it was made,
# since a Meshy body cut down comes apart at its seams (tools/bake_figure.py).
PROP_TRIANGLES = 600

out, *clips = sys.argv[sys.argv.index("--") + 1:]
for action in list(bpy.data.actions):
    if action.name not in clips:
        bpy.data.actions.remove(action)
    else:
        action.use_fake_user = True

rig = next(o for o in bpy.data.objects if o.type == "ARMATURE" and o.children)
skinned = [o for o in rig.children if o.type == "MESH" and o.parent_type != "BONE"]
props = [o for o in rig.children if o.type == "MESH" and o.parent_type == "BONE"]
body = max(skinned, key=lambda o: len(o.data.vertices))

# Skinned against the rest pose, so each prop is taken where it rests.
rig.data.pose_position = "REST"
bpy.context.view_layer.update()
for prop in props:
    triangles = sum(len(face.vertices) - 2 for face in prop.data.polygons)
    if triangles > PROP_TRIANGLES:
        cut = prop.modifiers.new("cut", "DECIMATE")
        cut.ratio = PROP_TRIANGLES / triangles
        bpy.context.view_layer.objects.active = prop
        bpy.ops.object.modifier_apply(modifier=cut.name)
    bone, world = prop.parent_bone, prop.matrix_world.copy()
    prop.parent = None
    prop.matrix_world = world
    prop.vertex_groups.new(name=bone).add(range(len(prop.data.vertices)), 1.0, "REPLACE")

bpy.ops.object.select_all(action="DESELECT")
for part in skinned + props:
    part.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
rig.data.pose_position = "POSE"

bpy.ops.object.select_all(action="DESELECT")
rig.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=True,
                          export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_step=1)
print(f"exported {body.name} with {len(props)} prop(s) and clips {sorted(clips)} to {out}")
