"""GLB normalization and CPU base-color review renderer. No Blender or network.
Usage: python3 prepare_portable.py BATCH_DIR [asset_id ...]
The GLB binary payload is preserved exactly; a root transform sets metres/origin.
Previews use base color and smooth directional lighting, not full PBR shading.
"""
import io, json, struct, sys, hashlib
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

def load_glb(path):
    raw=path.read_bytes()
    magic,version,length=struct.unpack_from('<III',raw)
    assert magic==0x46546c67 and version==2 and length==len(raw)
    n,kind=struct.unpack_from('<II',raw,12)
    assert kind==0x4e4f534a
    j=json.loads(raw[20:20+n]); off=20+n
    m,kind=struct.unpack_from('<II',raw,off)
    assert kind==0x004e4942
    return j,raw[off+8:off+8+m]

def accessor(j,b,i):
    a=j['accessors'][i]; v=j['bufferViews'][a['bufferView']]
    assert not a.get('sparse') and v.get('buffer',0)==0
    dtype={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1',5122:'<i2',5120:'i1'}[a['componentType']]
    w={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
    size=np.dtype(dtype).itemsize
    x=np.ndarray((a['count'],w),dtype=dtype,buffer=b,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',w*size),size)).copy()
    if a.get('normalized') and a['componentType']!=5126:
        x=x.astype(float)/np.iinfo(dtype).max
    return x

def matrix(n):
    if 'matrix' in n: return np.array(n['matrix']).reshape(4,4).T
    x,y,z,w=n.get('rotation',[0,0,0,1])
    r=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],
                [2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],
                [2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
    m=np.eye(4);m[:3,:3]=r@np.diag(n.get('scale',[1,1,1]));m[:3,3]=n.get('translation',[0,0,0]);return m

def primitives(j,b):
    out=[]; image_cache={}
    def visit(idx,parent):
        n=j['nodes'][idx]; world=parent@matrix(n)
        if 'mesh' in n:
            for p in j['meshes'][n['mesh']]['primitives']:
                assert p.get('mode',4)==4
                at=p['attributes'];v=accessor(j,b,at['POSITION']);v=(np.c_[v,np.ones(len(v))]@world.T)[:,:3]
                f=accessor(j,b,p['indices']).reshape(-1,3) if 'indices' in p else np.arange(len(v)).reshape(-1,3)
                assert f.max()<len(v) and np.isfinite(v).all()
                uv=accessor(j,b,at['TEXCOORD_0']);norm=accessor(j,b,at['NORMAL'])@np.linalg.inv(world[:3,:3])
                norm/=np.maximum(np.linalg.norm(norm,axis=1,keepdims=True),1e-12)
                mat=j['materials'][p['material']]['pbrMetallicRoughness']; tex=j['textures'][mat['baseColorTexture']['index']]['source']
                if tex not in image_cache:
                    iv=j['bufferViews'][j['images'][tex]['bufferView']];start=iv.get('byteOffset',0)
                    image_cache[tex]=np.array(Image.open(io.BytesIO(b[start:start+iv['byteLength']])).convert('RGB'))
                out.append({'v':v,'f':f,'uv':uv,'n':norm,'tex':image_cache[tex],'factor':np.array(mat.get('baseColorFactor',[1,1,1,1])[:3])})
        for c in n.get('children',[]):visit(c,world)
    for idx in j['scenes'][j.get('scene',0)]['nodes']:visit(idx,np.eye(4))
    return out

def render(parts,path,back=False):
    size=640; canvas=np.empty((size,size,3),np.uint8);canvas[:]=[222,229,220]
    depth=np.full((size,size),-np.inf)
    direction=np.array([-1.,.65,-2.] if back else [1.,.65,2.]);direction/=np.linalg.norm(direction)
    right=np.cross([0,1,0],direction);right/=np.linalg.norm(right);up=np.cross(direction,right)
    camera=np.array([right,up,direction]).T
    points=np.vstack([p['v']@camera for p in parts]);lo=points.min(0);hi=points.max(0);center=(lo+hi)/2
    scale=(size-80)/max((hi-lo)[:2])
    light=np.array([-.3,.8,.55]);light/=np.linalg.norm(light)
    for p in parts:
        q=(p['v']@camera-center)*scale
        q[:,0]+=size/2;q[:,1]=size/2-q[:,1]
        tex=p['tex'];th,tw=tex.shape[:2]
        for face in p['f']:
            t=q[face];mn=np.maximum(np.floor(t[:,:2].min(0)).astype(int),0);mx=np.minimum(np.ceil(t[:,:2].max(0)).astype(int),size-1)
            if (mx<mn).any():continue
            x0,y0=t[0,:2];x1,y1=t[1,:2];x2,y2=t[2,:2]
            den=(y1-y2)*(x0-x2)+(x2-x1)*(y0-y2)
            if abs(den)<1e-9:continue
            yy,xx=np.mgrid[mn[1]:mx[1]+1,mn[0]:mx[0]+1];xx=xx+.5;yy=yy+.5
            w0=((y1-y2)*(xx-x2)+(x2-x1)*(yy-y2))/den
            w1=((y2-y0)*(xx-x2)+(x0-x2)*(yy-y2))/den;w2=1-w0-w1
            z=w0*t[0,2]+w1*t[1,2]+w2*t[2,2]
            dst=depth[mn[1]:mx[1]+1,mn[0]:mx[0]+1]
            mask=(w0>=-1e-5)&(w1>=-1e-5)&(w2>=-1e-5)&(z>dst)
            if not mask.any():continue
            weights=np.stack([w0[mask],w1[mask],w2[mask]],1)
            uv=weights@p['uv'][face];norm=weights@p['n'][face];norm/=np.maximum(np.linalg.norm(norm,axis=1,keepdims=True),1e-9)
            shade=.67+.33*np.maximum(0,norm@light)
            tx=np.clip((uv[:,0]*tw).astype(int),0,tw-1);ty=np.clip((uv[:,1]*th).astype(int),0,th-1)
            color=np.clip(tex[ty,tx]*p['factor']*shade[:,None],0,255).astype(np.uint8)
            view=canvas[mn[1]:mx[1]+1,mn[0]:mx[0]+1];view[mask]=color;dst[mask]=z[mask]
    Image.fromarray(canvas).save(path)

def main():
    root=Path(sys.argv[1]).resolve();selected=set(sys.argv[2:])
    for sub in ['models','previews','reports']:(root/sub).mkdir(exist_ok=True)
    for a in json.loads((root/'batch.json').read_text())['assets']:
        key=a['id']
        if selected and key not in selected:continue
        src=root/'source'/(key+'.glb')
        if not src.exists():continue
        j,b=load_glb(src); original_binary=b
        if key=='log_jump':
            # The generated log floated above supports placed outside its ends.
            # The source has one identity-transformed triangle mesh; preserve UVs,
            # normals, materials and indices, moving only the separated parts.
            assert len(j['meshes'])==1 and len(j['meshes'][0]['primitives'])==1
            assert np.allclose(matrix(j['nodes'][0]),np.eye(4))
            p=j['meshes'][0]['primitives'][0]
            aid=p['attributes']['POSITION'];ac=j['accessors'][aid]
            assert ac['componentType']==5126 and ac['type']=='VEC3'
            v=accessor(j,b,aid);top=v[:,1]>0;left=(~top)&(v[:,0]<0);right=(~top)&(v[:,0]>0)
            assert top.any() and left.any() and right.any()
            v[top,1]-=.070;v[left,0]+=.160;v[right,0]-=.160
            bv=j['bufferViews'][ac['bufferView']];offset=bv.get('byteOffset',0)+ac.get('byteOffset',0)
            stride=bv.get('byteStride',12);edited=bytearray(b)
            for k,row in enumerate(v):struct.pack_into('<fff',edited,offset+k*stride,*row)
            b=bytes(edited);ac['min']=v.min(0).tolist();ac['max']=v.max(0).tolist()
        parts=primitives(j,b)
        vv=np.vstack([p['v'] for p in parts]);lo=vv.min(0);hi=vv.max(0)
        assert hi[1]>lo[1]
        scale=a['height']/(hi[1]-lo[1]);shift=-(lo+hi)/2;shift[1]=-lo[1]
        for p in parts:p['v']=(p['v']+shift)*scale
        scene=j['scenes'][j.get('scene',0)];old=scene['nodes'][:];scene['nodes']=[len(j['nodes'])]
        j['nodes'].append({'name':key,'children':old,'scale':[scale]*3,'translation':(shift*scale).tolist()})
        meta=json.dumps(j,separators=(',',':')).encode();meta+=b' '*((-len(meta))%4)
        payload=struct.pack('<III',0x46546c67,2,28+len(meta)+len(b))+struct.pack('<II',len(meta),0x4e4f534a)+meta+struct.pack('<II',len(b),0x004e4942)+b
        out=root/'models'/(key+'.glb');out.write_bytes(payload)
        j2,b2=load_glb(out);assert b2==b
        normalized=primitives(j2,b2);new=np.vstack([p['v'] for p in normalized]);dims=new.max(0)-new.min(0)
        assert abs(new[:,1].min())<1e-6 and abs(dims[1]-a['height'])<1e-6
        report={'id':key,'name':a['name'],'triangles':sum(len(p['f']) for p in parts),'requested_triangles':a['poly'],
                'dimensions_godot_xyz_m':dims.tolist(),'ground_error_m':abs(float(new[:,1].min())),
                'has_uv':True,'embedded_base_color_sizes':[list(p['tex'].shape[:2]) for p in parts],
                'binary_payload_preserved':b==original_binary,'source_sha256':hashlib.sha256(src.read_bytes()).hexdigest(),
                'model_sha256':hashlib.sha256(payload).hexdigest(),'geometry_change':('log lowered and supports moved inward; root uniform scale and translation' if key=='log_jump' else 'root uniform scale and translation only'),
                'preview_method':'CPU base-color rasterization with smooth directional lighting; normal/roughness maps not rendered'}
        (root/'reports'/(key+'.json')).write_text(json.dumps(report,indent=2))
        render(normalized,root/'previews'/(key+'_front.png'))
        render(normalized,root/'previews'/(key+'_back.png'),True)
        print('READY',key,report['triangles'],flush=True)

if __name__=='__main__':main()
