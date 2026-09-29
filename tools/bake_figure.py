"""Bakes a rigged, animated soldier into a figure the battlefield can draw by the hundred.

A battlefield holds a few hundred men, and a few hundred skeletons each evaluated and skinned every
frame is not something the engine can do; one MultiMesh of a baked figure is. So the animation is
worked out here, once, into textures — where every vertex of the man is at every frame of every
clip — and battlefield-figure.gdshader reads the frame each man is at out of them.

  1. The glTF is read directly: its skin, its joints' hierarchy, and its clips, sampled at FPS.
  2. The figure is kept exactly as it was made, with the painted atlas it came with; the
     accessories' palette (a bowstring, an arrow) is set beside it in one image. Cutting it down
     was tried twice: a fresh unwrap broke the paint into specks that averaged into grey glass, and
     a collapse keeping the UVs tore the body's loose patches apart so the man could be seen
     through. It is cut down by whoever rigs it.
  3. At every frame of every clip, every vertex stands where the skin carries it.
  4. Out go: <key>.png, the atlas; <key>.figure.bin, the figure's own geometry (so the order of its
     vertices is this tool's and not an importer's) followed by the baked positions and normals as
     half floats and bytes, one row a frame; and <key>.figure.json, where each of those starts and which
     rows are which clip.

Run: tools/.venv/bin/python tools/bake_figure.py <key> path-to-animated.glb
     (the key is the kind of soldier in recruits.json: bow, spear, sword ...)
"""

import json
import struct
import sys
import time
from pathlib import Path

import numpy as np
import trimesh
from PIL import Image
from io import BytesIO

sys.path.insert(0, str(Path(__file__).resolve().parent))
from decimate_meshy import OriginalSurface, UNIT_DIR

# How finely the clips are sampled. The shader blends between two frames, so fifteen a second
# reads as smooth as the thirty the clips were made at.
FPS = 15

# The widest the atlas is written, in pixels: the body's own painting is 2048 across, and a man
# on the field is never that big on the screen.
ATLAS_WIDTH = 1024

# How wide the baked textures are laid out, in texels.
ROW_WIDTH = 4096


COMPONENTS = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTHS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Gltf:
    """Just enough of a .glb to read a skinned, animated mesh out of it."""

    def __init__(self, path):
        data = path.read_bytes()
        length = struct.unpack_from("<I", data, 12)[0]
        self.json = json.loads(data[20:20 + length])
        self.bin = data[20 + length + 8:]

    def view(self, index):
        view = self.json["bufferViews"][index]
        start = view.get("byteOffset", 0)
        return self.bin[start:start + view["byteLength"]]

    def accessor(self, index):
        acc = self.json["accessors"][index]
        view = self.json["bufferViews"][acc["bufferView"]]
        dtype = COMPONENTS[acc["componentType"]]
        width = WIDTHS[acc["type"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride", 0)
        item = np.dtype(dtype).itemsize * width
        if stride and stride != item:
            rows = [np.frombuffer(self.bin, dtype, width, start + i * stride) for i in range(acc["count"])]
            values = np.stack(rows)
        else:
            values = np.frombuffer(self.bin, dtype, acc["count"] * width, start).reshape(acc["count"], width)
        values = values.astype(np.float64) if dtype == np.float32 else values
        if acc.get("normalized"):
            values = values / np.iinfo(dtype).max
        return values

    def image(self, index):
        return Image.open(BytesIO(self.view(self.json["images"][index]["bufferView"]))).convert("RGB")


def quaternion_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


class Rig:
    """The skeleton, posed at any moment of any clip."""

    def __init__(self, gltf):
        self.gltf = gltf
        nodes = gltf.json["nodes"]
        self.parent = {child: i for i, node in enumerate(nodes) for child in node.get("children", [])}
        self.rest = [(np.array(n.get("translation", [0, 0, 0]), float), np.array(n.get("rotation", [0, 0, 0, 1]), float),
                      np.array(n.get("scale", [1, 1, 1]), float)) for n in nodes]
        skin = gltf.json["skins"][0]
        self.joints = skin["joints"]
        self.inverse_bind = gltf.accessor(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        self.clips = {a["name"]: a for a in gltf.json["animations"]}

    def duration(self, name):
        clip = self.clips[name]
        return max(float(self.gltf.accessor(s["input"]).max()) for s in clip["samplers"])

    def joint_matrices(self, name, t):
        """The skin's joint matrices at `t` seconds into a clip: LINEAR keys blended, STEP keys held."""
        pose = [list(trs) for trs in self.rest]
        clip = self.clips[name]
        for channel in clip["channels"]:
            sampler = clip["samplers"][channel["sampler"]]
            times = self.gltf.accessor(sampler["input"]).ravel()
            values = self.gltf.accessor(sampler["output"])
            key = max(0, int(np.searchsorted(times, t + 1e-6)) - 1)
            slot = {"translation": 0, "rotation": 1, "scale": 2}[channel["target"]["path"]]
            value = values[key]
            # LINEAR keys are blended to the moment asked for; held from key to key instead, a
            # limb keyed a few times a second jumped from pose to pose, and the man twitched.
            if sampler.get("interpolation", "LINEAR") == "LINEAR" and key + 1 < len(times) and times[key + 1] > times[key]:
                u = float(np.clip((t - times[key]) / (times[key + 1] - times[key]), 0.0, 1.0))
                after = values[key + 1]
                if slot == 1:
                    after = after if np.dot(value, after) >= 0 else -after
                    value = value * (1 - u) + after * u
                    value = value / np.linalg.norm(value)
                else:
                    value = value * (1 - u) + after * u
            pose[channel["target"]["node"]][slot] = value

        world = {}

        def global_of(i):
            if i in world:
                return world[i]
            t_, r, s = pose[i]
            local = np.eye(4)
            local[:3, :3] = quaternion_matrix(r / np.linalg.norm(r)) * s
            local[:3, 3] = t_
            world[i] = global_of(self.parent[i]) @ local if i in self.parent else local
            return world[i]

        return np.stack([global_of(j) for j in self.joints]) @ self.inverse_bind


def skinned(points, normals, joints, weights, matrices):
    """Linear blend skinning of every vertex by the matrices of the joints it hangs from."""
    blend = np.einsum("vk,vkij->vij", weights, matrices[joints])
    moved = np.einsum("vij,vj->vi", blend[:, :3, :3], points) + blend[:, :3, 3]
    turned = np.einsum("vij,vj->vi", blend[:, :3, :3], normals)
    return moved, turned / np.maximum(np.linalg.norm(turned, axis=1, keepdims=True), 1e-12)


def primitives(gltf):
    """Each part of the mesh: its points, normals, UVs (V up, as trimesh holds them), faces, joints,
    weights and the colour texture it is painted with."""
    parts = []
    for prim in gltf.json["meshes"][0]["primitives"]:
        attrs = prim["attributes"]
        material = gltf.json["materials"][prim["material"]]
        texture = gltf.json["textures"][material["pbrMetallicRoughness"]["baseColorTexture"]["index"]]
        uv = gltf.accessor(attrs["TEXCOORD_0"])
        parts.append({
            "points": gltf.accessor(attrs["POSITION"]),
            "normals": gltf.accessor(attrs["NORMAL"]),
            "uv": np.column_stack([uv[:, 0], 1.0 - uv[:, 1]]),
            "faces": gltf.accessor(prim["indices"]).reshape(-1, 3).astype(np.int64),
            "joints": gltf.accessor(attrs["JOINTS_0"]).astype(np.int64),
            "weights": gltf.accessor(attrs["WEIGHTS_0"]).astype(np.float64),
            "image": gltf.image(texture["source"]),
        })
    return parts


def combined(parts):
    """Every part as one original: their textures side by side in one image, their UVs moved to
    match. Returns the image too, and for each part how its own UVs are carried onto it."""
    width = sum(p["image"].width for p in parts)
    height = max(p["image"].height for p in parts)
    sheet = Image.new("RGB", (width, height))
    points, normals, uvs, faces, joints, weights, left, base, placed = [], [], [], [], [], [], 0, 0, []
    for part in parts:
        image = part["image"]
        sheet.paste(image, (left, height - image.height))
        scale, offset = np.array([image.width / width, image.height / height]), np.array([left / width, 0.0])
        placed.append((scale, offset))
        uv = part["uv"] * scale + offset
        points.append(part["points"]); normals.append(part["normals"]); uvs.append(uv)
        faces.append(part["faces"] + base); joints.append(part["joints"]); weights.append(part["weights"])
        left += image.width
        base += len(part["points"])
    mesh = trimesh.Trimesh(vertices=np.vstack(points), faces=np.vstack(faces), vertex_normals=np.vstack(normals),
                           process=False,
                           visual=trimesh.visual.TextureVisuals(uv=np.vstack(uvs),
                                                                material=trimesh.visual.material.PBRMaterial(
                                                                    baseColorTexture=sheet)))
    return mesh, np.vstack(joints), np.vstack(weights), sheet, placed


def cloth_value(atlas):
    """The median brightness of the blue cloth the livery is painted on, in linear light — what the
    lord's colour is pinned to (livery.gdshaderinc). Measured, because each painter's blue is its
    own: pinned to the standard-bearer's brighter blue, the archer's yellow came out olive."""
    rgb = np.asarray(atlas.convert("RGB"), np.float64) / 255.0
    linear = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    cloth = linear[..., 2] - np.maximum(linear[..., 0], linear[..., 1]) > 0.04
    return float(np.median(linear[..., 2][cloth])) if cloth.any() else 0.11


def main():
    key, path = sys.argv[1], Path(sys.argv[2])
    started = time.time()
    gltf = Gltf(path)
    rig = Rig(gltf)
    parts = primitives(gltf)
    original, joints, weights, sheet, placed = combined(parts)
    surface = OriginalSurface(original)

    # Every part kept exactly as it was made. Cut down, the body — which arrives as a skin of loose
    # patches — came apart at their seams and the man could be seen through.
    vertices, rest_normals, uv, faces = [], [], [], []
    for part, (scale, offset) in zip(parts, placed):
        faces.append(part["faces"] + sum(len(v) for v in vertices))
        vertices.append(part["points"]); rest_normals.append(part["normals"])
        uv.append(part["uv"] * scale + offset)
    vertices, rest_normals, uv, faces = np.vstack(vertices), np.vstack(rest_normals), np.vstack(uv), np.vstack(faces)
    # The atlas is written V down, the way the engine reads it.
    uv = np.column_stack([uv[:, 0], 1.0 - uv[:, 1]])

    # Where on the original each vertex of the figure lies, to be carried with it.
    _, nearest_vertex = surface.tree.query(vertices)
    face, bary = surface._nearest(vertices, nearest_vertex)
    corners = surface.faces[face]

    frames, clips, row = [], {}, 0
    for name in rig.clips:
        seconds = rig.duration(name)
        loop = name.endswith("_loop")
        count = max(1, int(round(seconds * FPS)))
        # A loop gets its first frame again at the end, so the shader blending into the next row
        # never blends into the next clip.
        steps = [min(seconds, k / FPS) for k in range(count)] + ([0.0] if loop else [])
        if loop:
            # A loop's first and last moment are the same moment, and its last key is the one to
            # trust: an export can leave the first key of a clip holding the pose of the clip before
            # it (the archer's idle began on a walking stride), and a man standing easy kicked out a
            # leg every time his idle came round.
            steps = [seconds if t == 0.0 else t for t in steps]
        for t in steps:
            moved, turned = skinned(original.vertices, original.vertex_normals, joints, weights,
                                    rig.joint_matrices(name, t))
            at = np.einsum("vk,vkj->vj", bary, moved[corners])
            normal = np.einsum("vk,vkj->vj", bary, turned[corners])
            normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-12)
            frames.append((at, normal))
        clips[name] = {"start": row, "frames": count, "seconds": seconds, "loop": loop}
        row += len(steps)

    UNIT_DIR.mkdir(parents=True, exist_ok=True)
    atlas = sheet.resize((ATLAS_WIDTH, round(sheet.height * ATLAS_WIDTH / sheet.width)), Image.LANCZOS)
    atlas.save(UNIT_DIR / f"{key}.png")

    # Positions as half floats; normals, which only light the man, a byte a component.
    # Frame after frame, vertex after vertex, wrapped into rows ROW_WIDTH wide: one row a frame
    # would be wider than a texture may be for a man of more than 16,384 vertices.
    texels = len(frames) * len(vertices)
    height = -(-texels // ROW_WIDTH)
    positions = np.zeros((height * ROW_WIDTH, 4), np.float16)
    turned = np.zeros((height * ROW_WIDTH, 4), np.uint8)
    for r, (at, normal) in enumerate(frames):
        positions[r * len(vertices):(r + 1) * len(vertices), :3] = at
        turned[r * len(vertices):(r + 1) * len(vertices), :3] = np.round((normal * 0.5 + 0.5) * 255)

    blocks = [vertices.astype(np.float32).tobytes(), rest_normals.astype(np.float32).tobytes(),
              np.column_stack([uv[:, 0], uv[:, 1]]).astype(np.float32).tobytes(),
              faces.astype(np.uint32).tobytes(), positions.tobytes(), turned.tobytes()]
    offsets, at = [], 0
    for block in blocks:
        offsets.append(at)
        at += len(block)
    (UNIT_DIR / f"{key}.figure.bin").write_bytes(b"".join(blocks))
    meta = {
        "vertices": len(vertices), "indices": int(faces.size), "rows": len(frames), "fps": FPS,
        "texture_width": ROW_WIDTH, "texture_height": height,
        "offsets": dict(zip(["points", "normals", "uv", "indices", "positions", "turned"], offsets)),
        "height": float(vertices[:, 1].max() - vertices[:, 1].min()),
        "clips": clips,
        "cloth_value": cloth_value(atlas),
    }
    (UNIT_DIR / f"{key}.figure.json").write_text(json.dumps(meta, indent=1))
    print(f"  {key}: {len(original.faces):,} -> {len(faces):,} triangles, {len(vertices):,} vertices, "
          f"{len(frames)} frames ({', '.join(clips)}), {at / 1048576:.1f} MB   ({time.time() - started:.0f}s)")


if __name__ == "__main__":
    main()
