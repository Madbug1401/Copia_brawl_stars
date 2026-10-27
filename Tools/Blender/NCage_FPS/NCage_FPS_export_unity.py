"""Exporta os braços do N.Cage em 1.ª pessoa (com o arco composto e as 3 flechas) para o Unity (FBX).

Conteúdo do FBX (um só esqueleto, NCage_FP_Rig):
- ossos dos dois braços (pose do frame 1 aplicada como repouso: as animações não têm escala nos ossos);
- osso "Arco" (o arco inteiro) e o filho "Corda" (o meio da corda: a deformação do gancho do Blender passa a ser
  pesos de pele — exatamente a mesma deformação linear);
- ossos "Flecha_Meio", "Flecha_Cima", "Flecha_Baixo" (cada flecha rígida; escadas ~0 quando escondida).
Malhas: NCage_FP_Bracos (braços + unhas), NCage_FP_Arco, NCage_FP_Flechas.
Marcadores: CamRef / CamRef_Frente / CamRef_Cima (câmara do Blender) e Ponta (filho do osso Arco: apoio da flecha,
de onde saem as flechas do jogo).
Animações (takes): Parado (1–40), Carregar (40–96), Mirar (100–110–100, em ciclo), Atirar (111–125, flechas
escondidas: no jogo voam os projéteis).
Uso: python3 nc_export_unity.py -- <saida.fbx>
"""
import bpy, sys, math
from mathutils import Vector, Matrix

OUT_FBX = sys.argv[sys.argv.index("--") + 1]
sys.argv = ["x", "--", "none"]
exec(open(__import__("os").path.join(__import__("os").path.dirname(__file__), "NCage_FPS_build.py")).read())

sc = bpy.context.scene
TAKES = {
    "Parado": list(range(1, 41)),
    "Carregar": list(range(40, 97)),
    "Mirar": list(range(100, 111)) + list(range(109, 99, -1)),
    "Atirar": list(range(111, 126)),
}
HIDE_ARROWS = {"Atirar"}

# ---------------------------------------------------------------- gravar tudo em todos os frames
names = [b.name for b in arm.data.bones]
AWi = arm.matrix_world.inverted()
rec, rec_bow, rec_hook, rec_arrow, rec_vis = {}, {}, {}, {}, {}
for f in range(1, 151):
    sc.frame_set(f)
    rec[f] = {n: arm.pose.bones[n].matrix.copy() for n in names}
    rec_bow[f] = AWi @ bow.matrix_world
    rec_hook[f] = hook.location.copy()
    rec_arrow[f] = {a.name: AWi @ a.matrix_world for a in arrows}
    rec_vis[f] = arrows[0].scale[0] > 0.5
sc.frame_set(1)
cam_M = AWi @ cam.matrix_world

def nz(M):
    loc, rot, _ = M.decompose()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()

# ---------------------------------------------------------------- esqueleto de exportação
exp_data = arm.data.copy(); exp_data.name = "NCage_FP_Rig"
for b in exp_data.bones: b.inherit_scale = 'FULL'
exp = bpy.data.objects.new("NCage_FP_Rig", exp_data); sc.collection.objects.link(exp)
exp.matrix_world = arm.matrix_world.copy()
bpy.context.view_layer.update()

# ---------------------------------------------------------------- malha dos braços (suavização nível 1) + unhas
arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
mesh_obj = body.copy(); mesh_obj.data = body.data.copy(); mesh_obj.name = "NCage_FP_Bracos"
sc.collection.objects.link(mesh_obj)
for md in list(mesh_obj.modifiers):
    if md.type == 'ARMATURE': mesh_obj.modifiers.remove(md)
for md in mesh_obj.modifiers:
    if md.type == 'SUBSURF': md.levels = 1
bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True); bpy.context.view_layer.objects.active = mesh_obj
mw = mesh_obj.matrix_world.copy(); mesh_obj.parent = None; mesh_obj.matrix_world = mw
for md in list(mesh_obj.modifiers): bpy.ops.object.modifier_apply(modifier=md.name)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
# unhas: forma na pose de repouso (Surface Deform ligado em repouso = forma original) e pesos copiados da pele
dg = bpy.context.evaluated_depsgraph_get()
nails = []
for n in NAILS:
    me = bpy.data.meshes.new_from_object(n.evaluated_get(dg))
    me.transform(n.matrix_world)
    c = bpy.data.objects.new(n.name + "_exp", me); sc.collection.objects.link(c)
    for g in mesh_obj.vertex_groups: c.vertex_groups.new(name=g.name)
    nails.append(c)
from mathutils.bvhtree import BVHTree
bm_tris = [p.vertices[:] for p in mesh_obj.data.polygons]
bvh = BVHTree.FromPolygons([v.co for v in mesh_obj.data.vertices], bm_tris)
vg_names = {g.index: g.name for g in mesh_obj.vertex_groups}
def weights_at(co):
    """Pesos interpolados na face mais próxima da pele (média pesada pelos inversos das distâncias aos vértices)."""
    loc, nrm, idx, d = bvh.find_nearest(co)
    poly = mesh_obj.data.polygons[idx]
    acc = {}; tot = 0.0
    for vi in poly.vertices:
        v = mesh_obj.data.vertices[vi]
        wv = 1.0 / max(1e-5, (v.co - loc).length)
        tot += wv
        for g in v.groups: acc[g.group] = acc.get(g.group, 0.0) + g.weight * wv
    return {vg_names[k]: w / tot for k, w in acc.items() if w / tot > 1e-3}
for c in nails:
    for v in c.data.vertices:
        for gname, w in weights_at(v.co).items():
            c.vertex_groups[gname].add([v.index], w, 'REPLACE')
bpy.ops.object.select_all(action='DESELECT')
for c in nails: c.select_set(True)
mesh_obj.select_set(True); bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.join()
mesh_obj.parent = exp; mesh_obj.matrix_parent_inverse = exp.matrix_world.inverted()
md = mesh_obj.modifiers.new("Esqueleto", 'ARMATURE'); md.object = exp
arm.data.pose_position = 'POSE'
print("BRACOS verts", len(mesh_obj.data.vertices), "mats", [m.name for m in mesh_obj.data.materials])

# ---------------------------------------------------------------- pose do frame 1 passa a ser o repouso
def apply_pose(P):
    for pb in exp.pose.bones:
        if pb.name not in P: continue
        loc, rot, scl = P[pb.name].decompose()
        pb.rotation_mode = 'QUATERNION'
        pb.location = loc; pb.rotation_quaternion = rot; pb.scale = scl
def bake_arm(f, strip):
    R = rec[f]; out = {}
    for pb in exp.pose.bones:
        b = pb.bone
        if b.name not in R: continue
        M = nz(R[b.name]) if strip else R[b.name]
        if b.parent and b.parent.name in R:
            Mp = nz(R[b.parent.name]) if strip else R[b.parent.name]
            out[b.name] = (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ (Mp.inverted() @ M)
        else:
            out[b.name] = b.matrix_local.inverted() @ M
    return out
apply_pose(bake_arm(1, False)); bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True); bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.object.modifier_apply(modifier="Esqueleto")
bpy.ops.object.select_all(action='DESELECT')
exp.select_set(True); bpy.context.view_layer.objects.active = exp
bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode='EDIT')
for eb in exp.data.edit_bones: eb.use_connect = False

# ---------------------------------------------------------------- ossos do arco, corda e flechas (repouso = frame 1 / 100)
EW = exp.matrix_world.inverted()
bow_rest = nz(rec_bow[1])
cord_rest = bow_rest @ Matrix.Translation(NOCK_REST)
arrow_rest = {a.name: nz(rec_arrow[100][a.name]) for a in arrows}
def new_bone(name, M, parent=None, length=0.08):
    eb = exp.data.edit_bones.new(name)
    eb.head = (0, 0, 0); eb.tail = (0, length, 0)
    eb.matrix = M
    if parent: eb.parent = exp.data.edit_bones[parent]
    eb.use_deform = True
    return eb
new_bone("Arco", bow_rest, length=0.12)
new_bone("Corda", cord_rest, "Arco", 0.04)
for a in arrows: new_bone(a.name, arrow_rest[a.name], length=0.1)
bpy.ops.object.mode_set(mode='OBJECT')
for pb in exp.pose.bones:
    pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
md = mesh_obj.modifiers.new("Esqueleto", 'ARMATURE'); md.object = exp

# malha do arco: corda pesada entre "Arco" e "Corda" como o gancho (queda linear a partir do nó)
bow_mesh = bow.data.copy(); bow_mesh.name = "NCage_FP_Arco"
bo = bpy.data.objects.new("NCage_FP_Arco", bow_mesh); sc.collection.objects.link(bo)
# (a malha copiada ainda traz a pertença ao grupo antigo "Corda", índice 0: o 1.º grupo criado ocupa esse índice)
src_corda = bow.vertex_groups["Corda"].index
assert src_corda == 0 and len(bow.vertex_groups) == 1
g_corda = bo.vertex_groups.new(name="Corda"); g_arco = bo.vertex_groups.new(name="Arco")
for v in bow_mesh.vertices:
    w = 0.0
    for g in v.groups:
        if g.group == 0:
            w = g.weight * max(0.0, 1.0 - (v.co - Vector(NOCK_REST)).length / STRING_HALF)
    if w > 0.0: g_corda.add([v.index], w, 'REPLACE')
    else: g_corda.remove([v.index])
    if w < 1.0: g_arco.add([v.index], 1.0 - w, 'REPLACE')
bow_mesh.transform(bow_rest)
bo.parent = exp
mdb = bo.modifiers.new("Esqueleto", 'ARMATURE'); mdb.object = exp

# flechas (rígidas, uma por osso)
arrow_objs = []
for a in arrows:
    me = a.data.copy(); me.transform(arrow_rest[a.name])
    o = bpy.data.objects.new(a.name + "_exp", me); sc.collection.objects.link(o)
    o.vertex_groups.new(name=a.name).add([v.index for v in me.vertices], 1.0, 'REPLACE')
    arrow_objs.append(o)
bpy.ops.object.select_all(action='DESELECT')
for o in arrow_objs: o.select_set(True)
bpy.context.view_layer.objects.active = arrow_objs[0]
bpy.ops.object.join()
fo = arrow_objs[0]; fo.name = "NCage_FP_Flechas"; fo.data.name = "NCage_FP_Flechas"
fo.parent = exp
mdf = fo.modifiers.new("Esqueleto", 'ARMATURE'); mdf.object = exp

# ---------------------------------------------------------------- marcadores
def marker(name, pos, bone=None):
    e = bpy.data.objects.new(name, None); sc.collection.objects.link(e)
    e.empty_display_size = 0.01
    if bone:
        e.parent = exp; e.parent_type = 'BONE'; e.parent_bone = bone
        pb = exp.pose.bones[bone]
        Pbone = exp.matrix_world @ pb.matrix @ Matrix.Translation((0, pb.bone.length, 0))
        e.matrix_parent_inverse = Matrix.Identity(4)
        e.matrix_basis = Pbone.inverted() @ Matrix.Translation(pos)
    else:
        e.matrix_world = Matrix.Translation(pos)
    return e
bpy.context.view_layer.update()
markers = [marker("Ponta", exp.matrix_world @ (bow_rest @ Vector(REST)), "Arco")]
cam_pos = cam_M.translation
cam_fwd = -(cam_M.to_3x3().col[2].normalized()); cam_up = cam_M.to_3x3().col[1].normalized()
markers += [marker("CamRef", cam_pos), marker("CamRef_Frente", cam_pos + cam_fwd), marker("CamRef_Cima", cam_pos + cam_up)]

# ---------------------------------------------------------------- poses por frame
def bake_all(f, hide_arrows):
    P = bake_arm(f, True)
    def rootL(name, M):
        return exp.data.bones[name].matrix_local.inverted() @ M
    B = nz(rec_bow[f])
    P["Arco"] = rootL("Arco", B)
    C = B @ Matrix.Translation(rec_hook[f])
    b = exp.data.bones["Corda"]
    P["Corda"] = (b.parent.matrix_local.inverted() @ b.matrix_local).inverted() @ (B.inverted() @ C)
    vis = rec_vis[f] and not hide_arrows
    for a in arrows:
        M = nz(rec_arrow[f][a.name]) if vis else nz(rec_arrow[100][a.name])
        L = rootL(a.name, M)
        if not vis: L = L @ Matrix.Diagonal((1e-4, 1e-4, 1e-4, 1.0))
        P[a.name] = L
    return P

exp.animation_data_create()
made = []
for take, frames in TAKES.items():
    act = bpy.data.actions.new(take); act.use_fake_user = True
    exp.animation_data.action = act
    for i, f in enumerate(frames):
        apply_pose(bake_all(f, take in HIDE_ARROWS))
        for pb in exp.pose.bones:
            for p in ("location", "rotation_quaternion", "scale"):
                pb.keyframe_insert(p, frame=i + 1, group=pb.name)
    made.append(act)
exp.animation_data.action = made[0]
apply_pose(bake_all(1, False))
bpy.context.view_layer.update()
# verificação: o arco e a mão esquerda batem certo com a cena original no frame 100
exp.animation_data.action = bpy.data.actions["Mirar"]; sc.frame_set(1); bpy.context.view_layer.update()
print("CHK arco", round((exp.matrix_world @ exp.pose.bones["Arco"].head - (arm.matrix_world @ rec_bow[100]).translation).length, 5))
print("CHK mao", round((exp.matrix_world @ exp.pose.bones["smartrig:LeftHand"].head - arm.matrix_world @ rec[100]["smartrig:LeftHand"].translation).length, 5))
exp.animation_data.action = made[0]

# ---------------------------------------------------------------- limpar e exportar
keep = {exp, mesh_obj, bo, fo, *markers}
for o in list(bpy.data.objects):
    if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    if a not in made: bpy.data.actions.remove(a)
sc.frame_start = 1; sc.frame_end = 60
bpy.ops.object.select_all(action='DESELECT')
for o in keep: o.select_set(True)
bpy.ops.export_scene.fbx(filepath=OUT_FBX, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                         apply_scale_options='FBX_SCALE_ALL', add_leaf_bones=False, use_armature_deform_only=False,
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                         mesh_smooth_type='FACE', path_mode='STRIP', embed_textures=False)
print("EXPORTED", OUT_FBX, [a.name for a in made])
