extends SceneTree
func _initialize():
 call_deferred('run')
func run():
 var script = load('res://scripts/Stable/StableYard.cs')
 var yard = script.new()
 root.add_child(yard)
 await process_frame
 var holders = get_nodes_in_group('scenery_feed_trough')
 assert(holders.size() == 1)
 assert(holders[0].find_children('*','MeshInstance3D',true,false).size() > 0)
 assert(is_equal_approx(holders[0].position.y,0.31))
 assert(get_nodes_in_group('scenery_water_trough').size() == 1)
 var buckets = get_nodes_in_group('scenery_stable_bucket')
 assert(buckets.size() == 1)
 assert(buckets[0].find_children('*','MeshInstance3D',true,false).size() > 0)
 assert(is_equal_approx(buckets[0].position.y,0.31))
 assert(buckets[0].position.distance_to(holders[0].position)>1.0)
 print('FEED_TROUGH_AND_BUCKET_YARD_OK')
 yard.queue_free()
 await process_frame
 script.ReleaseCaches()
 quit()
