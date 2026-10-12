"""Compare accepted front/hoof motion after the authorized hind-only correction.
Run from the repository root using Blender; does not modify either source.
"""
import bpy,numpy as np,json,hashlib,sys,argparse
from pathlib import Path
from mathutils import Vector
parser=argparse.ArgumentParser();parser.add_argument('--out',type=Path,default=Path('art/derived/production/candidates/foal-walk-hind-geometry/motion-preservation-report.json'));args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []);args.out.parent.mkdir(parents=True,exist_ok=True)
R=Path.cwd();sys.path.insert(0,str(R/'tools/horse'));from derive_stock_walk import evaluated,fingerprint,NAMES
old=R/'art/derived/production/candidates/foal-walk-refined/horse_foal_walk.blend';new=R/'art/derived/production/candidates/foal-walk-hind-geometry/horse_foal_walk.blend';result={};base=[]
profile=json.load(open(old.parent/'walk-motion-profile.json'));sourcefootids={k:[p['sourceVertex'] for p in v['solePoints']] for k,v in profile['limbs'].items()}
for index,path in enumerate([old,new]):
 bpy.ops.wm.open_mainfile(filepath=str(path),use_scripts=False);rig=bpy.data.objects['Skeleton'];body=bpy.data.objects['Body'];s=bpy.context.scene
 fp={n:fingerprint(bpy.data.objects[n]) for n in NAMES}
 if index==0:before=fp;frontids=[v.index for v in body.data.vertices if v.co.x>=0];result={'originalBlendSha256':hashlib.sha256(old.read_bytes()).hexdigest(),'candidateBlendSha256':hashlib.sha256(new.read_bytes()).hexdigest(),'sampleCount':117,'frontBodyVertexCount':len(frontids),'maximumFrontVertexDifferenceM':0.,'maximumHoofWitnessDifferenceM':0.,'maximumUnchangedComponentDifferenceM':0.,'maximumJointCoincidenceErrorM':0.,'maximumStifleRelativeRotationDegrees':0.}
 else:result['protectedMeshDataExact']=fp==before  # Expected false for the authorized local geometry branch.
 for i in range(117):
  f=58*i/116;s.frame_set(int(f),subframe=f%1);p=evaluated()
  if index==0:base.append(p);continue
  result['maximumFrontVertexDifferenceM']=max(result['maximumFrontVertexDifferenceM'],float(np.max(np.linalg.norm(p['Body'][frontids]-base[i]['Body'][frontids],axis=1))))
  for ids in sourcefootids.values():result['maximumHoofWitnessDifferenceM']=max(result['maximumHoofWitnessDifferenceM'],float(np.max(np.linalg.norm(p['Body'][ids]-base[i]['Body'][ids],axis=1))))
  for n in ['Eyes','Mane','Tail']:result['maximumUnchangedComponentDifferenceM']=max(result['maximumUnchangedComponentDifferenceM'],float(np.max(np.linalg.norm(p[n]-base[i][n],axis=1))))
  for side in ['L','R']:
   sign=1 if side=='L' else -1;delta=lambda n:rig.pose.bones[n].matrix@rig.data.bones[n].matrix_local.inverted();upper=delta('Hind_'+side+'_Upper');gaskin=delta('Hind_'+side+'_Gaskin');lower=delta('Hind_'+side+'_Hock');stifle=Vector((-.38,sign*.09,.68));hock=Vector((-.575,sign*.09,.52))
   result['maximumJointCoincidenceErrorM']=max(result['maximumJointCoincidenceErrorM'],(upper@stifle-gaskin@stifle).length,(gaskin@hock-lower@hock).length)
   result['maximumStifleRelativeRotationDegrees']=max(result['maximumStifleRelativeRotationDegrees'],float(np.degrees(upper.to_quaternion().rotation_difference(gaskin.to_quaternion()).angle)))
result['comparisonToleranceM']=.0001
result['comparisonToleranceReason']='0.1 mm absolute bound for measured non-hind posed-surface differences; 50 times tighter than the unchanged 5 mm contact gate. Non-hind animation curves are checked separately for exact equality. The cause of these tiny surface differences is not established; actual errors are retained, not treated as exact equality.'
result['failures']=[]
for k in ['maximumFrontVertexDifferenceM','maximumHoofWitnessDifferenceM','maximumUnchangedComponentDifferenceM','maximumJointCoincidenceErrorM']:
 if result[k]>.0001:result['failures'].append(k)
# Rest geometry is validated independently by audit_foal_geometry_region.py.
if result['protectedMeshDataExact']:result['failures'].append('bounded geometry edit was not disclosed')
args.out.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))

import bpy,json
from pathlib import Path
paths=['art/derived/production/candidates/foal-walk-refined/horse_foal_walk.blend','art/derived/production/candidates/foal-walk-hind-geometry/horse_foal_walk.blend'];data=[]
for path in paths:
 bpy.ops.wm.open_mainfile(filepath=path,use_scripts=False);r=bpy.data.objects['Skeleton'];curves={}
 for layer in r.animation_data.action.layers:
  for strip in layer.strips:
   for slot in r.animation_data.action.slots:
    bag=strip.channelbag(slot)
    if bag:
     for fc in bag.fcurves:
      n=fc.data_path
      if any(x in n for x in ['Hind_','Walk_Knee_hind_','Trial_Stifle_','FoalSmart_Bone_014','FoalSmart_Bone_017']):continue
      curves[n+'|'+str(fc.array_index)]=[(tuple(k.co),k.interpolation) for k in fc.keyframe_points]
 data.append(curves)
p=args.out;d=json.loads(p.read_text());d['unchangedNonHindAnimationCurveCount']=len(data[0]);d['nonHindAnimationCurvesExact']=data[0]==data[1]
if not d['nonHindAnimationCurvesExact']:d['failures'].append('non-hind animation curves changed')
p.write_text(json.dumps(d,indent=2)+'\n');print(d['nonHindAnimationCurvesExact'],len(data[0]))

d["schema"]="horse.foal_walk.motion_preservation.v1"
d["validatorSha256"]=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
p.write_text(json.dumps(d,indent=2)+"\n")
if d["failures"]:raise RuntimeError(str(d["failures"]))
