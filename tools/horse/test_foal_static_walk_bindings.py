"""Active corrected foal Idle/Walk proof bindings, separate from shape math."""
import json,hashlib,unittest
from pathlib import Path
R=Path(__file__).resolve().parents[2]
read=lambda p:json.loads(p.read_text())
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
resource=lambda p:R/'game'/p.removeprefix('res://')
class FoalStaticBindingsTests(unittest.TestCase):
 def test_both_catalogs_and_families_bind_same_corrected_surface(self):
  expected=None;code=(R/'game/scripts/Visual/FoalRestGeometryEvidence.cs').read_text()
  for name in ['visual_catalog.json','visual_catalog_light_walk_preview.json']:
   families=read(R/'game/data'/name)['meshFamilies']
   for family in ['horse.standard.foal.v1','horse.compact.foal.v1']:
    value=families[family];binding=value['restGeometry']
    if expected is None:expected=binding
    self.assertEqual(binding,expected)
    self.assertEqual(sha(resource(value['scene'])),binding['sourceGlbSha256'])
    self.assertIn(binding['sourceGlbSha256'],code)
    for proof in ['region','consistency','morph','import']:
     p=resource(binding[proof+'ProofPath']);self.assertEqual(sha(p),binding[proof+'ProofSha256']);self.assertIn(sha(p),code)
 def test_game_and_editable_candidate_are_honestly_bound(self):
  c=R/'art/derived/production/candidates/foal-idle-hind-geometry';g=R/'game/assets/horse/production'
  self.assertEqual((c/'horse_foal.glb').read_bytes(),(g/'horse_foal.glb').read_bytes())
  m=read(g/'horse_foal.manifest.json');self.assertFalse(m['protectedMeshDataExact'])
  self.assertEqual(sha(R/m['canonicalEditableBlend']),m['candidateBlendSha256'])
  self.assertEqual(sha(g/'horse_foal.glb'),m['candidateGlbSha256'])
  self.assertEqual(sha(R/m['parentManifestPath']),m['parentManifestSha256'])
  region=read(c/'bounded-geometry-audit.json');self.assertTrue(region['pass']);self.assertFalse(region['allPositionsExact']);self.assertEqual(region['changedVertexCount'],1361)
  proof=read(c/'idle-walk-consistency.json');self.assertTrue(proof['pass']);self.assertEqual(proof['failures'],[])
 def test_import_and_full_morph_cases_keep_original_gates(self):
  e=R/'game/assets/horse/production/evidence/foal-idle-hind-geometry';d=read(e/'idle-morph-audit.json')
  self.assertEqual(d['caseCount'],25);self.assertEqual(d['sampleCountPerCase'],121);self.assertEqual(d['failures'],[])
  for row in d['cases']:
   self.assertTrue(row['allFinite']);self.assertLessEqual(max(row['maxEdgeStretch'].values()),2)
   self.assertGreaterEqual(row['minimumSoleHeightM'],-.002);self.assertLessEqual(row['maximumFootMotionM'],.005)
   self.assertLessEqual(max(row['maxAttachmentDriftM'].values()),.002);self.assertLessEqual(row['maximumLoopErrorM'],.001)
  native=read(e/'native-idle-report.json');self.assertEqual(native['failures'],[]);self.assertTrue(native['usesProjectImport']);self.assertEqual(native['projectImportFps'],30);self.assertFalse(native['animationOptimizerEnabled'])
  self.assertEqual(native['soleSupport']['sampleCount'],121);self.assertEqual(native['soleSupport']['failures'],[])
 def test_ci_and_export_include_corrected_static_evidence(self):
  plugin=(R/'game/addons/horse_asset_evidence/plugin.gd').read_text();self.assertIn('production/horse_foal.glb',plugin);self.assertIn('evidence/foal-idle-hind-geometry/',plugin)
  workflow=(R/'.github/workflows/visual.yml').read_text();self.assertIn('check_imported_foal_idle.gd',workflow);self.assertIn('FoalTransitionProbe.tscn',workflow)
if __name__=='__main__':unittest.main()
