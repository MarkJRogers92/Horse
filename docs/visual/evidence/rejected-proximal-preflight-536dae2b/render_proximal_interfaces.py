"""Changed ownership display only; opens frozen9c in background and never saves."""
import bpy,json,hashlib,sys
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
sys.path.insert(0,'/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse/tools/horse')
import production_motion as pm
OUT=Path(__file__).resolve().parent;ROOT=OUT.parent
r=json.loads((OUT/'light_attachment_contract.json').read_text());refs=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json').read_text())['nodes']
sc=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_shell_neck_contours.json').read_text())
source=Path(r['frozenSource']);assert hashlib.sha256(source.read_bytes()).hexdigest()==r['actualSourceSha256']
bpy.ops.wm.open_mainfile(filepath=str(source));rig=bpy.data.objects['Skeleton'];rig.animation_data.action=None
for track in rig.animation_data.nla_tracks:track.mute=True
pm.set_sagittal_pose(rig)
colors={'unchanged':(.61,.60,.58,1),'complete_Chest_cervical_proposal':(.20,.75,.82,1),'Mane_attachment_proposal':(.97,.77,.26,1),
 'Front_L_patch':(.22,.45,.91,1),'Front_R_patch':(.69,.33,.89,1),'Hind_L_patch':(.18,.69,.36,1),'Hind_R_patch':(.95,.56,.19,1),'Pelvis_Tail_proposal':(.92,.38,.38,1),'original_head':(.77,.60,.45,1),'original_Ear_L':(.18,.79,.36,1),'original_Ear_R':(.66,.34,.91,1),'Eyes':(.18,.18,.18,1)}
classes={}
for key,node in refs.items():
 region=r['regionsByNode'][key];cl='unchanged'
 if region=='Chest_cervical_complete_collar':cl='complete_Chest_cervical_proposal'
 elif '_proximal_Front_L' in region:cl='Front_L_patch'
 elif '_proximal_Front_R' in region:cl='Front_R_patch'
 elif '_proximal_Hind_L' in region:cl='Hind_L_patch'
 elif '_proximal_Hind_R' in region:cl='Hind_R_patch'
 elif region.startswith('Mane_attached'):cl='Mane_attachment_proposal'
 elif region=='Pelvis_proximal_Tail_transition':cl='Pelvis_Tail_proposal'
 elif region=='frozen_original_head_ear':cl='original_head'
 for a in node['sourceRefs']:classes[(a['component'],a['vertexId'])]=cl
for e in sc['originalEarMembership']['records']:
 left=sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_L'));right=sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_R'))
 classes[(e['component'],e['vertexId'])]='original_Ear_L' if left>=right else 'original_Ear_R'
for obj in bpy.data.objects:
 if obj.type!='MESH':continue
 obj.data.materials.clear();mi={}
 for name,color in colors.items():
  mat=bpy.data.materials.new('AttachmentProposal_'+obj.name+'_'+name);mat.diffuse_color=color
  mi[name]=len(obj.data.materials);obj.data.materials.append(mat)
 for poly in obj.data.polygons:
  votes=[classes.get((obj.name,i),'Eyes' if obj.name=='Eyes' else 'unchanged') for i in poly.vertices]
  obj.data.polygons[poly.index].material_index=mi[max(dict.fromkeys(votes),key=votes.count)]
scene=bpy.context.scene;scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1050;scene.render.resolution_y=740;scene.render.resolution_percentage=100
scene.display.shading.color_type='MATERIAL';scene.display.shading.light='STUDIO';scene.display.shading.studio_light='paint.sl';scene.display.shading.show_shadows=False;scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD';scene.world.color=(.19,.20,.22)
camd=bpy.data.cameras.new('AttachmentProposalCamera');cam=bpy.data.objects.new('AttachmentProposalCamera',camd);scene.collection.objects.link(cam);camd.type='ORTHO';scene.camera=cam
views={'left_context':((.1,5,1),(.1,0,1),3.2),'q3_context':((3.5,-4,2.8),(.1,0,1),3.4),
 'shoulder_q3':((3.5,-4,2.8),(.26,0,1.22),1.6),'hip_back':((-5,-1.5,1.9),(-.71,0,1.14),1.4)}
def project(p):
 v=world_to_camera_view(scene,cam,Vector(p));return [v.x*1050,(1-v.y)*740]
worst=[max((p for p in r['poseChecks'] if p['pose'].startswith('Walk')),key=lambda p:p['maximumFixedStretch']),next(p for p in r['poseChecks'] if p['pose']=='MildNeck')]
for view,(loc,tgt,scale) in views.items():
 cam.location=loc;cam.rotation_euler=(Vector(tgt)-cam.location).to_track_quat('-Z','Y').to_euler();camd.ortho_scale=scale;bpy.context.view_layer.update()
 scene.render.filepath=str(OUT/f'light_attachment_{view}_raw.png');bpy.ops.render.render(write_still=True)
 metadata={'taskId':'adult_motion_specialist','view':view,'colors':colors,'renderDisplayOnly':True,
  'attachmentPaths':[[project(p) for p in a['positionsAlongPath']] for a in r['attachmentPaths']],
  'proximalTailPoints':[project(refs[str(i)]['position']) for i in r['proximalTailBodyNodes']],
  'coupledBodyTailRoots':[project(refs[str(i)]['position']) for i in r['exactBodyTailRootNodes']],
  'parentBoundaryPoints':{q:[project(p) for p in c.get('sharedParentBoundaryPositions',[])] for q,c in r['intrinsicArcContracts'].items()},
  'distalBoundaryPoints':{q:[project(p) for p in c.get('distalBoundaryPositions',[])] for q,c in r['intrinsicArcContracts'].items()},
  'joints':{q:{'head':project(c['jointHead']),'tail':project(c['jointTail']),'length_m':c['jointLength_m']} for q,c in r['intrinsicArcContracts'].items() if 'jointHead' in c},
  'worstFixedEdges':[{'pose':p['pose'],'stretch':p['maximumFixedStretch'],'sourceRefs':[e['sourceRefs'] for e in p['worst']['endpoints']],
   'points':[project(p) for p in p['worst']['rest']]} for p in worst]}
 (OUT/f'light_attachment_{view}_projection.json').write_text(json.dumps(metadata,indent=2))
assert hashlib.sha256(source.read_bytes()).hexdigest()==r['actualSourceSha256']
print('TASK adult_motion_specialist CHANGED_OWNERSHIP_VIEWS_COMPLETE no source save',flush=True)
