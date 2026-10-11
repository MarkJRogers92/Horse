import base64, json, struct, hashlib
from pathlib import Path
root=Path(__file__).resolve().parent
# Packed glTF buffer with one flat quad and 8 float32-weighted joints.
buf=bytearray(); views=[]; accessors=[]
def add(data, target=None):
    while len(buf)%4: buf.append(0)
    off=len(buf); buf.extend(data)
    v={'buffer':0,'byteOffset':off,'byteLength':len(data)}
    if target: v['target']=target
    views.append(v); return len(views)-1
def acc(view, ctype, count, typ, **kw):
    a={'bufferView':view,'componentType':ctype,'count':count,'type':typ}; a.update(kw); accessors.append(a); return len(accessors)-1
positions=[(-.5,-.5,0),(.5,-.5,0),(.5,.5,0),(-.5,.5,0)]
idx=add(b''.join(struct.pack('<3f',*p) for p in positions),34962); pos=acc(idx,5126,4,'VEC3',min=[-.5,-.5,0],max=[.5,.5,0])
idx=add(b''.join(struct.pack('<3f',0,0,1) for _ in positions),34962); nor=acc(idx,5126,4,'VEC3')
# Each vertex receives .125 from each of joints 0..7. The source count is 8 exactly.
j0=add(b''.join(struct.pack('<4H',0,1,2,3) for _ in positions),34962); joints0=acc(j0,5123,4,'VEC4')
j1=add(b''.join(struct.pack('<4H',4,5,6,7) for _ in positions),34962); joints1=acc(j1,5123,4,'VEC4')
w0=add(b''.join(struct.pack('<4f',.125,.125,.125,.125) for _ in positions),34962); weights0=acc(w0,5126,4,'VEC4')
w1=add(b''.join(struct.pack('<4f',.125,.125,.125,.125) for _ in positions),34962); weights1=acc(w1,5126,4,'VEC4')
indices=add(struct.pack('<6H',0,1,2,0,2,3),34963); inds=acc(indices,5123,6,'SCALAR')
ibm=add(b''.join(struct.pack('<16f',1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1) for _ in range(8))); ibma=acc(ibm,5126,8,'MAT4')
gltf={
 'asset':{'version':'2.0','generator':'bounded-eight-weight-renderer-probe'},
 'scene':0,'scenes':[{'nodes':[0]}],
 'nodes':[{'name':'Root','children':[1,2,3,4,5,6,7,8,9]}, {'name':'ProbeQuad','mesh':0,'skin':0}]+[{'name':f'Joint{i+1}'} for i in range(8)],
 'meshes':[{'name':'EightWeightQuad','primitives':[{'attributes':{'POSITION':pos,'NORMAL':nor,'JOINTS_0':joints0,'WEIGHTS_0':weights0,'JOINTS_1':joints1,'WEIGHTS_1':weights1},'indices':inds,'material':0}]}],
 'materials':[{'name':'UnshadedProbe','pbrMetallicRoughness':{'baseColorFactor':[0.05,0.8,0.2,1.0],'metallicFactor':0,'roughnessFactor':1},'doubleSided':True}],
 'skins':[{'name':'EightJointSkin','inverseBindMatrices':ibma,'skeleton':0,'joints':list(range(2,10))}],
 'accessors':accessors,'bufferViews':views,
 'buffers':[{'byteLength':len(buf),'uri':'data:application/octet-stream;base64,'+base64.b64encode(buf).decode()}]
}
(root/'eight_weight_probe.gltf').write_text(json.dumps(gltf,separators=(',',':'))+'\n')
print('gltf_sha256',hashlib.sha256((root/'eight_weight_probe.gltf').read_bytes()).hexdigest())
print('buffer_bytes',len(buf),'weights_per_vertex',8,'joint_slots',list(range(8)))
