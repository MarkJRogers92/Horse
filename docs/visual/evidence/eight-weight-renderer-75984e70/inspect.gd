extends SceneTree
func _initialize():
    call_deferred("run")
func run():
    var packed = load("res://eight_weight_probe.gltf")
    if packed == null:
        printerr("LOAD_FAILED")
        quit(2)
        return
    var inst = packed.instantiate()
    print("ROOT ", inst.get_class(), " ", inst.name)
    _walk(inst, "")
    inst.free()
    await process_frame
    quit(0)
func _walk(n: Node, prefix: String):
    print(prefix, n.get_class(), " name=", n.name)
    if n is Skeleton3D:
        var sk = n as Skeleton3D
        print("SKELETON bones=",sk.get_bone_count())
        for i in range(sk.get_bone_count()): print("BONE ",i," ",sk.get_bone_name(i)," parent=",sk.get_bone_parent(i))
    if n is MeshInstance3D:
        var mi=n as MeshInstance3D
        print("MESH ",mi.mesh," meshclass=",mi.mesh.get_class() if mi.mesh else "null")
        if mi.mesh:
            for s in range(mi.mesh.get_surface_count()):
                var a=mi.mesh.surface_get_arrays(s)
                print("SURFACE ",s," vertices=",a[Mesh.ARRAY_VERTEX].size()," bones=",a[Mesh.ARRAY_BONES]," weights=",a[Mesh.ARRAY_WEIGHTS])
    for c in n.get_children(): _walk(c,prefix+"  ")
