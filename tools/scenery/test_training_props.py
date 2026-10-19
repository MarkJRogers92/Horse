"""Offline contracts for the authored, metre-scale practice equipment."""
import json
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[2]
ASSETS = {
    'ground_pole': (764, 0.11),
    'low_cavaletti': (1196, 0.444),
    'course_cone': (1000, 0.5),
    'training_jump': (7340, None),
    'training_barrel': (1724, 0.9),
}

class TrainingAssetTests(unittest.TestCase):
    def test_self_contained_meshes_and_budgets(self):
        for name, (triangles, _) in ASSETS.items():
            with self.subTest(asset=name):
                path = ROOT / f'game/assets/scenery/{name}/{name}.glb'
                self.assertTrue(path.is_file(), f'Missing practice asset: {name}')
                data = path.read_bytes()
                self.assertEqual(data[:4], b'glTF')
                self.assertEqual(struct.unpack_from('<I', data, 8)[0], len(data))
                length = struct.unpack_from('<I', data, 12)[0]
                doc = json.loads(data[20:20 + length])
                self.assertTrue(all('uri' not in b for b in doc['buffers']))
                self.assertFalse(doc.get('images'))
                self.assertFalse(doc.get('animations'))
                actual = sum(doc['accessors'][p['indices']]['count'] // 3
                             for mesh in doc['meshes'] for p in mesh['primitives'])
                self.assertEqual(actual, triangles)
                self.assertEqual(len(doc['materials']), 3)

if __name__ == '__main__':
    unittest.main()
