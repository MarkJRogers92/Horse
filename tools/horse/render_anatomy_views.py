"""Read-only side/front/quarter review of the complete neutral Walk loop."""
import bpy,sys,math,json,hashlib,argparse
from pathlib import Path
from mathutils import Vector
p=argparse.ArgumentParser();p.add_argument('blend',type=Path);p.add_argument('out',type=Path);p.add_argument('--frames',type=int,default=30);p.add_argument('--views',default='side,front,quarter');a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(a.blend.resolve()),use_scripts=False);s=bpy.context.scene;period=s.frame_end-s.frame_start;foal='foal' in a.blend.name
for o in list(s.objects):
 if o.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(o,do_unlink=True)
for name in ('Body','Eyes','Mane','Tail'):
 o=bpy.data.objects[name];m=bpy.data.materials.new('Inspect_'+name);m.use_nodes=True;bs=m.node_tree.nodes['Principled BSDF'];bs.inputs['Base Color'].default_value=(.25,.30,.34,1) if name=='Body' else (.045,.045,.05,1);bs.inputs['Roughness'].default_value=.7;o.data.materials.clear();o.data.materials.append(m)
 for poly in o.data.polygons:poly.material_index=0
s.render.engine='CYCLES';s.cycles.device='CPU';s.cycles.samples=6;s.cycles.use_denoising=True;s.render.resolution_x=640;s.render.resolution_y=480;s.render.resolution_percentage=100;s.world.color=(.25,.25,.25);s.view_settings.view_transform='AgX'
for n,loc,power,size in [('Key',(1,-4,6),850,5),('Fill',(-2,3,4),650,4)]:
 ld=bpy.data.lights.new(n,'AREA');ld.energy=power;ld.size=size;obj=bpy.data.objects.new(n,ld);s.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((0,0,.9))-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015));floor=bpy.context.object;m=bpy.data.materials.new('Floor');m.use_nodes=True;m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.13,.15,.17,1);floor.data.materials.append(m)
cd=bpy.data.cameras.new('Review');cam=bpy.data.objects.new('Review',cd);s.collection.objects.link(cam);s.camera=cam;cd.type='ORTHO';cd.ortho_scale=2.0 if foal else 3.0
center=Vector((-.03,0,.70 if foal else .99))
for view,offset in [('side',Vector((0,-6,0))),('front',Vector((6,0,0))),('quarter',Vector((3,-6,1.5)))]:
 if view not in a.views.split(','):continue
 cam.location=center+offset;cam.rotation_euler=(-offset).to_track_quat('-Z','Y').to_euler();out=a.out/view;out.mkdir(exist_ok=True)
 for i in range(a.frames):
  f=s.frame_start+period*i/a.frames;s.frame_set(int(f),subframe=f%1);s.render.filepath=str((out/f'{i:03d}.png').resolve());bpy.ops.render.render(write_still=True)
(a.out/'render-manifest.json').write_text(json.dumps({'sourceBlendSha256':hashlib.sha256(a.blend.read_bytes()).hexdigest(),'views':a.views.split(','),'framesPerView':a.frames,'sourcePeriodFrames':period,'sourceFps':s.render.fps,'method':'Actual evaluated complete in-place loop, neutral inspection material. No file save or geometry edits.'},indent=2)+'\n')
