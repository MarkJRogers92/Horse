"""Prepare the CC0 authored horse profiles for Cloverbrook's procedural visual shaders.

Run in the pinned Blender Python environment:
  blender --background --factory-startup --disable-autoexec --python tools/horse/import_reference.py

The source .blend is kept in art/reference/Horse_Reference by the branch owner.
The generated game asset is descriptor-neutral: the game presenter supplies all
coat, marking, hair and eye colors.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import struct
import sys
import time
from pathlib import Path

import bpy
import bmesh
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, str(Path(__file__).resolve().parent))
import reference_animation as gait

ASSET_VERSION = "horse-reference.v1"
EXPECTED_MESHES = {"Body", "Eyes", "Mane", "Tail"}
LEG_ORDER = ["Front_L", "Front_R", "Hind_L", "Hind_R"]
LEG_CANONICAL_INDEX = {name: i for i, name in enumerate(LEG_ORDER)}


def paths() -> tuple[Path, Path, Path, Path, list[str]]:
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--source", type=Path, default=repo / "art/reference/Horse_Reference/Horse_Reference.blend")
    parser.add_argument("--out", type=Path, default=repo / "game/assets/horse/reference")
    parser.add_argument("--blend-dir", type=Path, default=repo / "art/derived/Horse_Reference")
    parser.add_argument("--profile", choices=("adult","foal","both"), default="both")
    if "--" in sys.argv:
        cli_args=sys.argv[sys.argv.index("--")+1:]
    elif "--python" in sys.argv or "--python-expr" in sys.argv:
        cli_args=[]
    else:
        cli_args=sys.argv[1:]
    args = parser.parse_args(cli_args)
    profiles=["adult","foal"] if args.profile=="both" else [args.profile]
    return repo, args.source.resolve(), args.out.resolve(), args.blend_dir.resolve(), profiles


def ensure_uv0(mesh: bpy.types.Mesh) -> bpy.types.MeshUVLoopLayer:
    if len(mesh.uv_layers) == 0:
        layer = mesh.uv_layers.new(name="UVMap")
        coords = [v.co for v in mesh.vertices]
        lo = [min(v[i] for v in coords) for i in range(3)]
        hi = [max(v[i] for v in coords) for i in range(3)]
        for loop in mesh.loops:
            p = mesh.vertices[loop.vertex_index].co
            layer.data[loop.index].uv = (
                (p.x - lo[0]) / max(1e-8, hi[0] - lo[0]),
                (p.z - lo[2]) / max(1e-8, hi[2] - lo[2]),
            )
    layer = mesh.uv_layers[0]
    layer.name = "UVMap"
    mesh.uv_layers.active = layer
    return layer


def rename_source_objects(rig: bpy.types.Object) -> tuple[bpy.types.Object, list[bpy.types.Object], list[bpy.types.Object], bpy.types.Object, bpy.types.Object]:
    body = bpy.data.objects["Horse_Body"]
    hooves = [bpy.data.objects[n] for n in ("Hoof_Front_L", "Hoof_Front_R", "Hoof_Hind_L", "Hoof_Hind_R")]
    eyes = [bpy.data.objects["Horse_Eye.L"], bpy.data.objects["Horse_Eye.R"]]
    mane = bpy.data.objects["Horse_Mane_and_Forelock"]
    tail = bpy.data.objects["Horse_Tail"]
    for ob in [body, *hooves, *eyes, mane, tail]:
        if ob.parent != rig:
            raise RuntimeError(f"{ob.name} is not bound to the expected source rig")
        ensure_uv0(ob.data)
    body.name = "Body"
    mane.name = "Mane"
    tail.name = "Tail"
    return body, hooves, eyes, mane, tail


def set_root_and_axis(rig: bpy.types.Object) -> None:
    # The source is Blender Z-up and faces -Y. Rotating the rig object +90 degrees
    # about Z yields Blender +X forward, +Y anatomical left; glTF's standard Y-up
    # export then maps anatomical left to glTF -Z while retaining +X forward.
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    root = rig.data.edit_bones.get("Root")
    if root is None:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError("Source FK skeleton has no Root bone")
    root.head = (0.0, 0.0, 0.0)
    root.tail = (0.0, 0.0, 0.18)
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.name = "Skeleton"
    rig.data.name = "Skeleton"
    rig.rotation_mode = "XYZ"
    rig.rotation_euler.z = math.pi / 2
    rig.animation_data_clear()
    bpy.context.view_layer.update()


def set_eye_uv(eye: bpy.types.Object) -> None:
    mesh = eye.data
    layer = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    world = [eye.matrix_world @ v.co for v in mesh.vertices]
    side_center = sum(p.y for p in world) / len(world)
    outward_sign = 1.0 if side_center >= 0.0 else -1.0
    outward = [outward_sign * p.y for p in world]
    inner = min(outward)
    outer = max(outward)
    z_lo = min(p.z for p in world)
    z_hi = max(p.z for p in world)
    for loop in mesh.loops:
        p = world[loop.vertex_index]
        u = (outward_sign * p.y - inner) / max(1e-8, outer - inner)
        v = (p.z - z_lo) / max(1e-8, z_hi - z_lo)
        layer.data[loop.index].uv = (max(0.0, min(1.0, u)), max(0.0, min(1.0, v)))
    mesh.uv_layers.active = layer
    for poly in mesh.polygons:
        poly.use_smooth = True


def join_meshes(active: bpy.types.Object, others: list[bpy.types.Object]) -> bpy.types.Object:
    bpy.ops.object.select_all(action="DESELECT")
    active.select_set(True)
    for ob in others:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = active
    bpy.ops.object.join()
    return active


def create_neutral_material(name: str, normal_image: bpy.types.Image | None = None) -> bpy.types.Material:
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (0.8, 0.8, 0.8, 1.0)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bs.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1.0)
    bs.inputs["Roughness"].default_value = 0.72
    bs.inputs["Specular IOR Level"].default_value = 0.25
    nt.links.new(bs.outputs["BSDF"], out.inputs["Surface"])
    if normal_image:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = normal_image
        tex.image.colorspace_settings.name = "Non-Color"
        normal = nt.nodes.new("ShaderNodeNormalMap")
        normal.inputs["Strength"].default_value = 0.42
        nt.links.new(tex.outputs["Color"], normal.inputs["Color"])
        nt.links.new(normal.outputs["Normal"], bs.inputs["Normal"])
    return mat


def assign_material(ob: bpy.types.Object, mat: bpy.types.Material) -> None:
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    for poly in ob.data.polygons:
        poly.material_index = 0


def normalize_group_names(ob: bpy.types.Object, rig: bpy.types.Object) -> None:
    wanted = {b.name for b in rig.data.bones}
    existing = {g.name for g in ob.vertex_groups}
    missing = wanted - existing
    if missing:
        for name in sorted(missing):
            ob.vertex_groups.new(name=name)


def join_eyes(eyes: list[bpy.types.Object], neutral_eye: bpy.types.Material) -> bpy.types.Object:
    for ob in eyes:
        set_eye_uv(ob)
    result = join_meshes(eyes[0], eyes[1:])
    result.name = "Eyes"
    assign_material(result, neutral_eye)
    for poly in result.data.polygons:
        poly.use_smooth = True
    return result


def map_hair_uv(ob: bpy.types.Object, kind: str, body: bpy.types.Object) -> None:
    mesh = ob.data
    layer = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    world = [ob.matrix_world @ v.co for v in mesh.vertices]
    lateral = [p.y for p in world]
    lo_y, hi_y = min(lateral), max(lateral)
    body_bmesh=bmesh.new()
    body_bmesh.from_mesh(body.data)
    body_tree=BVHTree.FromBMesh(body_bmesh,epsilon=0.0)
    body_bmesh.free()
    body_inverse=body.matrix_world.inverted()
    root_distances=[]
    for p in world:
        nearest=body_tree.find_nearest(body_inverse @ p)
        root_distances.append(nearest[3] if nearest else 0.0)
    ordered=sorted(root_distances)
    tip_distance=ordered[min(len(ordered)-1,int(0.95*(len(ordered)-1)))]
    tip_distance=max(tip_distance,1e-5)
    for loop in mesh.loops:
        p = world[loop.vertex_index]
        u = (p.y - lo_y) / max(1e-8, hi_y - lo_y)
        # Source hair is a sculpted volume. Distance from the body surface gives
        # each attached clump a shared root-to-free-tip coordinate without
        # projecting the whole mane/tail along the horse's length.
        v = min(1.0,max(0.0,root_distances[loop.vertex_index]/tip_distance))
        layer.data[loop.index].uv = (u, v)
    mesh.uv_layers.active = layer
    for poly in mesh.polygons:
        poly.use_smooth = True


def vertex_group_weights(ob: bpy.types.Object, vertex_index: int) -> dict[str, float]:
    v = ob.data.vertices[vertex_index]
    by_index = {g.index: g.name for g in ob.vertex_groups}
    return {by_index[g.group]: g.weight for g in v.groups if g.group in by_index}


def foal_region(group_name: str) -> str:
    if group_name=="Head" or group_name.startswith("Ear_"):
        return "head"
    if group_name.endswith("_Hoof"):
        return "hoof"
    if group_name.startswith("Front_") or group_name.startswith("Hind_"):
        return "leg"
    if group_name.startswith("Tail_"):
        return "tail"
    if group_name=="Neck":
        return "neck"
    return "trunk"


def foal_point(p: Vector, region: str, withers: Vector, poll: Vector) -> Vector:
    # Input and output use the source Blender basis (X lateral, -Y forward, Z up).
    # This smooth regional form keeps the hooves grounded while compacting the
    # barrel, shortening the neck and tail, and retaining a proportionately large head.
    q=-p.y; q_w=-withers.y; q_poll=-poll.y
    q_new=q_w+0.68*(q-q_w)
    lateral_scale={"trunk":0.63,"neck":0.76,"head":1.00,"leg":0.84,"hoof":0.78,"tail":0.80}.get(region,0.63)
    if region=="head":
        q_poll_new=q_w+0.68*(q_poll-q_w)
        q_new=q_poll_new+0.66*(q-q_poll)
        z_poll_new=poll.z*0.68
        z_new=z_poll_new+0.88*(p.z-poll.z)
    elif region=="leg":
        z_new=p.z*0.74
    elif region=="hoof":
        z_new=p.z*0.74
    elif region=="tail":
        z_new=p.z*0.68
        q_new=q_w+0.68*(q-q_w)
    else:
        if region=="trunk":
            # Keep the topline near withers while lifting the belly to make a
            # shallower juvenile barrel, instead of scaling the whole torso.
            z_new=withers.z*0.68+0.52*(p.z-withers.z)
        else:
            z_new=p.z*0.68
        if region=="neck":q_new=q_w+0.76*(q-q_w)
    return Vector((p.x*lateral_scale,-q_new,z_new))


def apply_foal_deformation(rig: bpy.types.Object, mesh_objects: list[bpy.types.Object]) -> dict:
    withers=rig.data.bones["Chest"].tail_local.copy()
    poll=rig.data.bones["Head"].head_local.copy()
    old_height=withers.z
    # Build transformed joint positions once so connected bone endpoints remain shared.
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones=rig.data.edit_bones
    joint_samples={}
    endpoint_keys={}
    for bone in edit_bones:
        for label,point in (("head",bone.head.copy()),("tail",bone.tail.copy())):
            key=tuple(round(v,5) for v in point)
            endpoint_keys[(bone.name,label)]=key
            joint_samples.setdefault(key,[]).append(foal_point(point,foal_region(bone.name),withers,poll))
    mapped_joints={key:sum(points,Vector())/len(points) for key,points in joint_samples.items()}
    for bone in edit_bones:
        bone.head=mapped_joints[endpoint_keys[(bone.name,"head")]]
        bone.tail=mapped_joints[endpoint_keys[(bone.name,"tail")]]
    bpy.ops.object.mode_set(mode="OBJECT")

    rig_inverse=rig.matrix_world.inverted()
    for ob in mesh_objects:
        group_names={g.index:g.name for g in ob.vertex_groups}
        object_inverse=ob.matrix_world.inverted()
        for vertex in ob.data.vertices:
            weights={group_names[item.group]:item.weight for item in vertex.groups if item.group in group_names}
            total=sum(weights.values())
            if total<=1e-8:
                weights={"Root":1.0};total=1.0
            point=rig_inverse @ (ob.matrix_world @ vertex.co)
            mapped=Vector((0,0,0))
            for name,weight in weights.items():
                mapped+=foal_point(point,foal_region(name),withers,poll)*(weight/total)
            vertex.co=object_inverse @ (rig.matrix_world @ mapped)
        ob.data.update()
    new_withers=foal_point(withers,"trunk",withers,poll)
    return {"withersBefore_m":round(old_height,5),"withersAfter_m":round(new_withers.z,5),"withersScale":round(new_withers.z/max(old_height,1e-8),5),"regionalScales":{"barrel_axial":0.68,"barrel_lateral":0.63,"barrel_vertical_from_topline":0.52,"legs_vertical":0.74,"head_muzzle_axial":0.66,"head_relative_vertical":0.88,"hoof_lateral":0.78}}


def transform_mesh_rig_space(ob: bpy.types.Object, rig: bpy.types.Object, transform) -> None:
    rig_inverse=rig.matrix_world.inverted();ob_inverse=ob.matrix_world.inverted()
    for vertex in ob.data.vertices:
        point=rig_inverse @ (ob.matrix_world @ vertex.co)
        vertex.co=ob_inverse @ (rig.matrix_world @ transform(point))
    ob.data.update()


def shorten_tail_with_rig(tail: bpy.types.Object, rig: bpy.types.Object, factor: float) -> dict:
    """Shorten the tail as one coherent form and match every tail joint."""
    root=rig.data.bones["Tail_01"].head_local.copy()
    def shrink(point):return root+(point-root)*factor
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode="EDIT")
    for bone in rig.data.edit_bones:
        if bone.name.startswith("Tail_"):
            bone.head=shrink(bone.head.copy());bone.tail=shrink(bone.tail.copy())
    bpy.ops.object.mode_set(mode="OBJECT")
    transform_mesh_rig_space(tail,rig,shrink)
    return {"factor":factor,"anchor":"Tail_01 dock; shared affine map for mesh and Tail_* rest joints"}


def shorten_mane_from_crest(mane: bpy.types.Object, body: bpy.types.Object, rig: bpy.types.Object,
                            length_factor: float=0.24, lateral_factor: float=0.72,
                            forelock_factor: float=0.30) -> dict:
    """Compress mane hanging height to a sampled dorsal crest field, keeping roots coherent."""
    body_bm=bmesh.new();body_bm.from_mesh(body.data)
    tree=BVHTree.FromBMesh(body_bm,epsilon=0.0);body_bm.free()
    rig_inverse=rig.matrix_world.inverted();body_inverse=body.matrix_world.inverted();mane_inverse=mane.matrix_world.inverted()
    rig_to_body=body_inverse @ rig.matrix_world
    poll=rig.data.bones["Head"].head_local.copy()
    group_names={group.index:group.name for group in mane.vertex_groups}
    coords=[]
    for vertex in mane.data.vertices:
        point=rig_inverse @ (mane.matrix_world @ vertex.co)
        q=-point.y
        hit=tree.ray_cast(rig_to_body @ Vector((0.0,-q,10.0)),rig_to_body.to_3x3() @ Vector((0.0,0.0,-1.0)),20.0)
        crest_z=(rig_inverse @ (body.matrix_world @ hit[0])).z if hit[0] is not None else point.z
        is_forelock=any(group_names.get(weight.group)=="Head" and weight.weight>=0.5 for weight in vertex.groups)
        if is_forelock:
            mapped=poll+(point-poll)*forelock_factor
        else:
            mapped=Vector((point.x*lateral_factor,point.y,crest_z+(point.z-crest_z)*length_factor))
        coords.append(mane_inverse @ (rig.matrix_world @ mapped))
    for vertex,point in zip(mane.data.vertices,coords):vertex.co=point
    mane.data.update()
    for poly in mane.data.polygons:poly.use_smooth=True
    return {"mane_verticalToCrest":length_factor,"mane_lateralWidth":lateral_factor,
            "forelockScaleAboutPoll":forelock_factor,"crestField":"sampled centerline downward ray onto Body; no per-vertex nearest-surface projection"}


def point_segment_distance(p: Vector, a: Vector, b: Vector) -> float:
    ab = b - a
    d = ab.length_squared
    t = 0.0 if d < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / d))
    return (p - (a + t * ab)).length


def smoothstep(a: float, b: float, x: float) -> float:
    if b <= a:
        return 1.0 if x >= b else 0.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def rig_world_bones(rig: bpy.types.Object) -> dict[str, tuple[Vector, Vector]]:
    return {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}


def classify_vertices(ob: bpy.types.Object, rig: bpy.types.Object, profile: str="adult"):
    bones = rig_world_bones(rig)
    coords = [ob.matrix_world @ v.co for v in ob.data.vertices]
    weights = [vertex_group_weights(ob, i) for i in range(len(ob.data.vertices))]
    head_a, head_b = bones["Head"]
    hoof_height_limit=0.18*(0.74 if profile=="foal" else 1.0)
    head_axis = (head_b - head_a).normalized()
    head_len = (head_b - head_a).length
    head_s = [((p - head_a).dot(head_axis) / head_len) for p in coords]
    head_width_samples = [abs(p.y) for p,s,w in zip(coords,head_s,weights) if -0.1<=s<=1.15 and point_segment_distance(p,head_a,head_b)<=0.16 and (w.get("Head",0.0)>=0.05 or point_segment_distance(p,head_a,head_b)<=0.12)]
    head_half_width = max(0.04, max(head_width_samples, default=0.04))
    tags = []
    leg_segments = {}
    knee_heights = {}
    for leg in LEG_ORDER:
        leg_segments[leg] = [bones[f"{leg}_Cannon"], bones[f"{leg}_Pastern"], bones[f"{leg}_Hoof"]]
        knee_heights[leg] = max(0.25, bones[f"{leg}_Cannon"][0].z)

    for i, (p, w) in enumerate(zip(coords, weights)):
        head_weight = w.get("Head", 0.0)
        head_dist = point_segment_distance(p, head_a, head_b)
        s = head_s[i]
        is_head = (head_weight >= 0.14 or (head_dist <= 0.15 and -0.35 <= s <= 1.25))
        leg_scores = {}
        for leg in LEG_ORDER:
            prefix = leg + "_"
            score = sum(value for name, value in w.items() if name.startswith(prefix))
            dist = min(point_segment_distance(p, *segment) for segment in leg_segments[leg])
            h = p.z / knee_heights[leg]
            hoof_weight = w.get(leg + "_Hoof", 0.0)
            candidate = score >= 0.16 and dist <= 0.19 and -0.03 <= h <= 1.08
            if hoof_weight >= 0.98 and p.z <= hoof_height_limit:
                candidate = True
            if candidate:
                leg_scores[leg] = (score, dist)
        leg = max(leg_scores, key=lambda name: (leg_scores[name][0], -leg_scores[name][1])) if leg_scores else None
        tags.append({"head": is_head, "head_s": s, "head_dist": head_dist, "leg": leg, "weights": w, "p": p})
    return tags, coords, weights, bones, head_a, head_axis, head_len, head_half_width, knee_heights


def ear_point_mask(p: Vector, weights: dict[str, float], bones: dict[str, tuple[Vector, Vector]]) -> float:
    value = 0.0
    for side in ("L", "R"):
        name = "Ear_" + side
        if weights.get(name, 0.0) <= 0.02:
            continue
        a, b = bones[name]
        ab = b - a
        t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-8, ab.length_squared)))
        value = max(value, smoothstep(0.48, 0.9, t) * min(1.0, weights[name] * 1.5))
    return value


def body_attributes(ob: bpy.types.Object, rig: bpy.types.Object, profile: str="adult"):
    mesh = ob.data
    tags, coords, weights, bones, head_a, head_axis, head_len, head_width, knee_heights = classify_vertices(ob, rig,profile)
    width_bins = [0.0] * 16
    for tag, p in zip(tags, coords):
        if tag["head"] and tag["head_dist"]<=0.16 and -0.12 <= tag["head_s"] <= 1.12:
            bi = max(0, min(15, int(max(0.0, tag["head_s"]) * 15.999)))
            width_bins[bi] = max(width_bins[bi], abs(p.y))
    width_bins = [max(head_width * 0.72, v) for v in width_bins]

    # Existing texture UVs stay as UVMap; Canonical is UV1/TEXCOORD_1.
    for old in list(mesh.uv_layers):
        if old.name == "Canonical":
            mesh.uv_layers.remove(old)
    canonical = mesh.uv_layers.new(name="Canonical")

    for old in list(mesh.color_attributes):
        if old.name == "Color":
            mesh.color_attributes.remove(old)
    color = mesh.color_attributes.new(name="Color", type="FLOAT_COLOR", domain="CORNER")
    mesh.color_attributes.active_color = color

    eyes_ob=bpy.data.objects.get("Eyes")
    eye_centers=[]
    if eyes_ob:
        for side in (-1,1):
            side_verts=[eyes_ob.matrix_world @ v.co for v in eyes_ob.data.vertices if side*(eyes_ob.matrix_world @ v.co).y>0]
            if side_verts:eye_centers.append(sum(side_verts,Vector())/len(side_verts))
    head_front_axis=Vector((-head_axis.z,0.0,head_axis.x)).normalized()
    height_factor=max(0.5,rig.data.bones["Chest"].tail_local.z/1.6)
    head_front_count=0;forehead_front_count=0;head_poll_count=0;head_muzzle_count=0;leg_loop_counts={k:0 for k in LEG_ORDER};sentinel_loops=0
    for poly in mesh.polygons:
        ids=list(poly.vertices)
        head_ids=[i for i in ids if tags[i]["head"]]
        leg_votes={leg:sum(1 for i in ids if tags[i]["leg"]==leg) for leg in LEG_ORDER}
        leg = max(LEG_ORDER,key=lambda name:leg_votes[name]) if max(leg_votes.values(),default=0)>0 else None
        is_head_face=bool(head_ids) and (leg is None or len(head_ids)>=leg_votes[leg])
        if is_head_face:
            kind="head"
        elif leg is not None:
            kind="leg"
        else:
            kind="other"
        for li in poly.loop_indices:
            vi=mesh.loops[li].vertex_index; tag=tags[vi];p=coords[vi];w=weights[vi]
            if kind=="head":
                s=max(-0.45,min(1.35,tag["head_s"]))
                bi=max(0,min(15,int(max(0.0,min(1.0,s))*15.999)))
                t=max(-1.25,min(1.25,p.y/max(0.025,width_bins[bi])))
                canonical.data[li].uv=(s,t)
                head_poll_count+=int(s<0.12);head_muzzle_count+=int(s>0.82)
            elif kind=="leg":
                canonical.data[li].uv=(10.0+LEG_CANONICAL_INDEX[leg],0.15)
                if tag["leg"]==leg:
                    h=max(0.0,min(1.1,p.z/knee_heights[leg]))
                    if w.get(leg+"_Hoof",0.0)>=0.98:
                        h=min(h,0.15)
                else:
                    h=1.1
                canonical.data[li].uv=(10.0+LEG_CANONICAL_INDEX[leg],h)
                leg_loop_counts[leg]+=1
            else:
                canonical.data[li].uv=(-10.0,50.0)
                sentinel_loops+=1

            # Use the smoothed vertex normal transformed to world space.
            n=(ob.matrix_world.to_3x3() @ mesh.vertices[vi].normal).normalized()
            red=0.0;green=0.0;blue=0.0;alpha=0.0
            if kind=="leg" and tag["leg"]==leg:
                h=p.z/knee_heights[leg]
                red=max(red,1.0-smoothstep(0.22,0.78,h))
            red=max(red,ear_point_mask(p,w,bones))
            if kind=="head":
                if tag["head"]:
                    s=tag["head_s"]
                    green=max(green,smoothstep(0.68,0.94,s))
                    front=max(0.0,min(1.0,(n.dot(head_front_axis)-0.12)/0.52))
                    blue=front
                    head_front_count+=int(front>0.5)
                    forehead_front_count+=int(0.1<=s<=0.65 and front>0.5)
                for center in eye_centers:
                    dist=(p-center).length
                    green=max(green,math.exp(-((dist-0.055)/0.04)**2)*0.85)
            else:
                eye_skin=0.0
                for center in eye_centers:
                    dist=(p-center).length
                    eye_skin=max(eye_skin,math.exp(-((dist-0.06)/0.035)**2)*0.7)
                green=max(green,eye_skin)
            if kind=="other":
                trunk_weight=sum(w.get(name,0.0) for name in ("Pelvis","Spine","Chest"))
                neck_weight=w.get("Neck",0.0)
                dorsal=math.exp(-((p.y/max(0.02,0.035*height_factor))**2))*smoothstep(0.18,0.55,n.z)*max(trunk_weight,neck_weight)
                blue=max(blue,dorsal)
                under=(1.0-smoothstep(0.72*height_factor,1.15*height_factor,p.z))*(max(0.0,-n.z)**0.7)*trunk_weight
                alpha=max(alpha,under)
            color.data[li].color=(max(0,min(1,red)),max(0,min(1,green)),max(0,min(1,blue)),max(0,min(1,alpha)))
    mesh.uv_layers.active=mesh.uv_layers[0]
    return {"vertex_count":len(mesh.vertices),"head_poll_loops":head_poll_count,"head_muzzle_loops":head_muzzle_count,"head_front_loops":head_front_count,"forehead_front_loops":forehead_front_count,"leg_loops":leg_loop_counts,"sentinel_loops":sentinel_loops,"head_half_width_m":round(head_width,5)}


def prepare_body(ob: bpy.types.Object, rig: bpy.types.Object, profile: str="adult") -> dict:
    for p in ob.data.polygons:
        p.use_smooth=True
    ob.data.update()
    return body_attributes(ob,rig,profile)


def trim_weights_to_four(ob: bpy.types.Object, bone_names: list[str]) -> None:
    groups_by_idx={g.index:g.name for g in ob.vertex_groups}
    values=[]
    for v in ob.data.vertices:
        items=sorted(((groups_by_idx[g.group],g.weight) for g in v.groups if g.group in groups_by_idx and g.weight>1e-6),key=lambda x:x[1],reverse=True)[:4]
        total=sum(w for _,w in items)
        if not items or total<=1e-12:
            values.append({"Root":1.0})
        else:
            values.append({n:w/total for n,w in items})
    ob.vertex_groups.clear()
    new_groups={name:ob.vertex_groups.new(name=name) for name in bone_names}
    for i,weights in enumerate(values):
        for name,value in weights.items():
            new_groups[name].add([i],value,"REPLACE")


def evaluated_export_body(body: bpy.types.Object, hooves: list[bpy.types.Object], rig: bpy.types.Object, geometry_collection: bpy.types.Collection, profile: str) -> tuple[bpy.types.Object,dict,dict]:
    export_body=body.copy();export_body.data=body.data.copy();export_body.name="Body_Export"
    body.data.name="Body_Editable"
    export_body.data.name="Body"
    geometry_collection.objects.link(export_body)
    for mod in list(export_body.modifiers):
        if mod.type=="ARMATURE":
            export_body.modifiers.remove(mod)
    sub=next((m for m in export_body.modifiers if m.type=="SUBSURF"),None)
    if sub:
        bpy.ops.object.select_all(action="DESELECT");export_body.select_set(True);bpy.context.view_layer.objects.active=export_body
        bpy.ops.object.modifier_apply(modifier=sub.name)
    trim_weights_to_four(export_body,[b.name for b in rig.data.bones])
    join_meshes(export_body,hooves)
    export_body.name="Body"
    normalize_group_names(export_body,rig)
    trim_weights_to_four(export_body,[b.name for b in rig.data.bones])
    export_mask_stats=prepare_body(export_body,rig,profile)
    quality=mesh_quality([export_body])
    if any(any(v for v in counts.values()) for counts in quality.values()):raise RuntimeError(f"Export Body has non-clean topology: {quality}")
    arm=export_body.modifiers.new("Skeleton_Deform","ARMATURE");arm.object=rig;arm.use_vertex_groups=True
    export_body.parent=rig
    return export_body,{"vertices":len(export_body.data.vertices),"faces":len(export_body.data.polygons),"triangles":sum(len(p.vertices)-2 for p in export_body.data.polygons),"quality":quality},{"evaluatedExportBody":export_mask_stats}


def glb_chunks(path: Path):
    raw=path.read_bytes()
    magic,version,total=struct.unpack_from("<4sII",raw,0)
    if magic!=b"glTF" or version!=2 or total!=len(raw):
        raise RuntimeError("Invalid GLB header/length")
    offset=12;doc=None;binary=b""
    while offset<len(raw):
        length,kind=struct.unpack_from("<I4s",raw,offset);offset+=8
        chunk=raw[offset:offset+length];offset+=length
        if kind==b"JSON":doc=json.loads(chunk.decode("utf-8").rstrip(" \t\n\r\0"))
        elif kind==b"BIN\0":binary=chunk
    if doc is None:raise RuntimeError("GLB has no JSON chunk")
    return doc,binary


def read_accessor(doc,binary,index):
    acc=doc["accessors"][index];view=doc["bufferViews"][acc["bufferView"]]
    dims={"SCALAR":1,"VEC2":2,"VEC3":3,"VEC4":4,"MAT4":16}[acc["type"]]
    comp={5120:("b",1,True),5121:("B",1,False),5122:("h",2,True),5123:("H",2,False),5125:("I",4,False),5126:("f",4,True)}[acc["componentType"]]
    code,size,signed=comp;stride=view.get("byteStride",size*dims);start=view.get("byteOffset",0)+acc.get("byteOffset",0)
    out=[]
    for i in range(acc["count"]):
        vals=list(struct.unpack_from("<"+code*dims,binary,start+i*stride))
        if acc.get("normalized") and acc["componentType"]!=5126:
            denom=(2**(size*8-1)-1) if signed else (2**(size*8)-1)
            vals=[max(-1.0,v/denom) if signed else v/denom for v in vals]
        out.append(vals)
    return out


def gltf_node_local(node: dict) -> Matrix:
    if "matrix" in node:
        values=node["matrix"]
        return Matrix(((values[0],values[4],values[8],values[12]),(values[1],values[5],values[9],values[13]),(values[2],values[6],values[10],values[14]),(values[3],values[7],values[11],values[15])))
    translation=Vector(node.get("translation",(0,0,0)))
    q=node.get("rotation",(0,0,0,1))
    rotation=Quaternion((q[3],q[0],q[1],q[2])).to_matrix().to_4x4()
    scale=node.get("scale",(1,1,1))
    scaling=Matrix(((scale[0],0,0,0),(0,scale[1],0,0),(0,0,scale[2],0),(0,0,0,1)))
    result=rotation @ scaling
    result.translation=translation
    return result


def gltf_node_world(doc: dict, target: int) -> Matrix:
    parents={}
    for i,node in enumerate(doc.get("nodes",[])):
        for child in node.get("children",[]):parents[child]=i
    chain=[];current=target
    while current in range(len(doc.get("nodes",[]))):
        chain.append(current)
        if current not in parents:break
        current=parents[current]
    world=Matrix.Identity(4)
    for i in reversed(chain):world=world @ gltf_node_local(doc["nodes"][i])
    return world


def gltf_matrix(values: list[float]) -> Matrix:
    return Matrix(((values[0],values[4],values[8],values[12]),(values[1],values[5],values[9],values[13]),(values[2],values[6],values[10],values[14]),(values[3],values[7],values[11],values[15])))


def inspect_glb(path: Path, blender_report: dict, expected_durations: dict[str, float] | None = None) -> dict:
    doc,bin_data=glb_chunks(path)
    mesh_names={m.get("name") for m in doc.get("meshes",[])}
    if mesh_names!=EXPECTED_MESHES:
        raise RuntimeError(f"Expected exact mesh names {sorted(EXPECTED_MESHES)}, got {sorted(mesh_names)}")
    node_names=[n.get("name","") for n in doc.get("nodes",[])]
    if "Skeleton" not in node_names:
        raise RuntimeError("GLB scene has no Skeleton node")
    animations = doc.get("animations", [])
    animation_report = []
    if animations:
        if {a.get("name") for a in animations} != {"idle", "walk", "trot"}:
            raise RuntimeError(f"Expected only named idle/walk/trot clips, got {[a.get('name') for a in animations]}")
        node_names_by_index = [n.get("name", "") for n in doc.get("nodes", [])]
        for animation in animations:
            name = animation.get("name", "")
            sampler_reports = []
            sampler_ranges = {}
            for sampler in animation.get("samplers", []):
                times = read_accessor(doc, bin_data, sampler["input"])
                values = read_accessor(doc, bin_data, sampler["output"])
                if not times or not values or any(not math.isfinite(x) for row in times + values for x in row):
                    raise RuntimeError(f"Animation {name} contains empty/non-finite samples")
                start_time = times[0][0]; end_time = times[-1][0]
                delta = max(abs(a-b) for a,b in zip(values[0], values[-1])) if len(values[0]) == len(values[-1]) else float("inf")
                if delta > 0.002:
                    # Quaternion signs may differ while representing the same rotation.
                    if len(values[0]) == 4 and max(abs(a+b) for a,b in zip(values[0], values[-1])) <= 0.002:
                        delta = 0.0
                    else:
                        raise RuntimeError(f"Animation {name} seam differs by {delta:.5f}")
                sampler_ranges[len(sampler_reports)] = max((max(row[i] for row in values)-min(row[i] for row in values) for i in range(len(values[0]))),default=0.0)
                sampler_reports.append({"start_s":round(start_time,6),"end_s":round(end_time,6),"samples":len(times),"seamMaxComponentError":round(delta,7)})
            if not animation.get("channels") or not sampler_reports:
                raise RuntimeError(f"Animation {name} has no channels/samples")
            targets = [channel.get("target", {}) for channel in animation["channels"]]
            if any(target.get("node") is not None and node_names_by_index[target["node"]] == "Skeleton" for target in targets):
                raise RuntimeError(f"Animation {name} translates/rotates the whole Skeleton root")
            animated_nodes = sorted({node_names_by_index[target["node"]] for target in targets if target.get("node") is not None})
            changing_nodes = sorted({node_names_by_index[channel["target"]["node"]] for channel in animation["channels"]
                                     if channel["target"].get("node") is not None and sampler_ranges.get(channel["sampler"],0.0)>1e-4})
            start_time = min((sampler["start_s"] for sampler in sampler_reports), default=0.0)
            end_time = max((sampler["end_s"] for sampler in sampler_reports), default=0.0)
            if abs(start_time) > 0.001:
                raise RuntimeError(f"Animation {name} must start at zero, got {start_time:.6f} s")
            if expected_durations and name in expected_durations and abs((end_time-start_time)-expected_durations[name]) > 0.002:
                raise RuntimeError(f"Animation {name} duration mismatch: exported {end_time-start_time:.6f}, expected {expected_durations[name]:.6f} s")
            needed = {"Head", "Hind_L_Shin", "Hind_R_Shin", "Front_L_Cannon", "Front_R_Cannon"}
            if not needed.issubset(animated_nodes):
                raise RuntimeError(f"Animation {name} is missing authored joint channels: {sorted(needed-set(animated_nodes))}")
            moving_required = {"Head", "Hind_L_Shin", "Hind_R_Shin"}
            if name in {"walk","trot"}:
                moving_required|={"Front_L_Cannon","Front_R_Cannon"}
            if not moving_required.issubset(changing_nodes):
                raise RuntimeError(f"Animation {name} has static/incomplete gait channels: {sorted(moving_required-set(changing_nodes))}")
            animation_report.append({"name":name,"channels":len(animation["channels"]),"animatedNodes":animated_nodes,"changingNodes":changing_nodes,"start_s":round(start_time,6),"end_s":round(end_time,6),"duration_s":round(end_time-start_time,6),"samplers":sampler_reports})
    elif path.stat().st_size:
        raise RuntimeError("Expected authored idle/walk/trot animation clips in GLB")
    total_triangles=0;primitives={};attribute_report={};weight_sum_error=0.0;bad_normals=0;nonfinite=0;bad_categories=0;skin_weights_by_mesh={}
    for mesh in doc["meshes"]:
        name=mesh["name"];tri_count=0;attrs=[]
        for prim in mesh.get("primitives",[]):
            attr=prim["attributes"];attrs.append(sorted(attr))
            pos=read_accessor(doc,bin_data,attr["POSITION"]);norm=read_accessor(doc,bin_data,attr["NORMAL"])
            if any(not math.isfinite(x) for row in pos+norm for x in row):nonfinite+=1
            for n in norm:
                length=math.sqrt(sum(x*x for x in n))
                if not 0.97<=length<=1.03:bad_normals+=1
            indices=read_accessor(doc,bin_data,prim["indices"]) if "indices" in prim else [[i] for i in range(len(pos))]
            if prim.get("mode",4)==4:
                tri_count+=len(indices)//3
                if name=="Body":
                    uv2=read_accessor(doc,bin_data,attr["TEXCOORD_1"])
                    for i in range(0,len(indices)-2,3):
                        ids=[row[0] for row in indices[i:i+3]]
                        cat=[]
                        for vi in ids:
                            x=uv2[vi][0]
                            cat.append("head" if -0.5<x<1.6 else "leg" if 9.5<x<14 else "other")
                        if len(set(cat))>1:bad_categories+=1
            if name=="Body":
                req={"POSITION","NORMAL","TEXCOORD_0","TEXCOORD_1","COLOR_0","JOINTS_0","WEIGHTS_0"}
                if not req.issubset(attr):raise RuntimeError(f"Body missing glTF attributes: {sorted(req-set(attr))}")
                colors=read_accessor(doc,bin_data,attr["COLOR_0"])
                if any(not math.isfinite(x) or x < -1e-6 or x > 1.00001 for row in colors for x in row):
                    raise RuntimeError("Body COLOR_0 has non-finite/out-of-range values")
                uv1=read_accessor(doc,bin_data,attr["TEXCOORD_1"])
                if any(not math.isfinite(x) for row in uv1 for x in row):raise RuntimeError("Body UV1 has non-finite values")
                primitive_stats={"vertices":len(pos),"triangles":tri_count,"color_0":len(colors),"texcoord_1":len(uv1),"weights_0":len(pos)}
            else:primitive_stats={"triangles":tri_count}
            if name in EXPECTED_MESHES:
                if not {"JOINTS_0","WEIGHTS_0"}.issubset(attr):raise RuntimeError(f"{name} is missing skin weights")
                if "JOINTS_1" in attr or "WEIGHTS_1" in attr:raise RuntimeError(f"{name} exceeds the four-influence skin contract")
                weights=read_accessor(doc,bin_data,attr["WEIGHTS_0"])
                mesh_error=max((abs(sum(w)-1.0) for w in weights),default=0.0)
                weight_sum_error=max(weight_sum_error,mesh_error)
                skin_weights_by_mesh[name]={"vertices":len(weights),"max_sum_error":mesh_error}
        primitives[name]=primitive_stats;attribute_report[name]=attrs
        total_triangles+=tri_count
    if nonfinite or bad_normals:raise RuntimeError(f"Invalid positions/normals ({nonfinite} nonfinite values, {bad_normals} non-unit normals)")
    if weight_sum_error>1e-4:raise RuntimeError(f"GLB skin weights not normalized: {weight_sum_error}")
    if bad_categories:raise RuntimeError(f"{bad_categories} Body triangles interpolate between canonical regions/sentinel")
    body_mesh_index=next(i for i,m in enumerate(doc["meshes"]) if m.get("name")=="Body")
    body_nodes=[(i,n) for i,n in enumerate(doc["nodes"]) if n.get("mesh")==body_mesh_index]
    if len(body_nodes)!=1:raise RuntimeError(f"Expected one Body mesh node, found {len(body_nodes)}")
    body_node_index,body_node=body_nodes[0]
    skin=doc["skins"][body_node["skin"]]
    joint_nodes=skin["joints"]
    inverse_bind=read_accessor(doc,bin_data,skin["inverseBindMatrices"])
    joint_matrices=[gltf_node_world(doc,node) @ gltf_matrix(values) for node,values in zip(joint_nodes,inverse_bind)]
    poll_points=[];muzzle_points=[]
    for prim in doc["meshes"][body_mesh_index].get("primitives",[]):
        attrs=prim["attributes"]
        if "TEXCOORD_1" not in attrs:continue
        positions=read_accessor(doc,bin_data,attrs["POSITION"]);canonical=read_accessor(doc,bin_data,attrs["TEXCOORD_1"])
        joints=read_accessor(doc,bin_data,attrs["JOINTS_0"]);weights=read_accessor(doc,bin_data,attrs["WEIGHTS_0"])
        for p,uv,jids,wts in zip(positions,canonical,joints,weights):
            if -0.5<=uv[0]<=1.6:
                local=Vector((*p,1.0));point=Vector((0,0,0))
                for jid,weight in zip(jids,wts):
                    if weight>1e-6:point+=(joint_matrices[int(jid)] @ local).xyz*weight
                if uv[0]<0.12:poll_points.append(point)
                if uv[0]>0.82:muzzle_points.append(point)
    if not poll_points or not muzzle_points:raise RuntimeError("GLB head canonical UV region is empty at poll or muzzle")
    poll_center=sum(poll_points,Vector())/len(poll_points);muzzle_center=sum(muzzle_points,Vector())/len(muzzle_points)
    forward=(muzzle_center-poll_center).normalized()
    horizontal_len=math.hypot(forward.x,forward.z)
    horizontal_forward=(forward.x/horizontal_len,forward.z/horizontal_len) if horizontal_len>1e-8 else (0.0,0.0)
    if horizontal_forward[0]<0.9:raise RuntimeError(f"Exported GLB muzzle does not face horizontal +X: direction={tuple(round(v,5) for v in forward)}")
    return {"mesh_names":sorted(mesh_names),"node_names":node_names,"animations":animation_report,"triangles":total_triangles,"primitives":primitives,"attributes":attribute_report,"normal_unit_failures":bad_normals,"nonfinite_values":nonfinite,"max_skin_weight_sum_error":weight_sum_error,"skin_weights_by_mesh":skin_weights_by_mesh,"mixed_canonical_category_triangles":bad_categories,"body_has_descriptor_attributes":True,"body_has_skin":True,"exported_head_direction_muzzle_minus_poll":[round(v,5) for v in forward],"exported_horizontal_forward_XZ":[round(v,5) for v in horizontal_forward],"exported_poll_center_m":[round(v,5) for v in poll_center],"exported_muzzle_center_m":[round(v,5) for v in muzzle_center]}


def model_bounds(objects: list[bpy.types.Object]) -> dict:
    points=[]
    for ob in objects:
        points.extend(ob.matrix_world @ Vector(corner) for corner in ob.bound_box)
    lo=[min(p[i] for p in points) for i in range(3)]
    hi=[max(p[i] for p in points) for i in range(3)]
    return {"min_m":[round(x,5) for x in lo],"max_m":[round(x,5) for x in hi],"size_m":[round(hi[i]-lo[i],5) for i in range(3)]}


def mesh_quality(objects: list[bpy.types.Object]) -> dict:
    report={}
    for ob in objects:
        bm=bmesh.new();bm.from_mesh(ob.data)
        report[ob.name]={"nonmanifold_edges":sum(not e.is_manifold for e in bm.edges),"zero_area_faces":sum(f.calc_area()<1e-10 for f in bm.faces),"loose_vertices":sum(not v.link_edges for v in bm.verts)}
        bm.free()
    return report


def source_sha(path: Path) -> str:
    h=hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024),b""):h.update(chunk)
    return h.hexdigest()


def build_profile(profile: str, repo: Path, source: Path, out_dir: Path, blend_dir: Path) -> None:
    started=time.time()
    if not source.is_file():raise FileNotFoundError(f"Accepted horse source not found: {source}")
    out_dir.mkdir(parents=True,exist_ok=True);blend_dir.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
    rig=bpy.data.objects.get("Horse_Rig")
    if rig is None or rig.type!="ARMATURE":raise RuntimeError("Accepted reference has no Horse_Rig armature")
    body,hooves,eyes,mane,tail=rename_source_objects(rig)
    source_bounds=model_bounds([body,*hooves,*eyes,mane,tail])
    set_root_and_axis(rig)
    gait_rig_report=gait.prepare_gait_rig(rig)
    deformation_report={"profile":"adult","withers_m":1.6,"regionalScales":{"barrel_axial":1.0,"barrel_lateral":1.0,"barrel_vertical":1.0,"legs_vertical":1.0,"head_relative":1.0}}
    if profile=="foal":
        deformation_report=apply_foal_deformation(rig,[body,*hooves,*eyes,mane,tail])
        deformation_report["hairShortening"]={"mane":shorten_mane_from_crest(mane,body,rig),"tail":shorten_tail_with_rig(tail,rig,0.30)}
    gait.reset_ground_root(rig)
    body.name="Body"
    body.data.name="Body"
    normalize_group_names(body,rig)
    weight_repair_report=gait.repair_body_weights(body,rig,profile)
    for poly in body.data.polygons:poly.use_smooth=True
    eyes_ob=join_eyes(eyes,create_neutral_material("Eye_Descriptor_Neutral"))
    eyes_ob.data.name="Eyes"
    mane.data.name="Mane"
    tail.data.name="Tail"
    map_hair_uv(mane,"mane",body);map_hair_uv(tail,"tail",body)
    head_bone=rig.data.bones["Head"]
    head_direction=rig.matrix_world.to_3x3() @ (head_bone.tail_local-head_bone.head_local)
    if head_direction.x <= 0:
        raise RuntimeError(f"Horse head does not face Blender +X after conversion: {tuple(head_direction)}")
    coat_normal=None
    normal_path=source.parent/"textures"/"Horse_Coat_Normal.png"
    if normal_path.is_file():
        coat_normal=bpy.data.images.load(str(normal_path),check_existing=True);coat_normal.pack()
    coat_mat=create_neutral_material("Coat_Descriptor_Neutral",coat_normal)
    hair_mat=create_neutral_material("Hair_Descriptor_Neutral")
    assign_material(body,coat_mat)
    for hoof in hooves:assign_material(hoof,coat_mat)
    assign_material(mane,hair_mat);assign_material(tail,hair_mat)
    # Remove all fixed source coat/hair/eye images from active export materials;
    # only the neutral PBR normal detail map is retained on Body.
    geometry_collection=bpy.data.collections.get("Horse_Geometry")
    rig_collection=bpy.data.collections.get("Horse_Rig")
    if geometry_collection is None or rig_collection is None:raise RuntimeError("Expected model collections are missing")
    for ob in [body,*hooves,eyes_ob,mane,tail]:
        if ob.parent!=rig:raise RuntimeError(f"{ob.name} lost its rig parent")
        for mod in list(ob.modifiers):
            if mod.type=="ARMATURE":mod.object=rig;mod.use_vertex_groups=True
        normalize_group_names(ob,rig)
    # Recompute complete body masks after joining, keeping the source quad cage editable.
    body_mask_stats=prepare_body(body,rig,profile)
    if body_mask_stats["forehead_front_loops"]<=0:raise RuntimeError("No forehead-facing head vertices were classified")
    editable_quality=mesh_quality([body,*hooves,eyes_ob,mane,tail])
    if any(any(v for v in counts.values()) for counts in editable_quality.values()):raise RuntimeError(f"Editable source geometry has non-clean topology: {editable_quality}")
    # Retain only images needed by the neutral normal material in the adapted file.
    used_images={node.image for mat in [coat_mat,hair_mat,eyes_ob.data.materials[0]] if mat and mat.use_nodes for node in mat.node_tree.nodes if node.type=="TEX_IMAGE" and node.image}
    for image in list(bpy.data.images):
        if image not in used_images:bpy.data.images.remove(image)
    for mat in list(bpy.data.materials):
        if mat not in {coat_mat,hair_mat,eyes_ob.data.materials[0]}:bpy.data.materials.remove(mat)
    bpy.context.scene.unit_settings.system="METRIC";bpy.context.scene.unit_settings.scale_length=1.0
    animation_metadata=gait.author_actions(rig,profile,hooves)
    # The armature's object origin is the shared ground root. Keep the editable scene free of cameras/lights.
    for obj in list(bpy.data.objects):
        if obj.type in {"CAMERA","LIGHT"}:bpy.data.objects.remove(obj,do_unlink=True)
    body_path=blend_dir/f"horse_{profile}.blend"
    glb_path=out_dir/f"horse_{profile}.glb"
    manifest_path=out_dir/f"horse_{profile}.manifest.json"
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(body_path))
    bounds=model_bounds([body,*hooves,eyes_ob,mane,tail])

    export_body,export_geo,export_mask_stats=evaluated_export_body(body,hooves,rig,geometry_collection,profile)
    # Export exactly the four requested mesh nodes and the single rig. As the rig
    # is selected, unlink the editable low-res Body during export so hierarchy
    # traversal cannot include a duplicate body beside the evaluated mesh.
    original_name=body.name
    geometry_collection.objects.unlink(body)
    body.name="Body_Editable"
    export_body.name="Body"
    bpy.ops.object.select_all(action="DESELECT")
    for ob in (rig,export_body,eyes_ob,mane,tail):ob.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.gltf(filepath=str(glb_path),export_format="GLB",use_selection=True,export_yup=True,export_apply=False,export_texcoords=True,export_normals=True,export_skins=True,export_vertex_color="ACTIVE",export_all_vertex_colors=True,export_animations=True,export_animation_mode="ACTIONS",export_nla_strips=False,export_frame_range=False,export_frame_step=1,export_force_sampling=True,export_anim_single_armature=True,export_optimize_animation_size=False,export_cameras=False,export_lights=False,export_materials="EXPORT",export_image_format="AUTO")
    geometry_collection.objects.link(body)
    body.name=original_name
    bpy.data.objects.remove(export_body,do_unlink=True)
    expected_durations={"idle":3.0,"walk":gait.PROFILES[profile]["walk_duration"],"trot":gait.PROFILES[profile]["trot_duration"]}
    glb_report=inspect_glb(glb_path,{},expected_durations)
    if glb_report["mesh_names"]!=sorted(EXPECTED_MESHES):raise RuntimeError("Mesh node names do not match contract")
    if "Skeleton" not in glb_report["node_names"]:raise RuntimeError("Missing Skeleton scene node after export")
    if {clip["name"] for clip in glb_report["animations"]}!={"idle","walk","trot"}:raise RuntimeError("GLB does not contain exactly idle/walk/trot")
    if not glb_report["body_has_descriptor_attributes"] or glb_report["mixed_canonical_category_triangles"]:raise RuntimeError("GLB canonical attribute validation failed")

    centroids={}
    for ob in (eyes_ob,):
        for side in ("L","R"):
            # The final world Y axis is anatomical left/right after the +90 degree rig turn.
            verts=[]
            for v in ob.data.vertices:
                p=ob.matrix_world@v.co
                if (side=="L" and p.y>0) or (side=="R" and p.y<0):verts.append(p)
            if verts:centroids[side]=[round(sum(p[i] for p in verts)/len(verts),5) for i in range(3)]
    if centroids.get("L",[0])[1]<=0 or centroids.get("R",[0])[1]>=0:
        raise RuntimeError(f"Eye naming is anatomically reversed: {centroids}")
    bones=[{"name":b.name,"parent":b.parent.name if b.parent else None,"head_m":[round(x,5) for x in (rig.matrix_world@b.head_local)],"tail_m":[round(x,5) for x in (rig.matrix_world@b.tail_local)]} for b in rig.data.bones]
    withers_height=round(rig.data.bones["Chest"].tail_local.z,5)
    if profile=="foal" and not 1.05<=withers_height<=1.15:raise RuntimeError(f"Foal withers must be 1.05-1.15 m, got {withers_height}")
    if bounds["min_m"][2] < -0.02 or bounds["min_m"][2] > 0.04:
        raise RuntimeError(f"Ground-rooted model bounds are unexpected: {bounds}")
    manifest={
        "assetVersion":f"{ASSET_VERSION}.{profile}","profile":profile,"status":"CC0 authored reference adapted for Cloverbrook procedural presentation",
        "source":{"author":"Lyndon Daniels","submission":"Realtime Rancher's 3D Model Pack; Rigged Horse submitted by ChadM","license":"CC0 1.0 Universal","sourceUrl":"https://opengameart.org/content/rigged-horse","sourceBlend":"art/reference/Horse_Reference/Horse_Reference.blend","sha256":source_sha(source)},
        "units":"1 Blender unit = 1 metre; Blender Z up and +X forward; glTF +Y up and +X forward; ground root at origin; anatomical left Blender +Y / glTF -Z",
        "meshNodes":sorted(EXPECTED_MESHES),"rigNode":"Skeleton","animations":animation_metadata,
        "animationReferenceNotes":["Walk and trot bone trajectories are retargeted from evaluated CC0 clips of Horse Rigged All Gaits by fdoss001 (original mesh credit Tarnyloo), scaled to the target rig and grounded against its hoof meshes.","Walk footfalls follow the four-beat lateral sequence HL, FL, HR, FR described by Kentucky Equine Extension and UGA Extension.","Trot footfalls use diagonal pairs FL+HR and FR+HL as described by Kentucky Equine Extension; idle is separately authored because the source package has no idle clip."],
        "animationReferences":["https://horses.extension.org/horse-walk/","https://horses.extension.org/horse-trot/","https://extension.uga.edu/publications/detail.html?number=B1401&title=evaluating-common-equine-performance-classes"],
        "gaitRigAdaptation":{"newHindShinBones":gait_rig_report,"bodyWeightRepair":weight_repair_report},
        "triangles":glb_report["primitives"],"totalTriangles":glb_report["triangles"],"bones":[b["name"] for b in bones],
        "attributes":{"Body.COLOR_0":"linear float vec4: r points (lower limbs/ear tips), g soft muzzle and eye skin, b trunk/neck dorsal line and front-facing head, a underside countershade","Body.TEXCOORD_0":"retained source UV detail coordinates; glTF V is flipped by export","Body.TEXCOORD_1":"head=(s=0 poll to 1 muzzle, t lateral -1..1); legs=(10+FL0/FR1/HL2/HR3, h ground0 to knee/hock1, hoof<0.165); other=(-10,50), authored in Blender UV space","Eyes.TEXCOORD_0":"U 0 centre-facing pole to 1 outward pole; V vertical 0 bottom to 1 top (glTF V flip)","Mane/Tail.TEXCOORD_0":"U lateral across clump; V root0 to tips1 in Blender UV space (glTF V flip); parent shader should read 1-UV.y"},
        "bodyMaskCoverage":body_mask_stats,"exportMaskCoverage":export_mask_stats,"geometryQuality":{"editable":editable_quality,"export":export_geo["quality"]},"eyeCentroids_m":centroids,"bounds":bounds,"bonesRestWorld":bones,
        "foalDeformation":deformation_report,"withersHeight_m":withers_height,
        "ratiosToAdult":{"length":round(bounds["size_m"][0]/max(source_bounds["size_m"][1],1e-8),5),"width":round(bounds["size_m"][1]/max(source_bounds["size_m"][0],1e-8),5),"height":round(bounds["size_m"][2]/max(source_bounds["size_m"][2],1e-8),5)},
        "recommendedHeadViewTarget":[round((glb_report["exported_poll_center_m"][i]+glb_report["exported_muzzle_center_m"][i])*0.5,5) for i in range(3)],
        "recommendedHeadViewTarget_m":[round((glb_report["exported_poll_center_m"][i]+glb_report["exported_muzzle_center_m"][i])*0.5,5) for i in range(3)],
        "glbValidation":glb_report,"sourceGeometry":{"bodyControlVerts":len(body.data.vertices),"bodyControlFaces":len(body.data.polygons),"exportBody":export_geo},
        "normalDetailTexture":"Horse_Coat_Normal.png retained in the neutral PBR Body material; original fixed coat color texture is not exported",
        "hairGeometry":"solid authored sculpted clump meshes; retain opaque/solid-clump hair shading rather than card alpha clipping",
        "limitations":[
            ("Adult retains the authored source proportions and topology; it is an adaptation, not a new anatomical sculpt." if profile=="adult" else "Foal adapts the source authored anatomy and topology with intentionally changed juvenile proportions; it is not a new anatomical sculpt."),
            "The gait clips are authored in-place reference loops with baked IK-like FK bone motion, not a complete production locomotion/controller rig; no runtime IK constraints are required.",
            "The existing hair meshes are dense sculpted clumps rather than cards; UVs are projected across width/root-to-tip and the presentation shader must avoid card-shaped alpha clipping."
        ],
        "buildSeconds":round(time.time()-started,2),
    }
    manifest_path.write_text(json.dumps(manifest,indent=2))
    print(json.dumps({"profile":profile,"glb":str(glb_path),"blend":str(body_path),"manifest":str(manifest_path),"validation":glb_report,"bodyMaskCoverage":body_mask_stats,"eyes":centroids},indent=2))


if __name__=="__main__":
    repo,source,out_dir,blend_dir,profiles=paths()
    for profile in profiles:
        build_profile(profile,repo,source,out_dir,blend_dir)
