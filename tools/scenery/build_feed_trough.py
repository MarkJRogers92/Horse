import bpy, math, random, json
from pathlib import Path
from mathutils import Vector
R=Path.cwd() / 'output' / 'feed-trough'
(R/'models').mkdir(parents=True, exist_ok=True)
(R/'previews').mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
random.seed(37)
# Original procedural wood texture; no generated reference pixels are edited.
N=256; im=bpy.data.images.new('Trough_Wood_256',width=N,height=N)
p=[]
for y in range(N):
 for x in range(N):
  grain=math.sin(x*.22+math.sin(y*.035)*2.7+math.sin(y*.013)*4)*.035+math.sin(x*1.3+y*.009)*.012+random.uniform(-.016,.016)
  p.extend([max(0,min(1,.53+grain)),max(0,min(1,.29+grain*.65)),max(0,min(1,.115+grain*.4)),1])
im.pixels=p; im.filepath_raw=str(R/'models/wood-grain.png'); im.file_format='PNG'; im.save();im.pack()
wood=bpy.data.materials.new('Honey oak');wood.use_nodes=True
bs=wood.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.72
tex=wood.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;wood.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
iron=bpy.data.materials.new('Dark iron');iron.use_nodes=True
bs=iron.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(.065,.052,.041,1);bs.inputs['Metallic'].default_value=.75;bs.inputs['Roughness'].default_value=.5
parts=[]
def box(name,loc,size,mat,bevel=.008):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 o.data.materials.append(mat)
 if bevel:
  mod=o.modifiers.new('Soft worn edges','BEVEL');mod.width=bevel;mod.segments=2
  bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=mod.name)
 parts.append(o);return o
# Bottom-center pivot; dimensions approx. 1.60 x 0.76 x 0.85 meters.
for x in [-.66,.66]:
 for y in [-.25,.25]:box('Oak leg',(x,y,.215),(.10,.10,.43),wood)
for i in range(5):box('Interior floor plank',(-.56+i*.28,0,.4),(.275,.6,.055),wood,.004)
for z in [.485,.635,.785]:
 for y in [-.33,.33]:box('Long wall plank',(0,y,z),(1.5,.06,.145),wood,.004)
 for x in [-.72,.72]:box('End wall plank',(x,0,z),(.06,.6,.145),wood,.004)
for y in [-.345,.345]:box('Long rim',(0,y,.86),(1.44,.08,.045),wood)
for x in [-.76,.76]:box('End rim',(x,0,.86),(.08,.77,.045),wood)
for x in [-.72,.72]:
 for y in [-.33,.33]:
  for z in [.48,.77]:
   box('Iron corner long',(x-math.copysign(.035,x),y+math.copysign(.033,y),z),(.14,.012,.065),iron,.004)
   box('Iron corner end',(x+math.copysign(.033,x),y-math.copysign(.035,y),z),(.012,.14,.065),iron,.004)
# Join to one static mesh, two materials, and export only geometry.
bpy.ops.object.select_all(action='DESELECT')
for o in parts:o.select_set(True)
bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.join();o=bpy.context.object;o.name='Horse_Feed_Trough'
bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.export_scene.gltf(filepath=str(R/'models/feed-trough.glb'),export_format='GLB',use_selection=True)
sc=bpy.context.scene;sc.render.engine='CYCLES';sc.cycles.samples=24;sc.cycles.use_denoising=False
sc.render.resolution_x=900;sc.render.resolution_y=700;sc.render.resolution_percentage=100
sc.world=bpy.data.worlds.new('Studio');sc.world.use_nodes=True;sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.14,.17,.18,1);sc.world.node_tree.nodes['Background'].inputs[1].default_value=.65
for loc,power,size in [((2,-3,4),500,4),((-3,-1,2),350,3),((1,3,3),500,3)]:
 d=bpy.data.lights.new('Studio light','AREA');d.energy=power;d.size=size;l=bpy.data.objects.new('Studio light',d);sc.collection.objects.link(l);l.location=loc;l.rotation_euler=(Vector((0,0,.4))-l.location).to_track_quat('-Z','Y').to_euler()
d=bpy.data.cameras.new('Preview');cam=bpy.data.objects.new('Preview',d);sc.collection.objects.link(cam);sc.camera=cam;cam.location=(2.1,-2.7,2.0);cam.rotation_euler=(Vector((0,0,.44))-cam.location).to_track_quat('-Z','Y').to_euler();d.type='ORTHO';d.ortho_scale=2.25
bpy.ops.wm.save_as_mainfile(filepath=str(R/'models/feed-trough.blend'))
sc.render.filepath=str(R/'previews/feed-trough.png');bpy.ops.render.render(write_still=True)
print('TROUGH_OK',sum(len(p.vertices)-2 for p in o.data.polygons))
