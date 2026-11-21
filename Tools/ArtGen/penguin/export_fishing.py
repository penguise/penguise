"""Export the fishing clips (fish_catch.py, at the game's hole) for Unity: renders every frame's layers into
<work>/tmp, crops each clip and layer to the union of its frames, and packs them into sheets of at most
2048 px in <out>:

  PenguinFishCatch / PenguinFishMiss                body (untinted, with the rod)
  PenguinOutfitFishCatch / PenguinOutfitFishMiss    white outfit (tinted per player)
  FishTackleCatch / FishTackleMiss                  line, float, fish, hook, drops, sigh (untinted, in front)
  FishEffectCatch / FishEffectCatchFlipped          sparkles and the "+1" (unlit); the flipped variant has the
                                                    "+1" pre-mirrored for penguins drawn with flipX

A layer that does not fit one sheet continues in <name>_1.png and so on; sprites are numbered across sheets.
Frames run left to right, top row first, at FPS; a layer's leading and trailing empty frames are left out, so
its sprite 0 is clip frame `first`. Prints, and writes to <work>/fishing_import.json, each layer's
cell size, columns, rows, sheets, frame count and pivot (fraction, y up), and writes <work>/<clip>_game.gif:
the clip composited on the background's hole at the game's scale.

Same scale as the run and idle sheets: a quarter of the 1024 canvas, imported at 312.5 pixels per unit.

usage: python -I export_fishing.py <work> <out> [GameGround.png]"""
import json, math, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fish_catch as K
import faint
import outfit
from PIL import Image

FPS = 15
SCALE = 0.25
MAX_SHEET = 2048
PIVOT = faint.PIVOT
VIEW = (-900, -1000, 2800, 2300)
OUTFIT_W = outfit.parts("#FFFFFF")
LAYERS = {   # layer: (sheet prefix, clips)
    "b": ("PenguinFish", ("catch", "miss")),
    "c": ("PenguinOutfitFish", ("catch", "miss")),
    "f": ("FishTackle", ("catch", "miss")),
    "e": ("FishEffect", ("catch",)),
    "m": ("FishEffect", ("catch",)),
}


def frames(clip):
    return round(K.CLIPS[clip]["length"] * FPS)


def svg(inner):
    x, y, w, h = VIEW
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="{x} {y} {w} {h}">\n'
            f'<g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">\n{inner}\n</g>\n</svg>\n')


def layers(clip, k):
    t = k / FPS
    uid = f"{clip[0]}{k}"
    p = K.pose(clip, t)
    rod_svg, front, fx = K.tackle_layers(clip, p, p.c, t, uid)
    K.MIRROR_PLUS = True
    try:
        fx_mirrored = K.tackle_layers(clip, p, p.c, t, uid)[2]
    finally:
        K.MIRROR_PLUS = False
    base, worn = K.penguin_layers(p, p.c, uid, rod_svg, OUTFIT_W)
    clip_def = K.water_clip(uid)
    return {"b": "\n".join(base), "c": "\n".join(worn), "f": clip_def + front, "e": fx, "m": fx_mirrored}


def render(inner, path):
    src = path[:-4] + ".svg"
    open(src, "w").write(svg(inner))
    x, y, w, h = VIEW
    subprocess.run(["magick", "-background", "none", src, "-resize", f"{int(w * SCALE)}x{int(h * SCALE)}", path],
                   check=True)


def tmp_path(tmp, clip, k, layer):
    return os.path.join(tmp, f"{clip}_{k:03d}_{layer}.png")


def main():
    work, out = sys.argv[1], sys.argv[2]
    ground = sys.argv[3] if len(sys.argv) > 3 else None
    K.use_game_hole()
    tmp = os.path.join(work, "tmp")
    os.makedirs(tmp, exist_ok=True)
    os.makedirs(out, exist_ok=True)
    jobs = []
    for clip in K.CLIPS:
        for k in range(frames(clip)):
            for layer, svg_text in layers(clip, k).items():
                if clip in LAYERS[layer][1]:
                    jobs.append((svg_text, tmp_path(tmp, clip, k, layer)))
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda j: render(*j), jobs))

    px = (PIVOT[0] - VIEW[0]) * SCALE
    py = (PIVOT[1] - VIEW[1]) * SCALE
    info = dict(fps=FPS, stand_units=K.GAME_STAND)
    for clip in K.CLIPS:
        n = frames(clip)
        info[clip] = dict(frames=n, seconds=n / FPS)
        for layer, (prefix, clips) in LAYERS.items():
            if clip not in clips:
                continue
            name = prefix + clip.capitalize() + ("Flipped" if layer == "m" else "")
            ims = [Image.open(tmp_path(tmp, clip, k, layer)) for k in range(n)]
            drawn = [k for k, im in enumerate(ims) if im.getbbox()]
            first = drawn[0]
            ims = ims[first:drawn[-1] + 1]          # leading and trailing empty frames are left out
            boxes = [im.getbbox() for im in ims if im.getbbox()]
            x0 = math.floor(min(b[0] for b in boxes)) - 2
            y0 = math.floor(min(b[1] for b in boxes)) - 2
            x1 = math.ceil(max(b[2] for b in boxes)) + 2
            y1 = math.ceil(max(b[3] for b in boxes)) + 2
            w, h = x1 - x0, y1 - y0
            count = len(ims)
            cols = min(count, MAX_SHEET // w)
            rows = min(math.ceil(count / cols), MAX_SHEET // h)
            per = cols * rows
            sheets = math.ceil(count / per)
            for s in range(sheets):
                chunk = ims[s * per:(s + 1) * per]
                used_rows = math.ceil(len(chunk) / cols)
                im = Image.new("RGBA", (cols * w, used_rows * h))
                for i, frame in enumerate(chunk):
                    im.paste(frame.crop((x0, y0, x1, y1)), ((i % cols) * w, (i // cols) * h))
                im.save(os.path.join(out, name + (f"_{s}" if sheets > 1 else "") + ".png"))
            info[clip][name] = dict(first=first, frames=count, cell=[w, h], cols=cols, rows=rows, sheets=sheets,
                                    pivot=[round((px - x0) / w, 6), round((y1 - py) / h, 6)])
    json.dump(info, open(os.path.join(work, "fishing_import.json"), "w"), indent=1)
    print(json.dumps(info, indent=1))
    if ground:
        preview(work, tmp, ground)


def preview(work, tmp, ground):
    """The clips at the game's scale (background 100 px/unit, sheets 312.5) on the background's hole, from
    the left (as drawn) and from the right (mirrored, flipped "+1"); the outfit tinted orange."""
    bg = Image.open(ground).convert("RGBA")
    water = (1157.5, 559.5)
    k = 100 / 312.5
    for clip in K.CLIPS:
        out = []
        for i in range(frames(clip)):
            canvas = bg.crop((960, 420, 1360, 620)).resize((800, 400), Image.LANCZOS)
            for side in (-1, 1):
                feet = (water[0] + side * K.GAME_STAND * 100, water[1])
                for layer in "bcfm" if side > 0 else "bcfe":
                    path = tmp_path(tmp, clip, i, layer)
                    if not os.path.exists(path):
                        continue
                    im = Image.open(path)
                    if layer == "c":
                        r, g, b, a = im.split()
                        im = Image.merge("RGBA", (r.point(lambda v: v * 0xF4 // 255), g.point(lambda v: v * 0x51 // 255),
                                                  b.point(lambda v: v * 0x1E // 255), a))
                    if side > 0:
                        im = im.transpose(Image.FLIP_LEFT_RIGHT)
                    w, h = round(im.width * k), round(im.height * k)
                    im = im.resize((w, h), Image.LANCZOS)
                    pxv = (PIVOT[0] - VIEW[0]) * SCALE * k
                    if side > 0:
                        pxv = w - pxv
                    pyv = (PIVOT[1] - VIEW[1]) * SCALE * k
                    x, y = (feet[0] - 960 - pxv) * 2, (feet[1] - 420 - pyv) * 2
                    im = im.resize((w * 2, h * 2), Image.LANCZOS)
                    canvas.alpha_composite(im, (round(x), round(y))) if x >= 0 and y >= 0 else _paste(canvas, im, x, y)
            out.append(canvas.convert("RGB"))
        out[0].save(os.path.join(work, clip + "_game.gif"), save_all=True, append_images=out[1:],
                    duration=int(1000 / FPS), loop=0)


def _paste(canvas, im, x, y):
    layer = Image.new("RGBA", canvas.size)
    layer.paste(im, (round(x), round(y)))
    canvas.alpha_composite(layer)


if __name__ == "__main__":
    main()
