"""Compact exact-shell evidence and parent-relayed scaffold review; no solve."""
import json, hashlib
from collections import Counter
from pathlib import Path
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support')
OUT=ROOT/'virtual-shell-neck-light'
r=json.loads((OUT/'light_virtual_shell_neck_contours.json').read_text())
i=json.loads((OUT/'light_current_production_correspondence.json').read_text())
ear=r['originalEarMembership']['records']
cuts=[]
for c in r['cuts']:
    s=c['selected'];x=c['x_m']
    seams=[p for p in r['exactBoundaryPairs'] if (p['edges'][0]['positions'][0][0]-x)*(p['edges'][0]['positions'][1][0]-x)<0]
    contained=sum(e['vertexId'] in s['cranialSourceVertexIds'][e['component']] for e in ear)
    cuts.append({'x_m':x,'planeOrigin':s['planeOrigin'],'planeNormal':s['planeNormal'],
        'virtualNodeCount':s['virtualNodeCount'],'sourceVertexCounts':{n:len(v) for n,v in s['cranialSourceVertexIds'].items()},
        'bounds':s['bounds'],'closed':s['closed'],'containsPrevious':s['containsPreviousCranialComponent'],
        'containsWholeOriginalSkull':s['wholeOriginalSkullContained'],'containedOriginalEarRecords':contained,
        'excludedLimbContainedIds':s['excludedLimbContainedIds'],'caudalTorsoContainedIds':s['caudalTorsoContainedIds'],
        'provisionalWitnessContainedIds':s['provisionalWitnessContainedIds'],
        'exactSeamPairCrossings':len(seams),'seamCrossingSourceEdges':[p['edges'] for p in seams]})
summary={'taskId':'adult_motion_specialist','outcome':'Exact geometric cranial scaffold accepted by parent-relayed native Sol review; no weight solve authorized or run.',
    'candidateOnly':True,'fieldAccepted':False,'solveRun':False,'weightsChangedByPreflight':False,
    'source':r['source'],'sourceSha256':r['sourceSha256'],'currentProductionBinding':{
        'report':str(OUT/'light_current_production_correspondence.json'),
        'fullProtectedSourceCorrespondence':i['fullProtectedSourceCorrespondence'],
        'canonicalRestRigExact':i['canonicalRestRigExact'],
        'originalSkullMembershipExactlyEqual':i['originalSkullMembership']['exactlyEqual'],
        'originalEarControlsExactlyEqual':i['originalEarControlsExactlyEqual'],
        'existingWeightDifferences':{n:{k:d[k] for k in ('changedVertexCount','changedControls','groupNamesEqual')} for n,d in i['weightsComparison'].items()},
        'motionDifference':'Additional Walk_Diagnostic; production Idle curves are identical.',
        'productionSha256':i['sourceSha256']['production']},
    'shell':{'parts':r['parts'],'exactBoundaryEdgePairs':len(r['exactBoundaryPairs']),
        'pairTypes':dict(Counter('/'.join(sorted(p['edges'][j]['component'] for j in (0,1))) for p in r['exactBoundaryPairs'])),
        'pairProblems':len(r['boundaryPairProblems']),'virtualVertices':r['virtualNodeCount'],
        'existingFaceTriangles':r['derivedTriangleCount'],'virtualEdges':r['virtualEdgeCount'],
        'badIncidence':len(r['badVirtualEdgeIncidence']),'badWinding':len(r['badVirtualEdgeWinding']),
        'exactAcrossBasisAndAllFourMorphs':all(all(p['morphExactMatches'].values()) for p in r['exactBoundaryPairs']),
        'newFacesOrMeshWeld':False,'noCoordinateToleranceOrAveraging':True},
    'cuts':cuts,'originalSkullBodyCount':len(r['originalSkullBodyIds']),'originalSkullBounds':r['originalSkullBounds'],
    'exclusionCounts':{'limbs':{q:len(v) for q,v in r['excludedLimbBodyIds'].items()},
                       'caudalBodyXLE0':len(r['caudalTorsoGeometryExclusion']['ids'])},
    'ears':{'uniqueOriginalRecords':len(ear),'perComponent':dict(Counter(e['component'] for e in ear)),
            'perControlOverlappingCounts':r['originalEarMembership']['controls'],
            'recordsAndExactWeights':str(OUT/'light_virtual_shell_neck_contours.json'),
            'controlsRetained':True,'newEarMotionImplemented':False},
    'parentRelayedReview':{'cranialScaffoldAccepted':True,
        'correction':'The lowest x=.55 contour ventral extension is a Chest/cervical junction; do not assign the complete cranial domain cervical-only.',
        'earConstraint':'Preserve original legitimate ear controls and coherence under common head/neck deformation.',
        'frontDistalBoundaryOnly':True,'futureFieldTrialAuthorized':False},
    'views':{v:str(OUT/f'light_virtual_neck_{v}_overlay.png') for v in ('front','back','left','right','top','q3','left_context','q3_context')},
    'fullPlanesContainmentIdsPositions':str(OUT/'light_virtual_shell_neck_contours.json'),
    'exactSourceNodeFaceMap':str(OUT/'light_virtual_node_source_refs.json'),
    'protectedDataUnchanged':r['protectedGeometryMorphUVColorMasksRestGraphUnchanged'] and i['allSourceFilesUnchanged']}
summary['artifactHashes']={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.glob('*') if p.suffix in ('.json','.npz','.png') and p.name!='VIRTUAL_NECK_PREFLIGHT_SUMMARY.json'}
(OUT/'VIRTUAL_NECK_PREFLIGHT_SUMMARY.json').write_text(json.dumps(summary,indent=2))
print(json.dumps({k:summary[k] for k in ('taskId','outcome','currentProductionBinding','shell')},indent=2))
