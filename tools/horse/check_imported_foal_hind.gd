extends SceneTree
func descendants(node: Node, cls: String, result: Array) -> void:
 if node.is_class(cls): result.append(node)
 for child in node.get_children(): descendants(child,cls,result)
func _initialize() -> void: call_deferred("check")
func check() -> void:
 var args=OS.get_cmdline_user_args()
 if args.size()!=2:
  push_error("Supply imported foal resource and output JSON path");quit(1);return
 var scene=load(args[0])
 if scene==null:
  push_error("Foal scene missing");quit(1);return
 var model=scene.instantiate();root.add_child(model)
 var skeletons=[];var players=[];var meshes=[]
 descendants(model,"Skeleton3D",skeletons);descendants(model,"AnimationPlayer",players);descendants(model,"MeshInstance3D",meshes)
 if skeletons.size()!=1 or players.size()!=1:
  push_error("Expected one skeleton and animation player");quit(1);return
 var sk:Skeleton3D=skeletons[0];var player:AnimationPlayer=players[0];var body:MeshInstance3D
 for mesh in meshes:
  if mesh.name=="Body":body=mesh
 if body==null or body.skin==null or not player.has_animation("Walk"):
  push_error("Body skin or Walk animation missing");quit(1);return
 var binds={}
 for i in body.skin.get_bind_count():binds[str(body.skin.get_bind_name(i))]=body.skin.get_bind_pose(i)
 for side in ["L","R"]:
  for suffix in ["Upper","Gaskin","Hock","Hoof_02"]:
   var name="Hind_"+side+"_"+suffix
   if sk.find_bone(name)<0 or not binds.has(name):
    push_error("Missing hind bone/bind "+name);quit(1);return
 player.play("Walk");player.advance(0);var duration=player.get_animation("Walk").length
 var report={"engine":Engine.get_version_info(),"usesProjectImport":true,"sourceGlbSha256":FileAccess.get_sha256(args[0]),"sampleCount":581,"method":"Actual imported skin matrices applied independently to hip/stifle/hock/fetlock rest anchors. Stifle must stay forward of hip-hock chord; hock caudal to stifle-fetlock chord. These are trial articulation checks, not medical angle norms.","limbs":{},"failures":[]}
 for side in ["L","R"]:
  var sign_y=1.0 if side=="L" else -1.0;var pre="Hind_"+side+"_"
  var A=Vector3(-.43,.82,-sign_y*.09);var S=Vector3(-.38,.68,-sign_y*.09);var H=Vector3(-.575,.52,-sign_y*.09);var C=Vector3(-.65,.15,-sign_y*.11)
  var metric={"maximumJointCoincidenceErrorM":0.0,"minimumForwardStifleOffsetM":INF,"maximumForwardHockOffsetM":-INF,"stifleAngleMinDegrees":INF,"stifleAngleMaxDegrees":-INF,"hockAngleMinDegrees":INF,"hockAngleMaxDegrees":-INF,"relativeUpperGaskinRotationMinDegrees":INF,"relativeUpperGaskinRotationMaxDegrees":-INF}
  for i in 581:
   player.seek(duration*i/580.0,true);sk.force_update_all_bone_transforms()
   var U=sk.get_bone_global_pose(sk.find_bone(pre+"Upper"))*binds[pre+"Upper"]
   var G=sk.get_bone_global_pose(sk.find_bone(pre+"Gaskin"))*binds[pre+"Gaskin"]
   var L=sk.get_bone_global_pose(sk.find_bone(pre+"Hock"))*binds[pre+"Hock"]
   var F=sk.get_bone_global_pose(sk.find_bone(pre+"Hoof_02"))*binds[pre+"Hoof_02"]
   var ap:Vector3=U*A;var sp:Vector3=G*S;var hp:Vector3=L*H;var cp:Vector3=F*C
   metric.maximumJointCoincidenceErrorM=max(metric.maximumJointCoincidenceErrorM,max((U*S-sp).length(),max((G*H-hp).length(),(L*C-cp).length())))
   var chord:Vector3=hp-ap;var forward=Vector3(-chord.y,chord.x,0).normalized();metric.minimumForwardStifleOffsetM=min(metric.minimumForwardStifleOffsetM,(sp-ap).dot(forward))
   chord=cp-sp;forward=Vector3(-chord.y,chord.x,0).normalized();metric.maximumForwardHockOffsetM=max(metric.maximumForwardHockOffsetM,(hp-sp).dot(forward))
   var stifle=rad_to_deg((ap-sp).angle_to(hp-sp));var hock=rad_to_deg((sp-hp).angle_to(cp-hp));var relative=rad_to_deg(U.basis.get_rotation_quaternion().angle_to(G.basis.get_rotation_quaternion()))
   metric.stifleAngleMinDegrees=min(metric.stifleAngleMinDegrees,stifle);metric.stifleAngleMaxDegrees=max(metric.stifleAngleMaxDegrees,stifle);metric.hockAngleMinDegrees=min(metric.hockAngleMinDegrees,hock);metric.hockAngleMaxDegrees=max(metric.hockAngleMaxDegrees,hock);metric.relativeUpperGaskinRotationMinDegrees=min(metric.relativeUpperGaskinRotationMinDegrees,relative);metric.relativeUpperGaskinRotationMaxDegrees=max(metric.relativeUpperGaskinRotationMaxDegrees,relative)
  for value in metric.values():
   if not is_finite(value):report.failures.append(side+": non-finite joint metric")
  if metric.maximumJointCoincidenceErrorM>.001:report.failures.append(side+": separated joint anchors")
  if metric.minimumForwardStifleOffsetM<=0:report.failures.append(side+": backward stifle")
  if metric.maximumForwardHockOffsetM>=0:report.failures.append(side+": forward hock")
  if metric.relativeUpperGaskinRotationMaxDegrees-metric.relativeUpperGaskinRotationMinDegrees<5:report.failures.append(side+": insufficient independent stifle articulation")
  report.limbs[side]=metric
 var out=FileAccess.open(args[1],FileAccess.WRITE);out.store_string(JSON.stringify(report,"  "));print(JSON.stringify(report));quit(0 if report.failures.is_empty() else 1)
