"""Makes Tweyen's skin-selection icons in the style of RoR2's default skin icon (square split into four triangles
meeting in the middle, each with a lighter 4px outline), using each skin's four most prominent colours.

Colours come from unshaded front and back renders of the character (body + face, no bow, which is the same in every
skin), clustered, with near-duplicate clusters merged. Order: most prominent on top, then left, bottom, right.

usage: make_skin_icons.py <GBFRExtract dir> <out dir> [--sheet preview.png]
Writes <out>/texTweyenSkin00.png ... texTweyenSkin10.png (128x128) and <out>/skin_palettes.json.
"""
import argparse
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_anim as pa  # noqa: E402

SIZE = 128
OUTLINE = 4          # px, matches the reference icon
SUPERSAMPLE = 4
BG = np.array([40, 40, 48])


def render_unshaded(tris, cols, yaw, size=(300, 520)):
    """Painter's-algorithm render with flat albedo colours (no lighting)."""
    a = math.radians(yaw)
    rot = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
    v = tris @ rot.T
    flat = v.reshape(-1, 3)
    lo, hi = flat.min(0), flat.max(0)
    scale = min((size[0] - 10) / (hi[0] - lo[0]), (size[1] - 10) / (hi[1] - lo[1]))
    sx = (v[..., 0] - (lo[0] + hi[0]) / 2) * scale + size[0] / 2
    sy = size[1] / 2 - (v[..., 1] - (lo[1] + hi[1]) / 2) * scale
    im = Image.new("RGB", size, tuple(BG))
    d = ImageDraw.Draw(im)
    c = np.clip(cols, 0, 255).astype(int)
    for i in np.argsort(v[..., 2].mean(1)):
        d.polygon([(sx[i, 0], sy[i, 0]), (sx[i, 1], sy[i, 1]), (sx[i, 2], sy[i, 2])], fill=tuple(c[i]))
    return np.asarray(im).reshape(-1, 3)


def kmeans(px, k, iters=25, seed=0):
    rng = np.random.default_rng(seed)
    centers = px[rng.choice(len(px), k, replace=False)].astype(float)
    for _ in range(iters):
        d = ((px[:, None, :] - centers[None]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(k):
            sel = px[lab == j]
            if len(sel):
                centers[j] = sel.mean(0)
    counts = np.bincount(lab, minlength=k)
    return centers, counts


def palette(px, n=4, k=10, merge_dist=38.0):
    centers, counts = kmeans(px.astype(float), k)
    order = list(np.argsort(-counts))
    merged = []  # (colour, count)
    for j in order:
        c, w = centers[j], counts[j]
        for m in merged:
            if np.linalg.norm(m[0] - c) < merge_dist:
                m[0] = (m[0] * m[1] + c * w) / (m[1] + w)
                m[1] += w
                break
        else:
            merged.append([c.copy(), w])
    merged.sort(key=lambda m: -m[1])
    total = sum(m[1] for m in merged)
    return [(tuple(int(round(x)) for x in m[0]), m[1] / total) for m in merged[:n]]


def cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]


def lighter(c, amount=0.22):
    c = np.array(c, float)
    return tuple(int(round(x)) for x in c + (255 - c) * amount)


def make_icon(colors):
    """colors: [top, left, bottom, right]."""
    S = SIZE * SUPERSAMPLE
    o = OUTLINE * SUPERSAMPLE
    im = Image.new("RGB", (S, S))
    d = ImageDraw.Draw(im)
    m = S / 2
    tris = [
        [(0, 0), (S, 0), (m, m)],      # top
        [(0, 0), (0, S), (m, m)],      # left
        [(0, S), (S, S), (m, m)],      # bottom
        [(S, 0), (S, S), (m, m)],      # right
    ]
    for tri, col in zip(tris, colors):
        d.polygon(tri, fill=lighter(col))
        # inset the triangle by the outline width (move each edge inward along its normal)
        p = np.array(tri, float)
        cen = p.mean(0)
        lines = []
        for i in range(3):
            a, b = p[i], p[(i + 1) % 3]
            e = b - a
            nrm = np.array([-e[1], e[0]]) / np.linalg.norm(e)
            if np.dot(cen - a, nrm) < 0:
                nrm = -nrm
            lines.append((a + nrm * o, e))
        inner = []
        for i in range(3):
            (p1, d1), (p2, d2) = lines[i - 1], lines[i]
            t = cross2(p2 - p1, d2) / cross2(d1, d2)
            inner.append(tuple(p1 + d1 * t))
        d.polygon(inner, fill=tuple(int(x) for x in col))
    return im.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("extract")
    ap.add_argument("out")
    ap.add_argument("--sheet")
    ap.add_argument("--variants", type=int, default=11)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    result = {}
    icons = []
    for v in range(a.variants):
        body = pa.Model(f"{a.extract}/model/pl2300_var{v:02d}.gltf", [])
        face = pa.Model(f"{a.extract}/face/fp2300_var{v:02d}.gltf", [])
        g, b = pa.load_gltf(f"{a.extract}/animations/pl2300_0000.glb")
        pose, _ = pa.sample_anim(g, b, 0.0)
        pose.pop("Armature", None)
        shared, w = body.world(pose)
        bt, bc = body.skinned_tris(w)
        _, fw = face.world(pose, shared)
        ft, fc = face.skinned_tris(fw)
        tris, cols = np.concatenate([bt, ft]), np.concatenate([bc, fc])
        px = np.concatenate([render_unshaded(tris, cols, 0), render_unshaded(tris, cols, 180)])
        px = px[np.abs(px - BG).sum(1) > 6]
        pal = palette(px)
        result[f"var{v:02d}"] = [{"rgb": c, "share": round(s, 3)} for c, s in pal]
        icon = make_icon([c for c, _ in pal])
        icon.save(os.path.join(a.out, f"texTweyenSkin{v:02d}.png"))
        icons.append((icon, pal))
        print(f"var{v:02d}: " + ", ".join(f"#{c[0]:02x}{c[1]:02x}{c[2]:02x} ({s:.0%})" for c, s in pal))
    json.dump(result, open(os.path.join(a.out, "skin_palettes.json"), "w"), indent=1)
    if a.sheet:
        sheet = Image.new("RGB", (SIZE * len(icons) + 8 * (len(icons) + 1), SIZE + 16), (30, 30, 36))
        for i, (icon, _) in enumerate(icons):
            sheet.paste(icon, (8 + i * (SIZE + 8), 8))
        sheet.save(a.sheet)


if __name__ == "__main__":
    main()
