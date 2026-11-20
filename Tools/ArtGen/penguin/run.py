"""Penguin run cycle, drawn entirely from code.

Layers of motion:
  1. kinematics  - foot paths, stance compression + ballistic flight of the root, waddle roll
  2. springs     - head lag, landing pitch, wing inertia and flexible wing tips (simulated, then
                   the steady-state cycle is sampled so the loop is seamless)
  3. deformation - every path control point of the upper body goes through a bend / squash /
                   lag field, so the body flexes instead of moving as one rigid sprite
  4. effects     - snow puffs at each foot strike, shadow
"""
import math, re, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import penguin
import outfit
OUTFIT_COLOR = os.environ.get('OUTFIT', '#E53935')
OUTFIT = outfit.parts(OUTFIT_COLOR)
import itertools
CLIP_IDS = itertools.count()

# Cloth skinning at the near armhole: the sleeve follows the flipper; the sweater body follows it on the
# seam and hands over to the torso within SEAM_BLEND px on either side, so both share one seam line
# while the sweater's back edge stays on the body and keeps the back covered.
SEAM = [(470, 428), (522, 428), (570, 420), (605, 405), (641, 384)]   # (y, x), top to armpit
SEAM_BLEND = 70.0
SEAM_FADE = 50.0

def seam_x(y):
    if y <= SEAM[0][0]:
        return SEAM[0][1]
    for (y0, x0), (y1, x1) in zip(SEAM, SEAM[1:]):
        if y <= y1:
            return x0 + (x1 - x0) * (y - y0) / (y1 - y0)
    return SEAM[-1][1]

def near_weight(x, y):
    d = abs(x - seam_x(y))
    w = 1.0 - smoothstep(clamp01(d / SEAM_BLEND))
    if y > SEAM[-1][0]:
        w *= 1.0 - smoothstep(clamp01((y - SEAM[-1][0]) / SEAM_FADE))
    return w

def skinned(pose):
    wing = pose.wing_fn("wing-near")
    def f(x, y):
        w = near_weight(x, y)
        bx, by = pose.body(x, y)
        if w <= 0.0:
            return bx, by
        wx, wy = wing(x, y)
        return bx + (wx - bx) * w, by + (wy - by) * w
    return f

PART = {m.group(1): m.group(0) for m in re.finditer(r'<g id="([\w-]+)".*?</g>', penguin.PARTS_SVG, re.S)}
# wing-near holds several paths and the closing tag search above is non-greedy: re-grab multi-path groups fully
for gid in ("wing-near", "wing-far", "eyes", "beak", "belly", "foot-back", "foot-front", "body"):
    m = re.search(r'<g id="%s"[^>]*>(.*?)</g>' % gid, penguin.PARTS_SVG, re.S)
    PART[gid] = m.group(0)

# ---- tunables ---------------------------------------------------------------
CYCLE = 0.38            # s, two steps
STANCE = 0.275          # share of the cycle one foot is planted (<0.5 -> flight phase)
STRIDE = 36             # px foot travel from rest, each way
LIFT = 46               # px swing height
TOE_UP = 18             # deg toe-up in mid swing (about the ankle)
PUSH = 16               # deg heel lift at push-off (about the ball of the foot)
DIP = 18                # px root compression at mid-stance (flight height follows for continuity)
SQUASH = 0.035          # vertical squash at mid-stance
STRETCH = 0.02          # vertical stretch in flight
LEAN = 9                # deg forward lean
ROLL = 4                # deg waddle toward the planted foot
SPREAD = 14             # deg flippers held out for balance
WING_SWAY = 1.6         # flipper response to roll (counter-balance)
GROUND = 1000

HIP = (530.0, 905.0)
TOP = 72.0
ANKLE = {"foot-back": (408, 935), "foot-front": (640, 925)}
BALL = {"foot-back": (478, 985), "foot-front": (728, 972)}
SHOULDER = {"wing-near": (345.0, 425.0), "wing-far": (penguin.to_far((345, 425))[0], 410.0)}
WING_LEN = 360.0
WING_FOLLOW = 0.35                 # share of the body lean the flippers inherit (held back while running)

# springs: (frequency Hz, damping ratio, forcing gain)
HEAD_SPRING = (6.0, 0.35, 0.45)     # px head lag per unit root acceleration (inertial)
PITCH_SPRING = (4.5, 0.35, 1.0)    # deg forward pitch kicked at each landing
PITCH_KICK = 180.0                 # deg/s impulse per landing
WING_SPRING = (5.0, 0.38, 0.2)   # deg per px/s^2 of root acceleration
WING_FLEX = 0.035                  # deg of tip bend per deg/s of wing angular velocity

STANCE_HALF = STANCE / 0.5                                   # stance share of a half cycle
FLIGHT_H = DIP * math.pi * (1 - STANCE_HALF) / (4 * STANCE_HALF)  # velocity-continuous flight height
SPEED = 2 * STRIDE / (STANCE * CYCLE)                        # ground speed, px/s

def smoothstep(x): return x * x * (3 - 2 * x)
def smootherstep(x): return x * x * x * (x * (6 * x - 15) + 10)
def clamp01(x): return max(0.0, min(1.0, x))

# ---- 1. kinematics ----------------------------------------------------------
def root(t):
    """Vertical root offset (+ = down) and vertical squash factor."""
    u = (t % (CYCLE / 2)) / (CYCLE / 2)
    if u < STANCE_HALF:
        k = math.sin(math.pi * u / STANCE_HALF)
        return DIP * k, 1 - SQUASH * k
    v = (u - STANCE_HALF) / (1 - STANCE_HALF)
    return -FLIGHT_H * 4 * v * (1 - v), 1 + STRETCH * math.sin(math.pi * v)

def roll(t):
    p = (t % CYCLE) / CYCLE
    peak = STANCE / 2                       # lean hardest at mid-stance of the back foot
    return -ROLL * math.sin(2 * math.pi * (p - peak) + math.pi / 2)

def foot(q):
    """q: foot phase, contact at 0. Returns dx, dy, ankle rotation, ball rotation."""
    if q < STANCE:
        s = q / STANCE
        heel = smoothstep(clamp01((s - 0.6) / 0.4))
        return STRIDE - 2 * STRIDE * s, 0.0, 0.0, PUSH * heel
    s = (q - STANCE) / (1 - STANCE)
    lift = math.sin(math.pi * s ** 0.75)                      # rises fast, lands gently
    kick = -10 * math.sin(math.pi * clamp01(s / 0.45))        # heel kicks back right after toe-off
    x = -STRIDE + 2 * STRIDE * smootherstep(s) + kick
    heel = PUSH * (1 - smoothstep(clamp01(s / 0.35)))
    return x, -LIFT * lift, -TOE_UP * math.sin(math.pi * s), heel

def contacts_before(t, span):
    """Foot strikes (time, foot) within (t - span, t]."""
    out = []
    k = math.floor((t - span) / (CYCLE / 2))
    while k * CYCLE / 2 <= t:
        tc = k * CYCLE / 2
        if tc > t - span:
            out.append((tc, "foot-back" if k % 2 == 0 else "foot-front"))
        k += 1
    return out

# ---- 2. springs (simulated to steady state) -------------------------------
def simulate(samples, cycles=12, dt=1 / 4000):
    def accel_root(t, h=1e-4):
        return (root(t + h)[0] - 2 * root(t)[0] + root(t - h)[0]) / (h * h)
    head = [0.0, 0.0]; pitch = [0.0, 0.0]
    wing = {"wing-near": [SPREAD, 0.0], "wing-far": [SPREAD, 0.0]}
    want = [cycles * CYCLE + i * CYCLE / samples for i in range(samples)]
    out = []; t = 0.0; wi = 0; last_contact = -1
    def step(state, f, z, force, target=0.0):
        w = 2 * math.pi * f
        a = -w * w * (state[0] - target) - 2 * z * w * state[1] + force
        state[1] += a * dt; state[0] += state[1] * dt
    while wi < len(want):
        ay = max(-6e4, min(6e4, accel_root(t)))
        f, z, g = HEAD_SPRING; step(head, f, z, -ay * g)
        kc = math.floor(t / (CYCLE / 2))
        if kc != last_contact:
            pitch[1] += PITCH_KICK; last_contact = kc
        f, z, g = PITCH_SPRING; step(pitch, f, z, 0.0)
        r = roll(t)
        for name, sgn in (("wing-near", 1), ("wing-far", -1)):
            f, z, g = WING_SPRING
            step(wing[name], f, z, ay * g, SPREAD + sgn * WING_SWAY * r)
        if t >= want[wi]:
            out.append(dict(t=t, head=head[0], pitch=pitch[0],
                            wing={k: tuple(v) for k, v in wing.items()}))
            wi += 1
        t += dt
    return out

# ---- 3. deformation ---------------------------------------------------------
def rot(x, y, cx, cy, deg):
    a = math.radians(deg); c, s = math.cos(a), math.sin(a)
    return cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c

class Pose:
    def __init__(self, t, spring):
        self.t = t
        self.root_y, self.sy = root(t)
        self.sx = self.sy ** -0.6
        self.roll = roll(t)
        self.head = spring["head"]
        self.pitch = spring["pitch"]
        self.wing = spring["wing"]

    def height(self, y):
        return max(0.0, (HIP[1] - y) / (HIP[1] - TOP))

    def angle(self, h):
        return self.roll + LEAN * (0.45 + 0.55 * h) + self.pitch * h * h

    def body(self, x, y):
        hx, hy = HIP
        h = self.height(y)
        x1 = hx + (x - hx) * self.sx
        y1 = hy + (y - hy) * self.sy + self.head * h ** 1.5
        x1, y1 = rot(x1, y1, hx, hy, self.angle(h))
        return x1, y1 + self.root_y

    def wing_fn(self, name):
        sx, sy = SHOULDER[name]
        px, py = self.body(sx, sy)
        sgn = 1 if name == "wing-near" else -1
        ang, vel = self.wing[name]
        base = WING_FOLLOW * self.angle(self.height(sy)) + sgn * ang
        flex = -sgn * vel * WING_FLEX
        def f(x, y):
            d = math.hypot(x - sx, y - sy) / WING_LEN
            a = base + flex * d * d
            nx, ny = rot(x, y, sx, sy, a)
            return nx - sx + px, ny - sy + py
        return f

NUM = re.compile(r'[A-Za-z]|-?\d*\.?\d+(?:e-?\d+)?')
def deform_d(d, fn):
    toks = NUM.findall(d); out = []; nums = []
    for tk in toks:
        if tk.isalpha():
            out.append(tk)
        else:
            nums.append(float(tk))
            if len(nums) == 2:
                x, y = fn(*nums); out.append(f"{x:.1f} {y:.1f}"); nums = []
    return " ".join(out)

def deform_group(svg, fn, angle_fn=None, sx=1.0, sy=1.0):
    svg = re.sub(r'd="([^"]+)"', lambda m: f'd="{deform_d(m.group(1), fn)}"', svg)
    def ell(m):
        a = dict(re.findall(r'(\w+)="([^"]+)"', m.group(0)))
        cx, cy = float(a["cx"]), float(a["cy"])
        nx, ny = fn(cx, cy)
        ang = angle_fn(cy) if angle_fn else 0.0
        return (f'<ellipse cx="{nx:.1f}" cy="{ny:.1f}" rx="{float(a["rx"]) * sx:.1f}" ry="{float(a["ry"]) * sy:.1f}" '
                f'fill="{a["fill"]}" transform="rotate({ang:.2f} {nx:.1f} {ny:.1f})"/>')
    return re.sub(r'<ellipse[^>]*/>', ell, svg)

# ---- 4. frame ---------------------------------------------------------------
PUFF_LIFE = 0.32
PUFFS = [(-14, -4, -260, -330, 14), (6, -2, -150, -420, 18), (22, -6, -330, -230, 12),
         (-30, -2, -420, -160, 11), (14, -8, -90, -300, 10)]

def puffs(t, travel_ground=True):
    out = []
    for tc, name in contacts_before(t, PUFF_LIFE):
        a = t - tc
        bx, by = BALL[name]
        fx = bx + STRIDE - 50             # strike point under the foot
        k = a / PUFF_LIFE
        for ox, oy, vx, vy, r in PUFFS:
            x = fx + ox + vx * a - (SPEED * a if travel_ground else 0)
            y = min(GROUND - 4 - r * 0.6, GROUND - 14 + oy + vy * a + 0.5 * 2600 * a * a)
            rr = r * (1 + 1.6 * k)
            op = (1 - k) ** 1.6
            out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{rr:.1f}" fill="#F7FBFE" stroke="#6F98AE" '
                       f'stroke-width="5" opacity="{op:.2f}"/>')
    return "\n".join(out)

def frame_inner(pose):
    t = pose.t
    parts = []
    lift = -pose.root_y
    parts.append(f'<ellipse cx="560" cy="1003" rx="{235 - 0.9 * lift:.1f}" ry="{19 - 0.08 * lift:.1f}" '
                 f'fill="#7FA4B8" opacity="0.45" stroke="none"/>')
    parts.append(puffs(t))
    p = (t % CYCLE) / CYCLE
    for name, off in (("foot-back", 0.0), ("foot-front", 0.5)):
        dx, dy, ra, rb = foot((p + off) % 1)
        (ax, ay), (bx, by) = ANKLE[name], BALL[name]
        parts.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({ra:.2f} {ax} {ay}) rotate({rb:.2f} {bx} {by})">{PART[name]}</g>')
    ang = lambda y: pose.angle(pose.height(y))
    # dressed: the far flipper is trimmed along the far shoulder line, so its tip never shows above the sleeve
    clip_id = f"far-wing-clip-{next(CLIP_IDS)}"
    clip_d = deform_d(outfit.far_wing_clip(), pose.wing_fn("wing-far"))
    parts.append(f'<clipPath id="{clip_id}"><path d="{clip_d}"/></clipPath>'
                 f'<g clip-path="url(#{clip_id})">{deform_group(PART["wing-far"], pose.wing_fn("wing-far"))}</g>')
    parts.append(deform_group(PART["body"], pose.body))
    parts.append(deform_group(PART["belly"], pose.body))
    parts.append(deform_group(PART["wing-near"], pose.wing_fn("wing-near")))
    parts.append(deform_group(PART["eyes"], pose.body, ang, pose.sx, pose.sy))
    parts.append(deform_group(PART["beak"], pose.body))
    # outfit layer (a separate tinted sprite in game): sleeves ride the flippers, the rest the body
    parts.append(deform_group(OUTFIT["sleeve-far"], pose.wing_fn("wing-far")))
    cloth = skinned(pose)
    parts.append(deform_group(OUTFIT["sweater"], cloth))
    # the near flipper hangs in front of the sweater body and comes out of the sleeve (black stays black
    # under the outfit tint, so in game it can sit in the outfit layer)
    near_id = f"near-wing-clip-{next(CLIP_IDS)}"
    near_d = deform_d(outfit.near_wing_clip(), pose.wing_fn("wing-near"))
    parts.append(f'<clipPath id="{near_id}"><path d="{near_d}"/></clipPath>'
                 f'<g clip-path="url(#{near_id})">{deform_group(PART["wing-near"], pose.wing_fn("wing-near"))}</g>')
    parts.append(deform_group(OUTFIT["sleeve-near"], pose.wing_fn("wing-near")))
    parts.append(deform_group(OUTFIT["collar"], pose.body))
    parts.append(deform_group(OUTFIT["hat"], pose.body))
    return "\n".join(parts)

BACKDROP = ('<rect width="1024" height="1024" fill="#CFE6F2" stroke="none"/>\n'
            '<rect y="985" width="1024" height="39" fill="#EEF6FB" stroke="none"/>\n')

def frames(n):
    springs = simulate(n)
    return [Pose(s["t"], s) for s in springs]

def wrap(inner, w=1024, h=1024, vb="0 0 1024 1024", extra=""):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{vb}">\n{extra}'
            f'<g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">\n{inner}\n</g>\n</svg>\n')

def flipbook(inners, dur):
    n = len(inners); gs = []
    for i, inner in enumerate(inners):
        if i == 0:
            anim = f'<animate attributeName="display" values="inline;none" keyTimes="0;{1 / n:.5f}" dur="{dur}s" calcMode="discrete" repeatCount="indefinite"/>'
        else:
            anim = (f'<animate attributeName="display" values="none;inline;none" keyTimes="0;{i / n:.5f};{(i + 1) / n:.5f}" '
                    f'dur="{dur}s" calcMode="discrete" repeatCount="indefinite"/>')
        gs.append(f'<g display="{"inline" if i == 0 else "none"}">{anim}\n{inner}\n</g>')
    return "\n".join(gs)

if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(os.path.join(out, "frames"), exist_ok=True)
    for f in os.listdir(os.path.join(out, "frames")):
        os.remove(os.path.join(out, "frames", f))
    gif_n = int(sys.argv[2]) if len(sys.argv) > 2 else 19
    for i, pose in enumerate(frames(gif_n)):
        open(os.path.join(out, "frames", f"f{i:02d}.svg"), "w").write(wrap(frame_inner(pose), extra=BACKDROP))
    svg_poses = frames(32)
    inners = [frame_inner(p) for p in svg_poses]
    open(os.path.join(out, "penguin-run.svg"), "w").write(wrap(flipbook(inners, CYCLE), extra=BACKDROP))
    width = 3072
    tufts = "".join(f'<path d="M{x} 990 q8 -14 16 0 q8 -10 16 0" fill="#FFFFFF" stroke="#A9C7D8" stroke-width="3"/>' for x in range(60, width, 230))
    travel = (f'<rect width="{width}" height="1024" fill="#CFE6F2" stroke="none"/>'
              f'<rect y="985" width="{width}" height="39" fill="#EEF6FB" stroke="none"/>{tufts}\n'
              f'<g><animateTransform attributeName="transform" type="translate" dur="{(width + 1024) / SPEED:.3f}s" '
              f'repeatCount="indefinite" values="-1024 0;{width} 0"/>\n{flipbook(inners, CYCLE)}\n</g>')
    open(os.path.join(out, "penguin-run-travel.svg"), "w").write(wrap(travel, 1536, 512, f"0 0 {width} 1024"))
    print(f"speed {SPEED:.0f} px/s = {SPEED / 930:.2f} body heights/s, flight height {FLIGHT_H:.1f}px")
