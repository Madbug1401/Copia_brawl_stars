"""N.Cage em 1.ª pessoa com o arco composto e a flecha (Blender).

Animação (30 fps):
  1–40    Parado        arco em baixo, sem flecha na corda
  41–78   Carregar      a mão direita tira uma flecha da aljava (à anca) e encaixa-a na corda
  79–95   Puxar         o arco sobe para a mira e a mão direita puxa a corda até à cara
  96–110  Mirar         segura a corda puxada (tremor a aumentar)
  111–125 Atirar        larga: a corda volta (vibra), a flecha sai disparada, a mão recua e abre
  126–150 Parado        volta à pose de descanso
Uso: python3 nc_build.py -- [still|frames|anim|save <ficheiro.blend>]
"""
import bpy, math, sys, os
from mathutils import Vector, Matrix, Euler, Quaternion

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
MODE = ARGS[0] if ARGS else "still"
G = os.environ.get("GJ_MODELS", "/mnt/user-data/uploads/GameJam/3d models/")
CHAR = G + "characters/n cage character.fbx"
BOW = G + "arco/base.fbx"
ARROW = G + "flecha/base.fbx"
OUT = os.environ.get("NC_OUT", "/tmp/claude-0/nc/out/")

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30

# ---------------------------------------------------------------- personagem: só os braços (camisola de alças: braços nus)
bpy.ops.import_scene.fbx(filepath=CHAR)
arm = bpy.data.objects["SmartRigArmature"]; arm.name = "NCage_Rig"
body = bpy.data.objects["model"]; body.name = "NCage_Bracos"
import bmesh
gi = {vg.index for vg in body.vertex_groups if any(k in vg.name for k in ("LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand"))}
me = body.data
bm = bmesh.new(); bm.from_mesh(me)
dl = bm.verts.layers.deform.active
kill = [v for v in bm.verts if (sum(val for g, val in v[dl].items() if g in gi) if dl else 0) < 0.5]
bmesh.ops.delete(bm, geom=kill, context='VERTS')
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)       # costuras de UV separadas no FBX (evita rachas)
bm.to_mesh(me); bm.free()

# material com luz realista (a textura vinha ligada à emissão)
for _m in body.data.materials:
    _nt = _m.node_tree; _b = next(n for n in _nt.nodes if n.bl_idname == "ShaderNodeBsdfPrincipled")
    _tex = next((n for n in _nt.nodes if n.bl_idname == "ShaderNodeTexImage"), None)
    for _l in list(_nt.links):
        if _l.to_socket == _b.inputs["Emission Color"]: _nt.links.remove(_l)
    _b.inputs["Emission Strength"].default_value = 0.0
    if _tex: _nt.links.new(_tex.outputs["Color"], _b.inputs["Base Color"])
    _b.inputs["Roughness"].default_value = 0.6
    _b.inputs["Specular IOR Level"].default_value = 0.35
    _b.inputs["Subsurface Weight"].default_value = 0.0      # sem translucidez: a mão ficava mais clara do que o braço
    _b.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
    _b.inputs["Subsurface Scale"].default_value = 0.006
    # relevo fino da pele: poros e pequenas rugas (a malha suavizada ficava com ar de borracha)
    _tc = _nt.nodes.new("ShaderNodeTexCoord")
    _nz = _nt.nodes.new("ShaderNodeTexNoise"); _nz.inputs["Scale"].default_value = 900.0; _nz.inputs["Detail"].default_value = 4.0
    _nz2 = _nt.nodes.new("ShaderNodeTexNoise"); _nz2.inputs["Scale"].default_value = 140.0; _nz2.inputs["Detail"].default_value = 2.0
    _mx = _nt.nodes.new("ShaderNodeMath"); _mx.operation = 'ADD'
    _bp = _nt.nodes.new("ShaderNodeBump"); _bp.inputs["Strength"].default_value = 0.12; _bp.inputs["Distance"].default_value = 0.0006
    _nt.links.new(_tc.outputs["UV"], _nz.inputs["Vector"]); _nt.links.new(_tc.outputs["UV"], _nz2.inputs["Vector"])
    _nt.links.new(_nz.outputs[0], _mx.inputs[0]); _nt.links.new(_nz2.outputs[0], _mx.inputs[1])
    _nt.links.new(_mx.outputs[0], _bp.inputs["Height"])
    _nmap = next((n for n in _nt.nodes if n.bl_idname == "ShaderNodeNormalMap"), None)
    if _nmap is not None and _nmap.outputs[0].is_linked and _nmap.inputs["Color"].is_linked:
        _nt.links.new(_nmap.outputs[0], _bp.inputs["Normal"])
    _nt.links.new(_bp.outputs[0], _b.inputs["Normal"])
sub = body.modifiers.new("Suavizar", 'SUBSURF'); sub.levels = 2; sub.render_levels = 2   # iguais: as unhas estão presas à superfície suavizada
bpy.ops.object.select_all(action='DESELECT'); body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.shade_smooth()

# ---------------------------------------------------------------- unhas (moldadas aos dedos e presas ao esqueleto)
from mathutils import Matrix
def nail_material():
    m = bpy.data.materials.new("NCage_Unha"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.35
    b.inputs["Specular IOR Level"].default_value = 0.4
    try:
        b.inputs["Coat Weight"].default_value = 0.35; b.inputs["Coat Roughness"].default_value = 0.18   # brilho natural da unha
    except Exception: pass
    tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY_ADD'
    mul.inputs[1].default_value = 0.5; mul.inputs[2].default_value = 0.5    # y local (-1..1) -> 0..1
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    # do fundo (lúnula clara) à ponta (bordo livre esbranquiçado), corpo rosado-acastanhado
    e = ramp.color_ramp.elements
    e[0].position = 0.0; e[0].color = (0.20, 0.08, 0.065, 1)    # cutícula (junto à pele)
    e.new(0.07).color = (0.55, 0.34, 0.30, 1)                    # lúnula (meia-lua clara)
    e.new(0.24).color = (0.42, 0.20, 0.17, 1)                    # corpo da unha (rosado)
    e.new(0.80).color = (0.45, 0.22, 0.18, 1)
    e.new(0.87).color = (0.78, 0.66, 0.56, 1)                    # bordo livre esbranquiçado
    e[-1].position = 1.0; e[-1].color = (0.82, 0.71, 0.60, 1)
    mr = nt.nodes.new("ShaderNodeMapRange")       # y local da unha (-1 fundo .. 1 ponta) -> 0..1
    mr.inputs["From Min"].default_value = 0.0; mr.inputs["From Max"].default_value = 1.0
    nt.nodes.remove(mr)                         # o atributo já vai de 0 a 1
    nt.links.new(tc.outputs["UV"], sep.inputs[0]); nt.links.new(sep.outputs[1], ramp.inputs[0]); nt.links.new(ramp.outputs[0], b.inputs["Base Color"])
    return m
NAIL_MAT = nail_material()

from mathutils.bvhtree import BVHTree
arm.data.pose_position = 'REST'; bpy.context.view_layer.update()
_dg = bpy.context.evaluated_depsgraph_get()
_ev = body.evaluated_get(_dg); _me = _ev.to_mesh()
_bvh = BVHTree.FromPolygons([body.matrix_world @ v.co for v in _me.vertices], [p.vertices[:] for p in _me.polygons])
_ev.to_mesh_clear()
from mathutils.kdtree import KDTree
_kd = KDTree(len(body.data.vertices))
for _v in body.data.vertices: _kd.insert(body.matrix_world @ _v.co, _v.index)
_kd.balance()
_vgn = {g.index: g.name for g in body.vertex_groups}
NAILS = []
def o_tmp(): return bpy.context.active_object
def make_nail(side, finger):
    b3 = arm.data.bones.get(f"smartrig:{side}Hand{finger}3"); b4 = arm.data.bones.get(f"smartrig:{side}Hand{finger}4")
    if b3 is None or b4 is None: return
    W = arm.matrix_world
    k = W @ b3.head_local; tip0 = W @ b4.head_local
    ax = (tip0 - k).normalized()
    # dorso do dedo = contrário do sentido em que a ponta se mexe quando o dedo dobra (rotação +X do osso)
    X3 = (W.to_3x3() @ b3.matrix_local.to_3x3()).col[0].normalized()
    moved = X3.cross(tip0 - k)
    back = -(moved - ax * moved.dot(ax)).normalized()
    side_ax = ax.cross(back).normalized()
    # raios lançados de DENTRO do dedo (o osso passa por dentro): o primeiro toque é a pele do próprio dedo,
    # nunca a do dedo vizinho
    def out(o_, d_):
        h_ = _bvh.ray_cast(o_ + d_ * 0.0005, d_, 0.05)
        return h_[0], h_[1]
    end, _ = out(tip0, ax)
    if end is None: return
    tlen = (end - k).dot(ax)
    q = k + ax * (tlen - 0.0105)
    pl, _ = out(q, side_ax); pr, _ = out(q, -side_ax)
    width = min((pl - pr).length, 0.03) if (pl and pr) else 0.016
    if pl and pr: q = q + side_ax * ((pl + pr) * 0.5 - q).dot(side_ax)
    length = width * (0.70 if finger == "Thumb" else 0.66)
    q = q + ax * (tlen - length * 0.5 - 0.002 - (q - k).dot(ax))
    p, n = out(q, back)
    if p is None: return
    n = n.normalized()
    if n.dot(back) < 0: n = -n
    n = (n + back).normalized()
    bpy.ops.mesh.primitive_circle_add(vertices=32, radius=1.0, fill_type='TRIFAN')
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.subdivide(number_cuts=3); bpy.ops.object.mode_set(mode='OBJECT')
    for v in o_tmp().data.vertices:          # forma de unha: lados quase retos, ponta e base arredondadas
        x, y = v.co.x, v.co.y
        v.co.x = x * (1.0 - 0.10 * (y + 1.0) * 0.5) * 0.92
        v.co.y = y
    o = bpy.context.active_object; o.name = f"Unha_{side}_{finger}"
    o.data.materials.append(NAIL_MAT)
    at = o.data.attributes.new("unha_v", 'FLOAT', 'POINT')      # 0 = base da unha, 1 = ponta
    for v in o.data.vertices: at.data[v.index].value = v.co.y * 0.5 + 0.5
    bpy.ops.object.shade_smooth()
    y = (ax - n * ax.dot(n)).normalized(); x = y.cross(n).normalized()
    R = Matrix((x, y, n)).transposed().to_4x4()
    Mw = Matrix.Translation(p + n * 0.001) @ R @ Matrix.Diagonal((width * 0.56 * 0.5, length * 0.5, 1.0, 1.0))
    # a unha passa a fazer parte da "pele": cada vértice copia os pesos do vértice do braço mais próximo
    # e é deformada pelo mesmo esqueleto (segue o dedo exatamente)
    o.matrix_world = Mw
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    # degradê base -> ponta guardado num mapa UV; depois aplica-se a transformação (objeto sem escala não uniforme,
    # para o esqueleto o deformar exatamente como a pele)
    uvl = o.data.uv_layers.new(name="UnhaUV")
    for poly in o.data.polygons:
        for li in poly.loop_indices:
            co = o.data.vertices[o.data.loops[li].vertex_index].co
            uvl.data[li].uv = (co.x * 0.5 + 0.5, co.y * 0.5 + 0.5)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    # molda a unha à pele do dedo (malha suavizada, pose de repouso) e dá-lhe espessura
    sw = o.modifiers.new("Moldar", 'SHRINKWRAP'); sw.target = body; sw.wrap_method = 'NEAREST_SURFACEPOINT'
    sw.wrap_mode = 'OUTSIDE_SURFACE'; sw.offset = 0.00035
    bpy.ops.object.modifier_apply(modifier=sw.name)
    so = o.modifiers.new("Espessura", 'SOLIDIFY'); so.thickness = 0.0007; so.offset = 1.0
    bpy.ops.object.modifier_apply(modifier=so.name)
    o.data.update()
    for v in o.data.vertices:
        i = _kd.find(o.matrix_world @ v.co)[1]
        for g in body.data.vertices[i].groups:
            nm = _vgn[g.group]
            vg_ = o.vertex_groups.get(nm) or o.vertex_groups.new(name=nm)
            vg_.add([v.index], g.weight, 'REPLACE')
    md = o.modifiers.new("Esqueleto", 'ARMATURE'); md.object = arm
    Mw_keep = o.matrix_world.copy()
    o.parent = arm; o.matrix_parent_inverse = arm.matrix_world.inverted(); o.matrix_world = Mw_keep
    NAILS.append(o)
for side in ("Right", "Left"):
    for f in ("Thumb", "Index", "Middle", "Ring", "Pinky"): make_nail(side, f)
# as unhas ficam "coladas" à superfície da pele (Surface Deform ligado na pose de repouso): seguem cada dobra do dedo
# exatamente como a pele, sem flutuar nem atravessar
bpy.context.view_layer.update()
for o in NAILS:
    o.modifiers.remove(o.modifiers["Esqueleto"])
    o.vertex_groups.clear()
    sd = o.modifiers.new("Colar_Pele", 'SURFACE_DEFORM'); sd.target = body; sd.falloff = 4.0
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.surfacedeform_bind(modifier=sd.name)
    if not sd.is_bound: print("AVISO unha sem ligação", o.name)
arm.data.pose_position = 'POSE'
print("unhas", len(NAILS))


# ---------------------------------------------------------------- arco e flecha com textura PBR
def pbr(folder, name, emissive=False):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    def tex(f, cs="sRGB"):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(G + folder + "/" + f); t.image.colorspace_settings.name = cs; return t
    nt.links.new(tex("texture_diffuse.png").outputs[0], b.inputs["Base Color"])
    nt.links.new(tex("texture_roughness.png", "Non-Color").outputs[0], b.inputs["Roughness"])
    nt.links.new(tex("texture_metallic.png", "Non-Color").outputs[0], b.inputs["Metallic"])
    n = tex("texture_normal.png", "Non-Color"); nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(n.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], b.inputs["Normal"])
    if emissive and os.path.exists(G + folder + "/texture_emissive.png"):
        e = tex("texture_emissive.png"); nt.links.new(e.outputs[0], b.inputs["Emission Color"]); b.inputs["Emission Strength"].default_value = 1.0
    return m

def import_mesh(path, name, mat):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    o = [x for x in bpy.data.objects if x not in before and x.type == 'MESH'][0]
    o.name = name; o.data.materials.clear(); o.data.materials.append(mat)
    return o

BOW_LEN = 1.0          # arco composto com ~1 m de eixo a eixo
ARROW_LEN = 0.75
bow = import_mesh(BOW, "Arco", pbr("arco", "Arco_Mat", emissive=True))
arrow = import_mesh(ARROW, "Flecha", pbr("flecha", "Flecha_Mat"))
SB = BOW_LEN / 1.9; SA = ARROW_LEN / 1.9
for o, s in ((bow, (SB, SB, SB)), (arrow, (SA * 0.6, SA * 0.6, SA))):   # flecha mais fina: penas e haste com proporções reais
    o.scale = s; bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.transform_apply(scale=True)
print("FLECHA bbox", [round(min(v.co[i] for v in arrow.data.vertices),4) for i in range(3)], [round(max(v.co[i] for v in arrow.data.vertices),4) for i in range(3)])
# o N.Cage dispara 3 flechas de uma vez: duas cópias (mesma malha) encaixadas na corda por cima e por baixo da do meio
arrow.name = "Flecha_Meio"
arrows = [arrow]
for nm in ("Flecha_Cima", "Flecha_Baixo"):
    a2 = arrow.copy(); a2.name = nm; sc.collection.objects.link(a2); arrows.append(a2)
ARROW_OFF = {"Flecha_Meio": 0, "Flecha_Cima": 1, "Flecha_Baixo": -1}
ARROW_GAP = float(os.environ.get("AGAP", "0.035"))
ARROW_ROLL = float(os.environ.get("AROLL", "40"))     # distância entre flechas na corda (m)
FAN_NOCKED = float(os.environ.get("AFAN", "2.5")); FAN_FLY = 4.0                         # abertura em leque (graus) encaixadas / em voo

# pontos do arco (espaço local, em metros): punho, apoio da flecha, corda (a meio, em repouso)
GRIP = Vector((0.0, -0.05, 0.86)) * SB
REST = Vector((-0.045, -0.03, 1.0)) * SB          # lado do arco virado para o centro do ecrã
STRING_Y = 0.36 * SB
NOCK_REST = Vector((REST.x, STRING_Y, REST.z))    # ponto da corda onde encaixa a flecha
STRING_HALF = 0.70 * SB
# grupo de vértices da corda (a linha mais atrás, entre as roldanas)
vg = bow.vertex_groups.new(name="Corda")
vg.add([v.index for v in bow.data.vertices if v.co.y > 0.32 * SB and 0.22 * SB < v.co.z < 1.68 * SB], 1.0, 'REPLACE')
print("corda verts", sum(1 for v in bow.data.vertices if v.groups))
bow.data.update()

# ---------------------------------------------------------------- câmara 1.ª pessoa
cam = bpy.data.objects.new("FPS_Camera", bpy.data.cameras.new("FPS_Camera"))
sc.collection.objects.link(cam); sc.camera = cam
EYE = Vector((0.0, -0.08, 1.47))
cam.location = EYE
cam.rotation_euler = Euler((math.radians(86), 0, math.radians(180)))
cam.data.lens = 18; cam.data.clip_start = 0.01
root = bpy.data.objects.new("FP_Root", None); sc.collection.objects.link(root); root.parent = cam
bpy.context.view_layer.update()
CAMW = cam.matrix_world.copy()
def C(v):  # câmara -> mundo (ponto)
    return CAMW @ Vector(v)
def CD(v):  # câmara -> mundo (direção)
    return (CAMW.to_3x3() @ Vector(v)).normalized()

def empty(name, size=0.04, parent=root):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = size
    sc.collection.objects.link(e); e.parent = parent; return e
tL = empty("Alvo_Mao_Esquerda"); tR = empty("Alvo_Mao_Direita")
pL = empty("Cotovelo_Esquerdo"); pR = empty("Cotovelo_Direito")

# ---------------------------------------------------------------- IK dos braços
pb = arm.pose.bones
def ik(side, t, pole):
    b = pb[f"smartrig:{side}ForeArm"]
    c = b.constraints.new('IK'); c.target = t; c.pole_target = pole; c.chain_count = 2
    c.pole_angle = math.radians(-90); c.use_tail = True; c.use_stretch = True
    cr = pb[f"smartrig:{side}Hand"].constraints.new('COPY_ROTATION'); cr.target = t
ik("Left", tL, pL); ik("Right", tR, pR)
ARM_SCALE = {"Left": float(os.environ.get("ASL", "1.2")), "Right": float(os.environ.get("ASR", "1.12"))}
ARM_STRETCH = {"Left": 0.4, "Right": 0.05}
for side in ("Right", "Left"):
    # braços do boneco são curtos (cartoon): em vez de os esticar (o IK afinava o antebraço), aumenta-se o braço
    # inteiro por igual (braço + antebraço), e o IK só estica um pouco o que faltar
    pb[f"smartrig:{side}Arm"].scale = (ARM_SCALE[side],) * 3
    for bn in (f"smartrig:{side}Arm", f"smartrig:{side}ForeArm"): pb[bn].ik_stretch = ARM_STRETCH[side]
    arm.data.bones[f"smartrig:{side}Hand"].inherit_scale = 'NONE'

# palma no espaço local do osso da mão (medida na T-pose, palmas para baixo)
PALM_LOCAL = {"R": (-0.83, 0.56), "L": (0.86, 0.51)}
def hand_matrix(side, wrist, fwd, palm):
    """Matriz do alvo da mão: dedos (y do osso) segundo 'fwd' e palma virada para 'palm' (mundo)."""
    y = fwd.normalized()
    P = (palm - y * palm.dot(y)).normalized()
    v = y.cross(P)
    px, pz = PALM_LOCAL[side]; n = math.hypot(px, pz); px /= n; pz /= n
    x = px * P + pz * v; z = pz * P - px * v
    M = Matrix((x, y, z)).transposed().to_4x4(); M.translation = wrist
    return M

# ---------------------------------------------------------------- dedos
def finger(side, f, a):
    for i, ang in enumerate(a, 1):
        b = pb.get(f"smartrig:{side}Hand{f}{i}")
        if b: b.rotation_mode = 'XYZ'; b.rotation_euler = Euler((math.radians(ang), 0, 0))
def thumb(side, a, spread=0.0):
    for i, ang in enumerate(a, 1):
        b = pb.get(f"smartrig:{side}HandThumb{i}")
        if b:
            b.rotation_mode = 'XYZ'
            z = math.radians(spread if side == "Left" else -spread) if i == 1 else 0.0
            b.rotation_euler = Euler((math.radians(ang), 0, z))
HANDS = {
    # punho do arco (mão esquerda), dedos à volta do punho
    "grip":    {"Index": (40, 60, 45), "Middle": (65, 65, 45), "Ring": (70, 65, 45), "Pinky": (75, 60, 40), "Thumb": (10, 30, 25, 40)},
    "gripsoft": {"Index": (25, 40, 30), "Middle": (50, 50, 35), "Ring": (58, 52, 35), "Pinky": (65, 50, 32), "Thumb": (8, 22, 18, 40)},
    # mão direita relaxada / a segurar a flecha / dedos na corda / aberta depois de largar
    "relax":   {"Index": (12, 18, 12), "Middle": (16, 22, 14), "Ring": (20, 25, 15), "Pinky": (24, 25, 15), "Thumb": (5, 10, 10, 20)},
    "pinch":   {"Index": (35, 45, 30), "Middle": (55, 60, 40), "Ring": (70, 70, 45), "Pinky": (75, 70, 45), "Thumb": (25, 30, 30, 50)},
    "hook":    {"Index": (8, 62, 38), "Middle": (6, 66, 40), "Ring": (12, 64, 38), "Pinky": (60, 75, 50), "Thumb": (30, 40, 30, 35)},
    "open":    {"Index": (-5, 5, 5), "Middle": (-5, 5, 5), "Ring": (0, 8, 5), "Pinky": (5, 10, 8), "Thumb": (-5, 0, 5, 30)},
}
def set_hand(side, pose_a, pose_b=None, k=0.0):
    A = HANDS[pose_a]; B = HANDS[pose_b] if pose_b else A
    for f in ("Index", "Middle", "Ring", "Pinky"):
        finger(side, f, [a + (b - a) * k for a, b in zip(A[f], B[f])])
    ta = A["Thumb"]; tb = B["Thumb"]
    tv = [a + (b - a) * k for a, b in zip(ta, tb)]
    thumb(side, tv[:3], tv[3])

# ---------------------------------------------------------------- pontos da mão medidos no próprio modelo
def hand_rel(side, pose, kind):
    """Ponto da mão (no referencial do alvo da mão, sem escala): 'corda' = dobra das últimas falanges do
    indicador, médio e anelar (onde a corda assenta); 'punho' = centro da palma + raio do punho do arco."""
    set_hand(side, pose); bpy.context.view_layer.update()
    W = arm.matrix_world
    h = pb[f"smartrig:{side}Hand"]; Hm = W @ h.matrix
    R = Hm.to_3x3().normalized(); head = Hm.translation
    def P(n): return W @ pb[f"smartrig:{side}Hand{n}"].head
    if kind == "corda":
        pt = (P("Index3") + P("Middle3") + P("Ring3")) / 3.0
        pt = pt.lerp((P("Index2") + P("Middle2") + P("Ring2")) / 3.0, 0.35)
    else:
        knuck = (P("Index1") + P("Middle1") + P("Ring1") + P("Pinky1")) / 4.0
        pt = head.lerp(knuck, 0.55)
        px, pz = PALM_LOCAL["L" if side == "Left" else "R"]
        pt = pt + R @ (Vector((px, 0, pz)).normalized() * 0.026)
    return R.inverted() @ (pt - head)
REL_STRING = hand_rel("Right", "hook", "corda")
REL_GRIP = hand_rel("Left", "grip", "punho")
print("REL_STRING", [round(x, 3) for x in REL_STRING], "REL_GRIP", [round(x, 3) for x in REL_GRIP])
def wrist_for(side, point_w, fwd, palm, rel):
    """Matriz do alvo da mão para que o ponto 'rel' da mão fique em point_w."""
    M = hand_matrix(side, Vector((0, 0, 0)), fwd, palm)
    M.translation = point_w - M.to_3x3() @ rel
    return M

# ---------------------------------------------------------------- corda: gancho no meio da corda
hook = bpy.data.objects.new("Corda_Gancho", None); hook.empty_display_type = 'SPHERE'; hook.empty_display_size = 0.01
sc.collection.objects.link(hook); hook.parent = bow; hook.location = NOCK_REST
hm = bow.modifiers.new("Puxar_Corda", 'HOOK'); hm.object = hook; hm.vertex_group = "Corda"
hm.center = NOCK_REST; hm.falloff_type = 'LINEAR'; hm.falloff_radius = STRING_HALF
hm.matrix_inverse = Matrix.Translation(NOCK_REST).inverted()

# ---------------------------------------------------------------- poses-chave (espaço da câmara: x direita, y cima, -z frente)
def lerp(a, b, k): return a + (b - a) * k
def ease(k): k = max(0.0, min(1.0, k)); return k * k * (3 - 2 * k)
POSES = {
    #          punho do arco        inclinação  pulso direito / nó da flecha         dedos dir.   flecha
    "idle":  dict(grip=(-0.20, -0.17, -0.50), cant=-22, pitch=5,  r=(0.30, -0.47, -0.30), rf=(-0.10, 0.45, -1), rh="relax"),
    "fetch": dict(grip=(-0.19, -0.16, -0.52), cant=-18, pitch=4,  r=(0.36, -0.66, -0.05),  rf=(-0.2, -0.6, -0.6), rh="pinch"),
    "carry": dict(grip=(-0.14, -0.24, -0.50), cant=-18, pitch=10,  r=(0.22, -0.30, -0.42), rf=(-0.4, 0.15, -1),  rh="pinch"),
    "nock":  dict(grip=(-0.12, -0.26, -0.50), cant=-18, pitch=12,  r=None,                 rf=None,              rh="hook", draw=0.0),
    "aim":   dict(grip=(-0.035, -0.07, -0.60), cant=-5,  pitch=0,  r=None,                 rf=None,              rh="hook", draw=1.0),
}
ANCHOR = Vector(tuple(float(x) for x in os.environ.get("ANC", "0.15,-0.20,-0.34").split(",")))
PALM_DRAW = Vector((float(os.environ.get("PDX", "-0.85")), float(os.environ.get("PDY", "-0.5")), 0.0))   # palma para a cara e um pouco para baixo: os nós dos dedos ficam à vista   # nó da flecha com a corda toda puxada (junto à cara)

BOW_YAW = float(os.environ.get('BOWYAW', '15'))
FWDL_X = float(os.environ.get('FWDLX', '0.65'))
def bow_matrix(grip_c, cant, pitch):
    """Arco: +Z local = cima, -Y local = frente (para onde sai a flecha). O punho fica em grip_c (câmara)."""
    base = Matrix(((-1, 0, 0), (0, 0, 1), (0, 1, 0))).transposed()   # colunas: X→-x, Y→+z(trás), Z→+y
    R = Matrix.Rotation(math.radians(cant), 3, 'Z') @ Matrix.Rotation(math.radians(pitch), 3, 'X') @ Matrix.Rotation(math.radians(BOW_YAW), 3, 'Y') @ base
    Mc = R.to_4x4(); Mc.translation = Vector(grip_c) - R @ GRIP
    return CAMW @ Mc

def sample(f):
    """Estado de tudo no frame f (mundo)."""
    s = {}
    def seg(a, b, f0, f1):
        return a, b, ease((f - f0) / (f1 - f0))
    if f <= 40:   A, B, k = "idle", "idle", 0.0
    elif f <= 52: A, B, k = seg("idle", "fetch", 40, 52)
    elif f <= 56: A, B, k = "fetch", "fetch", 0.0
    elif f <= 68: A, B, k = seg("fetch", "carry", 56, 68)
    elif f <= 77: A, B, k = seg("carry", "nock", 68, 77)
    elif f <= 79: A, B, k = "nock", "nock", 0.0
    elif f <= 96:
        A, B = "nock", "aim"
        u = max(0.0, min(1.0, (f - 79) / 17.0))
        k = 1 - (1 - u) ** 2.6            # puxa depressa no início e devagar no fim (como um arco composto)
    elif f <= 110: A, B, k = "aim", "aim", 0.0
    elif f <= 125: A, B, k = "aim", "aim", 0.0
    else:          A, B, k = seg("aim", "idle", 125, 145)
    PA, PB = POSES[A], POSES[B]
    grip = Vector(PA["grip"]).lerp(Vector(PB["grip"]), k)
    cant = lerp(PA["cant"], PB["cant"], k); pitch = lerp(PA["pitch"], PB["pitch"], k)
    # respiração: balanço lento (menos a mirar — prende a respiração)
    hold = 1.0 if 96 <= f <= 110 else 0.0
    br = 1.0 - 0.75 * hold
    breath = Vector((0.0035 * math.sin(f * 2 * math.pi / 80), 0.005 * math.sin(f * 2 * math.pi / 80 + 0.6), 0.0)) * br
    grip += breath
    # a meio da puxada o arco sobe um pouco acima da mira e assenta (gesto natural)
    if 79 <= f <= 102:
        u = (f - 79) / 23.0
        grip += Vector((0, 0.018 * math.sin(u * math.pi) , 0))
    # tremor a segurar a corda / coice ao largar
    rel = f - 111
    if 96 <= f <= 110:
        tt = (f - 96) / 30.0
        amp = 0.6 + 0.8 * (f - 96) / 14.0      # o tremor aumenta com o esforço de segurar
        grip += Vector((0.0016 * math.sin(tt * 23), 0.0013 * math.sin(tt * 31 + 1), 0)) * amp
    if 0 <= rel <= 18:
        # coice: o arco salta para a frente e o topo cai para a frente (o pulso deixa-o rodar)
        kick = math.exp(-rel / 5.0) * math.sin(min(rel, 3) / 3 * math.pi / 2)
        grip += Vector((0.004 * kick, -0.03 * kick, -0.045 * kick)); pitch += 13 * kick; cant -= 3 * kick
    BW = bow_matrix(grip, cant, pitch)
    s["bow"] = BW
    grip_w = BW @ GRIP; rest_w = BW @ REST; nock_rest_w = BW @ NOCK_REST
    # mão esquerda no punho: pulso atrás e à esquerda do punho, palma para a direita (para o arco)
    fwdL = CD((FWDL_X, 0.10, -1.0)); palmL = CD((1.0, 0.0, -FWDL_X * 0.6))
    s["L"] = wrist_for("L", grip_w, fwdL, palmL, REL_GRIP)
    # corda: quanto está puxada e onde está o nó
    draw = None
    if "draw" in PA or "draw" in PB:
        da = PA.get("draw", 0.0); db = PB.get("draw", 0.0)
        draw = lerp(da, db, k) if (A != B) else da
    nock_w = nock_rest_w.copy()
    anchor_w = C(ANCHOR)
    if A in ("nock", "aim") and B in ("nock", "aim"):
        if f <= 110:
            dd = draw
            nock_w = nock_rest_w.lerp(anchor_w, dd)
            # a mão faz um arco por baixo da linha da flecha (não passa à frente da cara)
            nock_w += (CD((0.25, -1.0, 0.0)) * 0.05) * math.sin(math.pi * min(1.0, max(0.0, dd)))
            if 92 <= f <= 102:   # assenta no ancoradouro
                nock_w += (anchor_w - nock_rest_w).normalized() * 0.006 * math.sin((f - 92) / 10 * math.pi) * math.exp(-(f - 92) / 6)
    s["draw_nock"] = nock_w
    # corda depois de largar: volta e vibra
    if 111 <= f <= 125:
        osc = math.exp(-(f - 111) / 2.2) * math.cos((f - 111) * 2.4)
        back = (anchor_w - nock_rest_w)
        s["draw_nock"] = nock_rest_w + back * (0.12 * osc if f > 111 else 1.0)
    # mão direita
    if A in ("nock", "aim") and B in ("nock", "aim") and f <= 110:
        aim_dir = (rest_w - nock_w).normalized()
        palmR = CD(PALM_DRAW)
        # dedos em gancho na corda: a corda assenta na dobra das últimas falanges (medido no modelo)
        fwdR = (aim_dir + CD((0.35, -0.10, 0.0))).normalized()     # pulso direito, costas da mão para fora
        s["R"] = wrist_for("R", nock_w + breath * 0.6, fwdR, palmR, REL_STRING); s["rh"] = ("hook", None, 0.0)
    elif 111 <= f <= 125:
        # larga: a mão recua e abre (follow-through)
        t = (f - 111) / 14.0
        back = ease(min(1.0, t * 2.5))
        wrist0 = wrist_for("R", anchor_w, CD((0.25, -0.1, -1.0)), CD(PALM_DRAW), REL_STRING).translation
        wrist = wrist0 + CD((0.08, -0.06, 0.05)) * back      # a mão desliza para trás junto ao queixo
        palmR = CD(PALM_DRAW)
        s["R"] = hand_matrix("R", wrist, CD((-0.1, 0.05, -1.0)), palmR); s["rh"] = ("hook", "open", ease(min(1, (f - 111) / 3)))
    elif f > 125:
        k2 = ease((f - 125) / 20.0)
        wrist0 = wrist_for("R", anchor_w, CD((0.25, -0.1, -1.0)), CD(PALM_DRAW), REL_STRING).translation + CD((0.08, -0.06, 0.05))
        wrist = wrist0.lerp(C(POSES["idle"]["r"]), k2)
        fw = CD((-0.1, 0.05, -1.0)).lerp(CD(POSES["idle"]["rf"]), k2)
        s["R"] = hand_matrix("R", wrist, fw, CD((-1, 0, 0))); s["rh"] = ("open", "relax", k2)
    else:
        ra = Vector(PA["r"] if PA["r"] else (0.1, -0.1, -0.3)); rb = Vector(PB["r"] if PB["r"] else ra)
        fa = Vector(PA["rf"] if PA["rf"] else (-0.3, 0.1, -1)); fb = Vector(PB["rf"] if PB["rf"] else fa)
        if B == "nock":   # vai encaixar a flecha: o pulso acaba no nó da corda
            aim_dir = (rest_w - nock_rest_w).normalized()
            wristN = wrist_for("R", nock_rest_w, (aim_dir + CD((0.35, -0.10, 0.0))).normalized(), CD(PALM_DRAW), REL_STRING).translation
            wrist = C(ra).lerp(wristN, k)
            fw = CD(fa).lerp(aim_dir, k)
        else:
            wrist = C(ra.lerp(rb, k)) + breath; fw = CD(fa).lerp(CD(fb), k)
        s["R"] = hand_matrix("R", wrist, fw, CD((-1.0, 0.0, 0.0)))
        s["rh"] = (PA["rh"], PB["rh"], k)
    # flecha
    s["arrow_vis"] = 50 <= f <= 118
    if 50 <= f <= 68:
        # presa entre o polegar e o indicador, a apontar para a frente
        Rm = s["R"]; y = Rm.to_3x3().col[1].normalized()
        nockp = Rm.translation + y * 0.07
        u = min(1.0, max(0.0, (f - 54) / 10.0))
        adir = (CD((-0.25, 0.25, -1.0)) * u + CD((0.1, 1, 0.3)) * (1 - u)).normalized()
        s["arrow"] = (nockp, adir)
    elif 68 < f <= 77:
        Rm = s["R"]; y = Rm.to_3x3().col[1].normalized()
        nockp = Rm.translation + y * 0.075
        adir = (rest_w - nockp).normalized()
        s["arrow"] = (nockp, adir)
    elif 77 < f <= 110:
        nockp = s["draw_nock"]; s["arrow"] = (nockp, (rest_w - nockp).normalized())
    elif 111 <= f <= 118:
        d0 = (rest_w - anchor_w).normalized()
        travel = [0.0, 0.9, 2.6, 5.0, 8.0, 12.0, 17.0, 23.0][f - 111]
        s["arrow"] = (anchor_w + d0 * travel, d0); s["travel"] = travel
    else:
        s["arrow"] = (C((0, 0, 0)), CD((0, 0, -1)))
    return s

def arrow_matrix(nockp, d):
    z = d.normalized(); up = Vector((0, 0, 1))
    x = up.cross(z); x = x.normalized() if x.length > 1e-4 else Vector((1, 0, 0))
    y = z.cross(x)
    M = Matrix((x, y, z)).transposed().to_4x4(); M.translation = nockp
    return M

# ---------------------------------------------------------------- animação (um keyframe por frame)
START, END = 1, 150
sc.frame_start = START; sc.frame_end = END
for name, fr in (("Parado", 1), ("Carregar", 41), ("Puxar", 79), ("Mirar", 96), ("Atirar", 111), ("Parado", 126)):
    sc.timeline_markers.new(name, frame=fr)
RW = root.matrix_world.copy(); RWi = RW.inverted()
arrow_rest_scale = 1.0
def key_obj(o, Mw, f, parent_inv):
    L = parent_inv @ Mw
    loc, rot, _ = L.decompose()
    o.location = loc; o.rotation_mode = 'QUATERNION'; o.rotation_quaternion = rot
    o.keyframe_insert("location", frame=f); o.keyframe_insert("rotation_quaternion", frame=f)

pL.parent = root; pR.parent = root
pL.location = Vector((-0.55, -0.55, 0.05)); pR.location = Vector((0.75, 0.0, 0.75))
for f in range(START, END + 1):
    s = sample(f)
    key_obj(tL, s["L"], f, RWi); key_obj(tR, s["R"], f, RWi)
    bow.rotation_mode = 'QUATERNION'
    loc, rot, _ = s["bow"].decompose()
    bow.location = loc; bow.rotation_quaternion = rot
    bow.keyframe_insert("location", frame=f); bow.keyframe_insert("rotation_quaternion", frame=f)
    # gancho da corda (no espaço do arco)
    hook.location = s["bow"].inverted() @ s["draw_nock"]; hook.keyframe_insert("location", frame=f)
    # 3 flechas: encaixadas lado a lado na corda (ao longo do eixo "cima" do arco), abrem em leque ao sair
    nockp, d = s["arrow"]
    M0 = arrow_matrix(nockp, d)
    if f >= 68:
        up = s["bow"].to_3x3().col[2].normalized()
    else:
        up = M0.to_3x3().col[1].normalized()     # na mão: empilhadas entre os dedos
    up = (up - d * up.dot(d)).normalized()
    travel = s.get("travel", None)
    for ao in arrows:
        i = ARROW_OFF[ao.name]
        fan = FAN_FLY if travel is not None else FAN_NOCKED
        di = (Matrix.Rotation(math.radians(fan * i), 3, up) @ d).normalized()
        # um pouco de abertura também na vertical em voo (o leque do N.Cage espalha)
        if travel is not None:
            sideax = d.cross(up).normalized()
            di = (Matrix.Rotation(math.radians(-0.8 * i), 3, sideax) @ di).normalized()
            pi_ = nockp - d * travel + up * (ARROW_GAP * i) + di * travel
        else:
            pi_ = nockp + up * (ARROW_GAP * i)
        M = arrow_matrix(pi_, di) @ Matrix.Rotation(math.radians(ARROW_ROLL * i), 4, 'Z')   # penas desencontradas
        ao.rotation_mode = 'QUATERNION'
        loc, rot, _ = M.decompose()
        ao.location = loc; ao.rotation_quaternion = rot
        vis = 1.0 if s["arrow_vis"] else 0.0
        ao.scale = (vis, vis, vis)
        for p in ("location", "rotation_quaternion", "scale"): ao.keyframe_insert(p, frame=f)
    # dedos
    lk = ease((f - 84) / 12.0) if f < 111 else 1.0 - ease((f - 118) / 15.0)
    set_hand("Left", "grip", "gripsoft", lk)       # a mirar, a mão do arco relaxa (o arco "assenta" na palma)
    a, b, k = s["rh"]; set_hand("Right", a, b, k)
    for side in ("Left", "Right"):
        for bnm in [x.name for x in arm.pose.bones if x.name.startswith(f"smartrig:{side}Hand") and x.name != f"smartrig:{side}Hand"]:
            arm.pose.bones[bnm].keyframe_insert("rotation_euler", frame=f)

def all_fcurves(act):
    try: return list(act.fcurves)
    except AttributeError:
        return [fc for layer in act.layers for strip in layer.strips for cb in strip.channelbags for fc in cb.fcurves]
for ob in (tL, tR, bow, hook, *arrows, arm):
    if ob.animation_data and ob.animation_data.action:
        for fc in all_fcurves(ob.animation_data.action):
            for kp in fc.keyframe_points: kp.interpolation = 'LINEAR'
        ob.animation_data.action.name = "NCage_FPS_" + ob.name
for ao in arrows:
  for fc in all_fcurves(ao.animation_data.action):
    if fc.data_path == "scale":
        for kp in fc.keyframe_points: kp.interpolation = 'CONSTANT'

# a flecha sai muito depressa: o desfoque de movimento do render faz o rasto
sc.frame_set(1)

# ---------------------------------------------------------------- render
def setup_look():
    sc.render.engine = 'BLENDER_EEVEE'
    w = bpy.data.worlds.new("Ceu"); sc.world = w; w.use_nodes = True
    wn = w.node_tree; bg = wn.nodes["Background"]; bg.inputs[1].default_value = 1.0
    try:
        sky = wn.nodes.new("ShaderNodeTexSky")
        try: sky.sky_type = 'NISHITA'
        except Exception: pass
        sky.sun_elevation = math.radians(38); sky.sun_rotation = math.radians(200)
        wn.links.new(sky.outputs[0], bg.inputs[0]); bg.inputs[1].default_value = 0.25
    except Exception as e:
        print("sky", e); bg.inputs[0].default_value = (0.55, 0.66, 0.82, 1)
    sun = bpy.data.objects.new("Sol", bpy.data.lights.new("Sol", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 2.4; sun.data.angle = math.radians(2.5)
    sun.rotation_euler = (math.radians(52), 0, math.radians(200))
    # chão ao longe (terra e erva), só para dar contexto à vista
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, 0))
    gnd = bpy.context.active_object; gnd.name = "Chao"
    gm = bpy.data.materials.new("Chao_Mat"); gm.use_nodes = True; gnt = gm.node_tree; gb = gnt.nodes["Principled BSDF"]
    nz = gnt.nodes.new("ShaderNodeTexNoise"); nz.inputs["Scale"].default_value = 260.0; nz.inputs["Detail"].default_value = 10
    rp = gnt.nodes.new("ShaderNodeValToRGB")
    rp.color_ramp.elements[0].color = (0.16, 0.20, 0.07, 1); rp.color_ramp.elements[1].color = (0.33, 0.27, 0.15, 1)
    gnt.links.new(nz.outputs[0], rp.inputs[0]); gnt.links.new(rp.outputs[0], gb.inputs["Base Color"]); gb.inputs["Roughness"].default_value = 0.95
    gnd.data.materials.append(gm)
    sc.eevee.taa_render_samples = 16
    sc.render.use_motion_blur = True
    for attr, val in (("use_raytracing", True), ("use_shadows", True), ("shadow_ray_count", 2), ("shadow_step_count", 8)):
        try: setattr(sc.eevee, attr, val)
        except Exception: pass
    try: sc.render.motion_blur_shutter = 0.6
    except Exception: pass
    sc.render.resolution_x = 1280; sc.render.resolution_y = 720
    try:
        sc.view_settings.view_transform = 'AgX'; sc.view_settings.look = 'AgX - Medium High Contrast'
        sc.view_settings.exposure = -0.45      # a pele fica com o tom castanho do personagem (sem ficar rosada/lavada)
    except Exception: pass

if MODE == "frames":
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 640; sc.render.resolution_y = 360
    for f in [int(x) for x in (ARGS[1].split(",") if len(ARGS) > 1 else "20,50,62,70,85,100,112,114,135".split(","))]:
        sc.frame_set(f); sc.render.filepath = OUT + f"nc_{f:03d}.png"; bpy.ops.render.render(write_still=True)
if MODE == "eevee":
    setup_look(); sc.eevee.taa_render_samples = 8
    for f in [int(x) for x in ARGS[1].split(",")]:
        sc.frame_set(f); sc.render.filepath = OUT + f"ev_{f:03d}.png"; bpy.ops.render.render(write_still=True)
if MODE == "anim":
    setup_look(); sc.eevee.taa_render_samples = 8
    sc.render.image_settings.file_format = 'PNG'; sc.render.filepath = OUT + "anim/f_"
    bpy.ops.render.render(animation=True)
if MODE == "save":
    setup_look()
    sc.frame_set(1)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=ARGS[1], compress=True)
    print("SAVED", ARGS[1])

if MODE == "debug":
    sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x = 640; sc.render.resolution_y = 480
    dbg = bpy.data.objects.new("dbg", bpy.data.cameras.new("dbg")); sc.collection.objects.link(dbg); sc.camera = dbg
    bpy.ops.mesh.primitive_cone_add(radius1=0.03, depth=0.08); mk = bpy.context.active_object
    mk.matrix_world = CAMW @ Matrix.Rotation(math.pi, 4, 'X')
    for f in [int(x) for x in ARGS[1].split(",")]:
        sc.frame_set(f)
        for nm, pos in (("top", C((0.6, 1.2, -0.3))), ("side", C((1.4, 0.1, -0.35)))):
            dbg.location = pos; dbg.rotation_euler = (C((0, -0.1, -0.35)) - pos).to_track_quat('-Z', 'Y').to_euler(); dbg.data.lens = 28
            sc.render.filepath = OUT + f"dbg_{nm}_{f:03d}.png"; bpy.ops.render.render(write_still=True)

if MODE == "close":
    # grandes planos de verificação (do ponto de vista do jogador, com zoom): antebraço esquerdo/corda e dedos/unhas
    setup_look(); sc.eevee.taa_render_samples = 8
    sc.render.resolution_x = 960; sc.render.resolution_y = 540
    cc = bpy.data.objects.new("close", bpy.data.cameras.new("close")); sc.collection.objects.link(cc); sc.camera = cc
    cc.data.lens = 55; cc.data.clip_start = 0.005
    for f in [int(x) for x in ARGS[1].split(",")]:
        sc.frame_set(f)
        for nm, bn, t in (("antebracoL", "smartrig:LeftForeArm", 0.75), ("maoL", "smartrig:LeftHandMiddle2", 0.0), ("maoR", "smartrig:RightHandMiddle2", 0.0)):
            b = arm.pose.bones[bn]; tgt = arm.matrix_world @ b.head.lerp(b.tail, t)
            pos = CAMW.translation + CAMW.to_3x3() @ Vector((0.03, 0.0, 0.0))
            cc.location = pos; cc.rotation_euler = (tgt - pos).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = OUT + f"close_{nm}_{f:03d}.png"; bpy.ops.render.render(write_still=True)

if MODE == "nails":
    setup_look(); sc.eevee.taa_render_samples = 8
    sc.render.resolution_x = 960; sc.render.resolution_y = 540
    cc = bpy.data.objects.new("close", bpy.data.cameras.new("close")); sc.collection.objects.link(cc); sc.camera = cc
    cc.data.lens = 50; cc.data.clip_start = 0.005
    for f in [int(x) for x in ARGS[1].split(",")]:
        sc.frame_set(f); dg = bpy.context.evaluated_depsgraph_get()
        for side in ("Left", "Right"):
            pts = []; nrm = Vector()
            for nm in ("Index", "Middle", "Ring"):
                o = bpy.data.objects[f"Unha_{side}_{nm}"]; me = o.evaluated_get(dg).to_mesh()
                for p in me.polygons: nrm += o.matrix_world.to_3x3() @ p.normal * p.area
                pts += [o.matrix_world @ v.co for v in me.vertices]
                o.evaluated_get(dg).to_mesh_clear()
            c = sum(pts, Vector()) / len(pts); nrm.normalize()
            pos = CAMW.translation; cc.data.lens = 110
            cc.location = pos; cc.rotation_euler = (c - pos).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = OUT + f"nails_{side}_{f:03d}.png"; bpy.ops.render.render(write_still=True)
