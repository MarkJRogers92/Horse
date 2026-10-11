"""Bounded neutral foal walk candidate on preserved juvenile geometry.
The juvenile has a distinct source rig: reuse the checked four-beat phase/contact
contract, solve its shorter limbs in its own rest space, and retain its coat data.
No runtime/catalog activation is performed by this candidate builder.
"""
from pathlib import Path
import bpy,json,math,sys,hashlib
import numpy as np
from mathutils import Vector,Matrix
from mathutils.kdtree import KDTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from derive_stock_walk import fingerprint,read,digest,evaluated,NAMES
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art/derived/production/candidates/foal-walk-polish'
PERIOD=58;FPS=60;DUTY=.6;AMPLITUDE=.1;SPEED=2*AMPLITUDE/(DUTY*PERIOD/FPS)
LIMBS={
 'fore_l':dict(prefix='Front_L',phase=0,A=(.19,.082,.70),B=(.19,.082,.40),C=(.184,.099,.15),bend=1),
 'hind_r':dict(prefix='Hind_R',phase=.25,A=(-.43,-.09,.82),B=(-.575,-.09,.52),C=(-.65,-.11,.15),bend=-1),
 'fore_r':dict(prefix='Front_R',phase=.5,A=(.19,-.082,.70),B=(.19,-.082,.40),C=(.184,-.099,.15),bend=1),
 'hind_l':dict(prefix='Hind_L',phase=.75,A=(-.43,.09,.82),B=(-.575,.09,.52),C=(-.65,.11,.15),bend=-1),
}
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def segment(a,b,ap,bp):
 q=(b-a).rotation_difference(bp-ap);return Matrix.Translation(ap)@q.to_matrix().to_4x4()@Matrix.Translation(-a)
def trajectory(phase):
 if phase<DUTY:return AMPLITUDE-2*AMPLITUDE*phase/DUTY,0.
 t=(phase-DUTY)/(1-DUTY)
 # Cubic Hermite travel matches the stance velocity
 # at lift-off and landing. The 20 cm stance distance and actor speed stay fixed.
 m=-2*AMPLITUDE*(1-DUTY)/DUTY
 dx=-AMPLITUDE+2*AMPLITUDE*(3*t*t-2*t*t*t)+m*(2*t*t*t-3*t*t+t)
 return dx,.066*math.sin(math.pi*t)**2

def main():
 OUT.mkdir(parents=True,exist_ok=True);source=ROOT/'art/derived/production/horse_foal.blend';bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['Skeleton'];body=bpy.data.objects['Body'];scene=bpy.context.scene
 before={n:fingerprint(bpy.data.objects[n]) for n in NAMES};original_weights={n:[[(bpy.data.objects[n].vertex_groups[a.group].name,a.weight) for a in v.groups] for v in bpy.data.objects[n].data.vertices] for n in NAMES}
 rest=read(body.data.vertices,'co',3).reshape(-1,3);rig.animation_data_clear();rig.animation_data_create();action=bpy.data.actions.new('Walk');rig.animation_data.action=action
 # Mid-rotation knee helpers preserve cross-section volume during flexion.
 # They change only skin/rig data; the original juvenile mesh stays identical.
 bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
 for name,spec in LIMBS.items():
  bone=rig.data.edit_bones.new('Walk_Knee_'+name);bone.head=spec['B'];bone.tail=Vector(spec['B'])+Vector((0,0,.05));bone.parent=rig.data.edit_bones['Root'];bone.use_deform=True
 bpy.ops.object.mode_set(mode='OBJECT')
 for name in LIMBS:body.vertex_groups.new(name='Walk_Knee_'+name)
 matrices={b.name:b.matrix_local.copy() for b in rig.data.bones};groupnames={g.name for g in body.vertex_groups}
 # Whole visible hoof support is made rigid. The transition ends above the
 # hoof without moving any vertices, UVs, colors, or shape-key coordinates.
 limbs_profile={}
 for name,spec in LIMBS.items():
  pre=spec['prefix'];isfront=pre.startswith('Front');side=1 if pre.endswith('L') else -1
  ids=[v.index for v in body.data.vertices if v.co.z<.08 and (v.co.x>0 if isfront else v.co.x<-.35) and v.co.y*side>0]
  low=min(rest[i,2] for i in ids);sole=[i for i in ids if rest[i,2]<low+.012]
  foot=pre+'_Hoof_02';spec.update(sole=sole,sole_min=float(low),foot=foot)
  limbs_profile[name]={'boneName':foot,'stanceStartFrame':spec['phase']*PERIOD,'restBoneHead':[rig.data.bones[foot].head_local.x,rig.data.bones[foot].head_local.z,-rig.data.bones[foot].head_local.y], 'solePoints':[{'sourceVertex':i,'restPosition':[float(rest[i,0]),float(rest[i,2]),float(-rest[i,1])]} for i in sole]}
  for v in body.data.vertices:
   if not ((v.co.x>0 if isfront else v.co.x<-.35) and v.co.y*side>0 and v.co.z<.18):continue
   t=1-smooth(.08,.18,v.co.z)
   old=[(body.vertex_groups[g.group],g.weight) for g in v.groups]
   for g,w in old:g.remove([v.index])
   for g,w in old:
    if w*(1-t)>1e-9:g.add([v.index],w*(1-t),'REPLACE')
   g=body.vertex_groups[foot];oldfoot=next((w for x,w in old if x.name==foot),0)
   g.add([v.index],t+oldfoot*(1-t),'REPLACE')
 # Rebuild only the candidate skin from the source's limb membership, with
 # continuous proximal and knee transitions. Source group names alone contain
 # overlapping disconnected segments and create a shoulder kink when posed.
 memberships=np.zeros((len(rest),4));limbnames=list(LIMBS)
 for i,weights in enumerate(original_weights['Body']):
  for j,name in enumerate(limbnames):
   pre=LIMBS[name]['prefix'];memberships[i,j]=sum(w for n,w in weights if n.startswith(pre+'_') or (n=='FoalSmart_Bone_014' and name=='hind_r') or (n=='FoalSmart_Bone_017' and name=='hind_l'))
 neighbours=[[] for _ in rest]
 for e in body.data.edges:
  a,b=e.vertices;neighbours[a].append(b);neighbours[b].append(a)
 for _ in range(4):
  memberships=np.array([.5*v+.5*memberships[ns].mean(axis=0) if ns else v for v,ns in zip(memberships,neighbours)])
 tree=KDTree(len(rest))
 for i,p in enumerate(rest):tree.insert(p,i)
 tree.balance();prior=memberships.copy()
 for i,p in enumerate(rest):
  if p[2]<.38 or p[2]>.99:continue
  nearby=tree.find_range(p,.16);ids=[j for _,j,d in nearby];w=np.array([math.exp(-.5*(d/.07)**2) for _,j,d in nearby]);w/=w.sum()
  blend=smooth(.38,.52,p[2]);memberships[i]=(1-blend)*prior[i]+blend*np.sum(prior[ids]*w[:,None],axis=0)
 for v in body.data.vertices:
  p=v.co;weights={};mass=0
  for j,name in enumerate(limbnames):
   spec=LIMBS[name];pre=spec['prefix'];limbmass=float(memberships[v.index,j])*(1-smooth(.68,.95,p.z));side=1 if pre.endswith('L') else -1
   limbmass*=1-smooth(.42,.65,p.z)*(1-smooth(.0,.18,p.y*side))
   upper=smooth(spec['B'][2]-.12,spec['B'][2]+.12,p.z)
   foot=1-smooth(.08,.18,p.z)
   # Below the hoof cuff, select the actual geometric limb and use one rigid
   # sole transform; diffuse source labels cannot spill into another hoof.
   if p.z<.18:
    isfront=pre.startswith('Front');side=1 if pre.endswith('L') else -1
    if (p.x>0 if isfront else p.x<-.35) and p.y*side>0:limbmass=1
    else:limbmass=0
   mass+=limbmass
   # Quadratic partition: smooth orientation rate and partial volume support,
   # without the concentrated weight gradients of a fully rigid knee band.
   upperweight=upper*upper;lowerweight=(1-upper)*(1-upper);joint=2*upper*(1-upper)
   for n,w in [(pre+'_Upper',upperweight*(1-foot)),(pre+('_Cannon' if pre.startswith('Front') else '_Hock'),lowerweight*(1-foot)),('Walk_Knee_'+name,joint*(1-foot)),(pre+'_Hoof_02',foot)]:
    if limbmass*w>1e-9:weights[n]=limbmass*w
  weights['Pelvis']=max(0,1-mass);total=sum(weights.values())
  for a in list(v.groups):body.vertex_groups[a.group].remove([v.index])
  for n,w in weights.items():
   if w>1e-9:body.vertex_groups[n].add([v.index],w/total,'REPLACE')
 # Explicit source anatomical groups, including the two caudal auxiliaries.
 assignments={}
 for name,s in LIMBS.items():
  pre=s['prefix'];upper={'Upper','Forearm'} if pre.startswith('Front') else {'Upper','Gaskin'}
  for n in groupnames:
   if n.startswith(pre+'_'):
    suffix=n[len(pre)+1:];assignments[n]=(name,'foot' if suffix in {'Hoof_02','Hoof_03'} else 'upper' if suffix in upper else 'lower')
 assignments['FoalSmart_Bone_014']=('hind_r','lower');assignments['FoalSmart_Bone_017']=('hind_l','lower')
 for name in LIMBS:assignments['Walk_Knee_'+name]=(name,'joint')
 maxreach=0
 for frame in np.linspace(0,PERIOD,117):
  cycle=float(frame)/PERIOD;bodydz=-.05+.002*math.cos(4*math.pi*cycle);base=Matrix.Translation((0,0,bodydz));deltas={}
  for name,s in LIMBS.items():
   phase=(cycle-s['phase'])%1;dx,dz=trajectory(phase);A,B,C=(Vector(s[k]) for k in ('A','B','C'));Ap=A+Vector((0,0,bodydz));Cp=C+Vector((dx,0,dz-s['sole_min']))
   # Two-link solve in each sagittal plane. Rest joint locations intentionally
   # stay independent of the inconsistent original Smart Rig bone endpoints.
   L1=(B-A).length;L2=(C-B).length;v=Cp-Ap;dist=v.length;maxreach=max(maxreach,dist/(L1+L2));assert dist<L1+L2+1e-6,(name,frame,dist,L1+L2)
   e=v.normalized();a=(L1*L1-L2*L2+dist*dist)/(2*dist);h=math.sqrt(max(0,L1*L1-a*a));normal=Vector((-e.z,0,e.x)).normalized()*s['bend'];Bp=Ap+e*a+normal*h
   upperdelta=segment(A,B,Ap,Bp);lowerdelta=segment(B,C,Bp,Cp)
   halfway=upperdelta.to_quaternion().slerp(lowerdelta.to_quaternion(),.5)
   jointdelta=Matrix.Translation(Bp)@halfway.to_matrix().to_4x4()@Matrix.Translation(-B)
   deltas[name]={'upper':upperdelta,'lower':lowerdelta,'joint':jointdelta,'foot':Matrix.Translation((dx,0,dz-s['sole_min']))}
  posed={n:(deltas[assignments[n][0]][assignments[n][1]] if n in assignments else base)@m for n,m in matrices.items()}
  for pb in rig.pose.bones:
   b=pb.bone;kwargs={} if b.parent is None else dict(parent_matrix=posed[b.parent.name],parent_matrix_local=b.parent.matrix_local)
   pb.rotation_mode='QUATERNION';pb.matrix_basis=b.convert_local_to_pose(posed[b.name],b.matrix_local,invert=True,**kwargs)
   for prop in ('location','rotation_quaternion','scale'):pb.keyframe_insert(prop,frame=float(frame),group=b.name)
 for layer in action.layers:
  for strip in layer.strips:
   for slot in action.slots:
    bag=strip.channelbag(slot)
    if bag:
     for fc in bag.fcurves:
      for k in fc.keyframe_points:k.interpolation='LINEAR'
 scene.frame_start=0;scene.frame_end=PERIOD;scene.render.fps=FPS;scene.frame_set(0)
 assert before=={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'horse_foal_walk.blend'),compress=True)
 edges=np.array([tuple(e.vertices) for e in body.data.edges]);length=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1);valid=length>1e-8;edges=edges[valid];length=length[valid]
 worst={'ratio':0};poses=[];contacts={n:[] for n in LIMBS}
 for frame in np.linspace(0,PERIOD,61):
  scene.frame_set(int(frame),subframe=float(frame)%1);p=evaluated();poses.append(p);rat=np.linalg.norm(p['Body'][edges[:,0]]-p['Body'][edges[:,1]],axis=1)/length;i=int(np.argmax(rat))
  if rat[i]>worst['ratio']:worst={'ratio':float(rat[i]),'frame':float(frame),'edge':edges[i].tolist(),'restPoints':rest[edges[i]].tolist()}
  for n,s in LIMBS.items():
   phase=(frame/PERIOD-s['phase'])%1;points=p['Body'][s['sole']].copy();points[:,0]+=frame/FPS*SPEED;contacts[n].append((float(frame),float(phase),points))
 summaries={}
 for name,rows in contacts.items():
  slip=max(float(np.max(np.linalg.norm(sb-sa,axis=1))) for (fa,pa,sa),(fb,pb,sb) in zip(rows,rows[1:]) if pa<.6 and pb<.6 and pb>pa)
  stance=[s for _,p,s in rows if p<.6];swing=[s for _,p,s in rows if .7<=p<=.9]
  summaries[name]={'soleCount':len(stance[0]),'stanceAdjacentMaxMotionM':slip,'minStanceHeightM':min(float(s[:,2].min()) for s in stance),'maxStanceP05HeightM':max(float(np.quantile(s[:,2],.05)) for s in stance),'minMiddleSwingClearanceM':min(float(s[:,2].min()) for s in swing)}
 report={'schema':'horse.foal_walk.candidate.v1','sourceBlendSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'candidateBlendSha256':hashlib.sha256((OUT/'horse_foal_walk.blend').read_bytes()).hexdigest(),'protectedMeshData':before,'protectedMeshDataExact':True,'reusedLightAtlas':False,'sampleCount':61,'fps':FPS,'periodFrames':PERIOD,'forwardSpeedMetersPerSecond':SPEED,'bodyMaxEdgeStretch':worst['ratio'],'worstEdge':worst,'maxLimbReachRatio':maxreach,'loopMaxVertexDeltaM':max(float(np.max(np.linalg.norm(poses[0][n]-poses[-1][n],axis=1))) for n in NAMES),'midCycleMaxVertexMotionM':max(float(np.max(np.linalg.norm(poses[0][n]-poses[30][n],axis=1))) for n in NAMES),'contacts':summaries,'polish':'Four midpoint-rotation knee helpers and velocity-continuous swing travel; geometry, full stance travel and physical gates unchanged.','runtimeTested':False,'visualReview':'pending'}
 (OUT/'source-audit.json').write_text(json.dumps(report,indent=2)+'\n');print('FOAL_REPORT',json.dumps({k:v for k,v in report.items() if k!='protectedMeshData'}),flush=True)
 scene.frame_set(0);bpy.ops.object.select_all(action='DESELECT')
 for o in [rig,*[bpy.data.objects[n] for n in NAMES]]:o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.gltf(filepath=str(OUT/'horse_foal_walk.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIVE_ACTIONS',export_nla_strips_merged_animation_name='Walk',export_force_sampling=True,export_frame_range=True,export_frame_step=1,export_skins=True,export_all_influences=True,export_morph=True,export_morph_normal=True,export_morph_tangent=False,export_materials='EXPORT',export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,export_extras=False,export_yup=True)
 report['candidateGlbSha256']=hashlib.sha256((OUT/'horse_foal_walk.glb').read_bytes()).hexdigest();(OUT/'source-audit.json').write_text(json.dumps(report,indent=2)+'\n')
 profile={'schema':'horse.foal_walk.motion_profile.v1','candidateBlendSha256':report['candidateBlendSha256'],'sourceGlbSha256':report['candidateGlbSha256'],'periodFrames':PERIOD,'fps':FPS,'forwardSpeedMetersPerSecond':SPEED,'dutyFactor':DUTY,'coordinateSpace':'Skeleton local, Blender (x,y,z) to Godot (x,z,-y)','scope':'Neutral foal candidate; lower hoof skin locally rigid; original geometry and coat data preserved.','sourceRootInPlace':True,'limbs':limbs_profile};(OUT/'walk-motion-profile.json').write_text(json.dumps(profile,indent=2)+'\n')
if __name__=='__main__':main()
