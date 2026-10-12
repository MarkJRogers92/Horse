#!/usr/bin/env python3
"""Offline checks for the pinned active Stock/foal game assets and proof chain."""
import hashlib
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(path): return json.loads(path.read_text())
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def resource(path): return ROOT / 'game' / path.removeprefix('res://')

class IntegratedWalkTests(unittest.TestCase):
    def test_exact_active_asset_and_evidence_bindings(self):
        catalog = read(ROOT/'game/data/visual_catalog.json')
        parser = (ROOT/'game/scripts/Visual/RemainingWalkEvidence.cs').read_text()
        for family, name in [('horse.compact.adult.v1','stock'),('horse.standard.foal.v1','foal'),('horse.compact.foal.v1','foal')]:
            with self.subTest(family=family):
                variant = catalog['meshFamilies'][family]['walkVariant']
                self.assertIsNone(variant['bodySignedDetail'])
                self.assertEqual(variant['fps'],60)
                self.assertEqual(variant['periodFrames'],58)
                asset=resource(variant['scene'])
                self.assertEqual(sha(asset),variant['sourceGlbSha256'])
                self.assertEqual(asset.read_bytes(),(ROOT/f'art/derived/production/candidates/{name}-walk-{"refined" if name=="foal" else "anatomy"}/horse_{name}_walk.glb').read_bytes())
                for field in ['motionProfile','receipt','cpuMotionAudit']:
                    self.assertEqual(sha(resource(variant[field+'Path'])),variant[field+'Sha256'])
                evidence=resource(variant['motionProfilePath']).parent
                for path in evidence.glob('*.json'):
                    self.assertIn(sha(path),parser)
                profile=read(resource(variant['motionProfilePath']))
                self.assertEqual(profile['sourceGlbSha256'],variant['sourceGlbSha256'])
                self.assertEqual(profile['fps'],variant['fps'])
                self.assertEqual(profile['forwardSpeedMetersPerSecond'],variant['forwardSpeedMetersPerSecond'])
                imports=asset.with_suffix('.glb.import').read_text()
                self.assertIn('animation/fps=60',imports)
                self.assertIn('"optimizer/enabled": false',imports)
                runtime=read(resource(variant['receiptPath']))
                self.assertEqual(runtime['engine']['string'],'4.7.2-stable (official)')
                self.assertTrue(runtime['usesProjectImport'])
                self.assertEqual(runtime['projectImportFps'],60)
                self.assertFalse(runtime['animationOptimizerEnabled'])
                self.assertLess(runtime['animations'][0]['endpointBasisError'],0.00001)
                self.assertEqual(runtime['failures'],[])
                self.assertEqual(runtime['contact']['registered'],429 if name=='stock' else 176)
                self.assertEqual(set(runtime['allComponentDeformation']),{'Body','Eyes','Mane','Tail'})
                for mesh in runtime['meshes']:
                    self.assertEqual(mesh['blendShapes'],4)
                    self.assertEqual(mesh['zeroWeightVertices'],0)
                    self.assertEqual(mesh['nonUnitWeightVertices'],0)
                self.assertEqual(runtime['attachments']['failures'],[])
                surface=read(evidence/'surface-report.json')
                self.assertEqual(surface['failures'],[])
                for attrs in surface['components'].values():
                    for value in attrs.values(): self.assertEqual(value['maximumError'],0)
                self.assertIn('COLOR_0',surface['components']['Body'])
                self.assertIn('TEXCOORD_1',surface['components']['Body'])

    def test_foal_mappings_share_exact_proof(self):
        families=read(ROOT/'game/data/visual_catalog.json')['meshFamilies']
        self.assertEqual(families['horse.standard.foal.v1']['walkVariant'],families['horse.compact.foal.v1']['walkVariant'])

    def test_ci_import_profiles_match_active_catalog(self):
        workflow=(ROOT/'.github/workflows/visual.yml').read_text()
        template=re.search(r'"(res://[^"\n]*\$\{family\}[^"\n]*/walk-motion-profile.json)"',workflow)
        self.assertIsNotNone(template)
        catalog=read(ROOT/'game/data/visual_catalog.json')
        for name,family in [('stock','horse.compact.adult.v1'),('foal','horse.standard.foal.v1')]:
            path=template.group(1).replace('${family}',name)
            self.assertEqual(path,catalog['meshFamilies'][family]['walkVariant']['motionProfilePath'])
            self.assertTrue(resource(path).is_file())

    def test_all_raw_assets_exported(self):
        plugin=(ROOT/'game/addons/horse_asset_evidence/plugin.gd').read_text()
        for path in ['horse_light.glb','horse_light_walk.glb','horse_stock_walk.glb','horse_foal_walk.glb','horse_light_body_detail_v5.png','ai-neutral-skin-mask.png']:
            self.assertIn(path,plugin)
        for name in ['stock','foal']: self.assertIn(f'evidence/{name}-walk-anatomy/',plugin)

if __name__=='__main__': unittest.main()
