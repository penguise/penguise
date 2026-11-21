"""Penguise igloo, cartoon style, generated from a small 3D model with an orthographic camera.

Canvas, base pivot and silhouette follow Assets/Sprites/Props/Igloo.png (1254 px) so the
GameLayout igloo hull and window-light position stay valid.

The door is its own group hinged on its right edge and opens outward. Because the camera is
orthographic, the projected door at any opening angle is the closed door under one 2D affine
matrix (door_matrix), so a game can animate it with a single transform.
"""
import math, sys

W_CANVAS = 1254
CX, CY = 634.0, 794.0          # screen position of the dome's base-plane center
R, RY = 512.0, 590.0           # dome radii: horizontal, vertical
ELEV = math.radians(23.6)      # camera elevation
SE, CE = math.sin(ELEV), math.cos(ELEV)
VIEW = (0.0, SE, CE)

INK = "#1E3A55"
SEAM = "#41709A"
MORTAR = "#7FAED3"
EDGE = "#8DBBDD"
TONES = ("#A3CDEA", "#C4E2F5", "#E2F3FD")   # shade, mid, light
SNOW = "#FFFFFF"
OUTLINE_W, SEAM_W, DETAIL_W = 7, 3.5, 5

# ---- math helpers -------------------------------------------------------------
def norm(v):
    l = math.sqrt(sum(c * c for c in v)) or 1.0
    return tuple(c / l for c in v)
def dot(a, b): return sum(x * y for x, y in zip(a, b))
def add(*vs): return tuple(sum(c) for c in zip(*vs))
def mul(v, k): return tuple(c * k for c in v)
LIGHT = norm((-0.38, 0.7, 0.6))

def proj(p):
    x, y, z = p
    return (CX + x, CY - y * CE + z * SE)

def smooth_path(pts, closed=True, t=1.0):
    n = len(pts); d = f"M{pts[0][0]:.1f} {pts[0][1]:.1f}"
    for i in range(n if closed else n - 1):
        p0 = pts[(i - 1) % n] if (closed or i > 0) else pts[i]
        p1, p2 = pts[i], pts[(i + 1) % n]
        p3 = pts[(i + 2) % n] if (closed or i + 2 < n) else p2
        c1 = (p1[0] + (p2[0] - p0[0]) / 6 * t, p1[1] + (p2[1] - p0[1]) / 6 * t)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6 * t, p2[1] - (p3[1] - p1[1]) / 6 * t)
        d += f" C{c1[0]:.1f} {c1[1]:.1f} {c2[0]:.1f} {c2[1]:.1f} {p2[0]:.1f} {p2[1]:.1f}"
    return d + (" Z" if closed else "")

def poly_path(pts, closed=True):
    return "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + (" Z" if closed else "")

def line(a, b): return f"M{a[0]:.1f} {a[1]:.1f} L{b[0]:.1f} {b[1]:.1f}"

def tone(n):
    d = dot(n, LIGHT)
    return TONES[2] if d > 0.62 else TONES[1] if d > 0.3 else TONES[0]

def lerp(a, b, t): return a + (b - a) * t

# ---- dome -----------------------------------------------------------------------
def dome(th, ph, grow=0.0):
    t, p = math.radians(th), math.radians(ph)
    r, ry = R + grow, RY + grow
    return (r * math.cos(p) * math.sin(t), ry * math.sin(p), r * math.cos(p) * math.cos(t))

def dome_normal(th, ph):
    x, y, z = dome(th, ph)
    return norm((x / R ** 2, y / RY ** 2, z / R ** 2))

def th_max(ph):
    k = (R / RY) * math.tan(math.radians(ph)) * math.tan(ELEV)
    return 180.0 if k >= 1 else math.degrees(math.acos(-k))

PH_CRIT = math.degrees(math.atan(RY / (R * math.tan(ELEV))))   # above this latitude the whole ring faces the camera

def dome_silhouette():
    pts = [proj(dome(th, 0)) for th in range(-90, 91, 2)]
    for th in range(90, 271, 2):
        c = math.cos(math.radians(th))
        ph = math.degrees(math.atan(-(RY / R) * c / math.tan(ELEV))) if c < 0 else 0.0
        pts.append(proj(dome(th, ph)))
    return pts

def clamp_visible(params):
    out = []
    for th, ph in params:
        m = th_max(ph) - 0.4
        out.append((max(-m, min(m, th)), ph))
    return out

def block_params(th_a, th_b, ph_a, ph_b, n=8):
    ct, cp = min(0.2 * (th_b - th_a), 3.5), min(0.24 * (ph_b - ph_a), 3.0)
    def edge(a, b, k):
        return [(lerp(a[0], b[0], i / k), lerp(a[1], b[1], i / k)) for i in range(k + 1)]
    return (edge((th_a + ct, ph_a), (th_b - ct, ph_a), n) + edge((th_b, ph_a + cp), (th_b, ph_b - cp), 3)
            + edge((th_b - ct, ph_b), (th_a + ct, ph_b), n) + edge((th_a, ph_b - cp), (th_a, ph_a + cp), 3))

ROWS = [  # phi from, phi to, blocks around the ring, ring offset (deg)
    (0.0, 15.5, 13, 4.0),
    (15.5, 30.5, 12, 19.0),
    (30.5, 45.0, 11, 1.0),
    (45.0, 58.5, 9, 22.0),
    (58.5, 71.0, 7, 4.0),
    (71.0, 90.0, 1, 0.0),       # crown, under the snow cap
]
GAP = 1.5

def dome_blocks():
    out = []
    for ri, (pa, pb, count, off) in enumerate(ROWS):
        if count == 1:
            continue
        step = 360.0 / count
        g_th = GAP / max(0.3, math.cos(math.radians((pa + pb) / 2)))
        for i in range(count):
            c = ((off + i * step + 180) % 360) - 180
            th_a, th_b = c - step / 2 + g_th / 2, c + step / 2 - g_th / 2
            ph_a, ph_b = pa + GAP / 2, pb - GAP / 2
            if abs(c) - step / 2 > th_max(ph_b):
                continue
            base = clamp_visible(block_params(th_a, th_b, ph_a, ph_b))
            if max(p[0] for p in base) - min(p[0] for p in base) < 3:
                continue
            face = clamp_visible(block_params(th_a, th_b, ph_a + 2.2, ph_b))
            n = dome_normal(c, (pa + pb) / 2)
            hi = clamp_visible([(th_a + (0.2 + 0.05 * k) * (th_b - th_a), ph_b - 1.6) for k in range(6)])
            out.append(dict(base=[proj(dome(*p)) for p in base], face=[proj(dome(*p)) for p in face],
                            tone=tone(n), hi=[proj(dome(*p)) for p in hi], key=(ri, i), c=c,
                            pa=pa, pb=pb, th=(th_a, th_b), center=proj(dome(c, (pa + pb) / 2))))
    return out

def cap_edge(th):
    """Latitude of the snow cap's lower edge: low at the back so the cap reaches the limb there."""
    drip = sum(h * math.exp(-((th - c) / w) ** 2)
               for c, w, h in ((-58, 13, 3.0), (-14, 12, 2.4), (22, 14, 3.4), (62, 13, 2.8)))
    return 62.5 + 11.5 * math.cos(math.radians(th)) + 0.8 * math.sin(math.radians(th * 5.0)) - drip

def snow_cap():
    """Returns (fill polygon, visible edge polyline). The fill closes high above and is clipped by the dome."""
    edge = []
    for th in [x / 2 for x in range(-360, 361)]:
        ph = cap_edge(th)
        if abs(th) <= th_max(ph) - 0.4:
            edge.append(proj(dome(th, ph, 5)))
    fill = edge + [(edge[-1][0], edge[-1][1] - 1200), (edge[0][0], edge[0][1] - 1200)]
    return fill, edge

def ledge(th_a, th_b, ph, seed):
    """A rounded pillow of snow resting on a block's top edge (closed superellipse, no sharp ends)."""
    mid, half = (th_a + th_b) / 2, (th_b - th_a) / 2
    pts = []
    for i in range(48):
        t = 2 * math.pi * i / 48
        c, s_ = math.cos(t), math.sin(t)
        th = mid + half * math.copysign(abs(c) ** 0.55, c)
        if s_ >= 0:
            dph = 2.8 * s_ ** 0.8                                                  # rounded top
        else:
            dph = -(1.5 + 1.1 * (0.5 + 0.5 * math.sin(seed + 3 * t))) * (-s_) ** 0.7   # soft, lumpy underside
        pts.append((th, ph + 0.4 + dph))
    return [proj(dome(*p, 5)) for p in clamp_visible(pts)]

# ---- window -----------------------------------------------------------------------
WINDOW_TARGET = (0.265 * W_CANVAS, (1 - 0.444) * W_CANVAS)   # GameLayout.IglooWindow (light position)

def find_window():
    best = None
    for th10 in range(-900, 0, 2):
        for ph10 in range(50, 500, 2):
            th, ph = th10 / 10, ph10 / 10
            x, y = proj(dome(th, ph))
            d = (x - WINDOW_TARGET[0] + 8) ** 2 + (y - WINDOW_TARGET[1] - 14) ** 2
            if best is None or d < best[0]:
                best = (d, th, ph)
    return best[1], best[2]

def frame_at(th, ph):
    p = dome(th, ph)
    n = dome_normal(th, ph)
    t1 = norm(add(dome(th + 0.5, ph), mul(p, -1)))
    t2 = norm(add(dome(th, ph + 0.5), mul(p, -1)))
    return p, n, t1, t2

WINDOW_R_OUT, WINDOW_R_IN, WINDOW_R_GLASS, WINDOW_DEPTH, WINDOW_LIFT = 104.0, 70.0, 70.0, 18.0, 5.0
# Pane, inner glow and shine colors: lit by the lamp inside (the igloo art), with the lamp off, or the heal glow.
WINDOW_GLASS = {
    True: ("#FFC447", "#FFDD80", "#FFF7DB"),
    False: ("#2C4762", "#375A7C", "#8FB2D0"),
    "heal": ("#5FD9A0", "#A8F2CC", "#F2FFF8"),   # mint glow while a doctor heals inside
}

def window_glass(lit=True):
    """The pane seen through the window hole (draw inside the win-hole clip): pane, inner glow, shines."""
    wp, wn, wt1, wt2 = frame_at(*find_window())
    pane, glow, shine_c = WINDOW_GLASS[lit]
    glass_c = add(wp, mul(wn, -WINDOW_DEPTH))
    glass = [proj(q) for q in ring3(glass_c, wt1, wt2, WINDOW_R_GLASS)]
    el = [f'<path d="{poly_path(glass)}" fill="{pane}" stroke="{INK}" stroke-width="4"/>']
    inner = [proj(q) for q in ring3(add(glass_c, mul(wt1, -8), mul(wt2, -6)), wt1, wt2, 50)]
    el.append(f'<path d="{poly_path(inner)}" fill="{glow}"/>')
    shine = [proj(q) for q in ring3(add(glass_c, mul(wt1, 28), mul(wt2, 30)), wt1, wt2, 13, 36)]
    el.append(f'<path d="{poly_path(shine)}" fill="{shine_c}"/>')
    shine2 = [proj(q) for q in ring3(add(glass_c, mul(wt1, 44), mul(wt2, 6)), wt1, wt2, 6, 24)]
    el.append(f'<path d="{poly_path(shine2)}" fill="{shine_c}"/>')
    return el

def window_hole():
    """The window opening (the clip for window_glass) in canvas pixels."""
    wp, wn, wt1, wt2 = frame_at(*find_window())
    return [proj(q) for q in ring3(add(wp, mul(wn, WINDOW_LIFT)), wt1, wt2, WINDOW_R_IN)]

def window_overlay(lit=False):
    """Full-canvas SVG holding only the window pane, e.g. the lamp-off pane laid over the igloo art."""
    hole = window_hole()
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W_CANVAS}" height="{W_CANVAS}" viewBox="0 0 {W_CANVAS} {W_CANVAS}">\n'
            f'<defs><clipPath id="win-hole"><path d="{poly_path(hole)}"/></clipPath></defs>\n'
            f'<g stroke-linejoin="round" stroke-linecap="round"><g clip-path="url(#win-hole)">\n'
            + "\n".join(window_glass(lit)) + '\n</g></g>\n</svg>\n')

def ring3(c, u, v, r, k=72, a0=0.0, a1=360.0):
    return [add(c, mul(u, r * math.cos(math.radians(lerp(a0, a1, i / k)))), mul(v, r * math.sin(math.radians(lerp(a0, a1, i / k)))))
            for i in range(k + 1)]

# ---- tunnel (U-shaped cross-section) -------------------------------------------
TH_T = math.radians(25)
AX = (math.sin(TH_T), 0.0, math.cos(TH_T))      # axis, out of the dome
RT = (math.cos(TH_T), 0.0, -math.sin(TH_T))     # tunnel right
UP = (0.0, 1.0, 0.0)
D_FRONT = 568.0
OUTER = (250.0, 222.0, 250.0)   # half-width, straight wall height, arch rise
INNER = (112.0, 246.0, 112.0)   # door opening
JAMB = 44.0                     # depth of the opening's side walls
SILL_H, SILL_OUT = 18.0, 28.0   # threshold slab height and how far it sticks out

def profile(u, prof):
    """u: 0 = right foot, 1 = left foot, around the arch. Returns (s, y, ns, ny)."""
    w, hw, rv = prof
    arc = math.pi * math.sqrt((w * w + rv * rv) / 2)
    total = 2 * hw + arc
    t = u * total
    if t <= hw:
        return w, t, 1.0, 0.0
    if t <= hw + arc:
        a = (t - hw) / arc * math.pi
        nx, ny = math.cos(a) / w, math.sin(a) / rv
        l = math.hypot(nx, ny)
        return w * math.cos(a), hw + rv * math.sin(a), nx / l, ny / l
    return -w, hw - (t - hw - arc), -1.0, 0.0

def grow(prof, g): return (prof[0] + g, prof[1], prof[2] + g)

def tun(d, s, y): return add(mul(AX, d), mul(RT, s), mul(UP, y))
def tun_u(d, u, prof):
    s, y, _, _ = profile(u, prof)
    return tun(d, s, y)
def tun_normal(u, prof):
    _, _, ns, ny = profile(u, prof)
    return norm(add(mul(RT, ns), mul(UP, ny)))

def d_exit(s, y):
    q = R * R * (1 - (y / RY) ** 2) - s * s
    return math.sqrt(q) if q > 0 else 0.0
def d_exit_u(u, prof):
    s, y, _, _ = profile(u, prof)
    return d_exit(s, y)

def us(n): return [i / n for i in range(n + 1)]

# Voussoir joints, the same for every profile: walls get horizontal joints at equal heights,
# the arch gets radial joints at equal angles. Each joint maps to a profile-specific u.
JOINTS = [("R", 0.0), ("R", 0.5), ("A", 0.0), ("A", 36.0), ("A", 72.0), ("A", 108.0), ("A", 144.0), ("A", 180.0), ("L", 0.5), ("L", 0.0)]
def joint_u(j, prof):
    kind, v = j
    w, hw, rv = prof
    arc = math.pi * math.sqrt((w * w + rv * rv) / 2)
    total = 2 * hw + arc
    t = v * hw if kind == "R" else hw + v / 180.0 * arc if kind == "A" else hw + arc + (1 - v) * hw
    return t / total
def segment(i, prof): return joint_u(JOINTS[i], prof), joint_u(JOINTS[i + 1], prof)
VOUSSOIRS = len(JOINTS) - 1

# ---- door ------------------------------------------------------------------------
D_DOOR = D_FRONT - 4.0
DOOR = (INNER[0] - 1.5, INNER[1] - 1.5, INNER[2] - 1.5)   # leaf sits just inside the opening

def door_local(s, y):
    """Door leaf point in its own plane (closed position)."""
    return tun(D_DOOR, s, y)

def door_open_point(s, y, beta):
    """Leaf point after opening beta degrees outward about the hinge on its right edge."""
    b = math.radians(beta)
    hs = DOOR[0]
    rel = s - hs                      # <= 0 across the leaf
    return add(mul(AX, D_DOOR - rel * math.sin(b)), mul(RT, hs + rel * math.cos(b)), mul(UP, y))

def door_matrix(beta):
    """2D affine (a, b, c, d, e, f) mapping the closed door's screen image to the door opened by beta."""
    src = [proj(door_local(*q)) for q in ((DOOR[0], 0), (DOOR[0], 300), (-DOOR[0], 0))]
    dst = [proj(door_open_point(*q, beta)) for q in ((DOOR[0], 0), (DOOR[0], 300), (-DOOR[0], 0))]
    (x0, y0), (x1, y1), (x2, y2) = src
    (u0, v0), (u1, v1), (u2, v2) = dst
    det = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
    a = ((u1 - u0) * (y2 - y0) - (u2 - u0) * (y1 - y0)) / det
    c = ((u2 - u0) * (x1 - x0) - (u1 - u0) * (x2 - x0)) / det
    b = ((v1 - v0) * (y2 - y0) - (v2 - v0) * (y1 - y0)) / det
    d = ((v2 - v0) * (x1 - x0) - (v1 - v0) * (x2 - x0)) / det
    e = u0 - a * x0 - c * y0
    f = v0 - b * x0 - d * y0
    return a, b, c, d, e, f

def door_faces_camera(beta):
    b = math.radians(beta)
    n = norm(add(mul(AX, math.cos(b)), mul(RT, math.sin(b))))   # leaf front normal after opening
    return dot(n, VIEW) > 0

def door_leaf(front=True):
    el = []
    top_y = lambda s: DOOR[1] + DOOR[2] * math.sqrt(max(0.0, 1 - (s / DOOR[0]) ** 2))
    rim = [profile(u, DOOR)[:2] for u in us(120)]
    rim = [q for q in rim if q[1] > SILL_H]
    leaf = [proj(door_local(DOOR[0], SILL_H))] + [proj(door_local(*q)) for q in rim] + [proj(door_local(-DOOR[0], SILL_H))]
    wood, dark, light = ("#A8672F", "#5B3214", "#C98A4E") if front else ("#8C5427", "#4A2810", "#A86B36")
    el.append(f'<path d="{poly_path(leaf)}" fill="{wood}" stroke="none"/>')
    el.append(f'<clipPath id="leaf-clip-{int(front)}"><path d="{poly_path(leaf)}"/></clipPath>')
    el.append(f'<g clip-path="url(#leaf-clip-{int(front)})">')
    planks = [-0.6, -0.2, 0.2, 0.6]
    for k in planks:
        s = DOOR[0] * k
        el.append(f'<path d="{line(proj(door_local(s, 0)), proj(door_local(s, top_y(s) + 20)))}" stroke="{dark}" stroke-width="5"/>')
    edges = [-1.0] + planks + [1.0]
    for i in range(len(edges) - 1):
        s = DOOR[0] * (edges[i] + 0.09)
        a = proj(door_local(s, SILL_H + 26))
        b = proj(door_local(s, top_y(s) - 26))
        el.append(f'<path d="{line(a, b)}" stroke="{light}" stroke-width="7" stroke-linecap="round" opacity="0.75"/>')
    for k, y0, y1, ph in ((-0.8, 70, 140, 0.0), (-0.4, 180, 250, 1.3), (0.0, 50, 120, 2.1), (0.4, 140, 215, 0.7), (0.8, 60, 115, 1.9), (0.0, 230, 290, 2.6)):
        g = [proj(door_local(DOOR[0] * k + 5 * math.sin(ph + i * 1.4), lerp(y0, y1, i / 4))) for i in range(5)]
        el.append(f'<path d="{smooth_path(g, closed=False)}" fill="none" stroke="{dark}" stroke-width="3" stroke-linecap="round" opacity="0.55"/>')
    if front:
        # iron straps on the hinge side
        for y in (75.0, 255.0):
            strap = [door_local(DOOR[0] + 2, y - 9), door_local(-DOOR[0] * 0.15, y - 9),
                     door_local(-DOOR[0] * 0.15 - 9, y), door_local(-DOOR[0] * 0.15, y + 9), door_local(DOOR[0] + 2, y + 9)]
            el.append(f'<path d="{poly_path([proj(q) for q in strap])}" fill="#56616C" stroke="{INK}" stroke-width="3.5"/>')
            for sx in (0.75, 0.35, 0.0):
                c = proj(door_local(DOOR[0] * sx, y))
                el.append(f'<circle cx="{c[0]:.1f}" cy="{c[1]:.1f}" r="3.6" fill="#9AA6B2"/>')
    else:
        # back of the leaf: ledge-and-brace
        for y in (70.0, 250.0):
            band = [door_local(DOOR[0] + 2, y - 11), door_local(-DOOR[0] - 2, y - 11), door_local(-DOOR[0] - 2, y + 11), door_local(DOOR[0] + 2, y + 11)]
            el.append(f'<path d="{poly_path([proj(q) for q in band])}" fill="{light}" stroke="{dark}" stroke-width="3.5"/>')
        brace = [door_local(-DOOR[0] + 6, 81), door_local(-DOOR[0] + 22, 81), door_local(DOOR[0] - 6, 239), door_local(DOOR[0] - 22, 239)]
        el.append(f'<path d="{poly_path([proj(q) for q in brace])}" fill="{light}" stroke="{dark}" stroke-width="3.5"/>')
    el.append('</g>')
    el.append(f'<path d="{poly_path(leaf)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    if front:
        hc = door_local(-DOOR[0] * 0.56, 150)
        plate = [proj(q) for q in ring3(add(hc, (0, 18, 0)), RT, UP, 10, 24)]
        ring = [proj(q) for q in ring3(add(hc, (0, -6, 0)), RT, UP, 22, 48)]
        el.append(f'<path d="{poly_path(ring)}" fill="none" stroke="{INK}" stroke-width="12"/>')
        el.append(f'<path d="{poly_path(ring)}" fill="none" stroke="#B4BEC8" stroke-width="5"/>')
        el.append(f'<path d="{poly_path(plate)}" fill="#8E9AA6" stroke="{INK}" stroke-width="3.5"/>')
    return "\n".join(el)

# ---- assembly ---------------------------------------------------------------------
def build(door_beta=0.0):
    el, defs = [], []
    sil = dome_silhouette()
    defs.append(f'<clipPath id="dome-clip"><path d="{poly_path(sil)}"/></clipPath>')

    # window frame (needed early: ledges near it are skipped)
    wth, wph = find_window()
    wp, wn, wt1, wt2 = frame_at(wth, wph)
    wcenter = proj(wp)

    # tunnel outline (needed early: dome ledges under it are skipped)
    tun_front = [proj(tun_u(D_FRONT, u, OUTER)) for u in us(90)]
    tun_back = [proj(tun_u(d_exit_u(u, OUTER), u, OUTER)) for u in reversed(us(90))]
    tun_poly = tun_front + tun_back

    # ---------------- dome
    el.append('<g id="dome">')
    el.append(f'<path d="{poly_path(sil)}" fill="{MORTAR}"/>')
    el.append('<g clip-path="url(#dome-clip)">')
    blocks = dome_blocks()
    for b in blocks:
        el.append(f'<path d="{smooth_path(b["base"])}" fill="{EDGE}" stroke="{SEAM}" stroke-width="{SEAM_W}"/>')
        el.append(f'<path d="{smooth_path(b["face"])}" fill="{b["tone"]}"/>')
        if b["tone"] != TONES[0]:
            el.append(f'<path d="{smooth_path(b["hi"], closed=False)}" fill="none" stroke="#FFFFFF" stroke-width="7" stroke-linecap="round" opacity="0.85"/>')
    for b in blocks:
        if b["key"] not in LEDGES:
            continue
        th_a, th_b = b["th"]
        w = th_b - th_a
        el.append(f'<path d="{smooth_path(ledge(th_a + 0.14 * w, th_b - 0.2 * w, b["pb"] - GAP / 2, b["c"]))}" fill="{SNOW}" stroke="{INK}" stroke-width="4"/>')
    cap_fill, cap_edge_pts = snow_cap()
    el.append(f'<path d="{poly_path(cap_fill)}" fill="{SNOW}"/>')
    el.append(f'<path d="{poly_path(cap_edge_pts, closed=False)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    el.append('</g>')
    el.append('</g>')

    # ---------------- window
    lift = mul(wn, WINDOW_LIFT)
    c_front = add(wp, lift)
    R_OUT, R_IN = WINDOW_R_OUT, WINDOW_R_IN
    ring_o = [proj(q) for q in ring3(c_front, wt1, wt2, R_OUT)]
    ring_i = [proj(q) for q in ring3(c_front, wt1, wt2, R_IN)]
    defs.append(f'<clipPath id="win-ring"><path d="{poly_path(ring_o)} {poly_path(list(reversed(ring_i)))}" clip-rule="evenodd"/></clipPath>')
    defs.append(f'<clipPath id="win-hole"><path d="{poly_path(ring_i)}"/></clipPath>')
    el.append('<g id="window">')
    # eight ring stones, each toned by the direction its rounded face points
    def rpt(a, r):
        a = math.radians(a)
        return proj(add(c_front, mul(wt1, r * math.cos(a)), mul(wt2, r * math.sin(a))))
    for k in range(8):
        a0, a1 = 15 + 45 * k, 60 + 45 * k
        am = math.radians((a0 + a1) / 2)
        outward = norm(add(mul(wn, 0.8), mul(wt1, math.cos(am)), mul(wt2, math.sin(am))))
        stone = [rpt(lerp(a0, a1, j / 12), R_OUT) for j in range(13)] + [rpt(lerp(a1, a0, j / 12), R_IN) for j in range(13)]
        el.append(f'<path d="{poly_path(stone)}" fill="{tone(outward)}"/>')
    for k in range(8):
        a = 15 + 45 * k
        el.append(f'<path d="{line(rpt(a, R_IN), rpt(a, R_OUT))}" stroke="{SEAM}" stroke-width="{SEAM_W}"/>')
    el.append('<g clip-path="url(#win-hole)">')
    el.append(f'<path d="{poly_path(ring_i)}" fill="#6E9CC2"/>')                 # recess wall
    el.extend(window_glass(lit=True))
    el.append('</g>')
    el.append(f'<path d="{poly_path(ring_i)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    el.append(f'<path d="{poly_path(ring_o)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    # snow sitting on the ring: centred on screen-up, thins to nothing at both ends
    def wpt(a, r, out=0.0):
        a = math.radians(a)
        return proj(add(c_front, mul(wn, out), mul(wt1, r * math.cos(a)), mul(wt2, r * math.sin(a))))
    up_a = min(range(0, 360, 1), key=lambda a: wpt(a, 100)[1])
    span = 64
    top = [wpt(up_a - span + 2 * span * i / 30, R_OUT + 12 * math.sin(math.pi * i / 30) ** 0.7, 4) for i in range(31)]
    bot = []
    for i in range(31):
        s = i / 30
        dip = math.sin(math.pi * s) ** 0.8 * (20 + 7 * math.sin(s * 15.0))
        bot.append(wpt(up_a + span - 2 * span * s, R_OUT - dip, 4))
    el.append(f'<path d="{smooth_path(top + bot)}" fill="{SNOW}" stroke="{INK}" stroke-width="4"/>')
    el.append('</g>')

    # ---------------- snow heaped against the dome's foot (bottom edge is the dome's own base)
    for t0, t1, hgt in ((-74, -50, 6.0), (-40, -17, 6.0)):
        top, bot = [], []
        for i in range(31):
            s_ = i / 30
            th = lerp(t0, t1, s_)
            sh = math.sin(math.pi * s_) ** 0.6 * (0.88 + 0.12 * math.sin(s_ * 11 + t0))
            top.append(proj(dome(th, hgt * sh, 4)))
            bot.append(proj(dome(th, 0.0, 4)))
        el.append(f'<path d="{smooth_path(top + list(reversed(bot)))}" fill="{SNOW}"/>')
        el.append(f'<path d="{smooth_path(top, closed=False)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')

    el.append(f'<path d="{poly_path(sil)}" fill="none" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')

    # ---------------- tunnel
    el.append('<g id="tunnel">')
    el.append(f'<path d="{poly_path(tun_poly)}" fill="{MORTAR}"/>')
    # vault blocks: one row per voussoir, split along the axis where the vault is long
    for i in range(VOUSSOIRS):
        u0, u1 = segment(i, OUTER)
        um = (u0 + u1) / 2
        nrm = tun_normal(um, OUTER)
        if dot(nrm, VIEW) < -0.05:
            continue
        # straight joint across the vault at a fixed depth where the vault is long enough
        D_CUT = D_FRONT - 100
        u_samples = [lerp(u0, u1, j / 8) for j in range(9)]
        rings = [lambda u: d_exit_u(u, OUTER), lambda u: max(D_CUT, d_exit_u(u, OUTER)), lambda u: D_FRONT]
        for r0, r1 in (rings[:2], rings[1:]):
            near = [tun_u(r0(u), u, OUTER) for u in u_samples]
            far = [tun_u(r1(u), u, OUTER) for u in reversed(u_samples)]
            if max(r1(u) - r0(u) for u in u_samples) < 2:
                continue
            el.append(f'<path d="{poly_path([proj(q) for q in near + far])}" fill="{tone(nrm)}" stroke="{SEAM}" stroke-width="{SEAM_W}" stroke-linejoin="round"/>')
    # opening: interior, the visible (right) jamb wall, threshold
    opening = [proj(tun_u(D_FRONT, u, INNER)) for u in us(90)]
    defs.append(f'<clipPath id="opening"><path d="{poly_path(opening)}"/></clipPath>')
    el.append('<g clip-path="url(#opening)">')
    el.append(f'<path d="{poly_path(opening)}" fill="#24394F"/>')
    back = [proj(tun_u(D_FRONT - JAMB, u, INNER)) for u in us(90)]
    el.append(f'<path d="{poly_path(back)}" fill="#2E4A66"/>')
    floor = [proj(tun(D_FRONT - JAMB - 160, -INNER[0], 0)), proj(tun(D_FRONT - JAMB - 160, INNER[0], 0)),
             proj(tun(D_FRONT, INNER[0], 0)), proj(tun(D_FRONT, -INNER[0], 0))]
    el.append(f'<path d="{poly_path(floor)}" fill="#3B5E80"/>')
    for i in range(VOUSSOIRS):
        u0, u1 = segment(i, INNER)
        nrm = mul(tun_normal((u0 + u1) / 2, INNER), -1)
        if dot(nrm, VIEW) <= 0.01:
            continue
        pts = [tun_u(D_FRONT, lerp(u0, u1, j / 6), INNER) for j in range(7)]
        pts += [tun_u(D_FRONT - JAMB, lerp(u1, u0, j / 6), INNER) for j in range(7)]
        el.append(f'<path d="{poly_path([proj(q) for q in pts])}" fill="{TONES[0]}" stroke="{SEAM}" stroke-width="{SEAM_W}" stroke-linejoin="round"/>')
    sill_top = [tun(D_FRONT - JAMB, -INNER[0], SILL_H), tun(D_FRONT - JAMB, INNER[0], SILL_H),
                tun(D_FRONT, INNER[0], SILL_H), tun(D_FRONT, -INNER[0], SILL_H)]
    el.append(f'<path d="{poly_path([proj(q) for q in sill_top])}" fill="{TONES[2]}" stroke="{SEAM}" stroke-width="{SEAM_W}"/>')
    el.append('</g>')
    # front face: voussoirs filled edge to edge, joints stroked on top
    face_t = tone(AX)
    for i in range(VOUSSOIRS):
        o0, o1 = segment(i, OUTER)
        i0, i1 = segment(i, INNER)
        outer = [tun_u(D_FRONT, lerp(o0, o1, j / 12), OUTER) for j in range(13)]
        inner = [tun_u(D_FRONT, lerp(i1, i0, j / 12), INNER) for j in range(13)]
        el.append(f'<path d="{poly_path([proj(q) for q in outer + inner])}" fill="{face_t}"/>')
        # band along the opening side reads as the blocks' depth
        g0, g1 = segment(i, grow(INNER, 13))
        inner2 = [tun_u(D_FRONT, lerp(g0, g1, j / 12), grow(INNER, 13)) for j in range(13)]
        el.append(f'<path d="{poly_path([proj(q) for q in list(reversed(inner)) + list(reversed(inner2))])}" fill="{EDGE}"/>')
        if JOINTS[i][0] == "A" or JOINTS[i + 1][0] == "A":
            hl = [proj(tun_u(D_FRONT, lerp(*segment(i, grow(OUTER, -18)), 0.2 + 0.55 * j / 6), grow(OUTER, -18))) for j in range(7)]
            el.append(f'<path d="{smooth_path(hl, closed=False)}" fill="none" stroke="#FFFFFF" stroke-width="7" stroke-linecap="round" opacity="0.85"/>')
    for j in JOINTS[1:-1]:
        el.append(f'<path d="{line(proj(tun_u(D_FRONT, joint_u(j, INNER), INNER)), proj(tun_u(D_FRONT, joint_u(j, OUTER), OUTER)))}" stroke="{SEAM}" stroke-width="{SEAM_W + 0.5}"/>')
    el.append(f'<path d="{poly_path(opening)}" fill="none" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    # outline only where it is really seen: the face's outer edge, and the dome joint where the vault faces the camera
    el.append(f'<path d="{poly_path(tun_front, closed=False)}" fill="none" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')
    seen = [u for u in us(90) if dot(tun_normal(u, OUTER), VIEW) > 0]
    u_first = min(seen)
    joint_line = [proj(tun_u(d_exit_u(u, OUTER), u, OUTER)) for u in seen]
    el.append(f'<path d="{poly_path(joint_line, closed=False)}" fill="none" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')
    # where the vault turns away, its limb runs from the face edge back to the joint
    el.append(f'<path d="{line(proj(tun_u(D_FRONT, u_first, OUTER)), proj(tun_u(d_exit_u(u_first, OUTER), u_first, OUTER)))}" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')
    el.append(f'<path d="{line(proj(tun_u(D_FRONT, 1.0, OUTER)), proj(tun_u(d_exit_u(1.0, OUTER), 1.0, OUTER)))}" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')
    # ground edges of the face on both sides of the opening
    for a, b in (((OUTER[0], 0), (INNER[0], 0)), ((-INNER[0], 0), (-OUTER[0], 0))):
        el.append(f'<path d="{line(proj(tun(D_FRONT, *a)), proj(tun(D_FRONT, *b)))}" stroke="{INK}" stroke-width="{OUTLINE_W}"/>')
    # threshold lip in front of the face
    lip_top = [tun(D_FRONT, -INNER[0], SILL_H), tun(D_FRONT, INNER[0], SILL_H),
               tun(D_FRONT + SILL_OUT, INNER[0], SILL_H), tun(D_FRONT + SILL_OUT, -INNER[0], SILL_H)]
    lip_front = [tun(D_FRONT + SILL_OUT, -INNER[0], SILL_H), tun(D_FRONT + SILL_OUT, INNER[0], SILL_H),
                 tun(D_FRONT + SILL_OUT, INNER[0], 0), tun(D_FRONT + SILL_OUT, -INNER[0], 0)]
    el.append(f'<path d="{poly_path([proj(q) for q in lip_front])}" fill="{TONES[0]}" stroke="{INK}" stroke-width="4"/>')
    el.append(f'<path d="{poly_path([proj(q) for q in lip_top])}" fill="{TONES[2]}" stroke="{INK}" stroke-width="4"/>')
    # snow blanket on the vault, from the dome joint to a front edge that drapes over the face
    S_OUT = grow(OUTER, 10)
    def u_l(d): return 0.665 + 0.025 * math.sin(d * 0.04 + 1.0)
    def u_r(d): return 0.40 + 0.02 * math.sin(d * 0.05)
    df = D_FRONT + 8
    joint = lambda u: d_exit_u(u, S_OUT) - 6
    left = [tun_u(lerp(joint(u_l(df)), df, j / 10), u_l(df), S_OUT) for j in range(11)]
    front = []
    for j in range(1, 40):
        u = lerp(u_l(df), u_r(df), j / 40)
        s, y, ns, ny = profile(u, S_OUT)
        drip = 12 + sum(h * math.exp(-((u - c) / 0.026) ** 2) for c, h in ((0.615, 26), (0.555, 40), (0.49, 26), (0.43, 34)))
        front.append(tun(df, s - ns * drip, y - ny * drip))
    right = [tun_u(lerp(df, joint(u_r(df)), j / 10), u_r(df), S_OUT) for j in range(11)]
    backe = [tun_u(joint(u), u, S_OUT) for u in [lerp(u_r(df), u_l(df), j / 20) for j in range(1, 20)]]
    el.append(f'<path d="{smooth_path([proj(q) for q in left + front + right + backe])}" fill="{SNOW}" stroke="{INK}" stroke-width="{DETAIL_W}"/>')
    el.append('</g>')

    # ---------------- door (separate, hinged on its right edge, opens outward)
    m = door_matrix(door_beta)
    side = door_faces_camera(door_beta)
    el.append(f'<g id="door" transform="matrix({m[0]:.5f} {m[1]:.5f} {m[2]:.5f} {m[3]:.5f} {m[4]:.2f} {m[5]:.2f})">')
    el.append(door_leaf(front=side))
    el.append('</g>')

    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W_CANVAS}" height="{W_CANVAS}" viewBox="0 0 {W_CANVAS} {W_CANVAS}">\n'
            f'<!-- Penguise igloo: cartoon style, generated from a projected 3D block model. Door: hinged on its right edge, opens outward. -->\n'
            f'<defs>{"".join(defs)}</defs>\n<g stroke-linejoin="round" stroke-linecap="round">\n' + "\n".join(el) + '\n</g>\n</svg>\n')

LEDGES = {(3, 8), (3, 0), (0, 12), (2, 10)}

if __name__ == "__main__":
    beta = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0
    open(sys.argv[1], "w").write(build(beta))
