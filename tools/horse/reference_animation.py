"""Authored, in-place FK gait loops for the CC0 horse reference.

The foot paths are solved in the model's Blender scene basis before baking to
ordinary deform-bone channels. No constraints or runtime IK are required.
"""
from __future__ import annotations

import math
import json
from typing import Any
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


SOURCE_MOTION_PATH = Path(__file__).with_name("reference_motion_samples.json")
SOURCE_MOTION = json.loads(SOURCE_MOTION_PATH.read_text())


PROFILES = {
    "adult": {"walk_duration": 50.0 / 30.0, "trot_duration": 25.0 / 30.0, "walk_speed": 0.75,
              "trot_speed": 1.375, "lower_walk": 0.052, "lower_trot": 0.060,
              "walk_lift": 0.052, "trot_lift": 0.085, "walk_amplitude": 0.65, "trot_amplitude": 0.55},
    "foal": {"walk_duration": 36.0 / 30.0, "trot_duration": 18.0 / 30.0, "walk_speed": 0.60,
             "trot_speed": 1.10, "lower_walk": 0.036, "lower_trot": 0.040,
             "walk_lift": 0.043, "trot_lift": 0.070, "walk_amplitude": 0.62, "trot_amplitude": 0.45},
}

WALK_TOUCHDOWN = {"Hind_L": 0.0, "Front_L": 0.25, "Hind_R": 0.5, "Front_R": 0.75}
TROT_TOUCHDOWN = {"Front_L": 0.0, "Hind_R": 0.0, "Front_R": 0.5, "Hind_L": 0.5}
STANCE_DUTY = {"walk": 0.62, "trot": 0.50}


def _world_point(rig: bpy.types.Object, p: Vector) -> Vector:
    return rig.matrix_world @ p


def prepare_gait_rig(rig: bpy.types.Object) -> dict[str, Any]:
    """Fit anatomical shoulder/elbow/carpus and hip/stifle/hock chains.

    The original FK upper foreleg spans the scapula to carpus. Bending that
    single segment makes the entire forearm hinge at the chest. Split it into
    scapula, humerus and radius; these landmarks lie inside the existing cage.
    """
    inv = rig.matrix_world.inverted()
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edits = rig.data.edit_bones
    report = {}
    try:
        for side, lateral in (("L", 1.0), ("R", -1.0)):
            front_upper = edits[f"Front_{side}_Upper"]
            front_cannon = edits[f"Front_{side}_Cannon"]
            scapula_head = front_upper.head.copy()
            parent = front_upper.parent
            shoulder_world = Vector((0.50, 0.210 * lateral, 1.170))
            elbow_world = Vector((0.30, 0.222 * lateral, 0.940))
            scapula = edits.new(f"Front_{side}_Scapula")
            scapula.head = scapula_head
            scapula.tail = inv @ shoulder_world
            scapula.parent = parent
            front_upper.head = scapula.tail
            front_upper.tail = inv @ elbow_world
            front_upper.parent = scapula
            front_upper.use_connect = True
            forearm = edits.new(f"Front_{side}_Forearm")
            forearm.head = front_upper.tail
            forearm.tail = front_cannon.head.copy()
            forearm.parent = front_upper
            forearm.use_connect = True
            front_cannon.parent = forearm
            front_cannon.use_connect = True
            report[f"Front_{side}"] = {
                "shoulder_m": list(shoulder_world), "elbow_m": list(elbow_world),
                "carpus_m": list(rig.matrix_world @ front_cannon.head),
            }
            upper = edits[f"Hind_{side}_Upper"]
            cannon = edits[f"Hind_{side}_Cannon"]
            old_ankle = cannon.tail.copy()
            # These are the visible stifle/hock corridors in the authored mesh.
            # The edit points are expressed in world-like model metres, then
            # brought back into the armature's raw source basis.
            stifle_world = Vector((-0.215, 0.145 * lateral, 1.035))
            hock_world = Vector((-0.585, 0.165 * lateral, 0.655))
            stifle = inv @ stifle_world
            hock = inv @ hock_world
            upper.tail = stifle
            shin = edits.get(f"Hind_{side}_Shin") or edits.new(f"Hind_{side}_Shin")
            shin.head = stifle
            shin.tail = hock
            shin.parent = upper
            shin.use_connect = True
            cannon.head = hock
            cannon.tail = old_ankle
            cannon.parent = shin
            cannon.use_connect = True
            report[f"Hind_{side}"] = {
                "stifle_m": [round(v, 5) for v in stifle_world],
                "hock_m": [round(v, 5) for v in hock_world],
            }
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()
    return report


def reset_ground_root(rig: bpy.types.Object) -> None:
    """Foal regional mapping must not lift the shared skeleton root."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    root = rig.data.edit_bones["Root"]
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 0.0, 0.18)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.update()


def _distance_to_segment(point: Vector, a: Vector, b: Vector) -> float:
    ab = b - a
    denom = ab.length_squared
    t = max(0.0, min(1.0, (point - a).dot(ab) / denom)) if denom > 1e-12 else 0.0
    return (point - (a + t * ab)).length


def repair_body_weights(body: bpy.types.Object, rig: bpy.types.Object, profile: str) -> dict[str, Any]:
    """Skin the unchanged limb cages along their actual anatomical segments.

    Smooth arc-length transitions blend only adjacent segments. The proximal
    influence fades into the existing chest/pelvis weights, protecting the
    barrel and sternum; tail groups never deform Body.
    """
    group_by_index = {g.index: g.name for g in body.vertex_groups}
    groups = {g.name: g for g in body.vertex_groups}
    for bone in rig.data.bones:
        if bone.name not in groups:
            groups[bone.name] = body.vertex_groups.new(name=bone.name)
    scale = 0.74 if profile == "foal" else 1.0
    chains = {}
    for leg in ("Front_L", "Front_R", "Hind_L", "Hind_R"):
        parts = ("Scapula", "Upper", "Forearm", "Cannon", "Pastern", "Hoof") if leg.startswith("Front") else ("Upper", "Shin", "Cannon", "Pastern", "Hoof")
        names = [f"{leg}_{part}" for part in parts]
        segments = [(_world_point(rig, rig.data.bones[name].head_local),
                     _world_point(rig, rig.data.bones[name].tail_local)) for name in names]
        cumulative = [0.0]
        for start, end in segments:
            cumulative.append(cumulative[-1] + (end-start).length)
        chains[leg] = (names, segments, cumulative)
    counts = {leg: 0 for leg in chains}
    removed_tail = 0
    for vertex in body.data.vertices:
        point = body.matrix_world @ vertex.co
        old = {group_by_index[item.group]: item.weight for item in vertex.groups
               if item.group in group_by_index and item.weight > 1e-6}
        removed_tail += int(any(name.startswith("Tail_") for name in old))
        weights = {name: value for name, value in old.items() if not name.startswith("Tail_")}
        candidates = []
        for leg, (names, segments, cumulative) in chains.items():
            if (point.y >= 0.0) != leg.endswith("L"):
                continue
            for index, (start, end) in enumerate(segments):
                delta = end-start
                t = max(0.0, min(1.0, (point-start).dot(delta)/delta.length_squared))
                dist = (point-start-t*delta).length
                candidates.append((dist, leg, index, t))
        distance, leg, index, t = min(candidates)
        names, segments, cumulative = chains[leg]
        lower_joint = segments[2 if leg.startswith("Front") else 1][0].z
        top = segments[0][0].z
        # Narrow below the elbow/stifle, widening only inside the existing
        # shoulder/thigh mass. Never recruit the centerline or opposite limb.
        radius = (0.18 + 0.08*_ease((point.z-lower_joint)/(top-lower_joint))) * scale
        radial = 1.0-_ease((distance-radius*0.70)/(radius*0.30))
        side_gate = _ease((abs(point.y)-0.035*scale)/(0.075*scale))
        attachment = 1.0-_ease((point.z-(lower_joint-0.035*scale))/max(0.05,top-lower_joint))
        original_limb = sum(value for name, value in weights.items() if name.startswith(leg+"_"))
        influence = max(original_limb, radial*side_gate*attachment)
        if influence > 1e-4:
            arc = cumulative[index]+t*(cumulative[index+1]-cumulative[index])
            limb_weights = {names[index]: 1.0}
            joint_width = 0.070*scale
            for joint in range(1, len(names)):
                if abs(arc-cumulative[joint]) < joint_width:
                    u = _ease((arc-cumulative[joint]+joint_width)/(2.0*joint_width))
                    limb_weights = {names[joint-1]: 1.0-u, names[joint]: u}
                    break
            nonleg = {name: value for name, value in weights.items()
                      if not name.startswith(("Front_", "Hind_"))}
            total = sum(nonleg.values())
            if total <= 1e-8:
                nonleg = {"Chest" if leg.startswith("Front") else "Pelvis": 1.0}
                total = 1.0
            influence = min(1.0, influence)
            weights = {name: value/total*(1.0-influence) for name, value in nonleg.items()}
            weights.update({name: value*influence for name, value in limb_weights.items()})
            counts[leg] += 1
        total = sum(weights.values())
        if total <= 1e-8:
            weights = {"Pelvis": 1.0}
            total = 1.0
        weights = {name: value/total for name, value in weights.items() if value > 1e-6}
        for name in old:
            if name not in weights:
                groups[name].remove([vertex.index])
        for name, value in weights.items():
            groups[name].add([vertex.index], value, "REPLACE")
    return {"tailWeightVerticesCleared": removed_tail, "anatomicalLimbVerticesReweighted": counts,
            "adjacentJointBlendHalfWidth_m": 0.070*scale}


def _bone_ends(rig: bpy.types.Object, name: str) -> tuple[Vector, Vector]:
    pb = rig.pose.bones[name]
    return rig.matrix_world @ pb.head, rig.matrix_world @ pb.tail


def _reset_pose(rig: bpy.types.Object) -> None:
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
        pb.rotation_mode = "QUATERNION"
    bpy.context.view_layer.update()


def _set_bone_from_segment(rig: bpy.types.Object, name: str, head: Vector, tail: Vector) -> None:
    bone = rig.data.bones[name]
    rest = bone.matrix_local.to_3x3()
    rest_y = (rest @ Vector((0.0, 1.0, 0.0))).normalized()
    world_direction = (tail - head).normalized()
    inv3 = rig.matrix_world.inverted().to_3x3()
    local_direction = (inv3 @ world_direction).normalized()
    delta = rest_y.rotation_difference(local_direction)
    orient = delta.to_matrix() @ rest
    head_local = rig.matrix_world.inverted() @ head
    desired = orient.to_4x4()
    desired.translation = head_local
    rig.pose.bones[name].matrix = desired
    bpy.context.view_layer.update()


def _set_bone_translation(rig: bpy.types.Object, name: str, world_head: Vector) -> None:
    bone = rig.data.bones[name]
    pb = rig.pose.bones[name]
    orient = bone.matrix_local.to_3x3()
    matrix = orient.to_4x4()
    matrix.translation = rig.matrix_world.inverted() @ world_head
    pb.matrix = matrix
    bpy.context.view_layer.update()


def _leg_parts(leg: str) -> tuple[str, ...]:
    return ("Scapula", "Upper", "Forearm", "Cannon") if leg.startswith("Front") else ("Upper", "Shin", "Cannon")


def _source_segment_direction(bones: dict[str, Any], leg: str, part: str) -> Vector:
    side = leg[-1]
    prefix = ({"Scapula": "shoulder", "Upper": "upper_arm", "Forearm": "forearm", "Cannon": "forefoot", "Pastern": "f_toe", "Hoof": "f_hoof"}
              if leg.startswith("Front") else
              {"Upper": "thigh", "Shin": "lower_leg", "Cannon": "hind_foot", "Pastern": "r_toe", "Hoof": "r_hoof"})[part]
    first = f"DEF-{prefix}.{side}"
    last = first + ".001" if part in {"Upper", "Forearm", "Shin", "Cannon"} else first
    return (_source_point_to_model(bones[last]["tail"])-_source_point_to_model(bones[first]["head"])).normalized()


def _source_segment_delta(leg: str, part: str, sample: dict[str, Any]) -> Quaternion:
    # Bind landmarks supply a real neutral shaft direction. The source's saved
    # constraint-evaluated 'rest' already contains a bent IK pose, so it is not
    # an appropriate calibration for distal knee/hoof flexion.
    neutral = _source_segment_direction(SOURCE_MOTION["bind"], leg, part)
    posed = _source_segment_direction(sample["bones"], leg, part)
    return neutral.rotation_difference(posed)


def _fabrik(start: Vector, target: Vector, lengths: list[float], initial: list[Vector],
            label: str) -> tuple[list[Vector], float]:
    if (target-start).length >= sum(lengths)-1e-5:
        raise RuntimeError(f"{label}: unreachable anatomical chain target")
    points = [p.copy() for p in initial]
    for _ in range(160):
        points[-1] = target.copy()
        for index in range(len(points)-2, -1, -1):
            direction = points[index]-points[index+1]
            points[index] = points[index+1]+direction.normalized()*lengths[index]
        points[0] = start.copy()
        for index in range(1, len(points)):
            direction = points[index]-points[index-1]
            points[index] = points[index-1]+direction.normalized()*lengths[index-1]
        if (points[-1]-target).length < 1e-5:
            break
    error = (points[-1]-target).length
    if error > 0.001:
        raise RuntimeError(f"{label}: anatomical-chain solve error {error:.6f}m")
    return points, error


def _solve_leg(rig: bpy.types.Object, leg: str, target_sole: Vector,
               rest_hoof_offset: Vector, rest_pastern_vector: Vector,
               source_sample: dict[str, Any] | None = None) -> tuple[float, float]:
    target_head = target_sole+rest_hoof_offset
    target_fetlock = target_head-rest_pastern_vector
    parts = _leg_parts(leg)
    names = [f"{leg}_{part}" for part in parts]
    start = _bone_ends(rig, names[0])[0]
    lengths = [rig.data.bones[name].length for name in names]
    points = [start.copy()]
    for part, name in zip(parts, names):
        bone = rig.data.bones[name]
        direction = rig.matrix_world.to_3x3() @ (bone.tail_local-bone.head_local)
        if source_sample is not None:
            direction = _source_segment_delta(leg, part, source_sample) @ direction
        points.append(points[-1]+direction)
    points, solver_error = _fabrik(start, target_fetlock, lengths, points, leg)
    for index, name in enumerate(names):
        _set_bone_from_segment(rig, name, points[index], points[index+1])
    _set_bone_from_segment(rig, f"{leg}_Pastern", points[-1], target_head)
    _set_bone_translation(rig, f"{leg}_Hoof", target_head)
    endpoint_error = (_bone_ends(rig, f"{leg}_Hoof")[0]-target_head).length
    names.append(f"{leg}_Pastern")
    segment_error = max(abs((rig.pose.bones[name].tail-rig.pose.bones[name].head).length-rig.data.bones[name].length)
                        for name in names)
    return max(solver_error, endpoint_error), segment_error


def _set_pelvis_drop(rig: bpy.types.Object, amount: float) -> None:
    pb = rig.pose.bones["Pelvis"]
    matrix = pb.matrix.copy()
    local_delta = rig.matrix_world.inverted().to_3x3() @ Vector((0.0, 0.0, -amount))
    matrix.translation += local_delta
    pb.matrix = matrix
    bpy.context.view_layer.update()


def _ease(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)



def _pose_idle(rig: bpy.types.Object, t: float, profile: str,
               rest_soles: dict[str, Vector], hoof_offsets: dict[str, Vector],
               pastern_vectors: dict[str, Vector]) -> dict[str, Any]:
    _reset_pose(rig)
    pulse = math.sin(2.0 * math.pi * t / 3.0)
    idle_drop = (0.008 if profile == "adult" else 0.006) + (0.002 if profile == "adult" else 0.0015) * (0.5 - 0.5 * math.cos(2.0 * math.pi * t / 3.0))
    _set_pelvis_drop(rig, idle_drop)
    # Mild ribcage breathing and attentive head/ear/tail motion; hooves are
    # re-solved to the ground after the body pose so they remain planted.
    chest = rig.pose.bones["Chest"]
    chest.rotation_mode = "QUATERNION"
    chest.rotation_quaternion = Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(0.30) * pulse)
    head = rig.pose.bones["Head"]
    head.rotation_mode = "QUATERNION"
    head.rotation_quaternion = Quaternion(Vector((0.0, 1.0, 0.0)), math.radians(1.1) * pulse)
    for name, sign in (("Ear_L", 1.0), ("Ear_R", -1.0)):
        pb = rig.pose.bones[name]
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion(Vector((1.0, 0.0, 0.0)), math.radians(2.0 * sign) * math.sin(2.0 * math.pi * t / 1.5))
    for name, phase in (("Tail_01", 0.0), ("Tail_02", 0.15), ("Tail_03", 0.30)):
        pb = rig.pose.bones[name]
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion(Vector((0.0, 0.0, 1.0)), math.radians(2.0) * math.sin(2.0 * math.pi * t / 3.0 + phase))
    bpy.context.view_layer.update()
    errors = []
    for leg in ("Front_L", "Front_R", "Hind_L", "Hind_R"):
        sole = rest_soles[leg]
        target = Vector((sole.x, sole.y, sole.z))
        endpoint, seg_error = _solve_leg(rig, leg, target, hoof_offsets[leg], pastern_vectors[leg])
        errors.append((leg, endpoint, seg_error))
    return {"solverErrors": errors, "targetSoleZ_m": [0.0], "pelvisCompression_m": idle_drop}


def _source_bone(sample: dict[str, Any], name: str) -> dict[str, Any]:
    return sample.get("bones", sample)[name]


def _source_point_to_model(point: list[float]) -> Vector:
    # Source Z-up/-Y-forward/+X-left → target Blender +X-forward/+Y-left/Z-up.
    return Vector((-point[1], point[0], point[2]))


def _source_quaternion(values: list[float]) -> Quaternion:
    return Quaternion((values[0], values[1], values[2], values[3])).normalized()


def _source_clip_sample(clip: str, phase: float) -> dict[str, Any]:
    # The extraction stores a duplicated terminal seam sample. Treat the
    # remaining rows as unique cycle samples, wrapping the final interval to 0.
    rows = SOURCE_MOTION["clips"][clip]["samples"][:-1]
    wrapped = phase % 1.0
    position = wrapped * len(rows)
    first = int(math.floor(position))
    second = (first + 1) % len(rows)
    alpha = position - first
    a, b = rows[first], rows[second]
    bones = {}
    for name in a["bones"]:
        pa, pb = a["bones"][name], b["bones"][name]
        bones[name] = {
            "head": [pa["head"][i] * (1.0-alpha) + pb["head"][i] * alpha for i in range(3)],
            "tail": [pa["tail"][i] * (1.0-alpha) + pb["tail"][i] * alpha for i in range(3)],
            "quatWXYZ": list(_source_quaternion(pa["quatWXYZ"]).slerp(_source_quaternion(pb["quatWXYZ"]), alpha)),
        }
    surfaces = {}
    for leg in a["hoofSurfaces"]:
        surfaces[leg] = {"minimumZ": a["hoofSurfaces"][leg]["minimumZ"]*(1.0-alpha)+b["hoofSurfaces"][leg]["minimumZ"]*alpha}
    return {"phase": wrapped, "bones": bones, "hoofSurfaces": surfaces}


def _source_contact_info(clip: str) -> dict[str, dict[str, float]]:
    source_name = "horse." + clip
    # Drop the duplicated terminal frame so contact inference has 80 actual
    # phase intervals rather than biasing the seam as an extra sample.
    rows = SOURCE_MOTION["clips"][clip]["samples"][:-1]
    info = {}
    source_bones = {"Front_L":"DEF-f_hoof.L", "Front_R":"DEF-f_hoof.R",
                    "Hind_L":"DEF-r_hoof.L", "Hind_R":"DEF-r_hoof.R"}
    threshold = 0.008
    for leg, source_bone in source_bones.items():
        # The hoof head remains fixed during support. The toe tip and mesh
        # lowest vertex can dip below the source floor during authored roll;
        # using either as contact timing wrongly counts early swing as stance.
        zs = [row["bones"][source_bone]["head"][2] for row in rows]
        cutoff = min(zs) + threshold
        contact = [z <= cutoff for z in zs]
        n = len(contact)
        starts = [i for i in range(n) if contact[i] and not contact[(i-1) % n]]
        if not starts:
            raise RuntimeError(f"Source {clip} trajectory has no contact interval for {leg}")
        # Choose the longest contiguous interval, wrapping across the seam.
        intervals = []
        for start in starts:
            length = 0
            while length < n and contact[(start + length) % n]:
                length += 1
            intervals.append((length, start))
        length, start = max(intervals)
        stance_samples = [rows[(start+i) % n]["bones"][source_bone]["tail"] for i in range(length)]
        baseline = sorted(rows[(start+i) % n]["hoofSurfaces"][leg]["minimumZ"] for i in range(length))[length//2]
        baseline_point = [sorted(p[axis] for p in stance_samples)[len(stance_samples)//2] for axis in range(3)]
        start_point = stance_samples[0]
        end_point = stance_samples[-1]
        model_start_x = _source_point_to_model(start_point).x
        model_end_x = _source_point_to_model(end_point).x
        source_duration = SOURCE_MOTION["clips"][clip]["durationSeconds"]
        source_stance_speed = -(model_end_x-model_start_x) / max(source_duration * (length/n), 1e-6)
        info[leg] = {"touchdownPhase": start/n, "stanceDuty": length/n,
                     "stanceBaselineZ": baseline, "stanceBaselinePoint": baseline_point,
                     "stanceStartPoint": start_point, "stanceEndPoint": end_point,
                     "swingCutoffZ": cutoff, "sourceStanceSpeedMps": source_stance_speed}
    return info


def _target_rest_world(rig: bpy.types.Object, name: str) -> tuple[Vector, Quaternion]:
    bone = rig.data.bones[name]
    world = rig.matrix_world @ bone.matrix_local
    return rig.matrix_world @ bone.head_local, world.to_quaternion().normalized()


def _set_world_bone_transform(rig: bpy.types.Object, name: str, head: Vector, rotation: Quaternion) -> None:
    matrix = Matrix.LocRotScale(head, rotation.normalized(), Vector((1.0, 1.0, 1.0)))
    rig.pose.bones[name].matrix = rig.matrix_world.inverted() @ matrix
    bpy.context.view_layer.update()


def _source_delta_rotation(clip: str, name: str, sample: dict[str, Any]) -> Quaternion:
    # The source rest pose and the first keyed gait pose are not the same. Use
    # the first gait sample as a retarget calibration so the new model starts
    # from its own neutral stance instead of inheriting a static source offset.
    source_rest = _source_bone(SOURCE_MOTION["clips"][clip]["samples"][0], name)
    source_pose = _source_bone(sample, name)
    delta = _source_quaternion(source_pose["quatWXYZ"]) @ _source_quaternion(source_rest["quatWXYZ"]).inverted()
    basis = Quaternion((0.0, 0.0, 1.0), math.pi * 0.5)
    return (basis @ delta @ basis.inverted()).normalized()


def _apply_source_body_pose(rig: bpy.types.Object, clip: str, sample: dict[str, Any], profile: str, compression: float) -> None:
    body_scale = 0.88 if profile == "adult" else 0.60
    for target_name, source_name in (("Pelvis", "DEF-spine.001"), ("Chest", "DEF-spine.006")):
        target_head, target_rest_q = _target_rest_world(rig, target_name)
        source_rest = _source_bone(SOURCE_MOTION["clips"][clip]["samples"][0], source_name)
        source_pose = _source_bone(sample, source_name)
        delta_pos = _source_point_to_model(source_pose["head"]) - _source_point_to_model(source_rest["head"])
        target_q = _source_delta_rotation(clip, source_name, sample) @ target_rest_q
        target_head = target_head + delta_pos * body_scale
        target_head.z -= compression
        _set_world_bone_transform(rig, target_name, target_head, target_q)

    # Retarget the complete four-segment source neck as a single smooth axis.
    # The head then remains attached to the poll at the top of that axis.
    first, last = "DEF-neck.001", "DEF-neck.004"
    neck_target_head, neck_rest_q = _target_rest_world(rig, "Neck")
    first_rest = _source_bone(SOURCE_MOTION["clips"][clip]["samples"][0], first)
    first_pose = _source_bone(sample, first)
    rest_vector = (_source_point_to_model(_source_bone(SOURCE_MOTION["clips"][clip]["samples"][0], last)["tail"]) -
                   _source_point_to_model(first_rest["head"]))
    pose_vector = (_source_point_to_model(_source_bone(sample, last)["tail"]) -
                   _source_point_to_model(first_pose["head"]))
    if rest_vector.length > 1e-6 and pose_vector.length > 1e-6:
        neck_delta = rest_vector.rotation_difference(pose_vector)
    else:
        neck_delta = _source_delta_rotation(clip, first, sample)
    neck_head = neck_target_head + (_source_point_to_model(first_pose["head"]) -
                                    _source_point_to_model(first_rest["head"])) * body_scale
    neck_head.z -= compression
    _set_world_bone_transform(rig, "Neck", neck_head, neck_delta @ neck_rest_q)

    head_source = "DEF-skull"
    head_rest_q = _target_rest_world(rig, "Head")[1]
    neck_tail = _bone_ends(rig, "Neck")[1]
    _set_world_bone_transform(rig, "Head", neck_tail,
                               _source_delta_rotation(clip, head_source, sample) @ head_rest_q)


def _source_hoof_pose(rig: bpy.types.Object, clip: str, leg: str, sample: dict[str, Any], profile: str,
                      rest_soles: dict[str, Vector], rest_offsets: dict[str, Vector],
                      rest_pasterns: dict[str, Vector], hoof_objects: dict[str, bpy.types.Object],
                      rest_contact_centers: dict[str, Vector], amplitude: float,
                      contact: dict[str, Any], contact_phase: float, in_stance: bool,
                      common_speed: float, duration: float) -> tuple[float, float, float]:
    source_name = ("DEF-f_hoof." if leg.startswith("Front") else "DEF-r_hoof.") + leg[-1]
    src_base_name = "DEF-spine.006" if leg.startswith("Front") else "DEF-spine.001"
    src_foot_rest = _source_bone(SOURCE_MOTION["rest"], source_name)
    src_base = _source_bone(SOURCE_MOTION["rest"], src_base_name)
    dest_segments = [f"{leg}_{part}" for part in (*_leg_parts(leg), "Pastern")
                     if rig.data.bones.get(f"{leg}_{part}")]
    dest_length = sum(rig.data.bones[name].length for name in dest_segments)
    source_length = max(0.1, src_base["head"][2] - src_foot_rest["tail"][2])
    scale = min(1.0, dest_length / source_length) * amplitude
    src_pose = _source_bone(sample, source_name)
    # The source action begins at an arbitrary stride phase. Center its planar
    # path on the actual stance interval instead of applying a static offset
    # between two unrelated rest-pose stances.
    source_target_point = src_pose["tail"]
    if in_stance:
        u = max(0.0, min(1.0, contact_phase / max(contact["stanceDuty"], 1e-6)))
        source_target_point = [contact["stanceStartPoint"][i] * (1.0-u) +
                               contact["stanceEndPoint"][i] * u for i in range(3)]
    delta_tail = (_source_point_to_model(source_target_point) -
                  _source_point_to_model(contact["stanceBaselinePoint"])) * scale
    if in_stance:
        # Normalize every limb to one forward travel speed, rather than
        # preserving incompatible source stance velocities across legs.
        delta_tail.x = common_speed * duration * (0.5*contact["stanceDuty"]-contact_phase)
        delta_tail.y = 0.0
        delta_tail.z = 0.0
    else:
        # Preserve source swing shape around a continuous baseline whose ends
        # meet the common-speed stance endpoints exactly.
        swing_u = max(0.0, min(1.0, (contact_phase-contact["stanceDuty"]) /
                               max(1.0-contact["stanceDuty"], 1e-6)))
        source_lift_start = _source_point_to_model(contact["stanceEndPoint"])
        source_lift_end = _source_point_to_model(contact["stanceStartPoint"])
        source_swing_base = source_lift_start.lerp(source_lift_end, swing_u)
        source_swing_deviation = _source_point_to_model(src_pose["tail"]) - source_swing_base
        target_swing_base_x = common_speed * duration * contact["stanceDuty"] * (swing_u-0.5)
        delta_tail.x = target_swing_base_x + source_swing_deviation.x * scale
        delta_tail.y = source_swing_deviation.y * scale
        # Zero at the inferred contact threshold, so entering/leaving stance
        # does not introduce a vertical snap.
        delta_tail.z = max(0.0, sample["hoofSurfaces"][leg]["minimumZ"] - contact["stanceBaselineZ"]) * scale
        # The measured source support interval wraps cleanly, but sampled
        # contact boundaries can have a few millimetres of clearance. Taper
        # only at the two ends so the swing meets the planted sole exactly.
        delta_tail.z *= _ease(swing_u/0.08)*_ease((1.0-swing_u)/0.08)
    target_sole = rest_soles[leg] + delta_tail
    delta_q = _source_segment_delta(leg, "Hoof", sample)
    rest_hoof_q = _target_rest_world(rig, f"{leg}_Hoof")[1]
    target_hoof_q = (delta_q @ rest_hoof_q).normalized()
    target_hoof_offset = delta_q @ rest_offsets[leg]
    pastern_vector = _source_segment_delta(leg, "Pastern", sample) @ rest_pasterns[leg]
    max_endpoint = 0.0
    max_segment = 0.0
    actual_min_z = 0.0
    for _ in range(2):
        endpoint_error, segment_error = _solve_leg(rig, leg, target_sole, target_hoof_offset, pastern_vector, sample)
        max_endpoint = max(max_endpoint, endpoint_error)
        max_segment = max(max_segment, segment_error)
        head = _bone_ends(rig, f"{leg}_Hoof")[0]
        _set_world_bone_transform(rig, f"{leg}_Hoof", head, target_hoof_q)
        bpy.context.view_layer.update()
        hoof_sample = _sample_hoof_geometry([hoof_objects[leg]])[hoof_objects[leg].name]
        actual_min_z = hoof_sample["minZ"]
        if in_stance:
            actual_contact = Vector(hoof_sample["contactCentroid"])
            desired_contact = rest_contact_centers[leg] + delta_tail
            target_sole.x += desired_contact.x - actual_contact.x
            target_sole.y += desired_contact.y - actual_contact.y
        # Match the actual surface clearance, including hoof roll. A bone tip
        # is not a sole: merely preventing penetration can erase the swing.
        target_sole.z += delta_tail.z-actual_min_z
    # Resolve once with the ground-corrected sole and retain the authored hoof roll.
    endpoint_error, segment_error = _solve_leg(rig, leg, target_sole, target_hoof_offset, pastern_vector, sample)
    max_endpoint = max(max_endpoint, endpoint_error)
    max_segment = max(max_segment, segment_error)
    head = _bone_ends(rig, f"{leg}_Hoof")[0]
    _set_world_bone_transform(rig, f"{leg}_Hoof", head, target_hoof_q)
    actual_min_z = _sample_hoof_geometry([hoof_objects[leg]])[hoof_objects[leg].name]["minZ"]
    if actual_min_z < -0.003:
        raise RuntimeError(f"{leg}: source-retargeted hoof penetrates by {-actual_min_z:.5f} m")
    return max_endpoint, max_segment, actual_min_z


def _pose_source_gait(rig: bpy.types.Object, clip: str, t: float, profile: str,
                      rest_soles: dict[str, Vector], hoof_offsets: dict[str, Vector],
                      pastern_vectors: dict[str, Vector], hoof_objects: list[bpy.types.Object],
                      rest_contact_centers: dict[str, Vector]) -> dict[str, Any]:
    duration = PROFILES[profile][f"{clip}_duration"]
    amplitude = PROFILES[profile][f"{clip}_amplitude"]
    sample = _source_clip_sample(clip, t / duration)
    contact_info = _source_contact_info(clip)
    source_duration = SOURCE_MOTION["clips"][clip]["durationSeconds"]
    source_speeds = sorted(info["sourceStanceSpeedMps"] for info in contact_info.values())
    source_common_speed = (source_speeds[1] + source_speeds[2]) * 0.5
    common_speed = source_common_speed * amplitude * source_duration / duration
    _reset_pose(rig)
    # A small mean body drop gives the nearly straight source-reference legs
    # reach margin while keeping the foot targets symmetric and un-stretched.
    # This authored source stride starts from a near-straight juvenile foreleg;
    # the modest extra crouch keeps the symmetric retarget inside its measured
    # two-link reach without stretching the mesh or shortening its stride.
    compression = 0.025 if profile == "adult" else 0.018
    _apply_source_body_pose(rig, clip, sample, profile, compression)
    objects = {ob.name.removeprefix("Hoof_"): ob for ob in hoof_objects}
    errors, soles = [], []
    contact_phases = {}
    for leg in ("Front_L", "Front_R", "Hind_L", "Hind_R"):
        info = contact_info[leg]
        contact_phase = (sample["phase"] - info["touchdownPhase"]) % 1.0
        in_stance = contact_phase < info["stanceDuty"]
        endpoint, segment, min_z = _source_hoof_pose(rig, clip, leg, sample, profile, rest_soles,
                                                      hoof_offsets, pastern_vectors, objects,
                                                      rest_contact_centers, amplitude, info,
                                                      contact_phase, in_stance, common_speed, duration)
        errors.append((leg, endpoint, segment))
        soles.append(min_z)
        contact_phases[leg] = contact_phase
    return {"solverErrors": errors, "targetSoleZ_m": soles,
            "pelvisCompression_m": compression, "sourcePhase": sample["phase"],
            "contactPhases": contact_phases, "commonStanceSpeedMps": common_speed}


def _key_pose(rig: bpy.types.Object, frame: int) -> None:
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.keyframe_insert(data_path="location", frame=frame, group=pb.name)
        pb.keyframe_insert(data_path="rotation_quaternion", frame=frame, group=pb.name)


def _sample_hoof_geometry(hoof_objects: list[bpy.types.Object]) -> dict[str, dict[str, Any]]:
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    report = {}
    for ob in hoof_objects:
        evaluated = ob.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        try:
            points = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
            minimum_z = min((p.z for p in points), default=0.0)
            contact_points = [p for p in points if p.z <= minimum_z + 0.003]
            report[ob.name] = {
                "minZ": minimum_z,
                "centroid": tuple(sum(p[i] for p in points) / max(1, len(points)) for i in range(3)),
                "contactCentroid": tuple(sum(p[i] for p in contact_points) / max(1, len(contact_points)) for i in range(3)),
            }
        finally:
            evaluated.to_mesh_clear()
    return report


def _clear_gait_actions(rig: bpy.types.Object) -> None:
    rig.animation_data_clear()
    for name in ("idle", "walk", "trot"):
        action = bpy.data.actions.get(name)
        if action:
            bpy.data.actions.remove(action)
    for name in ("Hind_L_Shin", "Hind_R_Shin"):
        if rig.data.bones.get(name) is None:
            raise RuntimeError(f"Missing required gait bone {name}")


def author_actions(rig: bpy.types.Object, profile: str, hoof_objects: list[bpy.types.Object]) -> dict[str, Any]:
    """Bake the three looping cycles as named Blender actions and NLA strips."""
    _clear_gait_actions(rig)
    scene = bpy.context.scene
    scene.render.fps = 30
    # Per-frame checks only need the small hoof meshes. Evaluating the fully
    # subdivided body and sculpted hair at every bake sample needlessly makes
    # this several-minute operation; restore every viewport modifier before
    # saving/exporting the final asset.
    hoof_names = {ob.name for ob in hoof_objects}
    deferred_modifiers = []
    for ob in bpy.data.objects:
        if ob.type == "MESH" and ob.name not in hoof_names:
            for modifier in ob.modifiers:
                deferred_modifiers.append((modifier, modifier.show_viewport))
                modifier.show_viewport = False
    rest_soles = {}
    rest_contact_centers = {}
    hoof_offsets = {}
    pastern_vectors = {}
    for leg in ("Front_L", "Front_R", "Hind_L", "Hind_R"):
        head, tail = _bone_ends(rig, f"{leg}_Hoof")
        rest_sample = _sample_hoof_geometry([bpy.data.objects[f"Hoof_{leg}"]])[f"Hoof_{leg}"]
        actual_rest = rest_sample["minZ"]
        rest_contact_centers[leg] = Vector(rest_sample["contactCentroid"])
        sole_correction = actual_rest-tail.z
        rest_soles[leg] = Vector((tail.x,tail.y,tail.z-sole_correction))
        hoof_offsets[leg] = head - tail
        pastern_head, pastern_tail = _bone_ends(rig, f"{leg}_Pastern")
        pastern_vectors[leg] = pastern_tail-pastern_head
    metadata: dict[str, Any] = {}
    next_track_start = 0
    for clip in ("idle", "walk", "trot"):
        duration = 3.0 if clip == "idle" else PROFILES[profile][f"{clip}_duration"]
        steps = round(duration * scene.render.fps)
        action = bpy.data.actions.new(name=clip)
        rig.animation_data_create()
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots.new(id_type="OBJECT", name="Skeleton") if hasattr(action, "slots") else None
        # The terminal sample repeats the initial pose exactly for a seamless
        # cycle. 30 Hz is well above the game sampler's 24 Hz.
        frame_diagnostics = []
        hoof_samples: dict[str, list[tuple[float, float, tuple[float,float,float]]]] = {name: [] for name in (ob.name for ob in hoof_objects)}
        contact_info = _source_contact_info(clip) if clip in {"walk", "trot"} else {}
        for offset in range(steps + 1):
            t = duration * offset / steps
            frame = offset
            # Evaluate the frame first; frame_set() otherwise restores the
            # previous keyed pose and can silently erase newly-authored values.
            scene.frame_set(frame)
            _reset_pose(rig)
            diagnostic = (_pose_idle(rig, t, profile, rest_soles, hoof_offsets,pastern_vectors) if clip == "idle"
                          else _pose_source_gait(rig, clip, t, profile, rest_soles, hoof_offsets,
                                                 pastern_vectors, hoof_objects, rest_contact_centers))
            _key_pose(rig, frame)
            actual_hooves = _sample_hoof_geometry(hoof_objects)
            diagnostic["actualHoofSoles"] = {name: round(values["minZ"], 6) for name, values in actual_hooves.items()}
            phases = {}
            if clip in {"walk", "trot"}:
                for leg, info in contact_info.items():
                    phases[leg] = (t / duration - info["touchdownPhase"]) % 1.0
            else:
                phases = {name.removeprefix("Hoof_"): 0.0 for name in actual_hooves}
            diagnostic["phases"] = phases
            for hoof_name, values in actual_hooves.items():
                key = hoof_name.removeprefix("Hoof_")
                hoof_samples[hoof_name].append((t, values["minZ"], values["contactCentroid"]))
            frame_diagnostics.append(diagnostic)
        action.frame_range = (0.0, float(steps))
        channelbag = action.layers[0].strips[0].channelbag(action.slots[0])
        for curve in channelbag.fcurves:
            for keyframe in curve.keyframe_points:
                keyframe.interpolation = "LINEAR"
        rig.animation_data.action = None
        metadata[clip] = {
            "durationSeconds": duration, "fps": 30, "loop": True,
            "keyedSamplesIncludingSeam": steps + 1,
            "solverMaxEndpointError_m": round(max((err[1] for sample in frame_diagnostics for err in sample["solverErrors"]), default=0.0), 6),
            "solverMaxSegmentLengthError_m": round(max((err[2] for sample in frame_diagnostics for err in sample["solverErrors"]), default=0.0), 6),
            "maximumTargetLift_m": max((max(sample.get("targetSoleZ_m", [0.0]) if isinstance(sample.get("targetSoleZ_m", [0.0]), (list, tuple)) else [sample.get("targetSoleZ_m", 0.0)]) for sample in frame_diagnostics), default=0.0),
            "minimumPelvisCompression_m": min((sample.get("pelvisCompression_m", 0.0) for sample in frame_diagnostics), default=0.0),
        }
        if clip in {"walk", "trot"}:
            metadata[clip]["referenceMotion"] = {
                "source": SOURCE_MOTION["source"],
                "clip": "horse." + clip,
                "sourceDurationSeconds": SOURCE_MOTION["clips"][clip]["durationSeconds"],
                "axisConversion": "source Z-up/-Y-forward/+X-left to target Blender +X-forward/+Y-left via +90deg Z",
                "trajectoryAmplitudeScale": PROFILES[profile][f"{clip}_amplitude"],
            }
        sole_min = min((row[1] for samples in hoof_samples.values() for row in samples), default=0.0)
        sole_max = max((row[1] for samples in hoof_samples.values() for row in samples), default=0.0)
        if sole_min < -0.003:
            raise RuntimeError(f"{clip}: evaluated hoof geometry penetrates ground by {-sole_min:.5f} m")
        metadata[clip]["evaluatedHoofMinZ_m"] = round(sole_min, 6)
        metadata[clip]["evaluatedHoofMaxZ_m"] = round(sole_max, 6)
        metadata[clip]["evaluatedHoofGroundPenetration_m"] = round(max(0.0, -sole_min), 6)
        if clip == "idle":
            idle_slide = 0.0
            for samples in hoof_samples.values():
                centers = [row[2] for row in samples]
                idle_slide = max(idle_slide, math.sqrt(sum((max(p[i] for p in centers)-min(p[i] for p in centers))**2 for i in (0,1))))
            if idle_slide > 0.004:
                raise RuntimeError(f"Idle feet drift despite planted targets ({idle_slide:.5f} m)")
            metadata[clip]["maxPlantedHoofPlanarDrift_m"] = round(idle_slide, 6)
        if clip in {"walk", "trot"}:
            stance_duty = sum(v["stanceDuty"] for v in contact_info.values()) / len(contact_info)
            touchdown = {leg: round(info["touchdownPhase"], 5) for leg, info in contact_info.items()}
            footfall = [leg for _, leg in sorted((info["touchdownPhase"], leg) for leg, info in contact_info.items())]
            leg_speeds = []
            max_stance_slip = 0.0
            stance_soles = []
            stance_soles_by_leg = {}
            stance_delta_by_leg = {}
            for leg, info in contact_info.items():
                samples = hoof_samples[f"Hoof_{leg}"]
                previous = None
                leg_stance_soles = []
                for sample_index, (sample_time, sole_z, center) in enumerate(samples):
                    phi = (sample_time / duration - info["touchdownPhase"]) % 1.0
                    if phi < info["stanceDuty"]:
                        stance_soles.append(sole_z)
                        leg_stance_soles.append(sole_z)
                        if previous is not None:
                            prior_time, prior_center = previous
                            dt = sample_time - prior_time
                            if dt > 0:
                                speed_estimate = -(center[0] - prior_center[0]) / dt
                                leg_speeds.append(speed_estimate)
                                stance_delta_by_leg.setdefault(leg, []).append(
                                    (prior_center[0], prior_center[1], center[0], center[1], dt))
                        previous = (sample_time, center)
                    else:
                        previous = None
                stance_soles_by_leg[leg] = leg_stance_soles
            speed = sorted(leg_speeds)[len(leg_speeds)//2] if leg_speeds else 0.0
            speed = max(0.0, speed)
            cumulative_stance_drift = {}
            accumulated_residual_path = {}
            for leg, values in stance_delta_by_leg.items():
                cumulative_x = 0.0
                cumulative_y = 0.0
                residual_path = 0.0
                for x0, y0, x1, y1, dt in values:
                    compensated_x = (x1-x0) + speed*dt
                    lateral_y = y1-y0
                    step_drift = math.hypot(compensated_x, lateral_y)
                    max_stance_slip = max(max_stance_slip, step_drift)
                    cumulative_x += compensated_x
                    cumulative_y += lateral_y
                    residual_path += step_drift
                cumulative_stance_drift[leg] = math.hypot(cumulative_x, cumulative_y)
                accumulated_residual_path[leg] = residual_path
            if max_stance_slip > 0.006:
                raise RuntimeError(f"{clip}: virtual-forward compensated stance foot slip {max_stance_slip:.5f} m")
            for leg, values in stance_soles_by_leg.items():
                low=min(values, default=0.0); high=max(values, default=0.0)
                if low < -0.003 or high > 0.003:
                    raise RuntimeError(f"{clip} {leg}: stance hoof sole outside ±3mm ground tolerance ({low:.5f}..{high:.5f} m)")
            metadata[clip]["maxVirtualForwardCompensatedStanceSlip_m"] = round(max_stance_slip, 6)
            metadata[clip]["netVirtualForwardCompensatedContactCentroidDrift_m_byLeg"] = {
                leg: round(value, 6) for leg, value in cumulative_stance_drift.items()}
            metadata[clip]["accumulatedCompensatedContactResidualPath_m_byLeg"] = {
                leg: round(value, 6) for leg, value in accumulated_residual_path.items()}
            metadata[clip]["contactDriftMetricNote"] = (
                "Net XY residual after virtual forward travel, plus accumulated residual path length, "
                "measured from evaluated low-vertex centroids; the low-vertex set can change slightly "
                "as hoof roll changes."
            )
            metadata[clip]["stanceHoofMinZ_m"] = round(min(stance_soles, default=0.0), 6)
            metadata[clip]["stanceHoofMaxZ_m"] = round(max(stance_soles, default=0.0), 6)
            metadata[clip]["stanceSoleZByLeg_m"] = {leg:[round(min(values,default=0.0),6),round(max(values,default=0.0),6)] for leg,values in stance_soles_by_leg.items()}
            metadata[clip].update({
                "inPlace": True, "travelSpeedMps": round(speed, 4),
                "stanceDuty": round(stance_duty, 4),
                "stanceSweep_m": round(speed * duration * stance_duty, 5),
                "footfallPattern": ", ".join(footfall),
                "touchdownPhase": touchdown,
                "sourceClip": "horse." + clip,
            })

    # NLA strips give Blender artists direct clip handles while preserving the
    # original exact action names used in the GLB.
    rig.animation_data_create()
    rig.animation_data.action = None
    for name in ("idle", "walk", "trot"):
        action = bpy.data.actions[name]
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, next_track_start, action)
        strip.action_frame_start = action.frame_range[0]
        strip.action_frame_end = action.frame_range[1]
        strip.blend_type = "REPLACE"
        strip.extrapolation = "NOTHING"
        next_track_start += 100
    scene.frame_start = 0
    scene.frame_end = next_track_start
    scene.frame_set(0)
    bpy.context.view_layer.update()
    for modifier, was_visible in deferred_modifiers:
        modifier.show_viewport = was_visible
    bpy.context.view_layer.update()
    return metadata
