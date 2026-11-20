"""Export penguin + outfit for Unity: idle sprites (1024 canvas) and 16-frame run sheets (4x4 of 256 px),
each as an untinted body layer and a white (tintable) outfit layer on the same canvas and pivot."""
import os, sys, re, subprocess, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import penguin, outfit
import run as R
from PIL import Image

OUT = sys.argv[1]
N = 16
FRAME = 256
WHITE = "#FFFFFF"
OUTFIT_W = outfit.parts(WHITE)

def gpart(k):
    return re.search(r'<g id="%s"[^>]*>.*?</g>' % k, penguin.PARTS_SVG, re.S).group(0)

def clip(cid, d, inner):
    return f'<clipPath id="{cid}"><path d="{d}"/></clipPath><g clip-path="url(#{cid})">{inner}</g>'

def idle_layers():
    base = [gpart("foot-back"), gpart("foot-front"), clip("fw", outfit.far_wing_clip(), gpart("wing-far")),
            gpart("body"), gpart("belly"), gpart("wing-near"), gpart("eyes"), gpart("beak")]
    cloth = [OUTFIT_W["sleeve-far"], OUTFIT_W["sweater"], clip("nw", outfit.near_wing_clip(), gpart("wing-near")),
             OUTFIT_W["sleeve-near"], OUTFIT_W["collar"], OUTFIT_W["hat"]]
    return "\n".join(base), "\n".join(cloth)

def run_layers(pose, k):
    t = pose.t
    p = (t % R.CYCLE) / R.CYCLE
    base = []
    for name, off in (("foot-back", 0.0), ("foot-front", 0.5)):
        dx, dy, ra, rb = R.foot((p + off) % 1)
        (ax, ay), (bx, by) = R.ANKLE[name], R.BALL[name]
        base.append(f'<g transform="translate({dx:.2f} {dy:.2f}) rotate({ra:.2f} {ax} {ay}) rotate({rb:.2f} {bx} {by})">{R.PART[name]}</g>')
    ang = lambda y: pose.angle(pose.height(y))
    far = pose.wing_fn("wing-far"); near = pose.wing_fn("wing-near")
    base.append(clip(f"fw{k}", R.deform_d(outfit.far_wing_clip(), far), R.deform_group(R.PART["wing-far"], far)))
    base.append(R.deform_group(R.PART["body"], pose.body))
    base.append(R.deform_group(R.PART["belly"], pose.body))
    base.append(R.deform_group(R.PART["wing-near"], near))
    base.append(R.deform_group(R.PART["eyes"], pose.body, ang, pose.sx, pose.sy))
    base.append(R.deform_group(R.PART["beak"], pose.body))
    cloth = [R.deform_group(OUTFIT_W["sleeve-far"], far),
             R.deform_group(OUTFIT_W["sweater"], R.skinned(pose)),
             clip(f"nw{k}", R.deform_d(outfit.near_wing_clip(), near), R.deform_group(R.PART["wing-near"], near)),
             R.deform_group(OUTFIT_W["sleeve-near"], near),
             R.deform_group(OUTFIT_W["collar"], pose.body),
             R.deform_group(OUTFIT_W["hat"], pose.body)]
    return "\n".join(base), "\n".join(cloth)

def render(inner, path, size=1024):
    svg = path[:-4] + ".svg"
    open(svg, "w").write(penguin.wrap(inner))
    subprocess.run(["magick", "-background", "none", svg, "-resize", f"{size}x{size}", path], check=True)

os.makedirs(os.path.join(OUT, "tmp"), exist_ok=True)
b, c = idle_layers()
render(b, os.path.join(OUT, "Penguin.png"))
render(c, os.path.join(OUT, "PenguinOutfit.png"))
sheets = {"PenguinRun": Image.new("RGBA", (FRAME * 4, FRAME * 4)), "PenguinOutfitRun": Image.new("RGBA", (FRAME * 4, FRAME * 4))}
for k, pose in enumerate(R.frames(N)):
    b, c = run_layers(pose, k)
    for name, inner in (("PenguinRun", b), ("PenguinOutfitRun", c)):
        f = os.path.join(OUT, "tmp", f"{name}_{k:02d}.png")
        render(inner, f, FRAME)
        sheets[name].paste(Image.open(f), ((k % 4) * FRAME, (k // 4) * FRAME))
for name, im in sheets.items():
    im.save(os.path.join(OUT, name + ".png"))
print("frames", N, "cycle", R.CYCLE, "fps", N / R.CYCLE, "speed px/s", R.SPEED, "units/s", R.SPEED / 1250)
