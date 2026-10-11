"""Scientific rest ownership overlays for one rejected, unsolved contract."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support/junction-preflight-light')
r=json.loads((ROOT/'light_immutable_junction_contract.json').read_text())
legend=[('torso','Torso: Pelvis/spine/Chest'),('Chest_junction_shared','Shared thorax: Chest only'),
 ('Chest_junction_Front_L','Lateral front L: Chest + own chain'),('Chest_junction_Front_R','Lateral front R: Chest + own chain'),
 ('Front_L_distal','Distal front L: own chain'),('Front_R_distal','Distal front R: own chain'),
 ('Pelvis_junction_shared','Shared hip reference: Pelvis only'),('Pelvis_junction_Hind_L','Lateral hind L: Pelvis + own chain'),
 ('Pelvis_junction_Hind_R','Lateral hind R: Pelvis + own chain'),('Hind_L_distal','Distal hind L: own chain'),
 ('Hind_R_distal','Distal hind R: own chain'),('Chest_cervical_junction','Chest/cervical junction'),
 ('cranial_cervical','Cranial cervical (upper no Chest)'),('frozen_original_head_ear','Original coherent head vectors'),
 ('original_Ear_L','Original L ear controls preserved'),('original_Ear_R','Original R ear controls preserved'),
 ('tail_branch','Tail: Pelvis + tail chain')]
for path in ROOT.glob('light_immutable_*_projection.json'):
 d=json.loads(path.read_text());view=d['view'];im=Image.new('RGB',(1490,920),(28,30,33))
 im.paste(Image.open(ROOT/f'light_immutable_{view}_raw.png').convert('RGB'),(0,90));draw=ImageDraw.Draw(im)
 def shift(p):return (p[0],p[1]+90)
 def onscreen(p):return 0<=p[0]<1050 and 0<=p[1]<740
 for n,line in d['bones'].items():draw.line([shift(p) for p in line],fill=(224,224,224),width=1)
 for rows in d['rootRows'].values():
  for a in rows:
   if not onscreen(a['pixel']):continue
   x,y=shift(a['pixel']);draw.ellipse((x-1.4,y-1.4,x+1.4,y+1.4),fill='white')
 for a in d['rejectedTracePoints']:
  if not onscreen(a['pixel']):continue
  x,y=shift(a['pixel']);draw.line((x-2,y-2,x+2,y+2),fill=(255,86,88),width=1)
 for j,a in enumerate(d['worstFixedTraces'][:2]):
  if not any(onscreen(p) for p in a['pixels']):continue
  pp=[shift(p) for p in a['pixels']];draw.line(pp,fill=(255,40,70),width=4)
  for x,y in pp:draw.ellipse((x-4,y-4,x+4,y+4),outline='white',width=1)
  x,y=pp[0];ids=[str(ref[0]['vertexId']) for ref in a['worst']['sourceRefs']]
  draw.text((min(max(x+12,8),800),min(max(y-22-20*j,95),810)),f"Body {' / '.join(ids)}: {a['pose']} {a['stretch']:.3f}x",fill='white')
 draw.text((14,10),f'Light neutral rest / {view} / REJECTED IMMUTABLE JUNCTION PREFLIGHT / Task adult_motion_specialist',fill='white')
 draw.text((14,32),'Display ownership only. White dots: BOTH contour rows. Red marks: rejected empty source traces.',fill='white')
 draw.text((14,54),'Frozen 9c2975ba geometry/rest/ears; no solve, skin weights written, pose fit, new faces, weld or source save.',fill='white')
 for j,(key,label) in enumerate(legend):
  color=tuple(round(c*255) for c in d['colors'][key][:3]);y=108+j*26
  draw.rectangle((1066,y,1078,y+12),fill=color);draw.text((1086,y),label,fill='white')
 draw.text((1066,569),'Immutable donor permissions unchanged',fill='white')
 draw.text((1066,590),'0 wrong-side / 0 multi-branch anchors',fill='white')
 draw.text((1066,611),'219 empty torso traces REJECTED',fill=(255,110,110))
 draw.text((1066,632),f"{r['fixedEdgesChecked']} fixed edges / 63 poses",fill='white')
 draw.text((1066,653),'Walk max 1.609x / 2 failing frames',fill=(255,110,110))
 draw.text((1066,674),'Mild max 3.159x / 5 failing edges',fill=(255,110,110))
 draw.text((1066,695),'Stress max 5.131x / 185 failing edges',fill=(255,110,110))
 draw.text((1066,724),'Hard outer Chest trace fails before solve.',fill='white')
 draw.text((1066,745),'Hind cuts: distal references only.',fill='white')
 draw.text((1066,766),'No full hip/shoulder ownership claim.',fill='white')
 draw.text((14,848),'Faces use majority vertex color; exact per-node regions/permissions and all rejected IDs/positions are serialized.',fill='white')
 draw.text((14,870),'Shared-parent collar widths and trace construction remain unaccepted. Colored regions are proposals, not approved anatomy.',fill='white')
 draw.text((14,892),'This is rest ownership proof. Fixed-trace stretch is a pre-solve boundary test, not whole-body field or motion acceptance.',fill='white')
 im.save(ROOT/f'light_immutable_{view}_overlay.png')
print('TASK adult_motion_specialist IMMUTABLE_OWNERSHIP_ANNOTATIONS_COMPLETE')
