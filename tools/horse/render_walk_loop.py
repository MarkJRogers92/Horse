"""Render a hash-bound, evenly sampled neutral source walk review loop.

The actor travels at its profile speed; camera follow and periodic floor tiles
make planted-foot behavior visible. This never saves changes to the source.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('blend', type=Path)
    parser.add_argument('out', type=Path)
    parser.add_argument('--frames', type=int, default=30)
    parser.add_argument('--width', type=int, default=512)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    args.out.mkdir(parents=True, exist_ok=True)
    profile = json.loads((args.blend.parent / 'walk-motion-profile.json').read_text())
    source_hash = hashlib.sha256(args.blend.read_bytes()).hexdigest()
    assert profile['candidateBlendSha256'] == source_hash
    bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
    scene = bpy.context.scene
    rig = bpy.data.objects['Skeleton']
    is_foal = 'foal' in args.blend.name
    period, fps = profile['periodFrames'], profile['fps']
    speed = profile['forwardSpeedMetersPerSecond']
    distance = period / fps * speed
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 8
    scene.cycles.use_denoising = True
    scene.render.resolution_x = args.width
    scene.render.resolution_y = args.width * 3 // 4
    scene.render.resolution_percentage = 100
    scene.world.color = (.15, .15, .15)
    colors = {'Body': (.33, .15, .075, 1), 'Mane': (.055, .026, .013, 1),
              'Tail': (.055, .026, .013, 1), 'Eyes': (.012, .01, .008, 1)}
    for name, color in colors.items():
        obj = bpy.data.objects[name]
        assert all(abs(key.value) < 1e-9 for key in list(obj.data.shape_keys.key_blocks)[1:])
        material = bpy.data.materials.new('Inspection_' + name)
        material.use_nodes = True
        shader = material.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Base Color'].default_value = color
        shader.inputs['Roughness'].default_value = .65
        obj.data.materials.clear()
        obj.data.materials.append(material)
        for polygon in obj.data.polygons:
            polygon.material_index = 0
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.003))
    floor = bpy.context.object
    material = bpy.data.materials.new('Periodic inspection floor')
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    uv = nodes.new('ShaderNodeTexCoord')
    scale = nodes.new('ShaderNodeVectorMath')
    scale.operation = 'MULTIPLY'
    scale.inputs[1].default_value = (200 / distance, 200 / .35, 1)
    checker = nodes.new('ShaderNodeTexChecker')
    checker.inputs['Scale'].default_value = 2
    checker.inputs['Color1'].default_value = (.19, .23, .2, 1)
    checker.inputs['Color2'].default_value = (.17, .20, .18, 1)
    links.new(uv.outputs['UV'], scale.inputs[0])
    links.new(scale.outputs['Vector'], checker.inputs['Vector'])
    links.new(checker.outputs['Color'], nodes.get('Principled BSDF').inputs['Base Color'])
    floor.data.materials.append(material)
    for name, loc, power, size in [('Key', (1, -4, 6), 900, 5), ('Fill', (-2, 3, 4), 600, 4)]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy, data.shape, data.size = power, 'DISK', size
        obj = bpy.data.objects.new(name, data)
        scene.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_euler = (Vector((0, 0, 1)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('Camera')
    camera = bpy.data.objects.new('Camera', data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    data.type, data.ortho_scale = 'ORTHO', 2.2 if is_foal else 2.9
    target = Vector((-.04, 0, .72 if is_foal else .95))
    base_camera = Vector((3, -6, 2.1 if is_foal else 2.4))
    camera.rotation_euler = (target - base_camera).to_track_quat('-Z', 'Y').to_euler()
    for index in range(args.frames):
        phase = index / args.frames
        frame = phase * period
        scene.frame_set(math.floor(frame), subframe=frame % 1)
        travel = Vector((phase * distance, 0, 0))
        rig.location = travel
        camera.location = base_camera + travel
        scene.render.filepath = str((args.out / f'frame_{index:03d}.png').resolve())
        bpy.ops.render.render(write_still=True)
    manifest = {
        'candidateBlendSha256': source_hash, 'frames': args.frames,
        'periodFrames': period, 'sourceFps': fps, 'clipDurationSeconds': period / fps,
        'previewFramesPerSecond': args.frames * fps / period,
        'forwardSpeedMetersPerSecond': speed, 'actorTravelPerCycleMeters': distance,
        'method': 'Actual evaluated source frames, uniform phases; one actor translation and matching camera follow. Floor pattern repeats after one cycle. Plain inspection material, not an application coat render.',
        'pngSha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in args.out.glob('frame_*.png')},
    }
    (args.out / 'render-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
