"""Regression: exported joint anatomy, unchanged surfaces and the Light physical gates."""
import hashlib,json,subprocess,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_text())
class WalkAnatomyTests(unittest.TestCase):
 def test_actual_exports_and_all_evidence(self):
  catalog=read(ROOT/'game/data/visual_catalog.json')
  for family,f in [('horse.standard.adult.v1','light'),('horse.compact.adult.v1','stock'),('horse.standard.foal.v1','foal')]:
   with self.subTest(family=family):
    v=catalog['meshFamilies'][family]['walkVariant'];e=ROOT/'game'/v['motionProfilePath'].removeprefix('res://');p=e;e=e.parent
    glb=ROOT/'game'/v['scene'].removeprefix('res://');candidate=ROOT/f'art/derived/production/candidates/{f}-walk-anatomy/horse_{f}_walk.glb'
    self.assertEqual(glb.read_bytes(),candidate.read_bytes())
    with tempfile.TemporaryDirectory() as out:
     dest=Path(out)/'anatomy.json';run=subprocess.run([sys.executable,str(ROOT/'tools/horse/audit_exported_walk_anatomy.py'),str(glb),str(p),'--out',str(dest)],capture_output=True,text=True)
     self.assertEqual(run.returncode,0,run.stdout+run.stderr)
     self.assertEqual(read(dest),read(e/'anatomy-report.json'))
    runtime=read(e/'game-import-report.json');self.assertEqual(runtime['failures'],[]);self.assertTrue(runtime['usesProjectImport']);self.assertEqual(runtime['candidateGlbSha256'],sha(glb))
    native=read(e/'native-anatomy-report.json');self.assertEqual(native['failures'],[]);self.assertTrue(native['usesProjectImport']);self.assertEqual(native['sourceGlbSha256'],sha(glb));self.assertEqual(native['motionProfileSha256'],sha(p))
    raw=read(e/'anatomy-report.json')
    for side,metrics in native['limbs'].items():
     for key,value in metrics.items():self.assertAlmostEqual(value,raw['limbs'][side][key],delta=0.0001 if key.endswith('M') else 0.02)
    surface=read(e/'surface-report.json');self.assertEqual(surface['failures'],[]);self.assertEqual(surface['candidateGlbSha256'],sha(glb))
    for component in surface['components'].values():
     for attr in component.values():self.assertEqual(attr['maximumError'],0)
    for m in runtime['meshes']:self.assertEqual(m['zeroWeightVertices'],0);self.assertEqual(m['nonUnitWeightVertices'],0)
    if f=='light':
     audit=read(e/'neutral-motion-audit.json');self.assertEqual(len(audit['metric_gates']),9);self.assertTrue(all(audit['metric_gates'].values()))
     dense=read(e/'dense-deformation-audit.json');self.assertEqual(dense['sampleCount'],581);self.assertLessEqual(dense['bodyMaxEdgeStretch'],1.6)
     proof=read(e/'geometry-uv-preservation.json');self.assertTrue(proof['pass']);self.assertEqual(proof['forbidden_changes'],[]);self.assertEqual(proof['glb_sha256'],sha(glb))
 def test_historical_backward_knee_is_rejected(self):
  # Historical immutable candidate retains the regression. It must not pass the new anatomy gate.
  old=ROOT/'art/derived/production/candidates/stock-walk-polish'
  with tempfile.TemporaryDirectory() as out:
   dest=Path(out)/'negative.json';run=subprocess.run([sys.executable,str(ROOT/'tools/horse/audit_exported_walk_anatomy.py'),str(old/'horse_stock_walk.glb'),str(old/'walk-motion-profile.json'),'--out',str(dest)],capture_output=True,text=True)
   self.assertNotEqual(run.returncode,0)
   self.assertTrue(any('backward carpus' in v for v in read(dest)['failures']))
   self.assertTrue(any('frozen hoof pitch' in v for v in read(dest)['failures']))
if __name__=='__main__':unittest.main()
