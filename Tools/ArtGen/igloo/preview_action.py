"""Preview of the night action seen from outside an igloo while a visitor is inside: the window lamp
flickers off and on and the igloo shakes in bursts, then it all settles.

Usage: python -I preview_action.py <work> [seconds]

Writes to <work>: igloo_full.png, window_dark.png (the lamp-off pane, full canvas, also exported as
IglooWindowDark.png cropped), action_day.gif, action_night.gif, action.gif (both side by side) and
action_timeline.png (light and shake curves). The night look approximates the game's lighting
(DayNightCycle night ambient and the window's point light); the game renders it with Light2D.
"""
import math, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import igloo as g
from PIL import Image, ImageChops, ImageDraw

W = g.W_CANVAS
PIVOT = (0.5095694 * W, (1 - 0.16507177) * W)   # Igloo.png sprite pivot (base of the art), canvas px
FPS = 25
LEAD = 0.6                                      # calm seconds before and after the stay

# ---- timeline (seconds from the moment the visitor is inside) --------------------------------------
# Lamp: (time, lit) switch points; each switch fades over FADE seconds, so it flickers rather than blinks.
FADE = 0.05
def lamp_switches(stay):
    k = stay / 4.0                              # authored for a 4 s stay, scaled to the actual one
    pts = [(0.45, 0), (0.55, 1), (0.62, 0), (0.70, 1), (0.95, 0), (1.35, 1), (1.42, 0),
           (2.05, 1), (2.12, 0), (2.18, 1), (2.25, 0), (3.05, 1), (3.12, 0), (3.30, 1)]
    return [(t * k, s) for t, s in pts]

# Shake bursts: (start, length, strength). Each is a decaying wobble about the base pivot.
def shake_bursts(stay):
    k = stay / 4.0
    return [(0.95 * k, 0.45 * k, 1.0), (1.5 * k, 0.55 * k, 0.8), (2.3 * k, 0.6 * k, 1.15), (3.0 * k, 0.25 * k, 0.45)]

TILT_DEG = 2.4      # peak tilt at strength 1
WOBBLE_HZ = 7.5
SQUASH = 0.035      # peak vertical squash at strength 1 (the dome bulges sideways by half that)
HOP_PX = 7.0        # peak lift of the whole igloo, canvas px

def lamp(t, stay):
    lit = 1.0
    for at, s in lamp_switches(stay):
        if t >= at:
            lit = s if t >= at + FADE else lit + (s - lit) * (t - at) / FADE
    return lit

def shake(t, stay):
    """Returns (tilt deg, squash, lift px) at time t."""
    tilt = squash = lift = 0.0
    for start, length, strength in shake_bursts(stay):
        u = (t - start) / length
        if 0.0 <= u <= 1.0:
            env = strength * math.sin(math.pi * min(1.0, u * 3.0) / 2) * (1 - u) ** 1.3
            ph = 2 * math.pi * WOBBLE_HZ * (t - start)
            tilt += TILT_DEG * env * math.sin(ph)
            squash += SQUASH * env * abs(math.sin(ph))
            lift += HOP_PX * env * abs(math.cos(ph))
    return tilt, squash, lift

# ---- night lighting approximation ------------------------------------------------------------------
NIGHT_AMBIENT = tuple(c * 0.42 for c in (0.42, 0.52, 0.85))   # DayNightCycle night color * intensity
WINDOW_LIGHT = tuple(c * 1.4 for c in (1.0, 0.78, 0.45))      # GameLayout window light color * intensity
LIGHT_R = 0.75 * 300                                          # outer radius 0.75 units at 300 px/unit
LIGHT_R_IN = 0.08 * 300

# ---- rendering -------------------------------------------------------------------------------------
def render(svg, png):
    with open(png[:-4] + ".svg", "w") as f:
        f.write(svg)
    subprocess.run(["magick", "-background", "none", png[:-4] + ".svg", png], check=True)

def place(img, tilt, squash, lift, scale):
    """The igloo layer rotated and squashed about its base pivot, at `scale` of the canvas."""
    a = math.radians(tilt)
    sx, sy = (1 + squash / 2) * scale, (1 - squash) * scale
    px, py = PIVOT
    # forward map: p' = P*scale + R(a) * S * (p - P) - (0, lift*scale); Pillow needs the inverse
    cos, sin = math.cos(a), math.sin(a)
    ox, oy = px * scale, py * scale - lift * scale
    # inverse of [[cos*sx, -sin*sy], [sin*sx, cos*sy]]
    det = sx * sy
    ia, ib = cos * sy / det, sin * sy / det
    ic, id_ = -sin * sx / det, cos * sx / det
    size = (int(W * scale), int(W * scale))
    coeffs = (ia, ib, px - ia * ox - ib * oy, ic, id_, py - ic * ox - id_ * oy)
    return img.transform(size, Image.AFFINE, coeffs, resample=Image.BICUBIC), (ox, oy, cos, sin, sx, sy)

_falloff = None

def falloff():
    """Window light falloff (0..255) in a square of the light's outer diameter, at preview scale."""
    global _falloff
    if _falloff is None:
        r_out, r_in = LIGHT_R * S, LIGHT_R_IN * S
        n = int(2 * r_out) + 1
        _falloff = Image.new("L", (n, n))
        px = _falloff.load()
        for y in range(n):
            for x in range(n):
                u = min(1.0, max(0.0, (math.hypot(x - r_out, y - r_out) - r_in) / (r_out - r_in)))
                px[x, y] = int(255 * (1 - u) ** 2)
    return _falloff

def light_map(size, center, strength):
    """Per-pixel light multiplier (RGB, 0..255 = 0..1) for the night look: ambient plus the window light."""
    lm = Image.new("RGB", size, tuple(int(255 * c) for c in NIGHT_AMBIENT))
    f = falloff()
    glow = Image.merge("RGB", [f.point(lambda v, c=c: min(255, int(v * c * strength))) for c in WINDOW_LIGHT])
    layer = Image.new("RGB", size)
    layer.paste(glow, (int(center[0] - f.width / 2), int(center[1] - f.height / 2)))
    return ImageChops.add(lm, layer)

S = 0.42   # preview scale of the 1254 canvas
CROP = (90, 120, 1180, 1100)   # canvas region around the igloo (with room for the shake)

def main():
    work = sys.argv[1]
    stay = float(sys.argv[2]) if len(sys.argv) > 2 else 4.0
    os.makedirs(work, exist_ok=True)
    full = os.path.join(work, "igloo_full.png")
    dark = os.path.join(work, "window_dark.png")
    render(g.build(0.0), full)
    render(g.window_overlay(lit=False), dark)
    body = Image.open(full).convert("RGBA")
    pane = Image.open(dark).convert("RGBA")
    bbox = pane.getbbox()
    pane.crop(bbox).save(os.path.join(work, "IglooWindowDark.png"))
    print("dark pane crop", bbox)

    wp = g.proj(g.dome(*g.find_window()))
    crop = tuple(int(c * S) for c in CROP)
    bg_day = Image.new("RGBA", (int(W * S), int(W * S)), (232, 242, 250, 255))
    total = stay + 2 * LEAD
    frames_day, frames_night = [], []
    curve = []
    for k in range(int(total * FPS)):
        t = k / FPS - LEAD
        lit = lamp(t, stay)
        tilt, squash, lift = shake(t, stay)
        curve.append((t, lit, tilt))
        layer = body.copy()
        off = pane.copy()
        off.putalpha(off.getchannel("A").point(lambda a: int(a * (1 - lit))))
        layer.alpha_composite(off)
        moved, (ox, oy, cos, sin, sx, sy) = place(layer, tilt, squash, lift, S)
        day = bg_day.copy()
        day.alpha_composite(moved)
        frames_day.append(day.crop(crop).convert("RGB"))
        # window centre after the same transform, for the night light
        dx, dy = wp[0] - PIVOT[0], wp[1] - PIVOT[1]
        wc = (ox + cos * sx * dx - sin * sy * dy, oy + sin * sx * dx + cos * sy * dy)
        night = ImageChops.multiply(day.convert("RGB"), light_map(day.size, wc, lit))
        frames_night.append(night.crop(crop))
    ms = int(1000 / FPS)
    frames_day[0].save(os.path.join(work, "action_day.gif"), save_all=True, append_images=frames_day[1:], duration=ms, loop=0)
    frames_night[0].save(os.path.join(work, "action_night.gif"), save_all=True, append_images=frames_night[1:], duration=ms, loop=0)
    both = []
    for d, n in zip(frames_day, frames_night):
        f = Image.new("RGB", (d.width * 2 + 8, d.height), (255, 255, 255))
        f.paste(d, (0, 0)); f.paste(n, (d.width + 8, 0))
        both.append(f)
    both[0].save(os.path.join(work, "action.gif"), save_all=True, append_images=both[1:], duration=ms, loop=0)

    # timeline: lamp (yellow, top) and tilt (blue, bottom) over time; grey marks the stay
    tw, th = 900, 220
    tl = Image.new("RGB", (tw, th), (255, 255, 255))
    d = ImageDraw.Draw(tl)
    x = lambda t: 20 + (t + LEAD) / total * (tw - 40)
    d.rectangle((x(0), 0, x(stay), th), fill=(238, 238, 238))
    d.line([(x(t), 90 - 70 * lit) for t, lit, _ in curve], fill=(230, 160, 0), width=3)
    d.line([(x(t), 160 - 18 * tilt) for t, _, tilt in curve], fill=(30, 110, 200), width=2)
    for s in range(int(math.ceil(total))):
        d.text((x(s - LEAD) + 2, th - 14), f"{s - LEAD:.1f}s", fill=(80, 80, 80))
    d.text((24, 4), "lamp on/off", fill=(180, 120, 0))
    d.text((24, 112), "tilt", fill=(30, 110, 200))
    tl.save(os.path.join(work, "action_timeline.png"))
    print(len(frames_day), "frames at", FPS, "fps;", f"stay {stay:.1f} s")

if __name__ == "__main__":
    main()
