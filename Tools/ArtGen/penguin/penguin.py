"""Penguise penguin, drawn from code: smooth outlines (Catmull-Rom as cubic Beziers) for each part, kept as
separate SVG groups (feet, flippers, body, belly, eyes, beak) so run.py and idle.py can deform them.

usage: python penguin.py <out.svg>"""
import sys
import math
def offset(pts, d):
    # move points along normal; d>0 = outward
    n=len(pts); area=sum(pts[i][0]*pts[(i+1)%n][1]-pts[(i+1)%n][0]*pts[i][1] for i in range(n))
    sgn=1 if area>0 else -1
    out=[]
    for i in range(n):
        a=pts[i-1]; b=pts[(i+1)%n]; tx=b[0]-a[0]; ty=b[1]-a[1]; L=math.hypot(tx,ty) or 1
        nx,ny=ty/L*sgn, -tx/L*sgn
        out.append((pts[i][0]+nx*d, pts[i][1]+ny*d))
    return out
def smooth(pts, closed=True, t=1.0):
    n=len(pts); out=f"M{pts[0][0]:.0f} {pts[0][1]:.0f}"
    rng = range(n) if closed else range(n-1)
    for i in rng:
        p0=pts[(i-1)%n] if (closed or i>0) else pts[i]
        p1=pts[i]; p2=pts[(i+1)%n]
        p3=pts[(i+2)%n] if (closed or i+2<n) else p2
        c1=(p1[0]+(p2[0]-p0[0])/6*t, p1[1]+(p2[1]-p0[1])/6*t)
        c2=(p2[0]-(p3[0]-p1[0])/6*t, p2[1]-(p3[1]-p1[1])/6*t)
        out+=f" C{c1[0]:.1f} {c1[1]:.1f} {c2[0]:.1f} {c2[1]:.1f} {p2[0]:.0f} {p2[1]:.0f}"
    return out+(" Z" if closed else "")

body=[(535,72),(598,79),(643,99),(667,119),(684,139),(696,159),(707,179),(715,199),(722,219),(727,240),(731,265),(732,300),(728,380),(724,600),(700,820),(600,858),(450,866),(355,872),(318,866),(282,845),(266,826),(278,808),(294,785),(300,700),(311,430),(314,385),(310,340),(307,300),(310,265),(316,240),(322,219),(330,199),(341,179),(354,159),(371,139),(392,119),(421,99),(472,79)]
belly=[(645,213),(670,184),(693,178),(708,198),(716,240),(721,275),(717,340),(710,395),(711,440),(731,500),(744,560),(750,620),(751,680),(744,730),(731,780),(712,830),(697,858),(670,885),(640,893),(612,887),(592,870),(584,861),(555,864),(480,881),(430,896),(398,894),(368,879),(347,852),(383,800),(403,758),(426,700),(445,640),(463,580),(477,520),(482,470),(475,400),(458,355),(443,305),(445,250),(468,203),(505,177),(555,164),(605,172)]
wingL=[(345,370),(311,408),(297,440),(268,480),(245,520),(224,560),(208,600),(197,640),(192,680),(196,720),(204,745),(222,761),(245,763),(275,752),(320,720),(375,660),(420,580),(450,500),(455,440),(430,415),(370,400)]
wingR=[(682,392),(712,388),(738,405),(765,445),(793,500),(818,560),(835,620),(842,672),(836,708),(818,728),(793,732),(770,718),(750,690),(730,640),(708,575),(690,505),(680,440)]
crease=[(424,490),(412,555),(380,625),(335,680),(298,708)]
# far wing = near wing mirrored and narrowed for perspective, hung from the right shoulder
FAR_AXIS, FAR_SHOULDER, FAR_SX, FAR_SY = 400, 688, 0.72, 0.96
def to_far(p): return (FAR_SHOULDER + (FAR_AXIS - p[0]) * FAR_SX, 410 + (p[1] - 410) * FAR_SY)
wingR=[to_far(p) for p in wingL]
creaseR=[to_far(p) for p in crease]

footB="M382 855 L381 930 C362 942 350 958 360 972 C372 985 405 995 440 1004 C452 1007 460 1002 452 992 C470 994 498 996 508 990 C514 985 506 979 488 975 C508 976 525 974 524 967 C522 960 500 955 482 951 C460 946 444 940 437 928 L435 855 Z"
footB_toes=''
footF="M615 852 L615 922 C598 932 588 948 600 958 C620 972 665 980 700 990 C712 992 716 984 706 976 C728 980 755 984 762 978 C766 972 758 967 742 964 C765 963 785 958 782 950 C778 943 755 939 735 935 C705 930 680 928 666 918 L665 852 Z"
footF_toes=''

PARTS_SVG=f'''    <g id="foot-back"><path fill="#FF7A00" d="{footB}"/>{footB_toes}</g>
    <g id="foot-front"><path fill="#FF7A00" d="{footF}"/>{footF_toes}</g>
    <g id="wing-far"><path fill="#111111" d="{smooth(offset(wingR,-3.5))}"/><path fill="none" stroke="#5A5A5A" stroke-width="4" d="{smooth(creaseR, closed=False)}"/></g>
    <g id="body"><path fill="#111111" d="{smooth(offset(body,-3.5))}"/></g>
    <g id="belly"><path fill="#F7F7F7" d="{smooth(offset(belly,3.5))}"/><path fill="none" d="M556 867 C580 860 600 851 622 846"/></g>
    <g id="wing-near">
      <path fill="#111111" stroke="none" d="{smooth(offset(wingL,-3.5))}"/>
      <path fill="none" d="{smooth(offset(wingL,-3.5)[1:19], closed=False)}"/>
      <path fill="none" stroke="#5A5A5A" stroke-width="4" d="{smooth(crease, closed=False)}"/>
    </g>
    <g id="eyes" stroke="none">
      <ellipse cx="595" cy="262" rx="17.5" ry="32" fill="#1C0F0F"/>
      <ellipse cx="697" cy="255" rx="12.5" ry="28" fill="#1C0F0F"/>
      <ellipse cx="589" cy="245" rx="5" ry="8" fill="#FFFFFF"/>
      <ellipse cx="694" cy="241" rx="4" ry="7" fill="#FFFFFF"/>
    </g>
    <g id="beak">
      <path fill="#FF7A00" d="M625 322 C622 305 635 292 660 287 L700 290 C735 296 765 305 780 318 C765 332 735 350 700 362 C680 368 660 370 648 362 C632 352 624 338 625 322 Z"/>
      <path fill="none" stroke-width="6" d="M630 332 C670 338 720 336 776 322"/>
    </g>'''

def wrap(inner, extra=""):
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <!-- Penguise penguin, drawn from Penguin.png. Parts are separate groups for animation. -->
{extra}  <g stroke="#111111" stroke-width="7" stroke-linejoin="round" stroke-linecap="round">
{inner}
  </g>
</svg>
'''
if __name__=="__main__":
    open(sys.argv[1],"w").write(wrap(PARTS_SVG))
