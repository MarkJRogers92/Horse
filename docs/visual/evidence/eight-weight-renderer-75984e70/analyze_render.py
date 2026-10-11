import ast, base64, hashlib, json, re
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parent
names=['actual_rest','actual_pose','reference_expected','reference_first4']
images={n:Image.open(root/f'{n}.png').convert('RGB') for n in names}
def green_pixels(im):
    px=im.load(); pts=set()
    for y in range(im.height):
        for x in range(im.width):
            r,g,b=px[x,y]
            if g>150 and g>r*2 and g>b*1.5: pts.add((x,y))
    return pts
def metrics(pts):
    if not pts: raise RuntimeError('no green pixels')
    return {'pixelCount':len(pts),'centroidPx':[sum(x for x,y in pts)/len(pts),sum(y for x,y in pts)/len(pts)],'bboxPx':[min(x for x,y in pts),min(y for x,y in pts),max(x for x,y in pts),max(y for x,y in pts)]}
masks={n:green_pixels(im) for n,im in images.items()}
gltf=json.loads((root/'eight_weight_probe.gltf').read_text())
buffer=base64.b64decode(gltf['buffers'][0]['uri'].split(',',1)[1])
source_checks={}
for semantic in ['JOINTS_0','JOINTS_1','WEIGHTS_0','WEIGHTS_1']:
    accessor=gltf['accessors'][gltf['meshes'][0]['primitives'][0]['attributes'][semantic]]
    source_checks[semantic]={'componentType':accessor['componentType'],'type':accessor['type'],'count':accessor['count']}
    source_checks[semantic]['expected_component']=accessor['componentType']==(5123 if semantic.startswith('JOINTS') else 5126)
    source_checks[semantic]['expected_vec4_count4']=accessor['type']=='VEC4' and accessor['count']==4
stats={n:metrics(m) for n,m in masks.items()}
inspect_log=(root/'inspect.stdout.log').read_text()
array_match=re.search(r'SURFACE 0 vertices=4 bones=(\[.*?\]) weights=(\[.*?\])',inspect_log)
if not array_match: raise RuntimeError('imported surface array record not found')
imported_bones=ast.literal_eval(array_match.group(1))
imported_weights=ast.literal_eval(array_match.group(2))
imported_names=re.findall(r'^BONE \d+ (\S+) parent=-1$',inspect_log,re.M)
def compare(a,b):
    x,y=masks[a],masks[b]
    return {'intersection':len(x&y),'union':len(x|y),'iou':len(x&y)/len(x|y),'differentPixels':len(x^y)}
actual_expected=compare('actual_pose','reference_expected')
actual_first4=compare('actual_pose','reference_first4')
shift=stats['actual_pose']['centroidPx'][0]-stats['actual_rest']['centroidPx'][0]
checks={
 'allFourRendersHaveEqualPixelCounts':len({v['pixelCount'] for v in stats.values()})==1,
 'actualEightWeightPoseMatchesIndependentPlusPointFourReference':actual_expected['iou']>=0.999 and actual_expected['differentPixels']<=1,
 'actualPoseShiftMatchesPointFourMetersAtCameraScale':abs(shift-73.846153846)<0.6,
 'actualPoseClearlyDiffersFromFirstFourOnlyOutcome':actual_first4['iou']<0.5 and abs(stats['actual_pose']['centroidPx'][0]-stats['reference_first4']['centroidPx'][0])>70,
 'sourceUsesEightFloat32Influences':all(source_checks[k]['expected_vec4_count4'] and source_checks[k]['expected_component'] for k in source_checks),
 'importedSceneHasExpectedEightSlotsAndNames':imported_bones==[i for _ in range(4) for i in range(8)] and len(imported_weights)==32 and len(imported_names)==8 and imported_names==[f'Joint{i}' for i in range(1,9)],
 'importAndInspectionExitedCleanly':(root/'import.exit_code.txt').read_text().strip()=='0' and (root/'inspect.exit_code.txt').read_text().strip()=='0' and (root/'import.stderr.log').stat().st_size==0 and (root/'inspect.stderr.log').stat().st_size==0,
 'allRendererRunsExitedZero':all((root/f'{n}.exit_code.txt').read_text().strip()=='0' for n in names),
 'noRendererRunErrors':all(not (root/f'{n}.stderr.log').read_text().strip() for n in names),
}
report={
 'scope':'Synthetic eight-influence renderer support only; not horse anatomy, production weights, locomotion, contact, or performance acceptance.',
 'engine':'Godot 4.7.2.stable.mono.official.ed1daf0bf; OpenGL API 4.1 Metal; Apple M2; compatibility renderer.',
 'input':{'file':'eight_weight_probe.gltf','sha256':hashlib.sha256((root/'eight_weight_probe.gltf').read_bytes()).hexdigest(),'geometry':'single 1m x 1m quad; four vertices; 8 independent joints; float32 weights .125 each; indices 0..7'},
 'projectSha256':hashlib.sha256((root/'project.godot').read_bytes()).hexdigest(),
 'renderSetup':'640x480; same orthographic camera and unshaded material for all four captures; only joints 5-8 translated +0.8m X in actual_pose; independent unskinned expected reference translated +0.4m X; independent first-four-only reference untranslated.',
 'sourceAccessors':source_checks,
 'importedBuffers':{'skeletonBones':len(imported_names),'boneNames':imported_names,'sourceInfluencesPerVertex':8,'importedBoneSlots':imported_bones,'importedWeights':imported_weights,'importedWeightsPerSlot':sum(imported_weights[:8])/8,'importedSumPerVertex':sum(imported_weights[:8])},
 'importInspection':{'exitCode':int((root/'inspect.exit_code.txt').read_text()),'stdoutSha256':hashlib.sha256((root/'inspect.stdout.log').read_bytes()).hexdigest(),'stderrBytes':(root/'inspect.stderr.log').stat().st_size,'importExitCode':int((root/'import.exit_code.txt').read_text()),'importLogSha256':hashlib.sha256((root/'import.stdout.log').read_bytes()).hexdigest()},
 'testSourceSha256':hashlib.sha256((root/'render_probe.gd').read_bytes()).hexdigest(),
 'captures':{n:{**stats[n],'sha256':hashlib.sha256((root/f'{n}.png').read_bytes()).hexdigest(),'exitCode':int((root/f'{n}.exit_code.txt').read_text())} for n in names},
 'comparisons':{'actualPoseVsExpected':actual_expected,'actualPoseVsFirstFourOnly':actual_first4,'centroidShiftPixels':shift},
 'checks':checks,
 'passed':all(checks.values())
}
(root/'renderer_report.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps(report,indent=2))
if not report['passed']: raise SystemExit(1)
