"""Block action previews on the idle rig (idle.IdlePose): two clips, not in the game yet.

  guard    loop (BREATH s): the blocker stands at a door, brows down and eyes narrowed, holding a round
           "no entry" sign up in its far flipper (the one in front); it pumps the sign twice, nods sternly,
           then taps a foot
  startle  one shot (STARTLE s): calm, then it sees the blocker: eyes go wide and the beak drops, a flinch,
           a jump with the flippers out (two flaps), a hard landing, then it settles, still trembling

usage: python -I block.py <work> [guard color] [startle color]
Writes <work>/frames/*.svg|png, guard.gif, startle.gif, block.gif (both side by side) and block_sheet.png.
"""
import math, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import outfit, penguin
import run as R
import idle as I
from PIL import Image, ImageDraw

FPS = 20
BREATH = I.BREATH          # guard loop length, s
STARTLE = 2.3              # startle clip length, s


def smooth(x): return R.smootherstep(R.clamp01(x))


class Channels(I.Channels):
    def __init__(self):
        super().__init__()
        self.far = 0.0         # deg, extra outward turn of the far flipper only
        self.brows = 0.0       # 0 none, 1 angry
        self.eyes = 1.0        # eye size (1 normal, > 1 wide)
        self.squint = 0.0      # 0 open, 1 closed (from the top)
        self.beak = 0.0        # lower beak drop, deg
        self.sign = None       # pole angle (deg from horizontal, - = up) while holding the sign


class Pose(I.IdlePose):
    """Idle pose with an extra far-flipper turn."""

    def wing_fn(self, name):
        sx, sy = R.SHOULDER[name]
        px, py = self.body(sx, sy)
        sgn = 1 if name == "wing-near" else -1
        base = self.angle(self.height(sy)) + sgn * (self.c.wing + (self.c.far if sgn < 0 else 0.0))
        flex = -sgn * self.wing_vel * R.WING_FLEX

        def f(x, y):
            d = math.hypot(x - sx, y - sy) / R.WING_LEN
            nx, ny = R.rot(x, y, sx, sy, base + flex * d * d)
            return nx - sx + px, ny - sy + py
        return f


# ---- guard -------------------------------------------------------------------------------------------
GUARD_FLIPPER = 16         # far flipper held a little out, gripping the pole
POLE = -76                 # pole angle at rest (nearly upright)


def guard(t):
    c = Channels()
    I.breathe(t, c)
    c.brows, c.squint = 1.0, 0.28
    c.far = GUARD_FLIPPER
    c.lean += 2.5                                       # looming a little forward
    pump = I.bump(t, 0.25, 0.47) + I.bump(t, 0.55, 0.77)   # two firm pumps of the sign
    c.far += 7 * pump
    c.sign = POLE + 8 * pump
    c.nod += 4 * pump                                   # a stern nod with each
    c.root_y += 2 * pump
    for a in (1.35, 1.65):                              # impatient toe taps
        k = I.bump(t, a, a + 0.24)
        dx, dy, toe = c.feet["foot-front"]
        c.feet["foot-front"] = (dx, dy - 8 * k, toe - 14 * k)
    return c


# ---- startle ------------------------------------------------------------------------------------------
NOTICE, CROUCH, JUMP, LAND = 0.45, 0.6, 0.72, 1.12   # s


def startle(t):
    c = Channels()
    I.breathe(t, c)
    see = smooth((t - NOTICE) / 0.08)                  # eyes widen, beak drops, head jerks back
    calm = smooth((t - 1.75) / 0.4)
    c.eyes = 1 + 0.32 * see * (1 - calm)
    c.beak = 16 * see * (1 - smooth((t - 1.55) / 0.3))
    c.nod += -9 * see * (1 - smooth((t - 1.2) / 0.5))
    c.lean += -4 * see * (1 - smooth((t - 1.2) / 0.5))
    crouch = I.bump(t, CROUCH - 0.08, JUMP + 0.02)     # flinch down before the jump
    c.stretch += -0.05 * crouch
    c.root_y += 10 * crouch
    u = R.clamp01((t - JUMP) / (LAND - JUMP))
    air = math.sin(math.pi * u) if JUMP < t < LAND else 0.0
    c.root_y += -95 * air
    c.stretch += 0.05 * air
    land = I.bump(t, LAND - 0.02, LAND + 0.16)         # hard landing
    c.stretch += -0.06 * land
    c.root_y += 9 * land
    # flippers fling out, flap twice in the air, then come down slowly
    up = smooth((t - (CROUCH - 0.02)) / 0.14) * (1 - smooth((t - 1.25) / 0.6))
    flap = math.sin(2 * math.pi * 2 * u) if JUMP < t < LAND else 0.0
    c.wing += up * (26 + 6 * flap)
    # feet leave the ground with the body, dangling, toes down
    for name, side in (("foot-back", -1), ("foot-front", 1)):
        c.feet[name] = (side * 10 * air, -95 * air + 12 * air, 14 * air * side)
    # trembling while it settles
    shiver = I.bump(t, LAND + 0.1, 2.1) * 1.0
    c.lean += shiver * 1.5 * math.sin(2 * math.pi * 13 * t)
    c.head_dx += shiver * 2.5 * math.sin(2 * math.pi * 13 * t + 1)
    return c


CLIPS = {"guard": (guard, BREATH), "startle": (startle, STARTLE)}


def pose(fn, t):
    h = 1 / 240
    return Pose(fn(t), (fn(t + h).wing - fn(t - h).wing) / (2 * h))


# ---- drawing -----------------------------------------------------------------------------------------
EYES = [((595, 262), 17.5, 32.0), ((697, 255), 12.5, 28.0)]
BROWS = [((560, 214), (626, 236)), ((671, 230), (722, 206))]   # inner ends low: angry
BEAK_HINGE = (632, 334)
UPPER_CLIP = "M600 250 L800 250 L800 322 C776 322 720 336 670 338 C650 338 636 334 600 334 Z"
LOWER_CLIP = "M600 334 C636 334 650 338 670 338 C720 336 776 322 800 322 L800 400 L600 400 Z"
WING_TIP = penguin.to_far((236, 742))   # far flipper tip in the art (the grip)


def eyes(p, c, uid):
    """Eye ellipses, scaled about their centres; squint closes them from the top under a lid."""
    out = []
    for k, ((cx, cy), rx, ry) in enumerate(EYES):
        x, y = p.body(cx, cy)
        ang = p.angle(p.height(cy))
        rx2, ry2 = rx * c.eyes * p.sx, ry * c.eyes * p.sy
        hx, hy = p.body(cx - rx * 0.3 + (0.0 if k else 0.0), cy - ry * 0.47)
        lid = c.squint
        clip = ""
        if lid > 0:
            # keep only the part below the lid line
            top = y - ry2 + 2 * ry2 * lid
            clip = (f'<clipPath id="lid{uid}{k}"><rect x="{x - rx2 * 2:.1f}" y="{top:.1f}" width="{rx2 * 4:.1f}" '
                    f'height="{ry2 * 3:.1f}" transform="rotate({ang:.2f} {x:.1f} {y:.1f})"/></clipPath>')
        g = (f'<g{f" clip-path={chr(34)}url(#lid{uid}{k}){chr(34)}" if lid > 0 else ""}>'
             f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{rx2:.1f}" ry="{ry2:.1f}" fill="#1C0F0F" '
             f'transform="rotate({ang:.2f} {x:.1f} {y:.1f})"/>'
             f'<ellipse cx="{hx:.1f}" cy="{hy:.1f}" rx="{(5 if k == 0 else 4) * c.eyes:.1f}" '
             f'ry="{(8 if k == 0 else 7) * c.eyes:.1f}" fill="#FFFFFF"/></g>')
        out.append(clip + g)
    return "".join(out)


def brows(p, c):
    if c.brows <= 0:
        return ""
    out = []
    for a, b in BROWS:
        (x0, y0), (x1, y1) = p.body(*a), p.body(*b)
        out.append(f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" fill="none" stroke="#1C0F0F" '
                   f'stroke-width="13" opacity="{c.brows:.2f}"/>')
    return "".join(out)


def beak(p, c, uid):
    """The beak in two halves: the lower one drops about the hinge over a dark mouth."""
    whole = R.deform_group(R.PART["beak"], p.body)
    if c.beak <= 0.2:
        return whole
    hx, hy = p.body(*BEAK_HINGE)
    upper = R.deform_d(UPPER_CLIP, p.body)
    lower = R.deform_d(LOWER_CLIP, p.body)
    fill = R.deform_group(R.PART["beak"].replace('<path fill="none" stroke-width="6"', '<path fill="none" stroke="none"'),
                          p.body)
    mouth = (f'<g clip-path="url(#lo{uid})">'
             + R.deform_group(R.PART["beak"].replace('fill="#FF7A00"', 'fill="#6B1F1F"'), p.body) + '</g>')
    return (f'<clipPath id="up{uid}"><path d="{upper}"/></clipPath><clipPath id="lo{uid}"><path d="{lower}"/></clipPath>'
            f'{mouth}'
            f'<g transform="rotate({c.beak:.2f} {hx:.1f} {hy:.1f})"><g clip-path="url(#lo{uid})">{fill}</g></g>'
            f'<g clip-path="url(#up{uid})">{fill}</g>')


def sign(p, c):
    """A round "no entry" sign on a wooden pole, gripped at the far flipper's tip."""
    if c.sign is None:
        return ""
    gx, gy = p.wing_fn("wing-far")(*WING_TIP)
    a = math.radians(c.sign)
    dx, dy = math.cos(a), math.sin(a)
    x0, y0 = gx - dx * 40, gy - dy * 40
    x1, y1 = gx + dx * 300, gy + dy * 300
    sx, sy = gx + dx * 370, gy + dy * 370
    return (f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#111111" stroke-width="30" fill="none"/>'
            f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#B07434" stroke-width="16" fill="none"/>'
            f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="88" fill="#E53935"/>'
            f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="72" fill="none" stroke="#FFFFFF" stroke-width="8"/>'
            f'<rect x="{sx - 50:.1f}" y="{sy - 14:.1f}" width="100" height="28" rx="6" fill="#FFFFFF" stroke-width="6"/>')


def scene(p, cloth, uid):
    c = p.c
    parts = []
    for name in ("foot-back", "foot-front"):
        dx, dy, toe = c.feet[name]
        ax, ay = R.ANKLE[name]
        parts.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({toe:.2f} {ax} {ay})">{R.PART[name]}</g>')
    far, near = p.wing_fn("wing-far"), p.wing_fn("wing-near")
    parts.append(sign(p, c))                 # behind the far flipper, which grips the pole
    parts.append(I.clip(f"fw{uid}", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    parts.append(R.deform_group(R.PART["body"], p.body))
    parts.append(R.deform_group(R.PART["belly"], p.body))
    parts.append(R.deform_group(R.PART["wing-near"], near))
    parts.append(eyes(p, c, uid))
    parts.append(brows(p, c))
    parts.append(beak(p, c, uid))
    parts.append(R.deform_group(cloth["sleeve-far"], far))
    parts.append(R.deform_group(cloth["sweater"], I.skinned(p)))
    parts.append(I.clip(f"nw{uid}", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)))
    parts.append(R.deform_group(cloth["sleeve-near"], near))
    parts.append(R.deform_group(cloth["collar"], p.body))
    parts.append(R.deform_group(cloth["hat"], p.body))
    # ground shadow, shrinking while it is in the air
    lift = -min(0.0, c.root_y)
    shadow = (f'<ellipse cx="540" cy="1003" rx="{235 - 0.8 * lift:.1f}" ry="{19 - 0.07 * lift:.1f}" '
              f'fill="#7FA4B8" opacity="0.45" stroke="none"/>')
    return shadow + "\n".join(parts)


VIEW = (-60, -190, 1340, 1340)


def svg(inner):
    x, y, w, h = VIEW
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{x} {y} {w} {h}">'
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#CFE6F2"/>'
            f'<rect x="{x}" y="985" width="{w}" height="{y + h - 985}" fill="#EEF6FB"/>'
            f'<g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">{inner}</g></svg>')


def main():
    work = sys.argv[1]
    colors = {"guard": sys.argv[2] if len(sys.argv) > 2 else "#1E88E5",
              "startle": sys.argv[3] if len(sys.argv) > 3 else "#E53935"}
    out = os.path.join(work, "frames")
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))
    jobs = {}
    for name, (fn, length) in CLIPS.items():
        cloth = outfit.parts(colors[name])
        n = round(length * FPS)
        paths = []
        for k in range(n):
            path = os.path.join(out, f"{name}_{k:03d}.svg")
            open(path, "w").write(svg(scene(pose(fn, k / FPS), cloth, f"{name}{k}")))
            paths.append(path)
        jobs[name] = paths
    size = 512
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda s: subprocess.run(["magick", s, "-resize", f"{size}x{size}", s[:-4] + ".png"], check=True),
                      [p for ps in jobs.values() for p in ps]))
    ims = {name: [Image.open(p[:-4] + ".png").convert("RGB") for p in ps] for name, ps in jobs.items()}
    ms = int(1000 / FPS)
    # the guard loops twice; the startle holds its last frame a moment
    guard_frames = ims["guard"] * 2
    startle_frames = ims["startle"] + [ims["startle"][-1]] * 10
    for name, frames in (("guard", guard_frames), ("startle", startle_frames)):
        frames[0].save(os.path.join(work, f"{name}.gif"), save_all=True, append_images=frames[1:], duration=ms, loop=0)
    n = max(len(guard_frames), len(startle_frames))
    both = []
    for k in range(n):
        f = Image.new("RGB", (size * 2 + 8, size), (255, 255, 255))
        f.paste(guard_frames[k % len(guard_frames)], (0, 0))
        f.paste(startle_frames[min(k, len(startle_frames) - 1)], (size + 8, 0))
        both.append(f)
    both[0].save(os.path.join(work, "block.gif"), save_all=True, append_images=both[1:], duration=ms, loop=0)
    # contact sheet: 8 guard frames over one loop, 12 startle frames over the clip
    rows = [("guard", [round(i * len(ims["guard"]) / 8) for i in range(8)]),
            ("startle", [round(i * (len(ims["startle"]) - 1) / 11) for i in range(12)])]
    th = size // 2
    sheet = Image.new("RGB", (th * 12, th * 2), "white")
    d = ImageDraw.Draw(sheet)
    for r, (name, ks) in enumerate(rows):
        for i, k in enumerate(ks):
            sheet.paste(ims[name][k].resize((th, th)), (i * th, r * th))
            d.text((i * th + 3, r * th + 3), f"{name} {k / FPS:.2f}s", fill=(0, 0, 0))
    sheet.save(os.path.join(work, "block_sheet.png"))
    print({name: len(v) for name, v in ims.items()}, "frames at", FPS, "fps")


if __name__ == "__main__":
    main()
