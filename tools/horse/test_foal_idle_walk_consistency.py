"""Live GLB and adversarial regressions; Python/NumPy only, no Blender required."""
import copy
import json
from pathlib import Path
import struct
import tempfile
import unittest

import numpy as np

from audit_foal_idle_consistency import (
    CANDIDATE, FOUNDATION, MESH_NAMES, MORPH_TOLERANCE_M, ROOT, SHAPE_NAMES,
    WALK, Glb, audit_glbs, sha,
)

SOURCE = FOUNDATION / 'horse_foal.glb'
IDLE = CANDIDATE / 'horse_foal.glb'
ACCEPTED_WALK = WALK / 'horse_foal_walk.glb'


def mutate_glb(source, target, edit):
    """Edit actual decoded GLB data, then repack a real GLB for the auditor."""
    glb = Glb(source)
    doc, blob = copy.deepcopy(glb.document), bytearray(glb.binary)

    def replace(index, values):
        values = np.asarray(values)
        integer = values.dtype.kind in 'ui'
        values = values.astype('<u4' if integer else '<f4')
        blob.extend(b'\0' * (-len(blob) % 4))
        view = len(doc['bufferViews'])
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': len(blob), 'byteLength': values.nbytes})
        blob.extend(values.tobytes())
        accessor = doc['accessors'][index]
        for key in ('sparse', 'normalized', 'byteOffset', 'min', 'max'):
            accessor.pop(key, None)
        accessor.update(bufferView=view, componentType=5125 if integer else 5126,
                        count=len(values))

    edit(doc, glb, replace)
    doc['buffers'][0]['byteLength'] = len(blob)
    raw_json = json.dumps(doc, allow_nan=False).encode()
    raw_json += b' ' * (-len(raw_json) % 4)
    blob.extend(b'\0' * (-len(blob) % 4))
    size = 12 + 8 + len(raw_json) + 8 + len(blob)
    target.write_bytes(struct.pack('<III', 0x46546c67, 2, size) +
                       struct.pack('<II', len(raw_json), 0x4e4f534a) + raw_json +
                       struct.pack('<II', len(blob), 0x004e4942) + blob)


def primitive(doc, name='Body'):
    node = next(n for n in doc['nodes'] if n.get('name') == name)
    return doc['meshes'][node['mesh']]['primitives'][0]


def change_array(select, amount=.001):
    def edit(doc, glb, replace):
        index = select(doc)
        values = glb.accessor(index).copy()
        values[0, 0] += amount
        replace(index, values)
    return edit


class FoalIdleWalkLiveTests(unittest.TestCase):
    def test_live_appearance_and_original_idle_are_preserved(self):
        result = audit_glbs(SOURCE, IDLE, ACCEPTED_WALK)
        self.assertTrue(result['pass'], result['failures'])
        for key in ('glbRestPositionsExact', 'glbIndexedTopologyExact', 'glbUvExact',
                    'glbColorsExact', 'glbNormalsExact', 'glbMorphTargetsPreserved'):
            self.assertTrue(result[key], key)
        for mesh in result['meshes'].values():
            self.assertTrue(mesh['morphPositionsExact'])
            self.assertTrue(mesh['morphNormalsExact'])
        self.assertEqual(result['staticPreservation']['idleAnimationArrayCount'], 384)
        self.assertEqual(result['sourceGlbSha256'], sha(SOURCE))

    def test_old_static_shape_is_rejected_against_corrected_walk(self):
        result = audit_glbs(SOURCE, SOURCE, ACCEPTED_WALK)
        self.assertFalse(result['pass'])
        self.assertFalse(result['glbRestPositionsExact'])

    def test_intentional_cross_mode_rig_differences_are_not_surface_failures(self):
        idle, walk = Glb(IDLE), Glb(ACCEPTED_WALK)
        self.assertNotEqual(idle.document['nodes'], walk.document['nodes'])
        self.assertNotEqual(set(idle.meshes()['Body'][0]['attributes']),
                            set(walk.meshes()['Body'][0]['attributes']))
        self.assertTrue(audit_glbs(SOURCE, IDLE, ACCEPTED_WALK)['pass'])


class FoalIdleWalkMutationTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.path = Path(folder.name) / 'mutated.glb'

    def audit(self, edit, mutate_walk=False):
        mutate_glb(ACCEPTED_WALK if mutate_walk else IDLE, self.path, edit)
        return audit_glbs(SOURCE, IDLE if mutate_walk else self.path,
                          self.path if mutate_walk else ACCEPTED_WALK)

    def test_changed_rest_position_rejected(self):
        result = self.audit(change_array(lambda d: primitive(d)['attributes']['POSITION']))
        self.assertFalse(result['pass'])
        self.assertFalse(result['glbRestPositionsExact'])

    def test_each_uv_set_rejected(self):
        for name, uv in [('Body', 'TEXCOORD_0'), ('Body', 'TEXCOORD_1'),
                         ('Eyes', 'TEXCOORD_1'), ('Mane', 'TEXCOORD_0'), ('Tail', 'TEXCOORD_0')]:
            with self.subTest(name=name, uv=uv):
                result = self.audit(change_array(lambda d: primitive(d, name)['attributes'][uv]))
                self.assertFalse(result['pass'])
                self.assertFalse(result['glbUvExact'])

    def test_changed_coat_mask_rejected(self):
        result = self.audit(change_array(lambda d: primitive(d)['attributes']['COLOR_0']))
        self.assertFalse(result['pass'])
        self.assertFalse(result['glbColorsExact'])

    def test_all_four_morph_displacements_rejected_when_divergent(self):
        for i, name in enumerate(SHAPE_NAMES[1:]):
            with self.subTest(morph=name):
                result = self.audit(change_array(lambda d: primitive(d)['targets'][i]['POSITION']))
                self.assertFalse(result['pass'])
                self.assertFalse(result['glbMorphTargetsPreserved'])

    def test_sub_tolerance_morph_displacement_is_disclosed(self):
        result = self.audit(change_array(lambda d: primitive(d, 'Tail')['targets'][0]['POSITION'],
                                        MORPH_TOLERANCE_M / 2), mutate_walk=True)
        self.assertTrue(result['pass'], result['failures'])
        self.assertFalse(result['meshes']['Tail']['morphPositionsExact'])
        self.assertLessEqual(result['meshes']['Tail']['maxMorphPositionDifferenceM']['body_length'], MORPH_TOLERANCE_M)

    def test_rest_and_morph_normals_rejected(self):
        for select in [lambda d: primitive(d)['attributes']['NORMAL'],
                       lambda d: primitive(d)['targets'][3]['NORMAL']]:
            with self.subTest(select=select):
                self.assertFalse(self.audit(change_array(select))['pass'])

    def test_missing_shape_target_rejected(self):
        def edit(doc, glb, replace):
            primitive(doc)['targets'].pop()
        self.assertFalse(self.audit(edit)['pass'])

    def test_nonfinite_surface_rejected(self):
        result = self.audit(change_array(lambda d: primitive(d)['attributes']['POSITION'], np.nan))
        self.assertFalse(result['pass'])
        self.assertIn('Non-finite GLB accessor', str(result['failures']))

    def test_nonfinite_idle_animation_rejected(self):
        self.assertFalse(self.audit(change_array(lambda d: d['animations'][0]['samplers'][0]['output'], np.nan))['pass'])

    def test_idle_animation_times_and_values_rejected(self):
        for key in ('input', 'output'):
            with self.subTest(array=key):
                result = self.audit(change_array(lambda d: d['animations'][0]['samplers'][0][key]))
                self.assertFalse(result['pass'])
                self.assertFalse(result['staticPreservation']['idleAnimationArraysExact'])

    def test_idle_channel_metadata_rejected(self):
        for key, value in [('interpolation', 'STEP'), ('name', 'Walk')]:
            def edit(doc, glb, replace):
                target = doc['animations'][0]['samplers'][0] if key == 'interpolation' else doc['animations'][0]
                target[key] = ('LINEAR' if target.get(key) == 'STEP' else 'STEP') if key == 'interpolation' else value
            with self.subTest(field=key):
                self.assertFalse(self.audit(edit)['pass'])

    def test_node_transform_rejected(self):
        def edit(doc, glb, replace):
            doc['nodes'][0]['translation'] = [.01, 0, 0]
        self.assertFalse(self.audit(edit)['staticPreservation']['nodesAndMatricesExact'])

    def test_inverse_bind_and_skin_weight_changes_rejected(self):
        for select, flag in [(lambda d: d['skins'][0]['inverseBindMatrices'], 'inverseBindMatricesExact'),
                             (lambda d: primitive(d)['attributes']['WEIGHTS_0'], 'skinAttributesExact')]:
            with self.subTest(field=flag):
                result = self.audit(change_array(select))
                self.assertFalse(result['pass'])
                self.assertFalse(result['staticPreservation'][flag])

    def test_material_change_rejected(self):
        def edit(doc, glb, replace):
            doc['materials'][0]['pbrMetallicRoughness']['roughnessFactor'] = .1
        self.assertFalse(self.audit(edit)['pass'])

    def test_invalid_and_reordered_indices_rejected(self):
        for invalid in (False, True):
            def edit(doc, glb, replace):
                index = primitive(doc)['indices']
                indices = glb.accessor(index).astype(np.uint32)
                if invalid:
                    indices[0, 0] = 2**31
                else:
                    indices[[0, 1]] = indices[[1, 0]]
                replace(index, indices)
            with self.subTest(invalid=invalid):
                self.assertFalse(self.audit(edit)['pass'])


if __name__ == '__main__':
    unittest.main()
