"""Read-only witness localization and finite handoff; no solve or permissions edits."""
import json,hashlib,heapq,collections
from pathlib import Path
import numpy as np
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support');OUT=ROOT/'junction-preflight-light'
r=json.loads((OUT/'light_immutable_junction_contract.json').read_text());a=np.load(OUT/'light_immutable_junction_arrays.npz')
refs=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json').read_text())['nodes']
source=json.loads((ROOT/'virtual-shell-neck-light/light_production_identity_serialization.json').read_text())
P,E,names=a['points'],a['edges'],a['names'].tolist();allowed=a['allowed'];target=a['target'];fixed=a['fixed']
mane={int(i) for i,x in refs.items() if any(v['component']=='Mane' for v in x['sourceRefs'])}
roots={i for i in mane if any(v['component']=='Body' for v in refs[str(i)]['sourceRefs'])}
adj={i:[] for i in mane}
for u,v in E:
 if int(u) in mane and int(v) in mane:
  length=float(np.linalg.norm(P[u]-P[v]));adj[int(u)].append((int(v),length));adj[int(v)].append((int(u),length))
distance={i:float('inf') for i in mane};origin={};todo=[]
for i in sorted(roots):distance[i]=0.;origin[i]=i;heapq.heappush(todo,(0.,i))
while todo:
 dd,u=heapq.heappop(todo)
 if dd!=distance[u]:continue
 for v,length in adj[u]:
  q=dd+length
  if q<distance[v]:distance[v]=q;origin[v]=origin[u];heapq.heappush(todo,(q,v))
def witness(i):
 rr=refs[str(i)]['sourceRefs'];first=rr[0]
 return {'virtualNodeId':int(i),'sourceRefs':rr,'position':P[i].tolist(),'region':r['regionsByNode'][str(i)],
  'allowedDonors':[names[k] for k in np.where(allowed[i])[0]],'fixed':bool(fixed[i]),
  'prescribedWeights':{names[k]:float(w) for k,w in enumerate(target[i]) if w>0},
  'frozenSourceWeights':source['meshes'][first['component']]['weights'][first['vertexId']]}
empty_mane=[];empty_body=[]
for x in r['rejectedEmptyIllegalConflictingTraces']:
 i=x['node'];v=witness(i)
 if i in mane:
  root=origin.get(i);v.update({'nearestExactBodyManeRoot':witness(root) if root is not None else None,
   'maneOnlyGeodesicDistance_m':distance[i] if root is not None else None,
   'attachmentTopologyOnly':True,'ownershipInferredFromSourceDonors':False})
  empty_mane.append(v)
 else:empty_body.append(v)
worst_walk=max((p for p in r['poseChecks'] if p['pose'].startswith('Walk')),key=lambda p:p['maxFixedEdgeStretch'])
worst_neck=next(p for p in r['poseChecks'] if p['pose']=='MildNeck')
worst_stress=next(p for p in r['poseChecks'] if p['pose']=='GrazeStress')
failures=[]
for p in (worst_walk,worst_neck,worst_stress):
 failures.append({'pose':p['pose'],'maximumFixedStretch':p['maxFixedEdgeStretch'],'edgesOver1_6':p['over1_6Count'],
  'virtualEdgeId':p['worst']['virtualEdgeId'],'endpoints':[witness(i) for i in p['worst']['nodes']],
  'restLength_m':float(np.linalg.norm(np.diff(np.array(p['worst']['rest']),axis=0))),
  'posedLength_m':float(np.linalg.norm(np.diff(np.array(p['worst']['posed']),axis=0)))})
summary={'taskId':'adult_motion_specialist','outcome':'REJECTED PRE-SOLVE CONTRACT. No harmonic solve or mesh write.',
 'scope':'One neutral Light immutable ownership/anchor preflight, frozen9c reference and unchanged diagnostic pose matrices.',
 'emptyIntersectionClassification':{
  'proposedRegionVsFrozenSource':{'count':219,'maneCount':len(empty_mane),'bodyTailRootCount':len(empty_body),
   'interpretation':'An empty source intersection proves the old vector cannot anchor the proposed region. It does not independently prove proposed ownership. Frozen geometry/rest/head/ear binding does not require retaining wrong body/hair donor vectors.'},
  'definiteSourceDonorSupportProblem':{'count':len(empty_body),'reason':'Every one of these 25 Body vectors mixes both hind branches (often dominant on a remote hind Upper) with Tail at the caudal root. Opposite branches cannot both remain legal merely because those vectors came from frozen9c.',
   'nodes':empty_body},
  'unreviewedHairOwnership':{'count':len(empty_mane),'reason':'194 Mane vertices outside x>=.55 neck scaffold are not independently reviewed torso skin. Their cervical-only source vectors conflict with the proposed torso donor set; nearest exact seam-root paths are provided as attachment evidence, not a new ownership assignment.',
   'exactManeRootCount':len(roots),'nearestRootRegions':dict(collections.Counter(v['nearestExactBodyManeRoot']['region'] if v['nearestExactBodyManeRoot'] else 'UNREACHED' for v in empty_mane)),
   'vertices':empty_mane}},
 'unreviewedGeometricRegions':{'regions':r['regionCounts'],'forelegContours':'Accepted distal cuts only. The adjacent .16m graph collars and 35mm center threshold are provisional, not complete shoulders.',
  'hindContours':'Sole-contained Upper cuts are distal references only. Their .22m graph collars/center assignment are provisional and do not establish complete hips.',
  'neck':'Four reviewed cranial cuts remain the scaffold. Explicit x>.25/z>1.15/.24m junction collar and caudal hair ownership have not received anatomical acceptance.'},
 'incompatibleHardFixedTraces':{'reason':'One common trace map prevents competing anchor writes, but does not itself guarantee neighboring prescribed vectors deform continuously. The hard outer Chest1 row is incompatible with adjacent Chest/Neck_Base mass; the front parent/shaft transition also slightly fails unchanged Walk.',
  'witnesses':failures},
 'validation':{'immutablePermissionsUnchanged':r['permissionsUnchangedByAnchors'],'fallbackDonorsUsed':False,
  'wrongSideAnchors':len(r['wrongSideAnchorNodes']),'multiBranchAnchors':len(r['multiBranchAnchorNodes']),
  'illegalTargetDonors':int(np.sum((target>1e-12)&~allowed)),'fixedAnchorCount':r['fixedAnchorCount'],
  'fixedEdgesChecked':r['fixedEdgesChecked'],'poseCount':len(r['poseChecks']),'maximumAllowedInfluences':r['maximumAllowedInfluences'],
  'rejectedEmptyTraces':219,'headEarVectorsPreserved':True,'harmonicSolveRun':False,'meshWeightsWritten':False,
  'sourceSha256':r['actualBaselineSha256'],'walkMatrixSha256':r['walkMatrixSha256']},
 'requestedParentDecision':'One quick native Sol review of the proposed ownership and failed shared-parent traces. No solve is authorized. A future bounded correction should explicitly handle attached Mane ownership and caudal Pelvis/Tail branch ownership, then replace the incompatible hard Chest outer row with reviewed continuous boundary traces. Do not recover donors through fallback or mutable permissions.'}
paths=[OUT/'light_immutable_junction_contract.json',OUT/'light_immutable_junction_arrays.npz',
 ROOT/'corrected_junction_preflight_light.py',ROOT/'render_junction_preflight_light.py',ROOT/'annotate_junction_preflight_light.py',Path(__file__)]
paths.extend(OUT.glob('*_overlay.png'));paths.extend(OUT.glob('*_projection.json'))
summary['artifactSha256']={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
(OUT/'light_immutable_junction_review.json').write_text(json.dumps(summary,indent=2))
(OUT/'README.txt').write_text('''Task ID: adult_motion_specialist

REJECTED PRE-SOLVE CONTRACT. No harmonic solve, mesh weight write, pose fit, production mutation or live Blender use occurred.

light_immutable_junction_contract.json serializes immutable geometry-derived donor permissions, BOTH contour rows, collars, every allowed donor, actual fixed vectors, empty rejections and all63 pose boundary audits. light_immutable_junction_arrays.npz carries exact nodes/edges/faces/index mapping and unsolved target/fixed arrays. Frozen9c and unchanged61-frame Walk matrix hashes are verified.

The correction removes first-donor fallback and prevents set_anchor from changing permissions. It has zero wrong-side anchors, multi-branch anchors or illegal target donors. That procedural correction does not establish anatomical acceptance or deformation validity.

219 source intersections remain empty:194 Mane fringe nodes outside the reviewed cranial scaffold, and25 Body caudal-root nodes carrying both hind branches/Tail. The first group needs attachment/region review; it is not valid to label them torso only from their x coordinate. The second contains definite opposite-branch source support that cannot be preserved solely because source geometry is frozen. light_immutable_junction_review.json separates these cases and includes Mane-only paths to the172 exact Body/Mane seam roots, source vectors, positions, proposed permissions and actual fixed-boundary witnesses.

21051 fixed-fixed positive edges are checked under61 unchanged Walk matrices, mild+12/+4 neck, and unchanged GrazeStress. Maximum fixed stretch: Walk1.60945x (one edge, two failing frames), mild3.15910x (5 edges), stress5.13088x (185 edges). Body7954/8408 is Chest.1621+Neck_Base.8379 against Chest1 at the proposed neck outer row. Body885/922 is a front-right Chest/Forearm/Cannon trace with incompatible parent mass. These failures are caught before a numerical solve; whole-body field/contact/morph/Stock acceptance is not evaluated.

Six full-body rest ownership overlays plus neck_q3/hip_back details are display-only. White dots show BOTH contour rows; red marks show empty traces; original ears retain source controls. Faces use majority vertex region, so exact maps in JSON govern. Distal cuts are not complete shoulder/hip boundaries; collar widths remain provisional. No further correction/solve is authorized until parent/native Sol review.
''')
print(json.dumps({'taskId':summary['taskId'],'validation':summary['validation'],'nearestManeRootRegions':summary['emptyIntersectionClassification']['unreviewedHairOwnership']['nearestRootRegions'],'views':len(list(OUT.glob('*_overlay.png')))},indent=2))
