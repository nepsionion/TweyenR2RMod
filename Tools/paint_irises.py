"""Paints an iris and pupil onto Tweyen's eye-white textures.

The game draws irises with a dedicated eye shader that RoR2 doesn't have, so the eye mesh only carries the eye-white
("conj") texture and looks blank. This finds where each eye is visible between the eyelids (front view, depth
tested against the face mesh, rest pose = eyes open) and paints, in texture space:
  - an iris circle a little larger than the opening between the eyelids (IRIS_SCALE), hazel/gold
  - a smaller pupil circle in its centre, black

usage: paint_irises.py <GBFRExtract dir> <textures dir to edit in place>
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_anim as pa  # noqa: E402

IRIS_COLOR = (190, 140, 52)   # hazel / gold
PUPIL_COLOR = (0, 0, 0)
IRIS_SCALE = 1.6             # iris radius relative to the lid-to-lid opening (user: 1.0 -> 1.125 -> 1.4 -> 1.6)
IRIS_RAISE = 0.12            # move the iris up by this fraction of the lid-to-lid opening height
PUPIL_SCALE = 0.45            # pupil radius as a fraction of the iris radius
SUPERSAMPLE = 8               # anti-aliased circle edges

EYES = {"lod0.Face.0": "fp2300_l_eye_lod0_conj.png", "lod0.Face.1": "fp2300_r_eye_lod0_conj.png"}


def visible_eye_uvs(face_gltf):
    """Front orthographic z-buffer render of the face; returns {eye mesh name: (N, 2) visible glTF uvs}."""
    g, buf = pa.load_gltf(face_gltf)
    prims = []
    for mesh in g["meshes"]:
        if not mesh["name"].startswith("lod0."):
            continue
        for p in mesh["primitives"]:
            prims.append((mesh["name"], pa.acc(g, buf, p["attributes"]["POSITION"]).astype(float),
                          pa.acc(g, buf, p["attributes"]["TEXCOORD_0"]).astype(float), pa.acc(g, buf, p["indices"]).reshape(-1, 3)))
    eye_pts = np.concatenate([P for n, P, _, _ in prims if n in EYES])
    pad = 0.01
    x0, x1 = eye_pts[:, 0].min() - pad, eye_pts[:, 0].max() + pad
    y0, y1 = eye_pts[:, 1].min() - pad, eye_pts[:, 1].max() + pad
    W = 1400
    H = int(W * (y1 - y0) / (x1 - x0))
    zbuf = np.full((H, W), -1e9)
    idbuf = np.full((H, W), -1)
    uvbuf = np.zeros((H, W, 2))
    for pid, (name, P, UV, I) in enumerate(prims):
        sx = (P[:, 0] - x0) / (x1 - x0) * W
        sy = (y1 - P[:, 1]) / (y1 - y0) * H
        for tri in I:
            xs, ys = sx[tri], sy[tri]
            mnx, mxx = int(max(np.floor(xs.min()), 0)), int(min(np.ceil(xs.max()), W - 1))
            mny, mxy = int(max(np.floor(ys.min()), 0)), int(min(np.ceil(ys.max()), H - 1))
            if mnx > mxx or mny > mxy:
                continue
            gx, gy = np.meshgrid(np.arange(mnx, mxx + 1) + 0.5, np.arange(mny, mxy + 1) + 0.5)
            d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
            if abs(d) < 1e-12:
                continue
            a = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / d
            b = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / d
            c = 1 - a - b
            inside = (a >= 0) & (b >= 0) & (c >= 0)
            if not inside.any():
                continue
            zz = a * P[tri[0], 2] + b * P[tri[1], 2] + c * P[tri[2], 2]
            sub = zbuf[mny:mxy + 1, mnx:mxx + 1]
            upd = inside & (zz > sub)
            sub[upd] = zz[upd]
            idbuf[mny:mxy + 1, mnx:mxx + 1][upd] = pid
            for k in range(2):
                uvbuf[mny:mxy + 1, mnx:mxx + 1, k][upd] = (a * UV[tri[0], k] + b * UV[tri[1], k] + c * UV[tri[2], k])[upd]
    names = [p[0] for p in prims]
    result = {}
    for eye in EYES:
        m = idbuf == names.index(eye)
        result[eye] = (m, uvbuf)
    return result


def eye_circle(mask, uvbuf):
    """Iris centre and radius (glTF uv units) from the visible opening: centred in it, spanning lid to lid."""
    ys, xs = np.nonzero(mask)
    cx = int(round((xs.min() + xs.max()) / 2))
    rows = np.nonzero(mask[:, cx])[0]
    top = uvbuf[rows.min(), cx]
    bottom = uvbuf[rows.max(), cx]
    u_left = uvbuf[mask][:, 0].min()
    u_right = uvbuf[mask][:, 0].max()
    # v grows downward (image rows), so raising the iris means a smaller v
    center = np.array([(u_left + u_right) / 2, (top[1] + bottom[1]) / 2 - IRIS_RAISE * abs(bottom[1] - top[1])])
    radius = abs(bottom[1] - top[1]) / 2 * IRIS_SCALE
    return center, radius


def paint(path, center, radius):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    big = Image.new("RGBA", (w * SUPERSAMPLE, h * SUPERSAMPLE), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    cx, cy = center[0] * w * SUPERSAMPLE, center[1] * h * SUPERSAMPLE  # glTF uv: v grows downward like image rows
    r = radius * w * SUPERSAMPLE
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=IRIS_COLOR + (255,))
    rp = r * PUPIL_SCALE
    d.ellipse([cx - rp, cy - rp, cx + rp, cy + rp], fill=PUPIL_COLOR + (255,))
    overlay = big.resize((w, h), Image.LANCZOS)
    im.alpha_composite(overlay)
    im.save(path)
    return (cx / SUPERSAMPLE, cy / SUPERSAMPLE, r / SUPERSAMPLE)


def main():
    extract, tex_dir = sys.argv[1], sys.argv[2]
    vis = visible_eye_uvs(f"{extract}/face/fp2300_var00.gltf")
    for eye, tex in EYES.items():
        mask, uvbuf = vis[eye]
        center, radius = eye_circle(mask, uvbuf)
        px = paint(os.path.join(tex_dir, tex), center, radius)
        print(f"{tex}: iris centre uv ({center[0]:.3f}, {center[1]:.3f}) radius {radius:.3f} -> pixels centre ({px[0]:.1f}, {px[1]:.1f}) radius {px[2]:.1f}")


if __name__ == "__main__":
    main()
