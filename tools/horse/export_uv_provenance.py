"""Build a source-face UV0 sidecar and compare an immutable prior GLB.

Run in background Blender from the repository root:

  Blender --background --factory-startup --disable-autoexec \
    --python tools/horse/export_uv_provenance.py -- --reference-glb PATH --current-glb PATH

The sidecar records post-decimation source face/vertex IDs and the full-shell
per-corner UV0 before the approved hoof support position correction or hair
partition. It does not edit either GLB or the editable master.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
from pathlib import Path

import bpy
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[2]
SIDE_CAR = ROOT / "art/derived/production/evidence/horse_light_uv0_provenance.json"


def load_builder():
    spec = importlib.util.spec_from_file_location("horse_meshy_builder", ROOT / "tools/horse/build_meshy_production.py")
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


def clear_scene() -> None:
    # GLB import can leave linked helper objects in collections not exposed to
    # the active scene operator. Remove every object datablock explicitly.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def polygon_points(obj, polygon):
    return [obj.matrix_world @ obj.data.vertices[index].co for index in polygon.vertices]


def imported_component_records(path: Path):
    clear_scene()
    # Keep the GLB triangle streams intact. The default import merges vertices
    # and can reconstruct quads, which loses the source-face correspondence.
    bpy.ops.import_scene.gltf(filepath=str(path), merge_vertices=False)
    meshes = {obj.name.split(".")[0]: obj for obj in bpy.context.scene.objects if obj.type == "MESH"}
    records = {name: [] for name in ("Body", "Mane", "Tail")}
    for name in records:
        obj = meshes.get(name)
        if obj is None:
            continue
        uv = obj.data.uv_layers.active
        if name == "Body" and uv is None:
            raise RuntimeError(f"{path}: Body has no imported UV0")
        for poly in obj.data.polygons:
            points = polygon_points(obj, poly)
            corner_uvs = []
            if uv is not None:
                for loop_index in poly.loop_indices:
                    corner_uvs.append([float(v) for v in uv.data[loop_index].uv])
            records[name].append({"points": points, "cornerUVs": corner_uvs})
    return records


def index_source_faces(body):
    face_vertices = {}
    for poly in body.data.polygons:
        if len(poly.vertices) != 3:
            raise RuntimeError(f"Expected triangulated source face {poly.index}, got {len(poly.vertices)} corners")
        ids = [int(index) for index in poly.vertices]
        face_vertices[poly.index] = ids
    return face_vertices


def map_component_records(records, source_vertex_tree, source_face_by_vertices, *,
                          max_distance=0.000005, uniqueness_gap=0.000001):
    """Map imported triangle corners to unique source vertices; fail closed."""
    mapped = {}
    unknown = {"distance": 0, "ambiguous": 0, "not_triangle": 0}
    for record in records:
        ids, uv_by_vertex = [], {}
        status = "mapped"
        for point, uv in zip(record["points"], record["cornerUVs"]):
            neighbors = source_vertex_tree.find_n(point, 2)
            if not neighbors or neighbors[0][2] > max_distance:
                status = "distance"
                break
            if len(neighbors) > 1 and neighbors[1][2] - neighbors[0][2] <= uniqueness_gap:
                status = "ambiguous"
                break
            source_id = int(neighbors[0][1])
            ids.append(source_id)
            uv_by_vertex[source_id] = uv
        if status == "mapped" and len(ids) != 3:
            status = "not_triangle"
        face_id = source_face_by_vertices.get(tuple(sorted(ids))) if status == "mapped" and len(set(ids)) == 3 else None
        if face_id is None:
            unknown[status if status != "mapped" else "not_triangle"] += 1
            continue
        if face_id in mapped:
            raise RuntimeError(f"Ambiguous imported-to-source face correspondence: {face_id}")
        mapped[face_id] = {"uvByVertex": uv_by_vertex}
    return mapped, unknown


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    def option(name):
        if name not in args:
            raise ValueError(f"Required option: {name}")
        return Path(args[args.index(name) + 1]).expanduser().resolve()

    reference = option("--reference-glb")
    current = option("--current-glb")
    for path in (reference, current):
        if not path.is_file():
            raise FileNotFoundError(path)
    builder = load_builder()
    anatomy = builder.anatomy_regions_api()

    # Capture old and current body/component triangle sets using geometry, then
    # recreate the exact full-shell UV projection from fixed candidate source.
    old_records = imported_component_records(reference)
    current_records = imported_component_records(current)
    clear_scene()
    body = builder.import_candidate()
    decimation = builder.decimate(body)
    source_regions = anatomy.classify_source_hair_faces(body)
    source_region_report = anatomy.region_report(body)
    source_face_ids = set(range(len(body.data.polygons)))
    current_component_ids = {
        "Body": sorted(source_face_ids - source_regions["Mane"] - source_regions["Tail"]),
        "Mane": sorted(source_regions["Mane"]),
        "Tail": sorted(source_regions["Tail"]),
    }
    source_points = [vertex.co.copy() for vertex in body.data.vertices]
    historical_points = [builder.family_point(point, "light") for point in source_points]
    corrected_points = [builder.family_point_with_sole_support(point, "light") for point in source_points]
    for vertex, point in zip(body.data.vertices, historical_points):
        vertex.co = point
    body.data.update()
    builder.add_uv0(body)
    source_face_vertices = index_source_faces(body)
    source_face_by_vertices = {tuple(sorted(ids)): face_id for face_id, ids in source_face_vertices.items()}
    if len(source_face_by_vertices) != len(source_face_vertices):
        raise RuntimeError("Source triangle topology contains duplicate vertex-index faces")
    def make_tree(points):
        tree = KDTree(len(points))
        for index, point in enumerate(points):
            tree.insert(point, index)
        tree.balance()
        return tree

    historical_vertex_tree = make_tree(historical_points)
    corrected_vertex_tree = make_tree(corrected_points)
    uv = body.data.uv_layers.active
    if uv is None or uv.name != "UVMap":
        raise RuntimeError("Recreated full shell lacks the expected UVMap")

    # Every old/new imported component face should map back to the same fixed
    # decimated face ID. Build source face lookup plus corner-UV maps.
    source_corner_uv = {}
    for poly in body.data.polygons:
        ids = source_face_vertices[poly.index]
        by_vertex = {}
        for loop_index in poly.loop_indices:
            vertex_id = int(body.data.loops[loop_index].vertex_index)
            by_vertex[vertex_id] = [float(v) for v in uv.data[loop_index].uv]
        source_corner_uv[poly.index] = [by_vertex[index] for index in ids]

    old_by_component, old_unknown = {}, {}
    current_by_component, current_unknown = {}, {}
    for name in ("Body", "Mane", "Tail"):
        old_by_component[name], old_unknown[name] = map_component_records(
            old_records[name], historical_vertex_tree, source_face_by_vertices)
        current_by_component[name], current_unknown[name] = map_component_records(
            current_records[name], corrected_vertex_tree, source_face_by_vertices)
    old_body = set(old_by_component["Body"])
    old_hair = set(old_by_component["Mane"]) | set(old_by_component["Tail"])
    new_body = set(current_by_component["Body"])
    new_hair = set(current_by_component["Mane"]) | set(current_by_component["Tail"])
    old_uv_by_face = {}
    for face_id, record in old_by_component["Body"].items():
        old_uv_by_face[face_id] = [record["uvByVertex"].get(index) for index in source_face_vertices[face_id]]
    current_uv_by_face = {}
    for face_id, record in current_by_component["Body"].items():
        current_uv_by_face[face_id] = [record["uvByVertex"].get(index) for index in source_face_vertices[face_id]]

    old_diffs, current_diffs = [], []
    old_uv_compared = current_uv_compared = 0
    for face_id in sorted(old_body):
        expected = source_corner_uv[face_id]
        got_old = old_uv_by_face.get(face_id)
        if got_old and all(got_old):
            old_uv_compared += 1
            old_diffs.extend(abs(got_old[c][axis] - expected[c][axis]) for c in range(3) for axis in range(2))
    for face_id in sorted(new_body):
        expected = source_corner_uv[face_id]
        got_current = current_uv_by_face.get(face_id)
        if got_current and all(got_current):
            current_uv_compared += 1
            current_diffs.extend(abs(got_current[c][axis] - expected[c][axis]) for c in range(3) for axis in range(2))

    # Stable digest over IDs and per-corner UV values in source order.
    digest = hashlib.sha256()
    face_records = []
    for face_id in range(len(body.data.polygons)):
        vertex_ids = source_face_vertices[face_id]
        values = source_corner_uv[face_id]
        digest.update(int(face_id).to_bytes(4, "little"))
        for vertex_id, uv_pair in zip(vertex_ids, values):
            digest.update(int(vertex_id).to_bytes(4, "little"))
            digest.update(json.dumps(uv_pair, separators=(",", ":")).encode("ascii"))
        face_records.append({"sourceFaceId": face_id, "sourceVertexIds": vertex_ids,
                             "uv0PerCorner": [[float(v) for v in pair] for pair in values]})

    if old_uv_compared != len(old_body) or current_uv_compared != len(new_body):
        raise RuntimeError(f"UV source-face mapping incomplete: old_body={len(old_body)} old_compared={old_uv_compared} current_body={len(new_body)} current_compared={current_uv_compared}")
    max_old = max(old_diffs, default=0.0)
    max_current = max(current_diffs, default=0.0)
    if max_old > 0.000003 or max_current > 0.000003:
        raise RuntimeError(f"UV0 changed beyond float tolerance: old={max_old}, current={max_current}")
    expected_current_body = set(current_component_ids["Body"])
    if len(expected_current_body) != len(current_records["Body"]):
        raise RuntimeError("Source map Body face count differs from the imported current GLB")

    report = {
        "schema": "horse_uv0_source_map.v2",
        "sourceGlb": str(builder.SOURCE.relative_to(ROOT)), "sourceSha256": builder.sha256(builder.SOURCE),
        "referenceGlbSha256": sha256(reference), "currentGlbSha256": sha256(current),
        "family": "light", "decimation": decimation,
        "topologyIndexSha256": builder.topology_index_sha256(body),
        "sourceFaceMap": source_region_report,
        "referenceComponentSourceFaceIds": {
            "Body": sorted(old_body), "Mane": sorted(old_by_component["Mane"]),
            "Tail": sorted(old_by_component["Tail"]),
        },
        "currentComponentSourceFaceIds": current_component_ids,
        "fullShellUV0Sha256": digest.hexdigest(),
        "comparison": {
            "uniqueNearestOldBodyFaces": len(old_body), "uniqueNearestCurrentBodyFaces": len(new_body),
            "uniqueNearestNewBodyFacesPreviouslyHair": len(new_body & old_hair),
            "uniqueNearestNewHairFacesPreviouslyBody": len(new_hair & old_body),
            "uniqueNearestOldAndNewBodySharedFaces": len(old_body & new_body),
            "maximumPositionMatchDistance_m": 0.000005, "minimumNearestDistanceGap_m": 0.000001,
            "unmappedOldFaces": old_unknown, "unmappedCurrentFaces": current_unknown,
            "oldBodyFacesComparedToFullShell": old_uv_compared,
            "currentBodyFacesComparedToFullShell": current_uv_compared,
            "maxOldVsFullShellUvDelta": max_old, "maxCurrentVsFullShellUvDelta": max_current,
            "tolerance": 0.000003,
            "reclassificationFaceIds": {
                "currentBodyPreviouslyHair_uniqueMatched": sorted(new_body & old_hair),
                "currentManeOrTailPreviouslyBody_uniqueMatched": sorted(new_hair & old_body),
            },
            "note": "Historical faces map against pre-sole family geometry; corrected faces map against a separately generated forward sole-support transform. Both require three unique nearest source vertices within 5 micrometers; ambiguous/unmatched faces remain unmapped.",
        },
        "faces": face_records,
    }
    SIDE_CAR.parent.mkdir(parents=True, exist_ok=True)
    SIDE_CAR.write_text(json.dumps(report, separators=(",", ":")))
    manifest_path = ROOT / "game/assets/horse/production/horse_light.manifest.json"
    if manifest_path.is_file():
        manifest = json.loads(manifest_path.read_text())
        manifest["uvProvenance"] = {
            "sidecar": str(SIDE_CAR.relative_to(ROOT)), "sidecarSha256": sha256(SIDE_CAR),
            "fullShellUV0Sha256": report["fullShellUV0Sha256"],
            "currentComponentSourceFaceIds": {name: len(ids) for name, ids in current_component_ids.items()},
            "referenceGlbSha256": sha256(reference), "currentGlbSha256": sha256(current),
            "referenceBodyFacesComparedToFullShell": old_uv_compared,
            "currentBodyFacesComparedToFullShell": current_uv_compared,
            "retainedBodyFacesCompared": len(old_body & new_body),
            "maxHistoricalUVDelta": max_old, "maxCurrentGLBUVDelta": max_current,
            "mappingLimit": "Ambiguous/unmatched imported corners fail closed; exact current source IDs and full-shell UVs are in the sidecar.",
        }
        manifest_path.write_text(json.dumps(manifest, indent=2))
    print(json.dumps({
        "sidecar": str(SIDE_CAR.relative_to(ROOT)), "sidecarSha256": sha256(SIDE_CAR),
        "bytes": SIDE_CAR.stat().st_size, "fullShellUV0Sha256": report["fullShellUV0Sha256"],
        "componentFaceCounts": {name: len(ids) for name, ids in current_component_ids.items()},
        "comparison": report["comparison"],
    }, indent=2))


if __name__ == "__main__":
    main()
