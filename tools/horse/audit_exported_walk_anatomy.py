"""Independent anatomical gate for actual exported Walk poses; does not mutate assets."""
import struct,json,numpy as np,math
from pathlib import Path

import argparse,hashlib
p=argparse.ArgumentParser();p.add_argument('glb',type=Path);p.add_argument('profile',type=Path);p.add_argument('--out',type=Path,required=True);args=p.parse_args();profile=json.loads(args.profile.read_text());files={args.glb.stem:args.glb};result={}
def quat(q):
 x,y,z,w=q;return np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],[2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],[2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])
def slerp(a,b,t):
 dot=np.dot(a,b)
 if dot<0:b=-b;dot=-dot
 if dot>.9995:q=a*(1-t)+b*t;return q/np.linalg.norm(q)
 th=math.acos(np.clip(dot,-1,1));return (a*math.sin((1-t)*th)+b*math.sin(t*th))/math.sin(th)
for label,path in files.items():
 raw=path.read_bytes();n=struct.unpack_from('<I',raw,12)[0];d=json.loads(raw[20:20+n]);blob=raw[28+n:];nodes=d['nodes'];names={n.get('name'):i for i,n in enumerate(nodes)}
 def accessor(i):
  a=d['accessors'][i];v=d['bufferViews'][a['bufferView']];count={'SCALAR':1,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];dtype={5126:np.float32,5123:np.uint16,5125:np.uint32}[a['componentType']];return np.frombuffer(blob,dtype=dtype,count=a['count']*count,offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(-1,count)
 act=next(a for a in d['animations'] if a['name']=='Walk');tracks=[]
 for ch in act['channels']:
  s=act['samplers'][ch['sampler']];tracks.append((ch['target']['node'],ch['target']['path'],accessor(s['input']).ravel(),accessor(s['output']),s.get('interpolation','LINEAR')))
 duration=max(t[2][-1] for t in tracks);phases=[]
 for phase in np.linspace(0,1,117):
  vals={i:{k:np.array(v,float) for k,v in n.items() if k in ('translation','rotation','scale')} for i,n in enumerate(nodes)}
  for ni,prop,times,values,interp in tracks:
   if prop=='weights':continue
   t=phase*duration;j=np.searchsorted(times,t,side='right')-1;j=max(0,min(j,len(times)-2));f=np.clip((t-times[j])/(times[j+1]-times[j]),0,1)
   assert interp in ('LINEAR','STEP'), 'unsupported animation interpolation: '+interp
   vals[ni][prop]=values[min(np.searchsorted(times,t,side='right')-1,len(times)-1)] if interp=='STEP' else slerp(values[j],values[j+1],f) if prop=='rotation' else values[j]*(1-f)+values[j+1]*f
  glob={}
  def walk(i,parent):
   if 'matrix' in nodes[i]:local=np.array(nodes[i]['matrix']).reshape(4,4).T
   else:
    v=vals[i];local=np.eye(4);local[:3,:3]=quat(v.get('rotation',[0,0,0,1]))@np.diag(v.get('scale',[1,1,1]));local[:3,3]=v.get('translation',[0,0,0])
   glob[i]=parent@local
   for ch in nodes[i].get('children',[]):walk(ch,glob[i])
  for i in d['scenes'][d.get('scene',0)]['nodes']:walk(i,np.eye(4))
  def pos(name):
   p=glob[names[name]][:3,3];return p[[2,0,1]] if label=='native_reference' else p[[0,2,1]]*np.array([1,-1,1])
  joints={}
  bind={}
  for skin in d.get('skins',[]):
   for node,mat in zip(skin['joints'],accessor(skin['inverseBindMatrices'])):bind[node]=mat.reshape(4,4).T
  for side in ('l','r'):
   is_foal='foal' in label
   jointnames=([f'Walk_Elbow_fore_{side}',f'Walk_Knee_fore_{side}',f'Walk_Fetlock_fore_{side}'] if is_foal else [n+side for n in ('front_leg_upper_','front_leg_lower_','front_leg_ankle_')])
   if is_foal:
    sign=1 if side=='l' else -1
    def skin_anchor(bone,anchor):
     point=(glob[names[bone]]@bind[names[bone]]@np.array(anchor))[:3]
     return point[[0,2,1]]*np.array([1,-1,1])
    A,B,C=[skin_anchor(f'Front_{side.upper()}_'+bone,anchor) for bone,anchor in [('Upper',[.19,.70,-sign*.082,1]),('Cannon',[.19,.40,-sign*.082,1]),('Hoof_02',[.184,.15,-sign*.099,1])]]
   else:A,B,C=[pos(n) for n in jointnames]
   v=C-A;normal=np.array([-v[2],0,v[0]]);normal/=np.linalg.norm(normal);u=B-A;w=C-B
   foot=f'Front_{side.upper()}_Hoof_02' if is_foal else 'front_leg_ankle_'+side
   delta=glob[names[foot]]@bind[names[foot]];fwd=delta[:3,:3]@np.array([1.,0,0]);pitch=float(np.degrees(np.arctan2(fwd[1],fwd[0])))
   limbphase=(float(phase)-profile['limbs']['fore_'+side]['stanceStartFrame']/profile['periodFrames'])%1
   joints[side]={'limbPhase':limbphase,'carpusOffsetM':float(np.dot(B-A,normal)),'flexionDegrees':float(np.degrees(np.arccos(np.clip(np.dot(u,w)/(np.linalg.norm(u)*np.linalg.norm(w)),-1,1)))),'hoofPitchDegrees':pitch,'points':[A.tolist(),B.tolist(),C.tolist()]}
  phases.append(dict(phase=float(phase),joints=joints))
 summary={};failures=[]
 for side in ('l','r'):
  rows=[p['joints'][side] for p in phases];stance=[r for r in rows if r['limbPhase']<.6];swing=[r for r in rows if r['limbPhase']>=.6]
  metrics={'minSignedCarpusOffsetM':min(r['carpusOffsetM'] for r in rows),'maxStanceFlexionDegrees':max(r['flexionDegrees'] for r in stance),'maxSwingFlexionDegrees':max(r['flexionDegrees'] for r in swing),'hoofPitchRangeDegrees':max(r['hoofPitchDegrees'] for r in rows)-min(r['hoofPitchDegrees'] for r in rows),'stanceHoofPitchRangeDegrees':max(r['hoofPitchDegrees'] for r in stance)-min(r['hoofPitchDegrees'] for r in stance)}
  if metrics['minSignedCarpusOffsetM']<-.002:failures.append(side+': backward carpus')
  if metrics['maxStanceFlexionDegrees']>12:failures.append(side+': crouched supporting foreleg')
  if metrics['maxSwingFlexionDegrees']<20:failures.append(side+': insufficient swing folding')
  if metrics['hoofPitchRangeDegrees']<20:failures.append(side+': frozen hoof pitch')
  if metrics['stanceHoofPitchRangeDegrees']>.5:failures.append(side+': planted hoof rotation')
  summary['fore_'+side]=metrics
 result={'schema':'horse.walk.anatomy.v1','sourceGlbSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'motionProfileSha256':hashlib.sha256(args.profile.read_bytes()).hexdigest(),'sampleCount':117,'durationSeconds':float(duration),'method':'Independent glTF node hierarchy and quaternion track evaluation; actual adult joint heads and inverse skin binds. Foal joints are measured from actual deform-bone skin matrices applied to the solver rest anchors, independently of interpolated helper markers. Positive carpal offset is anterior to the elbow-fetlock chord.','limits':{'minimumSignedCarpusOffsetM':-.002,'maximumStanceFlexionDegrees':12,'minimumSwingFlexionDegrees':20,'minimumHoofPitchRangeDegrees':20,'maximumStanceHoofPitchRangeDegrees':.5},'limbs':summary,'failures':failures,'phases':phases}
 args.out.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(summary,indent=2));print('FAILURES',failures)
 raise SystemExit(bool(failures))
