"""Preview of the night heal seen from outside an igloo while the doctor is inside: the opposite of the
knock-out's chaos. The warm lamp dims softly, the igloo beats like a heart (lub-dub) with a mint glow in
the window, the beats quicken and grow, then the igloo hops once with a bright flash, plus signs and
sparkles rise from the dome, and the warm lamp comes back steady.

Usage: python -I preview_heal.py <work> [seconds]

Writes to <work>: heal_day.gif, heal_night.gif, heal.gif (both side by side), heal_timeline.png (lamp,
mint glow and swell curves), heal_sheet.png and heal_key0..6.png (key frames), and the SVGs it renders (window_heal.svg:
the mint pane; heal_plus.svg / heal_sparkle.svg: the particles). The night look approximates the game's
lighting like preview_action.py; the particles are drawn unlit (emissive).
"""
import math, os, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import igloo as g
import preview_action as pa
from PIL import Image, ImageChops, ImageDraw

W = g.W_CANVAS
PIVOT = pa.PIVOT
FPS = pa.FPS
LEAD = pa.LEAD
S = pa.S
CROP = (60, 0, 1200, 1100)   # canvas region: room above the dome for the rising particles

MINT_LIGHT = (0.45, 1.0, 0.7)   # night window light colour while it glows mint
MINT_GAIN = 1.5

# ---- timeline (authored for a 4 s stay, scaled to the actual one) -----------------------------------
def times(stay):
    k = stay / 4.0
    return dict(
        k=k,
        dim=(0.35 * k, 0.9 * k),                                   # warm lamp fades down to DIM_TO
        beats=[(t * k, s) for t, s in ((1.0, 0.45), (1.5, 0.6), (1.92, 0.75), (2.28, 0.9), (2.56, 1.0))],
        pop=2.75 * k,                                              # the revival: hop, flash, particles
    )
DIM_TO = 0.3
BEAT_GAP = 0.17                     # lub-dub spacing (s, not scaled: a heartbeat keeps its own shape)
SWELL = 0.022                       # peak uniform swell at beat strength 1
STRETCH = 0.012                     # extra vertical stretch at beat strength 1
HOP_PX, HOP_S = 22.0, 0.32          # pop: lift (canvas px) and air time (s)
LAND_SQUASH, LAND_S = 0.05, 0.35    # landing squash and its settle time

def smoothstep(a, b, t):
    u = min(1.0, max(0.0, (t - a) / (b - a)))
    return u * u * (3 - 2 * u)

def pulse(u):
    """One heartbeat thump: quick rise, softer fall (u = seconds since the thump)."""
    return math.exp(-(u / 0.045) ** 2) if u < 0 else math.exp(-u / 0.11)

def beat(t, tl):
    return sum(s * (pulse(t - at) + 0.6 * pulse(t - at - BEAT_GAP)) for at, s in tl["beats"])

def warm(t, tl):
    """Warm lamp level (1 = normal; above 1 during the comeback)."""
    d0, d1 = tl["dim"]
    p = tl["pop"]
    lvl = 1 - (1 - DIM_TO) * smoothstep(d0, d1, t)
    if t > p:
        u = t - p
        lvl = DIM_TO + (1 - DIM_TO) * smoothstep(0.0, 0.3, u) + 0.35 * math.sin(math.pi * min(1.0, u / 0.7)) * (u < 0.7)
    return lvl

def mint(t, tl):
    """Mint glow in the window (0..1): a faint hum while the doctor works, a thump per beat, the pop flash."""
    d1, p = tl["dim"][1], tl["pop"]
    hum = 0.3 * smoothstep(d1 - 0.2, d1 + 0.3, t) * (1 - smoothstep(p + 0.1, p + 0.9, t))
    flash = math.exp(-max(0.0, t - p) / 0.25) * (t >= p - 0.03)
    return min(1.0, hum + 0.7 * beat(t, tl) + flash)

def body_shape(t, tl):
    """Returns (sx, sy, lift px): the heartbeat swell, then the hop and its landing squash."""
    b = beat(t, tl)
    sx, sy, lift = 1 + SWELL * b, 1 + (SWELL + STRETCH) * b, 0.0
    u = t - tl["pop"]
    if 0 <= u < HOP_S:
        a = u / HOP_S
        lift = HOP_PX * math.sin(math.pi * a)
        sy += 0.03 * math.sin(math.pi * a)       # stretched in the air
        sx -= 0.015 * math.sin(math.pi * a)
    elif u >= HOP_S:
        v = (u - HOP_S) / LAND_S
        if v < 1:
            sq = LAND_SQUASH * math.exp(-3 * v) * math.cos(2.5 * math.pi * v)
            sy -= sq
            sx += sq / 2
    return sx, sy, lift

# ---- particles -------------------------------------------------------------------------------------
APEX = g.proj(g.dome(0.0, 90.0))            # top of the dome, canvas px
WINDOW = g.proj(g.dome(*g.find_window()))

def particles(tl):
    """(kind, start, life, x0, y0, dx, dy, size px, spin deg) in canvas px; all end before the stay does."""
    p, k = tl["pop"], tl["k"]
    out = []
    for i, (ox, oy, delay) in enumerate(((-150, 40, 0.0), (10, -10, 0.1), (160, 50, 0.2))):
        out.append(("plus", p + delay * k, 0.95 * k, APEX[0] + ox, APEX[1] + oy, 0.25 * ox, -160, 112 - 10 * i, 0))
    for i in range(9):   # burst around the dome top
        a = math.radians(-160 + i * 140 / 8)
        r0, r1 = 240, 330
        out.append(("spark", p + 0.04 * (i % 3), 0.6 * k, APEX[0] + r0 * math.cos(a), APEX[1] + 120 + r0 * math.sin(a),
                    (r1 - r0) * math.cos(a), (r1 - r0) * math.sin(a), 46 + 10 * (i % 2), 90))
    for at, s in tl["beats"][1:]:   # a small twinkle at the window on the stronger beats
        out.append(("spark", at, 0.4, WINDOW[0] - 70, WINDOW[1] - 80, -20, -40, 40 + 20 * s, 60))
    return out

def plus_svg(size=100):
    r = 18
    arm = (f"M{50 - r} 10 L{50 + r} 10 L{50 + r} {50 - r} L90 {50 - r} L90 {50 + r} L{50 + r} {50 + r} "
           f"L{50 + r} 90 L{50 - r} 90 L{50 - r} {50 + r} L10 {50 + r} L10 {50 - r} L{50 - r} {50 - r} Z")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 100 100">'
            f'<path d="{arm}" fill="#FFFFFF" stroke="#2FA673" stroke-width="7" stroke-linejoin="round"/>'
            f'<path d="M{50 - r + 7} 20 L{50 - r + 7} {50 - r + 4}" stroke="#D2F7E4" stroke-width="5" stroke-linecap="round"/>'
            '</svg>')

def sparkle_svg(size=100):
    pts = []
    for i in range(8):
        a = math.radians(i * 45 - 90)
        r = 46 if i % 2 == 0 else 11
        pts.append(f"{50 + r * math.cos(a):.1f} {50 + r * math.sin(a):.1f}")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 100 100">'
            f'<path d="M{" L".join(pts)} Z" fill="#FFF6C8" stroke="#F2C14E" stroke-width="3" stroke-linejoin="round"/>'
            '<circle cx="50" cy="50" r="7" fill="#FFFFFF"/></svg>')

def particle_frame(size, t, parts, sprites):
    layer = Image.new("RGBA", size)
    for kind, start, life, x0, y0, dx, dy, px, spin in parts:
        u = (t - start) / life
        if not 0 <= u <= 1:
            continue
        if kind == "plus":
            e = 1 - (1 - u) ** 2
            x = x0 + dx * e + 14 * math.sin(2 * math.pi * 1.4 * u)
            y = y0 + dy * e
            sc = min(1.0, u / 0.15) * (1.15 - 0.15 * min(1.0, u / 0.3))
            alpha = 1 - smoothstep(0.6, 1.0, u)
            rot = 12 * math.sin(2 * math.pi * u)
        else:
            e = 1 - (1 - u) ** 3
            x, y = x0 + dx * e, y0 + dy * e
            sc = math.sin(math.pi * u)
            alpha = 1.0
            rot = spin * u
        n = max(1, int(px * sc * S))
        spr = sprites[kind].resize((n, n), Image.LANCZOS).rotate(rot, Image.BICUBIC, expand=True)
        if alpha < 1:
            spr.putalpha(spr.getchannel("A").point(lambda a: int(a * alpha)))
        layer.alpha_composite(spr, (int(x * S - spr.width / 2), int(y * S - spr.height / 2)))
    return layer

# ---- rendering -------------------------------------------------------------------------------------
def place(img, sx, sy, lift, scale):
    """The igloo layer scaled about its base pivot (sx, sy) and lifted, at `scale` of the canvas."""
    px, py = PIVOT
    ox, oy = px * scale, (py - lift) * scale
    a, d = 1 / (sx * scale), 1 / (sy * scale)
    size = (int(W * scale), int(W * scale))
    return img.transform(size, Image.AFFINE, (a, 0, px - a * ox, 0, d, py - d * oy), resample=Image.BICUBIC), (ox, oy)

def light_map(size, center, warm_lvl, mint_lvl):
    lm = Image.new("RGB", size, tuple(int(255 * c) for c in pa.NIGHT_AMBIENT))
    f = pa.falloff()
    for color, strength in ((pa.WINDOW_LIGHT, warm_lvl), (MINT_LIGHT, mint_lvl * MINT_GAIN)):
        if strength <= 0.01:
            continue
        glow = Image.merge("RGB", [f.point(lambda v, c=c: min(255, int(v * c * strength))) for c in color])
        layer = Image.new("RGB", size)
        layer.paste(glow, (int(center[0] - f.width / 2), int(center[1] - f.height / 2)))
        lm = ImageChops.add(lm, layer)
    return lm

def svg_png(svg, png, size=None):
    with open(png[:-4] + ".svg", "w") as f:
        f.write(svg)
    cmd = ["magick", "-background", "none"] + (["-density", str(size)] if size else []) + [png[:-4] + ".svg", png]
    subprocess.run(cmd, check=True)
    return Image.open(png).convert("RGBA")

def main():
    work = sys.argv[1]
    stay = float(sys.argv[2]) if len(sys.argv) > 2 else 4.0
    os.makedirs(work, exist_ok=True)
    tl = times(stay)
    body = svg_png(g.build(0.0), os.path.join(work, "igloo_full.png"))
    dark = svg_png(g.window_overlay(lit=False), os.path.join(work, "window_dark.png"))
    heal = svg_png(g.window_overlay(lit="heal"), os.path.join(work, "window_heal.png"))
    sprites = {"plus": svg_png(plus_svg(), os.path.join(work, "heal_plus.png"), 192),
               "spark": svg_png(sparkle_svg(), os.path.join(work, "heal_sparkle.png"), 192)}
    parts = particles(tl)

    crop = tuple(int(c * S) for c in CROP)
    size = (int(W * S), int(W * S))
    bg_day = Image.new("RGBA", size, (232, 242, 250, 255))
    total = stay + 2 * LEAD
    frames_day, frames_night, curve = [], [], []
    for i in range(int(total * FPS)):
        t = i / FPS - LEAD
        w_lvl, m_lvl = warm(t, tl), mint(t, tl)
        sx, sy, lift = body_shape(t, tl)
        curve.append((t, w_lvl, m_lvl, sy - 1))
        layer = body.copy()
        for pane, a in ((dark, 1 - min(1.0, w_lvl)), (heal, m_lvl)):
            if a > 0.004:
                p = pane.copy()
                p.putalpha(p.getchannel("A").point(lambda v, a=a: int(v * a)))
                layer.alpha_composite(p)
        moved, (ox, oy) = place(layer, sx, sy, lift, S)
        fx = particle_frame(size, t, parts, sprites)
        day = bg_day.copy()
        day.alpha_composite(moved)
        wc = (ox + sx * (WINDOW[0] - PIVOT[0]) * S, oy + sy * (WINDOW[1] - PIVOT[1]) * S)
        night = ImageChops.multiply(day.convert("RGB"), light_map(size, wc, w_lvl, m_lvl)).convert("RGBA")
        day.alpha_composite(fx)
        night.alpha_composite(fx)
        frames_day.append(day.crop(crop).convert("RGB"))
        frames_night.append(night.crop(crop).convert("RGB"))
    ms = int(1000 / FPS)
    def gif(name, frames):
        frames[0].save(os.path.join(work, name), save_all=True, append_images=frames[1:], duration=ms, loop=0)
    gif("heal_day.gif", frames_day)
    gif("heal_night.gif", frames_night)
    both = []
    for d, n in zip(frames_day, frames_night):
        f = Image.new("RGB", (d.width * 2 + 8, d.height), (255, 255, 255))
        f.paste(d, (0, 0)); f.paste(n, (d.width + 8, 0))
        both.append(f)
    gif("heal.gif", both)

    # key frames: before, dimmed, a beat, the last beat, the hop, particles, back to rest
    keys = [0.2, 0.95, tl["beats"][1][0] + 0.03, tl["beats"][4][0] + 0.03, tl["pop"] + 0.15, tl["pop"] + 0.5, stay - 0.1]
    tw, th = frames_night[0].width // 2, frames_night[0].height // 2
    sheet = Image.new("RGB", (tw * len(keys), th * 2 + 18), (255, 255, 255))
    sd = ImageDraw.Draw(sheet)
    for j, kt in enumerate(keys):
        idx = min(len(frames_day) - 1, int(round((kt + LEAD) * FPS)))
        sheet.paste(frames_day[idx].resize((tw, th)), (j * tw, 0))
        sheet.paste(frames_night[idx].resize((tw, th)), (j * tw, th))
        both[idx].save(os.path.join(work, f"heal_key{j}.png"))
        sd.text((j * tw + 4, th * 2 + 3), f"{kt:.2f}s", fill=(60, 60, 60))
    sheet.save(os.path.join(work, "heal_sheet.png"))

    # timeline: warm lamp (amber), mint glow (green), vertical swell (blue); grey marks the stay
    tw, th = 900, 240
    img = Image.new("RGB", (tw, th), (255, 255, 255))
    d = ImageDraw.Draw(img)
    x = lambda t: 20 + (t + LEAD) / total * (tw - 40)
    d.rectangle((x(0), 0, x(stay), th), fill=(238, 238, 238))
    d.line([(x(t), 100 - 60 * w) for t, w, _, _ in curve], fill=(230, 160, 0), width=3)
    d.line([(x(t), 100 - 60 * m) for t, _, m, _ in curve], fill=(40, 180, 110), width=3)
    d.line([(x(t), 180 - 1200 * s) for t, _, _, s in curve], fill=(30, 110, 200), width=2)
    for s in range(int(math.ceil(total))):
        d.text((x(s - LEAD) + 2, th - 14), f"{s - LEAD:.1f}s", fill=(80, 80, 80))
    d.text((24, 4), "warm lamp", fill=(180, 120, 0))
    d.text((110, 4), "mint glow", fill=(30, 150, 90))
    d.text((24, 130), "swell / hop squash", fill=(30, 110, 200))
    img.save(os.path.join(work, "heal_timeline.png"))
    print(len(frames_day), "frames at", FPS, "fps;", f"stay {stay:.1f} s; pop at {tl['pop']:.2f} s")

if __name__ == "__main__":
    main()
