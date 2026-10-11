"""One proximal-interface constructor and authorized Mane interpolation only.
No main Body field solve or mesh writes.

Body coordinates follow existing Body edges only, so neither hair nor another
owned limb can shortcut a branch. Hair uses separately serialized root paths.
Control knots are geometric observations, independent of motion matrices.
"""
import json, hashlib, heapq, collections
from pathlib import Path
import numpy as np
OUT=Path(__file__).resolve().parent;ROOT=OUT.parent
REPO=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse')
BASELINE=REPO/'art/derived/production/horse_light.blend'
EXPECTED='9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295'
assert hashlib.sha256(BASELINE.read_bytes()).hexdigest()==EXPECTED
def read(p):return json.loads(p.read_text())
def json_numpy(value):
 if isinstance(value,np.generic):return value.item()
 raise TypeError('Unsupported diagnostic JSON value: '+type(value).__name__)
frozen=read(ROOT/'junction-preflight-light/light_immutable_junction_contract.json')
assert hashlib.sha256((ROOT/'junction-preflight-light/light_immutable_junction_contract.json').read_bytes()).hexdigest()=='75b3c38385cd3d82a88941e9918179e0ad8fd94e680bdc4ac9a90f418bde2938'
sc=read(ROOT/'virtual-shell-neck-light/light_virtual_shell_neck_contours.json')
src=read(ROOT/'virtual-shell-neck-light/light_production_identity_serialization.json')
refs=read(ROOT/'virtual-shell-neck-light/light_virtual_node_source_refs.json')['nodes']
old=np.load(ROOT/'junction-preflight-light/light_immutable_junction_arrays.npz')
prior=np.load(ROOT/'anatomical-light-trial/light_anatomical_field_arrays.npz')
P,E,T,mapping,names=[old[k] for k in ('points','edges','triangles','originalToVirtual','names')]
names=names.tolist();ni={n:i for i,n in enumerate(names)};N,K=len(P),len(names)
assert hashlib.sha256(prior['walk'].tobytes()).hexdigest()==frozen['walkMatrixSha256']
source=np.zeros((N,K));body_map={};source_refs={}
for key,node in refs.items():
 i=int(key);source_refs[i]=node['sourceRefs'];first=node['sourceRefs'][0]
 for n,w in src['meshes'][first['component']]['weights'][first['vertexId']].items():source[i,ni[n]]=w
 for a in node['sourceRefs']:
  if a['component']=='Body':body_map[a['vertexId']]=i
body=set(body_map.values());head={i for i in range(N) if frozen['regionsByNode'][str(i)]=='frozen_original_head_ear'}
body_edges=np.unique(np.sort(np.array([[body_map[a],body_map[b]] for a,b in src['meshes']['Body']['edges']]),axis=1),axis=0)
body_adj={i:[] for i in body};all_adj=[[] for _ in P]
for a,b in body_edges:
 length=float(np.linalg.norm(P[a]-P[b]));body_adj[int(a)].append((int(b),length));body_adj[int(b)].append((int(a),length))
for a,b in E:
 length=float(np.linalg.norm(P[a]-P[b]));all_adj[int(a)].append((int(b),length));all_adj[int(b)].append((int(a),length))
def distances(seeds,domain,adj):
 domain=set(domain);d={i:np.inf for i in domain};origin={};prev={};todo=[]
 for i in sorted(set(seeds)&domain):d[i]=0.;origin[i]=i;heapq.heappush(todo,(0.,i))
 while todo:
  dd,a=heapq.heappop(todo)
  if dd!=d[a]:continue
  for b,length in adj[a]:
   if b not in domain:continue
   value=dd+length
   if value<d[b]:d[b]=value;origin[b]=origin[a];prev[b]=a;heapq.heappush(todo,(value,b))
 return d,origin,prev
def boundary(domain,adj,excluded=()):
 excluded=set(excluded);return {i for i in domain if any(j not in domain and j not in excluded for j,_ in adj[i])}
def vector(seq,knots,s):
 if not np.isfinite(s) or not np.all(np.diff(knots)>1e-9):return np.zeros(K)
 j=min(len(seq)-2,max(0,int(np.searchsorted(knots,s,side='right')-1)))
 t=float(np.clip((s-knots[j])/(knots[j+1]-knots[j]),0,1));v=np.zeros(K)
 v[ni[seq[j]]]=1-t;v[ni[seq[j+1]]]=t;return v
def describe(i):return {'node':int(i),'sourceRefs':source_refs[i],'position':P[i].tolist()}
region=old['regions'].astype('<U90').copy();allowed=old['allowed'].copy()
cranial={c['x_m']:set(c['selected']['cranialVirtualNodeIds']) for c in sc['cuts']}
limbs={q:set(v) for q,v in frozen['declaredDistalScopes'].items()};all_limb=set.union(*limbs.values())
neck_rows=set(sc['cuts'][-1]['selected']['distalBoundaryVirtualNodeIds']+sc['cuts'][-1]['selected']['caudalBoundaryVirtualNodeIds'])
neck_body_rows=neck_rows&body
# The entire caudal collar is a proposed Chest/cervical junction. No x/z crop.
near,_,_=distances(neck_body_rows,body-head-all_limb-cranial[.60],body_adj)
new_neck_body={i for i,d in near.items() if d<.24}|neck_body_rows
new_neck_body-=head|all_limb|cranial[.60]
for i in new_neck_body:
 region[i]='Chest_cervical_complete_collar';allowed[i]=False
 if i in cranial[.55]:allowed[i,[ni[n] for n in ('Chest','Neck_Base','Neck','Neck_Mid','Neck_Upper','Neck_Top','Head_Base')]]=True
 else:allowed[i,[ni[n] for n in ('Chest','Neck_Base')]]=True
# Previous coordinate-clipped Body junction is superseded by this collar.
for i in body-new_neck_body-head:
 if region[i]=='Chest_cervical_junction' and i not in cranial[.55]:
  region[i]='torso';allowed[i]=False;allowed[i,[ni[n] for n in ('Pelvis','Spine','Spine_Mid','Spine_Upper','Chest')]]=True
empty=frozen['rejectedEmptyIllegalConflictingTraces']
body_tail={x['node'] for x in empty if any(v['component']=='Body' for v in x['sourceRefs'])}
assert len(body_tail)==25
tail_roots={i for i in range(N) if str(region[i])=='Pelvis_tail_shared_root'}
for i in body_tail|tail_roots:
 region[i]='Pelvis_proximal_Tail_transition';allowed[i]=False;allowed[i,[ni[n] for n in ('Pelvis','Tail_01')]]=True
# Root paths use ONLY actual Mane edges; exact shared nodes are the seeds.
mane={i for i in range(N) if any(a['component']=='Mane' for a in source_refs[i])};mane_roots=mane&body
mane_map={a['vertexId']:i for i in mane for a in source_refs[i] if a['component']=='Mane'}
mane_adj={i:[] for i in mane}
for a,b in src['meshes']['Mane']['edges']:
 u,v=mane_map[a],mane_map[b];length=float(np.linalg.norm(P[u]-P[v]));mane_adj[u].append((v,length));mane_adj[v].append((u,length))
md,mo,mp=distances(mane_roots,mane,mane_adj);attached={x['node'] for x in empty if x['node'] in mane};assert len(attached)==194
attachment_paths=[];constructor_rejections=[]
for i in sorted(attached):
 root=mo.get(i)
 if root is None:
  constructor_rejections.append({**describe(i),'reason':'no exact Body/Mane attachment path'});continue
 path=[i]
 while path[-1]!=root:path.append(mp[path[-1]])
 allowed[i]=allowed[root];region[i]='Mane_attached_'+str(region[root])
 attachment_paths.append({'node':i,'root':root,'path':path,'distance_m':md[i],'sourceRefsAlongPath':[source_refs[j] for j in path],
  'positionsAlongPath':P[path].tolist(),'rootRegion':str(region[root]),'allowedDonors':[names[k] for k in np.where(allowed[root])[0]]})
# BOTH x=.55 contour rows have the same reviewed junction permission band.
for i in neck_rows-head:
 allowed[i]=False;allowed[i,[ni[n] for n in ('Chest','Neck_Base','Neck','Neck_Mid','Neck_Upper','Neck_Top','Head_Base')]]=True
 if i in body:region[i]='Chest_cervical_complete_collar'
# Restore reviewed own-sole scopes and BOTH distal interface rows. A trunk
# row of a distal cut is not parent skin. Old broad collars carry own shafts.
for q in limbs:
 for i in (limbs[q]|set(frozen['rootRows'][q]))-head:
  owner=q
  if q.startswith('Hind') and i not in limbs[q]:
   owner='Hind_L' if P[i,1]>src['restRig']['bones']['Pelvis']['head'][1] else 'Hind_R'
  region[i]=owner+'_distal';allowed[i]=False;allowed[i,[ni[n] for n in names if n.startswith(owner+'_')]]=True
 for i in body:
  if str(region[i])==('Chest' if q.startswith('Front') else 'Pelvis')+'_junction_'+q:
   region[i]=q+'_distal';allowed[i]=False;allowed[i,[ni[n] for n in names if n.startswith(q+'_')]]=True
# Proposed proximal interfaces use actual Upper rest landmarks + connected
# original Body geometry. No distal cut is used as a parent origin.
proximal={};bones=src['restRig']['bones'];mid_y=bones['Chest']['head'][1]
for q in limbs:
 h=np.array(bones[q+'_Upper']['head']);t=np.array(bones[q+'_Upper']['tail']);axis=t-h;L=float(np.linalg.norm(axis));axis/=L
 side=1 if q.endswith('_L') else -1
 station=(P-h)@axis
 radius=np.linalg.norm(P-h-station[:,None]*axis,axis=1)
 candidate={i for i in body-head-cranial[.55]-all_limb-body_tail-tail_roots-mane_roots if (P[i,1]-mid_y)*side>0 and -.5*L<=station[i]<=.5*L and radius[i]<.65*L}
 seed=min(candidate,key=lambda i:float(np.linalg.norm(P[i]-h))) if candidate else None
 domain=set()
 if seed is not None:
  todo=[seed];domain={seed}
  while todo:
   u=todo.pop()
   for v,_ in body_adj[u]:
    if v in candidate and v not in domain:domain.add(v);todo.append(v)
 edges={i:[j for j,_ in body_adj[i] if j not in domain] for i in domain if any(j not in domain for j,_ in body_adj[i])}
 distal={i for i,neighbors in edges.items() if any(station[j]>.5*L for j in neighbors)}
 parent=set(edges)-distal
 for i in domain:
  region[i]=('Chest' if q.startswith('Front') else 'Pelvis')+'_proximal_'+q
  allowed[i]=False;allowed[i,[ni[n] for n in [('Chest' if q.startswith('Front') else 'Pelvis')]+[n for n in names if n.startswith(q+'_')]]]=True
 proximal[q]={'nodes':domain,'parent':parent,'distal':distal,'seed':seed,'head':h,'tail':t,'axis':axis,'length':L,'station':station,'boundaryNeighbors':edges}
trace=np.zeros((N,K));arc_reports={};arcs=np.full(N,np.nan)
# Free torso proposals remove source foreign donors; zero intersections reject.
for i in range(N):
 if region[i]=='frozen_original_head_ear':trace[i]=source[i]
 elif str(region[i]) in ('torso','tail_branch'):
  v=source[i]*allowed[i]
  if v.sum()>0:trace[i]=v/v.sum()
  else:constructor_rejections.append({**describe(i),'region':str(region[i]),'reason':'empty source/immutable-region trace intersection','sourceWeights':{names[k]:float(x) for k,x in enumerate(source[i]) if x>0}})
 elif region[i] in ('Chest_junction_shared','Pelvis_junction_shared'):
  trace[i,ni[str(region[i]).split('_junction_')[0]]]=1
# One anatomically positioned shaft trace per OWN canonical chain.
old_contours=read(ROOT/'contour-preflight-light/light_contour_components.json')
sole_ids=read(ROOT/'anatomical-light-trial/light_anatomical_field_report.json')['persistentSoleIds']
chains={q:[n for n in names if n.startswith(q+'_')] for q in limbs}
def shaft(point,q):
 seq=chains[q];cc=np.array([(np.array(bones[n]['head'])+np.array(bones[n]['tail']))/2 for n in seq]);knots=np.r_[0.,np.cumsum(np.linalg.norm(np.diff(cc,axis=0),axis=1))]
 best=np.inf;parameter=0.
 for j,(h,t) in enumerate(zip(cc,cc[1:])):
  delta=t-h;f=float(np.clip((point-h)@delta/(delta@delta),0,1));dist=float(np.linalg.norm(point-h-f*delta))
  if dist<best:best=dist;parameter=knots[j]+f*(knots[j+1]-knots[j])
 return vector(seq,knots,parameter),parameter,cc,knots
for q in limbs:
 parent='Chest' if q.startswith('Front') else 'Pelvis';domain={i for i in body if str(region[i]) in (q+'_distal',parent+'_proximal_'+q)}
 for i in domain:trace[i],arcs[i],_,_=shaft(P[i],q)
 patch=proximal[q];pn=patch['nodes'];dp,_,_=distances(patch['parent'],pn,body_adj);dl,_,_=distances(patch['distal'],pn,body_adj)
 for i in pn:
  den=dp[i]+dl[i]
  if not np.isfinite(den) or den<=1e-12:constructor_rejections.append({**describe(i),'reason':'undefined proximal parent/distal interface distance'});trace[i]=0;continue
  fraction=dp[i]/den;trace[i]*=fraction;trace[i,ni[parent]]+=1-fraction
 _,_,cc,knots=shaft(P[next(iter(domain))],q)
 arc_reports[q]={'domainNodes':sorted(domain),'controlSequence':chains[q],'controlKnots_m':knots.tolist(),'restShaftCenters':cc.tolist(),
  'parameter':'Rest canonical centers along this owned shaft; no remapping to a short collar arc.',
  'proximalInterfaceNodes':sorted(pn),'jointHead':patch['head'].tolist(),'jointTail':patch['tail'].tolist(),'jointLength_m':patch['length'],
  'patchBounds':[-.5*patch['length'],.5*patch['length'],.65*patch['length']],'nearestConnectedSurfaceSeed':describe(patch['seed']) if patch['seed'] is not None else None,
  'sharedParentBoundaryNodes':sorted(patch['parent']),'sharedParentBoundaryPositions':P[sorted(patch['parent'])].tolist(),
  'distalBoundaryNodes':sorted(patch['distal']),'distalBoundaryPositions':P[sorted(patch['distal'])].tolist(),
  'bothBoundaryNeighborRoles':{str(i):[{'node':j,'sourceRefs':source_refs[j],'position':P[j].tolist(),'jointAxisStation_m':float(patch['station'][j]),'region':str(region[j]),'role':'distal exit' if patch['station'][j]>.5*patch['length'] else 'proposed parent/trunk'} for j in neighbors] for i,neighbors in patch['boundaryNeighbors'].items()},
  'rootRows':frozen['rootRows'][q],'completeShoulderHipOwnershipClaimed':False}
# A Body-only caudal parent boundary drives ONE coordinate for the neck chain.
neck_domain={i for i in body-head if str(region[i]) in ('Chest_cervical_complete_collar','Chest_cervical_junction','cranial_cervical')}
neck_seeds=boundary(neck_domain,body_adj,excluded=head)
nd,_,_=distances(neck_seeds,neck_domain,body_adj)
upper=(cranial[.60]&neck_domain);base_knot=min(nd[i] for i in upper)
head_boundary={i for i in neck_domain if any(j in head for j,_ in body_adj[i])}
head_knot=float(np.median([nd[i] for i in head_boundary]))
neck_knots=np.r_[0.,np.linspace(base_knot,head_knot,6)]
neck_seq=['Chest','Neck_Base','Neck','Neck_Mid','Neck_Upper','Neck_Top','Head_Base']
if not np.all(np.isfinite(neck_knots)) or not np.all(np.diff(neck_knots)>1e-9):constructor_rejections.append({'branch':'Neck','reason':'non-increasing measured arc-control knots','knots':neck_knots.tolist()})
for i in neck_domain:arcs[i]=nd[i];trace[i]=vector(neck_seq,neck_knots,nd[i])
# Caudal permissions stop at the adjacent Chest/Neck_Base pair, independently
# of the trace. Any overrun rejects; donor permissions are never broadened.
arc_reports['Neck']={'domainNodes':sorted(neck_domain),'sharedParentBoundaryNodes':sorted(neck_seeds),'sharedParentBoundaryPositions':P[sorted(neck_seeds)].tolist(),
 'controlSequence':neck_seq,'controlKnots_m':neck_knots.tolist(),'baseKnotRule':'Minimum Body-only parent distance in reviewed x=.60 cranial component.',
 'headKnotRule':'Median Body-only parent distance at the original protected-head adjacency.',
 'caudalCollarProposalNodes':sorted(new_neck_body),'collarWidth_m':.24,'coordinateCapsUsed':False,'hairShortcutUsed':False,
 'headBoundaryNodes':sorted(head_boundary),'scaffoldBandDistances':{str(x):{'min_m':min(nd[i] for i in ids&neck_domain),'median_m':float(np.median([nd[i] for i in ids&neck_domain]))} for x,ids in cranial.items()}}
# Nonprotected existing neck hair inherits its closest exact Body root trace.
# This is explicit attachment propagation, not a Body ownership shortcut.
neck_hair={i for i in mane-body-head if str(region[i]) in ('Chest_cervical_junction','cranial_cervical')}
for i in neck_hair:
 root=mo.get(i)
 if root is not None:trace[i]=trace[root]
 else:constructor_rejections.append({**describe(i),'reason':'unreached existing neck hair root'})
# The caudal Body/tail seam proposal has Pelvis and proximal Tail only.
proximal_domain=body_tail|tail_roots
ps=boundary(proximal_domain,all_adj,excluded={i for i in range(N) if region[i]=='tail_branch'})
pd,_,_=distances(ps,proximal_domain,all_adj)
proximal_width=float(np.linalg.norm(np.array(src['restRig']['bones']['Tail_01']['tail'])-np.array(src['restRig']['bones']['Tail_01']['head'])))
for i in proximal_domain:trace[i]=vector(['Pelvis','Tail_01'],[0,proximal_width],pd[i]);arcs[i]=pd[i]
arc_reports['Pelvis_Tail']={'domainNodes':sorted(proximal_domain),'sharedParentBoundaryNodes':sorted(ps),'controlSequence':['Pelvis','Tail_01'],
 'controlKnots_m':[0,proximal_width],'forbiddenHindDonors':True,'exactBodyTailSeamsCoupled':True}
# Authorized Mane-only positive interpolation. Unknown patch follows the actual
# attachment paths; Body roots/protected head vectors are Dirichlet boundaries.
unknown=set(attached)
for path in attachment_paths:unknown.update(path['path'])
unknown-=body|head
# Use connected actual Mane patches bounded by exact roots/protected head,
# not a nearest-root Dirichlet cut across free hair.
expansion=list(unknown)
while expansion:
 u=expansion.pop()
 for v,_ in mane_adj[u]:
  if v not in body and v not in head and v not in unknown:unknown.add(v);expansion.append(v)
boundary_mane={j for i in unknown for j,_ in mane_adj[i] if j not in unknown}
root_for={}
for i in boundary_mane:
 root=i if i in body else mo.get(i)
 if i in head:root=i
 if root is None:constructor_rejections.append({**describe(i),'reason':'no measured root for Mane boundary'});continue
 root_for[i]=root
# Caudal measured Body attachment roots belong to the adjacent dorsal
# Spine_Upper/Chest station (geometry), rather than the whole torso family.
for root in set(root_for.values())|{p['root'] for p in attachment_paths}:
 if root in body and region[root]=='torso' and P[root,0]>=bones['Chest']['head'][0]-.5*np.linalg.norm(np.array(bones['Chest']['tail'])-np.array(bones['Chest']['head'])) and P[root,2]>=bones['Chest']['head'][2]:
  allowed[root]=False;allowed[root,[ni['Spine_Upper'],ni['Chest']]]=True
  region[root]='dorsal_Chest_Spine_Upper_attachment'
  v=source[root]*allowed[root]
  if v.sum()>0:trace[root]=v/v.sum()
  else:constructor_rejections.append({**describe(root),'reason':'empty dorsal adjacent-parent root trace'})
for i,root in root_for.items():
 if i not in body and i not in head:allowed[i]=allowed[root];trace[i]=trace[root];region[i]='Mane_compatible_boundary_'+str(region[root])
# Component unions are geometric root permissions, NOT the nonzero solution.
components=[];unvisited=set(unknown)
while unvisited:
 first=min(unvisited);todo=[first];component={first};unvisited.remove(first)
 while todo:
  u=todo.pop()
  for v,_ in mane_adj[u]:
   if v in unvisited:unvisited.remove(v);component.add(v);todo.append(v)
 components.append(component)
mane_solve_reports=[]
for comp in components:
 bd={j for i in comp for j,_ in mane_adj[i] if j not in comp};roots_used={root_for[j] for j in bd if j in root_for};union=np.any(allowed[list(roots_used)],axis=0) if roots_used else np.zeros(K,bool)
 foreign=[n for n in names if union[ni[n]] and n.startswith(('Front','Hind','Tail'))]
 if foreign or union.sum()>8 or not roots_used:
  constructor_rejections.append({'nodes':sorted(comp),'reason':'incompatible measured Mane root donor union','foreignDonors':foreign,'donors':[names[k] for k in np.where(union)[0]]});continue
 for i in comp:allowed[i]=union;region[i]='Mane_continuous_attachment'
 rows=sorted(comp);index={i:k for k,i in enumerate(rows)};A=np.zeros((len(rows),len(rows)));B=np.zeros((len(rows),K))
 for i in rows:
  z=index[i]
  for j,length in mane_adj[i]:
   weight=1/max(length,1e-9);A[z,z]+=weight
   if j in comp:A[z,index[j]]-=weight
   else:B[z]+=weight*trace[j]
 solution=np.linalg.solve(A,B);residual=float(np.max(np.abs(A@solution-B)));minimum=float(solution.min());sum_error=float(np.max(abs(solution.sum(axis=1)-1)))
 if minimum< -1e-10 or sum_error>1e-8:constructor_rejections.append({'nodes':rows,'reason':'positive Mane interpolation invariant failure','minimum':minimum,'sumError':sum_error});continue
 for i,v in zip(rows,solution):trace[i]=v
 mane_solve_reports.append({'nodes':rows,'boundaryNodes':sorted(bd),'measuredRootNodes':sorted(roots_used),'independentAllowedDonors':[names[k] for k in np.where(union)[0]],'residual':residual,'minimum':minimum,'maxSumError':sum_error,'mainBodyUnknownNodes':0,'matrixSize':len(rows),'pruningUsed':False})
# Permissions are sealed before any anchor registration or pose validation.
permission_sha=hashlib.sha256(allowed.tobytes()).hexdigest();allowed.setflags(write=False)
target=np.zeros((N,K),dtype=np.float32);fixed=np.zeros(N,bool);anchor_reasons={};rejections=list(constructor_rejections)
def set_anchor(ids,reason):
 for i in sorted(set(ids)):
  v=trace[i];bad={names[k]:float(v[k]) for k in np.where((v>1e-12)&~allowed[i])[0]};empty=not np.any(v>1e-12)
  proposed=v.astype(np.float32);conflict=fixed[i] and not np.array_equal(target[i],proposed)
  if bad or empty or conflict:
   rejections.append({**describe(i),'region':str(region[i]),'reason':reason,'illegalDonors':bad,'empty':empty,'conflict':conflict,'allowedDonors':[names[k] for k in np.where(allowed[i])[0]]});continue
  fixed[i]=True;target[i]=proposed;anchor_reasons.setdefault(str(i),[]).append(reason)
set_anchor(head,'protected original skull/head/ear vectors')
set_anchor({i for i in range(N) if str(region[i])=='tail_branch'}|proximal_domain,'anatomical tail and coupled Pelvis/proximal Tail transition')
set_anchor(unknown|boundary_mane|set(root_for.values()),'positive compatible multi-root Mane attachment')
for q,patch in proximal.items():set_anchor(patch['parent']|patch['distal'],'local proximal parent/distal interface '+q)
for q in limbs:
 set_anchor(frozen['rootRows'][q],'continuous own branch trace BOTH root contour rows '+q)
 for c in old_contours['branches'][q]['selectedContours']:
  rows={body_map[j] for j in c['distalBoundaryVertexIds']+c['trunkBoundaryVertexIds']};set_anchor(rows,'continuous own branch shaft rows '+c['bone'])
set_anchor(neck_seeds,'complete caudal shared-Chest boundary')
for c in sc['cuts']:
 set_anchor(c['selected']['distalBoundaryVirtualNodeIds']+c['selected']['caudalBoundaryVirtualNodeIds'],'reviewed cervical scaffold BOTH rows '+str(c['x_m']))
# Carry the existing rigid Hoof sole constraints; no target refit.
for q,ids in sole_ids.items():
 nodes={body_map[j] for j in ids}
 for i in nodes:trace[i]=0.;trace[i,ni[q+'_Hoof']]=1
 set_anchor(nodes,'unchanged persistent sole Hoof trace '+q)
assert hashlib.sha256(allowed.tobytes()).hexdigest()==permission_sha
length=np.linalg.norm(P[E[:,0]]-P[E[:,1]],axis=1);checked=np.where(fixed[E[:,0]]&fixed[E[:,1]]&(length>1e-10))[0]
G=target.astype(float)/np.maximum(target.sum(axis=1,keepdims=True,dtype=float),1e-12);ph=np.column_stack((P,np.ones(N)))
bones=src['restRig']['bones'];delta={}
for n in names:
 D=delta.get(bones[n]['parent'],np.eye(4)).copy();angle={'Neck_Base':12,'Neck':4}.get(n,0)
 if angle:
  theta=np.radians(angle);R=np.eye(4);R[0,0]=R[2,2]=np.cos(theta);R[0,2]=np.sin(theta);R[2,0]=-np.sin(theta);h=np.array(bones[n]['head']);A=np.eye(4);A[:3,3]=h;B=np.eye(4);B[:3,3]=-h;D=D@A@R@B
 delta[n]=D
poses={f'Walk{j+1}':D for j,D in enumerate(prior['walk'])};poses['MildNeck']=np.array([delta[n] for n in names]);poses['GrazeStress_DIAGNOSTIC']=prior['grazeStress'];checks=[]
for label,D in poses.items():
 M=np.einsum('vb,bca->vca',G,D[:,:3,:]);Q=np.einsum('vca,va->vc',M,ph);ratio=np.linalg.norm(Q[E[checked,0]]-Q[E[checked,1]],axis=1)/length[checked]
 ix=int(np.argmax(ratio));edge_id=int(checked[ix]);nodes=E[edge_id]
 checks.append({'pose':label,'maximumFixedStretch':float(ratio[ix]),'over1_6Count':int(sum(ratio>1.6)),
  'worst':{'virtualEdgeId':edge_id,'nodes':nodes.tolist(),'endpoints':[describe(int(i)) for i in nodes],
   'regions':[str(region[i]) for i in nodes],'rest':P[nodes].tolist(),'posed':Q[nodes].tolist(),
   'weights':[{names[k]:float(v) for k,v in enumerate(target[i]) if v>0} for i in nodes]}})
semantic=[]
for i in np.where(fixed)[0]:
 active=[q for q in limbs if any(target[i,ni[n]]>0 for n in chains[q])]
 wrong=[q for q in active if (P[i,1]-bones['Chest']['head'][1])*(1 if q.endswith('_L') else -1)<-.025]
 if len(active)>1 or wrong:semantic.append({**describe(i),'activeBranches':active,'wrongSideBranches':wrong})
skull_nodes={body_map[i] for i in sc['originalSkullBodyIds']};ear_nodes={(x['component'],x['vertexId']):i for i in head for x in source_refs[i]}
protected_mismatches=[]
for i in skull_nodes:
 if not np.array_equal(target[i].astype(float),source[i]):protected_mismatches.append(describe(i))
for x in sc['originalEarMembership']['records']:
 i=ear_nodes[(x['component'],x['vertexId'])]
 if not np.array_equal(target[i].astype(float),source[i]):protected_mismatches.append(describe(i))
for i in np.where(fixed)[0]:
 if np.any((target[i]>1e-12)&~allowed[i]):raise AssertionError('illegal fixed donor')
body_changes=[];hair_changes=[]
for i in np.where(fixed & np.any(target.astype(float)!=source,axis=1))[0]:
 record={**describe(i),'region':str(region[i]),'sourceWeights':{names[k]:float(v) for k,v in enumerate(source[i]) if v>0},'targetWeights':{names[k]:float(v) for k,v in enumerate(target[i]) if v>0}}
 (body_changes if i in body else hair_changes).append(record)
walk_checks=[p for p in checks if p['pose'].startswith('Walk')];mild=next(p for p in checks if p['pose']=='MildNeck');stress=checks[-1]
result={'taskId':'adult_motion_specialist','outcome':'PRE-SOLVE REVIEW ONLY','accepted':False,'harmonicSolveRun':False,'meshWeightsWritten':False,
 'frozenSource':str(BASELINE),'actualSourceSha256':hashlib.sha256(BASELINE.read_bytes()).hexdigest(),'frozenPreviousContractSha256':'75b3c38385cd3d82a88941e9918179e0ad8fd94e680bdc4ac9a90f418bde2938',
 'protectedReferenceSerializationSha256':hashlib.sha256((ROOT/'virtual-shell-neck-light/light_production_identity_serialization.json').read_bytes()).hexdigest(),
 'walkMatrixSha256':hashlib.sha256(prior['walk'].tobytes()).hexdigest(),'grazeStressMatrixSha256':hashlib.sha256(prior['grazeStress'].tobytes()).hexdigest(),
 'permissionsImmutableSha256':permission_sha,'permissionsUnchangedByAnchors':True,'fallbackDonorsUsed':False,'maximumAllowedInfluences':int(allowed.sum(axis=1).max()),
 'regionsByNode':{str(i):str(region[i]) for i in range(N)},'regionCounts':dict(collections.Counter(region.tolist())),
 'allowedDonorsByNode':{str(i):[names[k] for k in np.where(allowed[i])[0]] for i in range(N)},
 'fixedAnchors':{str(i):{**describe(int(i)),'region':str(region[i]),'weights':{names[k]:float(v) for k,v in enumerate(target[i]) if v>0},'reasons':anchor_reasons[str(i)]} for i in np.where(fixed)[0]},
 'attachmentPaths':attachment_paths,'maneAttachmentSolves':mane_solve_reports,'mainBodyFieldSolveRun':False,'proximalTailBodyNodes':sorted(body_tail),'exactBodyTailRootNodes':sorted(tail_roots),
 'intrinsicArcContracts':arc_reports,'rejections':rejections,'semanticAnchorViolations':semantic,'protectedTargetMismatches':protected_mismatches,
 'fixedEdgesChecked':len(checked),'fixedAnchorCount':int(fixed.sum()),'poseChecks':checks,'intentionalBodyTraceChanges':body_changes,'intentionalHairTailTraceChanges':hair_changes,
 'gates':{'independentImmutablePermissions':True,'zeroEmptyIllegalConflictingRejections':not rejections,'zeroWrongSideOrMultiBranchAnchors':not semantic,
  'originalSkullEarTargetsExact':not protected_mismatches,'atMost8AllowedInfluences':bool(max(allowed.sum(axis=1))<=8),
  'all61WalkFixedEdgesAtMost1_6':all(p['maximumFixedStretch']<=1.6 for p in walk_checks),'mildFixedEdgesAtMost1_6':mild['maximumFixedStretch']<=1.6,
  'grazeStressFixedEdgesAtMost1_6_DIAGNOSTIC':stress['maximumFixedStretch']<=1.6},
 'scopeLimits':'One neutral Light constructor with authorized local Mane interpolation only. No harmonic solve/free skin weights, contact/reach acceptance, morph/Stock expansion, pose refit, production save or live Blender. Graph/control knots and complete collar remain an anatomical proposal requiring parent/native Sol review.'}
np.savez(OUT/'light_attachment_contract_arrays.npz',points=P,edges=E,triangles=T,originalToVirtual=mapping,names=names,allowed=allowed,regions=region,target=target,fixed=fixed,intrinsicArc_m=arcs,bodyEdges=body_edges)
(OUT/'light_attachment_contract.json').write_text(json.dumps(result,indent=2,default=json_numpy))
print(json.dumps({'taskId':result['taskId'],'gates':result['gates'],'rejectionCount':len(rejections),'fixedEdges':len(checked),'walkMax':max(p['maximumFixedStretch'] for p in walk_checks),'mildMax':mild['maximumFixedStretch'],'stressMax':stress['maximumFixedStretch'],'attachmentPaths':len(attachment_paths),'arcKnots':{q:v['controlKnots_m'] for q,v in arc_reports.items()}},indent=2))
