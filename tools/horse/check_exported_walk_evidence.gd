extends SceneTree

func _initialize() -> void:
	var args = OS.get_cmdline_user_args()
	if args.is_empty() or not ProjectSettings.load_resource_pack(args[0]):
		push_error("Supply the exported PCK to verify independently of the editor project")
		quit(1)
		return
	var catalog = JSON.parse_string(FileAccess.get_file_as_string("res://data/visual_catalog.json"))
	var failures = []
	var checked = []
	var imported_scenes = []
	for family_id in catalog.meshFamilies:
		var family = catalog.meshFamilies[family_id]
		if not family.has("walkVariant"): continue
		var variant = family.walkVariant
		var files = {
			variant.scene:variant.sourceGlbSha256,
			variant.motionProfilePath:variant.motionProfileSha256,
			variant.receiptPath:variant.receiptSha256,
			variant.cpuMotionAuditPath:variant.cpuMotionAuditSha256
		}
		var anatomy_path = variant.motionProfilePath.get_base_dir()+"/anatomy-report.json"
		if not FileAccess.file_exists(anatomy_path): failures.append("Missing raw anatomy proof")
		if not FileAccess.file_exists(variant.motionProfilePath.get_base_dir()+"/native-anatomy-report.json"): failures.append("Missing imported anatomy proof")
		if family_id == "horse.standard.adult.v1":
			var detail = variant.bodySignedDetail
			files[detail.path] = detail.textureSha256
			files[detail.neutralMaskPath] = detail.neutralMaskSha256
			files[family.scene] = family.bodySignedDetail.sourceGlbSha256
		else:
			var directory = variant.motionProfilePath.get_base_dir()
			for name in ["surface-report","dense-deformation-audit","report","anatomy-report","native-anatomy-report"]:
				if not FileAccess.file_exists(directory+"/"+name+".json"): failures.append("Missing raw proof "+name)
			if family.age == "foal" and not FileAccess.file_exists(directory+"/attachment-audit.json"):
				failures.append("Missing foal attachment proof")
			if family.age == "foal" and not FileAccess.file_exists(directory+"/placement-report.json"):
				failures.append("Missing foal placement proof")
			if family.age == "foal" and not FileAccess.file_exists(directory+"/native-hind-report.json"):
				failures.append("Missing foal hind articulation proof")
		if family.age == "foal":
			if not family.has("restGeometry"):
				failures.append("Missing corrected foal rest binding")
			else:
				var rest = family.restGeometry
				files[family.scene] = rest.sourceGlbSha256
				for proof in ["region", "consistency", "morph", "import"]:
					files[rest[proof+"ProofPath"]] = rest[proof+"ProofSha256"]
		for path in files:
			var hash = FileAccess.get_sha256(path)
			if hash != files[path]: failures.append("Missing or altered raw file "+path)
			checked.append({"path":path,"sha256":hash})
		var scene_paths = [variant.scene]
		if family.age == "foal": scene_paths.append(family.scene)
		for scene_path in scene_paths:
			var scene = load(scene_path)
			if scene == null:
				failures.append("Missing imported scene "+scene_path)
				continue
			var model = scene.instantiate()
			var meshes = model.find_children("*","MeshInstance3D",true,false)
			if meshes.size() != 4: failures.append("Missing imported components "+scene_path)
			if family.age == "foal" and scene_path == family.scene:
				for mesh in meshes:
					if mesh.get_blend_shape_count() != 4: failures.append("Missing static foal morphs "+str(mesh.name))
				var players = model.find_children("*","AnimationPlayer",true,false)
				if players.size() != 1 or not players[0].has_animation("Idle"):
					failures.append("Missing packaged foal Idle animation")
			imported_scenes.append(scene_path)
			model.free()
	var report = {"engine":Engine.get_version_info(),"pack":args[0],"rawFiles":checked,"importedScenes":imported_scenes,"failures":failures}
	if args.size()>1:
		var out = FileAccess.open(args[1],FileAccess.WRITE)
		out.store_string(JSON.stringify(report,"  "))
	print("[export-evidence] "+JSON.stringify(report))
	quit(0 if failures.is_empty() else 1)
