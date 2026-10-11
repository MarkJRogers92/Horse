#!/usr/bin/env python3
"""Hash-check final candidate evidence and write an inactive integration manifest."""
import argparse, hashlib, json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def read(p): return json.loads(p.read_text())
def require(ok, message):
    if not ok: raise ValueError(message)

def prepare(runtime_dir, out, candidate_suffix="-walk"):
    require(candidate_suffix in ("-walk", "-walk-polish"), "Unsupported candidate suffix")
    records = []
    for family, ids in [('stock',['horse.compact.adult.v1']),('foal',['horse.standard.foal.v1','horse.compact.foal.v1'])]:
        folder = ROOT/'art/derived/production/candidates'/f'{family}{candidate_suffix}'
        glb, blend = folder/f'horse_{family}_walk.glb', folder/f'horse_{family}_walk.blend'
        profile_path, audit_path = folder/'walk-motion-profile.json', folder/'source-audit.json'
        profile, audit = read(profile_path), read(audit_path)
        runtime_path, surface_path = runtime_dir/f'{family}-report.json', runtime_dir/f'{family}-surface-report.json'
        runtime, surface = read(runtime_path), read(surface_path)
        gh,bh = sha(glb),sha(blend)
        require(profile['sourceGlbSha256']==audit['candidateGlbSha256']==gh,f'{family}: stale GLB proof')
        require(profile['candidateBlendSha256']==audit['candidateBlendSha256']==bh,f'{family}: stale source proof')
        require(audit['protectedMeshDataExact'] is True and audit['bodyMaxEdgeStretch']<=2.0,f'{family}: source deformation/preservation fails')
        for name,r in [('runtime',runtime),('surface',surface)]:
            require(r.get('candidateGlbSha256')==gh,f'{family}: {name} report not bound to final GLB')
            require(r.get('failures')==[],f'{family}: {name} failures')
        require(profile['sourceRootInPlace'] is True,f'{family}: unknown root motion')
        require(runtime.get('importBakeFps',30)==profile['fps'],f'{family}: import fps mismatch')
        contact=runtime.get('contact',{})
        require(contact.get('failures')==[] and contact.get('registered',0)>0,f'{family}: contact not verified')
        require(runtime['meshCount']==4 and all(m['blendShapes']==4 for m in runtime['meshes']),f'{family}: component/morph contract failed')
        require(len(runtime['meshes'])==4 and all(m.get('zeroWeightVertices')==0 and m.get('nonUnitWeightVertices')==0 for m in runtime['meshes']),f'{family}: unverified or invalid skin weights')
        skin=runtime.get('skinDeformation',{})
        require(skin.get('failures')==[] and skin.get('maxBodyEdgeStretch',float('inf'))<=2.0,f'{family}: imported deformation not verified')
        components=runtime.get('allComponentDeformation',{})
        require(set(components)=={'Body','Eyes','Mane','Tail'} and all(v.get('failures')==[] and v.get('maxBodyEdgeStretch',float('inf'))<=2.0 for v in components.values()),f'{family}: all-component deformation not verified')
        records.append({'familyIds':ids,'enabled':False,'scope':'neutral-shape staging, not game activation',
            'sourceScene':str(glb.relative_to(ROOT)),'suggestedGameScene':f'res://assets/horse/production/horse_{family}_walk.glb',
            'sourceGlbSha256':gh,'candidateBlendSha256':bh,
            'motionProfilePath':str(profile_path.relative_to(ROOT)),'motionProfileSha256':sha(profile_path),
            'sourceAuditSha256':sha(audit_path),'runtimeReportSha256':sha(runtime_path),'surfaceReportSha256':sha(surface_path),
            'periodFrames':profile['periodFrames'],'fps':profile['fps'],'dutyFactor':profile['dutyFactor'],
            'forwardSpeedMetersPerSecond':profile['forwardSpeedMetersPerSecond'],'godotImportAnimationFps':profile['fps'],
            'animationName':'Walk','loopMode':'linear','sourceRootInPlace':True,
            'bodySignedDetail':None,'coatPolicy':'preserve native family UV and masks; no Light atlas'})
    result={'schema':'horse.remaining_walks.integration_staging.v1','activeCatalogChanged':False,
        'gameRuntimeVerified':False,'macBuildChanged':False,'variants':records,
        'activationChecklist':'docs/visual/REMAINING_WALKS_INTEGRATION_PREP.md'}
    out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,indent=2)+'\n')
    print(f'Prepared inactive integration manifest: {out}')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--runtime-dir',type=Path,required=True);parser.add_argument('--out',type=Path,required=True);parser.add_argument('--candidate-suffix',choices=['-walk','-walk-polish'],default='-walk')
    args=parser.parse_args();prepare(args.runtime_dir.resolve(),args.out.resolve(),args.candidate_suffix)
