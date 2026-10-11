"""Adapt the inspected Meshy light-horse candidate into paired game masters.

Run with:
  blender --background --factory-startup --disable-autoexec --python tools/horse/build_meshy_production.py

The source candidate is immutable. The builder imports it afresh for each
family, bakes orientation/scale, decimates before applying family warps, then
rebuilds the project-specific rig, masks, and shape channels locally.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "art/candidates/meshy/light_01/source.glb"
SOURCE_REVIEW = ROOT.parent / "outputs/meshy-light-review"
OUT_DIR = ROOT / "game/assets/horse/production"
BLEND_DIR = ROOT / "art/derived/production"
EVIDENCE_DIR = BLEND_DIR / "evidence/meshy"
FLOOR_Z = -0.79319394
SCALE = 1.25
TARGET_TRIANGLES = 30000
SHAPE_KEYS = ("body_length", "chest_width", "neck_length", "muscle")
FAMILIES = {
    "light": {"length": 0.035, "width": 1.0, "haunch": 1.0, "shoulder": 1.0,
              "muscle": 0.0, "neck": 0.045, "profile": "Meshy refined light adult"},
    "stock": {"length": -0.080, "width": 1.16, "haunch": 1.24, "shoulder": 1.16,
              "muscle": 0.045, "neck": -0.095, "profile": "Meshy compact stock adult"},
}
EXPECTED_NODES = {"Body", "Eyes", "Mane", "Tail"}


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def importer_api():
    return load_module("horse_import_reference", ROOT / "tools/horse/import_reference.py")


def motion_api():
    return load_module("horse_production_motion", ROOT / "tools/horse/production_motion.py")


def anatomy_regions_api():
    return load_module("horse_anatomy_regions", ROOT / "tools/horse/anatomy_regions.py")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoothstep(a: float, b: float, value: float) -> float:
    t = max(0.0, min(1.0, (value - a) / max(1e-8, b - a)))
    return t * t * (3.0 - 2.0 * t)


def gaussian(value: float, center: float, width: float) -> float:
    return math.exp(-((value - center) / width) ** 2)


def family_point(p: Vector, family: str) -> Vector:
    """Candidate coordinates use +X forward, +Y left, Z up."""
    spec = FAMILIES[family]
    x, y, z = p
    trunk = smoothstep(-0.88, -0.55, x) * (1.0 - smoothstep(0.02, 0.38, x)) * smoothstep(0.74, 1.13, z)
    t = smoothstep(-0.63, 0.23, x)
    x += spec["length"] * (t - 0.5) * trunk
    chest = gaussian(x, -0.02, 0.50) * smoothstep(0.70, 1.18, z)
    y *= 1.0 + (spec["width"] - 1.0) * chest
    upper_mass = smoothstep(0.70, 1.08, z) * (1.0 - smoothstep(1.72, 1.96, z))
    haunch = gaussian(x, -0.66, 0.25) * upper_mass
    shoulder = gaussian(x, 0.24, 0.23) * upper_mass
    y *= 1.0 + (spec["haunch"] - 1.0) * haunch + (spec["shoulder"] - 1.0) * shoulder
    z += spec["muscle"] * (haunch + shoulder)
    neck = smoothstep(0.30, 0.67, x) * smoothstep(1.10, 1.50, z)
    x += spec["neck"] * neck
    return Vector((x, y, z))


def sole_support_delta(p: Vector) -> float:
    """Lower only the outer support rows of the four candidate hooves.

    The adult candidate's front soles sit about 6 mm above the floor while
    the hind feet touch on only a few central points. This small, topology-
    preserving correction moves the lower support band toward z=1 mm; it does
    not resize the whole foot or move the limb above the hoof.
    """
    regions = (
        ("Front_R", 0.34, 0.45, -0.23, -0.045),
        ("Front_L", 0.34, 0.45, 0.045, 0.23),
        ("Hind_R", -0.84, -0.63, -0.24, -0.055),
        ("Hind_L", -0.84, -0.63, 0.055, 0.24),
    )
    for _name, x0, x1, y0, y1 in regions:
        if x0 <= p.x <= x1 and y0 <= p.y <= y1 and p.z < 0.035:
            # Preserve points already at/just below the support plane. Limit
            # travel to 8 mm and taper out before the ankle.
            return min(0.008, max(0.0, p.z - 0.001)) * (1.0 - smoothstep(0.020, 0.055, p.z))
    return 0.0


def family_point_with_sole_support(p: Vector, family: str) -> Vector:
    q = p.copy()
    q.z -= sole_support_delta(q)
    return family_point(q, family)


def correct_sole_support(body: bpy.types.Object) -> dict:
    regions = {
        "Front_R": (0.34, 0.45, -0.23, -0.045),
        "Front_L": (0.34, 0.45, 0.045, 0.23),
        "Hind_R": (-0.84, -0.63, -0.24, -0.055),
        "Hind_L": (-0.84, -0.63, 0.055, 0.24),
    }
    before = {}
    for name, (x0, x1, y0, y1) in regions.items():
        zs = sorted(vertex.co.z for vertex in body.data.vertices
                    if x0 <= vertex.co.x <= x1 and y0 <= vertex.co.y <= y1 and vertex.co.z < 0.035)
        if len(zs) < 20:
            raise RuntimeError(f"Could not isolate a stable support sole for {name}: {len(zs)} vertices")
        before[name] = {"samples": len(zs), "minimum_m": zs[0], "p05_m": zs[max(0, int(len(zs)*0.05)-1)]}
    moved = 0
    max_delta = 0.0
    for vertex in body.data.vertices:
        delta = sole_support_delta(vertex.co)
        if delta > 0.0:
            vertex.co.z -= delta
            moved += 1
            max_delta = max(max_delta, delta)
    body.data.update()
    after = {}
    for name, (x0, x1, y0, y1) in regions.items():
        zs = sorted(vertex.co.z for vertex in body.data.vertices
                    if x0 <= vertex.co.x <= x1 and y0 <= vertex.co.y <= y1 and vertex.co.z < 0.035)
        after[name] = {"samples": len(zs), "minimum_m": zs[0], "p05_m": zs[max(0, int(len(zs)*0.05)-1)]}
        if not -0.002 <= after[name]["minimum_m"] <= 0.0025 or after[name]["p05_m"] > 0.003:
            raise RuntimeError(f"Sole support correction did not create broad near-floor contact for {name}: {after[name]}")
    return {"method": "per-foot lower support band, bounded 8 mm vertical correction after UV projection; foot warp is identity",
            "targetPlane_m": 0.001, "maxCorrection_m": round(max_delta, 6),
            "movedVertices": moved, "before": before, "after": after}


def full_shell_head_landmarks(body: bpy.types.Object, family: str) -> dict:
    """Cache poll/muzzle from the intact candidate before hair face extraction."""
    # The accepted Light ray diagnostic places the poll/crown just behind and
    # above the bilateral ear roots. Convert its post-Light-warp ear anchor
    # back through the known +45 mm Light neck shift, then apply the requested
    # family warp exactly once for both Light and Stock.
    ear_root_source_mid = Vector((0.903856, 0.000804, 1.849395))
    poll_source_estimate = Vector((0.868856, 0.000804, 1.893395))
    ear_root_mid = family_point(ear_root_source_mid, family)
    poll_estimate = family_point(poll_source_estimate, family)
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    poll_hit = tree.find_nearest(poll_estimate)
    if poll_hit is None or poll_hit[0] is None:
        raise RuntimeError("Could not locate the centered poll on the intact candidate shell")
    poll = poll_hit[0]
    points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
    maximum = max(point.x for point in points)
    muzzle_points = [point for point in points if point.x >= maximum - 0.012 and abs(point.y) <= 0.055]
    if len(muzzle_points) < 3:
        raise RuntimeError("Could not find centered muzzle-tip vertices on the intact candidate shell")
    muzzle = sum(muzzle_points, Vector()) / len(muzzle_points)
    if abs(poll.y) > 0.02:
        raise RuntimeError(f"Cached poll is not on the skull midline: {tuple(poll)}")
    axis = muzzle - poll
    horizontal = math.hypot(axis.x, axis.y)
    azimuth_x = axis.x / max(horizontal, 1e-8)
    if axis.length < 0.35 or azimuth_x < 0.95:
        raise RuntimeError(f"Intact-shell poll-to-muzzle axis failed horizontal azimuth: {tuple(axis)}")
    return {"poll": poll.copy(), "muzzle": muzzle.copy(),
            "method": "poll projected onto intact source shell from the reviewed centered behind-ear crown ray; muzzle from centered forward-most nose vertices",
            "earRootMidpoint_m": [round(v, 5) for v in ear_root_mid],
            "pollEstimate_m": [round(v, 5) for v in poll_estimate],
            "poll_m": [round(v, 5) for v in poll], "muzzle_m": [round(v, 5) for v in muzzle],
            "direction": [round(v, 5) for v in axis.normalized()],
            "horizontalForwardXY": round(azimuth_x, 5),
            "muzzleCandidateCount": len(muzzle_points)}


def morph_delta(p: Vector, key: str) -> Vector:
    x, y, z = p
    if key == "body_length":
        trunk = smoothstep(-0.88, -0.55, x) * (1.0 - smoothstep(0.02, 0.38, x)) * smoothstep(0.74, 1.13, z)
        t = smoothstep(-0.63, 0.23, x)
        return Vector((0.085 * (t - 0.5) * trunk, 0.0, 0.0))
    if key == "chest_width":
        amount = gaussian(x, -0.02, 0.50) * smoothstep(0.70, 1.18, z)
        return Vector((0.0, y * 0.14 * amount, 0.0))
    if key == "neck_length":
        amount = smoothstep(0.30, 0.67, x) * smoothstep(1.10, 1.50, z)
        return Vector((0.075 * amount, 0.0, 0.0))
    if key == "muscle":
        amount = gaussian(x, -0.02, 0.62) * smoothstep(0.75, 1.18, z) * (1.0 - smoothstep(1.75, 2.02, z))
        return Vector((0.0, y * 0.09 * amount, (z - 1.28) * 0.075 * amount))
    raise ValueError(key)


def remove_all_objects() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    # The paired Light/Stock build runs in one Blender process. The first
    # family's Idle action becomes orphaned when its objects are cleared;
    # remove that builder-owned orphan so the second action keeps the exact
    # exported name rather than Blender's automatic `.001` suffix.
    for action in list(bpy.data.actions):
        if action.users == 0:
            bpy.data.actions.remove(action)


def source_to_world(raw: Vector) -> Vector:
    # Source horse faces raw -Y; a quarter turn about Z maps it to game +X.
    return Vector((-raw.y, raw.x, raw.z - FLOOR_Z)) * SCALE


def import_candidate() -> bpy.types.Object:
    remove_all_objects()
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1 or len(meshes[0].data.vertices) < 100000:
        raise RuntimeError(f"Expected one dense candidate mesh, got {[o.name for o in meshes]}")
    body = meshes[0]
    body.name = "Body"
    body.data.name = "Body"
    for vertex in body.data.vertices:
        vertex.co = source_to_world(vertex.co.copy())
    body.data.update()
    body.location = (0.0, 0.0, 0.0)
    body.rotation_euler = (0.0, 0.0, 0.0)
    body.scale = (1.0, 1.0, 1.0)
    return body


def decimate(body: bpy.types.Object) -> dict:
    before = sum(len(poly.vertices) - 2 for poly in body.data.polygons)
    ratio = min(1.0, TARGET_TRIANGLES / max(1, before))
    modifier = body.modifiers.new("Production_Decimate", "DECIMATE")
    modifier.decimate_type = "COLLAPSE"
    modifier.ratio = ratio
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    after = sum(len(poly.vertices) - 2 for poly in body.data.polygons)
    if not 24000 <= after <= 36000:
        raise RuntimeError(f"Candidate decimation is outside the 24-36k target: {after}")
    for poly in body.data.polygons:
        poly.use_smooth = True
    return {"sourceTriangles": before, "exportTargetTriangles": after, "collapseRatio": round(ratio, 6)}


def topology_index_sha256(body: bpy.types.Object) -> str:
    """Hash decimated polygon vertex indices, independent of family positions."""
    digest = hashlib.sha256()
    for polygon in body.data.polygons:
        digest.update(len(polygon.vertices).to_bytes(2, "little"))
        for index in polygon.vertices:
            digest.update(int(index).to_bytes(4, "little"))
    return digest.hexdigest()


def _distance_to_polyline_xz(point: Vector, polyline: list[tuple[float, float]]) -> float:
    best = float("inf")
    for first, second in zip(polyline, polyline[1:]):
        a = Vector((first[0], first[1]))
        b = Vector((second[0], second[1]))
        p = Vector((point.x, point.z))
        segment = b - a
        t = max(0.0, min(1.0, (p - a).dot(segment) / max(1e-10, segment.length_squared)))
        best = min(best, (p - (a + t * segment)).length)
    return best


def _inside_polygon_xz(point: Vector, polygon: list[tuple[float, float]]) -> bool:
    """2D odd/even test for the hand-traced, source-specific mane sheet."""
    inside = False
    for index, first in enumerate(polygon):
        second = polygon[(index + 1) % len(polygon)]
        if ((first[1] > point.z) != (second[1] > point.z) and
                point.x < (second[0] - first[0]) * (point.z - first[1]) /
                (second[1] - first[1] + 1e-12) + first[0]):
            inside = not inside
    return inside


def classify_source_hair_faces(body: bpy.types.Object) -> dict[str, set[int]]:
    """Partition existing sculpted crest/tail surface without adding overlays.

    The source is one welded shell, so these face bands are a true partition:
    each original polygon remains in exactly one named output mesh. The shared
    face-edge rings are validated after extraction to ensure no rendered gaps.
    Thresholds are in the normalized pre-family coordinates.
    """
    # The source is a single sculpted shell. The dorsal crest and side-draped
    # mane are not separated by a material or object boundary, so the crest
    # and side-sheet regions below are a deliberately source-specific trace.
    # The side-sheet outline follows its raised hem from the withers to the
    # poll; it excludes the lower bare neck and cheek. The narrow centerline
    # extension captures the raised forelock between the ears.
    mane_path = [(-0.30, 1.54), (-0.10, 1.66), (0.12, 1.76), (0.34, 1.83),
                 (0.56, 1.84), (0.72, 1.89), (0.86, 1.94), (0.99, 1.97)]
    mane_sheet = [(0.08, 1.76), (0.28, 1.84), (0.48, 1.90), (0.70, 1.92),
                  (0.87, 1.88), (0.97, 1.79), (1.01, 1.70), (0.93, 1.80),
                  (0.82, 1.76), (0.72, 1.70), (0.63, 1.64), (0.55, 1.58),
                  (0.48, 1.52), (0.41, 1.36), (0.34, 1.20), (0.24, 1.16),
                  (0.16, 1.28), (0.14, 1.50)]
    tail_path = [(-0.88, 1.30), (-0.94, 1.13), (-1.00, 0.94), (-1.05, 0.73),
                 (-1.08, 0.52), (-1.03, 0.39)]
    faces = {"Mane": set(), "Tail": set()}
    for polygon in body.data.polygons:
        centroid = sum((body.data.vertices[index].co for index in polygon.vertices), Vector()) / len(polygon.vertices)
        is_crest_or_forelock = (
            -0.40 <= centroid.x <= 1.06 and centroid.z >= 1.48 and
            abs(centroid.y) <= 0.14 and
            _distance_to_polyline_xz(centroid, mane_path) <= 0.085)
        is_hanging_side_sheet = abs(centroid.y) > 0.045 and _inside_polygon_xz(centroid, mane_sheet)
        if is_crest_or_forelock or is_hanging_side_sheet:
            faces["Mane"].add(polygon.index)
        elif (centroid.x <= -0.82 and 0.32 <= centroid.z <= 1.34 and abs(centroid.y) <= 0.15
              and _distance_to_polyline_xz(centroid, tail_path) <= 0.112):
            faces["Tail"].add(polygon.index)
    if len(faces["Mane"]) < 40 or len(faces["Tail"]) < 80:
        counts = {key: len(value) for key, value in faces.items()}
        raise RuntimeError(f"Sculpted hair partition is unexpectedly small: {counts}")
    return faces


def _make_face_partition(name: str, source: bpy.types.Object, face_indices: list[int]) -> bpy.types.Object:
    source_mesh = source.data
    source_mesh.update()
    source_loop_normals = [normal.vector.copy() for normal in source_mesh.corner_normals]
    source_uv = source_mesh.uv_layers.get("UVMap")
    used = sorted({vertex for face_index in face_indices for vertex in source_mesh.polygons[face_index].vertices})
    remap = {old: new for new, old in enumerate(used)}
    vertices = [source_mesh.vertices[index].co.copy() for index in used]
    faces = [[remap[index] for index in source_mesh.polygons[face_index].vertices] for face_index in face_indices]
    mesh = bpy.data.meshes.new(name + "Data")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.name = name
    obj = bpy.data.objects.new(name, mesh)
    for collection in source.users_collection:
        collection.objects.link(obj)
    if source_uv:
        uv = mesh.uv_layers.new(name="UVMap")
        for out_poly, source_face_index in zip(mesh.polygons, face_indices):
            source_poly = source_mesh.polygons[source_face_index]
            for out_loop, source_loop in zip(out_poly.loop_indices, source_poly.loop_indices):
                uv.data[out_loop].uv = source_uv.data[source_loop].uv
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    # Preserve the original welded-shell loop normals through the named mesh
    # split. Recalculating each partition independently creates hard dark seams
    # along the mane/tail boundaries in neutral materials.
    split_normals = []
    for source_face_index in face_indices:
        source_poly = source_mesh.polygons[source_face_index]
        split_normals.extend(source_loop_normals[loop_index] for loop_index in source_poly.loop_indices)
    mesh.normals_split_custom_set(split_normals)
    return obj


def _source_vertex_indices(mesh, face_indices: list[int]) -> list[int]:
    return sorted({vertex for face_index in face_indices for vertex in mesh.polygons[face_index].vertices})


def split_sculpted_hair(body: bpy.types.Object, partitions: dict[str, set[int]]):
    source_polygons = body.data.polygons
    claimed = partitions["Mane"] | partitions["Tail"]
    if len(claimed) != len(partitions["Mane"]) + len(partitions["Tail"]):
        raise RuntimeError("Mane and Tail face partitions overlap")
    remaining = [poly.index for poly in source_polygons if poly.index not in claimed]
    body_sources = _source_vertex_indices(body.data, remaining)
    mane_sources = _source_vertex_indices(body.data, sorted(partitions["Mane"]))
    tail_sources = _source_vertex_indices(body.data, sorted(partitions["Tail"]))
    body_local = {source: local for local, source in enumerate(body_sources)}
    mane_local = {source: local for local, source in enumerate(mane_sources)}
    tail_local = {source: local for local, source in enumerate(tail_sources)}
    correspondence = {
        "bodyMane": [(body_local[source], mane_local[source]) for source in sorted(set(body_local) & set(mane_local))],
        "bodyTail": [(body_local[source], tail_local[source]) for source in sorted(set(body_local) & set(tail_local))],
    }
    total_faces = len(remaining) + len(claimed)
    if total_faces != len(source_polygons):
        raise RuntimeError("Hair partition does not conserve source faces")
    mane = _make_face_partition("Mane", body, sorted(partitions["Mane"]))
    tail = _make_face_partition("Tail", body, sorted(partitions["Tail"]))
    replacement = _make_face_partition("Body_Repartitioned", body, remaining)
    old_mesh = body.data
    body.data = replacement.data
    bpy.data.objects.remove(replacement, do_unlink=True)
    if old_mesh.users == 0:
        bpy.data.meshes.remove(old_mesh)
    body.name = "Body"
    body.data.name = "Body"
    # Every open seam edge is duplicated in exactly two named partitions.
    boundaries: dict[tuple, list[str]] = {}
    for obj in (body, mane, tail):
        edge_faces: dict[tuple[int, int], int] = {}
        for poly in obj.data.polygons:
            ids = list(poly.vertices)
            for a, b in zip(ids, ids[1:] + ids[:1]):
                edge = tuple(sorted((a, b)))
                edge_faces[edge] = edge_faces.get(edge, 0) + 1
        for edge, count in edge_faces.items():
            if count != 1:
                continue
            a, b = (obj.data.vertices[index].co for index in edge)
            key = tuple(sorted((tuple(round(v, 6) for v in a), tuple(round(v, 6) for v in b))))
            boundaries.setdefault(key, []).append(obj.name)
    unmatched = {edge: nodes for edge, nodes in boundaries.items() if len(nodes) != 2 or len(set(nodes)) != 2}
    if unmatched:
        raise RuntimeError(f"Hair partition left {len(unmatched)} unmatched surface seam edges")
    return mane, tail, {"sourceFaces": len(source_polygons), "bodyFaces": len(remaining),
                        "maneFaces": len(partitions["Mane"]), "tailFaces": len(partitions["Tail"]),
                        "sharedSeamEdges": len(boundaries), "facePartitionConserved": True,
                        "loopNormalsPreservedFromWeldedSource": True,
                        "bodyManeSharedVertices": len(correspondence["bodyMane"]),
                        "bodyTailSharedVertices": len(correspondence["bodyTail"])}, correspondence


def _vertex_bone_weights(obj: bpy.types.Object, vertex_index: int) -> dict[str, float]:
    groups = {group.index: group.name for group in obj.vertex_groups}
    return {groups[item.group]: float(item.weight) for item in obj.data.vertices[vertex_index].groups
            if item.group in groups and item.weight > 0.0}


def _set_vertex_bone_weights(obj: bpy.types.Object, vertex_index: int, weights: dict[str, float]) -> None:
    for group in obj.vertex_groups:
        try:
            group.remove([vertex_index])
        except RuntimeError:
            pass
    for name, value in weights.items():
        group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        group.add([vertex_index], value, "REPLACE")


def match_hair_boundary_weights(body: bpy.types.Object, mane: bpy.types.Object, tail: bpy.types.Object,
                                correspondence: dict, forelock_mane_indices: set[int]) -> dict:
    """Copy skin weights across duplicated source vertices at hair cut rims."""
    max_delta = 0.0
    max_sum_error = 0.0
    copied = {"Body_Mane": 0, "Body_Tail": 0}
    boundary_forelock = set()
    for key, target, label in (("bodyMane", mane, "Body_Mane"), ("bodyTail", tail, "Body_Tail")):
        for body_index, target_index in correspondence[key]:
            weights = _vertex_bone_weights(body, body_index)
            if not weights or len(weights) > 4:
                raise RuntimeError(f"Invalid source skin at {label} seam vertex: {weights}")
            _set_vertex_bone_weights(target, target_index, weights)
            if label == "Body_Mane":
                boundary_forelock.add(target_index)
            actual = _vertex_bone_weights(target, target_index)
            max_delta = max(max_delta, max((abs(weights.get(name, 0.0)-actual.get(name, 0.0))
                                            for name in set(weights) | set(actual)), default=0.0))
            max_sum_error = max(max_sum_error, abs(sum(actual.values()) - 1.0))
            if len(actual) > 4:
                raise RuntimeError(f"Copied seam weights exceed four influences at {label}")
            copied[label] += 1
    head_group = mane.vertex_groups.get("Head") or mane.vertex_groups.new(name="Head")
    base_group = mane.vertex_groups.get("Head_Base") or mane.vertex_groups.new(name="Head_Base")
    interior = forelock_mane_indices - boundary_forelock
    for vertex_index in interior:
        _set_vertex_bone_weights(mane, vertex_index, {"Head": 0.75, "Head_Base": 0.25})
    if max_delta > 1e-6 or max_sum_error > 1e-5:
        raise RuntimeError(f"Hair seam weights are not exact/normalized: {max_delta}, {max_sum_error}")
    return {"method": "exact source-vertex correspondence; Body weights copied to duplicated Mane/Tail seam vertices",
            "copiedBoundaryVertices": copied,
            "forelockInteriorVertices": len(interior),
            "forelockBoundaryVerticesKeepBodyWeights": len(boundary_forelock & forelock_mane_indices),
            "forelockInteriorWeights": {"Head": 0.75, "Head_Base": 0.25},
            "maximumWeightDelta": round(max_delta, 8),
            "maximumWeightSumError": round(max_sum_error, 8),
            "maximumInfluences": 4}


def add_uv0(body: bpy.types.Object) -> None:
    for layer in list(body.data.uv_layers):
        body.data.uv_layers.remove(layer)
    body.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.015, area_weight=0.0,
                             angle_limit=math.radians(66.0), correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    body.data.uv_layers.active = body.data.uv_layers.get("UVMap")


def add_eyes(body: bpy.types.Object, family: str) -> tuple[bpy.types.Object, dict]:
    """Fit separate eye forms just outside the actual family head surface."""
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    eye_parts = []
    fit = []
    for side in (-1, 1):
        # The source has a sculpted orbital depression below the high cheek
        # ridge. Fit inside that visible socket instead of the outer cheek.
        probe = family_point(Vector((1.02, 0.160 * side, 1.70)), family)
        hit = tree.find_nearest(probe)
        if hit is None or hit[0] is None:
            raise RuntimeError(f"Could not find a head surface for eye side {side}")
        surface, normal = hit[0], hit[1].normalized()
        if normal.y * side < 0.65:
            raise RuntimeError(f"Eye fit found a non-lateral surface normal: {tuple(normal)}")
        center = surface - normal * 0.004
        # The eye's local Y axis points out of the skull. Recess most of a
        # horizontal almond into the orbital ridge so it reads as an eye form,
        # not a round bead attached to the cheek.
        outward = Vector((0.0, float(side), 0.0))
        rotation = outward.rotation_difference(normal)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=24, radius=1.0,
                                             location=(0.0, 0.0, 0.0))
        eye = bpy.context.object
        eye.name = f"Eye_{'L' if side > 0 else 'R'}_Temporary"
        for vertex in eye.data.vertices:
            local = vertex.co.copy()
            vertex.co = center + rotation @ Vector((local.x * 0.014, local.y * 0.0065, local.z * 0.009))
        uv = eye.data.uv_layers.new(name="UVMap")
        outward = [side * vertex.co.y for vertex in eye.data.vertices]
        inner, outer = min(outward), max(outward)
        z_lo = min(vertex.co.z for vertex in eye.data.vertices)
        z_hi = max(vertex.co.z for vertex in eye.data.vertices)
        for loop in eye.data.loops:
            point = eye.data.vertices[loop.vertex_index].co
            u = (side * point.y - inner) / max(1e-8, outer - inner)
            v = (point.z - z_lo) / max(1e-8, z_hi - z_lo)
            uv.data[loop.index].uv = (max(0.0, min(1.0, u)), max(0.0, min(1.0, v)))
        eye_parts.append(eye)
        fit.append({"side": "left" if side > 0 else "right",
                    "socketReference_m": [round(v, 5) for v in probe],
                    "probe_m": [round(v, 5) for v in probe],
                    "surface_m": [round(v, 5) for v in surface],
                    "outwardNormal": [round(v, 5) for v in normal],
                    "center_m": [round(v, 5) for v in center],
                    "surfaceRecess_m": 0.004, "maxSurfaceProtrusion_m": 0.0025,
                    "semiAxes_m": [0.014, 0.0065, 0.009]})
    bpy.ops.object.select_all(action="DESELECT")
    for eye in eye_parts:
        eye.select_set(True)
    bpy.context.view_layer.objects.active = eye_parts[0]
    bpy.ops.object.join()
    eyes = bpy.context.object
    eyes.name = "Eyes"
    eyes.data.name = "Eyes"
    for poly in eyes.data.polygons:
        poly.use_smooth = True
    return eyes, {"method": "BVH orbital surface fit with partially recessed horizontal almond geometry", "sides": fit}


def add_hair_uv(obj: bpy.types.Object, kind: str) -> None:
    mesh = obj.data
    layer = mesh.uv_layers.get("UVMap") or mesh.uv_layers.new(name="UVMap")
    coords = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    lo_x, hi_x = min(p.x for p in coords), max(p.x for p in coords)
    lo_y, hi_y = min(p.y for p in coords), max(p.y for p in coords)
    lo_z, hi_z = min(p.z for p in coords), max(p.z for p in coords)
    for loop in mesh.loops:
        p = coords[loop.vertex_index]
        u = (p.y - lo_y) / max(1e-8, hi_y - lo_y)
        v = ((hi_x - p.x) / max(1e-8, hi_x - lo_x) if kind == "mane"
             else (hi_z - p.z) / max(1e-8, hi_z - lo_z))
        layer.data[loop.index].uv = (u, v)
    mesh.uv_layers.active = layer


def make_materials(importer, body, eyes, mane, tail) -> None:
    coat_normal = None
    body.data.materials.clear()
    body.data.materials.append(importer.create_neutral_material("Coat_Descriptor_Neutral", coat_normal))
    eyes.data.materials.clear()
    eyes.data.materials.append(importer.create_neutral_material("Eye_Descriptor_Neutral"))
    hair = importer.create_neutral_material("Hair_Descriptor_Neutral")
    for obj in (mane, tail):
        obj.data.materials.clear()
        obj.data.materials.append(hair)


def add_shape_keys(objects: list[bpy.types.Object]) -> dict:
    max_deltas = {}
    for obj in objects:
        obj.shape_key_add(name="Basis", from_mix=False)
        local_to_world = obj.matrix_world.copy()
        world_to_local = local_to_world.inverted()
        for name in SHAPE_KEYS:
            key = obj.shape_key_add(name=name, from_mix=False)
            max_delta = 0.0
            for index, vertex in enumerate(obj.data.vertices):
                p = local_to_world @ vertex.co
                delta = morph_delta(p, name)
                key.data[index].co = world_to_local @ (p + delta)
                max_delta = max(max_delta, delta.length)
            key.slider_min, key.slider_max, key.value = -1.0, 1.0, 0.0
            max_deltas.setdefault(name, {})[obj.name] = round(max_delta, 5)
        obj.data.shape_keys.use_relative = True
    return max_deltas


def prepare_candidate_masks(importer, body: bpy.types.Object, rig: bpy.types.Object,
                            head_landmarks: dict, *, minimum_axis_length: float = 0.35) -> tuple[dict, dict, dict]:
    """Apply canonical masks and repair per-corner category-boundary values."""
    bones = rig.data.bones
    if "Ear_L" not in bones or "Ear_R" not in bones or "Jaw_Tip" not in bones:
        raise RuntimeError("Smart Rig aliases do not provide ear roots and the lower facial chain")
    # Semantic anchors were cached against the intact source shell before its
    # hair faces were partitioned. Reprojecting to Body here could land on the
    # exposed seam or lateral neck and shift the canonical face axis.
    poll = head_landmarks["poll"].copy()
    muzzle = head_landmarks["muzzle"].copy()
    axis = muzzle - poll
    horizontal = math.hypot(axis.x, axis.y)
    horizontal_forward_x = axis.x / max(horizontal, 1e-8)
    if (not all(math.isfinite(value) for value in axis) or axis.length < minimum_axis_length
            or axis.x <= 0.0 or horizontal_forward_x < 0.95):
        raise RuntimeError(f"Poll-to-muzzle axis has invalid horizontal azimuth: {tuple(axis)}")
    original = importer.rig_world_bones
    def canonical_axis_bones(target_rig):
        result = original(target_rig)
        result["Head"] = (poll.copy(), muzzle.copy())
        return result
    importer.rig_world_bones = canonical_axis_bones
    try:
        report = importer.prepare_body(body, rig, "adult")
        # The legacy helper chooses a polygon region by majority, but only
        # writes the category mask for vertices whose weights voted for that
        # region. On this welded candidate, that leaves angular COLOR_0 and
        # UV1 transitions at the outer head and limb loops. Preserve the same
        # anatomical category decision, then evaluate each corner from its own
        # geometry and smooth normal.
        tags, coords, weights, bones, head_a, head_axis, head_len, _, knee_heights = importer.classify_vertices(body, rig, "adult")
        canonical = body.data.uv_layers.get("Canonical")
        color = body.data.color_attributes.get("Color")
        if canonical is None or color is None:
            raise RuntimeError("Canonical UV1 or COLOR_0 is missing after mask generation")
        head_front_axis = Vector((-head_axis.z, 0.0, head_axis.x)).normalized()
        repaired_head = repaired_leg = checked_head = checked_leg = 0
        max_head_uv_error = max_leg_height_error = 0.0
        for poly in body.data.polygons:
            ids = list(poly.vertices)
            head_ids = [i for i in ids if tags[i]["head"]]
            leg_votes = {leg: sum(1 for i in ids if tags[i]["leg"] == leg) for leg in importer.LEG_ORDER}
            leg = max(importer.LEG_ORDER, key=lambda name: leg_votes[name]) if max(leg_votes.values(), default=0) > 0 else None
            is_head = bool(head_ids) and (leg is None or len(head_ids) >= leg_votes[leg])
            if is_head:
                for loop_index in poly.loop_indices:
                    vertex_index = body.data.loops[loop_index].vertex_index
                    p = coords[vertex_index]
                    s = ((p - head_a).dot(head_axis) / head_len)
                    n = (body.matrix_world.to_3x3() @ body.data.vertices[vertex_index].normal).normalized()
                    uv = canonical.data[loop_index].uv
                    if not tags[vertex_index]["head"]:
                        repaired_head += 1
                    checked_head += 1
                    max_head_uv_error = max(max_head_uv_error, abs(uv.x - max(-0.45, min(1.35, s))))
                    # UV1 was already geometrically projected for every corner
                    # in a head-majority polygon. Re-evaluate front/muzzle
                    # channels for boundary corners using that same local s.
                    g, old_green, b, a = color.data[loop_index].color
                    green = max(old_green, importer.smoothstep(0.68, 0.94, s))
                    front = max(0.0, min(1.0, (n.dot(head_front_axis) - 0.12) / 0.52))
                    color.data[loop_index].color = (g, green, max(b, front), a)
            elif leg is not None:
                for loop_index in poly.loop_indices:
                    vertex_index = body.data.loops[loop_index].vertex_index
                    p = coords[vertex_index]
                    h = max(0.0, min(1.1, p.z / knee_heights[leg]))
                    if tags[vertex_index]["leg"] != leg:
                        repaired_leg += 1
                    checked_leg += 1
                    canonical.data[loop_index].uv = (10.0 + importer.LEG_CANONICAL_INDEX[leg], h)
                    max_leg_height_error = max(max_leg_height_error,
                                               abs(canonical.data[loop_index].uv.y - h))
                    red, g, b, a = color.data[loop_index].color
                    sock = 1.0 - importer.smoothstep(0.22, 0.78, h)
                    color.data[loop_index].color = (max(red, sock), g, b, a)
    finally:
        importer.rig_world_bones = original
    star_coordinate = poll + axis * 0.30
    axis_report = {**{key: value for key, value in head_landmarks.items() if key not in {"poll", "muzzle"}},
                   "method": head_landmarks["method"],
                   "poll_m": [round(value, 5) for value in poll],
                   "muzzle_m": [round(value, 5) for value in muzzle],
                   "direction": [round(value, 5) for value in axis.normalized()],
                   "axisLength_m": round(axis.length, 5),
                   "minimumAxisLength_m": round(minimum_axis_length, 5),
                   "horizontalForwardXY": round(horizontal_forward_x, 5),
                   "canonicalS0_30World_m": [round(value, 5) for value in star_coordinate]}
    repair_report = {"method": "per-corner mask projection on majority-classified head/limb polygons",
                     "headBoundaryLoopsReevaluated": repaired_head,
                     "legBoundaryLoopsReevaluated": repaired_leg,
                     "checkedHeadLoops": checked_head, "checkedLegLoops": checked_leg,
                     "maxHeadUvError": round(max_head_uv_error, 8),
                     "maxLegHeightError": round(max_leg_height_error, 8)}
    if max_head_uv_error > 1e-5 or max_leg_height_error > 1e-5:
        raise RuntimeError(f"Canonical per-corner boundary repair failed validation: {repair_report}")
    return report, axis_report, repair_report


def MatrixIdentity():
    from mathutils import Matrix
    return Matrix.Identity(4)


def bounds(objects: list[bpy.types.Object]) -> dict:
    points = [obj.matrix_world @ vertex.co for obj in objects for vertex in obj.data.vertices]
    lo = [min(p[i] for p in points) for i in range(3)]
    hi = [max(p[i] for p in points) for i in range(3)]
    return {"min_m": [round(x, 5) for x in lo], "max_m": [round(x, 5) for x in hi],
            "size_m": [round(hi[i] - lo[i], 5) for i in range(3)]}


def render_views(family: str, objects: list[bpy.types.Object]) -> dict:
    scene = bpy.context.scene
    old_engine = scene.render.engine
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 1100, 800
    scene.display.shading.light = "STUDIO"
    scene.display.shading.studio_light = "paint.sl"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_specular_highlight = False
    box = bounds(objects)
    center = Vector([(box["min_m"][i] + box["max_m"][i]) / 2 for i in range(3)])
    span = max(box["size_m"])
    camera_data = bpy.data.cameras.new("Production_Camera")
    camera = bpy.data.objects.new("Production_Camera", camera_data)
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = span * 1.36
    scene.camera = camera
    dirs = {"side": Vector((0, -1, 0.11)), "front": Vector((1, 0, 0.11)),
            "rear": Vector((-1, 0, 0.11)), "q3": Vector((0.72, -0.70, 0.18))}
    paths = {}
    for label, direction in dirs.items():
        direction.normalize()
        camera.location = center + direction * span * 3.0
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        path = EVIDENCE_DIR / f"horse_{family}_{label}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        paths[label] = str(path.relative_to(ROOT))
    # Workbench's show_wire flags do not render in background mode. Add
    # temporary thin wire geometry so this evidence is a real topology view;
    # restore each source mesh's materials/modifiers before saving the .blend.
    wire_material = bpy.data.materials.new("TEMP_ProductionWire")
    wire_material.diffuse_color = (0.035, 0.045, 0.06, 1.0)
    restore_materials = {}
    wire_modifiers = []
    for obj in objects:
        restore_materials[obj.name] = list(obj.data.materials)
        obj.data.materials.clear()
        obj.data.materials.append(wire_material)
        wire = obj.modifiers.new("TEMP_ProductionWire", "WIREFRAME")
        wire.thickness = 0.0015
        wire.use_replace = True
        wire_modifiers.append((obj, wire))
    camera.location = center + dirs["side"] * span * 3.0
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    wire = EVIDENCE_DIR / f"horse_{family}_wire.png"
    scene.render.filepath = str(wire)
    bpy.ops.render.render(write_still=True)
    paths["wire"] = str(wire.relative_to(ROOT))
    for obj, modifier in wire_modifiers:
        obj.modifiers.remove(modifier)
        obj.data.materials.clear()
        for material in restore_materials[obj.name]:
            obj.data.materials.append(material)
    bpy.data.materials.remove(wire_material)
    scene.render.engine = old_engine
    bpy.data.objects.remove(camera, do_unlink=True)
    return paths


def export_glb(path: Path, rig: bpy.types.Object, meshes: list[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
        export_yup=True, export_apply=False, export_texcoords=True, export_normals=True,
        export_skins=True, export_vertex_color="ACTIVE", export_all_vertex_colors=True,
        export_animations=True, export_nla_strips=False, export_cameras=False, export_lights=False,
        export_materials="EXPORT", export_image_format="AUTO", export_morph=True)


def morph_targets(path: Path, importer) -> dict:
    doc, _ = importer.glb_chunks(path)
    result = {}
    for node in doc.get("nodes", []):
        if "mesh" not in node:
            continue
        mesh = doc["meshes"][node["mesh"]]
        names = mesh.get("extras", {}).get("targetNames", [])
        if not names and mesh.get("primitives"):
            names = mesh["primitives"][0].get("extras", {}).get("targetNames", [])
        if names:
            result[node.get("name", "")] = names
    return result


def build(family: str, importer, motion, anatomy, source_hash: str) -> None:
    body = import_candidate()
    decimation = decimate(body)
    topology_hash = topology_index_sha256(body)
    # The face-ID helper is tied to exact, unwarped decimated topology and
    # coordinates. Cache all sets/remaps before touching even the sole vertices.
    hair_faces = anatomy.classify_source_hair_faces(body)
    anatomy_report = anatomy.region_report(body)
    forelock_mane_indices = anatomy.partition_forelock_vertex_indices(body, hair_faces)
    if anatomy_report["maneFaces"] != 3752 or anatomy_report["forelockFaces"] != 407 or anatomy_report["tailFaces"] != 4542:
        raise RuntimeError(f"Unexpected approved source hair map: {anatomy_report}")
    for vertex in body.data.vertices:
        vertex.co = family_point(vertex.co.copy(), family)
    body.data.update()
    body.name = "Body"
    body.data.name = "Body"
    # Preserve the user's/historical UV atlas input exactly: unwrap at the
    # uncorrected per-family geometry, then change only the sole positions.
    add_uv0(body)
    sole_support = correct_sole_support(body)
    # Cache semantic face-axis anchors from the intact full shell before
    # extracting Mane/Tail; face ownership must not move the poll onto a hole.
    head_landmarks = full_shell_head_landmarks(body, family)
    mane, tail, hair_partition, hair_vertex_correspondence = split_sculpted_hair(body, hair_faces)
    eyes, eye_fit_report = add_eyes(body, family)
    accessories = [eyes, mane, tail]
    add_hair_uv(mane, "mane")
    add_hair_uv(tail, "tail")
    make_materials(importer, body, eyes, mane, tail)
    rig, rig_report = motion.prepare_candidate_rig(
        body, family, related_meshes=accessories, rig_name="Skeleton",
        family_point=family_point_with_sole_support,
        smart_rig_path=ROOT / "art/candidates/meshy/light_01/smart-rig.glb")
    if rig is None or rig.type != "ARMATURE":
        raise RuntimeError("Candidate rig builder did not return Skeleton")
    animation_report = motion.build_actions(rig)
    if animation_report.get("actions") != ["Idle"]:
        raise RuntimeError(f"Expected only the bounded Idle action, got {animation_report}")
    # Forelock is part of the named Mane mesh, but follows the skull rather
    # than the crest/neck. Reapply a head-only rest attachment to the cached
    # Mane-local source indices produced before any vertex edits.
    if rig.data.bones.get("Head") is None or rig.data.bones.get("Head_Base") is None:
        raise RuntimeError("Candidate rig lacks the Head/Head_Base pair needed for the forelock")
    hair_weight_report = match_hair_boundary_weights(
        body, mane, tail, hair_vertex_correspondence, set(forelock_mane_indices))
    for obj in (body, eyes, mane, tail):
        if not obj.vertex_groups:
            raise RuntimeError(f"Candidate rig supplied no skin weights for {obj.name}")
        arm = next((mod for mod in obj.modifiers if mod.type == "ARMATURE"), None)
        if arm is None:
            arm = obj.modifiers.new("Skeleton_Deform", "ARMATURE")
            arm.object = rig
            arm.use_vertex_groups = True
        elif arm.object != rig:
            raise RuntimeError(f"{obj.name} has an armature modifier targeting the wrong rig")
        obj.parent = rig
    mask_report, head_axis_report, mask_repair_report = prepare_candidate_masks(importer, body, rig, head_landmarks)
    if mask_report["forehead_front_loops"] <= 0:
        raise RuntimeError("Candidate body mask failed to classify any forehead-facing loops")
    quality = importer.mesh_quality([body, eyes, mane, tail])
    expected_seam_edges = 2 * hair_partition["sharedSeamEdges"]
    actual_open_edges = sum(counts["nonmanifold_edges"] for counts in quality.values())
    if (any(counts["zero_area_faces"] or counts["loose_vertices"] for counts in quality.values())
            or actual_open_edges != expected_seam_edges):
        raise RuntimeError(f"Candidate base geometry has non-clean topology: {quality}")
    shape_max = add_shape_keys([body, eyes, mane, tail])
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.preferences.filepaths.save_version = 0
    meshes = [body, eyes, mane, tail]
    glb = OUT_DIR / f"horse_{family}.glb"
    export_glb(glb, rig, meshes)
    report = importer.inspect_glb(glb, {}, allow_animations=True)
    if set(report["mesh_names"]) != EXPECTED_NODES:
        raise RuntimeError(f"Candidate GLB node contract failed: {report['mesh_names']}")
    if not report["body_has_descriptor_attributes"] or report["mixed_canonical_category_triangles"]:
        raise RuntimeError("Candidate Body lost descriptor mask/canonical integrity")
    if not report["body_has_skin"] or report.get("animationNames") != ["Idle"]:
        raise RuntimeError(f"Candidate export must be skinned with only Idle: {report.get('animationNames')}")
    target_report = morph_targets(glb, importer)
    if set(target_report) != EXPECTED_NODES or any(set(names) != set(SHAPE_KEYS) for names in target_report.values()):
        raise RuntimeError(f"Candidate GLB morph targets do not match all mesh nodes: {target_report}")
    if not 24000 <= report["triangles"] <= 36000:
        raise RuntimeError(f"Candidate GLB is outside triangle budget: {report['triangles']}")
    head_view_target = [round((report["exported_poll_center_m"][i] + report["exported_muzzle_center_m"][i]) / 2, 5) for i in range(3)]
    view_paths = render_views(family, meshes)
    blend = BLEND_DIR / f"horse_{family}.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    manifest = {
        "assetVersion": "horse-production.meshy.v1." + family,
        "familyId": "horse.standard" if family == "light" else "horse.compact",
        "profile": FAMILIES[family]["profile"],
        "status": "Candidate rigged master for visual, appearance, and deformation review; not approved production anatomy",
        "source": {"file": "art/candidates/meshy/light_01/source.glb", "sha256": source_hash,
                   "generationModel": "Meshy 7.1", "accountPlan": "owner-reported Pro",
                   "privacySetting": "Private", "thirdPartyImageInput": False,
                   "ownershipBasis": "Owner-generated candidate; paid Meshy plan ownership subject to compliant input and privacy settings",
                   "termsCheckedDate": "2026-10-11",
                   "termsSources": [
                       "https://help.meshy.ai/en/articles/9992001-can-i-use-meshy-assets-commercially-license-copyright-explained",
                       "https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models"]},
        "transforms": {"rawToBlender": "(x,y,z)->(-y,x,z+0.79319394)*1.25; baked into vertices",
                       "forward": "+X", "left": "+Y", "up": "+Z", "floor_z_m": 0.0,
                       "family": FAMILIES[family],
                       "familyWarp": "trunk length; localized chest/haunch/shoulder lateral profile and muscle bulge; localized neck shift"},
        "meshNodes": sorted(EXPECTED_NODES), "rigNode": "Skeleton", "animations": report["animationNames"],
        "triangleCount": report["triangles"], "trianglesByMesh": report["primitives"],
        "decimation": decimation, "sharedTopologyIndexSha256": topology_hash,
        "hairPartition": {**hair_partition, "sourceFaceMap": anatomy_report,
                           "forelockManeVertices": len(forelock_mane_indices),
                           "weightContinuity": hair_weight_report},
        "hoofSupportCorrection": sole_support,
        "bodyShapeKeys": {name: {"neutral": 0.0, "range": [-1.0, 1.0],
                                  "nodes": {node: shape_max[name][node] for node in sorted(EXPECTED_NODES)}} for name in SHAPE_KEYS},
        "bodyMasks": {"COLOR_0": True, "UV0": "Smart UV Project from Meshy source without source UV", "canonicalUV1": "existing importer anatomy helper"},
        "maskCoverage": mask_report, "maskBoundaryRepair": mask_repair_report,
        "canonicalHeadAxis": head_axis_report, "eyeFit": eye_fit_report,
        "motion": animation_report, "rig": rig_report,
        "bounds": bounds(meshes), "headViewTarget_m": head_view_target,
        "glbValidation": report, "glbMorphTargets": target_report,
        "geometryQuality": quality, "evidence": view_paths,
        "limitations": ["Meshy source was one welded mesh with no UV, skin, morphs, or named components.",
                        "Eyes are newly authored UV spheres. Existing sculpted Mane and Tail faces were partitioned into named meshes; seam edges are shared and verified.",
                        "Idle is a subtle candidate clip; walk/graze remain excluded, and no animation or final anatomy acceptance is claimed."],
    }
    (OUT_DIR / f"horse_{family}.manifest.json").write_text(json.dumps(manifest, indent=2))
    print(json.dumps({"family": family, "blend": str(blend), "glb": str(glb), "triangles": report["triangles"],
                      "morphs": target_report, "headViewTarget_m": head_view_target,
                      "bounds": bounds(meshes), "views": view_paths}, indent=2))


def main() -> None:
    for folder in (OUT_DIR, BLEND_DIR, EVIDENCE_DIR):
        folder.mkdir(parents=True, exist_ok=True)
    if not SOURCE.is_file():
        raise FileNotFoundError(SOURCE)
    source_hash = sha256(SOURCE)
    importer = importer_api()
    motion = motion_api()
    anatomy = anatomy_regions_api()
    requested = None
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1:]
        if "--family" in args:
            index = args.index("--family")
            if index + 1 >= len(args):
                raise RuntimeError("--family requires light or stock")
            requested = args[index + 1]
    if requested is not None and requested not in FAMILIES:
        raise RuntimeError(f"Unknown family {requested!r}; expected one of {sorted(FAMILIES)}")
    for family in ([requested] if requested else FAMILIES):
        build(family, importer, motion, anatomy, source_hash)


if __name__ == "__main__":
    main()
