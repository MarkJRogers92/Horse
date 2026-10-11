extends SceneTree

const WIDTH := 640
const HEIGHT := 480
const QUAD_RES := "res://eight_weight_probe.gltf"

func _initialize():
    call_deferred("run")

func run():
    var args := OS.get_cmdline_user_args()
    if args.size() < 2:
        printerr("usage: -- <mode> <output.png>")
        quit(2)
        return
    var mode: String = args[0]
    var out_path: String = args[1]
    DisplayServer.window_set_size(Vector2i(WIDTH, HEIGHT))

    var world := Node3D.new()
    world.name = "World"
    root.add_child(world)
    var viewport := get_root()
    viewport.transparent_bg = false
    var camera := Camera3D.new()
    camera.name = "Camera"
    camera.projection = Camera3D.PROJECTION_ORTHOGONAL
    camera.size = 2.6
    camera.position = Vector3(0, 0, 5)
    camera.current = true
    world.add_child(camera)

    var env := WorldEnvironment.new()
    var environment := Environment.new()
    environment.background_mode = Environment.BG_COLOR
    environment.background_color = Color(0.03, 0.035, 0.045, 1)
    env.environment = environment
    world.add_child(env)

    var packed := load(QUAD_RES) as PackedScene
    if packed == null:
        printerr("could not load imported glTF scene")
        quit(3)
        return
    var imported := packed.instantiate()
    world.add_child(imported)
    var skeleton := _find_skeleton(imported)
    var mesh_instance := _find_mesh(imported)
    if skeleton == null or mesh_instance == null or mesh_instance.mesh == null:
        printerr("missing imported skeleton or mesh")
        quit(4)
        return
    if skeleton.get_bone_count() != 8:
        printerr("expected 8 bones, got ", skeleton.get_bone_count())
        quit(5)
        return

    var arrays: Array = mesh_instance.mesh.surface_get_arrays(0)
    var bones: PackedInt32Array = arrays[Mesh.ARRAY_BONES]
    var weights: PackedFloat32Array = arrays[Mesh.ARRAY_WEIGHTS]
    var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
    if bones.size() != 32 or weights.size() != 32 or vertices.size() != 4:
        printerr("bad imported arrays bones=",bones.size()," weights=",weights.size()," vertices=",vertices.size())
        quit(6)
        return
    for i in range(vertices.size()):
        var active := 0
        var total := 0.0
        for j in range(8):
            var bone_index := bones[i * 8 + j]
            var w := weights[i * 8 + j]
            if bone_index != j or absf(w - 0.125) > 0.00002:
                printerr("unexpected imported slot vertex=",i," slot=",j," bone=",bone_index," weight=",w)
                quit(7)
                return
            if w > 0.0001: active += 1
            total += w
        if active != 8 or absf(total - 1.0) > 0.001:
            printerr("invalid vertex weights vertex=",i," active=",active," sum=",total)
            quit(7)
            return

    if mode == "actual_pose":
        for i in range(4, 8):
            skeleton.set_bone_pose_position(i, Vector3(0.8, 0, 0))
    elif mode == "reference_expected":
        _replace_with_unskinned_reference(mesh_instance, 0.4)
    elif mode == "reference_first4":
        _replace_with_unskinned_reference(mesh_instance, 0.0)
    elif mode != "actual_rest":
        printerr("unknown mode: ", mode)
        quit(8)
        return

    var mat := StandardMaterial3D.new()
    mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
    mat.albedo_color = Color(0.05, 0.8, 0.2, 1)
    mat.cull_mode = BaseMaterial3D.CULL_DISABLED
    mesh_instance.material_override = mat
    # Ensure a fresh frame has been rendered before reading the framebuffer.
    for i in range(8): await process_frame
    var image := viewport.get_texture().get_image()
    if image == null or image.is_empty():
        printerr("framebuffer image unavailable")
        quit(9)
        return
    var err := image.save_png(out_path)
    if err != OK:
        printerr("save png failed: ", err)
        quit(10)
        return
    print("mode=",mode," bones=",skeleton.get_bone_count()," vertices=",vertices.size()," eight_weights_per_vertex=true"," weight_sum_first=",_sum8(weights,0)," image=",out_path," dimensions=",image.get_width(),"x",image.get_height())
    quit(0)

func _find_skeleton(n: Node) -> Skeleton3D:
    if n is Skeleton3D: return n as Skeleton3D
    for c in n.get_children():
        var found := _find_skeleton(c)
        if found != null: return found
    return null

func _find_mesh(n: Node) -> MeshInstance3D:
    if n is MeshInstance3D: return n as MeshInstance3D
    for c in n.get_children():
        var found := _find_mesh(c)
        if found != null: return found
    return null

func _replace_with_unskinned_reference(mi: MeshInstance3D, translate_x: float) -> void:
    var source: ArrayMesh = mi.mesh as ArrayMesh
    var source_arrays: Array = source.surface_get_arrays(0)
    var vertices: PackedVector3Array = source_arrays[Mesh.ARRAY_VERTEX].duplicate()
    for i in range(vertices.size()): vertices[i].x += translate_x
    var arrays: Array = []
    arrays.resize(Mesh.ARRAY_MAX)
    arrays[Mesh.ARRAY_VERTEX] = vertices
    arrays[Mesh.ARRAY_NORMAL] = source_arrays[Mesh.ARRAY_NORMAL].duplicate()
    arrays[Mesh.ARRAY_INDEX] = source_arrays[Mesh.ARRAY_INDEX].duplicate()
    var ref := ArrayMesh.new()
    ref.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
    if ref.get_surface_count() != 1:
        printerr("unskinned reference surface creation failed")
        quit(11)
        return
    mi.mesh = ref
    mi.skeleton = NodePath("")

func _sum8(w: PackedFloat32Array, vertex: int) -> float:
    var s := 0.0
    for i in range(8): s += w[vertex * 8 + i]
    return s
