"""Trace visible landmarks on the frozen adult Light horse and render two views.

Run with Blender in background mode:
  blender -b --python tools/horse/diagnose_anatomy.py -- --blend PATH --manifest PATH --out-dir PATH

Landmarks are selected in the rendered image, then traced through the camera
onto the frontmost evaluated mesh. They are not guessed from bone aliases.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


WIDTH, HEIGHT = 1150, 850
CORAL = (0.96, 0.08, 0.025, 1.0)


def options():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--blend", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--out-dir", required=True)
    return parser.parse_args(argv)


def digest(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = .75
    shader.inputs["Emission Color"].default_value = color
    shader.inputs["Emission Strength"].default_value = .35
    return mat


def camera(scene, name, location, target, width):
    bpy.ops.object.camera_add(location=location)
    cam = bpy.context.object
    cam.name = name
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = width
    scene.camera = cam
    bpy.context.view_layer.update()
    return cam


def ray_from_pixel(scene, cam, x, y):
    # Blender camera rays use image coordinates from the top-left here.
    scale = cam.data.ortho_scale
    aspect = WIDTH / HEIGHT
    origin_local = Vector(((x + .5) / WIDTH * scale - scale / 2,
                           scale / aspect / 2 - (y + .5) / HEIGHT * scale / aspect,
                           0.0))
    origin = cam.matrix_world @ origin_local
    direction = (cam.matrix_world.to_3x3() @ Vector((0, 0, -1))).normalized()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    hit, point, normal, face, obj, matrix = scene.ray_cast(depsgraph, origin, direction, distance=100.0)
    if not hit:
        raise RuntimeError("Selected image pixel did not hit geometry: (%d,%d)" % (x, y))
    return point, normal.normalized(), obj.name, int(face)


def annotation_collection(scene, name):
    col = bpy.data.collections.new(name)
    scene.collection.children.link(col)
    return col


def marker(col, world, label, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=.010, location=world)
    obj = bpy.context.object
    obj.name = "Landmark_" + label.replace(" ", "_")
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)
    obj.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return obj


def screen_text(col, cam, label, x, y, color, size=.027):
    # Put a concise key/label in a camera-front legend outside the horse.
    scale = cam.data.ortho_scale
    aspect = WIDTH / HEIGHT
    cam_local = Vector((x, y, -0.7))
    world = cam.matrix_world @ cam_local
    data = bpy.data.curves.new("Landmark_label_" + label[:18], "FONT")
    data.body = label
    data.size = size
    data.extrude = 0
    obj = bpy.data.objects.new(data.name, data)
    col.objects.link(obj)
    obj.rotation_euler = cam.rotation_euler
    obj.location = world
    data.materials.append(color)
    return obj


def marker_number(col, cam, point, number, color):
    local = cam.matrix_world.inverted() @ point
    data = bpy.data.curves.new("Landmark_id_%02d" % number, "FONT")
    data.body = str(number)
    data.size = .027
    obj = bpy.data.objects.new(data.name, data)
    col.objects.link(obj)
    obj.rotation_euler = cam.rotation_euler
    # Keep the small ID just above/right of its point, on the front overlay plane.
    obj.location = cam.matrix_world @ Vector((local.x + .014, local.y + .012, -.68))
    data.materials.append(color)
    return obj


def trace_group(scene, cam, col, points, color, number_color):
    result = []
    for index, entry in enumerate(points, 1):
        point, normal, obj_name, face = ray_from_pixel(scene, cam, entry["pixel"][0], entry["pixel"][1])
        if obj_name not in {"Body", "Mane", "Eyes"}:
            raise RuntimeError("Unexpected hit object %r for %s" % (obj_name, entry["key"]))
        marker(col, point + normal * .004, entry["key"], color)
        result.append({
            "legend_id": index,
            "key": entry["key"],
            "label": entry["label"],
            "view_pixel_xy_top_left": entry["pixel"],
            "coordinate_m": [round(float(v), 6) for v in point],
            "surface_normal": [round(float(v), 6) for v in normal],
            "hit_object": obj_name,
            "face_index": face,
            "method": "rendered-image pixel ray cast to frontmost evaluated scene geometry",
            "confidence": entry["confidence"],
            "note": entry["note"],
        })
    return result


def main():
    a = options()
    blend = Path(a.blend).resolve()
    manifest_path = Path(a.manifest).resolve()
    out = Path(a.out_dir).resolve()
    out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    scene = bpy.context.scene
    required = ("Body", "Eyes", "Mane", "Skeleton")
    if any(bpy.data.objects.get(name) is None for name in required):
        raise RuntimeError("Expected adult Light snapshot objects: " + ", ".join(required))
    for obj_name in ("Body", "Eyes", "Mane"):
        obj = bpy.data.objects[obj_name]
        if obj.data.shape_keys:
            for key in obj.data.shape_keys.key_blocks:
                if key.name != "Basis":
                    key.value = 0.0
    manifest = json.loads(manifest_path.read_text())

    # Clear cameras/lights from saved scene and standardize a neutral clay view.
    for obj in list(scene.objects):
        if obj.type in {"CAMERA", "LIGHT"}:
            bpy.data.objects.remove(obj, do_unlink=True)
        elif obj.type == "ARMATURE":
            obj.hide_render = True
    clay = material("Anatomy_Neutral_Clay", (.52, .54, .56, 1))
    for name in ("Body", "Eyes", "Mane"):
        obj = bpy.data.objects[name]
        obj.data.materials.clear()
        obj.data.materials.append(clay)
        if obj.type == "MESH":
            for poly in obj.data.polygons:
                poly.material_index = 0
                poly.use_smooth = True

    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 48
    scene.render.resolution_x, scene.render.resolution_y = WIDTH, HEIGHT
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene.world.color = (.07, .07, .07)

    # These camera definitions are the exact clean views used to select pixels.
    views = [
        {"name": "front", "loc": (2.85, 0, 1.70), "target": (.82, 0, 1.68), "width": 1.85,
         "legend_x": -.90, "legend_start_y": .55, "legend_step": -.19,
         "points": [
             {"key": "left_ear_base", "label": "Horse-left ear base (+Y)", "pixel": (640, 320), "confidence": "visible surface point", "note": "Picked on the rendered attachment outside the forelock; horse-left is +Y."},
             {"key": "right_ear_base", "label": "Horse-right ear base (-Y)", "pixel": (510, 320), "confidence": "visible surface point", "note": "Picked on the rendered attachment outside the forelock; horse-right is -Y."},
             {"key": "forelock_between_ears", "label": "Forelock between ears", "pixel": (574, 300), "confidence": "visible surface point", "note": "Visual anchor only; hair-face partition boundaries are owned by the separate hair mapping."},
             {"key": "left_nostril", "label": "Horse-left nostril (+Y)", "pixel": (610, 550), "confidence": "visible depression point", "note": "Picked at the visible nostril depression in the frontal render; not a separate mesh boundary."},
             {"key": "right_nostril", "label": "Horse-right nostril (-Y)", "pixel": (540, 550), "confidence": "visible depression point", "note": "Picked at the visible nostril depression in the frontal render; not a separate mesh boundary."},
             {"key": "muzzle_nose_tip", "label": "Muzzle / nose tip", "pixel": (575, 555), "confidence": "visible surface point", "note": "Centered near the forward-most muzzle surface."},
         ]},
        {"name": "side", "loc": (.82, 2.55, 1.68), "target": (.78, 0, 1.66), "width": 2.20,
         "legend_x": -1.00, "legend_start_y": .48, "legend_step": -.24,
         "points": [
             {"key": "jaw_head_neck_transition", "label": "Behind jaw: head / neck transition", "pixel": (552, 469), "confidence": "visible surface anchor", "note": "Pixel mirrors the reviewed behind-jaw location from the opposite flank; continuous sculpt, not a sharp seam."},
             {"key": "mane_root_behind_ears_poll_end", "label": "Mane root behind ears (poll end)", "pixel": (520, 315), "confidence": "visual crest anchor", "note": "A visible crest point behind the ears; hair boundary remains subject to the separate hair-segmentation map."},
             {"key": "mane_end_near_withers", "label": "Mane end near withers", "pixel": (706, 378), "confidence": "visual crest anchor", "note": "Visible crest endpoint near withers; do not treat the old separate Mane-object bounds as full mane anatomy."},
         ]},
    ]

    report = {
        "diagnostic": "horse_light_image_ray_landmarks.v2",
        "source": {"blend": str(blend), "blend_sha256": digest(blend),
                   "manifest": str(manifest_path), "manifest_sha256": digest(manifest_path),
                   "asset_version": manifest.get("assetVersion"),
                   "coordinate_convention": manifest.get("transforms")},
        "landmarks": {},
        "views": [],
        "scope_note": "Every saved point is selected in a rendered view and ray-cast to visible evaluated geometry. Soft anatomical transitions and hair region boundaries remain visual anchors, not exact segmentation boundaries; see the senior hair-face map when available.",
    }
    marker_mat = material("Landmark_Marker", CORAL)
    label_mat = material("Landmark_Legend", (1.0, .36, .20, 1.0))
    for view in views:
        cam = camera(scene, "Anatomy_Camera_" + view["name"], view["loc"], view["target"], view["width"])
        # Key light is kept on the camera-facing flank so both views show detail.
        light_location = (2.8, 2.0 if view["name"] == "side" else -2.0, 3.2)
        bpy.ops.object.light_add(type="AREA", location=light_location)
        light = bpy.context.object
        light.name = "Anatomy_KeyLight"
        light.data.energy = 430
        light.data.shape = "DISK"
        light.data.size = 2.4
        light.rotation_euler = (Vector((.8, 0, 1.65)) - light.location).to_track_quat("-Z", "Y").to_euler()

        # Render a no-overlay reference first, then raycast those same pixels.
        scene.render.filepath = str(out / ("horse_light_anatomy_%s_clean.png" % view["name"]))
        bpy.ops.render.render(write_still=True)
        overlay = annotation_collection(scene, "Anatomy_Overlay_" + view["name"])
        number_mat = material("Landmark_Number_" + view["name"], (.03, .03, .03, 1.0))
        traced = trace_group(scene, cam, overlay, view["points"], marker_mat, number_mat)
        for idx, entry in enumerate(traced, 1):
            marker_number(overlay, cam, Vector(entry["coordinate_m"]), idx, number_mat)
            screen_text(overlay, cam, "%d  %s" % (idx, entry["label"]),
                        view["legend_x"], view["legend_start_y"] + (idx - 1) * view["legend_step"],
                        label_mat, .026)
            entry["view"] = view["name"]
            report["landmarks"][entry["key"]] = entry
        scene.render.filepath = str(out / ("horse_light_anatomy_%s.png" % view["name"]))
        bpy.ops.render.render(write_still=True)
        report["views"].append({"name": view["name"], "camera": {"location": list(view["loc"]),
                              "target": list(view["target"]), "orthographic_width": view["width"]},
                              "clean_render": str(out / ("horse_light_anatomy_%s_clean.png" % view["name"])),
                              "annotated_render": str(out / ("horse_light_anatomy_%s.png" % view["name"]))})
        bpy.data.objects.remove(light, do_unlink=True)
        bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.collections.remove(overlay)

    path = out / "horse_light_anatomy_landmarks.json"
    path.write_text(json.dumps(report, indent=2) + "\n")
    print("ANATOMY_DIAGNOSTIC_OUT=" + str(out))
    print("ANATOMY_DIAGNOSTIC_LANDMARKS=" + str(path))


if __name__ == "__main__":
    main()
