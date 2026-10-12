"""Build an isolated Light Walk anatomy correction; preserve every mesh attribute.
The existing source stays immutable. Promotion requires independent source,
GLB, runtime contact/attachment, coat and visual checks.
"""
import bpy,math,json,sys
import numpy as np
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[2];BASE=ROOT.parent;OUT=ROOT/'art/derived/production/candidates/light-walk-anatomy';OUT.mkdir(parents=True,exist_ok=True);SOURCE=ROOT/'art/derived/production/horse_light_walk.blend'
sys.path.insert(0,str(ROOT/'tools/horse'));from derive_stock_walk import fingerprint,read,evaluated,NAMES
bpy.ops.wm.open_mainfile(filepath=str(SOURCE),use_scripts=False);rig=bpy.data.objects['Skeleton'];scene=bpy.context.scene
protected={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
profile=json.load(open(ROOT/'game/assets/horse/production/evidence/light-walk-v5-detail/walk-motion-profile.json'))
names=[b.name for b in rig.data.bones if not b.name.startswith('DRV_')];samples=[];records=[]
def rotmap(M,A,B,Ap,Bp):
 q=(B-A).rotation_difference(Bp-Ap);return Matrix.Translation(Ap)@q.to_matrix().to_4x4()@Matrix.Translation(-A)@M
for frame in np.linspace(0,29,117):
 scene.frame_set(int(frame),subframe=float(frame)%1);bpy.context.view_layer.update();mat={n:rig.pose.bones[n].matrix.copy() for n in names};rec={'phase':float(frame/29),'joints':{}}
 for side,off in [('l',0),('r',.5)]:
  phase=(frame/29-off)%1
  hn='front_humerus_'+side;un='front_leg_upper_'+side;ln='front_leg_lower_'+side;an='front_leg_ankle_'+side
  S,E,K,F=[mat[n].translation.copy() for n in (hn,un,ln,an)];oldF=F.copy();h=(E-S).length;r=(K-E).length;c=(F-K).length
  if phase>=.6:
   t=(phase-.6)/.4;theta=math.radians(40)*t*t*(1-t)**4/(16/729)
   points=[Vector((p['restPosition'][0],-p['restPosition'][2],p['restPosition'][1])) for p in profile['limbs']['fore_'+side]['solePoints']]
   toe=max(points,key=lambda p:p.x);pivot=mat[an]@rig.data.bones[an].matrix_local.inverted()@toe
   roll=Matrix.Translation(pivot)@Matrix.Rotation(theta,4,'Y')@Matrix.Translation(-pivot)
   for n in (an,'front_leg_foot_'+side,'front_leg_leaf_'+side):mat[n]=roll@mat[n]
   F=mat[an].translation.copy()
  flex=math.radians(5 if phase<.6 else 5+35*math.sin(math.pi*(phase-.6)/.4)**2)
  dist=(F-S).length;d=math.sqrt(r*r+c*c+2*r*c*math.cos(flex))
  assert abs(h-d)<dist<h+d,(side,phase,dist,h+d)
  e=(F-S).normalized();a=(h*h-d*d+dist*dist)/(2*dist);height=math.sqrt(max(0,h*h-a*a));norm=Vector((-e.z,0,e.x)).normalized();Ep=S+e*a-norm*height
  v=F-Ep;di=v.length;e2=v.normalized();a2=(r*r-c*c+di*di)/(2*di);h2=math.sqrt(max(0,r*r-a2*a2));n2=Vector((-e2.z,0,e2.x)).normalized();Kp=Ep+e2*a2+n2*h2
  mat[hn]=rotmap(mat[hn],S,E,S,Ep);mat[un]=rotmap(mat[un],E,K,Ep,Kp);mat[ln]=rotmap(mat[ln],K,oldF,Kp,F)
  rec['joints'][side]={'flex':math.degrees(flex),'elbowShift':(Ep-E).length,'carpusShift':(Kp-K).length}
 samples.append((float(frame),mat));records.append(rec)
rig.animation_data_clear()
for pb in rig.pose.bones:
 for c in list(pb.constraints):pb.constraints.remove(c)
rig.animation_data_create();a=bpy.data.actions.new('Walk Anatomical Trial');rig.animation_data.action=a
for frame,mat in samples:
 for n in names:
  pb=rig.pose.bones[n];b=pb.bone;kw={} if b.parent is None else dict(parent_matrix=mat[b.parent.name],parent_matrix_local=b.parent.matrix_local);pb.rotation_mode='QUATERNION';pb.matrix_basis=b.convert_local_to_pose(mat[n],b.matrix_local,invert=True,**kw)
  for prop in ('location','rotation_quaternion','scale'):pb.keyframe_insert(prop,frame=frame*2,group=n)
for layer in a.layers:
 for strip in layer.strips:
  for slot in a.slots:
   bag=strip.channelbag(slot)
   if bag:
    for fc in bag.fcurves:
     for k in fc.keyframe_points:k.interpolation='LINEAR'
scene.frame_start=0;scene.frame_end=58;scene.render.fps=60;scene.frame_set(0)
# Candidate skin repairs below preserve all geometric and coat source data.



o=bpy.data.objects['Body']
def sm(a,b,x):t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
for v in o.data.vertices:
 x,y,z=v.co
 blend=sm(.53,.65,z)*(1-sm(1.12,1.20,z))*sm(.14,.30,x)*(1-sm(.50,.65,x))
 if blend<1e-8:continue
 old={o.vertex_groups[g.group].name:g.weight for g in v.groups};limb=1-sm(.65,1.15,z);left=sm(-.14,.14,y);target={'front_leg_upper_l':limb*left,'front_leg_upper_r':limb*(1-left)};torso={n:w for n,w in old.items() if not n.startswith(('front_leg_','front_humerus_'))};total=sum(torso.values())
 if total<1e-8:torso={'spine_3':1};total=1
 for n,w in torso.items():target[n]=target.get(n,0)+w/total*(1-limb)
 result={n:(1-blend)*old.get(n,0)+blend*target.get(n,0) for n in set(old)|set(target)};top=sorted(result,key=result.get)[-8:];total=sum(result[n] for n in top)
 for g in list(v.groups):o.vertex_groups[g.group].remove([v.index])
 for n in top:o.vertex_groups[n].add([v.index],result[n]/total,'REPLACE')

# Match every exact Body/hair root before extending free hair weights.
from repair_stock_mane_weights import repair_mane
body=bpy.data.objects['Body'];lookup={tuple(round(c,6) for c in v.co):v for v in body.data.vertices}
root_counts={}
for component in ('Mane','Tail'):
 hair=bpy.data.objects[component];count=0
 for v in hair.data.vertices:
  source=lookup.get(tuple(round(c,6) for c in v.co))
  if source is None:continue
  count+=1
  for g in list(v.groups):hair.vertex_groups[g.group].remove([v.index])
  for g in source.groups:
   name=body.vertex_groups[g.group].name
   if name not in hair.vertex_groups:hair.vertex_groups.new(name=name)
   hair.vertex_groups[name].add([v.index],g.weight,'REPLACE')
 root_counts[component]=count
mane_report=repair_mane(bpy.data.objects['Mane'],body)
tail=bpy.data.objects['Tail'];neighbors=[set() for _ in tail.data.vertices]
for edge in tail.data.edges:
 a,b=edge.vertices;neighbors[a].add(b);neighbors[b].add(a)
missing=[v.index for v in tail.data.vertices if sum(g.weight for g in v.groups)<1e-8]
for i in missing:
 totals={}
 for j in neighbors[i]:
  factor=1/max(1e-6,(tail.data.vertices[i].co-tail.data.vertices[j].co).length)
  for g in tail.data.vertices[j].groups:totals[g.group]=totals.get(g.group,0)+factor*g.weight
 top=sorted(totals,key=totals.get)[-8:];total=sum(totals[k] for k in top)
 assert total>0
 for k in top:tail.vertex_groups[k].add([i],totals[k]/total,'REPLACE')
assert protected=={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
scene.frame_set(0)
blend=OUT/'horse_light_walk.blend';glb=OUT/'horse_light_walk.glb'
bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
bpy.ops.object.select_all(action='DESELECT')
for o in [rig,*[bpy.data.objects[n] for n in NAMES]]:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIVE_ACTIONS',export_nla_strips_merged_animation_name='Walk',export_force_sampling=True,export_frame_range=True,export_frame_step=1,export_skins=True,export_all_influences=True,export_morph=True,export_morph_normal=True,export_morph_tangent=False,export_materials='EXPORT',export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,export_extras=False,export_yup=True)
import hashlib
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
profile['periodFrames']=58;profile['fps']=60
for limb in profile['limbs'].values():limb['stanceStartFrame']*=2
profile['candidateBlendSha256']=sha(blend);profile['sourceGlbSha256']=sha(glb)
profile['scope']='Neutral-only anatomical front-leg solve, 5-degree support flexion, forward carpus and 40-degree toe-pivot recovery fold; full stance sole witnesses retained.'
(OUT/'walk-motion-profile.json').write_text(json.dumps(profile,indent=2)+'\n')
report={'schema':'horse.light_walk.candidate.v1','sourceBlendSha256':sha(SOURCE),'candidateBlendSha256':sha(blend),'candidateGlbSha256':sha(glb),'protectedMeshDataExact':True,'protectedMeshData':protected,'reusedLightAtlas':True,'rootPairs':root_counts,'maneRepair':mane_report,'tailZeroWeightRepairs':missing,'motion':'Anatomically constrained shoulder/elbow and two-link radius/cannon solve; toe-pivot recovery rotation, planted feet unchanged.','samples':records}
(OUT/'source-audit.json').write_text(json.dumps(report,indent=2)+'\n')
print('ANATOMY_CANDIDATE',sha(blend),sha(glb),flush=True)
