"""Ice fishing preview on the idle rig (idle.IdlePose): one clip, not in the game yet.

  fish  one shot (LENGTH s), starts and ends waiting with the line in the water: it stands at the ice hole
        holding a rod in its far flipper (the one in front), the float bobbing; a nibble or two, the float
        goes under, eyes go wide; it yanks the rod up, a fish flies out of the hole on the line in an arc
        and lands right in its open beak; gulp; a happy double hop with eyes squeezed shut; then it lowers
        the rod and the float drops back into the hole.

Rig limit: the far flipper turns out at most ~26 deg; the rod gives the reach, the fish comes to the beak.

usage: python -I fish.py <work> [color]
Writes <work>/frames/*.svg|png, fish.gif and fish_sheet.png.
"""
import math, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import outfit
import run as R
import idle as I
import block as B
from PIL import Image, ImageDraw

FPS = 20
WAIT_ROD, PULL_ROD = -32, -70  # rod angle, deg from horizontal (- = up)
ROD_LEN = 540
HOLE = (1080, 996)             # centre of the hole's water
HOLE_R = (130, 34)
NIBBLES = (1.5, 1.85)          # s
DIP, PULL = 2.1, 2.25          # float goes under; yank
CATCH = 2.8                    # fish reaches the beak
GULP = 3.0
HAPPY = 3.1
RECAST, SPLASH = 3.85, 4.35    # rod comes down; float back in the water
LENGTH = 4.8
BEAK_TIP = (782, 330)          # art point the fish goes into
LINE_LEN = 230                 # float hanging below the rod tip while it is out of the water


def smooth(x): return R.smootherstep(R.clamp01(x))


def fish_channels(t):
    c = B.Channels()
    I.breathe(t, c)
    c.far = 10                                            # holds the rod out a little
    c.sign = WAIT_ROD                                     # rod angle (reuses the sign channel)
    # nibbles: a twitch of attention each
    for a in NIBBLES:
        k = I.bump(t, a, a + 0.18)
        c.lean += 2.5 * k
        c.sign += 5 * k
    see = smooth((t - DIP + 0.05) / 0.08) * (1 - smooth((t - CATCH + 0.1) / 0.2))
    c.eyes = 1 + 0.28 * see
    c.lean += 3 * smooth((t - DIP) / 0.1) * (1 - smooth((t - PULL) / 0.1))
    # the yank: rod up, flipper out, leaning back
    yank = smooth((t - PULL) / 0.16) * (1 - smooth((t - RECAST) / 0.5))
    c.sign += (PULL_ROD - WAIT_ROD) * yank
    c.far += 16 * I.bump(t, PULL - 0.02, PULL + 0.55)
    c.lean += -6 * I.bump(t, PULL, PULL + 0.7)
    c.nod += -8 * I.bump(t, PULL + 0.1, CATCH + 0.1)     # head tips back, beak up for the catch
    # beak opens for the fish, snaps shut, gulp
    c.beak = 18 * smooth((t - CATCH + 0.3) / 0.15) * (1 - smooth((t - GULP) / 0.06))
    gulp = I.bump(t, GULP - 0.08, GULP + 0.22)
    c.stretch += 0.03 * gulp
    c.head_dy += -6 * gulp
    # happy: eyes squeezed, two hops, flippers flutter
    happy = I.envelope(t, HAPPY, RECAST + 0.1, 0.1)
    c.happy = happy
    for a in (HAPPY + 0.05, HAPPY + 0.35):
        h = I.bump(t, a, a + 0.28)
        c.root_y += -26 * h
        c.wing += 10 * h
        for name in c.feet:
            dx, dy, toe = c.feet[name]
            c.feet[name] = (dx, dy - 22 * h, toe)
    c.squint = max(c.squint, I.blink_at(t, 0.7))
    c.squint = max(c.squint, I.blink_at(t, SPLASH + 0.1))
    return c


def happy_eyes(p, c):
    """Closed happy eyes (upturned arcs, "^ ^") in place of the eye ellipses."""
    out = []
    for (cx, cy), rx, ry in B.EYES:
        a, m, b = p.body(cx - rx * 1.1, cy + 6), p.body(cx, cy - ry * 0.75), p.body(cx + rx * 1.1, cy + 6)
        out.append(f'<path d="M{a[0]:.1f} {a[1]:.1f} Q{m[0]:.1f} {m[1]:.1f} {b[0]:.1f} {b[1]:.1f}" fill="none" '
                   f'stroke="#1C0F0F" stroke-width="10"/>')
    return "".join(out)


def pose(t):
    h = 1 / 240
    return B.Pose(fish_channels(t), (fish_channels(t + h).wing - fish_channels(t - h).wing) / (2 * h))


# ---- drawing -----------------------------------------------------------------------------------------
def rod(p, c):
    """The rod from the far flipper's tip: wooden pole with a reel near the grip. Returns svg and tip."""
    gx, gy = p.wing_fn("wing-far")(*B.WING_TIP)
    a = math.radians(c.sign)
    dx, dy = math.cos(a), math.sin(a)
    x0, y0 = gx - dx * 60, gy - dy * 60
    x1, y1 = gx + dx * ROD_LEN, gy + dy * ROD_LEN
    rx, ry = gx - dx * 28 + dy * 22, gy - dy * 28 - dx * 22
    svg = (f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#111111" stroke-width="24" fill="none"/>'
           f'<path d="M{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f}" stroke="#A86B32" stroke-width="11" fill="none"/>'
           f'<circle cx="{rx:.1f}" cy="{ry:.1f}" r="17" fill="#6E7B85"/>')
    return svg, (x1, y1)


def float_svg(x, y, under=0.0):
    """The red and white float; `under` sinks it into the water (clipped by the caller)."""
    y += 26 * under
    return (f'<g><circle cx="{x:.1f}" cy="{y:.1f}" r="15" fill="#FFFFFF"/>'
            f'<path d="M{x - 15:.1f} {y:.1f} A15 15 0 0 1 {x + 15:.1f} {y:.1f} Z" fill="#E53935"/>'
            f'<circle cx="{x:.1f}" cy="{y:.1f}" r="15" fill="none"/></g>')


def fish_svg(x, y, ang, s=1.0, flap=0.0):
    """A little silver-blue fish centred on (x, y), facing `ang` degrees (mouth first)."""
    tail = 14 * flap
    return (f'<g transform="translate({x:.1f} {y:.1f}) rotate({ang:.1f}) scale({s:.3f})">'
            f'<path d="M-50 0 L-82 {-24 + tail:.1f} L-78 {24 + tail:.1f} Z" fill="#5D8FB5"/>'
            f'<ellipse cx="0" cy="0" rx="56" ry="25" fill="#8EC3E6"/>'
            f'<path d="M-30 -6 Q0 -16 30 -8" stroke="#D8EEFA" stroke-width="7" fill="none"/>'
            f'<circle cx="32" cy="-6" r="5" fill="#111111" stroke="none"/></g>')


def ripples(u, x, y):
    if not 0 <= u <= 1:
        return ""
    return "".join(f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{(20 + 90 * v) :.1f}" ry="{(6 + 24 * v):.1f}" '
                   f'fill="none" stroke="#DDEFF8" stroke-width="5" opacity="{1 - v:.2f}"/>'
                   for v in (u, max(0.0, u - 0.3)) if v > 0)


def hole():
    (x, y), (rx, ry) = HOLE, HOLE_R
    return (f'<ellipse cx="{x}" cy="{y}" rx="{rx + 22}" ry="{ry + 10}" fill="#DCEBF3"/>'
            f'<ellipse cx="{x}" cy="{y}" rx="{rx}" ry="{ry}" fill="#1E5878"/>'
            f'<path d="M{x - rx + 10} {y - 6} Q{x} {y - ry - 8} {x + rx - 10} {y - 6}" stroke="#2F7699" '
            f'stroke-width="10" fill="none"/>')


def water_clip(uid):
    (x, y), (rx, ry) = HOLE, HOLE_R
    # what is above the water's surface plus the water itself (a float going under disappears below it)
    return (f'<clipPath id="w{uid}"><rect x="{x - 400}" y="-400" width="800" height="{y + 400}"/>'
            f'<ellipse cx="{x}" cy="{y}" rx="{rx}" ry="{ry}"/></clipPath>')


def tackle(p, c, t, uid):
    """Line, float and fish: under the rod tip in every phase."""
    rod_svg, (tx, ty) = rod(p, c)
    out = []
    hx, hy = HOLE
    bob = 3 * math.sin(2 * math.pi * t / 1.3)
    if t < PULL:
        under = max(I.bump(t, NIBBLES[0], NIBBLES[0] + 0.2) * 0.4, I.bump(t, NIBBLES[1], NIBBLES[1] + 0.2) * 0.5,
                    smooth((t - DIP) / 0.08))
        fx, fy = hx + 10, hy - 4 + bob
        out.append(f'<path d="M{tx:.1f} {ty:.1f} Q{(tx + fx) / 2 + 20:.1f} {(ty + fy) / 2:.1f} {fx:.1f} {fy:.1f}" '
                   f'stroke="#33414A" stroke-width="4" fill="none"/>')
        out.append(f'<g clip-path="url(#w{uid})">{float_svg(fx, fy, under)}</g>')
        for a in NIBBLES + (DIP,):
            out.append(ripples((t - a) / 0.5, fx, hy))
    elif t < CATCH:
        # fish on the line: out of the hole and up in an arc into the beak
        u = (t - PULL - 0.05) / (CATCH - PULL - 0.05)
        bx, by = p.body(*BEAK_TIP)
        if u < 0:
            fxp, fyp, ang = hx + 10, hy, -90
        else:
            k = smooth(u) * 0.6 + u * 0.4
            fxp = hx + 10 + (bx + 30 - hx - 10) * k
            fyp = hy + (by - hy) * k - 300 * math.sin(math.pi * u)
            ang = -90 + 270 * k                                      # tumbles over, mouth-first into the beak
        mx, my = fxp + 50 * math.cos(math.radians(ang)), fyp + 50 * math.sin(math.radians(ang))
        out.append(f'<path d="M{tx:.1f} {ty:.1f} L{mx:.1f} {my:.1f}" stroke="#33414A" stroke-width="4" fill="none"/>')
        bfx, bfy = tx + (mx - tx) * 0.55, ty + (my - ty) * 0.55
        out.append(float_svg(bfx, bfy))
        out.append(fish_svg(fxp, fyp, ang, 1.0, math.sin(t * 40)))
        out.append(ripples((t - PULL) / 0.5, hx, hy))
    else:
        # unhooked: the fish goes down the beak; the float swings below the rod tip, then is lowered back in
        bx, by = p.body(*BEAK_TIP)
        g = (t - CATCH) / (GULP - CATCH)
        if g < 1:
            out.append(f'<clipPath id="m{uid}"><rect x="{bx - 14:.1f}" y="{by - 200:.1f}" width="600" '
                       f'height="400"/></clipPath>'
                       f'<g clip-path="url(#m{uid})">{fish_svg(bx + 30 - 150 * g, by + 4, 180, 1.0, math.sin(t * 40))}</g>')
        swing = 18 * math.exp(-(t - CATCH) * 2.5) * math.sin((t - CATCH) * 9)
        hang = (tx + swing, ty + LINE_LEN)
        k = smooth((t - RECAST) / (SPLASH - RECAST))
        fx, fy = hang[0] + (hx + 10 - hang[0]) * k, hang[1] + (hy - 4 + bob - hang[1]) * k
        out.append(f'<path d="M{tx:.1f} {ty:.1f} L{fx:.1f} {fy:.1f}" stroke="#33414A" stroke-width="4" fill="none"/>')
        out.append(f'<g clip-path="url(#w{uid})">{float_svg(fx, fy)}</g>')
        out.append(ripples((t - SPLASH) / 0.6, hx + 10, hy))
    return rod_svg, "".join(out)


def scene(t, cloth, uid):
    p = pose(t)
    c = p.c
    parts = []
    for name in ("foot-back", "foot-front"):
        dx, dy, toe = c.feet[name]
        ax, ay = R.ANKLE[name]
        parts.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({toe:.2f} {ax} {ay})">{R.PART[name]}</g>')
    far, near = p.wing_fn("wing-far"), p.wing_fn("wing-near")
    rod_svg, tackle_svg = tackle(p, c, t, uid)
    parts.append(rod_svg)                         # behind the far flipper, which grips it
    parts.append(I.clip(f"fw{uid}", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    parts.append(R.deform_group(R.PART["body"], p.body))
    parts.append(R.deform_group(R.PART["belly"], p.body))
    parts.append(R.deform_group(R.PART["wing-near"], near))
    parts.append(happy_eyes(p, c) if getattr(c, "happy", 0.0) > 0.5 else B.eyes(p, c, uid))
    parts.append(B.beak(p, c, uid))
    parts.append(R.deform_group(cloth["sleeve-far"], far))
    parts.append(R.deform_group(cloth["sweater"], I.skinned(p)))
    parts.append(I.clip(f"nw{uid}", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)))
    parts.append(R.deform_group(cloth["sleeve-near"], near))
    parts.append(R.deform_group(cloth["collar"], p.body))
    parts.append(R.deform_group(cloth["hat"], p.body))
    lift = -min(0.0, c.root_y)
    shadow = (f'<ellipse cx="540" cy="1003" rx="{235 - 0.8 * lift:.1f}" ry="19" fill="#7FA4B8" opacity="0.45" '
              f'stroke="none"/>')
    return water_clip(uid) + hole() + shadow + "\n".join(parts) + tackle_svg


VIEW = (-60, -190, 1500, 1340)


def svg(inner):
    x, y, w, h = VIEW
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{x} {y} {w} {h}">'
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#CFE6F2"/>'
            f'<rect x="{x}" y="985" width="{w}" height="{y + h - 985}" fill="#EEF6FB"/>'
            f'<g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">{inner}</g></svg>')


def main():
    work = sys.argv[1]
    color = sys.argv[2] if len(sys.argv) > 2 else "#F4511E"
    out = os.path.join(work, "frames")
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))
    cloth = outfit.parts(color)
    n = round(LENGTH * FPS)
    paths = []
    for k in range(n):
        path = os.path.join(out, f"fish_{k:03d}.svg")
        open(path, "w").write(svg(scene(k / FPS, cloth, f"f{k}")))
        paths.append(path)
    w = 560
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda s: subprocess.run(["magick", s, "-resize", f"{w}x", s[:-4] + ".png"], check=True), paths))
    ims = [Image.open(p[:-4] + ".png").convert("RGB") for p in paths]
    frames = ims * 2
    frames[0].save(os.path.join(work, "fish.gif"), save_all=True, append_images=frames[1:],
                   duration=int(1000 / FPS), loop=0)
    ks = [round(i * (n - 1) / 15) for i in range(16)]
    tw, th = w // 2, ims[0].height // 2
    sheet = Image.new("RGB", (tw * 8, th * 2), "white")
    d = ImageDraw.Draw(sheet)
    for i, k in enumerate(ks):
        x, y = (i % 8) * tw, (i // 8) * th
        sheet.paste(ims[k].resize((tw, th)), (x, y))
        d.text((x + 3, y + 3), f"{k / FPS:.2f}s", fill=(0, 0, 0))
    sheet.save(os.path.join(work, "fish_sheet.png"))
    print(n, "frames at", FPS, "fps")


if __name__ == "__main__":
    main()
