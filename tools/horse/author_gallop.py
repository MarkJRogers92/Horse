"""Author an in-place transverse gallop (D-015) for the adult walk rigs and export it as
an animation-only GLB that Godot adds to the checked Walk model at runtime.

    python3 tools/horse/author_gallop.py --family light --review DIR [--export]

The audited Walk meshes are never re-exported: only the armature and the "Gallop" action
leave Blender. Joint angles are armature-space rotations about the horse's lateral (Y) axis,
so the same table drives both adult rigs (they share deform-bone names).
"""
from __future__ import annotations
import argparse, math, sys
from pathlib import Path
import bpy
from mathutils import Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCES = {"light": "game/assets/horse/production/horse_light_walk.glb",
           "stock": "game/assets/horse/production/horse_stock_walk.glb"}
FPS = 24
FRAMES = 16                      # one stride = 0.667 s
STANCE = 0.34                    # fraction of the stride each hoof is on the ground
LEGS = {("back", "l"): 0.00, ("back", "r"): 0.10, ("front", "r"): 0.38, ("front", "l"): 0.50}


def ease(x: float) -> float:
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def leg_angles(limb: str, p: float) -> dict[str, float]:
    """Joint angles in degrees (+ = rotate backward about the lateral axis) at leg phase p."""
    if p < STANCE:                                    # support: hoof planted, body passes over it
        s = p / STANCE
        if limb == "front":
            return {"humerus": -30 + 58 * s, "upper": 4 * math.sin(math.pi * s), "lower": 0.0,
                    "ankle": -16 * math.sin(math.pi * s), "foot": 6 * math.sin(math.pi * s)}
        return {"upper": -26 + 62 * s, "lower": -8 * s, "ankle": 10 * s,
                "foot": -14 * math.sin(math.pi * s), "toe": 6 * math.sin(math.pi * s)}
    s = (p - STANCE) / (1 - STANCE)                  # swing: fold, carry forward, reach
    fold = math.sin(math.pi * min(s * 1.15, 1.0))
    if limb == "front":
        return {"humerus": 28 - 58 * ease(s), "upper": -48 * fold, "lower": 105 * fold,
                "ankle": 35 * fold, "foot": 10 * fold}
    return {"upper": 36 - 62 * ease(s), "lower": 34 * fold, "ankle": -70 * fold,
            "foot": 32 * fold, "toe": 10 * fold}


BONES = {"front": {"humerus": "front_humerus_{s}", "upper": "front_leg_upper_{s}", "lower": "front_leg_lower_{s}",
                   "ankle": "front_leg_ankle_{s}", "foot": "front_leg_foot_{s}"},
         "back": {"upper": "back_leg_upper_{s}", "lower": "back_leg_lower_{s}", "ankle": "back_leg_ankle_{s}",
                  "foot": "back_leg_foot_{s}", "toe": "back_leg_toe_{s}"}}


def local_rotation(rig, name: str, degrees: float) -> Quaternion:
    bone = rig.data.bones[name]
    rest = bone.matrix_local.to_3x3()
    world = Quaternion(Vector((0, 1, 0)), math.radians(degrees)).to_matrix()
    return (rest.inverted() @ world @ rest).to_quaternion()


def pose_at(rig, c: float) -> None:
    """Pose the whole horse at stride fraction c in [0, 1)."""
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    angles: dict[str, float] = {}
    for (limb, side), offset in LEGS.items():
        for joint, value in leg_angles(limb, (c - offset) % 1.0).items():
            angles[BONES[limb][joint].format(s=side)] = value
    wave = math.sin(2 * math.pi * (c - 0.15))
    # Back flexes and extends once per stride; neck pumps against it; tail streams back.
    angles.update({"hips": -5 * wave, "spine_1": 3 * wave, "spine_2": 3 * wave, "spine_3": 2 * wave,
                   "spine_5": -9 * math.sin(2 * math.pi * (c - 0.05)), "spine_6": 6 * math.sin(2 * math.pi * (c + 0.1)) - 4,
                   "head": -5 * math.sin(2 * math.pi * (c + 0.15)) + 4,
                   "tail_1": 38 + 6 * wave, "tail_2": 14 + 8 * math.sin(2 * math.pi * (c - 0.3)),
                   "tail_3": 10 * math.sin(2 * math.pi * (c - 0.45)), "tail_4": 10 * math.sin(2 * math.pi * (c - 0.6)),
                   "ear_1_l": 12, "ear_1_r": 12})
    for name, degrees in angles.items():
        if name in rig.pose.bones:
            rig.pose.bones[name].rotation_quaternion = local_rotation(rig, name, degrees)
    # Vertical bob: highest in the suspension phase after the leading fore leaves the ground.
    lift = 0.06 * math.sin(2 * math.pi * (c - 0.62)) - 0.015
    hips = rig.data.bones["hips"]
    rig.pose.bones["hips"].location = hips.matrix_local.to_3x3().inverted() @ Vector((0, 0, lift))


def build(rig) -> bpy.types.Action:
    rig.animation_data_create()
    action = bpy.data.actions.new("Gallop")
    rig.animation_data.action = action
    for frame in range(FRAMES + 1):
        pose_at(rig, (frame % FRAMES) / FRAMES)
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            pb.keyframe_insert("location", frame=frame)
    action.frame_range = (0, FRAMES)
    return action


def review(rig, out: Path, family: str) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"; scene.display.shading.color_type = "OBJECT"
    scene.render.resolution_x, scene.render.resolution_y = 520, 360
    scene.world = scene.world or bpy.data.worlds.new("w")
    for ob in bpy.data.objects:
        if ob.type == "MESH": ob.color = (0.62, 0.38, 0.22, 1)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"; cam.data.ortho_scale = 3.4
    cam.location = (0.0, -6.0, 0.95); cam.rotation_euler = (math.radians(90), 0, 0); scene.camera = cam
    ground = bpy.data.objects.new("ground", None)
    out.mkdir(parents=True, exist_ok=True)
    for frame in range(0, FRAMES, 2):
        scene.frame_set(frame)
        scene.render.filepath = str(out / f"{family}_gallop_{frame:02d}.png")
        bpy.ops.render.render(write_still=True)


def export(rig, family: str) -> Path:
    for ob in list(bpy.data.objects):
        if ob is not rig: bpy.data.objects.remove(ob, do_unlink=True)
    for action in list(bpy.data.actions):
        if action.name != "Gallop": bpy.data.actions.remove(action)
    path = ROOT / f"game/assets/horse/production/horse_{family}_gallop.glb"
    bpy.ops.object.select_all(action="DESELECT"); rig.select_set(True); bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True, export_animations=True,
                              export_animation_mode="ACTIONS", export_skins=True, export_morph=False, export_force_sampling=True,
                              export_frame_range=False)
    return path


def main() -> None:
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--family", choices=sorted(SOURCES), required=True)
    parser.add_argument("--review"); parser.add_argument("--export", action="store_true")
    args = parser.parse_args(argv)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT / SOURCES[args.family]))
    rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    bpy.context.scene.render.fps = FPS
    build(rig)
    if args.review: review(rig, Path(args.review), args.family)
    if args.export: print("exported", export(rig, args.family))


if __name__ == "__main__":
    main()
