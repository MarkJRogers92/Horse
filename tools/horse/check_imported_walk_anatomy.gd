extends SceneTree
## Actual Godot-imported skeleton/skin anatomy. Complements the independent raw GLB audit.

func descendants(node: Node, cls: String, result: Array) -> void:
	if node.is_class(cls): result.append(node)
	for child in node.get_children(): descendants(child, cls, result)

func _initialize() -> void:
	call_deferred("run_check")

func run_check() -> void:
	var args = OS.get_cmdline_user_args()
	var model = load(args[0]).instantiate()
	root.add_child(model)
	var profile = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
	var skeletons = []; var players = []; var meshes = []
	descendants(model,"Skeleton3D",skeletons); descendants(model,"AnimationPlayer",players); descendants(model,"MeshInstance3D",meshes)
	var skeleton: Skeleton3D = skeletons[0]
	var player: AnimationPlayer = players[0]
	var body: MeshInstance3D
	for mesh in meshes:
		if mesh.name == "Body": body = mesh
	var binds = {}
	for i in body.skin.get_bind_count(): binds[str(body.skin.get_bind_name(i))] = body.skin.get_bind_pose(i)
	var walk = "Walk"
	for name in player.get_animation_list():
		if "walk" in name.to_lower(): walk = name
	player.play(walk); player.advance(0)
	var duration = player.get_animation(walk).length
	var foal = "foal" in str(profile.schema)
	var result = {"schema":"horse.walk.anatomy.v1","engine":Engine.get_version_info(),"usesProjectImport":true,"sourceGlbSha256":FileAccess.get_sha256(args[0]),"motionProfileSha256":FileAccess.get_sha256(args[1]),"sampleCount":117,"durationSeconds":duration,"method":"Actual Godot project-imported AnimationPlayer and Skeleton3D. Adult bone heads; foal actual skin matrices applied to elbow/carpus/fetlock rest anchors. Positive offset is anterior to elbow-fetlock chord.","limbs":{},"failures":[]}
	for side in ["l","r"]:
		var metric = {"minSignedCarpusOffsetM":INF,"maxStanceFlexionDegrees":0.0,"maxSwingFlexionDegrees":0.0,"hoofPitchRangeDegrees":0.0,"stanceHoofPitchRangeDegrees":0.0}
		var lo = INF; var hi = -INF; var stance_lo = INF; var stance_hi = -INF
		for sample in 117:
			var phase = sample / 116.0
			player.seek(phase * duration,true); skeleton.force_update_all_bone_transforms()
			var points = []
			var foot: String
			if foal:
				var prefix = "Front_"+side.to_upper()+"_"
				var sign_y = 1.0 if side == "l" else -1.0
				var anchors = [Vector3(0.19,0.70,-sign_y*0.082),Vector3(0.19,0.40,-sign_y*0.082),Vector3(0.184,0.15,-sign_y*0.099)]
				var names = [prefix+"Upper",prefix+"Cannon",prefix+"Hoof_02"]
				for i in 3: points.append(skeleton.get_bone_global_pose(skeleton.find_bone(names[i])) * binds[names[i]] * anchors[i])
				foot = prefix+"Hoof_02"
			else:
				for name in ["front_leg_upper_","front_leg_lower_","front_leg_ankle_"]: points.append(skeleton.get_bone_global_pose(skeleton.find_bone(name+side)).origin)
				foot = "front_leg_ankle_"+side
			var chord: Vector3 = points[2]-points[0]
			var normal = Vector3(-chord.y,chord.x,0).normalized()
			var upper: Vector3 = points[1]-points[0]
			var lower: Vector3 = points[2]-points[1]
			var flex = rad_to_deg(upper.angle_to(lower))
			var forward: Vector3 = (skeleton.get_bone_global_pose(skeleton.find_bone(foot)) * binds[foot]).basis * Vector3.RIGHT
			var pitch = rad_to_deg(atan2(forward.y,forward.x))
			var tau = fposmod(phase-float(profile.limbs["fore_"+side].stanceStartFrame)/float(profile.periodFrames),1.0)
			metric.minSignedCarpusOffsetM = min(metric.minSignedCarpusOffsetM,upper.dot(normal))
			lo = min(lo,pitch); hi = max(hi,pitch)
			if tau < 0.6:
				metric.maxStanceFlexionDegrees = max(metric.maxStanceFlexionDegrees,flex)
				stance_lo = min(stance_lo,pitch); stance_hi = max(stance_hi,pitch)
			else: metric.maxSwingFlexionDegrees = max(metric.maxSwingFlexionDegrees,flex)
		metric.hoofPitchRangeDegrees = hi-lo; metric.stanceHoofPitchRangeDegrees = stance_hi-stance_lo
		if metric.minSignedCarpusOffsetM < -0.002: result.failures.append(side+": backward carpus")
		if metric.maxStanceFlexionDegrees > 12: result.failures.append(side+": crouched supporting foreleg")
		if metric.maxSwingFlexionDegrees < 20 or metric.maxSwingFlexionDegrees > 100: result.failures.append(side+": swing folding outside range")
		if metric.hoofPitchRangeDegrees < 20 or metric.hoofPitchRangeDegrees > 80: result.failures.append(side+": hoof pitch outside range")
		if metric.stanceHoofPitchRangeDegrees > 0.5: result.failures.append(side+": planted hoof rotation")
		result.limbs["fore_"+side] = metric
	var output = FileAccess.open(args[2],FileAccess.WRITE)
	output.store_string(JSON.stringify(result,"  "))
	print(JSON.stringify(result))
	quit(0 if result.failures.is_empty() else 1)
