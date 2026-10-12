"""Read-only corrected foal Idle deformation, contact, joins and morph audit."""
import bpy,numpy as np,json,hashlib,sys,itertools,math
from pathlib import Path
from mathutils.bvhtree import BVHTree
R=Path(__file__).resolve().parents[2];sys.path.insert(0,str(R/'tools/horse'))
from derive_stock_walk import read,evaluated,NAMES
from production_motion import _barycentric
P=R/'art/derived/production/candidates/foal-idle-hind-geometry/horse_foal.blend';OUT=P.parent/'idle-morph-audit.json'
bpy.ops.wm.open_mainfile(filepath=str(P),use_scripts=False);scene=bpy.context.scene;rig=bpy.data.objects['Skeleton'];keys=['body_length','chest_width','neck_length','muscle']
body=bpy.data.objects['Body'];body.data.calc_loop_triangles();tri=[tuple(t.vertices) for t in body.data.loop_triangles];coords=[v.co.copy() for v in body.data.vertices];tree=BVHTree.FromPolygons(coords,tri,all_triangles=True)
witnesses={}
for name in ['Mane','Tail']:
 rows=[]
 for v in bpy.data.objects[name].data.vertices:
  p,n,i,d=tree.find_nearest(v.co)
  if d<=.012:rows.append((v.index,tri[i],_barycentric(p,*[coords[j] for j in tri[i]])))
 witnesses[name]=rows
profile=json.load(open(R/'art/derived/production/candidates/foal-walk-hind-geometry/walk-motion-profile.json'));soles={n:[p['sourceVertex'] for p in l['solePoints']] for n,l in profile['limbs'].items()}
base={n:read(bpy.data.objects[n].data.vertices,'co',3).reshape(-1,3) for n in NAMES};deltas={n:{k:read(bpy.data.objects[n].data.shape_keys.key_blocks[k].data,'co',3).reshape(-1,3)-base[n] for k in keys} for n in NAMES}
edgeids={n:np.array([tuple(e.vertices) for e in bpy.data.objects[n].data.edges]) for n in NAMES}
cases=[dict.fromkeys(keys,0.)]
for key in keys:
 for value in [-1.,1.]:cases.append(dict(cases[0],**{key:value}))
cases += [dict(zip(keys,values)) for values in itertools.product([-1.,1.],repeat=4)]
report={'schema':'horse.foal_idle.morph_deformation.v1','candidateBlendSha256':hashlib.sha256(P.read_bytes()).hexdigest(),'sampleCountPerCase':121,'caseCount':len(cases),'limits':{'maximumStretch':2.,'minimumSoleHeightM':-.002,'maximumSoleMinimumHeightM':.003,'maximumFootMotionM':.005,'maximumAttachmentDriftM':.002,'maximumLoopErrorM':.001},'cases':[],'failures':[]}
for values in cases:
 rest={n:base[n]+sum(deltas[n][k]*v for k,v in values.items()) for n in NAMES}
 edges={}
 for n in NAMES:
  for k,v in values.items():bpy.data.objects[n].data.shape_keys.key_blocks[k].value=v
  idx=edgeids[n];length=np.linalg.norm(rest[n][idx[:,0]]-rest[n][idx[:,1]],axis=1);used=length>1e-8;edges[n]=(idx[used],length[used])
 gaps={n:[rest[n][v]-np.sum(rest['Body'][list(ids)]*np.array(bary)[:,None],axis=0) for v,ids,bary in rows] for n,rows in witnesses.items()}
 row={'morphs':values,'maxEdgeStretch':dict.fromkeys(NAMES,0.),'minimumSoleHeightM':1.,'maximumSoleMinimumHeightM':-1.,'maximumFootMotionM':0.,'maxAttachmentDriftM':dict.fromkeys(witnesses,0.),'maximumLoopErrorM':0.,'allFinite':True}
 first=None
 for frame in np.linspace(scene.frame_start,scene.frame_end,121):
  scene.frame_set(int(frame),subframe=frame%1);p=evaluated()
  if first is None:first={n:v.copy() for n,v in p.items()}
  for n,(idx,length) in edges.items():
   row['allFinite'] &= bool(np.isfinite(p[n]).all());row['maxEdgeStretch'][n]=max(row['maxEdgeStretch'][n],float((np.linalg.norm(p[n][idx[:,0]]-p[n][idx[:,1]],axis=1)/length).max()))
  for ids in soles.values():
   z=p['Body'][ids,2];row['minimumSoleHeightM']=min(row['minimumSoleHeightM'],float(z.min()));row['maximumSoleMinimumHeightM']=max(row['maximumSoleMinimumHeightM'],float(z.min()));row['maximumFootMotionM']=max(row['maximumFootMotionM'],float(np.abs(p['Body'][ids]-first['Body'][ids]).max()))
  for n,rows in witnesses.items():
   for j,(v,ids,bary) in enumerate(rows):
    gap=p[n][v]-np.sum(p['Body'][list(ids)]*np.array(bary)[:,None],axis=0);row['maxAttachmentDriftM'][n]=max(row['maxAttachmentDriftM'][n],float(np.linalg.norm(gap-gaps[n][j])))
 row['maximumLoopErrorM']=max(float(np.linalg.norm(p[n]-first[n],axis=1).max()) for n in NAMES)
 bad=(not row['allFinite'] or max(row['maxEdgeStretch'].values())>2 or row['minimumSoleHeightM']<-.002 or row['maximumSoleMinimumHeightM']>.003 or row['maximumFootMotionM']>.005 or max(row['maxAttachmentDriftM'].values())>.002 or row['maximumLoopErrorM']>.001)
 if bad:report['failures'].append({'case':len(report['cases']),'metrics':row})
 report['cases'].append(row)
report['attachmentWitnessCounts']={n:len(v) for n,v in witnesses.items()};report['pass']=not report['failures'];OUT.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items() if k!='cases'}));
if report['failures']:raise RuntimeError('Idle morph audit failed')
