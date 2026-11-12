extends SceneTree

var points: Array[Vector3] = []
var mesh_count: int = 0

func _initialize() -> void:
	call_deferred("verify")

func collect(node: Node, parent: Transform3D) -> void:
	var transform := parent
	if node is Node3D: transform = parent * node.transform
	if node is MeshInstance3D and node.mesh != null:
		mesh_count += 1
		var box: AABB = node.mesh.get_aabb()
		for i in range(8): points.append(transform * box.get_endpoint(i))
	for child in node.get_children(): collect(child, transform)

func verify() -> void:
	var assets: Array = JSON.parse_string(FileAccess.get_file_as_string("res://catalog.json"))
	var results: Array = []
	var failures: int = 0
	for a: Dictionary in assets:
		points.clear()
		mesh_count = 0
		var packed := load("res://models/" + str(a.id) + ".glb") as PackedScene
		if packed == null:
			results.append({"id": a.id, "pass": false, "reason": "load failed"})
			failures += 1
			continue
		var instance := packed.instantiate()
		collect(instance, Transform3D.IDENTITY)
		var lower := Vector3(INF,INF,INF)
		var upper := Vector3(-INF,-INF,-INF)
		for p in points:
			lower = lower.min(p)
			upper = upper.max(p)
		var dims := upper-lower
		var passed := mesh_count > 0 and lower.is_finite() and upper.is_finite() and absf(lower.y) < 0.001 and absf(dims.y-float(a.height_m)) < 0.001
		if not passed: failures += 1
		results.append({"id": a.id, "pass": passed, "meshes": mesh_count, "height_m": dims.y, "ground_y": lower.y})
		instance.free()
	var file := FileAccess.open("res://godot-validation.json", FileAccess.WRITE)
	file.store_string(JSON.stringify({"engine": Engine.get_version_info().string, "assets":results,"failures":failures},"  "))
	file.close()
	print("ASSET_IMPORT_VERIFIED ", results.size(), " failures=", failures)
	quit(0 if failures == 0 else 1)
