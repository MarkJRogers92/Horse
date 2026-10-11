# Adult and foal motion workflow — unfinished / paused

**Preservation only:** the owner rejected the deformation and stopped animation work on 2026-10-10. The instructions below describe experimental tooling, not an approved asset. Adult and foal files are at different repair stages, and the latest source/binary set has not received final verification. See `VISUAL_STATUS.md` before continuing.

This is the idle/walk/trot slice of P1-02. It uses the owner's selected CC0 horse and the existing descriptor-driven presenter. The original reference package remains intact. Full P1-02 acceptance, additional gaits, tack, body morphs and gameplay locomotion are separate work.

## Using the clips

Open the Horse Viewer, choose a supported coat, toggle **Foal** for the juvenile model, and choose **idle**, **walk** or **trot**. The same `HorsePresenter.Play` entry point is used by other scenes. It blends clip changes over 0.18 seconds. The `(still)` choice remains available for form review. `look` and `breathe` are not separate authored actions.

When importing the GLB into a different Godot project, enable looping for these three animations in the import settings or AnimationPlayer. This project's presenter enables linear looping on import; glTF itself does not carry a universal playback-loop flag.

These clips are in-place: the model's root does not travel through the scene. A future locomotion controller must move the character along local +X at the matching speed recorded in each asset's `.manifest.json`. Multiply that speed by both the model's uniform scale and the animation playback speed. Playing the clip without translation is useful in the viewer, but its stance hooves visibly travel backward relative to the stationary scene.

The adult and foal have separate actions fitted to their own geometry. Do not copy adult translations into the foal rig or scale the entire adult to make a juvenile. Preserve the foal's higher belly, shorter barrel and short hair when editing.

## Editable source and reproducibility

The working Blender files are `art/derived/Horse_Reference/horse_adult.blend` and `horse_foal.blend`. Named actions are exported to their corresponding `game/assets/horse/reference/horse_*.glb` files. The generator is `tools/horse/import_reference.py`, with motion authored in `tools/horse/reference_animation.py`. Changes made directly to a derived Blender file will be replaced on regeneration; preserve deliberate hand edits as a new source before running the generator again.

The rig includes articulated hind stifles. The motion pass also removes tail-bone influence from body vertices and repairs the relevant leg weights. The editable hooves remain separate; game export joins them into `Body` while retaining their joint weights. No diffuse coat colour is baked into the animations.

The walk uses four separate contacts; the trot uses diagonal pairs. Reference: [University of Kentucky walk description](https://horses.extension.org/horse-walk/), [trot description](https://horses.extension.org/horse-trot/). These references explain the gait pattern; they do not certify the asset's deformation or motion quality.

## Owner-provided motion source

The owner supplied **Horse Rigged All Gaits.blend**, from [fdoss001's BlendSwap listing 28627](https://blendswap.com/blend/28627). The listing identifies CC0 and includes walk, trot, canter and gallop. The local source is 612,745,224 bytes; SHA-256 is `b848037332fe62064989f250a94faa04c75e3fb21ae93f6a100f19079c1b21aa`.

The motion inspection loads only the rig and actions with embedded Python disabled, mutes scene path travel, and evaluates the walk and trot. It does not import the source's body, particle hair, saddle, reins or materials into the game. The source walk spans 40 frames at 24 fps (1.6667 seconds), and trot spans 20 frames (0.8333 seconds). Its bone endpoints are motion references rather than verified sole positions; the target horse's actual hoof geometry must determine ground contact.

The listing's comments attribute its underlying mesh to [Tarnyloo's Horse, listing 17172](https://blendswap.com/blend/17172), which lists CC-BY. That mesh is not used in this adaptation. This provenance note preserves both authors' identities instead of treating the entire source scene as unquestionably original to the animation uploader. The preferred game's body remains the separate Lyndon Daniels CC0 reference credited in `REFERENCE_ASSET_WORKFLOW.md`.

## Playback evidence

The ordinary capture scene creates six static phases for every required clip at both ages. A missing idle, walk or trot is a capture failure. Static phases are useful for checking poses, but do not establish continuous playback.

`game/scenes/motion_capture.tscn` advances the actual imported AnimationPlayer at 24 frames per second and captures the real renderer. Run it with `--age adult|foal --animation idle|walk|trot --view side|q3 --loops 5 --out <directory>` after the normal game build/import. Use two loops for the three-second idle. Its final extra frame is for validation and should be omitted when encoding a looping video. The `--animation transitions --age both` diagnostic exercises idle → walk → trot → idle using the presenter's normal blending path.

Capture reports describe the imported skeleton and animation tracks, finite/changing local bone poses and observed across-wrap steps. Blender diagnostics must separately verify evaluated hoof geometry, stance contact and the repeated endpoint pose. Neither diagnostic is a substitute for inspecting the resulting joint deformation and movement.

Verification results and exact checkpoint/run identifiers are recorded in `VISUAL_STATUS.md` once the candidate is accepted.
