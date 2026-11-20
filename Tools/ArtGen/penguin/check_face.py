"""Run check: the outfit never covers the beak or the eyes. Renders 10 run frames dressed and bare and
compares beak-orange and eye pixel counts.

usage: python check_face.py <work dir>"""
import os, sys, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import run as R
from PIL import Image
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
saved = dict(R.OUTFIT)
def count(svg, name):
    p = os.path.join(out, name + ".svg"); open(p, "w").write(svg)
    subprocess.run(["magick", "-background", "white", p, "-flatten", p[:-4] + ".png"], check=True)
    im = Image.open(p[:-4] + ".png").convert("RGB"); px = im.load()
    beak = eye = 0
    for y in range(150, 460):
        for x in range(520, 820):
            r, g, b = px[x, y]
            if r > 230 and 100 < g < 140 and b < 30: beak += 1
            if r < 50 and g < 30 and b < 30 and abs(r - 28) < 12: eye += 1
    return beak, eye
for i, pose in enumerate(R.frames(10)):
    R.OUTFIT.update(saved)
    dressed = count(R.wrap(R.frame_inner(pose)), f"d{i}")
    for k in R.OUTFIT: R.OUTFIT[k] = ""
    bare = count(R.wrap(R.frame_inner(pose)), f"b{i}")
    R.OUTFIT.update(saved)
    print(i, "beak bare/dressed", bare[0], dressed[0], " eye-ish", bare[1], dressed[1])
