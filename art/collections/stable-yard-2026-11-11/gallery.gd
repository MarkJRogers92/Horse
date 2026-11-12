extends Node3D

var assets: Array = []
var selected: int = 0
var model: Node3D
var pivot := Node3D.new()
var camera := Camera3D.new()
var caption := Label.new()
var distance: float = 3.0
var target := Vector3.ZERO

func _ready() -> void:
	assets = JSON.parse_string(FileAccess.get_file_as_string("res://catalog.json"))
	add_child(pivot)
	add_child(camera)
	camera.current = true
	var environment := WorldEnvironment.new()
	environment.environment = Environment.new()
	environment.environment.background_mode = Environment.BG_COLOR
	environment.environment.background_color = Color(0.16, 0.20, 0.18)
	environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.environment.ambient_light_color = Color(0.85, 0.88, 0.83)
	environment.environment.ambient_light_energy = 0.7
	add_child(environment)
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-45, -35, 0)
	light.light_energy = 1.3
	add_child(light)
	var overlay := CanvasLayer.new()
	add_child(overlay)
	caption.position = Vector2(24, 22)
	caption.add_theme_font_size_override("font_size", 22)
	overlay.add_child(caption)
	var previous := Button.new()
	previous.text = "Previous"
	previous.position = Vector2(24, 120)
	previous.pressed.connect(func(): show_asset(selected - 1))
	overlay.add_child(previous)
	var next := Button.new()
	next.text = "Next"
	next.position = Vector2(135, 120)
	next.pressed.connect(func(): show_asset(selected + 1))
	overlay.add_child(next)
	show_asset(0)

func show_asset(index: int) -> void:
	selected = posmod(index, assets.size())
	if is_instance_valid(model):
		pivot.remove_child(model)
		model.queue_free()
	var a: Dictionary = assets[selected]
	var packed := load("res://models/" + str(a.id) + ".glb") as PackedScene
	model = packed.instantiate()
	pivot.add_child(model)
	pivot.rotation.y = 0.35
	var dims: Array = a.dimensions_godot_xyz_m
	distance = maxf(maxf(float(dims[0]), float(dims[1])), float(dims[2])) * 2.2
	target = Vector3(0, float(dims[1]) * 0.5, 0)
	caption.text = "%02d / %02d  %s\n%s triangles | %.2f m tall\nArrows: browse   Drag: rotate   Wheel: zoom" % [selected+1, assets.size(), a.name, a.triangles, a.height_m]
	update_camera()

func update_camera() -> void:
	camera.position = target + Vector3(distance * 0.45, distance * 0.32, distance)
	camera.look_at(target)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_RIGHT: show_asset(selected + 1)
		if event.keycode == KEY_LEFT: show_asset(selected - 1)
	if event is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		pivot.rotation.y += event.relative.x * 0.01
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP: distance *= 0.9
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN: distance *= 1.1
		update_camera()
