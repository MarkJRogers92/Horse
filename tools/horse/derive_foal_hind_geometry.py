"""Owner-approved, bounded upper-hind shape correction on the isolated stifle rig.
No topology, UV, color, front motion, hoof path or other family change.
"""
import bpy,json,hashlib,sys
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(Path(__file__).resolve().parent))
from derive_stock_walk import read,NAMES,evaluated
SOURCE=ROOT/'art/derived/production/candidates/foal-walk-stifle';OUT=ROOT/'art/derived/production/candidates/foal-walk-hind-geometry'
def smooth(a,b,x):
 t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)
def main():
 OUT.mkdir(parents=True,exist_ok=True);bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'horse_foal_walk.blend'),use_scripts=False)
 body=bpy.data.objects['Body'];mesh=body.data;p=read(mesh.vertices,'co',3).reshape(-1,3);delta=np.zeros_like(p);weights=np.array([sum(g.weight for g in v.groups if body.vertex_groups[g.group].name.startswith(('Hind_','Walk_Knee_hind_','Trial_Stifle_'))) for v in mesh.vertices])
 # Bring the visible upper-limb mass toward the already-independent stifle.
 # Pelvis/topline, front half, lower cannon and all hoof landmarks fade to zero.
 mask=(1-smooth(-.34,-.24,p[:,0]))*smooth(.50,.66,p[:,2])*(1-smooth(.76,.89,p[:,2]))*np.minimum(1,weights*2)
 delta[:,0]=.050*mask
 # Local rounding at the inherited squared hock. One bounded Laplacian pass,
 # with no whole-body smoothing and no influence below z=.40.
 ns=[[] for _ in p]
 for e in mesh.edges:
  a,b=e.vertices;ns[a].append(b);ns[b].append(a)
 mean=np.array([p[v].mean(axis=0) if len(v) else p[i] for i,v in enumerate(ns)])
 hock=(1-smooth(-.56,-.46,p[:,0]))*smooth(.40,.47,p[:,2])*(1-smooth(.55,.63,p[:,2]))*np.minimum(1,weights*2)
 delta+=.35*hock[:,None]*(mean-p)
 delta[:,0]+=.012*hock
 # Explicit hard envelope: all four morphology targets receive exactly the same
 # local offset, retaining their relative displacement field and coat coordinates.
 allowed=(p[:,0]<-.24)&(p[:,2]>.40)&(p[:,2]<.91);delta[~allowed]=0
 assert np.max(np.linalg.norm(delta,axis=1))<=.09
 oldkeys={k.name:read(k.data,'co',3).reshape(-1,3).copy() for k in mesh.shape_keys.key_blocks}
 for k in mesh.shape_keys.key_blocks:k.data.foreach_set('co',(oldkeys[k.name]+delta).reshape(-1))
 mesh.vertices.foreach_set('co',(p+delta).reshape(-1));mesh.update()
 # The source has packed custom corner normals. Zero only the affected local
 # smoothing support so Blender derives those normals from the new geometry;
 # packed normal data everywhere else is untouched.
 changed_ids=set(np.where(np.any(delta!=0,axis=1))[0].tolist());support=set(changed_ids)
 for face in mesh.polygons:
  if any(v in changed_ids for v in face.vertices):support.update(face.vertices)
 custom=mesh.attributes.get('custom_normal')
 if custom:
  for loop in mesh.loops:
   if loop.vertex_index in support:custom.data[loop.index].value=(0,0)
 mesh.update();bpy.context.view_layer.update()
 scene=bpy.context.scene;scene.frame_set(0);bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'horse_foal_walk.blend'),compress=True)
 bpy.ops.object.select_all(action='DESELECT');rig=bpy.data.objects['Skeleton']
 for o in [rig,*[bpy.data.objects[n] for n in NAMES]]:o.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.gltf(filepath=str(OUT/'horse_foal_walk.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIVE_ACTIONS',export_nla_strips_merged_animation_name='Walk',export_force_sampling=True,export_frame_range=True,export_frame_step=1,export_skins=True,export_all_influences=True,export_morph=True,export_morph_normal=True,export_morph_tangent=False,export_materials='EXPORT',export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,export_extras=False,export_yup=True)
 sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest();profile=json.load(open(SOURCE/'walk-motion-profile.json'));profile.update(candidateBlendSha256=sha(OUT/'horse_foal_walk.blend'),sourceGlbSha256=sha(OUT/'horse_foal_walk.glb'));(OUT/'walk-motion-profile.json').write_text(json.dumps(profile,indent=2)+'\n')
 changed=np.where(np.any(delta!=0,axis=1))[0];report={'schema':'horse.foal_walk.geometry_construction.v1','sourceBlendSha256':sha(SOURCE/'horse_foal_walk.blend'),'candidateBlendSha256':profile['candidateBlendSha256'],'candidateGlbSha256':profile['sourceGlbSha256'],'protectedMeshDataExact':False,'changeScope':'Owner-approved Body upper-hind and hock geometry only; independent region audit required.','changedVertexCount':len(changed),'maximumOffsetM':float(np.linalg.norm(delta,axis=1).max()),'changedVertices':[{'vertex':int(i),'before':p[i].tolist(),'offset':delta[i].tolist()} for i in changed]};(OUT/'geometry-construction.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items() if k!='changedVertices'}))
if __name__=='__main__':main()
