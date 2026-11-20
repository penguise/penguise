"""Door open/close preview: flipbook SVG (only the door group switches) and PNG frames for a GIF."""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import igloo

N = 36            # frames per open+close cycle
OPEN = 115.0      # deg
HOLD = 6          # frames held open and closed
DUR = 2.4         # seconds per cycle

def angles():
    half = (N - 2 * HOLD) // 2
    ease = lambda t: 0.5 - 0.5 * math.cos(math.pi * t)
    up = [OPEN * ease(i / half) for i in range(half)]
    return [0.0] * HOLD + up + [OPEN] * HOLD + [OPEN * (1 - ease(i / half)) for i in range(half)]

def split(svg):
    i = svg.index('<g id="door"')
    j = svg.rindex('\n</g>\n</svg>')
    return svg[:i], svg[i:j], svg[j:]

if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(os.path.join(out, "door-frames"), exist_ok=True)
    bs = angles()
    head, _, tail = split(igloo.build(0.0))
    doors = []
    for k, b in enumerate(bs):
        svg = igloo.build(b)
        open(os.path.join(out, "door-frames", f"f{k:02d}.svg"), "w").write(svg)
        doors.append(split(svg)[1])
    n = len(doors)
    parts = []
    for k, d in enumerate(doors):
        vals = ";".join("inline" if m == k else "none" for m in range(n))
        parts.append(f'<g display="{"inline" if k == 0 else "none"}"><animate attributeName="display" '
                     f'calcMode="discrete" dur="{DUR}s" repeatCount="indefinite" values="{vals}"/>{d}</g>')
    open(os.path.join(out, "igloo-door.svg"), "w").write(head + "\n".join(parts) + tail)
    print(n, "frames")
