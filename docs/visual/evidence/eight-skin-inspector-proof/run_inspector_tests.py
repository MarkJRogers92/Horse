import sys, json, struct, hashlib, traceback, importlib.util, os
from pathlib import Path

args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
repo = Path(args[0])
proof = Path(args[1])
diag = Path(args[2])
prod = [
    repo/'game/assets/horse/production/horse_light.glb',
    repo/'game/assets/horse/production/horse_stock.glb',
    repo/'game/assets/horse/production/horse_foal.glb',
]
spec = importlib.util.spec_from_file_location('horse_import_reference', repo/'tools/horse/import_reference.py')
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

report = {'production_default4': [], 'diagnostic': {}, 'malformed': [], 'checks': {}}
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def record_expect(name, fn, should_pass):
    try:
        value = fn()
        if not should_pass:
            report['checks'][name] = {'passed': False, 'unexpected': 'accepted'}
            return False
        report['checks'][name] = {'passed': True, 'value': value}
        return True
    except Exception as exc:
        if should_pass:
            report['checks'][name] = {'passed': False, 'exception': type(exc).__name__+': '+str(exc)}
            return False
        report['checks'][name] = {'passed': True, 'rejected_by': type(exc).__name__+': '+str(exc)}
        return True

def empty_br(): return {}

for path in prod:
    try:
        result=mod.inspect_glb(path, empty_br(), allow_animations=True)
        row={'path':str(path),'sha256':sha(path),'passed':True,'max_skin_influences':result['max_skin_influences'],'max_active_skin_influences':result['max_active_skin_influences'],'max_skin_weight_sum_error':result['max_skin_weight_sum_error'],'mesh_names':result['mesh_names']}
    except Exception as exc:
        row={'path':str(path),'sha256':sha(path),'passed':False,'exception':type(exc).__name__+': '+str(exc)}
    report['production_default4'].append(row)

report['diagnostic']={'path':str(diag),'sha256':sha(diag),'anatomy_status':'anatomically rejected diagnostic; no motion acceptance'}
record_expect('diagnostic_default4_rejects', lambda: mod.inspect_glb(diag, empty_br()), False)
report['diagnostic']['default4_result']=report['checks']['diagnostic_default4_rejects']
record_expect('diagnostic_explicit8_passes', lambda: mod.inspect_glb(diag, empty_br(), max_skin_influences=8), True)
if report['checks']['diagnostic_explicit8_passes'].get('passed'):
    d=report['checks']['diagnostic_explicit8_passes']['value']
    report['diagnostic']['explicit8_summary']={k:d[k] for k in ('max_skin_influences','max_active_skin_influences','max_skin_weight_sum_error','skin_weights_by_mesh')}
    del report['checks']['diagnostic_explicit8_passes']['value']
report['diagnostic']['explicit8_result']=report['checks']['diagnostic_explicit8_passes']

# Clone and mutate the diagnostic GLB only inside the proof folder.
doc0, binary0=mod.glb_chunks(diag)
def locate():
    mesh=next(m for m in doc0['meshes'] if m.get('name')=='Body')
    prim=mesh['primitives'][0]
    attrs=prim['attributes']
    return prim, attrs, attrs['JOINTS_1'], attrs['WEIGHTS_1']

def serialize(doc, binary, name):
    json_bytes=json.dumps(doc,separators=(',',':')).encode('utf-8')
    json_bytes += b' ' * ((-len(json_bytes)) % 4)
    binary=bytes(binary)
    binary += b'\0' * ((-len(binary)) % 4)
    chunks=struct.pack('<I4s',len(json_bytes),b'JSON')+json_bytes
    if binary: chunks += struct.pack('<I4s',len(binary),b'BIN\0')+binary
    raw=struct.pack('<4sII',b'glTF',2,12+len(chunks))+chunks
    path=proof/'malformed'/name
    path.write_bytes(raw)
    return path

def mutate(name, mutator):
    doc=json.loads(json.dumps(doc0)); binary=bytearray(binary0)
    mutator(doc,binary)
    path=serialize(doc,binary,name+'.glb')
    try:
        mod.inspect_glb(path,empty_br(),max_skin_influences=8)
        return {'case':name,'sha256':sha(path),'accepted':True,'exception':None}
    except Exception as exc:
        return {'case':name,'sha256':sha(path),'accepted':False,'rejected_by':type(exc).__name__+': '+str(exc)}

# each mutation is independently based on the original frozen diagnostic
for name, fn in [
 ('unpaired_attribute', lambda d,b: locate_attrs(d).pop('WEIGHTS_1')),
 ('count_mismatch', lambda d,b: d['accessors'].__getitem__(locate_attrs(d)['WEIGHTS_1']).__setitem__('count', d['accessors'][locate_attrs(d)['WEIGHTS_1']]['count']-1)),
 ('wrong_accessor_type', lambda d,b: d['accessors'].__getitem__(locate_attrs(d)['WEIGHTS_1']).__setitem__('type','VEC3')),
 ('wrong_component_type', lambda d,b: d['accessors'].__getitem__(locate_attrs(d)['WEIGHTS_1']).__setitem__('componentType',5125)),
 ('negative_weight', lambda d,b: write_first_float(d,b,locate_attrs(d)['WEIGHTS_1'],-0.01)),
 ('nonfinite_weight', lambda d,b: write_first_float(d,b,locate_attrs(d)['WEIGHTS_1'],float('nan'))),
 ('invalid_joint_index', lambda d,b: write_first_joint(d,b,locate_attrs(d)['JOINTS_1'])),
 ('unsupported_set2', lambda d,b: add_set2(d,locate_attrs(d))),
]:
    def locate_attrs(doc):
        return next(m for m in doc['meshes'] if m.get('name')=='Body')['primitives'][0]['attributes']
    def accessor_start(doc, accessor_index):
        acc=doc['accessors'][accessor_index]; view=doc['bufferViews'][acc['bufferView']]
        return view.get('byteOffset',0)+acc.get('byteOffset',0)
    def write_first_float(doc,b,accessor_index,value):
        struct.pack_into('<f',b,accessor_start(doc,accessor_index),value)
    def write_first_joint(doc,b,accessor_index):
        acc=doc['accessors'][accessor_index]; fmt='<B' if acc['componentType']==5121 else '<H'
        value=255 if acc['componentType']==5121 else 65535
        struct.pack_into(fmt,b,accessor_start(doc,accessor_index),value)
    def add_set2(doc,attrs):
        attrs['JOINTS_2']=attrs['JOINTS_1']; attrs['WEIGHTS_2']=attrs['WEIGHTS_1']
    report['malformed'].append(mutate(name,fn))

record_expect('unsupported_limit_rejected', lambda: mod.inspect_glb(diag,empty_br(),max_skin_influences=6), False)
report['checks']['production_all_pass']=all(row['passed'] for row in report['production_default4'])
report['checks']['all_malformed_rejected']=all(not item['accepted'] for item in report['malformed'])
report['checks']['all_expected']=all(x.get('passed') for x in report['checks'].values() if isinstance(x,dict)) and report['checks']['production_all_pass'] and report['checks']['all_malformed_rejected']
report['source_sha256']={'inspector':sha(repo/'tools/horse/import_reference.py'),'diagnostic':sha(diag),'production':[row['sha256'] for row in report['production_default4']]}
(proof/'report.json').write_text(json.dumps(report,indent=2,sort_keys=True)+'\n')
print(json.dumps({k:v for k,v in report['checks'].items()},indent=2,sort_keys=True))
print('REPORT',proof/'report.json')
if not report['checks']['all_expected'] or not report['checks']['production_all_pass'] or not report['checks']['all_malformed_rejected']:
    sys.stderr.flush()
    os._exit(1)
