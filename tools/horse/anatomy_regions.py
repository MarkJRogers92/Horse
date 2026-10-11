"""Fixed source-face regions for the inspected Meshy light_01 adult.

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


def _read_guarded_regions(body, data_path: Path | None = None) -> tuple[dict, dict[str, set[int]]]:
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
        raise RuntimeError("Classify Meshy anatomical faces before the family warp; source coordinates differ")
    source = ROOT / data["sourceFile"]["path"]
    if hashlib.sha256(source.read_bytes()).hexdigest() != data["sourceFile"]["sha256"]:
        raise RuntimeError("Meshy anatomical face IDs refer to a different immutable source GLB")
    stored = data["regions"]
    if set(stored) != {"Mane", "Forelock", "Tail"} or _region_sha256(stored) != data["regionIdsSha256"]:
        raise RuntimeError("Meshy anatomical face-ID data failed its integrity guard")
    regions = {}
    for name, ids in stored.items():
        if (not ids or any(type(index) is not int or not 0 <= index < len(mesh.polygons) for index in ids)
                or ids != sorted(set(ids)) or len(ids) != data["faceCounts"][name]):
            raise RuntimeError(f"Invalid source face IDs in {name}")
        regions[name] = set(ids)
    if not regions["Forelock"] <= regions["Mane"] or regions["Mane"] & regions["Tail"]:
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
