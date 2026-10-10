"""Build the TEMPORARY pipeline horse (adult + foal) as rigged, animated glTF.

    python3 tools/horse/build_horse.py --out game/assets/horse/placeholder [--profiles adult foal]

Requires: numpy, scikit-image and the `bpy` wheel (Blender as a Python module; version
pinned in docs/visual/TOOLCHAIN.md).  No GUI, no manual steps.

Output per profile: horse_<profile>.glb containing
  Body (skinned; COLOR_0 = coat masks, TEXCOORD_1 = canonical marking coords),
  Eyes, Mane, Tail (skinned hair cards), Skeleton armature and animations
  idle, breathe, look, walk, trot.
plus horse_<profile>.manifest.json (counts, bone map, attribute conventions).

THIS IS A PLACEHOLDER for pipeline validation (D-002).  It is deliberately labelled
"placeholder" everywhere and must be replaced by a licensed/commissioned horse.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import anatomy  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Quaternion, Vector  # noqa: E402

ASSET_VERSION = "placeholder-horse.v1"
TARGET_BODY_TRIS = {"adult": 36000, "foal": 26000}
RNG = np.random.default_rng(20261010)  # fixed: the build is reproducible


# --------------------------------------------------------------------------------------
# Skeleton
# --------------------------------------------------------------------------------------

def bone_layout(h: anatomy.Horse):
    L = h.landmarks; T = h.T
    poll, u, nrm, Lh = h.head_frame
    bones = {}  # name: (head, tail, parent)
    root = np.array([0.0, 0.0, 0.0])
    bones["root"] = (root, root + [0, 0, 0.25 * h.p.s_leg], None)
    hips = T(-0.15, 1.30)
    bones["hips"] = (hips, T(-0.70, 1.36), "root")
    bones["spine"] = (hips, T(0.25, 1.32), "root")
    bones["chest"] = (T(0.25, 1.32), T(0.62, 1.36), "spine")
    nb, nc, ne = L["neck_base"], L["neck_ctrl"], poll
    mid = 0.25 * nb + 0.5 * nc + 0.25 * ne
    bones["neck1"] = (T(0.62, 1.36), mid, "chest")
    bones["neck2"] = (mid, ne, "neck1")
    bones["head"] = (ne, ne + u * Lh * 0.95, "neck2")
    for tag in ("ear_L", "ear_R"):
        bones[tag] = (L[tag + "_base"], L[tag + "_tip"], "head")
    tr = L["tail_root"]
    tail_dir = np.array([-0.32, 0, -1.0]); tail_dir /= np.linalg.norm(tail_dir)
    seg = h.p.tail_length / 4
    prev = "hips"; start = tr
    for i in range(1, 5):
        end = start + tail_dir * seg * (0.6 if i == 1 else 1.0)
        bones[f"tail{i}"] = (start, end, prev); prev = f"tail{i}"; start = end
    for leg, (front, side) in anatomy.LEGS.items():
        top, knee, fet, cor, toe = (L[k + "_" + leg] for k in ("legtop", "knee", "fetlock", "coronet", "hoof"))
        y = top[1]
        if front:
            sh = T(0.50, 1.47); sh[1] = y * 0.6
            pt = T(0.80, 1.18); pt[1] = y
            bones["scapula_" + leg] = (sh, pt, "chest")
            bones["upper_" + leg] = (pt, top, "scapula_" + leg)
        else:
            hp = T(-0.55, 1.40); hp[1] = y * 0.6
            bones["upper_" + leg] = (hp, top, "hips")
        bones["lower_" + leg] = (top, knee, "upper_" + leg)
        bones["cannon_" + leg] = (knee, fet, "lower_" + leg)
        bones["pastern_" + leg] = (fet, cor, "cannon_" + leg)
        bones["hoof_" + leg] = (cor, toe, "pastern_" + leg)
    return bones


REGION_BONES = {
    "trunk": ["hips", "spine", "chest"],
    "neck": ["chest", "neck1", "neck2"],
    "head": ["head", "neck2"],
    "ear_L": ["ear_L"], "ear_R": ["ear_R"],
    "tail": ["tail1", "hips"],
}
for _leg, (_front, _s) in anatomy.LEGS.items():
    REGION_BONES["leg_" + _leg] = ([("scapula_" + _leg)] if _front else ["hips"]) + [
        b + "_" + _leg for b in ("upper", "lower", "cannon", "pastern", "hoof")]


def seg_dist(p, a, b):
    ab = b - a; t = np.clip(((p - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(p - (a + np.outer(t, ab)), axis=1)


def skin_weights(verts, regions, bones, power=4.0):
    names = list(bones)
    idx = {n: i for i, n in enumerate(names)}
    W = np.zeros((len(verts), len(names)))
    for reg in np.unique(regions):
        m = regions == reg
        allowed = REGION_BONES.get(reg, ["root"])
        for b in allowed:
            a, t, _ = bones[b]
            d = seg_dist(verts[m], np.asarray(a), np.asarray(t))
            W[m, idx[b]] = 1.0 / (d ** power + 1e-7)
    top = np.argsort(-W, 1)[:, :4]
    out = np.zeros_like(W)
    rows = np.arange(len(verts))[:, None]
    out[rows, top] = W[rows, top]
    out /= out.sum(1, keepdims=True)
    return names, out


# --------------------------------------------------------------------------------------
# Hair cards
# --------------------------------------------------------------------------------------

def strip(points, widths, side_vec):
    """Card along a polyline; returns verts, faces, uvs (u across, v along root->tip)."""
    vs, uvs, fs = [], [], []
    n = len(points)
    for i, (p, w) in enumerate(zip(points, widths)):
        vs += [p - side_vec * w / 2, p + side_vec * w / 2]
        v = i / (n - 1); uvs += [(0.0, v), (1.0, v)]
    for i in range(n - 1):
        a = 2 * i; fs.append((a, a + 1, a + 3, a + 2))
    return vs, fs, uvs


def hug_surface(h, pts, clearance):
    """Push strand points out of the body so hair lies ON the neck instead of through it."""
    e = 0.002
    for _ in range(4):
        d = h.field(pts)
        g = np.stack([(h.field(pts + np.eye(3)[i] * e) - d) / e for i in range(3)], 1)
        g /= np.linalg.norm(g, axis=1, keepdims=True) + 1e-9
        pts = pts + g * np.maximum(0.0, clearance - d)[:, None]
    return pts


def mane_cards(h, body_verts):
    p = h.p
    nb, nc, ne = (h.landmarks[k] for k in ("neck_base", "neck_ctrl", "poll"))
    V, F, U, R = [], [], [], []  # verts, faces, uvs, region-for-weights
    def add(card, region):
        vs, fs, uvs = card; o = len(V)
        V.extend(vs); U.extend(uvs); F.extend([tuple(i + o for i in f) for f in fs]); R.extend([region] * len(vs))
    near_mid = np.abs(body_verts[:, 1]) < 0.02
    mid_pts = body_verts[near_mid]
    count = 46 if p.name == "adult" else 34
    for i in range(count):
        t = 0.04 + 0.94 * i / (count - 1)
        c = (1 - t) ** 2 * nb + 2 * (1 - t) * t * nc + t * t * ne
        # crest: highest midline surface point near this neck station
        d = 2 * (1 - t) * (nc - nb) + 2 * t * (ne - nc); d /= np.linalg.norm(d)
        near = mid_pts[np.abs((mid_pts - c) @ d) < 0.02]
        if len(near) == 0: continue
        crest = near[np.argmax(near[:, 2] - near[:, 0] * 0.0)]
        for layer in range(3):
            L = p.mane_length * (0.75 + 0.5 * RNG.random()) * (0.6 if layer == 2 else 1.0) * (0.85 + 0.3 * t)
            side = -1.0 if layer < 2 else 1.0  # mane falls to the off (right) side, a few hairs left
            out = np.array([0, side * 0.8, 0.0]); down = np.array([0.0, 0, -1.0])
            pts = []
            start = crest + np.array([0, 0, -0.004]) - d * 0.01 * layer
            for k in range(6):
                s = k / 5
                pts.append(start + L * (s * (0.45 * out + 0.35 * down) + s * s * (0.05 * out + 0.6 * down))
                           + d * 0.012 * (RNG.random() - 0.5))
            pts = hug_surface(h, np.array(pts), 0.006 + 0.004 * layer)
            w0 = (0.05 if p.name == "adult" else 0.035) * (1.0 - 0.2 * layer)
            widths = [w0 * (1.0 - 0.55 * k / 5) for k in range(6)]
            add(strip(pts, widths, d), "mane")
    # forelock
    poll, u, nrm, Lh = h.head_frame
    for j in range(7):
        start = poll + np.array([0, (j - 3) * 0.012, 0.01]) - nrm * 0.03
        L = p.mane_length * (0.7 + 0.3 * RNG.random())
        pts = np.array([start + L * (s / 5) * (u * 0.9 - nrm * 0.25) for s in range(6)])
        add(strip(pts, [0.035 * (1 - 0.5 * s / 5) for s in range(6)], np.array([0, 1.0, 0])), "forelock")
    return np.array(V), F, np.array(U), np.array(R)


def tail_cards(h, bones):
    p = h.p
    chain = [bones[f"tail{i}"] for i in range(1, 5)]
    axis_pts = [np.asarray(chain[0][0])] + [np.asarray(b[1]) for b in chain]
    V, F, U = [], [], []
    count = 64 if p.name == "adult" else 40
    for i in range(count):
        ang = 2 * math.pi * i / count + RNG.random() * 0.3
        spread = 0.035 + 0.05 * RNG.random()
        L = 0.75 + 0.25 * RNG.random()
        pts = []
        for k in range(8):
            s = k / 7 * L
            seg = min(int(s * 4), 3); f = s * 4 - seg
            a = axis_pts[seg] + (axis_pts[seg + 1] - axis_pts[seg]) * f
            off = np.array([math.cos(ang) * 0.35, math.sin(ang), 0]) * spread * (0.6 + 1.6 * s) * (p.s_body ** 0.5)
            pts.append(a + off)
        pts = np.array(pts)
        side = np.array([-math.sin(ang), math.cos(ang), 0])
        w0 = 0.05 if p.name == "adult" else 0.035
        vs, fs, uvs = strip(pts, [w0 * (1 - 0.4 * k / 7) for k in range(8)], side)
        o = len(V); V.extend(vs); U.extend(uvs); F.extend([tuple(j + o for j in f) for f in fs])
    return np.array(V), F, np.array(U)


# --------------------------------------------------------------------------------------
# Blender helpers
# --------------------------------------------------------------------------------------

def new_mesh_object(name, verts, faces, smooth=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    me.update()
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def set_uv(me, uvs, name):
    layer = me.uv_layers.new(name=name)
    loop_vi = np.empty(len(me.loops), np.int64); me.loops.foreach_get("vertex_index", loop_vi)
    layer.data.foreach_set("uv", np.asarray(uvs, np.float32)[loop_vi].ravel())
    return layer


def set_color(me, colors, name="Mask"):
    attr = me.color_attributes.new(name=name, type="FLOAT_COLOR", domain="POINT")
    attr.data.foreach_set("color", np.asarray(colors, np.float32).ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    return attr


def add_material(ob, name):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    ob.data.materials.append(mat)


def decimate(ob, target_tris):
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    mod = ob.modifiers.new("dec", "DECIMATE"); mod.ratio = min(1.0, target_tris / tris)
    deps = bpy.context.evaluated_depsgraph_get()
    new = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    ob.modifiers.clear(); old = ob.data; ob.data = new; bpy.data.meshes.remove(old)


def smooth_mesh(ob, iterations=6, factor=0.5):
    mod = ob.modifiers.new("sm", "SMOOTH"); mod.iterations = iterations; mod.factor = factor
    deps = bpy.context.evaluated_depsgraph_get()
    new = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    ob.modifiers.clear(); old = ob.data; ob.data = new; bpy.data.meshes.remove(old)


def mesh_arrays(me):
    v = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", v)
    n = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("normal", n)
    return v.reshape(-1, 3).astype(float), n.reshape(-1, 3).astype(float)


def bind(ob, arm, names, W):
    for n in names:
        ob.vertex_groups.new(name=n)
    for bi, n in enumerate(names):
        vg = ob.vertex_groups[n]
        nz = np.nonzero(W[:, bi] > 1e-4)[0]
        for vi in nz:
            vg.add([int(vi)], float(W[vi, bi]), "REPLACE")
    mod = ob.modifiers.new("Armature", "ARMATURE"); mod.object = arm
    ob.parent = arm


def build_armature(bones):
    arm_data = bpy.data.armatures.new("Skeleton")
    arm = bpy.data.objects.new("Skeleton", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (a, b, parent) in bones.items():
        eb = arm_data.edit_bones.new(name)
        eb.head = Vector(a); eb.tail = Vector(b)
        eb.roll = 0.0
        if parent:
            eb.parent = arm_data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


# --------------------------------------------------------------------------------------
# Animation (procedural, in-place).  Angles: degrees about the horse's lateral axis;
# positive values swing the distal end FORWARD.
# --------------------------------------------------------------------------------------

def world_axis_rot(arm, bone, axis, deg):
    rest = arm.data.bones[bone].matrix_local.to_3x3()
    q_world = Quaternion(Vector(axis), math.radians(deg))
    return (rest.inverted() @ q_world.to_matrix() @ rest).to_quaternion()


def key_pose(arm, frame, rots, root_z=0.0):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    for name, (axis, deg) in rots.items():
        pb = arm.pose.bones[name]
        pb.rotation_quaternion = world_axis_rot(arm, name, axis, deg)
        pb.keyframe_insert("rotation_quaternion", frame=frame)
    rb = arm.pose.bones["root"]
    rb.location = (0, root_z, 0)  # root bone y axis points up
    rb.keyframe_insert("location", frame=frame)


LAT = (0, -1, 0)   # rotation about -Y: positive degrees move a downward bone's end forward
UP = (0, 0, 1)


def gait_rots(phase_by_leg, swing_start, amp, flex, head_amp, t, body_roll=0.0):
    rots = {}
    for leg, ph in phase_by_leg.items():
        front = leg[0] == "F"
        x = (t + ph) % 1.0
        if x < swing_start:  # stance: hoof planted, limb sweeps back
            s = x / swing_start
            swing = amp * (1 - 2 * s)
            fl = 0.0
        else:
            s = (x - swing_start) / (1 - swing_start)
            swing = amp * (-1 + 2 * (0.5 - 0.5 * math.cos(math.pi * s)))
            fl = flex * math.sin(math.pi * s)
        if front:
            rots["upper_" + leg] = (LAT, swing * 0.5)
            rots["lower_" + leg] = (LAT, swing * 0.6)
            rots["cannon_" + leg] = (LAT, -fl * 1.3)
            rots["pastern_" + leg] = (LAT, -fl * 0.8)
        else:
            rots["upper_" + leg] = (LAT, swing * 0.7)
            rots["lower_" + leg] = (LAT, -fl * 0.6)
            rots["cannon_" + leg] = (LAT, fl * 0.9 + swing * 0.2)
            rots["pastern_" + leg] = (LAT, -fl * 0.7)
    rots["neck1"] = (LAT, head_amp * math.sin(2 * math.pi * 2 * t))
    rots["head"] = (LAT, -head_amp * 0.6 * math.sin(2 * math.pi * 2 * t))
    rots["tail1"] = (UP, 4 * math.sin(2 * math.pi * t))
    return rots


def make_action(arm, name, frames, fn):
    act = bpy.data.actions.new(name); act.use_fake_user = True
    arm.animation_data_create(); arm.animation_data.action = act
    for f in range(frames + 1):
        rots, rz = fn(f / frames)
        key_pose(arm, f + 1, rots, rz)
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    return act


def build_animations(arm, scale):
    fps = 30
    bpy.context.scene.render.fps = fps
    acts = []
    acts.append(make_action(arm, "idle", 120, lambda t: ({
        "chest": (LAT, 0.6 * math.sin(2 * math.pi * t)),
        "neck1": (LAT, -2.0 + 1.5 * math.sin(2 * math.pi * t)),
        "head": (LAT, 1.0 * math.sin(2 * math.pi * t + 1.0)),
        "ear_L": (UP, 8 * math.sin(2 * math.pi * t)), "ear_R": (UP, -6 * math.sin(2 * math.pi * 2 * t)),
        "tail1": (UP, 6 * math.sin(2 * math.pi * t)), "tail2": (UP, 5 * math.sin(2 * math.pi * t - 0.6)),
    }, 0.004 * scale * math.sin(2 * math.pi * t))))
    acts.append(make_action(arm, "breathe", 90, lambda t: ({
        "spine": (LAT, 0.8 * math.sin(2 * math.pi * t)), "chest": (LAT, -0.8 * math.sin(2 * math.pi * t)),
    }, 0.006 * scale * math.sin(2 * math.pi * t))))
    acts.append(make_action(arm, "look", 150, lambda t: ({
        "neck1": (UP, 14 * math.sin(2 * math.pi * t)), "neck2": (UP, 10 * math.sin(2 * math.pi * t)),
        "head": (LAT, 6 * math.sin(4 * math.pi * t)),
        "ear_L": (UP, 12 * math.sin(2 * math.pi * t)), "ear_R": (UP, 12 * math.sin(2 * math.pi * t)),
    }, 0.0)))
    walk_ph = {"HL": 0.0, "FL": 0.25, "HR": 0.5, "FR": 0.75}
    acts.append(make_action(arm, "walk", 36, lambda t: (gait_rots(walk_ph, 0.62, 16, 30, 3.0, t),
                                                           0.008 * scale * math.cos(4 * math.pi * t))))
    trot_ph = {"FL": 0.0, "HR": 0.0, "FR": 0.5, "HL": 0.5}
    acts.append(make_action(arm, "trot", 22, lambda t: (gait_rots(trot_ph, 0.45, 20, 55, 1.5, t),
                                                           0.03 * scale * abs(math.sin(2 * math.pi * t)))))
    arm.animation_data.action = None
    return acts


# --------------------------------------------------------------------------------------

def build(profile: anatomy.Profile, out_dir: str):
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    horse = anatomy.Horse(profile)
    verts, faces = horse.mesh()
    body = new_mesh_object("Body", verts, faces)
    smooth_mesh(body, 4, 0.5)
    decimate(body, TARGET_BODY_TRIS[profile.name])
    v, n = mesh_arrays(body.data)
    region, color, uv2 = horse.vertex_data(v, n)
    # UV0: side projection, left and right halves separated (placeholder layout)
    lo, hi = v.min(0), v.max(0)
    u = (v[:, 0] - lo[0]) / (hi[0] - lo[0]) * 0.49 + np.where(v[:, 1] < 0, 0.51, 0.0)
    w = (v[:, 2] - lo[2]) / (hi[2] - lo[2])
    set_uv(body.data, np.column_stack([u, w]), "UVMap")
    set_uv(body.data, uv2, "Canonical")
    set_color(body.data, color)
    add_material(body, "Coat")

    bones = bone_layout(horse)
    arm = build_armature(bones)
    names, W = skin_weights(v, region, bones)
    bind(body, arm, names, W)

    # Eyes (separate mesh so the eye material can be glossy and recoloured)
    eye_r = 0.0235 * profile.s_eye * (horse.head_frame[3] / 0.58) ** 0.4
    eye_objs = []
    for tag in ("eye_L", "eye_R"):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16, radius=eye_r, location=tuple(horse.landmarks[tag]))
        e = bpy.context.active_object; e.data.shade_smooth()
        # local frame: +X of the eye mesh points outward so the iris can be drawn from object coords
        eye_objs.append(e)
    for e in eye_objs: e.select_set(True)
    bpy.context.view_layer.objects.active = eye_objs[0]
    bpy.ops.object.join(); eyes = bpy.context.active_object; eyes.name = "Eyes"
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ev, _ = mesh_arrays(eyes.data)
    # eye UV: u = lateral outwardness so the shader can place the iris/pupil without textures
    cen = np.where(ev[:, 1:2] > 0, horse.landmarks["eye_L"], horse.landmarks["eye_R"])
    rel = (ev - cen) / eye_r
    set_uv(eyes.data, np.column_stack([np.abs(rel[:, 1]), rel[:, 2] * 0.5 + 0.5]), "UVMap")
    add_material(eyes, "Eye")
    ew = np.zeros((len(ev), len(names))); ew[:, names.index("head")] = 1
    bind(eyes, arm, names, ew)

    # Mane and tail cards
    mv, mf, mu, mr = mane_cards(horse, v)
    mane = new_mesh_object("Mane", mv, mf); set_uv(mane.data, mu, "UVMap"); add_material(mane, "Hair")
    mw_names = ["neck1", "neck2", "head"]
    mw = np.zeros((len(mv), len(names)))
    for b in mw_names:
        a, t, _ = bones[b]; mw[:, names.index(b)] = 1 / (seg_dist(mv, np.asarray(a), np.asarray(t)) ** 4 + 1e-7)
    fl = mr == "forelock"; mw[fl] = 0; mw[fl, names.index("head")] = 1
    keep = np.argsort(-mw, 1)[:, :2]; m2 = np.zeros_like(mw); rows = np.arange(len(mv))[:, None]
    m2[rows, keep] = mw[rows, keep]; m2 /= m2.sum(1, keepdims=True)
    bind(mane, arm, names, m2)

    tv, tf, tu = tail_cards(horse, bones)
    tail = new_mesh_object("Tail", tv, tf); set_uv(tail.data, tu, "UVMap"); add_material(tail, "Hair")
    tw = np.zeros((len(tv), len(names)))
    for i in range(1, 5):
        a, t, _ = bones[f"tail{i}"]; tw[:, names.index(f"tail{i}")] = 1 / (seg_dist(tv, np.asarray(a), np.asarray(t)) ** 4 + 1e-7)
    keep = np.argsort(-tw, 1)[:, :2]; t2 = np.zeros_like(tw); rows = np.arange(len(tv))[:, None]
    t2[rows, keep] = tw[rows, keep]; t2 /= t2.sum(1, keepdims=True)
    bind(tail, arm, names, t2)

    acts = build_animations(arm, profile.s_leg)

    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"horse_{profile.name}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False,
        export_texcoords=True, export_normals=True, export_skins=True,
        export_vertex_color="ACTIVE", export_all_vertex_colors=True,
        export_animations=True, export_animation_mode="ACTIONS", export_force_sampling=True,
        export_frame_range=False, export_def_bones=False, export_morph=False, export_materials="EXPORT",
    )
    tri = lambda ob: sum(len(p.vertices) - 2 for p in ob.data.polygons)
    manifest = {
        "assetVersion": ASSET_VERSION, "profile": profile.name, "status": "TEMPORARY PLACEHOLDER - not an approved horse asset",
        "units": "metres; glTF +Y up; horse faces +X", "generator": "tools/horse/build_horse.py",
        "blender": bpy.app.version_string,
        "triangles": {"Body": tri(body), "Eyes": tri(eyes), "Mane": tri(mane), "Tail": tri(tail)},
        "bones": list(bones), "animations": [a.name for a in acts],
        "attributes": {
            "COLOR_0": "r=points (lower legs/ear tips), g=soft nose/eye skin, b=midline (dorsal stripe on trunk/neck; face-front on head), a=countershade (belly/flank)",
            "TEXCOORD_0": "placeholder side projection; left half / right half separated",
            "TEXCOORD_1": "canonical marking coords: head=(s along face 0 poll..1 muzzle, t lateral -1..1); leg=(10+legIndex FL0 FR1 HL2 HR3, height 0 ground..1 knee/hock); other=(-10,50)",
        },
        "landmarks": {k: [round(float(x), 4) for x in val] for k, val in horse.landmarks.items()},
        "buildSeconds": round(time.time() - t0, 1),
    }
    with open(os.path.join(out_dir, f"horse_{profile.name}.manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"[build_horse] {profile.name}: {manifest['triangles']} in {manifest['buildSeconds']}s -> {path}")
    return manifest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="game/assets/horse/placeholder")
    ap.add_argument("--profiles", nargs="+", default=["adult", "foal"])
    a = ap.parse_args()
    for name in a.profiles:
        build({"adult": anatomy.ADULT, "foal": anatomy.FOAL}[name], a.out)


if __name__ == "__main__":
    main()
