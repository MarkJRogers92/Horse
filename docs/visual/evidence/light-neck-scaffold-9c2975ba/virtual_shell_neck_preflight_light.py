"""Exact boundary-pair virtual adjacency, then four authorized neck cuts.
Only analysis arrays are built. No mesh weld, new source face, weights or solve.
"""
import bpy,json,sys,hashlib
import numpy as np
from pathlib import Path
from collections import defaultdict
sys.path.insert(0,'/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse/tools/horse');import production_motion as pm
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support');OUT=ROOT/'virtual-shell-neck-light';OUT.mkdir(exist_ok=True)
SOURCE=ROOT.parent/'light/horse_light_motion_candidate.blend';EXPECTED='b2cff126e8af50088290a7c58d92f56e99c750181302c33a07254299d294ff8e';assert hashlib.sha256(SOURCE.read_bytes()).hexdigest()==EXPECTED
bpy.ops.wm.open_mainfile(filepath=str(SOURCE));body,rig=[bpy.data.objects[n] for n in ('Body','Skeleton')];parts=[bpy.data.objects[n] for n in ('Body','Mane','Tail')]
s=(ROOT/'stage_audit.py').read_text();exec(s[s.index('def protected'):s.index('bpy.ops.wm.open_mainfile')]);before=protected(body,rig)
matrices={o.name:[list(row) for row in o.matrix_world] for o in parts};same_space=all(matrices[o.name]==matrices['Body'] for o in parts)
assert same_space,'Part matrices differ; exact local coordinate pairing is not valid'
points=[];refs=[];triangles=[];face_refs=[];offset={};boundary=[];component_info={};ear_records=[]
for obj in parts:
 name=obj.name;off=len(points);offset[name]=off;P=np.array([v.co[:] for v in obj.data.vertices]);points.extend(P.tolist());refs.extend({'component':name,'vertexId':v.index} for v in obj.data.vertices)
 local_edges={tuple(sorted(e.vertices[:])):e.index for e in obj.data.edges};incidence=defaultdict(list)
 for poly in obj.data.polygons:
  vv=list(poly.vertices)
  for a,b in zip(vv,vv[1:]+vv[:1]):incidence[tuple(sorted((a,b)))].append({'faceId':poly.index,'orientedVertexIds':[a,b]})
 for pair,rows in incidence.items():
  if len(rows)==1:
   a,b=rows[0]['orientedVertexIds'];boundary.append({'component':name,'sourceEdgeId':local_edges[pair],'sourceFaceId':rows[0]['faceId'],'orientedVertexIds':[a,b],'globalVertexIds':[off+a,off+b],'positions':[P[a].tolist(),P[b].tolist()]})
 obj.data.calc_loop_triangles()
 for tr in obj.data.loop_triangles:triangles.append([off+v for v in tr.vertices]);face_refs.append({'component':name,'polygonId':tr.polygon_index,'loopTriangleId':tr.index})
 component_info[name]={'vertices':len(P),'sourcePolygons':len(obj.data.polygons),'derivedTriangles':len(obj.data.loop_triangles),'boundaryEdges':sum(e['component']==name for e in boundary),'nonmanifoldOriginalEdges':[list(pair) for pair,rows in incidence.items() if len(rows)>2]}
 groups={g.index:g.name for g in obj.vertex_groups}
 for v in obj.data.vertices:
  w={groups[g.group]:g.weight for g in v.groups if g.weight>0};ew={n:x for n,x in w.items() if n.startswith('Ear')}
  if ew:ear_records.append({'component':name,'vertexId':v.index,'position':P[v.index].tolist(),'earMass':sum(ew.values()),'earWeights':ew,'allWeights':w})
P=np.array(points);T=np.array(triangles,int);parent=list(range(len(P)))
def find(i):
 while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
 return i
def union(a,b):
 a,b=find(a),find(b)
 if a!=b:parent[max(a,b)]=min(a,b)
def edge_key(row):return tuple(sorted(tuple(p) for p in row['positions']))
by_key=defaultdict(list)
for e in boundary:by_key[edge_key(e)].append(e)
pairs=[];problems=[]
for key,rows in by_key.items():
 if len(rows)!=2 or rows[0]['component']==rows[1]['component']:
  problems.append({'reason':'boundary edge does not have exactly one distinct-component exact counterpart','edges':rows});continue
 a,b=rows;opposite=a['positions'][0]==b['positions'][1] and a['positions'][1]==b['positions'][0]
 if not opposite:problems.append({'reason':'exact counterpart has incompatible winding','edges':rows});continue
 # Check basis and every morph coordinate directly, with no epsilon/rounding.
 key_names=[k.name for k in bpy.data.objects[a['component']].data.shape_keys.key_blocks]
 morph_matches={}
 for n in key_names:
  ka=bpy.data.objects[a['component']].data.shape_keys.key_blocks[n];kb=bpy.data.objects[b['component']].data.shape_keys.key_blocks[n]
  morph_matches[n]=all(ka.data[x].co[:]==kb.data[y].co[:] for x,y in zip(a['orientedVertexIds'],reversed(b['orientedVertexIds'])))
 if not all(morph_matches.values()):problems.append({'reason':'exact seam morph coordinates differ','edges':rows,'morphExactMatches':morph_matches});continue
 union(a['globalVertexIds'][0],b['globalVertexIds'][1]);union(a['globalVertexIds'][1],b['globalVertexIds'][0]);pairs.append({'edges':[a,b],'oppositeWinding':True,'uniqueCounterpart':True,'morphExactMatches':morph_matches})
roots=[find(i) for i in range(len(P))];unique=sorted(set(roots));root_index={r:i for i,r in enumerate(unique)};mapping=np.array([root_index[r] for r in roots]);VP=P[unique];VT=mapping[T];node_refs=defaultdict(list)
for i,vi in enumerate(mapping):node_refs[int(vi)].append(refs[i])
edge_rows=defaultdict(list)
for ti,tr in enumerate(VT):
 for a,b in zip(tr,np.roll(tr,-1)):edge_rows[tuple(sorted((int(a),int(b))))].append({'triangleId':ti,'oriented':[int(a),int(b)]})
VE=np.array(list(edge_rows),int);edge_index={e:i for i,e in enumerate(edge_rows)};TE=np.array([[edge_index[tuple(sorted((int(tr[a]),int(tr[b]))))] for a,b in ((0,1),(1,2),(2,0))] for tr in VT])
bad_incidence=[{'virtualEdgeId':i,'nodes':list(e),'incidence':len(rows)} for i,(e,rows) in enumerate(edge_rows.items()) if len(rows)!=2]
bad_winding=[i for i,rows in enumerate(edge_rows.values()) if len(rows)==2 and rows[0]['oriented']!=list(reversed(rows[1]['oriented']))]
adj=[[] for _ in VP]
for ei,(a,b) in enumerate(VE):adj[a].append((int(b),ei));adj[b].append((int(a),ei))
def comp(seed,cut):
 seen={int(seed)};todo=[int(seed)]
 while todo:
  a=todo.pop()
  for b,ei in adj[a]:
   if ei not in cut and b not in seen:seen.add(b);todo.append(b)
 return seen
def loops_at(x):
 signed=VP[:,0]-x;cross=signed[VE[:,0]]*signed[VE[:,1]]<0;nodes=defaultdict(list)
 for te in TE[np.sum(cross[TE],axis=1)==2]:a,b=te[cross[te]];nodes[int(a)].append(int(b));nodes[int(b)].append(int(a))
 visited=set();loops=[]
 for start in sorted(nodes):
  if start in visited:continue
  group=set();todo=[start]
  while todo:
   a=todo.pop()
   if a in group:continue
   group.add(a);todo.extend(nodes[a])
  visited|=group;closed=len(group)>=3 and all(len(nodes[a])==2 for a in group);order=[]
  if closed:
   prev=None;a=min(group)
   while a not in order:order.append(a);nexts=[b for b in nodes[a] if b!=prev];prev,a=a,nexts[0]
   closed=a==order[0] and len(order)==len(group)
  else:order=sorted(group)
  pts=[]
  for ei in order:a,b=VE[ei];t=signed[a]/(signed[a]-signed[b]);pts.append((VP[a]+t*(VP[b]-VP[a])).tolist())
  loops.append({'closed':closed,'virtualEdgeIds':order,'intersectionPoints':pts,'nonTwoDegrees':{str(a):len(nodes[a]) for a in group if len(nodes[a])!=2}})
 return loops
original={v.index:{body.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0} for v in body.data.vertices}
skull=[i for i,w in original.items() if sum(x for n,x in w.items() if n.startswith(('Head','Jaw','Ear')))>.999];skull_nodes=set(mapping[skull]);front=json.loads((ROOT/'front-surface-contours-light'/'light_front_surface_contours.json').read_text());old=json.loads((ROOT/'contour-preflight-light'/'light_contour_components.json').read_text())
limbs={q:b['lastValidContour']['componentVertexIds'] for q,b in front['branches'].items()}
for q in ('Hind_L','Hind_R'):limbs[q]=next(c for c in old['branches'][q]['selectedContours'] if c['bone']==q+'_Upper')['componentVertexIds']
limb_nodes={q:set(mapping[ids]) for q,ids in limbs.items()};forbidden=set.union(*limb_nodes.values());caudal=[v.index for v in body.data.vertices if v.co.x<=0];caudal_nodes=set(mapping[caudal]);witness=sum([vv for vv in old['provisionalTrunkWitnessIds'].values()],[])
valid_shell=not problems and not bad_incidence and not bad_winding;cuts=[];previous=set();component_maps={}
if valid_shell:
 for x in (.70,.65,.60,.55):
  choices=[];rejects=[]
  for loop in loops_at(x):
   if not loop['closed']:rejects.append({'reason':'open or branched virtual contour','loop':loop});continue
   ids=comp(mapping[skull[0]],set(loop['virtualEdgeIds']));edge_nodes=set(VE[loop['virtualEdgeIds']].reshape(-1));inside=edge_nodes&ids;outside=edge_nodes-ids
   reasons=[]
   if not skull_nodes<=ids:reasons.append('missing whole original skull set')
   if forbidden&ids:reasons.append('contains excluded limb domain')
   if caudal_nodes&ids:reasons.append('contains explicit caudal Body x<=0 set')
   if not previous<=ids:reasons.append('does not contain previous cranial component')
   if not inside or not outside:reasons.append('remote contour does not cut skull-connected component')
   body_ids=[i for i in range(len(body.data.vertices)) if mapping[i] in ids]
   by_component={o.name:[i for i in range(len(o.data.vertices)) if mapping[offset[o.name]+i] in ids] for o in parts}
   loop.update({'planeOrigin':[x,0.,0.],'planeNormal':[1.,0.,0.],'cranialVirtualNodeIds':sorted(ids),'cranialSourceVertexIds':by_component,'virtualNodeCount':len(ids),'bounds':[VP[sorted(ids)].min(axis=0).tolist(),VP[sorted(ids)].max(axis=0).tolist()],
                'wholeOriginalSkullContained':skull_nodes<=ids,'excludedLimbContainedIds':{q:sorted(set(limbs[q])&set(body_ids)) for q in limbs},'caudalTorsoContainedIds':sorted(set(caudal)&set(body_ids)),
                'provisionalWitnessContainedIds':sorted(set(witness)&set(body_ids)),'containsPreviousCranialComponent':previous<=ids,'distalBoundaryVirtualNodeIds':sorted(inside),'caudalBoundaryVirtualNodeIds':sorted(outside),'rejectionCauses':reasons})
   (rejects if reasons else choices).append(loop)
  selected=min(choices,key=lambda c:c['virtualNodeCount']) if choices else None
  if selected:previous=set(selected['cranialVirtualNodeIds'])
  cuts.append({'x_m':x,'selected':selected,'rejectedContours':rejects})
after=protected(body,rig);assert before==after
ear_summary={n:{'vertexCount':sum(n in e['earWeights'] for e in ear_records),'maximumMass':max([e['earWeights'].get(n,0) for e in ear_records] or [0])} for n in sorted({n for e in ear_records for n in e['earWeights']})}
report={'taskId':'adult_motion_specialist','family':'light','source':str(SOURCE),'sourceSha256':EXPECTED,'candidateOnly':True,'accepted':False,'solveRun':False,'meshWeldOrNewSourceFaces':False,'method':'exact numeric endpoint correspondence only at unique paired original component boundary edges, opposite winding and exact every-morph coordinates; analysis vertex equivalence and existing-face triangulation; no tolerance, caps or new source geometry',
 'componentMatrices':matrices,'parts':component_info,'exactBoundaryPairs':pairs,'boundaryPairProblems':problems,'virtualNodeCount':len(VP),'derivedTriangleCount':len(VT),'virtualEdgeCount':len(VE),'badVirtualEdgeIncidence':bad_incidence,'badVirtualEdgeWinding':bad_winding,'exactShellVerified':valid_shell,
 'originalSkullBodyIds':skull,'originalSkullBounds':[np.array([body.data.vertices[i].co[:] for i in skull]).min(axis=0).tolist(),np.array([body.data.vertices[i].co[:] for i in skull]).max(axis=0).tolist()],
 'excludedLimbBodyIds':limbs,'caudalTorsoGeometryExclusion':{'definition':'every original Body vertex with model x<=0; geometric caudal exclusion, not nearest-axis anatomy assignment','ids':caudal,'bounds':[P[caudal].min(axis=0).tolist(),P[caudal].max(axis=0).tolist()]},
 'referencePositions':{'limbs':{q:[{'vertexId':i,'position':P[i].tolist()} for i in ids] for q,ids in limbs.items()},'caudal':[{'vertexId':i,'position':P[i].tolist()} for i in caudal],'skull':[{'vertexId':i,'position':P[i].tolist()} for i in skull]},
 'cuts':cuts,'originalEarMembership':{'controls':ear_summary,'records':ear_records,'weightsChanged':False,'preservationRule':'retain legitimate original ear-control capability; common head/neck motion must be coherent, without flattening all ear vertices to Head_Base'},
 'protectedGeometryMorphUVColorMasksRestGraphUnchanged':before==after,'protectedSourceHashFields':before,'sourceHashStillMatches':hashlib.sha256(SOURCE.read_bytes()).hexdigest()==EXPECTED,
 'gates':{'exactUniqueSeamCounterparts':not problems,'closedOppositeManifoldVirtualShell':valid_shell,'fourValidNestedCranialCuts':len(cuts)==4 and all(c['selected'] for c in cuts),'anatomicalOwnershipReviewed':False},
 'conclusion':'Neck contour ownership awaits review. No weighting solve or new ear motion implementation.'}
def numpy_json(value):return value.item() if isinstance(value,np.generic) else value.tolist()
(OUT/'light_virtual_shell_neck_contours.json').write_text(json.dumps(report,indent=2,default=numpy_json));np.savez(OUT/'light_virtual_shell_arrays.npz',points=VP,triangles=VT,edges=VE,originalToVirtual=mapping,originalPoints=P)
(OUT/'light_virtual_node_source_refs.json').write_text(json.dumps({'taskId':'adult_motion_specialist','nodes':{str(i):{'position':VP[i].tolist(),'sourceRefs':node_refs[i]} for i in range(len(VP))},'derivedTrianglesSourceFaceRefs':face_refs},indent=2,default=numpy_json))
print('VIRTUAL_SHELL_NECK_PREFLIGHT_COMPLETE','pairs',len(pairs),'problems',len(problems),'incidence',len(bad_incidence),'winding',len(bad_winding),'cuts',[(c['x_m'],c['selected']['virtualNodeCount'] if c['selected'] else None,c['selected']['bounds'] if c['selected'] else None) for c in cuts],'ears',ear_summary,flush=True)
