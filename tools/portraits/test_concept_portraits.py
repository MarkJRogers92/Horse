"""The archived art must stay intact and separate from phenotype portraits."""
import hashlib
import json
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[2]
EXPECTED = {
    'horse-02-palomino-card.png': '81752bd095b4a16eb207b9820145c66f5887713ee5046f6b45727781870395a0',
    'horse-04-black-card.png': 'b616f72cb7c2a0f7406cb169ee66efee0671cb84d8e0bbb868d844f998854c4e',
    'horse-03-foal-variation.png': '6cbd342f291b221ad24b2c7f9c2e73fa84d7421638f3faf2bd173f1b80d4231c',
}


class ConceptPortraitTests(unittest.TestCase):
    def test_original_catalog_is_complete_and_honest(self):
        path = ROOT / 'game/assets/ui/concept_portraits/catalog.json'
        self.assertTrue(path.is_file(), 'Concept portrait catalog is not installed')
        catalog = json.loads(path.read_text())
        self.assertEqual(catalog['usage'], 'illustration_gallery_only')
        self.assertFalse(catalog['commercial_release_cleared'])
        self.assertEqual(len(catalog['assets']), 3)
        self.assertEqual({a['file'] for a in catalog['assets']}, set(EXPECTED))
        for asset in catalog['assets']:
            with self.subTest(file=asset['file']):
                content = (path.parent / asset['file']).read_bytes()
                self.assertEqual(hashlib.sha256(content).hexdigest(), EXPECTED[asset['file']])
                self.assertEqual(asset['sha256'], EXPECTED[asset['file']])
                self.assertEqual(content[:8], b'\x89PNG\r\n\x1a\n')
                self.assertEqual(struct.unpack('>II', content[16:24]), (1536, 1536))
                self.assertFalse(asset['genotype_verified'])
                self.assertTrue(asset['quality_notes'])
                self.assertTrue(asset['prompt'])
                self.assertEqual(asset['paid_credits'], 0)

    def test_background_uses_clean_region_and_preserves_original(self):
        path = ROOT / 'game/assets/ui/concept_portraits/catalog.json'
        catalog = json.loads(path.read_text())
        self.assertIn('background', catalog, 'Clean background crop is not installed')
        backdrop = catalog['background']
        content = (path.parent / backdrop['file']).read_bytes()
        self.assertEqual(hashlib.sha256(content).hexdigest(), backdrop['sha256'])
        self.assertEqual(struct.unpack('>II', content[16:24]), (1696, 960))
        self.assertEqual(backdrop['display_region'], [0, 0, 1696, 730])
        self.assertIn('baked', backdrop['quality_notes'])

    def test_game_exposes_gallery_without_replacing_owned_horse_portraits(self):
        menu = (ROOT / 'game/scripts/Scenes/MainMenu.cs').read_text()
        self.assertIn('res://scenes/concept_portraits.tscn', menu)
        self.assertTrue((ROOT / 'game/scenes/concept_portraits.tscn').is_file())
        studio = (ROOT / 'game/scripts/Visual/HorsePortraitStudio.cs').read_text()
        self.assertNotIn('concept_portraits', studio)
        self.assertIn('_presenter.Present(descriptor, "idle")', studio)


if __name__ == '__main__':
    unittest.main()
