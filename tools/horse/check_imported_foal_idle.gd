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
	var bake_fps = 30.0
	if args.size() > 2: bake_fps = float(JSON.parse_string(FileAccess.get_file_as_string(args[2])).fps)
	state.bake_fps = bake_fps
	var imported = "--imported" in args
	var model: Node
	if imported:
		model = load(args[0]).instantiate()
	else:
		var err = doc.append_from_file(args[0], state)
		if err != OK:
			push_error("GLB import failed: " + str(err))
			quit(3)
			return
		model = doc.generate_scene(state, bake_fps)
	root.add_child(model)
	var players: Array = []
	var skeletons: Array = []
	var meshes: Array = []
	descendants(model, "AnimationPlayer", players)
	descendants(model, "Skeleton3D", skeletons)
	descendants(model, "MeshInstance3D", meshes)
	var report = {"engine": Engine.get_version_info(), "usesProjectImport": imported, "candidateGlbSha256": FileAccess.get_sha256(args[0]), "importBakeFps": bake_fps, "file": args[0], "skeletons": skeletons.size(), "meshCount": meshes.size(), "meshes": [], "animations": [], "failures": []}
	if imported:
		var settings = ConfigFile.new()
		var import_error = settings.load(args[0]+".import")
		report["projectImportFps"] = settings.get_value("params","animation/fps",0) if import_error == OK else 0
		var subresources = settings.get_value("params","_subresources",{}) if import_error == OK else {}
		report["animationOptimizerEnabled"] = subresources.get("nodes",{}).get("PATH:AnimationPlayer",{}).get("optimizer/enabled",true)
		if report.projectImportFps != bake_fps or report.animationOptimizerEnabled:
			report.failures.append("Project importer fps/optimizer differs from checked profile")
	for m in meshes:
		var item = {"name":m.name,"blendShapes":m.mesh.get_blend_shape_count(),"surfaces":m.mesh.get_surface_count(),"skinBinds":m.skin.get_bind_count() if m.skin else 0}
		item["surfaceArrays"] = []
		item["zeroWeightVertices"] = 0
		item["nonUnitWeightVertices"] = 0
		for si in m.mesh.get_surface_count():
			var wa = m.mesh.surface_get_arrays(si)
			var vc = wa[Mesh.ARRAY_VERTEX].size()
			var weights = wa[Mesh.ARRAY_WEIGHTS]
			if weights == null:
				item.zeroWeightVertices += vc
				continue
			var width = weights.size() / vc
			for vi in vc:
				var mass = 0.0
				for wi in width: mass += weights[vi * width + wi]
				if mass < 0.000001: item.zeroWeightVertices += 1
				if abs(mass - 1.0) > 0.0001: item.nonUnitWeightVertices += 1
		if item.zeroWeightVertices > 0 or item.nonUnitWeightVertices > 0:
			report.failures.append("Missing or nonunit skin weights: " + str(m.name))
		for si in m.mesh.get_surface_count():
			var a = m.mesh.surface_get_arrays(si)
			item["surfaceArrays"].append({"vertices":a[Mesh.ARRAY_VERTEX].size(),"uv0":a[Mesh.ARRAY_TEX_UV].size() if a[Mesh.ARRAY_TEX_UV] != null else 0,"uv1":a[Mesh.ARRAY_TEX_UV2].size() if a[Mesh.ARRAY_TEX_UV2] != null else 0,"colors":a[Mesh.ARRAY_COLOR].size() if a[Mesh.ARRAY_COLOR] != null else 0})
		report.meshes.append(item)
	if players.is_empty() or skeletons.is_empty(): report.failures.append("Missing skeleton or animation player")
	for player in players:
		for anim_name in player.get_animation_list():
			if not "idle" in anim_name.to_lower(): continue
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
			var loop_basis_error = 0.0
			for bi in frames[0].size():
				for pi in [1,2,3]: movement = max(movement, frames[0][bi].origin.distance_to(frames[pi][bi].origin))
				loop_error = max(loop_error, frames[0][bi].origin.distance_to(frames[4][bi].origin))
				for axis in 3: loop_basis_error = max(loop_basis_error,frames[0][bi].basis[axis].distance_to(frames[4][bi].basis[axis]))
			report.animations.append({"name":anim_name,"length":anim.length,"tracks":anim.get_track_count(),"boneMovementMeters":movement,"endpointTranslationErrorMeters":loop_error,"endpointBasisError":loop_basis_error,"importLoopMode":anim.loop_mode})
			if movement < 0.000001: report.failures.append("Idle has no motion")
			if loop_error > 0.002 or loop_basis_error > 0.00001: report.failures.append("Idle endpoint transforms do not match")
	if report.animations.is_empty(): report.failures.append("No Idle animation")
	if not players.is_empty() and not skeletons.is_empty():
		report["skinDeformation"] = check_skin_deformation(players[0],skeletons[0],meshes)
		report.failures.append_array(report.skinDeformation.failures)
		var components = {"Body":report.skinDeformation.duplicate(true)}
		for component in ["Eyes","Mane","Tail"]:
			components[component] = check_skin_deformation(players[0],skeletons[0],meshes,component)
			report.failures.append_array(components[component].failures)
		report["allComponentDeformation"] = components
	report["soleSupport"] = check_idle_support(players[0], skeletons[0], meshes)
	report.failures.append_array(report.soleSupport.failures)
	report["attachments"] = check_attachments(players[0], skeletons[0], meshes)
	report.failures.append_array(report.attachments.failures)
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

func check_skin_deformation(player: AnimationPlayer, skeleton: Skeleton3D, meshes: Array, component_name: String = "Body") -> Dictionary:
	var body: MeshInstance3D
	for m in meshes:
		if m.name == component_name: body = m
	var skin = body.skin
	var mapping = []
	for i in skin.get_bind_count():
		var bn = skin.get_bind_name(i)
		mapping.append(skeleton.find_bone(bn) if not bn.is_empty() else skin.get_bind_bone(i))
	var a = body.mesh.surface_get_arrays(0)
	var vertices = a[Mesh.ARRAY_VERTEX]
	var indices = a[Mesh.ARRAY_INDEX]
	var width = a[Mesh.ARRAY_BONES].size() / vertices.size()
	var edges: Dictionary = {}
	for i in range(0,indices.size(),3):
		for pair in [Vector2i(indices[i],indices[i+1]),Vector2i(indices[i+1],indices[i+2]),Vector2i(indices[i+2],indices[i])]:
			var key = Vector2i(min(pair.x,pair.y),max(pair.x,pair.y))
			var length = vertices[key.x].distance_to(vertices[key.y])
			if length > 0.000001: edges[key] = length
	var result = {"samples":61,"maxBodyEdgeStretch":0.0,"worstPhase":0.0,"worstEdge":[],"failures":[]}
	var walk_name = "Idle"
	for an in player.get_animation_list():
		if "idle" in an.to_lower(): walk_name = an
	player.play(walk_name)
	player.advance(0)
	var length = player.get_animation(walk_name).length
	for sample in 61:
		var phase = sample / 60.0
		player.seek(phase * length,true)
		skeleton.force_update_all_bone_transforms()
		var transforms = []
		for bi in skin.get_bind_count(): transforms.append(skeleton.get_bone_global_pose(mapping[bi]) * skin.get_bind_pose(bi))
		var posed = PackedVector3Array()
		posed.resize(vertices.size())
		for vi in vertices.size():
			var p = Vector3.ZERO
			for wi in width:
				var w = a[Mesh.ARRAY_WEIGHTS][vi * width + wi]
				if w > 0.0: p += w * (transforms[a[Mesh.ARRAY_BONES][vi * width + wi]] * vertices[vi])
			posed[vi] = p
		for edge in edges:
			var ratio = posed[edge.x].distance_to(posed[edge.y]) / edges[edge]
			if ratio > result.maxBodyEdgeStretch:
				result.maxBodyEdgeStretch = ratio
				result.worstPhase = phase
				result.worstEdge = [edge.x,edge.y]
	if result.maxBodyEdgeStretch > 2.0: result.failures.append("Imported " + component_name + " edge stretch exceeds 2x")
	return result

# Closest-point barycentrics are fixed at rest, then evaluated through each mesh's
# actual imported Skin binds. This checks opening joints, not coincident bone pivots.
func triangle_weights(p: Vector3, a: Vector3, b: Vector3, c: Vector3) -> Vector3:
	var ab = b-a
	var ac = c-a
	var ap = p-a
	var d1 = ab.dot(ap)
	var d2 = ac.dot(ap)
	if d1 <= 0 and d2 <= 0: return Vector3(1,0,0)
	var bp = p-b
	var d3 = ab.dot(bp)
	var d4 = ac.dot(bp)
	if d3 >= 0 and d4 <= d3: return Vector3(0,1,0)
	var vc = d1*d4-d3*d2
	if vc <= 0 and d1 >= 0 and d3 <= 0:
		var v = d1/(d1-d3)
		return Vector3(1-v,v,0)
	var cp = p-c
	var d5 = ab.dot(cp)
	var d6 = ac.dot(cp)
	if d6 >= 0 and d5 <= d6: return Vector3(0,0,1)
	var vb = d5*d2-d1*d6
	if vb <= 0 and d2 >= 0 and d6 <= 0:
		var w = d2/(d2-d6)
		return Vector3(1-w,0,w)
	var va = d3*d6-d5*d4
	if va <= 0 and (d4-d3) >= 0 and (d5-d6) >= 0:
		var w = (d4-d3)/((d4-d3)+(d5-d6))
		return Vector3(0,1-w,w)
	var denominator = va+vb+vc
	if abs(denominator) < 1e-20: return Vector3(1,0,0)
	var v = vb/denominator
	var w = vc/denominator
	return Vector3(1-v-w,v,w)

func grid_key(p: Vector3) -> Vector3i:
	return Vector3i(floor(p.x/0.08),floor(p.y/0.08),floor(p.z/0.08))

func skin_context(mesh: MeshInstance3D, skeleton: Skeleton3D) -> Dictionary:
	var a = mesh.mesh.surface_get_arrays(0)
	var map = []
	for i in mesh.skin.get_bind_count():
		var name = mesh.skin.get_bind_name(i)
		map.append(skeleton.find_bone(name) if not name.is_empty() else mesh.skin.get_bind_bone(i))
	return {"mesh":mesh,"arrays":a,"map":map,"width":a[Mesh.ARRAY_WEIGHTS].size()/a[Mesh.ARRAY_VERTEX].size()}

func skin_point(ctx: Dictionary, vertex: int, skeleton: Skeleton3D) -> Vector3:
	var result = Vector3.ZERO
	var a = ctx.arrays
	for wi in ctx.width:
		var weight = a[Mesh.ARRAY_WEIGHTS][vertex*ctx.width+wi]
		if weight <= 0: continue
		var bind = a[Mesh.ARRAY_BONES][vertex*ctx.width+wi]
		result += weight*(skeleton.get_bone_global_pose(ctx.map[bind])*ctx.mesh.skin.get_bind_pose(bind)*a[Mesh.ARRAY_VERTEX][vertex])
	return result

func check_attachments(player: AnimationPlayer, skeleton: Skeleton3D, meshes: Array) -> Dictionary:
	var contexts = {}
	for mesh in meshes: contexts[str(mesh.name)] = skin_context(mesh,skeleton)
	var body = contexts.Body.arrays[Mesh.ARRAY_VERTEX]
	var indices = contexts.Body.arrays[Mesh.ARRAY_INDEX]
	var grid = {}
	for i in range(0,indices.size(),3):
		var center = (body[indices[i]]+body[indices[i+1]]+body[indices[i+2]])/3.0
		var key = grid_key(center)
		if not grid.has(key): grid[key]=[]
		grid[key].append(i)
	var is_foal = "foal" in OS.get_cmdline_user_args()[0]
	var maximum_rest_gap = 0.012 if is_foal else 0.00002
	var witnesses = []
	var result = {"samples":61,"method":"Fixed closest rest-triangle barycentrics, imported Skin binds", "components":{},"failures":[]}
	for component in ["Mane","Tail"]:
		var vertices = contexts[component].arrays[Mesh.ARRAY_VERTEX]
		var count = 0
		for vi in vertices.size():
			var p = vertices[vi]
			var key = grid_key(p)
			var nearest = maximum_rest_gap
			var witness = {}
			for dx in range(-1,2):
				for dy in range(-1,2):
					for dz in range(-1,2):
						for ti in grid.get(key+Vector3i(dx,dy,dz),[]):
							var ids = Vector3i(indices[ti],indices[ti+1],indices[ti+2])
							var bary = triangle_weights(p,body[ids.x],body[ids.y],body[ids.z])
							var gap = p-(body[ids.x]*bary.x+body[ids.y]*bary.y+body[ids.z]*bary.z)
							if gap.length() <= nearest:
								nearest = gap.length()
								witness = {"component":component,"vertex":vi,"triangle":ids,"bary":bary,"restGap":gap}
			if not witness.is_empty(): witnesses.append(witness); count += 1
		result.components[component] = {"witnessCount":count,"maximumGapDriftMeters":0.0}
		if count == 0: result.failures.append("No imported attachment witnesses: "+component)
	var name = "Idle"
	player.play(name)
	player.advance(0)
	for sample in 61:
		player.seek(sample/60.0*player.get_animation(name).length,true)
		skeleton.force_update_all_bone_transforms()
		for w in witnesses:
			var ids = w.triangle
			var surface = skin_point(contexts.Body,ids.x,skeleton)*w.bary.x+skin_point(contexts.Body,ids.y,skeleton)*w.bary.y+skin_point(contexts.Body,ids.z,skeleton)*w.bary.z
			var gap = skin_point(contexts[w.component],w.vertex,skeleton)-surface
			var drift = (gap-w.restGap).length()
			result.components[w.component].maximumGapDriftMeters = max(result.components[w.component].maximumGapDriftMeters,drift)
	for component in result.components:
		if result.components[component].maximumGapDriftMeters > 0.002:
			result.failures.append("Imported attachment gap drift exceeds 2 mm: "+component)
	return result

# Neutral Idle: all imported sole vertices stay on the floor without a gait path.
func check_idle_support(player: AnimationPlayer, skeleton: Skeleton3D, meshes: Array) -> Dictionary:
	var body: MeshInstance3D
	for mesh in meshes:
		if mesh.name=="Body":body=mesh
	var ctx=skin_context(body,skeleton)
	var vertices=ctx.arrays[Mesh.ARRAY_VERTEX]
	var ids=[]
	for i in vertices.size():
		if vertices[i].y<.006:ids.append(i)
	var report={"sampleCount":121,"soleVertexCount":ids.size(),"minimumHeightM":INF,"maximumFrameSoleMinimumM":-INF,"maximumPointMotionM":0.0,"loopErrorM":0.0,"failures":[]}
	if ids.is_empty():report.failures.append("No sole witnesses");return report
	player.play("Idle");player.advance(0);var first=[];var last=[]
	for phase in 121:
		player.seek(player.get_animation("Idle").length*phase/120.0,true);skeleton.force_update_all_bone_transforms()
		var minimum=INF;last=[]
		for j in ids.size():
			var point=skin_point(ctx,ids[j],skeleton);last.append(point)
			if not point.is_finite():report.failures.append("Non-finite sole");continue
			minimum=min(minimum,point.y)
			if phase==0:first.append(point)
			else:report.maximumPointMotionM=max(report.maximumPointMotionM,point.distance_to(first[j]))
		report.minimumHeightM=min(report.minimumHeightM,minimum);report.maximumFrameSoleMinimumM=max(report.maximumFrameSoleMinimumM,minimum)
	for i in first.size():report.loopErrorM=max(report.loopErrorM,first[i].distance_to(last[i]))
	if report.minimumHeightM<-.002 or report.maximumFrameSoleMinimumM>.003:report.failures.append("Idle sole floor gate")
	if report.maximumPointMotionM>.005 or report.loopErrorM>.001:report.failures.append("Idle sole motion/loop gate")
	return report
