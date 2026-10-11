"""Validate isolated saved actions, anatomy, and physical hoof surfaces.

blender --background --factory-startup --disable-autoexec --python-exit-code 1 \
  --python tools/horse/validate_reference_actions.py -- \
  --blend art/derived/Horse_Reference/horse_adult.blend --report /tmp/report.json
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import reference_animation as motion


def joint_angle(first: Vector, second: Vector) -> float:
    """Signed sagittal angle, positive when second points farther forward."""
    return math.degrees(math.atan2(first.x*second.z-first.z*second.x,
                                   first.x*second.x+first.z*second.z))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", type=Path, required=True)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()), use_scripts=False)
    rig = bpy.data.objects.get("Skeleton")
    if rig is None:
        raise RuntimeError("Skeleton not found")
    scene = bpy.context.scene
    if scene.render.fps != 30 or abs(scene.render.fps_base-1.0) > 1e-6:
        raise RuntimeError("Saved bake FPS must be exactly 30")
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    legs = ("Front_L", "Front_R", "Hind_L", "Hind_R")
    bone_names = [bone.name for bone in rig.data.bones if bone.use_deform]
    for leg in legs:
        for part in (*motion._leg_parts(leg), "Pastern", "Hoof"):
            if rig.data.bones.get(f"{leg}_{part}") is None:
                raise RuntimeError(f"Missing anatomical bone {leg}_{part}")
    # Avoid evaluating the high-detail torso for a hoof/action validation.
    for obj in bpy.data.objects:
        if obj.type == "MESH" and not obj.name.startswith("Hoof_"):
            for modifier in obj.modifiers:
                modifier.show_viewport = False
    profile = "foal" if "foal" in args.blend.name else "adult"
    failures = []
    results = {}
    for clip in ("idle", "walk", "trot"):
        action = bpy.data.actions.get(clip)
        if action is None:
            raise RuntimeError(f"Missing action {clip}")
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        expected_duration = 3.0 if clip == "idle" else motion.PROFILES[profile][f"{clip}_duration"]
        if abs(start) > 1e-6 or abs(end/30.0-expected_duration) > 1e-6:
            failures.append(f"{clip}: incorrect frame range {start}..{end}")
        contact = motion._source_contact_info(clip) if clip != "idle" else {}
        if contact:
            speeds = sorted(info["sourceStanceSpeedMps"] for info in contact.values())
            travel_speed = (speeds[1]+speeds[2])*0.5*motion.PROFILES[profile][f"{clip}_amplitude"]*motion.SOURCE_MOTION["clips"][clip]["durationSeconds"]/expected_duration
        else:
            travel_speed = 0.0
        hoof_rows = defaultdict(list)
        pose_rows = defaultdict(list)
        branch_rows = defaultdict(list)
        maximum_length_error = 0.0
        maximum_scale_error = 0.0
        maximum_connected_joint_error = 0.0
        for sample_index in range(round(end*2)+1):
            frame = sample_index*0.5
            scene.frame_set(math.floor(frame), subframe=frame % 1.0)
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            for leg in legs:
                obj = bpy.data.objects[f"Hoof_{leg}"]
                evaluated = obj.evaluated_get(depsgraph)
                mesh = evaluated.to_mesh()
                try:
                    points = [evaluated.matrix_world @ vertex.co for vertex in mesh.vertices]
                    if not points or any(not all(math.isfinite(v) for v in point) for point in points):
                        raise RuntimeError(f"{clip} {leg}: empty/nonfinite hoof surface")
                    min_z = min(point.z for point in points)
                    low = [point for point in points if point.z <= min_z+0.003]
                    contact_center = Vector(tuple(sum(point[axis] for point in low)/len(low) for axis in range(3)))
                    center = Vector(tuple(sum(point[axis] for point in points)/len(points) for axis in range(3)))
                    phi = (frame/end-contact[leg]["touchdownPhase"]) % 1.0 if contact else 0.0
                    stance = phi < contact[leg]["stanceDuty"] if contact else True
                    hoof_rows[leg].append((frame/30.0, min_z, center, contact_center, stance, phi))
                finally:
                    evaluated.to_mesh_clear()
                names = [f"{leg}_{part}" for part in motion._leg_parts(leg)]
                directions = [rig.matrix_world.to_3x3() @ (rig.pose.bones[name].tail-rig.pose.bones[name].head) for name in names]
                # Fore elbow positive, carpus negative; hind stifle negative,
                # hock positive. These checks catch the opposite IK branches.
                if leg.startswith("Front"):
                    branch_rows[leg+"_elbow"].append(joint_angle(directions[1], directions[2]))
                    branch_rows[leg+"_carpus"].append(joint_angle(directions[2], directions[3]))
                else:
                    branch_rows[leg+"_stifle"].append(joint_angle(directions[0], directions[1]))
                    branch_rows[leg+"_hock"].append(joint_angle(directions[1], directions[2]))
            for name in bone_names:
                bone = rig.pose.bones[name]
                if not all(math.isfinite(value) for row in bone.matrix for value in row):
                    raise RuntimeError(f"{clip} {name}: nonfinite pose")
                pose_rows[name].append((bone.matrix.translation.copy(), bone.matrix.to_quaternion()))
                maximum_scale_error = max(maximum_scale_error, *(abs(value-1.0) for value in bone.scale))
                maximum_length_error = max(maximum_length_error, abs((bone.tail-bone.head).length-rig.data.bones[name].length))
                if bone.bone.use_connect and bone.parent:
                    maximum_connected_joint_error = max(maximum_connected_joint_error, (bone.head-bone.parent.tail).length)
        seam_position = max((poses[-1][0]-poses[0][0]).length for poses in pose_rows.values())
        seam_rotation = max(poses[-1][1].rotation_difference(poses[0][1]).angle for poses in pose_rows.values())
        maximum_change = max(max((pose[0]-poses[0][0]).length+pose[1].rotation_difference(poses[0][1]).angle for pose in poses) for poses in pose_rows.values())
        max_rotation_step = max(poses[index][1].rotation_difference(poses[index-1][1]).angle for poses in pose_rows.values() for index in range(1,len(poses)))
        changing = [name for name, poses in pose_rows.items() if max((pose[0]-poses[0][0]).length+pose[1].rotation_difference(poses[0][1]).angle for pose in poses) > 1e-4]
        if seam_position > 1e-4 or seam_rotation > 1e-3:
            failures.append(f"{clip}: persisted full-deform-bone seam mismatch")
        if maximum_change < 1e-3 or "Head" not in changing:
            failures.append(f"{clip}: action is constant or lacks head motion")
        if max_rotation_step > math.radians(35):
            failures.append(f"{clip}: bone rotates more than35deg in half a frame")
        if maximum_length_error > 1e-4 or maximum_scale_error > 1e-5 or maximum_connected_joint_error > 1e-4:
            failures.append(f"{clip}: stretched/disconnected anatomical chain")
        hoof_report = {}
        for leg, rows in hoof_rows.items():
            stance_rows = [row for row in rows if row[4]]
            min_z = min(row[1] for row in rows)
            max_z = max(row[1] for row in rows)
            stance_min = min(row[1] for row in stance_rows)
            stance_max = max(row[1] for row in stance_rows)
            residual_path = 0.0
            net_residual = Vector((0.0,0.0,0.0))
            max_step_residual = 0.0
            for previous, current in zip(rows,rows[1:]):
                if previous[4] and current[4] and current[5] >= previous[5]:
                    delta = current[3]-previous[3]
                    delta.x += travel_speed*(current[0]-previous[0])
                    delta.z = 0.0
                    residual_path += delta.length
                    net_residual += delta
                    max_step_residual = max(max_step_residual,delta.length)
            idle_drift = math.sqrt(sum((max(row[2][axis] for row in rows)-min(row[2][axis] for row in rows))**2 for axis in (0,1)))
            if min_z < -0.003:
                failures.append(f"{clip} {leg}: sole penetration {-min_z:.5f}m")
            if stance_min < -0.003 or stance_max > 0.005:
                failures.append(f"{clip} {leg}: support sole outside -3..+5mm")
            if clip == "idle" and idle_drift > 0.004:
                failures.append(f"idle {leg}: planted hoof drifts {idle_drift:.5f}m")
            if clip != "idle":
                minimum_lift = 0.012 if clip == "walk" else 0.030
                if max_z < minimum_lift:
                    failures.append(f"{clip} {leg}: sole clearance {max_z:.5f}m is below {minimum_lift:.3f}m")
                if max_step_residual > 0.006 or net_residual.length > 0.020:
                    failures.append(f"{clip} {leg}: compensated contact drift too large")
            hoof_report[leg] = {"minSoleZ_m": round(min_z,6), "maxSoleZ_m": round(max_z,6),
                                "stanceSoleZRange_m": [round(stance_min,6),round(stance_max,6)],
                                "maximumCompensatedContactStep_m": round(max_step_residual,6),
                                "netCompensatedContactResidual_m": round(net_residual.length,6),
                                "accumulatedCompensatedContactResidualPath_m": round(residual_path,6),
                                "idlePlanarCentroidDrift_m": round(idle_drift,6)}
        branches = {name: [round(min(values),3),round(max(values),3)] for name,values in branch_rows.items()}
        for name, values in branch_rows.items():
            if name.endswith(("elbow","hock")) and min(values) < -12.0:
                failures.append(f"{clip} {name}: reversed anatomical bend")
            if name.endswith(("carpus","stifle")) and max(values) > 12.0:
                failures.append(f"{clip} {name}: reversed anatomical bend")
        results[clip] = {"frameRange": [start,end], "durationSeconds": expected_duration,
                         "samplesPerHoof": len(next(iter(hoof_rows.values()))),
                         "evaluatedHoofGeometry": hoof_report, "jointBendRanges_deg": branches,
                         "maximumBoneLengthError_m": maximum_length_error,
                         "maximumBoneScaleError": maximum_scale_error,
                         "maximumConnectedJointError_m": maximum_connected_joint_error,
                         "maximumDeformBonePositionSeamError_m": seam_position,
                         "maximumDeformBoneRotationSeamError_rad": seam_rotation,
                         "maximumHalfFrameBoneRotation_deg": math.degrees(max_rotation_step),
                         "changingDeformBones": changing, "virtualForwardSpeedMps": travel_speed}
    result = {"blend": str(args.blend.resolve()), "fps": 30, "subframeStepFrames": 0.5,
              "deformBonesChecked": bone_names, "validatedActions": results,
              "failures": failures, "passed": not failures,
              "limits": "Geometric action checks do not establish visual motion quality; renderer review is required."}
    serialized = json.dumps(result,indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True)
        args.report.write_text(serialized+"\n")
    print(serialized)
    if failures:
        raise RuntimeError("Saved action validation failed: "+"; ".join(failures))


if __name__ == "__main__":
    main()
