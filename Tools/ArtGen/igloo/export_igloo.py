"""Export the igloo for Unity: doorless body (1254 canvas), door front/back (cropped, hinge pivot)."""
import math, os, sys, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import igloo as g
from PIL import Image

OUT = sys.argv[1]
W = g.W_CANVAS
svg = g.build(0.0)
i = svg.index('<g id="door"'); j = svg.rindex('\n</g>\n</svg>')
head, tail = svg[:i], svg[j:]
open(os.path.join(OUT, "body.svg"), "w").write(head + tail)
defs_end = head.index('</defs>')
for name, front in (("front", True), ("back", False)):
    open(os.path.join(OUT, f"door_{name}.svg"), "w").write(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{W}" viewBox="0 0 {W} {W}">\n'
        f'<g stroke-linejoin="round" stroke-linecap="round">{g.door_leaf(front)}</g></svg>\n')
for n in ("body", "door_front", "door_back"):
    subprocess.run(["magick", "-background", "none", os.path.join(OUT, n + ".svg"), os.path.join(OUT, n + "_full.png")], check=True)

# shared crop for both door faces
boxes = [Image.open(os.path.join(OUT, f"door_{n}_full.png")).getbbox() for n in ("front", "back")]
pad = 4
x0 = min(b[0] for b in boxes) - pad; y0 = min(b[1] for b in boxes) - pad
x1 = max(b[2] for b in boxes) + pad; y1 = max(b[3] for b in boxes) + pad
for n in ("front", "back"):
    Image.open(os.path.join(OUT, f"door_{n}_full.png")).crop((x0, y0, x1, y1)).save(os.path.join(OUT, f"IglooDoor{n.capitalize()}.png"))
Image.open(os.path.join(OUT, "body_full.png")).save(os.path.join(OUT, "Igloo.png"))
hinge = g.proj(g.door_local(g.DOOR[0], g.SILL_H))          # on the hinge axis, bottom of the leaf
cw, ch = x1 - x0, y1 - y0
print("door crop", x0, y0, cw, ch)
print("door pivot frac %.7f %.7f" % ((hinge[0] - x0) / cw, 1 - (hinge[1] - y0) / ch))
print("hinge canvas px %.3f %.3f" % hinge)
# window center (light position)
wth, wph = g.find_window()
wc = g.proj(g.dome(wth, wph))
print("window frac %.4f %.4f" % (wc[0] / W, 1 - wc[1] / W))
# projected axes in Unity space (y up)
ax = (g.AX[0], -( -g.AX[1] * g.CE + g.AX[2] * g.SE)); rt = (g.RT[0], -(-g.RT[1] * g.CE + g.RT[2] * g.SE))
print("TH_T deg", math.degrees(g.TH_T), "ELEV deg", math.degrees(g.ELEV), "AXp", ax, "RTp", rt, "UPp", (0, g.CE))
