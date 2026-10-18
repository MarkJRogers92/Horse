extends SceneTree
func _initialize():
 call_deferred("run")
func run():
 var yard_script = load("res://scripts/Stable/StableYard.cs")
 var yard = yard_script.new()
 root.add_child(yard)
 await process_frame
 yard.set_process(false)
 var camera = Camera3D.new()
 root.add_child(camera)
 camera.position = Vector3(0.2, 2.3, -8.8)
 camera.look_at(Vector3(-1.9, 0.60, -12.8))
 camera.current = true
 camera.fov = 42
 root.size = Vector2i(1280, 720)
 for i in 20:
  await process_frame
 await RenderingServer.frame_post_draw
 var result = root.get_texture().get_image().save_png("user://props-closeup.png")
 assert(result == OK)
 print("PROP_CAPTURE_SAVED:" + ProjectSettings.globalize_path("user://props-closeup.png"))
 yard.queue_free()
 camera.queue_free()
 await process_frame
 yard_script.ReleaseCaches()
 quit()
