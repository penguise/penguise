"""Idle check (after idle.py has rendered into <dir>/tmp). Counts body pixels showing through around the collar and the sweater (dark body pixel, outfit alpha low)
per frame, inside the sweater's static outline area below the collar."""
import sys, glob, os
from PIL import Image
d = sys.argv[1]
res = {}
for f in sorted(glob.glob(os.path.join(d, 'tmp', '*_b.png'))):
    name, k = os.path.basename(f).split('_')[:2]
    b = Image.open(f).convert('RGBA'); c = Image.open(f.replace('_b.png', '_c.png')).convert('RGBA')
    bp, cp = b.load(), c.load(); n = 0
    for y in range(108, 150):            # canvas 432..600: collar's lower edge down to mid-chest
        for x in range(72, 190):         # canvas 288..760
            r, g, bl, a = bp[x, y]
            if a > 200 and r + g + bl < 150 and cp[x, y][3] < 60:
                n += 1
    res.setdefault(name, []).append(n)
for name, v in res.items():
    print(f"{name:8s} max {max(v):3d} at frame {v.index(max(v)):2d}  min {min(v):3d}")
