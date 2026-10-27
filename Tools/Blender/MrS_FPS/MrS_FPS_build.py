import bpy, math, sys, os
from mathutils import Vector, Matrix, Euler, Quaternion

ARGS = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
MODE = ARGS[0] if ARGS else "still"
CHAR = "/mnt/user-data/uploads/GameJam/3d models/characters/ms character.fbx"
CAN = "/mnt/user-data/uploads/GameJam/3d models/arma ms/base.fbx"
TEX = "/mnt/user-data/uploads/GameJam/3d models/arma ms/"
OUT = "/tmp/claude-0/ms/out/"

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = 30

# ---------------------------------------------------------------- personagem: só os braços
bpy.ops.import_scene.fbx(filepath=CHAR)
arm = bpy.data.objects["SmartRigArmature"]; arm.name = "MrS_Rig"
body = bpy.data.objects["model"]; body.name = "MrS_Bracos"
keep_parts = ("Arm", "ForeArm", "Hand")
groups = [vg for vg in body.vertex_groups if any(k in vg.name for k in ("LeftArm","LeftForeArm","LeftHand","RightArm","RightForeArm","RightHand"))]
gi = {vg.index for vg in groups}
me = body.data
import bmesh
bm = bmesh.new(); bm.from_mesh(me)
dl = bm.verts.layers.deform.active
kill = []
for v in bm.verts:
    w = sum(val for g, val in v[dl].items() if g in gi) if dl else 0
    if w < 0.5: kill.append(v)
bmesh.ops.delete(bm, geom=kill, context='VERTS')
# mangas mais finas na 1.ª pessoa (o casaco é largo e taparia o ecrã): aproxima cada vértice do eixo do osso
SLEEVE = 0.62
segs = {}
for side in ("Left", "Right"):
    for bn in (f"smartrig:{side}Arm", f"smartrig:{side}ForeArm"):
        b = arm.data.bones[bn]; segs[bn] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
name_of = {vg.index: vg.name for vg in body.vertex_groups}
for v in bm.verts:
    best = None; bw = 0.0
    for g, val in v[dl].items():
        n = name_of.get(g)
        if n in segs and val > bw: best, bw = n, val
    if best is None or bw < 0.5: continue
    a, b = segs[best]; ab = b - a
    t = max(0.0, min(1.0, (v.co - a).dot(ab) / ab.length_squared))
    c = a + ab * t
    k = SLEEVE if best.endswith("Arm") and not best.endswith("ForeArm") else (1.0 - (1.0 - SLEEVE) * (1.0 - t * 0.6))
    v.co = c + (v.co - c) * k
bm.to_mesh(me); bm.free()
# o FBX vem com os vértices separados nas costuras de UV; com a suavização isso abria rachas escuras
# (sobretudo na base do polegar). Junta-se os vértices na mesma posição (as UV ficam intactas).
bm = bmesh.new(); bm.from_mesh(me)
_r = bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)
bm.to_mesh(me); bm.free()
print("arms faces", len(me.polygons))

# ---------------------------------------------------------------- material do braço com luz realista
for _m in body.data.materials:
    _nt = _m.node_tree; _b = next(n for n in _nt.nodes if n.bl_idname == "ShaderNodeBsdfPrincipled")
    _tex = next((n for n in _nt.nodes if n.bl_idname == "ShaderNodeTexImage"), None)
    for _l in list(_nt.links):
        if _l.to_socket == _b.inputs["Emission Color"]: _nt.links.remove(_l)
    _b.inputs["Emission Strength"].default_value = 0.0
    if _tex: _nt.links.new(_tex.outputs["Color"], _b.inputs["Base Color"])
    _b.inputs["Roughness"].default_value = 0.62
    _b.inputs["Specular IOR Level"].default_value = 0.35
    _b.inputs["Subsurface Weight"].default_value = 0.06          # um pouco de luz a atravessar a pele
    _b.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
    _b.inputs["Subsurface Scale"].default_value = 0.006

# ---------------------------------------------------------------- mãos mais suaves e unhas
# subdivisão (depois do esqueleto) para os dedos deixarem de ficar angulosos
sub = body.modifiers.new("Suavizar", 'SUBSURF'); sub.levels = 1; sub.render_levels = 2
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.shade_smooth()

def nail_material():
    m = bpy.data.materials.new("MrS_Unha"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.42
    b.inputs["Specular IOR Level"].default_value = 0.25
    try: b.inputs["Coat Weight"].default_value = 0.0
    except Exception: pass
    tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY_ADD'
    mul.inputs[1].default_value = 0.5; mul.inputs[2].default_value = 0.5    # y local (-1..1) -> 0..1
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    # do fundo (lúnula clara) à ponta (bordo livre esbranquiçado), corpo rosado-acastanhado
    e = ramp.color_ramp.elements
    e[0].position = 0.0; e[0].color = (0.10, 0.035, 0.025, 1)   # cutícula (junto à pele)
    e.new(0.10).color = (0.30, 0.13, 0.10, 1)                    # lúnula, mais clara
    e.new(0.26).color = (0.20, 0.075, 0.06, 1)                   # corpo da unha (rosado-acastanhado)
    e.new(0.84).color = (0.22, 0.085, 0.065, 1)
    e.new(0.90).color = (0.45, 0.30, 0.22, 1)                    # bordo livre mais claro
    e[-1].position = 1.0; e[-1].color = (0.48, 0.33, 0.24, 1)
    mr = nt.nodes.new("ShaderNodeMapRange")       # y local da unha (-1 fundo .. 1 ponta) -> 0..1
    mr.inputs["From Min"].default_value = 0.0; mr.inputs["From Max"].default_value = 1.0
    nt.nodes.remove(mr)                         # o atributo já vai de 0 a 1
    nt.links.new(tc.outputs["Object"], sep.inputs[0]); nt.links.new(sep.outputs[1], mul.inputs[0])
    nt.links.new(mul.outputs[0], ramp.inputs[0]); nt.links.new(ramp.outputs[0], b.inputs["Base Color"])
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
    length = width * (0.62 if finger == "Thumb" else 0.58)
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
    Mw = Matrix.Translation(p + n * 0.001) @ R @ Matrix.Diagonal((width * 0.46 * 0.5, length * 0.5, 1.0, 1.0))
    # a unha passa a fazer parte da "pele": cada vértice copia os pesos do vértice do braço mais próximo
    # e é deformada pelo mesmo esqueleto (segue o dedo exatamente)
    o.matrix_world = Mw
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    # (a transformação NÃO é aplicada: as coordenadas locais da unha servem para o degradê base -> ponta)
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
arm.data.pose_position = 'POSE'
print("unhas", len(NAILS))

# ---------------------------------------------------------------- latas com textura
def can_material():
    m = bpy.data.materials.new("SITA_Spray")
    m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    def tex(name, cs="sRGB"):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(TEX + name); t.image.colorspace_settings.name = cs; return t
    d = tex("texture_diffuse.png"); nt.links.new(d.outputs[0], b.inputs["Base Color"])
    r = tex("texture_roughness.png", "Non-Color"); nt.links.new(r.outputs[0], b.inputs["Roughness"])
    mt = tex("texture_metallic.png", "Non-Color"); nt.links.new(mt.outputs[0], b.inputs["Metallic"])
    n = tex("texture_normal.png", "Non-Color"); nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(n.outputs[0], nm.inputs["Color"]); nt.links.new(nm.outputs[0], b.inputs["Normal"])
    return m
mat = can_material()
_before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=CAN)
can_src = [o for o in bpy.data.objects if o not in _before and o.type == 'MESH'][0]
can_src.data.materials.clear(); can_src.data.materials.append(mat)
CAN_H = 0.19
s = CAN_H / 1.904
# origem no centro da lata
bpy.context.view_layer.objects.active = can_src
can_src.select_set(True)
bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
can_src.location = (0, 0, 0); can_src.scale = (s, s, s)
bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
# (o bico tem furo dos dois lados na textura, por isso o spray pode sair para a frente sem mexer na lata)
NOZZLE_TOP = 0.095     # topo do bico, no espaço da lata
can_R = can_src; can_R.name = "Lata_Direita"
can_L = can_R.copy(); can_L.data = can_R.data; can_L.name = "Lata_Esquerda"; sc.collection.objects.link(can_L)
CAN_R = 0.59 * s

# ---------------------------------------------------------------- câmara 1.ª pessoa
cam = bpy.data.objects.new("FPS_Camera", bpy.data.cameras.new("FPS_Camera"))
sc.collection.objects.link(cam); sc.camera = cam
EYE = Vector((0.0, -0.04, 1.48))   # olhos do Mr. S
cam.location = EYE
cam.rotation_euler = Euler((math.radians(80), 0, math.radians(180)))   # olha para −Y (frente da personagem), ligeiramente para baixo
cam.data.lens = 18; cam.data.clip_start = 0.01
root = bpy.data.objects.new("FP_Root", None); sc.collection.objects.link(root)
root.parent = cam   # tudo o que se segura segue a câmara

# alvos das mãos (no espaço da câmara: x direita, y cima, −z frente)
def target(name, loc):
    e = bpy.data.objects.new(name, None); e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.05
    sc.collection.objects.link(e); e.parent = root; e.location = loc; return e
CAN_Z = float(os.environ.get("CAN_Z", "0.0"))      # altura da lata na mão (negativo = mão mais perto do topo)
HAND_Y = -0.17 + (0.035 - CAN_Z)                     # a lata fica no mesmo sítio do ecrã
tR = target("Alvo_Mao_Direita", (0.16, HAND_Y, -0.43))
tL = target("Alvo_Mao_Esquerda", (-0.16, HAND_Y, -0.43))
pR = target("Cotovelo_Direito", (0.45, -0.45, 0.10))
pL = target("Cotovelo_Esquerdo", (-0.45, -0.45, 0.10))

# orientação das mãos: dedos (y do osso) para a frente e PALMA virada para a lata (para o centro do ecrã).
# A normal da palma no espaço local do osso da mão (medida na T-pose, palmas para baixo):
PALM_LOCAL = {"R": Vector((-0.87, 0.49)).normalized(), "L": Vector((0.87, 0.48)).normalized()}
FWD = Vector((0, -1, 0.12)).normalized()
def hand_basis(side, fwd=FWD):
    y = fwd.normalized()
    P = Vector((1, 0, 0)) if side == "R" else Vector((-1, 0, 0))      # palma para o centro
    P = (P - y * P.dot(y)).normalized()
    v = y.cross(P)
    px, pz = PALM_LOCAL[side]
    x = px * P + pz * v
    z = pz * P - px * v
    return Matrix((x, y, z)).transposed(), P

bpy.context.view_layer.update()
def set_target_rot(t, side):
    B, P = hand_basis(side)
    Mw = B.to_4x4(); Mw.translation = t.matrix_world.translation
    t.matrix_world = Mw
    return P
PR = set_target_rot(tR, "R"); PL = set_target_rot(tL, "L")
# mão direita = espelho exato da esquerda (o modelo não é simétrico; assim as duas mãos ficam com a mesma forma)
MIRROR_X = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))
bpy.context.view_layer.update()
tR.matrix_world = MIRROR_X @ tL.matrix_world @ MIRROR_X
PR = Vector((-PL.x, PL.y, PL.z))

pb = arm.pose.bones
def ik(side, t, pole):
    b = pb[f"smartrig:{side}ForeArm"]
    c = b.constraints.new('IK'); c.target = t; c.pole_target = pole; c.chain_count = 2; c.use_tail = False
    c.pole_angle = math.radians(-90); c.use_tail = True; c.use_stretch = True
    h = pb[f"smartrig:{side}Hand"]
    cr = h.constraints.new('COPY_ROTATION'); cr.target = t
    return c
ik("Right", tR, pR); ik("Left", tL, pL)
# braços da 1.ª pessoa mais compridos (a personagem é cartoon, de braços curtos): estica braço e antebraço
for side in ("Right", "Left"):
    for bn in (f"smartrig:{side}Arm", f"smartrig:{side}ForeArm"):
        pb[bn].ik_stretch = 0.8       # o IK pode esticar o braço até à lata
    arm.data.bones[f"smartrig:{side}Hand"].inherit_scale = 'NONE'   # a mão não herda o esticão

# ---------------------------------------------------------------- latas presas aos alvos das mãos
def place_can(can, t, P):
    bpy.context.view_layer.update()
    Mt = t.matrix_world
    y = Mt.to_3x3().col[1].normalized()
    pos = Mt.translation + y * CAN_OFS[0] + P * (CAN_R + CAN_OFS[1]) + Vector((0, 0, CAN_OFS[2]))
    can.parent = t
    can.matrix_parent_inverse = Matrix.Identity(4)
    can.matrix_world = Matrix.Translation(pos) @ Matrix.Rotation(math.pi, 4, 'Z')   # de pé, logótipo SITA virado para o jogador
CAN_OFS = (0.06, 0.012, CAN_Z)   # (à frente do pulso, folga da palma, altura)
place_can(can_R, tR, PR); place_can(can_L, tL, PL)
bpy.context.view_layer.update()
can_R.matrix_world = MIRROR_X @ can_L.matrix_world @ MIRROR_X     # lata direita no espelho exato da esquerda

FINGERS = ["Index", "Middle", "Ring", "Pinky"]
def curl(side, finger, a1, a2, a3):
    sgn = 1 if side == "Right" else 1
    for i, a in enumerate((a1, a2, a3), 1):
        b = pb.get(f"smartrig:{side}Hand{finger}{i}")
        if b is None: continue
        b.rotation_mode = 'XYZ'
        b.rotation_euler = Euler((math.radians(a) * sgn, 0, 0))
def thumb(side, a1, a2, a3, spread=0.0):
    # spread: roda a base do polegar para o lado de fora da lata (fica por fora, virado para o jogador)
    for i, a in enumerate((a1, a2, a3), 1):
        b = pb.get(f"smartrig:{side}HandThumb{i}")
        if b is None: continue
        b.rotation_mode = 'XYZ'
        z = math.radians(spread if side == "Left" else -spread) if i == 1 else 0.0
        b.rotation_euler = Euler((math.radians(a), 0, z))
GRIP = {"Index": (5, 25, 20), "Middle": (50, 55, 40), "Ring": (55, 55, 40), "Pinky": (60, 55, 40)}
for side in ("Right", "Left"):
    for f, a in GRIP.items(): curl(side, f, *a)
    pass

# polegar: em repouso fica levantado ao lado do bico; a pintar desce e carrega no bico (como se pinta de verdade).
# (x, y, z) da base do polegar + dobra da 2.ª e 3.ª falange. Valores ajustados para o polegar chegar ao topo do bico
# sem atravessar a lata (medido contra o perfil da lata).
# Esquerdo: polegar encostado ao topo do bico (folga < 0,5 mm, medida na malha suavizada).
# Direito: no modelo o polegar direito é ~10% mais pequeno e a mão não é simétrica; por isso o polegar direito
# é escalado 1,104× (fica do tamanho do esquerdo) e os ângulos foram ajustados para as articulações ficarem
# no espelho exato das do esquerdo (erro total < 7 mm nas 4 articulações), em repouso e a carregar.
THUMB_PRESS = {"Right": (30, -8, 52, 19, 45), "Left": (17, -10, 38, 35, 46)}
THUMB_IDLE = {"Right": (25, -5, 52, 13, 38), "Left": (12, -10, 38, 30, 36)}   # levantado logo acima do bico
pb["smartrig:RightHandThumb1"].scale = (1.104, 1.104, 1.104)
def set_thumb(side, p, key=None):
    s_ = 1 if side == "Left" else -1
    b1 = pb[f"smartrig:{side}HandThumb1"]; b2 = pb[f"smartrig:{side}HandThumb2"]; b3 = pb[f"smartrig:{side}HandThumb3"]
    for b in (b1, b2, b3): b.rotation_mode = 'XYZ'
    b1.rotation_euler = Euler((math.radians(p[0]), math.radians(p[1] * s_), math.radians(p[2] * s_)))
    b2.rotation_euler = Euler((math.radians(p[3]), 0, 0))
    b3.rotation_euler = Euler((math.radians(p[4]), 0, 0))
    if key is not None:
        for b in (b1, b2, b3): b.keyframe_insert("rotation_euler", frame=key)
def mix_thumb(side, k, extra=0.0):
    a, b = THUMB_IDLE[side], THUMB_PRESS[side]
    p = [x + (y - x) * k for x, y in zip(a, b)]
    p[4] += extra * 0.6; p[3] += extra * 0.3      # aperto extra = carrega mais fundo no bico
    return p
for side in ("Right", "Left"): set_thumb(side, THUMB_IDLE[side])

bpy.context.view_layer.update()


# ---------------------------------------------------------------- braço direito = espelho do esquerdo
# O modelo do Mr. S não é simétrico (polegar direito mais curto, pesos diferentes), por isso a mão direita nunca
# ficava igual à esquerda. Solução: o braço direito que se vê é uma CÓPIA ESPELHADA do braço esquerdo, movida por
# uma cópia do esqueleto que segue o alvo da mão direita (espelhado para o lado esquerdo). Mesma forma, mesmas
# unhas, mesmo polegar; o movimento de cada mão continua independente.
def _mirror_gn(obj):
    """Modificador de nós que espelha a geometria no plano x = 0 do mundo (e vira as faces)."""
    M = obj.matrix_world.copy()
    N = M.inverted() @ MIRROR_X @ M
    ng = bpy.data.node_groups.new("FP_Espelho_" + obj.name, 'GeometryNodeTree')
    ng.interface.new_socket("Geometry", in_out='INPUT', socket_type='NodeSocketGeometry')
    ng.interface.new_socket("Geometry", in_out='OUTPUT', socket_type='NodeSocketGeometry')
    gi = ng.nodes.new("NodeGroupInput"); go = ng.nodes.new("NodeGroupOutput")
    sp = ng.nodes.new("GeometryNodeSetPosition"); pos = ng.nodes.new("GeometryNodeInputPosition")
    tp = ng.nodes.new("FunctionNodeTransformPoint"); cm = ng.nodes.new("FunctionNodeCombineMatrix")
    for col in range(4):
        for row in range(4):
            cm.inputs[col * 4 + row].default_value = N[row][col]
    ff = ng.nodes.new("GeometryNodeFlipFaces")
    L = ng.links
    L.new(gi.outputs[0], sp.inputs["Geometry"]); L.new(pos.outputs[0], tp.inputs["Vector"])
    L.new(cm.outputs[0], tp.inputs["Transform"]); L.new(tp.outputs[0], sp.inputs["Position"])
    L.new(sp.outputs[0], ff.inputs["Mesh"]); L.new(ff.outputs[0], go.inputs[0])
    md = obj.modifiers.new("Espelho", 'NODES'); md.node_group = ng
    return md

bpy.context.view_layer.update()
# grupo com os vértices do braço esquerdo
_left_idx = {g.index for g in body.vertex_groups if g.name.startswith("smartrig:Left")}
vg_left = body.vertex_groups.new(name="FP_Esquerdo")
vg_left.add([v.index for v in body.data.vertices
             if sum(g.weight for g in v.groups if g.group in _left_idx) >= 0.5], 1.0, 'REPLACE')
mk = body.modifiers.new("So_Esquerdo", 'MASK'); mk.vertex_group = "FP_Esquerdo"
# alvos do braço espelhado (no lado esquerdo)
tRm = bpy.data.objects.new("Alvo_Mao_Direita_Espelho", None); sc.collection.objects.link(tRm)
tRm.parent = root; tRm.empty_display_size = 0.03
tRm.matrix_world = MIRROR_X @ tR.matrix_world @ MIRROR_X
pRm = bpy.data.objects.new("Cotovelo_Direito_Espelho", None); sc.collection.objects.link(pRm)
pRm.parent = root; pRm.matrix_world = MIRROR_X @ pR.matrix_world @ MIRROR_X
# esqueleto cópia, a seguir os alvos espelhados
arm2 = arm.copy(); arm2.name = "MrS_Rig_Espelho"; sc.collection.objects.link(arm2)
for c in arm2.pose.bones["smartrig:LeftForeArm"].constraints:
    if c.type == 'IK': c.target = tRm; c.pole_target = pRm
for c in arm2.pose.bones["smartrig:LeftHand"].constraints:
    if c.type == 'COPY_ROTATION': c.target = tRm
arm2.hide_viewport = False; arm2.hide_render = True
body2 = body.copy(); body2.name = "MrS_Braco_Direito_Espelho"; sc.collection.objects.link(body2)
body2.parent = arm2
for md in body2.modifiers:
    if md.type == 'ARMATURE': md.object = arm2
bpy.context.view_layer.update()
_mirror_gn(body2)
for n in list(NAILS):
    if "_Right_" in n.name:
        bpy.data.objects.remove(n, do_unlink=True); NAILS.remove(n)
for n in list(NAILS):
    n2 = n.copy(); n2.name = n.name.replace("_Left_", "_Direita_Espelho_"); sc.collection.objects.link(n2)
    Mk = n.matrix_world.copy(); n2.parent = arm2; n2.matrix_world = Mk
    for md in n2.modifiers:
        if md.type == 'ARMATURE': md.object = arm2
    bpy.context.view_layer.update()
    _mirror_gn(n2)
bpy.context.view_layer.update()

# ---------------------------------------------------------------- nuvem de tinta (spray)
def mist(name, color, can):
    """Nuvem de tinta: cone estreito no bico que abre e se desfaz com a distância (fiapos de tinta, não uma bola)."""
    bpy.ops.mesh.primitive_cone_add(vertices=32, radius1=0.003, radius2=0.16, depth=0.75, end_fill_type='NOTHING')
    m = bpy.context.active_object; m.name = name
    # cone ao longo de +Y local da lata (a lata está rodada 180°: +Y local = frente do jogador)
    m.rotation_euler = (math.radians(-90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True)
    for v in m.data.vertices: v.co.y += 0.375
    bpy.ops.object.shade_smooth()
    mt = bpy.data.materials.new(name + "_Mat"); mt.use_nodes = True
    nt = mt.node_tree; nt.nodes.clear(); L = nt.links
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission"); em.inputs[0].default_value = color; em.inputs[1].default_value = 1.6
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); mp.inputs["Scale"].default_value = (14.0, 3.5, 14.0)   # fiapos ao longo do jato
    noise = nt.nodes.new("ShaderNodeTexNoise"); noise.inputs["Scale"].default_value = 1.0
    noise.inputs["Detail"].default_value = 6.0
    ramp = nt.nodes.new("ShaderNodeValToRGB"); ramp.color_ramp.elements[0].position = 0.36; ramp.color_ramp.elements[1].position = 0.66
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    fade = nt.nodes.new("ShaderNodeMath"); fade.operation = 'MULTIPLY_ADD'      # 1 - y/0.75
    fade.inputs[1].default_value = -1.0 / 0.75; fade.inputs[2].default_value = 1.0; fade.use_clamp = True
    pw = nt.nodes.new("ShaderNodeMath"); pw.operation = 'POWER'; pw.inputs[1].default_value = 1.6
    mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY'
    dens = nt.nodes.new("ShaderNodeMath"); dens.operation = 'MULTIPLY'; dens.inputs[1].default_value = 1.9; dens.use_clamp = True
    L.new(tc.outputs["Object"], mp.inputs["Vector"]); L.new(mp.outputs[0], noise.inputs["Vector"])
    L.new(noise.outputs[0], ramp.inputs[0])
    L.new(tc.outputs["Object"], sep.inputs[0]); L.new(sep.outputs[1], fade.inputs[0]); L.new(fade.outputs[0], pw.inputs[0])
    L.new(ramp.outputs[0], mul.inputs[0]); L.new(pw.outputs[0], mul.inputs[1]); L.new(mul.outputs[0], dens.inputs[0])
    L.new(dens.outputs[0], mix.inputs[0])
    L.new(tr.outputs[0], mix.inputs[1]); L.new(em.outputs[0], mix.inputs[2])
    L.new(mix.outputs[0], out.inputs[0])
    try: mt.surface_render_method = 'BLENDED'
    except Exception: pass
    mt.use_backface_culling = False
    m.data.materials.append(mt)
    m.parent = can; m.matrix_parent_inverse = Matrix.Identity(4)
    m.location = (0, 0.016, 0.088)          # sai do furo do bico, para a frente
    m.rotation_euler = (math.radians(TILT), 0, 0)   # compensa a inclinação da lata: o spray sai a direito
    m.scale = (0.001, 0.001, 0.001)
    return m

def droplets(name, color, can, f0, f1):
    """Gotículas de tinta a sair do bico enquanto se pinta (partículas)."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1.0)
    drop = bpy.context.active_object; drop.name = name + "_Gota"
    dm = bpy.data.materials.new(name + "_Gota_Mat"); dm.use_nodes = True
    bb = dm.node_tree.nodes["Principled BSDF"]; bb.inputs["Base Color"].default_value = color
    bb.inputs["Emission Color"].default_value = color; bb.inputs["Emission Strength"].default_value = 0.6
    bb.inputs["Roughness"].default_value = 0.3
    drop.data.materials.append(dm); drop.hide_render = False
    drop.location = (0, 0, -50)                       # o original fica escondido longe da câmara
    bpy.ops.mesh.primitive_plane_add(size=0.004)
    em = bpy.context.active_object; em.name = name + "_Emissor"
    em.parent = can; em.matrix_parent_inverse = Matrix.Identity(4)
    em.location = (0, 0.02, 0.088); em.rotation_euler = (math.radians(TILT - 90), 0, 0)
    em.hide_render = True
    ps = em.modifiers.new("Gotas", 'PARTICLE_SYSTEM').particle_system
    st = ps.settings
    st.count = 420; st.frame_start = f0; st.frame_end = f1; st.lifetime = 12; st.lifetime_random = 0.4
    st.emit_from = 'FACE'; st.normal_factor = 2.6; st.factor_random = 0.35
    st.effector_weights.gravity = 0.25
    st.render_type = 'OBJECT'; st.instance_object = drop
    st.particle_size = 0.0035; st.size_random = 0.6
    ps.seed = 3 if "Dir" in name else 7
    return em, drop
TILT = 28
mistR = mist("Spray_Direita", (1.0, 0.25, 0.65, 1), can_R)
mistL = mist("Spray_Esquerda", (0.2, 0.9, 0.85, 1), can_L)
# gotículas: saem enquanto o bico está carregado (frames da secção "Pintar")
dropsR = droplets("Spray_Direita", (1.0, 0.25, 0.65, 1), can_R, 61 + 14, 130 - 9)
dropsL = droplets("Spray_Esquerda", (0.2, 0.9, 0.85, 1), can_L, 61 + 14, 130 - 9)

# ---------------------------------------------------------------- animação
bpy.context.view_layer.update()
BASE = {tR: tR.matrix_world.copy(), tL: tL.matrix_world.copy()}
RIGHT_W = Vector((-1, 0, 0))     # direita do ecrã no mundo
FWD_W = Vector((0, -1, 0))
UP_W = Vector((0, 0, 1))

def key_target(t, f, ofs=(0, 0, 0), tilt=0.0, roll=0.0):
    sc.frame_current = f
    B = BASE[t]
    R = Matrix.Rotation(math.radians(tilt), 4, Vector((1, 0, 0))) @ Matrix.Rotation(math.radians(roll), 4, FWD_W)
    M = R @ B.to_3x3().to_4x4()
    M.translation = B.translation + RIGHT_W * ofs[0] + UP_W * ofs[1] + FWD_W * ofs[2]
    # local = inversa do pai (câmara/raiz, parados) × mundo — sem avaliar a animação a meio
    L = t.parent.matrix_world.inverted() @ M
    t.location = L.translation; t.rotation_euler = L.to_euler('XYZ')
    t.keyframe_insert("location", frame=f); t.keyframe_insert("rotation_euler", frame=f)
    if t is tR:
        Lm = tRm.parent.matrix_world.inverted() @ (MIRROR_X @ M @ MIRROR_X)
        tRm.location = Lm.translation; tRm.rotation_euler = Lm.to_euler('XYZ')
        tRm.keyframe_insert("location", frame=f); tRm.keyframe_insert("rotation_euler", frame=f)

def key_index(f, extra):
    for side in ("Right", "Left"):
        a = GRIP["Index"]
        for i, base in enumerate(a, 1):
            b = pb.get(f"smartrig:{side}HandIndex{i}")
            if b is None: continue
            b.rotation_euler = Euler((math.radians(base + (extra if i == 1 else extra * 0.5)), 0, 0))
            b.keyframe_insert("rotation_euler", frame=f)

def key_mist(f, on, flick=1.0):
    for m in (mistR, mistL):
        k = 1.0 * flick if on else 0.001
        m.scale = (k, k, k); m.keyframe_insert("scale", frame=f)

IDLE = (1, 60); SPRAY = (61, 130); SHAKE = (131, 200); END = 210
sc.frame_start = 1; sc.frame_end = END
for name, f in (("Parado", IDLE[0]), ("Pintar", SPRAY[0]), ("Recarregar (abanar)", SHAKE[0]), ("Parado", SHAKE[1])):
    sc.timeline_markers.new(name, frame=f)

# Parado: respiração (sobe/desce) e balanço lateral, as duas mãos desfasadas
for f in range(IDLE[0], IDLE[1] + 1, 6):
    ph = (f - 1) / 60 * 2 * math.pi
    key_target(tR, f, (0.004 * math.sin(ph), 0.006 * math.sin(ph), 0), roll=1.5 * math.sin(ph))
    key_target(tL, f, (0.004 * math.sin(ph + 0.8), 0.006 * math.sin(ph + 0.8), 0), roll=-1.5 * math.sin(ph + 0.8))
key_index(1, 0); key_mist(1, False)

# Pintar: levanta e inclina as latas para a frente, carrega no bico, tinta a sair com tremor
key_index(SPRAY[0], 0); key_mist(SPRAY[0], False)
for t, sgn in ((tR, 1), (tL, -1)):
    key_target(t, SPRAY[0] + 8, (-0.015 * sgn, 0.03, 0.0), tilt=TILT)
key_index(SPRAY[0] + 10, 0)
def key_thumbs(f, k, extra=0.0):
    for side in ("Right", "Left"): set_thumb(side, mix_thumb(side, k, extra), key=f)
key_thumbs(1, 0); key_thumbs(SPRAY[0], 0)
key_thumbs(SPRAY[0] + 8, 0.75)            # o polegar vai para cima do bico
key_thumbs(SPRAY[0] + 13, 1.0, 6)         # carrega

key_mist(SPRAY[0] + 12, False); key_mist(SPRAY[0] + 14, True)
import random
rnd = random.Random(7)
for f in range(SPRAY[0] + 14, SPRAY[1] - 10, 2):
    for t, sgn in ((tR, 1), (tL, -1)):
        j = (rnd.uniform(-1, 1) * 0.003, rnd.uniform(-1, 1) * 0.003, rnd.uniform(-1, 1) * 0.002)
        key_target(t, f, (-0.015 * sgn + j[0], 0.03 + j[1], j[2]), tilt=TILT + rnd.uniform(-1.5, 1.5))
    if (f // 2) % 3 == 0: key_mist(f, True, rnd.uniform(0.85, 1.1))
for f in range(SPRAY[0] + 16, SPRAY[1] - 10, 6):
    key_thumbs(f, 1.0, 6 + rnd.uniform(-2, 2))   # mantém carregado, com pequenas variações de força
key_thumbs(SPRAY[1] - 10, 1.0, 6)
key_thumbs(SPRAY[1] - 7, 0.8)             # larga o bico
key_thumbs(SPRAY[1], 0); key_thumbs(SHAKE[1], 0); key_thumbs(END, 0)
key_mist(SPRAY[1] - 9, True); key_mist(SPRAY[1] - 7, False)
for t in (tR, tL): key_target(t, SPRAY[1], (0, 0, 0), tilt=0)

# Recarregar: abanar as latas para cima e para baixo (6 vezes), com rodar do pulso
key_index(SHAKE[0], 0); key_mist(SHAKE[0], False)
for t in (tR, tL): key_target(t, SHAKE[0], (0, 0, 0))
for t, sgn in ((tR, 1), (tL, -1)): key_target(t, SHAKE[0] + 8, (-0.02 * sgn, 0.04, 0.0), tilt=-8)
n = 6
for i in range(n * 2 + 1):
    f = SHAKE[0] + 10 + i * 4
    up = (i % 2 == 0)
    for t, sgn, ph in ((tR, 1, 0), (tL, -1, 1)):
        u = up if ph == 0 else not up
        key_target(t, f, (-0.02 * sgn, 0.07 if u else -0.01, 0.0), tilt=-10 if u else 8, roll=(8 if u else -8) * sgn)
for t in (tR, tL): key_target(t, SHAKE[1], (0, 0, 0)); key_target(t, END, (0, 0, 0))
key_index(END, 0); key_mist(END, False)

sc.frame_current = 1
# interpolação suave; o tremor do spray e o abanar ficam mais secos
# o esqueleto espelhado usa a mesma animação dos dedos/polegar (a do lado esquerdo)
arm2.animation_data_create(); arm2.animation_data.action = arm.animation_data.action
try: arm2.animation_data.action_slot = arm.animation_data.action_slot
except Exception: pass
for ob in (tR, tL, tRm, arm, mistR, mistL):
    if ob.animation_data and ob.animation_data.action:
        act = ob.animation_data.action
        try:
            curves = act.fcurves
        except AttributeError:
            curves = [fc for layer in act.layers for strip in layer.strips for cb in strip.channelbags for fc in cb.fcurves]
        for fc in curves:
            for kp in fc.keyframe_points: kp.interpolation = 'BEZIER'
for ob, nm in ((tR, "FP_Alvo_Direita"), (tRm, "FP_Alvo_Direita_Espelho"), (tL, "FP_Alvo_Esquerda"), (arm, "FP_Dedos"), (mistR, "FP_Spray_Direita"), (mistL, "FP_Spray_Esquerda")):
    if ob.animation_data and ob.animation_data.action: ob.animation_data.action.name = "MrS_FPS_" + nm

# ---------------------------------------------------------------- render
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.show_cavity = True
sc.render.resolution_x = 1280; sc.render.resolution_y = 720
def render(path):
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
if MODE == "still":
    render(OUT + "fp_still.png")
    # vista de lado para depurar
    dbg = bpy.data.objects.new("dbg", bpy.data.cameras.new("dbg")); sc.collection.objects.link(dbg)
    dbg.location = (1.2, -0.4, 1.45); d = Vector((0, -0.25, 1.3)) - dbg.location
    dbg.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler(); dbg.data.lens = 35
    sc.camera = dbg; render(OUT + "fp_side.png"); sc.camera = cam

# ---------------------------------------------------------------- luz e mundo (EEVEE) para pré-visualizar
def setup_look():
    sc.render.engine = 'BLENDER_EEVEE'
    w = bpy.data.worlds.new("Ceu_Cidade_Velha"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.55, 0.66, 0.82, 1); bg.inputs[1].default_value = 1.0
    sun = bpy.data.objects.new("Sol", bpy.data.lights.new("Sol", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    sc.eevee.taa_render_samples = 16
    sc.render.resolution_x = 1280; sc.render.resolution_y = 720
    try: sc.view_settings.view_transform = 'AgX'
    except Exception: pass
if MODE in ("frames", "anim", "save", "grip"):
    setup_look()
if MODE == "grip":
    sc.render.resolution_x = 640; sc.render.resolution_y = 480
    sc.frame_set(30)
    dbg = bpy.data.objects.new("dbg", bpy.data.cameras.new("dbg")); sc.collection.objects.link(dbg); sc.camera = dbg; dbg.data.lens = 35
    hp = tR.matrix_world.translation
    for nm, off in (("lado", Vector((-0.6, 0.0, 0.08))), ("cima", Vector((0.0, -0.05, 0.6))), ("frente", Vector((0.05, -0.6, 0.12)))):
        dbg.location = hp + off; d = hp - dbg.location
        dbg.rotation_euler = d.to_track_quat('-Z', 'Y' if nm != "cima" else 'X').to_euler()
        sc.render.filepath = OUT + f"grip_{nm}.png"; bpy.ops.render.render(write_still=True)
    sc.camera = cam; sc.render.filepath = OUT + "grip_fp.png"; bpy.ops.render.render(write_still=True)
if MODE == "frames":
    sc.render.resolution_x = 640; sc.render.resolution_y = 360
    for f in (30, 69, 100, 147, 151, 200):
        sc.frame_set(f); sc.render.filepath = OUT + f"f_{f:03d}.png"; bpy.ops.render.render(write_still=True)
if MODE == "anim":
    sc.render.resolution_x = 960; sc.render.resolution_y = 540
    sc.render.image_settings.file_format = 'PNG'
    sc.render.filepath = OUT + "anim/f_"
    bpy.ops.render.render(animation=True)
if MODE == "save":
    path = ARGS[1]
    sc.frame_set(1)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    print("SAVED", path)
