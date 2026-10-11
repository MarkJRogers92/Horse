"""Build a juvenile horse asset from the immutable foal candidate.

Run from the repository root with:
  /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
    --disable-autoexec --python tools/horse/build_foal_production.py

This intentionally keeps the foal's own proportions and uses only localized
softening and juvenile-scale morph amplitudes; it is not an adult scaled down.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils import Quaternion
from mathutils.bvhtree import BVHTree
from mathutils import geometry
import bmesh

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "art/candidates/meshy/foal_01/source.glb"
RIG_SOURCE = ROOT / "art/candidates/meshy/foal_01/smart-rig-retry.glb"
OUT_DIR = ROOT / "game/assets/horse/production"
BLEND_DIR = ROOT / "art/derived/production"
EVIDENCE_DIR = BLEND_DIR / "evidence/foal"
FAMILY = "foal"
SHAPE_KEYS = ("body_length", "chest_width", "neck_length", "muscle")
EXPECTED_NODES = {"Body", "Eyes", "Mane", "Tail"}
SOURCE_Z_FLOOR = -0.9517204165458679
SOURCE_TO_WORLD_SCALE = 0.76
TARGET_TRIANGLES = 30000
ADULT_REFERENCE_BODY_HEIGHT_M = 1.97074


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def smoothstep(a: float, b: float, value: float) -> float:
    t = max(0.0, min(1.0, (value - a) / max(1e-8, b - a)))
    return t * t * (3.0 - 2.0 * t)


def foal_point(point: Vector, family: str = "foal") -> Vector:
    """Final foal space is already baked; callback required by the rig adapter."""
    if family != "foal":
        raise ValueError(f"Unsupported juvenile family: {family}")
    return point.copy()


def source_to_world(raw: Vector) -> Vector:
    # Meshy source faces raw -Y; this bakes it into game +X-forward space.
    return Vector((-raw.y, raw.x, raw.z - SOURCE_Z_FLOOR)) * SOURCE_TO_WORLD_SCALE


def import_and_decimate() -> tuple[bpy.types.Object, dict]:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for action in list(bpy.data.actions):
        if action.users == 0:
            bpy.data.actions.remove(action)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 1 or len(meshes[0].data.vertices) < 100000:
        raise RuntimeError(f"Expected the dense source mesh, got {[(o.name, len(o.data.vertices)) for o in meshes]}")
    body = meshes[0]
    body.name = "Body"
    body.data.name = "Body"
    for vertex in body.data.vertices:
        vertex.co = source_to_world(vertex.co.copy())
    body.data.update()
    body.location = (0.0, 0.0, 0.0)
    body.rotation_euler = (0.0, 0.0, 0.0)
    body.scale = (1.0, 1.0, 1.0)
    source_triangles = sum(len(poly.vertices) - 2 for poly in body.data.polygons)
    decimate = body.modifiers.new("Foal_Production_Decimate", "DECIMATE")
    decimate.decimate_type = "COLLAPSE"
    decimate.ratio = min(1.0, TARGET_TRIANGLES / max(1, source_triangles))
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    triangles = sum(len(poly.vertices) - 2 for poly in body.data.polygons)
    if not 24000 <= triangles <= 36000:
        raise RuntimeError(f"Foal decimation target outside range: {triangles}")
    for poly in body.data.polygons:
        poly.use_smooth = True
    body.data.update()
    return body, {"sourceTriangles": source_triangles, "exportTargetTriangles": triangles,
                  "collapseRatio": round(TARGET_TRIANGLES / source_triangles, 6)}


def localized_softening(body: bpy.types.Object) -> dict:
    """Reduce rib/shoulder/haunch grooves without smoothing the legs or face."""
    group = body.vertex_groups.new(name="TEMP_Foal_Torso_Soften")
    affected = 0
    for vertex in body.data.vertices:
        p = vertex.co
        # The candidate's rib ripples and shoulder/haunch cuts sit on the barrel;
        # preserve limbs, face, dorsal crest, and juvenile joints.
        torso = smoothstep(-0.48, -0.27, p.x) * (1.0 - smoothstep(0.30, 0.55, p.x))
        midheight = smoothstep(0.63, 0.78, p.z) * (1.0 - smoothstep(1.05, 1.20, p.z))
        side = smoothstep(0.035, 0.085, abs(p.y))
        weight = torso * midheight * side
        if weight > 0.02:
            group.add([vertex.index], weight, "REPLACE")
            affected += 1
    smooth = body.modifiers.new("Localized_Foal_Surface_Soften", "SMOOTH")
    smooth.factor = 0.22
    smooth.iterations = 4
    smooth.vertex_group = group.name
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    if body.vertex_groups.get("TEMP_Foal_Torso_Soften") is not None:
        body.vertex_groups.remove(body.vertex_groups.get("TEMP_Foal_Torso_Soften"))
    body.data.update()
    return {"method": "4-iteration Smooth modifier with barrel-only vertex falloff",
            "factor": 0.22, "affectedVertices": affected,
            "excluded": ["lower legs", "head", "dorsal mane", "outside torso band"]}


def soften_poll_stubs(body: bpy.types.Object) -> dict:
    """Tame residual central bristle tips without smoothing the ear shells."""
    group = body.vertex_groups.new(name="TEMP_Foal_Poll_Soften")
    affected = 0
    for vertex in body.data.vertices:
        point = vertex.co
        weight = (smoothstep(0.39, 0.42, point.x) *
                  (1.0 - smoothstep(0.52, 0.55, point.x)) *
                  smoothstep(1.32, 1.35, point.z) *
                  (1.0 - smoothstep(1.46, 1.49, point.z)) *
                  (1.0 - smoothstep(0.025, 0.048, abs(point.y))) )
        if weight > 0.02:
            group.add([vertex.index], weight, "REPLACE")
            affected += 1
    smooth = body.modifiers.new("Foal_Poll_Artifact_Soften", "SMOOTH")
    smooth.factor = 0.70
    smooth.iterations = 7
    smooth.vertex_group = group.name
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    group = body.vertex_groups.get("TEMP_Foal_Poll_Soften")
    if group is not None:
        body.vertex_groups.remove(group)
    body.data.update()
    return {"method": "7-iteration localized Smooth modifier on centered upper poll; ear shells excluded by lateral falloff",
            "factor": 0.70, "affectedVertices": affected,
            "region_m": {"x": [0.39, 0.55], "z": [1.32, 1.49], "absY_max": 0.048}}


def _distance_to_polyline(point: Vector, path: list[tuple[float, float]]) -> float:
    best = float("inf")
    for a2, b2 in zip(path, path[1:]):
        a, b = Vector(a2), Vector(b2)
        p = Vector((point.x, point.z))
        segment = b - a
        t = max(0.0, min(1.0, (p - a).dot(segment) / max(1e-10, segment.length_squared)))
        best = min(best, (p - (a + t * segment)).length)
    return best


def split_mesh_faces(name: str, source: bpy.types.Object, face_ids: list[int]) -> bpy.types.Object:
    mesh = source.data
    mesh.update()
    source_uv = mesh.uv_layers.get("UVMap")
    loop_normals = [normal.vector.copy() for normal in mesh.corner_normals]
    used = sorted({vertex for face_index in face_ids for vertex in mesh.polygons[face_index].vertices})
    remap = {old: new for new, old in enumerate(used)}
    verts = [mesh.vertices[index].co.copy() for index in used]
    faces = [[remap[index] for index in mesh.polygons[face_index].vertices] for face_index in face_ids]
    out_mesh = bpy.data.meshes.new(name + "Data")
    out_mesh.from_pydata(verts, [], faces)
    out_mesh.update()
    obj = bpy.data.objects.new(name, out_mesh)
    for collection in source.users_collection:
        collection.objects.link(obj)
    if source_uv:
        uv = out_mesh.uv_layers.new(name="UVMap")
        for out_poly, face_id in zip(out_mesh.polygons, face_ids):
            for out_loop, source_loop in zip(out_poly.loop_indices, mesh.polygons[face_id].loop_indices):
                uv.data[out_loop].uv = source_uv.data[source_loop].uv
    for poly in out_mesh.polygons:
        poly.use_smooth = True
    normals = []
    for face_id in face_ids:
        normals.extend(loop_normals[index] for index in mesh.polygons[face_id].loop_indices)
    out_mesh.normals_split_custom_set(normals)
    return obj


def remove_fin_and_cap_body(body: bpy.types.Object, source_mane_faces: set[int], root_boundary: dict) -> dict:
    """Remove the rejected foal bristle fin and close its Body opening."""
    original_faces = len(body.data.polygons)
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.faces.ensure_lookup_table()
    bm.faces.index_update()
    if max(source_mane_faces, default=-1) >= len(bm.faces):
        bm.free()
        raise RuntimeError("Foal source hair face map no longer matches the fixed decimated mesh")
    removed = [bm.faces[index] for index in sorted(source_mane_faces)]
    bmesh.ops.delete(bm, geom=removed, context="FACES_ONLY")
    boundary_edges = [edge for edge in bm.edges if len(edge.link_faces) == 1]
    if len(boundary_edges) != len(root_boundary["sourceEdges"]):
        bm.free()
        raise RuntimeError(f"Foal hair cut boundary changed: {len(boundary_edges)} != {len(root_boundary['sourceEdges'])}")
    # Cap each simple connected boundary loop independently. An automatic
    # holes_fill can bridge nearby loops in this source and make a four-face
    # edge, so use one low center fan per loop and avoid a raised cut ridge.
    remaining_set = set(boundary_edges)
    cap_face_count = 0
    cap_centers = []
    while remaining_set:
        seed = remaining_set.pop()
        component, stack, vertices = {seed}, [seed], set(seed.verts)
        while stack:
            edge = stack.pop()
            for vertex in edge.verts:
                for linked in vertex.link_edges:
                    if linked in remaining_set:
                        remaining_set.remove(linked)
                        component.add(linked)
                        stack.append(linked)
                        vertices.update(linked.verts)
        degree = {vertex: sum(1 for edge in vertex.link_edges if edge in component) for vertex in vertices}
        if any(value != 2 for value in degree.values()):
            details = [(tuple(round(value, 6) for value in vertex.co), count) for vertex, count in degree.items() if count != 2]
            bm.free()
            raise RuntimeError(f"Foal source cut loop is still branched after regularization: {details[:8]}")
        first = seed.verts[0]
        ordered = [first]
        previous_edge = None
        current_vertex = first
        while True:
            candidates = [edge for edge in current_vertex.link_edges if edge in component and edge is not previous_edge]
            if not candidates:
                break
            edge = candidates[0]
            next_vertex = edge.other_vert(current_vertex)
            if next_vertex is first:
                break
            ordered.append(next_vertex)
            previous_edge, current_vertex = edge, next_vertex
        if len(ordered) != len(component):
            bm.free()
            raise RuntimeError(f"Foal source cap boundary walk mismatch: {len(ordered)} verts / {len(component)} edges")
        center = sum((vertex.co for vertex in ordered), Vector()) / len(ordered)
        # The source cut is not planar and its average center formed a visible
        # spike. Bias it slightly toward the low side of the ring to make a
        # shallow, closed patch that the following local surface pass can blend.
        center.z = min(vertex.co.z for vertex in ordered) + 0.40 * (
            max(vertex.co.z for vertex in ordered) - min(vertex.co.z for vertex in ordered))
        center_vertex = bm.verts.new(center)
        cap_centers.append([round(value, 6) for value in center])
        for index, vertex in enumerate(ordered):
            next_vertex = ordered[(index + 1) % len(ordered)]
            try:
                bm.faces.new((center_vertex, vertex, next_vertex))
                cap_face_count += 1
            except ValueError as exc:
                bm.free()
                raise RuntimeError(f"Foal Body cap triangle creation failed at loop edge {index}: {exc}") from exc
    orphan_edges = [edge for edge in bm.edges if not edge.link_faces]
    if orphan_edges:
        bmesh.ops.delete(bm, geom=orphan_edges, context="EDGES")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bad_edges = [edge for edge in bm.edges if len(edge.link_faces) != 2]
    remaining_boundaries = len(bad_edges)
    if remaining_boundaries:
        details = [{"faces": len(edge.link_faces), "coords": [[round(value, 6) for value in vertex.co] for vertex in edge.verts]} for edge in bad_edges[:8]]
        bm.free()
        raise RuntimeError(f"Foal Body fin cut remains open/nonmanifold at {remaining_boundaries} edges: {details}")
    bm.normal_update()
    bm.to_mesh(body.data)
    bm.free()
    for poly in body.data.polygons:
        poly.use_smooth = True
    body.data.update()
    return {"sourceHairFacesRemoved": len(source_mane_faces),
            "sourceCutEdges": len(root_boundary["sourceEdges"]),
            "capTriangles": cap_face_count, "capCenters_m": cap_centers,
            "capMethod": "per-loop center fan with center biased toward lower boundary to avoid raised ridge",
            "finalBodyFaces": len(body.data.polygons),
            "openOrNonmanifoldEdgesAfterCap": remaining_boundaries,
            "method": "guarded source hair faces removed; cut closed by a triangulated Body cap"}


def regularize_foal_cut_junctions(body: bpy.types.Object, regions: dict, anatomy) -> tuple[dict, dict]:
    """Make the approved branched source cut into simple local cap loops."""
    mane = set(regions["Mane"])
    junctions = []
    for _ in range(3):
        active = {"Mane": mane, "Forelock": set(regions["Forelock"])}
        boundary = anatomy.foal_mane_root_boundary(body, active)
        degree = {}
        for a, b in boundary["sourceEdges"]:
            degree[a] = degree.get(a, 0) + 1
            degree[b] = degree.get(b, 0) + 1
        branches = sorted(vertex for vertex, valence in degree.items() if valence > 2)
        if not branches:
            return active, {"method": "all source faces incident to the guarded degree-four cut junction assigned to the removal side",
                            "junctionVerticesExpanded": junctions,
                            "sourceManeFaces": len(regions["Mane"]),
                            "effectiveManeFaces": len(mane),
                            "addedClosureFaces": len(mane - set(regions["Mane"])),
                            "resultingBoundaryEdges": len(boundary["sourceEdges"]),
                            "maximumBoundaryValence": max(degree.values(), default=0)}
        for vertex_index in branches:
            incident = [poly.index for poly in body.data.polygons if vertex_index in poly.vertices]
            if not incident:
                raise RuntimeError(f"Foal cut junction vertex {vertex_index} has no incident faces")
            mane.update(incident)
            junctions.append({"sourceVertex": vertex_index, "expandedIncidentFaces": incident})
    raise RuntimeError("Foal source hair cut still has a branched boundary after local junction regularization")


def classify_residual_centerline_fin(body: bpy.types.Object, regions: dict) -> tuple[dict, dict]:
    """Remove the fixed-source central poll bristle island missed by side traces."""
    mapped = set(regions["Mane"])
    extra = set()
    for polygon in body.data.polygons:
        if polygon.index in mapped:
            continue
        center = sum((body.data.vertices[index].co for index in polygon.vertices), Vector()) / len(polygon.vertices)
        # This narrow centerline region is behind the brow and between the
        # measured ear roots; it excludes the bilateral ear shells.
        if (0.405 <= center.x <= 0.48 and abs(center.y) <= 0.035
                and center.z >= 1.35):
            extra.add(polygon.index)
    regions = dict(regions)
    regions["Mane"] = mapped | extra
    digest = hashlib.sha256(json.dumps(sorted(extra), separators=(",", ":")).encode()).hexdigest()
    report = {"method": "fixed-source residual central fin patch between measured ear roots; x/z/lateral bounded polygon centroid filter",
              "sourceFaceCount": len(extra), "sourceFaceIdsSha256": digest,
              "region_m": {"x": [0.405, 0.48], "z_min": 1.35, "absY_max": 0.035},
              "sourceFaceIds": sorted(extra)}
    return regions, report


def _curve_mesh(name: str, splines: list[tuple[list[Vector], list[float]]], bevel: float) -> bpy.types.Object:
    curve = bpy.data.curves.new(name + "Curve", type="CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 3
    curve.bevel_depth = bevel
    curve.bevel_resolution = 3
    for points, radii in splines:
        spline = curve.splines.new("POLY")
        spline.points.add(len(points) - 1)
        for item, point, radius in zip(spline.points, points, radii):
            item.co = (point.x, point.y, point.z, 1.0)
            item.radius = radius
    obj = bpy.data.objects.new(name, curve)
    (bpy.context.scene.collection).objects.link(obj)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.name, obj.data.name = name, name
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def _top_surface_path(body: bpy.types.Object, xs: list[float], *, y: float = 0.0, lift: float = 0.010) -> list[Vector]:
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    result = []
    for x in xs:
        hit = tree.ray_cast(Vector((x, y, 1.7)), Vector((0.0, 0.0, -1.0)), 1.7)
        if hit[0] is None:
            raise RuntimeError(f"Could not find the foal dorsal surface at x={x}, y={y}")
        point = hit[0]
        result.append(Vector((point.x, point.y, point.z + lift)))
    return result


def _forelock_patch(body: bpy.types.Object) -> bpy.types.Object:
    """Create one shallow, scalp-conforming juvenile forelock with three lobes."""
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    # The larger earlier sampling span crossed the bilateral ear shells. Keep
    # the patch inside the measured central scalp corridor between the ears.
    lateral = [-0.018, -0.0135, -0.009, -0.0045, 0.0, 0.0045, 0.009, 0.0135, 0.018]
    longitudinal = [0.0, 0.18, 0.36, 0.54, 0.70, 0.84, 0.94, 1.0]

    def scalp(x: float, y: float) -> Vector:
        hit = tree.ray_cast(Vector((x, y, 1.7)), Vector((0.0, 0.0, -1.0)), 1.7)
        if hit[0] is None:
            raise RuntimeError(f"Could not fit forelock patch to scalp at x={x}, y={y}")
        point, normal = hit[0], hit[1].normalized()
        if abs(y) > 0.01801 or not 1.27 <= point.z <= 1.36 or normal.z < 0.25:
            raise RuntimeError(f"Forelock scalp ray entered a lateral/steep/non-skull surface at {tuple(point)} with normal {tuple(normal)}")
        return point

    top = []
    for t in longitudinal:
        for y in lateral:
            edge = abs(y) / 0.040
            root_x = 0.463 + 0.004 * edge
            # Three soft forward lobes end just behind the eye sockets.
            tip_x = (0.492 +
                     0.033 * math.exp(-((y - 0.020) / 0.010) ** 2) +
                     0.033 * math.exp(-((y + 0.020) / 0.010) ** 2) +
                     0.028 * math.exp(-(y / 0.010) ** 2))
            x = root_x + (tip_x - root_x) * t
            surface = scalp(x, y)
            dome = math.sqrt(max(0.0, 1.0 - edge * edge))
            lift = 0.0015 + 0.010 * math.sin(math.pi * t) * dome
            top.append(Vector((surface.x, surface.y, surface.z + lift)))

    row_size = len(lateral)
    count = len(top)
    bottom = [point + Vector((0.0, 0.0, -0.0035)) for point in top]
    vertices = [tuple(point) for point in top + bottom]
    faces = []
    for row in range(len(longitudinal) - 1):
        for col in range(row_size - 1):
            a = row * row_size + col
            b = a + 1
            c = a + row_size + 1
            d = a + row_size
            faces.append((a, b, c, d))
            faces.append((count + d, count + c, count + b, count + a))
    perimeter = ([i for i in range(row_size)] +
                 [row * row_size + row_size - 1 for row in range(1, len(longitudinal))] +
                 [((len(longitudinal) - 1) * row_size + col) for col in range(row_size - 2, -1, -1)] +
                 [row * row_size for row in range(len(longitudinal) - 2, 0, -1)])
    for index, current in enumerate(perimeter):
        following = perimeter[(index + 1) % len(perimeter)]
        faces.append((current, count + current, count + following, following))
    mesh = bpy.data.meshes.new("FoalForelockPatchData")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("FoalForelockPatch", mesh)
    bpy.context.scene.collection.objects.link(obj)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    forelock_group = obj.vertex_groups.new(name="TEMP_Foal_Forelock")
    forelock_group.add(list(range(len(vertices))), 1.0, "REPLACE")
    return obj


def create_mane(body: bpy.types.Object) -> tuple[bpy.types.Object, dict]:
    # The previous path began on the rump and ran down the face. This shorter,
    # narrow crest starts at the withers and follows the juvenile neck to poll;
    # the forelock is a short curved three-strand patch rooted at the crown.
    crest = _top_surface_path(body, [0.08, 0.16, 0.24, 0.32, 0.40, 0.48, 0.515], lift=0.008)
    # The poll-end is tucked underneath the sculpted patch so it cannot read
    # as an isolated forehead pin when viewed head-on.
    crest[-1].z -= 0.006
    crest_obj = _curve_mesh("Mane", [(crest, [0.08, 0.48, 0.74, 0.90, 0.84, 0.55, 0.12])], bevel=0.010)
    patch_obj = _forelock_patch(body)
    bpy.ops.object.select_all(action="DESELECT")
    crest_obj.select_set(True)
    patch_obj.select_set(True)
    bpy.context.view_layer.objects.active = crest_obj
    bpy.ops.object.join()
    mane = bpy.context.object
    mane.name, mane.data.name = "Mane", "Mane"
    add_uv0(mane)
    return mane, {"method": "neck crest plus a closed three-lobed forelock patch conformed to the edited scalp",
                  "splineCount": 1, "forelockPatch": "single scalp-conforming closed mesh with three forward lobes",
                  "forelockBoundaryProfile": "three eased forward points; narrow at temples, widened behind forehead",
                  "sourceFinFacesRemoved": True,
                  "crestLength_m": round(sum((b-a).length for a,b in zip(crest,crest[1:])), 5),
                  "root_m": [round(v,5) for v in crest[0]],
                  "pollEnd_m": [round(v,5) for v in crest[-1]],
                  "forelockPatchBounds_m": {"x": [0.463, 0.527], "y": [-0.018, 0.018]},
                  "scalpFit": {"method": "vertical evaluated-Body rays inside measured central crown corridor",
                               "maxAbsY_m": 0.018, "acceptedSurfaceZ_m": [1.27, 1.37],
                               "minimumUpNormalZ": 0.25, "earShellsExcluded": True},
                  "forelockMaxDome_m": 0.010, "forelockThickness_m": 0.0035,
                  "surfaceLift_m": {"crest": 0.008, "forelockRoot": 0.0015}, "bevelRadius_m": 0.010}


def add_uv0(obj: bpy.types.Object) -> None:
    if obj.data.uv_layers.get("UVMap"):
        obj.data.uv_layers.remove(obj.data.uv_layers.get("UVMap"))
    obj.data.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.015, area_weight=0.0,
                             angle_limit=math.radians(66.0), correct_aspect=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.data.uv_layers.active = obj.data.uv_layers.get("UVMap")


def create_eyes(body: bpy.types.Object) -> tuple[bpy.types.Object, dict]:
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    eyes = []
    report = []
    # The lower/posterior centers come from the independently ray-inspected
    # almond socket, rather than the earlier high/anterior cheek probe.
    for side in (-1, 1):
        # This is 10 mm posterior and 12 mm below the previous bead center,
        # aligning it with the center of the visible almond recess.
        probe = Vector((0.558, side * 0.22, 1.232))
        surface, normal, _face, _distance = tree.ray_cast(probe, Vector((0.0, -float(side), 0.0)), 0.5)
        if surface is None:
            raise RuntimeError(f"Could not ray-fit foal eye socket on side {side}")
        normal = normal.normalized()
        if normal.y * side < 0.50 or abs(surface.x - 0.574) > 0.045 or abs(surface.z - 1.245) > 0.035:
            raise RuntimeError(f"Foal eye ray missed the lower almond socket: {tuple(surface)}, {tuple(normal)}")
        center = surface - normal * 0.0030
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16, radius=1.0)
        eye = bpy.context.object
        eye.name = f"Foal_Eye_{side}"
        rotation = Vector((0.0, float(side), 0.0)).rotation_difference(normal)
        for vertex in eye.data.vertices:
            q = vertex.co.copy()
            vertex.co = center + rotation @ Vector((q.x * 0.009, q.y * 0.0038, q.z * 0.005))
        eyes.append(eye)
        report.append({"side": "left" if side > 0 else "right",
                       "socketSurface_m": [round(v, 5) for v in surface],
                       "normal": [round(v, 5) for v in normal],
                       "center_m": [round(v, 5) for v in center],
                       "semiAxes_m": [0.009, 0.0038, 0.005], "recess_m": 0.0030,
                       "basis": "same anatomical x/z center ray-fitted on both lateral socket faces"})
    bpy.ops.object.select_all(action="DESELECT")
    for eye in eyes:
        eye.select_set(True)
    bpy.context.view_layer.objects.active = eyes[0]
    bpy.ops.object.join()
    eyes_obj = bpy.context.object
    eyes_obj.name = "Eyes"
    eyes_obj.data.name = "Eyes"
    uv = eyes_obj.data.uv_layers.new(name="UVMap")
    for poly in eyes_obj.data.polygons:
        poly.use_smooth = True
    return eyes_obj, {"method": "bilateral horizontal rays into the lower/posterior almond socket references", "sides": report}


def make_tail(body: bpy.types.Object) -> tuple[bpy.types.Object, dict]:
    """Author one tapered juvenile tuft rooted to the actual rump surface."""
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    rump_vertices = [vertex.co for vertex in body.data.vertices
                     if vertex.co.z > 0.72 and abs(vertex.co.y) < 0.06 and vertex.co.x < 0.0]
    if len(rump_vertices) < 8:
        raise RuntimeError("Foal source has too few centered upper-rump vertices for a tail attachment")
    rear_x = min(point.x for point in rump_vertices)
    root_z = max(point.z for point in rump_vertices if point.x < rear_x + 0.18)
    # Cast from behind the rump, through the body. This gives an actual rear
    # silhouette contact rather than a generic nearest point on the haunch.
    ray_origin = Vector((rear_x - 0.10, 0.0, root_z - 0.015))
    hit = tree.ray_cast(ray_origin, Vector((1.0, 0.0, 0.0)), 0.35)
    if hit is None or hit[0] is None:
        raise RuntimeError("Could not fit juvenile tail root to the actual rump surface")
    root_surface, root_normal = hit[0], hit[1].normalized()
    if root_surface.x >= 0.0 or root_surface.z < 0.80 or abs(root_surface.y) > 0.02:
        raise RuntimeError(f"Tail root ray missed the centered rump: {tuple(root_surface)}")
    root = root_surface + root_normal * 0.001
    # Follow the rump surface outward instead of growing down into it. The
    # small downward component keeps a natural hanging juvenile tail silhouette.
    points = [root,
              root + Vector((-0.030, 0.0, -0.012)),
              root + Vector((-0.070, 0.0, -0.036)),
              root + Vector((-0.130, 0.0, -0.070))]
    tail = _curve_mesh("Tail", [(points, [0.75, 0.68, 0.36, 0.025])], bevel=0.015)
    add_uv0(tail)
    return tail, {"method": "single short tapered juvenile tuft rooted at centered rear-rump ray hit",
                  "strandCount": 1, "length_m": round(sum((b-a).length for a,b in zip(points,points[1:])), 5),
                  "surfaceRoot_m": [round(v, 5) for v in root_surface],
                  "surfaceNormal": [round(v, 5) for v in root_normal],
                  "status": "new authored tail tuft; not adult tail geometry",
                  "rootRayOrigin_m": [round(v, 5) for v in ray_origin],
                  "surfaceToTuftOffset_m": 0.001}


def _surface_weight_sampler(body: bpy.types.Object, rig: bpy.types.Object):
    """Barycentrically interpolate Body skin weights on its nearest surface triangle."""
    body.data.calc_loop_triangles()
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    group_names = {group.index: group.name for group in body.vertex_groups}
    allowed = set(rig.data.bones.keys())
    body_weights = []
    for vertex in body.data.vertices:
        body_weights.append({group_names[item.group]: float(item.weight)
                             for item in vertex.groups
                             if item.group in group_names and group_names[item.group] in allowed
                             and item.weight > 1e-8})

    triangles_by_polygon = {}
    for triangle in body.data.loop_triangles:
        triangles_by_polygon.setdefault(triangle.polygon_index, []).append(tuple(triangle.vertices))

    def barycentric(point: Vector, a: Vector, b: Vector, c: Vector) -> tuple[float, float, float]:
        v0, v1, v2 = b-a, c-a, point-a
        d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
        d20, d21 = v2.dot(v0), v2.dot(v1)
        denom = d00*d11 - d01*d01
        if abs(denom) < 1e-12:
            return (1.0, 0.0, 0.0)
        second = (d11*d20 - d01*d21) / denom
        third = (d00*d21 - d01*d20) / denom
        first = 1.0 - second - third
        values = [max(0.0, min(1.0, value)) for value in (first, second, third)]
        total = sum(values)
        return tuple(value / total for value in values)

    def sample(point: Vector) -> tuple[dict[str, float], dict]:
        result = tree.find_nearest(point)
        if result is None or result[0] is None or result[2] < 0:
            raise RuntimeError("Body surface weight interpolation found no polygon")
        nearest_point, _, polygon_index, distance = result
        best = None
        for tri in triangles_by_polygon.get(polygon_index, ()):
            coords = [body.data.vertices[index].co for index in tri]
            surface_point = geometry.closest_point_on_tri(point, *coords)
            tri_distance = (surface_point-point).length
            if best is None or tri_distance < best[0]:
                best = (tri_distance, tri, barycentric(surface_point, *coords))
        if best is None:
            raise RuntimeError(f"Body polygon {polygon_index} had no triangulated surface")
        surface_distance, indices, blend = best
        weights = {}
        for index, amount in zip(indices, blend):
            for name, value in body_weights[index].items():
                weights[name] = weights.get(name, 0.0) + amount * value
        total = sum(weights.values())
        if total <= 1e-8:
            raise RuntimeError(f"No deforming Body skin weights near accessory point {tuple(point)}")
        return {name: value / total for name, value in weights.items()}, {
            "sourceVertexIndices": list(indices),
            "sourceInterpolation": [round(amount, 6) for amount in blend],
            "nearestSurfaceDistance_m": round(surface_distance, 6),
        }
    return sample


def _normalize_influences(weights: dict[str, float], maximum: int = 4) -> dict[str, float]:
    selected = sorted(((name, max(0.0, value)) for name, value in weights.items() if value > 1e-7),
                      key=lambda item: item[1], reverse=True)[:maximum]
    total = sum(value for _, value in selected)
    if total <= 1e-8:
        raise RuntimeError("Accessory weight blend removed every skin influence")
    return {name: value / total for name, value in selected}


def _lerp_weights(base: dict[str, float], target: dict[str, float], amount: float) -> dict[str, float]:
    amount = max(0.0, min(1.0, amount))
    result = {name: value * (1.0 - amount) for name, value in base.items()}
    for name, value in target.items():
        result[name] = result.get(name, 0.0) + value * amount
    return _normalize_influences(result)


def _set_vertex_weights(obj: bpy.types.Object, vertex_index: int, weights: dict[str, float],
                        group_cache: dict[str, bpy.types.VertexGroup]) -> None:
    for name, value in weights.items():
        group = group_cache.get(name)
        if group is None:
            group = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
            group_cache[name] = group
        group.add([vertex_index], value, "REPLACE")


def reparent_head_to_head_base(rig: bpy.types.Object) -> dict:
    """Make the actual deforming Head inherit the cervical chain, without moving rest joints."""
    if rig is None or rig.type != "ARMATURE":
        raise TypeError("Head hierarchy repair expects the adapted foal Skeleton armature")
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    head, parent = edit.get("Head"), edit.get("Head_Base")
    if head is None or parent is None:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError("Foal rig needs both Head and Head_Base before local hierarchy repair")
    old_parent = head.parent.name if head.parent else None
    before = {"head": tuple(head.head), "tail": tuple(head.tail), "roll": float(head.roll)}
    head.use_connect = False
    head.parent = parent
    # Explicitly restore the original armature-space rest geometry and roll.
    # Blender edit-bone coordinates are armature-local; setting only parent
    # changes inheritance, not the saved bind-pose location.
    head.head, head.tail, head.roll = before["head"], before["tail"], before["roll"]
    bpy.context.view_layer.update()
    after = {"head": tuple(head.head), "tail": tuple(head.tail), "roll": float(head.roll)}
    max_rest_delta = max([abs(a-b) for a,b in zip(before["head"], after["head"])] +
                         [abs(a-b) for a,b in zip(before["tail"], after["tail"])] +
                         [abs(before["roll"]-after["roll"])])
    if head.parent is None or head.parent.name != "Head_Base" or head.use_connect:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError("Foal Head was not reparented as an unconnected child of Head_Base")
    if max_rest_delta > 1e-7:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"Foal Head rest pose changed during hierarchy repair by {max_rest_delta}")
    bpy.ops.object.mode_set(mode="OBJECT")
    return {"bone": "Head", "oldParent": old_parent, "newParent": "Head_Base",
            "useConnect": False, "restHead_m": [round(v, 8) for v in after["head"]],
            "restTail_m": [round(v, 8) for v in after["tail"]],
            "restRoll_rad": round(after["roll"], 9), "maxRestPoseDelta": max_rest_delta,
            "reason": "Head had bypassed the Neck hierarchy by parenting directly to Pelvis; Head_Base inherits Neck"}


def reparent_cervical_bone_to_neck(rig: bpy.types.Object) -> dict:
    """Put the displaced cervical deformer under Neck without changing its rest frame."""
    bone_name, parent_name, child_name = "FoalSmart_Bone_013", "Neck", "FoalSmart_Bone_012"
    if rig is None or rig.type != "ARMATURE":
        raise TypeError("Foal cervical hierarchy repair expects Skeleton")
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    bone, parent = edit.get(bone_name), edit.get(parent_name)
    if bone is None or parent is None:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"Foal rig requires {bone_name} and {parent_name}")
    old_parent = bone.parent.name if bone.parent else None
    old_children = sorted(child.name for child in bone.children)
    before = {"head": tuple(bone.head), "tail": tuple(bone.tail), "roll": float(bone.roll)}
    bone.parent = parent
    bone.use_connect = False
    bone.head, bone.tail, bone.roll = before["head"], before["tail"], before["roll"]
    bpy.context.view_layer.update()
    after = {"head": tuple(bone.head), "tail": tuple(bone.tail), "roll": float(bone.roll)}
    max_delta = max([abs(a-b) for a,b in zip(before["head"],after["head"])] +
                    [abs(a-b) for a,b in zip(before["tail"],after["tail"])] +
                    [abs(before["roll"]-after["roll"])])
    new_children = sorted(child.name for child in bone.children)
    if bone.parent is None or bone.parent.name != parent_name or bone.use_connect:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"{bone_name} did not become an unconnected child of {parent_name}")
    if child_name not in new_children or child_name not in old_children:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"{bone_name} lost its intended cervical child {child_name}: {new_children}")
    if max_delta > 1e-7:
        bpy.ops.object.mode_set(mode="OBJECT")
        raise RuntimeError(f"{bone_name} rest frame changed by {max_delta}")
    bpy.ops.object.mode_set(mode="OBJECT")
    return {"bone": bone_name, "oldParent": old_parent, "newParent": parent_name,
            "useConnect": False, "childrenPreserved": new_children,
            "restHead_m": [round(v, 8) for v in after["head"]],
            "restTail_m": [round(v, 8) for v in after["tail"]],
            "restRoll_rad": round(after["roll"], 9), "maxRestPoseDelta": max_delta,
            "reason": "Bone_013 carries cervical skin but bypassed Neck under Pelvis, causing a cervical shelf during Neck rotation"}


def normalize_foal_skin_group_names(rig: bpy.types.Object,
                                    meshes: list[bpy.types.Object]) -> dict:
    """Repair importer double-prefix labels without dropping skin influence mass."""
    bone_names = set(rig.data.bones.keys())
    changes = []
    merges = []
    unresolved = []
    mass_by_mesh = {}
    for mesh in meshes:
        group_names = {group.index: group.name for group in mesh.vertex_groups}
        rename = {}
        original_group_mass = {group.index: 0.0 for group in mesh.vertex_groups}
        original_group_vertices = {group.index: set() for group in mesh.vertex_groups}
        for group in mesh.vertex_groups:
            name = group.name
            candidate = name[len("FoalSmart_"):] if name.startswith("FoalSmart_FoalSmart_") else name
            if candidate in bone_names:
                rename[group.index] = candidate
            else:
                rename[group.index] = name

        assignments = []
        lost_mass = 0.0
        pre_total_mass = 0.0
        post_total_mass = 0.0
        unresolved_group_mass = {}
        unresolved_group_vertices = {}
        merge_counts = {}
        merge_mass = {}
        for vertex in mesh.data.vertices:
            weights = {}
            source_names = {}
            for item in vertex.groups:
                if item.weight <= 1e-8:
                    continue
                original_name = group_names[item.group]
                original_group_mass[item.group] += float(item.weight)
                original_group_vertices[item.group].add(vertex.index)
                mapped = rename[item.group]
                if mapped not in bone_names:
                    unresolved_group_mass[original_name] = unresolved_group_mass.get(original_name, 0.0) + float(item.weight)
                    unresolved_group_vertices.setdefault(original_name, set()).add(vertex.index)
                    continue
                weights[mapped] = weights.get(mapped, 0.0) + float(item.weight)
                source_names.setdefault(mapped, set()).add(original_name)
            pre_total_mass += sum(weights.values())
            for mapped, names in source_names.items():
                if len(names) > 1:
                    merge_counts[mapped] = merge_counts.get(mapped, 0) + 1
                    merge_mass[mapped] = merge_mass.get(mapped, 0.0) + weights[mapped]
            if len(weights) > 4:
                selected = sorted(weights.items(), key=lambda row: (-row[1], row[0]))[:4]
                kept = sum(value for _, value in selected)
                lost_mass += sum(weights.values()) - kept
                weights = dict(selected)
            total = sum(weights.values())
            if total <= 1e-8:
                assignments.append([])
                continue
            normalized = {joint: value / total for joint, value in weights.items()}
            units_f = {joint: value * 255.0 for joint, value in normalized.items()}
            units = {joint: int(math.floor(value)) for joint, value in units_f.items()}
            remainder = 255 - sum(units.values())
            for joint in sorted(units_f, key=lambda key: (-(units_f[key] - units[key]), key))[:remainder]:
                units[joint] += 1
            row = [(joint, unit) for joint, unit in units.items() if unit > 0]
            post_total_mass += sum(unit / 255.0 for _, unit in row)
            assignments.append(row)

        if unresolved_group_mass:
            for name, mass in sorted(unresolved_group_mass.items()):
                unresolved.append({"mesh": mesh.name, "group": name,
                                   "influencedVertexCount": len(unresolved_group_vertices[name]),
                                   "weightMassBefore": round(mass, 9),
                                   "weightMassAfter": 0.0,
                                   "unresolvedWeightMass": round(mass, 9)})

        for group_index, name in group_names.items():
            mapped = rename[group_index]
            if mapped != name:
                changes.append({"mesh": mesh.name, "from": name, "to": mapped,
                                "influencedVertexCount": len(original_group_vertices[group_index]),
                                "weightMassBefore": round(original_group_mass[group_index], 9),
                                "weightMassAfter": round(original_group_mass[group_index], 9),
                                "lostWeightMass": 0.0})

        # Replace the complete group table transactionally. This also merges
        # aliases safely when the canonical target group already exists.
        for group in list(mesh.vertex_groups):
            mesh.vertex_groups.remove(group)
        by_joint_units = {}
        for vertex_index, row in enumerate(assignments):
            for joint, unit in row:
                by_joint_units.setdefault(joint, {}).setdefault(unit, []).append(vertex_index)
        for joint, buckets in by_joint_units.items():
            target = mesh.vertex_groups.new(name=joint)
            for unit, indices in buckets.items():
                target.add(indices, unit / 255.0, "REPLACE")
        post_joint_mass = {joint: sum(unit * len(indices) / 255.0 for unit, indices in buckets.items())
                           for joint, buckets in by_joint_units.items()}
        post_joint_vertices = {joint: len({index for indices in buckets.values() for index in indices})
                               for joint, buckets in by_joint_units.items()}
        for change in changes:
            if change["mesh"] == mesh.name:
                joint = change["to"]
                change["exportVisibleJoint"] = joint
                change["influencedVertexCountAfter"] = post_joint_vertices.get(joint, 0)
                change["weightMassAfter"] = round(post_joint_mass.get(joint, 0.0), 9)
        mass_by_mesh[mesh.name] = {
            "positiveInfluenceMassBefore": round(pre_total_mass, 9),
            "positiveInfluenceMassAfter": round(post_total_mass, 9),
            "difference": round(post_total_mass - pre_total_mass, 9),
            "lostWeightMassBeforeRenormalization": round(lost_mass, 9),
        }
        for joint, count in sorted(merge_counts.items()):
            source_names = sorted(name for index, name in group_names.items() if rename[index] == joint)
            if len(source_names) > 1:
                merges.append({"mesh": mesh.name, "sourceGroups": source_names,
                           "into": joint, "affectedVertexCount": count,
                           "mergedWeightMass": round(merge_mass[joint], 9),
                           "lostWeightMassBeforeRenormalization": round(lost_mass, 9),
                           "maxInfluencesAfter": 4})
    if unresolved:
        raise RuntimeError(f"Foal skin contains non-bone vertex groups after adapter normalization: {unresolved}")
    return {"method": "strip one duplicate FoalSmart_ prefix only when the result names an existing rig bone",
            "renamedGroups": changes, "mergedGroups": merges,
            "influenceMassByMesh": mass_by_mesh,
            "unresolvedWeightedGroups": unresolved,
            "failClosed": True}


def bind_authored_hair(rig: bpy.types.Object, body: bpy.types.Object,
                       mane: bpy.types.Object, tail: bpy.types.Object,
                       mane_root: Vector, tail_root: Vector,
                       forelock_vertex_indices: set[int] | None = None) -> dict:
    """Interpolate Body root weights, then blend authored hair into its chains."""
    available = set(rig.data.bones.keys())
    required = {"Neck_Base", "Neck", "Head", "Tail_01", "Tail_02"}
    if not required <= available:
        raise RuntimeError(f"Foal rig lacks authored-hair attachment bones: {sorted(required-available)}")

    def clear_groups(obj: bpy.types.Object) -> None:
        for group in list(obj.vertex_groups):
            obj.vertex_groups.remove(group)

    forelock_group = mane.vertex_groups.get("TEMP_Foal_Forelock")
    forelock_vertices = set(forelock_vertex_indices or ())
    if not forelock_vertices and forelock_group is not None:
        forelock_vertices = {vertex.index for vertex in mane.data.vertices
                             if any(item.group == forelock_group.index and item.weight >= 0.5
                                    for item in vertex.groups)}
    if any(index < 0 or index >= len(mane.data.vertices) for index in forelock_vertices):
        raise RuntimeError("Foal forelock vertex correspondence changed before attachment binding")
    clear_groups(mane)
    sampler = _surface_weight_sampler(body, rig)
    mane_groups = {group.name: group for group in mane.vertex_groups}
    assignments = []
    mane_source_assignments = []
    source_reports = []
    head_blends = []
    for vertex in mane.data.vertices:
        base, source_report = sampler(vertex.co)
        mane_source_assignments.append(base)
        source_reports.append(source_report)
        if vertex.index in forelock_vertices:
            t = max(0.0, min(1.0, (vertex.co.x - 0.463) / 0.064))
            amount = 0.45 + 0.55 * t
            assigned = _lerp_weights(base, {"Head": 1.0}, amount)
            head_blends.append(amount)
        else:
            t = max(0.0, min(1.0, (vertex.co.x - 0.08) / (0.515 - 0.08)))
            target = {"Neck_Base": 1.0} if t < 0.48 else (
                {"Neck": 1.0} if t < 0.90 else {"Head": 1.0})
            amount = 0.20 + 0.50 * t
            assigned = _lerp_weights(base, target, amount)
        assignments.append(assigned)
    for index, assigned in enumerate(assignments):
        _set_vertex_weights(mane, index, assigned, mane_groups)
    if mane.vertex_groups.get("TEMP_Foal_Forelock"):
        mane.vertex_groups.remove(mane.vertex_groups.get("TEMP_Foal_Forelock"))

    clear_groups(tail)
    tail_groups = {}
    tail_assignments = []
    tail_source_assignments = []
    tail_source_reports = []
    for vertex in tail.data.vertices:
        base, source_report = sampler(vertex.co)
        tail_source_assignments.append(base)
        tail_source_reports.append(source_report)
        distance = (vertex.co - tail_root).length
        progress = max(0.0, min(1.0, distance / 0.17))
        target = {"Tail_01": 1.0 - progress, "Tail_02": progress}
        assigned = _lerp_weights(base, target, min(1.0, distance / 0.085))
        tail_assignments.append(assigned)
    for index, assigned in enumerate(tail_assignments):
        _set_vertex_weights(tail, index, assigned, tail_groups)

    def average_influences(rows):
        totals = {}
        for row in rows:
            for name, value in row.items():
                totals[name] = totals.get(name, 0.0) + value / max(1, len(rows))
        return {name: round(value, 6) for name, value in sorted(totals.items(), key=lambda item: -item[1]) if value > 0.001}
    forelock_assignments = [row for i,row in enumerate(assignments) if i in forelock_vertices]
    forelock_source_assignments = [row for i,row in enumerate(mane_source_assignments) if i in forelock_vertices]
    crest_assignments = [row for i,row in enumerate(assignments) if i not in forelock_vertices]
    root_mane_ids = [i for i,v in enumerate(mane.data.vertices)
                     if i not in forelock_vertices and (v.co-mane_root).length < 0.025]
    root_mane = [assignments[i] for i in root_mane_ids]
    root_mane_source = [mane_source_assignments[i] for i in root_mane_ids]
    tail_root_ids = [i for i,v in enumerate(tail.data.vertices) if (v.co-tail_root).length < 0.018]
    tail_root_assignments = [tail_assignments[i] for i in tail_root_ids]
    tail_root_source = [tail_source_assignments[i] for i in tail_root_ids]
    max_influences = max([len(row) for row in assignments + tail_assignments] or [0])
    if not math.isfinite(max_influences) or max_influences > 4:
        raise RuntimeError(f"Authored hair exceeded the four-influence limit: {max_influences}")
    return {"method": "barycentric Body-surface weight interpolation at hair vertices, followed by a smooth local anatomical-chain blend",
            "maneVertexCount": len(assignments), "tailVertexCount": len(tail_assignments),
            "forelockVertices": len(forelock_vertices),
            "forelockVertexIndices": sorted(forelock_vertices),
            "forelockRootBlendedFromBody": True,
            "forelockBlendAmount": {"min": round(min(head_blends),6) if head_blends else 0.0,
                                    "max": round(max(head_blends),6) if head_blends else 0.0},
            "forelockRootInterpolatedBodyInfluences": average_influences(forelock_source_assignments),
            "forelockAverageFinalInfluences": average_influences(forelock_assignments),
            "crestAverageFinalInfluences": average_influences(crest_assignments),
            "maneRootVertexCount": len(root_mane_ids),
            "maneRootInterpolatedBodyInfluences": average_influences(root_mane_source),
            "maneRootAverageFinalInfluences": average_influences(root_mane),
            "tailRootVertexCount": len(tail_root_ids),
            "tailRootInterpolatedBodyInfluences": average_influences(tail_root_source),
            "tailRootAverageFinalInfluences": average_influences(tail_root_assignments),
            "bodyWeightInterpolation": {"method": "barycentric interpolation on nearest triangulated Body surface",
                                        "maneMaxSurfaceDistance_m": max(row["nearestSurfaceDistance_m"] for row in source_reports),
                                        "tailMaxSurfaceDistance_m": max(row["nearestSurfaceDistance_m"] for row in tail_source_reports)},
            "maxInfluences": max_influences, "allVerticesWeighted": True}


def morph_delta(point: Vector, key: str) -> Vector:
    x, y, z = point
    if key == "body_length":
        trunk = smoothstep(-0.55, -0.30, x) * (1.0 - smoothstep(0.28, 0.48, x)) * smoothstep(0.65, 0.84, z)
        return Vector((0.030 * (smoothstep(-0.50, 0.20, x) - 0.5) * trunk, 0.0, 0.0))
    if key == "chest_width":
        amount = math.exp(-((x - 0.20) / 0.26) ** 2) * smoothstep(0.70, 0.95, z)
        return Vector((0.0, y * 0.055 * amount, 0.0))
    if key == "neck_length":
        amount = smoothstep(0.23, 0.50, x) * smoothstep(1.00, 1.24, z)
        return Vector((0.035 * amount, 0.0, 0.0))
    if key == "muscle":
        amount = (math.exp(-((x + 0.46) / 0.22) ** 2) + math.exp(-((x - 0.20) / 0.24) ** 2)) * smoothstep(0.70, 0.94, z)
        return Vector((0.0, y * 0.045 * amount, (z - 0.94) * 0.035 * amount))
    raise ValueError(key)


def add_shape_keys(objects: list[bpy.types.Object]) -> dict:
    maxima = {key: {} for key in SHAPE_KEYS}
    for obj in objects:
        obj.shape_key_add(name="Basis", from_mix=False)
        world = obj.matrix_world.copy()
        inv = world.inverted()
        for key_name in SHAPE_KEYS:
            shape = obj.shape_key_add(name=key_name, from_mix=False)
            max_delta = 0.0
            for index, vertex in enumerate(obj.data.vertices):
                point = world @ vertex.co
                delta = morph_delta(point, key_name)
                shape.data[index].co = inv @ (point + delta)
                max_delta = max(max_delta, delta.length)
            shape.slider_min, shape.slider_max, shape.value = -1.0, 1.0, 0.0
            maxima[key_name][obj.name] = round(max_delta, 5)
        obj.data.shape_keys.use_relative = True
    return maxima


def prepare_masks(importer, body: bpy.types.Object, rig: bpy.types.Object):
    # Use actual geometry at the poll and muzzle rather than short anonymous
    # Smart Rig skull segments. These coordinates also drive the juvenile
    # canonical face-mark axis.
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    poll_probe = Vector((0.53, 0.0, 1.405))
    poll_hit = tree.find_nearest(poll_probe)
    if poll_hit is None or poll_hit[0] is None:
        raise RuntimeError("Could not locate foal poll on upper skull")
    poll = poll_hit[0]
    points = [vertex.co.copy() for vertex in body.data.vertices]
    head_points = [point for point in points if point.z > 0.95 and point.x > 0.42]
    muzzle_points = [point for point in head_points if point.x >= max(p.x for p in head_points) - 0.012 and abs(point.y) < 0.055]
    if len(muzzle_points) < 3:
        raise RuntimeError("Could not identify centered foal muzzle-tip vertices")
    muzzle = sum(muzzle_points, Vector()) / len(muzzle_points)
    axis = muzzle - poll
    horizontal = math.hypot(axis.x, axis.y)
    azimuth_x = axis.x / max(horizontal, 1e-8)
    if axis.length < 0.20 or azimuth_x < 0.70:
        raise RuntimeError(f"Foal poll-to-muzzle axis is invalid: {tuple(axis)}")
    original = importer.rig_world_bones
    def geometry_axis(target_rig):
        result = original(target_rig)
        result["Head"] = (poll.copy(), muzzle.copy())
        return result
    importer.rig_world_bones = geometry_axis
    try:
        report = importer.prepare_body(body, rig, "foal")
    finally:
        importer.rig_world_bones = original
    axis_report = {"method": "poll projected from juvenile skull top; muzzle from centered forward-most head vertices",
                   "poll_m": [round(v, 5) for v in poll], "muzzle_m": [round(v, 5) for v in muzzle],
                   "direction": [round(v, 5) for v in axis.normalized()],
                   "horizontalForwardXY": round(azimuth_x, 5),
                   "muzzleCandidateCount": len(muzzle_points)}
    if report["forehead_front_loops"] <= 0:
        raise RuntimeError("Foal mask did not classify forehead-facing surface loops")
    return report, axis_report


def add_neutral_material(importer, obj, name, color=(0.72, 0.72, 0.72, 1.0)):
    obj.data.materials.clear()
    material = importer.create_neutral_material(name)
    material.diffuse_color = color
    obj.data.materials.append(material)


def foal_head_landmarks(body: bpy.types.Object) -> dict:
    """Ray-hit the actual post-cap Body crown on the midline behind the brow."""
    tree = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    points = [vertex.co.copy() for vertex in body.data.vertices]
    ear_anchors = derive_foal_ear_anchors(points)
    ear_root_mid = (ear_anchors["R"][0] + ear_anchors["L"][0]) * 0.5
    # The old nearest-point midpoint landed above the skull because the two
    # ear roots are lateral and elevated. A vertical ray at their midline X
    # gives a real centered Body surface point, independent of the removed fin
    # and Mane ownership. The cap sits well caudal to this ray (x≈0.35).
    poll_query = Vector((ear_root_mid.x, 0.0, 1.75))
    poll_hit = tree.ray_cast(poll_query, Vector((0.0, 0.0, -1.0)), 1.0)
    if poll_hit is None or poll_hit[0] is None or poll_hit[3] > 0.5:
        raise RuntimeError("Foal poll ray did not hit the centered post-cap Body crown")
    poll = poll_hit[0]
    face = int(poll_hit[2])
    surface_normal = poll_hit[1].normalized()
    ray_distance = float(poll_hit[3])
    if (not all(math.isfinite(value) for value in poll)
            or abs(poll.y) > 1e-5
            or abs(poll.x - ear_root_mid.x) > 0.005
            or poll.z < ear_root_mid.z - 0.10 or poll.z > ear_root_mid.z + 0.04
            or surface_normal.z < 0.35):
        raise RuntimeError(f"Foal centered poll ray is inconsistent with crown geometry: {poll}, roots={ear_root_mid}, normal={surface_normal}")
    head_points = [point for point in points if point.x > 0.35 and point.z > 0.90]
    if not head_points:
        raise RuntimeError("Foal head vertex region is empty")
    max_x = max(point.x for point in head_points)
    muzzle_candidates = [point for point in head_points if point.x >= max_x - 0.012 and abs(point.y) <= 0.055]
    if len(muzzle_candidates) < 3:
        raise RuntimeError("Could not identify centered foal muzzle-tip vertices")
    muzzle = sum(muzzle_candidates, Vector()) / len(muzzle_candidates)
    nostrils = {}
    for side, label in ((-1, "R"), (1, "L")):
        probe = Vector((muzzle.x - 0.025, side * 0.045, muzzle.z + 0.025))
        hit = tree.find_nearest(probe)
        if hit is None or hit[0] is None or hit[3] > 0.07:
            raise RuntimeError(f"Could not locate the foal {label} nostril-region surface")
        nostrils[label] = {"surface_m": [round(v, 5) for v in hit[0]],
                           "normal": [round(v, 5) for v in hit[1].normalized()],
                           "probe_m": [round(v, 5) for v in probe],
                           "fitDistance_m": round(float(hit[3]), 5)}
    axis = muzzle - poll
    horizontal = math.hypot(axis.x, axis.y)
    forward = axis.x / max(horizontal, 1e-8)
    if (not all(math.isfinite(value) for value in axis) or axis.length < 0.20
            or axis.x <= 0.0 or forward < 0.95):
        raise RuntimeError(f"Foal skull-to-muzzle axis is invalid: {tuple(axis)}")
    return {"poll": poll, "muzzle": muzzle,
            "method": "vertical ray on midline at bilateral ear-root midpoint X, direct hit on the post-cap Body crown; muzzle from centered forward-most nose vertices",
            "poll_m": [round(v, 5) for v in poll],
            "pollSourceFace": int(face), "pollBodySurfaceDistance_m": 0.0,
            "pollRayStart_m": [round(v, 5) for v in poll_query],
            "pollRayDirection": [0.0, 0.0, -1.0],
            "pollRayDistance_m": round(ray_distance, 6),
            "pollSurfaceNormal": [round(v, 5) for v in surface_normal],
            "pollToEarRootMidpoint_m": round((poll - ear_root_mid).length, 6),
            "earRootMidpoint_m": [round(v, 5) for v in ear_root_mid],
            "earRootMidpoint_m": [round(v, 5) for v in ear_root_mid],
            "muzzle_m": [round(v, 5) for v in muzzle],
            "muzzleCandidateCount": len(muzzle_candidates), "nostrilSurfaceReferences": nostrils,
            "horizontalForwardXY": round(forward, 5)}


def derive_foal_ear_anchors(vertices: list[Vector]) -> dict:
    """Estimate bilateral ear shells from their separated high lateral tips."""
    candidates = [point for point in vertices if 0.46 <= point.x <= 0.72 and point.z >= 1.30]
    if len(candidates) < 8:
        raise RuntimeError("Foal source mesh has too few upper-skull vertices for ear landmarks")
    anchors = {}
    for side, label in ((-1, "R"), (1, "L")):
        sided = [point for point in candidates if point.y * side > 0.025]
        if len(sided) < 4:
            raise RuntimeError(f"Foal geometry does not expose ear vertices on side {label}")
        tip = max(sided, key=lambda point: point.z)
        root_candidates = [point for point in sided if point.z <= tip.z - 0.075]
        root = max(root_candidates, key=lambda point: point.z) if root_candidates else min(sided, key=lambda point: point.z)
        anchors[label] = (root.copy(), tip.copy())
    return anchors


def add_semantic_mask_bones(rig: bpy.types.Object, body: bpy.types.Object,
                            head_landmarks: dict) -> dict:
    """Add non-deforming geometry landmarks needed by the existing mask API."""
    vertices = [vertex.co.copy() for vertex in body.data.vertices]
    anchors = derive_foal_ear_anchors(vertices)
    muzzle = head_landmarks["muzzle"]
    jaw_head = muzzle - Vector((0.055, 0.0, 0.025))
    jaw_tail = muzzle.copy()
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    edit = rig.data.edit_bones
    created = []
    for label, (root, tip) in anchors.items():
        name = "Ear_" + label
        bone = edit.get(name)
        if bone is None:
            bone = edit.new(name)
            created.append(name)
        bone.head, bone.tail = root, tip
        bone.use_deform = False
        bone.use_connect = False
        bone.parent = edit.get("Head") or edit.get("Head_Base")
    jaw = edit.get("Jaw_Tip")
    if jaw is None:
        jaw = edit.new("Jaw_Tip")
        created.append("Jaw_Tip")
    jaw.head, jaw.tail = jaw_head, jaw_tail
    jaw.use_deform = False
    jaw.use_connect = False
    jaw.parent = edit.get("Head") or edit.get("Head_Base")
    bpy.ops.object.mode_set(mode="OBJECT")
    return {"method": "non-deforming semantic mask anchors measured from foal mesh geometry",
            "semanticHelperBoneNames": created, "semanticHelperBoneCount": len(created),
            "earRootsAndTips_m": {label: {"root": [round(v,5) for v in root],
                                           "tip": [round(v,5) for v in tip]}
                                   for label,(root,tip) in anchors.items()},
            "jawTip_m": [round(v,5) for v in jaw_tail],
            "deformWeightsAdded": False}


def triangle_count(objects):
    return sum(sum(len(poly.vertices) - 2 for poly in obj.data.polygons) for obj in objects)


def make_preview(family: str, objects: list[bpy.types.Object]) -> dict:
    # Reuse the adult visual helper without touching adult geometry.
    adult = load_module("horse_meshy_builder", ROOT / "tools/horse/build_meshy_production.py")
    adult.EVIDENCE_DIR = EVIDENCE_DIR
    adult.BLEND_DIR = BLEND_DIR
    return adult.render_views(family, objects)


def render_eye_proofs(objects: list[bpy.types.Object]) -> dict:
    scene = bpy.context.scene
    old_engine = scene.render.engine
    old_resolution = (scene.render.resolution_x, scene.render.resolution_y)
    old_cavity = scene.display.shading.show_cavity
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = 900, 700
    scene.display.shading.light = "STUDIO"
    scene.display.shading.studio_light = "paint.sl"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    target = Vector((0.57, 0.0, 1.295))
    camera_data = bpy.data.cameras.new("Foal_EyeProof_Camera")
    camera = bpy.data.objects.new("Foal_EyeProof_Camera", camera_data)
    scene.collection.objects.link(camera)
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 0.72
    scene.camera = camera
    views = {"front": Vector((2.3, 0.0, 1.295)),
             "near": Vector((0.57, -2.0, 1.295)),
             "far": Vector((0.57, 2.0, 1.295))}
    paths = {}
    for label, location in views.items():
        camera.location = location
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        path = EVIDENCE_DIR / f"horse_foal_eye_{label}.png"
        scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        paths[label] = str(path.relative_to(ROOT))
    scene.render.engine = old_engine
    scene.render.resolution_x, scene.render.resolution_y = old_resolution
    scene.display.shading.show_cavity = old_cavity
    bpy.data.objects.remove(camera, do_unlink=True)
    return paths


def export_glb(path: Path, rig: bpy.types.Object, objects: list[bpy.types.Object]) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(filepath=str(path), export_format="GLB", use_selection=True,
        export_yup=True, export_apply=False, export_texcoords=True, export_normals=True,
        export_skins=True, export_vertex_color="ACTIVE", export_all_vertex_colors=True,
        export_animations=True, export_nla_strips=False,
        export_cameras=False, export_lights=False,
        export_materials="EXPORT", export_image_format="AUTO", export_morph=True)


def inspect_idle_export(path: Path, importer) -> dict:
    """Separate varying Idle motion from static rest tracks emitted by glTF export."""
    doc, binary = importer.glb_chunks(path)
    animations = doc.get("animations", [])
    if [item.get("name") for item in animations] != ["Idle"]:
        raise RuntimeError(f"Foal export must contain exactly Idle: {[item.get('name') for item in animations]}")
    nodes = doc.get("nodes", [])
    rows = []
    for animation in animations:
        for channel in animation.get("channels", []):
            sampler = animation["samplers"][channel["sampler"]]
            times = importer.read_accessor(doc, binary, sampler["input"])
            outputs = importer.read_accessor(doc, binary, sampler["output"])
            first = outputs[0] if outputs else []
            variation = max((math.sqrt(sum((float(a)-float(b))**2 for a,b in zip(first, row)))
                             for row in outputs[1:]), default=0.0)
            rows.append({"node": nodes[channel["target"]["node"]].get("name"),
                         "path": channel["target"]["path"],
                         "startSeconds": round(min((row[0] for row in times), default=0.0), 8),
                         "durationSeconds": round(max((row[0] for row in times), default=0.0)
                                                   - min((row[0] for row in times), default=0.0), 8),
                         "keys": len(times), "interpolation": sampler.get("interpolation", "LINEAR"),
                         "maxOutputDelta": variation})
    varying = [row for row in rows if row["maxOutputDelta"] > 1e-6]
    expected = {("Spine_Base", "rotation"), ("Neck", "rotation"), ("Head", "rotation")}
    if {(row["node"], row["path"]) for row in varying} != expected:
        raise RuntimeError(f"Unexpected varying Idle channels: {varying}")
    roots = [row for row in rows if row["node"] in ("Root", "Pelvis_Root")]
    if any(row["maxOutputDelta"] > 1e-6 for row in roots):
        raise RuntimeError(f"Idle export contains root motion: {roots}")
    return {"clip": "Idle", "channelCount": len(rows),
            "varyingChannelCount": len(varying), "staticRestChannelCount": len(rows)-len(varying),
            "varyingChannels": varying, "rootMotion": "none; Root and Pelvis_Root tracks are static",
            "exporterBehavior": "Blender glTF emits constant rest tracks for otherwise unkeyed bones"}


def author_foal_idle(rig: bpy.types.Object, motion) -> dict:
    """Create a quiet 4-second loop with breathing and a small head settle."""
    required = {"Spine_Base", "Neck", "Head", "Pelvis_Root"}
    missing = sorted(required - set(rig.data.bones.keys()))
    if missing:
        raise RuntimeError(f"Foal idle requires its repaired torso/head/root bones: {missing}")
    if not all(rig.data.bones[name].use_deform for name in ("Spine_Base", "Neck", "Head")):
        raise RuntimeError("Foal Idle may only animate deforming Spine_Base, Neck, and Head controls")
    rig.animation_data_clear()
    rig.animation_data_create()
    scene = bpy.context.scene
    scene.render.fps = 24
    motion._reset_pose(rig)
    animated_bones = ("Spine_Base", "Neck", "Head")
    keyframes = [
        (1, {name: 0.0 for name in animated_bones}),
        (25, {"Spine_Base": 0.0857, "Neck": 0.10, "Head": -0.05}),
        (49, {"Spine_Base": 0.15, "Neck": 0.20, "Head": -0.10}),
        (73, {"Spine_Base": 0.0857, "Neck": 0.08, "Head": -0.03}),
        (97, {name: 0.0 for name in animated_bones}),
    ]
    action = motion._create_action(rig, "Idle", keyframes, fps=24, frame_end=97)
    rig.animation_data.action = action
    scene.frame_start, scene.frame_end = 1, 97
    scene.frame_set(1)
    bpy.context.view_layer.update()
    bpy.context.evaluated_depsgraph_get().update()
    # Keep the build in a neutral rest preview; the active action is exported.
    motion._reset_pose(rig)
    scene.frame_set(1)
    scene.render.fps = 24
    if abs(scene.frame_current - 1) > 0:
        raise RuntimeError("Foal Idle did not return the scene to its neutral start frame")
    # Preserve the scene's intended 24 fps and 97-frame clip settings; restore
    # only the prior current frame if it was already inside this new range.
    scene.frame_set(1)
    scene.render.fps = 24
    return {"actions": ["Idle"], "fps": 24, "durationSeconds": 4.0,
            "frameRange": [1, 97], "loopEndpoints": [1, 97],
            "keyframes": [{"frame": frame, "worldYRotationDegrees": values}
                          for frame, values in keyframes],
            "animatedDeformingBones": list(animated_bones),
            "rootMotion": "none; Pelvis_Root has no keyed transform",
            "style": "subtle breathing and head settle; torso rotation peak reduced to 0.15 degrees after hoof-support audit; no ear, tail, gait, or graze channels",
            "loop": "exact neutral pose at frame 1 and frame 97; CYCLES modifiers on channels"}


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    BLEND_DIR.mkdir(parents=True, exist_ok=True)
    EVIDENCE_DIR.mkdir(parents=True, exist_ok=True)
    if not SOURCE.is_file() or not RIG_SOURCE.is_file():
        raise FileNotFoundError("Foal source mesh or retry Smart Rig is missing")
    importer = load_module("horse_import_reference", ROOT / "tools/horse/import_reference.py")
    motion = load_module("horse_production_motion", ROOT / "tools/horse/production_motion.py")
    adult_builder = load_module("horse_meshy_builder", ROOT / "tools/horse/build_meshy_production.py")
    source_hash, rig_hash = sha256(SOURCE), sha256(RIG_SOURCE)
    body, decimation = import_and_decimate()
    anatomy = load_module("horse_anatomy_regions", ROOT / "tools/horse/anatomy_regions.py")
    # This guarded face-ID map is tied to the fixed decimated source. Read and
    # cache every ID/remap before softening, UV generation, or coordinate edits.
    source_hair_faces = anatomy.classify_foal_source_hair_faces(body)
    foal_regions = anatomy.foal_region_report(body)
    foal_forelock_source_vertices = anatomy.partition_foal_forelock_vertex_indices(body, source_hair_faces)
    source_hair_faces, supplemental_fin = classify_residual_centerline_fin(body, source_hair_faces)
    source_hair_faces, closure_junction_report = regularize_foal_cut_junctions(body, source_hair_faces, anatomy)
    foal_mane_root = anatomy.foal_mane_root_boundary(body, source_hair_faces)
    softening = localized_softening(body)
    # The source's raised fin/bristle cluster is removed as mapped. Close its
    # skin opening, then author a restrained crest and tail to avoid grafting an
    # adult asset or scaling a mature horse down.
    fin_replacement = remove_fin_and_cap_body(body, source_hair_faces["Mane"], foal_mane_root)
    poll_softening = soften_poll_stubs(body)
    add_uv0(body)
    mane, mane_partition = create_mane(body)
    eyes, eye_fit = create_eyes(body)
    tail, tail_report = make_tail(body)
    forelock_marker = mane.vertex_groups.get("TEMP_Foal_Forelock")
    forelock_vertex_indices = ({vertex.index for vertex in mane.data.vertices
                                if any(item.group == forelock_marker.index and item.weight >= 0.5
                                       for item in vertex.groups)}
                               if forelock_marker is not None else set())
    if not forelock_vertex_indices:
        raise RuntimeError("Foal forelock patch lost its vertex correspondence during creation")
    for obj in (mane, eyes, tail):
        if not obj.data.uv_layers.get("UVMap"):
            add_uv0(obj)
    add_neutral_material(importer, body, "Foal_Coat_Descriptor_Neutral", (0.70, 0.70, 0.70, 1.0))
    add_neutral_material(importer, eyes, "Foal_Eye_Descriptor_Neutral", (0.025, 0.018, 0.016, 1.0))
    add_neutral_material(importer, mane, "Foal_Mane_Descriptor_Neutral", (0.16, 0.12, 0.10, 1.0))
    add_neutral_material(importer, tail, "Foal_Tail_Descriptor_Neutral", (0.16, 0.12, 0.10, 1.0))
    related = [eyes, mane, tail]
    rig, rig_report = motion.prepare_foal_rig(body, family="foal", related_meshes=related,
        rig_name="Skeleton", family_point=foal_point, smart_rig_path=RIG_SOURCE)
    if rig is None or rig.type != "ARMATURE":
        raise RuntimeError("Foal rig adapter did not return an armature")
    group_name_repair = normalize_foal_skin_group_names(rig, [body, *related])
    for obj in [body, *related]:
        if not obj.vertex_groups:
            raise RuntimeError(f"Foal rig supplied no skin groups for {obj.name}")
        arm = next((modifier for modifier in obj.modifiers if modifier.type == "ARMATURE"), None)
        if arm is None or arm.object != rig:
            raise RuntimeError(f"Foal rig adapter did not bind {obj.name} to Skeleton exactly once")
    head_hierarchy_repair = reparent_head_to_head_base(rig)
    cervical_hierarchy_repair = reparent_cervical_bone_to_neck(rig)
    head_landmarks = foal_head_landmarks(body)
    semantic_bones = add_semantic_mask_bones(rig, body, head_landmarks)
    hair_attachments = bind_authored_hair(rig, body, mane, tail,
                                          Vector(mane_partition["root_m"]),
                                          Vector(tail_report["surfaceRoot_m"]),
                                          forelock_vertex_indices=forelock_vertex_indices)
    body_height = max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices)
    minimum_axis_length = 0.35 * body_height / ADULT_REFERENCE_BODY_HEIGHT_M
    if not math.isfinite(minimum_axis_length) or minimum_axis_length <= 0.0:
        raise RuntimeError(f"Foal minimum canonical-axis length is invalid: {minimum_axis_length}")
    mask_report, head_axis_report, mask_repair_report = adult_builder.prepare_candidate_masks(
        importer, body, rig, head_landmarks, minimum_axis_length=minimum_axis_length)
    head_axis_report["minimumAxisBasis"] = {
        "formula": "adult minimum * juvenile body height / adult reference body height",
        "adultMinimum_m": 0.35, "juvenileBodyHeight_m": round(body_height, 6),
        "adultReferenceBodyHeight_m": ADULT_REFERENCE_BODY_HEIGHT_M,
        "minimumAxisLength_m": round(minimum_axis_length, 6),
    }
    max_deltas = add_shape_keys([body, *related])
    quality = importer.mesh_quality([body, *related])
    if any(counts["zero_area_faces"] or counts["loose_vertices"] for counts in quality.values()):
        raise RuntimeError(f"Foal geometry quality check failed: {quality}")
    objects = [body, eyes, mane, tail]
    idle_report = author_foal_idle(rig, motion)
    glb = OUT_DIR / "horse_foal.glb"
    export_glb(glb, rig, objects)
    report = importer.inspect_glb(glb, {}, allow_animations=True)
    if set(report["mesh_names"]) != EXPECTED_NODES or not report["body_has_descriptor_attributes"]:
        raise RuntimeError(f"Foal GLB lost node or marking-mask contract: {report}")
    if not report["body_has_skin"]:
        raise RuntimeError("Foal Body exported without skin weights")
    glb_document, _ = importer.glb_chunks(glb)
    exported_animation_names = [animation.get("name") for animation in glb_document.get("animations", [])]
    if exported_animation_names != ["Idle"]:
        raise RuntimeError(f"Foal GLB must export exactly one Idle action: {exported_animation_names}")
    idle_export_audit = inspect_idle_export(glb, importer)
    target_report = adult_targets(glb, importer)
    if set(target_report) != EXPECTED_NODES or any(set(names) != set(SHAPE_KEYS) for names in target_report.values()):
        raise RuntimeError(f"Foal morph node contract failed: {target_report}")
    head_target_blender = (head_landmarks["poll"] + head_landmarks["muzzle"]) * 0.5
    head_target = [round(head_target_blender.x, 6),
                   round(head_target_blender.z, 6),
                   round(-head_target_blender.y, 6)]
    views = make_preview("foal", objects)
    eye_views = render_eye_proofs(objects)
    blend = BLEND_DIR / "horse_foal.blend"
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    rig_source_manifest = dict(rig_report)
    rig_source_manifest["adapterBoneCount"] = rig_source_manifest.pop("boneCount")
    rig_source_manifest["boneCountStage"] = "after rig adaptation and before semantic mask helper bones"
    semantic_bone_names = semantic_bones.get("semanticHelperBoneNames", [])
    bone_counts = {"sourceRigBones": rig_report["sourceBoneCount"],
                   "afterAdapterBeforeMaskHelpers": rig_report["boneCount"],
                   "semanticMaskHelpersAdded": len(semantic_bone_names),
                   "semanticMaskHelperNames": semantic_bone_names,
                   "finalSkeletonBones": len(rig.data.bones),
                   "exportedSkeletonBones": len(rig.data.bones)}
    if bone_counts["finalSkeletonBones"] != bone_counts["afterAdapterBeforeMaskHelpers"] + bone_counts["semanticMaskHelpersAdded"]:
        raise RuntimeError(f"Foal rig bone-count stages do not reconcile: {bone_counts}")
    manifest = {
        "assetVersion": "horse-production.meshy.v1.foal",
        "familyId": "horse.foal",
        "profile": "Meshy juvenile foal; distinct juvenile proportions",
        "status": "Candidate-derived juvenile asset for visual, masks, and rig review; not approved production anatomy",
        "source": {"file": "art/candidates/meshy/foal_01/source.glb", "sha256": source_hash,
                   "rigFile": "art/candidates/meshy/foal_01/smart-rig-retry.glb", "rigSha256": rig_hash,
                   "generationModel": "Meshy 7.1", "accountPlan": "owner-reported Pro",
                   "privacySetting": "Private", "thirdPartyImageInput": False,
                   "ownershipBasis": "Owner-generated candidate; paid Meshy plan ownership subject to compliant input and privacy settings",
                   "termsCheckedDate": "2026-10-11",
                   "termsSources": ["https://help.meshy.ai/en/articles/9992001-can-i-use-meshy-assets-commercially-license-copyright-explained",
                                    "https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models"]},
        "transforms": {"rawToBlender": f"(x,y,z)->(-y,x,z+{abs(SOURCE_Z_FLOOR):.10f})*{SOURCE_TO_WORLD_SCALE}; baked into vertices",
                       "forward": "+X", "left": "+Y", "up": "+Z", "floor_z_m": 0.0,
                       "bodyHeight_m": round(max(v.co.z for v in body.data.vertices)-min(v.co.z for v in body.data.vertices), 5),
                       "familyWarp": "none; juvenile source proportions preserved"},
        "meshNodes": sorted(EXPECTED_NODES), "rigNode": "Skeleton", "animations": ["Idle"],
        "idleAnimation": idle_report, "idleAnimationExport": idle_export_audit,
        "triangleCount": triangle_count(objects),
        "trianglesByMesh": {obj.name: {"vertices": len(obj.data.vertices),
                                       "triangles": sum(len(poly.vertices)-2 for poly in obj.data.polygons),
                                       "COLOR_0": len(obj.data.loops) if obj.name == "Body" else 0,
                                       "TEXCOORD_1": len(obj.data.loops) if obj.name == "Body" else 0}
                            for obj in objects},
        "decimation": decimation, "localizedSoftening": softening,
        "pollArtifactSoftening": poll_softening,
        "sourceHairMap": foal_regions, "supplementalCenterlineFinRemoval": supplemental_fin,
        "sourceHairRemoval": fin_replacement,
        "cutJunctionRegularization": closure_junction_report,
        "sourceRootBoundary": {"edgeCount": len(foal_mane_root["sourceEdges"]),
                               "vertexCount": len(foal_mane_root["sourceVertices"]),
                               "forelockReferenceVertices": len(foal_forelock_source_vertices)},
        "manePartition": mane_partition, "tail": tail_report,
        "authoredHairAttachments": hair_attachments,
        "shapeKeys": {name: {"neutral": 0.0, "range": [-1.0, 1.0], "nodes": max_deltas[name]} for name in SHAPE_KEYS},
        "bodyMasks": {"COLOR_0": True, "UV0": "Smart UV Project from Meshy source without source UV",
                       "canonicalUV1": "existing importer anatomy helper with geometry-grounded poll/muzzle axis",
                       "perCornerBoundaryRepair": mask_repair_report},
        "maskCoverage": mask_report, "canonicalHeadAxis": head_axis_report,
        "anatomyLandmarks": {**semantic_bones,
                              "poll_m": head_axis_report["poll_m"],
                              "muzzle_m": head_axis_report["muzzle_m"],
                              "neckBase_m": [round(v,5) for v in (rig.data.bones["Neck_Base"].head_local if "Neck_Base" in rig.data.bones else rig.data.bones["Chest"].tail_local)],
                              "nostrilSurfaceReferences": head_landmarks["nostrilSurfaceReferences"],
                              "eyeCenters_m": {eye["side"]: eye["center_m"] for eye in eye_fit["sides"]},
                              "maneStart_m": mane_partition.get("root_m"),
                              "manePollEnd_m": mane_partition.get("pollEnd_m"),
                              "tailSurfaceRoot_m": tail_report["surfaceRoot_m"],
                              "neckSurfaceReferences": measure_lateral_body_surface_references(body)},
        "anatomyLandmarkNaming": {
            "manePollSideEndpoint": "legacy anatomyLandmarks.manePollEnd_m; current endpoint is 17-30mm forward of sampled ear roots (+X), not behind the ears",
            "maneEndAtWithers": "legacy anatomyLandmarks.maneStart_m; withers-side root of the Mane crest",
            "neckBase_m": "Skeleton.Neck_Base head joint; its nearest Body surface projection lies behind the withers and is not a lower-neck landmark",
            "neckSurfaceReferences": "Bilateral lateral ray hits on Body at named approximate anatomical probe stations; measured geometry references, not bone-derived projections",
            "jawTip_m": "Non-deforming semantic helper location exactly aliases muzzle_m; not an independent jaw or head-neck surface measurement",
        },
        "eyeFit": eye_fit,
        "headHierarchyRepair": head_hierarchy_repair,
        "cervicalHierarchyRepair": cervical_hierarchy_repair,
        "skinGroupNameRepair": group_name_repair,
        "motion": {"source": rig_source_manifest, "boneCounts": bone_counts,
                   "acceptance": "candidate transfer with restrained Idle; no gait or graze clips"},
        "bounds": adult_bounds(objects), "headViewTarget_m": head_target,
        "anatomyLandmarksCompanion": "art/derived/production/evidence/foal/horse_foal_anatomy_landmarks.json",
        "glbValidation": report, "glbMorphTargets": target_report,
        "geometryQuality": quality, "evidence": {**views, "eyes": eye_views},
        "limitations": ["The foal is a distinct Meshy juvenile candidate, not an adult miniaturization.",
                        "Localized smoothing only reduces the strongest barrel grooves; inspect rendered output before accepting anatomy.",
                        "The candidate Smart Rig was reviewed before this Idle addition; see target proof for the cleaned target. Idle is a restrained candidate clip, not a production motion acceptance.",
                        "Compact foal support is not provided by this single juvenile asset."]}
    (OUT_DIR / "horse_foal.manifest.json").write_text(json.dumps(manifest, indent=2))
    write_anatomy_companion(manifest, glb, EVIDENCE_DIR / "horse_foal_anatomy_landmarks.json")
    print(json.dumps({"blend": str(blend), "glb": str(glb), "sha256": sha256(glb),
                      "triangles": report["triangles"], "bodyHeight_m": manifest["transforms"]["bodyHeight_m"],
                      "headViewTarget_m": head_target, "morphTargets": target_report,
                      "maskCoverage": mask_report, "rig": rig_report["limbWeightIntegrity"], "views": {**views, "eyes": eye_views}}, indent=2))


def adult_targets(path: Path, importer) -> dict:
    doc, _ = importer.glb_chunks(path)
    targets = {}
    for node in doc.get("nodes", []):
        if "mesh" not in node:
            continue
        mesh = doc["meshes"][node["mesh"]]
        names = mesh.get("extras", {}).get("targetNames", [])
        if not names and mesh.get("primitives"):
            names = mesh["primitives"][0].get("extras", {}).get("targetNames", [])
        if names:
            targets[node.get("name", "")] = names
    return targets


def adult_bounds(objects: list[bpy.types.Object]) -> dict:
    points = [obj.matrix_world @ v.co for obj in objects for v in obj.data.vertices]
    lo, hi = [min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]
    return {"min_m": [round(v, 5) for v in lo], "max_m": [round(v, 5) for v in hi],
            "size_m": [round(hi[i]-lo[i], 5) for i in range(3)]}


def write_anatomy_companion(manifest: dict, glb: Path, output_path: Path) -> None:
    """Write explicit landmark semantics while retaining legacy manifest keys."""
    landmarks = manifest["anatomyLandmarks"]
    payload = {
        "schema": "horse-anatomy-landmarks.v1", "asset": "horse_foal.glb",
        "assetSha256": sha256(glb), "familyId": manifest["familyId"],
        "status": "candidate; landmarks support inspection and procedural setup, not anatomy acceptance",
        "coordinateSpace": {"unit": "metres", "source": "Blender object-local XYZ; identity transform",
                            "godotXYZFromBlender": "(x,z,-y)"},
        "landmarks": {
            "earRootsAndTips": {"kind": "geometry_surface_sample", "confidence": "medium; bilateral ear-shell vertices selected from the juvenile source mesh",
                                "points_m": landmarks["earRootsAndTips_m"]},
            "pollBetweenEars": {"kind": "geometry_surface", "confidence": "high; actual vertical ray hit on centered post-cap crown",
                                "point_m": landmarks["poll_m"]},
            "muzzleTip": {"kind": "geometry_surface", "confidence": "high; centered forward-most nose vertices",
                          "point_m": landmarks["muzzle_m"]},
            "nostrils": {"kind": "geometry_surface_reference", "confidence": "medium; nearest Body surface to nostril probes",
                         "points": landmarks["nostrilSurfaceReferences"]},
            "eyeCenters": {"kind": "fitted_component_center", "confidence": "medium; socket-fitted Eyes component centers",
                           "points_m": landmarks["eyeCenters_m"]},
            "neckBaseJoint": {"kind": "bone_joint", "confidence": "high for skeleton joint position only; not a Body surface",
                              "point_m": landmarks["neckBase_m"],
                              "note": "Its nearest Body surface projection is behind the withers; do not interpret it as the lower-neck surface."},
            "neckSurfaceStations": landmarks["neckSurfaceReferences"],
            "manePollSideEndpoint": {"kind": "authored_mane_endpoint", "confidence": "medium; endpoint location measured from the Mane component",
                                     "point_m": landmarks["manePollEnd_m"],
                                     "forwardOfEarRootSamplesX_m": {
                                         side: round(float(landmarks["manePollEnd_m"][0] - landmarks["earRootsAndTips_m"][side]["root"][0]), 6)
                                         for side in ("R", "L")},
                                     "note": "On this foal this point is 17-30mm forward (+X) of the sampled ear roots; it is not behind the ears."},
            "maneEndAtWithers": {"kind": "authored_mane_endpoint", "confidence": "medium; withers-side root of the crest",
                                 "point_m": landmarks["maneStart_m"]},
            "jawTip": {"kind": "semantic_helper_alias", "confidence": "not an independent anatomical measurement",
                       "point_m": landmarks["jawTip_m"],
                       "note": "This non-deforming helper exactly aliases muzzle_m; it is retained for mask compatibility."},
            "tailRoot": {"kind": "geometry_surface_attachment", "confidence": "medium; authored tail root from rump surface ray",
                         "point_m": landmarks["tailSurfaceRoot_m"]},
        },
        "legacyManifestFieldAliases": {"manePollEnd_m": "manePollSideEndpoint; not behind the ears on this foal",
                                        "maneStart_m": "maneEndAtWithers",
                                        "neckBase_m": "bone joint; nearest surface is behind withers, not lower neck",
                                        "jawTip_m": "non-deforming semantic helper alias of muzzle_m"},
        "semanticHelperAliases": {"Jaw_Tip": {"kind": "non_deforming_mask_helper",
                                                 "aliases": "muzzle_m",
                                                 "independentAnatomicalMeasurement": False}},
        "headViewTarget_m": manifest["headViewTarget_m"],
        "proofImages": manifest.get("evidence", {}),
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2) + "\n")


def measure_lateral_body_surface_references(body: bpy.types.Object) -> dict:
    """Measure paired Body surface points with lateral rays at named foal stations."""
    stations = {
        "middleNeck": (0.285, 1.11),
        "neckShoulderTransition": (0.20, 0.90),
        "headJawNeckTransition": (0.46, 1.14),
    }
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    measured = {}
    for name, (x, z) in stations.items():
        sides = {}
        for side, origin_y, direction_y in (("right", -0.6, 1.0), ("left", 0.6, -1.0)):
            hit, loc, normal, face_index = evaluated.ray_cast(
                Vector((x, origin_y, z)), Vector((0.0, direction_y, 0.0)), distance=2.0)
            if not hit:
                raise RuntimeError(f"Could not measure Body surface reference {name}/{side}")
            sides[side] = {"blenderXYZ_m": [round(float(v), 6) for v in loc],
                           "godotXYZ_m": [round(float(loc.x), 6), round(float(loc.z), 6), round(float(-loc.y), 6)],
                           "normal_blenderXYZ": [round(float(v), 6) for v in normal],
                           "bodyFaceIndex": int(face_index),
                           "ray": {"origin_blenderXYZ_m": [x, origin_y, z],
                                   "direction_blenderXYZ": [0.0, direction_y, 0.0]}}
        measured[name] = {"kind": "bilateral_body_surface_ray_hits", "confidence": "geometry hit at named approximate probe station",
                          "method": "lateral ray from outside Body; exact face and normal recorded",
                          "probeXZ_blender_m": [x, z], "sides": sides}
    return measured


if __name__ == "__main__":
    main()
