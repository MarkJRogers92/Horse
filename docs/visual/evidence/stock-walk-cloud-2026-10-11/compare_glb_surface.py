import json,struct,sys,numpy as np
from pathlib import Path

def read(path):
 b=Path(path).read_bytes(); n=struct.unpack_from('<I',b,12)[0]; d=json.loads(b[20:20+n]); binary=b[28+n:]
 def raw(view,dtype,count,width,offset=0):
  v=d['bufferViews'][view]; dt=np.dtype(dtype); start=v.get('byteOffset',0)+offset
  return np.ndarray((count,width),dtype=dt,buffer=binary,offset=start,strides=(v.get('byteStride',dt.itemsize*width),dt.itemsize)).copy()
 def accessor(i):
  a=d['accessors'][i]; width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']];dt={5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}[a['componentType']]
  v=raw(a['bufferView'],dt,a['count'],width,a.get('byteOffset',0)) if 'bufferView' in a else np.zeros((a['count'],width),dtype=dt)
  if 'sparse' in a:
   s=a['sparse'];ii=s['indices'];vv=s['values'];idx=raw(ii['bufferView'],{5121:'u1',5123:'<u2',5125:'<u4'}[ii['componentType']],s['count'],1,ii.get('byteOffset',0)).ravel();v[idx]=raw(vv['bufferView'],dt,s['count'],width,vv.get('byteOffset',0))
  if a.get('normalized'):
   info=np.iinfo(v.dtype);v=v.astype(float)/info.max;v=np.maximum(v,-1)
  return v
 out={}
 for m in d['meshes']:
  p=m['primitives'][0];idx=accessor(p['indices']).ravel();attrs={k:accessor(v)[idx] for k,v in p['attributes'].items() if not k.startswith(('JOINTS','WEIGHTS'))}
  for j,t in enumerate(p.get('targets',[])):
   for k,v in t.items():attrs[f'morph{j}.{k}']=accessor(v)[idx]
  out[next(n['name'] for n in d['nodes'] if n.get('mesh') == d['meshes'].index(m))]=attrs
 return out

a,b=read(sys.argv[1]),read(sys.argv[2]);report={'source':sys.argv[1],'candidate':sys.argv[2],'components':{},'failures':[]}
for name,attrs in a.items():
 got=b[name]; r={};report['components'][name]=r
 # Sort corners by stable position+primary UV to tolerate exporter index splitting/order.
 def order(x):
  key=np.concatenate([x[k] for k in ['POSITION','TEXCOORD_0'] if k in x],axis=1).round(5)
  return np.lexsort(key[:,::-1].T[::-1])
 ia,ib=order(attrs),order(got)
 for k,arr in attrs.items():
  if k not in got:r[k]={'missing':True};report['failures'].append(name+'.'+k);continue
  v=got[k]
  if arr.shape!=v.shape:r[k]={'shapeMismatch':[arr.shape,v.shape]};report['failures'].append(name+'.'+k);continue
  err=float(np.max(np.abs(arr[ia].astype(float)-v[ib].astype(float))))
  r[k]={'maximumError':err,'cornerCount':len(arr)}
  if err>2e-5:report['failures'].append(name+'.'+k)
print(json.dumps(report,indent=2));sys.exit(bool(report['failures']))
