"""
FPS-LAN · Mapa Pique-Gemas "Cidade Velha — Pelourinho" (Santiago, Cabo Verde)
==============================================================================

Constrói, numa cena própria ("CV_Pelourinho"), o cenário estilizado da praça do Pelourinho,
os materiais e texturas procedurais, os colisores simples, os marcadores de jogo
(spawns, pontos de geração/aterragem das gemas, waypoints dos bots) e uma demonstração
animada da geração de gemas. Opcionalmente exporta FBX e renderiza pré-visualizações.

É uma ADAPTAÇÃO PARA GAMEPLAY, não uma reconstrução histórica. O emissor de gemas
(halo, anel de partículas, marcas de aterragem) é uma adição fictícia do jogo e está
separado do monumento na collection "GemGenerator".

Requisitos: Blender 4.2 LTS ou mais recente (testado com 5.2.2). Unidades em metros.

Uso
---
Dentro do Blender:  Scripting → Open → este ficheiro → Run Script
    (usa os PARAMS por defeito; os ficheiros vão para "<pasta do script>/out").

Linha de comandos:
    blender --background --python cidade_velha_pelourinho.py -- \
            --out ./out --detail 2 --export --save-blend --render

    Opções: --width 40 --length 50 --detail 1|2|3 --wall-height 2.6 --fps 30
            --t-idle 1.5 --t-warn 1.6 --t-emerge 0.7 --t-arc 1.1 --t-float 3.0 --t-collect 0.8
            --samples 24 --res 1280 --anim-preview (renderiza a sequência da gema)

Não apaga nada fora da cena "CV_Pelourinho" (se já existir, só essa cena e os objetos
que só ela usa são recriados).

Eixos: Blender Z para cima, equipa AZUL em -Y, equipa VERMELHA em +Y. A exportação FBX
usa "Apply Transform" (Y para cima no Unity). O mapa é simétrico em X e em Y e o Unity
identifica tudo pelos NOMES dos objetos (SPAWN_Blue_*, GEM_Land_*, COL_*), por isso uma
inversão de eixo no import não troca regras de jogo — mas confirma a orientação na mesma.
"""

import bpy
import bmesh
import math
import os
import sys
import json
import random
from mathutils import Vector, Matrix

# =============================================================================== parâmetros

PARAMS = dict(
    arena_width=40.0,     # X (metros) — largura jogável
    arena_length=50.0,    # Y (metros) — eixo spawn ↔ spawn
    detail=2,             # 1 = leve · 2 = normal · 3 = mais adereços/ornamentos
    wall_height=2.6,      # muros de cobertura (acima dos olhos de quem está no topo dos degraus: 0.9 + 1.6)
    fps=30,
    # duração (segundos) de cada fase da demonstração
    t_idle=1.5, t_warn=1.6, t_emerge=0.7, t_arc=1.1, t_float=3.0, t_collect=0.8, t_tail=0.8,
    out_dir=None,         # None → <pasta do script>/out
    export=True,
    save_blend=True,
    render=False,
    anim_preview=False,
    samples=24,
    res=1280,
    seed=1512,            # ano associado ao Pelourinho — só semente dos aleatórios
)

SCENE_NAME = "CV_Pelourinho"


def parse_args():
    p = dict(PARAMS)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    i = 0
    flags = {"--export": "export", "--save-blend": "save_blend", "--render": "render", "--anim-preview": "anim_preview"}
    nums = {"--width": "arena_width", "--length": "arena_length", "--detail": "detail", "--wall-height": "wall_height",
            "--fps": "fps", "--t-idle": "t_idle", "--t-warn": "t_warn", "--t-emerge": "t_emerge", "--t-arc": "t_arc",
            "--t-float": "t_float", "--t-collect": "t_collect", "--samples": "samples", "--res": "res", "--seed": "seed"}
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
    if not p["out_dir"]:
        base = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//")
        p["out_dir"] = os.path.join(base or os.getcwd(), "out")
    p["out_dir"] = os.path.abspath(p["out_dir"])
    return p


# =============================================================================== cena e collections

COLL_NAMES = ["Ground", "Pelourinho", "Buildings", "Cover", "Props", "GemGenerator", "Gems", "Collision", "Preview"]
C = {}      # nome lógico → collection
OBJ = {}    # objetos importantes


def setup_scene(P):
    old = bpy.data.scenes.get(SCENE_NAME)
    if old is not None:
        # Só remove o que pertence exclusivamente a esta cena
        for ob in list(old.objects):
            if len(ob.users_scene) <= 1:
                bpy.data.objects.remove(ob, do_unlink=True)
        for coll in list(old.collection.children_recursive):
            if coll.name.startswith("CV_") or coll.name.split(".")[0] in COLL_NAMES:
                if all(s == old for s in bpy.data.scenes if coll.name in s.collection.children_recursive):
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
    root = bpy.data.collections.new("CV_CidadeVelha")
    sc.collection.children.link(root)
    for n in COLL_NAMES:
        c = bpy.data.collections.new(n)
        root.children.link(c)
        C[n] = c
    C["Gameplay"] = bpy.data.collections.new("Gameplay")  # marcadores (spawns, waypoints) dentro de Ground
    C["Ground"].children.link(C["Gameplay"])
    C["Modules"] = bpy.data.collections.new("Modules")     # peças-mãe dos módulos (escondidas, não exportadas)
    C["Preview"].children.link(C["Modules"])
    return sc


# =============================================================================== texturas procedurais (numpy)

TEX_RES = 512


def _noise(res, cells, rng, octaves=4):
    import numpy as np
    out = np.zeros((res, res), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = max(2, cells * (2 ** o))
        g = rng.random((n, n)).astype(np.float32)
        x = np.linspace(0, n, res, endpoint=False)
        i0 = np.floor(x).astype(int)
        f = x - i0
        f = f * f * (3 - 2 * f)
        i1 = (i0 + 1) % n
        a = g[i0][:, i0] * (1 - f)[None, :] + g[i0][:, i1] * f[None, :]
        b = g[i1][:, i0] * (1 - f)[None, :] + g[i1][:, i1] * f[None, :]
        out += amp * (a * (1 - f)[:, None] + b * f[:, None])
        total += amp
        amp *= 0.5
    return out / total


def _voronoi(res, cells, rng):
    """Distâncias F1/F2 numa grelha jitter que repete (tileable). Devolve (f1, f2, id)."""
    import numpy as np
    pts = rng.random((cells, cells, 2)).astype(np.float32) * 0.8 + 0.1
    y, x = np.mgrid[0:res, 0:res].astype(np.float32) / res * cells
    cx, cy = np.floor(x).astype(int), np.floor(y).astype(int)
    f1 = np.full((res, res), 9.0, np.float32)
    f2 = np.full((res, res), 9.0, np.float32)
    cid = np.zeros((res, res), np.int32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx, ny = cx + dx, cy + dy
            p = pts[ny % cells, nx % cells]
            px, py = nx + p[..., 0], ny + p[..., 1]
            d = np.sqrt((x - px) ** 2 + (y - py) ** 2)
            ids = (ny % cells) * cells + (nx % cells)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            cid = np.where(closer, ids, cid)
            f1 = np.where(closer, d, f1)
    return f1, f2, cid


def make_textures(P, folder):
    import numpy as np
    os.makedirs(folder, exist_ok=True)
    rng = np.random.default_rng(P["seed"])
    R = TEX_RES
    tex = {}

    def save(name, rgb):
        rgb = np.clip(rgb, 0, 1)
        img = bpy.data.images.get(name)
        if img is None or tuple(img.size) != (R, R):
            if img is not None:
                bpy.data.images.remove(img)
            img = bpy.data.images.new(name, R, R, alpha=False)
        rgba = np.concatenate([rgb, np.ones((R, R, 1), np.float32)], axis=2)
        img.pixels.foreach_set(rgba[::-1].reshape(-1).astype(np.float32))
        path = os.path.join(folder, name + ".png")
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        img.filepath = path
        tex[name] = img

    # Calçada de basalto (pedra escura irregular com juntas de areia)
    f1, f2, cid = _voronoi(R, 10, rng)
    edge = np.clip((f2 - f1) * 6.0, 0, 1)
    tone = (cid * 2654435761 % 1000).astype(np.float32) / 1000.0
    n = _noise(R, 6, rng)
    stone = 0.30 + 0.14 * tone + 0.10 * n
    base = np.stack([stone * 0.98, stone * 1.0, stone * 1.06], axis=2)
    joint = np.array([0.72, 0.64, 0.50], np.float32)
    rgb = base * edge[..., None] + joint * (1 - edge[..., None])
    rgb *= (0.85 + 0.3 * f1[..., None] * 0.5)
    save("T_Calcada_Basalto", rgb)

    # Lajes claras da praça (anel em volta do Pelourinho)
    f1, f2, cid = _voronoi(R, 5, rng)
    edge = np.clip((f2 - f1) * 9.0, 0, 1)
    tone = (cid * 40503 % 997).astype(np.float32) / 997.0
    s = 0.70 + 0.10 * tone + 0.08 * _noise(R, 8, rng)
    rgb = np.stack([s * 1.02, s * 0.97, s * 0.88], 2) * edge[..., None] + np.array([0.45, 0.40, 0.34]) * (1 - edge[..., None])
    save("T_Lajes_Claras", rgb)

    # Reboco caiado (branco com manchas suaves — tingido por material)
    n = _noise(R, 4, rng, 5)
    speck = rng.random((R, R)).astype(np.float32)
    v = 0.86 + 0.10 * n - 0.05 * (speck > 0.97)
    save("T_Reboco", np.stack([v, v * 0.99, v * 0.96], 2))

    # Alvenaria de pedra (muros e embasamentos)
    rows = 6
    y, x = np.mgrid[0:R, 0:R].astype(np.float32) / R
    row = np.floor(y * rows).astype(int)
    xs = (x + (row % 2) * 0.37 + rng.random(rows)[row] * 0.2) % 1.0
    widths = 4 + (row % 3)
    col = np.floor(xs * widths).astype(int)
    fy = (y * rows) % 1.0
    fx = (xs * widths) % 1.0
    mortar = np.minimum(np.minimum(fy, 1 - fy) * 8, np.minimum(fx, 1 - fx) * 10)
    mortar = np.clip(mortar * 2.5, 0, 1)
    tone = ((row * 31 + col * 17) * 2654435761 % 1000).astype(np.float32) / 1000.0
    n = _noise(R, 8, rng)
    s = 0.48 + 0.16 * tone + 0.12 * n
    stone = np.stack([s * 1.05, s * 0.98, s * 0.88], 2)
    rgb = stone * mortar[..., None] + np.array([0.70, 0.66, 0.58]) * (1 - mortar[..., None])
    save("T_Alvenaria", rgb)

    # Telha de barro (meia-cana em fiadas)
    rows, cols = 8, 6
    fy = (y * rows) % 1.0
    row = np.floor(y * rows).astype(int)
    fx = (x * cols + (row % 2) * 0.5) % 1.0
    curve = np.sin(fx * math.pi) ** 0.6
    shade = 0.55 + 0.45 * curve * (0.6 + 0.4 * (1 - fy))
    gap = np.clip(fy * 12, 0, 1)
    tone = 0.9 + 0.2 * _noise(R, 6, rng)
    rgb = np.stack([0.78 * shade * tone, 0.36 * shade * tone, 0.20 * shade * tone], 2) * gap[..., None]
    save("T_Telha", rgb)

    # Madeira (tábuas verticais)
    planks = 5
    px = (x * planks) % 1.0
    pid = np.floor(x * planks).astype(int)
    grain = np.sin((y * 40 + _noise(R, 3, rng) * 6 + pid * 1.7) * 2) * 0.5 + 0.5
    seam = np.clip(np.minimum(px, 1 - px) * 30, 0, 1)
    v = (0.75 + 0.15 * grain + 0.1 * rng.random(planks)[pid]) * seam
    save("T_Madeira", np.stack([v, v * 0.92, v * 0.80], 2))

    # Pedra clara / mármore do Pelourinho (veios suaves)
    n = _noise(R, 3, rng, 5)
    veins = np.abs(np.sin((x * 3 + y * 1.5 + n * 2.5) * math.pi * 2))
    v = 0.86 + 0.08 * n - 0.10 * (veins < 0.06)
    save("T_Pedra_Clara", np.stack([v * 1.0, v * 0.98, v * 0.93], 2))

    # Terra/areia fora da arena
    n = _noise(R, 6, rng, 5)
    v = 0.6 + 0.2 * n
    save("T_Terra", np.stack([v * 0.82, v * 0.66, v * 0.48], 2))
    return tex


# =============================================================================== materiais

MAT = {}
MAT_TILE = {}   # metros por repetição de textura (usado no mapeamento UV)


def material(name, color, tex=None, tile=2.0, rough=0.8, metal=0.0, emission=None, strength=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (-300, 0)
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if tex is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = tex
        t.location = (-800, 0)
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.inputs[0].default_value = 1.0
        mix.inputs[6].default_value = (*color, 1.0)
        nt.links.new(t.outputs[0], mix.inputs[7])
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        mix.location = (-520, 0)
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    if alpha < 1.0:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = 'BLENDED'
        except AttributeError:
            pass
    m.diffuse_color = (*color, alpha)
    MAT[name] = m
    MAT_TILE[name] = tile
    return m


def make_materials(tex):
    T = tex.get
    material("MAT_Calcada_Basalto", (1.0, 1.0, 1.0), T("T_Calcada_Basalto"), tile=2.5, rough=0.9)
    material("MAT_Lajes_Claras", (1.0, 1.0, 1.0), T("T_Lajes_Claras"), tile=3.0, rough=0.85)
    material("MAT_Terra", (1.0, 1.0, 1.0), T("T_Terra"), tile=6.0, rough=1.0)
    material("MAT_Pedra_Muro", (1.0, 0.97, 0.92), T("T_Alvenaria"), tile=2.0, rough=0.95)
    material("MAT_Pedra_Escura", (0.62, 0.60, 0.60), T("T_Alvenaria"), tile=2.0, rough=0.95)
    material("MAT_Pedra_Clara", (1.0, 1.0, 1.0), T("T_Pedra_Clara"), tile=1.5, rough=0.55)
    material("MAT_Degraus", (0.86, 0.84, 0.80), T("T_Pedra_Clara"), tile=1.2, rough=0.8)
    material("MAT_Reboco_Branco", (0.97, 0.96, 0.92), T("T_Reboco"), tile=3.0)
    material("MAT_Reboco_Ocre", (0.96, 0.78, 0.45), T("T_Reboco"), tile=3.0)
    material("MAT_Reboco_Terracota", (0.86, 0.48, 0.34), T("T_Reboco"), tile=3.0)
    material("MAT_Reboco_Azul", (0.62, 0.78, 0.92), T("T_Reboco"), tile=3.0)
    material("MAT_Reboco_Rosa", (0.95, 0.70, 0.66), T("T_Reboco"), tile=3.0)
    material("MAT_Telha", (1.0, 1.0, 1.0), T("T_Telha"), tile=1.6, rough=0.75)
    material("MAT_Madeira", (0.66, 0.46, 0.30), T("T_Madeira"), tile=1.2)
    material("MAT_Madeira_Verde", (0.24, 0.58, 0.42), T("T_Madeira"), tile=1.2)
    material("MAT_Madeira_Azul", (0.20, 0.44, 0.72), T("T_Madeira"), tile=1.2)
    material("MAT_Ferro", (0.13, 0.13, 0.15), None, rough=0.5, metal=0.8)
    material("MAT_Pano_Vermelho", (0.86, 0.20, 0.18), None)
    material("MAT_Pano_Amarelo", (0.98, 0.78, 0.18), None)
    material("MAT_Pano_Azul", (0.15, 0.42, 0.82), None)
    material("MAT_Pano_Verde", (0.15, 0.62, 0.42), None)
    material("MAT_Folhas", (0.22, 0.50, 0.20), None, rough=0.9)
    # Folhas dos arbustos do jogo (LeafBush): a cor vem dos vértices, como no M_BushLeaves do Unity
    m = material("MAT_Folhas_Arbusto", (1.0, 1.0, 1.0), None, rough=0.85)
    nt = m.node_tree
    ca = nt.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    nt.links.new(ca.outputs[0], nt.nodes["Principled BSDF"].inputs["Base Color"])
    material("MAT_Folhas_Claras", (0.38, 0.64, 0.26), None, rough=0.9)
    material("MAT_Tronco", (0.40, 0.29, 0.20), None, rough=0.95)
    material("MAT_Mar", (0.10, 0.45, 0.70), None, rough=0.2)
    material("MAT_Barro", (0.72, 0.38, 0.24), None, rough=0.9)
    material("MAT_Flor_Vermelha", (0.92, 0.16, 0.20), None)
    material("MAT_Flor_Rosa", (0.98, 0.45, 0.70), None)
    material("MAT_Flor_Amarela", (1.0, 0.82, 0.18), None)
    material("MAT_Vidro_Lanterna", (1.0, 0.86, 0.55), None, rough=0.3, emission=(1.0, 0.78, 0.45), strength=1.2)
    material("MAT_Palha", (0.78, 0.64, 0.38), None, rough=1.0)
    material("MAT_Cesto", (0.80, 0.62, 0.36), None, rough=0.95)
    material("MAT_Bote_Azul", (0.12, 0.40, 0.80), None)
    material("MAT_Bote_Amarelo", (0.98, 0.76, 0.16), None)
    material("MAT_Bote_Vermelho", (0.85, 0.20, 0.18), None)
    material("MAT_Basalto_Escuro", (0.22, 0.22, 0.25), T("T_Calcada_Basalto"), tile=1.2, rough=0.9)
    material("MAT_Monte", (0.56, 0.44, 0.32), None, rough=1.0)
    material("MAT_Monte_Verde", (0.42, 0.52, 0.28), None, rough=1.0)
    material("MAT_Spawn_Azul", (0.20, 0.50, 0.95), None, emission=(0.2, 0.5, 1.0), strength=0.4)
    material("MAT_Spawn_Vermelho", (0.95, 0.28, 0.22), None, emission=(1.0, 0.3, 0.2), strength=0.4)
    # Ficção do jogo (emissor e gemas)
    material("MAT_Gema", (0.70, 0.40, 1.00), None, rough=0.1, emission=(0.62, 0.30, 1.0), strength=1.5)
    material("MAT_Emissor", (0.85, 0.70, 1.00), None, rough=0.2, emission=(0.70, 0.45, 1.0), strength=2.0)
    material("MAT_Marca_Aterragem", (0.75, 0.55, 1.00), None, rough=0.5, emission=(0.70, 0.45, 1.0), strength=0.0)
    material("MAT_Marca_Aterragem_Apagada", (0.75, 0.55, 1.00), None, rough=0.5, emission=(0.70, 0.45, 1.0), strength=0.0)
    material("MAT_Faisca", (1.0, 0.9, 1.0), None, emission=(0.85, 0.65, 1.0), strength=8.0)
    material("MAT_Jogador_Demo", (0.25, 0.55, 1.0), None, rough=0.6)
    material("MAT_Colisor", (1.0, 0.2, 0.6), None, alpha=0.25)


# =============================================================================== geometria (bmesh)

def box_projection_uv(bm, mats):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        name = mats[f.material_index].name if f.material_index < len(mats) else ""
        tile = MAT_TILE.get(name, 2.0)
        for l in f.loops:
            co = l.vert.co
            if ax == 0:
                u, v = (co.y if n[0] > 0 else -co.y), co.z
            elif ax == 1:
                u, v = (-co.x if n[1] > 0 else co.x), co.z
            else:
                u, v = co.x, co.y
            l[uv].uv = (u / tile, v / tile)


def mesh_from_bm(bm, name, mats):
    bm.normal_update()
    box_projection_uv(bm, mats)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    return me


def place(me, name, coll, loc=(0, 0, 0), rot_z=0.0, scale=(1, 1, 1)):
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    ob.location = Vector(loc)
    ob.rotation_euler = (0.0, 0.0, rot_z)
    ob.scale = scale
    return ob


def add_box(bm, sx, sy, sz, center=(0, 0, 0), mi=0, bottom=True):
    """Caixa dentro de um bmesh existente. bottom=True: center é o centro da base."""
    cx, cy, cz = center
    if bottom:
        cz += sz / 2
    hx, hy, hz = sx / 2, sy / 2, sz / 2
    v = [bm.verts.new((cx + x * hx, cy + y * hy, cz + z * hz))
         for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    for f in faces:
        bm.faces.new([v[i] for i in f]).material_index = mi


def add_prism(bm, n, r, h, z0=0.0, r_top=None, mi=0, center=(0, 0), rot=0.0, smooth=False, twist=0.0, cap=True):
    """Prisma/tronco de n lados (octógonos, colunas). twist roda o topo (efeito torcido manuelino)."""
    r_top = r if r_top is None else r_top
    cx, cy = center
    bot = [bm.verts.new((cx + r * math.cos(rot + 2 * math.pi * i / n), cy + r * math.sin(rot + 2 * math.pi * i / n), z0)) for i in range(n)]
    top = [bm.verts.new((cx + r_top * math.cos(rot + twist + 2 * math.pi * i / n), cy + r_top * math.sin(rot + twist + 2 * math.pi * i / n), z0 + h)) for i in range(n)]
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new((bot[i], bot[j], top[j], top[i]))
        f.material_index = mi
        f.smooth = smooth
    if cap:
        fb = bm.faces.new(list(reversed(bot)))
        fb.material_index = mi
        if r_top > 1e-4:
            ft = bm.faces.new(top)
            ft.material_index = mi
    return bot, top


def add_lathe(bm, profile, n, mi=0, smooth=True, twist_per_m=0.0, center=(0, 0)):
    """Sólido de revolução a partir de [(raio, z), ...] de baixo para cima."""
    cx, cy = center
    rings = []
    for (r, z) in profile:
        tw = twist_per_m * z
        rings.append([bm.verts.new((cx + r * math.cos(tw + 2 * math.pi * i / n), cy + r * math.sin(tw + 2 * math.pi * i / n), z)) for i in range(n)])
    for a, b in zip(rings[:-1], rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((a[i], a[j], b[j], b[i]))
            f.material_index = mi
            f.smooth = smooth
    f = bm.faces.new(list(reversed(rings[0])))
    f.material_index = mi
    if profile[-1][0] > 1e-4:
        f = bm.faces.new(rings[-1])
        f.material_index = mi


def add_torus(bm, R, r, seg=24, sides=8, mi=0, center=(0, 0, 0), axis='Z'):
    cx, cy, cz = center
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            d = R + r * math.cos(b)
            p = Vector((d * math.cos(a), d * math.sin(a), r * math.sin(b)))
            if axis == 'X':
                p = Vector((p.z, p.x, p.y))
            elif axis == 'Y':
                p = Vector((p.x, p.z, p.y))
            ring.append(bm.verts.new((cx + p.x, cy + p.y, cz + p.z)))
        rings.append(ring)
    for i in range(seg):
        a, b = rings[i], rings[(i + 1) % seg]
        for j in range(sides):
            k = (j + 1) % sides
            f = bm.faces.new((a[j], b[j], b[k], a[k]))
            f.material_index = mi
            f.smooth = True


def add_icosphere(bm, radius, center, mi=0, subdiv=1, squash=1.0):
    ret = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    vs = ret["verts"]
    for v in vs:
        v.co.z *= squash
        v.co += Vector(center)
    for f in {f for v in vs for f in v.link_faces}:
        f.material_index = mi


def new_bm():
    return bmesh.new()


# =============================================================================== colisores e marcadores

def col_box(name, size, loc, rot_z=0.0):
    """Colisor simples (caixa). No Unity: COL_Box_* → BoxCollider; a malha é removida."""
    bm = new_bm()
    add_box(bm, *size, center=(0, 0, 0), bottom=True)
    me = mesh_from_bm(bm, "COL_Box_" + name, [MAT["MAT_Colisor"]])
    ob = place(me, "COL_Box_" + name, C["Collision"], loc, rot_z)
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def col_prism(name, n, r, h, loc, rot=0.0):
    """Colisor convexo (octógono dos degraus). No Unity: COL_Mesh_* → MeshCollider convexo."""
    bm = new_bm()
    add_prism(bm, n, r, h, rot=rot)
    me = mesh_from_bm(bm, "COL_Mesh_" + name, [MAT["MAT_Colisor"]])
    ob = place(me, "COL_Mesh_" + name, C["Collision"], loc)
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def marker(name, loc, coll, size=0.5, shape='ARROWS'):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = shape
    ob.empty_display_size = size
    ob.location = Vector(loc)
    coll.objects.link(ob)
    return ob


# =============================================================================== layout (metros, Blender XY)

class Layout:
    """Distribuição original adaptada da referência (praça central + bolsas laterais + corredores de flanco).
    Definida para 40 × 50 m e escalada em X/Y pelos parâmetros de dimensão. Simétrica em X e em Y."""

    def __init__(self, P):
        self.sx = P["arena_width"] / 40.0
        self.sy = P["arena_length"] / 50.0
        self.hx = P["arena_width"] / 2.0
        self.hy = P["arena_length"] / 2.0
        self.wall_h = P["wall_height"]

    def X(self, v):
        return v * self.sx

    def Y(self, v):
        return v * self.sy

    def rect(self, x0, x1, y0, y1):
        """Retângulo em coordenadas de referência → (centro x, centro y, largura, profundidade) escalado."""
        a, b = sorted((self.X(x0), self.X(x1)))
        c, d = sorted((self.Y(y0), self.Y(y1)))
        return ((a + b) / 2, (c + d) / 2, b - a, d - c)


# =============================================================================== módulos reutilizáveis

MODULES = {}


def build_modules(P):
    """Peças partilhadas (linked duplicates): janela, porta, segmento de muro, segmento de muro com janela."""
    det = P["detail"]
    wall_h = P["wall_height"]

    for color, mat_name in (("Verde", "MAT_Madeira_Verde"), ("Azul", "MAT_Madeira_Azul")):
        # Janela: moldura de pedra + portadas de madeira + peitoril. Frente em -Y, origem no centro da base.
        bm = new_bm()
        mats = [MAT["MAT_Pedra_Clara"], MAT[mat_name], MAT["MAT_Ferro"]]
        w, h = 1.0, 1.3
        add_box(bm, w + 0.24, 0.10, 0.12, center=(0, -0.05, -0.06), mi=0)                # peitoril
        add_box(bm, 0.12, 0.08, h, center=(-(w / 2 + 0.06), -0.04, 0), mi=0)             # ombreiras
        add_box(bm, 0.12, 0.08, h, center=((w / 2 + 0.06), -0.04, 0), mi=0)
        add_box(bm, w + 0.24, 0.08, 0.14, center=(0, -0.04, h), mi=0)                    # verga
        add_box(bm, w * 0.48, 0.05, h - 0.06, center=(-w * 0.25, -0.03, 0.03), mi=1)     # portadas
        add_box(bm, w * 0.48, 0.05, h - 0.06, center=(w * 0.25, -0.03, 0.03), mi=1)
        if det >= 2:
            for k in range(4):                                                           # ripas
                z = 0.2 + k * (h - 0.35) / 3
                add_box(bm, w * 0.94, 0.02, 0.04, center=(0, -0.065, z), mi=1)
        if det >= 2:  # floreira com flores no peitoril
            mats += [MAT["MAT_Barro"], MAT["MAT_Folhas_Claras"], MAT["MAT_Flor_Vermelha" if color == "Verde" else "MAT_Flor_Rosa"]]
            add_box(bm, w * 0.86, 0.2, 0.16, center=(0, -0.2, -0.04), mi=3)
            for k in range(5 if det >= 3 else 4):
                fx = -w * 0.34 + k * (w * 0.68 / (4 if det >= 3 else 3))
                add_icosphere(bm, 0.09, (fx, -0.2, 0.15), mi=4, subdiv=1, squash=0.8)
                add_icosphere(bm, 0.055, (fx + 0.03, -0.27, 0.2), mi=5, subdiv=1)
        MODULES["Janela_" + color] = mesh_from_bm(bm, "MOD_Janela_" + color, mats)

        # Porta: arco reto de pedra + folhas de madeira
        bm = new_bm()
        w, h = 1.3, 2.3
        add_box(bm, 0.16, 0.10, h, center=(-(w / 2 + 0.08), -0.05, 0), mi=0)
        add_box(bm, 0.16, 0.10, h, center=((w / 2 + 0.08), -0.05, 0), mi=0)
        add_box(bm, w + 0.32, 0.10, 0.20, center=(0, -0.05, h), mi=0)
        add_box(bm, w, 0.06, h - 0.02, center=(0, -0.02, 0), mi=1)
        if det >= 2:
            add_box(bm, 0.04, 0.03, h - 0.1, center=(0, -0.06, 0.05), mi=1)
            add_box(bm, 0.08, 0.04, 0.08, center=(0.18, -0.07, 1.05), mi=2)  # puxador
            add_box(bm, w + 0.5, 0.42, 0.13, center=(0, -0.21, 0), mi=0)      # soleira/degrau de pedra (0.13 m: sobe-se)
        MODULES["Porta_" + color] = mesh_from_bm(bm, "MOD_Porta_" + color, mats)

    # Candeeiro de parede (ferro + vidro aceso)
    bm = new_bm()
    mats = [MAT["MAT_Ferro"], MAT["MAT_Vidro_Lanterna"]]
    add_box(bm, 0.05, 0.42, 0.05, center=(0, -0.21, 0.32), mi=0)
    add_box(bm, 0.05, 0.05, 0.3, center=(0, -0.02, 0.05), mi=0)
    add_box(bm, 0.2, 0.2, 0.26, center=(0, -0.42, 0.02), mi=1)
    add_prism(bm, 4, 0.17, 0.12, z0=0.28, r_top=0.03, mi=0, center=(0, -0.42), rot=math.pi / 4)
    add_box(bm, 0.24, 0.24, 0.03, center=(0, -0.42, 0.0), mi=0)
    MODULES["Candeeiro"] = mesh_from_bm(bm, "MOD_Candeeiro", mats)

    # Varanda de ferro (sobrados)
    bm = new_bm()
    mats = [MAT["MAT_Pedra_Clara"], MAT["MAT_Ferro"]]
    add_box(bm, 1.6, 0.6, 0.12, center=(0, -0.3, 0), mi=0)
    add_box(bm, 1.6, 0.04, 0.05, center=(0, -0.58, 0.95), mi=1)
    for k in range(9 if det >= 2 else 3):
        x = -0.78 + k * (1.56 / (8 if det >= 2 else 2))
        add_box(bm, 0.03, 0.03, 0.85, center=(x, -0.58, 0.12), mi=1)
    MODULES["Varanda"] = mesh_from_bm(bm, "MOD_Varanda", mats)

    # Segmento de muro de pedra (2 m, origem no centro da base, comprimento em X)
    def muro(name, window):
        bm = new_bm()
        mats = [MAT["MAT_Pedra_Muro"], MAT["MAT_Pedra_Escura"]]
        t = 0.6
        if window:
            add_box(bm, 2.0, t, 1.0, center=(0, 0, 0), mi=0)                            # parapeito
            add_box(bm, 0.45, t, wall_h - 1.0, center=(-0.775, 0, 1.0), mi=0)          # pilares
            add_box(bm, 0.45, t, wall_h - 1.0, center=(0.775, 0, 1.0), mi=0)
            add_box(bm, 1.1, t, wall_h - 1.9, center=(0, 0, 1.9), mi=0)                 # verga
            add_box(bm, 1.24, t + 0.06, 0.08, center=(0, 0, 0.94), mi=1)                # soleira
        else:
            add_box(bm, 2.0, t, wall_h, center=(0, 0, 0), mi=0)
        add_box(bm, 2.02, t + 0.12, 0.16, center=(0, 0, wall_h), mi=1)                  # capeamento
        if det >= 2 and not window:
            add_box(bm, 2.02, t + 0.08, 0.22, center=(0, 0, 0), mi=1)                   # sapata
        MODULES[name] = mesh_from_bm(bm, "MOD_" + name, mats)

    muro("Muro_2m", False)
    muro("Muro_Janela_2m", True)

    # Parapeito baixo (limite com vista para o mar)
    bm = new_bm()
    add_box(bm, 2.0, 0.5, 1.05, mi=0)
    add_box(bm, 2.02, 0.62, 0.12, center=(0, 0, 1.05), mi=1)
    MODULES["Parapeito_2m"] = mesh_from_bm(bm, "MOD_Parapeito_2m", [MAT["MAT_Pedra_Muro"], MAT["MAT_Pedra_Escura"]])


def wall_run(name, p0, p1, coll, module="Muro_2m", window_at=None):
    """Muro feito de segmentos de 2 m (escala em X para acertar o comprimento)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    L = d.length
    n = max(1, round(L / 2.0))
    seg = L / n
    ang = math.atan2(d.y, d.x)
    obs = []
    for i in range(n):
        c = p0 + d * ((i + 0.5) / n)
        mod = module
        if window_at is not None and i in window_at:
            mod = "Muro_Janela_2m"
        obs.append(place(MODULES[mod], f"{name}_{i:02d}", coll, (c.x, c.y, 0), ang, (seg / 2.0, 1, 1)))
    return obs


# =============================================================================== edifícios

def roof_mesh(bm, sx, sy, z0, rise, kind, mi_tile, mi_wood, over=0.35):
    """Cobertura de telha: 'gable' (duas águas, cumeeira em X) ou 'hip' (quatro águas)."""
    hx, hy = sx / 2 + over, sy / 2 + over
    if kind == 'gable':
        a = [bm.verts.new(p) for p in ((-hx, -hy, z0), (hx, -hy, z0), (hx, 0, z0 + rise), (-hx, 0, z0 + rise))]
        b = [bm.verts.new(p) for p in ((-hx, hy, z0), (hx, hy, z0), (hx, 0, z0 + rise), (-hx, 0, z0 + rise))]
        bm.faces.new(a).material_index = mi_tile
        bm.faces.new(list(reversed(b))).material_index = mi_tile
        # empenas (triângulos de reboco por baixo da telha — ficam com material da madeira/beiral)
        g1 = [bm.verts.new(p) for p in ((-sx / 2, -sy / 2, z0 + 0.02), (-sx / 2, sy / 2, z0 + 0.02), (-sx / 2, 0, z0 + rise - 0.05))]
        g2 = [bm.verts.new(p) for p in ((sx / 2, sy / 2, z0 + 0.02), (sx / 2, -sy / 2, z0 + 0.02), (sx / 2, 0, z0 + rise - 0.05))]
        bm.faces.new(g1).material_index = mi_wood
        bm.faces.new(g2).material_index = mi_wood
        add_box(bm, sx + 2 * over + 0.05, 0.22, 0.14, center=(0, 0, z0 + rise - 0.06), mi=mi_tile)  # cumeeira
    else:
        ridge = max(0.0, hx - hy) if hx >= hy else 0.0
        ridge_y = max(0.0, hy - hx) if hy > hx else 0.0
        c = [bm.verts.new(p) for p in ((-hx, -hy, z0), (hx, -hy, z0), (hx, hy, z0), (-hx, hy, z0))]
        t1 = bm.verts.new((-ridge, -ridge_y, z0 + rise))
        t2 = bm.verts.new((ridge, ridge_y, z0 + rise))
        if ridge > 0:
            bm.faces.new((c[0], c[1], t2, t1)).material_index = mi_tile
            bm.faces.new((c[2], c[3], t1, t2)).material_index = mi_tile
            bm.faces.new((c[1], c[2], t2)).material_index = mi_tile
            bm.faces.new((c[3], c[0], t1)).material_index = mi_tile
        elif ridge_y > 0:
            bm.faces.new((c[1], c[2], t2, t1)).material_index = mi_tile
            bm.faces.new((c[3], c[0], t1, t2)).material_index = mi_tile
            bm.faces.new((c[0], c[1], t1)).material_index = mi_tile
            bm.faces.new((c[2], c[3], t2)).material_index = mi_tile
        else:
            for i in range(4):
                bm.faces.new((c[i], c[(i + 1) % 4], t1)).material_index = mi_tile
        bm.faces.new(list(reversed(c))).material_index = mi_wood
    # beiral de madeira
    add_box(bm, sx + 2 * over, sy + 2 * over, 0.08, center=(0, 0, z0 - 0.08), mi=mi_wood)


def building(L, name, rect, height, plaster, roof='gable', storeys=1, faces=('S',), shutters="Verde",
             roof_rise=1.4, doors=True, balcony=False, roof_rot=0.0, det=2):
    """Casa/sobrado: corpo de reboco + embasamento de pedra + cunhais + cobertura + módulos de fachada.
    rect = (cx, cy, largura X, profundidade Y). faces: lados com fachada ('N' +Y, 'S' -Y, 'E' +X, 'W' -X)."""
    cx, cy, sx, sy = rect
    coll = C["Buildings"]
    bm = new_bm()
    mats = [MAT[plaster], MAT["MAT_Pedra_Muro"], MAT["MAT_Telha"], MAT["MAT_Madeira"], MAT["MAT_Pedra_Clara"]]
    add_box(bm, sx, sy, height, mi=0)                                        # corpo
    add_box(bm, sx + 0.08, sy + 0.08, 0.55, mi=1)                            # embasamento
    if storeys >= 2:
        add_box(bm, sx + 0.1, sy + 0.1, 0.14, center=(0, 0, height * 0.5 - 0.07), mi=4)   # friso entre pisos
    add_box(bm, sx + 0.12, sy + 0.12, 0.18, center=(0, 0, height - 0.18), mi=4)          # cornija
    if det >= 2:                                                             # cunhais de pedra
        for qx in (-1, 1):
            for qy in (-1, 1):
                k = 0
                z = 0.55
                while z < height - 0.4:
                    w = 0.42 if k % 2 == 0 else 0.30
                    add_box(bm, w, w, 0.34, center=(qx * (sx / 2 - w / 2 + 0.05), qy * (sy / 2 - w / 2 + 0.05), z), mi=4)
                    z += 0.36
                    k += 1
    # cobertura (rodada 90° se a cumeeira for em Y)
    rb = new_bm()
    if abs(roof_rot) > 1e-3:
        roof_mesh(rb, sy, sx, height, roof_rise, roof, 2, 3)
        bmesh.ops.rotate(rb, verts=rb.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(roof_rot, 3, 'Z'))
    else:
        roof_mesh(rb, sx, sy, height, roof_rise, roof, 2, 3)
    me_roof = mesh_from_bm(rb, f"{name}_Cobertura", [MAT["MAT_Telha"], MAT["MAT_Madeira"], MAT["MAT_Telha"], MAT["MAT_Madeira"]])
    me = mesh_from_bm(bm, f"{name}_Corpo", mats)
    body = place(me, f"{name}_Corpo", coll, (cx, cy, 0))
    place(me_roof, f"{name}_Cobertura", coll, (cx, cy, 0))

    # Fachadas com módulos (vãos de ~3 m)
    side = {'S': (0, -1, 0.0), 'N': (0, 1, math.pi), 'E': (1, 0, math.pi / 2), 'W': (-1, 0, -math.pi / 2)}
    for f in faces:
        nx, ny, rz = side[f]
        length = sx if ny != 0 else sy
        bays = max(1, int(length // 3.0))
        for b in range(bays):
            t = (b + 0.5) / bays - 0.5
            along = t * length
            if ny != 0:
                px, py = cx + along, cy + ny * sy / 2
            else:
                px, py = cx + nx * sx / 2, cy + along
            mid = b == bays // 2
            if doors and mid:
                place(MODULES["Porta_" + shutters], f"{name}_Porta_{f}{b}", coll, (px, py, 0.0), rz)
            else:
                place(MODULES["Janela_" + shutters], f"{name}_Janela_{f}{b}", coll, (px, py, 1.0), rz)
            if storeys >= 2:
                z2 = height * 0.5 + 0.55
                if balcony and mid:
                    place(MODULES["Varanda"], f"{name}_Varanda_{f}{b}", coll, (px, py, height * 0.5 + 0.02), rz)
                    place(MODULES["Porta_" + shutters], f"{name}_PortaVaranda_{f}{b}", coll, (px, py, height * 0.5 + 0.14), rz, (0.85, 1, 0.82))
                else:
                    place(MODULES["Janela_" + shutters], f"{name}_Janela2_{f}{b}", coll, (px, py, z2), rz)
        if det >= 2 and bays >= 2:  # candeeiros entre os vãos (fora do alcance dos jogadores)
            for b in range(1, bays):
                t = b / bays - 0.5
                along = t * length
                px, py = (cx + along, cy + ny * sy / 2) if ny != 0 else (cx + nx * sx / 2, cy + along)
                place(MODULES["Candeeiro"], f"{name}_Candeeiro_{f}{b}", coll, (px, py, min(height - 0.6, 2.75)), rz)
    if det >= 2 and (storeys >= 2 or stable_seed(name) % 2 == 0):  # chaminé
        bmc = new_bm()
        add_box(bmc, 0.55, 0.55, roof_rise + 0.9, mi=0)
        add_box(bmc, 0.7, 0.7, 0.1, center=(0, 0, roof_rise + 0.9), mi=1)
        add_box(bmc, 0.18, 0.18, 0.12, center=(0, 0, roof_rise + 1.0), mi=1)
        off = (sx * 0.22, sy * 0.12) if sx >= sy else (sx * 0.12, sy * 0.22)
        place(mesh_from_bm(bmc, f"{name}_Chamine", [MAT[plaster], MAT["MAT_Pedra_Clara"]]), f"{name}_Chamine", coll,
              (cx + off[0], cy + off[1], height - 0.2))
    # Colisor: corpo inteiro (telhado incluído na altura; ninguém sobe aos telhados)
    col_box(name, (sx, sy, height + roof_rise), (cx, cy, 0))
    return body


def chapel(L, name, rect, facing_y):
    """Fachada de capela caiada com cunhais de pedra e sineira (inspiração na arquitetura religiosa local)."""
    cx, cy, sx, sy = rect
    coll = C["Buildings"]
    h = 6.8
    building(L, name, rect, h, "MAT_Reboco_Branco", roof='gable', storeys=1, faces=(), roof_rise=1.8, det=3)
    bm = new_bm()
    mats = [MAT["MAT_Reboco_Branco"], MAT["MAT_Pedra_Clara"], MAT["MAT_Ferro"], MAT["MAT_Madeira_Verde"]]
    fy = -facing_y * sy / 2
    # frontão triangular + sineira
    add_box(bm, 3.4, 0.5, 2.0, center=(0, fy - facing_y * 0.1, h), mi=0)
    add_box(bm, 1.6, 0.5, 1.6, center=(0, fy - facing_y * 0.1, h + 2.0), mi=0)
    add_box(bm, 0.9, 0.56, 1.0, center=(0, fy - facing_y * 0.1, h + 2.2), mi=2)        # vão do sino (escuro)
    add_box(bm, 2.0, 0.6, 0.16, center=(0, fy - facing_y * 0.1, h + 3.6), mi=1)
    add_box(bm, 0.14, 0.14, 0.7, center=(0, fy - facing_y * 0.1, h + 3.76), mi=2)       # cruz
    add_box(bm, 0.5, 0.14, 0.12, center=(0, fy - facing_y * 0.1, h + 4.2), mi=2)
    # portal de pedra
    add_box(bm, 2.6, 0.2, 3.6, center=(0, fy - facing_y * 0.08, 0), mi=1)
    add_box(bm, 1.9, 0.24, 3.0, center=(0, fy - facing_y * 0.1, 0), mi=3)
    me = mesh_from_bm(bm, f"{name}_Fachada", mats)
    ob = place(me, f"{name}_Fachada", coll, (cx, cy, 0))
    col_box(name + "_Portal", (2.6, 0.5, 3.6), (cx, cy + fy - facing_y * 0.2, 0))  # o portal sai da fachada
    # óculo (objeto à parte, no plano da fachada)
    bm = new_bm()
    add_torus(bm, 0.45, 0.09, seg=16, sides=6, mi=0, axis='Y')
    add_box(bm, 0.8, 0.04, 0.8, center=(0, 0, -0.4), mi=1)
    me2 = mesh_from_bm(bm, f"{name}_Oculo", [MAT["MAT_Pedra_Clara"], MAT["MAT_Ferro"]])
    place(me2, f"{name}_Oculo", coll, (cx, cy + fy - facing_y * 0.15, 4.9))
    return ob


# =============================================================================== Pelourinho

PEL = dict(
    steps=[(3.10, 0.30), (2.50, 0.30), (1.95, 0.30)],   # (raio circunscrito do octógono, altura) — 0.30 < stepOffset 0.35
    pedestal_r=0.78, pedestal_h=0.55,
    shaft_r=0.32, shaft_h=3.9,
    capital_h=1.30,
)


def pelourinho(P):
    """Pelourinho modelado em volume: escadaria octogonal de 3 degraus, pedestal, fuste com anéis,
    capitel torcido de inspiração manuelina, braços de ferro e esfera armilar com cruz."""
    det = P["detail"]
    coll = C["Pelourinho"]
    rot8 = math.pi / 8  # face plana virada para cada eixo
    z = 0.0
    # Degraus
    bm = new_bm()
    for i, (r, h) in enumerate(PEL["steps"]):
        add_prism(bm, 8, r, h, z0=z, mi=0, rot=rot8)
        if det >= 2:  # bocel do degrau
            add_prism(bm, 8, r + 0.04, 0.05, z0=z + h - 0.05, mi=0, rot=rot8, cap=False)
        z += h
    me = mesh_from_bm(bm, "PEL_Degraus", [MAT["MAT_Degraus"]])
    place(me, "PEL_Degraus", coll)
    z_steps = z

    # Pedestal octogonal com base e moldura
    bm = new_bm()
    r = PEL["pedestal_r"]
    add_prism(bm, 8, r + 0.10, 0.12, z0=z, mi=0, rot=rot8)
    add_prism(bm, 8, r, PEL["pedestal_h"] - 0.22, z0=z + 0.12, mi=0, rot=rot8)
    add_prism(bm, 8, r + 0.06, 0.10, z0=z + PEL["pedestal_h"] - 0.10, r_top=r - 0.1, mi=0, rot=rot8)
    me = mesh_from_bm(bm, "PEL_Pedestal", [MAT["MAT_Pedra_Clara"]])
    place(me, "PEL_Pedestal", coll)
    z += PEL["pedestal_h"]
    z_shaft0 = z

    # Fuste: base moldurada, fuste de 16 lados com ligeira êntase e dois anéis
    bm = new_bm()
    sr, sh = PEL["shaft_r"], PEL["shaft_h"]
    n = 16 if det >= 2 else 12
    prof = [(sr + 0.10, 0.0), (sr + 0.10, 0.10), (sr + 0.04, 0.16), (sr, 0.22)]
    steps_n = 8
    for k in range(1, steps_n + 1):
        t = k / steps_n
        prof.append((sr * (1.0 - 0.12 * t + 0.05 * math.sin(t * math.pi)), 0.22 + t * (sh - 0.22)))
    add_lathe(bm, [(r_, z_ + z_shaft0) for r_, z_ in prof], n, mi=0)
    for rz in (1.35, 2.95):                                              # anéis
        add_torus(bm, sr * 0.98, 0.07, seg=n, sides=6, mi=0, center=(0, 0, z_shaft0 + rz))
    me = mesh_from_bm(bm, "PEL_Fuste", [MAT["MAT_Pedra_Clara"]])
    place(me, "PEL_Fuste", coll)
    z = z_shaft0 + sh
    z_cap0 = z

    # Capitel torcido (cordame/espiral) — remate alongado que alarga e afunila
    bm = new_bm()
    cap_h = PEL["capital_h"]
    prof = []
    k_n = 10 if det >= 2 else 6
    for k in range(k_n + 1):
        t = k / k_n
        rad = 0.30 + 0.24 * math.sin(min(1.0, t * 1.25) * math.pi * 0.85) - 0.12 * t
        prof.append((max(0.12, rad), z_cap0 + t * cap_h))
    add_lathe(bm, prof, 8, mi=0, smooth=False, twist_per_m=1.2)
    add_torus(bm, 0.34, 0.07, seg=16, sides=6, mi=0, center=(0, 0, z_cap0 + 0.06))      # cordão manuelino
    if det >= 2:
        add_torus(bm, 0.36, 0.05, seg=16, sides=6, mi=0, center=(0, 0, z_cap0 + cap_h * 0.55))
    me = mesh_from_bm(bm, "PEL_Capitel", [MAT["MAT_Pedra_Clara"]])
    place(me, "PEL_Capitel", coll)
    z = z_cap0 + cap_h

    # Braços de ferro com ganchos (4 direções, na base do capitel)
    bm = new_bm()
    za = z_cap0 + 0.32
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0))
        L_arm = 1.05
        mid = d * (0.25 + L_arm / 2)
        b = new_bm()
        add_box(b, L_arm, 0.06, 0.06, center=(0, 0, -0.03), mi=0)
        add_box(b, 0.06, 0.06, 0.22, center=(L_arm / 2 - 0.03, 0, 0.0), mi=0)          # gancho virado para cima
        add_icosphere(b, 0.05, (L_arm / 2 - 0.03, 0, 0.24), mi=0, subdiv=1)
        bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(a, 3, 'Z'))
        bmesh.ops.translate(b, verts=b.verts, vec=(mid.x, mid.y, za))
        tmp = bpy.data.meshes.new("tmp")
        b.to_mesh(tmp)
        b.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    # Pescoço, esfera armilar e cruz
    add_prism(bm, 8, 0.07, 0.25, z0=z, mi=0)
    zc = z + 0.25 + 0.30
    for axis in ('Z', 'X', 'Y'):
        add_torus(bm, 0.28, 0.022, seg=24, sides=5, mi=0, center=(0, 0, zc), axis=axis)
    if det >= 2:
        add_torus(bm, 0.28, 0.02, seg=24, sides=5, mi=0, center=(0, 0, zc), axis='X')  # eclíptica inclinada
        bmesh.ops.rotate(bm, verts=[v for v in bm.verts[-24 * 5:]], cent=(0, 0, zc), matrix=Matrix.Rotation(math.radians(23), 3, 'Y'))
    add_icosphere(bm, 0.06, (0, 0, zc), mi=0)
    add_box(bm, 0.05, 0.05, 0.50, center=(0, 0, zc + 0.28), mi=0)
    add_box(bm, 0.26, 0.05, 0.05, center=(0, 0, zc + 0.58), mi=0)
    me = mesh_from_bm(bm, "PEL_Ferragens", [MAT["MAT_Ferro"]])
    place(me, "PEL_Ferragens", coll)
    top = zc + 0.78

    # Colisores: degraus (prismas convexos, sobem-se), pedestal e coluna (caixas)
    zz = 0.0
    for i, (r_, h_) in enumerate(PEL["steps"]):
        zz += h_
        col_prism(f"Pelourinho_Degrau{i}", 8, r_, zz, (0, 0, 0), rot=rot8)
    col_box("Pelourinho_Pedestal", (PEL["pedestal_r"] * 1.8, PEL["pedestal_r"] * 1.8, PEL["pedestal_h"]), (0, 0, z_steps))
    col_box("Pelourinho_Coluna", (0.8, 0.8, z_cap0 + cap_h - z_shaft0), (0, 0, z_shaft0))
    return dict(top=top, z_steps=z_steps, z_shaft0=z_shaft0, z_cap0=z_cap0)


# =============================================================================== emissor fictício + gemas

def gem_mesh():
    """Gema facetada estilizada (coroa + pavilhão de 8 lados). Origem no centro."""
    bm = new_bm()
    n = 8
    r_girdle, r_table = 0.24, 0.13
    h_crown, h_pav = 0.13, 0.30
    table = [bm.verts.new((r_table * math.cos(2 * math.pi * i / n + math.pi / n), r_table * math.sin(2 * math.pi * i / n + math.pi / n), h_crown)) for i in range(n)]
    girdle = [bm.verts.new((r_girdle * math.cos(2 * math.pi * i / n), r_girdle * math.sin(2 * math.pi * i / n), 0.0)) for i in range(n)]
    tip = bm.verts.new((0, 0, -h_pav))
    bm.faces.new(table)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((girdle[i], girdle[j], table[i]))
        bm.faces.new((table[i], girdle[j], table[j]))  # facetas da coroa
        bm.faces.new((girdle[j], girdle[i], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_from_bm(bm, "MSH_Gema", [MAT["MAT_Gema"]])


def gem_generator(L, P, pel):
    """Emissor fictício: halo sobre a cruz (ponto de geração), anel de base de onde sobem partículas
    e oito marcas de aterragem simétricas em volta da base."""
    coll = C["GemGenerator"]
    gen = dict()
    z_spawn = pel["top"] + 0.55
    # Halo do emissor (anel de cristal flutuante + 4 lascas)
    bm = new_bm()
    add_torus(bm, 0.50, 0.045, seg=32, sides=6, mi=0)
    for i in range(4):
        a = i * math.pi / 2
        b = new_bm()
        add_prism(b, 4, 0.06, 0.22, z0=-0.11, r_top=0.0, mi=0)
        bmesh.ops.translate(b, verts=b.verts, vec=(0.5 * math.cos(a), 0.5 * math.sin(a), 0))
        tmp = bpy.data.meshes.new("tmp")
        b.to_mesh(tmp)
        b.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    halo = place(mesh_from_bm(bm, "GEN_Emissor_Halo", [MAT["MAT_Emissor"]]), "GEN_Emissor_Halo", coll, (0, 0, z_spawn))
    # Anel de base (no topo do pedestal, à volta do fuste) — origem das partículas
    bm = new_bm()
    add_torus(bm, 0.62, 0.035, seg=32, sides=5, mi=0)
    base_ring = place(mesh_from_bm(bm, "GEN_Emissor_Base", [MAT["MAT_Emissor"]]), "GEN_Emissor_Base", coll, (0, 0, pel["z_shaft0"] + 0.12))
    # Ponto de geração
    sp = marker("GEM_Spawn", (0, 0, z_spawn), coll, 0.4, 'SPHERE')
    # Pontos de aterragem: 8 à volta da base, fora da pedra (degrau inferior r=3.10) e dentro das correntes
    r_land = 4.3
    lands = []
    for i in range(8):
        a = math.radians(i * 45.0)
        p = (r_land * math.cos(a), r_land * math.sin(a), 0.6)
        lands.append(marker(f"GEM_Land_{i:02d}", p, coll, 0.3, 'SINGLE_ARROW'))
    # Marcas de aterragem (disco subtil no chão; acende quando uma gema vai cair ali)
    bm = new_bm()
    add_torus(bm, 0.42, 0.03, seg=24, sides=4, mi=0)
    me_mark = mesh_from_bm(bm, "GEN_Marca_Aterragem", [MAT["MAT_Marca_Aterragem_Apagada"]])
    marks = []
    for i, l in enumerate(lands):
        marks.append(place(me_mark, f"GEN_Marca_{i:02d}", coll, (l.location.x, l.location.y, 0.04)))
    gen.update(halo=halo, base=base_ring, spawn=sp, lands=lands, marks=marks, z_spawn=z_spawn)
    return gen


# =============================================================================== chão, cobertura, adereços

def ground(L, P):
    coll = C["Ground"]
    W, Lg = P["arena_width"], P["arena_length"]
    bm = new_bm()
    add_box(bm, W + 1.4, Lg + 6.0, 0.2, center=(0, 0, -0.2), mi=0)
    me = mesh_from_bm(bm, "GRD_Calcada", [MAT["MAT_Calcada_Basalto"]])
    place(me, "GRD_Calcada", coll)
    # Lajes claras: anel octogonal à volta do Pelourinho
    bm = new_bm()
    add_prism(bm, 8, 6.4, 0.012, z0=0.0, mi=0, rot=math.pi / 8)
    me = mesh_from_bm(bm, "GRD_Lajes_Praca", [MAT["MAT_Lajes_Claras"]])
    place(me, "GRD_Lajes_Praca", coll)
    # Faixas de pedra clara (eixo spawn-spawn e transversal) — leitura de rotas
    bm = new_bm()
    add_box(bm, 1.2, Lg - 12.0, 0.01, center=(0, 0, 0.0), mi=0)
    add_box(bm, W - 10.0, 1.0, 0.01, center=(0, 0, 0.0), mi=0)
    me = mesh_from_bm(bm, "GRD_Faixas", [MAT["MAT_Lajes_Claras"]])
    place(me, "GRD_Faixas", coll)
    # Terra/areia à volta (fora da arena, sem jogo)
    bm = new_bm()
    add_box(bm, 160, 170, 0.2, center=(0, 0, -0.42), mi=0)
    place(mesh_from_bm(bm, "GRD_Terra", [MAT["MAT_Terra"]]), "GRD_Terra", coll)
    # Inlays dos spawns
    for team, sy, mat in (("Blue", -1, "MAT_Spawn_Azul"), ("Red", 1, "MAT_Spawn_Vermelho")):
        bm = new_bm()
        cxr = L.rect(-7.5, 7.5, sy * 21.0, sy * 23.6)
        add_box(bm, cxr[2], cxr[3], 0.015, mi=0)
        place(mesh_from_bm(bm, f"GRD_Spawn_{team}", [MAT[mat]]), f"GRD_Spawn_{team}", coll, (cxr[0], cxr[1], 0))
    # Colisores do chão e limites (invisíveis, altos)
    col_box("Chao", (W + 1.4, Lg + 6.0, 1.0), (0, 0, -1.0))
    hx, hy = W / 2, Lg / 2
    col_box("Limite_E", (1.0, Lg + 4, 9.0), (hx + 0.6, 0, 0))
    col_box("Limite_W", (1.0, Lg + 4, 9.0), (-hx - 0.6, 0, 0))
    col_box("Limite_N", (W + 4, 1.0, 9.0), (0, hy + 0.6, 0))
    col_box("Limite_S", (W + 4, 1.0, 9.0), (0, -hy - 0.6, 0))


def stall(name, cx, cy, rot, cloth, det):
    """Banca de mercado (tampo de madeira, toldo de pano, panos pendurados). Meia cobertura (1 m)."""
    coll = C["Cover"]
    bm = new_bm()
    mats = [MAT["MAT_Madeira"], MAT[cloth], MAT["MAT_Pano_Amarelo"], MAT["MAT_Pano_Azul"], MAT["MAT_Pano_Verde"]]
    add_box(bm, 2.4, 1.1, 0.06, center=(0, 0, 0.92), mi=0)
    add_box(bm, 2.3, 1.0, 0.86, center=(0, 0, 0.04), mi=1)             # saia de pano até ao chão
    for qx in (-1, 1):
        for qy in (-1, 1):
            add_box(bm, 0.07, 0.07, 2.3 if qy < 0 else 2.0, center=(qx * 1.15, qy * 0.5, 0), mi=0)
    # toldo inclinado
    v = [bm.verts.new(p) for p in ((-1.35, -0.75, 2.32), (1.35, -0.75, 2.32), (1.35, 0.75, 2.02), (-1.35, 0.75, 2.02))]
    bm.faces.new(v).material_index = 1
    bm.faces.new(list(reversed([bm.verts.new((p.co.x, p.co.y, p.co.z - 0.03)) for p in v]))).material_index = 1
    if det >= 2:  # mercadoria/panos coloridos dobrados no tampo
        for k in range(5):
            add_box(bm, 0.36, 0.5, 0.10 + 0.04 * (k % 2), center=(-0.9 + k * 0.45, 0.05, 0.98), mi=2 + (k % 3))
    if det >= 2:  # panos de terra pendurados à frente do toldo
        for k in range(3):
            add_box(bm, 0.5, 0.02, 0.62 if det < 3 else 0.8, center=(-0.75 + k * 0.75, -0.76, 1.62 if det < 3 else 1.44), mi=2 + k)
    me = mesh_from_bm(bm, name, mats)
    place(me, name, coll, (cx, cy, 0), rot)
    if det >= 2:  # balaios, potes de barro e cachos de banana em cima do tampo
        bg = new_bm()
        add_lathe(bg, [(0.0, 0.0), (0.16, 0.02), (0.22, 0.16), (0.24, 0.2)], 10, mi=0)
        bmesh.ops.translate(bg, verts=bg.verts, vec=(0.85, 0.32, 0.95))
        add_lathe(bg, [(r_, z_ + 0.95) for r_, z_ in ((0.0, 0.0), (0.12, 0.02), (0.17, 0.14), (0.1, 0.3), (0.07, 0.36), (0.09, 0.4))],
                  10, mi=1, center=(-0.95, 0.3))
        for k in range(6):
            add_box(bg, 0.06, 0.2, 0.05, center=(0.15 + k * 0.05, -0.25, 1.02), mi=2)
        place(mesh_from_bm(bg, name + "_Mercadoria", [MAT["MAT_Cesto"], MAT["MAT_Barro"], MAT["MAT_Flor_Amarela"]]),
              name + "_Mercadoria", coll, (cx, cy, 0), rot)
    col_box(name, (2.4, 1.1, 1.0), (cx, cy, 0), rot)


def bench(name, cx, cy, rot):
    bm = new_bm()
    add_box(bm, 1.8, 0.5, 0.08, center=(0, 0, 0.42), mi=0)
    add_box(bm, 0.3, 0.42, 0.42, center=(-0.65, 0, 0), mi=0)
    add_box(bm, 0.3, 0.42, 0.42, center=(0.65, 0, 0), mi=0)
    add_box(bm, 1.5, 0.38, 0.3, center=(0, 0, 0.0), mi=0)
    place(mesh_from_bm(bm, name, [MAT["MAT_Pedra_Muro"]]), name, C["Cover"], (cx, cy, 0), rot)
    col_box(name, (1.8, 0.5, 0.5), (cx, cy, 0), rot)


def crates(name, cx, cy, det, side=1.0):
    bm = new_bm()
    add_box(bm, 1.2, 1.2, 1.1, mi=0)
    add_box(bm, 1.24, 1.24, 0.1, center=(0, 0, 0.1), mi=1)
    add_box(bm, 1.24, 1.24, 0.1, center=(0, 0, 0.9), mi=1)
    if det >= 2:  # barril ao lado
        add_prism(bm, 12, 0.32, 0.9, z0=0.0, mi=2, center=(0.95 * side, 0.0), smooth=True)
    place(mesh_from_bm(bm, name, [MAT["MAT_Madeira"], MAT["MAT_Madeira_Verde"], MAT["MAT_Madeira"]]), name, C["Cover"], (cx, cy, 0))
    col_box(name, (1.2, 1.2, 1.1), (cx, cy, 0))
    if det >= 2:
        col_box(name + "_Barril", (0.64, 0.64, 0.9), (cx + 0.95 * side, cy, 0))


def tree(name, cx, cy, seed, det, scale=1.0, collide=True):
    rng = random.Random(seed)
    bm = new_bm()
    add_prism(bm, 8, 0.32 * scale, 2.6 * scale, z0=0, r_top=0.22 * scale, mi=0, smooth=True)
    for k in range(3):  # ramos
        a = k * 2.1 + rng.random()
        b = new_bm()
        add_prism(b, 6, 0.12 * scale, 1.4 * scale, z0=0, r_top=0.06 * scale, mi=0)
        bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(0.8, 3, 'X'))
        bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(a, 3, 'Z'))
        bmesh.ops.translate(b, verts=b.verts, vec=(0, 0, 2.2 * scale))
        tmp = bpy.data.meshes.new("tmp")
        b.to_mesh(tmp)
        b.free()
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    blobs = 4 + det * 2
    for k in range(blobs):
        a = rng.random() * math.tau
        d = rng.uniform(0.4, 1.6) * scale
        add_icosphere(bm, rng.uniform(0.9, 1.4) * scale, (d * math.cos(a), d * math.sin(a), rng.uniform(3.3, 4.3) * scale),
                      mi=1 + (k % 2), subdiv=1, squash=0.7)
    place(mesh_from_bm(bm, name, [MAT["MAT_Tronco"], MAT["MAT_Folhas"], MAT["MAT_Folhas_Claras"]]), name, C["Props"], (cx, cy, 0))
    if collide:
        col_box(name + "_Tronco", (0.7 * scale, 0.7 * scale, 3.0 * scale), (cx, cy, 0))


BUSH_HEIGHT = 2.0   # tapa uma personagem em pé (CharacterController 1.8 m)


def _leafbush_module():
    """Importa a porta do LeafBush.cs (mesma pasta do script)."""
    here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else bpy.path.abspath("//")
    if here not in sys.path:
        sys.path.insert(0, here)
    import leafbush_port
    return leafbush_port


def bush(name, cx, cy, w, d, seed, det, data):
    """Arbusto de esconder com o MESMO arbusto do jogo (LeafBush.cs + sistema Bush). Sem colisor.
    BUSH_<nome>_S<seed>: caixa da zona → no Unity vira componente Bush + folhas LeafBush com esta seed.
    BSHV_<nome>: as mesmas folhas geradas aqui pela porta do LeafBush, só para ver no Blender (não exportado)."""
    zname = f"BUSH_{name}_S{seed}"
    bm = new_bm()
    add_box(bm, w, d, BUSH_HEIGHT, mi=0)
    zone = place(mesh_from_bm(bm, zname, [MAT["MAT_Colisor"]]), zname, C["Gameplay"], (cx, cy, 0))
    zone.display_type = 'WIRE'
    zone.hide_render = True
    lb = _leafbush_module()
    # Unity: size = (2·meia largura X, altura do Bush − 0.2, 2·meia largura Z); Y do Unity = Z do Blender
    V, Cl, T = lb.generate((w, BUSH_HEIGHT, d), seed)
    verts = [(x, z, y) for (x, y, z) in V]               # troca Y↔Z (é um espelho: inverte-se a ordem dos triângulos)
    faces = [(T[i], T[i + 2], T[i + 1]) for i in range(0, len(T), 3)]
    me = bpy.data.meshes.new("BSHV_" + name)
    me.from_pydata(verts, [], faces)
    attr = me.color_attributes.new("Col", 'BYTE_COLOR', 'POINT')
    flat = []
    for c in Cl:
        flat.extend((c[0], c[1], c[2], 1.0))
    # BYTE_COLOR guarda sRGB; 'color_srgb' escreve os valores tal como o Unity os usa
    if hasattr(attr.data[0], "color_srgb") if len(attr.data) else False:
        attr.data.foreach_set("color_srgb", flat)
    else:
        attr.data.foreach_set("color", flat)
    me.materials.append(MAT["MAT_Folhas_Arbusto"])
    me.update()
    place(me, "BSHV_" + name, C["Preview"], (cx, cy, 0))
    data["bushes"].append(dict(name=zname, center=[cx, cy], size=[w, d, BUSH_HEIGHT], seed=seed))


def palm(name, cx, cy, seed):
    rng = random.Random(seed)
    bm = new_bm()
    lean = rng.uniform(-0.25, 0.25)
    prof = []
    for k in range(7):
        t = k / 6
        prof.append((0.22 - 0.06 * t, t * 6.0))
    add_lathe(bm, prof, 8, mi=0)
    bmesh.ops.translate(bm, verts=[v for v in bm.verts], vec=(0, 0, 0))
    for v in bm.verts:
        v.co.x += lean * (v.co.z / 6.0) ** 2 * 2
    top = Vector((lean * 2, 0, 6.0))
    for k in range(7):
        a = k * math.tau / 7
        d = Vector((math.cos(a), math.sin(a), 0))
        v = [bm.verts.new(top), bm.verts.new(top + d * 1.4 + Vector((-d.y, d.x, 0)) * 0.35 + Vector((0, 0, 0.3))),
             bm.verts.new(top + d * 2.6 + Vector((0, 0, -0.6))), bm.verts.new(top + d * 1.4 - Vector((-d.y, d.x, 0)) * 0.35 + Vector((0, 0, 0.3)))]
        bm.faces.new(v).material_index = 1
        bm.faces.new(list(reversed([bm.verts.new(x.co + Vector((0, 0, -0.02))) for x in v]))).material_index = 1
    place(mesh_from_bm(bm, name, [MAT["MAT_Tronco"], MAT["MAT_Folhas_Claras"]]), name, C["Props"], (cx, cy, 0))


def chain_ring(P):
    """Postes de pedra com correntes à volta do Pelourinho (como na referência).
    8 postes; correntes só em 4 vãos (diagonais) → 4 entradas largas nos eixos para as gemas."""
    det = P["detail"]
    coll = C["Props"]
    R = 5.4
    bm_post = new_bm()
    add_prism(bm_post, 8, 0.14, 0.72, z0=0, mi=0, smooth=True)
    add_icosphere(bm_post, 0.15, (0, 0, 0.74), mi=0, subdiv=1)
    me_post = mesh_from_bm(bm_post, "MSH_Poste_Corrente", [MAT["MAT_Pedra_Escura"]])
    posts = []
    for i in range(8):
        a = math.radians(22.5 + 45 * i)
        p = (R * math.cos(a), R * math.sin(a))
        posts.append(p)
        place(me_post, f"PRP_Poste_{i}", coll, (p[0], p[1], 0))
        col_box(f"Poste_{i}", (0.3, 0.3, 0.8), (p[0], p[1], 0), a)
    for i in range(8):
        a_mid = math.radians(45 * (i + 1))
        if (i + 1) % 2 == 0:
            continue  # vão aberto (eixos 0/90/180/270 graus)
        p0, p1 = Vector(posts[i]), Vector(posts[(i + 1) % 8])
        bm = new_bm()
        links = 10 if det >= 2 else 4
        for k in range(links + 1):
            t = k / links
            pos = p0.lerp(p1, t)
            sag = 0.62 - 0.22 * (1 - (2 * t - 1) ** 2)
            if det >= 2:
                add_torus(bm, 0.05, 0.012, seg=8, sides=4, mi=0, center=(pos.x, pos.y, sag), axis='X' if k % 2 else 'Y')
            else:
                add_box(bm, 0.05, 0.05, 0.05, center=(pos.x, pos.y, sag), mi=0)
        place(mesh_from_bm(bm, f"PRP_Corrente_{i}", [MAT["MAT_Ferro"]]), f"PRP_Corrente_{i}", coll)
        mid = (p0 + p1) / 2
        ang = math.atan2((p1 - p0).y, (p1 - p0).x)
        col_box(f"Corrente_{i}", ((p1 - p0).length - 0.3, 0.12, 0.7), (mid.x, mid.y, 0), ang)


def laundry(name, p0, p1, z, seed):
    """Estendal com roupa colorida entre duas fachadas (acima das cabeças, sem colisão)."""
    rng = random.Random(seed)
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    L = d.length
    ang = math.atan2(d.y, d.x)
    bm = new_bm()
    add_box(bm, L, 0.015, 0.015, center=(L / 2, 0, z), mi=0)
    x = 0.4
    k = 0
    while x < L - 0.5:
        wpc = rng.uniform(0.35, 0.7)
        hpc = rng.uniform(0.35, 0.75)
        sag = 0.12 * math.sin(math.pi * (x + wpc / 2) / L)
        add_box(bm, wpc, 0.02, hpc, center=(x + wpc / 2, 0, z - sag - hpc), mi=1 + (k % 4))
        x += wpc + rng.uniform(0.1, 0.3)
        k += 1
    mats = [MAT["MAT_Ferro"], MAT["MAT_Pano_Vermelho"], MAT["MAT_Pano_Amarelo"], MAT["MAT_Pano_Azul"], MAT["MAT_Reboco_Branco"]]
    place(mesh_from_bm(bm, name, mats), name, C["Props"], (p0.x, p0.y, 0), ang)


def plaza_pattern(P):
    """Calçada decorativa da praça: rosa de 8 pontas de basalto escuro e anel à volta das correntes (plano, sem relevo)."""
    bm = new_bm()
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        tip = Vector((6.2 * math.cos(a), 6.2 * math.sin(a), 0.016))
        l = Vector((3.4 * math.cos(a - 0.12), 3.4 * math.sin(a - 0.12), 0.016))
        r = Vector((3.4 * math.cos(a + 0.12), 3.4 * math.sin(a + 0.12), 0.016))
        bm.faces.new([bm.verts.new(v) for v in (l, r, tip)])
    n = 48
    for i in range(n):
        a0, a1 = i * math.tau / n, (i + 1) * math.tau / n
        vs = [bm.verts.new((R_ * math.cos(a), R_ * math.sin(a), 0.017)) for R_, a in ((5.85, a0), (6.05, a0), (6.05, a1), (5.85, a1))]
        bm.faces.new(vs)
    place(mesh_from_bm(bm, "GRD_Rosa_Calcada", [MAT["MAT_Basalto_Escuro"]]), "GRD_Rosa_Calcada", C["Ground"])


def thatched_house(name, x, y, rot, seed):
    """Casa de pedra com cobertura de palha (como as da Rua Banana), só no fundo."""
    rng = random.Random(seed)
    w, d, h = rng.uniform(4.0, 5.0), rng.uniform(3.2, 3.8), 2.6
    bm = new_bm()
    add_box(bm, w, d, h, mi=0)
    add_box(bm, 0.9, 0.08, 1.9, center=(0, -d / 2 - 0.02, 0), mi=2)
    hx, hy, top = w / 2 + 0.45, d / 2 + 0.45, h + 2.6
    c = [bm.verts.new(p) for p in ((-hx, -hy, h - 0.2), (hx, -hy, h - 0.2), (hx, hy, h - 0.2), (-hx, hy, h - 0.2))]
    t1, t2 = bm.verts.new((-w * 0.18, 0, top)), bm.verts.new((w * 0.18, 0, top))
    for f in ((c[0], c[1], t2, t1), (c[2], c[3], t1, t2), (c[1], c[2], t2), (c[3], c[0], t1)):
        bm.faces.new(f).material_index = 1
    place(mesh_from_bm(bm, name, [MAT["MAT_Pedra_Escura"], MAT["MAT_Palha"], MAT["MAT_Madeira"]]), name, C["Props"], (x, y, 0), rot)


def boat(name, x, y, rot, mat):
    bm = new_bm()
    prof = []
    for k in range(9):
        t = k / 8
        prof.append(t)
    L, W, H = 5.0, 1.5, 0.7
    rows = []
    for k in range(9):
        t = k / 8
        wx = W / 2 * math.sin(math.pi * t) ** 0.6
        xx = -L / 2 + L * t
        rows.append([bm.verts.new((xx, -wx, H)), bm.verts.new((xx, -wx * 0.6, 0.1)), bm.verts.new((xx, wx * 0.6, 0.1)), bm.verts.new((xx, wx, H))])
    for a, b in zip(rows[:-1], rows[1:]):
        for i in range(3):
            bm.faces.new((a[i], b[i], b[i + 1], a[i + 1])).material_index = 0
    add_box(bm, L * 0.8, 0.08, 0.08, center=(0, 0, H - 0.05), mi=1)
    place(mesh_from_bm(bm, name, [MAT[mat], MAT["MAT_Madeira"]]), name, C["Props"], (x, y, -1.15), rot)


def background(P):
    """Envolvente sem jogo: mar, montes, palmeiras e silhueta de fortaleza (sem colisão)."""
    coll = C["Props"]
    det = P["detail"]
    bm = new_bm()
    add_box(bm, 400, 400, 0.1, center=(0, 0, -1.2), mi=0)
    place(mesh_from_bm(bm, "BG_Mar", [MAT["MAT_Mar"]]), "BG_Mar", coll)
    rng = random.Random(7)
    hx, hy = P["arena_width"] / 2, P["arena_length"] / 2
    for i in range(10 + det * 4):
        a = rng.random() * math.tau
        side = 1 if math.sin(a) > -0.2 else -1
        d = rng.uniform(55, 95)
        x, y = d * math.cos(a), d * math.sin(a)
        if y < -hy - 10 and abs(x) < 50:
            continue  # o lado sul fica aberto para o mar
        bm = new_bm()
        add_icosphere(bm, rng.uniform(14, 26), (0, 0, -4), mi=0 if i % 3 else 1, subdiv=2, squash=0.45)
        place(mesh_from_bm(bm, f"BG_Monte_{i}", [MAT["MAT_Monte"], MAT["MAT_Monte_Verde"]]), f"BG_Monte_{i}", coll, (x, y, 0))
    # Fortaleza no alto (silhueta simplificada)
    bm = new_bm()
    add_icosphere(bm, 30, (0, 0, -8), mi=1, subdiv=2, squash=0.5)
    for k in range(6):
        a = k * math.tau / 6
        add_box(bm, 12, 1.5, 4, center=(11 * math.cos(a), 11 * math.sin(a), 6.5), mi=0)
    add_box(bm, 6, 6, 6, center=(0, 0, 6.5), mi=0)
    place(mesh_from_bm(bm, "BG_Fortaleza", [MAT["MAT_Pedra_Muro"], MAT["MAT_Monte"]]), "BG_Fortaleza", coll, (-35, 85, 4))
    # Árvores fora da área jogável (a vista para lá dos parapeitos; sem colisão)
    for i, (x, y) in enumerate(((hx + 3.5, 20.5), (-hx - 3.5, -20.5), (hx + 4.0, -9.0), (-hx - 4.0, 9.0))):
        tree(f"BG_Arvore_{i}", x, y, 40 + i, det, scale=0.9, collide=False)
    # Cidade Velha à volta: casas de palha, ruína de pedra e botes no mar
    if det >= 2:
        for i, (x, y, r) in enumerate(((hx + 12, -2, 0.3), (hx + 19, 6, -0.4), (-hx - 13, 3, 2.9), (-hx - 20, -7, 3.4),
                                      (hx + 15, -15, 0.1), (-hx - 15, 16, 3.0))):
            thatched_house(f"BG_CasaPalha_{i}", x, y, r, 70 + i)
        bm = new_bm()
        for k in range(4):
            add_box(bm, 1.6, 1.2, 6.0 - k * 1.1, center=(k * 3.2, 0, 0), mi=0)
        add_box(bm, 11.2, 1.2, 1.4, center=(4.8, 0, 4.0), mi=0)
        place(mesh_from_bm(bm, "BG_Ruina", [MAT["MAT_Pedra_Muro"]]), "BG_Ruina", coll, (18, 70, 1.0), 0.3)
        for i, (x, y, r, m) in enumerate(((-12, -hy - 30, 0.4, "MAT_Bote_Azul"), (-4, -hy - 36, -0.2, "MAT_Bote_Amarelo"),
                                          (9, -hy - 32, 1.2, "MAT_Bote_Vermelho"), (20, -hy - 40, 0.8, "MAT_Bote_Azul"))):
            boat(f"BG_Bote_{i}", x, y, r, m)
    # Palmeiras fora dos parapeitos
    for i, (x, y) in enumerate(((hx + 4, 15.5), (hx + 6, 11), (-hx - 4, -15.5), (-hx - 5, -11), (hx + 5, -16), (-hx - 6, 16))):
        palm(f"BG_Palmeira_{i}", x, y, i)
    # Casas de fundo (volumes simples atrás dos edifícios de jogo)
    mats_bg = ["MAT_Reboco_Ocre", "MAT_Reboco_Branco", "MAT_Reboco_Rosa", "MAT_Reboco_Azul"]
    for i in range(10):
        sxs = 1 if i % 2 else -1
        y = -24 + i * 5.3
        bm = new_bm()
        add_box(bm, 6, 4.5, 3.4 + (i % 3), mi=0)
        rb = new_bm()
        roof_mesh(rb, 6, 4.5, 3.4 + (i % 3), 1.2, 'hip', 0, 1)
        tmp = bpy.data.meshes.new("tmp")
        rb.to_mesh(tmp)
        rb.free()
        for p in tmp.polygons:
            p.material_index += 1
        bm.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
        place(mesh_from_bm(bm, f"BG_Casa_{i}", [MAT[mats_bg[i % 4]], MAT["MAT_Telha"], MAT["MAT_Madeira"]]),
              f"BG_Casa_{i}", coll, (sxs * (hx + 8 + (i % 3) * 3), y, 0), 0.2 * (i % 3))


# =============================================================================== montagem do mapa

def stable_seed(text):
    return sum((i + 1) * ord(c) for i, c in enumerate(text)) % 1000


def build_map(P):
    L = Layout(P)
    det = P["detail"]
    wall_h = P["wall_height"]
    plasters = {(1, 1): "MAT_Reboco_Ocre", (-1, 1): "MAT_Reboco_Terracota", (1, -1): "MAT_Reboco_Azul", (-1, -1): "MAT_Reboco_Rosa"}
    shutters = {(1, 1): "Verde", (-1, 1): "Azul", (1, -1): "Azul", (-1, -1): "Verde"}
    data = dict(boxes=[], spawns=[], lands=[], waypoints=[], bushes=[])

    ground(L, P)
    if det >= 2:
        plaza_pattern(P)
    pel = pelourinho(P)
    gen = gem_generator(L, P, pel)
    chain_ring(P)

    for sx in (1, -1):
        for sy in (1, -1):
            q = (sx, sy)
            tag = ("E" if sx > 0 else "W") + ("N" if sy > 0 else "S")
            # Casa de esquina (sobrado de 2 pisos) junto ao spawn
            building(L, f"BLD_Esquina_{tag}", L.rect(sx * 9.0, sx * 20.0, sy * 19.0, sy * 25.0), 6.2, plasters[q], roof='hip',
                     storeys=2, faces=('S' if sy > 0 else 'N', 'W' if sx > 0 else 'E'), shutters=shutters[q], balcony=True, det=det)
            # Muro de pedra que fecha o spawn (barra horizontal da referência) — abre um flanco junto à borda
            y = L.Y(sy * 15.3)
            wall_run(f"CVR_MuroSpawn_{tag}", (L.X(sx * 6.0), y), (L.X(sx * 16.0), y), C["Cover"])
            col_box(f"MuroSpawn_{tag}", (abs(L.X(16.0) - L.X(6.0)), 0.6, wall_h), (L.X(sx * 11.0), y, 0))
            # Casa térrea da fila (lado de dentro do corredor de flanco)
            building(L, f"BLD_Fila_{tag}", L.rect(sx * 13.0, sx * 20.0, sy * 7.0, sy * 12.0), 3.6, plasters[(-sx, sy)],
                     roof='gable', storeys=1, faces=('N' if sy > 0 else 'S', 'W' if sx > 0 else 'E'), shutters=shutters[(-sx, -sy)], det=det)
            # Parapeito de limite (vista para o mar) no corredor exterior
            x = L.X(sx * 20.3)
            wall_run(f"CVR_Parapeito_{tag}", (x, L.Y(sy * 12.0)), (x, L.Y(sy * 19.0)), C["Cover"], module="Parapeito_2m")
            # Banca de mercado (meia cobertura na praça)
            cloth = ["MAT_Pano_Vermelho", "MAT_Pano_Azul", "MAT_Pano_Amarelo", "MAT_Pano_Verde"][(sx > 0) * 2 + (sy > 0)]
            stall(f"CVR_Banca_{tag}", L.X(sx * 4.6), L.Y(sy * 10.5), 0.0 if sy < 0 else math.pi, cloth, det)
            # Banco de pedra perto do centro
            bench(f"CVR_Banco_{tag}", L.X(sx * 2.6), L.Y(sy * 6.9), 0.0)
            # Caixotes dentro da bolsa lateral
            crates(f"CVR_Caixotes_{tag}", L.X(sx * 12.0), L.Y(sy * 2.6), det, side=sx)
            # Arbustos de esconder (substituem as árvores da área jogável; mesmas zonas verdes):
            # junção praça/corredor, canto do spawn e faixa encostada ao edifício da bolsa lateral
            bush(f"Juncao_{tag}", L.X(sx * 7.0), L.Y(sy * 12.5), L.X(2.8), L.Y(2.6), stable_seed(tag), det, data)
            bush(f"Spawn_{tag}", L.X(sx * 6.9), L.Y(sy * 21.4), L.X(2.8), L.Y(2.8), stable_seed(tag) + 7, det, data)
            bush(f"Bolsa_{tag}", L.X(sx * 15.0), L.Y(sy * 2.8), L.X(2.0), L.Y(3.6), stable_seed(tag) + 13, det, data)
            # Estendal de roupa sobre o corredor exterior (entre a casa da fila e a casa de esquina)
            if det >= 2:
                laundry(f"PRP_Estendal_{tag}", (L.X(sx * 18.0), L.Y(sy * 12.0)), (L.X(sx * 18.0), L.Y(sy * 19.0)), 3.25, stable_seed(tag) + 3)
            # Waypoints dos bots
            for (wx, wy) in ((12.5, 13.6), (18.0, 17.3), (12.0, 0.0), (11.0, 5.8), (6.4, 7.2), (4.0, 13.6), (14.5, 4.8)):
                if wx == 12.0 and sy < 0:
                    continue
                data["waypoints"].append((L.X(sx * wx), L.Y(sy * wy)))
        # Edifício da bolsa (coluna de bolinhas da referência): fecha a bolsa pelo lado de fora
        building(L, f"BLD_Bolsa_{'E' if sx > 0 else 'W'}", L.rect(sx * 16.0, sx * 20.0, -7.0, 7.0), 4.2,
                 "MAT_Reboco_Branco", roof='gable', storeys=1, faces=('W' if sx > 0 else 'E',), shutters="Verde",
                 roof_rot=math.pi / 2, det=det)
        # Muro interior da bolsa com janela de tiro ao meio (cobertura total, vê-se pela janela)
        x = L.X(sx * 8.8)
        wall_run(f"CVR_MuroBolsa_{'E' if sx > 0 else 'W'}", (x, L.Y(-5.0)), (x, L.Y(5.0)), C["Cover"], window_at={2})
        seg = abs(L.Y(5.0) - L.Y(-5.0)) / 5.0           # comprimento de cada segmento (2 m na escala base)
        side = 'E' if sx > 0 else 'W'
        pil = 0.45 * seg / 2.0                              # pilares da janela (0.45 m no módulo de 2 m)
        col_box(f"MuroBolsa_{side}_A", (0.6, seg * 2, wall_h), (x, L.Y(-5.0) + seg, 0))
        col_box(f"MuroBolsa_{side}_B", (0.6, seg * 2, wall_h), (x, L.Y(5.0) - seg, 0))
        col_box(f"MuroBolsa_{side}_Peitoril", (0.6, seg, 1.0), (x, 0, 0))
        col_box(f"MuroBolsa_{side}_Verga", (0.6, seg, wall_h - 1.9), (x, 0, 1.9))
        col_box(f"MuroBolsa_{side}_PilarA", (0.6, pil, wall_h), (x, -seg / 2 + pil / 2, 0))
        col_box(f"MuroBolsa_{side}_PilarB", (0.6, pil, wall_h), (x, seg / 2 - pil / 2, 0))

    # Edifícios de topo atrás dos spawns
    chapel(L, "BLD_Capela_N", L.rect(-9.0, 9.0, 25.0, 28.5), facing_y=1)
    building(L, "BLD_Sobrado_S", L.rect(-9.0, 9.0, -25.0, -28.5), 7.0, "MAT_Reboco_Ocre", roof='hip', storeys=2,
             faces=('N',), shutters="Azul", balcony=True, det=det)

    # Spawns (5 por equipa) e waypoints
    for team, sy in (("Blue", -1), ("Red", 1)):
        for i, x in enumerate((-5.0, -2.5, 0.0, 2.5, 5.0)):
            p = (L.X(x), L.Y(sy * 22.4), 0.05)
            m = marker(f"SPAWN_{team}_{i:02d}", p, C["Gameplay"], 0.8)
            m.rotation_euler = (0, 0, 0.0 if sy < 0 else math.pi)  # olha para o centro
            data["spawns"].append(dict(team=team, pos=p))
    for (x, y) in ((0, L.Y(-17.5)), (0, L.Y(17.5)), (L.X(6.2), 0), (L.X(-6.2), 0), (0, L.Y(-7.0)), (0, L.Y(7.0))):
        data["waypoints"].append((x, y))
    for i, (x, y) in enumerate(data["waypoints"]):
        marker(f"WP_{i:02d}", (x, y, 0.1), C["Gameplay"], 0.3, 'PLAIN_AXES')

    background(P)

    for ob in C["Collision"].objects:
        data["boxes"].append(dict(name=ob.name, kind="mesh" if ob.name.startswith("COL_Mesh_") else "box",
                                  loc=list(ob.location), rot_z=ob.rotation_euler.z,
                                  size=[ob.dimensions.x, ob.dimensions.y, ob.dimensions.z]))
    data["lands"] = [list(l.location) for l in gen["lands"]]
    data["gem_spawn"] = list(gen["spawn"].location)
    data["arena"] = dict(width=P["arena_width"], length=P["arena_length"])
    data["pelourinho_step_radii"] = [r for r, _ in PEL["steps"]]
    return pel, gen, data


# =============================================================================== animação

def fcurves_of(action):
    """F-curves de uma Action (compatível com Actions clássicas e com camadas/slots do Blender 4.4+)."""
    if hasattr(action, "fcurves") and len(getattr(action, "fcurves", [])) > 0:
        return list(action.fcurves)
    out = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                out.extend(bag.fcurves)
    return out


def set_interp(action, interp='BEZIER', cyclic=False):
    for fc in fcurves_of(action):
        for kp in fc.keyframe_points:
            kp.interpolation = interp
        if cyclic and not any(m.type == 'CYCLES' for m in fc.modifiers):
            fc.modifiers.new('CYCLES')


def key(ob, frame, loc=None, rot=None, scale=None):
    if loc is not None:
        ob.location = loc
        ob.keyframe_insert("location", frame=frame)
    if rot is not None:
        ob.rotation_euler = rot
        ob.keyframe_insert("rotation_euler", frame=frame)
    if scale is not None:
        ob.scale = scale if hasattr(scale, "__len__") else (scale, scale, scale)
        ob.keyframe_insert("scale", frame=frame)


def take_action(ob, name):
    """Retira a Action ativa do objeto, renomeia-a e guarda-a (fake user)."""
    ad = ob.animation_data
    act = ad.action
    act.name = name
    act.use_fake_user = True
    ad.action = None
    return act


def push_strip(ob, act, track_name, start, extrap='HOLD_FORWARD', blend='REPLACE', repeat=1.0):
    ad = ob.animation_data or ob.animation_data_create()
    tr = ad.nla_tracks.get(track_name) or ad.nla_tracks.new()
    tr.name = track_name
    st = tr.strips.new(act.name, int(start), act)
    # Blender 4.4+: Actions com slots — a faixa NLA tem de apontar para o slot, senão não é avaliada
    if hasattr(st, "action_slot") and getattr(act, "slots", None) and len(act.slots) > 0 and st.action_slot is None:
        st.action_slot = act.slots[0]
    st.extrapolation = extrap
    st.blend_type = blend
    st.repeat = repeat
    return st


def mat_key(mat, frame, strength):
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    sock = bsdf.inputs["Emission Strength"]
    sock.default_value = strength
    sock.keyframe_insert("default_value", frame=frame)


def animate(P, pel, gen, data):
    """Timeline de demonstração (Blender) + Actions exportáveis.
    Fases: 1 espera · 2 aviso · 3 surgimento · 4 arco · 5 flutuação/rotação · 6 recolha."""
    sc = bpy.context.scene
    fps = P["fps"]
    F = {}
    t = 1
    for ph in ("idle", "warn", "emerge", "arc", "float", "collect", "tail"):
        F[ph] = t
        t += max(1, round(P["t_" + ph] * fps))
    F["end"] = t
    sc.frame_start, sc.frame_end = 1, F["end"]
    # Marcadores na timeline (nomes das fases)
    for ph, label in (("idle", "1 Espera"), ("warn", "2 Aviso"), ("emerge", "3 Surgimento"), ("arc", "4 Arco"),
                      ("float", "5 Flutuar/Rodar"), ("collect", "6 Recolha"), ("tail", "Fim")):
        sc.timeline_markers.new(label, frame=F[ph])

    me_gem = gem_mesh()
    # ---------------------------------------------------------------- gema exportável (origem 0,0,0)
    gema = place(me_gem, "Gema", C["Gems"], (0, 0, 0))
    OBJ["Gema"] = gema
    # Actions exportáveis (relativas à origem da gema). Unity: só referência — no jogo o movimento é do código.
    dur_e = F["arc"] - F["emerge"]
    key(gema, 1, loc=(0, 0, -0.6), scale=0.0)
    key(gema, 1 + dur_e, loc=(0, 0, 0), scale=1.0)
    act_surgir = take_action(gema, "ACT_Gema_Surgir")
    per = max(2, round(2.0 * fps))
    for k in range(5):
        f = 1 + k * per / 4
        key(gema, f, loc=(0, 0, 0.12 * math.sin(k * math.pi / 2)), rot=(0, 0, k * math.pi / 2))
    act_flutuar = take_action(gema, "ACT_Gema_Flutuar")
    set_interp(act_flutuar, 'LINEAR')
    dur_c = F["tail"] - F["collect"]
    key(gema, 1, loc=(0, 0, 0), rot=(0, 0, 0), scale=1.0)
    key(gema, 1 + dur_c * 0.35, loc=(0, 0, 0.35), rot=(0, 0, math.pi * 2), scale=1.3)
    key(gema, 1 + dur_c, loc=(0, 0, 0.9), rot=(0, 0, math.pi * 5), scale=0.0)
    act_recolher = take_action(gema, "ACT_Gema_Recolher")
    # Cada clip na sua faixa e em intervalos separados: no frame 0 nenhum está ativo → pose de repouso limpa no FBX
    for k, a in enumerate((act_surgir, act_flutuar, act_recolher)):
        push_strip(gema, a, a.name, 1 + 100 * k, extrap='NOTHING')
    gema.location = (0, 0, 0)
    gema.rotation_euler = (0, 0, 0)
    gema.scale = (1, 1, 1)

    # ---------------------------------------------------------------- gema da demonstração (posições do mundo)
    demo = place(me_gem, "Demo_Gema", C["Preview"], (0, 0, gen["z_spawn"]))
    p_spawn = Vector((0, 0, gen["z_spawn"]))
    land_idx = 6  # GEM_Land_06 (-Y, lado azul) — o jogador azul de demonstração apanha-a
    p_land = Vector(data["lands"][land_idx])
    # 3 surgimento: escala 0 → 1 e sobe 0.6 m
    key(demo, F["emerge"], loc=p_spawn, rot=(0, 0, 0), scale=0.0)
    key(demo, F["arc"], loc=p_spawn + Vector((0, 0, 0.6)), rot=(0, 0, math.pi), scale=1.0)
    a_surgir = take_action(demo, "ACT_Demo_Gema_Surgir")
    # 4 arco: parábola até à marca de aterragem
    n = 8
    start = p_spawn + Vector((0, 0, 0.6))
    for k in range(n + 1):
        u = k / n
        p = start.lerp(p_land, u) + Vector((0, 0, 2.2 * 4 * u * (1 - u)))
        key(demo, F["arc"] + u * (F["float"] - F["arc"]), loc=p, rot=(0, 0, math.pi + u * math.pi * 3), scale=1.0)
    a_arco = take_action(demo, "ACT_Demo_Gema_Arco")
    set_interp(a_arco, 'LINEAR')
    # 5 flutuar e rodar
    nf = F["collect"] - F["float"]
    steps = 12
    for k in range(steps + 1):
        u = k / steps
        key(demo, F["float"] + u * nf, loc=p_land + Vector((0, 0, 0.12 * math.sin(u * math.tau * 1.5))),
            rot=(0, 0, math.pi * 4 + u * math.tau * 1.2), scale=1.0)
    a_flut = take_action(demo, "ACT_Demo_Gema_Flutuar")
    # 6 recolha: sobe, roda e encolhe
    nc = F["tail"] - F["collect"]
    key(demo, F["collect"], loc=p_land, rot=(0, 0, 0), scale=1.0)
    key(demo, F["collect"] + nc * 0.35, loc=p_land + Vector((0, 0, 0.5)), rot=(0, 0, math.tau), scale=1.35)
    key(demo, F["tail"], loc=p_land + Vector((0, 0, 1.3)), rot=(0, 0, math.tau * 2.5), scale=0.0)
    a_rec = take_action(demo, "ACT_Demo_Gema_Recolher")
    push_strip(demo, a_surgir, "Gema", F["emerge"], extrap='HOLD')
    push_strip(demo, a_arco, "Gema", F["arc"])
    push_strip(demo, a_flut, "Gema", F["float"])
    push_strip(demo, a_rec, "Gema", F["collect"])

    # ---------------------------------------------------------------- emissor: espera (loop) + aviso (aditivo)
    halo = gen["halo"]
    loop = max(2, round(4.0 * fps))
    key(halo, 1, rot=(0, 0, 0))
    key(halo, 1 + loop, rot=(0, 0, math.tau))
    a_idle = take_action(halo, "ACT_Emissor_Espera")
    set_interp(a_idle, 'LINEAR')
    nw = F["arc"] - F["warn"]
    key(halo, 1, rot=(0, 0, 0), scale=(0, 0, 0))
    key(halo, 1 + nw * 0.6, rot=(0, 0, math.tau * 1.5), scale=(0.35, 0.35, 0.35))
    key(halo, 1 + nw, rot=(0, 0, math.tau * 2.0), scale=(0.15, 0.15, 0.15))
    key(halo, 1 + nw + 15, rot=(0, 0, math.tau * 2.0), scale=(0, 0, 0))
    a_warn = take_action(halo, "ACT_Emissor_Aviso")
    push_strip(halo, a_idle, "Espera", 1, extrap='HOLD', repeat=max(1.0, F["end"] / loop + 1))
    push_strip(halo, a_warn, "Aviso", F["warn"], extrap='NOTHING', blend='ADD')

    # Base: roda devagar no sentido contrário
    base = gen["base"]
    key(base, 1, rot=(0, 0, 0))
    key(base, 1 + loop, rot=(0, 0, -math.tau))
    a_base = take_action(base, "ACT_EmissorBase_Espera")
    set_interp(a_base, 'LINEAR')
    push_strip(base, a_base, "Espera", 1, extrap='HOLD', repeat=max(1.0, F["end"] / loop + 1))

    # ---------------------------------------------------------------- brilho (animação de material — só Blender)
    em = MAT["MAT_Emissor"]
    mat_key(em, F["idle"], 2.0)
    mat_key(em, F["warn"], 2.0)
    mat_key(em, F["emerge"] - 4, 14.0)
    mat_key(em, F["arc"], 3.0)
    mat_key(em, F["end"], 2.0)
    mk = MAT["MAT_Marca_Aterragem"]
    mat_key(mk, F["arc"] - 1, 0.0)
    mat_key(mk, F["arc"] + 4, 6.0)
    mat_key(mk, F["float"] + 6, 6.0)
    mat_key(mk, F["float"] + 16, 0.0)
    # Só a marca da aterragem da demo acende: malha própria com o material animado; as outras ficam apagadas
    mark_demo = gen["marks"][land_idx]
    mark_demo.data = mark_demo.data.copy()
    mark_demo.data.name = "GEN_Marca_Aterragem_Demo"
    mark_demo.data.materials[0] = mk

    # ---------------------------------------------------------------- partículas a subir junto da coluna (só Blender)
    bm = new_bm()
    add_icosphere(bm, 0.06, (0, 0, 0), mi=0, subdiv=1)
    spark = place(mesh_from_bm(bm, "FX_Faisca", [MAT["MAT_Faisca"]]), "FX_Faisca", C["Preview"], (0, 0, -30))
    bm = new_bm()
    add_torus(bm, 0.55, 0.02, seg=24, sides=3)
    emitter = place(mesh_from_bm(bm, "FX_Emissor_Particulas", [MAT["MAT_Faisca"]]), "FX_Emissor_Particulas", C["Preview"],
                    (0, 0, pel["z_shaft0"] + 0.12))
    emitter.hide_render = True
    mod = emitter.modifiers.new("Particulas", 'PARTICLE_SYSTEM')
    ps = mod.particle_system.settings
    ps.name = "PS_Particulas_Coluna"
    ps.count = 140 * P["detail"]
    ps.frame_start = F["warn"]
    ps.frame_end = F["emerge"] + 4
    ps.lifetime = max(20, round(1.3 * fps))
    ps.lifetime_random = 0.4
    ps.emit_from = 'VERT'
    ps.normal_factor = 0.15
    ps.factor_random = 0.25
    ps.effector_weights.gravity = -0.32
    ps.render_type = 'OBJECT'
    ps.instance_object = spark
    ps.particle_size = 1.0
    ps.size_random = 0.6
    ps.use_rotations = False
    emitter.show_instancer_for_render = False

    # ---------------------------------------------------------------- efeito de recolha (só Blender)
    bm = new_bm()
    add_torus(bm, 0.5, 0.03, seg=32, sides=4)
    flash = place(mesh_from_bm(bm, "FX_Recolha_Anel", [MAT["MAT_Faisca"]]), "FX_Recolha_Anel", C["Preview"], p_land)
    key(flash, F["collect"], scale=0.0)
    key(flash, F["collect"] + 2, scale=0.4)
    key(flash, F["collect"] + nc * 0.8, scale=3.0)
    key(flash, F["collect"] + nc * 0.8 + 1, scale=0.0)
    shards = []
    rng = random.Random(3)
    bm = new_bm()
    add_prism(bm, 3, 0.05, 0.12, z0=-0.06, r_top=0.0)
    me_sh = mesh_from_bm(bm, "FX_Lasca", [MAT["MAT_Gema"]])
    for i in range(8):
        sh = place(me_sh, f"FX_Lasca_{i}", C["Preview"], p_land)
        d = Vector((math.cos(i * math.tau / 8), math.sin(i * math.tau / 8), rng.uniform(0.6, 1.4)))
        key(sh, F["collect"], loc=p_land + Vector((0, 0, 0.3)), rot=(0, 0, 0), scale=0.0)
        key(sh, F["collect"] + 3, loc=p_land + Vector((0, 0, 0.4)), scale=1.0)
        key(sh, F["tail"] + 6, loc=p_land + d * 1.4, rot=(rng.random() * 6, rng.random() * 6, 0), scale=0.0)
        shards.append(sh)

    # ---------------------------------------------------------------- jogador de demonstração (cápsula azul)
    bm = new_bm()
    add_lathe(bm, [(0.0, 0.0), (0.3, 0.08), (0.4, 0.4), (0.4, 1.4), (0.3, 1.72), (0.0, 1.8)], 12, mi=0)
    add_box(bm, 0.5, 0.12, 0.16, center=(0, -0.38, 1.38), mi=1)
    player = place(mesh_from_bm(bm, "Demo_Jogador_Azul", [MAT["MAT_Jogador_Demo"], MAT["MAT_Ferro"]]), "Demo_Jogador_Azul", C["Preview"])
    p0 = Vector((-1.5, -10.5, 0))
    p_stop = Vector((p_land.x, p_land.y - 0.9, 0))
    key(player, 1, loc=p0, rot=(0, 0, 0))
    key(player, F["float"] + 10, loc=p0.lerp(p_stop, 0.55), rot=(0, 0, 0.1))
    key(player, F["collect"], loc=p_stop, rot=(0, 0, 0))
    key(player, F["end"], loc=p_stop + Vector((1.5, 0.6, 0)), rot=(0, 0, -0.6))
    OBJ["player"] = player
    return F


# =============================================================================== câmaras, luz, mundo

def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()


def setup_preview(P, pel, F):
    coll = C["Preview"]
    sc = bpy.context.scene
    # Sol quente de fim de tarde + luz de preenchimento
    sun = bpy.data.lights.new("LGT_Sol", 'SUN')
    sun.energy = 4.2
    sun.color = (1.0, 0.93, 0.82)
    sun.angle = math.radians(2.0)
    o = bpy.data.objects.new("LGT_Sol", sun)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(42), 0, math.radians(-35))
    fill = bpy.data.lights.new("LGT_Ceu", 'SUN')
    fill.energy = 0.8
    fill.color = (0.7, 0.8, 1.0)
    o = bpy.data.objects.new("LGT_Ceu", fill)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(-60), 0, math.radians(140))
    # Mundo (céu azul claro)
    w = bpy.data.worlds.get("WRL_CidadeVelha") or bpy.data.worlds.new("WRL_CidadeVelha")
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.52, 0.72, 0.95, 1)
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
        if ortho:
            ob.rotation_euler = (0, 0, 0)
        else:
            look_at(ob, target)
        cams[name] = ob
        return ob

    cam("CAM_Planta", (0, 0, 60), (0, 0, 0), ortho=max(P["arena_width"], P["arena_length"]) + 6)
    cam("CAM_Pelourinho", (7.2, -8.6, 2.3), (0, 0, 3.4), lens=28)
    cam("CAM_Gemas", (1.6, -13.6, 3.0), (-0.2, -2.0, 4.6), lens=18)
    pres = cam("CAM_Apresentacao", (26, -34, 22), (0, 0, 0), lens=30)
    # Câmara de apresentação: órbita lenta ao longo da timeline
    piv = bpy.data.objects.new("CAM_Apresentacao_Pivot", None)
    coll.objects.link(piv)
    pres.parent = piv
    piv.rotation_euler = (0, 0, 0)
    piv.keyframe_insert("rotation_euler", frame=1)
    piv.rotation_euler = (0, 0, math.radians(35))
    piv.keyframe_insert("rotation_euler", frame=F["end"])
    act = piv.animation_data.action
    act.name = "ACT_Camara_Apresentacao"
    set_interp(act, 'LINEAR')
    sc.camera = cams["CAM_Gemas"]
    return cams


# =============================================================================== exportação

def objects_in(names):
    out = []
    for n in names:
        out.extend(C[n].all_objects)
    return out


def export_fbx(path, objs, anim=False, nla=False):
    import addon_utils
    try:
        addon_utils.enable("io_scene_fbx", default_set=True)
    except Exception:
        pass
    vl = bpy.context.view_layer
    vl.update()
    # O exportador FBX perde materiais em malhas partilhadas (módulos linked) → cópias temporárias
    seen, swapped = set(), []
    for ob in objs:
        if ob.type == 'MESH':
            if ob.data.name in seen:
                swapped.append((ob, ob.data))
                ob.data = ob.data.copy()
            seen.add(ob.data.name)
    for ob in vl.objects:
        ob.select_set(False)
    names = set(o.name for o in vl.objects)
    for ob in objs:
        if ob.name in names:
            ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', global_scale=1.0,
        bake_space_transform=True, axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, path_mode='STRIP', embed_textures=False,
        bake_anim=anim, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=nla,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
    for ob, me in swapped:
        tmp = ob.data
        ob.data = me
        bpy.data.meshes.remove(tmp)
    for ob in vl.objects:
        ob.select_set(False)
    return len(bpy.context.selected_objects)


# =============================================================================== render

def render_still(cam, path, res_x, res_y, samples, frame=None):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x = res_x
    sc.render.resolution_y = res_y
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    sc.cycles.samples = samples
    if frame is not None:
        # avança frame a frame para as partículas serem simuladas por ordem
        for f in range(sc.frame_current if sc.frame_current <= frame else 1, frame + 1):
            sc.frame_set(f)
    bpy.ops.render.render(write_still=True)


def setup_render(P):
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = P["samples"]
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 4
    sc.cycles.diffuse_bounces = 2
    sc.cycles.glossy_bounces = 1
    sc.cycles.transparent_max_bounces = 4
    sc.render.image_settings.file_format = 'PNG'
    try:
        sc.view_settings.view_transform = 'AgX'
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except TypeError:
        pass
    C["Collision"].hide_render = True
    C["Modules"].hide_render = True


# =============================================================================== principal

def main():
    P = parse_args()
    random.seed(P["seed"])
    out = P["out_dir"]
    os.makedirs(out, exist_ok=True)
    tex_dir = os.path.join(out, "Textures")
    sc = setup_scene(P)
    # Sem janela (modo --background) o contexto continua na cena original: forçar a cena própria
    with bpy.context.temp_override(scene=sc, view_layer=sc.view_layers[0]):
        run(P, sc, out, tex_dir)
    print("[CV] Concluído.")


def run(P, sc, out, tex_dir):
    tex = make_textures(P, tex_dir)
    make_materials(tex)
    build_modules(P)
    pel, gen, data = build_map(P)
    F = animate(P, pel, gen, data)
    cams = setup_preview(P, pel, F)
    # Coleção de colisores visível só no viewport como wireframe
    for ob in C["Collision"].objects:
        ob.hide_render = True

    data["frames"] = F
    data["params"] = {k: v for k, v in P.items() if k != "out_dir"}
    with open(os.path.join(out, "cidade_velha_layout.json"), "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=1)

    report = dict(triangles={}, objects={})
    for n in COLL_NAMES:
        tris = 0
        for ob in C[n].all_objects:
            if ob.type == 'MESH':
                tris += sum(len(p.vertices) - 2 for p in ob.data.polygons)
        report["triangles"][n] = tris
        report["objects"][n] = len(C[n].all_objects)
    print("[CV] Triângulos por collection:", report["triangles"])

    if P["export"]:
        fbx_dir = os.path.join(out, "FBX")
        os.makedirs(fbx_dir, exist_ok=True)
        static = [o for o in objects_in(["Ground", "Pelourinho", "Buildings", "Cover", "Props", "Collision"])]
        export_fbx(os.path.join(fbx_dir, "CidadeVelha_Static.fbx"), static)
        sc.frame_set(0)  # antes de qualquer clip: transformações de repouso
        gen_objs = list(C["GemGenerator"].all_objects)
        animated = [o for o in gen_objs if o.name in ("GEN_Emissor_Halo", "GEN_Emissor_Base")]
        export_fbx(os.path.join(fbx_dir, "CidadeVelha_GemGenerator.fbx"), gen_objs)
        export_fbx(os.path.join(fbx_dir, "CidadeVelha_Emissor_Anim.fbx"), animated, anim=True, nla=True)
        export_fbx(os.path.join(fbx_dir, "CidadeVelha_Gem.fbx"), [OBJ["Gema"]], anim=True, nla=True)
        print("[CV] FBX exportados para", fbx_dir)

    sc.frame_set(F["float"] + 20)
    if P["save_blend"]:
        path = os.path.join(out, "CidadeVelha_Pelourinho.blend")
        bpy.ops.wm.save_as_mainfile(filepath=path, check_existing=False)
        try:
            bpy.ops.file.make_paths_relative()
            bpy.ops.wm.save_mainfile()
        except Exception as e:
            print("[CV] Aviso: caminhos relativos:", e)
        print("[CV] .blend guardado em", path)

    if P["render"] or P["anim_preview"]:
        setup_render(P)
        rdir = os.path.join(out, "Renders")
        os.makedirs(rdir, exist_ok=True)
    if P["render"]:
        R = P["res"]
        W, Lg = P["arena_width"] + 6, P["arena_length"] + 6
        cams["CAM_Planta"].data.ortho_scale = max(W, Lg)
        render_still(cams["CAM_Planta"], os.path.join(rdir, "planta_rotas.png"), int(R * W / Lg), R, P["samples"], frame=F["float"] + 20)
        render_still(cams["CAM_Pelourinho"], os.path.join(rdir, "pelourinho_perspetiva.png"), R, int(R * 0.75), P["samples"])
        render_still(cams["CAM_Apresentacao"], os.path.join(rdir, "apresentacao.png"), R, int(R * 0.5625), P["samples"])
        # Fases da geração de gemas (contact sheet montado fora do Blender)
        bpy.context.scene.frame_set(1)
        for ph, f in (("1_espera", F["idle"] + 10), ("2_aviso", F["warn"] + round((F["emerge"] - F["warn"]) * 0.75)),
                      ("3_surgimento", F["arc"] - 2), ("4_arco", (F["arc"] + F["float"]) // 2),
                      ("5_flutuar", F["float"] + 30), ("6_recolha", F["collect"] + round((F["tail"] - F["collect"]) * 0.4))):
            render_still(cams["CAM_Gemas"], os.path.join(rdir, f"gema_{ph}.png"), 800, 450, max(12, P["samples"] // 2), frame=f)
    if P["anim_preview"]:
        sc = bpy.context.scene
        sc.camera = cams["CAM_Gemas"]
        sc.render.resolution_x, sc.render.resolution_y = 480, 270
        sc.cycles.samples = 8
        sc.frame_step = 2
        sc.render.filepath = os.path.join(rdir, "anim", "gema_")
        bpy.ops.render.render(animation=True)


if __name__ == "__main__":
    main()
