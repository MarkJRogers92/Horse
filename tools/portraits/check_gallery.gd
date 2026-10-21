extends SceneTree

var failures: Array[String] = []
var checks := 0
var output := ""

func _initialize() -> void:
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--out="):
            output = arg.trim_prefix("--out=")
    call_deferred("run")

func check(condition: bool, message: String) -> void:
    checks += 1
    if not condition:
        failures.append(message)
        push_error("[portrait-gallery] " + message)

func settle() -> void:
    for i in range(5):
        await process_frame

func capture(name: String) -> void:
    if output.is_empty() or DisplayServer.get_name() == "headless":
        return
    DirAccess.make_dir_recursive_absolute(output)
    await RenderingServer.frame_post_draw
    check(root.get_texture().get_image().save_png(output.path_join(name + ".png")) == OK, "save " + name)

func run() -> void:
    root.size = Vector2i(1280, 720)
    check(change_scene_to_file("res://scenes/concept_portraits.tscn") == OK, "gallery scene loads")
    await settle()
    var gallery := current_scene
    var backdrop := gallery.find_child("StableBackdrop", true, false) as TextureRect
    check(backdrop != null and backdrop.texture is AtlasTexture, "background uses engine crop")
    if backdrop != null and backdrop.texture is AtlasTexture:
        check(backdrop.texture.region == Rect2(0, 0, 1696, 730), "fake menu is excluded from displayed region")
        check(backdrop.texture.atlas.get_size() == Vector2(1696, 960), "original background remains intact")
    var cards := gallery.find_child("PortraitCards", true, false) as GridContainer
    check(cards != null, "gallery creates its card grid")
    if cards == null:
        finish()
        return
    check(cards.get_child_count() == 6, "gallery page is bounded at six illustrations")
    check(cards.columns == 3, "wide layout has three columns")
    var labels := gallery.find_children("*", "Label", true, false)
    check(labels.any(func(label): return "do not depict your saved horses" in label.text), "illustration disclosure is visible")
    check(labels.any(func(label): return "rights not yet verified" in label.text), "license status is visible")
    for card in cards.get_children():
        var portrait := card.find_child("Portrait", true, false) as TextureRect
        check(portrait != null and portrait.texture != null, "card texture exists")
        if portrait != null and portrait.texture != null:
            var base: Texture2D = portrait.texture.atlas if portrait.texture is AtlasTexture else portrait.texture
            check(base.get_size() == Vector2(768, 768), "runtime texture respects import budget")
            check(portrait.material is ShaderMaterial, "art corners are rounded like the cards")
            check(portrait.stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED, "art is displayed without crop or distortion")
            check(portrait.get_global_rect().end.x <= root.size.x, "card fits wide viewport")
    # The first Breeds card (Arabian) has a baked black frame that must be trimmed, not shown.
    var first := cards.get_child(0).find_child("Portrait", true, false) as TextureRect
    check(first.texture is AtlasTexture, "baked black frame is trimmed")
    var back_at_start := gallery.find_child("BackToMenu", true, false) as Button
    check(back_at_start != null and back_at_start.has_focus(), "keyboard focus starts on Back")
    var prev := gallery.find_child("PreviousPage", true, false) as Button
    var pager_next := gallery.find_child("NextPage", true, false) as Button
    check(pager_next.get_global_rect().position.x - prev.get_global_rect().end.x < 120, "pager controls stay together")
    await capture("gallery-wide")
    var next := gallery.find_child("NextPage", true, false) as Button
    next.pressed.emit()
    await settle()
    check(cards.get_child_count() == 6, "next page remains bounded")
    var collection := gallery.find_child("ArtCollection", true, false) as OptionButton
    collection.select(2)
    collection.item_selected.emit(2)
    await settle()
    check(cards.get_child_count() == 6, "stable life collection loads")
    # Reaching the last page must not strand keyboard focus on the now-disabled Next button.
    next.grab_focus()
    while not next.disabled:
        next.pressed.emit()
        await settle()
    check(not next.has_focus() and gallery.get_viewport().gui_get_focus_owner() != null, "focus leaves disabled Next")
    await capture("gallery-stable-life")
    # Simulate compact canvas space, with the same scrollable gallery instance.
    gallery.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
    gallery.size = Vector2(700, 600)
    await settle()
    check(cards.columns == 1, "compact layout stacks the cards")
    var scroll := gallery.find_child("PortraitScroll", true, false) as ScrollContainer
    check(scroll.get_v_scroll_bar().max_value > scroll.size.y, "compact gallery is vertically scrollable")
    for card in cards.get_children():
        check(card.get_global_rect().end.x <= 700, "compact card stays inside canvas")
    await capture("gallery-compact")
    var back := gallery.find_child("BackToMenu", true, false) as Button
    check(back != null, "Back button exists")
    back.pressed.emit()
    back.pressed.emit()
    await settle()
    check(current_scene.scene_file_path == "res://scenes/main.tscn", "repeated Back returns safely to menu")
    var gallery_buttons := current_scene.find_children("*", "Button", true, false).filter(func(button): return button.text == "Concept portraits")
    check(gallery_buttons.size() == 1, "main menu exposes one gallery entry")
    if gallery_buttons.size() == 1:
        gallery_buttons[0].pressed.emit()
        await settle()
        check(current_scene.scene_file_path == "res://scenes/concept_portraits.tscn", "gallery reopens from menu")
        var escape := InputEventAction.new()
        escape.action = "ui_cancel"
        escape.pressed = true
        Input.parse_input_event(escape)
        await settle()
        check(current_scene.scene_file_path == "res://scenes/main.tscn", "Escape returns to menu")
    # Tear down audio before quitting, just as the existing stable-life probe does.
    var audio := root.get_node_or_null("GameAudio")
    if audio != null:
        audio.queue_free()
        # Headless process frames can finish before the audio mixer releases playback.
        await create_timer(0.2).timeout
    # The rendered probe ends in the 3D menu. Release it while the renderer is
    # still pumping frames. These assertions verify explicit test teardown;
    # they do not claim to fix the separate Mesa Vulkan shutdown limitation.
    var final_menu: WeakRef = weakref(current_scene)
    unload_current_scene()
    await settle()
    check(current_scene == null, "final menu scene released before shutdown")
    check(final_menu.get_ref() == null, "final menu nodes freed before cache release")
    load("res://scripts/Stable/StableYard.cs").ReleaseCaches()
    load("res://scripts/Visual/HorsePresenter.cs").ReleaseCaches()
    await settle()
    finish()

func finish() -> void:
    if failures.is_empty():
        print("[portrait-gallery] OK %d checks" % checks)
    else:
        print("[portrait-gallery] FAILED %d of %d checks" % [failures.size(), checks])
    quit(0 if failures.is_empty() else 1)
