"""Fast shader contract checks; YardAtmosphereProbe also exercises real Godot materials."""
import pathlib
import re
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]


class YardAtmosphereContractTests(unittest.TestCase):
    def shader(self, name):
        path = ROOT / "game" / "shaders" / name
        self.assertTrue(path.exists(), f"Missing atmosphere shader: {name}")
        return path.read_text()

    def test_paddock_wetness_defaults_to_dry_and_uses_explicit_clock(self):
        shader = self.shader("paddock_sand.gdshader")
        self.assertRegex(shader, r"uniform float wetness[^;]*=\s*0\.0\s*;")
        self.assertRegex(shader, r"uniform float atmosphere_time[^;]*=\s*0\.0\s*;")
        self.assertRegex(shader, r"uniform float rain_amount[^;]*=\s*0\.0\s*;")
        self.assertNotRegex(shader, r"\bTIME\b|\bALPHA\s*=")

    def test_wind_is_rooted_and_does_not_use_global_shader_state(self):
        shader = self.shader("yard_foliage_wind.gdshader")
        self.assertIn("root_weight", shader)
        self.assertRegex(shader, r"uniform float wind_strength")
        self.assertRegex(shader, r"uniform float atmosphere_time")
        self.assertNotRegex(shader, r"\bTIME\b|global\s+uniform|\bALPHA\s*=")

    def test_path_stays_opaque_and_shares_weather_inputs(self):
        shader = self.shader("yard_path.gdshader")
        self.assertRegex(shader, r"uniform float wetness[^;]*=\s*0\.0\s*;")
        self.assertNotRegex(shader, r"\bALPHA\s*=|global\s+uniform")


if __name__ == "__main__":
    unittest.main()
