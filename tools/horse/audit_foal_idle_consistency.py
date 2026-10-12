"""Read-only foal Idle/Walk appearance and original Idle preservation proof.

The full report requires Blender; audit_glbs() recomputes the exported contract
without Blender or stored proof flags. Neither path edits any input asset.
Rig and skin are deliberately compared only original-Idle to corrected-Idle.
The separate bounded-geometry audit owns the authorized original-rest edit.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

from audit_foal_geometry_region import (
    Glb, MESH_NAMES, SHAPE_NAMES, array_hash, exact, sha, snapshot_blend,
)

SCHEMA = 'horse.foal_idle.walk_consistency.v1'
ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / 'art/derived/production'
CANDIDATE = BASE / 'candidates/foal-idle-hind-geometry'
FOUNDATION = BASE / 'candidates/foal-idle-foundation'
WALK = BASE / 'candidates/foal-walk-hind-geometry'
MORPH_TOLERANCE_M = 1e-7
SURFACE_ATTRIBUTES = {
    'Body': {'POSITION', 'NORMAL', 'TEXCOORD_0', 'TEXCOORD_1', 'COLOR_0'},
    'Eyes': {'POSITION', 'NORMAL', 'TEXCOORD_0', 'TEXCOORD_1'},
    'Mane': {'POSITION', 'NORMAL', 'TEXCOORD_0'},
    'Tail': {'POSITION', 'NORMAL', 'TEXCOORD_0'},
}


def json_exact(a, b):
    """Reject non-finite metadata and require the same JSON value types."""
    return json.dumps(a, sort_keys=True, allow_nan=False) == json.dumps(b, sort_keys=True, allow_nan=False)


def json_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, allow_nan=False).encode()).hexdigest()


def surface_keys(attributes):
    return {k for k in attributes if not k.startswith(('JOINTS_', 'WEIGHTS_'))}


def compare_surface(name, idle, walk):
    """Compare identical indexed-corner order; never match by candidate values."""
    row = {'failures': []}

    def check(key, value):
        row[key] = bool(value)
        if not value:
            row['failures'].append(name + ': ' + key)

    a, b = idle['attributes'], walk['attributes']
    keys = surface_keys(a)
    check('surfaceAttributeSetExact', keys == surface_keys(b) == SURFACE_ATTRIBUTES[name])
    check('indexedTopologyExact', exact(idle['indices'], walk['indices']) and idle['material'] == walk['material'])
    check('restPositionsExact', 'POSITION' in a and 'POSITION' in b and exact(a['POSITION'], b['POSITION']))
    check('uvExact', bool(keys & {'TEXCOORD_0'}) and
          {k for k in a if k.startswith('TEXCOORD_')} == {k for k in b if k.startswith('TEXCOORD_')} and
          all(k in b and exact(a[k], b[k]) for k in a if k.startswith('TEXCOORD_')))
    check('colorsExact', {k for k in a if k.startswith('COLOR_')} == {k for k in b if k.startswith('COLOR_')} and
          all(k in b and exact(a[k], b[k]) for k in a if k.startswith('COLOR_')))
    check('normalsExact', 'NORMAL' in a and 'NORMAL' in b and exact(a['NORMAL'], b['NORMAL']))
    morph_ok = (idle['target_names'] == walk['target_names'] == list(SHAPE_NAMES[1:]) and
                len(idle['targets']) == len(walk['targets']) == 4)
    errors, morph_exact, morph_normals = {}, morph_ok, morph_ok
    for key, x, y in zip(SHAPE_NAMES[1:], idle['targets'], walk['targets']):
        compatible = set(x) == set(y) == {'POSITION', 'NORMAL'}
        if not compatible or x['POSITION'].shape != y['POSITION'].shape:
            morph_ok = morph_exact = morph_normals = False
            errors[key] = None
            continue
        delta = np.abs(x['POSITION'].astype(np.float64) - y['POSITION'].astype(np.float64))
        error = float(np.max(delta, initial=0))
        errors[key] = error
        morph_ok &= bool(np.isfinite(delta).all() and error <= MORPH_TOLERANCE_M)
        morph_exact &= exact(x['POSITION'], y['POSITION'])
        morph_normals &= exact(x['NORMAL'], y['NORMAL'])
    check('morphTargetsPreserved', morph_ok and morph_normals)
    check('morphNormalsExact', morph_normals)
    row.update(morphPositionsExact=bool(morph_exact), maxMorphPositionDifferenceM=errors,
               cornerCount=len(idle['indices']), triangleCount=len(idle['indices']) // 3,
               indexedCornerSha256={k: array_hash(a[k]) for k in sorted(keys)},
               morphTargetSha256=[{k: array_hash(v) for k, v in t.items()} for t in idle['targets']])
    row['pass'] = not row['failures']
    return row


def _accessor_equivalent(a, ai, b, bi):
    """Accessor storage offsets may change; decoded data and type may not."""
    x, y = a.document['accessors'][ai], b.document['accessors'][bi]
    return (all(x.get(k) == y.get(k) for k in ('componentType', 'type', 'count', 'normalized')) and
            exact(a.accessor(ai), b.accessor(bi)))


def compare_static_exports(source, candidate):
    """Keep every original Idle node, skin, material and animation array exact."""
    a, b = source.document, candidate.document
    checks = {}
    # Includes node children, local transforms, skeleton references, mesh weights,
    # animation targets/interpolation and material descriptors, not buffer offsets.
    for label, fields in [
        ('nodesAndMatricesExact', ('nodes', 'scene', 'scenes')),
        ('materialsExact', ('materials', 'textures', 'images', 'samplers', 'extensionsUsed', 'extensionsRequired')),
        ('skinStructureExact', ('skins',)),
        ('meshStructureExact', ('meshes',)),
        ('idleAnimationStructureExact', ('animations',)),
    ]:
        checks[label] = all(json_exact(a.get(k), b.get(k)) for k in fields)
    checks['idleAnimationPresent'] = ([x.get('name') for x in a.get('animations', [])] ==
                                      [x.get('name') for x in b.get('animations', [])] == ['Idle'])
    skin_ok = checks['skinStructureExact'] and len(a.get('skins', [])) > 0
    for x, y in zip(a.get('skins', []), b.get('skins', [])):
        skin_ok &= ('inverseBindMatrices' in x and 'inverseBindMatrices' in y and
                    _accessor_equivalent(source, x['inverseBindMatrices'], candidate, y['inverseBindMatrices']))
    checks['inverseBindMatricesExact'] = bool(skin_ok)
    animation_ok = checks['idleAnimationStructureExact'] and checks['idleAnimationPresent']
    animation_arrays = []
    for x, y in zip(a.get('animations', []), b.get('animations', [])):
        animation_ok &= len(x.get('samplers', [])) > 0 and len(x.get('channels', [])) > 0
        for sa, sb in zip(x.get('samplers', []), y.get('samplers', [])):
            for key in ('input', 'output'):
                animation_ok &= _accessor_equivalent(source, sa[key], candidate, sb[key])
                animation_arrays.append(array_hash(source.accessor(sa[key])))
    checks['idleAnimationArraysExact'] = bool(animation_ok)
    skin_attributes_ok = checks['meshStructureExact']
    for x, y in zip(a['meshes'], b['meshes']):
        for p, q in zip(x['primitives'], y['primitives']):
            ak, bk = set(p['attributes']), set(q['attributes'])
            skin_keys = {k for k in ak if k.startswith(('JOINTS_', 'WEIGHTS_'))}
            skin_attributes_ok &= {'JOINTS_0', 'WEIGHTS_0'}.issubset(skin_keys)
            skin_attributes_ok &= skin_keys == {k for k in bk if k.startswith(('JOINTS_', 'WEIGHTS_'))}
            for k in skin_keys:
                skin_attributes_ok &= k in q['attributes'] and _accessor_equivalent(
                    source, p['attributes'][k], candidate, q['attributes'][k])
    checks['skinAttributesExact'] = bool(skin_attributes_ok)
    return dict(checks, pass_=all(checks.values()),
                idleAnimationArrayCount=len(animation_arrays),
                idleAnimationArraysSha256=json_hash(animation_arrays),
                sourceProtectedMetadataSha256=json_hash({k: a.get(k) for k in
                    ('nodes', 'scene', 'scenes', 'skins', 'materials', 'animations')}))


def _validate_glb(glb):
    """Reject malformed/unsupported shapes before NumPy can wrap bad indices."""
    d = glb.document
    json.dumps(d, allow_nan=False)
    for i in range(len(d.get('accessors', []))):
        glb.accessor(i)  # All arrays, including unreferenced arrays, must be finite.
    names = [n.get('name') for n in d['nodes'] if 'mesh' in n]
    if len(names) != 4 or set(names) != set(MESH_NAMES):
        raise ValueError('Expected four uniquely named component mesh nodes')
    if len(d['meshes']) != 4:
        raise ValueError('Expected exactly four mesh resources')
    for node in d['nodes']:
        if 'mesh' not in node:
            continue
        if not 0 <= node['mesh'] < len(d['meshes']):
            raise ValueError('Invalid mesh reference')
        mesh = d['meshes'][node['mesh']]
        if len(mesh['primitives']) != 1:
            raise ValueError('Expected one primitive per component')
        p = mesh['primitives'][0]
        if p.get('mode', 4) != 4 or 'indices' not in p:
            raise ValueError('Expected indexed triangles')
        indices = glb.accessor(p['indices'])
        if indices.dtype.kind not in 'ui' or indices.shape[1] != 1 or len(indices) % 3:
            raise ValueError('Invalid triangle index array')
        attributes = p['attributes']
        if 'POSITION' not in attributes:
            raise ValueError('Missing position accessor')
        count = len(glb.accessor(attributes['POSITION']))
        if not count or not len(indices) or np.max(indices) >= count:
            raise ValueError('Out-of-range or empty triangle indices')
        for ai in [*attributes.values(), *(v for t in p.get('targets', []) for v in t.values())]:
            if len(glb.accessor(ai)) != count:
                raise ValueError('Attribute/target vertex counts differ')
    if len({n['mesh'] for n in d['nodes'] if 'mesh' in n}) != 4:
        raise ValueError('Duplicate/unused component mesh resource')


def audit_glbs(source_path, candidate_path, walk_path):
    """Live, Blender-free audit of three files. No report flags are trusted."""
    result = {'schema': SCHEMA + '.glb', 'failures': [], 'meshes': {},
              'scope': 'Live exported data only; full Blend proof requires the full auditor.'}
    try:
        source, candidate, walk = [Glb(p) for p in (source_path, candidate_path, walk_path)]
        for glb in (source, candidate, walk):
            _validate_glb(glb)
        cm, wm = candidate.meshes(), walk.meshes()
        for name in MESH_NAMES:
            row = compare_surface(name, cm[name][0], wm[name][0])
            result['meshes'][name] = row
            result['failures'].extend(row['failures'])
        preserved = compare_static_exports(source, candidate)
        result['staticPreservation'] = preserved
        for key, value in preserved.items():
            if value is False:
                result['failures'].append('static export: ' + key)
        for flag, field in [('glbRestPositionsExact', 'restPositionsExact'),
                            ('glbIndexedTopologyExact', 'indexedTopologyExact'),
                            ('glbUvExact', 'uvExact'), ('glbColorsExact', 'colorsExact'),
                            ('glbNormalsExact', 'normalsExact'),
                            ('glbMorphTargetsPreserved', 'morphTargetsPreserved')]:
            result[flag] = all(r[field] for r in result['meshes'].values())
        result['crossModeMaterialsExact'] = json_exact(candidate.document.get('materials'), walk.document.get('materials'))
        if not result['crossModeMaterialsExact']:
            result['failures'].append('Idle/Walk material descriptors differ')
        if [a.get('name') for a in walk.document.get('animations', [])] != ['Walk']:
            result['failures'].append('Accepted Walk export must contain exactly Walk')
    except (ValueError, KeyError, IndexError, TypeError, OverflowError, OSError) as exc:
        result['failures'].append('GLB audit: ' + str(exc))
    result.update(sourceGlbSha256=sha(source_path), candidateGlbSha256=sha(candidate_path),
                  acceptedWalkGlbSha256=sha(walk_path))
    result['pass'] = not result['failures']
    return result


def _plain(value):
    if isinstance(value, (str, int, float, bool)) or value is None:
        return value
    return [_plain(v) for v in value]


def _rna_values(item):
    """Finite scalar/array RNA properties plus stable ID references."""
    values = {}
    for prop in item.bl_rna.properties:
        key = prop.identifier
        if key in {'rna_type', 'session_uid'} or prop.type == 'COLLECTION':
            continue
        value = getattr(item, key)
        if prop.type == 'POINTER':
            values[key] = None if value is None else (value.bl_rna.identifier, getattr(value, 'name', ''))
        elif getattr(prop, 'is_array', False):
            values[key] = _plain(value)
        elif prop.type in {'BOOLEAN', 'INT', 'FLOAT', 'STRING', 'ENUM'}:
            values[key] = sorted(value) if isinstance(value, set) else value
    return values


def _curves(action):
    result = []
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                bag = strip.channelbag(slot)
                if bag is None:
                    continue
                for fc in bag.fcurves:
                    result.append({'slot': slot.identifier, 'layer': layer.name, 'path': fc.data_path,
                                   'arrayIndex': fc.array_index, 'settings': _rna_values(fc),
                                   'keyframes': [_rna_values(k) for k in fc.keyframe_points],
                                   'samples': [_rna_values(k) for k in fc.sampled_points],
                                   'modifiers': [_rna_values(m) for m in fc.modifiers]})
    return result


def snapshot_protected_blend():
    """Snapshot the currently open Blend's authored rig, transforms and actions."""
    import bpy
    nodes, rigs, bindings = {}, {}, {}
    for obj in bpy.data.objects:
        nodes[obj.name] = {'type': obj.type, 'parent': obj.parent.name if obj.parent else None,
                           'parentType': obj.parent_type, 'parentBone': obj.parent_bone,
                           'matrixLocal': np.array(obj.matrix_local).tolist(),
                           'matrixWorld': np.array(obj.matrix_world).tolist(),
                           'matrixParentInverse': np.array(obj.matrix_parent_inverse).tolist(),
                           'rotationMode': obj.rotation_mode,
                           'materialSlots': [(slot.name, slot.link) for slot in obj.material_slots],
                           'constraints': [_rna_values(c) for c in obj.constraints],
                           'modifiers': [_rna_values(m) for m in obj.modifiers]}
        ad = obj.animation_data
        bindings[obj.name] = None if ad is None else {
            'action': ad.action.name if ad.action else None,
            'slot': ad.action_slot.identifier if ad.action_slot else None,
            'settings': _rna_values(ad),
            'drivers': [{'settings': _rna_values(f), 'driver': _rna_values(f.driver),
                         'variables': [{'settings': _rna_values(v), 'targets': [_rna_values(t) for t in v.targets]}
                                       for v in f.driver.variables]} for f in ad.drivers],
            'nla': [{'settings': _rna_values(t), 'strips': [_rna_values(s) for s in t.strips]} for t in ad.nla_tracks]}
        if obj.type == 'ARMATURE':
            rigs[obj.name] = {'bones': {bone.name: _rna_values(bone) for bone in obj.data.bones},
                              'pose': {p.name: {'settings': _rna_values(p),
                                               'constraints': [_rna_values(c) for c in p.constraints]}
                                       for p in obj.pose.bones}}
    materials = {}
    assigned_materials = {slot.material.name for obj in bpy.data.objects for slot in obj.material_slots if slot.material}
    for mat in bpy.data.materials:
        if mat.name not in assigned_materials:
            continue
        tree = mat.node_tree
        materials[mat.name] = {'settings': _rna_values(mat),
            'nodes': [] if tree is None else [dict(settings=_rna_values(n), inputs=[
                {'name': s.name, 'default': list(s.default_value) if hasattr(s.default_value, '__len__') and not isinstance(s.default_value, str)
                 else s.default_value} for s in n.inputs if hasattr(s, 'default_value')]) for n in tree.nodes],
            'links': [] if tree is None else [(l.from_node.name, l.from_socket.identifier, l.to_node.name, l.to_socket.identifier) for l in tree.links]}
    animation = {'bindings': bindings, 'actions': {a.name: {'settings': _rna_values(a), 'curves': _curves(a)} for a in bpy.data.actions},
                 'scenes': {s.name: {'fps': s.render.fps, 'fpsBase': s.render.fps_base, 'start': s.frame_start,
                                    'end': s.frame_end, 'current': s.frame_current, 'subframe': s.frame_subframe}
                            for s in bpy.data.scenes}}
    # Validate before comparison/hashing; no NaN == NaN loophole.
    data = {'nodes': nodes, 'rigs': rigs, 'materials': materials, 'animation': animation,
            'unusedMaterialDatablocks': sorted(m.name for m in bpy.data.materials if m.name not in assigned_materials)}
    json.dumps(data, allow_nan=False)
    return data


def audit_blends(source_path, candidate_path, walk_path):
    source_meshes = snapshot_blend(source_path)
    source = snapshot_protected_blend()
    candidate_meshes = snapshot_blend(candidate_path)
    candidate = snapshot_protected_blend()
    walk_meshes = snapshot_blend(walk_path)
    failures, meshes = [], {}
    if set(source_meshes) != set(candidate_meshes) or set(candidate_meshes) != set(walk_meshes) or set(walk_meshes) != set(MESH_NAMES):
        failures.append('Blend component mesh set mismatch')
    topology_keys = ('edges', 'loop_vertices', 'loop_edges', 'polygon_ranges', 'polygon_materials', 'polygon_smooth', 'triangle_loops')
    for name in MESH_NAMES:
        a, b = candidate_meshes[name], walk_meshes[name]
        checks = {'restPositionsExact': exact(a['positions'], b['positions']),
                  'topologyExact': all(exact(a[k], b[k]) for k in topology_keys),
                  'shapeEndpointsExact': list(a['shapes']) == list(b['shapes']) == list(SHAPE_NAMES) and
                     a['shape_metadata'] == b['shape_metadata'] and all(exact(a['shapes'][k], b['shapes'][k]) for k in a['shapes']),
                  'uvExact': a['uv_metadata'] == b['uv_metadata'] and list(a['uv']) == list(b['uv']) and
                     all(exact(a['uv'][k], b['uv'][k]) for k in a['uv']),
                  'colorsExact': a['color_metadata'] == b['color_metadata'] and list(a['colors']) == list(b['colors']) and
                     all(exact(a['colors'][k], b['colors'][k]) for k in a['colors']),
                  'normalsExact': a['normal_metadata'] == b['normal_metadata'] and
                     all(exact(a[k], b[k]) for k in ('vertex_normals', 'polygon_normals', 'corner_normals', 'packed_custom_normals')),
                  'staticWeightsExact': json_exact(source_meshes[name]['weights'], a['weights']),
                  'objectTransformsExact': all(exact(a[k], b[k]) for k in ('matrix_world', 'matrix_parent_inverse'))}
        meshes[name] = dict(checks, vertexCount=len(a['positions']),
                            shapeEndpointSha256={k: array_hash(v) for k, v in a['shapes'].items()})
        failures.extend(name + ': Blend ' + k for k, v in checks.items() if not v)
    result = {'meshes': meshes, 'failures': failures}
    for flag, field in [('blendRestPositionsExact', 'restPositionsExact'), ('blendTopologyExact', 'topologyExact'),
                        ('blendShapeEndpointsExact', 'shapeEndpointsExact'), ('blendUvExact', 'uvExact'),
                        ('blendColorsExact', 'colorsExact'), ('blendNormalsExact', 'normalsExact')]:
        result[flag] = all(r[field] for r in meshes.values())
    result['staticRigAndWeightsExact'] = json_exact(source['rigs'], candidate['rigs']) and all(r['staticWeightsExact'] for r in meshes.values())
    for flag, key in [('staticNodesAndMatricesExact', 'nodes'), ('staticMaterialsExact', 'materials'), ('idleAnimationExact', 'animation')]:
        result[flag] = json_exact(source[key], candidate[key])
    result['unusedMaterialDatablocks'] = {'source': source['unusedMaterialDatablocks'], 'candidate': candidate['unusedMaterialDatablocks'],
                                         'scope': 'Unassigned orphan material datablocks are disclosed, not part of assigned/exported material equality. Blender session_uid values are transient per-load counters and excluded.'}
    result['protectedSourceHashes'] = {k: json_hash(v) for k, v in source.items()}
    result['protectedCandidateHashes'] = {k: json_hash(v) for k, v in candidate.items()}
    failures.extend(k for k, v in result.items() if v is False)
    result['pass'] = not failures
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-blend', type=Path, default=BASE / 'horse_foal.blend')
    parser.add_argument('--source-glb', type=Path, default=FOUNDATION / 'horse_foal.glb')
    parser.add_argument('--candidate-blend', type=Path, default=CANDIDATE / 'horse_foal.blend')
    parser.add_argument('--candidate-glb', type=Path, default=CANDIDATE / 'horse_foal.glb')
    parser.add_argument('--walk-blend', type=Path, default=WALK / 'horse_foal_walk.blend')
    parser.add_argument('--walk-glb', type=Path, default=WALK / 'horse_foal_walk.glb')
    parser.add_argument('--out', type=Path, default=CANDIDATE / 'idle-walk-consistency.json')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ([] if '--python' in sys.argv else sys.argv[1:]))
    report = {'schema': SCHEMA, 'validatorSha256': sha(__file__),
              'dependencySha256': {'audit_foal_geometry_region.py': sha(Path(__file__).with_name('audit_foal_geometry_region.py')),
                                   'import_reference.py': sha(Path(__file__).with_name('import_reference.py')),
                                   'build_foal_production.py': sha(Path(__file__).with_name('build_foal_production.py'))},
              'morphPositionToleranceM': MORPH_TOLERANCE_M,
              'scope': 'Corrected Idle and accepted Walk have identical rest surfaces and authored morph endpoints. Original Idle nodes, rig, weights, materials and animation are preserved. Cross-mode rig/skin/animation equality is intentionally excluded. Bounded-edit authorization and posed gait/physical gates remain separate.',
              'correspondence': 'Original indexed corner order; no position sorting, nearest-neighbor matching or construction-report flags.',
              'failures': []}
    bindings = [('sourceBlendSha256', args.source_blend), ('sourceGlbSha256', args.source_glb),
                ('candidateBlendSha256', args.candidate_blend), ('candidateGlbSha256', args.candidate_glb),
                ('acceptedWalkBlendSha256', args.walk_blend), ('acceptedWalkGlbSha256', args.walk_glb)]
    before = {key: sha(path) for key, path in bindings}
    report.update(before)
    report['sourcePaths'] = {key.replace('Sha256', ''): str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path) for key, path in bindings}
    try:
        glb = audit_glbs(args.source_glb, args.candidate_glb, args.walk_glb)
        blend = audit_blends(args.source_blend, args.candidate_blend, args.walk_blend)
        report.update(glbAudit=glb, blendAudit=blend)
        report['failures'].extend(glb['failures'] + blend['failures'])
        report.update({k: v for k, v in glb.items() if k.startswith('glb') and isinstance(v, bool)})
        report.update({k: v for k, v in blend.items() if k.startswith('blend') and isinstance(v, bool)})
        static = glb.get('staticPreservation', {})
        report['staticRigAndWeightsExact'] = blend['staticRigAndWeightsExact'] and static.get('inverseBindMatricesExact', False) and static.get('skinAttributesExact', False)
        report['staticNodesAndMatricesExact'] = blend['staticNodesAndMatricesExact'] and static.get('nodesAndMatricesExact', False)
        report['staticMaterialsExact'] = blend['staticMaterialsExact'] and static.get('materialsExact', False)
        report['idleAnimationExact'] = blend['idleAnimationExact'] and static.get('idleAnimationArraysExact', False)
        import import_reference
        from build_foal_production import inspect_idle_export, adult_targets
        report['staticImportInspection'] = import_reference.inspect_glb(args.candidate_glb, {}, allow_animations=True)
        export_report = {'schema': 'horse.foal_idle.static_export.v1',
                         'candidateGlbSha256': before['candidateGlbSha256'],
                         'candidateBlendSha256': before['candidateBlendSha256'],
                         'validatorSha256': report['validatorSha256'],
                         'dependencySha256': report['dependencySha256'],
                         'glbAudit': report['staticImportInspection'],
                         'idleExportAudit': inspect_idle_export(args.candidate_glb, import_reference),
                         'morphTargetsByComponent': adult_targets(args.candidate_glb, import_reference)}
        if export_report['morphTargetsByComponent'] != {n: list(SHAPE_NAMES[1:]) for n in MESH_NAMES}:
            raise ValueError('Static morph target set differs')
        export_report['pass'] = True
        (args.out.parent / 'static-export-audit.json').write_text(json.dumps(export_report, indent=2, allow_nan=False) + '\n')
        report['staticDefaultFourInfluenceInspectionPass'] = True
    except Exception as exc:
        report['failures'].append(type(exc).__name__ + ': ' + str(exc))
    report['inputsUnchanged'] = before == {key: sha(path) for key, path in bindings}
    if not report['inputsUnchanged']:
        report['failures'].append('Input asset bytes changed during read-only audit')
    report['pass'] = not report['failures']
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps({'pass': report['pass'], 'failures': report['failures'], 'report': str(args.out)}))
    if not report['pass']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
