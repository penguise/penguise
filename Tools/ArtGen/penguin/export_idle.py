"""Export the idle clips for Unity: renders idle.py's frames (256 px) into <work>/tmp, then packs them into
sheets in <out>: all clips in idle.CLIPS order, frame after frame, 8x8 frames per 2048 sheet, row 0 at the
top. Body and outfit layers get separate sheets (PenguinIdle0..N, PenguinOutfitIdle0..N).

usage: python export_idle.py <work dir> <out dir>"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import idle
from PIL import Image

SRC, OUT = sys.argv[1], sys.argv[2]
idle.render_frames(SRC, 256)
FRAME, COLS = 256, 8
PER_SHEET = COLS * COLS
frames = [(name, k) for name in idle.CLIPS for k in range(idle.CLIPS[name][0] * idle.FRAMES_PER_BREATH)]
os.makedirs(OUT, exist_ok=True)
for layer, prefix in (("b", "PenguinIdle"), ("c", "PenguinOutfitIdle")):
    for sheet in range((len(frames) + PER_SHEET - 1) // PER_SHEET):
        im = Image.new("RGBA", (FRAME * COLS, FRAME * COLS))
        for i, (name, k) in enumerate(frames[sheet * PER_SHEET:(sheet + 1) * PER_SHEET]):
            im.paste(Image.open(os.path.join(SRC, "tmp", f"{name}_{k:03d}_{layer}.png")), ((i % COLS) * FRAME, (i // COLS) * FRAME))
        im.save(os.path.join(OUT, f"{prefix}{sheet}.png"))
print("frames", len(frames), "clips", [(n, idle.CLIPS[n][0] * idle.FRAMES_PER_BREATH) for n in idle.CLIPS],
      "sheets", (len(frames) + PER_SHEET - 1) // PER_SHEET)
