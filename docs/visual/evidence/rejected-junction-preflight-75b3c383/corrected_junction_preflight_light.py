"""REJECTED immutable ownership/boundary preflight. No solve or mesh writes.
This single contract catches empty source intersections and incompatible fixed
Chest/cervical traces; it is review evidence, not a passing anchor foundation.
"""
import json,hashlib,heapq
from pathlib import Path
import numpy as np
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support');OUT=ROOT/'junction-preflight-light';OUT.mkdir(exist_ok=True)
REPO=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse')
BASELINE=REPO/'art/derived/production/horse_light.blend'
EXPECTED='9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295'
assert hashlib.sha256(BASELINE.read_bytes()).hexdigest()==EXPECTED
sc=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_shell_neck_contours.json').read_text());raw=np.load(ROOT/'virtual-shell-neck-light/light_virtual_shell_arrays.npz')
P,E,T,mapping=[raw[n] for n in ('points','edges','triangles','originalToVirtual')]
refs=json.loads((ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json').read_text())['nodes'];src=json.loads((ROOT/'virtual-shell-neck-light/light_production_identity_serialization.json').read_text())
prior=np.load(ROOT/'anatomical-light-trial/light_anatomical_field_arrays.npz');names=prior['names'].tolist();ni={n:i for i,n in enumerate(names)};N,K=len(P),len(names);bones=src['restRig']['bones']
assert set(names)==set(bones)
walk_sha=hashlib.sha256(prior['walk'].tobytes()).hexdigest()
assert walk_sha=='e59fe908721c1c99c81f1b9fa42b42e67a392728d1f83d437cb38e2a830c5df4'
f=json.loads((ROOT/'front-surface-contours-light/light_front_surface_contours.json').read_text());old=json.loads((ROOT/'contour-preflight-light/light_contour_components.json').read_text())
source=np.zeros((N,K))
for i,rr in refs.items():
 r=rr['sourceRefs'][0]
 for n,w in src['meshes'][r['component']]['weights'][r['vertexId']].items():source[int(i),ni[n]]=w
head={i for i in range(N) if sum(source[i,ni[n]] for n in names if n.startswith(('Head','Jaw','Ear')))>.999 or any(source[i,ni[n]]>0 for n in names if n.startswith('Ear'))}
tail={int(i) for i,rr in refs.items() if any(r['component']=='Tail' for r in rr['sourceRefs'])};tail_roots={i for i in tail if len(refs[str(i)]['sourceRefs'])>1}
cranial={c['x_m']:set(c['selected']['cranialVirtualNodeIds']) for c in sc['cuts']};limbs={q:set(mapping[ids].tolist()) for q,ids in sc['excludedLimbBodyIds'].items()};all_limb=set.union(*limbs.values())
adj=[[] for _ in P];length=np.linalg.norm(P[E[:,0]]-P[E[:,1]],axis=1)
for (a,b),l in zip(E,length):adj[a].append((int(b),float(l)));adj[b].append((int(a),float(l)))
def distance(seeds,blocked=()):
 blocked=set(blocked);d=np.full(N,np.inf);todo=[]
 for i in seeds:
  if i not in blocked:d[i]=0.;heapq.heappush(todo,(0.,int(i)))
 while todo:
  dd,a=heapq.heappop(todo)
  if dd!=d[a]:continue
  for b,l in adj[a]:
   v=dd+l
   if b not in blocked and v<d[b]:d[b]=v;heapq.heappush(todo,(v,b))
 return d
chains={q:[n for n in names if n.startswith(q+'_')] for q in limbs};root_rows={};patches={};root_contours={};ring_specs=[]
for q in limbs:
 c=f['branches'][q]['lastValidContour'] if q.startswith('Front') else next(c for c in old['branches'][q]['selectedContours'] if c['bone']==q+'_Upper')
 root_contours[q]=c;root_rows[q]=set(mapping[c['distalBoundaryVertexIds']+c['trunkBoundaryVertexIds']].tolist())
 gd=distance(root_rows[q],head|cranial[.60]|(all_limb-limbs[q]))
 patches[q]=set(np.where(gd<(.16 if q.startswith('Front') else .22))[0].tolist())|root_rows[q]
 ring_specs.extend((q,c) for c in old['branches'][q]['selectedContours'] if c['bone']!=q+'_Upper')
# No cervical-only caudal growth. The explicit junction always permits Chest.
nr=sc['cuts'][-1]['selected'];neck_rows=set(nr['distalBoundaryVirtualNodeIds']+nr['caudalBoundaryVirtualNodeIds'])
neck_d=distance(neck_rows,head|all_limb);neck_junction=set(np.where((neck_d<.24)&(P[:,0]>.25)&(P[:,2]>1.15))[0].tolist())|neck_rows
fore_union=patches['Front_L']|patches['Front_R'];hip_union=patches['Hind_L']|patches['Hind_R'];neck_junction-=cranial[.60]|head
region=np.full(N,'torso',dtype='<U48');owners={};allowed=np.zeros((N,K),bool);mid_y=bones['Chest']['head'][1]
torso_names=['Pelvis','Spine','Spine_Mid','Spine_Upper','Chest'];neck_names=['Chest','Neck_Base','Neck','Neck_Mid','Neck_Upper','Neck_Top','Head_Base']
for i in range(N):allowed[i,[ni[n] for n in torso_names]]=True
for q,ids in limbs.items():
 for i in ids:region[i]=q+'_distal';allowed[i]=False;allowed[i,[ni[n] for n in chains[q]]]=True
for parent,qs,union in [('Chest',('Front_L','Front_R'),fore_union),('Pelvis',('Hind_L','Hind_R'),hip_union)]:
 for i in union:
  if i in head or i in cranial[.60]:continue
  contributors=[q for q in qs if i in patches[q]]
  # Both contour rows are a shared-parent junction, never a complete shoulder/hip.
  same=[q for q in contributors if (P[i,1]-mid_y)*(1 if q.endswith('_L') else -1)>0]
  q=same[0] if len(contributors)==1 and len(same)==1 and abs(P[i,1]-mid_y)>.035 else None
  region[i]=parent+'_junction_'+(q if q else 'shared');owners[i]=q
  allowed[i]=False;allowed[i,ni[parent]]=True
  if q:allowed[i,[ni[n] for n in chains[q]]]=True
for i in cranial[.55]-head:
 region[i]='cranial_cervical';allowed[i]=False;allowed[i,[ni[n] for n in neck_names]]=True
 if i in cranial[.60]:allowed[i,ni['Chest']]=False
for i in neck_junction:
 if i in head:continue
 region[i]='Chest_cervical_junction';allowed[i]=False;allowed[i,[ni[n] for n in neck_names]]=True
for i in tail:
 region[i]='tail_branch';allowed[i]=False;allowed[i,[ni[n] for n in names if n.startswith('Tail') or n=='Pelvis']]=True
for i in tail_roots:region[i]='Pelvis_tail_shared_root';allowed[i]=False;allowed[i,ni['Pelvis']]=True
for i in head:region[i]='frozen_original_head_ear';allowed[i]=source[i]>0
assert max(allowed.sum(axis=1))<=8
permission_sha=hashlib.sha256(allowed.tobytes()).hexdigest();allowed.setflags(write=False)
target=np.zeros((N,K));fixed=np.zeros(N,bool);anchor_reason={};rejections=[]
def set_anchor(ids,values,reason):
 for j,i in enumerate(ids):
  w=values[j] if np.ndim(values)==2 else values
  bad={names[k]:float(w[k]) for k in np.where((w>1e-12)&~allowed[i])[0]}
  empty=not np.any(w>1e-12)
  conflict=fixed[i] and not np.allclose(target[i],w,rtol=0,atol=1e-12)
  if bad or empty or conflict:
   rejections.append({'node':int(i),'sourceRefs':refs[str(i)]['sourceRefs'],'position':P[i].tolist(),'region':str(region[i]),'reason':reason,'illegalDonors':bad,'empty':empty,'conflict':conflict});continue
  target[i]=w;fixed[i]=True;anchor_reason.setdefault(str(i),[]).append(reason)
assert_trace=(ROOT/'author_anatomical_light_trial.py').read_text();exec(assert_trace[assert_trace.index('def adjacent_trace'):assert_trace.index('def set_anchor')])
def chain_centers(seq):return np.array([(np.array(bones[n]['head'])+np.array(bones[n]['tail']))/2 for n in seq])
neck_head=np.array(bones['Neck']['head']);nc=np.array([neck_head-np.array([.18,0,.20]),neck_head]+[bones[n]['head'] for n in ('Neck_Mid','Neck_Upper','Neck_Top','Head_Base','Head')])
trace=np.zeros((N,K))
# One common trace map is computed BEFORE any anchor proposal; overlaps cannot
# independently prescribe a second vector. Permissions remain independent.
for i in range(N):
 r=region[i]
 if r=='frozen_original_head_ear':trace[i]=source[i]
 elif r in ('tail_branch','torso'):
  trace[i]=source[i]*allowed[i]
  if trace[i].sum()==0:
   rejections.append({'node':i,'sourceRefs':refs[str(i)]['sourceRefs'],'position':P[i].tolist(),'region':str(r),'reason':'frozen source has no legal anatomical trace','empty':True,'sourceWeights':{names[k]:float(x) for k,x in enumerate(source[i]) if x>0},'allowedDonors':[names[k] for k in np.where(allowed[i])[0]],'anchorSubmitted':False});continue
  trace[i]/=trace[i].sum()
 elif r=='Pelvis_tail_shared_root':trace[i,ni['Pelvis']]=1
 elif r in ('cranial_cervical','Chest_cervical_junction'):
  tr,_,_=adjacent_trace(P[i:i+1],neck_names,nc);trace[i]=tr[0]
  if r=='Chest_cervical_junction' and P[i,2]<neck_head[2]:
   lo=min(P[j,2] for j in neck_rows);t=np.clip((P[i,2]-lo)/(neck_head[2]-lo),0,1);trace[i]=0;trace[i,ni['Chest']]=1-t;trace[i,ni['Neck_Base']]=t
  trace[i]*=allowed[i]
  if trace[i].sum()==0:
   rejections.append({'node':i,'sourceRefs':refs[str(i)]['sourceRefs'],'position':P[i].tolist(),'region':str(r),'reason':'cervical trace/immutable region intersection empty','empty':True});continue
  trace[i]/=trace[i].sum()
 elif '_junction_' in r:
  parent=str(r).split('_junction_')[0];q=owners[i]
  if q is None:trace[i,ni[parent]]=1
  else:
   seq=chains[q];tr,_,_=adjacent_trace(P[i:i+1],seq,chain_centers(seq));trace[i]=tr[0]
 else:
  q=next(q for q in limbs if str(r)==q+'_distal');tr,_,_=adjacent_trace(P[i:i+1],chains[q],chain_centers(chains[q]));trace[i]=tr[0]
# A measured boundary interpolation joins lateral traces to common parent.
# This is an anchor trace only, never a solved field or composed skin mask.
for parent,qs in [('Chest',('Front_L','Front_R')),('Pelvis',('Hind_L','Hind_R'))]:
 shared=[i for i in range(N) if region[i]==parent+'_junction_shared']
 ds=distance(shared) if shared else np.full(N,np.inf)
 for i in range(N):
  if str(region[i]).startswith(parent+'_junction_') and owners.get(i):
   mass=min(ds[i]/.20,1.);trace[i]*=mass;trace[i,ni[parent]]+=1-mass
# Outer Chest/cervical junction boundary is explicitly shared Chest. Free
# interior junction permissions retain Chest + cervical; no abrupt donor loss.
neck_outer={i for i in neck_junction if any(j not in neck_junction and j not in cranial[.55] for j,_ in adj[i]) and i not in neck_rows and i not in head}
for i in neck_outer:trace[i]=0;trace[i,ni['Chest']]=1
for i in sorted(head|tail):set_anchor([i],trace[i],'preserve original head/ear or anatomical tail trace')
for q,ids in root_rows.items():set_anchor(sorted(ids-head),trace[sorted(ids-head)],'shared-parent junction BOTH contour rows '+q)
for q,c in ring_specs:
 ids=set(mapping[c['distalBoundaryVertexIds']+c['trunkBoundaryVertexIds']].tolist())-head
 set_anchor(sorted(ids),trace[sorted(ids)],'own distal shaft trace '+q)
for c in sc['cuts']:
 ids=set(c['selected']['distalBoundaryVirtualNodeIds']+c['selected']['caudalBoundaryVirtualNodeIds'])-head
 set_anchor(sorted(ids),trace[sorted(ids)],'reviewed neck trace '+str(c['x_m']))
set_anchor(sorted(neck_outer),trace[sorted(neck_outer)],'explicit outer Chest/cervical shared transition')
for q,ids in json.loads((ROOT/'anatomical-light-trial/light_anatomical_field_report.json').read_text())['persistentSoleIds'].items():
 ii=sorted(set(mapping[ids].tolist()));w=np.zeros(K);w[ni[q+'_Hoof']]=1;set_anchor(ii,w,'persistent sole '+q)
assert hashlib.sha256(allowed.tobytes()).hexdigest()==permission_sha
# Adjacency preflight uses only fixed trace endpoints, never fills free weights.
edge_ids=np.where(fixed[E[:,0]]&fixed[E[:,1]]&(length>1e-10))[0]
G=target/np.maximum(target.sum(axis=1,keepdims=True),1e-12);ph=np.column_stack((P,np.ones(N)))
DM={f'Walk{j+1}':D for j,D in enumerate(prior['walk'])};DM['GrazeStress']=prior['grazeStress'];delta={}
for n in names:
 par=bones[n]['parent'];D=delta.get(par,np.eye(4)).copy();angle={'Neck_Base':12,'Neck':4}.get(n,0)
 if angle:
  theta=np.radians(angle);R=np.eye(4);R[0,0]=R[2,2]=np.cos(theta);R[0,2]=np.sin(theta);R[2,0]=-np.sin(theta);h=np.array(bones[n]['head']);A=np.eye(4);A[:3,3]=h;B=np.eye(4);B[:3,3]=-h;D=D@A@R@B
 delta[n]=D
DM['MildNeck']=np.array([delta[n] for n in names]);pose_checks=[]
for label,D in DM.items():
 M=np.einsum('vb,bca->vca',G,D[:,:3,:]);Q=np.einsum('vca,va->vc',M,ph);rat=np.linalg.norm(Q[E[edge_ids,0]]-Q[E[edge_ids,1]],axis=1)/length[edge_ids]
 k=int(np.argmax(rat));ei=int(edge_ids[k]);uv=E[ei]
 pose_checks.append({'pose':label,'maxFixedEdgeStretch':float(rat[k]),'over1_6Count':int(sum(rat>1.6)),'worst':{'virtualEdgeId':ei,'nodes':uv.tolist(),'sourceRefs':[refs[str(i)]['sourceRefs'] for i in uv],'rest':P[uv].tolist(),'posed':Q[uv].tolist(),'regions':[str(region[i]) for i in uv],'weights':[{names[k]:float(x) for k,x in enumerate(target[i]) if x>0} for i in uv]}})
contralateral=[]
for i in range(N):
 if not fixed[i]:continue
 qs={n.rsplit('_',1)[0] for n in names if False}
 active={q for q in limbs if any(target[i,ni[n]]>0 for n in chains[q])}
 if len(active)>1:contralateral.append(i)
wrong_side=[i for i in range(N) if fixed[i] and any(target[i,ni[n]]>0 and (P[i,1]-mid_y)*(1 if q.endswith('_L') else -1)<-.025 for q in limbs for n in chains[q])]
contract={'taskId':'adult_motion_specialist','candidateOnly':True,'harmonicSolveRun':False,'meshWeightsWritten':False,'accepted':False,
 'baselineSha256':'9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295','exactShellSourceBinding':'virtual-shell-neck-light/light_current_production_correspondence.json',
 'permissionsImmutableSha256':permission_sha,'permissionsUnchangedByAnchors':True,'fallbackDonorsUsed':False,'headEarSourceVectorsPreserved':True,
 'regionCounts':{str(r):int(sum(region==r)) for r in np.unique(region)},'rootRows':{q:sorted(v) for q,v in root_rows.items()},
 'junctionDomains':{q:sorted(v) for q,v in patches.items()},'declaredDistalScopes':{q:sorted(v) for q,v in limbs.items()},
 'hindRootScope':'Sole-contained Upper cuts remain distal reference sets; BOTH rows and their collars are shared-parent junctions, not complete hip ownership.',
 'explicitChestCervicalJunctionNodes':sorted(neck_junction),'outerChestTransitionAnchors':sorted(neck_outer),
 'allowedDonorsByNode':{str(i):[names[k] for k in np.where(allowed[i])[0]] for i in range(N)},'regionsByNode':{str(i):str(region[i]) for i in range(N)},
 'fixedAnchors':{str(i):{'sourceRefs':refs[str(i)]['sourceRefs'],'position':P[i].tolist(),'region':str(region[i]),'weights':{names[k]:float(x) for k,x in enumerate(target[i]) if x>0},'reasons':anchor_reason.get(str(i),[])} for i in np.where(fixed)[0]},
 'rejectedEmptyIllegalConflictingTraces':rejections,'fixedEdgesChecked':len(edge_ids),'poseChecks':pose_checks,
 'multiBranchAnchorNodes':contralateral,'wrongSideAnchorNodes':wrong_side,'sourceFilesMutated':False,
 'gates':{'immutablePermissions':True,'zeroIllegalEmptyConflictingTraces':not rejections,'zeroMultiBranchAnchors':not contralateral,'zeroWrongSideAnchors':not wrong_side,'fixedBoundaryStretchAllPoses':all(p['maxFixedEdgeStretch']<=1.6 for p in pose_checks)},
 'limitation':'Permissions and prescribed fixed traces only. No free harmonic weights, whole-body animation acceptance, reach fit, morph/Stock expansion or production write.'}
contract.update({'frozenSource':str(BASELINE),'actualBaselineSha256':hashlib.sha256(BASELINE.read_bytes()).hexdigest(),
 'referenceSerializationSha256':hashlib.sha256((ROOT/'virtual-shell-neck-light/light_production_identity_serialization.json').read_bytes()).hexdigest(),
 'sourceIndexMapSha256':hashlib.sha256((ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json').read_bytes()).hexdigest(),
 'walkMatrixSha256':walk_sha,'walkMotionProvenance':json.loads((ROOT/'anatomical-light-trial/light_anatomical_field_report.json').read_text())['motionProvenance'],
 'grazeStressMatrixSha256':hashlib.sha256(prior['grazeStress'].tobytes()).hexdigest(),
 'fixedAnchorCount':int(fixed.sum()),'maximumAllowedInfluences':int(allowed.sum(axis=1).max()),
 'directBoundaryCounts':{'multiBranch':len(contralateral),'wrongSide':len(wrong_side),'illegalTargetDonors':int(np.sum((target>1e-12)&~allowed))},
 'traceRejectionMeaning':'The 219 rejected torso proposals are non-anchor source intersections. They were left empty and were never substituted with any donor. A complete anchor constructor must deliberately handle or omit them before a solve; source foreign mass is not anatomical permission.',
 'fieldNotEvaluated':True,'prescribedTraceOutcome':'REJECTED: hard outer Chest trace is incompatible with nearby Chest/Neck_Base traces. No numerical weight solve follows this preflight.'})
np.savez(OUT/'light_immutable_junction_arrays.npz',points=P,edges=E,triangles=T,originalToVirtual=mapping,allowed=allowed,regions=region,target=target,fixed=fixed,names=names)
(OUT/'light_immutable_junction_contract.json').write_text(json.dumps(contract,indent=2))
print('TASK adult_motion_specialist IMMUTABLE_JUNCTION_PREFLIGHT',contract['gates'],'rejections',len(rejections),'wrongside',len(wrong_side),'fixededges',len(edge_ids),'max',max(p['maxFixedEdgeStretch'] for p in pose_checks))
