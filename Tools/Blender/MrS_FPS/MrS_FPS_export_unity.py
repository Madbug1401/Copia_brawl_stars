"""Exporta os braços do Mr. S em 1.ª pessoa para o Unity (FBX).

Conteúdo do FBX:
- MrS_FP_Rig: esqueleto (só os ossos são animados) + MrS_FP_Braco: braço ESQUERDO com mão, unhas e manga
  (malha já suavizada; o Unity usa o material toon do personagem, a mesma textura/UV).
- 6 animações (takes): L_Idle, L_Spray, L_Shake (braço esquerdo) e R_Idle, R_Spray, R_Shake (o braço DIREITO
  espelhado para o lado esquerdo). No Unity, o braço direito é o mesmo FBX com escala X = -1.
- Marcadores (filhos do osso da mão): Lata, Lata_Cima, Lata_Frente — onde fica a lata (centro, cima, direção do jato).
- Referências da câmara FPS: CamRef, CamRef_Frente, CamRef_Cima — o Unity alinha estes pontos com a sua câmara.
Uso: blender -b -P MrS_FPS_export_unity.py -- <Assets/_Game/Resources/Characters/sprayer_fpview.fbx>
"""
import bpy, sys, math, os
from mathutils import Vector, Matrix

OUT_FBX = sys.argv[sys.argv.index("--") + 1]
sys.argv = ["x", "--", "none"]
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "MrS_FPS_build.py")).read())

sc = bpy.context.scene
SEGMENTS = {"Idle": (1, 60), "Spray": (61, 130), "Shake": (131, 210)}

# ---------------------------------------------------------------- gravar a pose dos dois esqueletos em todos os frames
names = [b.name for b in arm.data.bones]
rec = {"L": {}, "R": {}}
for f in range(1, 211):
    sc.frame_set(f)
    rec["L"][f] = {n: arm.pose.bones[n].matrix.copy() for n in names}
    rec["R"][f] = {n: arm2.pose.bones[n].matrix.copy() for n in names}
sc.frame_set(1)
can_M = can_L.matrix_world.copy()
cam_M = cam.matrix_world.copy()

# ---------------------------------------------------------------- esqueleto de exportação (sem restrições)
exp_data = arm.data.copy(); exp_data.name = "MrS_FP_Rig"
for b in exp_data.bones:
    b.inherit_scale = 'FULL'
exp = bpy.data.objects.new("MrS_FP_Rig", exp_data); sc.collection.objects.link(exp)
exp.matrix_world = arm.matrix_world.copy()
bpy.context.view_layer.update()

# ---------------------------------------------------------------- malha do braço esquerdo (máscara + suavização aplicadas)
mesh_obj = body.copy(); mesh_obj.data = body.data.copy(); mesh_obj.name = "MrS_FP_Braco"
sc.collection.objects.link(mesh_obj)
for md in list(mesh_obj.modifiers):
    if md.type == 'ARMATURE': mesh_obj.modifiers.remove(md)
for md in mesh_obj.modifiers:
    if md.type == 'SUBSURF': md.levels = 1
bpy.ops.object.select_all(action='DESELECT')
arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
mesh_obj.select_set(True); bpy.context.view_layer.objects.active = mesh_obj
mw = mesh_obj.matrix_world.copy()
mesh_obj.parent = None; mesh_obj.matrix_world = mw
for md in list(mesh_obj.modifiers):
    bpy.ops.object.modifier_apply(modifier=md.name)
# unhas do braço esquerdo, juntas à malha (já têm os pesos do esqueleto)
nails = []
for n in [o for o in bpy.data.objects if o.name.startswith("Unha_Left_")]:
    c = n.copy(); c.data = n.data.copy(); sc.collection.objects.link(c)
    cw = n.matrix_world.copy(); c.parent = None; c.matrix_world = cw
    for md in list(c.modifiers): c.modifiers.remove(md)
    nails.append(c)
bpy.ops.object.select_all(action='DESELECT')
for c in nails:
    c.select_set(True); bpy.context.view_layer.objects.active = c
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    c.select_set(False)
mesh_obj.select_set(True)
bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for c in nails: c.select_set(True)
bpy.context.view_layer.objects.active = mesh_obj
if nails: bpy.ops.object.join()
mesh_obj.parent = exp; mesh_obj.matrix_parent_inverse = exp.matrix_world.inverted()
md = mesh_obj.modifiers.new("Esqueleto", 'ARMATURE'); md.object = exp
arm.data.pose_position = 'POSE'
print("BRACO verts", len(mesh_obj.data.vertices), "mats", [m.name for m in mesh_obj.data.materials])

# ---------------------------------------------------------------- pose (frame 1) e marcadores da lata no osso da mão
def bake_pose(side, f):
    """Pose gravada -> rotação/posição local de cada osso do esqueleto de exportação."""
    R = rec[side][f]
    out = {}
    for pb in exp.pose.bones:
        b = pb.bone
        M = R[b.name]
        if b.parent:
            Mp = R[b.parent.name]
            L = (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ (Mp.inverted() @ M)
        else:
            L = b.matrix_local.inverted() @ M
        out[b.name] = L
    return out

def apply_pose(P):
    for pb in exp.pose.bones:
        loc, rot, scl = P[pb.name].decompose()
        pb.rotation_mode = 'QUATERNION'
        pb.location = loc; pb.rotation_quaternion = rot; pb.scale = scl

apply_pose(bake_pose("L", 1))
bpy.context.view_layer.update()
for n in ("smartrig:LeftArm","smartrig:LeftForeArm","smartrig:LeftHand","smartrig:LeftHandThumb3"):
    print("CMP", n, (exp.matrix_world @ exp.pose.bones[n].head - arm.matrix_world @ rec["L"][1][n].translation).length)
# A pose do frame 1 (braços esticados pelo IK) passa a ser a pose de repouso: assim as animações não precisam de
# escala nos ossos (o FBX/Unity não lidam bem com escala não uniforme herdada).
bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True); bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.modifier_apply(modifier="Esqueleto")
bpy.ops.object.select_all(action='DESELECT')
exp.select_set(True); bpy.context.view_layer.objects.active = exp
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='EDIT')
for eb in exp.data.edit_bones: eb.use_connect = False
bpy.ops.object.mode_set(mode='OBJECT')
md = mesh_obj.modifiers.new("Esqueleto", 'ARMATURE'); md.object = exp
def bake_pose(side, f):
    """Pose gravada (sem escala) -> transformação local de cada osso, relativa ao novo repouso."""
    R = rec[side][f]
    def nz(M):
        loc, rot, _ = M.decompose()
        return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()
    out = {}
    for pb in exp.pose.bones:
        b = pb.bone
        M = nz(R[b.name])
        if b.parent:
            L = (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ (nz(R[b.parent.name]).inverted() @ M)
        else:
            L = b.matrix_local.inverted() @ M
        out[b.name] = L
    return out
for pb in exp.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
hand = exp.pose.bones["smartrig:LeftHand"]
Pbone = exp.matrix_world @ hand.matrix @ Matrix.Translation((0, hand.bone.length, 0))
c_ctr = can_M.translation
c_up = can_M.to_3x3().col[2].normalized()
c_fwd = can_M.to_3x3().col[1].normalized()      # o jato sai pelo +Y local da lata
def marker(name, pos, parent_bone=True):
    e = bpy.data.objects.new(name, None); sc.collection.objects.link(e)
    e.empty_display_size = 0.01
    if parent_bone:
        e.parent = exp; e.parent_type = 'BONE'; e.parent_bone = hand.name
        e.matrix_parent_inverse = Matrix.Identity(4)
        e.matrix_basis = Pbone.inverted() @ Matrix.Translation(pos)
    else:
        e.matrix_world = Matrix.Translation(pos)
    return e
markers = [marker("Lata", c_ctr), marker("Lata_Cima", c_ctr + c_up * 0.1), marker("Lata_Frente", c_ctr + c_fwd * 0.1)]
cam_pos = cam_M.translation
cam_fwd = -(cam_M.to_3x3().col[2].normalized())
cam_up = cam_M.to_3x3().col[1].normalized()
refs = [marker("CamRef", cam_pos, False), marker("CamRef_Frente", cam_pos + cam_fwd, False), marker("CamRef_Cima", cam_pos + cam_up, False)]
bpy.context.view_layer.update()
print("CHECK marker vs can", (markers[0].matrix_world.translation - c_ctr).length)

# ---------------------------------------------------------------- ações (uma por braço e por secção)
exp.animation_data_create()
made = []
for side in ("L", "R"):
    for seg, (f0, f1) in SEGMENTS.items():
        act = bpy.data.actions.new(f"{side}_{seg}")
        act.use_fake_user = True
        exp.animation_data.action = act
        for f in range(f0, f1 + 1):
            apply_pose(bake_pose(side, f))
            for pb in exp.pose.bones:
                pb.keyframe_insert("location", frame=f - f0 + 1, group=pb.name)
                pb.keyframe_insert("rotation_quaternion", frame=f - f0 + 1, group=pb.name)
                pb.keyframe_insert("scale", frame=f - f0 + 1, group=pb.name)
        made.append(act)
exp.animation_data.action = made[0]
apply_pose(bake_pose("L", 1))

# ---------------------------------------------------------------- limpar tudo o resto e exportar
keep = {exp, mesh_obj, *markers, *refs}
for o in list(bpy.data.objects):
    if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    if a not in made: bpy.data.actions.remove(a)
sc.frame_start = 1; sc.frame_end = 80
bpy.ops.object.select_all(action='DESELECT')
for o in keep: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         apply_scale_options='FBX_SCALE_ALL', add_leaf_bones=False, use_armature_deform_only=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False)
print("EXPORTED", OUT_FBX, [a.name for a in made])
