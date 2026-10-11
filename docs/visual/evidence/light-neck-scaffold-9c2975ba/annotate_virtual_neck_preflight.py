"""Labeled scientific overlays of measured plane intersections and source controls."""
import json
from pathlib import Path
from PIL import Image, ImageDraw
ROOT = Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support/virtual-shell-neck-light')
colors = ((255,80,90),(255,176,65),(246,231,84),(78,220,245))
views = ('front','back','left','right','top','q3','left_context','q3_context')
for view in views:
    d=json.loads((ROOT/f'light_virtual_neck_{view}_projection.json').read_text())
    im=Image.new('RGB',(1490,860),(28,30,33))
    im.paste(Image.open(ROOT/f'light_virtual_neck_{view}_raw.png').convert('RGB'),(0,80))
    draw=ImageDraw.Draw(im)
    def shift(p): return (p[0],p[1]+80)
    for n,line in d['bones'].items():
        draw.line([shift(p) for p in line],fill=(225,225,225),width=1)
    draw.text((14,10),f'Light rest / {view} / EXACT VIRTUAL-SHELL NECK PREFLIGHT / Task adult_motion_specialist',fill='white')
    draw.text((14,30),'Four measured +X cut planes; colored head = ORIGINAL skull / green-purple = ORIGINAL ear membership.',fill='white')
    draw.text((14,50),'Analysis only: no mesh weld, new faces, changed weights, ear flattening or solve. Ownership awaits review.',fill='white')
    for j,(c,color) in enumerate(zip(d['loops'],colors)):
        pts=[shift(p) for p in c['points']]
        draw.line(pts+[pts[0]],fill=color,width=3)
        y=106+j*112
        draw.text((1066,y),f"x = {c['x_m']:.2f} m / normal +X",fill=color)
        draw.text((1066,y+18),f"Closed / {c['virtualNodeCount']} shell nodes",fill=color)
        counts=c['cranialSourceVertexCounts']
        draw.text((1066,y+36),f"Body {counts['Body']} / Mane {counts['Mane']}",fill=color)
        draw.text((1066,y+54),f"Min x/z: {c['bounds'][0][0]:.4f} / {c['bounds'][0][2]:.4f} m",fill=color)
        draw.text((1066,y+72),'Whole skull; no limb/caudal exclusion',fill=color)
    draw.text((1066,575),'222 exact unique seam-edge pairs',fill='white')
    draw.text((1066,595),'Basis + four morphs exact at seams',fill='white')
    draw.text((1066,615),'0 manifold or winding failures',fill='white')
    draw.text((1066,645),'All four cranial components nested',fill='white')
    draw.text((1066,665),'2931 original skull Body vertices',fill=(230,176,134))
    draw.text((1066,685),'911 source ear membership records',fill='white')
    draw.text((1066,705),'Ear faces: >=2 positive members',fill='white')
    draw.text((1066,725),'White: canonical rest head/neck rig',fill='white')
    draw.text((14,838),'Grey skin is unassigned. Cranial topology is geometric evidence; shoulder/neck weighting has not been accepted.',fill='white')
    im.save(ROOT/f'light_virtual_neck_{view}_overlay.png')
print('TASK adult_motion_specialist VIRTUAL_NECK_ANNOTATIONS_COMPLETE')
