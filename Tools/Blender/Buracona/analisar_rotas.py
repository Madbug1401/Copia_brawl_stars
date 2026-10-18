"""
Análise 2D das rotas do mapa Buracona (sem Unity). Adaptado de ../CidadeVelha/analisar_rotas.py.

Lê buracona_layout.json (gerado pelo script do Blender), rasteriza os colisores
inflacionados pelo raio do CharacterController (0.4 m) e verifica:
  - ligação spawn azul ↔ spawn vermelho;
  - acesso a todos os pontos de aterragem das gemas;
  - existência de 3 rotas independentes (esquerda, centro, direita);
  - largura mínima de cada rota (gargalo);
  - distâncias/tempos spawn → gemas para as duas equipas.
Opcionalmente desenha as rotas por cima do render de cima.

Uso: python analisar_rotas.py <layout.json> [render_planta.png] [saida.png]
Requer numpy, scipy (e matplotlib + pillow para a imagem).
"""
import json
import math
import sys
from collections import deque

import numpy as np
from scipy import ndimage

CELL = 0.1          # metros por célula
RADIUS = 0.4        # raio do CharacterController (Player.prefab)
STEP = 0.35         # stepOffset
WALK = 6.5          # velocidade de andar típica (m/s)


def load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def blocking(box):
    """Colisores que bloqueiam o movimento a pé (o chão e os degraus do Pelourinho sobem-se)."""
    n = box["name"]
    if n.startswith("COL_Box_Chao") or n.startswith("COL_Box_Limite"):
        return False
    z0 = box["loc"][2]
    top = z0 + box["size"][2]
    return top > STEP and z0 < 1.8


def raster(data, inflate):
    W, L = data["arena"]["width"], data["arena"]["length"]
    nx, ny = int(round(W / CELL)), int(round(L / CELL))
    xs = (np.arange(nx) + 0.5) * CELL - W / 2
    ys = (np.arange(ny) + 0.5) * CELL - L / 2
    X, Y = np.meshgrid(xs, ys)
    occ = np.zeros_like(X, dtype=bool)
    for b in data["boxes"]:
        if not blocking(b):
            continue
        cx, cy = b["loc"][0], b["loc"][1]
        if b.get("kind") == "circle":                    # barreira do buraco (rocha da borda)
            occ |= (X - cx) ** 2 + (Y - cy) ** 2 <= (b["radius"] + inflate) ** 2
            continue
        sx, sy = b["size"][0] / 2 + inflate, b["size"][1] / 2 + inflate
        a = -b["rot_z"]
        lx = (X - cx) * math.cos(a) - (Y - cy) * math.sin(a)
        ly = (X - cx) * math.sin(a) + (Y - cy) * math.cos(a)
        occ |= (np.abs(lx) <= sx) & (np.abs(ly) <= sy)
    # limites da arena
    m = int(math.ceil(inflate / CELL))
    occ[:m, :] = occ[-m:, :] = True
    occ[:, :m] = occ[:, -m:] = True
    return occ, xs, ys


def idx(xs, ys, p):
    return int(np.clip(np.searchsorted(ys, p[1]), 0, len(ys) - 1)), int(np.clip(np.searchsorted(xs, p[0]), 0, len(xs) - 1))


def bfs(free, start):
    dist = np.full(free.shape, -1, np.int32)
    q = deque()
    for s in start:
        if free[s]:
            dist[s] = 0
            q.append(s)
    par = {}
    while q:
        r, c = q.popleft()
        for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            rr, cc = r + dr, c + dc
            if 0 <= rr < free.shape[0] and 0 <= cc < free.shape[1] and free[rr, cc] and dist[rr, cc] < 0:
                if dr and dc and not (free[r + dr, c] and free[r, c + dc]):
                    continue
                dist[rr, cc] = dist[r, c] + 1
                par[(rr, cc)] = (r, c)
                q.append((rr, cc))
    return dist, par


def geodesic(free, a, b):
    """Distância (m) e caminho entre dois pontos (A* simples em 8 vizinhos)."""
    import heapq
    h = lambda p: math.hypot(p[0] - b[0], p[1] - b[1])
    g = {a: 0.0}
    par = {}
    pq = [(h(a), a)]
    while pq:
        _, cur = heapq.heappop(pq)
        if cur == b:
            path = [cur]
            while cur in par:
                cur = par[cur]
                path.append(cur)
            return g[b] * CELL, path[::-1]
        r, c = cur
        for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            rr, cc = r + dr, c + dc
            if not (0 <= rr < free.shape[0] and 0 <= cc < free.shape[1]) or not free[rr, cc]:
                continue
            if dr and dc and not (free[r + dr, c] and free[r, c + dc]):
                continue
            ng = g[cur] + (1.4142 if dr and dc else 1.0)
            if ng < g.get((rr, cc), 1e18):
                g[(rr, cc)] = ng
                par[(rr, cc)] = cur
                heapq.heappush(pq, (ng + h((rr, cc)), (rr, cc)))
    return None, []


def route_width(data, region_mask_fn, a_pts, b_pts):
    """Maior raio que ainda liga A a B só dentro da região → largura útil do gargalo (2 × raio)."""
    lo, hi = 0.0, 3.0
    for _ in range(12):
        mid = (lo + hi) / 2
        occ, xs, ys = raster(data, mid)
        X, Y = np.meshgrid(xs, ys)
        free = ~occ & region_mask_fn(X, Y)
        dist, _ = bfs(free, [idx(xs, ys, p) for p in a_pts])
        ok = any(dist[idx(xs, ys, p)] >= 0 for p in b_pts)
        lo, hi = (mid, hi) if ok else (lo, mid)
    return 2 * lo


def main():
    data = load(sys.argv[1])
    occ, xs, ys = raster(data, RADIUS)
    free = ~occ
    X, Y = np.meshgrid(xs, ys)
    blue = [s["pos"] for s in data["spawns"] if s["team"] == "Blue"]
    red = [s["pos"] for s in data["spawns"] if s["team"] == "Red"]
    report = []
    ok_all = True

    def check(label, cond):
        nonlocal ok_all
        ok_all &= bool(cond)
        report.append(("OK  " if cond else "FALHA ") + label)

    for p in blue + red:
        check(f"spawn livre em ({p[0]:.1f}, {p[1]:.1f})", free[idx(xs, ys, p)])
    dist, _ = bfs(free, [idx(xs, ys, p) for p in blue])
    check("spawn azul liga ao spawn vermelho", all(dist[idx(xs, ys, p)] >= 0 for p in red))
    for i, l in enumerate(data["lands"]):
        check(f"GEM_Land_{i:02d} alcançável a pé", dist[idx(xs, ys, l)] >= 0)
        r = math.hypot(l[0], l[1])
        check(f"GEM_Land_{i:02d} em terreno seco fora da borda (r={r:.2f} ≥ {data['hole']['radius'] + RADIUS + 0.5:.2f})",
              r >= data["hole"]["radius"] + RADIUS + 0.5)
    for p in blue + red:
        r = math.hypot(p[0], p[1])
        check(f"spawn ({p[0]:.1f}, {p[1]:.1f}) longe da borda do buraco ({r:.1f} m)", r > data["hole"]["radius"] + 8)
    # linha de tiro direta entre bases (cobertura alta ≥ altura dos olhos, sem contar a água/arbustos)
    hi_occ, _, _ = raster(dict(data, boxes=[b for b in data["boxes"] if b.get("kind") != "circle" and b["size"][2] > 1.7]), 0.05)
    def los(a, b):
        n = int(math.hypot(b[0] - a[0], b[1] - a[1]) / (CELL * 0.5)) + 1
        for k in range(n + 1):
            t = k / n
            if hi_occ[idx(xs, ys, (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))]:
                return False
        return True
    open_pairs = sum(1 for a in blue for b in red if los(a, b))
    check(f"sem linha de tiro direta spawn azul ↔ spawn vermelho ({open_pairs} pares abertos de {len(blue) * len(red)})", open_pairs == 0)
    longest = 0.0
    for yy in np.arange(-22, 22.1, 1.0):
        run = 0
        for x in xs:
            run = run + CELL if not hi_occ[idx(xs, ys, (x, yy))] else 0
            longest = max(longest, run)
    report.append(f"INFO maior linha horizontal livre de cobertura alta: {longest:.1f} m")
    # Arbustos: não bloqueiam, ficam alcançáveis, não tapam spawns nem aterragens de gemas
    def in_bush(pt, margin=0.0):
        for b in data.get("bushes", []):
            if abs(pt[0] - b["center"][0]) <= b["size"][0] / 2 + margin and abs(pt[1] - b["center"][1]) <= b["size"][1] / 2 + margin:
                return b["name"]
        return None
    for b in data.get("bushes", []):
        c = b["center"]
        check(f"{b['name']} atravessável e alcançável ({b['size'][0]:.1f}×{b['size'][1]:.1f}×{b['size'][2]:.1f} m)",
              free[idx(xs, ys, c)] and dist[idx(xs, ys, c)] >= 0 and b["size"][2] >= 1.9)
    for p in blue + red:
        check(f"spawn ({p[0]:.1f}, {p[1]:.1f}) fora de arbustos", in_bush(p, RADIUS) is None)
    for i, l in enumerate(data["lands"]):
        check(f"GEM_Land_{i:02d} fora de arbustos", in_bush(l, 0.6) is None)
    if data.get("bushes"):
        area = sum(b["size"][0] * b["size"][1] for b in data["bushes"])
        report.append(f"INFO {len(data['bushes'])} arbustos, {area:.0f} m² de esconderijo")

    # zonas sem acesso (becos fechados): células livres não alcançadas
    unreach = free & (dist < 0)
    lab, n = ndimage.label(unreach)
    big = [int((lab == k).sum() * CELL * CELL) for k in range(1, n + 1) if (lab == k).sum() * CELL * CELL > 1.0]
    check(f"sem áreas livres isoladas > 1 m² (encontradas: {big})", len(big) == 0)

    # três rotas: centro (|x| < 8.5), direita (x > 8.4), esquerda (x < -8.4) — no troço médio
    def region(kind):
        def f(Xg, Yg):
            mid = np.abs(Yg) < 8.0
            if kind == "centro":
                lane = np.abs(Xg) < 8.5
            elif kind == "direita":
                lane = Xg > 8.4
            else:
                lane = Xg < -8.4
            return ~mid | lane
        return f

    routes = {}
    for kind in ("esquerda", "centro", "direita"):
        reg = region(kind)(X, Y)
        f2 = free & reg
        d2, _ = bfs(f2, [idx(xs, ys, p) for p in blue])
        exists = any(d2[idx(xs, ys, p)] >= 0 for p in red)
        w = route_width(data, region(kind), blue, red) if exists else 0.0
        length, path = geodesic(f2, idx(xs, ys, blue[4]), idx(xs, ys, red[4]))
        routes[kind] = dict(exists=exists, width=w, length=length, path=path)
        check(f"rota {kind}: existe, gargalo ≈ {w:.2f} m, spawn→spawn {length or 0:.1f} m "
              f"(~{(length or 0) / WALK:.1f} s a andar)", exists and w >= 2.0)

    # equilíbrio: distância de cada equipa à gema mais próxima e média a todas
    for team, pts in (("Azul", blue), ("Vermelha", red)):
        ds = []
        for l in data["lands"]:
            d, _ = geodesic(free, idx(xs, ys, pts[4]), idx(xs, ys, l))
            ds.append(d)
        report.append(f"INFO equipa {team}: gema mais próxima {min(ds):.1f} m · média {sum(ds) / len(ds):.1f} m")

    print("\n".join(report))
    print("RESULTADO:", "tudo OK" if ok_all else "há falhas")

    if len(sys.argv) >= 4:
        draw(data, routes, sys.argv[2], sys.argv[3], free, xs, ys)
    return 0 if ok_all else 1


def draw(data, routes, render_path, out_path, free, xs, ys):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from PIL import Image
    img = Image.open(render_path).convert("RGB")
    W, L = data["arena"]["width"], data["arena"]["length"]
    w_px, h_px = img.size
    ppm = h_px / (L + 2.5)                              # CAM_Planta: ortho_scale = comprimento + 2.5 m
    to_px = lambda x, y: (w_px / 2 + x * ppm, h_px / 2 - y * ppm)
    fig = plt.figure(figsize=(w_px / 100, h_px / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.imshow(img)
    ax.set_xlim(0, w_px)
    ax.set_ylim(h_px, 0)
    ax.axis("off")
    colors = {"esquerda": "#ffb000", "centro": "#ffffff", "direita": "#00d4ff"}
    for k, r in routes.items():
        if not r["path"]:
            continue
        pts = [to_px(xs[c], ys[rw]) for rw, c in r["path"][::6]]
        ax.plot([p[0] for p in pts], [p[1] for p in pts], color=colors[k], lw=3.2, alpha=0.9,
                label=f"rota {k} · gargalo {r['width']:.1f} m · {r['length']:.0f} m")
    import matplotlib.patches as mpatches
    for b in data.get("bushes", []):
        (cx, cy), (w, d) = b["center"], b["size"][:2]
        x0, y0 = to_px(cx - w / 2, cy + d / 2)
        ax.add_patch(mpatches.Rectangle((x0, y0), w * ppm, d * ppm, fill=True, fc=(0.1, 0.8, 0.2, 0.35), ec="#0b5d1e", lw=2, hatch="//"))
    ax.add_patch(mpatches.Rectangle((0, 0), 0, 0, fc=(0.1, 0.8, 0.2, 0.35), ec="#0b5d1e", hatch="//", label="arbusto (esconde, atravessa-se)"))
    for s in data["spawns"]:
        x, y = to_px(*s["pos"][:2])
        ax.plot(x, y, "o", ms=9, mec="black", mfc="#2f7bff" if s["team"] == "Blue" else "#ff3b30")
    for i, l in enumerate(data["lands"]):
        x, y = to_px(*l[:2])
        ax.plot(x, y, "D", ms=8, mec="white", mfc="#b061ff")
    x, y = to_px(*data["gem_spawn"][:2])
    ax.plot(x, y, "*", ms=18, mec="black", mfc="#e8c6ff")
    for (wx, wy) in data["waypoints"]:
        x, y = to_px(wx, wy)
        ax.plot(x, y, "+", ms=6, color="#222222")
    ax.plot([], [], "o", mfc="#2f7bff", mec="black", label="spawn azul")
    ax.plot([], [], "o", mfc="#ff3b30", mec="black", label="spawn vermelho")
    ax.plot([], [], "D", mfc="#b061ff", mec="white", label="aterragem de gemas")
    ax.plot([], [], "*", mfc="#e8c6ff", mec="black", ms=12, label="GEM_Spawn (debaixo de água)")
    ax.legend(loc="lower left", fontsize=9, framealpha=0.85)
    fig.savefig(out_path)


if __name__ == "__main__":
    sys.exit(main())
