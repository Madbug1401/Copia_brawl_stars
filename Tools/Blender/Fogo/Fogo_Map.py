"""
FPS-LAN · Mapa de sobrevivência "Fogo" (ilha do Fogo, Cabo Verde)
==================================================================

Interpretação artística da paisagem vulcânica do Fogo para uma partida de sobrevivência com tempestade de cinza:
planalto de cinza e basalto, rios e bolsas de lava nas zonas que a planta marca como água, arbustos secos de
esconder, coberturas de basalto e, ao centro, um grande vulcão com cratera aberta, quatro desfiladeiros de
entrada (com rampas largas) e uma arena interior de rocha sólida onde acaba o combate.

Lê a planta de referência (referencias/planta_fogo.png) e classifica-a por cor (lava, arbustos, muros,
caixas, vedações, postes, pontos de spawn). O mapa é simétrico: usa o quadrante superior esquerdo e espelha-o.
A região do círculo vermelho é substituída pelo vulcão; o interior da cratera conserva o desenho da planta
(bolsas de lava em L, muros centrais) com retoques para caber na arena.

Uso:
    blender --background --python Fogo_Map.py -- --out ./out --export --save-blend --render [--anim-preview]
    Opções: --detail 1|2|3  --seed 238  --samples 20 --res 1200  --plan caminho.png

Unidades: metros. 0,1 m por píxel da planta → área 60,8 × 64 m. Blender Z para cima; Unity recebe pelos nomes
(ver LEIA-ME.md). Cria a cena "FG_Fogo" e não apaga mais nada.
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
for sub in ("CidadeVelha", "Buracona"):
    d = os.path.normpath(os.path.join(HERE, "..", sub))
    if d not in sys.path:
        sys.path.insert(0, d)
import cidade_velha_pelourinho as cv      # primitivas bmesh, UV, materiais, colisores, exportação, render
import Buracona_Map as bu                # blocos de lava facetados (cristas, penedos)

PARAMS = dict(detail=2, fps=30, out_dir=None, export=True, save_blend=True, render=False, anim_preview=False,
              samples=20, res=1200, seed=238, plan=None)

SCENE_NAME = "FG_Fogo"
COLL_NAMES = ["Terrain", "Volcano", "Lava", "Cover", "Bushes", "Markers", "Collision", "FX", "Preview"]

# ------------------------------------------------------------------ escala e vulcão
PX = 0.1                 # m por píxel da planta (tabuleiro de 608 × 641 px)
CELL = 0.4               # resolução do relevo (heightfield) e da máscara de lava
NX, NY = 152, 160        # 60,8 × 64 m
HALF_X, HALF_Y = NX * CELL / 2, NY * CELL / 2
PLAN_W, PLAN_H = 608, 641
PLAN_CX, PLAN_CY = 304.0, 320.5

R_BOWL = 13.5            # raio do chão da arena interior
R_RIM_IN = 15.4          # pé da parede interior → crista
R_RIM_OUT = 16.9
R_FOOT = 22.5            # pé exterior do cone
RIM_H = 9.0              # altura média da crista (relativa ao planalto)
BOWL_Y = -1.5            # arena interior abaixo do planalto (entra-se a descer pelas rampas)
GORGES = [45.0, 135.0, 225.0, 315.0]
GORGE_HW = 3.2           # meia largura do desfiladeiro (6,4 m)
GORGE_BLEND = 1.4

LAVA_SURF_OUT, LAVA_FLOOR_OUT = -0.16, -0.38   # margem a 43° (< limite de 46° do CharacterController): sai-se da lava a andar
LAVA_SURF_IN, LAVA_FLOOR_IN = BOWL_Y - 0.16, BOWL_Y - 0.38

WALL_H, LOW_H, FENCE_H, POST_H, BUSH_H = 2.4, 1.1, 0.9, 1.8, 2.0

# classes da planta
BG, LAVA, FENCE, BUSH, WALL, BOX, JUMP, POST, CYL, SPAWN, RED = range(11)

C, MAT, MAT_TILE, OBJ = {}, {}, {}, {}


def bind_helpers():
    for mod in (cv, bu):
        mod.C, mod.MAT, mod.MAT_TILE, mod.OBJ = C, MAT, MAT_TILE, OBJ


def parse_args():
    p = dict(PARAMS)
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    flags = {"--export": "export", "--save-blend": "save_blend", "--render": "render", "--anim-preview": "anim_preview"}
    nums = {"--detail": "detail", "--samples": "samples", "--res": "res", "--seed": "seed", "--fps": "fps"}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in flags:
            p[flags[a]] = True
        elif a in ("--no-export", "--no-blend"):
            p["export" if a == "--no-export" else "save_blend"] = False
        elif a in ("--out", "--plan"):
            i += 1
            p["out_dir" if a == "--out" else "plan"] = argv[i]
        elif a in nums:
            i += 1
            p[nums[a]] = int(float(argv[i]))
        i += 1
    p["detail"] = max(1, min(3, p["detail"]))
    p["out_dir"] = os.path.abspath(p["out_dir"] or os.path.join(HERE, "out"))
    p["plan"] = p["plan"] or os.path.join(HERE, "referencias", "planta_fogo.png")
    return p


# =============================================================================== planta

def classify_plan(path):
    """Classe por píxel do tabuleiro (cores da planta; o círculo vermelho é ignorado)."""
    import numpy as np
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3] * 255.0
    bpy.data.images.remove(img)
    # bordas do tabuleiro na imagem original (637 × 667)
    x0, y0 = 14, 18
    board = px[y0:y0 + PLAN_H, x0:x0 + PLAN_W]
    protos = {BG: [(45, 35, 64), (55, 50, 63), (40, 37, 68), (35, 30, 60)], LAVA: [(5, 143, 106), (20, 160, 110), (10, 125, 90)],
              FENCE: [(5, 104, 47), (10, 80, 40), (20, 120, 60)], BUSH: [(33, 110, 162), (30, 150, 200), (40, 125, 170), (25, 95, 145)],
              WALL: [(64, 78, 136), (100, 100, 180), (90, 95, 160), (120, 120, 200)], BOX: [(227, 141, 70), (240, 150, 60), (200, 110, 50)],
              JUMP: [(240, 181, 15)], POST: [(82, 89, 107), (30, 32, 40), (60, 62, 75)], CYL: [(88, 104, 198), (70, 80, 210)],
              SPAWN: [(150, 150, 155), (109, 108, 117), (200, 200, 200)], RED: [(230, 40, 40), (200, 30, 30), (250, 70, 70)]}
    P, L = [], []
    for k, v in protos.items():
        for c in v:
            P.append(c)
            L.append(k)
    P = np.array(P, np.float32)
    L = np.array(L)
    d = ((board[:, :, None, :] - P[None, None]) ** 2).sum(-1)
    return L[d.argmin(-1)]


def to_px(x, y):
    """Metros (Blender) → píxel do tabuleiro, no quadrante superior esquerdo (simetria)."""
    return PLAN_CX - abs(x) / PX, PLAN_CY - abs(y) / PX


def cell_center(i, j):
    return -HALF_X + (i + 0.5) * CELL, -HALF_Y + (j + 0.5) * CELL


def build_grid(cls):
    """Classe por célula de 0,4 m (maioria dos píxeis), espelhada a partir do quadrante superior esquerdo."""
    import numpy as np
    grid = np.zeros((NX, NY), np.int8)
    keep = (LAVA, FENCE, BUSH, WALL, BOX)
    for i in range(NX):
        for j in range(NY):
            x, y = cell_center(i, j)
            u, v = to_px(x, y)
            u0, v0 = int(round(u - 2)), int(round(v - 2))
            win = cls[max(0, v0):max(0, v0) + 4, max(0, u0):max(0, u0) + 4].ravel()
            if win.size == 0:
                continue
            best, bc = BG, 0
            for k in keep:
                c = int((win == k).sum())
                if c > bc:
                    best, bc = k, c
            if bc >= 0.4 * win.size:
                grid[i, j] = best
    # limpeza da máscara de lava: sem ilhotas secas de uma célula dentro da lava (refúgio a salvo no meio da
    # lava) nem salpicos de lava isolados (dano onde não se vê rio)
    for _ in range(2):
        lv = grid == LAVA
        for i in range(NX):
            for j in range(NY):
                n = sum(1 for a, b in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1))
                        if 0 <= a < NX and 0 <= b < NY and lv[a, b])
                if not lv[i, j] and grid[i, j] == BG and n >= 3:
                    grid[i, j] = LAVA
                elif lv[i, j] and n <= 1:
                    grid[i, j] = BG
    # pontes naturais: a lava da cratera é interrompida no eixo de cada entrada, para que quem desce a rampa
    # chegue ao centro da arena por rocha seca (sem degrau de lava a meio do caminho)
    for i in range(NX):
        for j in range(NY):
            if grid[i, j] != LAVA:
                continue
            x, y = cell_center(i, j)
            if math.hypot(x, y) >= R_BOWL + 0.5:
                continue
            for g in GORGES:
                a = math.radians(g)
                along = x * math.cos(a) + y * math.sin(a)
                lat = abs(-x * math.sin(a) + y * math.cos(a))
                if along > 0 and lat < 2.0:
                    grid[i, j] = BG
    return grid


def components(cls, klass, min_px, max_px):
    """Centróides (m) das manchas de uma classe no quadrante superior esquerdo, espelhados nos quatro quadrantes."""
    import numpy as np
    from scipy import ndimage
    q = cls[:int(PLAN_CY) + 1, :int(PLAN_CX) + 1] == klass
    lab, n = ndimage.label(q)
    out = []
    for k in range(1, n + 1):
        ys, xs = np.where(lab == k)
        if not (min_px <= len(xs) <= max_px):
            continue
        x = (xs.mean() - PLAN_CX) * PX
        y = (PLAN_CY - ys.mean()) * PX
        for (mx, my) in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
            p = (x * mx, y * my)
            if all(math.hypot(p[0] - o[0], p[1] - o[1]) > 1.0 for o in out):
                out.append(p)
    return out


def rects(mask, max_w=None, max_h=None):
    """Retângulos gulosos (i0, j0, i1, j1 exclusivo) que cobrem as células verdadeiras."""
    import numpy as np
    m = mask.copy()
    out = []
    W, H = m.shape
    for j in range(H):
        i = 0
        while i < W:
            if not m[i, j]:
                i += 1
                continue
            i1 = i
            while i1 < W and m[i1, j] and (max_w is None or i1 - i < max_w):
                i1 += 1
            j1 = j + 1
            while j1 < H and (max_h is None or j1 - j < max_h) and m[i:i1, j1].all():
                j1 += 1
            m[i:i1, j:j1] = False
            out.append((i, j, i1, j1))
            i = i1
    return out


def rect_m(r):
    i0, j0, i1, j1 = r
    x0, y0 = -HALF_X + i0 * CELL, -HALF_Y + j0 * CELL
    x1, y1 = -HALF_X + i1 * CELL, -HALF_Y + j1 * CELL
    return ((x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0)


# =============================================================================== vulcão

def per_noise(seed, k=5):
    return bu.per_noise(seed, k)


class Volcano:
    def __init__(self, seed):
        self.nr = per_noise(seed + 1, 6)
        self.nr2 = per_noise(seed + 2, 5)
        self.ns = per_noise(seed + 3, 7)
        rng = random.Random(seed)
        self.ph = [rng.uniform(0, 100) for _ in range(6)]

    def rim(self, th):
        return RIM_H + 1.6 * self.nr(th) + 1.6 * abs(self.nr2(5 * th + 0.7)) - 0.6   # picos recortados

    def gorge(self, x, y):
        """(altura do fundo, peso 0..1 do desfiladeiro) para o desfiladeiro mais próximo."""
        best = (None, 0.0)
        for ang in GORGES:
            a = math.radians(ang)
            u = (math.cos(a), math.sin(a))
            along = x * u[0] + y * u[1]
            d = abs(-u[1] * x + u[0] * y)
            if along < R_BOWL - 2.0 or along > R_FOOT + 1.5:
                continue
            t = min(1.0, max(0.0, (along - R_BOWL) / (R_FOOT - R_BOWL)))
            floor = BOWL_Y * (1 - t)
            w = 1.0 - smooth(GORGE_HW, GORGE_HW + GORGE_BLEND, d)
            if w > best[1]:
                best = (floor, w)
        return best

    def height(self, x, y):
        r = math.hypot(x, y)
        if r >= R_FOOT + 0.4:
            return None
        th = math.atan2(y, x)
        rim = self.rim(th)
        if r <= R_BOWL:
            h = BOWL_Y
        elif r <= R_RIM_IN:
            t = (r - R_BOWL) / (R_RIM_IN - R_BOWL)
            h = BOWL_Y + (rim - BOWL_Y) * smooth(0.0, 1.0, t) ** 0.75
        elif r <= R_RIM_OUT:
            h = rim + 0.35 * math.sin(th * 23 + self.ph[0])
        else:
            t = min(1.0, (r - R_RIM_OUT) / (R_FOOT - R_RIM_OUT))
            h = rim * (1 - t) ** 2.0                       # encosta côncava: base larga, cume íngreme
        if R_BOWL + 0.4 < r < R_FOOT - 0.2:   # rugosidade das encostas (ravinas)
            h += 0.55 * self.ns(th * 2 + r * 0.35) * min(1.0, h / 3.0) + 0.25 * math.sin(th * 41 + r * 1.3 + self.ph[1])
        floor, w = self.gorge(x, y)
        if floor is not None and w > 0:
            h = floor * w + h * (1 - w) if h > floor else h
        if r > R_FOOT - 0.3:
            h = min(h, 0.0)
        return h


def smooth(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# =============================================================================== cena

def setup_scene(P):
    old = bpy.data.scenes.get(SCENE_NAME)
    if old is not None:
        for ob in list(old.objects):
            if len(ob.users_scene) <= 1:
                bpy.data.objects.remove(ob, do_unlink=True)
        if len(bpy.data.scenes) > 1:
            bpy.data.scenes.remove(old)
        else:
            old.name = SCENE_NAME + "_old"
    sc = bpy.data.scenes.new(SCENE_NAME)
    if bpy.context.window is not None:
        bpy.context.window.scene = sc
    sc.unit_settings.system = 'METRIC'
    sc.render.fps = P["fps"]
    root = bpy.data.collections.new("FG_Fogo")
    sc.collection.children.link(root)
    for n in COLL_NAMES:
        c = bpy.data.collections.new("FG_" + n if n in ("Terrain", "Lava", "Cover") else n)
        root.children.link(c)
        C[n] = c
    C["Envolvente"] = bpy.data.collections.new("Envolvente")
    C["Terrain"].children.link(C["Envolvente"])
    C["Gameplay"] = C["Markers"]
    C["Modules"] = C["Preview"]
    return sc


# =============================================================================== texturas e materiais

def make_textures(P, folder):
    import numpy as np
    os.makedirs(folder, exist_ok=True)
    rng = np.random.default_rng(P["seed"])
    R = cv.TEX_RES
    tex = {}

    def save(name, rgb, alpha=None, res=R):
        rgb = np.clip(rgb, 0, 1)
        a = np.ones((res, res, 1), np.float32) if alpha is None else np.clip(alpha, 0, 1)[..., None]
        img = bpy.data.images.get(name)
        if img is not None:
            bpy.data.images.remove(img)
        img = bpy.data.images.new(name, res, res, alpha=alpha is not None)
        img.pixels.foreach_set(np.concatenate([rgb, a], 2)[::-1].reshape(-1).astype(np.float32))
        path = os.path.join(folder, name + ".png")
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        img.filepath = path
        tex[name] = img

    y, x = np.mgrid[0:R, 0:R].astype(np.float32) / R

    # Cinza vulcânica: grão fino, pedrinhas de escória
    n = cv._noise(R, 6, rng, 5)
    g = 0.42 + 0.1 * n + 0.05 * cv._noise(R, 40, rng, 2)
    f1, f2, cid = cv._voronoi(R, 60, rng)
    peb = (f1 < 0.18) * (((cid * 2654435761) % 7) < 2)
    g = g * (1 - 0.35 * peb)
    save("T_Cinza", np.stack([g * 1.02, g * 0.97, g * 0.94], 2))

    # Basalto (reutiliza a lógica da Buracona: blocos, fissuras, vesículas)
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

    # Encosta do vulcão: estratos avermelhados e castanhos (escoadas antigas)
    n = cv._noise(R, 4, rng, 5)
    f1, f2, cid = cv._voronoi(R, 14, rng)
    clink = np.clip((f2 - f1) * 6.0, 0, 1)
    red = np.clip((cv._noise(R, 3, rng, 4) - 0.4) * 2.5, 0, 1)
    v = (0.22 + 0.16 * n + 0.08 * cv._noise(R, 20, rng, 3)) * (0.7 + 0.3 * clink)
    save("T_Vulcao", np.stack([v * (1.05 + 0.5 * red), v * (0.9 + 0.08 * red), v * (0.86 - 0.1 * red)], 2))

    # Escória junto da lava: preta, porosa, com pontos avermelhados
    n = cv._noise(R, 10, rng, 4)
    v = 0.16 + 0.12 * n
    hot = (cv._noise(R, 30, rng, 2) > 0.72).astype(np.float32)
    save("T_Escoria", np.stack([v + 0.18 * hot, v * 0.9 + 0.04 * hot, v * 0.85], 2))

    # Lava — crosta (RGB = rocha arrefecida, A = cobertura da crosta: 1 placa, 0 fissura)
    f1, f2, cid = cv._voronoi(R, 11, rng)
    edge = np.clip((f2 - f1) * 6.0 - 0.1, 0, 1)                    # 0 nas fissuras vermelhas entre placas
    n = cv._noise(R, 3, rng, 5)
    island = np.clip((n - 0.41) * 3.2, 0, 1)                       # ilhas de crosta negra a boiar no fluxo
    plates = np.clip(edge * island * 1.3, 0, 1)
    rough = 0.12 + 0.12 * cv._noise(R, 28, rng, 3) + 0.05 * ((cid * 7919) % 11) / 11.0
    save("T_Lava_Crosta", np.stack([rough * 1.05, rough * 0.95, rough * 0.92], 2), alpha=plates)

    # Lava — calor (fluxo em faixas alongadas na direção X, como na fotografia)
    base = cv._noise(R, 10, rng, 5)
    k = 48                                                          # esticar ao longo de X (fluxo viscoso)
    ext = np.concatenate([base[:, -k:], base, base[:, :k]], 1)
    cs = np.cumsum(ext, 1)
    streak = (cs[:, 2 * k:] - cs[:, :-2 * k]) / (2 * k)
    streak = (streak - streak.min()) / (streak.max() - streak.min() + 1e-6)
    heat = np.clip(0.75 * streak + 0.25 * cv._noise(R, 24, rng, 3), 0, 1) ** 1.1
    save("T_Lava_Calor", np.stack([heat, heat, heat], 2))
    return tex


def make_materials(tex):
    T = tex.get
    m = cv.material
    m("MAT_Cinza", (0.62, 0.6, 0.58), T("T_Cinza"), tile=4.0, rough=1.0)
    m("MAT_Basalto", (0.4, 0.35, 0.33), T("T_Basalto"), tile=2.2, rough=0.9)
    m("MAT_Basalto_Escuro", (0.24, 0.22, 0.22), T("T_Basalto"), tile=2.2, rough=0.9)
    m("MAT_Vulcao", (0.62, 0.5, 0.44), T("T_Vulcao"), tile=7.0, rough=0.95)
    m("MAT_Vulcao_Cinza", (0.5, 0.47, 0.45), T("T_Cinza"), tile=4.0, rough=1.0)
    m("MAT_Vulcao_Escuro", (0.42, 0.36, 0.34), T("T_Vulcao"), tile=4.0, rough=0.95)
    m("MAT_Cinza_Escura", (0.42, 0.4, 0.39), T("T_Cinza"), tile=4.0, rough=1.0)
    m("MAT_Escoria", (0.75, 0.7, 0.7), T("T_Escoria"), tile=3.0, rough=0.9)
    m("MAT_Ramo", (0.22, 0.16, 0.12), None, rough=1.0)
    m("MAT_Spawn", (0.85, 0.55, 0.25), None, emission=(1.0, 0.5, 0.15), strength=0.3)
    m("MAT_Colisor", (1.0, 0.2, 0.6), None, alpha=0.25)
    m("MAT_Brasa", (1.0, 0.45, 0.1), None, emission=(1.0, 0.38, 0.06), strength=2.5)
    m("MAT_Fumo", (0.3, 0.29, 0.28), None, rough=1.0, alpha=0.55)
    m("MAT_Monte", (0.3, 0.26, 0.25), None, rough=1.0)
    m("MAT_Monte_Cinza", (0.45, 0.42, 0.4), None, rough=1.0)
    # Arbusto vulcânico: cor por vértice
    mm = m("MAT_Arbusto_Vulcanico", (1.0, 1.0, 1.0), None, rough=0.95)
    ca = mm.node_tree.nodes.new("ShaderNodeVertexColor")
    ca.layer_name = "Col"
    mm.node_tree.links.new(ca.outputs[0], mm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    lava_material(T("T_Lava_Crosta"), T("T_Lava_Calor"))


def lava_material(crust, heat):
    """Pré-visualização no Blender: as mesmas texturas que o shader FPSLAN/Lava usa no Unity, com o fluxo animado."""
    mat = bpy.data.materials.get("MAT_Lava") or bpy.data.materials.new("MAT_Lava")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    comb = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(sep.outputs[0], comb.inputs[0])
    nt.links.new(sep.outputs[1], comb.inputs[1])

    def mapping(name, scale):
        mp = nt.nodes.new("ShaderNodeMapping")
        mp.name = name
        mp.inputs["Scale"].default_value = (scale, scale, 1)
        nt.links.new(comb.outputs[0], mp.inputs[0])
        return mp

    mp_c = mapping("MapCrosta", 0.22)
    mp_h = mapping("MapCalor", 0.13)
    tcrust = nt.nodes.new("ShaderNodeTexImage")
    tcrust.image = crust
    theat = nt.nodes.new("ShaderNodeTexImage")
    theat.image = heat
    nt.links.new(mp_c.outputs[0], tcrust.inputs[0])
    nt.links.new(mp_h.outputs[0], theat.inputs[0])
    # crosta efetiva = alfa da crosta + (0,5 − calor) → os canais quentes abrem a crosta
    sub = nt.nodes.new("ShaderNodeMath")
    sub.operation = 'SUBTRACT'
    sub.inputs[0].default_value = 0.5
    nt.links.new(theat.outputs[0], sub.inputs[1])
    add = nt.nodes.new("ShaderNodeMath")
    add.operation = 'MULTIPLY_ADD'
    nt.links.new(sub.outputs[0], add.inputs[0])
    add.inputs[1].default_value = 0.7
    nt.links.new(tcrust.outputs["Alpha"], add.inputs[2])
    ramp_mask = nt.nodes.new("ShaderNodeValToRGB")
    ramp_mask.color_ramp.elements[0].position = 0.4
    ramp_mask.color_ramp.elements[1].position = 0.62
    nt.links.new(add.outputs[0], ramp_mask.inputs[0])
    # cor quente: vermelho → laranja → amarelo
    ramp_hot = nt.nodes.new("ShaderNodeValToRGB")
    cr = ramp_hot.color_ramp
    cr.elements[0].color = (0.55, 0.04, 0.01, 1)
    cr.elements[1].position = 0.7
    cr.elements[1].color = (1.0, 0.42, 0.04, 1)
    e = cr.elements.new(0.95)
    e.color = (1.0, 0.85, 0.25, 1)
    nt.links.new(theat.outputs[0], ramp_hot.inputs[0])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(ramp_mask.outputs[0], inv.inputs[1])
    emis = nt.nodes.new("ShaderNodeMath")
    emis.operation = 'MULTIPLY'
    emis.name = "ForcaEmissao"
    emis.inputs[1].default_value = 1.8
    nt.links.new(inv.outputs[0], emis.inputs[0])
    nt.links.new(ramp_hot.outputs[0], bsdf.inputs["Emission Color"])
    nt.links.new(emis.outputs[0], bsdf.inputs["Emission Strength"])
    mixc = nt.nodes.new("ShaderNodeMix")
    mixc.data_type = 'RGBA'
    nt.links.new(ramp_mask.outputs[0], mixc.inputs[0])
    mixc.inputs[6].default_value = (0.35, 0.05, 0.02, 1)
    nt.links.new(tcrust.outputs["Color"], mixc.inputs[7])
    nt.links.new(mixc.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.7
    MAT["MAT_Lava"] = mat
    MAT_TILE["MAT_Lava"] = 1.0
    return mat


# =============================================================================== relevo

def build_terrain(P, grid, volc, data):
    """Heightfield único (planalto + vulcão + leitos de lava). É também o colisor do chão (COL_Tri_Chao)."""
    import numpy as np
    det = P["detail"]
    lava = grid == LAVA
    rng = random.Random(P["seed"] + 7)
    H = np.zeros((NX + 1, NY + 1), np.float32)
    inner = np.zeros((NX + 1, NY + 1), bool)
    for i in range(NX + 1):
        for j in range(NY + 1):
            x, y = -HALF_X + i * CELL, -HALF_Y + j * CELL
            vh = volc.height(x, y)
            h = vh if vh is not None else 0.03 * math.sin(x * 1.7 + y * 0.9) * math.cos(y * 1.3)
            adj = [lava[a, b] for a in (i - 1, i) for b in (j - 1, j) if 0 <= a < NX and 0 <= b < NY]
            r = math.hypot(x, y)
            if adj and all(adj):
                h = LAVA_FLOOR_IN if r < R_BOWL else LAVA_FLOOR_OUT
            H[i, j] = h
            inner[i, j] = r < R_FOOT
    data["terrain"] = dict(nx=NX, ny=NY, cell=CELL, x0=-HALF_X, y0=-HALF_Y)

    names = ["MAT_Cinza", "MAT_Vulcao", "MAT_Vulcao_Cinza", "MAT_Escoria", "MAT_Basalto_Escuro", "MAT_Vulcao_Escuro",
             "MAT_Cinza_Escura"]
    patch = per_noise(P["seed"] + 17, 4)

    def terrain_bm():
        bm = bmesh.new()
        vs = [[bm.verts.new((-HALF_X + i * CELL, -HALF_Y + j * CELL, float(H[i, j]))) for j in range(NY + 1)] for i in range(NX + 1)]
        for i in range(NX):
            for j in range(NY):
                f = bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
                x, y = cell_center(i, j)
                r = math.hypot(x, y)
                near_lava = lava[max(0, i - 1):i + 2, max(0, j - 1):j + 2].any()
                hs = (H[i, j], H[i + 1, j], H[i, j + 1], H[i + 1, j + 1])
                steep = max(hs) - min(hs) > 0.45
                if near_lava:
                    f.material_index = 3
                elif r < R_FOOT:
                    if r < R_BOWL and not steep:
                        f.material_index = 2                       # chão da arena (cinza)
                    elif r < R_RIM_IN + 0.6 or max(hs) - min(hs) > 1.2:
                        f.material_index = 5                       # parede interior / ravinas: rocha escura
                    else:
                        f.material_index = 1                       # encosta exterior
                else:
                    dark = patch(x * 0.21) + patch(y * 0.17 + 2.0) > 0.45  # manchas de escoada antiga
                    f.material_index = 4 if steep else (6 if dark else 0)
        # simplificação: o planalto e os fundos planos têm milhares de quadrados quase coplanares; junta-os
        # (sem atravessar mudanças de material) e volta a triangular. O colisor usa exatamente a mesma malha.
        bm.normal_update()
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(2.5), verts=list(bm.verts), edges=list(bm.edges),
                                 delimit={'MATERIAL'})
        bmesh.ops.triangulate(bm, faces=list(bm.faces), quad_method='BEAUTY', ngon_method='BEAUTY')
        return bm

    me = cv.mesh_from_bm(terrain_bm(), "TER_Relevo", [MAT[n] for n in names])
    for poly in me.polygons:
        poly.use_smooth = True
    cv.place(me, "TER_Relevo", C["Terrain"])
    bm = terrain_bm()
    me_c = cv.mesh_from_bm(bm, "COL_Tri_Chao", [MAT["MAT_Colisor"]])
    ob = cv.place(me_c, "COL_Tri_Chao", C["Collision"])
    ob.display_type = 'WIRE'
    ob.hide_render = True
    OBJ["H"] = H
    return H


def ground_height(H, x, y):
    """Altura do relevo (interpolação bilinear)."""
    fi = (x + HALF_X) / CELL
    fj = (y + HALF_Y) / CELL
    i = int(min(NX - 1, max(0, math.floor(fi))))
    j = int(min(NY - 1, max(0, math.floor(fj))))
    tx, ty = fi - i, fj - j
    return float(H[i, j] * (1 - tx) * (1 - ty) + H[i + 1, j] * tx * (1 - ty) + H[i, j + 1] * (1 - tx) * ty + H[i + 1, j + 1] * tx * ty)


# =============================================================================== lava

def build_lava(P, grid, data):
    """Superfícies de lava (uma malha exterior, uma da cratera) e a máscara usada no Unity para o dano."""
    lava = grid == LAVA
    cells_out, cells_in = [], []
    for i in range(NX):
        for j in range(NY):
            if lava[i, j]:
                x, y = cell_center(i, j)
                (cells_in if math.hypot(x, y) < R_BOWL else cells_out).append((i, j))

    def lava_mesh(name, cells, z):
        bm = bmesh.new()
        verts = {}

        def V(i, j):
            if (i, j) not in verts:
                verts[(i, j)] = bm.verts.new((-HALF_X + i * CELL, -HALF_Y + j * CELL, z))
            return verts[(i, j)]
        for (i, j) in cells:
            bm.faces.new((V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1)))
        me = cv.mesh_from_bm(bm, name, [MAT["MAT_Lava"]])
        return cv.place(me, name, C["Lava"])

    OBJ["lava_out"] = lava_mesh("LAV_Rios", cells_out, LAVA_SURF_OUT)
    OBJ["lava_in"] = lava_mesh("LAV_Cratera", cells_in, LAVA_SURF_IN)
    # máscara: uma string de '0'/'1'/'2' por linha (0 seco, 1 lava exterior, 2 lava da cratera)
    set_in = set(cells_in)
    rows = ["".join(("2" if (i, j) in set_in else "1") if lava[i, j] else "0" for i in range(NX)) for j in range(NY)]
    data["lava"] = dict(nx=NX, ny=NY, cell=CELL, x0=-HALF_X, y0=-HALF_Y, surface_out=LAVA_SURF_OUT,
                        surface_in=LAVA_SURF_IN, rows=rows, cells_out=len(cells_out), cells_in=len(cells_in))
    return cells_out, cells_in


def lava_blobs(cells):
    """Manchas ligadas de lava → (centro, nº de células, células)."""
    from scipy import ndimage
    import numpy as np
    m = np.zeros((NX, NY), bool)
    for (i, j) in cells:
        m[i, j] = True
    lab, n = ndimage.label(m)
    out = []
    for k in range(1, n + 1):
        ii, jj = np.where(lab == k)
        cs = list(zip(ii.tolist(), jj.tolist()))
        # célula da mancha mais perto do centróide (fica sempre dentro da lava)
        cx, cy = ii.mean(), jj.mean()
        best = min(cs, key=lambda c: (c[0] - cx) ** 2 + (c[1] - cy) ** 2)
        out.append((cell_center(*best), len(cs), cs))
    out.sort(key=lambda b: -b[1])
    return out


def lava_edge_rocks(P, grid, H):
    """Crosta de escória partida ao longo das margens (decoração sem colisão, quebra as arestas retas)."""
    lava = grid == LAVA
    rng = random.Random(P["seed"] + 31)
    bm = bmesh.new()
    count = 0
    step = (3, 2, 1)[P["detail"] - 1]
    for i in range(0, NX, 1):
        for j in range(0, NY, 1):
            if not lava[i, j]:
                continue
            border = any(0 <= i + di < NX and 0 <= j + dj < NY and not lava[i + di, j + dj]
                         for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if not border or (i + j) % step:
                continue
            x, y = cell_center(i, j)
            z = ground_height(H, x, y)
            s = rng.uniform(0.14, 0.3)
            cv.add_icosphere(bm, s, (x + rng.uniform(-0.15, 0.15), y + rng.uniform(-0.15, 0.15),
                                     max(z, LAVA_SURF_IN if math.hypot(x, y) < R_BOWL else LAVA_SURF_OUT) - 0.02),
                             mi=0, subdiv=1, squash=rng.uniform(0.35, 0.6))
            count += 1
    cv.place(cv.mesh_from_bm(bm, "LAV_Margem_Escoria", [MAT["MAT_Escoria"]]), "LAV_Margem_Escoria", C["Lava"])
    return count


# =============================================================================== coberturas

def build_cover(P, grid, H, data):
    det = P["detail"]
    seed = P["seed"]
    k = 0

    def place_rects(mask, kind):
        nonlocal k
        for r in rects(mask, max_w=12, max_h=12):
            cx, cy, sx, sy = rect_m(r)
            z = ground_height(H, cx, cy)
            name = f"{kind}_{k:03d}"
            if kind == "Muro":
                ob = bu.lava_ridge(name, cx, cy, sx, sy, WALL_H, seed + k, det)
            elif kind == "Vedacao":
                ob = bu.lava_ridge(name, cx, cy, sx, sy, FENCE_H, seed + k, det)
            else:
                ob = bu.boulder(name, cx, cy, sx, sy, LOW_H, seed + k)
            ob.location.z = z
            col = bpy.data.objects.get("COL_Box_" + name)
            if col is not None:
                col.location.z = z
            data["boxes"].append(dict(name="COL_Box_" + name, loc=[cx, cy, z], size=[sx, sy,
                                       WALL_H if kind == "Muro" else FENCE_H if kind == "Vedacao" else LOW_H]))
            k += 1

    allowed = keep_mask(grid)
    place_rects((grid == WALL) & allowed, "Muro")
    place_rects((grid == FENCE) & allowed, "Vedacao")
    place_rects((grid == BOX) & allowed, "Pedra")


def keep_mask(grid):
    """Elementos da planta: fora do vulcão e dentro da arena interior (a faixa do cone fica livre)."""
    import numpy as np
    m = np.zeros(grid.shape, bool)
    for i in range(NX):
        for j in range(NY):
            x, y = cell_center(i, j)
            r = math.hypot(x, y)
            m[i, j] = r > R_FOOT + 0.8 or r < R_BOWL - 1.1
            # corredor de cada entrada livre (de dentro da arena até 4 m fora do pé do cone): nada da planta
            # tapa a aproximação nem a saída das rampas, por isso as 4 entradas são equivalentes
            if 7.5 < r < R_FOOT + 4.5:
                for g in GORGES:
                    a = math.radians(g)
                    along = x * math.cos(a) + y * math.sin(a)
                    lat = abs(-x * math.sin(a) + y * math.cos(a))
                    if along > 0 and lat < GORGE_HW + 0.7:
                        m[i, j] = False
    return m


def interior_extras(P, H, data):
    """Retoques à arena interior: filas de muros com arbustos a norte e sul, moitas a leste e oeste."""
    seed = P["seed"] + 400
    z = BOWL_Y
    k = 0
    for sy in (1, -1):
        for sx in (1, -1):
            name = f"Muro_Interior_{'N' if sy > 0 else 'S'}{'E' if sx > 0 else 'W'}"
            ob = bu.lava_ridge(name, sx * 4.0, sy * 11.0, 1.6, 0.8, WALL_H, seed + k, P["detail"])
            ob.location.z = z
            bpy.data.objects["COL_Box_" + name].location.z = z
            k += 1
    for sy in (1, -1):
        scrub(f"Interior_{'N' if sy > 0 else 'S'}", 0.0, sy * 11.0, 6.0, 1.7, seed + 50 + sy, data, z)
    for sx in (1, -1):
        scrub(f"Interior_{'E' if sx > 0 else 'W'}", sx * 10.8, 0.0, 1.8, 3.6, seed + 60 + sx, data, z)


# =============================================================================== vulcão: decoração e entradas

def volcano_details(P, volc, H, data):
    det = P["detail"]
    rng = random.Random(P["seed"] + 101)
    # Fragas na crista e nas encostas (silhueta recortada; sem colisão — o relevo já é o colisor)
    bm = bmesh.new()
    n_crags = (60, 110, 170)[det - 1]
    placed = 0
    tries = 0
    while placed < n_crags and tries < n_crags * 20:
        tries += 1
        th = rng.uniform(0, math.tau)
        r = rng.uniform(R_RIM_IN - 0.3, R_FOOT - 1.5)
        x, y = r * math.cos(th), r * math.sin(th)
        floor, w = volc.gorge(x, y)
        if floor is not None and w > 0.05:
            continue
        z = ground_height(H, x, y)
        near_rim = r < R_RIM_OUT + 0.8
        s = rng.uniform(0.6, 1.4) if near_rim else rng.uniform(0.4, 1.0)
        b = bu.lava_block(rng, s * 1.6, s * 1.3, s * (1.8 if near_rim else 1.1), 2)
        bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rng.uniform(0, math.tau), 3, 'Z'))
        bmesh.ops.translate(b, verts=b.verts, vec=(x, y, z - 0.35 * s))
        for f in b.faces:
            f.material_index = 0 if rng.random() < 0.6 else 1
        bu.bm_join(bm, b)
        placed += 1
    cv.place(cv.mesh_from_bm(bm, "VOL_Fragas", [MAT["MAT_Basalto_Escuro"], MAT["MAT_Vulcao"]]), "VOL_Fragas", C["Volcano"])

    # Entradas: penedo central (divide o desfiladeiro em duas faixas) e arco de rocha na boca exterior
    data["entrances"] = []
    for gi, ang in enumerate(GORGES):
        a = math.radians(ang)
        u = Vector((math.cos(a), math.sin(a), 0))
        t = Vector((-u.y, u.x, 0))
        c = u * 17.6
        name = f"Entrada_{gi}_Penedo"
        ob = bu.boulder(name, c.x, c.y, 1.5, 1.5, 2.2, P["seed"] + 700 + gi)
        z = ground_height(H, c.x, c.y)
        ob.location.z = z
        bpy.data.objects["COL_Box_" + name].location.z = z
        data["boxes"].append(dict(name="COL_Box_" + name, loc=[c.x, c.y, z], size=[1.35, 1.35, 2.2]))
        # arco: blocos em semicírculo entre as paredes do desfiladeiro, a 4,8–7 m de altura
        bm = bmesh.new()
        mouth = u * 20.8
        span = GORGE_HW + 1.0
        for s in range(9):
            tt = -1 + 2 * s / 8
            p = mouth + t * (tt * span)
            zz = 4.9 + 1.8 * math.sqrt(max(0.0, 1 - tt * tt)) + ground_height(H, mouth.x, mouth.y)
            b = bu.lava_block(rng, 1.3, 1.6, 1.5, 2)
            bmesh.ops.rotate(b, verts=b.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(a + rng.uniform(-0.2, 0.2), 3, 'Z'))
            bmesh.ops.translate(b, verts=b.verts, vec=(p.x, p.y, zz - 0.6))
            bu.bm_join(bm, b)
        cv.place(cv.mesh_from_bm(bm, f"VOL_Arco_{gi}", [MAT["MAT_Basalto_Escuro"]]), f"VOL_Arco_{gi}", C["Volcano"])
        # colisor do arco (só acima de 4,6 m: não trava passagem nem tiros à altura do jogador)
        cb = bmesh.new()
        cv.add_box(cb, 2.0, span * 2 + 1.0, 2.4, center=(0, 0, 0), mi=0)
        me = cv.mesh_from_bm(cb, f"COL_Box_Arco_{gi}", [MAT["MAT_Colisor"]])
        oc = cv.place(me, f"COL_Box_Arco_{gi}", C["Collision"], (mouth.x, mouth.y, 4.6 + ground_height(H, mouth.x, mouth.y)), a)
        oc.display_type = 'WIRE'
        oc.hide_render = True
        data["entrances"].append(dict(angle=ang, outer=[*(u * 24.5)[:2]], mouth=[*(u * 21.0)[:2]], middle=[*(u * 17.6)[:2]],
                                      inner=[*(u * 11.5)[:2]], lanes=[[*(u * 17.6 + t * 2.1)[:2]], [*(u * 17.6 - t * 2.1)[:2]]]))
        # marcador para o Unity (luz e brasas à entrada, para se reconhecer de longe)
        cv.marker(f"FX_Entrada_{gi}", (mouth.x, mouth.y, ground_height(H, mouth.x, mouth.y) + 0.2), C["Markers"], 0.5, 'CONE')

    # Fumarolas na crista (fumo moderado no Unity) e luz da cratera
    for k in range(8):
        th = math.radians(22.5 + 45 * k) + rng.uniform(-0.15, 0.15)
        r = (R_RIM_IN + R_RIM_OUT) / 2
        x, y = r * math.cos(th), r * math.sin(th)
        cv.marker(f"FX_Fumo_{k:02d}", (x, y, ground_height(H, x, y) + 0.3), C["Markers"], 0.6, 'SPHERE')
    cv.marker("FX_Cratera_Luz", (0, 0, BOWL_Y + 4.5), C["Markers"], 1.0, 'SPHERE')


# =============================================================================== arbustos vulcânicos

# cores sRGB (convertidas para linear ao escrever): verde-oliva seco, castanho, cinzento-esverdeado
PAL_BUSH = [(0.36, 0.38, 0.18), (0.45, 0.42, 0.22), (0.4, 0.3, 0.18), (0.4, 0.42, 0.34), (0.3, 0.33, 0.17)]
ASH = (0.66, 0.64, 0.6)


def lin(c):
    return tuple((ch ** 2.2) for ch in c)


def scrub(name, cx, cy, w, d, seed, data, z=None):
    """Arbusto seco de esconder: zona BUSH_ (sistema Bush do jogo) + folhagem leve BSHM_ (cor por vértice).
    Muitos tufos pequenos e espinhosos (icosfera de 80 triângulos com vértices puxados para fora), cinza
    acumulada nas pontas de cima e ramos torcidos a sair da folhagem. ~250 triângulos por m²."""
    rng = random.Random(seed)
    H = OBJ["H"]
    if z is None:
        z = ground_height(H, cx, cy)
    zname = f"BUSH_{name}_S{seed}"
    bm = bmesh.new()
    cv.add_box(bm, w, d, BUSH_H, mi=0)
    zone = cv.place(cv.mesh_from_bm(bm, zname, [MAT["MAT_Colisor"]]), zname, C["Bushes"], (cx, cy, z))
    zone.display_type = 'WIRE'
    zone.hide_render = True

    bm = bmesh.new()
    col = bm.verts.layers.float_color.new("Col")
    base_pal = PAL_BUSH[rng.randrange(len(PAL_BUSH))]
    area = w * d
    n = max(5, int(round(area * 3.0)))
    for k in range(n):
        layer = k % 3                                       # 0 = baixo (tapa pernas), 1 = meio, 2 = copa
        rr = rng.uniform(0.42, 0.62)
        px = rng.uniform(-w / 2 + rr * 0.5, w / 2 - rr * 0.5) if w > rr else 0.0
        py = rng.uniform(-d / 2 + rr * 0.5, d / 2 - rr * 0.5) if d > rr else 0.0
        pz = (0.4, 1.0, 1.62)[layer] + rng.uniform(-0.12, 0.15)
        ret = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rr)
        pal = PAL_BUSH[rng.randrange(len(PAL_BUSH))] if rng.random() < 0.35 else base_pal
        for v in ret["verts"]:
            spike = rng.uniform(0.8, 1.35)                      # pontas irregulares (folhagem rija)
            v.co.x = v.co.x * spike + px
            v.co.y = v.co.y * spike + py
            v.co.z = v.co.z * spike * 0.85 + pz
            rel = (v.co.z - pz) / rr
            shade = (0.62, 0.8, 1.0)[layer] * (0.85 + 0.25 * rel) * rng.uniform(0.85, 1.12)
            c = [min(1.0, ch * shade) for ch in pal]
            if layer == 2 and rel > 0.35 and rng.random() < 0.65:     # cinza acumulada
                c = [c[i] * 0.45 + ASH[i] * 0.55 for i in range(3)]
            v[col] = (*lin(c), 1.0)
    # ramos torcidos a espreitar da folhagem
    nb = max(2, int(area * 0.6))
    for k in range(nb):
        px = rng.uniform(-w / 2 + 0.2, w / 2 - 0.2)
        py = rng.uniform(-d / 2 + 0.2, d / 2 - 0.2)
        hgt = rng.uniform(1.9, 2.45)
        cxp, cyp = px, py
        ring_prev = None
        for s_ in range(4):
            zz = hgt * s_ / 3
            r_ = 0.05 * (1 - s_ / 3.5)
            if s_:
                cxp += rng.uniform(-0.22, 0.22)
                cyp += rng.uniform(-0.22, 0.22)
            ring = [bm.verts.new((cxp + r_ * math.cos(a), cyp + r_ * math.sin(a), zz)) for a in (0, math.pi / 2, math.pi, 1.5 * math.pi)]
            for v in ring:
                v[col] = (*lin((0.2, 0.15, 0.11)), 1.0)
            if ring_prev is not None:
                for q in range(4):
                    bm.faces.new((ring_prev[q], ring_prev[(q + 1) % 4], ring[(q + 1) % 4], ring[q]))
            ring_prev = ring
    me = bpy.data.meshes.new("BSHM_" + name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(MAT["MAT_Arbusto_Vulcanico"])
    attr = me.color_attributes.get("Col")
    if attr is not None:
        me.color_attributes.active_color = attr
    cv.place(me, "BSHM_" + name, C["Bushes"], (cx, cy, z))
    data["bushes"].append(dict(name=zname, center=[cx, cy], size=[w, d, BUSH_H], tris=sum(len(p.vertices) - 2 for p in me.polygons)))


def build_bushes(P, grid, data):
    import numpy as np
    mask = (grid == BUSH) & keep_mask(grid)
    k = 0
    for r in rects(mask, max_w=10, max_h=10):
        cx, cy, sx, sy = rect_m(r)
        if sx * sy < 1.0:
            continue
        scrub(f"{k:03d}", cx, cy, sx, sy, P["seed"] + 1000 + k, data)
        k += 1


# =============================================================================== marcadores de jogo

def build_markers(P, grid, cls, H, data):
    coll = C["Markers"]
    allowed = keep_mask(grid)
    # Spawns (círculos da planta) — fora do vulcão, em chão seco
    spawns = components(cls, SPAWN, 25, 900)
    k = 0
    for (x, y) in spawns:
        if math.hypot(x, y) < R_FOOT + 1.5:
            continue
        i, j = int((x + HALF_X) / CELL), int((y + HALF_Y) / CELL)
        if grid[min(NX - 1, i), min(NY - 1, j)] in (LAVA, WALL, BOX, FENCE):
            continue
        z = ground_height(H, x, y)
        m = cv.marker(f"SPAWN_{k:02d}", (x, y, z + 0.05), coll, 0.8)
        m.rotation_euler = (0, 0, math.atan2(-x, y))
        data["spawns"].append(dict(pos=[x, y, z]))
        k += 1
    # Postes da planta → pilares de basalto
    posts = components(cls, POST, 25, 400)
    q = 0
    for (x, y) in posts:
        r = math.hypot(x, y)
        i, j = int((x + HALF_X) / CELL), int((y + HALF_Y) / CELL)
        if not (0 <= i < NX and 0 <= j < NY) or not allowed[i, j] or grid[i, j] in (LAVA, WALL, BOX):
            continue
        # o miolo escuro dos círculos de spawn da planta também é lido como poste: nunca pôr pilares em cima de um spawn
        if any(math.hypot(x - s["pos"][0], y - s["pos"][1]) < 2.2 for s in data["spawns"]):
            continue
        z = ground_height(H, x, y)
        name = f"Pilar_{q:03d}"
        ob = bu.boulder(name, x, y, 0.8, 0.8, POST_H, P["seed"] + 1500 + q)
        ob.location.z = z
        bpy.data.objects["COL_Box_" + name].location.z = z
        data["boxes"].append(dict(name="COL_Box_" + name, loc=[x, y, z], size=[0.72, 0.72, POST_H]))
        q += 1
    # Waypoints dos bots: chão seco, fora dos muros, mais os pontos das entradas e da arena
    import numpy as np
    blocked = np.isin(grid, (LAVA, WALL, BOX, FENCE))
    wps = []
    for gx in np.arange(-26, 27, 5.0):
        for gy in np.arange(-28, 29, 5.0):
            r = math.hypot(gx, gy)
            if R_FOOT + 1.5 > r > R_BOWL - 1.5:
                continue
            i, j = int((gx + HALF_X) / CELL), int((gy + HALF_Y) / CELL)
            if blocked[max(0, i - 2):i + 3, max(0, j - 2):j + 3].any():
                continue
            wps.append((float(gx), float(gy)))
    for e in data["entrances"]:
        for key in ("outer", "mouth", "inner"):
            wps.append(tuple(e[key]))
        for lane in e["lanes"]:
            wps.append(tuple(lane))
    for i, (x, y) in enumerate(wps):
        cv.marker(f"WP_{i:02d}", (x, y, ground_height(H, x, y) + 0.1), coll, 0.3, 'PLAIN_AXES')
    data["waypoints"] = [[x, y, ground_height(H, x, y)] for (x, y) in wps]

    # Limites da arena
    W, L = NX * CELL, NY * CELL
    for nm, size, loc in (("Limite_E", (1.0, L + 4, 14.0), (W / 2 + 0.5, 0, -1)), ("Limite_W", (1.0, L + 4, 14.0), (-W / 2 - 0.5, 0, -1)),
                          ("Limite_N", (W + 4, 1.0, 14.0), (0, L / 2 + 0.5, -1)), ("Limite_S", (W + 4, 1.0, 14.0), (0, -L / 2 - 0.5, -1))):
        cv.col_box(nm, size, loc)


def lava_markers(P, cells_out, cells_in, data):
    """Jatos ocasionais (perigo real com aviso, no Unity) e luzes de calor nas manchas maiores."""
    blobs_in = lava_blobs(cells_in)
    blobs_out = lava_blobs(cells_out)
    jets, lights = [], []
    for (c, n, cs) in blobs_in[:4]:
        jets.append(c)
        lights.append(c)
    if blobs_out:
        # demonstração e câmara numa mancha grande perto do vulcão (longe dos limites do mapa)
        cand = [bl for bl in blobs_out if R_FOOT + 1.0 < math.hypot(*bl[0]) < 28.0] or blobs_out
        OBJ["lava_demo"] = max(cand, key=lambda bl: bl[1])[0]
    for (c, n, cs) in blobs_out[:6]:
        lights.append(c)
        if len(jets) < 8:
            jets.append(c)
    for k, (x, y) in enumerate(jets):
        z = LAVA_SURF_IN if math.hypot(x, y) < R_BOWL else LAVA_SURF_OUT
        cv.marker(f"JET_{k:02d}", (x, y, z), C["Markers"], 0.6, 'CONE')
    for k, (x, y) in enumerate(lights):
        z = LAVA_SURF_IN if math.hypot(x, y) < R_BOWL else LAVA_SURF_OUT
        cv.marker(f"FX_Calor_{k:02d}", (x, y, z + 0.8), C["Markers"], 0.6, 'SPHERE')
    data["jets"] = [list(j) for j in jets]
    data["heat_lights"] = [list(l) for l in lights]
    return jets


# =============================================================================== envolvente

def surroundings(P):
    coll = C["Envolvente"]
    rng = random.Random(P["seed"] + 900)
    hw, hl = NX * CELL / 2, NY * CELL / 2
    bm = bmesh.new()
    for (cx, cy, sx, sy) in ((0, hl + 60, 260, 120), (0, -hl - 60, 260, 120), (hw + 60, 0, 120, 2 * hl), (-hw - 60, 0, 120, 2 * hl)):
        cv.add_box(bm, sx + 0.05, sy + 0.05, 0.1, center=(cx, cy, -0.12), mi=0)
    cv.place(cv.mesh_from_bm(bm, "BG_Planicie", [MAT["MAT_Cinza"]]), "BG_Planicie", coll)
    # Campos de lava antiga (blocos escuros) junto dos limites
    bm = bmesh.new()
    for i in range(30 + 15 * P["detail"]):
        side = rng.randrange(4)
        if side < 2:
            x = rng.uniform(-hw - 14, hw + 14)
            y = (1 if side == 0 else -1) * rng.uniform(hl + 1.5, hl + 14)
        else:
            y = rng.uniform(-hl - 14, hl + 14)
            x = (1 if side == 2 else -1) * rng.uniform(hw + 1.5, hw + 14)
        b = bu.lava_block(rng, rng.uniform(1.5, 4), rng.uniform(1.5, 4), rng.uniform(0.6, 2.4), 2)
        bmesh.ops.translate(b, verts=b.verts, vec=(x, y, -0.1))
        bu.bm_join(bm, b)
    cv.place(cv.mesh_from_bm(bm, "BG_Lava_Antiga", [MAT["MAT_Basalto_Escuro"]]), "BG_Lava_Antiga", coll)
    # Bordeira (parede da caldeira) e Pico ao longe
    bm = bmesh.new()
    for i in range(16):
        a = math.radians(200 + i * 9)
        d = rng.uniform(95, 110)
        cv.add_icosphere(bm, rng.uniform(14, 22), (d * math.cos(a), d * math.sin(a), -4), mi=0, subdiv=2, squash=rng.uniform(0.6, 0.9))
    cv.place(cv.mesh_from_bm(bm, "BG_Bordeira", [MAT["MAT_Monte"]]), "BG_Bordeira", coll)
    bm = bmesh.new()
    cv.add_lathe(bm, [(70.0, 0.0), (52.0, 20.0), (30.0, 46.0), (12.0, 66.0), (7.0, 70.0), (0.0, 68.0)], 32, mi=0)
    cv.place(cv.mesh_from_bm(bm, "BG_Pico", [MAT["MAT_Monte_Cinza"]]), "BG_Pico", coll, (110, 150, -3))


# =============================================================================== animação de demonstração

def animate(P, data):
    sc = bpy.context.scene
    fps = P["fps"]
    end = 8 * fps
    sc.frame_start, sc.frame_end = 1, end
    for lbl, f in (("Fluxo e crosta", 1), ("Bolhas", fps), ("Salpicos", 3 * fps), ("Jato com aviso", 5 * fps)):
        sc.timeline_markers.new(lbl, frame=f)
    # fluxo contínuo: as duas texturas deslizam a velocidades diferentes (crosta mais lenta)
    nt = MAT["MAT_Lava"].node_tree
    for name, d in (("MapCrosta", (0.6, 0.15, 0)), ("MapCalor", (1.6, 0.3, 0))):
        mp = nt.nodes[name]
        mp.inputs["Location"].default_value = (0, 0, 0)
        mp.inputs["Location"].keyframe_insert("default_value", frame=1)
        mp.inputs["Location"].default_value = d
        mp.inputs["Location"].keyframe_insert("default_value", frame=end)
    em = nt.nodes["ForcaEmissao"]
    for f in range(1, end + 1, fps // 2):
        em.inputs[1].default_value = 1.8 + 0.25 * math.sin(f * 0.37)
        em.inputs[1].keyframe_insert("default_value", frame=f)

    # demonstração junto do primeiro jato (bolsa de lava da cratera)
    jx, jy = OBJ.get("lava_demo", (0.0, 0.0))
    base = Vector((jx, jy, LAVA_SURF_OUT))
    rng = random.Random(5)
    # bolhas que crescem e rebentam
    for k in range(5):
        bm = bmesh.new()
        cv.add_icosphere(bm, 0.18, (0, 0, 0), mi=0, subdiv=2, squash=0.7)
        o = cv.place(cv.mesh_from_bm(bm, f"FX_Bolha_{k}", [MAT["MAT_Lava"]]), f"FX_Bolha_{k}", C["FX"],
                     base + Vector((rng.uniform(-1.2, 1.2), rng.uniform(-1.2, 1.2), 0)))
        t0 = fps + k * 9
        for cyc in range(3):
            s = t0 + cyc * 50
            cv.key(o, max(1, s - 1), scale=0.0)
            cv.key(o, s, scale=0.2)
            cv.key(o, s + 26, scale=1.4)
            cv.key(o, s + 28, scale=0.0)
    # salpicos: gotas em arco que voltam à lava
    for k in range(10):
        bm = bmesh.new()
        cv.add_icosphere(bm, 0.07, (0, 0, 0), mi=0, subdiv=1)
        o = cv.place(cv.mesh_from_bm(bm, f"FX_Gota_{k}", [MAT["MAT_Brasa"]]), f"FX_Gota_{k}", C["FX"], base)
        a = rng.uniform(0, math.tau)
        dist = rng.uniform(0.6, 1.6)
        hgt = rng.uniform(0.6, 1.4)
        s = 3 * fps + rng.randrange(0, 30)
        cv.key(o, s - 1, loc=base, scale=0.0)
        for q in range(9):
            u = q / 8
            p = base + Vector((math.cos(a) * dist * u, math.sin(a) * dist * u, hgt * 4 * u * (1 - u)))
            cv.key(o, s + q * 2, loc=p, scale=1.0 if q < 8 else 0.0)
    # jato com aviso: brilho → coluna → recolhe
    bm = bmesh.new()
    cv.add_prism(bm, 12, 0.35, 2.6, r_top=0.12, mi=0)
    jet = cv.place(cv.mesh_from_bm(bm, "FX_Jato", [MAT["MAT_Brasa"]]), "FX_Jato", C["FX"], base)
    w0 = 5 * fps
    cv.key(jet, w0 - 1, scale=(1, 1, 0.0))
    cv.key(jet, w0 + 45, scale=(1, 1, 0.0))          # aviso (1,5 s): só brilho
    cv.key(jet, w0 + 52, scale=(1, 1, 1.0))
    cv.key(jet, w0 + 80, scale=(1, 1, 0.9))
    cv.key(jet, w0 + 90, scale=(1, 1, 0.0))
    bm = bmesh.new()
    cv.add_torus(bm, 0.9, 0.08, seg=24, sides=4)
    ring = cv.place(cv.mesh_from_bm(bm, "FX_Aviso_Jato", [MAT["MAT_Brasa"]]), "FX_Aviso_Jato", C["FX"], base + Vector((0, 0, 0.03)))
    cv.key(ring, w0 - 1, scale=0.0)
    cv.key(ring, w0, scale=0.6)
    cv.key(ring, w0 + 45, scale=1.3)
    cv.key(ring, w0 + 50, scale=0.0)
    # fumo da cratera (partículas, só Blender)
    for k in range(4):
        th = math.radians(22.5 + 90 * k)
        r = (R_RIM_IN + R_RIM_OUT) / 2
        H = OBJ["H"]
        x, y = r * math.cos(th), r * math.sin(th)
        bm = bmesh.new()
        cv.add_prism(bm, 8, 0.6, 0.05)
        e = cv.place(cv.mesh_from_bm(bm, f"FX_Emissor_Fumo_{k}", [MAT["MAT_Fumo"]]), f"FX_Emissor_Fumo_{k}", C["FX"],
                     (x, y, ground_height(H, x, y) + 0.2))
        e.hide_render = True
        mod = e.modifiers.new("Fumo", 'PARTICLE_SYSTEM')
        ps = mod.particle_system.settings
        ps.count = 40 * P["detail"]
        ps.frame_start, ps.frame_end = 1, end
        ps.lifetime = 3 * fps
        ps.normal_factor = 1.2
        ps.effector_weights.gravity = -0.03
        ps.render_type = 'OBJECT'
        if "FX_Puff" not in bpy.data.objects:
            pb = bmesh.new()
            cv.add_icosphere(pb, 0.6, (0, 0, 0), mi=0, subdiv=1)
            puff = cv.place(cv.mesh_from_bm(pb, "FX_Puff", [MAT["MAT_Fumo"]]), "FX_Puff", C["FX"], (0, 0, -50))
        ps.instance_object = bpy.data.objects["FX_Puff"]
        ps.size_random = 0.5
        e.show_instancer_for_render = False
    return end


# =============================================================================== câmaras e luz

def setup_preview(P):
    coll = C["Preview"]
    sc = bpy.context.scene
    sun = bpy.data.lights.new("LGT_Sol", 'SUN')
    sun.energy = 3.4
    sun.color = (1.0, 0.86, 0.72)
    o = bpy.data.objects.new("LGT_Sol", sun)
    coll.objects.link(o)
    o.rotation_euler = (math.radians(42), 0, math.radians(-38))
    w = bpy.data.worlds.get("WRL_Fogo") or bpy.data.worlds.new("WRL_Fogo")
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.62, 0.56, 0.52, 1)
    bg.inputs[1].default_value = 0.9
    sc.world = w
    pl = bpy.data.lights.new("LGT_Cratera", 'POINT')
    pl.energy = 3000
    pl.color = (1.0, 0.45, 0.15)
    pl.shadow_soft_size = 3
    o = bpy.data.objects.new("LGT_Cratera", pl)
    coll.objects.link(o)
    o.location = (0, 0, BOWL_Y + 4)
    cams = {}

    def cam(name, loc, target, lens=30.0, ortho=None):
        cd = bpy.data.cameras.new(name)
        cd.lens = lens
        cd.clip_end = 800
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

    cam("CAM_Planta", (0, 0, 90), (0, 0, 0), ortho=NY * CELL + 2)
    cam("CAM_Geral", (34, -46, 30), (0, 0, 2), lens=26)
    cam("CAM_Interior", (6.5, -10.5, BOWL_Y + 3.2), (-1.0, 3.0, BOWL_Y + 0.3), lens=20)
    jx, jy = OBJ.get("lava_demo", (0.0, 0.0))
    d = Vector((-jx, -jy, 0)).normalized()             # do lado do vulcão, a olhar para fora
    side = Vector((-d.y, d.x, 0))
    cam("CAM_Lava", (jx - d.x * 1.0 + side.x * 4.0, jy - d.y * 1.0 + side.y * 4.0, 4.2), (jx, jy, LAVA_SURF_OUT), lens=24)
    sc.camera = cams["CAM_Geral"]
    return cams


# =============================================================================== principal

def main():
    P = parse_args()
    random.seed(P["seed"])
    os.makedirs(P["out_dir"], exist_ok=True)
    sc = setup_scene(P)
    bind_helpers()
    with bpy.context.temp_override(scene=sc, view_layer=sc.view_layers[0]):
        run(P, sc, P["out_dir"])
    print("[FG] Concluído.")


def run(P, sc, out):
    tex = make_textures(P, os.path.join(out, "Textures"))
    make_materials(tex)
    data = dict(boxes=[], spawns=[], bushes=[], waypoints=[],
                volcano=dict(r_bowl=R_BOWL, r_rim_in=R_RIM_IN, r_rim_out=R_RIM_OUT, r_foot=R_FOOT, rim_h=RIM_H, bowl_y=BOWL_Y,
                             gorges=GORGES, gorge_half_width=GORGE_HW))
    cls = classify_plan(P["plan"])
    grid = build_grid(cls)
    volc = Volcano(P["seed"])
    H = build_terrain(P, grid, volc, data)
    cells_out, cells_in = build_lava(P, grid, data)
    n_edge = lava_edge_rocks(P, grid, H)
    build_cover(P, grid, H, data)
    volcano_details(P, volc, H, data)
    build_bushes(P, grid, data)
    interior_extras(P, H, data)
    build_markers(P, grid, cls, H, data)
    jets = lava_markers(P, cells_out, cells_in, data)
    if jets:
        OBJ["jet0"] = jets[0]
    surroundings(P)
    F_end = animate(P, data)
    cams = setup_preview(P)
    for ob in C["Collision"].objects:
        ob.hide_render = True

    tris = {}
    for n in COLL_NAMES:
        tris[n] = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in C[n].all_objects if o.type == 'MESH')
    data["triangles"] = tris
    data["bush_triangles"] = sum(b["tris"] for b in data["bushes"])
    print("[FG] Triângulos por collection:", tris, "arbustos:", data["bush_triangles"], "em", len(data["bushes"]), "zonas;",
          "spawns:", len(data["spawns"]), "lava:", len(cells_out), "+", len(cells_in), "células")
    with open(os.path.join(out, "fogo_layout.json"), "w", encoding="utf-8") as fh:
        json.dump(data, fh, indent=1)

    if P["export"]:
        fbx = os.path.join(out, "FBX")
        os.makedirs(fbx, exist_ok=True)
        sc.frame_set(0)
        static = [o for n in ("Terrain", "Volcano", "Cover", "Bushes", "Markers", "Collision") for o in C[n].all_objects
                  if o.type in ('MESH', 'EMPTY')]
        cv.export_fbx(os.path.join(fbx, "Fogo_Static.fbx"), static)
        cv.export_fbx(os.path.join(fbx, "Fogo_Lava.fbx"), list(C["Lava"].all_objects))
        fx = [bpy.data.objects[n] for n in ("FX_Jato", "FX_Aviso_Jato") if n in bpy.data.objects]
        fx += [o for o in C["FX"].objects if o.name.startswith("FX_Bolha_")]
        cv.export_fbx(os.path.join(fbx, "Fogo_Lava_FX_Ref.fbx"), fx, anim=True)
        print("[FG] FBX exportados para", fbx)

    sc.frame_set(int(F_end * 0.7))
    if P["save_blend"]:
        path = os.path.join(out, "Fogo.blend")
        bpy.ops.wm.save_as_mainfile(filepath=path, check_existing=False)
        try:
            bpy.ops.file.make_paths_relative()
            bpy.ops.file.pack_all()
            bpy.ops.wm.save_mainfile(compress=True)
        except Exception as e:
            print("[FG] Aviso ao guardar:", e)
        print("[FG] .blend guardado em", path)

    if P["render"] or P["anim_preview"]:
        cv.setup_render(P)
        C["Collision"].hide_render = True
        C["Preview"].hide_render = False
        rdir = os.path.join(out, "Renders")
        os.makedirs(rdir, exist_ok=True)
    if P["render"]:
        R = P["res"]
        cv.render_still(cams["CAM_Planta"], os.path.join(rdir, "planta.png"), int(R * NX / NY), R, P["samples"], frame=int(F_end * 0.4))
        cv.render_still(cams["CAM_Geral"], os.path.join(rdir, "geral.png"), R, int(R * 0.5625), P["samples"])
        cv.render_still(cams["CAM_Interior"], os.path.join(rdir, "interior.png"), R, int(R * 0.5625), P["samples"])
        sc.frame_set(1)
        for lbl, f in (("lava_fluxo", 20), ("lava_bolhas", P["fps"] + 22), ("lava_salpicos", 3 * P["fps"] + 18), ("lava_jato", 5 * P["fps"] + 60)):
            cv.render_still(cams["CAM_Lava"], os.path.join(rdir, f"{lbl}.png"), 900, 506, max(12, P["samples"] // 2), frame=f)
    if P["anim_preview"]:
        sc.camera = cams["CAM_Lava"]
        sc.render.resolution_x, sc.render.resolution_y = 480, 270
        sc.cycles.samples = 8
        for f in range(1, F_end + 1, 2):
            sc.frame_set(f)
            sc.render.filepath = os.path.join(rdir, "anim", f"lava_{f:04d}.png")
            bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
