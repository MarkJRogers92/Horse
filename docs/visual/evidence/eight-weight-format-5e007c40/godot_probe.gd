extends SceneTree

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var packed := load("res://horse_light_float_probe_8weights.glb") as PackedScene
	if packed == null:
		push_error("GLB did not load as PackedScene")
		quit(2)
		return
	var root := packed.instantiate()
	var skeletons: Array[Skeleton3D] = []
	var meshes: Array[MeshInstance3D] = []
	_collect(root, skeletons, meshes)
	var skeleton_count := 0
	for skeleton in skeletons:
		skeleton_count = maxi(skeleton_count, skeleton.get_bone_count())
	var report := {
		"godotVersion": Engine.get_version_info(),
		"source": "res://horse_light_float_probe_8weights.glb",
		"skeletons": [],
		"meshes": [],
		"checks": {"allFinite": true, "allNormalized": true, "normalizationToleranceAbsolute": 2.0e-4, "bonesInRange": true, "eightInfluenceSurfaces": 0, "lodSurfaces": 0, "surfaceCount": 0}
	}
	for skeleton in skeletons:
		var bone_names: Array[String] = []
		for bone_index in range(skeleton.get_bone_count()):
			bone_names.append(skeleton.get_bone_name(bone_index))
		(report["skeletons"] as Array).append({"name": skeleton.name, "boneCount": skeleton.get_bone_count(), "rootBone": skeleton.get_bone_name(skeleton.get_bone_count() - 1) if skeleton.get_bone_count() > 0 else "", "boneNamesInIndexOrder": bone_names})
	for instance in meshes:
		if not instance.mesh is ArrayMesh:
			continue
		var mesh := instance.mesh as ArrayMesh
		for surface in range(mesh.get_surface_count()):
			report["checks"]["surfaceCount"] += 1
			var arrays := mesh.surface_get_arrays(surface)
			var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			var bones: PackedInt32Array = arrays[Mesh.ARRAY_BONES]
			var weights: PackedFloat32Array = arrays[Mesh.ARRAY_WEIGHTS]
			var influence_stride := int(weights.size() / vertices.size()) if not vertices.is_empty() else 0
			var sum_min := INF
			var sum_max := -INF
			var max_nonzero := 0
			var vertices_more_than_four := 0
			var all_finite := true
			var normalized := true
			var in_range := true
			for vertex_index in range(vertices.size()):
				var total := 0.0
				var nonzero := 0
				for influence in range(influence_stride):
					var weight := weights[vertex_index * influence_stride + influence]
					var bone := bones[vertex_index * influence_stride + influence]
					all_finite = all_finite and is_finite(weight)
					if weight > 1.0e-8:
						nonzero += 1
						if bone < 0 or bone >= skeleton_count:
							in_range = false
					total += weight
				max_nonzero = maxi(max_nonzero, nonzero)
				if nonzero > 4:
					vertices_more_than_four += 1
				if total > 1.0e-8:
					sum_min = minf(sum_min, total)
					sum_max = maxf(sum_max, total)
					# Godot's imported vertex buffers slightly quantize weights; bound the measured roundoff.
					normalized = normalized and absf(total - 1.0) <= 2.0e-4
			var native_surface: Dictionary = RenderingServer.mesh_get_surface(mesh.get_rid(), surface)
			var lods: Array = native_surface.get("lods", [])
			var lod_info: Array[Dictionary] = []
			for lod in lods:
				var lod_bytes: PackedByteArray = lod.get("index_data", PackedByteArray())
				lod_info.append({"edgeLength": lod.get("edge_length", 0.0), "indexDataBytes": lod_bytes.size(), "bufferPresent": not lod_bytes.is_empty()})
			if influence_stride >= 8 and vertices_more_than_four > 0:
				report["checks"]["eightInfluenceSurfaces"] += 1
			if not lods.is_empty():
				report["checks"]["lodSurfaces"] += 1
			report["checks"]["allFinite"] = report["checks"]["allFinite"] and all_finite
			report["checks"]["allNormalized"] = report["checks"]["allNormalized"] and normalized
			report["checks"]["bonesInRange"] = report["checks"]["bonesInRange"] and in_range
			(report["meshes"] as Array).append({
				"node": str(instance.name), "mesh": mesh.resource_name, "surface": surface,
				"vertexCount": vertices.size(), "indexCount": mesh.surface_get_array_index_len(surface),
				"boneArraySize": bones.size(), "weightArraySize": weights.size(), "influenceStride": influence_stride,
				"maxNonzeroInfluences": max_nonzero, "verticesMoreThanFour": vertices_more_than_four,
				"weightSumMin": sum_min, "weightSumMax": sum_max, "allFinite": all_finite,
				"normalized": normalized, "boneIndicesInRange": in_range,
				"lodCount": lods.size(), "lods": lod_info
			})
	var failures: Array[String] = []
	if skeleton_count != 58:
		failures.append("expected 58 imported bones; got %d" % skeleton_count)
	for key in ["allFinite", "allNormalized", "bonesInRange"]:
		if not report["checks"][key]:
			failures.append("check failed: %s" % key)
	if report["checks"]["eightInfluenceSurfaces"] < 1:
		failures.append("no surface retained eight influence slots with >4 nonzero weights")
	if report["checks"]["lodSurfaces"] != report["checks"]["surfaceCount"]:
		failures.append("not every surface has generated LODs")
	for item in report["meshes"]:
		for lod in item["lods"]:
			if not lod["bufferPresent"]:
				failures.append("empty LOD index buffer: %s surface %d" % [item["node"], item["surface"]])
	report["checks"]["failureReasons"] = failures
	report["checks"]["passed"] = failures.is_empty()
	var out := FileAccess.open("res://godot_probe_report.json", FileAccess.WRITE)
	out.store_string(JSON.stringify(report, "\t"))
	out.close()
	print("GODOT_EIGHT_WEIGHT_REPORT=" + ProjectSettings.globalize_path("res://godot_probe_report.json"))
	print(JSON.stringify(report))
	root.free()
	quit(0 if report["checks"]["passed"] else 1)

func _collect(node: Node, skeletons: Array[Skeleton3D], meshes: Array[MeshInstance3D]) -> void:
	if node is Skeleton3D:
		skeletons.append(node)
	if node is MeshInstance3D:
		meshes.append(node)
	for child in node.get_children():
		_collect(child, skeletons, meshes)
