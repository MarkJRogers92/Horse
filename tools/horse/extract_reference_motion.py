"""Extract evaluated gait landmarks; never execute or save the reference file.

Run with Blender --background --factory-startup --disable-autoexec --python
this_file -- --source '/path/to/Horse Rigged All Gaits.blend'.
Only armature data/actions are appended; no reference mesh is redistributed.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--out", type=Path, default=Path(__file__).with_name("reference_motion_samples.json"))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    with bpy.data.libraries.load(str(args.source), link=False) as (src, dst):
        dst.objects = ["horse.rig", "horse"]
        dst.actions = ["horse.walk", "horse.trot"]
    rig = bpy.data.objects["horse.rig"]
    body = bpy.data.objects["horse"]
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.scene.collection.objects.link(body)
    for modifier in body.modifiers:
        if modifier.type != "ARMATURE":
            modifier.show_viewport = False
    for constraint in rig.constraints:
        constraint.mute = True
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()
    with bpy.data.libraries.load(str(args.source), link=True) as (src, dst):
        dst.scenes = ["Scene"]
    fps = dst.scenes[0].render.fps / dst.scenes[0].render.fps_base
    names = ["DEF-spine.001", "DEF-spine.003", "DEF-spine.006", "DEF-neck.001", "DEF-neck.004", "DEF-head", "DEF-skull"]
    for side in ("L", "R"):
        for part in ("shoulder", "upper_arm", "upper_arm.%s", "forearm", "forearm.%s", "forefoot", "forefoot.%s", "f_toe", "f_hoof", "thigh", "thigh.%s", "lower_leg", "lower_leg.%s", "hind_foot", "hind_foot.%s", "r_toe", "r_hoof"):
            name = "DEF-" + part.replace(".%s", "." + side + ".001") if "%s" in part else "DEF-" + part + "." + side
            names.append(name)
    hoof_vertices = {}
    by_index = {group.index: group.name for group in body.vertex_groups}
    for leg in ("Front_L", "Front_R", "Hind_L", "Hind_R"):
        name = ("DEF-f_hoof." if leg.startswith("Front") else "DEF-r_hoof.") + leg[-1]
        hoof_vertices[leg] = [vertex.index for vertex in body.data.vertices
                              if any(by_index[item.group] == name and item.weight > 0.30 for item in vertex.groups)]
        if not hoof_vertices[leg]:
            raise RuntimeError(f"No physical hoof vertices for {name}")

    def hoof_snapshot():
        evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        try:
            result = {}
            for leg, indices in hoof_vertices.items():
                points = [evaluated.matrix_world @ mesh.vertices[index].co for index in indices]
                minimum = min(point.z for point in points)
                low = [point for point in points if point.z <= minimum + 0.003]
                result[leg] = {"minimumZ": round(minimum, 6),
                               "contactCentroid": [round(sum(point[axis] for point in low)/len(low), 6) for axis in range(3)],
                               "vertexCount": len(points)}
            return result
        finally:
            evaluated.to_mesh_clear()

    def snapshot():
        result = {}
        for name in names:
            bone = rig.pose.bones[name]
            matrix = rig.matrix_world @ bone.matrix
            result[name] = {"head": [round(x, 6) for x in rig.matrix_world @ bone.head],
                            "tail": [round(x, 6) for x in rig.matrix_world @ bone.tail],
                            "quatWXYZ": [round(x, 7) for x in matrix.to_quaternion()]}
        return result

    sha = hashlib.sha256()
    with args.source.open("rb") as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            sha.update(block)
    result = {"source": {"title": "Horse Rigged All Gaits", "creator": "fdoss001",
                         "originalMeshCredit": "Tarnyloo (BlendSwap 17172, CC-BY; mesh is not included)",
                         "url": "https://blendswap.com/blend/28627", "license": "CC0 listing",
                         "note": "Evaluated walk/trot motion only; no source mesh or materials."},
              "sourceSHA256": sha.hexdigest(), "sourceFPS": fps,
              "units": "source scene world metres, Z-up, -Y-forward,+X-left; path/object constraints muted",
              "rest": snapshot(), "bind": {}, "clips": {}}
    for name in names:
        bone = rig.data.bones[name]
        matrix = rig.matrix_world @ bone.matrix_local
        result["bind"][name] = {"head": list(rig.matrix_world @ bone.head_local),
                                "tail": list(rig.matrix_world @ bone.tail_local),
                                "quatWXYZ": list(matrix.to_quaternion())}
    for name in ("horse.walk", "horse.trot"):
        action = bpy.data.actions[name]
        for bone in rig.pose.bones:
            bone.matrix_basis.identity()
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        rows = []
        for index in range(81):
            frame = start + (end - start) * index / 80
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame % 1)
            bpy.context.view_layer.update()
            rows.append({"phase": index / 80, "frame": frame, "bones": snapshot(), "hoofSurfaces": hoof_snapshot()})
        result["clips"][name.removeprefix("horse.")] = {"frameRange": [start, end],
                                                          "durationSeconds": (end - start) / fps, "samples": rows}
    args.out.write_text(json.dumps(result, separators=(",", ":")) + "\n")
    print("Extracted", len(names), "landmarks from", result["sourceSHA256"], "to", args.out)


if __name__ == "__main__":
    main()
