"""Read-only source rendering for exact-shell neck preflight; never saves .blend."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
sys.path.insert(0, '/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse/tools/horse')
import production_motion as pm
ROOT = Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support')
OUT = ROOT / 'virtual-shell-neck-light'
r = json.loads((OUT / 'light_virtual_shell_neck_contours.json').read_text())
assert r['exactShellVerified'] and r['gates']['fourValidNestedCranialCuts']
bpy.ops.wm.open_mainfile(filepath=r['source'])
body, rig = (bpy.data.objects[n] for n in ('Body', 'Skeleton'))
rig.animation_data.action = None
for track in rig.animation_data.nla_tracks: track.mute = True
pm.set_sagittal_pose(rig)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 1050
scene.render.resolution_y = 740
scene.render.resolution_percentage = 100
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.light = 'STUDIO'
scene.display.shading.studio_light = 'paint.sl'
scene.display.shading.show_shadows = False
scene.display.shading.show_cavity = True
scene.display.shading.background_type = 'WORLD'
scene.world.color = (.24, .25, .27)
colors = {'Unassigned': (.67,.66,.64,1), 'OriginalSkull': (.87,.66,.49,1),
          'SourceEarL': (.15,.72,.42,1), 'SourceEarR': (.63,.35,.85,1)}
skull = set(r['originalSkullBodyIds'])
ears = {}
for e in r['originalEarMembership']['records']:
    left = sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_L'))
    right = sum(w for n,w in e['earWeights'].items() if n.startswith('Ear_R'))
    ears[(e['component'],e['vertexId'])] = 'SourceEarL' if left >= right else 'SourceEarR'
for obj in bpy.data.objects:
    if obj.type != 'MESH': continue
    obj.data.materials.clear()
    index = {}
    for name,color in colors.items():
        mat = bpy.data.materials.new('VirtualNeckReview_' + obj.name + '_' + name)
        mat.diffuse_color = color
        index[name] = len(obj.data.materials)
        obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        name = 'OriginalSkull' if obj == body and sum(i in skull for i in poly.vertices) >= 2 else 'Unassigned'
        for side in ('SourceEarL','SourceEarR'):
            if sum(ears.get((obj.name,i)) == side for i in poly.vertices) >= 2: name = side
        poly.material_index = index[name]
camd = bpy.data.cameras.new('VirtualNeckReviewCamera')
cam = bpy.data.objects.new('VirtualNeckReviewCamera',camd)
scene.collection.objects.link(cam)
camd.type = 'ORTHO'
scene.camera = cam
target = (.73,0,1.52)
views = {'front':((5,0,1.52),target,1.55), 'back':((-5,0,1.52),target,1.55),
         'left':((.73,5,1.52),target,1.55), 'right':((.73,-5,1.52),target,1.55),
         'top':((.73,0,5),target,1.55), 'q3':((3.5,-4,2.8),target,1.7),
         'left_context':((.1,5,1.25),(.15,0,1),3.2),
         'q3_context':((3.5,-4,2.8),(.15,0,1),3.4)}
def project(p):
    v = world_to_camera_view(scene,cam,Vector(p))
    return [v.x*1050,(1-v.y)*740]
for view,(loc,tgt,scale) in views.items():
    cam.location = loc
    cam.rotation_euler = (Vector(tgt)-cam.location).to_track_quat('-Z','Y').to_euler()
    camd.ortho_scale = scale
    bpy.context.view_layer.update()
    scene.render.filepath = str(OUT/f'light_virtual_neck_{view}_raw.png')
    bpy.ops.render.render(write_still=True)
    metadata = {'view':view, 'colors':colors, 'sourceMaterialDisplayOnly':True,
        'loops':[{'x_m':c['x_m'],'points':[project(p) for p in c['selected']['intersectionPoints']],
                  'virtualNodeCount':c['selected']['virtualNodeCount'],
                  'bounds':c['selected']['bounds'],
                  'cranialSourceVertexCounts':{q:len(ids) for q,ids in c['selected']['cranialSourceVertexIds'].items()}}
                 for c in r['cuts']],
        'bones':{b.name:[project(b.head_local),project(b.tail_local)] for b in rig.data.bones
                 if b.name.startswith(('Chest','Neck','Head','Jaw','Ear'))},
        'landmarks':{'skullMinX':project(r['originalSkullBounds'][0]),
                     'lastCutLowestVertex':project(r['cuts'][-1]['selected']['bounds'][0])}}
    (OUT/f'light_virtual_neck_{view}_projection.json').write_text(json.dumps(metadata,indent=2))
print('TASK adult_motion_specialist VIRTUAL_NECK_RENDERS_COMPLETE no source saved',flush=True)
