"""Knocked-out penguin, on the idle's rig (idle.IdlePose): three clips that chain without a pop.

  walk  loop, 2 steps (LOOP s at the nominal speed): dizzy walk, spiral eyes, stars circling the head;
        in game its frames follow the distance walked, like the run
  fall  one shot (FALL_CLIP s): starts from the walk at loop position FALL_START (both feet down), sways
        on the spot, slips and falls backwards like a board, bounces once on its back
  down  loop (DOWN_LOOP s): lying on its back, feet up, breathing, stars and eyes still turning

Stars and eyes turn once per LOOP and the sway repeats every LOOP, so the walk loops; the fall lasts until
the next whole LOOP, so the down loop starts where the fall ends. The penguin never gets up.

The fall turns the whole penguin about its back heel and lifts it so the lowest point stays on the ground;
the feet slip forward (SLIP) so it ends lying across its slot. Its feet pivot stays at canvas (512, 1008).
Every clip faces right (the art's direction); it falls backwards, to the left.
"""
import math, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import penguin, outfit
import run as R
import idle as I

# ---- walk --------------------------------------------------------------------------------------------
CYC = 0.42              # s, two steps at the nominal speed
STANCE = 0.6            # share of the cycle a foot is planted (> 0.5: always one foot down)
STEP = 62               # px foot travel from rest, each way
LIFT = 30               # px swing height
TRAVEL = 2 * STEP / STANCE          # body travel per cycle, px (the planted foot stays still)
LOOP = 2 * CYC                      # s, one walk loop; stars, eyes and sway repeat every LOOP
FALL_START = 0.275                  # walk loop position the fall starts from (both feet down)
CANVAS_PER_UNIT = 1250.0            # Penguin.png: 1024 canvas px at 1250 px/unit

# ---- fall and lying ------------------------------------------------------------------------------------
STOP = 0.5              # swaying on the slot
FALL = 0.42             # tipping over
FALL_CLIP = 2 * LOOP - FALL_START * LOOP   # the fall ends on a whole LOOP of the shared clock
SETTLE = FALL_CLIP - STOP - FALL            # the bounce and going limp
SLIP = 430              # px the feet slip forward while it falls
DOWN_LOOP = 3 * LOOP    # s, one slow breath lying down
GROUND = 1000.0
PIVOT = (512.0, 1008.0)  # feet pivot of every penguin sprite, canvas px


def smooth(x): return R.smootherstep(R.clamp01(x))


def foot(q):
    """dx, dy, toe for foot phase q (contact at 0)."""
    if q < STANCE:
        return STEP - 2 * STEP * q / STANCE, 0.0, 0.0
    s = (q - STANCE) / (1 - STANCE)
    return -STEP + 2 * STEP * R.smootherstep(s), -LIFT * math.sin(math.pi * s), -12 * math.sin(math.pi * s)


def dizzy(clock, c, amount):
    """Dizzy sway on the shared clock (repeats every LOOP): lurching lean, the head lolling round,
    flippers out for balance."""
    u = 2 * math.pi * clock / LOOP
    c.lean += amount * (2 + 6 * math.sin(u) + 3 * math.sin(3 * u + 1))
    c.nod += amount * 7 * math.sin(u + 0.5)
    c.head_dx += amount * 12 * math.sin(u)
    c.head_dy += amount * 7 * math.cos(u)
    c.wing += amount * (18 + 12 * math.sin(u + 2) + 6 * math.sin(3 * u))


class Frame:
    """One pose: rig channels, the rigid turn (deg, negative = backwards), the slip (px) and the shared
    clock that turns the stars and eyes."""

    def __init__(self, c, angle, slip, clock, wing_vel):
        self.pose = I.IdlePose(c, wing_vel)
        self.angle, self.slip, self.clock = angle, slip, clock


def planted(c):
    p = FALL_START * 2                       # walk cycles at the fall's start: both feet down
    for name, off in (("foot-back", 0.0), ("foot-front", 0.5)):
        c.feet[name] = foot((p + off) % 1)


def walk_channels(t):
    c = I.Channels()
    p = t / CYC
    for name, off in (("foot-back", 0.0), ("foot-front", 0.5)):
        c.feet[name] = foot((p + off) % 1)
    c.root_y += 8 * (0.5 - 0.5 * math.cos(4 * math.pi * p))   # a dip on each step
    dizzy(t, c, 1.0)
    return c, 0.0, 0.0, t


def fall_channels(s):
    c = I.Channels()
    planted(c)
    clock = FALL_START * LOOP + s
    c.root_y += 8 * (0.5 - 0.5 * math.cos(4 * math.pi * FALL_START * 2)) * (1 - smooth(s / 0.15))
    wild = 1 + 0.6 * smooth(s / 0.25)
    gone = 1 - smooth((s - STOP) / (FALL * 0.8))
    dizzy(clock, c, wild * gone)
    tip = smooth((s - (STOP - 0.25)) / 0.25) * gone     # up on its toes, leaning back
    c.lean += -7 * tip
    c.stretch += 0.02 * tip
    c.wing += 45 * I.bump(s, STOP - 0.05, STOP + FALL + 0.3)   # flippers fling up
    c.nod += 10 * I.bump(s, STOP, STOP + FALL + 0.15)          # the head lags behind
    c.wing += 6 * smooth((s - STOP - FALL) / SETTLE)           # limp
    u = R.clamp01((s - STOP) / FALL)
    angle = -90 * u * u                                        # falls like a board
    if s > STOP + FALL:
        k = s - STOP - FALL
        angle = -90 + 9 * math.exp(-k * 9) * math.sin(k * 26) * (1 - smooth(k / SETTLE))
    slip = SLIP * smooth((s - STOP + 0.08) / (FALL + 0.2))
    return c, angle, slip, clock


def down_channels(s):
    c = I.Channels()
    planted(c)
    breath = math.sin(2 * math.pi * s / DOWN_LOOP)
    c.wing += 6 + 3 * breath
    c.stretch += 0.012 * breath
    dx, dy, toe = c.feet["foot-front"]
    c.feet["foot-front"] = (dx, dy, toe - 14 * I.bump(s, 1.3, 1.6))   # a foot twitch
    return c, -90.0, float(SLIP), s


def frame(channels, t):
    h = 1 / 240
    wing_vel = (channels(t + h)[0].wing - channels(t - h)[0].wing) / (2 * h)
    c, angle, slip, clock = channels(t)
    return Frame(c, angle, slip, clock, wing_vel)


WALK_FRAMES, FALL_FRAMES, DOWN_FRAMES = 24, 29, 36
CLIPS = {
    "walk": (walk_channels, LOOP, WALK_FRAMES),
    "fall": (fall_channels, FALL_CLIP, FALL_FRAMES),
    "down": (down_channels, DOWN_LOOP, DOWN_FRAMES),
}


def clip_frames(name):
    channels, length, n = CLIPS[name]
    return [frame(channels, length * k / n) for k in range(n)]


# ---- drawing -----------------------------------------------------------------------------------------
EYES = [((595, 262), 17.5, 32.0), ((697, 255), 12.5, 28.0)]
OUTFIT_W = outfit.parts("#FFFFFF")


def spiral_eyes(p, clock):
    """Dizzy spiral eyes in place of the eye ellipses, one turn per LOOP."""
    out = []
    for (cx, cy), rx, ry in EYES:
        pts = []
        for k in range(46):
            f = k / 45
            a = f * 2.3 * 2 * math.pi + 2 * math.pi * clock / LOOP
            r = 0.12 + f
            pts.append(p.body(cx + rx * 1.15 * r * math.cos(a), cy + ry * 0.95 * r * math.sin(a)))
        out.append(f'<path d="M{" L".join(f"{x:.1f} {y:.1f}" for x, y in pts)}" fill="none" stroke="#1C0F0F" '
                   f'stroke-width="7"/>')
    return "\n".join(out)


FOOT_PTS = {name: [(float(a), float(b)) for a, b in re.findall(r'(-?\d+\.?\d*) (-?\d+\.?\d*)', d)]
            for name, d in (("foot-back", penguin.footB), ("foot-front", penguin.footF))}


def outline(p):
    """Body, flipper and feet points of the pose (canvas px), for ground contact."""
    pts = [p.body(x, y) for x, y in penguin.body]
    pts += [p.wing_fn("wing-near")(x, y) for x, y in penguin.wingL]
    pts += [p.wing_fn("wing-far")(x, y) for x, y in penguin.wingR]
    for name, fp in FOOT_PTS.items():
        dx, dy, _ = p.c.feet[name]
        pts += [(x + dx, y + dy) for x, y in fp]
    return pts


def turn(angle, pivot):
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    px, py = pivot
    return lambda x, y: (px + (x - px) * ca - (y - py) * sa, py + (x - px) * sa + (y - py) * ca)


def placement(f):
    """SVG transform of the frame (turn about the back heel, lift onto the ground, slip), its head
    centre x and top, and its outline in canvas px."""
    p = f.pose
    pivot = (R.ANKLE["foot-back"][0] + p.c.feet["foot-back"][0] - 20, GROUND)
    m = turn(f.angle, pivot)
    rest = outline(p)
    lift = max(y for _, y in rest) - max(y for _, y in (m(x, y) for x, y in rest))
    head = [m(*p.body(x, y)) for x, y in penguin.body if y < 330]
    hx = sum(x for x, _ in head) / len(head) + f.slip
    htop = min(y for _, y in head) + lift
    tf = f'translate({f.slip:.2f} {lift:.2f}) rotate({f.angle:.3f} {pivot[0]:.2f} {pivot[1]:.2f})'
    return tf, (hx, htop), [(x + f.slip, y + lift) for x, y in (m(x, y) for x, y in rest)]


def star(cx, cy, r, spin):
    pts = []
    for k in range(10):
        a = spin + k * math.pi / 5 - math.pi / 2
        rr = r if k % 2 == 0 else r * 0.45
        pts.append(f"{cx + rr * math.cos(a):.1f} {cy + rr * math.sin(a):.1f}")
    return f'<path d="M{" L".join(pts)} Z" fill="#FFD54A"/>'


STAR_RISE = 110          # px from the head top up to the stars' orbit


def stars(cx, cy, clock, behind):
    """Three stars circling at (cx, cy) once per LOOP; `behind`: the half of the orbit behind the head."""
    out = []
    for k in range(3):
        a = 2 * math.pi * (clock / LOOP + k / 3)
        depth = math.sin(a)
        if (depth < 0) != behind:
            continue
        s = 0.8 + 0.25 * depth
        spin = 2 * math.pi / 5 * clock / LOOP + k       # a fifth of a turn per LOOP (5-fold star)
        out.append(star(cx + 150 * math.cos(a), cy + 40 * depth, 46 * s, spin))
    return "\n".join(out)


def layers(f, uid):
    """Body layer (untinted: body, spiral eyes, stars) and white outfit layer, in canvas px."""
    p = f.pose
    c = p.c
    tf, (hx, htop), _ = placement(f)
    sx, sy = hx, htop - STAR_RISE
    base = []
    for name in ("foot-back", "foot-front"):
        dx, dy, toe = c.feet[name]
        ax, ay = R.ANKLE[name]
        base.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({toe:.2f} {ax} {ay})">{R.PART[name]}</g>')
    far, near = p.wing_fn("wing-far"), p.wing_fn("wing-near")
    base.append(I.clip(f"fw{uid}", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    base.append(R.deform_group(R.PART["body"], p.body))
    base.append(R.deform_group(R.PART["belly"], p.body))
    base.append(R.deform_group(R.PART["wing-near"], near))
    base.append(spiral_eyes(p, f.clock))
    base.append(R.deform_group(R.PART["beak"], p.body))
    cloth = [R.deform_group(OUTFIT_W["sleeve-far"], far),
             R.deform_group(OUTFIT_W["sweater"], I.skinned(p)),
             I.clip(f"nw{uid}", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)),
             R.deform_group(OUTFIT_W["sleeve-near"], near),
             R.deform_group(OUTFIT_W["collar"], p.body),
             R.deform_group(OUTFIT_W["hat"], p.body)]
    body = (f'{stars(sx, sy, f.clock, True)}<g transform="{tf}">{"".join(base)}</g>'
            f'{stars(sx, sy, f.clock, False)}')
    return body, f'<g transform="{tf}">{"".join(cloth)}</g>'


# Render window (canvas px), large enough for every clip: standing, walking and lying with the stars.
VIEW = (-700, -400, 2000, 1500)    # x, y, width, height


def svg(inner):
    x, y, w, h = VIEW
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{x} {y} {w} {h}">\n'
            f'<g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">\n{inner}\n</g>\n</svg>\n')
