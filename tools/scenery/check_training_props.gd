extends SceneTree
# Fail explicitly (including in non-debug builds), and exercise the real yard.
var failures: Array[String] = []
func check(ok: bool, message: String):
 if not ok:
  failures.append(message)
  push_error(message)
func _initialize():
 call_deferred('run')
func run():
 var script = load('res://scripts/Stable/StableYard.cs')
 var yard = script.new()
 root.add_child(yard)
 await process_frame
 yard.set_process(false)
 var expected = {'training_jump': 1, 'ground_pole': 2, 'low_cavaletti': 1, 'course_cone': 3, 'training_barrel': 1}
 var boxes: Array[AABB] = []
 for asset in expected:
  var holders = get_nodes_in_group('scenery_' + asset)
  check(holders.size() == expected[asset], asset + ' instance count')
  for holder in holders:
   check(holder.scale.is_equal_approx(Vector3.ONE), asset + ' holder scale')
   var meshes = holder.find_children('*', 'MeshInstance3D', true, false)
   check(meshes.size() > 0, asset + ' real imported geometry')
   var bounds = AABB()
   var first = true
   var prop_boxes: Array[AABB] = []
   for mesh in meshes:
    var mesh_bounds = AABB()
    var mesh_first = true
    # Rotated component AABBs overestimate the jump's feet: use real vertices.
    for surface in mesh.mesh.get_surface_count():
     var vertices = mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]
     for vertex in vertices:
      var point = mesh.global_transform * vertex
      bounds = AABB(point, Vector3.ZERO) if first else bounds.expand(point)
      first = false
      mesh_bounds = AABB(point, Vector3.ZERO) if mesh_first else mesh_bounds.expand(point)
      mesh_first = false
    prop_boxes.append(mesh_bounds)
   check(absf(bounds.position.y - 0.0) < 0.025, asset + ' rests on flat grass, no floating or sinking')
   var nearest_x = clampf(0, bounds.position.x, bounds.end.x)
   var nearest_z = clampf(-0.5, bounds.position.z, bounds.end.z)
   check(pow(nearest_x / 7.4, 2) + pow((nearest_z + 0.5) / 5.4, 2) > 1.02, asset + ' outside paddock fence and horse roaming')
   for spot in [Vector3(6.6, 0, -10), Vector3(9.6, 0, -4.2), Vector3(2, 0, -9)]:
    var nearest = Vector3(clampf(spot.x, bounds.position.x, bounds.end.x), 0, clampf(spot.z, bounds.position.z, bounds.end.z))
    check(nearest.distance_to(spot) > 1.2, asset + ' clears purchased decoration spots')
   check(bounds.end.z < -4.0, asset + ' clear of front paddock opening')
   # Decorative scenery matches the yard: no physics/nav obstacles or misleading interaction areas.
   check(holder.find_children('*', 'CollisionObject3D', true, false).is_empty(), asset + ' adds no hidden collision')
   check(holder.find_children('*', 'NavigationRegion3D', true, false).is_empty(), asset + ' adds no navigation mesh')
   for box in prop_boxes:
    for old in boxes:
     check(not old.intersects(box), asset + ' does not overlap another training prop')
   boxes.append_array(prop_boxes)
   if asset == 'training_jump': check(absf(bounds.size.y - 1.63) < 0.01, 'Jump retains authored metre scale')
   if asset == 'ground_pole': check(absf(bounds.size.y - 0.11) < 0.01, 'Pole retains authored metre scale')
   if asset == 'low_cavaletti': check(absf(bounds.size.y - 0.444) < 0.01, 'Cavaletti retains authored metre scale')
   if asset == 'course_cone': check(absf(bounds.size.y - 0.5) < 0.01, 'Cone retains authored metre scale')
   if asset == 'training_barrel': check(absf(bounds.size.y - 0.9) < 0.01, 'Barrel retains authored metre scale')
 for asset in ['fair_tent', 'hay_props', 'wheelbarrow', 'stable_block', 'water_trough']:
  for holder in get_nodes_in_group('scenery_' + asset):
   for mesh in holder.find_children('*', 'MeshInstance3D', true, false):
    var bounds = AABB()
    var first = true
    for surface in mesh.mesh.get_surface_count():
     for vertex in mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:
      var point = mesh.global_transform * vertex
      bounds = AABB(point, Vector3.ZERO) if first else bounds.expand(point)
      first = false
    for equipment in boxes:
     check(not equipment.intersects(bounds), 'Equipment clears existing ' + asset)
 check(get_nodes_in_group('scenery_show_jump').is_empty(), 'Old show jump replaced rather than overlapped')
 check(get_nodes_in_group('scenery_feed_trough').size() == 1, 'Porch trough retained')
 check(get_nodes_in_group('scenery_stable_bucket').size() == 1, 'Porch bucket retained')
 var output = ''
 for arg in OS.get_cmdline_user_args():
  if arg.begins_with('--out='): output = arg.trim_prefix('--out=')
 if output != '' and failures.is_empty():
  DirAccess.make_dir_recursive_absolute(output)
  root.size = Vector2i(1280,720)
  await capture(output.path_join('training-yard-context.png'))
  var camera = Camera3D.new()
  yard.add_child(camera)
  camera.position = Vector3(11.5, 4.6, 0.5)
  camera.look_at(Vector3(6.8, 0.55, -6.9))
  camera.fov = 47
  camera.current = true
  await capture(output.path_join('training-closeup.png'))
 yard.queue_free()
 await process_frame
 script.ReleaseCaches()
 if failures.is_empty(): print('TRAINING_PROPS_OK')
 quit(0 if failures.is_empty() else 1)
func capture(path: String):
 for i in 30: await process_frame
 await RenderingServer.frame_post_draw
 check(root.get_texture().get_image().save_png(path) == OK, 'Save capture ' + path)
