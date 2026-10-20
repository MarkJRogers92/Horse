extends SceneTree
var failures: Array[String] = []
var checks := 0
var output := ""
func check(ok: bool, message: String) -> void:
    checks += 1
    if not ok:
        failures.append(message)
        push_error("[context-art] " + message)
func _initialize() -> void:
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--out="): output = arg.trim_prefix("--out=")
    call_deferred("run")
func settle() -> void:
    for i in range(8): await process_frame
func run() -> void:
    root.size = Vector2i(1280,720)
    check(change_scene_to_file("res://scenes/stable_day.tscn") == OK, "stable loads")
    await settle()
    # Hide only the initial name prompt in this isolated test slot; do not mutate a save.
    for label in current_scene.find_children("*", "Label", true, false):
        if label.text == "Name your stable":
            var parent: Node = label
            while parent.get_parent() != null and not parent.get_parent().get_parent() is CanvasLayer:
                parent = parent.get_parent()
            if parent is Control: parent.hide()
    var art := current_scene.find_children("Illustration", "TextureRect", true, false)
    check(art.size() == 8, "all eight contextual placements exist")
    for item in art:
        check(item.texture != null, "illustration texture loads")
        check(item.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED, "full image retained")
        check(item.mouse_filter == Control.MOUSE_FILTER_IGNORE, "illustration cannot intercept gameplay input")
    # Exercise real filled dialogs without committing an action.
    for method in ["ShowShop", "ShowPasture", "ShowReview"]:
        check(current_scene.has_method(method), method + " exists")
        if not current_scene.has_method(method): continue
        current_scene.call(method)
        await settle()
        for item in art:
            if item.is_visible_in_tree():
                check(item.get_global_rect().position.x >= 0 and item.get_global_rect().end.x <= root.size.x, "visible artwork fits dialog")
        if not output.is_empty() and DisplayServer.get_name() != "headless":
            DirAccess.make_dir_recursive_absolute(output)
            await RenderingServer.frame_post_draw
            check(root.get_texture().get_image().save_png(output.path_join(method + ".png")) == OK, "capture " + method)
        # Close displayed overlays between shots, including initial name prompt.
        for item in art:
            var ancestor: Node = item
            while ancestor.get_parent() != null and not ancestor.get_parent().get_parent() is CanvasLayer:
                ancestor = ancestor.get_parent()
            if ancestor is Control: ancestor.hide()
    var audio := root.get_node_or_null("GameAudio")
    if audio != null: audio.queue_free()
    unload_current_scene()
    await create_timer(0.2).timeout
    load("res://scripts/Stable/StableYard.cs").ReleaseCaches()
    load("res://scripts/Visual/HorsePresenter.cs").ReleaseCaches()
    if failures.is_empty(): print("[context-art] OK ", checks, " checks")
    quit(0 if failures.is_empty() else 1)
