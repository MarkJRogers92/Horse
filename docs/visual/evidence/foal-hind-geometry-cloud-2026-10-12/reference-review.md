# Foal photo review — read-only, 2026-10-12

## Main finding

The next useful correction is the **hind articulation**, not more indiscriminate narrowing or a change to the improved front motion. The remaining awkwardness is a combination of the inherited backward-set thigh silhouette and the current motion rig concentrating the bend at the hock.

Actual candidate `acd4698` inspection establishes that `Hind_R_Upper` and `Hind_R_Gaskin` have effectively identical deformation transforms at all 117 sampled phases (largest matrix-element difference 3.58e-7). They move as one hip-to-hock segment. The Light adult has independent hip → stifle → hock → fetlock articulation. The new foal solver does not yet supply independent stifle movement, even though the source rig includes several named hind bones.

`model-hind-diagnosis.png` shows unchanged rest geometry beside the current Walk at phase 0.55, with actual projected solver anchors. The labels are solver landmarks, not a claim that we inferred skeletal centers from the surface. `model-rest/` and `model-stance/` also contain rear views. All were rendered read-only, without saving the model.

## What the photographs actually support

18 breeder photographs were inspected, including explicitly 3- and 4-month-old foals, a one-month foal, a nine-day-old neonatal contrast, and a dated approximately five-month gallery. Source/view/age information is in `references/reference-index.json`.

- In suitable side stances, the visible thigh comes down and forward toward the stifle, the gaskin goes back toward the hock, and the cannon is relatively upright below it. The current foal instead reads more like a diagonal upper strut with a pronounced bend behind the body.
- The forward-reaching hind leg can pass beneath the barrel; a trailing foot is normal during push-off. We must not turn one static photo into a fixed hoof position throughout Walk.
- Young foals retain long legs and less adult-looking torso/withers. These references do not justify shortening the limbs or copying adult body proportions.
- Their rounded quarters and gentle toplines do not support adding a large spinal arch. The model's inherited silhouette can be reassessed after the hind articulation improves.
- The near-front and rear-quarter views do not establish a precise hip or hock width target. Current separate, narrowed hoof lanes should be held for the next experiment rather than narrowed again by guesswork.

## Source links and distinctions

1. [Spendthrift Maximus Mischief progeny gallery](https://www.spendthriftfarm.com/stallions/maximus-mischief/): Beijos do Brasil 21 filly (3 months), Term Paper 21 colt (4 months), Lawanda 21 filly (3 months), explicitly captioned. Main older-foal side/front-quarter references. Dulce Periculum 21 filly (9 days) is in motion and is used only to distinguish neonatal appearance, not as a resting or walking template.
2. [Running Brook horses for sale](https://www.runningbrookqh.net/horses-for-sale.html): Rusty, birth March 24 2026, gallery August 22 2026 (approximately five months). `runningbrook-rusty-9827.jpg` near-front standing; `-9754.jpg` side walking; `-9813.jpg` rear-quarter moving. Turned/moving body, not a square rear conformation measurement.
3. [Kadon Park Desires](https://www.kadonpark.com.au/kadonparkdesires): explicitly one month old; useful youthful torso/hindquarter silhouette, grass obscures hooves.
4. [Wimborne Covergirl](https://www.wimborneparkstud.com.au/index.php?page=wimborne-covergirl): explicitly three months old, useful side-in-motion silhouette, not gait timing.
5. [Newcastle Equine Centre, foal conformation](https://www.newcastleequinecentre.net.au/equine-reproduction-foal-conformation/): contextual guidance on viewing from multiple directions and age-related change. Clinical correction examples were not used as healthy templates.
6. [Virginia Tech, crooked legs in foals](https://emc.vetmed.vt.edu/education-and-jobs/client-education/crooked-legs-foals-when-to-watch-or-worry.html): explicitly clinical orthopedic-care image and discussion. Excluded from visual target set; newborn laxity/clinical deformity should not be conflated with the intended generic foal.
7. [American Trakehner Association conformation guide](https://americantrakehner.com/the-trakehner-horse/conformation-guide/): developmental context for young horses whose croup sits higher than withers. Not a quantitative calibration source for this asset.

Breeder gallery photographs are not medical health certifications. No photographed horse is diagnosed here. No clean, age-confirmed square rear view was obtained; the original Running Brook rear-oblique picture has unverified current page identity/age. Obliquity, staggered loading and grass make numerical joint-angle/width extraction unreliable.

## Smallest next experiment

Keep the accepted front tracks, all adult assets, the current foal hip/hoof lane widths, and the protected rest mesh/UV/coat/morph arrays. Build a separate hind-only candidate with a real proximal thigh/stifle pivot and a coordinated hock/cannon solve; use it to bring the upper hind-leg transition forward and make mid-stance less permanently crouched/backward-set. Retain the existing hoof-contact paths initially to isolate articulation from another width/stride change.

Then inspect side and rear full loops and run the unchanged contact/stretch/join/loop gates. If the existing mesh cannot deform into the reference silhouette without damage, report that as a specific rest-geometry limitation before proposing any geometry edit. Do not disguise that boundary by loosening preservation tests. No implementation, production edits, commit, push, Mac access, or spending was performed for this review.
