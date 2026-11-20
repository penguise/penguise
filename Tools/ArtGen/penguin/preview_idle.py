"""Previews for idle.py output: one GIF per clip, a contact sheet of each clip, and a group GIF where eight
penguins idle with random clip choices, start phases and speeds (what the game would do)."""
import os, sys, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import idle
from PIL import Image, ImageDraw

OUT = sys.argv[1]
TMP = os.path.join(OUT, "tmp")
BG = (207, 230, 242, 255)
COLORS = ["#E53935", "#FB8C00", "#FDD835", "#43A047", "#1E88E5", "#8E24AA", "#EC6FA9", "#8D6E63"]
cache = {}


def frame(name, k, color):
    key = (name, k, color)
    if key not in cache:
        b = Image.open(os.path.join(TMP, f"{name}_{k:03d}_b.png")).convert("RGBA")
        c = Image.open(os.path.join(TMP, f"{name}_{k:03d}_c.png")).convert("RGBA")
        cache[key] = idle.tint(b, c, color)
    return cache[key]


def count(name):
    return idle.CLIPS[name][0] * idle.FRAMES_PER_BREATH


def on_bg(im):
    bg = Image.new("RGBA", im.size, BG)
    bg.alpha_composite(im)
    return bg.convert("RGB")


ms = round(1000 / idle.FPS)
# one GIF per clip
for name in idle.CLIPS:
    frames = [on_bg(frame(name, k, COLORS[0])) for k in range(count(name))]
    frames[0].save(os.path.join(OUT, f"idle_{name}.gif"), save_all=True, append_images=frames[1:], duration=ms, loop=0)

# contact sheet: 6 evenly spaced frames per clip, cropped to the penguin
crop = (8, 0, 248, 256)
cols = 6
sheet = Image.new("RGB", ((crop[2] - crop[0]) * cols, 256 * len(idle.CLIPS)), BG[:3])
draw = ImageDraw.Draw(sheet)
for row, name in enumerate(idle.CLIPS):
    n = count(name)
    for col in range(cols):
        k = round(col * n / cols)
        sheet.paste(on_bg(frame(name, k, COLORS[0])).crop(crop), (col * (crop[2] - crop[0]), row * 256))
    draw.text((4, row * 256 + 4), name, fill=(0, 0, 0))
sheet.save(os.path.join(OUT, "idle_sheet.png"))

# group: eight penguins, each chaining random clips with its own phase and speed
random.seed(7)
seconds, dt = 14.0, 1 / idle.FPS
WEIGHTS = {"breathe": 5, "blink": 3, "blink2": 1, "flap": 1, "stomp": 1, "look": 1}
actors = []
for i in range(8):
    speed = random.uniform(0.92, 1.08)
    seq, total = [], 0.0
    first = True
    while total < seconds * speed + 10:
        name = random.choices(list(WEIGHTS), list(WEIGHTS.values()))[0]
        seq.append(name)
        total += idle.CLIPS[name][0] * idle.BREATH
    actors.append((speed, random.uniform(0, idle.BREATH), seq))

W = 240
group = []
for f in range(int(seconds / dt)):
    canvas = Image.new("RGBA", (W * 8, 256), BG)
    for i, (speed, offset, seq) in enumerate(actors):
        t = f * dt * speed + offset
        for name in seq:
            length = idle.CLIPS[name][0] * idle.BREATH
            if t < length:
                break
            t -= length
        k = min(count(name) - 1, int(t / length * count(name)))
        canvas.alpha_composite(frame(name, k, COLORS[i]).crop(crop), (i * W, 0))
    group.append(canvas.convert("RGB"))
group[0].save(os.path.join(OUT, "idle_group.gif"), save_all=True, append_images=group[1:], duration=ms, loop=0)
print("written", len(group), "group frames")
