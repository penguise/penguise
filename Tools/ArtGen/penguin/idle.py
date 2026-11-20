"""Penguin idle animation, drawn from code with the same rig as the run (run.py).

Design: every clip is the shared breathing loop plus an optional "fidget" offset that is zero (with zero
velocity) at both ends, and every clip lasts a whole number of breaths. So clips can be chained in any
order without a pop: at each loop end the game picks the next clip at random, per penguin, with a random
start phase and a slightly different speed, so no two penguins move in sync.

Clips (B = one breath):
  breathe  1B  plain breathing: chest rises, head follows a little late, flippers lift slightly
  blink    1B  breathing + one blink (sometimes a double blink)
  flap     1B  flippers flutter out to warm up, small bounce
  stomp    1B  cold feet: a shiver, then each foot lifts and stamps, weight shifting over the other
  look     2B  looks up at the sky, then down at its feet, blinking as the head moves
"""
import math, os, sys, re, subprocess
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import penguin, outfit
import run as R
from PIL import Image

BREATH = 2.4            # s, one breath
FPS = 15
FRAMES_PER_BREATH = round(BREATH * FPS)

HIP, TOP = R.HIP, R.TOP
WHITE = "#FFFFFF"
OUTFIT_W = outfit.parts(WHITE)


def smooth(x): return R.smootherstep(R.clamp01(x))


def bump(t, a, b):
    """0 outside [a, b], smooth 0 -> 1 -> 0 inside (zero slope at the ends)."""
    if t <= a or t >= b:
        return 0.0
    return math.sin(math.pi * (t - a) / (b - a)) ** 2


def envelope(t, a, b, ramp):
    """1 inside [a + ramp, b - ramp], smooth ramps at both ends, 0 outside."""
    return smooth((t - a) / ramp) * smooth((b - t) / ramp)


def keys(t, pts):
    """Smooth (smootherstep) interpolation through (time, value) keys, held outside."""
    if t <= pts[0][0]:
        return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * smooth((t - t0) / (t1 - t0))
    return pts[-1][1]


def blink_at(t, start):
    """Eyelid closure 0..1: close in 60 ms, hold 40 ms, open in 110 ms."""
    u = t - start
    if u < 0 or u > 0.21:
        return 0.0
    if u < 0.06:
        return smooth(u / 0.06)
    if u < 0.10:
        return 1.0
    return 1.0 - smooth((u - 0.10) / 0.11)


class Channels:
    def __init__(self):
        self.lean = 0.0       # deg, whole body about the hip (+ forward)
        self.nod = 0.0        # deg, extra at the head (+ down)
        self.head_dx = 0.0    # px at the top of the head
        self.head_dy = 0.0    # px at the top of the head (+ down)
        self.stretch = 0.0    # vertical stretch (+ taller)
        self.root_y = 0.0     # px (+ down)
        self.wing = 0.0       # deg, both flippers outward
        self.blink = 0.0      # 0 open, 1 closed
        self.feet = {"foot-back": (0.0, 0.0, 0.0), "foot-front": (0.0, 0.0, 0.0)}  # dx, dy, toe angle


def breathe(t, c):
    phase = 2 * math.pi * t / BREATH
    c.stretch += 0.013 * math.sin(phase)
    c.head_dy += 2.5 * math.sin(phase - 0.9)            # the head follows the chest a little late
    c.nod += 0.8 * math.sin(phase - 1.2)
    c.wing += 2.2 * math.sin(phase - 0.5)


def fidget_blink(t, c, double=False):
    c.blink = max(blink_at(t, 1.1), blink_at(t, 1.42) if double else 0.0)


def fidget_flap(t, c):
    env = envelope(t, 0.25, 1.75, 0.3)
    flap = math.sin(2 * math.pi * 3.0 * (t - 0.25))
    c.wing += env * (20 + 14 * flap)
    c.root_y += -env * 3 * max(0.0, flap)                 # each downstroke lifts it a touch
    c.stretch += env * 0.005 * flap
    c.blink = max(c.blink, blink_at(t, 0.2))


def fidget_stomp(t, c):
    shiver = envelope(t, 0.0, 0.5, 0.12)
    c.lean += shiver * 1.6 * math.sin(2 * math.pi * 11 * t)
    c.head_dx += shiver * 2.0 * math.sin(2 * math.pi * 11 * t + 1.0)
    for name, a, b, side in (("foot-front", 0.6, 1.05, -1), ("foot-back", 1.2, 1.65, 1)):
        k = bump(t, a, b)
        lift = math.sin(math.pi * R.clamp01((t - a) / (b - a)))
        c.feet[name] = (0.0, -26 * lift ** 1.4, -10 * lift)
        c.lean += side * 2.4 * k                          # weight over the planted foot
        c.root_y += -3 * k
    # a little settle after the stamps
    c.stretch += -0.006 * bump(t, 1.65, 2.1)


def fidget_look(t, c):
    c.nod += keys(t, [(0.3, 0.0), (0.9, -10.0), (1.9, -10.0), (2.6, 9.0), (3.6, 9.0), (4.3, 0.0)])
    c.lean += keys(t, [(0.3, 0.0), (0.9, -2.0), (1.9, -2.0), (2.6, 1.5), (3.6, 1.5), (4.3, 0.0)])
    c.head_dx += keys(t, [(0.3, 0.0), (0.9, -6.0), (1.9, -6.0), (2.6, 8.0), (3.6, 8.0), (4.3, 0.0)])
    c.blink = max(blink_at(t, 0.35), blink_at(t, 2.0), blink_at(t, 3.75))


CLIPS = {
    "breathe": (1, lambda t, c: None),
    "blink": (1, lambda t, c: fidget_blink(t, c)),
    "blink2": (1, lambda t, c: fidget_blink(t, c, True)),
    "flap": (1, fidget_flap),
    "stomp": (1, fidget_stomp),
    "look": (2, fidget_look),
}


def channels(clip, t):
    c = Channels()
    breathe(t, c)
    CLIPS[clip][1](t, c)
    return c


class IdlePose:
    """Same interface as the run's Pose, so the outfit skinning (R.skinned) works unchanged."""

    def __init__(self, c, wing_vel):
        self.c = c
        self.sy = 1 + c.stretch
        self.sx = self.sy ** -0.6
        self.root_y = c.root_y
        self.wing_vel = wing_vel

    def height(self, y):
        return max(0.0, (HIP[1] - y) / (HIP[1] - TOP))

    def angle(self, h):
        return self.c.lean * (0.3 + 0.7 * h) + self.c.nod * h * h

    def body(self, x, y):
        hx, hy = HIP
        h = self.height(y)
        x1 = hx + (x - hx) * self.sx + self.c.head_dx * h ** 2
        y1 = hy + (y - hy) * self.sy + self.c.head_dy * h ** 1.5
        x1, y1 = R.rot(x1, y1, hx, hy, self.angle(h))
        return x1, y1 + self.root_y

    def wing_fn(self, name):
        sx, sy = R.SHOULDER[name]
        px, py = self.body(sx, sy)
        sgn = 1 if name == "wing-near" else -1
        base = self.angle(self.height(sy)) + sgn * self.c.wing
        flex = -sgn * self.wing_vel * R.WING_FLEX

        def f(x, y):
            d = math.hypot(x - sx, y - sy) / R.WING_LEN
            nx, ny = R.rot(x, y, sx, sy, base + flex * d * d)
            return nx - sx + px, ny - sy + py
        return f


def pose(clip, t):
    dt = 1 / 240
    wing_vel = (channels(clip, t + dt).wing - channels(clip, t - dt).wing) / (2 * dt)
    return IdlePose(channels(clip, t), wing_vel)


# The run's cloth skinning moves the sweater near the armhole seam with the near flipper. Raised far out
# (flap), that drags the sweater's top edge down from under the collar and bares the body there, so near
# the collar the sweater stays on the torso: the flipper's pull fades in from SKIN_TOP down to SKIN_FULL.
SKIN_TOP, SKIN_FULL = 455.0, 540.0


def skinned(p):
    wing = p.wing_fn("wing-near")

    def f(x, y):
        bx, by = p.body(x, y)
        w = R.near_weight(x, y) * R.smoothstep(R.clamp01((y - SKIN_TOP) / (SKIN_FULL - SKIN_TOP)))
        if w <= 0.0:
            return bx, by
        wx, wy = wing(x, y)
        return bx + (wx - bx) * w, by + (wy - by) * w
    return f


def clip(cid, d, inner):
    return f'<clipPath id="{cid}"><path d="{d}"/></clipPath><g clip-path="url(#{cid})">{inner}</g>'


def layers(p):
    """Body layer (untinted) and white outfit layer, same structure as the game export."""
    c = p.c
    base = []
    for name in ("foot-back", "foot-front"):
        dx, dy, toe = c.feet[name]
        (ax, ay) = R.ANKLE[name]
        base.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({toe:.2f} {ax} {ay})">{R.PART[name]}</g>')
    far, near = p.wing_fn("wing-far"), p.wing_fn("wing-near")
    ang = lambda y: p.angle(p.height(y))
    base.append(clip("fw", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    base.append(R.deform_group(R.PART["body"], p.body))
    base.append(R.deform_group(R.PART["belly"], p.body))
    base.append(R.deform_group(R.PART["wing-near"], near))
    eye_sy = p.sy * (1 - 0.88 * c.blink)
    base.append(R.deform_group(R.PART["eyes"], p.body, ang, p.sx, eye_sy))
    base.append(R.deform_group(R.PART["beak"], p.body))
    cloth = [R.deform_group(OUTFIT_W["sleeve-far"], far),
             R.deform_group(OUTFIT_W["sweater"], skinned(p)),
             clip("nw", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)),
             R.deform_group(OUTFIT_W["sleeve-near"], near),
             R.deform_group(OUTFIT_W["collar"], p.body),
             R.deform_group(OUTFIT_W["hat"], p.body)]
    return "\n".join(base), "\n".join(cloth)


def render(inner, path, size):
    svg = path[:-4] + ".svg"
    open(svg, "w").write(penguin.wrap(inner))
    subprocess.run(["magick", "-background", "none", svg, "-resize", f"{size}x{size}", path], check=True)


def clip_frames(name):
    n = CLIPS[name][0] * FRAMES_PER_BREATH
    return [pose(name, BREATH * CLIPS[name][0] * k / n) for k in range(n)]


def tint(body, cloth, color):
    r, g, b = int(color[1:3], 16), int(color[3:5], 16), int(color[5:7], 16)
    px = cloth.split()
    tinted = Image.merge("RGBA", (px[0].point(lambda v: v * r // 255), px[1].point(lambda v: v * g // 255),
                                  px[2].point(lambda v: v * b // 255), px[3]))
    out = body.copy()
    out.alpha_composite(tinted)
    return out


def render_frames(out, size=256):
    """Renders every clip's body and outfit frames to <out>/tmp/<clip>_<frame>_b.png / _c.png."""
    tmp = os.path.join(out, "tmp")
    os.makedirs(tmp, exist_ok=True)
    jobs = []
    for name in CLIPS:
        for k, p in enumerate(clip_frames(name)):
            b, c = layers(p)
            jobs.append((b, os.path.join(tmp, f"{name}_{k:03d}_b.png")))
            jobs.append((c, os.path.join(tmp, f"{name}_{k:03d}_c.png")))
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda j: render(j[0], j[1], size), jobs))
    print("clips", {n: CLIPS[n][0] * FRAMES_PER_BREATH for n in CLIPS}, "fps", FPS, "breath", BREATH)


if __name__ == "__main__":
    render_frames(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 256)
