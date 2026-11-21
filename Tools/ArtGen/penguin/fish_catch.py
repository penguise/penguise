"""Ice fishing at the hole, two one-shot clips on the idle rig (idle.IdlePose). Going to the hole and back home
is the ordinary run, so both clips start and end in the plain standing pose. export_fishing.py renders them for
the game against the background's ice hole (use_game_hole); this script previews them at a drawn hole.

  catch  it pulls out a telescopic pocket rod that extends as it swings up, casts the float into the hole,
         waits (bobbing, a nibble or two); the float goes under, eyes go wide; it yanks and a big fish flies
         out of the hole and swings on the line; a happy double hop; the fish pops away in a burst of
         sparkles and a "+1" with a little fish rises and fades (it went into the inventory); the rod
         shrinks away.
  miss   the same until the float goes under; it yanks and the hook comes out bare, a few drops flying off;
         it stares at the empty hook, then droops: sad brows, eyes half shut, head and flippers down, a sigh;
         the rod sags and shrinks away, and it straightens up again.

The fish is drawn large (about a quarter of the penguin's height) so it reads at the penguin's small size on
screen. It hangs by its mouth on the line like a damped pendulum driven by the rod tip, so it swings with the
yank and the hops; the bare hook does the same.

usage: python -I fish_catch.py <work> [color] [game]
With "game" the preview uses the game's hole geometry instead (no hole drawn, water line clip).
Writes <work>/<clip>/*.svg|png, <clip>.gif and <clip>_sheet.png for catch and miss.
"""
import math, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import outfit
import run as R
import idle as I
import block as B
import fish as F
from PIL import Image, ImageDraw

FPS = 20
REST_ROD, WAIT_ROD, PULL_ROD = 50, -32, -70   # rod angle, deg from horizontal (- = up); rest: pointing down
STOW_ROD = -55                 # held up while it shrinks after a catch
SAD_ROD = -8                   # sagging after a miss (still clear of the ice)
ROD_LEN = 540
HOLE = F.HOLE
WATER_LINE = None              # game: things below this canvas y are under water (None: the drawn hole's clip)

# The game's hole (GameGround.png, 100 px/unit; one background px is 12.5 canvas px): the water is an
# ellipse about 84 x 45 background px. The penguin stands GAME_STAND units left of the water's centre, its
# feet level with it, so the float lands in the water's near-left part.
GAME_STAND = 0.72
GAME_HOLE = (512 + 740, 998)
GAME_WATER_LINE = 1006
# On screen the canvas is 12.5x smaller than the background, so the game draws the float and line bigger.
FLOAT_SCALE, LINE_WIDTH = 1.0, 4
GAME_FLOAT_SCALE, GAME_LINE_WIDTH = 2.2, 9
DRAW = (0.1, 0.6)              # the rod extends and swings up
CAST = (0.62, 1.0)             # the float flies from the rod tip into the water
NIBBLES = (2.0, 2.35)
DIP, PULL = 2.65, 2.8          # float goes under; yank
LINE_OUT = 200                 # px of line below the rod tip once out of the water
MISS_LINE = 90                 # px left out after a miss
FISH_SCALE = 1.7               # the fish's size (1 = the old 140 px fish)
FISH_MOUTH = 56 * FISH_SCALE   # px from the fish's centre to its mouth
HOOK_MASS = 24                 # px from the line's end to the bare hook's centre (its pendulum length)
GRAVITY = 15000.0              # px/s^2 (the penguin's 930 px are about 0.6 m)
DAMP = 1.6                     # 1/s

CLIPS = {
    # name: (length, happy start, pop (fish vanishes), stow start, stow end)
    "catch": dict(length=5.4, happy=3.4, pop=4.05, stow=(4.6, 5.15)),
    "miss": dict(length=5.6, stare=(2.95, 3.55), sad=(3.55, 5.3), stow=(4.3, 4.95)),
}


def smooth(x): return R.smootherstep(R.clamp01(x))


def use_game_hole():
    global HOLE, WATER_LINE, FLOAT_SCALE, LINE_WIDTH
    HOLE, WATER_LINE = GAME_HOLE, GAME_WATER_LINE
    FLOAT_SCALE, LINE_WIDTH = GAME_FLOAT_SCALE, GAME_LINE_WIDTH
    _swings.clear()


def bobber(x, y, under=0.0):
    """The float (fish.float_svg) at FLOAT_SCALE about its centre; `under` sinks it by its own size."""
    if FLOAT_SCALE == 1.0:
        return F.float_svg(x, y, under)
    return (f'<g transform="translate({x:.1f} {y:.1f}) scale({FLOAT_SCALE})">{F.float_svg(0, 0, under)}</g>')


def water_clip(uid):
    if WATER_LINE is None:
        return F.water_clip(uid)
    return f'<clipPath id="w{uid}"><rect x="-3000" y="-3000" width="7000" height="{3000 + WATER_LINE}"/></clipPath>'


# ---- poses ----------------------------------------------------------------------------------------------
def channels(clip, t):
    k = CLIPS[clip]
    c = B.Channels()
    I.breathe(t, c)
    c.sad = 0.0
    out = smooth((t - DRAW[0]) / (DRAW[1] - DRAW[0]))
    s0, s1 = k["stow"]
    stow = smooth((t - s0) / (s1 - s0))
    c.rod = out * (1 - stow)
    c.far = 10 * out * (1 - stow)
    c.sign = REST_ROD + (WAIT_ROD - REST_ROD) * out
    c.lean += 3 * I.bump(t, CAST[0] - 0.1, CAST[1])           # a little throw forward for the cast
    for a in NIBBLES:
        b = I.bump(t, a, a + 0.18)
        c.lean += 2.5 * b
        c.sign += 5 * b
    c.lean += 3 * smooth((t - DIP) / 0.1) * (1 - smooth((t - PULL) / 0.1))
    c.far += 16 * I.bump(t, PULL - 0.02, PULL + 0.55)
    c.lean += -6 * I.bump(t, PULL, PULL + 0.7)
    c.squint = I.blink_at(t, 1.5)
    if clip == "catch":
        settle = smooth((t - k["happy"] - 0.5) / 0.4)         # from the pull to holding the rod up
        c.sign += (PULL_ROD - WAIT_ROD) * smooth((t - PULL) / 0.16) * (1 - settle) + (STOW_ROD - WAIT_ROD) * settle
        c.eyes = 1 + 0.28 * smooth((t - DIP + 0.05) / 0.08) * (1 - smooth((t - k["happy"]) / 0.2))
        c.nod += -5 * I.bump(t, PULL + 0.1, k["happy"])       # looks up at the fish
        c.happy = I.envelope(t, k["happy"], k["pop"] + 0.45, 0.1)
        for a in (k["happy"] + 0.05, k["happy"] + 0.4):
            h = I.bump(t, a, a + 0.3)
            c.root_y += -26 * h
            c.wing += 6 * h
            for name in c.feet:
                dx, dy, toe = c.feet[name]
                c.feet[name] = (dx, dy - 22 * h, toe)
        c.squint = max(c.squint, I.blink_at(t, s1 + 0.05))
    else:
        a0, a1 = k["stare"]
        b0, b1 = k["sad"]
        yank = smooth((t - PULL) / 0.16) * (1 - smooth((t - a1) / 0.35))
        sag = I.envelope(t, a1, b1, 0.45)                     # the rod sags with the mood
        c.sign += (PULL_ROD - WAIT_ROD) * yank + (SAD_ROD - WAIT_ROD) * sag * (1 - yank)
        c.eyes = 1 + 0.28 * smooth((t - DIP + 0.05) / 0.08) * (1 - smooth((t - a0) / 0.15))
        c.nod += -6 * I.envelope(t, PULL + 0.1, a1 + 0.1, 0.15)   # stares up at the bare hook
        c.head_dx += 6 * I.bump(t, a0 + 0.1, a0 + 0.35) - 6 * I.bump(t, a0 + 0.3, a0 + 0.55)   # a little double take
        sad = I.envelope(t, a1, b1, 0.4)
        c.sad = sad
        c.nod += 9 * sad                                      # head hangs
        c.lean += 3 * sad
        c.wing += -5 * sad                                    # flippers droop in
        c.squint = max(c.squint, 0.3 * sad)                   # eyes a little shut
        sigh = I.bump(t, a1 + 0.35, a1 + 1.25)
        c.stretch += 0.03 * I.bump(t, a1 + 0.2, a1 + 0.6) - 0.035 * sigh   # breath in, long breath out
        c.root_y += 10 * sigh
    return c


def pose(clip, t):
    h = 1 / 240
    return B.Pose(channels(clip, t), (channels(clip, t + h).wing - channels(clip, t - h).wing) / (2 * h))


# ---- rod, line, fish ------------------------------------------------------------------------------------
def rod(p, c):
    """The telescopic rod from the far flipper's grip; its length follows c.rod. Returns svg and tip."""
    gx, gy = p.wing_fn("wing-far")(*B.WING_TIP)
    if c.rod <= 0.01:
        return "", (gx, gy)
    a = math.radians(c.sign)
    dx, dy = math.cos(a), math.sin(a)
    grow = min(1.0, c.rod * 3)
    x0, y0 = gx - dx * 60 * grow, gy - dy * 60 * grow
    length = ROD_LEN * c.rod
    x1, y1 = gx + dx * length, gy + dy * length
    rx, ry = gx - dx * 28 + dy * 22, gy - dy * 28 - dx * 22
    joints = "".join(f'<circle cx="{gx + dx * length * s:.1f}" cy="{gy + dy * length * s:.1f}" r="7" fill="#7C4A1E" '
                     f'stroke="none"/>' for s in (0.45, 0.75) if c.rod > 0.2)
    svg = (f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#111111" stroke-width="24" fill="none"/>'
           f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#A86B32" stroke-width="11" fill="none"/>'
           + joints + f'<circle cx="{rx:.1f}" cy="{ry:.1f}" r="{17 * grow:.1f}" fill="#6E7B85"/>')
    return svg, (x1, y1)


def tip_at(clip, t):
    p = pose(clip, t)
    return rod(p, p.c)[1]


def line(a, b, sag=0.0):
    (x0, y0), (x1, y1) = a, b
    return (f'<path d="M{x0:.1f} {y0:.1f} Q{(x0 + x1) / 2 + sag:.1f} {(y0 + y1) / 2:.1f} {x1:.1f} {y1:.1f}" '
            f'stroke="#33414A" stroke-width="{LINE_WIDTH}" fill="none"/>')


def big_fish(x, y, ang, s=1.0, flap=0.0):
    """A plump silver-blue fish centred on (x, y), facing `ang` degrees (mouth first), FISH_SCALE * s big."""
    tail = 16 * flap
    return (f'<g transform="translate({x:.1f} {y:.1f}) rotate({ang:.1f}) scale({FISH_SCALE * s:.3f})">'
            f'<path d="M-46 0 L-84 {-28 + tail:.1f} Q-74 {tail:.1f} -84 {28 + tail:.1f} Z" fill="#4F86AE" stroke-width="5"/>'
            f'<path d="M-6 -24 Q8 -44 26 -24 Z" fill="#4F86AE" stroke-width="5"/>'
            f'<ellipse cx="0" cy="0" rx="56" ry="27" fill="#8EC3E6" stroke-width="5"/>'
            f'<path d="M-36 8 Q0 22 40 8" stroke="#D8EEFA" stroke-width="7" fill="none"/>'
            f'<path d="M16 -18 Q24 0 16 18" stroke="#4F86AE" stroke-width="4" fill="none"/>'
            f'<circle cx="34" cy="-7" r="6.5" fill="#111111" stroke="none"/>'
            f'<circle cx="36" cy="-9" r="2" fill="#FFFFFF" stroke="none"/></g>')


def hook_svg(x, y, ang):
    """A bare hook hanging at the line's end (x, y), along `ang` (deg, line direction)."""
    return (f'<g transform="translate({x:.1f} {y:.1f}) rotate({ang - 90:.1f})">'
            f'<path d="M0 0 L0 30 Q0 46 -14 46 Q-26 46 -26 34 L-26 28" fill="none" stroke="#5F6B73" stroke-width="6"/>'
            f'</g>')


def swing(pivot, length, t0, t1, ang0, dt=1 / 2400):
    """Damped pendulum on a taut line of length(t) from pivot(t) (px). ang: direction from the pivot to the
    weight, deg (90 = straight down). Returns a function of t."""
    def acc(t):
        e = 1 / 240
        (x0, y0), (x1, y1), (x2, y2) = pivot(t - e), pivot(t), pivot(t + e)
        return (x0 - 2 * x1 + x2) / (e * e), (y0 - 2 * y1 + y2) / (e * e)
    a, v = math.radians(ang0), 0.0
    angs = [a]
    t = t0
    while t < t1:
        ax, ay = acc(t)
        v += ((math.sin(a) * ax + math.cos(a) * (GRAVITY - ay)) / length(t) - DAMP * v) * dt
        a += v * dt
        t += dt
        angs.append(a)

    def f(q):
        return math.degrees(angs[min(len(angs) - 1, max(0, int((q - t0) / dt)))])
    return f


_swings = {}


def out_line(clip, t):
    """Line length once out of the water: from the water's distance, reeled up to LINE_OUT, then in with the
    shrinking rod."""
    tx, ty = tip_at(clip, PULL)
    start = math.hypot(HOLE[0] + 10 - tx, HOLE[1] - ty) - (FISH_MOUTH if clip == "catch" else HOOK_MASS)
    length = start + (LINE_OUT - start) * smooth((t - PULL) / 0.5)
    if clip == "miss":                                         # reels the bare hook in a bit while it stares
        a0, a1 = CLIPS[clip]["stare"]
        length += (MISS_LINE - LINE_OUT) * smooth((t - a0) / (a1 + 0.3 - a0))
    s0, s1 = CLIPS[clip]["stow"]
    return length * (1 - smooth((t - s0) / (s1 - s0)))


def hanging_angle(clip, t):
    if clip not in _swings:
        tx, ty = tip_at(clip, PULL)
        ang0 = math.degrees(math.atan2(HOLE[1] - ty, HOLE[0] + 10 - tx))
        weight = FISH_MOUTH if clip == "catch" else HOOK_MASS
        _swings[clip] = swing(lambda q: tip_at(clip, q), lambda q: max(8.0, out_line(clip, q)) + weight, PULL,
                              CLIPS[clip]["length"] + 0.1, ang0)
    return _swings[clip](t)


# ---- the "+1" -------------------------------------------------------------------------------------------
def plus_one(x, y, alpha, s):
    """A little fish and '+1', drawn as shapes (white with a dark outline); (x, y) is the fish's centre."""
    plus = "M93 -7 L108 -7 L108 -22 L122 -22 L122 -7 L137 -7 L137 7 L122 7 L122 22 L108 22 L108 7 L93 7 Z"
    one = "M150 -16 L168 -32 L184 -32 L184 18 L196 18 L196 32 L152 32 L152 18 L166 18 L166 -10 L156 -4 Z"
    # Mirrored about its own middle for a sprite that the game flips back (it then reads normally).
    mirror = ' translate(124 0) scale(-1 1)' if MIRROR_PLUS else ''
    return (f'<g transform="translate({x:.1f} {y:.1f}) scale({s:.3f}){mirror}" opacity="{alpha:.2f}" stroke-width="7">'
            + big_fish(0, 0, 0, 0.5) + f'<path d="{plus}" fill="#FFFFFF"/><path d="{one}" fill="#FFFFFF"/></g>')


MIRROR_PLUS = False
PLUS_SCALE = 2.4


def sparkles(x, y, u):
    """A ring of sparkles bursting from (x, y), u = 0..1 over the burst."""
    if not 0 <= u <= 1:
        return ""
    out = []
    for i in range(8):
        a = math.radians(i * 45 + 20)
        r = 40 + 150 * (1 - (1 - u) ** 2)
        s = 26 * math.sin(math.pi * u)
        cx, cy = x + r * math.cos(a), y + r * math.sin(a)
        pts = " ".join(f"{cx + (s if k % 2 == 0 else s * 0.3) * math.cos(math.radians(k * 45)):.1f},"
                       f"{cy + (s if k % 2 == 0 else s * 0.3) * math.sin(math.radians(k * 45)):.1f}" for k in range(8))
        out.append(f'<polygon points="{pts}" fill="#FFF6C8" stroke="#F2C14E" stroke-width="3"/>')
    flash = 1 - u
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{30 + 90 * u:.1f}" fill="#FFFFFF" opacity="{0.8 * flash:.2f}" '
               f'stroke="none"/>')
    return "".join(out)


# ---- scene ----------------------------------------------------------------------------------------------
def sad_brows(p, c):
    """Worried brows: inner ends raised."""
    if c.sad <= 0.05:
        return ""
    out = []
    for (a, b) in (((560, 226), (624, 192)), ((670, 190), (724, 222))):
        (x0, y0), (x1, y1) = p.body(*a), p.body(*b)
        out.append(f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" fill="none" stroke="#1C0F0F" '
                   f'stroke-width="12" opacity="{min(1.0, c.sad * 1.5):.2f}"/>')
    return "".join(out)


def drops(x, y, u):
    """Water drops flung off the bare hook."""
    if not 0 <= u <= 1:
        return ""
    out = []
    for vx, vy in ((-260, -420), (180, -520), (330, -300), (-60, -600)):
        dx, dy = vx * u * 0.5, vy * u * 0.5 + 0.5 * 2600 * (u * 0.5) ** 2
        out.append(f'<ellipse cx="{x + dx:.1f}" cy="{y + dy:.1f}" rx="13" ry="18" fill="#8FC7E8" stroke-width="4" '
                   f'opacity="{1 - u:.2f}"/>')
    return "".join(out)


def sigh_puff(p, u):
    """A little breath cloud drifting off the beak with the sigh."""
    if not 0 <= u <= 1:
        return ""
    x, y = p.body(800, 352)
    x, y = x + 90 * u, y + 20 * u
    r = 14 + 26 * u
    a = 0.85 * (1 - u)
    return "".join(f'<circle cx="{x + dx * r:.1f}" cy="{y + dy * r:.1f}" r="{r * k:.1f}" fill="#FFFFFF" '
                   f'stroke="#A9C7D8" stroke-width="4" opacity="{a:.2f}"/>'
                   for dx, dy, k in ((0, 0, 1), (0.9, -0.3, 0.75), (1.6, 0.1, 0.55)))


def tackle(clip, p, c, t, uid):
    """Returns (rod svg behind the flipper, tackle svg in front)."""
    k = CLIPS[clip]
    rod_svg, tip = rod(p, c)
    hx, hy = HOLE
    bob = 3 * math.sin(2 * math.pi * t / 1.3)
    water = (hx + 10, hy - 4 + bob)
    out = []
    if t < PULL:
        if c.rod <= 0.01:
            return rod_svg, ""
        if t < CAST[0]:
            fpos = (tip[0], tip[1] + 40 + 190 * c.rod)
            return rod_svg, line(tip, fpos) + bobber(*fpos)
        under = max(I.bump(t, NIBBLES[0], NIBBLES[0] + 0.2) * 0.4, I.bump(t, NIBBLES[1], NIBBLES[1] + 0.2) * 0.5,
                    smooth((t - DIP) / 0.08))
        u = R.clamp01((t - CAST[0]) / (CAST[1] - CAST[0]))
        start = (tip[0], tip[1] + 230)
        fx = start[0] + (water[0] - start[0]) * smooth(u)
        fy = start[1] + (water[1] - start[1]) * u - 160 * math.sin(math.pi * u)
        out.append(line(tip, (fx, fy), 20 * u))
        out.append(f'<g clip-path="url(#w{uid})">{bobber(fx, fy, under if u >= 1 else 0.0)}</g>')
        for a in (CAST[1],) + NIBBLES + (DIP,):
            out.append(F.ripples((t - a) / 0.5, water[0], hy))
        return rod_svg, "".join(out)

    ang = hanging_angle(clip, t)
    s0, s1 = k["stow"]
    away = smooth((t - s0 - 0.6 * (s1 - s0)) / (0.4 * (s1 - s0)))   # line, float and hook go into the rod
    r = math.radians(ang)
    dx, dy = math.cos(r), math.sin(r)
    length = out_line(clip, t)
    ex, ey = tip[0] + length * dx, tip[1] + length * dy          # end of the line
    fl = (tip[0] + length * 0.45 * dx, tip[1] + length * 0.45 * dy)
    hang = [line(tip, (ex, ey)), bobber(*fl)] if away < 0.99 else []
    hook = hook_svg(ex, ey, ang) if away < 0.99 else ""
    if clip == "catch":
        pop = k["pop"]
        cx, cy = ex + FISH_MOUTH * dx, ey + FISH_MOUTH * dy
        if t < pop + 0.2:
            grow = 1 + 0.25 * smooth((t - pop + 0.1) / 0.1)
            shrink = 1 - smooth((t - pop) / 0.15)
            wig = math.sin(t * 34) * (0.3 + 0.7 * math.exp(-(t - PULL) * 1.5))
            if shrink > 0.01:
                hang.append(big_fish(cx, cy, ang + 180, grow * shrink, wig))
        else:
            hang.append(hook)
        fx_pop = sparkles(cx, cy, (t - pop) / 0.45)
        u = (t - pop) / 1.1
        plus = ""
        if 0 <= u <= 1:
            # Big enough to read at the game's size: centred above the fish, rising clear of the rod tip and
            # below the player's name (and clear of a second fisher's on the hole's other side).
            s = PLUS_SCALE * (0.6 + 0.4 * smooth(u / 0.15))
            plus = plus_one(cx - 40 - 62 * s, cy - 345 - 90 * (1 - (1 - u) ** 2), 1 - smooth((u - 0.6) / 0.4), s)
        body = "".join(hang)
        out.append(f'<g clip-path="url(#w{uid})">{body}</g>' if t < PULL + 0.3 else body)
        out.append(F.ripples((t - PULL) / 0.5, hx, hy))
        if _fx is None:
            out.append(fx_pop + plus)
        else:
            _fx.append(fx_pop + plus)
    else:
        hang.append(hook)
        body = "".join(hang)
        out.append(f'<g clip-path="url(#w{uid})">{body}</g>' if t < PULL + 0.2 else body)
        out.append(F.ripples((t - PULL) / 0.5, hx, hy))
        out.append(drops(ex, ey, (t - PULL - 0.05) / 0.6))
        out.append(sigh_puff(p, (t - k["stare"][1] - 0.45) / 0.9))
    return rod_svg, "".join(out)


_fx = None


def tackle_layers(clip, p, c, t, uid):
    """(rod svg, front svg: line, float, fish, drops, puff; effects svg: sparkles and the "+1") for the game,
    where the effects layer is unlit and never mirrored."""
    global _fx
    _fx = []
    try:
        rod_svg, front = tackle(clip, p, c, t, uid)
        return rod_svg, front, "".join(_fx)
    finally:
        _fx = None


def penguin_parts(p, c, uid, rod_svg):
    base, worn = penguin_layers(p, c, uid, rod_svg, CLOTH)
    return "\n".join(base + worn)


def penguin_layers(p, c, uid, rod_svg, cloth):
    """Body layer (untinted, rod included) and outfit layer parts, like idle.layers."""
    parts = []
    for name in ("foot-back", "foot-front"):
        dx, dy, toe = c.feet[name]
        ax, ay = R.ANKLE[name]
        parts.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({toe:.2f} {ax} {ay})">{R.PART[name]}</g>')
    far, near = p.wing_fn("wing-far"), p.wing_fn("wing-near")
    parts.append(rod_svg)                         # behind the far flipper, which grips it
    parts.append(I.clip(f"fw{uid}", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    parts.append(R.deform_group(R.PART["body"], p.body))
    parts.append(R.deform_group(R.PART["belly"], p.body))
    parts.append(R.deform_group(R.PART["wing-near"], near))
    parts.append(F.happy_eyes(p, c) if getattr(c, "happy", 0.0) > 0.5 else B.eyes(p, c, uid))
    parts.append(sad_brows(p, c))
    parts.append(B.beak(p, c, uid))
    worn = [R.deform_group(cloth["sleeve-far"], far),
            R.deform_group(cloth["sweater"], I.skinned(p)),
            I.clip(f"nw{uid}", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)),
            R.deform_group(cloth["sleeve-near"], near),
            R.deform_group(cloth["collar"], p.body),
            R.deform_group(cloth["hat"], p.body)]
    return parts, worn


def scene(clip, t, uid):
    p = pose(clip, t)
    c = p.c
    rod_svg, tackle_svg = tackle(clip, p, c, t, uid)
    lift = -min(0.0, c.root_y)
    shadow = (f'<ellipse cx="540" cy="1003" rx="{235 - 0.8 * lift:.1f}" ry="19" fill="#7FA4B8" opacity="0.45" '
              f'stroke="none"/>')
    hole = F.hole() if WATER_LINE is None else ""
    return water_clip(uid) + hole + shadow + penguin_parts(p, c, uid, rod_svg) + tackle_svg


# ---- output ---------------------------------------------------------------------------------------------
CLOTH = None


def main():
    global CLOTH
    work = sys.argv[1]
    color = sys.argv[2] if len(sys.argv) > 2 else "#F4511E"
    CLOTH = outfit.parts(color)
    if len(sys.argv) > 3 and sys.argv[3] == "game":
        use_game_hole()
    for clip, k in CLIPS.items():
        d = os.path.join(work, clip)
        os.makedirs(d, exist_ok=True)
        for f in os.listdir(d):
            os.remove(os.path.join(d, f))
        n = round(k["length"] * FPS)
        jobs = []
        for i in range(n):
            path = os.path.join(d, f"{clip}_{i:03d}.svg")
            open(path, "w").write(F.svg(scene(clip, i / FPS, f"{clip[0]}{i}")))
            jobs.append(path)
        w = 560
        with ThreadPoolExecutor(8) as pool:
            list(pool.map(lambda s: subprocess.run(["magick", s, "-resize", f"{w}x", s[:-4] + ".png"], check=True), jobs))
        ims = [Image.open(s[:-4] + ".png").convert("RGB") for s in jobs]
        ims[0].save(os.path.join(work, clip + ".gif"), save_all=True, append_images=ims[1:] + ims[-1:] * 10,
                    duration=int(1000 / FPS), loop=0)
        ks = [round(i * (n - 1) / 15) for i in range(16)]
        tw, th = w // 2, ims[0].height // 2
        sheet = Image.new("RGB", (tw * 8, th * 2), "white")
        dr = ImageDraw.Draw(sheet)
        for i, f in enumerate(ks):
            x, y = (i % 8) * tw, (i // 8) * th
            sheet.paste(ims[f].resize((tw, th)), (x, y))
            dr.text((x + 3, y + 3), f"{f / FPS:.2f}s", fill=(0, 0, 0))
        sheet.save(os.path.join(work, clip + "_sheet.png"))
        print(clip, n, "frames at", FPS, "fps")


if __name__ == "__main__":
    main()
