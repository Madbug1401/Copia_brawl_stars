"""
FPS-LAN · Mapa Pique-Gemas "Buracona" (ilha do Sal, Cabo Verde)
================================================================

Constrói numa cena própria ("BU_Buracona") a arena estilizada da Buracona: chão de cascalho vulcânico,
cristas e penedos de basalto (coberturas), o buraco central com profundidade real (borda rochosa, paredes
em gruta, água turquesa e fundo visível), arbustos do jogo nas zonas verdes da planta, marcadores de jogo
(spawns, waypoints, ponto de geração e aterragens das gemas), colisores simples e uma demonstração animada
da água e da geração de gemas. Exporta FBX e renderiza pré-visualizações.

É uma ADAPTAÇÃO PARA GAMEPLAY inspirada na Buracona, não uma reconstrução geográfica. A geração de gemas a
partir da água é ficção do jogo.

Requisitos: Blender 4.2 LTS ou mais recente (testado com 5.2.2). Unidades em metros.
Reutiliza as primitivas do script da Cidade Velha (../CidadeVelha/cidade_velha_pelourinho.py) e a porta do
gerador de arbustos do jogo (../CidadeVelha/leafbush_port.py).

Uso
---
    blender --background --python Buracona_Map.py -- --out ./out --export --save-blend --render

    Opções: --detail 1|2|3  --fps 30  --t-idle 1.5 --t-warn 1.8 --t-emerge 0.6 --t-arc 1.1 --t-float 3.0
            --samples 24 --res 1280  --anim-preview (sequência da água/gema)  --seed 1960

Escala: a planta de referência (800 × 960 px) é lida a 0,05 m/px → área jogável 40 × 48 m (a mesma ordem
de grandeza da Cidade Velha, para as mesmas velocidades de movimento). Eixos: Blender Z para cima; equipa
AZUL em -Y, VERMELHA em +Y. O Unity identifica tudo pelos NOMES (ver LEIA-ME.md).

Não apaga nada fora da cena "BU_Buracona".
"""

import bpy
import bmesh
import math
import os
import sys
import json
import random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//")
CV_DIR = os.path.normpath(os.path.join(HERE, "..", "CidadeVelha"))
if CV_DIR not in sys.path:
    sys.path.insert(0, CV_DIR)
import cidade_velha_pelourinho as cv   # primitivas bmesh, UV, materiais, colisores, animação, exportação
import leafbush_port                   # mesmo gerador de arbustos do jogo (LeafBush.cs)

# =============================================================================== parâmetros

PARAMS = dict(
    detail=2,
    fps=30,
    t_idle=1.5, t_warn=1.8, t_emerge=0.6, t_arc=1.1, t_float=3.0, t_tail=0.6,
    out_dir=None, export=True, save_blend=True, render=False, anim_preview=False,
    samples=24, res=1280, seed=1960,
)

SCENE_NAME = "BU_Buracona"
COLL_NAMES = ["Ground", "VolcanicRocks", "BuraconaPool", "Water", "Cover", "Bushes",
              "GemGenerator", "Gems", "Collision", "Markers", "Preview"]

PX = 0.05                 # metros por pixel da planta
ARENA_W, ARENA_L = 40.0, 48.0
WALL_H = 2.4              # cristas de basalto: acima dos olhos (1.6 m) e acima do salto (1.3 m)
LOW_ROCK_H = 1.1          # meia cobertura (salta-se para cima)
BUSH_H = 2.0              # tapa uma personagem em pé (CharacterController 1.8 m)

# Buracona (raios em metros, centro na origem)
WATER_R = 3.3             # boca da água
RIM_OUT = 4.75            # onde a rocha da borda encontra o chão = limite da barreira de movimento
WATER_Z = -1.1            # superfície da água
FLOOR_Z = -3.45           # fundo
LAND_R = 5.9              # aterragens: fora da borda (+ raio do jogador + folga)
SPAWN_Z = -1.45           # a gema nasce debaixo de água e emerge

C, MAT, MAT_TILE, OBJ = {}, {}, {}, {}
cv.C, cv.MAT, cv.MAT_TILE, cv.OBJ = C, MAT, MAT_TILE, OBJ     # as primitivas do cv usam estes dicionários


def parse_args():
    p = dict(PARAMS)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    flags = {"--export": "export", "--save-blend": "save_blend", "--render": "render", "--anim-preview": "anim_preview"}
    nums = {"--detail": "detail", "--fps": "fps", "--t-idle": "t_idle", "--t-warn": "t_warn", "--t-emerge": "t_emerge",
            "--t-arc": "t_arc", "--t-float": "t_float", "--samples": "samples", "--res": "res", "--seed": "seed"}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in flags:
            p[flags[a]] = True
        elif a in ("--no-export", "--no-blend"):
            p["export" if a == "--no-export" else "save_blend"] = False
        elif a == "--out":
            i += 1
            p["out_dir"] = argv[i]
        elif a in nums:
            i += 1
            v = float(argv[i])
            p[nums[a]] = int(v) if nums[a] in ("detail", "fps", "samples", "res", "seed") else v
        i += 1
    p["detail"] = max(1, min(3, int(p["detail"])))
    p["out_dir"] = os.path.abspath(p["out_dir"] or os.path.join(HERE or os.getcwd(), "out"))
    return p


def px(u, v):
    """Pixel da planta → metros (Blender XY)."""
    return ((u - 400.0) * PX, (480.0 - v) * PX)


def px_rect(u0, u1, v0, v1):
    (x0, y0), (x1, y1) = px(u0, v0), px(u1, v1)
    return ((x0 + x1) / 2, (y0 + y1) / 2, abs(x1 - x0), abs(y1 - y0))


def mirrors(cx, cy, mx=True, my=True):
    out = [(cx, cy)]
    if mx and abs(cx) > 1e-3:
        out.append((-cx, cy))
    if my and abs(cy) > 1e-3:
        out += [(x, -y) for (x, y) in list(out)]
    return out


def tag_of(x, y):
    return ("E" if x > 0.01 else "W" if x < -0.01 else "C") + ("N" if y > 0.01 else "S" if y < -0.01 else "C")


# =============================================================================== cena

def setup_scene(P):
    old = bpy.data.scenes.get(SCENE_NAME)
    if old is not None:
        for ob in list(old.objects):
            if len(ob.users_scene) <= 1:
                bpy.data.objects.remove(ob, do_unlink=True)
        for coll in list(old.collection.children_recursive):
            if coll.name.startswith("BU_") or coll.name.split(".")[0] in COLL_NAMES + ["Envolvente"]:
                bpy.data.collections.remove(coll)
        if len(bpy.data.scenes) > 1:
            bpy.data.scenes.remove(old)
        else:
            old.name = SCENE_NAME + "_old"
    sc = bpy.data.scenes.new(SCENE_NAME)
    if bpy.context.window is not None:
        bpy.context.window.scene = sc
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    sc.render.fps = P["fps"]
    root = bpy.data.collections.new("BU_Buracona")
    sc.collection.children.link(root)
    for n in COLL_NAMES:
        c = bpy.data.collections.new(n)
        root.children.link(c)
        C[n] = c
    C["Modules"] = C["Preview"]          # o setup_render do cv esconde "Modules"; aqui não há módulos
    C["Envolvente"] = bpy.data.collections.new("Envolvente")   # fundo sem jogo (mar, montes, salinas)
    C["Ground"].children.link(C["Envolvente"])
    # O cv.col_box e o cv.place usam C["Collision"]; o cv.marker recebe a collection
    return sc


# =============================================================================== texturas

def make_textures(P, folder):
    import numpy as np
    os.makedirs(folder, exist_ok=True)
    rng = np.random.default_rng(P["seed"])
    R = cv.TEX_RES
    tex = {}

    def save(name, rgb, alpha=None):
        rgb = np.clip(rgb, 0, 1)
        a = np.ones((R, R, 1), np.float32) if alpha is None else np.clip(alpha, 0, 1)[..., None]
        img = bpy.data.images.get(name)
        if img is not None:
            bpy.data.images.remove(img)
        img = bpy.data.images.new(name, R, R, alpha=alpha is not None)
        rgba = np.concatenate([rgb, a], axis=2)
        img.pixels.foreach_set(rgba[::-1].reshape(-1).astype(np.float32))
        path = os.path.join(folder, name + ".png")
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        img.filepath = path
        tex[name] = img

    y, x = np.mgrid[0:R, 0:R].astype(np.float32) / R

    # Cascalho vulcânico (chão): grãos escuros, manchas avermelhadas de óxido
    f1, f2, cid = cv._voronoi(R, 26, rng)
    edge = np.clip((f2 - f1) * 5.0, 0, 1)
    tone = (cid * 2654435761 % 1000).astype(np.float32) / 1000.0
    rust = np.clip((cv._noise(R, 3, rng, 4) - 0.45) * 3.0, 0, 1)
    g = 0.2 + 0.12 * tone + 0.06 * cv._noise(R, 12, rng)
    rgb = np.stack([g * (1.05 + 0.35 * rust), g * (0.97 + 0.08 * rust), g * (0.95 - 0.1 * rust)], 2)
    rgb = rgb * (0.84 + 0.16 * edge[..., None])
    speck = (rng.random((R, R)) > 0.985).astype(np.float32)[..., None]
    rgb = rgb * (1 - speck) + speck * 0.55
    save("T_Cascalho", rgb)

    # Areia clara (manchas no chão e fundo da piscina)
    n = cv._noise(R, 5, rng, 5)
    rip = 0.5 + 0.5 * np.sin((y * 18 + n * 3.0) * math.pi)
    s = 0.74 + 0.08 * n + 0.04 * rip
    save("T_Areia", np.stack([s * 1.02, s * 0.9, s * 0.72], 2))

    # Basalto: blocos com fissuras escuras e vesículas (poros)
    f1, f2, cid = cv._voronoi(R, 5, rng)
    crack = np.clip((f2 - f1) * 14.0, 0, 1)
    tone = (cid * 40503 % 997).astype(np.float32) / 997.0
    n = cv._noise(R, 6, rng, 5)
    n2 = cv._noise(R, 24, rng, 3)
    v = 0.42 + 0.1 * tone + 0.3 * n + 0.12 * n2
    pores_f1, _, _ = cv._voronoi(R, 55, rng)
    pores = (pores_f1 < 0.18).astype(np.float32) * (n2 > 0.45)
    v = v * (0.72 + 0.28 * crack) * (1 - 0.5 * pores)
    save("T_Basalto", np.stack([v * 1.04, v * 0.98, v * 0.94], 2))

    # Água: cáusticas suaves (linhas claras) que repetem
    w1 = np.sin((x * 6 + cv._noise(R, 2, rng) * 1.6) * math.tau) * np.sin((y * 5 + cv._noise(R, 2, rng) * 1.6) * math.tau)
    caust = np.clip(1 - np.abs(w1) * 3.2, 0, 1) ** 2
    base = 0.55 + 0.1 * cv._noise(R, 4, rng)
    save("T_Agua", np.stack([base * 0.55 + caust * 0.45, base * 0.95 + caust * 0.05, base * 0.95 + caust * 0.05], 2))

    # Espuma: manchas brancas com transparência
    n = cv._noise(R, 8, rng, 5)
    a = np.clip((n - 0.5) * 4.0, 0, 1)
    save("T_Espuma", np.ones((R, R, 3), np.float32), alpha=a)
    return tex


def make_materials(tex):
    T = tex.get
    m = cv.material
    m("MAT_Chao_Vulcanico", (0.72, 0.68, 0.66), T("T_Cascalho"), tile=3.0, rough=0.95)
    m("MAT_Areia", (0.7, 0.62, 0.5), T("T_Areia"), tile=4.0, rough=1.0)
    m("MAT_Basalto", (0.4, 0.35, 0.33), T("T_Basalto"), tile=2.2, rough=0.9)
    m("MAT_Basalto_Escuro", (0.24, 0.22, 0.22), T("T_Basalto"), tile=2.2, rough=0.9)
    m("MAT_Rocha_Borda", (0.5, 0.44, 0.4), T("T_Basalto"), tile=1.8, rough=0.85)
    m("MAT_Rocha_Molhada", (0.2, 0.22, 0.25), T("T_Basalto"), tile=1.8, rough=0.35)
    m("MAT_Fundo_Agua", (0.35, 0.85, 0.85), T("T_Areia"), tile=3.0, rough=0.6, emission=(0.1, 0.55, 0.6), strength=0.6)
    m("MAT_Agua", (0.3, 0.88, 0.92), T("T_Agua"), tile=3.5, rough=0.05, alpha=0.72)
    m("MAT_Espuma", (1.0, 1.0, 1.0), T("T_Espuma"), tile=1.5, rough=0.6, alpha=0.8)
    m("MAT_Brilho_Agua", (0.4, 1.0, 0.95), None, emission=(0.3, 1.0, 0.95), strength=0.5)
    m("MAT_Sal", (0.95, 0.95, 0.93), None, rough=0.7)
    m("MAT_Mar", (0.12, 0.55, 0.75), None, rough=0.15)
    m("MAT_Monte", (0.66, 0.52, 0.38), None, rough=1.0)
    m("MAT_Monte_Escuro", (0.32, 0.28, 0.26), None, rough=1.0)
    m("MAT_Madeira", (0.55, 0.4, 0.28), None)
    m("MAT_Spawn_Azul", (0.2, 0.5, 0.95), None, emission=(0.2, 0.5, 1.0), strength=0.4)
    m("MAT_Spawn_Vermelho", (0.95, 0.28, 0.22), None, emission=(1.0, 0.3, 0.2), strength=0.4)
    m("MAT_Marca_Aterragem", (0.75, 0.55, 1.0), None, emission=(0.7, 0.45, 1.0), strength=0.0)
    m("MAT_Marca_Aterragem_Apagada", (0.75, 0.55, 1.0), None, emission=(0.7, 0.45, 1.0), strength=0.0)
    m("MAT_Gema", (0.7, 0.4, 1.0), None, rough=0.1, emission=(0.62, 0.3, 1.0), strength=1.5)
    m("MAT_Faisca", (0.9, 1.0, 1.0), None, emission=(0.7, 1.0, 1.0), strength=6.0)
    m("MAT_Jogador_Demo", (0.25, 0.55, 1.0), None, rough=0.6)
    m("MAT_Colisor", (1.0, 0.2, 0.6), None, alpha=0.25)
    # Folhas dos arbustos do jogo (LeafBush): cor por vértice, como o M_BushLeaves do Unity (só pré-visualização)
    mm = m("MAT_Folhas_Arbusto", (1.0, 1.0, 1.0), None, rough=0.85)
    ca = mm.node_tree.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    mm.node_tree.links.new(ca.outputs[0], mm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    # Água: a textura desliza (mapeamento animado na demonstração)
    agua = MAT["MAT_Agua"]
    nt = agua.node_tree
    timg = next(n for n in nt.nodes if n.type == 'TEX_IMAGE')
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.name = "MapAgua"
    nt.links.new(tc.outputs["UV"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], timg.inputs["Vector"])


# =============================================================================== utilitários

def bm_join(dst, src):
    tmp = bpy.data.meshes.new("tmp")
    src.to_mesh(tmp)
    src.free()
    dst.from_mesh(tmp)
    bpy.data.meshes.remove(tmp)


def per_noise(seed, k=4):
    """Ruído periódico em ângulo (soma de senos) → bordas irregulares que fecham sem costura."""
    rng = random.Random(seed)
    terms = [(i + 1, rng.uniform(0, math.tau), rng.uniform(0.3, 1.0) / (i + 1)) for i in range(k)]
    tot = sum(a for _, _, a in terms)
    return lambda th: sum(a * math.sin(f * th + ph) for f, ph, a in terms) / tot


def col_mesh(name, bm, convex=True, layer_pass=False, loc=(0, 0, 0)):
    nm = ("COL_Mesh_" if convex else "COL_Tri_") + name + ("_Pass" if layer_pass else "")
    me = cv.mesh_from_bm(bm, nm, [MAT["MAT_Colisor"]])
    ob = cv.place(me, nm, C["Collision"], loc)
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


# =============================================================================== chão com o buraco

def ground(P, data):
    coll = C["Ground"]
    hw, hl = ARENA_W / 2 + 0.7, ARENA_L / 2 + 0.7
    n = 48 if P["detail"] >= 2 else 32

    def ground_bm():
        bm = bmesh.new()
        outer = [bm.verts.new(p) for p in ((-hw, -hl, 0), (hw, -hl, 0), (hw, hl, 0), (-hw, hl, 0))]
        inner = [bm.verts.new((RIM_OUT * 0.985 * math.cos(math.tau * i / n), RIM_OUT * 0.985 * math.sin(math.tau * i / n), 0))
                 for i in range(n)]
        edges = [bm.edges.new((outer[i], outer[(i + 1) % 4])) for i in range(4)]
        edges += [bm.edges.new((inner[i], inner[(i + 1) % n])) for i in range(n)]
        bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=edges)
        for f in bm.faces:
            if f.normal.z < 0:
                f.normal_flip()
        return bm

    me = cv.mesh_from_bm(ground_bm(), "GRD_Chao", [MAT["MAT_Chao_Vulcanico"]])
    cv.place(me, "GRD_Chao", coll)
    # Colisor do chão = a mesma superfície (com o buraco), malha não convexa
    col_mesh("Chao", ground_bm(), convex=False)
    # Saia de chão para lá dos limites (sem jogo)
    bm = bmesh.new()
    E = 34.0                                              # a leste o planalto acaba numa falésia sobre o mar
    for (cx, cy, sx, sy) in (((E - 100) / 2, hl + 50, 100 + E, 100), ((E - 100) / 2, -hl - 50, 100 + E, 100),
                             ((hw + E) / 2, 0, E - hw, 2 * hl), (-hw - 50, 0, 100, 2 * hl)):
        cv.add_box(bm, sx + 0.02, sy + 0.02, 0.1, center=(cx, cy, -0.12), mi=0)
    cv.place(cv.mesh_from_bm(bm, "GRD_Planicie", [MAT["MAT_Chao_Vulcanico"]]), "GRD_Planicie", C["Envolvente"])

    # Manchas de areia (planas, 1 cm acima do chão) — leitura das rotas
    rng = random.Random(P["seed"] + 3)
    bm = bmesh.new()
    for (cx, cy, r) in ((0, 15.6, 2.4), (0, -15.6, 2.4), (16.5, 0, 2.6), (-16.5, 0, 2.6),
                        (8.5, 12.0, 1.8), (-8.5, 12.0, 1.8), (8.5, -12.0, 1.8), (-8.5, -12.0, 1.8)):
        k = 14
        nz = per_noise(rng.randint(0, 9999), 3)
        vs = [bm.verts.new((cx + r * (1 + 0.25 * nz(math.tau * i / k)) * math.cos(math.tau * i / k),
                            cy + r * (1 + 0.25 * nz(math.tau * i / k)) * math.sin(math.tau * i / k), 0.012)) for i in range(k)]
        bm.faces.new(vs)
    cv.place(cv.mesh_from_bm(bm, "GRD_Areia", [MAT["MAT_Areia"]]), "GRD_Areia", coll)

    # Chão dos spawns (dentro dos corredores de rocha)
    for team, sy, mat in (("Blue", -1, "MAT_Spawn_Azul"), ("Red", 1, "MAT_Spawn_Vermelho")):
        bm = bmesh.new()
        cv.add_box(bm, 5.0, 5.4, 0.014, mi=0)
        cv.place(cv.mesh_from_bm(bm, f"GRD_Spawn_{team}", [MAT[mat]]), f"GRD_Spawn_{team}", coll, (0, sy * 21.0, 0))

    # Limites da arena (altos, invisíveis no jogo) — iguais à Cidade Velha
    W, L = ARENA_W, ARENA_L
    cv.col_box("Limite_E", (1.0, L + 4, 9.0), (W / 2 + 0.6, 0, 0))
    cv.col_box("Limite_W", (1.0, L + 4, 9.0), (-W / 2 - 0.6, 0, 0))
    cv.col_box("Limite_N", (W + 4, 1.0, 9.0), (0, L / 2 + 0.6, 0))
    cv.col_box("Limite_S", (W + 4, 1.0, 9.0), (0, -L / 2 - 0.6, 0))


# =============================================================================== a Buracona (buraco, água)

def pool(P, data):
    """Borda rochosa irregular, paredes em gruta (mais largas por baixo, como na fotografia), água e fundo."""
    det = P["detail"]
    n = (32, 56, 80)[det - 1]
    nr = per_noise(P["seed"] + 11, 5)     # irregularidade do raio
    nh = per_noise(P["seed"] + 23, 6)     # irregularidade da altura da borda
    # Perfil (raio, altura) de fora para dentro e de cima para baixo; índice → material
    prof = [
        (RIM_OUT, -0.02, "MAT_Rocha_Borda"), (4.5, 0.38, "MAT_Rocha_Borda"), (4.2, 0.78, "MAT_Rocha_Borda"),
        (3.9, 0.98, "MAT_Rocha_Borda"), (3.58, 0.86, "MAT_Basalto"), (3.36, 0.42, "MAT_Basalto"),
        (3.28, -0.15, "MAT_Basalto_Escuro"), (3.32, -0.7, "MAT_Basalto_Escuro"), (3.45, -1.1, "MAT_Rocha_Molhada"),
        (3.85, -1.85, "MAT_Rocha_Molhada"), (3.95, -2.55, "MAT_Rocha_Molhada"), (3.55, -3.1, "MAT_Fundo_Agua"),
        (2.5, -3.32, "MAT_Fundo_Agua"), (1.2, -3.42, "MAT_Fundo_Agua"), (0.0, FLOOR_Z, "MAT_Fundo_Agua"),
    ]
    mat_names = []
    for _, _, mn in prof:
        if mn not in mat_names:
            mat_names.append(mn)

    def pool_bm():
        bm = bmesh.new()
        rings = []
        for j, (r, z, _) in enumerate(prof):
            ring = []
            for i in range(n):
                th = math.tau * i / n
                rr = r
                zz = z
                if 0 < j < len(prof) - 1:
                    rr = r * (1 + 0.08 * nr(th) + 0.035 * nr(3 * th + 1.3))
                    if z > 0.2:
                        zz = z + (0.4 * nh(th) + 0.1 * nh(5 * th + 2.0)) * min(1.0, z)          # a borda sobe e desce
                if j == 0:
                    rr = RIM_OUT                                     # encosta exatamente ao chão
                ring.append(bm.verts.new((rr * math.cos(th), rr * math.sin(th), zz)) if r > 1e-4 or i == 0 else None)
            rings.append(ring)
        centre = rings[-1][0]
        for j in range(len(prof) - 1):
            a, b = rings[j], rings[j + 1]
            mi = mat_names.index(prof[j][2])
            for i in range(n):
                k = (i + 1) % n
                if b[0] is centre and b[1] is None:
                    f = bm.faces.new((a[i], a[k], centre))
                else:
                    f = bm.faces.new((a[i], a[k], b[k], b[i]))
                f.material_index = mi
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        # as normais têm de apontar para cima/para dentro do buraco (para o espaço aberto)
        up = sum((f.normal.z for f in bm.faces if f.calc_center_median().z > 0.3), 0.0)
        if up < 0:
            for f in bm.faces:
                f.normal_flip()
        return bm

    me = cv.mesh_from_bm(pool_bm(), "POOL_Buraco", [MAT[x] for x in mat_names])
    cv.place(me, "POOL_Buraco", C["BuraconaPool"])
    col_mesh("Buraco_Interior", pool_bm(), convex=False)

    # Barreira de movimento: cilindro com o raio exato onde a rocha começa; alto para nenhum salto/impulso
    # passar por cima; layer Ignore Raycast (_Pass) → os tiros atravessam por cima da água.
    bm = bmesh.new()
    cv.add_prism(bm, 32, RIM_OUT / math.cos(math.pi / 32), 30.0, z0=-5.0, mi=0, rot=0.0)
    col_mesh("Buraco_Barreira", bm, convex=True, layer_pass=True)
    data["hole"] = dict(center=[0, 0], radius=RIM_OUT, water_z=WATER_Z, floor_z=FLOOR_Z)

    # Penedos soltos em cima da borda (decoração dentro da barreira)
    rng = random.Random(P["seed"] + 5)
    bm = bmesh.new()
    k = (6, 11, 16)[det - 1]
    for i in range(k):
        th = math.tau * (i + rng.uniform(-0.3, 0.3)) / k
        r = rng.uniform(3.75, 4.3)
        h = 0.95 + 0.4 * nh(th)
        cv.add_icosphere(bm, rng.uniform(0.22, 0.42), (r * math.cos(th), r * math.sin(th), h + 0.05), mi=i % 2, subdiv=1,
                         squash=rng.uniform(0.55, 0.85))
    cv.place(cv.mesh_from_bm(bm, "ROCK_Borda_Penedos", [MAT["MAT_Rocha_Borda"], MAT["MAT_Basalto"]]), "ROCK_Borda_Penedos",
             C["VolcanicRocks"])


def water(P):
    coll = C["Water"]
    # Superfície (disco que entra um pouco nas paredes para não deixar fendas)
    bm = bmesh.new()
    n = 48
    c = bm.verts.new((0, 0, 0))
    ring = [bm.verts.new((3.75 * math.cos(math.tau * i / n), 3.75 * math.sin(math.tau * i / n), 0)) for i in range(n)]
    for i in range(n):
        bm.faces.new((c, ring[i], ring[(i + 1) % n]))
    agua = cv.place(cv.mesh_from_bm(bm, "WTR_Agua", [MAT["MAT_Agua"]]), "WTR_Agua", coll, (0, 0, WATER_Z))
    # Espuma localizada junto à rocha
    bm = bmesh.new()
    nr = per_noise(P["seed"] + 41, 4)
    inner = [bm.verts.new(((3.0 + 0.12 * nr(math.tau * i / n)) * math.cos(math.tau * i / n),
                           (3.0 + 0.12 * nr(math.tau * i / n)) * math.sin(math.tau * i / n), 0)) for i in range(n)]
    outer = [bm.verts.new((3.62 * math.cos(math.tau * i / n), 3.62 * math.sin(math.tau * i / n), 0)) for i in range(n)]
    for i in range(n):
        k = (i + 1) % n
        bm.faces.new((inner[i], inner[k], outer[k], outer[i]))
    cv.place(cv.mesh_from_bm(bm, "WTR_Espuma", [MAT["MAT_Espuma"]]), "WTR_Espuma", coll, (0, 0, WATER_Z + 0.012))
    # Brilho turquesa por baixo da superfície (emissor do jogo: pulsa e anuncia a próxima gema)
    bm = bmesh.new()
    cv.add_prism(bm, 32, 2.4, 0.04, z0=-0.02, r_top=2.2, mi=0)
    brilho = cv.place(cv.mesh_from_bm(bm, "GEN_Agua_Brilho", [MAT["MAT_Brilho_Agua"]]), "GEN_Agua_Brilho", coll,
                      (0, 0, WATER_Z - 0.55))
    OBJ["agua"], OBJ["brilho"] = agua, brilho


# =============================================================================== rocha (coberturas)

def lava_block(rng, sx, sy, h, det):
    """Bloco de lava facetado: casco convexo de pontos aleatórios dentro da caixa sx × sy × h (base cheia)."""
    b = bmesh.new()
    pts = []
    for cx in (-1, 1):
        for cy in (-1, 1):
            pts.append((cx * sx / 2 * rng.uniform(0.9, 1.0), cy * sy / 2 * rng.uniform(0.9, 1.0), -0.05))
            pts.append((cx * sx / 2 * rng.uniform(0.82, 1.0), cy * sy / 2 * rng.uniform(0.82, 1.0), h * rng.uniform(0.3, 0.6)))
    for _ in range(6 + 3 * det):
        pts.append((rng.uniform(-0.42, 0.42) * sx, rng.uniform(-0.42, 0.42) * sy, h * rng.uniform(0.82, 1.0)))
    for _ in range(4 + 2 * det):
        side = rng.choice((-1, 1))
        if rng.random() < 0.5:
            pts.append((side * sx / 2 * rng.uniform(0.85, 1.0), rng.uniform(-0.5, 0.5) * sy, h * rng.uniform(0.2, 0.85)))
        else:
            pts.append((rng.uniform(-0.5, 0.5) * sx, side * sy / 2 * rng.uniform(0.85, 1.0), h * rng.uniform(0.2, 0.85)))
    vs = [b.verts.new(p) for p in pts]
    bmesh.ops.convex_hull(b, input=vs)
    for v in [v for v in b.verts if not v.link_faces]:
        b.verts.remove(v)
    return b


def lava_ridge(name, cx, cy, sx, sy, h, seed, det):
    """Crista de basalto irregular (blocos facetados) dentro da pegada (sx × sy) do colisor."""
    rng = random.Random(seed)
    long_x = sx >= sy
    L, Wd = (sx, sy) if long_x else (sy, sx)
    n = max(1, round(L / 1.6))
    seg = L / n
    bm = bmesh.new()
    for i in range(n):
        hh = h * rng.uniform(0.9, 1.08)
        b = lava_block(rng, seg * 1.12, Wd * rng.uniform(0.9, 1.0), hh, det)
        mi = 1 if rng.random() < 0.35 else 0
        for f in b.faces:
            f.material_index = mi
        off = (i + 0.5) * seg - L / 2
        if long_x:
            bmesh.ops.translate(b, verts=b.verts, vec=(off, 0, 0))
        else:
            bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'Z'))
            bmesh.ops.translate(b, verts=b.verts, vec=(0, off, 0))
        bm_join(bm, b)
    me = cv.mesh_from_bm(bm, "CVR_" + name, [MAT["MAT_Basalto"], MAT["MAT_Basalto_Escuro"]])
    ob = cv.place(me, "CVR_" + name, C["Cover"], (cx, cy, 0))
    cv.col_box(name, (sx, sy, h + 0.1), (cx, cy, 0))
    return ob


def boulder(name, cx, cy, sx, sy, h, seed, coll=None, collide=True):
    """Penedo de lava: um bloco facetado que preenche a pegada (o colisor fica por dentro dele) + 1–2 lascas."""
    rng = random.Random(seed)
    bm = bmesh.new()
    b = lava_block(rng, sx, sy, h * rng.uniform(0.8, 0.95), 2)
    bm_join(bm, b)
    for i in range(1 if min(sx, sy) < 2 else 2):
        rx = sx * rng.uniform(0.22, 0.32)
        ry = sy * rng.uniform(0.22, 0.32)
        ox = rng.uniform(-sx / 2 + rx, sx / 2 - rx)
        oy = rng.uniform(-sy / 2 + ry, sy / 2 - ry)
        b = lava_block(rng, 2 * rx, 2 * ry, h * rng.uniform(1.0, 1.08), 2)
        bmesh.ops.translate(b, verts=b.verts, vec=(ox, oy, 0))
        for f in b.faces:
            f.material_index = 1
        bm_join(bm, b)
    ob = cv.place(cv.mesh_from_bm(bm, "CVR_" + name, [MAT["MAT_Basalto"], MAT["MAT_Basalto_Escuro"]]), "CVR_" + name,
                  coll or C["Cover"], (cx, cy, 0))
    if collide:
        cv.col_box(name, (sx * 0.9, sy * 0.9, h), (cx, cy, 0))
    return ob


def cover(P, data):
    det = P["detail"]
    seed = P["seed"]
    # Cristas da planta (barras laranja): (nome, retângulo em px, espelhar X)
    bars = [
        ("Corredor", (317, 347, 0, 125), True),       # paredes do corredor do spawn
        ("Canto_V", (145, 175, 0, 75), True),
        ("Canto_H", (73, 245, 127, 158), True),
        ("Lado_V", (145, 175, 217, 303), True),
    ]
    k = 0
    for name, (u0, u1, v0, v1), mx in bars:
        cx, cy, sx, sy = px_rect(u0, u1, v0, v1)
        for (x, y) in mirrors(cx, cy, mx, True):
            lava_ridge(f"{name}_{tag_of(x, y)}", x, y, sx, sy, WALL_H, seed + k, det)
            k += 1
    # Bolso central (duas barras com arbustos no meio): centrado em X
    for name, (v0, v1) in (("Bolso_Ext", (193, 226)), ("Bolso_Int", (295, 328))):
        _, cy, _, sy = px_rect(316, 488, v0, v1)
        for y in (cy, -cy):
            lava_ridge(f"{name}_{tag_of(0, y)}", 0.0, y, 8.6, sy, WALL_H, seed + k, det)
            k += 1
    # Penedos laterais: cortam a linha de tiro de borda a borda pelo meio do mapa (e por cima da água)
    for x in (12.5, -12.5):
        lava_ridge(f"Penedo_Lateral_{tag_of(x, 0)}", x, 0.0, 2.4, 6.0, WALL_H, seed + 100 + k, det)
        k += 1
    # Penedos altos nas diagonais do anel: partem as linhas verticais entre o bolso central e os cantos
    for (x, y) in mirrors(5.85, 6.2):
        boulder(f"Penedo_Anel_{tag_of(x, y)}", x, y, 3.1, 1.6, WALL_H, seed + 150 + k)
        k += 1
    # Penedos à frente das bases: partem a linha transversal entre o bolso e as cristas dos cantos
    for (x, y) in mirrors(9.5, 15.2):
        boulder(f"Penedo_Frente_{tag_of(x, y)}", x, y, 1.4, 1.2, WALL_H, seed + 180 + k)
        k += 1
    # Rochas baixas (meia cobertura, salta-se para cima) nas rotas laterais, à altura da água
    for (x, y) in mirrors(8.4, 0.0, True, False):
        boulder(f"Rocha_Baixa_{tag_of(x, y)}", x, y, 1.2, 1.8, LOW_ROCK_H, seed + 200 + k)
        k += 1


def scatter_rocks(P):
    """Pedras pequenas decorativas (sem colisão, < 25 cm: não prendem o CharacterController)."""
    rng = random.Random(P["seed"] + 77)
    bm = bmesh.new()
    count = (40, 90, 160)[P["detail"] - 1]
    placed = 0
    while placed < count:
        x, y = rng.uniform(-19.5, 19.5), rng.uniform(-23.5, 23.5)
        if math.hypot(x, y) < RIM_OUT + 0.5:
            continue
        cv.add_icosphere(bm, rng.uniform(0.07, 0.2), (x, y, 0.0), mi=placed % 2, subdiv=1, squash=0.5)
        placed += 1
    cv.place(cv.mesh_from_bm(bm, "ROCK_Pedras_Soltas", [MAT["MAT_Basalto"], MAT["MAT_Basalto_Escuro"]]),
             "ROCK_Pedras_Soltas", C["VolcanicRocks"])


# =============================================================================== arbustos (zonas verdes da planta)

def bush(name, cx, cy, w, d, seed, density, data):
    zname = f"BUSH_{name}_S{seed}_D{int(round(density * 100))}"
    bm = bmesh.new()
    cv.add_box(bm, w, d, BUSH_H, mi=0)
    zone = cv.place(cv.mesh_from_bm(bm, zname, [MAT["MAT_Colisor"]]), zname, C["Bushes"], (cx, cy, 0))
    zone.display_type = 'WIRE'
    zone.hide_render = True
    V, Cl, T = leafbush_port.generate((w, BUSH_H, d), seed, density)
    verts = [(x, z, y) for (x, y, z) in V]                  # Y do Unity ↔ Z do Blender (espelho: inverte os triângulos)
    faces = [(T[i], T[i + 2], T[i + 1]) for i in range(0, len(T), 3)]
    me = bpy.data.meshes.new("BSHV_" + name)
    me.from_pydata(verts, [], faces)
    attr = me.color_attributes.new("Col", 'BYTE_COLOR', 'POINT')
    flat = []
    for c in Cl:
        flat.extend((c[0], c[1], c[2], 1.0))
    attr.data.foreach_set("color_srgb" if hasattr(attr.data[0], "color_srgb") else "color", flat)
    me.materials.append(MAT["MAT_Folhas_Arbusto"])
    me.update()
    cv.place(me, "BSHV_" + name, C["Bushes"], (cx, cy, 0))
    data["bushes"].append(dict(name=zname, center=[cx, cy], size=[w, d, BUSH_H], seed=seed, density=density))


def bushes(P, data):
    base = P["seed"] + 500
    k = 0

    def add(name, rect_px, density, mx=True, my=True, override=None):
        nonlocal k
        cx, cy, sx, sy = override or px_rect(*rect_px)
        for (x, y) in mirrors(cx, cy, mx, my):
            bush(f"{name}_{tag_of(x, y)}", x, y, sx, sy, base + k * 7, density, data)
            k += 1

    add("Canto", (7, 92, 4, 88), 0.5)                       # 2×2 círculos nos cantos
    add("Coluna", (12, 52, 242, 325), 0.7)                  # braço vertical do "L"
    # braço horizontal do "L" (13 m) em três arbustos
    _, cy, _, sy = px_rect(12, 272, 333, 416)
    for i, (x0, x1) in enumerate(((-19.4, -15.05), (-14.95, -10.75), (-10.65, -6.4))):
        add(f"Fila{i}", None, 0.4, override=((x0 + x1) / 2, cy, x1 - x0, sy))
    add("Bolso", None, 0.5, mx=False, override=(0.0, px_rect(335, 465, 243, 282)[1], 6.5, 2.6))


# =============================================================================== emissor e marcadores

def generator(P, data):
    coll = C["GemGenerator"]
    sp = cv.marker("GEM_Spawn", (0, 0, SPAWN_Z), coll, 0.4, 'SPHERE')
    lands = []
    bm = bmesh.new()
    cv.add_torus(bm, 0.42, 0.03, seg=24, sides=4, mi=0)
    me_mark = cv.mesh_from_bm(bm, "GEN_Marca_Aterragem", [MAT["MAT_Marca_Aterragem_Apagada"]])
    for i in range(8):
        a = math.radians(22.5 + 45 * i)                     # simétrico entre equipas (espelho em Y)
        p = (LAND_R * math.cos(a), LAND_R * math.sin(a), 0.6)
        lands.append(cv.marker(f"GEM_Land_{i:02d}", p, coll, 0.3, 'SINGLE_ARROW'))
        cv.place(me_mark, f"GEN_Marca_{i:02d}", coll, (p[0], p[1], 0.04))
    data["lands"] = [list(l.location) for l in lands]
    data["gem_spawn"] = list(sp.location)
    OBJ["spawn"], OBJ["lands"] = sp, lands


def markers(P, data):
    coll = C["Markers"]
    # Spawns dentro dos corredores de rocha (protegidos dos lados e, à frente, pelo bolso central)
    for team, sy in (("Blue", -1), ("Red", 1)):
        for i, (x, y) in enumerate(((-1.3, 22.6), (1.3, 22.6), (-1.3, 20.1), (1.3, 20.1), (0.0, 21.35))):
            m = cv.marker(f"SPAWN_{team}_{i:02d}", (x, sy * y, 0.05), coll, 0.8)
            m.rotation_euler = (0, 0, 0.0 if sy < 0 else math.pi)
            data["spawns"].append(dict(team=team, pos=[x, sy * y, 0.05]))
    wps = []
    for i in range(8):                                      # anel à volta da água
        a = math.tau * i / 8
        wps.append((6.9 * math.cos(a), 6.9 * math.sin(a)))
    for (x, y) in mirrors(8.0, 20.0) + mirrors(14.0, 19.2) + mirrors(7.5, 12.0) + mirrors(16.5, 12.5) + \
            mirrors(16.8, 0.0, True, False) + mirrors(9.0, 0.0, True, False) + mirrors(0.0, 16.0, False, True):
        wps.append((x, y))
    for i, (x, y) in enumerate(wps):
        cv.marker(f"WP_{i:02d}", (x, y, 0.1), coll, 0.3, 'PLAIN_AXES')
    data["waypoints"] = wps


# =============================================================================== envolvente (sem jogo)

def surroundings(P):
    coll = C["Envolvente"]
    det = P["detail"]
    rng = random.Random(P["seed"] + 900)
    hw, hl = ARENA_W / 2, ARENA_L / 2
    # Mar a leste (a Buracona fica na costa oeste do Sal; aqui o mar enquadra o lado direito) e a sul
    bm = bmesh.new()
    cv.add_box(bm, 300, 300, 0.1, center=(185.5, 0, -2.2), mi=0)
    cv.place(cv.mesh_from_bm(bm, "BG_Mar", [MAT["MAT_Mar"]]), "BG_Mar", coll)
    # Falésia de lava entre o planalto e o mar
    bm = bmesh.new()
    for i in range(22):
        y = -110 + i * 10.5
        cv.add_box(bm, rng.uniform(2.5, 4), 11, 2.4 + rng.uniform(-0.05, 0.25), center=(35.2 + rng.uniform(-0.6, 0.6), y, -2.4), mi=i % 2)
    cv.place(cv.mesh_from_bm(bm, "BG_Falesia", [MAT["MAT_Basalto_Escuro"], MAT["MAT_Basalto"]]), "BG_Falesia", coll)
    # Montes ao longe (Monte Grande / Monte Leão estilizados) e afloramentos de lava
    for i in range(8 + det * 3):
        a = rng.uniform(math.pi * 0.55, math.pi * 1.45)
        d = rng.uniform(70, 120)
        x, y = d * math.cos(a), d * math.sin(a)
        bm = bmesh.new()
        cv.add_icosphere(bm, rng.uniform(12, 28), (0, 0, -6), mi=0 if i % 3 else 1, subdiv=2, squash=rng.uniform(0.25, 0.45))
        cv.place(cv.mesh_from_bm(bm, f"BG_Monte_{i}", [MAT["MAT_Monte"], MAT["MAT_Monte_Escuro"]]), f"BG_Monte_{i}", coll,
                 (x, y, 0))
    for i in range(6 + det * 4):
        x = rng.choice([-1, 1]) * rng.uniform(hw + 4, hw + 9) if i % 2 else rng.uniform(-hw, hw)
        y = rng.uniform(-hl - 9, hl + 9) if i % 2 else rng.choice([-1, 1]) * rng.uniform(hl + 4, hl + 9)
        if x > hw + 9:
            continue
        boulder(f"BG_Lava_{i}", x, y, rng.uniform(2, 4), rng.uniform(2, 4), rng.uniform(1.0, 2.6), P["seed"] + 950 + i,
                coll=coll, collide=False)
    # Salinas ao longe (Pedra de Lume)
    bm = bmesh.new()
    for i in range(5):
        cv.add_box(bm, rng.uniform(10, 16), rng.uniform(7, 11), 0.05, center=(-60 + i * 4, 70 + rng.uniform(-6, 6) + i * 9, -0.1), mi=0)
    cv.place(cv.mesh_from_bm(bm, "BG_Salinas", [MAT["MAT_Sal"]]), "BG_Salinas", coll)
    # Postes de madeira com corda junto ao buraco ficam de fora: a borda é rocha (identidade da Buracona)


# =============================================================================== animação de demonstração

def animate(P, data):
    sc = bpy.context.scene
    fps = P["fps"]
    F = {}
    t = 1
    for ph in ("idle", "warn", "emerge", "arc", "float", "tail"):
        F[ph] = t
        t += max(1, round(P["t_" + ph] * fps))
    F["end"] = t
    sc.frame_start, sc.frame_end = 1, F["end"]
    for ph, label in (("idle", "1 Água em repouso"), ("warn", "2-3 Brilho, ondulação e bolhas"), ("emerge", "4 Emerge com salpico"),
                      ("arc", "5 Arco até à borda"), ("float", "6 Flutua e roda"), ("tail", "Fim")):
        sc.timeline_markers.new(label, frame=F[ph])

    # 1 · água em repouso: a textura desliza e a superfície oscila uns milímetros (só Blender: animação de material)
    mp = MAT["MAT_Agua"].node_tree.nodes["MapAgua"]
    mp.inputs["Location"].default_value = (0, 0, 0)
    mp.inputs["Location"].keyframe_insert("default_value", frame=1)
    mp.inputs["Location"].default_value = (0.25, 0.12, 0)
    mp.inputs["Location"].keyframe_insert("default_value", frame=F["end"])
    agua = OBJ["agua"]
    per = max(2, round(2.4 * fps))
    for i in range(0, F["end"] + per, per // 2):
        cv.key(agua, i + 1, loc=(0, 0, WATER_Z + (0.015 if (i // (per // 2)) % 2 else -0.015)))
    act = agua.animation_data.action
    act.name = "ACT_Agua_Repouso"

    # 2 · brilho turquesa por baixo da superfície
    b = MAT["MAT_Brilho_Agua"]
    cv.mat_key(b, F["idle"], 0.5)
    cv.mat_key(b, F["warn"], 0.5)
    cv.mat_key(b, F["emerge"], 4.0)
    cv.mat_key(b, F["arc"] + 4, 1.0)
    cv.mat_key(b, F["end"], 0.5)

    # 3 · ondulação circular (anéis que se expandem) — Action exportável como referência
    bm = bmesh.new()
    cv.add_torus(bm, 0.5, 0.025, seg=40, sides=4)
    me_r = cv.mesh_from_bm(bm, "FX_Ondulacao", [MAT["MAT_Espuma"]])
    nw = F["emerge"] - F["warn"]
    ring_len = max(6, round(0.75 * fps))
    for k in range(3):
        o = cv.place(me_r, f"FX_Ondulacao_{k}", C["Preview"], (0, 0, WATER_Z + 0.02))
        cv.key(o, 1, scale=0.0)
        start = F["warn"] + k * max(1, nw // 3)
        cv.key(o, start, scale=0.0)
        cv.key(o, start + 1, scale=0.3)
        cv.key(o, start + ring_len, scale=3.4)
        cv.key(o, start + ring_len + 1, scale=0.0)
        cv.key(o, start + nw, scale=0.0)
        cv.key(o, start + nw + 1, scale=0.3)
        cv.key(o, start + nw + ring_len, scale=3.4)
        cv.key(o, start + nw + ring_len + 1, scale=0.0)
        o.animation_data.action.name = f"ACT_Ondulacao_{k}"
        OBJ.setdefault("ripples", []).append(o)

    # bolhas (sistema de partículas: só Blender — no Unity são reconstruídas com o Fx)
    bm = bmesh.new()
    cv.add_icosphere(bm, 0.05, (0, 0, 0), mi=0, subdiv=1)
    bub = cv.place(cv.mesh_from_bm(bm, "FX_Bolha", [MAT["MAT_Faisca"]]), "FX_Bolha", C["Preview"], (0, 0, -30))
    bm = bmesh.new()
    cv.add_prism(bm, 16, 2.0, 0.02)
    em = cv.place(cv.mesh_from_bm(bm, "FX_Emissor_Bolhas", [MAT["MAT_Faisca"]]), "FX_Emissor_Bolhas", C["Preview"],
                  (0, 0, WATER_Z - 0.45))
    em.hide_render = True
    mod = em.modifiers.new("Bolhas", 'PARTICLE_SYSTEM')
    ps = mod.particle_system.settings
    ps.name = "PS_Bolhas"
    ps.count = 60 * P["detail"]
    ps.frame_start = F["warn"] + 6
    ps.frame_end = F["emerge"] + 2
    ps.lifetime = max(12, round(0.6 * fps))
    ps.emit_from = 'FACE'
    ps.normal_factor = 0.4
    ps.effector_weights.gravity = -0.12
    ps.render_type = 'OBJECT'
    ps.instance_object = bub
    ps.size_random = 0.6
    em.show_instancer_for_render = False

    # 4 · a gema emerge (de baixo de água) com um salpico curto
    me_gem = cv.gem_mesh()
    gem = cv.place(me_gem, "Demo_Gema", C["Gems"], (0, 0, SPAWN_Z))
    land = Vector(data["lands"][5])                         # aterragem do lado azul (-Y)
    start = Vector((0, 0, SPAWN_Z))
    cv.key(gem, 1, loc=start, scale=0.0)
    cv.key(gem, F["emerge"], loc=start, rot=(0, 0, 0), scale=0.0)
    cv.key(gem, F["arc"], loc=Vector((0, 0, WATER_Z + 0.55)), rot=(0, 0, math.pi), scale=1.0)
    # 5 · arco por cima da borda até à aterragem
    p0 = Vector((0, 0, WATER_Z + 0.55))
    n = 10
    for i in range(1, n + 1):
        u = i / n
        p = p0.lerp(land, u) + Vector((0, 0, 3.0 * 4 * u * (1 - u)))
        cv.key(gem, F["arc"] + u * (F["float"] - F["arc"]), loc=p, rot=(0, 0, math.pi + u * math.tau * 1.5), scale=1.0)
    # 6 · flutua e roda
    nf = F["tail"] - F["float"]
    for i in range(13):
        u = i / 12
        cv.key(gem, F["float"] + u * nf, loc=land + Vector((0, 0, 0.12 * math.sin(u * math.tau * 1.5))),
               rot=(0, 0, math.pi * 4 + u * math.tau * 1.2), scale=1.0)
    cv.set_interp(gem.animation_data.action, 'LINEAR')
    gem.animation_data.action.name = "ACT_Demo_Gema_Buracona"

    # salpico: coroa de gotas que sobem e caem
    bm = bmesh.new()
    cv.add_icosphere(bm, 0.07, (0, 0, 0), mi=0, subdiv=1)
    me_d = cv.mesh_from_bm(bm, "FX_Gota", [MAT["MAT_Espuma"]])
    rng = random.Random(4)
    for i in range(12):
        a = math.tau * i / 12
        d = Vector((math.cos(a), math.sin(a), 0))
        o = cv.place(me_d, f"FX_Gota_{i}", C["Preview"], (0, 0, WATER_Z))
        cv.key(o, F["emerge"] - 1, loc=Vector((0, 0, WATER_Z)), scale=0.0)
        cv.key(o, F["emerge"], loc=Vector((0, 0, WATER_Z + 0.05)), scale=1.0)
        cv.key(o, F["emerge"] + 6, loc=Vector((0, 0, WATER_Z)) + d * 0.7 + Vector((0, 0, rng.uniform(0.6, 1.0))), scale=1.0)
        cv.key(o, F["emerge"] + 14, loc=Vector((0, 0, WATER_Z)) + d * 1.2, scale=0.0)

    # marca de aterragem acende quando a gema vai cair ali
    mk = MAT["MAT_Marca_Aterragem"]
    mark = bpy.data.objects.get("GEN_Marca_05")
    mark.data = mark.data.copy()
    mark.data.materials[0] = mk
    cv.mat_key(mk, F["arc"] - 1, 0.0)
    cv.mat_key(mk, F["arc"] + 4, 6.0)
    cv.mat_key(mk, F["float"] + 8, 6.0)
    cv.mat_key(mk, F["float"] + 20, 0.0)

    # jogador azul de demonstração a caminho da gema
    bm = bmesh.new()
    cv.add_lathe(bm, [(0.0, 0.0), (0.3, 0.08), (0.4, 0.4), (0.4, 1.4), (0.3, 1.72), (0.0, 1.8)], 12, mi=0)
    pl = cv.place(cv.mesh_from_bm(bm, "Demo_Jogador_Azul", [MAT["MAT_Jogador_Demo"]]), "Demo_Jogador_Azul", C["Preview"])
    a0 = Vector((-6.5, -10.5, 0))
    stop = Vector((land.x - 0.8, land.y - 0.9, 0))
    cv.key(pl, 1, loc=a0)
    cv.key(pl, F["end"], loc=stop)
    return F


# =============================================================================== câmaras, luz, mundo

def setup_preview(P, F):
    coll = C["Preview"]
    sc = bpy.context.scene
    sun = bpy.data.lights.new("LGT_Sol", 'SUN')
    sun.energy = 4.8
    sun.color = (1.0, 0.95, 0.86)
    sun.angle = math.radians(1.5)
    o = bpy.data.objects.new("LGT_Sol", sun)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(38), 0, math.radians(-30))
    fill = bpy.data.lights.new("LGT_Ceu", 'SUN')
    fill.energy = 0.9
    fill.color = (0.7, 0.82, 1.0)
    o = bpy.data.objects.new("LGT_Ceu", fill)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(-60), 0, math.radians(150))
    w = bpy.data.worlds.get("WRL_Buracona") or bpy.data.worlds.new("WRL_Buracona")
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.55, 0.76, 0.97, 1)
    bg.inputs[1].default_value = 1.0
    sc.world = w
    cams = {}

    def cam(name, loc, target, lens=35.0, ortho=None):
        cd = bpy.data.cameras.new(name)
        cd.lens = lens
        cd.clip_end = 600
        if ortho:
            cd.type = 'ORTHO'
            cd.ortho_scale = ortho
        ob = bpy.data.objects.new(name, cd)
        coll.objects.link(ob)
        ob.location = Vector(loc)
        if not ortho:
            cv.look_at(ob, target)
        cams[name] = ob
        return ob

    cam("CAM_Planta", (0, 0, 60), (0, 0, 0), ortho=ARENA_L + 2.5)
    cam("CAM_Geral", (24, -33, 21), (0, -1, -1), lens=28)
    cam("CAM_Emissor", (0.6, -6.6, 2.7), (0, -1.6, -0.9), lens=22)
    sc.camera = cams["CAM_Emissor"]
    return cams


# =============================================================================== principal

def main():
    P = parse_args()
    random.seed(P["seed"])
    out = P["out_dir"]
    os.makedirs(out, exist_ok=True)
    sc = setup_scene(P)
    with bpy.context.temp_override(scene=sc, view_layer=sc.view_layers[0]):
        run(P, sc, out)
    print("[BU] Concluído.")


def run(P, sc, out):
    tex = make_textures(P, os.path.join(out, "Textures"))
    make_materials(tex)
    data = dict(boxes=[], spawns=[], lands=[], waypoints=[], bushes=[], arena=dict(width=ARENA_W, length=ARENA_L))
    ground(P, data)
    pool(P, data)
    water(P)
    cover(P, data)
    scatter_rocks(P)
    bushes(P, data)
    generator(P, data)
    markers(P, data)
    surroundings(P)
    F = animate(P, data)
    cams = setup_preview(P, F)
    for ob in C["Collision"].objects:
        ob.hide_render = True

    # Dados para a análise de rotas (colisores bloqueantes; a barreira do buraco é um círculo)
    for ob in C["Collision"].objects:
        if ob.name.startswith("COL_Tri_"):
            continue
        if "Barreira" in ob.name:
            data["boxes"].append(dict(name=ob.name, kind="circle", loc=[0, 0, 0], radius=RIM_OUT, rot_z=0.0, size=[0, 0, 30.0]))
            continue
        data["boxes"].append(dict(name=ob.name, kind="box", loc=list(ob.location), rot_z=ob.rotation_euler.z,
                                  size=[ob.dimensions.x, ob.dimensions.y, ob.dimensions.z]))
    data["obstacle_radius"] = RIM_OUT
    data["frames"] = F
    data["params"] = {k: v for k, v in P.items() if k != "out_dir"}
    with open(os.path.join(out, "buracona_layout.json"), "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=1)

    tris = {}
    for n in COLL_NAMES:
        tris[n] = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in C[n].all_objects if o.type == 'MESH')
    print("[BU] Triângulos por collection:", tris)

    if P["export"]:
        fbx = os.path.join(out, "FBX")
        os.makedirs(fbx, exist_ok=True)
        sc.frame_set(0)
        static = [o for n in ("Ground", "VolcanicRocks", "BuraconaPool", "Cover", "Bushes", "Markers", "Collision")
                  for o in C[n].all_objects if not o.name.startswith("BSHV_")]
        cv.export_fbx(os.path.join(fbx, "Buracona_Static.fbx"), static)
        cv.export_fbx(os.path.join(fbx, "Buracona_GemGenerator.fbx"), list(C["GemGenerator"].all_objects))
        cv.export_fbx(os.path.join(fbx, "Buracona_Agua.fbx"), list(C["Water"].all_objects))
        cv.export_fbx(os.path.join(fbx, "Buracona_Emissor_Anim.fbx"), OBJ.get("ripples", []), anim=True)
        print("[BU] FBX exportados para", fbx)

    sc.frame_set(F["float"] + 20)
    if P["save_blend"]:
        path = os.path.join(out, "Buracona.blend")
        bpy.ops.wm.save_as_mainfile(filepath=path, check_existing=False, copy=False)
        try:
            bpy.ops.file.make_paths_relative()
            bpy.ops.file.pack_all()                   # texturas dentro do .blend: abre-se em qualquer pasta
            bpy.ops.wm.save_mainfile(compress=True)
        except Exception as e:
            print("[BU] Aviso: caminhos relativos:", e)
        print("[BU] .blend guardado em", path)

    if P["render"] or P["anim_preview"]:
        cv.setup_render(P)
        C["Preview"].hide_render = False
        C["Collision"].hide_render = True
        rdir = os.path.join(out, "Renders")
        os.makedirs(rdir, exist_ok=True)
    if P["render"]:
        R = P["res"]
        cams["CAM_Planta"].data.ortho_scale = ARENA_L + 2.5
        cv.render_still(cams["CAM_Planta"], os.path.join(rdir, "planta_rotas.png"), int(R * (ARENA_W + 2.5) / (ARENA_L + 2.5)), R,
                        P["samples"], frame=F["float"] + 20)
        cv.render_still(cams["CAM_Geral"], os.path.join(rdir, "perspetiva_geral.png"), R, int(R * 0.5625), P["samples"])
        bpy.context.scene.frame_set(1)
        for ph, f in (("1_repouso", F["idle"] + 10), ("2_brilho_ondulacao", F["warn"] + round((F["emerge"] - F["warn"]) * 0.7)),
                      ("3_emerge", F["emerge"] + 6), ("4_arco", (F["arc"] + F["float"]) // 2), ("5_flutua", F["float"] + 30)):
            cv.render_still(cams["CAM_Emissor"], os.path.join(rdir, f"emissor_{ph}.png"), 800, 450, max(12, P["samples"] // 2), frame=f)
    if P["anim_preview"]:
        sc = bpy.context.scene
        sc.camera = cams["CAM_Emissor"]
        sc.render.resolution_x, sc.render.resolution_y = 480, 270
        sc.cycles.samples = 8
        for f in range(1, F["end"] + 1):
            sc.frame_set(f)
            if f % 2 == 1:
                sc.render.filepath = os.path.join(rdir, "anim", f"agua_{f:04d}.png")
                bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
