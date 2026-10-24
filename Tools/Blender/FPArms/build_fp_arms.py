"""
Braços da 1.ª pessoa a partir dos modelos dos personagens (Blender 4.2+ / bpy 5.x).

Para cada personagem:
  1. importa o FBX (modelo + esqueleto SmartRig);
  2. corta o braço de cada lado (do ombro ao pulso) da malha do personagem — manga, punho, pele e textura ficam
     iguais à roupa que ele tem vestida;
  3. refaz a mão com 5 dedos separados (os modelos gerados têm os dedos colados): palma, dedos e polegar em tubos
     orgânicos fundidos por remesh de voxels, com a cor da pele / luva tirada da textura do próprio personagem;
  4. cria um esqueleto limpo por braço: UpperArm → ForeArm → Hand → 5 dedos × 3 falanges
     (pesos: braço por distância aos ossos, mão por "bone heat");
  5. exporta <Nome>_FPArms.fbx (dois braços, Arm_R e Arm_L, cada um com o seu esqueleto).

Uso:
  python3 build_fp_arms.py -- [Ekko NCage ...]          (bpy como módulo)
  blender -b -P build_fp_arms.py -- [Ekko ...]           (Blender instalado)
"""
import bpy, bmesh, os, sys, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
CHAR = os.path.join(REPO, "Assets", "_Game", "Art", "Characters")

# nome, fbx, textura, luvas (palma e 1.ª falange com a cor da luva)
CHARS = {
    "Ekko":         ("Ekko/Ekko_Source.fbx", "Ekko/Ekko_diffuse.png", False),
    "NCage":        ("NCage/NCage_Source.fbx", "NCage/NCage_diffuse.png", False),
    "LupusMarinus": ("LupusMarinus/LupusMarinus_Source.fbx", "LupusMarinus/LupusMarinus_diffuse.png", False),
    "MrS":          ("MrS/MrS_Source.fbx", "MrS/MS_diffuse.png", False),
    "Riot":         ("Riot/Riot_Source.fbx", "Riot/Riot_diffuse.png", True),
    "Lince":        ("Lince/Lince_rig.fbx", "Lince/Lince_diffuse.png", False),
}

FINGERS = ["Thumb", "Index", "Middle", "Ring", "Pinky"]


def log(*a):
    print("[FPArms]", *a, flush=True)


# ---------------------------------------------------------------------------------------------- utilidades

def bone_head(arm, name):
    b = arm.data.bones.get(name)
    return arm.matrix_world @ b.head_local if b else None


def bone_tail(arm, name):
    b = arm.data.bones.get(name)
    return arm.matrix_world @ b.tail_local if b else None


def link(obj):
    bpy.context.scene.collection.objects.link(obj)


def select_only(*objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def sample_image(img, uv):
    w, h = img.size
    x = min(w - 1, max(0, int(uv.x * w)))
    y = min(h - 1, max(0, int(uv.y * h)))
    i = (y * w + x) * 4
    p = img.pixels
    return Vector((p[i], p[i + 1], p[i + 2]))


# ---------------------------------------------------------------------------------------------- lado do braço

class Side:
    def __init__(self, arm, side):
        self.side = side                       # "R" / "L"
        pre = "smartrig:" + ("Right" if side == "R" else "Left")
        self.pre = pre
        self.S = bone_head(arm, pre + "Arm")        # ombro
        self.E = bone_head(arm, pre + "ForeArm")    # cotovelo
        self.W = bone_head(arm, pre + "Hand")       # pulso
        self.F = (self.W - self.E).normalized()     # para fora (direção dos dedos em T-pose)
        idx = bone_head(arm, pre + "HandIndex1")
        pin = bone_head(arm, pre + "HandPinky1")
        mid = bone_head(arm, pre + "HandMiddle1")
        mtip = bone_tail(arm, pre + "HandMiddle4") or bone_tail(arm, pre + "HandMiddle3")
        if idx is not None and pin is not None:
            U = idx - pin
        else:
            U = Vector((0, -1, 0))                  # lado do indicador = frente do personagem
        U = (U - self.F * U.dot(self.F)).normalized()
        P = self.F.cross(U).normalized()
        if P.z > 0:                                  # palma para baixo na T-pose
            P = -P
        self.U, self.P = U, P
        self.lp = (mid - self.W).length if mid is not None else 0.085   # pulso → nós dos dedos
        self.hand_len = (mtip - self.W).length if mtip is not None else self.lp * 1.9
        self.width = (idx - pin).length * 1.25 if idx is not None and pin is not None else self.lp * 0.95
        self.arm_bones = {n for n in (pre + "Arm", pre + "ForeArm", pre + "Hand")} | \
            {b.name for b in arm.data.bones if b.name.startswith(pre + "Hand")}
        self.hand_bones = {b.name for b in arm.data.bones if b.name.startswith(pre + "Hand")}

    def along(self, p):
        """Distância ao longo do braço desde o pulso (positivo = para a mão)."""
        return (p - self.W).dot(self.F)


# ---------------------------------------------------------------------------------------------- 1. braço do modelo

def cut_arm(src, arm, sd):
    """Malha do braço (ombro → pulso), com UVs e material do personagem."""
    me = src.data
    groups = {g.index: g.name for g in src.vertex_groups}
    armw = [0.0] * len(me.vertices)
    handw = [0.0] * len(me.vertices)
    for v in me.vertices:
        for g in v.groups:
            n = groups.get(g.group)
            if n in sd.arm_bones:
                armw[v.index] += g.weight
            if n in sd.hand_bones:
                handw[v.index] += g.weight
    M = src.matrix_world
    upper_lim = -( (sd.W - sd.S).length - 0.03 )    # um pouco antes da articulação do ombro
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    kill = []
    for f in bm.faces:
        ok = True
        cen = Vector()
        for v in f.verts:
            if armw[v.index] < 0.5:
                ok = False
                break
            cen += M @ v.co
        if ok:
            cen /= len(f.verts)
            a = sd.along(cen)
            if a > -0.028 or a < upper_lim:
                ok = False
        if not ok:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    # tapa a abertura do pulso (senão vê-se o interior do braço à volta da mão nova)
    wrist_edges = [e for e in bm.edges if e.is_boundary and all(sd.along(M @ v.co) > -0.05 for v in e.verts)]
    if wrist_edges:
        bmesh.ops.holes_fill(bm, edges=wrist_edges, sides=0)
    out = bpy.data.meshes.new("Arm_" + sd.side)
    bm.to_mesh(out)
    bm.free()
    ob = bpy.data.objects.new("ArmPiece_" + sd.side, out)
    ob.matrix_world = M.copy()
    link(ob)
    # aplica a transformação (fica tudo em coordenadas do mundo)
    select_only(ob)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    ob.data.materials.clear()
    for s in src.data.materials:
        ob.data.materials.append(s)
    return ob, armw, handw


def forearm_radii(piece, sd):
    """Raio do antebraço junto ao pulso (na malha do braço já cortada)."""
    ru, rp, n = 0.0, 0.0, 0
    for v in piece.data.vertices:
        a = sd.along(v.co)
        if -0.035 <= a <= -0.005:
            d = v.co - (sd.W + sd.F * a)
            ru = max(ru, abs(d.dot(sd.U)))
            rp = max(rp, abs(d.dot(sd.P)))
            n += 1
    return (ru, rp) if n >= 6 else (None, None)


def wrist_radii(src, sd, handw):
    """Raio da pele no pulso (elipse: largura no sentido do indicador e espessura palma-costas)."""
    M = src.matrix_world
    ru, rp, n = 0.0, 0.0, 0
    for v in src.data.vertices:
        if handw[v.index] < 0.5:
            continue
        p = M @ v.co
        a = sd.along(p)
        if 0.0 <= a <= 0.025:
            d = p - (sd.W + sd.F * a)
            ru = max(ru, abs(d.dot(sd.U)))
            rp = max(rp, abs(d.dot(sd.P)))
            n += 1
    if n < 6:
        return sd.width * 0.42, sd.width * 0.3
    return ru, rp


def pick_uvs(src, sd, img, gloves):
    """UV de pele (falange distal / pulso) e de luva/costas da mão, tirados da própria malha."""
    me = src.data
    M = src.matrix_world
    groups = {g.index: g.name for g in src.vertex_groups}
    uvl = me.uv_layers.active.data
    tip_names = {sd.pre + "Hand" + f + str(i) for f in FINGERS for i in (3, 4)}
    back = []
    tips = []
    for poly in me.polygons:
        cen = M @ poly.center
        dom = {}
        for vi in poly.vertices:
            for g in me.vertices[vi].groups:
                n = groups.get(g.group)
                dom[n] = dom.get(n, 0) + g.weight
        if not dom:
            continue
        top = max(dom, key=dom.get)
        uvc = Vector((0, 0))
        for li in poly.loop_indices:
            uvc += uvl[li].uv
        uvc /= len(poly.loop_indices)
        if top == sd.pre + "Hand":
            # costas da mão (lado oposto à palma), a meio entre o pulso e os nós
            a = sd.along(cen)
            if 0.2 * sd.lp < a < 0.75 * sd.lp and (cen - sd.W).dot(sd.P) < 0:
                back.append(uvc.copy())
        elif top in tip_names:
            tips.append(uvc.copy())

    def median_uv(lst):
        if not lst:
            return None
        cols = [(sample_image(img, u), u) for u in lst]
        lum = sorted(cols, key=lambda c: c[0].length)
        return lum[len(lum) // 2][1]

    def skin_like(u):
        c = sample_image(img, u)
        r = max(c.x, 1e-4)
        return c.x > c.z and 0.38 <= c.y / r <= 0.82 and c.z / r >= 0.18

    back_uv = median_uv(back)
    # pele: candidatos com cor de pele (evita tinta/graffiti, luvas e tecido)
    skin_c = [u for u in tips + back if skin_like(u)]
    tip_uv = median_uv([u for u in tips if skin_like(u)]) or median_uv(skin_c) or median_uv(tips) or back_uv
    skin_uv = tip_uv if gloves else (median_uv([u for u in back if skin_like(u)]) or tip_uv or back_uv)
    glove_uv = back_uv if gloves else skin_uv
    log(sd.side, "UV pele", tuple(round(x, 3) for x in skin_uv), "cor", tuple(round(x, 2) for x in sample_image(img, skin_uv)),
        "| luva", tuple(round(x, 2) for x in sample_image(img, glove_uv)))
    return skin_uv, glove_uv


# ---------------------------------------------------------------------------------------------- 2. mão nova

def hand_layout(sd):
    """Articulações da mão (mundo): para cada dedo, [base dentro da palma, MCP, PIP, DIP, ponta]."""
    W, F, U, P, lp, w = sd.W, sd.F, sd.U, sd.P, sd.lp, sd.width
    def rot(v, axis, deg):
        return Matrix.Rotation(math.radians(deg), 3, axis) @ v
    fan = {"Index": 5, "Middle": 0, "Ring": -5, "Pinky": -11}
    mcp_u = {"Index": 0.37, "Middle": 0.12, "Ring": -0.13, "Pinky": -0.36}
    mcp_f = {"Index": 0.97, "Middle": 1.0, "Ring": 0.96, "Pinky": 0.87}
    segs = {"Index": (0.46, 0.28, 0.22), "Middle": (0.5, 0.31, 0.23), "Ring": (0.47, 0.29, 0.22), "Pinky": (0.37, 0.22, 0.19)}
    # comprimento dos dedos igual ao do modelo (pulso→ponta do médio)
    fl = max(0.6, min(1.1, (sd.hand_len - lp) / lp / 1.04))
    segs = {k: tuple(x * fl for x in v) for k, v in segs.items()}
    rad = {"Index": 0.112, "Middle": 0.117, "Ring": 0.108, "Pinky": 0.094}
    lay = {}
    for f in ("Index", "Middle", "Ring", "Pinky"):
        ang = math.radians(fan[f])                  # leque: o indicador abre para o lado do polegar (U)
        d = (F * math.cos(ang) + U * math.sin(ang)).normalized()
        mcp = W + F * lp * mcp_f[f] + U * w * mcp_u[f] - P * 0.002
        pts = [mcp - d * lp * 0.3, mcp]
        cur = mcp
        for k, s in enumerate(segs[f]):
            cur = cur + d * lp * s
            pts.append(cur.copy())
        r = rad[f] * w
        lay[f] = (pts, [r * 1.3, r * 1.18, r * 1.0, r * 0.92, r * 0.76])
    # polegar: sai da base da palma, para o lado do indicador e para o lado da palma
    cmc = W + F * lp * 0.2 + U * w * 0.3 + P * w * 0.1
    d0 = (F * 0.62 + U * 0.6 + P * 0.42).normalized()
    mcp = cmc + d0 * lp * 0.42
    d1 = (F * 0.85 + U * 0.38 + P * 0.25).normalized()
    ip = mcp + d1 * lp * 0.34 * max(0.8, fl)
    tip = ip + (F * 0.9 + U * 0.25 + P * 0.2).normalized() * lp * 0.3 * max(0.8, fl)
    rt = 0.125 * w
    lay["Thumb"] = ([cmc - d0 * lp * 0.1, cmc, mcp, ip, tip], [rt * 1.5, rt * 1.35, rt * 1.12, rt * 1.0, rt * 0.8])
    return lay


def skin_tube(name, pts, radii):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(p) for p in pts], [(i, i + 1) for i in range(len(pts) - 1)], [])
    ob = bpy.data.objects.new(name, me)
    link(ob)
    mod = ob.modifiers.new("skin", 'SKIN')
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i])
        sv.use_root = (i == 0)
    sub = ob.modifiers.new("sub", 'SUBSURF')
    sub.levels = 2
    select_only(ob)
    bpy.ops.object.convert(target='MESH')
    return ob


def ellipsoid(name, center, F, U, P, rf, ru, rp):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=1.0)
    ob = bpy.context.active_object
    ob.name = name
    basis = Matrix((F * rf, U * ru, P * rp)).transposed()
    m = basis.to_4x4()
    m.translation = center
    ob.matrix_world = m
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def rounded_box(name, center, F, U, P, lf, lu, lp_):
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    ob = bpy.context.active_object
    ob.name = name
    basis = Matrix((F * lf, U * lu, P * lp_)).transposed()
    m = basis.to_4x4()
    m.translation = center
    ob.matrix_world = m
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bev = ob.modifiers.new("bev", 'BEVEL')
    bev.width = min(lf, lu, lp_) * 0.48
    bev.segments = 5
    bpy.ops.object.modifier_apply(modifier="bev")
    return ob


def build_hand(sd, ru, rp):
    lay = hand_layout(sd)
    W, F, U, P, lp, w = sd.W, sd.F, sd.U, sd.P, sd.lp, sd.width
    parts = []
    # palma (achatada), base do polegar (eminência tenar) e pulso a entrar no antebraço
    th = min(rp * 1.6, w * 0.21)
    parts.append(rounded_box("palm", W + F * lp * 0.5 + P * th * 0.08, F, U, P, lp * 1.02, w * 0.9, th))
    parts.append(ellipsoid("thenar", W + F * lp * 0.3 + U * w * 0.26 + P * th * 0.35, F, U, P, lp * 0.32, w * 0.17, th * 0.62))
    parts.append(skin_tube("wrist", [W - F * 0.075, W - F * 0.02, W + F * lp * 0.22], [max(ru, rp) * 0.94, max(ru, rp) * 0.97, max(ru, rp) * 0.97]))
    # o tubo do pulso é redondo; achata-o na direção da palma
    wr = parts[-1]
    for v in wr.data.vertices:
        d = v.co - W
        v.co = v.co - P * d.dot(P) * (1.0 - rp / max(ru, rp))
    for f, (pts, radii) in lay.items():
        parts.append(skin_tube("f_" + f, pts, radii))
    select_only(*parts)
    bpy.ops.object.join()
    hand = bpy.context.active_object
    hand.name = "Hand_" + sd.side
    rem = hand.modifiers.new("remesh", 'REMESH')
    rem.mode = 'VOXEL'
    rem.voxel_size = max(0.0016, w * 0.022)
    rem.use_smooth_shade = True
    sm = hand.modifiers.new("smooth", 'CORRECTIVE_SMOOTH')
    sm.iterations = 6
    sm.use_only_smooth = True
    dec = hand.modifiers.new("dec", 'DECIMATE')
    dec.ratio = 0.3
    select_only(hand)
    for m in list(hand.modifiers):
        if m.type == 'DECIMATE':
            # alvo ~3000 faces
            dg = bpy.context.evaluated_depsgraph_get()
            n = len(hand.evaluated_get(dg).data.polygons)
            m.ratio = min(1.0, 3000.0 / max(1, n / max(m.ratio, 1e-3)))
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.shade_smooth()
    log(sd.side, "mão:", len(hand.data.polygons), "faces", "comprimento", round(sd.hand_len, 3))
    return hand, lay


# ---------------------------------------------------------------------------------------------- 3. esqueleto e pesos

def build_armature(sd, lay):
    s = sd.side
    ad = bpy.data.armatures.new("Rig_" + s)
    ao = bpy.data.objects.new("Rig_" + s, ad)
    link(ao)
    select_only(ao)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = ad.edit_bones

    def bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name)
        b.head, b.tail = head, tail
        # rolo: eixo Z do osso para o lado da palma
        b.align_roll(sd.P)
        if parent:
            b.parent = parent
            b.use_connect = connect
        return b

    ua = bone(s + "_UpperArm", sd.S, sd.E)
    fa = bone(s + "_ForeArm", sd.E, sd.W, ua, True)
    mid_mcp = lay["Middle"][0][1]
    hd = bone(s + "_Hand", sd.W, mid_mcp, fa, True)
    for f, (pts, radii) in lay.items():
        prev = hd
        for i in range(3):
            b = bone(f"{s}_{f}{i + 1}", pts[1 + i], pts[2 + i], prev, i > 0)
            prev = b
    bpy.ops.object.mode_set(mode='OBJECT')
    return ao


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length, t


def weight_arm_piece(piece, sd, rig):
    """Pesos do braço: ombro→cotovelo→pulso com transições suaves nas articulações."""
    names = [sd.side + "_UpperArm", sd.side + "_ForeArm", sd.side + "_Hand"]
    vg = [piece.vertex_groups.new(name=n) for n in names]
    L_up = (sd.E - sd.S).length
    for v in piece.data.vertices:
        p = v.co
        a_e = (p - sd.E).dot((sd.E - sd.S).normalized())    # >0 depois do cotovelo
        a_w = sd.along(p)                                     # >0 depois do pulso
        # cotovelo: transição de 6 cm; pulso: 3 cm
        t_e = max(0.0, min(1.0, (a_e + 0.03) / 0.06))
        # pulso: transição longa (o jogo encolhe a mão dos cartoons; o antebraço afina até ela)
        t_w = max(0.0, min(1.0, (a_w + 0.08) / 0.075)) ** 1.5
        w_up = 1.0 - t_e
        w_fa = t_e * (1.0 - t_w)
        w_hd = t_e * t_w
        for g, wv in zip(vg, (w_up, w_fa, w_hd)):
            if wv > 0.001:
                g.add([v.index], wv, 'REPLACE')


def weight_hand(hand, sd, rig, lay):
    """Pesos da mão por 'bone heat'; se falhar, pela distância aos segmentos de cada dedo."""
    select_only(hand, rig)
    bpy.context.view_layer.objects.active = rig
    try:
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
        ok = all(len(v.groups) > 0 for v in hand.data.vertices[:200])
    except Exception as e:
        log("bone heat falhou:", e)
        ok = False
    if ok:
        # o braço/cotovelo não deve mexer na mão
        for n in (sd.side + "_UpperArm",):
            g = hand.vertex_groups.get(n)
            if g:
                hand.vertex_groups.remove(g)
        hand.parent = None
        return
    hand.parent = None
    for m in list(hand.modifiers):
        hand.modifiers.remove(m)
    for g in list(hand.vertex_groups):
        hand.vertex_groups.remove(g)
    segs = [(sd.side + "_Hand", sd.W, lay["Middle"][0][1])]
    for f, (pts, radii) in lay.items():
        for i in range(3):
            segs.append((f"{sd.side}_{f}{i + 1}", pts[1 + i], pts[2 + i]))
    groups = {n: hand.vertex_groups.new(name=n) for n, a, b in segs}
    groups[sd.side + "_ForeArm"] = hand.vertex_groups.new(name=sd.side + "_ForeArm")
    for v in hand.data.vertices:
        ds = sorted(((seg_dist(v.co, a, b)[0], n) for n, a, b in segs))
        (d0, n0), (d1, n1) = ds[0], ds[1]
        w0 = 1.0 / max(d0, 1e-4) ** 4
        w1 = 1.0 / max(d1, 1e-4) ** 4
        if d1 > d0 * 1.6:
            w1 = 0.0
        tot = w0 + w1
        groups[n0].add([v.index], w0 / tot, 'ADD')
        if w1 > 0:
            groups[n1].add([v.index], w1 / tot, 'ADD')


def firm_palm(hand, sd):
    """A palma não deve acompanhar os dedos: antes dos nós, o peso das 1.as falanges passa para a mão."""
    hg = hand.vertex_groups.get(sd.side + "_Hand")
    if hg is None:
        return
    names = {g.index: g.name for g in hand.vertex_groups}
    for v in hand.data.vertices:
        a = sd.along(v.co)
        lim = sd.lp * 0.8
        if a >= lim:
            continue
        k = max(0.0, min(1.0, (lim - a) / (sd.lp * 0.12)))
        moved = 0.0
        for g in v.groups:
            n = names[g.group]
            if n.endswith("1") and "Thumb" not in n:
                take = g.weight * k
                g.weight -= take
                moved += take
        if moved > 0:
            hg.add([v.index], moved, 'ADD')


def set_hand_uvs(hand, skin_uv, glove_uv, gloves, sd):
    me = hand.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uvl = me.uv_layers.active.data
    gnames = {g.index: g.name for g in hand.vertex_groups}
    for poly in me.polygons:
        use_glove = False
        if gloves:
            # luva sem dedos: palma, costas e 1.ª falange
            dom = {}
            for vi in poly.vertices:
                for g in me.vertices[vi].groups:
                    n = gnames[g.group]
                    dom[n] = dom.get(n, 0) + g.weight
            top = max(dom, key=dom.get) if dom else ""
            use_glove = top.endswith("_Hand") or top.endswith("1") or top.endswith("_ForeArm")
        uv = glove_uv if use_glove else skin_uv
        for li in poly.loop_indices:
            uvl[li].uv = uv


# ---------------------------------------------------------------------------------------------- personagem

def build(name, outdir):
    fbx, tex, gloves = CHARS[name]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(CHAR, fbx))
    src = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    img = bpy.data.images.load(os.path.join(CHAR, tex))
    results = []
    for side in ("R", "L"):
        sd = Side(arm, side)
        log(name, side, "ombro→cotovelo", round((sd.E - sd.S).length, 3), "cotovelo→pulso", round((sd.W - sd.E).length, 3),
            "palma", round(sd.lp, 3), "largura", round(sd.width, 3))
        piece, armw, handw = cut_arm(src, arm, sd)
        ru, rp = forearm_radii(piece, sd)
        if ru is None:
            ru, rp = wrist_radii(src, sd, handw)
        skin_uv, glove_uv = pick_uvs(src, sd, img, gloves)
        hand, lay = build_hand(sd, ru, rp)
        rig = build_armature(sd, lay)
        weight_arm_piece(piece, sd, rig)
        weight_hand(hand, sd, rig, lay)
        firm_palm(hand, sd)
        set_hand_uvs(hand, skin_uv, glove_uv, gloves, sd)
        hand.data.materials.clear()
        for s in piece.data.materials:
            hand.data.materials.append(s)
        select_only(piece, hand)
        bpy.context.view_layer.objects.active = piece
        bpy.ops.object.join()
        piece.name = "Arm_" + side
        piece.data.name = "Arm_" + side
        # modificador de esqueleto
        for m in list(piece.modifiers):
            piece.modifiers.remove(m)
        mod = piece.modifiers.new("Armature", 'ARMATURE')
        mod.object = rig
        piece.parent = rig
        results.append((piece, rig))
    # apaga o personagem original
    for o in list(bpy.data.objects):
        if o not in [r for pr in results for r in pr]:
            bpy.data.objects.remove(o, do_unlink=True)
    os.makedirs(outdir, exist_ok=True)
    path = os.path.join(outdir, name + "_FPArms.fbx")
    select_only(*[r for pr in results for r in pr])
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=False, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE',
                             path_mode='STRIP', embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(outdir, name + "_FPArms.blend"))
    tris = sum(len(p.data.polygons) for p, r in results)
    log(name, "->", path, tris, "faces")
    return path


if __name__ == "__main__":
    names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(CHARS)
    names = [n for n in names if n in CHARS] or list(CHARS)
    for n in names:
        build(n, os.path.join(CHAR, n, "FP"))
