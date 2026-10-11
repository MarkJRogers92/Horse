import json
import struct
import sys

path = sys.argv[1]
with open(path, "rb") as f:
    blob = f.read()
magic, version, total_len = struct.unpack_from("<4sII", blob, 0)
assert magic == b"glTF" and version == 2 and total_len == len(blob)
offset = 12
doc = None
binary = None
while offset < len(blob):
    length, kind = struct.unpack_from("<I4s", blob, offset)
    offset += 8
    chunk = blob[offset:offset + length]
    offset += length
    if kind == b"JSON":
        doc = json.loads(chunk.decode("utf-8").rstrip(" \t\r\n\0"))
    elif kind == b"BIN\0":
        binary = chunk
assert doc is not None and binary is not None

component_formats = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
type_widths = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

def values(accessor_index):
    accessor = doc["accessors"][accessor_index]
    view = doc["bufferViews"][accessor["bufferView"]]
    fmt, component_bytes = component_formats[accessor["componentType"]]
    width = type_widths[accessor["type"]]
    stride = view.get("byteStride", component_bytes * width)
    base = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    output = []
    for item in range(accessor["count"]):
        start = base + item * stride
        output.append(struct.unpack_from("<" + fmt * width, binary, start))
    return accessor, output

summary = {"version": doc.get("asset", {}).get("version"), "skins": [], "primitives": []}
for skin in doc.get("skins", []):
    joint_nodes = skin.get("joints", [])
    summary["skins"].append({
        "name": skin.get("name"),
        "jointCount": len(joint_nodes),
        "skeleton": skin.get("skeleton"),
        "jointNamesInIndexOrder": [doc.get("nodes", [])[node_index].get("name", f"node_{node_index}") for node_index in joint_nodes],
    })
for mesh in doc.get("meshes", []):
    for primitive_index, primitive in enumerate(mesh.get("primitives", [])):
        attrs = primitive.get("attributes", {})
        item = {"mesh": mesh.get("name"), "primitive": primitive_index, "attributes": {}}
        selected = {name: index for name, index in attrs.items() if name.startswith("JOINTS_") or name.startswith("WEIGHTS_")}
        decoded = {}
        for name, accessor_index in sorted(selected.items()):
            accessor, data = values(accessor_index)
            item["attributes"][name] = {
                "accessor": accessor_index,
                "componentType": accessor["componentType"],
                "type": accessor["type"],
                "count": accessor["count"],
                "normalized": accessor.get("normalized", False),
                "min": accessor.get("min"),
                "max": accessor.get("max"),
            }
            decoded[name] = data
        weight_names = sorted(name for name in decoded if name.startswith("WEIGHTS_"))
        if "WEIGHTS_0" in decoded and "WEIGHTS_1" in decoded:
            sums = [sum(decoded["WEIGHTS_0"][i]) + sum(decoded["WEIGHTS_1"][i]) for i in range(len(decoded["WEIGHTS_0"]))]
            item["combinedWeightSums"] = {"min": min(sums), "max": max(sums), "finite": all(map(lambda x: x == x and abs(x) != float("inf"), sums))}
            item["nonzeroSecondSetVertices"] = sum(any(x > 1e-8 for x in values) for values in decoded["WEIGHTS_1"])
            item["weightsFloat32"] = all(item["attributes"][name]["componentType"] == 5126 for name in weight_names)
        summary["primitives"].append(item)
print(json.dumps(summary, indent=2, sort_keys=True))
