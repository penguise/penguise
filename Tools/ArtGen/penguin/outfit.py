"""Penguise penguin outfit (beanie + sweater), drawn from PenguinOutfit.png on the penguin's 1024 canvas.

White fills (tinted per player in game) and dark outlines. Parts are separate groups so the run
animation can move each with the body part under it:
  sleeve-far  -> far flipper   (behind the sweater body)
  sweater     -> body
  sleeve-near -> near flipper  (over the sweater body)
  collar      -> body
  hat         -> body (head)
Every path uses absolute M/C/L/Z commands only, so the animation's point deformation applies.
"""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from penguin import smooth, offset

INK = "#111111"
OUTLINE, DETAIL, RIB = 7, 5.5, 4

def poly(pts, closed=False):
    s = "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts)
    return s + (" Z" if closed else "")

def resample(pts, n):
    """n+1 points evenly spaced by arc length along an open polyline."""
    seg = [math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1)]
    total = sum(seg); out = []
    for k in range(n + 1):
        t = total * k / n; i = 0
        while i < len(seg) - 1 and t > seg[i]:
            t -= seg[i]; i += 1
        f = t / seg[i] if seg[i] else 0
        out.append((pts[i][0] + (pts[i + 1][0] - pts[i][0]) * f, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * f))
    return out

def catmull(pts, k=8):
    """Dense open Catmull-Rom polyline through pts (for sampling ribs along a smooth edge)."""
    out = []
    for i in range(len(pts) - 1):
        p0 = pts[max(i - 1, 0)]; p1 = pts[i]; p2 = pts[i + 1]; p3 = pts[min(i + 2, len(pts) - 1)]
        for j in range(k):
            t = j / k; t2 = t * t; t3 = t2 * t
            out.append(tuple(0.5 * (2 * p1[c] + (-p0[c] + p2[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t2
                                    + (-p0[c] + 3 * p1[c] - 3 * p2[c] + p3[c]) * t3) for c in range(2)))
    out.append(pts[-1])
    return out

# ---- hat: traced outer edge of the reference (alpha edge), pulled in by half the outline width
HAT_EDGE = [(511, 20), (552, 22), (596, 37), (620, 52), (651, 88), (667, 91), (678, 98), (700, 128), (706, 144),
            (709, 163), (703, 179), (657, 160), (581, 159), (546, 166), (484, 186), (449, 202), (386, 239),
            (354, 265), (307, 317), (304, 287), (312, 231), (351, 131), (366, 106), (385, 83), (423, 49), (473, 26)]
HAT = offset(HAT_EDGE, -3.5)
HAT_FOLD = [(327, 201), (360, 175), (400, 150), (450, 124), (500, 104), (550, 92), (595, 87), (630, 88), (655, 92)]

# ---- collar band: neck edge (traced) and lower edge (measured)
NECK = [(316, 373), (370, 405), (410, 424), (485, 446), (551, 454), (601, 454), (651, 444), (695, 426), (722, 400)]
COLLAR_LOW = [(306, 421), (380, 451), (480, 479), (560, 494), (630, 491), (700, 471), (741, 433)]
COLLAR = NECK + list(reversed(COLLAR_LOW))

# ---- sweater body: right seam down, hem, up the back to under the sleeve (covers the back); the rest hides under sleeves/collar
SWEATER_SEEN = [(719, 506), (734, 540), (746, 575), (752, 600), (756, 671), (754, 725), (740, 784), (725, 808),
                (688, 820), (658, 825), (475, 830), (349, 810), (300, 796), (286, 786), (289, 760), (292, 720),
                (296, 680), (300, 640), (304, 600), (307, 565)]
SWEATER_HIDDEN = [(312, 480), (322, 420), (400, 445), (480, 467), (560, 478), (640, 474), (705, 445), (726, 440), (722, 475)]
SWEATER = SWEATER_SEEN + SWEATER_HIDDEN

# ---- near sleeve (over the near flipper's top): outer edge, cuff, then the armhole seam back up
SLEEVE_NEAR_SEEN = [(306, 412), (288, 442), (272, 470), (258, 496), (244, 522), (230, 548), (216, 572),
                    (258, 575), (292, 585), (330, 605), (362, 626), (386, 642), (405, 605), (420, 570), (428, 522)]
SLEEVE_NEAR_HIDDEN = [(426, 470), (360, 436), (318, 420)]
SLEEVE_NEAR = SLEEVE_NEAR_SEEN + SLEEVE_NEAR_HIDDEN

# ---- far sleeve (behind the sweater body, over the far flipper's top)
SLEEVE_FAR_SEEN = [(728, 404), (758, 416), (777, 446), (795, 481), (812, 518), (828, 556), (836, 568),
                   (815, 582), (785, 593), (754, 603), (725, 611), (695, 616)]
SLEEVE_FAR_HIDDEN = [(662, 580), (648, 520), (668, 470), (712, 430)]
# The far flipper's tip pokes above this shoulder line; the dressed penguin trims it here (see FAR_WING_CLIP).
FAR_SHOULDER = SLEEVE_FAR_SEEN[:7]
SLEEVE_FAR = SLEEVE_FAR_SEEN + SLEEVE_FAR_HIDDEN

def ribs(n=26):
    top = resample(catmull(NECK), 400)
    low = resample(catmull(COLLAR_LOW), 400)
    out = []
    for i in range(1, n):
        a = top[round(400 * i / n)]; b = low[round(400 * i / n)]
        L = math.dist(a, b); ux, uy = (b[0] - a[0]) / L, (b[1] - a[1]) / L
        out.append(f'<path fill="none" stroke-width="{RIB}" d="{poly([(a[0] + ux * 7, a[1] + uy * 7), (b[0] - ux * 7, b[1] - uy * 7)])}"/>')
    return "".join(out)

def piece(gid, fill_pts, seen_pts, fill, closed_seen=False, extra=""):
    """Smooth fill over the whole piece; outline only along the visible run (same curve)."""
    d_fill = smooth(fill_pts)
    n = len(seen_pts)
    # the visible run is the first n points of the closed fill loop, so draw it from the same spline
    seg = d_fill.split(" C")
    d_seen = seg[0] + "".join(" C" + s for s in seg[1:n])
    if closed_seen:
        d_seen = d_fill
    return (f'<g id="{gid}"><path fill="{fill}" stroke="none" d="{d_fill}"/>{extra}'
            f'<path fill="none" stroke-width="{OUTLINE}" d="{d_seen.replace(" Z", "")}"/></g>')

def parts(fill="#FFFFFF"):
    return {
        "sleeve-far": piece("outfit-sleeve-far", SLEEVE_FAR, SLEEVE_FAR_SEEN, fill),
        "sweater": piece("outfit-sweater", SWEATER, SWEATER_SEEN, fill),
        "sleeve-near": piece("outfit-sleeve-near", SLEEVE_NEAR, SLEEVE_NEAR_SEEN, fill),
        "collar": (f'<g id="outfit-collar"><path fill="{fill}" stroke-width="{DETAIL}" d="{smooth(COLLAR)}"/>{ribs()}</g>'),
        "hat": (f'<g id="outfit-hat"><path fill="{fill}" stroke="none" d="{smooth(HAT)}"/>'
                f'<path fill="none" stroke-width="{DETAIL}" d="{smooth(HAT_FOLD, closed=False)}"/>'
                f'<path fill="none" stroke-width="{OUTLINE}" d="{smooth(HAT)}"/></g>'),
    }

ORDER = ("sleeve-far", "sweater", "sleeve-near", "collar", "hat")

def outfit_svg(fill="#FFFFFF"):
    p = parts(fill)
    return "\n".join(p[k] for k in ORDER)

def dressed_svg(fill):
    """Static dressed penguin with the same layering as the run: far flipper trimmed at the shoulder,
    near flipper over the sweater body and under its sleeve."""
    import penguin, re
    part = {k: re.search(r'<g id="%s"[^>]*>.*?</g>' % k, penguin.PARTS_SVG, re.S).group(0)
            for k in ("foot-back", "foot-front", "wing-far", "body", "belly", "wing-near", "eyes", "beak")}
    p = parts(fill)
    far = (f'<clipPath id="far-wing-clip"><path d="{far_wing_clip()}"/></clipPath>'
           f'<g clip-path="url(#far-wing-clip)">{part["wing-far"]}</g>')
    near = (f'<clipPath id="near-wing-clip"><path d="{near_wing_clip()}"/></clipPath>'
            f'<g clip-path="url(#near-wing-clip)">{part["wing-near"]}</g>')
    return chr(10).join([part["foot-back"], part["foot-front"], far, part["body"], part["belly"], part["wing-near"],
                      part["eyes"], part["beak"], p["sleeve-far"], p["sweater"], near, p["sleeve-near"],
                      p["collar"], p["hat"]])


def far_wing_clip():
    """Region the far flipper may show in when dressed: below the far shoulder line (the cut runs
    under the sleeve's outline) plus everything left of it, which the body hides anyway."""
    ring = SLEEVE_FAR + [SLEEVE_FAR[0]]
    shoulder = catmull([SLEEVE_FAR[-1]] + FAR_SHOULDER + [SLEEVE_FAR_SEEN[7]], 10)
    shoulder = shoulder[10:-10]               # the spline through the shoulder points only
    x_end, y_end = shoulder[-1]
    pts = [(480, shoulder[0][1]), *shoulder, (1010, y_end), (1010, 1020), (480, 1020)]
    return poly(pts, closed=True)

def near_wing_clip():
    """Region the near flipper may show in when it is drawn over the sweater body: left of the armhole
    seam and below the sleeve's top edge (both cuts run under the sleeve), plus everything below the armpit."""
    seam = catmull([(362, 626), (386, 642), (405, 605), (420, 570), (428, 522), (426, 470)], 10)
    seam = seam[10:-10]                       # spline from the armpit up to the seam top
    top_x, top_y = seam[-1]
    # above the seam: the sleeve's top edge (hidden under the collar), so the flipper's tip stays inside
    pts = [*seam, (360, 436), (318, 420), (306, 412), (60, 330), (60, 1020), (560, 1020), (560, 700)]
    return poly(pts, closed=True)


if __name__ == "__main__":
    import penguin
    out = sys.argv[1]
    open(os.path.join(out, "penguin-outfit.svg"), "w").write(penguin.wrap(outfit_svg()))
    open(os.path.join(out, "penguin-dressed.svg"), "w").write(penguin.wrap(dressed_svg("#E53935")))
