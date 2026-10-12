"""Fresh immutable foundation-to-candidate mesh and source-to-GLB corner proof."""
import bpy,sys,json,hashlib,struct,argparse
import numpy as np
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(Path(__file__).resolve().parent));from derive_stock_walk import read,NAMES
p=argparse.ArgumentParser();p.add_argument('candidate',type=Path);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.candidate=a.candidate.resolve();baseline=ROOT/'art/derived/production/horse_light.blend';glb=a.candidate.with_suffix('.glb')
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def digest(v):return hashlib.sha256(v if isinstance(v,bytes) else json.dumps(v,sort_keys=True,separators=(',',':')).encode()).hexdigest()
def snapshots():
 out={}
 for n in NAMES:
  o=bpy.data.objects[n];m=o.data
  out[n]={'positions':digest(read(m.vertices,'co',3).tobytes()),'topology':digest(read(m.loops,'vertex_index',1,np.int32).tobytes()),'transform':digest([list(r) for r in o.matrix_world]),'UV_layers_order':digest([u.name for u in m.uv_layers]),'UV_active_render':digest([u.active_render for u in m.uv_layers]),'frozen_UVs':digest({u.name:digest(read(u.data,'uv',2).tobytes()) for u in m.uv_layers}),'colors':digest({c.name:[c.domain,c.data_type,digest(read(c.data,'color',4).tobytes())] for c in m.color_attributes}),'morphs':digest({k.name:digest(read(k.data,'co',3).tobytes()) for k in m.shape_keys.key_blocks}),'UV0':digest(read(m.uv_layers[0].data,'uv',2).tobytes()),'weights':digest([[(o.vertex_groups[g.group].name,g.weight) for g in v.groups] for v in m.vertices])}
 return out
bpy.ops.wm.open_mainfile(filepath=str(baseline),use_scripts=False);before_set={o.name for o in bpy.data.objects if o.type=='MESH'};before=snapshots();bpy.ops.wm.open_mainfile(filepath=str(a.candidate),use_scripts=False);after_set={o.name for o in bpy.data.objects if o.type=='MESH'};after=snapshots();assert before_set==after_set==set(NAMES), 'mesh set changed';preserved=[k for k in before['Body'] if k!='weights'];forbidden=[f'{n}.{k}' for n in NAMES for k in preserved if before[n][k]!=after[n][k]]
raw=glb.read_bytes();length=struct.unpack_from('<I',raw,12)[0];d=json.loads(raw[20:20+length]);binary=raw[28+length:]
def acc(i):
 x=d['accessors'][i];width={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[x['type']];dt=np.dtype({5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}[x['componentType']])
 def buf(v,count,w,typ,off=0):
  vw=d['bufferViews'][v];return np.ndarray((count,w),dtype=typ,buffer=binary,offset=vw.get('byteOffset',0)+off,strides=(vw.get('byteStride',typ.itemsize*w),typ.itemsize)).copy()
 v=buf(x['bufferView'],x['count'],width,dt,x.get('byteOffset',0)) if 'bufferView' in x else np.zeros((x['count'],width),dtype=dt)
 if 'sparse' in x:
  sp=x['sparse'];ii=sp['indices'];vv=sp['values'];typ=np.dtype({5121:'u1',5123:'<u2',5125:'<u4'}[ii['componentType']]);ix=buf(ii['bufferView'],sp['count'],1,typ,ii.get('byteOffset',0)).ravel();v[ix]=buf(vv['bufferView'],sp['count'],width,dt,vv.get('byteOffset',0))
 if x.get('normalized'):v=v.astype(float)/np.iinfo(dt).max
 return v
assert {node['name'] for node in d['nodes'] if 'mesh' in node}==set(NAMES), 'export mesh set changed'
corner={'source_sha256':sha(a.candidate),'glb_sha256':sha(glb),'meshes':{},'pass':True,'mesh_set_matches':True};morph={'source_sha256':sha(a.candidate),'glb_sha256':sha(glb),'meshes':{},'pass':True,'mesh_set_matches':True}
for n in NAMES:
 o=bpy.data.objects[n];m=o.data;mi=next(node['mesh'] for node in d['nodes'] if node.get('name')==n);prim=d['meshes'][mi]['primitives'][0];indices=acc(prim['indices']).ravel();attrs={k:acc(v)[indices] for k,v in prim['attributes'].items()};m.calc_loop_triangles();tri_loops=np.array([i for t in m.loop_triangles for i in t.loops]);ids=read(m.loops,'vertex_index',1,np.int32)[tri_loops];xyz=read(m.vertices,'co',3).reshape(-1,3);src={'POSITION':xyz[ids][:,[0,2,1]]*np.array([1,1,-1])}
 for i,u in enumerate(m.uv_layers):
  uv=read(u.data,'uv',2).reshape(-1,2)[tri_loops];uv[:,1]=1-uv[:,1];src['TEXCOORD_'+str(i)]=uv
 if m.color_attributes.get('Color') is not None:
  assert 'COLOR_0' in attrs, (n,'export lost coat colors')
  color=m.color_attributes['Color'];src['COLOR_0']=read(color.data,'color',4).reshape(-1,4)[tri_loops]
 assert all(k in attrs for k in src), (n,'export lost protected attributes')
 # Match complete triangles using canonical corner position order, retaining
 # duplicate multiplicity. Match each corner only within its matched triangle.
 def permutation(a):
  key=a['POSITION'].astype(np.float64).reshape(-1,3,3);ordered=[]
  for ti,t in enumerate(key):
   perm=sorted(range(3),key=lambda i:tuple(np.round(t[i],5)));ordered.append((tuple(np.round(t[perm].ravel(),5)),[ti*3+i for i in perm]))
  ordered.sort(key=lambda v:v[0]);return np.array([i for _,v in ordered for i in v]),[k for k,_ in ordered]
 si,sk=permutation(src);ei,ek=permutation(attrs);match=sk==ek;assert match,(n,'triangle correspondence');assert len(set(sk))==len(sk),(n,'ambiguous coincident triangles')
 errors={k:float(np.max(abs(src[k][si]-attrs[k][ei]))) for k in src if k!='POSITION'};poserr=float(np.max(abs(src['POSITION'][si]-attrs['POSITION'][ei])));assert poserr<2e-6
 ok=all(v<=(.5/65535+1e-7 if k=='COLOR_0' else 2e-6) for k,v in errors.items());corner['meshes'][n]={'source_vertices':len(m.vertices),'export_vertices':d['accessors'][prim['attributes']['POSITION']]['count'],'source_triangles':len(ids)//3,'export_triangles':len(indices)//3,'unmatched_export_triangles':0,'unmatched_source_triangles':0,'ambiguous_matches':0,'max_absolute_attribute_error':errors,'pass':ok};corner['pass']&=ok
 names=[k.name for k in m.shape_keys.key_blocks][1:];deltaerrors={}
 for i,name in enumerate(names):
  delta=(read(m.shape_keys.key_blocks[name].data,'co',3).reshape(-1,3)-xyz)[ids][:,[0,2,1]]*np.array([1,1,-1]);got=acc(prim['targets'][i]['POSITION'])[indices];deltaerrors[name]=float(np.max(abs(delta[si]-got[ei])))
 ok=max(deltaerrors.values())<2e-6;morph['meshes'][n]={'target_names':names,'source_triangles':len(ids)//3,'export_triangles':len(indices)//3,'unmatched_export_triangles':0,'unmatched_source_triangles':0,'ambiguous_matches':0,'max_position_delta_error':deltaerrors,'pass':ok};morph['pass']&=ok
result={'schema':'horse_rig_only_geometry_uv_preservation.v1','baseline_blend_sha256':sha(baseline),'candidate_blend_sha256':sha(a.candidate),'glb_sha256':sha(glb),'mesh_set_matches':True,'preserved_fields':preserved,'forbidden_changes':forbidden,'baseline':before,'candidate':after,'export_corner_audit':corner,'export_morph_audit':morph,'pass':not forbidden and corner['pass'] and morph['pass'],'scope':'Fresh foundation/candidate snapshots and actual candidate Blend-to-GLB complete-triangle correspondence. Geometry, transforms, UV order/data, colors and all shape keys unchanged. Weights, rig and motion may differ. Does not certify gait or morph-extreme animation.','validator_sha256':sha(Path(__file__))};(a.candidate.parent/'geometry-uv-preservation.json').write_text(json.dumps(result,indent=2)+'\n');print('PRESERVATION',result['pass'],forbidden,corner['pass'],morph['pass']);assert result['pass']
