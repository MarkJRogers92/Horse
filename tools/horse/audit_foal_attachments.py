"""Read-only neutral foal hair/body attachment audit; no source changes."""
import bpy,json,sys,hashlib,math,numpy as np
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/horse'))
from derive_stock_walk import evaluated
from production_motion import _barycentric
out=Path(sys.argv[sys.argv.index('--')+1]).resolve() if '--' in sys.argv else root/'art/derived/production/candidates/foal-walk';bpy.ops.wm.open_mainfile(filepath=str(out/'horse_foal_walk.blend'));body=bpy.data.objects['Body'];body.data.calc_loop_triangles();tri=[t.vertices[:] for t in body.data.loop_triangles];coords=[v.co.copy() for v in body.data.vertices];tree=BVHTree.FromPolygons(coords,tri,all_triangles=True);w={}
for name in ['Mane','Tail']:
 pairs=[]
 for v in bpy.data.objects[name].data.vertices:
  p,normal,i,d=tree.find_nearest(v.co)
  if d>.012:continue
  ids=tri[i];bary=_barycentric(p,*[coords[j] for j in ids]);pairs.append((v.index,ids,bary,np.array(v.co)-np.array(p)))
 w[name]=pairs
report={n:{'witnessCount':len(p),'maxRestGapM':max(float(np.linalg.norm(gap)) for _,_,_,gap in p),'maxGapVectorDriftM':0.,'maxPosedGapM':0.} for n,p in w.items()}
for f in np.linspace(bpy.context.scene.frame_start,bpy.context.scene.frame_end,61):
 bpy.context.scene.frame_set(int(f),subframe=float(f)%1);p=evaluated()
 for name,pairs in w.items():
  for idx,ids,bary,gap in pairs:
   surface=np.sum(p['Body'][list(ids)]*np.array(bary)[:,None],axis=0);posedgap=p[name][idx]-surface
   report[name]['maxGapVectorDriftM']=max(report[name]['maxGapVectorDriftM'],float(np.linalg.norm(posedgap-gap)));report[name]['maxPosedGapM']=max(report[name]['maxPosedGapM'],float(np.linalg.norm(posedgap)))
r={'candidateBlendSha256':hashlib.sha256((out/'horse_foal_walk.blend').read_bytes()).hexdigest(),'samples':61,'method':'All hair vertices within 12 mm of rest Body surface; fixed rest-triangle barycentric witnesses. Rest offsets are source geometry and not a new seam opening.','attachments':report};(out/'attachment-audit.json').write_text(json.dumps(r,indent=2)+'\n');print(json.dumps(r))
