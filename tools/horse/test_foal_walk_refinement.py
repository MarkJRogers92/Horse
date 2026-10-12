"""Foal-specific placement/style regressions; original physical gates stay separate."""
import copy
import math
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from test_walk_anatomy import ROOT, read, sha, assert_report_matches

# Authored limits for this juvenile rig, not general equine measurements.
STYLE_BOUNDS = {
    'foreTrackWidthMinM': (.09, .13), 'foreTrackWidthMaxM': (.09, .13),
    'hindTrackWidthMinM': (.15, .19), 'hindTrackWidthMaxM': (.15, .19),
    'meanForeHindSpanM': (.70, .80), 'stanceTravelM': (.27, .29),
}

def style_passes(placement, anatomy):
    metrics = placement['metrics']
    return (all(math.isfinite(metrics[key]) and lo <= metrics[key] <= hi
                for key, (lo, hi) in STYLE_BOUNDS.items())
            and all(math.isfinite(limb['maxSwingFlexionDegrees'])
                    and 20 <= limb['maxSwingFlexionDegrees'] <= 45
                    for limb in anatomy['limbs'].values()))

class FoalRefinementTests(unittest.TestCase):
    def test_actual_export_placement_and_style(self):
        evidence = ROOT/'game/assets/horse/production/evidence/foal-walk-anatomy'
        glb = ROOT/'game/assets/horse/production/horse_foal_walk.glb'
        profile = evidence/'walk-motion-profile.json'
        with tempfile.TemporaryDirectory() as folder:
            placement, anatomy = self.audit(glb, profile, Path(folder))
            assert_report_matches(placement, read(evidence/'placement-report.json'))
            assert_report_matches(anatomy, read(evidence/'anatomy-report.json'))
        self.assertEqual(placement['sourceGlbSha256'], sha(glb))
        self.assertEqual(placement['motionProfileSha256'], sha(profile))
        self.assertEqual(placement['sampleCount'], 117)
        self.assertEqual(placement['registeredSolePoints'], 176)
        self.assertTrue(style_passes(placement, anatomy))
        # Positive signed separation, rather than absolute width, rules out crossed lanes.
        for row in placement['phases']:
            feet = row['soleCentroidsGodotM']
            for kind in ['fore', 'hind']:
                self.assertLess(feet[kind+'_l'][2], feet[kind+'_r'][2])

    def test_historical_splay_and_high_fold_are_rejected(self):
        old = ROOT/'art/derived/production/candidates/foal-walk-anatomy'
        with tempfile.TemporaryDirectory() as folder:
            placement, anatomy = self.audit(old/'horse_foal_walk.glb', old/'walk-motion-profile.json', Path(folder))
        self.assertEqual(anatomy['failures'], [])  # Old physical/anatomical pass was insufficient.
        self.assertFalse(style_passes(placement, anatomy))
        for key in ['foreTrackWidthMaxM', 'hindTrackWidthMaxM', 'meanForeHindSpanM']:
            self.assertGreater(placement['metrics'][key], STYLE_BOUNDS[key][1])
        self.assertGreater(anatomy['limbs']['fore_l']['maxSwingFlexionDegrees'], 45)

    def test_out_of_bounds_and_nonfinite_styles_are_rejected(self):
        evidence = ROOT/'game/assets/horse/production/evidence/foal-walk-anatomy'
        p, a = read(evidence/'placement-report.json'), read(evidence/'anatomy-report.json')
        for key, (lo, hi) in STYLE_BOUNDS.items():
            for value in [lo-.001, hi+.001, float('nan'), float('inf')]:
                with self.subTest(key=key, value=value):
                    mutated = copy.deepcopy(p); mutated['metrics'][key] = value
                    self.assertFalse(style_passes(mutated, a))
        for side in ['fore_l','fore_r']:
            for value in [19.9,45.1,float('nan'),float('inf')]:
                mutated = copy.deepcopy(a); mutated['limbs'][side]['maxSwingFlexionDegrees'] = value
                self.assertFalse(style_passes(p, mutated))

    def audit(self, glb, profile, folder):
        a, p = folder/'anatomy.json', folder/'placement.json'
        result = subprocess.run([sys.executable, str(ROOT/'tools/horse/audit_exported_walk_anatomy.py'),
                                 str(glb), str(profile), '--out', str(a), '--placement-out', str(p)],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout+result.stderr)
        return read(p), read(a)

if __name__ == '__main__': unittest.main()
