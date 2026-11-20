"""Preview of the knocked-out clips after export_faint.py: the dizzy walk (frames by distance, as in game)
for a few loops across the ground, then the fall and one loop lying down, tinted in a player color.

usage: python -I preview_faint.py <work> [loops] [color]
Writes <work>/faint.gif and <work>/faint_sheet.png from <work>/tmp."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import faint as F
import idle as I
from PIL import Image, ImageDraw

work = sys.argv[1]
loops = int(sys.argv[2]) if len(sys.argv) > 2 else 4
color = sys.argv[3] if len(sys.argv) > 3 else "#E53935"
tmp = os.path.join(work, "tmp")
S = 0.25
FPS = 20


def frame(name, k):
    b = Image.open(os.path.join(tmp, f"{name}_{k:03d}_b.png")).convert("RGBA")
    c = Image.open(os.path.join(tmp, f"{name}_{k:03d}_c.png")).convert("RGBA")
    return I.tint(b, c, color)


walk_px = loops * 2 * F.TRAVEL * S
fw, fh = Image.open(os.path.join(tmp, "walk_000_b.png")).size
W = int(walk_px + fw)
ground = int((F.GROUND - F.VIEW[1]) * S)
shots = []                      # (clip, frame, x offset)
walk_seconds = loops * F.LOOP
for k in range(int(walk_seconds * FPS)):
    t = k / FPS
    shots.append(("walk", int(t / F.LOOP * F.WALK_FRAMES) % F.WALK_FRAMES, t / F.LOOP * 2 * F.TRAVEL * S))
# the walk ends exactly at the fall's start position in the loop
end = (loops - 1 + F.FALL_START) * 2 * F.TRAVEL * S
for k in range(int(F.FALL_CLIP * FPS)):
    shots.append(("fall", int(k / FPS / F.FALL_CLIP * F.FALL_FRAMES), end))
for k in range(int(F.DOWN_LOOP * FPS)):
    shots.append(("down", int(k / FPS / F.DOWN_LOOP * F.DOWN_FRAMES), end))
shots = [s for s in shots if not (s[0] == "walk" and s[2] > end)]

frames = []
for clip, k, x in shots:
    im = Image.new("RGBA", (W, fh), (207, 230, 242, 255))
    ImageDraw.Draw(im).rectangle((0, ground - 4, W, fh), fill=(238, 246, 251, 255))
    im.alpha_composite(frame(clip, k), (int(x), 0))
    frames.append(im.convert("RGB"))
frames[0].save(os.path.join(work, "faint.gif"), save_all=True, append_images=frames[1:],
               duration=int(1000 / FPS), loop=0)
picks = list(range(0, len(frames), 7))
cols = 2
sheet = Image.new("RGB", (W * cols, fh * ((len(picks) + cols - 1) // cols)), "white")
d = ImageDraw.Draw(sheet)
for i, k in enumerate(picks):
    sheet.paste(frames[k], ((i % cols) * W, (i // cols) * fh))
    d.text(((i % cols) * W + 4, (i // cols) * fh + 4), f"{shots[k][0]} {k / FPS:.2f}s", fill=(0, 0, 0))
sheet.save(os.path.join(work, "faint_sheet.png"))
print(len(frames), "frames")
