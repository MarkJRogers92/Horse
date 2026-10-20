"""Integrity and boundaries of the curated illustration collection."""
import hashlib,json,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
class ArtTests(unittest.TestCase):
 def test_catalog(self):
  base=ROOT/'game/assets/ui/illustrations'
  cat=json.loads((base/'catalog.json').read_text())
  self.assertEqual(len(cat['assets']),45)
  self.assertFalse(cat['commercial_release_cleared'])
  self.assertEqual(len({a['sha256'] for a in cat['assets']}),45)
  for a in cat['assets']:
   self.assertFalse(a['genetics_reference_approved'])
   self.assertEqual(hashlib.sha256((base/a['file']).read_bytes()).hexdigest(),a['sha256'])
   self.assertTrue(a['quality_notes'])
   self.assertIn('process/size_limit=768', (base/(a['file']+'.import')).read_text())
 def test_breed_mapping_points_to_curated_files(self):
  mapping=json.loads((ROOT/'docs/art/breed-library-map.json').read_text())
  self.assertEqual(mapping['approved'],21)
  self.assertEqual(mapping['held'],4)
  for breed in mapping['breeds']:
   if breed['file']:
    self.assertTrue((ROOT/'game/assets/ui/illustrations'/breed['file']).exists())
 def test_gameplay_boundary(self):
  for path in ['game/scripts/Visual/HorsePortraitStudio.cs','game/scripts/Domain/DomainBridge.cs']:
   self.assertNotIn('illustrations', (ROOT/path).read_text())
 def test_full_image_and_bounded_gallery(self):
  source=(ROOT/'game/scripts/UI/IllustrationArt.cs').read_text()
  self.assertIn('KeepAspectCentered',source)
  gallery=(ROOT/'game/scripts/Scenes/ConceptPortraitGallery.cs').read_text()
  self.assertIn('PageSize = 6',gallery)
  self.assertIn('IllustrationArt',gallery)
if __name__=='__main__':unittest.main()
