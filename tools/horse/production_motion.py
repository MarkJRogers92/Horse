"""Candidate-derived horse skeleton adapters, transferred skinning, and motion.

Adult and foal Meshy Smart Rig GLBs are read-only sources. Their major joints
receive stable equine names and barycentric skin weights transfer onto the
family-warped production meshes with a four-influence cap. Foal hierarchy
repairs are isolated from the adult adapter; neither adapter accepts locomotion
as production without pose review.
"""
from __future__ import annotations

import hashlib
import math
from pathlib import Path
from typing import Callable, Iterable

import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

RIG_VERSION = "cloverbrook.meshy-smart-rig-transfer.v1"
PROJECT_ROOT = Path(__file__).resolve().parents[2]
SMART_RIG_SOURCE = PROJECT_ROOT / "art/candidates/meshy/light_01/smart-rig.glb"
FOAL_SMART_RIG_SOURCE = PROJECT_ROOT / "art/candidates/meshy/foal_01/smart-rig-retry.glb"
MAX_INFLUENCES = 4

# Smart Rig IDs preserve the website's original hierarchy.  The untouched
# IDs remain readable as Smart_Bone_NNN; these aliases give runtime clips and
# the existing importer a stable equine vocabulary.
BONE_ALIASES = {
    "Bone_000": "Pelvis_Root",
    "Bone_001": "Pelvis",
    "Bone_014": "Spine",
    "Bone_013": "Spine_Mid",
    "Bone_012": "Spine_Upper",
    "Bone_011": "Chest",
    "Bone_010": "Neck_Base",
    "Bone_009": "Neck",
    "Bone_033": "Neck_Mid",
    "Bone_032": "Neck_Upper",
    "Bone_031": "Neck_Top",
    "Bone_030": "Head_Base",
    "Bone_029": "Head",
    "Bone_028": "Head_Upper",
    "Bone_027": "Head_Nose",
    "Bone_050": "Jaw_Upper",
    "Bone_049": "Jaw_Mid",
    "Bone_048": "Jaw_Tip",
    "Bone_008": "Tail_01",
    "Bone_007": "Tail_02",
    "Bone_006": "Tail_03",
    "Bone_005": "Tail_04",
    "Bone_004": "Tail_05",
    "Bone_003": "Tail_06",
    "Bone_002": "Tail_07",
    "Bone_056": "Ear_L",
    "Bone_055": "Ear_L_02",
    "Bone_054": "Ear_L_03",
    "Bone_053": "Ear_R",
    "Bone_052": "Ear_R_02",
    "Bone_051": "Ear_R_03",
    "Bone_047": "Front_L_Upper",
    "Bone_046": "Front_L_Forearm",
    "Bone_045": "Front_L_Cannon",
    "Bone_044": "Front_L_Pastern",
    "Bone_043": "Front_L_Hoof",
    "Bone_042": "Front_L_Hoof_02",
    "Bone_041": "Front_L_Hoof_03",
    "Bone_040": "Front_R_Upper",
    "Bone_039": "Front_R_Forearm",
    "Bone_038": "Front_R_Cannon",
    "Bone_037": "Front_R_Pastern",
    "Bone_036": "Front_R_Hoof",
    "Bone_035": "Front_R_Hoof_02",
    "Bone_034": "Front_R_Hoof_03",
    "Bone_026": "Hind_L_Upper",
    "Bone_025": "Hind_L_Gaskin",
    "Bone_024": "Hind_L_Cannon",
    "Bone_023": "Hind_L_Pastern",
    "Bone_022": "Hind_L_Hoof",
    "Bone_021": "Hind_L_Hoof_02",
    "Bone_020": "Hind_R_Upper",
    "Bone_019": "Hind_R_Gaskin",
    "Bone_018": "Hind_R_Cannon",
    "Bone_017": "Hind_R_Pastern",
    "Bone_016": "Hind_R_Hoof",
    "Bone_015": "Hind_R_Hoof_02",
}
REQUIRED_BONES = {
    "Root", "Pelvis", "Spine", "Chest", "Neck_Base", "Neck_Mid", "Neck",
    "Head", "Ear_L", "Ear_R", "Tail_01", "Tail_02", "Tail_03",
    *(f"Front_{side}_{part}" for side in ("L", "R")
      for part in ("Upper", "Forearm", "Cannon", "Pastern", "Hoof")),
    *(f"Hind_{side}_{part}" for side in ("L", "R")
      for part in ("Upper", "Gaskin", "Cannon", "Pastern", "Hoof")),
}


def _clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def _smoothstep(lo: float, hi: float, value: float) -> float:
    t = _clamp((value - lo) / max(1e-9, hi - lo), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _segment_distance_squared(point: Vector, start: Vector, end: Vector) -> float:
    delta = end - start
    t = _clamp((point - start).dot(delta) / max(1e-10, delta.length_squared), 0.0, 1.0)
    return (point - (start + t * delta)).length_squared


def _barycentric(point: Vector, a: Vector, b: Vector, c: Vector) -> tuple[float, float, float]:
    ab, ac, ap = b - a, c - a, point - a
    d00, d01, d11 = ab.dot(ab), ab.dot(ac), ac.dot(ac)
    d20, d21 = ap.dot(ab), ap.dot(ac)
    denominator = d00 * d11 - d01 * d01
    if abs(denominator) < 1e-12:
        return (1.0, 0.0, 0.0)
    v = (d11 * d20 - d01 * d21) / denominator
    w = (d00 * d21 - d01 * d20) / denominator
    raw = [max(0.0, 1.0 - v - w), max(0.0, v), max(0.0, w)]
    total = sum(raw)
    return tuple(value / total for value in raw)


def _quantized_weights(weights: list[tuple[str, float]]) -> list[tuple[str, int]]:
    """Apportion exactly 255 integer units, keeping normalized sums at one."""
    if len(weights) == 1:
        return [(weights[0][0], 255)]
    total = sum(max(0.0, value) for _, value in weights)
    scaled = [(name, max(0.0, value) / total * 255.0) for name, value in weights]
    result = [(name, math.floor(value)) for name, value in scaled]
    remaining = 255 - sum(value for _, value in result)
    order = sorted(range(len(scaled)), key=lambda i: scaled[i][1] - result[i][1], reverse=True)
    for i in order[:remaining]:
        name, value = result[i]
        result[i] = (name, value + 1)
    return result


def _family_transform(point: Vector, family: str) -> Vector:
    """Fallback matching the current production builder's family space-warp.

    The builder passes its own callback in normal use.  This fallback preserves
    compatibility with direct probes and the previously agreed basic warp.
    """
    x, y, z = point
    spec = {"light": (0.035, 1.0, 0.045),
            "stock": (-0.080, 1.20, -0.055)}.get(family)
    if spec is None:
        raise ValueError(f"Unknown adult family {family!r}")
    length, width, neck = spec
    def smooth(a: float, b: float, value: float) -> float:
        t = _clamp((value - a) / max(1e-8, b - a), 0.0, 1.0)
        return t * t * (3.0 - 2.0 * t)
    def gaussian(v: float, center: float, spread: float) -> float:
        return math.exp(-((v - center) / spread) ** 2)
    trunk = smooth(-0.88, -0.55, x) * (1.0 - smooth(0.02, 0.38, x)) * smooth(0.74, 1.13, z)
    t = smooth(-0.63, 0.23, x)
    x += length * (t - 0.5) * trunk
    chest = gaussian(x, -0.02, 0.50) * smooth(0.70, 1.18, z)
    y *= 1.0 + (width - 1.0) * chest
    x += neck * smooth(0.30, 0.67, x) * smooth(1.10, 1.50, z)
    return Vector((x, y, z))


def _source_to_target(raw: Vector, height: float, family: str,
                      family_point: Callable[[Vector, str], Vector] | None) -> Vector:
    # Smart Rig source faces raw -Y.  Scale its 1.7m rest height to the baked
    # production body bounds and rotate to +X forward / +Y anatomical left.
    mapped = Vector((-raw.y, raw.x, raw.z)) * (height / 1.7)
    return (family_point or _family_transform)(mapped, family)


def _clear_vertex_groups(mesh: bpy.types.Object) -> None:
    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)


def _write_transferred_weights(source: bpy.types.Object, target: bpy.types.Object,
                               allowed: set[str] | None = None,
                               fallback_bone: str | None = None,
                               localize_body_weights: bool = True) -> dict:
    source.data.calc_loop_triangles()
    triangles = [tuple(triangle.vertices) for triangle in source.data.loop_triangles]
    source_coords = [vertex.co.copy() for vertex in source.data.vertices]
    bvh = BVHTree.FromPolygons(source_coords, triangles, all_triangles=True)
    source_groups = {group.index: group.name for group in source.vertex_groups}
    source_vertices = source.data.vertices
    target_coords = [vertex.co.copy() for vertex in target.data.vertices]
    rig = source.find_armature()
    local_segments = {}
    if localize_body_weights and target.name.lower() == "body" and rig is not None:
        local_segments = {bone.name: (bone.head_local.copy(), bone.tail_local.copy())
                          for bone in rig.data.bones if bone.use_deform}
    per_vertex: list[list[tuple[str, float]]] = []
    max_distance = 0.0
    for point in target_coords:
        hit = bvh.find_nearest(point)
        if hit is None or hit[2] < 0:
            raise RuntimeError(f"Could not project {target.name} onto Smart Rig source mesh")
        nearest, _normal, triangle_index, distance = hit
        max_distance = max(max_distance, distance)
        tri = triangles[triangle_index]
        bary = _barycentric(nearest, *(source_coords[index] for index in tri))
        blended: dict[str, float] = {}
        for source_index, coefficient in zip(tri, bary):
            for assignment in source_vertices[source_index].groups:
                name = source_groups[assignment.group]
                if allowed is None or name in allowed:
                    blended[name] = blended.get(name, 0.0) + coefficient * assignment.weight
        if local_segments:
            # Keep Smart Rig's authored weights, but attenuate transfer spill
            # onto the barrel/neck when a point lies far from its rest segment.
            # Lost influence is reassigned to the thorax before normalization;
            # otherwise a collar vertex with only neck weights becomes 100%
            # neck after normalization and creates a hard wither shelf.
            thoracic_transfer = 0.0
            for name, weight in list(blended.items()):
                segment = local_segments.get(name)
                if segment is None:
                    continue
                sigma = None
                if name.startswith("Front_"):
                    # The shaft/hoof surface is genuinely part of the limb.
                    # Preserve the source's authored skin there; transferring
                    # its attenuated mass to Chest would lock the lower leg to
                    # the torso and create folded knees during a step.
                    front_limb_surface = point.z < 0.58 and point.x > -0.05
                    sigma = (0.17 if name.endswith("_Upper") else
                             0.14 if name.endswith("_Forearm") else
                             0.115 if name.endswith("_Cannon") else 0.09)
                    gate = _smoothstep(0.04, 0.26, point.x)
                    if name.endswith("_Cannon"):
                        gate *= 1.0 - _smoothstep(0.52, 0.84, point.z)
                    elif name.endswith("_Pastern"):
                        gate *= 1.0 - _smoothstep(0.20, 0.42, point.z)
                    elif "_Hoof" in name:
                        gate *= 1.0 - _smoothstep(0.12, 0.31, point.z)
                elif name.startswith(("Neck", "Head", "Jaw", "Ear_")):
                    sigma = 0.18 if name in {"Neck_Base", "Neck"} else 0.16
                    gate = 1.0
                    if name.startswith("Neck"):
                        # A soft anatomical collar keeps withers and sternum
                        # on the thoracic skeleton while preserving the neck
                        # chain above its real emergence from the chest.
                        gate *= _smoothstep(0.18, 0.46, point.x)
                        gate *= _smoothstep(0.88, 1.20, point.z)
                if sigma is not None:
                    if name.startswith("Front_") and front_limb_surface:
                        attenuation = 1.0
                    else:
                        distance2 = _segment_distance_squared(point, segment[0], segment[1])
                        attenuation = gate * math.exp(-distance2 / (2.0 * sigma * sigma))
                    retained = weight * attenuation
                    blended[name] = retained
                    if (name.startswith("Neck") or
                            (name.startswith("Front_") and not front_limb_surface)):
                        thoracic_transfer += max(0.0, weight - retained)
            if thoracic_transfer > 0.0 and point.x < 0.55:
                blended["Chest"] = blended.get("Chest", 0.0) + thoracic_transfer
        chosen = sorted(blended.items(), key=lambda pair: pair[1], reverse=True)[:MAX_INFLUENCES]
        if not chosen or sum(weight for _, weight in chosen) <= 1e-10:
            if fallback_bone is None:
                raise RuntimeError(f"Projected weights were empty for {target.name} vertex")
            chosen = [(fallback_bone, 1.0)]
        else:
            chosen = [(name, weight) for name, weight in chosen if weight > 1e-7]
        per_vertex.append(_quantized_weights(chosen))

    _clear_vertex_groups(target)
    names = sorted({name for weights in per_vertex for name, _units in weights})
    groups = {name: target.vertex_groups.new(name=name) for name in names}
    buckets: dict[str, dict[int, list[int]]] = {name: {} for name in names}
    for index, weights in enumerate(per_vertex):
        for name, units in weights:
            if units:
                buckets[name].setdefault(units, []).append(index)
    for name, values in buckets.items():
        for units, indices in values.items():
            groups[name].add(indices, units / 255.0, "REPLACE")
    modifier = next((item for item in target.modifiers if item.type == "ARMATURE"), None)
    if modifier is None:
        modifier = target.modifiers.new("Skeleton_Deform", "ARMATURE")
    modifier.object = source.find_armature()
    modifier.use_vertex_groups = True
    target.parent = modifier.object
    target.data.update()
    return {"mesh": target.name, "vertices": len(target.data.vertices),
            "groups": len(groups), "maxInfluences": MAX_INFLUENCES,
            "maxProjectionDistance_m": round(max_distance, 6),
            "weightMethod": "nearest Smart Rig triangle, barycentric 4-influence transfer, exact-sum 8-bit weights",
            "localization": ("rest-segment attenuation for forelimb and neck/head weights on Body"
                             if local_segments else "none; source surface weights preserved")}


def _rename_source_bones(rig: bpy.types.Object, body: bpy.types.Object,
                         height: float, family: str,
                         family_point: Callable[[Vector, str], Vector] | None) -> dict:
    old_to_new = dict(BONE_ALIASES)
    root = "Root"
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    for bone in list(edit):
        original = bone.name
        bone.name = old_to_new.get(original, "Smart_" + original)
        bone.head = _source_to_target(bone.head.copy(), height, family, family_point)
        bone.tail = _source_to_target(bone.tail.copy(), height, family, family_point)
        bone.use_connect = False
    new_root = edit.new(root)
    origin = _source_to_target(Vector((0, 0, 0)), height, family, family_point)
    new_root.head = origin
    new_root.tail = origin + Vector((0.0, 0.0, 0.12))
    new_root.use_deform = False
    new_root.use_connect = False
    top = edit.get(old_to_new["Bone_000"])
    top.parent = new_root
    top.use_connect = False
    # The website rig attached both front-leg chains to its lowest neck bone.
    # That made any graze pose move the forelegs through hierarchy inheritance.
    # Keep the same rest-space joints but attach the shoulders to the chest.
    chest = edit.get("Chest")
    for shoulder_name in ("Front_L_Upper", "Front_R_Upper"):
        shoulder = edit.get(shoulder_name)
        if shoulder is not None:
            shoulder.parent = chest
            shoulder.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    # The source skin's group names must exactly match the newly renamed bones.
    canonical_names = set(old_to_new.values())
    for group in body.vertex_groups:
        if group.name in canonical_names:
            continue  # Blender may have renamed matching vertex groups with bones.
        group.name = old_to_new.get(group.name, "Smart_" + group.name)
    rig.show_in_front = True
    rig.data.display_type = "STICK"
    return {"root": root, "aliases": old_to_new,
            "unmappedNamesPrefix": "Smart_"}


def prepare_candidate_rig(body: bpy.types.Object, family: str,
                          landmarks: dict | None = None,
                          related_meshes: Iterable[bpy.types.Object] = (),
                          rig_name: str = "Skeleton",
                          family_point: Callable[[Vector, str], Vector] | None = None,
                          smart_rig_path: str | Path | None = None) -> tuple[bpy.types.Object, dict]:
    """Import the matching Smart Rig, mirror the family warp, transfer weights.

    ``body`` is the final, identity-transform family mesh. ``family_point`` is
    the exact callable used by the production builder and is applied to source
    skin vertices and every source bone head/tail so projected weights stay in
    the same family space. ``landmarks`` is accepted for builder compatibility;
    Smart Rig landmarks are source-matched and therefore do not use bounds fits.
    """
    if body is None or body.type != "MESH":
        raise TypeError("prepare_candidate_rig expects the final Body mesh")
    if family not in {"light", "stock"}:
        raise ValueError(f"Unknown adult horse family {family!r}")
    identity = Matrix.Identity(4)
    if any(abs(body.matrix_world[row][column] - identity[row][column]) > 1e-5
           for row in range(4) for column in range(4)):
        raise RuntimeError("Candidate body transform must be baked before rig creation")
    if bpy.data.objects.get(rig_name) is not None:
        raise RuntimeError(f"Object {rig_name} already exists; refusing to replace it")
    source_path = Path(smart_rig_path) if smart_rig_path else SMART_RIG_SOURCE
    if not source_path.is_file():
        raise FileNotFoundError(f"Meshy Smart Rig source not found: {source_path}")
    source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(source_path))
    imported = set(bpy.data.objects) - before
    rig = next((obj for obj in imported if obj.type == "ARMATURE"), None)
    source = next((obj for obj in imported if obj.type == "MESH" and
                   len(obj.vertex_groups) > 0), None)
    if rig is None or source is None:
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)
        raise RuntimeError("Smart Rig GLB must contain its weighted horse mesh and armature")
    # Remove unrelated exporter nodes (including the website's decorative sphere).
    for obj in list(imported):
        if obj not in (rig, source):
            bpy.data.objects.remove(obj, do_unlink=True)
    source.name = "SmartRig_WeightSource"
    source.data.name = "SmartRig_WeightSourceData"
    body_coords = [vertex.co.copy() for vertex in body.data.vertices]
    height = max(v.z for v in body_coords) - min(v.z for v in body_coords)
    if height <= 0.5:
        raise RuntimeError(f"Invalid production horse height: {height}")
    for vertex in source.data.vertices:
        vertex.co = _source_to_target(vertex.co.copy(), height, family, family_point)
    source.data.update()
    mapping_report = _rename_source_bones(rig, source, height, family, family_point)
    rig.name = rig_name
    rig.data.name = rig_name + "Data"
    collection = body.users_collection[0] if body.users_collection else bpy.context.scene.collection
    if collection not in rig.users_collection:
        collection.objects.link(rig)
    rig.parent = None
    bpy.context.view_layer.update()
    body_weights = _write_transferred_weights(source, body)
    related_reports = []
    for mesh in related_meshes or ():
        if mesh is None or mesh.type != "MESH":
            continue
        lower = mesh.name.lower()
        if "eye" in lower:
            allowed = {"Head_Base", "Head", "Head_Upper", "Head_Nose", "Ear_L", "Ear_R"}
        elif "mane" in lower or "forelock" in lower:
            allowed = {"Neck_Base", "Neck", "Neck_Mid", "Neck_Upper", "Neck_Top",
                       "Head_Base", "Head", "Head_Upper"}
        elif "tail" in lower:
            allowed = {f"Tail_{index:02d}" for index in range(1, 8)}
        else:
            allowed = None
        fallback = ("Head" if "eye" in lower else
                    "Neck_Mid" if "mane" in lower or "forelock" in lower else
                    "Tail_01" if "tail" in lower else "Chest")
        related_reports.append(_write_transferred_weights(source, mesh, allowed, fallback))
    # Remove source mesh data now that weights have been transferred.
    source_mesh_data = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    if source_mesh_data.users == 0:
        bpy.data.meshes.remove(source_mesh_data)
    bone_names = {bone.name for bone in rig.data.bones}
    missing = sorted(REQUIRED_BONES - bone_names)
    if missing:
        raise RuntimeError(f"Smart Rig alias map missed required bones: {missing}")
    report = {"version": RIG_VERSION, "family": family,
              "source": str(source_path), "sourceSha256": source_hash,
              "sourceBoneCount": len(BONE_ALIASES), "boneCount": len(rig.data.bones),
              "boneNames": sorted(bone_names), "boneMapping": mapping_report,
              "weighting": [body_weights, *related_reports],
              "skinInfluenceLimit": MAX_INFLUENCES,
              "walkAuthored": False,
              "poseGate": "static and locomotion pose review required before clips"}
    return rig, report


# The second Meshy foal Smart Rig has 60 weighted joints and (unlike the first
# attempt) bilateral hind-leg skin groups.  Its short pastern/hoof chains are
# exported as separate root children, so this adapter reconnects them to their
# anatomical limb chain without moving any rest-space head/tail positions.
FOAL_BONE_ALIASES = {
    "Bone_000": "Pelvis_Root", "Bone_001": "Pelvis",
    "Bone_011": "Spine_Base", "Bone_010": "Chest",
    "Bone_038": "Neck_Base", "Bone_044": "Neck",
    "Bone_040": "Tail_01", "Bone_039": "Tail_02", "Bone_045": "Tail_03",
    "Bone_059": "Head_Base", "Bone_021": "Head", "Bone_020": "Head_Nose",
    # Source +X is anatomical left after the source-to-target quarter turn.
    "Bone_009": "Front_L_Upper", "Bone_008": "Front_L_Forearm",
    "Bone_007": "Front_L_Cannon", "Bone_025": "Front_L_Pastern",
    "Bone_024": "Front_L_Hoof", "Bone_033": "Front_L_Hoof_02",
    "Bone_032": "Front_L_Hoof_03",
    "Bone_006": "Front_R_Upper", "Bone_005": "Front_R_Forearm",
    "Bone_004": "Front_R_Cannon", "Bone_023": "Front_R_Pastern",
    "Bone_022": "Front_R_Hoof", "Bone_031": "Front_R_Hoof_02",
    "Bone_030": "Front_R_Hoof_03",
    "Bone_019": "Hind_L_Upper", "Bone_018": "Hind_L_Gaskin",
    "Bone_049": "Hind_L_Hock", "Bone_048": "Hind_L_Cannon",
    "Bone_029": "Hind_L_Pastern", "Bone_028": "Hind_L_Hoof",
    "Bone_037": "Hind_L_Hoof_02", "Bone_036": "Hind_L_Hoof_03",
    "Bone_016": "Hind_R_Upper", "Bone_015": "Hind_R_Gaskin",
    "Bone_047": "Hind_R_Hock", "Bone_046": "Hind_R_Cannon",
    "Bone_027": "Hind_R_Pastern", "Bone_026": "Hind_R_Hoof",
    "Bone_035": "Hind_R_Hoof_02", "Bone_034": "Hind_R_Hoof_03",
}
FOAL_REQUIRED_BONES = {
    "Root", "Pelvis", "Chest", "Neck_Base", "Neck", "Head",
    "Front_L_Upper", "Front_L_Forearm", "Front_L_Cannon", "Front_L_Pastern", "Front_L_Hoof",
    "Front_R_Upper", "Front_R_Forearm", "Front_R_Cannon", "Front_R_Pastern", "Front_R_Hoof",
    "Hind_L_Upper", "Hind_L_Gaskin", "Hind_L_Hock", "Hind_L_Cannon",
    "Hind_L_Pastern", "Hind_L_Hoof", "Hind_R_Upper", "Hind_R_Gaskin",
    "Hind_R_Hock", "Hind_R_Cannon", "Hind_R_Pastern", "Hind_R_Hoof",
}


def _foal_source_to_target(raw: Vector, height: float, family: str,
                           family_point: Callable[[Vector, str], Vector]) -> Vector:
    mapped = Vector((-raw.y, raw.x, raw.z)) * (height / 1.7)
    return family_point(mapped, family)


def _canonicalize_foal_rig(rig: bpy.types.Object, source: bpy.types.Object,
                           body: bpy.types.Object, height: float, family: str,
                           family_point: Callable[[Vector, str], Vector]) -> dict:
    aliases = dict(FOAL_BONE_ALIASES)
    reparent = {
        # Shoulder/forelimb roots belong to the thorax, never the neck chain.
        "Front_L_Upper": "Chest", "Front_R_Upper": "Chest",
        "Hind_L_Upper": "Pelvis", "Hind_R_Upper": "Pelvis",
        "Front_L_Pastern": "Front_L_Cannon", "Front_L_Hoof": "Front_L_Pastern",
        "Front_L_Hoof_02": "Front_L_Hoof", "Front_L_Hoof_03": "Front_L_Hoof_02",
        "Front_R_Pastern": "Front_R_Cannon", "Front_R_Hoof": "Front_R_Pastern",
        "Front_R_Hoof_02": "Front_R_Hoof", "Front_R_Hoof_03": "Front_R_Hoof_02",
        "Hind_L_Hock": "Hind_L_Gaskin", "Hind_L_Cannon": "Hind_L_Hock",
        "Hind_L_Pastern": "Hind_L_Cannon", "Hind_L_Hoof": "Hind_L_Pastern",
        "Hind_L_Hoof_02": "Hind_L_Hoof", "Hind_L_Hoof_03": "Hind_L_Hoof_02",
        "Hind_R_Hock": "Hind_R_Gaskin", "Hind_R_Cannon": "Hind_R_Hock",
        "Hind_R_Pastern": "Hind_R_Cannon", "Hind_R_Hoof": "Hind_R_Pastern",
        "Hind_R_Hoof_02": "Hind_R_Hoof", "Hind_R_Hoof_03": "Hind_R_Hoof_02",
    }
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    by_old = {bone.name: bone for bone in edit}
    original_names = list(by_old)
    for original in original_names:
        bone = by_old[original]
        bone.name = aliases.get(original, "FoalSmart_" + original)
        bone.head = _foal_source_to_target(bone.head.copy(), height, family, family_point)
        bone.tail = _foal_source_to_target(bone.tail.copy(), height, family, family_point)
        bone.use_connect = False
    root = edit.new("Root")
    root.head = Vector((0.0, 0.0, 0.0))
    root.tail = Vector((0.0, 0.0, 0.10))
    root.use_deform = False
    root.use_connect = False
    edit.get("Pelvis_Root").parent = root
    for child_name, parent_name in reparent.items():
        child, parent = edit.get(child_name), edit.get(parent_name)
        if child is None or parent is None:
            bpy.ops.object.mode_set(mode="OBJECT")
            raise RuntimeError(f"Foal rig is missing reparent endpoint {child_name} -> {parent_name}")
        child.parent = parent
        child.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    # Blender can rename vertex groups when a bone is renamed.  Normalize any
    # original labels left behind so all weights still address the mapped bone.
    alias_by_old = {old: new for old, new in aliases.items()}
    for group in source.vertex_groups:
        if group.name in set(aliases.values()) or group.name == "Root":
            continue
        group.name = alias_by_old.get(group.name, "FoalSmart_" + group.name)
    for vertex in source.data.vertices:
        vertex.co = _foal_source_to_target(vertex.co.copy(), height, family, family_point)
    source.data.update()
    rig.show_in_front = True
    rig.data.display_type = "STICK"
    return {"sourceBoneCount": len(original_names), "boneAliases": aliases,
            "unmappedNamePrefix": "FoalSmart_", "reparented": reparent,
            "restPosePolicy": "source head/tail coordinates transformed; hierarchy only reparented"}


def _audit_foal_limb_weights(mesh: bpy.types.Object) -> dict:
    """Summarize four-leg assignment integrity without hiding seam spill."""
    limb_names = {
        side: {f"{part}_{side}_{segment}" for part, segments in
               (("Front", ("Upper", "Forearm", "Cannon", "Pastern", "Hoof", "Hoof_02", "Hoof_03")),
                ("Hind", ("Upper", "Gaskin", "Hock", "Cannon", "Pastern", "Hoof", "Hoof_02", "Hoof_03")))
               for segment in segments}
        for side in ("L", "R")
    }
    names = {group.index: group.name for group in mesh.vertex_groups}
    opposite_side = 0
    max_influences = 0
    max_weight_sum_error = 0.0
    for vertex in mesh.data.vertices:
        weights = [(names[item.group], item.weight) for item in vertex.groups]
        max_influences = max(max_influences, len(weights))
        max_weight_sum_error = max(max_weight_sum_error, abs(sum(weight for _, weight in weights) - 1.0))
        left = sum(weight for name, weight in weights if name in limb_names["L"])
        right = sum(weight for name, weight in weights if name in limb_names["R"])
        # The final body uses +Y as the horse's anatomical left. Count only
        # clear lateral surfaces; a few weights can legitimately meet at the
        # medial seam between paired legs.
        if vertex.co.y > 0.045 and right > 0.08:
            opposite_side += 1
        elif vertex.co.y < -0.045 and left > 0.08:
            opposite_side += 1
    return {"maxInfluences": max_influences,
            "maxWeightSumError": round(max_weight_sum_error, 8),
            "clearOppositeSideVerticesOver8Pct": opposite_side}


def prepare_foal_rig(body: bpy.types.Object, family: str = "foal",
                     related_meshes: Iterable[bpy.types.Object] = (),
                     rig_name: str = "Skeleton",
                     family_point: Callable[[Vector, str], Vector] | None = None,
                     smart_rig_path: str | Path | None = None) -> tuple[bpy.types.Object, dict]:
    """Transfer the second foal Smart Rig onto a final, baked juvenile mesh.

    Source mesh surface weights are projected barycentrically; unlike the
    adult adapter this does not apply adult-specific Gaussian joint falloffs.
    The source has an explicit bilateral hind skeleton and should be validated
    in four isolated limb poses before any locomotion clips are authored.
    """
    if body is None or body.type != "MESH":
        raise TypeError("prepare_foal_rig expects the final Body mesh")
    if family != "foal":
        raise ValueError("prepare_foal_rig currently supports only family='foal'")
    if family_point is None:
        raise ValueError("prepare_foal_rig requires the builder's exact foal_point callback")
    identity = Matrix.Identity(4)
    if any(abs(body.matrix_world[row][column] - identity[row][column]) > 1e-5
           for row in range(4) for column in range(4)):
        raise RuntimeError("Foal body transform must be baked before rig creation")
    if bpy.data.objects.get(rig_name) is not None:
        raise RuntimeError(f"Object {rig_name} already exists; refusing to replace it")
    source_path = Path(smart_rig_path) if smart_rig_path else FOAL_SMART_RIG_SOURCE
    if not source_path.is_file():
        raise FileNotFoundError(f"Foal Smart Rig source not found: {source_path}")
    source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(source_path))
    imported = set(bpy.data.objects) - before
    rig = next((obj for obj in imported if obj.type == "ARMATURE"), None)
    source = next((obj for obj in imported if obj.type == "MESH" and
                   len(obj.vertex_groups) > 0), None)
    if rig is None or source is None:
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)
        raise RuntimeError("Foal Smart Rig GLB must contain a weighted mesh and armature")
    for obj in list(imported):
        if obj not in (rig, source):
            bpy.data.objects.remove(obj, do_unlink=True)
    source.name = "FoalSmartRig_WeightSource"
    source.data.name = "FoalSmartRig_WeightSourceData"
    height = max(v.co.z for v in body.data.vertices) - min(v.co.z for v in body.data.vertices)
    if height <= 0.25:
        raise RuntimeError(f"Invalid foal production height: {height}")
    mapping_report = _canonicalize_foal_rig(rig, source, body, height, family, family_point)
    rig.name = rig_name
    rig.data.name = rig_name + "Data"
    collection = body.users_collection[0] if body.users_collection else bpy.context.scene.collection
    if collection not in rig.users_collection:
        collection.objects.link(rig)
    rig.parent = None
    bpy.context.view_layer.update()
    body_report = _write_transferred_weights(source, body, localize_body_weights=False)
    accessory_reports = []
    for mesh in related_meshes or ():
        if mesh is None or mesh.type != "MESH":
            continue
        name = mesh.name.lower()
        allowed = ({"Head_Base", "Head", "Head_Nose", "FoalSmart_Bone_058"}
                   if "eye" in name else None)
        fallback = "Head" if "eye" in name else "Pelvis"
        accessory_reports.append(_write_transferred_weights(
            source, mesh, allowed=allowed, fallback_bone=fallback, localize_body_weights=False))
    source_mesh_data = source.data
    bpy.data.objects.remove(source, do_unlink=True)
    if source_mesh_data.users == 0:
        bpy.data.meshes.remove(source_mesh_data)
    bone_names = {bone.name for bone in rig.data.bones}
    missing = sorted(FOAL_REQUIRED_BONES - bone_names)
    if missing:
        raise RuntimeError(f"Foal Smart Rig alias map missed required bones: {missing}")
    report = {"version": "cloverbrook.foal-smart-rig-transfer.v1",
              "family": family, "source": str(source_path), "sourceSha256": source_hash,
              "sourceBoneCount": mapping_report["sourceBoneCount"],
              "boneCount": len(bone_names), "boneNames": sorted(bone_names),
              "boneMapping": mapping_report,
              "weighting": [body_report, *accessory_reports],
              "limbWeightIntegrity": _audit_foal_limb_weights(body),
              "skinInfluenceLimit": MAX_INFLUENCES,
              "limbPoseValidation": "required; no clips authored by this adapter"}
    return rig, report


def _pose_rotation(rig: bpy.types.Object, name: str, degrees: float,
                   axis: Vector = Vector((0.0, 1.0, 0.0))) -> Quaternion:
    bone = rig.data.bones.get(name)
    if bone is None:
        return Quaternion()
    arm_delta = Quaternion(axis, math.radians(degrees)).to_matrix()
    rest_basis = bone.matrix_local.to_3x3()
    return (rest_basis.inverted() @ arm_delta @ rest_basis).to_quaternion()


def _reset_pose(rig: bpy.types.Object) -> None:
    for bone in rig.pose.bones:
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = Quaternion()
        bone.location = Vector((0.0, 0.0, 0.0))


def set_pose(rig: bpy.types.Object, pose: str) -> dict:
    """Set a bounded proof pose; head_lower is not accepted as graze."""
    poses = {
        "neutral": {},
        "front_raised": {"Front_L_Upper": -12.0, "Front_L_Forearm": 24.0,
                         "Front_L_Cannon": 8.0, "Front_L_Hoof": -12.0},
        "hind_stifle_flex": {"Hind_L_Upper": 4.0, "Hind_L_Gaskin": -22.0,
                              "Hind_L_Cannon": 8.0, "Hind_L_Hoof": -5.0},
        # Diagnostic reach pose: the main drop comes from the cervical base;
        # counter-rotation at the head keeps the muzzle aimed toward the floor.
        # This is intentionally named head_lower until reviewed evidence accepts
        # the complete silhouette and hoof clearance as a production graze.
        "head_lower": {"Neck": 120.0, "Head_Base": -20.0, "Head": -20.0},
    }
    if pose not in poses:
        raise ValueError(f"Unknown adult-horse proof pose: {pose}")
    _reset_pose(rig)
    for name, degrees in poses[pose].items():
        bone = rig.pose.bones.get(name)
        if bone:
            bone.rotation_quaternion = _pose_rotation(rig, name, degrees)
    bpy.context.view_layer.update()
    return {"pose": pose, "rotationsDegrees": poses[pose]}


def _key_pose(rig: bpy.types.Object, frame: int, values: dict[str, float]) -> None:
    for name, degrees in values.items():
        bone = rig.pose.bones.get(name)
        if bone is None:
            continue
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = _pose_rotation(rig, name, degrees)
        bone.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=name)


def _create_action(rig: bpy.types.Object, name: str,
                   keyframes: list[tuple[int, dict[str, float]]],
                   *, fps: int, frame_end: int, cyclic: bool = True) -> bpy.types.Action:
    action = bpy.data.actions.new(name)
    rig.animation_data.action = action
    for frame, values in keyframes:
        _key_pose(rig, frame, values)
    action.frame_range = (1, frame_end)
    curves = list(action.fcurves) if hasattr(action, "fcurves") else []
    if not curves:
        # Blender 5.x stores curves in layered action channel bags.
        for layer in action.layers:
            for strip in layer.strips:
                for slot in action.slots:
                    bag = strip.channelbag(slot)
                    if bag is not None:
                        curves.extend(bag.fcurves)
    for curve in curves:
        for key in curve.keyframe_points:
            key.interpolation = "BEZIER"
            key.handle_left_type = "AUTO_CLAMPED"
            key.handle_right_type = "AUTO_CLAMPED"
        if cyclic and curve.keyframe_points:
            modifier = curve.modifiers.new("CYCLES")
    return action


def _add_nla_action(rig: bpy.types.Object, action: bpy.types.Action) -> None:
    track = rig.animation_data.nla_tracks.new()
    track.name = action.name
    strip = track.strips.new(action.name, int(action.frame_range[0]), action)
    strip.name = action.name


def _walk_keyframes() -> tuple[list[tuple[int, dict[str, float]]], int]:
    phase_by_leg = {
        ("Hind", "L"): 0.0,
        ("Front", "L"): 0.25,
        ("Hind", "R"): 0.5,
        ("Front", "R"): 0.75,
    }
    frames = []
    frame_end = 49  # two-second stride at 24 fps; frame 49 repeats frame 1
    stance_fraction = 0.62
    for frame in range(1, frame_end + 1, 3):
        cycle = (frame - 1) / (frame_end - 1)
        values: dict[str, float] = {
            "Spine_Mid": 0.25 * math.sin(2.0 * math.pi * cycle),
            "Spine_Upper": 0.16 * math.sin(2.0 * math.pi * cycle + 0.35),
            "Head": 0.2 * math.sin(2.0 * math.pi * cycle - 0.25),
            "Tail_03": 0.4 * math.sin(2.0 * math.pi * cycle + 1.0),
        }
        for (limb, side), phase_offset in phase_by_leg.items():
            phase = (cycle - phase_offset) % 1.0
            if phase < stance_fraction:
                # Touch down with the limb reaching forward, then let the
                # body pass over it during support before toe-off.
                progress = phase / stance_fraction
                eased = progress * progress * (3.0 - 2.0 * progress)
                upper = -12.0 + 22.0 * eased
                lift = 0.0
            else:
                # Flex the joints during the swing, then return the hoof
                # forward for the next contact. The joint signs are checked
                # against the source rig's rendered single-bone probes.
                progress = (phase - stance_fraction) / (1.0 - stance_fraction)
                upper = 10.0 - 22.0 * progress
                lift = math.sin(math.pi * progress)
            if limb == "Front":
                prefix = f"Front_{side}"
                values.update({
                    f"{prefix}_Upper": upper,
                    f"{prefix}_Forearm": 22.0 * lift,
                    f"{prefix}_Cannon": 4.0 * lift,
                    f"{prefix}_Pastern": -2.0 * lift,
                    f"{prefix}_Hoof": -4.0 * lift,
                })
            else:
                prefix = f"Hind_{side}"
                values.update({
                    f"{prefix}_Upper": upper,
                    f"{prefix}_Gaskin": 20.0 * lift,
                    f"{prefix}_Cannon": -5.0 * lift,
                    f"{prefix}_Pastern": -3.0 * lift,
                    f"{prefix}_Hoof": -4.0 * lift,
                })
        frames.append((frame, values))
    if frames[-1][0] != frame_end:
        frames.append((frame_end, frames[0][1]))
    return frames, frame_end


def build_walk_candidate(rig: bpy.types.Object) -> bpy.types.Action:
    """Author a review-only, in-place four-beat walk candidate.

    It stays separate from ``build_actions`` so unreviewed locomotion cannot
    enter a production export by default. Rendered contact/deformation review
    is still required before a candidate can be exported as ``Walk``.
    """
    bone_names = {bone.name for bone in rig.data.bones}
    if not REQUIRED_BONES.issubset(bone_names):
        raise RuntimeError("prepare_candidate_rig must run before building actions")
    rig.animation_data_create()
    previous_action = rig.animation_data.action
    frames, frame_end = _walk_keyframes()
    action = _create_action(rig, "Walk_Diagnostic", frames,
                            fps=24, frame_end=frame_end)
    action["production_candidate_only"] = True
    action["review_status"] = "pending planted-hoof and deformation review"
    animation = rig.animation_data
    if animation is not None:
        animation.action = previous_action
    return action


def build_actions(rig: bpy.types.Object, *, include_graze: bool = False,
                  include_walk: bool = False, approved_poses: bool = False,
                  approved_walk: bool = False) -> dict:
    """Create only the approved quiet idle by default.

    Head-lower/graze remains excluded until its muzzle reaches the ground
    without neck or shoulder artifacts. Walk remains excluded until gait timing,
    planted-hoof motion, and both-family deformations pass a separate review.
    """
    bone_names = {bone.name for bone in rig.data.bones}
    if not REQUIRED_BONES.issubset(bone_names):
        raise RuntimeError("prepare_candidate_rig must run before building actions")
    if include_graze and not approved_poses:
        raise RuntimeError("Graze is gated on reviewed ground-reaching pose evidence")
    if include_walk and not approved_walk:
        raise RuntimeError("Walk is gated on reviewed locomotion and planted-hoof evidence")
    # The current project has no accepted graze/walk pose data. Refuse to label
    # draft motion as production clips even if a caller accidentally opts in.
    if include_graze or include_walk:
        if include_graze:
            raise RuntimeError("No graze clip is approved for production yet")
    rig.animation_data_clear()
    rig.animation_data_create()
    scene = bpy.context.scene
    old_frame, old_fps = scene.frame_current, scene.render.fps
    scene.render.fps = 24
    _reset_pose(rig)
    idle = _create_action(rig, "Idle", [
        (1, {}),
        (25, {"Head": 0.55,
              "Tail_03": -1.5, "Ear_L": 1.5, "Ear_R": -1.1}),
        (49, {"Head": 0.0,
              "Tail_03": 1.4, "Ear_L": -0.9, "Ear_R": 1.2}),
        (73, {"Head": -0.35,
              "Tail_03": -0.8, "Ear_L": 0.8, "Ear_R": -0.7}),
        (97, {})], fps=24, frame_end=97)
    animation = rig.animation_data_create()
    animation.action = idle
    actions = [idle.name]
    durations = {"Idle": 4.0}
    if include_walk:
        frames, frame_end = _walk_keyframes()
        walk = _create_action(rig, "Walk", frames, fps=24, frame_end=frame_end)
        _add_nla_action(rig, walk)
        actions.append(walk.name)
        durations["Walk"] = 2.0
        animation.action = idle
    scene.frame_set(old_frame)
    scene.render.fps = old_fps
    return {"actions": actions, "fps": 24, "durationsSeconds": durations,
            "walkAuthored": include_walk, "grazeAuthored": False,
            "walkGate": "awaiting locomotion and planted-hoof review",
            "grazeGate": "awaiting ground-reaching pose and deformation review"}
