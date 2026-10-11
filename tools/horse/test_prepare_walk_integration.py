#!/usr/bin/env python3
"""Small fail-closed tests for inactive integration preparation (not game QA)."""
import copy, importlib.util, json, tempfile, unittest
from pathlib import Path
SPEC=importlib.util.spec_from_file_location('prep',Path(__file__).with_name('prepare_walk_integration.py'))
prep=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(prep)

class PreparationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name);self.oldroot=prep.ROOT;prep.ROOT=self.root;self.addCleanup(setattr,prep,'ROOT',self.oldroot)
        self.runtime=self.root/'reports';self.runtime.mkdir();self.output=self.root/'manifest.json'
        for family in ('stock','foal'):
            folder=self.root/'art/derived/production/candidates'/f'{family}-walk';folder.mkdir(parents=True)
            glb=folder/f'horse_{family}_walk.glb';blend=folder/f'horse_{family}_walk.blend'
            glb.write_bytes((family+'fake test GLB').encode());blend.write_bytes((family+'fake test source').encode())
            gh,bh=prep.sha(glb),prep.sha(blend)
            profile={'sourceGlbSha256':gh,'candidateBlendSha256':bh,'sourceRootInPlace':True,'fps':60,'periodFrames':58,'dutyFactor':.6,'forwardSpeedMetersPerSecond':.3}
            audit={'candidateGlbSha256':gh,'candidateBlendSha256':bh,'protectedMeshDataExact':True,'bodyMaxEdgeStretch':1.5}
            runtime={'candidateGlbSha256':gh,'failures':[],'importBakeFps':60,'contact':{'failures':[],'registered':100},'meshCount':4,'meshes':[{'blendShapes':4,'zeroWeightVertices':0,'nonUnitWeightVertices':0}]*4,'skinDeformation':{'failures':[],'maxBodyEdgeStretch':1.5}}
            runtime['allComponentDeformation']={k:{'failures':[],'maxBodyEdgeStretch':1.5} for k in ['Body','Eyes','Mane','Tail']}
            surface={'candidateGlbSha256':gh,'failures':[]}
            for p,v in [(folder/'walk-motion-profile.json',profile),(folder/'source-audit.json',audit),(self.runtime/f'{family}-report.json',runtime),(self.runtime/f'{family}-surface-report.json',surface)]:p.write_text(json.dumps(v))
    def change(self,path,key,value):
        p=self.root/path;d=json.loads(p.read_text());d[key]=value;p.write_text(json.dumps(d))
    def runprep(self):prep.prepare(self.runtime,self.output)
    def test_inactive_manifest(self):
        self.runprep();d=json.loads(self.output.read_text());self.assertFalse(d['activeCatalogChanged']);self.assertFalse(d['gameRuntimeVerified']);self.assertFalse(d['macBuildChanged']);self.assertEqual(len(d['variants']),2)
        for v in d['variants']:self.assertFalse(v['enabled']);self.assertIsNone(v['bodySignedDetail'])
    def test_stale_glb(self):
        (self.root/'art/derived/production/candidates/stock-walk/horse_stock_walk.glb').write_bytes(b'new')
        with self.assertRaisesRegex(ValueError,'stale GLB'):self.runprep()
    def test_wrong_runtime_hash(self):
        self.change('reports/foal-report.json','candidateGlbSha256','bad')
        with self.assertRaisesRegex(ValueError,'not bound'):self.runprep()
    def test_surface_failure(self):
        self.change('reports/stock-surface-report.json','failures',['COLOR_0'])
        with self.assertRaisesRegex(ValueError,'surface failures'):self.runprep()
    def test_import_downsample(self):
        self.change('reports/foal-report.json','importBakeFps',30)
        with self.assertRaisesRegex(ValueError,'fps mismatch'):self.runprep()
    def test_failed_contact(self):
        self.change('reports/foal-report.json','contact',{'failures':['slide'],'registered':100})
        with self.assertRaisesRegex(ValueError,'contact not verified'):self.runprep()
    def test_missing_morph(self):
        self.change('reports/stock-report.json','meshes',[{'blendShapes':3}]*4)
        with self.assertRaisesRegex(ValueError,'morph contract'):self.runprep()
    def test_unbound_skin(self):
        self.change('reports/stock-report.json','meshes',[{'blendShapes':4,'zeroWeightVertices':1,'nonUnitWeightVertices':1}]*4)
        with self.assertRaisesRegex(ValueError,'invalid skin weights'):self.runprep()
    def test_missing_import_deformation(self):
        self.change('reports/foal-report.json','skinDeformation',{})
        with self.assertRaisesRegex(ValueError,'imported deformation'):self.runprep()
    def test_missing_hair_deformation(self):
        self.change('reports/stock-report.json','allComponentDeformation',{})
        with self.assertRaisesRegex(ValueError,'all-component deformation'):self.runprep()
    def test_source_stretch(self):
        self.change('art/derived/production/candidates/foal-walk/source-audit.json','bodyMaxEdgeStretch',2.1)
        with self.assertRaisesRegex(ValueError,'source deformation'):self.runprep()

if __name__=='__main__':unittest.main()
