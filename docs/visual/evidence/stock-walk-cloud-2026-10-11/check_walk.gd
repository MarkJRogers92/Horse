extends SceneTree

func descendants(node: Node, cls: String, result: Array) -> void:
	if node.is_class(cls): result.append(node)
	for child in node.get_children(): descendants(child, cls, result)

func _initialize() -> void:
	call_deferred("run_check")

func run_check() -> void:
	var args = OS.get_cmdline_user_args()
	if args.size() < 2:
		push_error("Usage: -- input.glb report.json")
		quit(2)
		return
	var doc = GLTFDocument.new()
	var state = GLTFState.new()
	var err = doc.append_from_file(args[0], state)
	if err != OK:
		push_error("GLB import failed: " + str(err))
		quit(3)
		return
	var model = doc.generate_scene(state)
	root.add_child(model)
	var players: Array = []
	var skeletons: Array = []
	var meshes: Array = []
	descendants(model, "AnimationPlayer", players)
	descendants(model, "Skeleton3D", skeletons)
	descendants(model, "MeshInstance3D", meshes)
	var report = {"engine": Engine.get_version_info(), "file": args[0], "skeletons": skeletons.size(), "meshCount": meshes.size(), "meshes": [], "animations": [], "failures": []}
	for m in meshes:
		var item = {"name":m.name,"blendShapes":m.mesh.get_blend_shape_count(),"surfaces":m.mesh.get_surface_count(),"skinBinds":m.skin.get_bind_count() if m.skin else 0}
		item["surfaceArrays"] = []
		for si in m.mesh.get_surface_count():
			var a = m.mesh.surface_get_arrays(si)
			item["surfaceArrays"].append({"vertices":a[Mesh.ARRAY_VERTEX].size(),"uv0":a[Mesh.ARRAY_TEX_UV].size() if a[Mesh.ARRAY_TEX_UV] != null else 0,"uv1":a[Mesh.ARRAY_TEX_UV2].size() if a[Mesh.ARRAY_TEX_UV2] != null else 0,"colors":a[Mesh.ARRAY_COLOR].size() if a[Mesh.ARRAY_COLOR] != null else 0})
		report.meshes.append(item)
	if players.is_empty() or skeletons.is_empty(): report.failures.append("Missing skeleton or animation player")
	for player in players:
		for anim_name in player.get_animation_list():
			if not "walk" in anim_name.to_lower(): continue
			var anim = player.get_animation(anim_name)
			player.play(anim_name)
			player.advance(0)
			var frames: Array = []
			for phase in [0.0,0.25,0.5,0.75,1.0]:
				player.seek(anim.length * phase, true)
				for skeleton in skeletons: skeleton.force_update_all_bone_transforms()
				var poses: Array = []
				for skeleton in skeletons:
					for bi in skeleton.get_bone_count(): poses.append(skeleton.get_bone_global_pose(bi))
				frames.append(poses)
			var movement = 0.0
			var loop_error = 0.0
			for bi in frames[0].size():
				for pi in [1,2,3]: movement = max(movement, frames[0][bi].origin.distance_to(frames[pi][bi].origin))
				loop_error = max(loop_error, frames[0][bi].origin.distance_to(frames[4][bi].origin))
			report.animations.append({"name":anim_name,"length":anim.length,"tracks":anim.get_track_count(),"boneMovementMeters":movement,"endpointTranslationErrorMeters":loop_error,"importLoopMode":anim.loop_mode})
			if movement < 0.01: report.failures.append("Walk has insufficient bone movement")
			if loop_error > 0.002: report.failures.append("Walk endpoints do not match")
	if report.animations.is_empty(): report.failures.append("No Walk animation")
	if args.size() > 2 and not players.is_empty() and not skeletons.is_empty():
		report["contact"] = check_contact(model, players[0], skeletons[0], meshes, args[2])
		report.failures.append_array(report.contact.failures)
	var file = FileAccess.open(args[1], FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"  "))
	print(JSON.stringify(report))
	quit(0 if report.failures.is_empty() else 1)

func vec(a: Array) -> Vector3:
	return Vector3(a[0], a[1], a[2])

func check_contact(model: Node, player: AnimationPlayer, skeleton: Skeleton3D, meshes: Array, profile_path: String) -> Dictionary:
	var profile = JSON.parse_string(FileAccess.get_file_as_string(profile_path))
	var body: MeshInstance3D
	for m in meshes:
		if m.name == "Body": body = m
	var result = {"registered":0,"failures":[],"limbs":{}}
	if body == null:
		result.failures.append("No Body")
		return result
	var skin = body.skin
	var mapping = []
	for i in skin.get_bind_count():
		var bn = skin.get_bind_name(i)
		mapping.append(skeleton.find_bone(bn) if not bn.is_empty() else skin.get_bind_bone(i))
	var period = float(profile.periodFrames)
	var fps = float(profile.fps)
	var duty = float(profile.dutyFactor)
	var cycle_distance = float(profile.forwardSpeedMetersPerSecond) * period / fps
	var skeleton_from_body = skeleton.global_transform.affine_inverse() * body.global_transform
	for limb_name in profile.limbs:
		var limb = profile.limbs[limb_name]
		var bi = skeleton.find_bone(limb.boneName)
		if bi < 0:
			result.failures.append("Missing limb bone: " + limb.boneName)
			continue
		var witnesses = []
		for point in limb.solePoints:
			var rest = vec(point.restPosition)
			var matched = false
			for si in body.mesh.get_surface_count():
				var a = body.mesh.surface_get_arrays(si)
				var width = a[Mesh.ARRAY_BONES].size() / a[Mesh.ARRAY_VERTEX].size()
				for vi in a[Mesh.ARRAY_VERTEX].size():
					if (skeleton_from_body * a[Mesh.ARRAY_VERTEX][vi]).distance_to(rest) > 0.00002: continue
					var mass = 0.0
					var other = 0.0
					var binding = -1
					for wi in width:
						var idx = a[Mesh.ARRAY_BONES][vi * width + wi]
						var w = a[Mesh.ARRAY_WEIGHTS][vi * width + wi]
						if mapping[idx] == bi:
							mass += w
							if w > 0.0: binding = idx
						else: other += w
					if mass >= 0.99999 and other <= 0.000001:
						witnesses.append({"bind": binding,"local":a[Mesh.ARRAY_VERTEX][vi],"min":Vector3(INF,INF,INF),"max":Vector3(-INF,-INF,-INF)})
						matched = true
						break
				if matched: break
			if not matched: result.failures.append("Unregistered sole " + limb_name + ":" + str(point.sourceVertex))
		result.registered += witnesses.size()
		var min_stance = INF
		var max_min_stance = -INF
		var swing_min = INF
		var stance_start = float(limb.stanceStartFrame) / period
		for sample in 61:
			var phase = sample / 60.0
			player.seek(phase * period / fps, true)
			skeleton.force_update_all_bone_transforms()
			var tau = fposmod(phase - stance_start, 1.0)
			var stance = tau <= duty + 0.0000001
			var min_here = INF
			for witness in witnesses:
				var p = skeleton.get_bone_global_pose(bi) * skin.get_bind_pose(witness.bind) * witness.local
				min_here = min(min_here, p.y)
				if stance:
					p.x += cycle_distance * (phase + (1.0 if phase < stance_start else 0.0))
					if sample == 60 and stance_start == 0.0: p.x -= cycle_distance
					witness.min = witness.min.min(p)
					witness.max = witness.max.max(p)
			if stance:
				min_stance = min(min_stance, min_here)
				max_min_stance = max(max_min_stance, min_here)
			elif (tau-duty)/(1.0-duty) > 0.25 and (tau-duty)/(1.0-duty) < 0.75: swing_min = min(swing_min,min_here)
		var slip = 0.0
		for witness in witnesses:
			var extent = witness.max - witness.min
			slip = max(slip,max(extent.x,max(extent.y,extent.z)))
		result.limbs[limb_name] = {"registered":witnesses.size(),"maxPerAxisStanceMotionMeters":slip,"minStanceHeightMeters":min_stance,"maxStanceSoleMinimumMeters":max_min_stance,"centralSwingMinimumMeters":swing_min}
		if slip > 0.005 or min_stance < -0.002 or max_min_stance > 0.003 or swing_min < 0.01:
			result.failures.append("Contact gate failed: " + limb_name)
	return result
