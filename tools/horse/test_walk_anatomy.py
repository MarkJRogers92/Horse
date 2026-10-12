"""Regression: exported joint anatomy, unchanged surfaces and the Light physical gates."""
import copy,hashlib,json,math,subprocess,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
read=lambda p:json.loads(p.read_text())

# Reports are recomputed from identical, SHA-bound GLB bytes on different
# Python/NumPy builds. Last-bit arithmetic is not a byte-identity contract.
# This absolute-only tolerance is at most one nanometre for coordinates,
# 1e-9 degrees for angles, or 1e-9 seconds/cycles for timing. It is many orders
# below every anatomy gate. Limits and all non-measurement metadata stay exact.
MEASUREMENT_ABS_TOLERANCE = 1e-9

def assert_report_matches(actual, expected, path=()):
 """Compare every leaf, allowing rounding noise only in measured floats."""
 location = '.'.join(map(str,path)) or '<report>'
 if type(actual) is not type(expected):
  raise AssertionError(f'{location}: type changed: {type(actual).__name__} != {type(expected).__name__}')
 if isinstance(expected,dict):
  if actual.keys()!=expected.keys():raise AssertionError(f'{location}: object keys changed')
  for key in expected:assert_report_matches(actual[key],expected[key],path+(key,))
 elif isinstance(expected,list):
  if len(actual)!=len(expected):raise AssertionError(f'{location}: array length changed')
  for index,(a,b) in enumerate(zip(actual,expected)):assert_report_matches(a,b,path+(index,))
 elif isinstance(expected,float):
  if not math.isfinite(actual) or not math.isfinite(expected):raise AssertionError(f'{location}: non-finite measurement')
  measured = path and path[0] in ('durationSeconds','limbs','phases','metrics')
  if not (math.isclose(actual,expected,rel_tol=0,abs_tol=MEASUREMENT_ABS_TOLERANCE) if measured else actual==expected):
   raise AssertionError(f'{location}: {actual!r} != {expected!r}')
 elif actual!=expected:
  raise AssertionError(f'{location}: {actual!r} != {expected!r}')

class AnatomyReportComparisonTests(unittest.TestCase):
 def setUp(self):
  self.report=read(ROOT/'game/assets/horse/production/evidence/light-walk-v6-anatomy/anatomy-report.json')

 def test_rounding_noise_is_accepted(self):
  def perturb(value,path=()):
   if isinstance(value,dict):return {k:perturb(v,path+(k,)) for k,v in value.items()}
   if isinstance(value,list):return [perturb(v,path+(i,)) for i,v in enumerate(value)]
   if isinstance(value,float) and path and path[0] in ('durationSeconds','limbs','phases','metrics'):
    return value+MEASUREMENT_ABS_TOLERANCE/4
   return value
  assert_report_matches(perturb(self.report),self.report)

 def test_structure_metadata_and_gate_changes_are_rejected(self):
  cases=[
   (('sourceGlbSha256',),'0'*64),
   (('motionProfileSha256',),'0'*64),
   (('schema',),'horse.walk.anatomy.v2'),
   (('sampleCount',),118),
   (('sampleCount',),117.0),
   (('sampleCount',),True),
   (('failures',),['backward carpus']),
   (('limits','maximumStanceHoofPitchRangeDegrees'),0.5+MEASUREMENT_ABS_TOLERANCE/4),
   (('limbs','fore_l','minSignedCarpusOffsetM'),self.report['limbs']['fore_l']['minSignedCarpusOffsetM']+0.001),
   (('limbs','fore_l','maxStanceFlexionDegrees'),12.01),
   (('limbs','fore_l','hoofPitchRangeDegrees'),19.99),
   (('phases',0,'joints','l','points',0,0),self.report['phases'][0]['joints']['l']['points'][0][0]+0.001),
   (('phases',0,'joints','l','flexionDegrees'),self.report['phases'][0]['joints']['l']['flexionDegrees']+0.01),
   (('durationSeconds',),self.report['durationSeconds']+0.000001),
   (('durationSeconds',),self.report['durationSeconds']+2*MEASUREMENT_ABS_TOLERANCE),
   (('durationSeconds',),float('nan')),
   (('durationSeconds',),float('inf')),
  ]
  for path,value in cases:
   with self.subTest(path=path,value=value):
    altered=copy.deepcopy(self.report);target=altered
    for key in path[:-1]:target=target[key]
    target[path[-1]]=value
    with self.assertRaises(AssertionError):assert_report_matches(altered,self.report)
  for altered in [dict(self.report,extra=True),{k:v for k,v in self.report.items() if k!='method'},dict(self.report,phases=self.report['phases'][:-1])]:
   with self.assertRaises(AssertionError):assert_report_matches(altered,self.report)
  for actual in [False,1,1.0]:
   with self.assertRaises(AssertionError):assert_report_matches(actual,True,('accepted',))

class WalkAnatomyTests(unittest.TestCase):
 def test_actual_exports_and_all_evidence(self):
  catalog=read(ROOT/'game/data/visual_catalog.json')
  for family,f in [('horse.standard.adult.v1','light'),('horse.compact.adult.v1','stock'),('horse.standard.foal.v1','foal')]:
   with self.subTest(family=family):
    v=catalog['meshFamilies'][family]['walkVariant'];e=ROOT/'game'/v['motionProfilePath'].removeprefix('res://');p=e;e=e.parent
    glb=ROOT/'game'/v['scene'].removeprefix('res://');candidate=ROOT/f'art/derived/production/candidates/{f}-walk-{"hind-geometry" if f=="foal" else "anatomy"}/horse_{f}_walk.glb'
    self.assertEqual(glb.read_bytes(),candidate.read_bytes())
    with tempfile.TemporaryDirectory() as out:
     dest=Path(out)/'anatomy.json';run=subprocess.run([sys.executable,str(ROOT/'tools/horse/audit_exported_walk_anatomy.py'),str(glb),str(p),'--out',str(dest)],capture_output=True,text=True)
     self.assertEqual(run.returncode,0,run.stdout+run.stderr)
     assert_report_matches(read(dest),read(e/'anatomy-report.json'))
    runtime=read(e/'game-import-report.json');self.assertEqual(runtime['failures'],[]);self.assertTrue(runtime['usesProjectImport']);self.assertEqual(runtime['candidateGlbSha256'],sha(glb))
    native=read(e/'native-anatomy-report.json');self.assertEqual(native['failures'],[]);self.assertTrue(native['usesProjectImport']);self.assertEqual(native['sourceGlbSha256'],sha(glb));self.assertEqual(native['motionProfileSha256'],sha(p))
    raw=read(e/'anatomy-report.json')
    for side,metrics in native['limbs'].items():
     for key,value in metrics.items():self.assertAlmostEqual(value,raw['limbs'][side][key],delta=0.0001 if key.endswith('M') else 0.02)
    surface=read(e/'surface-report.json');self.assertEqual(surface['failures'],[]);self.assertEqual(surface['candidateGlbSha256'],sha(glb))
    if f=='foal':
     self.assertEqual(surface['schema'],'horse.foal_walk.bounded_geometry.v1');self.assertTrue(surface['pass']);self.assertFalse(surface['allPositionsExact']);self.assertTrue(surface['untouchedPositionsExact']);self.assertTrue(surface['exportProtectedAttributesExact'])
    else:
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
