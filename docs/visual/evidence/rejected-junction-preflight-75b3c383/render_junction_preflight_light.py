"""Rest ownership render only: frozen source, display materials, no blend save."""
import bpy, json, hashlib, sys
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
sys.path.insert(0, '/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse/tools/horse')
import production_motion as pm
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support')
OUT=ROOT/'junction-preflight-light'
r=json.loads((OUT/'light_immutable_junction_contract.json').read_text())
refs=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json').read_text())['nodes']
sc=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_shell_neck_contours.json').read_text())
source=Path(r['frozenSource']); assert hashlib.sha256(source.read_bytes()).hexdigest()==r['actualBaselineSha256']
bpy.ops.wm.open_mainfile(filepath=str(source))
rig=bpy.data.objects['Skeleton'];rig.animation_data.action=None
for track in rig.animation_data.nla_tracks:track.mute=True
pm.set_sagittal_pose(rig)
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH'
scene.render.resolution_x=1050;scene.render.resolution_y=740;scene.render.resolution_percentage=100
scene.display.shading.color_type='MATERIAL';scene.display.shading.light='STUDIO';scene.display.shading.studio_light='paint.sl'
scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD';scene.world.color=(.19,.20,.22)
colors={'torso':(.57,.57,.56,1),'Front_L_distal':(.16,.38,.85,1),'Front_R_distal':(.61,.23,.79,1),
 'Chest_junction_Front_L':(.43,.66,.94,1),'Chest_junction_Front_R':(.81,.55,.93,1),'Chest_junction_shared':(.89,.29,.31,1),
 'Hind_L_distal':(.14,.63,.35,1),'Hind_R_distal':(.92,.52,.14,1),'Pelvis_junction_Hind_L':(.49,.80,.58,1),
 'Pelvis_junction_Hind_R':(.97,.70,.43,1),'Pelvis_junction_shared':(.91,.39,.29,1),
 'Chest_cervical_junction':(.20,.77,.82,1),'cranial_cervical':(.93,.83,.32,1),
 'frozen_original_head_ear':(.78,.61,.46,1),'tail_branch':(.37,.25,.16,1),'Pelvis_tail_shared_root':(.96,.39,.27,1),
 'original_Ear_L':(.12,.91,.33,1),'original_Ear_R':(.71,.26,.99,1),'Eyes':(.16,.16,.16,1)}
vertex_region={}
for i,node in refs.items():
 for a in node['sourceRefs']:vertex_region[(a['component'],a['vertexId'])]=r['regionsByNode'][i]
for e in sc['originalEarMembership']['records']:
 left=sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_L'))
 right=sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_R'))
 vertex_region[(e['component'],e['vertexId'])]='original_Ear_L' if left>=right else 'original_Ear_R'
for obj in bpy.data.objects:
 if obj.type!='MESH':continue
 obj.data.materials.clear();mi={}
 for name,color in colors.items():
  mat=bpy.data.materials.new('ImmutableOwnership_'+obj.name+'_'+name);mat.diffuse_color=color
  mi[name]=len(obj.data.materials);obj.data.materials.append(mat)
 for poly in obj.data.polygons:
  votes=[vertex_region.get((obj.name,i),'Eyes' if obj.name=='Eyes' else 'torso') for i in poly.vertices]
  chosen=max(dict.fromkeys(votes),key=votes.count);poly.material_index=mi[chosen]
camd=bpy.data.cameras.new('ImmutablePreflightCamera');cam=bpy.data.objects.new('ImmutablePreflightCamera',camd)
scene.collection.objects.link(cam);camd.type='ORTHO';scene.camera=cam
views={'front':((5,0,1),(.10,0,1),3.2),'back':((-5,0,1),(.10,0,1),3.2),
 'left':((.1,5,1),(.1,0,1),3.2),'right':((.1,-5,1),(.1,0,1),3.2),
 'top':((.1,0,6),(.1,0,1),3.2),'q3':((3.5,-4,2.8),(.1,0,1),3.4),
 'neck_q3':((3.5,-4,2.8),(.47,0,1.29),1.4),'hip_back':((-5,0,1),(-.48,0,.95),1.6)}
def project(p):
 a=world_to_camera_view(scene,cam,Vector(p));return [a.x*1050,(1-a.y)*740]
worst=[max((p for p in r['poseChecks'] if p['pose'].startswith('Walk')),key=lambda p:p['maxFixedEdgeStretch']),
 next(p for p in r['poseChecks'] if p['pose']=='MildNeck'),next(p for p in r['poseChecks'] if p['pose']=='GrazeStress')]
for view,(loc,tgt,scale) in views.items():
 cam.location=loc;cam.rotation_euler=(Vector(tgt)-cam.location).to_track_quat('-Z','Y').to_euler();camd.ortho_scale=scale
 bpy.context.view_layer.update();scene.render.filepath=str(OUT/f'light_immutable_{view}_raw.png');bpy.ops.render.render(write_still=True)
 metadata={'taskId':'adult_motion_specialist','view':view,'sourceSha256':r['actualBaselineSha256'],'materialDisplayOnly':True,
  'colors':colors,'regionCounts':r['regionCounts'],
  'rootRows':{q:[{'node':i,'region':r['regionsByNode'][str(i)],'pixel':project(refs[str(i)]['position'])} for i in ids] for q,ids in r['rootRows'].items()},
  'rejectedTracePoints':[{'node':a['node'],'pixel':project(a['position'])} for a in r['rejectedEmptyIllegalConflictingTraces']],
  'worstFixedTraces':[{'pose':p['pose'],'stretch':p['maxFixedEdgeStretch'],'worst':p['worst'],'pixels':[project(x) for x in p['worst']['rest']]} for p in worst],
  'bones':{b.name:[project(b.head_local),project(b.tail_local)] for b in rig.data.bones if b.name in ('Pelvis','Spine','Spine_Mid','Spine_Upper','Chest') or b.name.startswith(('Neck','Head','Front','Hind'))}}
 (OUT/f'light_immutable_{view}_projection.json').write_text(json.dumps(metadata,indent=2))
assert hashlib.sha256(source.read_bytes()).hexdigest()==r['actualBaselineSha256']
print('TASK adult_motion_specialist IMMUTABLE_OWNERSHIP_VIEWS_COMPLETE no blend saved',flush=True)
