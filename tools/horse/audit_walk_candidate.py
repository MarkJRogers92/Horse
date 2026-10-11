"""Dense, read-only neutral deformation audit for a saved walk candidate.

Example:
  blender -b --python tools/horse/audit_walk_candidate.py -- \
    art/derived/production/candidates/foal-walk/horse_foal_walk.blend \
    --out /tmp/foal-dense.json --samples 581

This supplements actual runtime and visual inspection; it cannot approve motion.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from derive_stock_walk import evaluated, read, NAMES


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('blend', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--samples', type=int, default=581)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    if args.samples < 61:
        raise ValueError('At least 61 samples are required')
    bpy.ops.wm.open_mainfile(filepath=str(args.blend.resolve()))
    body = bpy.data.objects['Body']
    rest = read(body.data.vertices, 'co', 3).reshape(-1, 3)
    edges = np.array([tuple(edge.vertices) for edge in body.data.edges])
    lengths = np.linalg.norm(rest[edges[:, 0]] - rest[edges[:, 1]], axis=1)
    valid = lengths > 1e-8
    edges, lengths = edges[valid], lengths[valid]
    worst = {'ratio': 0.0}
    min_ratio, sum_error, max_influences = 1.0, 0.0, 0
    for name in NAMES:
        obj = bpy.data.objects[name]
        if any(abs(key.value) > 1e-9 for key in list(obj.data.shape_keys.key_blocks)[1:]):
            raise ValueError('Audit requires neutral morphs')
        for vertex in obj.data.vertices:
            weights = [assignment.weight for assignment in vertex.groups if assignment.weight > 1e-8]
            max_influences = max(max_influences, len(weights))
            sum_error = max(sum_error, abs(sum(weights) - 1))
    component_edges = {}
    component_report = {}
    for name in NAMES:
        mesh = bpy.data.objects[name].data
        points = read(mesh.vertices, 'co', 3).reshape(-1, 3)
        indices = np.array([tuple(edge.vertices) for edge in mesh.edges])
        distances = np.linalg.norm(points[indices[:, 0]] - points[indices[:, 1]], axis=1)
        used = distances > 1e-8
        component_edges[name] = (indices[used], distances[used])
        component_report[name] = {'maxEdgeStretch': 0.0, 'minimumEdgeRatio': 1.0}
    scene = bpy.context.scene
    for frame in np.linspace(scene.frame_start, scene.frame_end, args.samples):
        scene.frame_set(math.floor(frame), subframe=float(frame) % 1)
        all_posed = evaluated()
        for name, (indices, distances) in component_edges.items():
            positions = all_posed[name]
            if not np.isfinite(positions).all():
                raise ValueError(f'Nonfinite {name} posed geometry')
            local_ratios = np.linalg.norm(positions[indices[:, 0]] - positions[indices[:, 1]], axis=1) / distances
            component_report[name]['maxEdgeStretch'] = max(component_report[name]['maxEdgeStretch'], float(local_ratios.max()))
            component_report[name]['minimumEdgeRatio'] = min(component_report[name]['minimumEdgeRatio'], float(local_ratios.min()))
        posed = all_posed['Body']
        if not np.isfinite(posed).all():
            raise ValueError('Nonfinite posed geometry')
        ratios = np.linalg.norm(posed[edges[:, 0]] - posed[edges[:, 1]], axis=1) / lengths
        index = int(np.argmax(ratios))
        min_ratio = min(min_ratio, float(ratios.min()))
        if ratios[index] > worst['ratio']:
            worst = {
                'ratio': float(ratios[index]), 'frame': float(frame),
                'edge': edges[index].tolist(), 'restPoints': rest[edges[index]].tolist(),
            }
    report = {
        'candidateBlendSha256': hashlib.sha256(args.blend.read_bytes()).hexdigest(),
        'sampleCount': args.samples, 'bodyMaxEdgeStretch': worst['ratio'],
        'worstEdge': worst, 'minimumEdgeRatio': min_ratio,
        'maximumInfluences': max_influences, 'maximumWeightSumError': sum_error,
        'componentEdgeStretch': component_report,
        'bodyStretchLimit': 2.0, 'bodyStretchPass': worst['ratio'] <= 2.0,
        'scope': 'Neutral shape only; dense source deformation check supplementary to runtime/visual gates.',
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
