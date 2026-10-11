"""Fixed source-face regions for inspected Meshy adult and foal sources.

Call ``classify_source_hair_faces(body)`` immediately after the fixed 30k
decimation, before any family warp or face extraction. Mane includes Forelock;
Forelock is also returned separately so its head attachment can be retained in
the single exported Mane mesh. No vertices, UVs, normals or faces are modified.

These IDs are deliberately source-specific. A different source, decimator,
polygon ordering or pre-family geometry fails closed instead of using a
position threshold that can paint the folded mane's underlying neck skin.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import struct


ROOT = Path(__file__).resolve().parents[2]
REGION_DATA = Path(__file__).with_name("anatomy_regions_meshy_light_01.json")
FOAL_REGION_DATA = Path(__file__).with_name("anatomy_regions_meshy_foal_01.json")


def _mesh(body):
    return body.data if hasattr(body, "data") else body


def topology_index_sha256(body) -> str:
    """Match the builder's polygon-index hash, including face order/winding."""
    digest = hashlib.sha256()
    for polygon in _mesh(body).polygons:
        digest.update(len(polygon.vertices).to_bytes(2, "little"))
        for index in polygon.vertices:
            digest.update(int(index).to_bytes(4, "little"))
    return digest.hexdigest()


def source_position_sha256(body) -> str:
    """Guard normalized source coordinates to six decimal places (meters)."""
    digest = hashlib.sha256()
    for vertex in _mesh(body).vertices:
        digest.update(struct.pack("<3d", *(round(float(value), 6) for value in vertex.co)))
    return digest.hexdigest()


def _region_sha256(regions: dict) -> str:
    serialized = json.dumps(regions, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


def _read_guarded_regions(body, data_path: Path | None = None, *,
                          expected_regions=frozenset({"Mane", "Forelock", "Tail"})) -> tuple[dict, dict[str, set[int]]]:
    data = json.loads(Path(data_path or REGION_DATA).read_text())
    if data.get("schemaVersion") != 1:
        raise RuntimeError("Unsupported Meshy anatomical-region schema")
    mesh = _mesh(body)
    expected = data["sourceTopology"]
    if (len(mesh.vertices), len(mesh.polygons)) != (expected["vertexCount"], expected["polygonCount"]):
        raise RuntimeError("Meshy anatomical face IDs require the original fixed decimated mesh")
    if topology_index_sha256(mesh) != expected["indexSha256"]:
        raise RuntimeError("Meshy anatomical face IDs do not match this topology/order")
    if source_position_sha256(mesh) != expected["positionSha256"]:
        stage = "the family warp" if "Tail" in expected_regions else "localized softening or geometry edits"
        raise RuntimeError(f"Classify Meshy anatomical faces before {stage}; source coordinates differ")
    source = ROOT / data["sourceFile"]["path"]
    if hashlib.sha256(source.read_bytes()).hexdigest() != data["sourceFile"]["sha256"]:
        raise RuntimeError("Meshy anatomical face IDs refer to a different immutable source GLB")
    stored = data["regions"]
    if set(stored) != expected_regions or _region_sha256(stored) != data["regionIdsSha256"]:
        raise RuntimeError("Meshy anatomical face-ID data failed its integrity guard")
    regions = {}
    for name, ids in stored.items():
        if (not ids or any(type(index) is not int or not 0 <= index < len(mesh.polygons) for index in ids)
                or ids != sorted(set(ids)) or len(ids) != data["faceCounts"][name]):
            raise RuntimeError(f"Invalid source face IDs in {name}")
        regions[name] = set(ids)
    if not regions["Forelock"] <= regions["Mane"] or regions["Mane"] & regions.get("Tail", set()):
        raise RuntimeError("Forelock must be a Mane subset, and Mane/Tail must be disjoint")
    return data, regions


def classify_source_hair_faces(body, *, data_path: Path | None = None) -> dict[str, set[int]]:
    """Return guarded original polygon IDs; every unclaimed face remains Body."""
    return _read_guarded_regions(body, data_path)[1]


def partition_forelock_vertex_indices(body, partitions: dict[str, set[int]] | None = None) -> set[int]:
    """Map Forelock onto the builder's sorted-source-vertex Mane extraction.

    Call on the intact pre-family source before ``split_sculpted_hair``. The
    resulting indices refer to the future Mane object. Shared root vertices
    are included; the caller owns any head/neck weight blending at that root.
    """
    regions = classify_source_hair_faces(body) if partitions is None else partitions
    if not regions["Forelock"] <= regions["Mane"]:
        raise RuntimeError("Forelock faces are absent from the Mane partition")
    polygons = _mesh(body).polygons
    used = sorted({index for face in regions["Mane"] for index in polygons[face].vertices})
    forelock = {index for face in regions["Forelock"] for index in polygons[face].vertices}
    return {out_index for out_index, source_index in enumerate(used) if source_index in forelock}


def region_report(body) -> dict:
    """Small diagnostic/manifest payload, without storing a second mask model."""
    data, regions = _read_guarded_regions(body)
    return {
        "dataset": REGION_DATA.name,
        "sourceTopology": data["sourceTopology"],
        "regionIdsSha256": data["regionIdsSha256"],
        "sourceFaces": len(_mesh(body).polygons),
        "bodyFaces": len(_mesh(body).polygons) - len(regions["Mane"]) - len(regions["Tail"]),
        "maneFaces": len(regions["Mane"]),
        "forelockFaces": len(regions["Forelock"]),
        "tailFaces": len(regions["Tail"]),
        "forelockIsManeSubset": True,
        "hairRegionsDisjoint": True,
        "classifiedBeforeFamilyWarp": True,
        "method": data["method"],
    }


def classify_foal_source_hair_faces(body, *, data_path: Path | None = None) -> dict[str, set[int]]:
    """Return foal-only crest/stump IDs immediately after fixed decimation.

    Call before localized softening, UV generation or extraction. No Tail is
    included: the builder's authored juvenile tuft has its own geometry. This
    isolates the generated fin/fuzz; it does not repair that source silhouette.
    """
    return _read_guarded_regions(body, data_path or FOAL_REGION_DATA,
                                 expected_regions=frozenset({"Mane", "Forelock"}))[1]


def partition_foal_forelock_vertex_indices(body, partitions: dict[str, set[int]] | None = None) -> set[int]:
    """Future foal Mane indices under sorted-source-vertex face extraction."""
    regions = classify_foal_source_hair_faces(body) if partitions is None else partitions
    return partition_forelock_vertex_indices(body, regions)


def foal_mane_root_boundary(body, partitions: dict[str, set[int]] | None = None) -> dict:
    """Original cut edges and their indices in future Body/Mane partitions.

    Evaluate on the intact fixed source. This describes the retained source
    root only; construction of any authored replacement is builder-owned.
    Vertex mappings match ``split_mesh_faces`` sorted-source-vertex order.
    """
    regions = classify_foal_source_hair_faces(body) if partitions is None else partitions
    mesh = _mesh(body)
    mane = regions["Mane"]
    owners = {}
    for face in mesh.polygons:
        vertices = list(face.vertices)
        for a, b in zip(vertices, vertices[1:] + vertices[:1]):
            owners.setdefault(tuple(sorted((int(a), int(b)))), []).append(face.index)
    if any(len(faces) != 2 for faces in owners.values()):
        raise RuntimeError("Foal root boundary requires the intact welded source shell")
    edges = sorted(edge for edge, faces in owners.items()
                   if (faces[0] in mane) != (faces[1] in mane))
    vertices = sorted({index for edge in edges for index in edge})
    mappings = {}
    for name, selected in (("Body", set(range(len(mesh.polygons))) - mane), ("Mane", mane)):
        used = sorted({index for face in selected for index in mesh.polygons[face].vertices})
        remap = {source: output for output, source in enumerate(used)}
        mappings[name] = [remap[index] for index in vertices]
    return {"sourceEdges": [list(edge) for edge in edges], "sourceVertices": vertices,
            "bodyVertexIndices": mappings["Body"], "maneVertexIndices": mappings["Mane"],
            "sourceCoordinates": [[float(value) for value in mesh.vertices[index].co] for index in vertices]}


def foal_region_report(body) -> dict:
    """Foal boundary evidence; classification is distinct from art approval."""
    data, regions = _read_guarded_regions(body, FOAL_REGION_DATA,
                                         expected_regions=frozenset({"Mane", "Forelock"}))
    return {
        "dataset": FOAL_REGION_DATA.name,
        "sourceTopology": data["sourceTopology"],
        "regionIdsSha256": data["regionIdsSha256"],
        "sourceFaces": len(_mesh(body).polygons),
        "bodyFaces": len(_mesh(body).polygons) - len(regions["Mane"]),
        "maneFaces": len(regions["Mane"]),
        "forelockFaces": len(regions["Forelock"]),
        "forelockIsManeSubset": True,
        "tailExcluded": True,
        "classifiedBeforeGeometryEdits": True,
        "reviewStatus": data["reviewStatus"],
        "sourceHairProductionRecommended": data["sourceHairProductionRecommended"],
        "method": data["method"],
    }
