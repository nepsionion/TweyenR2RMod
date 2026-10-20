"""Makes the README badge (Art/Readme/github_issues.png): GitHub's mark and "GitHub Suggestions / Issues" on a dark
pill, like the badges other Risk of Rain 2 mods put at the top of their Thunderstore page.
Run with any Python that has Pillow: python Tools/make_badges.py"""
import math
import os
import re
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Art", "Readme", "github_issues.png")
SS = 4                       # supersampling
H = 56                       # final height (px)
BG, FG, SUB = (36, 41, 47), (255, 255, 255), (201, 209, 217)
FONTS = "C:/Windows/Fonts"

# GitHub's mark (16x16 octicon)
MARK = ("M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49"
        "-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07"
        "-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2"
        ".82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82"
        " 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0"
        "016 8c0-4.42-3.58-8-8-8z")


def path_points(d, steps=24):
    """Flattens an SVG path (M, L, H, V, C, S, A, Z; absolute and relative) into polygons."""
    tokens = re.findall(r"[MmLlHhVvCcSsAaZz]|-?(?:\d+\.?\d*|\.\d+)", d)
    polys, poly, i, cmd = [], [], 0, None
    x = y = sx = sy = 0.0
    last_ctrl = None

    def num():
        nonlocal i
        v = float(tokens[i]); i += 1
        return v

    def flag():
        # arc flags may be packed without separators ("0 016" = 0, 0, 16)
        nonlocal i
        t = tokens[i]
        if len(t) > 1 and t[0] in "01" and not t.startswith(("0.", "-")):
            tokens[i] = t[1:]
            return float(t[0])
        i += 1
        return float(t)

    def cubic(p0, p1, p2, p3):
        for k in range(1, steps + 1):
            t = k / steps; u = 1 - t
            poly.append((u**3*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t**3*p3[0],
                         u**3*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t**3*p3[1]))

    while i < len(tokens):
        if re.match(r"[A-Za-z]", tokens[i]):
            cmd = tokens[i]; i += 1
        rel = cmd.islower()
        c = cmd.upper()
        ox, oy = (x, y) if rel else (0.0, 0.0)
        if c == "M":
            if poly: polys.append(poly)
            x, y = ox + num(), oy + num(); sx, sy = x, y
            poly = [(x, y)]; cmd = "l" if rel else "L"; last_ctrl = None
        elif c == "L":
            x, y = ox + num(), oy + num(); poly.append((x, y)); last_ctrl = None
        elif c == "H":
            x = ox + num(); poly.append((x, y)); last_ctrl = None
        elif c == "V":
            y = oy + num(); poly.append((x, y)); last_ctrl = None
        elif c == "C":
            p1 = (ox + num(), oy + num()); p2 = (ox + num(), oy + num()); p3 = (ox + num(), oy + num())
            cubic((x, y), p1, p2, p3); x, y = p3; last_ctrl = p2
        elif c == "S":
            p1 = (2*x - last_ctrl[0], 2*y - last_ctrl[1]) if last_ctrl else (x, y)
            p2 = (ox + num(), oy + num()); p3 = (ox + num(), oy + num())
            cubic((x, y), p1, p2, p3); x, y = p3; last_ctrl = p2
        elif c == "A":
            rx, ry, rot = num(), num(), math.radians(num())
            large, sweep = flag(), flag()
            x2, y2 = ox + num(), oy + num()
            # endpoint -> centre parameterisation (SVG spec F.6.5)
            dx, dy = (x - x2) / 2, (y - y2) / 2
            x1p = math.cos(rot)*dx + math.sin(rot)*dy
            y1p = -math.sin(rot)*dx + math.cos(rot)*dy
            lam = x1p*x1p/(rx*rx) + y1p*y1p/(ry*ry)
            if lam > 1: rx *= math.sqrt(lam); ry *= math.sqrt(lam)
            num_ = rx*rx*ry*ry - rx*rx*y1p*y1p - ry*ry*x1p*x1p
            coef = math.sqrt(max(0, num_ / (rx*rx*y1p*y1p + ry*ry*x1p*x1p)))
            if large == sweep: coef = -coef
            cxp, cyp = coef*rx*y1p/ry, -coef*ry*x1p/rx
            cx = math.cos(rot)*cxp - math.sin(rot)*cyp + (x + x2)/2
            cy = math.sin(rot)*cxp + math.cos(rot)*cyp + (y + y2)/2
            a1 = math.atan2((y1p - cyp)/ry, (x1p - cxp)/rx)
            a2 = math.atan2((-y1p - cyp)/ry, (-x1p - cxp)/rx)
            da = a2 - a1
            if sweep and da < 0: da += 2*math.pi
            if not sweep and da > 0: da -= 2*math.pi
            for k in range(1, steps*2 + 1):
                a = a1 + da*k/(steps*2)
                poly.append((cx + rx*math.cos(a)*math.cos(rot) - ry*math.sin(a)*math.sin(rot),
                             cy + rx*math.cos(a)*math.sin(rot) + ry*math.sin(a)*math.cos(rot)))
            x, y = x2, y2; last_ctrl = None
        elif c == "Z":
            poly.append((sx, sy)); x, y = sx, sy; last_ctrl = None
    if poly: polys.append(poly)
    return polys


def main():
    h = H * SS
    pad = 16 * SS
    mark = 32 * SS
    bold = ImageFont.truetype(os.path.join(FONTS, "segoeuib.ttf"), 22 * SS)
    regular = ImageFont.truetype(os.path.join(FONTS, "segoeui.ttf"), 22 * SS)
    title, sub = "GitHub", "Suggestions / Issues"
    gap = 10 * SS
    tw = bold.getlength(title); sw = regular.getlength(sub)
    w = int(pad + mark + 12 * SS + tw + gap + sw + pad + 6 * SS)

    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=h // 2, fill=BG)

    # the mark: the outline is filled, then the cat is cut out (the path's inner contour)
    scale = mark / 16
    mx, my = pad + 6 * SS, (h - mark) // 2
    layer = Image.new("L", (w, h), 0)
    ld = ImageDraw.Draw(layer)
    for poly in path_points(MARK):
        ld.polygon([(mx + px*scale, my + py*scale) for px, py in poly], fill=255)
    img.paste(Image.new("RGBA", (w, h), FG), (0, 0), layer)

    tx = mx + mark + 12 * SS
    ty = h // 2
    d.text((tx, ty), title, font=bold, fill=FG, anchor="lm")
    d.text((tx + tw + gap, ty), sub, font=regular, fill=SUB, anchor="lm")

    img = img.resize((w // SS, H), Image.LANCZOS)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    img.save(OUT)
    print(OUT, img.size)


if __name__ == "__main__":
    main()
