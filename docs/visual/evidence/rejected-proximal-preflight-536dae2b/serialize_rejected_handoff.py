"""Read the frozen rejected preflight and serialize its localized handoff.

This does not run the constructor, solve any field, or write any mesh.
"""
import collections
import hashlib
import json
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
REPO = Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse')
CORE_HASHES = {
    'constructor_proximal_preflight.py': '1c884fdca62db2e4e48be4b9fc6105d41d8443763b12c8d08de0daad24d42e71',
    'light_attachment_contract.json': '536dae2b36fbf6cdc4ed0ea9740631717a27f6fceea75d0e1f2588129fc30687',
    'light_attachment_contract_arrays.npz': 'ef0c85fe2f85079d92694fc47ffcab1e301397d3f2902188ed6dce3b0d71abfa',
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text())


for filename, expected in CORE_HASHES.items():
    assert sha(HERE / filename) == expected, filename
c = read(HERE / 'light_attachment_contract.json')
a = np.load(HERE / 'light_attachment_contract_arrays.npz')
old = np.load(ROOT / 'junction-preflight-light/light_immutable_junction_arrays.npz')
refs = read(ROOT / 'virtual-shell-neck-light/light_virtual_node_source_refs.json')['nodes']
src = read(ROOT / 'virtual-shell-neck-light/light_production_identity_serialization.json')
names = a['names'].tolist()
P = a['points']
body_map = {}
for node, ref in refs.items():
    for x in ref['sourceRefs']:
        if x['component'] == 'Body':
            body_map[x['vertexId']] = int(node)


def node_record(node):
    node = int(node)
    return {
        'node': node,
        'sourceRefs': refs[str(node)]['sourceRefs'],
        'position': P[node].tolist(),
        'region': c['regionsByNode'][str(node)],
        'allowedDonors': c['allowedDonorsByNode'][str(node)],
        'fixed': bool(a['fixed'][node]),
        'targetWeights': {n: float(w) for n, w in zip(names, a['target'][node]) if w > 0},
        'anchorReasons': c['fixedAnchors'].get(str(node), {}).get('reasons', []),
    }


patches = {}
for branch in ('Front_L', 'Front_R', 'Hind_L', 'Hind_R'):
    v = c['intrinsicArcContracts'][branch]
    parent, distal = set(v['sharedParentBoundaryNodes']), set(v['distalBoundaryNodes'])
    crossing = [tuple(map(int, edge)) for edge in a['bodyEdges']
                if (int(edge[0]) in parent and int(edge[1]) in distal)
                or (int(edge[1]) in parent and int(edge[0]) in distal)]
    h = np.array(v['jointHead'])
    axis = np.array(v['jointTail']) - h
    axis /= np.linalg.norm(axis)
    patches[branch] = {
        'patchCount': len(v['proximalInterfaceNodes']),
        'jointHead': v['jointHead'], 'jointTail': v['jointTail'],
        'jointLength_m': v['jointLength_m'], 'patchBounds': v['patchBounds'],
        'surfaceSeed': v['nearestConnectedSurfaceSeed'],
        'parentBoundaryCount': len(parent), 'distalBoundaryCount': len(distal),
        'parentDistalSetIntersection': sorted(parent & distal),
        'directParentDistalBodyEdgeCount': len(crossing),
        'directParentDistalBodyEdges': [
            {'nodes': list(edge), 'restLength_m': float(np.linalg.norm(P[edge[0]] - P[edge[1]])),
             'endpoints': [dict(node_record(i), boundaryRole='parent' if i in parent else 'distal',
                                jointAxisStation_m=float((P[i] - h) @ axis),
                                outsideNeighborRoles=v['bothBoundaryNeighborRoles'].get(str(i), []))
                           for i in edge]} for edge in crossing],
        'completeShoulderHipOwnershipClaimed': False,
        'reasonRejected': 'The disjoint perimeter sets directly touch across real Body edges. A proximal joint tube does not establish separated anatomical parent/distal interfaces.',
    }

walk = max((p for p in c['poseChecks'] if p['pose'].startswith('Walk')),
           key=lambda p: p['maximumFixedStretch'])
mild = next(p for p in c['poseChecks'] if p['pose'] == 'MildNeck')
stress = next(p for p in c['poseChecks'] if p['pose'] == 'GrazeStress_DIAGNOSTIC')
extrema = {}
for label, check in [('Walk', walk), ('MildNeck', mild), ('GrazeStress_DIAGNOSTIC', stress)]:
    w = check['worst']
    extrema[label] = dict(check,
        restEdgeLength_m=float(np.linalg.norm(np.diff(np.array(w['rest']), axis=0))),
        posedEdgeLength_m=float(np.linalg.norm(np.diff(np.array(w['posed']), axis=0))),
        detailedEndpoints=[node_record(i) for i in w['nodes']])

shaft_witnesses = [node_record(body_map[i]) for i in (3859, 4135, 885, 922)]
shaft_parent_mass = {
    str(x['sourceRefs']): sum(w for n, w in x['targetWeights'].items()
                             if n in ('Chest', 'Pelvis') or n.endswith('_Upper'))
    for x in shaft_witnesses}
mane_reject = next(x for x in c['rejections'] if x['reason'] == 'incompatible measured Mane root donor union')
mane_unknown = set(mane_reject['nodes'])
mane_map = {x['vertexId']: int(node) for node, ref in refs.items()
            for x in ref['sourceRefs'] if x['component'] == 'Mane'}
mane_boundary = set()
for u, v in src['meshes']['Mane']['edges']:
    u, v = mane_map[u], mane_map[v]
    if (u in mane_unknown) != (v in mane_unknown):
        mane_boundary.add(v if u in mane_unknown else u)
root_donor_witnesses = {}
for donor in mane_reject['donors']:
    root_donor_witnesses[donor] = [node_record(i) for i in sorted(mane_boundary)
                                  if donor in c['allowedDonorsByNode'][str(i)]][:3]
illegal = [x for x in c['rejections'] if x.get('illegalDonors')]
empty = [x for x in c['rejections'] if x.get('empty')]
conflicting = [x for x in c['rejections'] if x.get('conflict')]

files = {str(HERE / filename): value for filename, value in CORE_HASHES.items()}
for filename in ('render_proximal_interfaces.py', 'annotate_proximal_interfaces.py',
                 'serialize_rejected_handoff.py'):
    files[str(HERE / filename)] = sha(HERE / filename)
views = []
for name in ('left_context', 'q3_context', 'shoulder_q3', 'hip_back'):
    p = HERE / f'light_proximal_{name}_overlay.png'
    views.append({'path': str(p), 'sha256': sha(p),
                  'purpose': 'Labeled frozen rest geometry and proposed ownership, not a solved/deformed mesh.'})
geometry_checks = {key: bool(np.array_equal(a[key], old[key]))
                   for key in ('points', 'edges', 'triangles', 'originalToVirtual')}
array_hashes = {key: {'shape': list(a[key].shape), 'dtype': str(a[key].dtype),
                     'sha256': hashlib.sha256(a[key].tobytes()).hexdigest()} for key in a.files}
result = {
    'taskId': 'adult_motion_specialist', 'outcome': 'REJECTED CONSTRUCTOR; FROZEN FOR NATIVE SOL PEER REVIEW',
    'coreFilesSha256': files, 'arrayContentHashes': array_hashes,
    'sourceBlendSha256': sha(REPO / 'art/derived/production/horse_light.blend'),
    'sourceGlbSha256': sha(REPO / 'game/assets/horse/production/horse_light.glb'),
    'sharedHelperSha256': sha(REPO / 'tools/horse/production_motion.py'),
    'walkMatrixSha256': c['walkMatrixSha256'], 'grazeStressMatrixSha256': c['grazeStressMatrixSha256'],
    'unchangedScaffoldArrays': geometry_checks, 'protectedSkullEarTargetMismatches': c['protectedTargetMismatches'],
    'mainBodyFieldSolveRun': False, 'maneAttachmentSolvesRun': len(c['maneAttachmentSolves']),
    'meshWeightsWritten': False, 'sourceProductionFilesWritten': False,
    'fixedAnchorCount': c['fixedAnchorCount'], 'fixedPositiveEdgesChecked': c['fixedEdgesChecked'],
    'poseCount': len(c['poseChecks']), 'gates': c['gates'], 'fixedBoundaryExtrema': extrema,
    'proximalPatchEvidence': patches,
    'distalShaftWitnesses': shaft_witnesses, 'distalShaftChestPelvisUpperMass': shaft_parent_mass,
    'neckContract': c['intrinsicArcContracts']['Neck'],
    'attachment': {
        'measuredRejectedAttachmentPathsCount': len(c['attachmentPaths']),
        'positiveInterpolationRun': False, 'rejectedUnknownManeComponentCount': len(mane_unknown),
        'independentRootPermissionUnion': mane_reject['donors'], 'unionCount': len(mane_reject['donors']),
        'foreignLimbTailDonorsInUnion': mane_reject['foreignDonors'],
        'measuredBoundaryCount': len(mane_boundary), 'donorBoundaryWitnesses': root_donor_witnesses,
        'cause': 'The present component-wide boundary union is 12 controls. Local compatible multi-root domains are not yet established; this rejection does not prove that every possible continuous eight-control attachment is impossible.',
        'nearestRootPropagationIsUnresolvedDiagnostic': True,
    },
    'rejectionCount': len(c['rejections']),
    'rejectionCountsByReason': dict(collections.Counter(x['reason'] for x in c['rejections'])),
    'illegalAnchorIntersections': illegal, 'emptyAnchorIntersectionCount': len(empty),
    'conflictingAnchorIntersectionCount': len(conflicting),
    'wrongSideProvisionalHindScopeWitnesses': c['semanticAnchorViolations'],
    'tailTransition': c['intrinsicArcContracts']['Pelvis_Tail'], 'labeledViews': views,
    'visualInspection': {'shoulder_q3': 'Viewed: patch perimeter roles approach one another at corners; cyan collar remains a Chest/cervical proposal.',
                         'hip_back': 'Viewed: joint tube patches are proximal proposals; no complete hip ownership is accepted.'},
    'parentReportedRendererEvidence': 'Isolated Godot GPU eight-influence probe accepted by parent; anatomical rejection and the 12-control Mane rejection remain unchanged.',
    'conclusion': 'Do not solve the Body field. Canonical distal shaft stationing is corrected, but proximal parent/distal interfaces still touch, caudal cervical root permissions reject overrun, six provisional hind anchors cross sides, and the Mane component donor union blocks interpolation. Parent/native Sol must choose one bounded corrective package from this frozen evidence.',
}
assert all(geometry_checks.values())
assert not c['mainBodyFieldSolveRun'] and not c['meshWeightsWritten'] and not c['maneAttachmentSolves']
output = HERE / 'light_rejected_proximal_handoff.json'
output.write_text(json.dumps(result, indent=2))
print(json.dumps({'taskId': result['taskId'], 'path': str(output), 'sha256': sha(output),
                  'coreHashesVerified': True, 'fixedEdges': c['fixedEdgesChecked'],
                  'walk': walk['maximumFixedStretch'], 'mild': mild['maximumFixedStretch'],
                  'stressDiagnostic': stress['maximumFixedStretch'],
                  'rejections': len(c['rejections']), 'wrongSide': len(c['semanticAnchorViolations']),
                  'illegal': len(illegal), 'empty': len(empty), 'conflicting': len(conflicting)}, indent=2))
