"""Convex hull of an igloo sprite (alpha >= 64) as C# texture fractions (y up) for GameLayout.IglooHull.
Points closer than <tolerance> px to the line through their neighbours are dropped.

usage: python igloo_hull.py <Igloo.png> <tolerance px>"""
import sys, math
from PIL import Image
im = Image.open(sys.argv[1]).convert("RGBA"); W, H = im.size; a = im.split()[3].load()
pts = []
for y in range(H):
    xs = [x for x in range(W) if a[x, y] >= 64]
    if xs: pts += [(xs[0], y), (xs[-1] + 1, y), (xs[0], y + 1), (xs[-1] + 1, y + 1)]
pts = sorted(set(pts))
def cross(o, p, q): return (p[0]-o[0])*(q[1]-o[1]) - (p[1]-o[1])*(q[0]-o[0])
lo, up = [], []
for p in pts:
    while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
    lo.append(p)
for p in reversed(pts):
    while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
    up.append(p)
hull = lo[:-1] + up[:-1]
tol = float(sys.argv[2])
def dev(i, h):
    a, b, c = h[i-1], h[i], h[(i+1) % len(h)]
    return abs(cross(a, c, b)) / math.dist(a, c)
while True:
    d = [dev(i, hull) for i in range(len(hull))]
    k = min(range(len(hull)), key=d.__getitem__)
    if d[k] > tol: break
    hull.pop(k)
# y up fractions, clockwise-ish like the original (start at left)
fr = [(x / W, 1 - y / H) for x, y in hull]
s = min(range(len(fr)), key=lambda i: (fr[i][0], fr[i][1]))
fr = fr[s:] + fr[:s]
fr = [fr[0]] + fr[1:][::-1]
print(len(fr))
out = [f"new Vector2({x:.4f}f, {y:.4f}f)" for x, y in fr]
for i in range(0, len(out), 3): print("        " + ", ".join(out[i:i+3]) + ",")
