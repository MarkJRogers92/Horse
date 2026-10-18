import pathlib,struct,json,unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
class TroughTests(unittest.TestCase):
 def test_registered_as_separate_decor(self):
  source=(ROOT/'game/scripts/Stable/StableYard.cs').read_text()
  self.assertIn('Place("feed_trough", 0.85f, new Vector3(-1.8f, 0.31f, -13.1f)',source)
  self.assertIn('Place("water_trough"',source)
  self.assertIn('Place("stable_bucket", 0.60f, new Vector3(-2.9f, 0.31f, -12.1f)',source)
 def test_mesh_budget_and_embedded_texture(self):
  p=ROOT/'game/assets/scenery/feed_trough/feed_trough.glb'
  self.assertTrue(p.exists())
  b=p.read_bytes();self.assertEqual(b[:4],b'glTF')
  size=struct.unpack_from('<I',b,12)[0];doc=json.loads(b[20:20+size])
  tris=sum(doc['accessors'][p['indices']]['count']//3 for m in doc['meshes'] for p in m['primitives'])
  self.assertEqual(tris,4428);self.assertEqual(len(doc['materials']),2)
  self.assertTrue(all('bufferView' in i for i in doc['images']))
if __name__=='__main__':unittest.main()
