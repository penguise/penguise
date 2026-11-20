"""Doorway mask for Unity: the opening's face outline (white), cropped, pivot on the door hinge point."""
import os, sys, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import igloo as g
from PIL import Image
out = sys.argv[1]
W = g.W_CANVAS
opening = [g.proj(g.tun_u(g.D_FRONT, u, g.INNER)) for u in g.us(180)]
svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{W}" viewBox="0 0 {W} {W}">'
       f'<path d="{g.poly_path(opening)}" fill="#FFFFFF"/></svg>')
open(os.path.join(out, "doorway.svg"), "w").write(svg)
subprocess.run(["magick", "-background", "none", os.path.join(out, "doorway.svg"), os.path.join(out, "doorway_full.png")], check=True)
im = Image.open(os.path.join(out, "doorway_full.png"))
x0, y0, x1, y1 = im.getbbox()
hinge = g.proj(g.door_local(g.DOOR[0], g.SILL_H))
pad = 4
x0 -= pad; y0 -= pad; x1 = max(x1 + pad, int(hinge[0]) + 6); y1 = max(y1 + pad, int(hinge[1]) + 6)
im.crop((x0, y0, x1, y1)).save(os.path.join(out, "IglooDoorway.png"))
cw, ch = x1 - x0, y1 - y0
print("crop", x0, y0, cw, ch, "pivot %.7f %.7f" % ((hinge[0] - x0) / cw, 1 - (hinge[1] - y0) / ch))
print("dome center frac %.6f %.6f" % (g.CX / W, 1 - g.CY / W))
print("R", g.R, "D_FRONT", g.D_FRONT, "D_DOOR", g.D_DOOR, "SILL", g.SILL_H, g.SILL_OUT, "DOOR", g.DOOR, "INNER", g.INNER, "OUTER", g.OUTER)
