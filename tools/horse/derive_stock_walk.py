"""Derive an isolated Stock Walk from the checked Light controller/skin.
Run with Blender 5.2.2 in background. Never writes accepted foundation assets.
Stock topology must exactly match Light; all Stock mesh data remains untouched.
"""
from pathlib import Path
import bpy, json, hashlib, math, sys
import numpy as np
from mathutils import Vector, Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from build_meshy_production import family_point
from repair_stock_mane_weights import repair_mane
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/derived/production/candidates/stock-walk-polish'
OUT.mkdir(parents=True,exist_ok=True)
NAMES=('Body','Eyes','Mane','Tail')

def digest(value): return hashlib.sha256(np.asarray(value).tobytes()).hexdigest()
def read(coll,prop,n,dtype=np.float32):
 a=np.empty(len(coll)*n,dtype=dtype);coll.foreach_get(prop,a);return a

def fingerprint(o):
 m=o.data
 return {'vertices':len(m.vertices),'faces':len(m.polygons),
 'positions':digest(read(m.vertices,'co',3)), 'normals':digest(read(m.vertices,'normal',3)),
 'topology':digest(read(m.loops,'vertex_index',1,np.int32)),
 'polygonRanges':digest([(p.loop_start,p.loop_total,p.material_index,p.use_smooth) for p in m.polygons]),
 'uv':{u.name:digest(read(u.data,'uv',2)) for u in m.uv_layers},
 'colors':{a.name:dict(domain=a.domain,type=a.data_type,data=digest(read(a.data,'color',4))) for a in m.color_attributes},
 'morphs':{k.name:digest(read(k.data,'co',3)) for k in m.shape_keys.key_blocks} if m.shape_keys else {}}

def evaluated():
 dg=bpy.context.evaluated_depsgraph_get();result={}
 for n in NAMES:
  ob=bpy.data.objects[n].evaluated_get(dg);me=ob.to_mesh();result[n]=read(me.vertices,'co',3).reshape(-1,3).copy();ob.to_mesh_clear()
 return result

def main():
 global OUT
 donor_override=None
 if '--' in sys.argv:
  import argparse
  parser=argparse.ArgumentParser();parser.add_argument('--donor',type=Path);parser.add_argument('--out',type=Path);args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);donor_override=args.donor
  if args.out is not None:OUT=args.out.resolve();OUT.mkdir(parents=True,exist_ok=True)
 source=ROOT/'art/derived/production/horse_stock.blend';donor=ROOT/'art/derived/production/horse_light_walk.blend'
 if donor_override is not None:donor=donor_override.resolve()
 bpy.ops.wm.open_mainfile(filepath=str(donor));donor_period=bpy.context.scene.frame_end-bpy.context.scene.frame_start
 rig=bpy.data.objects['Skeleton'];originals={n:bpy.data.objects[n] for n in NAMES}
 with bpy.data.libraries.load(str(source),link=False) as (a,b): b.objects=list(NAMES)
 targets={n:o for n,o in zip(NAMES,b.objects)}
 tail_skin_repairs=[]
 before={n:fingerprint(o) for n,o in targets.items()}
 # Fit the donor's rest pivots to Stock with the same documented family warp
 # used to build its mesh. Bake GLOBAL donor poses with the matching constant
 # pivot shift, so distal IK/footplants stay exact and head/ear rotations do not
 # orbit the Light skull's 140 mm longer neck.
 bone_names=[b.name for b in rig.data.bones if not b.name.startswith('DRV_')]
 offsets={}
 for name in bone_names:
  p=rig.data.bones[name].head_local.copy();q=p.copy()
  for _ in range(20):q+=p-family_point(q,'light')
  offsets[name]=family_point(q,'stock')-p
 samples=[]
 for f in np.linspace(0,donor_period,117):
  bpy.context.scene.frame_set(int(f),subframe=float(f)%1);bpy.context.view_layer.update()
  samples.append((float(f),{n:Matrix.Translation(offsets[n])@rig.pose.bones[n].matrix.copy() for n in bone_names}))
 rig.animation_data_clear()
 for pb in rig.pose.bones:
  for c in list(pb.constraints):pb.constraints.remove(c)
 bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
 for eb in list(rig.data.edit_bones):
  if eb.name.startswith('DRV_'):rig.data.edit_bones.remove(eb)
 for eb in rig.data.edit_bones:eb.use_connect=False
 for eb in rig.data.edit_bones:eb.translate(offsets[eb.name])
 bpy.ops.object.mode_set(mode='OBJECT');rig.animation_data_create()
 action=bpy.data.actions.new('Walk');rig.animation_data.action=action
 for f,matrices in samples:
  for pb in rig.pose.bones:
   b=pb.bone;kwargs={} if b.parent is None else dict(parent_matrix=matrices[b.parent.name],parent_matrix_local=b.parent.matrix_local)
   pb.rotation_mode='QUATERNION';pb.matrix_basis=b.convert_local_to_pose(matrices[b.name],b.matrix_local,invert=True,**kwargs)
   for prop in ('location','rotation_quaternion','scale'):pb.keyframe_insert(prop,frame=f/donor_period*58,group=b.name)
 for layer in action.layers:
  for strip in layer.strips:
   for slot in action.slots:
    bag=strip.channelbag(slot)
    if bag:
     for fc in bag.fcurves:
      for key in fc.keyframe_points:key.interpolation='LINEAR'

 for n,target in targets.items():
  orig=originals[n]
  assert len(orig.data.vertices)==len(target.data.vertices)
  print('TOPOLOGY',n,target.name,flush=True)
  assert sorted(tuple(sorted(p.vertices)) for p in orig.data.polygons)==sorted(tuple(sorted(p.vertices)) for p in target.data.polygons)
  weights=[[(orig.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in orig.data.vertices]
  for g in list(target.vertex_groups):target.vertex_groups.remove(g)
  for g in orig.vertex_groups:target.vertex_groups.new(name=g.name)
  for i,w in enumerate(weights):
   for name,value in w:target.vertex_groups[name].add([i],value,'REPLACE')
  missing=[i for i,w in enumerate(weights) if sum(value for _,value in w)<1e-8]
  if missing:
   assert n=='Tail',f'Unexpected unweighted {n} vertices'
   neighbours=[set() for _ in weights]
   for edge in target.data.edges:
    a,b=edge.vertices;neighbours[a].add(b);neighbours[b].add(a)
   for i in missing:
    totals={}
    for j in sorted(neighbours[i]):
     if not weights[j]:continue
     coefficient=1/max(1e-6,(target.data.vertices[i].co-target.data.vertices[j].co).length)
     for name,value in weights[j]:totals[name]=totals.get(name,0)+coefficient*value
    assert totals,f'Tail vertex {i} has no weighted neighbours'
    total=sum(totals.values())
    for name,value in totals.items():target.vertex_groups[name].add([i],value/total,'REPLACE')
    tail_skin_repairs.append({'vertex':i,'weights':{name:value/total for name,value in totals.items()}})
  target.parent=rig;target.matrix_parent_inverse=orig.matrix_parent_inverse.copy();target.matrix_world=orig.matrix_world.copy()
  for mod in target.modifiers:
   if mod.type=='ARMATURE':mod.object=rig
  bpy.context.collection.objects.link(target)
  bpy.data.objects.remove(orig,do_unlink=True);target.name=n
 for o in list(bpy.data.objects):
  if o.type=='ARMATURE' and o!=rig:bpy.data.objects.remove(o,do_unlink=True)
 mane_skin_repair=repair_mane(targets['Mane'],targets['Body'])
 assert before=={n:fingerprint(o) for n,o in targets.items()}
 rig.animation_data.action.name='Walk'
 bpy.context.scene.frame_end=58;bpy.context.scene.render.fps=60
 bpy.context.scene.frame_set(0)
 bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'horse_stock_walk.blend'),compress=True)
 profile_path=donor.parent/'walk-motion-profile.json'
 if not profile_path.is_file():profile_path=ROOT/'game/assets/horse/production/evidence/light-walk-v5-detail/walk-motion-profile.json'
 profile=json.loads(profile_path.read_text());profile_period=profile['periodFrames']
 profile['periodFrames']=58;profile['fps']=60
 for limb in profile['limbs'].values():limb['stanceStartFrame']*=58/profile_period
 scene=bpy.context.scene
 # Same indexed distal geometry and weights permit direct reuse of the recorded
 # sole witnesses. Recheck actual points, rather than assume family identity.
 body=targets['Body'];rest=read(body.data.vertices,'co',3).reshape(-1,3)
 witness_delta=0
 for limb in profile['limbs'].values():
  for p in limb['solePoints']:
   gx,gy,gz=p['restPosition'];r=np.array([gx,-gz,gy]);witness_delta=max(witness_delta,float(np.linalg.norm(rest[p['sourceVertex']]-r)))
 edges=np.array([tuple(e.vertices) for e in body.data.edges]);length=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1);valid=length>1e-8
 edges=edges[valid];length=length[valid]
 lookup={tuple(np.round(p,6)):i for i,p in enumerate(rest)}
 joins={}
 for name in ('Mane','Tail'):
  coords=read(targets[name].data.vertices,'co',3).reshape(-1,3)
  joins[name]=[(i,lookup[tuple(np.round(p,6))]) for i,p in enumerate(coords) if tuple(np.round(p,6)) in lookup]
 phase_data=[];stretch=0.;seam=0.;first=None;last=None
 contacts={name:[] for name in profile['limbs']}
 for frame in np.linspace(0,58,61):
  scene.frame_set(math.floor(frame),subframe=frame%1);bpy.context.view_layer.update();poses=evaluated();p=poses['Body']
  if first is None:first=poses
  last=poses
  stretch=max(stretch,float(np.max(np.linalg.norm(p[edges[:,0]]-p[edges[:,1]],axis=1)/length)))
  for name,pairs in joins.items():
   if pairs:seam=max(seam,max(float(np.linalg.norm(poses[name][a]-p[b])) for a,b in pairs))
  for name,limb in profile['limbs'].items():
   ids=[p['sourceVertex'] for p in limb['solePoints']];sole=p[ids].copy();sole[:,0]+=frame/60*profile['forwardSpeedMetersPerSecond']
   phase=((frame-limb['stanceStartFrame'])/58)%1
   contacts[name].append((float(frame),float(phase),sole))
 summaries={}
 for name,rows in contacts.items():
  slip=0.;pairs=0
  for (fa,pa,sa),(fb,pb,sb) in zip(rows,rows[1:]):
   if pa<.6 and pb<.6 and pb>pa:slip=max(slip,float(np.max(np.linalg.norm(sb-sa,axis=1))));pairs+=1
  stance=[s for _,p,s in rows if p<.6];swing=[s for _,p,s in rows if .7<=p<=.9]
  summaries[name]={'soleCount':len(stance[0]),'stanceAdjacentMaxMotionM':slip,'minStanceHeightM':min(float(s[:,2].min()) for s in stance),'maxStanceP05HeightM':max(float(np.quantile(s[:,2],.05)) for s in stance),'minMiddleSwingClearanceM':min(float(s[:,2].min()) for s in swing),'stanceComparisons':pairs}
 loop=max(float(np.max(np.linalg.norm(first[n]-last[n],axis=1))) for n in NAMES)
 maxmotion=max(float(np.max(np.linalg.norm(first[n]-evaluated()[n],axis=1))) for n in NAMES)
 scene.frame_set(28);mid=evaluated();maxmotion=max(float(np.max(np.linalg.norm(first[n]-mid[n],axis=1))) for n in NAMES)
 report={'schema':'horse.stock_walk.candidate.v1','scope':'Neutral shape only. Isolated candidate; original Stock/Light/foal and game guard unchanged.',
 'sourceBlendSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'donorBlendSha256':hashlib.sha256(donor.read_bytes()).hexdigest(),
 'candidateBlendSha256':hashlib.sha256((OUT/'horse_stock_walk.blend').read_bytes()).hexdigest(),
 'protectedMeshData':before,'protectedMeshDataExact':before=={n:fingerprint(o) for n,o in targets.items()},
 'reusedLightAtlas':False,'periodFrames':58,'fps':60,'forwardSpeedMetersPerSecond':profile['forwardSpeedMetersPerSecond'],
 'sampleCount':61,'soleSourceMaxDifferenceM':witness_delta,'bodyMaxEdgeStretch':stretch,'seamPairCounts':{n:len(v) for n,v in joins.items()},'maxSeamGapM':seam,'loopMaxVertexDeltaM':loop,'midCycleMaxVertexMotionM':maxmotion,'contacts':summaries,
 'rigFitMethod':'Known Light-to-Stock family warp at rest pivots; sampled donor global transforms translated by the same offsets. Distal foot offsets remain zero.', 'rigPivotOffsets':{n:list(v) for n,v in offsets.items()},'polish':'Same proven poses at 60 fps; eight inherited unweighted Tail vertices bound by immediate neighbours; Mane discontinuities smoothed with exact roots locked. Duration, speed and geometry unchanged.','tailSkinRepairs':tail_skin_repairs,'maneSkinRepair':mane_skin_repair,'runtimeTested':False,'visualReview':'pending'}
 (OUT/'source-audit.json').write_text(json.dumps(report,indent=2)+'\n')
 print('STOCK_REPORT',json.dumps({k:v for k,v in report.items() if k!='protectedMeshData'}),flush=True)
 # Export constraint-evaluated animation and all eight weights from the checked
 # skin. Morphs/UV/colors remain the Stock source data, no Light texture attached.
 scene.frame_set(0)
 bpy.ops.object.select_all(action='DESELECT')
 for o in [rig,*targets.values()]:o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.gltf(filepath=str(OUT/'horse_stock_walk.glb'),export_format='GLB',use_selection=True,
  export_animations=True,export_animation_mode='ACTIVE_ACTIONS',export_nla_strips_merged_animation_name='Walk',export_force_sampling=True,
  export_frame_range=True,export_frame_step=1,export_skins=True,export_all_influences=True,
  export_morph=True,export_morph_normal=True,export_morph_tangent=False,export_materials='EXPORT',export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,
  export_extras=False,export_yup=True)
 report['candidateGlbSha256']=hashlib.sha256((OUT/'horse_stock_walk.glb').read_bytes()).hexdigest()
 (OUT/'source-audit.json').write_text(json.dumps(report,indent=2)+'\n')
 profile['schema']='horse.stock_walk.motion_profile.v1';profile['candidateBlendSha256']=report['candidateBlendSha256'];profile['sourceGlbSha256']=report['candidateGlbSha256']
 (OUT/'walk-motion-profile.json').write_text(json.dumps(profile,indent=2)+'\n')
if __name__=='__main__':main()
