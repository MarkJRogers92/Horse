"""Independent bounded-geometry regressions; these never replace physical gates."""
import copy
import json
from pathlib import Path
import struct
import tempfile
import unittest

import numpy as np

from audit_foal_geometry_region import (
    MESH_NAMES, SHAPE_NAMES, audit_glb_region, compare_mesh, gltf_xyz,
)

ROOT = Path(__file__).resolve().parents[2]


def mesh_fixture():
    # First and second triangles share a neighbor of edited vertex 0. The third
    # triangle is disconnected, exposing accidental global normal modifications.
    xyz = np.array([[-.50, .10, .60], [-.48, .15, .55], [-.43, .12, .58],
                    [-.40, .18, .54], [-.37, .12, .50], [.20, .10, .30],
                    [.25, .15, .35], [.30, .12, .30]], dtype=np.float32)
    loops = np.array([0, 1, 2, 1, 3, 4, 5, 6, 7], np.int32)
    edges = np.array([[0, 1], [1, 2], [2, 0], [1, 3], [3, 4], [4, 1],
                      [5, 6], [6, 7], [7, 5]], np.int32)
    uv = np.arange(18, dtype=np.float32).reshape(9, 2) / 20
    return {
        'positions': xyz, 'edges': edges, 'loop_vertices': loops,
        'loop_edges': np.arange(9, dtype=np.int32),
        'polygon_ranges': np.array([[0, 3], [3, 3], [6, 3]], np.int32),
        'polygon_materials': np.zeros(3, np.int32),
        'polygon_smooth': np.ones(3, bool),
        'triangle_loops': np.arange(9, dtype=np.int32).reshape(3, 3),
        'matrix_world': np.eye(4), 'matrix_parent_inverse': np.eye(4),
        'uv': {'UVMap': uv}, 'uv_metadata': [('UVMap', True, True, True)],
        'colors': {'Color': np.ones((9, 4), np.float32)},
        'color_metadata': [('Color', 'CORNER', 'FLOAT_COLOR')],
        'shapes': {key: xyz.copy() + (0 if key == 'Basis' else .01 * i)
                   for i, key in enumerate(SHAPE_NAMES)},
        'shape_metadata': [(key, 'Basis', 'KEY_LINEAR', '') for key in SHAPE_NAMES],
        'weights': [[('Bone', 1.)] for _ in xyz],
        'vertex_normals': np.tile(np.array([0, 0, 1], np.float32), (8, 1)),
        'polygon_normals': np.tile(np.array([0, 0, 1], np.float32), (3, 1)),
        'corner_normals': np.tile(np.array([0, 0, 1], np.float32), (9, 1)),
        'normal_metadata': {'hasCustomNormals': True, 'packedAttributeType': 'INT16_2D'},
        'packed_custom_normals': np.ones((9, 2), np.int32),
    }


def move(mesh, vertex=0, dx=.02):
    mesh['positions'][vertex, 0] += dx
    for key in mesh['shapes'].values():
        key[vertex, 0] += dx


def write_glb(path, meshes, mutate=None):
    """Tiny real GLBs exercise parsing/corner validation, not precomputed flags."""
    doc = {'asset': {'version': '2.0'}, 'buffers': [], 'bufferViews': [],
           'accessors': [], 'meshes': [], 'nodes': []}
    blob = bytearray()

    def accessor(values):
        values = np.asarray(values)
        integer = values.dtype.kind in 'ui'
        values = values.astype('<u4' if integer else '<f4')
        width = values.shape[1] if values.ndim > 1 else 1
        while len(blob) % 4:
            blob.append(0)
        offset = len(blob); blob.extend(values.tobytes())
        vi = len(doc['bufferViews'])
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': values.nbytes})
        ai = len(doc['accessors'])
        doc['accessors'].append({'bufferView': vi, 'componentType': 5125 if integer else 5126,
                                 'count': len(values), 'type': {1: 'SCALAR', 2: 'VEC2', 3: 'VEC3', 4: 'VEC4'}[width]})
        return ai

    for name, mesh in meshes.items():
        ids = mesh['loop_vertices'][mesh['triangle_loops'].ravel()]
        uv = mesh['uv']['UVMap'].copy(); uv[:, 1] = 1 - uv[:, 1]
        attrs = {'POSITION': gltf_xyz(mesh['positions'][ids]),
                 'NORMAL': gltf_xyz(mesh['corner_normals']), 'TEXCOORD_0': uv,
                 'COLOR_0': mesh['colors']['Color'].copy()}
        targets = [{'POSITION': gltf_xyz((mesh['shapes'][key] - mesh['positions'])[ids]),
                    'NORMAL': np.zeros((len(ids), 3), np.float32)} for key in SHAPE_NAMES[1:]]
        indices = np.arange(len(ids), dtype=np.uint32)
        if mutate:
            mutate(name, attrs, targets, indices)
        p = {'indices': accessor(indices), 'attributes': {k: accessor(v) for k, v in attrs.items()},
             'targets': [{k: accessor(v) for k, v in t.items()} for t in targets]}
        doc['nodes'].append({'name': name, 'mesh': len(doc['meshes'])})
        doc['meshes'].append({'primitives': [p], 'extras': {'targetNames': list(SHAPE_NAMES[1:])}})
    doc['buffers'].append({'byteLength': len(blob)})
    text = json.dumps(doc).encode()
    text += b' ' * (-len(text) % 4)
    blob.extend(b'\0' * (-len(blob) % 4))
    raw = struct.pack('<III', 0x46546c67, 2, 12 + 8 + len(text) + 8 + len(blob))
    raw += struct.pack('<II', len(text), 0x4e4f534a) + text
    raw += struct.pack('<II', len(blob), 0x004e4942) + blob
    path.write_bytes(raw)


class BoundedGeometryCoreTests(unittest.TestCase):
    def setUp(self):
        self.source = mesh_fixture()
        self.candidate = copy.deepcopy(self.source)
        move(self.candidate)

    def result(self):
        return compare_mesh('Body', self.source, self.candidate)

    def test_valid_local_edit_discloses_inexact_positions(self):
        result = self.result()
        self.assertTrue(result['pass_'], result['failures'])
        self.assertFalse(result['allPositionsExact'])
        self.assertEqual(result['changedVertexIds'], [0])
        self.assertEqual(result['normalSupport'], {'faceIds': [0], 'vertexIds': [0, 1, 2],
                                                   'cornerIds': [0, 1, 2, 3]})

    def test_outside_box_and_other_components_are_rejected(self):
        move(self.candidate, 5)
        self.assertFalse(self.result()['positionsWithinAuthorizedRegion'])
        result = compare_mesh('Eyes', self.source, self.candidate)
        self.assertFalse(result['pass_'])

    def test_original_coordinates_determine_eligibility(self):
        for axis, value in [(0, -.24), (2, .40), (2, .91)]:
            with self.subTest(axis=axis, value=value):
                source = mesh_fixture()
                for positions in [source['positions'], *source['shapes'].values()]:
                    positions[0, axis] = value
                candidate = copy.deepcopy(source)
                # A candidate cannot move an unauthorized source point into the box.
                move(candidate, dx=-.03)
                self.assertFalse(compare_mesh('Body', source, candidate)['pass_'])

    def test_overlarge_offset_rejected(self):
        move(self.candidate, dx=.08)
        self.assertFalse(self.result()['maxOffsetWithinLimit'])

    def test_uv_color_and_topology_changes_rejected(self):
        for category in ['uv', 'colors', 'edges']:
            with self.subTest(category=category):
                candidate = copy.deepcopy(self.candidate)
                if category == 'uv':
                    candidate['uv']['UVMap'][5, 0] += .01
                elif category == 'colors':
                    candidate['colors']['Color'][6, 0] -= .01
                else:
                    candidate['edges'][0, 0] = 2
                self.assertFalse(compare_mesh('Body', self.source, candidate)['pass_'])

    def test_morph_only_offset_and_missing_shape_rejected(self):
        self.candidate['shapes']['muscle'][0, 0] += .001
        self.assertFalse(self.result()['morphDeltasPreserved'])
        self.candidate = copy.deepcopy(self.source)
        self.candidate['shapes']['muscle'][5, 0] += .001
        self.assertFalse(self.result()['untouchedShapePositionsExact'])
        self.candidate = copy.deepcopy(self.source)
        del self.candidate['shapes']['muscle']
        self.assertFalse(self.result()['shapeKeyStructureExact'])

    def test_normal_support_is_local_and_explicit(self):
        self.candidate['corner_normals'][3] = [.1, 0, .99]
        self.candidate['packed_custom_normals'][3] = 0
        self.assertTrue(self.result()['pass_'])
        for category, index in [('corner_normals', 4), ('vertex_normals', 3),
                                ('polygon_normals', 1), ('packed_custom_normals', 4)]:
            with self.subTest(category=category):
                candidate = copy.deepcopy(self.candidate)
                candidate[category][index, 0] = 5
                self.assertFalse(compare_mesh('Body', self.source, candidate)['normalsOutsideSupportExact'])

    def test_nonfinite_and_truncated_geometry_rejected(self):
        self.candidate['positions'][0, 0] = np.nan
        self.assertFalse(self.result()['pass_'])
        self.candidate['positions'] = self.candidate['positions'][:-1]
        self.assertFalse(self.result()['pass_'])


class BoundedGeometryGlbTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.source_path = Path(self.folder.name) / 'source.glb'
        self.candidate_path = Path(self.folder.name) / 'candidate.glb'
        self.source = {n: mesh_fixture() for n in MESH_NAMES}
        self.candidate = copy.deepcopy(self.source)
        move(self.candidate['Body'])
        write_glb(self.source_path, self.source)

    def audit(self, mutate=None):
        write_glb(self.candidate_path, self.candidate, mutate)
        return audit_glb_region(self.source_path, self.candidate_path)

    def test_local_export_changes_pass(self):
        result = self.audit()
        self.assertTrue(result['pass'], result['failures'])
        self.assertFalse(result['meshes']['Body']['allPositionsExact'])

    def test_outside_box_change_rejected_even_under_export_tolerance(self):
        def mutate(name, attrs, targets, indices):
            if name == 'Body':
                attrs['POSITION'][6, 0] += 1e-6
        self.assertFalse(self.audit(mutate)['pass'])

    def test_uv_color_topology_morph_and_remote_normals_rejected(self):
        for category in ['uv', 'color', 'indices', 'morph', 'normal']:
            with self.subTest(category=category):
                def mutate(name, attrs, targets, indices):
                    if name != 'Body':
                        return
                    if category == 'uv': attrs['TEXCOORD_0'][0, 0] += .01
                    elif category == 'color': attrs['COLOR_0'][0, 0] -= .01
                    elif category == 'indices': indices[[0, 1]] = indices[[1, 0]]
                    elif category == 'morph': targets[0]['POSITION'][0, 0] += .001
                    else: attrs['NORMAL'][6] = [1, 0, 0]
                self.assertFalse(self.audit(mutate)['pass'])

    def test_live_final_candidate_from_actual_glb_arrays(self):
        source = ROOT / 'art/derived/production/candidates/foal-walk-stifle/horse_foal_walk.glb'
        candidate = ROOT / 'art/derived/production/candidates/foal-walk-hind-geometry/horse_foal_walk.glb'
        # Required assets: no skip and no pre-recorded passing flags.
        self.assertTrue(source.is_file())
        self.assertTrue(candidate.is_file())
        result = audit_glb_region(source, candidate)
        self.assertTrue(result['pass'], result['failures'])
        body = result['meshes']['Body']
        self.assertFalse(body['allPositionsExact'])
        self.assertGreater(body['changedCornerCount'], 0)
        self.assertGreater(body['maxOffsetM'], .01)
        self.assertLessEqual(body['maxOffsetM'], .09 + 1e-7)
        for name in ('Eyes', 'Mane', 'Tail'):
            self.assertTrue(result['meshes'][name]['allPositionsExact'])


if __name__ == '__main__':
    unittest.main()
