"""Export the knocked-out clips (faint.py) for Unity: renders every frame's body and outfit layers into
<work>/tmp, crops each clip to the union of its frames, and packs one sheet per clip and layer into <out>:
PenguinDizzy / PenguinOutfitDizzy (walk), PenguinFaint / PenguinOutfitFaint (fall), PenguinDown /
PenguinOutfitDown (lying). Frames run left to right, top row first. Prints, and writes to
<work>/faint_import.json, each sheet's cell size, frame count and feet pivot (fraction, y up).

Same scale as the run and idle sheets: a quarter of the 1024 canvas, imported at 312.5 pixels per unit.

usage: python -I export_faint.py <work> <out>"""
import json, math, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import faint as F
from PIL import Image

SCALE = 0.25
MAX_SHEET = 2048


def render(inner, path):
    svg = path[:-4] + ".svg"
    open(svg, "w").write(F.svg(inner))
    x, y, w, h = F.VIEW
    subprocess.run(["magick", "-background", "none", svg, "-resize", f"{int(w * SCALE)}x{int(h * SCALE)}", path],
                   check=True)


def render_clips(work):
    tmp = os.path.join(work, "tmp")
    os.makedirs(tmp, exist_ok=True)
    jobs = []
    for name in F.CLIPS:
        for k, f in enumerate(F.clip_frames(name)):
            b, c = F.layers(f, f"{name}{k}")
            jobs.append((b, os.path.join(tmp, f"{name}_{k:03d}_b.png")))
            jobs.append((c, os.path.join(tmp, f"{name}_{k:03d}_c.png")))
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda j: render(*j), jobs))
    return tmp


def main():
    work, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    tmp = render_clips(work)
    px = (F.PIVOT[0] - F.VIEW[0]) * SCALE
    py = (F.PIVOT[1] - F.VIEW[1]) * SCALE
    info = {}
    for name, sheet in (("walk", "Dizzy"), ("fall", "Faint"), ("down", "Down")):
        n = F.CLIPS[name][2]
        ims = {layer: [Image.open(os.path.join(tmp, f"{name}_{k:03d}_{layer}.png")) for k in range(n)]
               for layer in "bc"}
        boxes = [im.getbbox() for layer in "bc" for im in ims[layer]]
        x0 = math.floor(min(b[0] for b in boxes)) - 2
        y0 = math.floor(min(b[1] for b in boxes)) - 2
        x1 = math.ceil(max(b[2] for b in boxes)) + 2
        y1 = math.ceil(max(b[3] for b in boxes)) + 2
        w, h = x1 - x0, y1 - y0
        cols = min(n, MAX_SHEET // w)
        rows = math.ceil(n / cols)
        if rows * h > MAX_SHEET:
            raise SystemExit(f"{name}: {rows} rows of {h} px do not fit a {MAX_SHEET} sheet")
        for layer, prefix in (("b", "Penguin"), ("c", "PenguinOutfit")):
            im = Image.new("RGBA", (cols * w, rows * h))
            for k, frame in enumerate(ims[layer]):
                im.paste(frame.crop((x0, y0, x1, y1)), ((k % cols) * w, (k // cols) * h))
            im.save(os.path.join(out, f"{prefix}{sheet}.png"))
        info[sheet] = dict(cell=[w, h], cols=cols, rows=rows, frames=n,
                           pivot=[round((px - x0) / w, 6), round((y1 - py) / h, 6)])
    timing = dict(loop_seconds=F.LOOP, loop_units=2 * F.TRAVEL / F.CANVAS_PER_UNIT,
                  speed=F.TRAVEL / F.CYC / F.CANVAS_PER_UNIT, fall_start=F.FALL_START,
                  fall_seconds=F.FALL_CLIP, down_seconds=F.DOWN_LOOP)
    # Name heights (units above the feet) clear of the stars: walking and lying.
    tops = {}
    for name in ("walk", "down"):
        top = min(F.placement(f)[1][1] for f in F.clip_frames(name)) - F.STAR_RISE - 40 - 46 * 1.05
        tops[name] = (F.PIVOT[1] - top) / F.CANVAS_PER_UNIT
    info["timing"], info["star_top_units"] = timing, tops
    json.dump(info, open(os.path.join(work, "faint_import.json"), "w"), indent=1)
    print(json.dumps(info, indent=1))


if __name__ == "__main__":
    main()
