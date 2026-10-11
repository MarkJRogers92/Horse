"""Rejected proximal-interface geometry and boundary-role evidence only."""
import json
from pathlib import Path
from PIL import Image,ImageDraw
OUT=Path(__file__).resolve().parent;r=json.loads((OUT/'light_attachment_contract.json').read_text())
for p in OUT.glob('*_projection.json'):
 d=json.loads(p.read_text());view=d['view'];im=Image.new('RGB',(1490,910),(28,30,33));im.paste(Image.open(OUT/f'light_attachment_{view}_raw.png').convert('RGB'),(0,85));draw=ImageDraw.Draw(im)
 def shift(p):return p[0],p[1]+85
 def valid(p):return 0<=p[0]<1050 and 0<=p[1]<740
 for q,j in d['joints'].items():
  a,b=shift(j['head']),shift(j['tail']);draw.line((a,b),fill='white',width=2)
  for point in (a,b):draw.ellipse((point[0]-4,point[1]-4,point[0]+4,point[1]+4),outline='white',width=1)
  draw.text((a[0]+6,a[1]-15),q+' Upper head',fill='white')
 for points,color in ((d['parentBoundaryPoints'],(255,155,75)),(d['distalBoundaryPoints'],(85,225,255))):
  for q,values in points.items():
   if q not in d['joints']:continue
   for a in values:
    if valid(a):
     x,y=shift(a);draw.ellipse((x-1.7,y-1.7,x+1.7,y+1.7),fill=color)
 for points in d['attachmentPaths']:
  if any(valid(a) for a in points):draw.line([shift(a) for a in points],fill=(248,220,130),width=1)
 for edge in d['worstFixedEdges']:
  if not any(valid(a) for a in edge['points']):continue
  points=[shift(a) for a in edge['points']];draw.line(points,fill=(255,50,75),width=3)
  x,y=points[0];ids=[str(a[0]['vertexId']) for a in edge['sourceRefs']]
  draw.text((min(max(x+10,10),780),min(max(y-22,95),810)),f"{' / '.join(ids)} {edge['pose']} {edge['stretch']:.3f}x",fill='white')
 draw.text((14,10),f'Light rest / {view} / REJECTED PROXIMAL-INTERFACE PREFLIGHT / Task adult_motion_specialist',fill='white')
 draw.text((14,32),'White: actual source Upper joint head/tail. Orange: proposed parent edge. Cyan dots: proposed distal exit.',fill='white')
 draw.text((14,54),'Frozen 9c2975ba geometry; no Body solve, mesh weights written, pose refit or production save.',fill='white')
 for j,(key,label) in enumerate([('Front_L_patch','Front L connected patch'),('Front_R_patch','Front R connected patch'),('Hind_L_patch','Hind L connected patch'),('Hind_R_patch','Hind R connected patch'),('complete_Chest_cervical_proposal','Complete Chest/cervical proposal'),('Pelvis_Tail_proposal','25 Body + 50 coupled tail roots')]):
  y=112+j*28;color=tuple(round(v*255) for v in d['colors'][key][:3]);draw.rectangle((1066,y,1078,y+12),fill=color);draw.text((1086,y),label,fill='white')
 for j,q in enumerate(('Front_L','Front_R','Hind_L','Hind_R')):
  c=r['intrinsicArcContracts'][q];y=300+j*46
  draw.text((1066,y),f"{q}: {len(c['proximalInterfaceNodes'])} nodes",fill='white')
  draw.text((1066,y+18),f"Parent {len(c['sharedParentBoundaryNodes'])} / distal {len(c['distalBoundaryNodes'])}",fill='white')
 for j,text in enumerate(['Parent/distal roles touch at a corner.','Body1815 Chest1 / Body2006 Upper1','Walk 19.155x across 5.29mm','Mane compatible union:12 controls','Positive Mane solve NOT run','261 rejections;6 wrong-side anchors','Mild8.398x / stress75.787x diagnostic','Skull/ears exact; tail roots hind-free']):draw.text((1066,503+j*24),text,fill=(255,130,130) if j<7 else 'white')
 draw.text((14,852),'Rest-joint proximity plus connected geometry does not establish an anatomically valid proximal ownership interface.',fill='white')
 draw.text((14,877),'Both boundary neighbor roles, source landmarks, attachment guards and all failed traces are serialized; no passing field is claimed.',fill='white')
 im.save(OUT/f'light_proximal_{view}_overlay.png')
print('TASK adult_motion_specialist PROXIMAL_PATCH_ANNOTATIONS_COMPLETE')
