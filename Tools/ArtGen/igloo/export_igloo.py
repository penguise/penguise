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
# lamp-off window pane (night action: the lamp flickers off), cropped, centre pivot
open(os.path.join(OUT, "window_dark.svg"), "w").write(g.window_overlay(lit=False))
subprocess.run(["magick", "-background", "none", os.path.join(OUT, "window_dark.svg"), os.path.join(OUT, "window_dark_full.png")], check=True)
pane = Image.open(os.path.join(OUT, "window_dark_full.png"))
box = pane.getbbox()
pane.crop(box).save(os.path.join(OUT, "IglooWindowDark.png"))
print("window dark crop", box, "centre frac %.6f %.6f" % ((box[0] + box[2]) / 2 / W, 1 - (box[1] + box[3]) / 2 / W))
# heal (night action: a doctor inside): the mint pane, cropped like the lamp-off pane (same centre pivot),
# and the particles rising from the dome (plus sign and sparkle, PARTICLE px squares, centre pivot)
import preview_heal as heal
open(os.path.join(OUT, "window_heal.svg"), "w").write(g.window_overlay(lit="heal"))
subprocess.run(["magick", "-background", "none", os.path.join(OUT, "window_heal.svg"), os.path.join(OUT, "window_heal_full.png")], check=True)
Image.open(os.path.join(OUT, "window_heal_full.png")).crop(box).save(os.path.join(OUT, "IglooWindowHeal.png"))
PARTICLE = 128
for name, svg in (("HealPlus", heal.plus_svg(PARTICLE)), ("HealSparkle", heal.sparkle_svg(PARTICLE))):
    open(os.path.join(OUT, name + ".svg"), "w").write(svg)
    subprocess.run(["magick", "-background", "none", os.path.join(OUT, name + ".svg"), os.path.join(OUT, name + ".png")], check=True)
apex = g.proj(g.dome(0.0, 90.0))
print("dome apex frac %.6f %.6f" % (apex[0] / W, 1 - apex[1] / W), "particle px", PARTICLE)
