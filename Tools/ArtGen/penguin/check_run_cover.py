"""Exposed-back check: body painted magenta, everything else as drawn; count magenta pixels
that the outfit or flippers should hide (below the collar, above the hem)."""
import os, sys, re, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run as R
from PIL import Image
out = sys.argv[1]; n = int(sys.argv[2]) if len(sys.argv) > 2 else 19
R.PART["body"] = R.PART["body"].replace('fill="#111111"', 'fill="#FF00FF"')
poses = R.frames(n)
static = os.environ.get("STATIC")
res = []
for i, pose in enumerate(poses):
    inner = R.frame_inner(pose)
    svg = R.wrap(inner)
    p = os.path.join(out, f"e{i:02d}.svg"); open(p, "w").write(svg)
    png = p[:-4] + ".png"
    subprocess.run(["magick", "-background", "white", p, "-flatten", png], check=True)
    im = Image.open(png).convert("RGB"); px = im.load()
    cnt = {"near": 0, "far": 0}; pts = {"near": [], "far": []}
    for y in range(430, 830):
        for x in range(150, 900):
            r, g, b = px[x, y]
            if r > 200 and g < 80 and b > 200:
                side = "near" if x < 520 else "far"
                cnt[side] += 1; pts[side].append((x, y))
    res.append((i, cnt["near"], cnt["far"], pts["near"][:1], pts["far"][:1]))
    print(i, cnt, pts["near"][:1], pts["near"][-1:], pts["far"][:1], pts["far"][-1:])
