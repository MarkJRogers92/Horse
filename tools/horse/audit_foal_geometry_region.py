"""Read-only bounded foal rest-geometry proof, separate from gait/physical gates.

Run with Blender 5.2.2:
  blender -b --python tools/horse/audit_foal_geometry_region.py -- \
    --source-blend SOURCE.blend --candidate-blend CANDIDATE.blend --out REPORT.json

Only the report is written. The immutable source determines eligibility and
normal support. Exact positions are deliberately NOT claimed after a valid edit.
Core comparisons are importable without bpy for adversarial unit tests.
"""
import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import numpy as np

SCHEMA = 'horse.foal_walk.bounded_geometry.v1'
MESH_NAMES = ('Body', 'Eyes', 'Mane', 'Tail')
SHAPE_NAMES = ('Basis', 'body_length', 'chest_width', 'neck_length', 'muscle')
MAX_OFFSET_M = .09
MORPH_TOLERANCE_M = 1e-7
EXPORT_TOLERANCE_M = 2e-6


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def exact(a, b):
    """Exact ordered values; non-finite numeric data never passes."""
    a, b = np.asarray(a), np.asarray(b)
    return (a.shape == b.shape and a.dtype == b.dtype and
            (a.dtype.kind not in 'fc' or (np.isfinite(a).all() and np.isfinite(b).all()))
            and a.tobytes() == b.tobytes())


def array_hash(a):
    a = np.asarray(a)
    return hashlib.sha256(a.dtype.str.encode() + str(a.shape).encode() + a.tobytes()).hexdigest()


def allowed_vertices(positions):
    return ((positions[:, 0] < -.24) & (positions[:, 2] > .40) &
            (positions[:, 2] < .91))


def normal_support(mesh, changed):
    """Faces touching an edited point; their vertices and ALL incident corners.

    Smooth vertex/corner normals can change at a vertex neighboring an edited
    point, including corners on other faces incident to that neighboring point.
    Polygon normals themselves are permitted to change only on touched faces.
    """
    loops = mesh['loop_vertices']
    faces = [i for i, (start, count) in enumerate(mesh['polygon_ranges'])
             if np.any(changed[loops[start:start + count]])]
    vertices = sorted({int(v) for i in faces
                       for v in loops[slice(*_span(mesh['polygon_ranges'][i]))]})
    corners = np.flatnonzero(np.isin(loops, vertices)).tolist()
    return {'faceIds': faces, 'vertexIds': vertices, 'cornerIds': corners}


def _span(row):
    return int(row[0]), int(row[0] + row[1])


def compare_mesh(name, source, candidate):
    """Compare independent snapshots; no assertion depends on author metadata."""
    failures = []
    checks = {}

    def check(key, value):
        checks[key] = bool(value)
        if not value:
            failures.append(name + ': ' + key)

    def maps_equal(a, b):
        return list(a) == list(b) and all(exact(a[k], b[k]) for k in a)

    check('topologyExact', all(exact(source[k], candidate[k]) for k in
                              ('edges', 'loop_vertices', 'loop_edges', 'polygon_ranges',
                               'polygon_materials', 'polygon_smooth', 'triangle_loops')))
    check('uvExact', source['uv_metadata'] == candidate['uv_metadata'] and
          maps_equal(source['uv'], candidate['uv']))
    check('colorsExact', source['color_metadata'] == candidate['color_metadata'] and
          maps_equal(source['colors'], candidate['colors']))
    check('transformsExact', exact(source['matrix_world'], candidate['matrix_world']) and
          exact(source['matrix_parent_inverse'], candidate['matrix_parent_inverse']))
    check('weightsExact', source['weights'] == candidate['weights'])
    check('shapeKeyStructureExact', source['shape_metadata'] == candidate['shape_metadata'] and
          list(source['shapes']) == list(candidate['shapes']) == list(SHAPE_NAMES))
    before, after = source['positions'], candidate['positions']
    check('vertexCountExact', before.shape == after.shape)
    check('finiteGeometry', np.isfinite(before).all() and np.isfinite(after).all() and
          all(np.isfinite(a).all() for a in candidate['shapes'].values()))
    if not checks['vertexCountExact']:
        return dict(checks, pass_=False, failures=failures, changedVertexIds=[],
                    changedVertices=[], changedVertexCount=0, maxOffsetM=0,
                    normalSupport={'faceIds': [], 'vertexIds': [], 'cornerIds': []})
    delta = after.astype(np.float64) - before.astype(np.float64)
    changed = np.any(before.view(np.uint8).reshape(len(before), -1) !=
                     after.view(np.uint8).reshape(len(after), -1), axis=1)
    ids = np.flatnonzero(changed)
    allowed = allowed_vertices(before) if name == 'Body' else np.zeros(len(before), bool)
    check('positionsWithinAuthorizedRegion', not np.any(changed & ~allowed))
    check('untouchedPositionsExact', exact(before[~allowed], after[~allowed]))
    lengths = np.linalg.norm(delta, axis=1)
    maximum = float(np.max(lengths, initial=0))
    check('maxOffsetWithinLimit', np.isfinite(lengths).all() and maximum <= MAX_OFFSET_M + MORPH_TOLERANCE_M)
    # Disclosure only: a bounded edit must never masquerade as exact geometry.
    checks['allPositionsExact'] = exact(before, after)
    morph_errors = {}
    morph_ok = checks['shapeKeyStructureExact']
    untouched_shapes = morph_ok
    for key in source['shapes']:
        a, b = source['shapes'][key], candidate['shapes'].get(key)
        if b is None or a.shape != b.shape or a.shape != before.shape:
            morph_ok = untouched_shapes = False
            morph_errors[key] = None
            continue
        error = float(np.max(np.abs((b.astype(float) - a.astype(float)) - delta), initial=0))
        morph_errors[key] = error
        morph_ok &= np.isfinite(error) and error <= MORPH_TOLERANCE_M
        untouched_shapes &= exact(a[~changed], b[~changed])
    check('morphDeltasPreserved', morph_ok)
    check('untouchedShapePositionsExact', untouched_shapes)
    check('basisMatchesPositions', 'Basis' in candidate['shapes'] and
          exact(candidate['shapes']['Basis'], after) and exact(source['shapes']['Basis'], before))
    support = normal_support(source, changed) if checks['topologyExact'] else {'faceIds': [], 'vertexIds': [], 'cornerIds': []}
    normal_ok = checks['topologyExact'] and source['normal_metadata'] == candidate['normal_metadata']
    normal_counts = {}
    for kind, support_key in [('vertex_normals', 'vertexIds'), ('polygon_normals', 'faceIds'),
                              ('corner_normals', 'cornerIds')]:
        a, b = source[kind], candidate[kind]
        if a.shape != b.shape:
            normal_ok = False
            normal_counts[kind] = None
            continue
        outside = np.ones(len(a), bool)
        outside[support[support_key]] = False
        normal_ok &= exact(a[outside], b[outside]) and np.isfinite(b).all()
        normal_counts[kind] = int(np.count_nonzero(np.any(a != b, axis=1)))
    a, b = source['packed_custom_normals'], candidate['packed_custom_normals']
    packed_ok = a.shape == b.shape
    if packed_ok and len(a):
        outside = np.ones(len(a), bool)
        outside[support['cornerIds']] = False
        packed_ok = exact(a[outside], b[outside])
    check('rawPackedNormalsOutsideSupportExact', packed_ok)
    check('normalsOutsideSupportExact', normal_ok and packed_ok)
    return dict(checks, pass_=not failures, failures=failures,
                vertexCount=len(before), authorizedVertexCount=int(allowed.sum()),
                changedVertexIds=ids.tolist(), changedVertexCount=len(ids),
                changedVertices=[{'vertexId': int(i), 'sourcePositionM': before[i].tolist(),
                                  'candidatePositionM': after[i].tolist(), 'deltaM': delta[i].tolist(),
                                  'offsetM': float(lengths[i])} for i in ids],
                maxOffsetM=maximum, maxShapeOffsetDifferenceM=morph_errors,
                normalSupport=support, normalChangedCounts=normal_counts,
                sourcePositionSha256=array_hash(before), candidatePositionSha256=array_hash(after))


def read_array(collection, prop, width, dtype=np.float32):
    a = np.empty(len(collection) * width, dtype=dtype)
    collection.foreach_get(prop, a)
    return a.reshape(len(collection), width) if width > 1 else a


def snapshot_blend(path):
    import bpy
    bpy.ops.wm.open_mainfile(filepath=str(Path(path).resolve()), use_scripts=False)
    meshes = {}
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        m = obj.data
        m.calc_loop_triangles()
        meshes[obj.name] = {
            'positions': read_array(m.vertices, 'co', 3),
            'edges': read_array(m.edges, 'vertices', 2, np.int32),
            'loop_vertices': read_array(m.loops, 'vertex_index', 1, np.int32),
            'loop_edges': read_array(m.loops, 'edge_index', 1, np.int32),
            'polygon_ranges': np.array([(p.loop_start, p.loop_total) for p in m.polygons], np.int32),
            'polygon_materials': read_array(m.polygons, 'material_index', 1, np.int32),
            'polygon_smooth': read_array(m.polygons, 'use_smooth', 1, bool),
            'triangle_loops': np.array([list(t.loops) for t in m.loop_triangles], np.int32),
            'matrix_world': np.array(obj.matrix_world, np.float64),
            'matrix_parent_inverse': np.array(obj.matrix_parent_inverse, np.float64),
            'uv': {u.name: read_array(u.data, 'uv', 2) for u in m.uv_layers},
            'uv_metadata': [(u.name, u.active, u.active_render, u.active_clone) for u in m.uv_layers],
            'colors': {c.name: read_array(c.data, 'color', 4) for c in m.color_attributes},
            'color_metadata': [(c.name, c.domain, c.data_type) for c in m.color_attributes],
            'shapes': {k.name: read_array(k.data, 'co', 3) for k in m.shape_keys.key_blocks} if m.shape_keys else {},
            'shape_metadata': [(k.name, k.relative_key.name, k.interpolation, k.vertex_group)
                               for k in m.shape_keys.key_blocks] if m.shape_keys else [],
            'weights': [[(obj.vertex_groups[g.group].name, float(g.weight)) for g in v.groups] for v in m.vertices],
            'vertex_normals': read_array(m.vertices, 'normal', 3),
            'polygon_normals': read_array(m.polygons, 'normal', 3),
            'corner_normals': read_array(m.corner_normals, 'vector', 3),
            'normal_metadata': {'hasCustomNormals': bool(m.has_custom_normals),
                                'packedAttributeType': m.attributes['custom_normal'].data_type if 'custom_normal' in m.attributes else None},
            'packed_custom_normals': (read_array(m.attributes['custom_normal'].data, 'value', 2, np.int32)
                                      if 'custom_normal' in m.attributes else np.empty((0, 2), np.int32)),
        }
    return meshes


class Glb:
    """glTF accessor reader, including byte strides, sparse data and normalization."""
    def __init__(self, path):
        raw = Path(path).read_bytes()
        magic, version, length = struct.unpack_from('<III', raw)
        if magic != 0x46546c67 or version != 2 or length != len(raw):
            raise ValueError('Invalid GLB header')
        self.document, self.binary = None, None
        offset = 12
        while offset < len(raw):
            length, kind = struct.unpack_from('<II', raw, offset)
            chunk = raw[offset + 8:offset + 8 + length]
            if kind == 0x4e4f534a:
                self.document = json.loads(chunk)
            elif kind == 0x004e4942:
                self.binary = chunk
            offset += length + 8
        if self.document is None or self.binary is None or offset != len(raw):
            raise ValueError('Invalid GLB chunks')

    def accessor(self, index):
        d = self.document
        a = d['accessors'][index]
        width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        types = {5120: 'i1', 5121: 'u1', 5122: '<i2', 5123: '<u2', 5125: '<u4', 5126: '<f4'}
        dtype = np.dtype(types[a['componentType']])

        def buffer(view, count, width, dtype, offset=0):
            v = d['bufferViews'][view]
            if v.get('buffer', 0) != 0:
                raise ValueError('External GLB buffers are unsupported')
            return np.ndarray((count, width), dtype=dtype, buffer=self.binary,
                              offset=v.get('byteOffset', 0) + offset,
                              strides=(v.get('byteStride', dtype.itemsize * width), dtype.itemsize)).copy()

        values = (buffer(a['bufferView'], a['count'], width, dtype, a.get('byteOffset', 0))
                  if 'bufferView' in a else np.zeros((a['count'], width), dtype))
        if 'sparse' in a:
            sp = a['sparse']; ix = sp['indices']; vv = sp['values']
            ids = buffer(ix['bufferView'], sp['count'], 1, np.dtype(types[ix['componentType']]),
                         ix.get('byteOffset', 0)).ravel()
            values[ids] = buffer(vv['bufferView'], sp['count'], width, dtype, vv.get('byteOffset', 0))
        if a.get('normalized'):
            values = np.maximum(values.astype(np.float64) / np.iinfo(dtype).max, -1)
        if not np.isfinite(values).all():
            raise ValueError('Non-finite GLB accessor')
        return values

    def meshes(self):
        out = {}
        for node in self.document['nodes']:
            if 'mesh' not in node:
                continue
            name = node.get('name')
            if name in out:
                raise ValueError('Duplicate exported mesh name: ' + str(name))
            m = self.document['meshes'][node['mesh']]
            primitives = []
            for p in m['primitives']:
                if p.get('mode', 4) != 4 or 'indices' not in p:
                    raise ValueError('Only indexed triangles are supported')
                indices = self.accessor(p['indices']).ravel().astype(np.int64)
                if len(indices) % 3:
                    raise ValueError('Incomplete GLB triangle')
                primitives.append({'indices': indices,
                                   'attributes': {k: self.accessor(v)[indices] for k, v in p['attributes'].items()},
                                   'targets': [{k: self.accessor(v)[indices] for k, v in t.items()}
                                               for t in p.get('targets', [])],
                                   'material': p.get('material'),
                                   'target_names': m.get('extras', {}).get('targetNames', [])})
            out[name] = primitives
        return out


def gltf_xyz(array):
    return array[:, [0, 2, 1]] * np.array([1, 1, -1], dtype=array.dtype)


def source_corner_mapping(mesh, attributes):
    """Map immutable source triangles once; never sort candidate positions.

    Source-only rest positions establish triangle membership; per-corner UVs
    disambiguate coincident positions. Ambiguous complete triangles fail closed.
    Returned indices are original Blender loop IDs in exported corner order.
    """
    loops = mesh['triangle_loops'].ravel()
    vertices = mesh['loop_vertices'][loops]
    src = gltf_xyz(mesh['positions'][vertices])
    got = attributes['POSITION']
    if len(src) != len(got):
        raise ValueError('Source/export triangle counts differ')

    def signatures(positions, uv):
        result = []
        for t in range(len(positions) // 3):
            keys = [tuple(np.round(np.concatenate((positions[3*t+c], uv[3*t+c])), 5)) for c in range(3)]
            order = sorted(range(3), key=lambda c: keys[c])
            if len(set(keys)) != 3:
                raise ValueError('Ambiguous source triangle corners')
            result.append((tuple(keys[c] for c in order), order))
        return result

    if not mesh['uv'] or 'TEXCOORD_0' not in attributes:
        raise ValueError('Source correspondence requires preserved primary UVs')
    uv = next(iter(mesh['uv'].values()))[loops].copy(); uv[:, 1] = 1 - uv[:, 1]
    source_keys = signatures(src, uv)
    lookup = {}
    for t, (key, order) in enumerate(source_keys):
        if key in lookup:
            raise ValueError('Ambiguous coincident source triangles')
        lookup[key] = (t, order)
    mapping = np.empty(len(got), np.int64)
    seen = set()
    for t, (key, order) in enumerate(signatures(got, attributes['TEXCOORD_0'])):
        if key not in lookup or key in seen:
            raise ValueError('Source/export triangle correspondence fails')
        seen.add(key)
        st, so = lookup[key]
        for ec, sc in zip(order, so):
            mapping[t*3+ec] = loops[st*3+sc]
    if len(seen) != len(lookup):
        raise ValueError('Unmatched source triangles')
    expected = gltf_xyz(mesh['positions'][mesh['loop_vertices'][mapping]])
    if np.max(np.abs(expected - got), initial=0) > EXPORT_TOLERANCE_M:
        raise ValueError('Source GLB positions do not match source Blend')
    return mapping


def audit_exports(source_meshes, candidate_meshes, mesh_reports, source_path, candidate_path):
    source, candidate = Glb(source_path).meshes(), Glb(candidate_path).meshes()
    failures = []
    reports = {}
    mesh_match = set(source) == set(candidate) == set(MESH_NAMES)
    if not mesh_match:
        failures.append('Export mesh set mismatch')
    for name in MESH_NAMES:
        if name not in source or name not in candidate or name not in mesh_reports:
            continue
        row = {'failures': []}
        try:
            # Current authored meshes have one material primitive each. Reject
            # structural changes instead of guessing a new correspondence.
            if len(source[name]) != 1 or len(candidate[name]) != 1:
                raise ValueError('Expected one primitive per component')
            a, b = source[name][0], candidate[name][0]
            sa, ca = a['attributes'], b['attributes']
            sm, cm = source_meshes[name], candidate_meshes[name]
            loop_ids = source_corner_mapping(sm, sa)
            vertex_ids = sm['loop_vertices'][loop_ids]
            if len(ca['POSITION']) != len(vertex_ids):
                raise ValueError('Candidate corner count changed')
            topology = (exact(a['indices'], b['indices']) and a['material'] == b['material'] and
                        set(sa) == set(ca))
            protected = topology
            errors = {}
            for key in sa:
                if key.startswith('TEXCOORD_') or key.startswith('COLOR_'):
                    protected &= exact(sa[key], ca[key])
                    if key.startswith('TEXCOORD_'):
                        index = int(key.split('_')[1])
                        uv_name = list(cm['uv'])[index]
                        expected = cm['uv'][uv_name][loop_ids].copy(); expected[:, 1] = 1 - expected[:, 1]
                        tolerance = EXPORT_TOLERANCE_M
                    else:
                        if key != 'COLOR_0' or 'Color' not in cm['colors']:
                            raise ValueError('Unrecognized exported color layer')
                        domain = next(d for n, d, _ in cm['color_metadata'] if n == 'Color')
                        expected = cm['colors']['Color'][loop_ids if domain == 'CORNER' else vertex_ids]
                        tolerance = .5 / 65535 + 1e-7
                    errors[key] = float(np.max(np.abs(expected - ca[key]), initial=0))
                    protected &= errors[key] <= tolerance
            if len([k for k in ca if k.startswith('TEXCOORD_')]) != len(cm['uv']):
                protected = False
            if 'Color' in cm['colors'] and 'COLOR_0' not in ca:
                protected = False
            expected = gltf_xyz(cm['positions'][vertex_ids])
            position_error = float(np.max(np.abs(expected - ca['POSITION']), initial=0))
            position_ok = position_error <= EXPORT_TOLERANCE_M
            original_expected = gltf_xyz(sm['positions'][vertex_ids])
            expected_delta = expected.astype(float) - original_expected.astype(float)
            actual_delta = ca['POSITION'].astype(float) - sa['POSITION'].astype(float)
            position_ok &= float(np.max(np.abs(actual_delta - expected_delta), initial=0)) <= EXPORT_TOLERANCE_M
            untouched = ~np.isin(vertex_ids, mesh_reports[name]['changedVertexIds'])
            untouched_position_ok = exact(sa['POSITION'][untouched], ca['POSITION'][untouched])
            skin_ok = all(exact(sa[key], ca[key]) for key in sa
                          if key.startswith('JOINTS_') or key.startswith('WEIGHTS_'))
            support = mesh_reports[name]['normalSupport']['cornerIds']
            outside = ~np.isin(loop_ids, support)
            normal_ok = ('NORMAL' in sa and 'NORMAL' in ca and exact(sa['NORMAL'][outside], ca['NORMAL'][outside]))
            morph_ok = (a['target_names'] == b['target_names'] == list(SHAPE_NAMES[1:]) and
                        len(a['targets']) == len(b['targets']) == 4)
            morph_errors = {}
            for index, (at, bt) in enumerate(zip(a['targets'], b['targets'])):
                key = SHAPE_NAMES[index + 1]
                if set(at) != set(bt) or 'POSITION' not in at or 'POSITION' not in bt:
                    morph_ok = False
                    continue
                expected = gltf_xyz((cm['shapes'][key] - cm['positions'])[vertex_ids])
                error = float(np.max(np.abs(expected - bt['POSITION']), initial=0))
                unchanged_delta_error = float(np.max(np.abs(at['POSITION'].astype(float) - bt['POSITION'].astype(float)), initial=0))
                morph_errors[key] = {'candidateBlendErrorM': error, 'sourceDeltaDifferenceM': unchanged_delta_error}
                morph_ok &= (error <= EXPORT_TOLERANCE_M and unchanged_delta_error <= MORPH_TOLERANCE_M
                             and exact(at['POSITION'][untouched], bt['POSITION'][untouched]))
                for attr in ('NORMAL', 'TANGENT'):
                    if attr in at:
                        normal_ok &= exact(at[attr][outside], bt[attr][outside])
            row.update(cornerCount=len(vertex_ids), triangleCount=len(vertex_ids)//3,
                       indexedTopologyExact=bool(topology), protectedAttributesExact=bool(protected),
                       positionsMatchCandidate=bool(position_ok), maxPositionErrorM=position_error,
                       untouchedPositionsExact=bool(untouched_position_ok), skinAttributesExact=bool(skin_ok),
                       maxProtectedAttributeError=errors, morphDeltasPreserved=bool(morph_ok),
                       maxMorphErrors=morph_errors, normalsOutsideSupportExact=bool(normal_ok),
                       sourceCornerLoopIdSha256=array_hash(loop_ids),
                       correspondenceMethod='Immutable source triangle mapping, then same indexed-corner order; candidate positions never determine matching.')
            for key, value in [('indexedTopologyExact', topology), ('protectedAttributesExact', protected),
                               ('positionsMatchCandidate', position_ok), ('morphDeltasPreserved', morph_ok),
                               ('untouchedPositionsExact', untouched_position_ok), ('skinAttributesExact', skin_ok),
                               ('normalsOutsideSupportExact', normal_ok)]:
                if not value:
                    row['failures'].append(name + ': export ' + key)
        except (ValueError, KeyError, IndexError, TypeError) as exc:
            row['failures'].append(name + ': export audit: ' + str(exc))
        row['pass'] = not row['failures']
        failures.extend(row['failures'])
        reports[name] = row
    return {'pass': not failures, 'meshSetMatches': mesh_match, 'meshes': reports, 'failures': failures}


def audit_glb_region(source_path, candidate_path):
    """Recompute the live indexed-corner contract without Blender or proof JSON.

    The Blender audit records exact original mesh IDs. This independent export
    check uses immutable source rest positions to join UV-split copies of a
    vertex when finding normal support; coincident vertices share that support.
    Candidate positions never establish correspondence or eligibility.
    """
    source, candidate = Glb(source_path).meshes(), Glb(candidate_path).meshes()
    failures = []
    reports = {}
    if set(source) != set(candidate) or set(source) != set(MESH_NAMES):
        failures.append('Export mesh set mismatch')
    for name in MESH_NAMES:
        row = {'failures': []}

        def check(key, value):
            row[key] = bool(value)
            if not value:
                row['failures'].append(name + ': ' + key)

        try:
            if name not in source or name not in candidate:
                raise ValueError('Missing exported component')
            if len(source[name]) != 1 or len(candidate[name]) != 1:
                raise ValueError('Expected one primitive per component')
            a, b = source[name][0], candidate[name][0]
            sa, ca = a['attributes'], b['attributes']
            check('indexedTopologyExact', exact(a['indices'], b['indices']) and
                  a['material'] == b['material'] and set(sa) == set(ca))
            check('protectedAttributesExact', set(sa) == set(ca) and
                  all(exact(sa[k], ca[k]) for k in sa if k not in ('POSITION', 'NORMAL', 'TANGENT')))
            before, after = sa['POSITION'], ca['POSITION']
            if before.shape != after.shape:
                raise ValueError('Exported position dimensions changed')
            delta = after.astype(float) - before.astype(float)
            changed = np.any(before != after, axis=1)
            original_blender = before[:, [0, 2, 1]] * np.array([1, -1, 1], dtype=before.dtype)
            allowed = allowed_vertices(original_blender) if name == 'Body' else np.zeros(len(before), bool)
            check('positionsWithinAuthorizedRegion', not np.any(changed & ~allowed))
            check('untouchedPositionsExact', exact(before[~allowed], after[~allowed]))
            lengths = np.linalg.norm(delta, axis=1)
            row['maxOffsetM'] = float(np.max(lengths, initial=0))
            row['changedCornerCount'] = int(changed.sum())
            row['allPositionsExact'] = exact(before, after)
            check('maxOffsetWithinLimit', row['maxOffsetM'] <= MAX_OFFSET_M + MORPH_TOLERANCE_M)
            # The immutable source is expanded into complete triangles. A face
            # normal is affected only when that face contains an edited point.
            face_ids = np.flatnonzero(np.any(changed.reshape(-1, 3), axis=1))
            face_corners = (face_ids[:, None] * 3 + np.arange(3)).ravel()
            support_points = {tuple(point) for point in before[face_corners]}
            normal_support = np.array([tuple(point) in support_points for point in before])
            outside = ~normal_support
            normal_ok = 'NORMAL' in sa and 'NORMAL' in ca and exact(sa['NORMAL'][outside], ca['NORMAL'][outside])
            if 'TANGENT' in sa:
                normal_ok &= 'TANGENT' in ca and exact(sa['TANGENT'][outside], ca['TANGENT'][outside])
            morph_ok = (a['target_names'] == b['target_names'] == list(SHAPE_NAMES[1:]) and
                        len(a['targets']) == len(b['targets']) == 4)
            errors = {}
            for key, at, bt in zip(SHAPE_NAMES[1:], a['targets'], b['targets']):
                if set(at) != set(bt) or 'POSITION' not in at:
                    morph_ok = False
                    continue
                error = float(np.max(np.abs(bt['POSITION'].astype(float) - at['POSITION'].astype(float)), initial=0))
                errors[key] = error
                morph_ok &= error <= MORPH_TOLERANCE_M and exact(at['POSITION'][~changed], bt['POSITION'][~changed])
                for attr in ('NORMAL', 'TANGENT'):
                    if attr in at:
                        normal_ok &= exact(at[attr][outside], bt[attr][outside])
            check('morphDeltasPreserved', morph_ok)
            check('normalsOutsideSupportExact', normal_ok)
            row.update(normalSupportTriangleIds=face_ids.tolist(),
                       normalSupportCornerIds=np.flatnonzero(normal_support).tolist(),
                       maxMorphDeltaDifferenceM=errors)
        except (ValueError, KeyError, IndexError, TypeError) as exc:
            row['failures'].append(name + ': GLB region audit: ' + str(exc))
        row['pass'] = not row['failures']
        failures.extend(row['failures'])
        reports[name] = row
    return {'pass': not failures, 'failures': failures, 'meshes': reports,
            'sourceGlbSha256': sha(source_path), 'candidateGlbSha256': sha(candidate_path)}


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-blend', type=Path, required=True)
    p.add_argument('--candidate-blend', type=Path, required=True)
    p.add_argument('--source-glb', type=Path)
    p.add_argument('--candidate-glb', type=Path)
    p.add_argument('--out', type=Path, required=True)
    args = p.parse_args(argv)
    args.source_glb = args.source_glb or args.source_blend.with_suffix('.glb')
    args.candidate_glb = args.candidate_glb or args.candidate_blend.with_suffix('.glb')
    paths = {'sourceBlendSha256': args.source_blend, 'candidateBlendSha256': args.candidate_blend,
             'sourceGlbSha256': args.source_glb, 'candidateGlbSha256': args.candidate_glb}
    initial_hashes = {key: sha(path) for key, path in paths.items()}
    source, candidate = snapshot_blend(args.source_blend), snapshot_blend(args.candidate_blend)
    mesh_match = set(source) == set(candidate) == set(MESH_NAMES)
    failures = [] if mesh_match else ['Blend mesh set mismatch']
    meshes = {}
    for name in MESH_NAMES:
        if name in source and name in candidate:
            row = compare_mesh(name, source[name], candidate[name])
            row['pass'] = row.pop('pass_')
            meshes[name] = row
            failures.extend(row['failures'])
    exports = audit_exports(source, candidate, meshes, args.source_glb, args.candidate_glb)
    failures.extend(exports['failures'])
    live_exports = audit_glb_region(args.source_glb, args.candidate_glb)
    failures.extend(live_exports['failures'])
    if initial_hashes != {key: sha(path) for key, path in paths.items()}:
        failures.append('Source or candidate changed during read-only audit')
    report = {
        'schema': SCHEMA,
        **initial_hashes,
        'validatorSha256': sha(__file__), 'meshSetMatches': mesh_match,
        'limits': {'mesh': 'Body', 'sourceRestSpace': 'Blender object-local meters',
                   'sourceXExclusiveMaximumM': -.24, 'sourceZExclusiveMinimumM': .40,
                   'sourceZExclusiveMaximumM': .91, 'maxOffsetM': MAX_OFFSET_M,
                   'floatQuantizationToleranceM': MORPH_TOLERANCE_M,
                   'morphDeltaToleranceM': MORPH_TOLERANCE_M,
                   'exportPositionToleranceM': EXPORT_TOLERANCE_M},
        'meshes': meshes, 'exportAudit': exports, 'liveGlbRegionAudit': live_exports,
        'liveGlbRegionPass': live_exports['pass'], 'failures': failures,
        'scope': 'Only localized original-rest Body geometry may change. All four shape keys receive the same displacement as Basis. Normal changes are allowed only in explicit geometric support. Physical, contact, attachment, stretch, gait and visual acceptance gates are separate and unchanged.',
    }
    for key in ('topologyExact', 'uvExact', 'colorsExact', 'transformsExact', 'weightsExact',
                'untouchedPositionsExact', 'positionsWithinAuthorizedRegion', 'maxOffsetWithinLimit',
                'morphDeltasPreserved', 'untouchedShapePositionsExact', 'normalsOutsideSupportExact',
                'rawPackedNormalsOutsideSupportExact', 'allPositionsExact'):
        report[key] = mesh_match and all(row.get(key, False) for row in meshes.values())
    for top, key in [('exportCornerCorrespondenceExact', 'indexedTopologyExact'),
                     ('exportProtectedAttributesExact', 'protectedAttributesExact'),
                     ('exportPositionsMatchCandidate', 'positionsMatchCandidate'),
                     ('exportUntouchedPositionsExact', 'untouchedPositionsExact'),
                     ('exportSkinAttributesExact', 'skinAttributesExact'),
                     ('exportMorphDeltasPreserved', 'morphDeltasPreserved'),
                     ('exportNormalsOutsideSupportExact', 'normalsOutsideSupportExact')]:
        report[top] = exports['meshSetMatches'] and all(row.get(key, False) for row in exports['meshes'].values())
    report['changedVertexCount'] = sum(row['changedVertexCount'] for row in meshes.values())
    report['maxOffsetM'] = max((row['maxOffsetM'] for row in meshes.values()), default=0)
    report['pass'] = not failures
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    print(json.dumps({k: report[k] for k in ('schema', 'pass', 'allPositionsExact', 'changedVertexCount', 'maxOffsetM', 'failures')}, indent=2))
    return 0 if report['pass'] else 1


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    raise SystemExit(main(argv))
