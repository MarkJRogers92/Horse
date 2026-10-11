import bpy, sys, json, math, hashlib, struct, traceback, os
from pathlib import Path
from mathutils import Vector, Matrix

args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
root=Path(args[0]); out=Path(args[1]); out.mkdir(parents=True,exist_ok=True)
walk_path=root/'quadruped-walk.glb'
EXPECTED='8d6a78260e5b911cb0a27eafd2c1482bec718548b4dc97eeff3a22e1b6d9a8ba'
report={'scope':'isolated unmodified Walk candidate inspection; no production promotion','inputs':{},'raw_gltf':{},'import':{},'uniform_scale':{},'leg_sole_sets':{},'motion':{},'renders':{}}
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def vec(v): return [round(float(x),7) for x in v]
def glb_doc(p):
    raw=p.read_bytes(); magic,version,total=struct.unpack_from('<4sII',raw,0)
    if magic!=b'glTF' or version!=2 or total!=len(raw): raise RuntimeError('invalid GLB header')
    off=12; doc=None
    while off<len(raw):
        n,k=struct.unpack_from('<I4s',raw,off);off+=8;chunk=raw[off:off+n];off+=n
        if k==b'JSON':doc=json.loads(chunk.decode('utf-8').rstrip(' \t\n\r\0'))
    return doc
def import_glb(p):
    before=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(p));return list(set(bpy.data.objects)-before)
def armature_for(mesh,objects):
    for m in mesh.modifiers:
        if m.type=='ARMATURE' and m.object in objects:return m.object
    return None
def bounds_world(obj):
    pts=[obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return [min(p[i] for p in pts) for i in range(3)],[max(p[i] for p in pts) for i in range(3)]
def set_frame(fr):
    f=math.floor(fr);bpy.context.scene.frame_set(f,subframe=fr-f);bpy.context.scene.view_layers.update()
def evaluated_positions(mesh,depsgraph):
    ob=mesh.evaluated_get(depsgraph);me=ob.to_mesh();pts=[ob.matrix_world @ v.co for v in me.vertices];ob.to_mesh_clear();return pts
def p05_centroid(points):
    ordered=sorted(points,key=lambda p:p.z);n=max(1,math.ceil(len(ordered)*.05));bottom=ordered[:n]
    return Vector((sum(p.x for p in bottom)/n,sum(p.y for p in bottom)/n,ordered[n-1].z)),len(bottom)
def runs(mask):
    result=[];start=None
    for i,v in enumerate(mask+[False]):
        if v and start is None:start=i
        elif not v and start is not None:result.append((start,i-1));start=None
    return result
def look_at(cam,eye,target):
    cam.location=eye;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler()
def add_material(name,color):
    m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF')
    if bs:bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Roughness'].default_value=.82
    return m
def render_one(mesh,arm,frame,view,center,scale,path,res=(640,480)):
    scene=bpy.context.scene;set_frame(frame);deps=bpy.context.evaluated_depsgraph_get();pts=evaluated_positions(mesh,deps);h=max(.01,max(p.z for p in pts)-min(p.z for p in pts));dist=scale*1.25;target=Vector(center)
    cam=bpy.data.objects.get('iso_camera')
    if cam is None:
        data=bpy.data.cameras.new('iso_camera_data');cam=bpy.data.objects.new('iso_camera',data);scene.collection.objects.link(cam)
    cam.data.type='ORTHO';cam.data.ortho_scale=scale;scene.camera=cam
    if view=='side': eye=target+Vector((dist,0,h*.12))
    elif view=='front': eye=target+Vector((0,-dist,h*.1))
    else: eye=target+Vector((dist*.75,-dist*.75,h*.18))
    look_at(cam,eye,target);scene.render.resolution_x=res[0];scene.render.resolution_y=res[1];scene.render.resolution_percentage=100;scene.render.filepath=str(path);scene.render.image_settings.file_format='PNG';bpy.ops.render.render(write_still=True)
    return {'frame':float(frame),'time_seconds':float((frame-action_start)/scene.render.fps),'view':view,'path':str(path)}

try:
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    report['inputs']={'walk':{'path':str(walk_path),'sha256':sha(walk_path),'size_bytes':walk_path.stat().st_size}}
    doc=glb_doc(walk_path)
    report['raw_gltf']={'mesh_names':[m.get('name') for m in doc.get('meshes',[])],'mesh_count':len(doc.get('meshes',[])),'skin_joint_count':len(doc.get('skins',[{}])[0].get('joints',[])) if doc.get('skins') else 0,'animation_names':[a.get('name','') for a in doc.get('animations',[])],'animation_sampler_input_key_counts':{a.get('name',''):[doc['accessors'][s['input']]['count'] for s in a.get('samplers',[])] for a in doc.get('animations',[])}}
    imported=import_glb(walk_path);meshes=[o for o in imported if o.type=='MESH'];arms=[o for o in imported if o.type=='ARMATURE']
    mesh=next((o for o in meshes if o.name.lower().startswith('char1')),None)
    arm=armature_for(mesh,imported) if mesh else None
    if mesh is None or arm is None: raise RuntimeError('Walk GLB import did not provide char1 plus its own armature modifier')
    action=arm.animation_data.action if arm.animation_data and arm.animation_data.action else None
    if action is None: raise RuntimeError('Imported Walk armature has no assigned action')
    # Preserve the imported objects and their relative transforms under one scale-only inspection parent.
    lo,hi=bounds_world(mesh);mesh_h=hi[2]-lo[2]
    if mesh_h<=0:raise RuntimeError('char1 has no positive world height')
    scale_factor=1.55/mesh_h
    parent=bpy.data.objects.new('INSPECTION_UNIFORM_SCALE_PARENT',None);bpy.context.scene.collection.objects.link(parent);parent.matrix_world=Matrix.Identity(4);parent.scale=(scale_factor,)*3
    for o in imported:
        world=o.matrix_world.copy();o.parent=parent;o.matrix_parent_inverse=Matrix.Identity(4);o.matrix_basis=world
    bpy.context.scene.view_layers.update()
    # Blender's GLTF importer adds a bone-display helper named Icosphere; retain it but keep it out of presentation renders.
    helpers=[]
    for o in meshes:
        if o is not mesh and not armature_for(o,imported):o.hide_render=True;o.hide_set(True);helpers.append({'name':o.name,'vertices':len(o.data.vertices),'reason':'non-skinned importer helper; retained in Blend, hidden in render'})
    set_frame(float(action.frame_range[0]));deps=bpy.context.evaluated_depsgraph_get();rest_world=evaluated_positions(mesh,deps)
    lo=[min(p[i] for p in rest_world) for i in range(3)];hi=[max(p[i] for p in rest_world) for i in range(3)];dim=[hi[i]-lo[i] for i in range(3)];height=dim[2];center=[(lo[i]+hi[i])*.5 for i in range(3)]
    report['import']={'objects':[{'name':o.name,'type':o.type} for o in imported],'mesh_object':mesh.name,'mesh_vertex_count':len(mesh.data.vertices),'mesh_edge_count':len(mesh.data.edges),'mesh_triangle_count':sum(len(p.vertices)-2 for p in mesh.data.polygons),'armature':arm.name,'bone_count':len(arm.data.bones),'bone_names':[b.name for b in arm.data.bones],'action':action.name,'action_frame_range':[float(action.frame_range[0]),float(action.frame_range[1])],'scene_fps':bpy.context.scene.render.fps,'hidden_import_helpers':helpers,'scaled_mesh_bounds_world':{'min':vec(lo),'max':vec(hi),'extent':vec(dim)}}
    report['uniform_scale']={'parent':parent.name,'uniform_factor':scale_factor,'source_mesh_height':mesh_h,'target_mesh_height':1.55,'transform':'one common parent scales imported mesh, armature and helper objects uniformly; no vertices, joints, weights, animation keys or relative object transforms rewritten'}
    names={b.name for b in arm.data.bones};group_names={g.name:g.index for g in mesh.vertex_groups}
    leg_defs={
      'hind_left':['backleg','backleg0','backleg1','backleg2'],
      'hind_right':['R_backleg','R_backleg0','R_backleg1','R_backleg2'],
      'fore_left':['frontleg','frontleg0','frontleg1','frontleg2'],
      'fore_right':['R_frontleg','R_frontleg0','R_frontleg1','R_frontleg2'],
    }
    if any(n not in names for chain in leg_defs.values() for n in chain):raise RuntimeError('Expected four named leg chains are not present in the imported skeleton')
    if any(n not in group_names for chain in leg_defs.values() for n in chain):raise RuntimeError('Expected bone vertex groups are missing; cannot derive hoof regions from skin weights')
    all_z=[p.z for p in rest_world];rest_height=max(all_z)-min(all_z)
    arm_eval=arm.evaluated_get(deps)
    head_world={n:arm_eval.matrix_world @ arm_eval.pose.bones[n].head for chain in leg_defs.values() for n in chain}
    selected_group_indices={group_names[n] for c in leg_defs.values() for n in c};index_to_group={g.index:g.name for g in mesh.vertex_groups}
    vweight=[]
    for v in mesh.data.vertices:
        vweight.append({index_to_group[g.group]:g.weight for g in v.groups if g.group in selected_group_indices})
    sole_sets={};sole_indices={}
    for label,chain in leg_defs.items():
        idx=[i for i,w in enumerate(vweight) if sum(w.get(n,0.0) for n in chain)>=0.05]
        if not idx:raise RuntimeError('No leg-chain weighted vertices for '+label)
        zs=[rest_world[i].z for i in idx];cut=sorted(zs)[max(0,math.ceil(len(zs)*.10)-1)]
        sole=[i for i in idx if rest_world[i].z<=cut]
        if len(sole)<4:
            cut=min(zs)+.01*rest_height;sole=[i for i in idx if rest_world[i].z<=cut]
        if len(sole)<4:raise RuntimeError('Could not derive at least four low sole vertices for '+label)
        sole_indices[label]=sole;pts=[rest_world[i] for i in sole]
        smin=[min(p[a] for p in pts) for a in range(3)];smax=[max(p[a] for p in pts) for a in range(3)]
        terminal=chain[-1];joint=head_world[terminal];sole_center=Vector((sum(p.x for p in pts)/len(pts),sum(p.y for p in pts)/len(pts),sum(p.z for p in pts)/len(pts)))
        sole_sets[label]={'chain_bones':chain,'terminal_rest_joint_head_world':vec(joint),'chain_weighted_vertex_count':len(idx),'sole_vertex_rule':'chain weight sum >=0.05; lowest 10th percentile of rest-geometry Z within that limb component','sole_vertex_count':len(sole),'sole_vertex_indices':sole,'rest_sole_bounds_world':{'min':vec(smin),'max':vec(smax)},'rest_sole_centroid_world':vec(sole_center),'terminal_joint_head_to_sole_centroid_distance':(joint-sole_center).length}
    report['leg_sole_sets']={'selection':'four named front/back and left/right chains; actual mesh vertices selected by those chains\' weights and low sole geometry, never generated bone tails','rest_mesh_height_world':rest_height,'sets':sole_sets}
    action_start,action_end=map(float,action.frame_range);fps=bpy.context.scene.render.fps;frames=[action_start+(action_end-action_start)*i/60 for i in range(61)]
    trajectories={k:[] for k in leg_defs};root_name='Hips' if 'Hips' in arm.pose.bones else arm.data.bones[0].name
    root_rows=[];mesh_ground_min=math.inf;mesh_ground_max=-math.inf;motion_min=[math.inf]*3;motion_max=[-math.inf]*3
    for fr in frames:
        set_frame(fr);deps=bpy.context.evaluated_depsgraph_get();pts=evaluated_positions(mesh,deps)
        if len(pts)!=len(mesh.data.vertices):raise RuntimeError('Evaluated mesh vertex indices changed; cannot map sole vertex sets')
        mesh_ground_min=min(mesh_ground_min,min(p.z for p in pts));mesh_ground_max=max(mesh_ground_max,max(p.z for p in pts))
        for p in pts:
            for axis in range(3):motion_min[axis]=min(motion_min[axis],p[axis]);motion_max[axis]=max(motion_max[axis],p[axis])
        arm_eval=arm.evaluated_get(deps);rootp=arm_eval.matrix_world @ arm_eval.pose.bones[root_name].head;root_rows.append({'frame':fr,'time_seconds':(fr-action_start)/fps,'world':vec(rootp)})
        for label,indices in sole_indices.items():
            q,count=p05_centroid([pts[i] for i in indices]);rel=q-rootp
            trajectories[label].append({'frame':fr,'time_seconds':(fr-action_start)/fps,'sole_p05_world':vec(q),'root_relative':vec(rel),'bottom_vertices_used':count})
    # Ground proximity, root travel, and stance slip are separate quantities.
    height=mesh_ground_max-mesh_ground_min;band=.025*height;per_leg=[]
    for label,rows in trajectories.items():
        mask=[r['sole_p05_world'][2]<=mesh_ground_min+band for r in rows];contact_runs=runs(mask)
        world_path=0.0;root_path=0.0;contact_world_slip=0.0;contact_root_slip=0.0
        for i in range(1,len(rows)):
            a=Vector(rows[i-1]['sole_p05_world']);b=Vector(rows[i]['sole_p05_world']);world_path+=math.hypot(b.x-a.x,b.y-a.y)
            a=Vector(rows[i-1]['root_relative']);b=Vector(rows[i]['root_relative']);root_path+=math.hypot(b.x-a.x,b.y-a.y)
            if mask[i-1] and mask[i]:
                a=Vector(rows[i-1]['sole_p05_world']);b=Vector(rows[i]['sole_p05_world']);contact_world_slip+=math.hypot(b.x-a.x,b.y-a.y)
                a=Vector(rows[i-1]['root_relative']);b=Vector(rows[i]['root_relative']);contact_root_slip+=math.hypot(b.x-a.x,b.y-a.y)
        episodes=[]
        for a,b in contact_runs:
            if b>a:
                p0=Vector(rows[a]['sole_p05_world']);p1=Vector(rows[b]['sole_p05_world']);r0=Vector(rows[a]['root_relative']);r1=Vector(rows[b]['root_relative'])
                ws=math.hypot(p1.x-p0.x,p1.y-p0.y);rs=math.hypot(r1.x-r0.x,r1.y-r0.y)
            else:ws=rs=0.0
            episodes.append({'sample_start':a,'sample_end':b,'start_time_seconds':rows[a]['time_seconds'],'end_time_seconds':rows[b]['time_seconds'],'duration_seconds':(b-a)*(action_end-action_start)/(60*fps),'world_horizontal_endpoint_drift':ws,'root_relative_horizontal_endpoint_drift':rs})
        per_leg.append({'label':label,'p05_ground_band_samples':sum(mask),'ground_band_duration_seconds':sum((b-a)*(action_end-action_start)/(60*fps) for a,b in contact_runs),'contact_episodes':episodes,'world_horizontal_path_length_over_cycle':world_path,'root_relative_horizontal_path_length_over_cycle':root_path,'world_horizontal_slip_path_during_contact':contact_world_slip,'root_relative_horizontal_slip_path_during_contact':contact_root_slip,'p05_trajectory':rows})
    root0=Vector(root_rows[0]['world']);root1=Vector(root_rows[-1]['world']);rootmax=max((Vector(r['world'])-root0).length for r in root_rows)
    sweep_dim=[motion_max[i]-motion_min[i] for i in range(3)];center=[(motion_min[i]+motion_max[i])*.5 for i in range(3)]
    report['motion']={'action_frame_range':[action_start,action_end],'duration_seconds':(action_end-action_start)/fps,'sample_count':len(frames),'root_bone':root_name,'root_start':root_rows[0]['world'],'root_end':root_rows[-1]['world'],'root_net_translation':(root1-root0).length,'root_max_excursion_from_start':rootmax,'mesh_ground_estimate_z_range':[mesh_ground_min,mesh_ground_max],'animated_mesh_swept_bounds_world':{'min':vec(motion_min),'max':vec(motion_max),'extent':vec(sweep_dim)},'contact_band_above_global_minimum':band,'limbs':per_leg,'interpretation':'Root travel, whole-cycle foot-point path, and sole-p05 contact/slip are measured independently. Bone tails are not used as foot points.'}
    # Side/front/quarter true-scale renders with a simple floor and directional lights.
    floor_mat=add_material('inspection_floor',(0.22,0.24,0.27));floor_mesh=bpy.data.meshes.new('inspection_floor_mesh');floor_mesh.from_pydata([(-5,-5,mesh_ground_min),(5,-5,mesh_ground_min),(5,5,mesh_ground_min),(-5,5,mesh_ground_min)],[],[(0,1,2,3)]);floor_mesh.materials.append(floor_mat);floor=bpy.data.objects.new('inspection_floor',floor_mesh);bpy.context.scene.collection.objects.link(floor)
    scene=bpy.context.scene;scene.render.engine='BLENDER_EEVEE';scene.world.color=(.035,.04,.05)
    light_data=bpy.data.lights.new('inspection_area','AREA');light=bpy.data.objects.new('inspection_area',light_data);scene.collection.objects.link(light);light.location=(2,-3,4);light_data.energy=1800;light_data.shape='DISK';light_data.size=4
    scene.render.image_settings.file_format='PNG';scene.render.resolution_percentage=100
    # Ortho scale is camera width; include aspect ratio so the full vertical horse fits.
    scale=max(sweep_dim[1]*1.3, sweep_dim[2]*1.3*(640/480), sweep_dim[0]*1.6)
    render_plan=[]
    for f in (0,.25,.5,.75):render_plan.append((action_start+(action_end-action_start)*f,'side'))
    render_plan += [(action_start+(action_end-action_start)*.5,'front'),(action_start+(action_end-action_start)*.75,'quarter')]
    renders=[]
    for i,(fr,view) in enumerate(render_plan):renders.append(render_one(mesh,arm,fr,view,center,scale,out/f'pose_{i+1}_{view}_{fr:.2f}.png'))
    # Contact sheet from exact rendered frames.
    import numpy as np
    imgs=[bpy.data.images.load(r['path'],check_existing=False) for r in renders];tw,th=640,480;canvas=np.zeros((th*2,tw*3,4),dtype=np.float32)
    for i,img in enumerate(imgs):
        pix=np.empty(len(img.pixels),dtype=np.float32);img.pixels.foreach_get(pix);arr=pix.reshape((th,tw,4));y=(i//3)*th;x=(i%3)*tw;canvas[y:y+th,x:x+tw]=arr
    sheet=bpy.data.images.new('isolated_walk_contact_sheet',width=tw*3,height=th*2,alpha=True);sheet.pixels.foreach_set(canvas.ravel());sheet.filepath_raw=str(out/'contact_sheet.png');sheet.file_format='PNG';sheet.save()
    gif_info={'created':False,'reason':'Pillow unavailable or GIF encoding failed'}
    try:
        from PIL import Image
        frames_gif=[]
        for i in range(12):
            fr=action_start+(action_end-action_start)*i/12
            img_path=out/f'walk12_{i:02d}.png';render_one(mesh,arm,fr,'side',center,scale*.95,img_path,(320,240));frames_gif.append(Image.open(img_path).convert('P',palette=Image.Palette.ADAPTIVE))
        frames_gif[0].save(out/'walk12.gif',save_all=True,append_images=frames_gif[1:],duration=100,loop=0,optimize=False);gif_info={'created':True,'path':str(out/'walk12.gif'),'frames':12,'duration_ms':100}
    except Exception as gif_exc:gif_info={'created':False,'reason':type(gif_exc).__name__+': '+str(gif_exc)}
    report['renders']={'poses':renders,'contact_sheet':str(out/'contact_sheet.png'),'twelve_frame_preview':gif_info,'helper_policy':'Generated importer bone-display helper retained under scale parent but hidden in renders; char1 only is shown.'}
    scene.frame_set(round((action_start+action_end)/2));bpy.ops.wm.save_as_mainfile(filepath=str(out/'quadruped_walk_isolated_scaled.blend'))
    report['verification']={'walk_sha256_expected':EXPECTED,'walk_sha256_actual':sha(walk_path),'source_unchanged':sha(walk_path)==EXPECTED,'no_source_edits':True,'all_4_leg_sets_derived':len(sole_sets)==4,'all_61_sampled_sole_trajectories':all(len(v)==61 for v in trajectories.values()),'no_whole_mesh_edge_sweep_repeated':True}
    (out/'report.json').write_text(json.dumps(report,indent=2,sort_keys=True)+'\n');print('REPORT',out/'report.json');print('VERIFICATION',json.dumps(report['verification'],sort_keys=True))
    if not all(report['verification'].values()):sys.stderr.flush();os._exit(1)
except BaseException as exc:
    report['fatal']={'type':type(exc).__name__,'message':str(exc),'traceback':traceback.format_exc()}
    (out/'report.json').write_text(json.dumps(report,indent=2,sort_keys=True)+'\n');print(traceback.format_exc(),file=sys.stderr);sys.stderr.flush();os._exit(1)
