# Blender 5.2 to Godot 4.7 eight-weight format probe

**Scope:** format compatibility only. This used a diagnostic Light Blend already rejected for anatomy. It is not evidence for anatomy, deformation quality, gait, or promotion into production.

## Inputs and outputs

- Source Blend: `/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support/float-diagnostic8/horse_light_float_probe.blend`
- Source Blend SHA-256 before and after: `fd2f997eca0cce2fa9609b8aa5ea3abd5aa0c6924e3a4309a14ace5351cfac6e`
- Exported scratch GLB SHA-256: `5e007c40a99fbf82fa87e7174ed43a93849ea9f782d4bfb349bc6e53224edb7a`
- Blender: `5.2.1 LTS` (`9e2066aef7ef`, 2026-08-25)
- Godot: `4.7.2.stable.mono.official.ed1daf0bf`

## Commands

From `/Users/markrogers/Documents/Codex/2026-10-11/con/work/godot-eight-weight-probe`:

```sh
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --disable-autoexec --python run_blender_probe.py -- --input /Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support/float-diagnostic8/horse_light_float_probe.blend --output scratch/horse_light_float_probe_8weights.glb --report scratch/blender_probe_report.json
python3 inspect_glb.py scratch/horse_light_float_probe_8weights.glb > scratch/gltf_accessor_report.json
/Users/markrogers/Documents/Codex/2026-10-10/referenced-chatgpt-conversation-this-is-an/work/runtime/mono/Godot_mono.app/Contents/MacOS/Godot --headless --editor --path scratch/godot_project --import --quit
/Users/markrogers/Documents/Codex/2026-10-10/referenced-chatgpt-conversation-this-is-an/work/runtime/mono/Godot_mono.app/Contents/MacOS/Godot --headless --path scratch/godot_project --script res://probe.gd --quit-after 30
```

The Blender export and Godot editor import completed successfully in the original run, but their raw command logs were not retained; those results are recorded as reported. The small Godot inspector was rerun separately against that already-imported GLB. Its stdout, stderr, and observed exit code (`0`) are retained beside this report. The inspector instantiated and read the imported scene and RenderingServer mesh surfaces in headless mode; it did not render the scene. The isolated Godot import used `meshes/generate_lods=true`.

## Results

The Blend has one 58-bone armature and 8-influence data on the Body and Mane. Blender exported `JOINTS_0/1` as unsigned-byte `VEC4` accessors and `WEIGHTS_0/1` as float32 `VEC4` accessors. The float weight accessors do not set glTF's `normalized` flag, which is correct for float components. Pre-import combined weight sums were Body `0.9999998892–1.0000001187`, Mane `0.9999999031–1.0000000522`, and Tail `0.9999999069–1.0000000466`.

Godot imported actual 8-wide bone and weight arrays on Body, Mane, and Tail. Body has 5,643 vertices with more than four nonzero weights; Mane has 896; Tail has 8. The imported Eyes surface has four array slots and no vertex above three nonzero weights. All active bone indices are within the 58-bone skeleton, and the imported bone names are in the same index order as the 58 entries in the GLB skin joint list. Imported float-array sums were Body `0.9998779111–1.0000000489`, Mane `0.9999542646–1.0000000522`, and Tail `0.9999695132–1.0000000466`; the total observed range is `0.9998779111–1.0000000522`. The largest drift is approximately `1.22e-4` (about `0.0122%` of the sum). This is an aggregate sum check only: the `2e-4` tolerance does not bound the error of any individual weight or establish posed deformation quality. The source FLOAT accessors remain normalized to within about `1.2e-7`; the drift therefore appears after GLB export. It is consistent with Godot-side weight packing, and Godot 4.7's renderer shader unpacks skin weights from UNORM16, but this headless inspector does not distinguish importer/resource packing from GPU conversion or render the instantiated scene.

The imported resources retain nonempty LOD index buffers on all four surfaces: Body 4 LODs, Eyes 9, Mane 4, Tail 7. Each recorded LOD has a present index buffer.

Detailed Blender, glTF accessor, and Godot inspection reports are in the JSON files beside this report. The Blender export script, glTF inspection script, Godot inspection script, scratch GLB, and final inspector stdout/stderr are also archived beside it. The original isolated imported project and working copies remain under `/Users/markrogers/Documents/Codex/2026-10-11/con/work/godot-eight-weight-probe/`.

Godot renderer reference: [Godot 4.7-stable skeleton shader](https://github.com/godotengine/godot/blob/4.7-stable/servers/rendering/renderer_rd/shaders/skeleton.glsl#L1181-L1211).
