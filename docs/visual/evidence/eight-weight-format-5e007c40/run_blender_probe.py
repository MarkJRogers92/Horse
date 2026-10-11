import bpy
import json
import os
import sys


def arg_after(flag):
    if flag not in sys.argv:
        raise RuntimeError(f"missing {flag}")
    return sys.argv[sys.argv.index(flag) + 1]


source = os.path.abspath(arg_after("--input"))
output = os.path.abspath(arg_after("--output"))
report_path = os.path.abspath(arg_after("--report"))
bpy.ops.wm.open_mainfile(filepath=source, load_ui=False, use_scripts=False)

objects = list(bpy.context.scene.objects)
armatures = [obj for obj in objects if obj.type == "ARMATURE"]
mesh_objects = [obj for obj in objects if obj.type == "MESH"]
meshes = []
for obj in mesh_objects:
    mesh = obj.data
    max_influences = 0
    influence_histogram = {}
    greater_than_four = 0
    max_weight_sum_error = 0.0
    for vertex in mesh.vertices:
        count = 0
        total = 0.0
        for assignment in vertex.groups:
            if assignment.weight > 1e-8:
                count += 1
                total += assignment.weight
        max_influences = max(max_influences, count)
        influence_histogram[str(count)] = influence_histogram.get(str(count), 0) + 1
        if count > 4:
            greater_than_four += 1
            max_weight_sum_error = max(max_weight_sum_error, abs(total - 1.0))
    meshes.append({
        "object": obj.name,
        "vertexCount": len(mesh.vertices),
        "polygonCount": len(mesh.polygons),
        "vertexGroupCount": len(obj.vertex_groups),
        "maxNonzeroInfluences": max_influences,
        "verticesWithMoreThanFour": greater_than_four,
        "influenceHistogram": influence_histogram,
        "maxEightPlusWeightSumError": max_weight_sum_error,
        "armatureModifiers": [m.object.name for m in obj.modifiers if m.type == "ARMATURE" and m.object],
    })

op = bpy.ops.export_scene.gltf.get_rna_type()
props = {p.identifier: {"type": p.type, "default": p.default} for p in op.properties if p.identifier in {"export_influence_nb", "export_all_influences", "export_skins", "export_format", "export_animations"}}
if "export_influence_nb" not in props:
    raise RuntimeError("Blender glTF exporter has no export_influence_nb option")

result = bpy.ops.export_scene.gltf(
    filepath=output,
    export_format="GLB",
    export_skins=True,
    export_influence_nb=8,
    export_all_influences=False,
    export_animations=False,
)
if "FINISHED" not in result:
    raise RuntimeError(f"glTF export did not finish: {result}")

report = {
    "blenderVersion": bpy.app.version_string,
    "sourceBlend": source,
    "outputGlb": output,
    "exportOperatorProperties": props,
    "sceneObjectCount": len(objects),
    "armatures": [{"name": obj.name, "boneCount": len(obj.data.bones)} for obj in armatures],
    "meshes": meshes,
    "exportResult": sorted(result),
}
with open(report_path, "w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=2, sort_keys=True)
print("EIGHT_WEIGHT_PROBE_REPORT=" + report_path)
