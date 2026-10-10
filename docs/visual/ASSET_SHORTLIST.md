# Licensed hero-horse shortlist (D-002): owner decision needed

**Nothing has been purchased.** These are candidates to replace the temporary procedural horse.

**Unverified:** marketplace pages could not be opened from the build environment (DNS blocked). Prices and features below come from search-result listings dated 2026-10-10. Open each link to check the screenshots, file list and licence before buying.

## Important constraint for an owner without a PC
**Unity Asset Store downloads go through the Unity Editor.** Unity's FAQ says Asset Store assets may be used in other engines under the EULA, but downloading them normally needs Unity installed. Marketplaces with **browser downloads** (Superhive/Blender Market, CGTrader, Fab, RenderHub, TurboSquid) fit our setup better. You download the zip on any device, and we commit it to the private repo or attach it for a model to import.

## Candidates

| # | Asset | Price (listed) | Why it is interesting | Risks / to verify |
| --- | --- | --- | --- | --- |
| 1 | [Realistic Horse, Rigged and Animated (Superhive, Marius)](https://superhivemarket.com/products/realistic-horse-rigged-and-animated) | ~$35 | Native **Blender** source with walk/trot/gallop cycles and a custom rig. Cheapest realistic option. | Listing says Blender 2.9x and a **Creative Commons** licence (check which CC, and attribution terms). A reviewer called the textures the weakest part, but we replace textures with our coat shader anyway. Mane/tail are dynamic hair, so they need converting to cards. |
| 2 | [Realistic Horse with Animations (Fab, Aragon 3D)](https://www.fab.com/sellers/Aragon%203D) | from ~$94.99 | Highest-quality animated horse seen; rated 4.5 (13 reviews). The Fab Standard Licence generally permits use in any engine. | It was released mainly for Unreal, so confirm FBX/Blender files are offered on Fab (not just a UE project). Price. |
| 3 | [Realistic Brown Horse, full rigged (CGTrader)](https://www.cgtrader.com/3d-models/animal/mammal/realistic-brown-horse-full-rigged) | check listing | Includes **.blend**, 4K textures, "proper UV mapping", advanced rig, walk/run/idle/grazing. | Ships in a forest scene; licence type (royalty-free vs editorial) not visible. |
| 4 | [Realistic Horse (Unity Asset Store)](https://marketplace.unity.com/packages/3d/characters/animals/realistic-horse-115811) / [Realistic Horse Herd](https://marketplace.unity.com/packages/3d/characters/animals/mammals/realistic-horse-herd-231231) | $59 / $59.99 | 36K polys, generic rig, 4K textures. Herd updated Feb 2025. | **Needs the Unity Editor to download.** Not recommended without a PC. |
| 5 | [Foal / horse models by Nyilonelycompany (RenderHub / Superhive)](https://www.renderhub.com/low-poly-models/animals/horses) | ~$50 each | Same seller lists adult breeds **and a foal**, rigged, PBR, Extended Use licence. Only candidate with a matching foal. | Search couldn't confirm .blend files or animation quality for the foal. |

## Recommendation
Buy **#1 ($35)** first as a low-cost test of the swap-in pipeline: Blender source, an open licence and realistic anatomy. If its head and anatomy pass the clay review, use it. If you want top quality and #2's Fab download includes FBX, **#2** is the premium pick. Either way, the foal still needs a decision: #5, or adapting the adult with juvenile proportions in Blender.

## What happens after purchase (no manual Blender work for you)
1. Upload the zip to the repo (or a Google Drive link) and tell Claude or GPT which listing it is.
2. A model writes `tools/horse/import_licensed.py`. It adds our mask attributes and canonical coordinates, maps the bones to `docs/visual/TOOLCHAIN.md` names and exports a glb.
3. Add a new mesh family entry in `game/data/visual_catalog.json` with `placeholder: false`. The presenter and shaders are unchanged.
4. CI renders the clay and coat sheets for your review.
