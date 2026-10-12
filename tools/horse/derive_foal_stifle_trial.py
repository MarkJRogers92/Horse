"""Isolated hind-only articulation experiment. No source or production writes."""
import bpy,json,math,sys,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
import numpy as np
R=Path(__file__).resolve().parents[2];sys.path.insert(0,str(R/'tools/horse'))
from derive_stock_walk import fingerprint,NAMES
source=R/'art/derived/production/candidates/foal-walk-refined/horse_foal_walk.blend';out=R/'art/derived/production/candidates/foal-walk-stifle';out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False);s=bpy.context.scene;r=bpy.data.objects['Skeleton'];body=bpy.data.objects['Body'];before={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
oldnames=[b.name for b in r.data.bones];rest={b.name:b.matrix_local.copy() for b in r.data.bones};frames=np.linspace(0,58,117);cache=[]
for frame in frames:
 s.frame_set(int(frame),subframe=float(frame)%1);cache.append({b.name:b.matrix.copy() for b in r.pose.bones})
# The front animation, root/body motion and all original hoof transforms are cached untouched.
bpy.context.view_layer.objects.active=r;r.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for side in ['L','R']:
 sign=1 if side=='L' else -1;b=r.data.edit_bones.new('Trial_Stifle_'+side);b.head=(-.38,sign*.09,.68);b.tail=(-.575,sign*.09,.52);b.parent=r.data.edit_bones['Root'];b.use_deform=True
bpy.ops.object.mode_set(mode='OBJECT');rest={b.name:b.matrix_local.copy() for b in r.data.bones}
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def segment(a,b,ap,bp):return Matrix.Translation(ap)@(b-a).rotation_difference(bp-ap).to_matrix().to_4x4()@Matrix.Translation(-a)
# Split only the existing hind-upper skin mass. Front/root/hoof weights stay exact.
weight_before=[{body.vertex_groups[g.group].name:g.weight for g in v.groups} for v in body.data.vertices]
for side in ['L','R']:body.vertex_groups.new(name='Trial_Stifle_'+side)
for v in body.data.vertices:
 for side in ['L','R']:
  name='Hind_'+side+'_Upper';w=weight_before[v.index].get(name,0)
  if w<=0:continue
  u=smooth(.56,.80,v.co.z);body.vertex_groups[name].add([v.index],w*u*u,'REPLACE');body.vertex_groups['Hind_'+side+'_Gaskin'].add([v.index],w*(1-u)*(1-u),'REPLACE');body.vertex_groups['Trial_Stifle_'+side].add([v.index],w*2*u*(1-u),'REPLACE')
# Keep the authored source within Godot's eight-influence representation. Only
# tiny new hind blend tails are consolidated; front/root/hoof weights stay intact.
consolidated_count=0;maximum_consolidated_weight=0.
for v in body.data.vertices:
 positive=[(body.vertex_groups[g.group].name,g.weight) for g in v.groups if g.weight>1e-9]
 if len(positive)<=8:continue
 hind=[(n,w) for n,w in positive if n.startswith(('Hind_','Walk_Knee_hind_','Trial_Stifle_')) and 'Hoof' not in n]
 remove=sorted(hind,key=lambda item:item[1])[:len(positive)-8];amount=sum(w for n,w in remove)
 assert amount<.002,(v.index,amount)
 removed={n for n,w in remove};target=max(((n,w) for n,w in hind if n not in removed),key=lambda item:item[1])
 for n,w in remove:body.vertex_groups[n].remove([v.index])
 body.vertex_groups[target[0]].add([v.index],target[1]+amount,'REPLACE');consolidated_count+=1;maximum_consolidated_weight=max(maximum_consolidated_weight,amount)
# Exact front-group preservation and frontmost region preservation.
frontmax=0
for v,old in zip(body.data.vertices,weight_before):
 new={body.vertex_groups[g.group].name:g.weight for g in v.groups}
 for n,w in old.items():
  if n.startswith('Front_') or n.startswith('Walk_Elbow_fore') or n.startswith('Walk_Cannon_fore') or n.startswith('Walk_Fetlock_fore') or n.startswith('Walk_Knee_fore'):assert new[n]==w
r.animation_data.action.name='PreservedOriginal';act=r.animation_data.action.copy();act.name='Walk';r.animation_data.action=act
changed={n for n in oldnames if n.startswith(('Hind_L_','Hind_R_','Walk_Knee_hind_')) or n in ['FoalSmart_Bone_014','FoalSmart_Bone_017']}|{'Trial_Stifle_L','Trial_Stifle_R'}
for layer in act.layers:
 for strip in layer.strips:
  for slot in act.slots:
   bag=strip.channelbag(slot)
   if bag:
    for fc in list(bag.fcurves):
     if any(fc.data_path.startswith('pose.bones["'+n+'"]') for n in changed):bag.fcurves.remove(fc)
trace=[]
for fi,frame in enumerate(frames):
 orig=cache[fi];posed={n:m.copy() for n,m in orig.items()};row={'phase':float(frame)/58,'limbs':{}}
 for side in ['L','R']:
  sign=1 if side=='L' else -1;key='hind_'+side.lower();pre='Hind_'+side
  A=Vector((-.43,sign*.09,.82));S=Vector((-.38,sign*.09,.68));B=Vector((-.575,sign*.09,.52));C=Vector((-.65,sign*.11,.15))
  bodydelta=orig['Pelvis']@rest['Pelvis'].inverted();footdelta=orig[pre+'_Hoof_02']@rest[pre+'_Hoof_02'].inverted();Ap=bodydelta@A;Cp=footdelta@C
  # Keep accepted hoof path exactly. Coordinate cannon lean with stride position,
  # allowing a near-upright lower leg around mid-stance without a backward upper strut.
  dx=.6*(-.57-Cp.x)-.02;dy=(sign*.09-Cp.y)*.4;L3=(C-B).length
  Hp=Cp+Vector((dx,dy,math.sqrt(L3*L3-dx*dx-dy*dy)))
  L1=(S-A).length;L2=(B-S).length;v=Hp-Ap;d=v.length
  assert abs(L1-L2)<d<L1+L2,(side,frame,d,L1+L2)
  e=v.normalized();along=(L1*L1-L2*L2+d*d)/(2*d);height=math.sqrt(L1*L1-along*along);normal=Vector((-e.z,0,e.x)).normalized();Sp=Ap+e*along+normal*height
  upper=segment(A,S,Ap,Sp);gaskin=segment(S,B,Sp,Hp);lower=segment(B,C,Hp,Cp)
  midupper=upper.to_quaternion().slerp(gaskin.to_quaternion(),.5);stifledelta=Matrix.Translation(Sp)@midupper.to_matrix().to_4x4()@Matrix.Translation(-S)
  midlower=gaskin.to_quaternion().slerp(lower.to_quaternion(),.5);hockdelta=Matrix.Translation(Hp)@midlower.to_matrix().to_4x4()@Matrix.Translation(-B)
  for n in oldnames:
   if n.startswith(pre+'_'):
    suffix=n[len(pre)+1:]
    delta=footdelta if suffix in ['Hoof_02','Hoof_03'] else upper if suffix=='Upper' else gaskin if suffix=='Gaskin' else lower
    posed[n]=delta@rest[n]
  aux='FoalSmart_Bone_017' if side=='L' else 'FoalSmart_Bone_014';posed[aux]=lower@rest[aux];posed['Walk_Knee_'+key]=hockdelta@rest['Walk_Knee_'+key];posed['Trial_Stifle_'+side]=stifledelta@rest['Trial_Stifle_'+side]
  row['limbs'][key]={'hip':list(Ap),'stifle':list(Sp),'hock':list(Hp),'fetlock':list(Cp),'hockAngle':math.degrees((Sp-Hp).angle(Cp-Hp)),'stifleAngle':math.degrees((Ap-Sp).angle(Hp-Sp))}
 for pb in r.pose.bones:
  if pb.name not in changed:continue
  b=pb.bone;kw={} if b.parent is None else dict(parent_matrix=posed[b.parent.name],parent_matrix_local=b.parent.matrix_local);pb.rotation_mode='QUATERNION';pb.matrix_basis=b.convert_local_to_pose(posed[b.name],b.matrix_local,invert=True,**kw)
  for prop in ['location','rotation_quaternion','scale']:pb.keyframe_insert(prop,frame=float(frame),group=b.name)
 trace.append(row)
for layer in act.layers:
 for strip in layer.strips:
  for slot in act.slots:
   bag=strip.channelbag(slot)
   if bag:
    for fc in bag.fcurves:
     for k in fc.keyframe_points:k.interpolation='LINEAR'
assert before=={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
s.frame_start=0;s.frame_end=58;s.render.fps=60;s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'horse_foal_walk.blend'),compress=True)
bpy.ops.object.select_all(action='DESELECT')
for o in [r,*[bpy.data.objects[n] for n in NAMES]]:o.select_set(True)
bpy.context.view_layer.objects.active=r
bpy.ops.export_scene.gltf(filepath=str(out/'horse_foal_walk.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIVE_ACTIONS',export_nla_strips_merged_animation_name='Walk',export_force_sampling=True,export_frame_range=True,export_frame_step=1,export_skins=True,export_all_influences=True,export_morph=True,export_morph_normal=True,export_morph_tangent=False,export_materials='EXPORT',export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,export_extras=False,export_yup=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();profile=json.load(open(source.parent/'walk-motion-profile.json'));profile.update(candidateBlendSha256=sha(out/'horse_foal_walk.blend'),sourceGlbSha256=sha(out/'horse_foal_walk.glb'));(out/'walk-motion-profile.json').write_text(json.dumps(profile,indent=2)+'\n')
(out/'construction-report.json').write_text(json.dumps({'sourceBlendSha256':sha(source),'candidateBlendSha256':sha(out/'horse_foal_walk.blend'),'candidateGlbSha256':sha(out/'horse_foal_walk.glb'),'protectedMeshDataExact':True,'frontWeightsExact':True,'eightInfluenceConsolidation':{'vertices':consolidated_count,'maximumWeightMoved':maximum_consolidated_weight},'originalHoofPathsRetainedByConstruction':True,'scope':'Isolated hind-only stifle experiment; not production. Independent audits and visual review pending.','phases':trace},indent=2)+'\n')
