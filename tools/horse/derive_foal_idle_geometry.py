"""Transfer the accepted hind rest shape to the original foal Idle/morph rig.
Original foundation and Walk candidate are read-only; no rerig or UV changes.
"""
import bpy,json,hashlib,sys
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(Path(__file__).resolve().parent))
from derive_stock_walk import read,NAMES
SOURCE=ROOT/'art/derived/production/horse_foal.blend'
WALK=ROOT/'art/derived/production/candidates/foal-walk-hind-geometry/horse_foal_walk.blend'
STIFLE=ROOT/'art/derived/production/candidates/foal-walk-stifle/horse_foal_walk.blend'
OUT=ROOT/'art/derived/production/candidates/foal-idle-hind-geometry'
def snapshot(path):
 bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False)
 result={}
 for name in NAMES:
  mesh=bpy.data.objects[name].data
  result[name]={'positions':read(mesh.vertices,'co',3).copy(),'shapes':{k.name:read(k.data,'co',3).copy() for k in mesh.shape_keys.key_blocks},'normals':[tuple(n.value) for n in mesh.attributes['custom_normal'].data] if mesh.attributes.get('custom_normal') else None}
 return result
before=snapshot(STIFLE);target=snapshot(WALK);original=snapshot(SOURCE)
for name in NAMES:
 assert np.array_equal(original[name]['positions'],before[name]['positions']),name+' baseline coordinates'
 for key in original[name]['shapes']:assert np.array_equal(original[name]['shapes'][key],before[name]['shapes'][key]),name+': '+key
 mesh=bpy.data.objects[name].data
 mesh.vertices.foreach_set('co',target[name]['positions'])
 for key in mesh.shape_keys.key_blocks:key.data.foreach_set('co',target[name]['shapes'][key.name])
 # Same original topology, same local support and final normals as approved Walk.
 if target[name]['normals'] is not None:
  for item,value in zip(mesh.attributes['custom_normal'].data,target[name]['normals']):item.value=value
 mesh.update()
bpy.context.view_layer.update();OUT.mkdir(parents=True,exist_ok=True)
scene=bpy.context.scene;scene.frame_set(scene.frame_start)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'horse_foal.blend'),compress=True)
bpy.ops.object.select_all(action='DESELECT');rig=bpy.data.objects['Skeleton']
for obj in [rig,*[bpy.data.objects[n] for n in NAMES]]:obj.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/'horse_foal.glb'),export_format='GLB',use_selection=True,export_yup=True,export_apply=False,export_texcoords=True,export_normals=True,export_skins=True,export_vertex_color='ACTIVE',export_all_vertex_colors=True,export_animations=True,export_nla_strips=False,export_cameras=False,export_lights=False,export_materials='EXPORT',export_image_format='AUTO',export_morph=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
r={'schema':'horse.foal_idle.geometry_transfer.v1','sourceBlendSha256':sha(SOURCE),'acceptedWalkBlendSha256':sha(WALK),'candidateBlendSha256':sha(OUT/'horse_foal.blend'),'candidateGlbSha256':sha(OUT/'horse_foal.glb'),'meshPositionsAndAllShapeKeysTakenExactlyFromAcceptedWalk':True,'originalIdleRigWeightsAndAnimationRetained':True,'independentAuditRequired':True}
(OUT/'construction-report.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r))
