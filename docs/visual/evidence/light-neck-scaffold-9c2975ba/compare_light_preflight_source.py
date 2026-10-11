"""Bind the preflight input to current production, without modifying either .blend."""
import bpy, json, hashlib
from pathlib import Path
ROOT=Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/motion-candidates/adjacent-support')
OUT=ROOT/'virtual-shell-neck-light'
sources={'production':Path('/Users/markrogers/Documents/Codex/2026-10-11/con/work/Horse/art/derived/production/horse_light.blend'),
         'preflight':ROOT.parent/'light/horse_light_motion_candidate.blend'}
expected={'production':'9c2975ba2cd2cf4311b45e96c827bae0474c9259b92fe8b81bafa100f12bc295',
          'preflight':'b2cff126e8af50088290a7c58d92f56e99c750181302c33a07254299d294ff8e'}
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def digest(value): return hashlib.sha256(json.dumps(value,separators=(',',':'),sort_keys=True).encode()).hexdigest()
def matrix(m): return [list(row) for row in m]
def datum(v):
    for n in ('color','vector','uv','value'):
        if hasattr(v,n):
            p=getattr(v,n)
            return list(p) if hasattr(p,'__len__') else p
    raise AssertionError('Unserialized attribute type '+str(type(v)))
def image_source(im):
    packed=getattr(im,'packed_file',None)
    return {'name':im.name,'source':im.source,'filepath':im.filepath,'size':list(im.size),
            'packedSha256':hashlib.sha256(bytes(packed.data)).hexdigest() if packed else None}
def material_source(mat):
    if not mat: return None
    return {'name':mat.name,'diffuseColor':list(mat.diffuse_color),'useNodes':mat.use_nodes,
            'imageNodes':{n.name:image_source(n.image) if n.image else None for n in mat.node_tree.nodes if n.type=='TEX_IMAGE'} if mat.use_nodes else {}}
def curves(action):
    if hasattr(action,'fcurves'): return list(action.fcurves)
    return [c for layer in action.layers for strip in layer.strips
            for bag in getattr(strip,'channelbags',[]) for c in bag.fcurves]
def snapshot():
    meshes={}
    for obj in bpy.data.objects:
        if obj.type!='MESH': continue
        me=obj.data
        meshes[obj.name]={
          'positions':[list(v.co) for v in me.vertices],
          'edges':[list(e.vertices) for e in me.edges],
          'polygons':[list(p.vertices) for p in me.polygons],
          'loops':[l.vertex_index for l in me.loops],
          'morphs':{k.name:[list(v.co) for v in k.data] for k in me.shape_keys.key_blocks} if me.shape_keys else {},
          'morphContract':{k.name:{'sliderMin':k.slider_min,'sliderMax':k.slider_max,'relativeKey':k.relative_key.name,'vertexGroup':k.vertex_group} for k in me.shape_keys.key_blocks} if me.shape_keys else {},
          'uv':{a.name:{'activeRender':a.active_render,'values':[list(v.uv) for v in a.data]} for a in me.uv_layers},
          'colors':{a.name:{'domain':a.domain,'type':a.data_type,'values':[list(v.color) for v in a.data]} for a in me.color_attributes},
          'attributes':{a.name:{'domain':a.domain,'type':a.data_type,'values':[datum(v) for v in a.data]} for a in me.attributes},
          'faceMaterialMask':[p.material_index for p in me.polygons],
          'materials':[material_source(m) for m in me.materials],
          'objectMatrix':matrix(obj.matrix_world),
          'parent':obj.parent.name if obj.parent else None,
          'modifiers':[{'name':m.name,'type':m.type,'object':m.object.name if m.type=='ARMATURE' and m.object else None} for m in obj.modifiers],
          'weights':[{obj.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0} for v in me.vertices],
          'groupNames':[g.name for g in obj.vertex_groups]}
    rig=bpy.data.objects['Skeleton']
    return {'meshes':meshes,'restRig':{'objectMatrix':matrix(rig.matrix_world),
            'bones':{b.name:{'parent':b.parent.name if b.parent else None,'matrixLocal':matrix(b.matrix_local),'head':list(b.head_local),'tail':list(b.tail_local),'useDeform':b.use_deform,'useConnect':b.use_connect,'inheritScale':b.inherit_scale} for b in rig.data.bones}},
            'motionState':{'activeAction':rig.animation_data.action.name if rig.animation_data and rig.animation_data.action else None,
              'actions':{a.name:{'frameRange':list(a.frame_range),'curveCount':len(curves(a)),'curveSha256':digest([{'path':c.data_path,'index':c.array_index,'keyframes':[{'co':list(k.co),'interpolation':k.interpolation} for k in c.keyframe_points]} for c in curves(a)])} for a in bpy.data.actions},
              'pose':{b.name:{'location':list(b.location),'rotationMode':b.rotation_mode,'rotationQuaternion':list(b.rotation_quaternion),'rotationEuler':list(b.rotation_euler),'scale':list(b.scale)} for b in rig.pose.bones},
              'morphValues':{o.name:{k.name:k.value for k in o.data.shape_keys.key_blocks} for o in bpy.data.objects if o.type=='MESH' and o.data.shape_keys}}}
snap={}
for name,path in sources.items():
    assert sha(path)==expected[name]
    bpy.ops.wm.open_mainfile(filepath=str(path))
    snap[name]=snapshot()
    (OUT/f'light_{name}_identity_serialization.json').write_text(json.dumps(snap[name],separators=(',',':')))
    assert sha(path)==expected[name]
a,b=snap['production'],snap['preflight']
comparisons={};weight_diffs={}
for name in sorted(set(a['meshes'])|set(b['meshes'])):
    ma,mb=a['meshes'].get(name),b['meshes'].get(name)
    assert ma is not None and mb is not None,'Source mesh set differs'
    comparisons[name]={key:{'equal':ma[key]==mb[key],'productionSha256':digest(ma[key]),'preflightSha256':digest(mb[key])} for key in ma if key not in ('weights','groupNames')}
    changes=[{'vertexId':i,'position':ma['positions'][i],'production':wa,'preflight':wb} for i,(wa,wb) in enumerate(zip(ma['weights'],mb['weights'])) if wa!=wb]
    weight_diffs[name]={'changedVertexCount':len(changes),'changes':changes,'groupNamesEqual':ma['groupNames']==mb['groupNames'],'productionSha256':digest(ma['weights']),'preflightSha256':digest(mb['weights'])}
rest=a['restRig']==b['restRig']
def original_skull(snapshot):
    return [i for i,w in enumerate(snapshot['meshes']['Body']['weights'])
            if sum(x for n,x in w.items() if n.startswith(('Head','Jaw','Ear')))>.999]
def original_ears(snapshot):
    return {name:{str(i):{n:x for n,x in w.items() if n.startswith('Ear')}
                  for i,w in enumerate(m['weights']) if any(n.startswith('Ear') for n in w)}
            for name,m in snapshot['meshes'].items()}
for name,d in weight_diffs.items():
    d['changedControls']=sorted({n for c in d['changes'] for n in set(c['production'])|set(c['preflight'])
                                if c['production'].get(n,0)!=c['preflight'].get(n,0)})
critical=all(x['equal'] for mesh in comparisons.values() for x in mesh.values()) and rest
report={'taskId':'adult_motion_specialist','readOnly':True,'sourcePaths':{n:str(p) for n,p in sources.items()},'sourceSha256':expected,
        'componentComparisons':comparisons,'canonicalRestRigExact':rest,'restRigHashes':{n:digest(s['restRig']) for n,s in snap.items()},
        'fullProtectedSourceCorrespondence':critical,'weightsComparison':weight_diffs,
        'originalSkullMembership':{'productionIds':original_skull(a),'preflightIds':original_skull(b),'exactlyEqual':original_skull(a)==original_skull(b)},
        'originalEarControlsExactlyEqual':original_ears(a)==original_ears(b),
        'deliberateCandidateDifferences':'Existing preflight input adds Walk_Diagnostic to identical Idle action and contains prior adult support reweighting; it is not weight-identical to production. Exact differences are serialized, with no new changes made by this comparison.',
        'motionDifferences':{'production':a['motionState'],'preflight':b['motionState']},
        'allSourceFilesUnchanged':all(sha(p)==expected[n] for n,p in sources.items()),
        'conclusion':'Protected source correspondence confirmed' if critical else 'Protected source correspondence failed; inspect unequal fields before any field solve'}
(OUT/'light_current_production_correspondence.json').write_text(json.dumps(report,indent=2))
print('TASK adult_motion_specialist CURRENT_PRODUCTION_CORRESPONDENCE',critical,
      'rest',rest,'unequalProtectedFields',[(n,k) for n,m in comparisons.items() for k,v in m.items() if not v['equal']],
      'weightChanges',{n:x['changedVertexCount'] for n,x in weight_diffs.items()},
      'actions',{n:list(s['motionState']['actions']) for n,s in snap.items()},flush=True)
