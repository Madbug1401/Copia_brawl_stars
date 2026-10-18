"""
Porta para Python do gerador de arbustos do jogo (Assets/_Game/Scripts/World/LeafBush.cs).

Usa o mesmo algoritmo e o mesmo gerador aleatório do .NET (System.Random com semente),
por isso, com a mesma `seed` e o mesmo `size`, dá o mesmo arbusto que o Unity gera em runtime.
No Blender serve só para pré-visualizar; no jogo as folhas são sempre geradas pelo LeafBush.cs.

Coordenadas devolvidas no espaço do Unity (Y para cima). Cores por vértice em sRGB (como no Unity).
"""
import math

LEAF_RINGS = 5
LEAF_SIDES = 8
MAX_LEAVES_PER_DOME = 400

PALETTES = [
    [(0.10, 0.30, 0.10), (0.24, 0.52, 0.16), (0.58, 0.84, 0.30)],   # verde fresco
    [(0.08, 0.25, 0.11), (0.16, 0.44, 0.17), (0.42, 0.72, 0.28)],   # verde profundo
    [(0.16, 0.32, 0.07), (0.34, 0.58, 0.12), (0.72, 0.88, 0.32)],   # lima
    [(0.07, 0.26, 0.16), (0.17, 0.47, 0.27), (0.50, 0.80, 0.40)],   # verde-água
]


class NetRandom:
    """System.Random (algoritmo subtrativo de Knuth, como no .NET Framework/Mono com semente)."""
    MBIG = 2147483647
    MSEED = 161803398

    def __init__(self, seed):
        sa = [0] * 56
        subtraction = self.MBIG if seed == -2147483648 else abs(seed)
        mj = self.MSEED - subtraction
        sa[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            sa[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = sa[ii]
        for _ in range(1, 5):
            for i in range(1, 56):
                sa[i] -= sa[1 + (i + 30) % 55]
                if sa[i] < 0:
                    sa[i] += self.MBIG
        self.sa = sa
        self.inext = 0
        self.inextp = 21

    def _sample(self):
        a = self.inext + 1
        if a >= 56:
            a = 1
        b = self.inextp + 1
        if b >= 56:
            b = 1
        r = self.sa[a] - self.sa[b]
        if r == self.MBIG:
            r -= 1
        if r < 0:
            r += self.MBIG
        self.sa[a] = r
        self.inext, self.inextp = a, b
        return r

    def next_double(self):
        return self._sample() * (1.0 / self.MBIG)


def _norm(v):
    l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / l, v[1] / l, v[2] / l) if l > 1e-9 else (0.0, 0.0, 0.0)


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _add(a, b, s=1.0):
    return (a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s)


def _clamp01(x):
    return 0.0 if x < 0 else 1.0 if x > 1 else x


def generate(size, seed, density=1.0, palette_index=-1):
    """Devolve (vértices, cores, triângulos) no espaço do Unity. size = (x, y, z) em metros."""
    rng = NetRandom(seed)
    R = lambda a, b: a + (b - a) * rng.next_double()

    pr = rng.next_double()
    rolled = 0 if pr < 0.38 else 2 if pr < 0.62 else 1 if pr < 0.86 else 3
    pal = PALETTES[min(max(palette_index, 0), 3) if palette_index >= 0 else rolled]
    core = tuple(c * 0.8 for c in pal[0])
    dark, light = pal[1], pal[2]
    tint = (R(-0.04, 0.04), R(-0.03, 0.03), R(-0.03, 0.03))

    leaf_len = 0.5 * R(0.88, 1.15)
    wid_r = R(0.56, 0.68)
    thick_r = R(0.2, 0.26)
    crease = R(0.35, 0.55)
    lift = R(28.0, 46.0) * math.pi / 180.0
    curl = R(-0.14, -0.02)
    lump = R(0.04, 0.09)
    density = min(max(density, 0.1), 4.0)

    sx, sy, sz = max(0.5, size[0]), max(0.4, size[1]), max(0.5, size[2])
    step = 1.7
    nx = max(1, round(sx / step))
    nz = max(1, round(sz / step))
    domes = []
    for ix in range(nx):
        for iz in range(nz):
            cx = -sx * 0.5 + (ix + 0.5) * sx / nx + R(-0.15, 0.15)
            cz = -sz * 0.5 + (iz + 0.5) * sz / nz + R(-0.15, 0.15)
            rx = max(0.85, sx / nx * 0.5 * 1.3) * R(0.92, 1.08)
            rz = max(0.85, sz / nz * 0.5 * 1.3) * R(0.92, 1.08)
            cy = -0.3 * R(0.8, 1.2)
            top = sy * R(0.88, 1.12)
            phase = R(0.0, 100.0)
            domes.append(((cx, cy, cz), (rx, top - cy, rz), phase))

    V, Cl, T = [], [], []

    # núcleo escuro
    for (c, r, _) in domes:
        m, n = 10, 6
        yc = max(-1.0, (-c[1] - 0.1) / r[1])
        ph_max = math.acos(min(max(yc, -1.0), 1.0))
        b0 = len(V)
        for a in range(n + 1):
            phi = ph_max * a / n
            for b in range(m):
                th = 2 * math.pi * b / m
                d = (math.sin(phi) * math.cos(th), math.cos(phi), math.sin(phi) * math.sin(th))
                V.append((c[0] + d[0] * r[0] * 0.82, c[1] + d[1] * r[1] * 0.82, c[2] + d[2] * r[2] * 0.82))
                Cl.append(core)
        for a in range(n):
            for b in range(m):
                i0 = b0 + a * m + b
                i1 = b0 + a * m + (b + 1) % m
                i2, i3 = i0 + m, i1 + m
                T.extend((i0, i1, i2, i1, i3, i2))

    def inside_other(skip, p):
        for k, (c, r, _) in enumerate(domes):
            if k == skip:
                continue
            x = (p[0] - c[0]) / (r[0] * 0.96)
            y = (p[1] - c[1]) / (r[1] * 0.96)
            z = (p[2] - c[2]) / (r[2] * 0.96)
            if x * x + y * y + z * z < 1.0:
                return True
        return False

    golden = math.pi * (3.0 - math.sqrt(5.0))
    K = 48
    for k, (c, r, phase) in enumerate(domes):
        a_ = math.sqrt(r[0] * r[2])
        c_ = r[1]
        y0 = max(-1.0, (-c[1] - 0.05) / r[1])
        ys = [1.0 - (1.0 - y0) * q / K for q in range(K + 1)]
        cum = [0.0] * (K + 1)
        for q in range(K):
            d0 = math.sqrt(a_ * a_ * ys[q] ** 2 + c_ * c_ * (1 - ys[q] ** 2))
            d1 = math.sqrt(a_ * a_ * ys[q + 1] ** 2 + c_ * c_ * (1 - ys[q + 1] ** 2))
            cum[q + 1] = cum[q] + (d0 + d1) * 0.5 * (1 - y0) / K
        area = 2 * math.pi * a_ * cum[K]
        count = min(MAX_LEAVES_PER_DOME, int(area / (leaf_len * leaf_len * wid_r * 0.75) * 1.6 * density))
        qi = 0
        for i in range(count):
            tq = (i + 0.5) / count * cum[K]
            while qi < K - 1 and cum[qi + 1] < tq:
                qi += 1
            f = (tq - cum[qi]) / max(1e-6, cum[qi + 1] - cum[qi])
            y = ys[qi] + (ys[qi + 1] - ys[qi]) * f
            rr = math.sqrt(max(0.0, 1 - y * y))
            ang = i * golden + phase + R(-0.25, 0.25)
            d = (rr * math.cos(ang), y, rr * math.sin(ang))
            bump = 1 + lump * math.sin(3 * ang + phase) * math.sin(4 * y + phase)
            p = (c[0] + d[0] * r[0] * bump, c[1] + d[1] * r[1] * bump, c[2] + d[2] * r[2] * bump)
            if p[1] < -0.05 or inside_other(k, p):
                continue
            nrm = _norm((d[0] / r[0], d[1] / r[1], d[2] / r[2]))
            if abs(nrm[1]) > 0.97:
                a2 = R(0.0, 2 * math.pi)
                t = (math.cos(a2), 0.0, math.sin(a2))
            else:
                down = (0.0, -1.0, 0.0)
                t = _norm(_add(down, nrm, -_dot(down, nrm)))
            roll = R(-45.0, 45.0) * math.pi / 180
            bn = _cross(nrm, t)
            t = _norm((t[0] * math.cos(roll) + bn[0] * math.sin(roll), t[1] * math.cos(roll) + bn[1] * math.sin(roll),
                       t[2] * math.cos(roll) + bn[2] * math.sin(roll)))
            la = min(48 * math.pi / 180, lift * R(0.75, 1.25) * (0.55 + 0.45 * max(0.0, y)))
            L = _norm((t[0] * math.cos(la) + nrm[0] * math.sin(la), t[1] * math.cos(la) + nrm[1] * math.sin(la),
                       t[2] * math.cos(la) + nrm[2] * math.sin(la)))
            N = _norm(_add(nrm, L, -_dot(L, nrm)))
            W = _cross(N, L)
            ln = leaf_len * R(0.85, 1.15) * (1.12 - 0.25 * max(0.0, y))
            wd = ln * wid_r * R(0.9, 1.1)
            th = ln * thick_r
            base = (p[0] - L[0] * ln * 0.32 - nrm[0] * 0.03, p[1] - L[1] * ln * 0.32 - nrm[1] * 0.03, p[2] - L[2] * ln * 0.32 - nrm[2] * 0.03)
            tc = rng.next_double() ** 0.8 * 0.75 + 0.35 * max(0.0, nrm[1]) - 0.1
            j = R(-0.02, 0.02)
            tcc = _clamp01(tc)
            col = tuple(_clamp01(dark[q] + (light[q] - dark[q]) * tcc + tint[q] + j) for q in range(3))
            vein = tuple(x * 0.78 for x in col)
            vb = len(V)
            for kk in range(LEAF_RINGS + 1):
                u = kk / LEAF_RINGS
                w = wd * 0.5 * max(0.0, math.sin(math.pi * u ** 0.85)) ** 0.6
                tt = th * max(0.0, math.sin(math.pi * u)) ** 0.5
                bend = curl * ln * u * u
                ctr = _add(base, L, u * ln)
                for jj in range(LEAF_SIDES):
                    a = 2 * math.pi * jj / LEAF_SIDES
                    cc, ss = math.cos(a), math.sin(a)
                    up = (ss ** 0.7 * tt * (1 - crease * math.exp(-cc * cc / 0.06))) if ss >= 0 else ss * tt * 0.35
                    V.append((ctr[0] + W[0] * w * cc + N[0] * (up + bend), ctr[1] + W[1] * w * cc + N[1] * (up + bend),
                              ctr[2] + W[2] * w * cc + N[2] * (up + bend)))
                    Cl.append(vein if ss > 0.9 else col)
            for kk in range(LEAF_RINGS):
                for jj in range(LEAF_SIDES):
                    i0 = vb + kk * LEAF_SIDES + jj
                    i1 = vb + kk * LEAF_SIDES + (jj + 1) % LEAF_SIDES
                    T.extend((i0, i1, i0 + LEAF_SIDES, i1, i1 + LEAF_SIDES, i0 + LEAF_SIDES))
    return V, Cl, T
