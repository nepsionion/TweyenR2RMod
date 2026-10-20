"""Quick preview renderer for the exported GBFR glTFs (bind pose, flat-shaded, per-triangle texture colour).

usage: preview_gltf.py <model_dir> <out.png> [yaw_degrees] [gltf_glob]
Renders every matching glTF side by side (one column per variant).
"""
import glob
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

COMP = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_accessor(gltf, binbuf, idx):
    acc = gltf["accessors"][idx]
    view = gltf["bufferViews"][acc["bufferView"]]
    dtype = np.dtype(COMP[acc["componentType"]])
    n = NCOMP[acc["type"]]
    stride = view.get("byteStride", dtype.itemsize * n)
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    count = acc["count"]
    raw = np.frombuffer(binbuf, dtype=np.uint8, count=stride * (count - 1) + dtype.itemsize * n, offset=start)
    out = np.lib.stride_tricks.as_strided(raw, shape=(count, dtype.itemsize * n), strides=(stride, 1)).copy()
    return out.view(dtype).reshape(count, n)


def load_tris(path, texcache):
    gltf = json.load(open(path))
    d = os.path.dirname(path)
    binbuf = open(os.path.join(d, gltf["buffers"][0]["uri"]), "rb").read()
    tris, cols = [], []
    for mesh in gltf["meshes"]:
        if not mesh["name"].startswith("lod0.") or any(t in mesh["name"] for t in os.environ.get("EXCLUDE", "").split(",") if t):
            continue
        for prim in mesh["primitives"]:
            pos = read_accessor(gltf, binbuf, prim["attributes"]["POSITION"])
            uv = read_accessor(gltf, binbuf, prim["attributes"]["TEXCOORD_0"])
            ind = read_accessor(gltf, binbuf, prim["indices"]).reshape(-1, 3)
            col = np.full((len(ind), 3), 200, np.float32)
            mat = gltf["materials"][prim["material"]] if "material" in prim else {}
            tex = mat.get("pbrMetallicRoughness", {}).get("baseColorTexture")
            if tex is not None:
                uri = gltf["images"][gltf["textures"][tex["index"]]["source"]]["uri"]
                if uri not in texcache:
                    texcache[uri] = np.asarray(Image.open(os.path.join(d, uri)).convert("RGB").resize((512, 512)), np.float32)
                img = texcache[uri]
                c = uv[ind].mean(axis=1)
                px = (np.mod(c[:, 0], 1) * 511).astype(int)
                py = (np.mod(c[:, 1], 1) * 511).astype(int)
                col = img[py, px]
            tris.append(pos[ind])
            cols.append(col)
    return np.concatenate(tris), np.concatenate(cols)


def render(tris, cols, yaw, size=(420, 760)):
    a = math.radians(yaw)
    rot = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]], np.float32)
    v = tris @ rot.T
    n = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    shade = 0.45 + 0.55 * np.abs(n @ np.array([0.3, 0.4, 0.86], np.float32))
    flat = v.reshape(-1, 3)
    lo, hi = flat.min(0), flat.max(0)
    scale = min((size[0] - 20) / (hi[0] - lo[0]), (size[1] - 20) / (hi[1] - lo[1]))
    sx = (v[..., 0] - (lo[0] + hi[0]) / 2) * scale + size[0] / 2
    sy = size[1] / 2 - (v[..., 1] - (lo[1] + hi[1]) / 2) * scale
    order = np.argsort(v[..., 2].mean(1))  # back to front (camera looks down -z)
    im = Image.new("RGB", size, (40, 40, 48))
    dr = ImageDraw.Draw(im)
    c = np.clip(cols * shade[:, None], 0, 255).astype(int)
    for i in order:
        dr.polygon([(sx[i, 0], sy[i, 0]), (sx[i, 1], sy[i, 1]), (sx[i, 2], sy[i, 2])], fill=tuple(c[i]))
    return im


def main():
    model_dir, out = sys.argv[1], sys.argv[2]
    yaw = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
    pattern = sys.argv[4] if len(sys.argv) > 4 else "*.gltf"
    paths = sorted(glob.glob(os.path.join(model_dir, pattern)))
    texcache = {}
    frames = []
    for p in paths:
        tris, cols = load_tris(p, texcache)
        im = render(tris, cols, yaw)
        ImageDraw.Draw(im).text((8, 8), os.path.basename(p), fill=(255, 255, 255))
        frames.append(im)
    cols_n = min(6, len(frames))
    rows = math.ceil(len(frames) / cols_n)
    w, h = frames[0].size
    sheet = Image.new("RGB", (w * cols_n, h * rows))
    for i, f in enumerate(frames):
        sheet.paste(f, ((i % cols_n) * w, (i // cols_n) * h))
    sheet.save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main()
