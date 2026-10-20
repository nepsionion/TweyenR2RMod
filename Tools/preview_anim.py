"""Renders a skinned GBFR glTF model posed by a mot2gltf animation (.glb), as a strip of frames.

usage: preview_anim.py <out.png|out.gif> <anim.glb> <model.gltf> [<model2.gltf> ...]
       [--frames N] [--exclude wing,vfx] [--yaw DEG] [--size W H] [--gif]
Models after the first (e.g. the face) take shared bones' world transforms from the first model.
"""
import argparse
import json
import math
import os
import struct

import numpy as np
from PIL import Image, ImageDraw

COMP = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def load_gltf(path):
    if path.endswith(".glb"):
        d = open(path, "rb").read()
        jlen = struct.unpack_from("<I", d, 12)[0]
        g = json.loads(d[20:20 + jlen])
        blen = struct.unpack_from("<I", d, 20 + jlen)[0]
        return g, d[28 + jlen:28 + jlen + blen]
    g = json.load(open(path))
    return g, open(os.path.join(os.path.dirname(path), g["buffers"][0]["uri"]), "rb").read()


def acc(g, buf, i):
    a = g["accessors"][i]
    v = g["bufferViews"][a["bufferView"]]
    dt = np.dtype(COMP[a["componentType"]])
    n = NCOMP[a["type"]]
    stride = v.get("byteStride", dt.itemsize * n)
    start = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    raw = np.frombuffer(buf, np.uint8, stride * (a["count"] - 1) + dt.itemsize * n, start)
    out = np.lib.stride_tricks.as_strided(raw, (a["count"], dt.itemsize * n), (stride, 1)).copy().view(dt).reshape(a["count"], n)
    if a.get("normalized") and dt == np.uint16:
        out = out.astype(np.float32) / 65535.0
    elif a.get("normalized") and dt == np.uint8:
        out = out.astype(np.float32) / 255.0
    return out


def trs_matrix(t, q, s):
    x, y, z, w = q
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.asarray(s)
    m[:3, 3] = t
    return m


class Model:
    def __init__(self, path, exclude):
        self.g, self.buf = load_gltf(path)
        g = self.g
        self.dir = os.path.dirname(path)
        self.parent = {c: i for i, n in enumerate(g["nodes"]) for c in n.get("children", [])}
        skin = g["skins"][0]
        self.joints = skin["joints"]
        self.ibm = acc(g, self.buf, skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        self.prims = []
        for node in g["nodes"]:
            if "mesh" not in node:
                continue
            mesh = g["meshes"][node["mesh"]]
            if not mesh["name"].startswith("lod0.") or any(e and e in mesh["name"] for e in exclude):
                continue
            for p in mesh["primitives"]:
                at = p["attributes"]
                pos = acc(g, self.buf, at["POSITION"]).astype(np.float64)
                if "JOINTS_0" not in at:
                    # rigid mesh (e.g. the arrow): follows the skeleton's root joint
                    js = [np.zeros((len(pos), 1), int)]
                    ws = [np.ones((len(pos), 1))]
                else:
                    js = [acc(g, self.buf, at["JOINTS_0"])]
                    ws = [acc(g, self.buf, at["WEIGHTS_0"])]
                if "JOINTS_1" in at:
                    js.append(acc(g, self.buf, at["JOINTS_1"]))
                    ws.append(acc(g, self.buf, at["WEIGHTS_1"]))
                ind = acc(g, self.buf, p["indices"]).reshape(-1, 3)
                uv = acc(g, self.buf, at["TEXCOORD_0"])
                self.prims.append((pos, np.concatenate(js, 1).astype(int), np.concatenate(ws, 1).astype(np.float64), ind, uv, p.get("material")))
        self.colors = [self.tri_colors(pr) for pr in self.prims]

    def tri_colors(self, prim):
        pos, j, w, ind, uv, mat = prim
        g = self.g
        col = np.full((len(ind), 3), 200.0)
        if mat is None:
            return col
        tex = g["materials"][mat].get("pbrMetallicRoughness", {}).get("baseColorTexture")
        if tex is None:
            return col
        uri = g["images"][g["textures"][tex["index"]]["source"]]["uri"]
        img = np.asarray(Image.open(os.path.join(self.dir, uri)).convert("RGB").resize((256, 256)), np.float64)
        c = uv[ind].mean(1)
        return img[(np.mod(c[:, 1], 1) * 255).astype(int), (np.mod(c[:, 0], 1) * 255).astype(int)]

    def world(self, local_override, shared_world=None):
        g = self.g
        cache = {}

        def w(i):
            if i in cache:
                return cache[i]
            n = g["nodes"][i]
            name = n.get("name")
            if shared_world is not None and name in shared_world:
                cache[i] = shared_world[name]
                return cache[i]
            t, q, s = local_override.get(name, (n.get("translation", [0, 0, 0]), n.get("rotation", [0, 0, 0, 1]), n.get("scale", [1, 1, 1])))
            m = trs_matrix(t, q, s)
            if i in self.parent:
                m = w(self.parent[i]) @ m
            cache[i] = m
            return m
        return {g["nodes"][j].get("name"): w(j) for j in self.joints}, [w(j) for j in self.joints]

    def skinned_tris(self, joint_worlds):
        mats = np.stack(joint_worlds) @ self.ibm
        tris, cols = [], []
        for prim, col in zip(self.prims, self.colors):
            pos, j, w, ind, uv, mat = prim
            wsum = w.sum(1, keepdims=True)
            w = w / np.where(wsum > 0, wsum, 1)
            hp = np.concatenate([pos, np.ones((len(pos), 1))], 1)
            out = np.zeros((len(pos), 3))
            for k in range(j.shape[1]):
                m = mats[j[:, k]]
                out += w[:, k:k + 1] * np.einsum("nij,nj->ni", m, hp)[:, :3]
            tris.append(out[ind])
            cols.append(col)
        return np.concatenate(tris), np.concatenate(cols)


def sample_anim(g, buf, t):
    """Returns {node_name: (t, q, s)} at time t (seconds), plus the animation duration."""
    anim = g["animations"][0]
    nodes = g["nodes"]
    pose = {}
    dur = 0.0
    for ch in anim["channels"]:
        smp = anim["samplers"][ch["sampler"]]
        times = acc(g, buf, smp["input"])[:, 0]
        vals = acc(g, buf, smp["output"])
        dur = max(dur, times[-1])
        k = int(np.clip(np.searchsorted(times, t, side="right") - 1, 0, len(times) - 1))
        v = vals[k]
        n = nodes[ch["target"]["node"]]
        name = n.get("name")
        cur = list(pose.get(name, (n.get("translation", [0, 0, 0]), n.get("rotation", [0, 0, 0, 1]), n.get("scale", [1, 1, 1]))))
        cur[{"translation": 0, "rotation": 1, "scale": 2}[ch["target"]["path"]]] = v
        pose[name] = tuple(cur)
    return pose, dur


def render(tris, cols, yaw, size, bounds):
    a = math.radians(yaw)
    rot = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
    v = tris @ rot.T
    n = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
    shade = 0.45 + 0.55 * np.abs(n @ np.array([0.3, 0.4, 0.86]))
    (cx, cy), scale = bounds
    sx = (v[..., 0] - cx) * scale + size[0] / 2
    sy = size[1] / 2 - (v[..., 1] - cy) * scale
    order = np.argsort(v[..., 2].mean(1))
    im = Image.new("RGB", size, (40, 40, 48))
    dr = ImageDraw.Draw(im)
    c = np.clip(cols * shade[:, None], 0, 255).astype(int)
    for i in order:
        dr.polygon([(sx[i, 0], sy[i, 0]), (sx[i, 1], sy[i, 1]), (sx[i, 2], sy[i, 2])], fill=tuple(c[i]))
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("anim")
    ap.add_argument("models", nargs="+")
    ap.add_argument("--frames", type=int, default=6)
    ap.add_argument("--exclude", default="wing,vfx")
    ap.add_argument("--yaw", type=float, default=30)
    ap.add_argument("--size", type=int, nargs=2, default=[240, 320])
    ap.add_argument("--root-motion", action="store_true", help="keep root motion (otherwise the model stays centred)")
    a = ap.parse_args()
    exclude = a.exclude.split(",")
    models = [Model(m, exclude) for m in a.models]
    ag, abuf = load_gltf(a.anim)
    _, dur = sample_anim(ag, abuf, 0)
    times = np.linspace(0, dur, a.frames) if a.frames > 1 else [0.0]
    posed = []
    for t in times:
        pose, _ = sample_anim(ag, abuf, t)
        if not a.root_motion:
            pose.pop("Armature", None)
        shared, worlds = models[0].world(pose)
        tris, cols = models[0].skinned_tris(worlds)
        allt, allc = [tris], [cols]
        for m in models[1:]:
            _, w2 = m.world(pose, shared)
            t2, c2 = m.skinned_tris(w2)
            allt.append(t2)
            allc.append(c2)
        posed.append((np.concatenate(allt), np.concatenate(allc)))
    # common framing for all frames
    pts = np.concatenate([p[0].reshape(-1, 3) for p in posed])
    ang = math.radians(a.yaw)
    xs = pts[:, 0] * math.cos(ang) + pts[:, 2] * math.sin(ang)
    lo = np.array([xs.min(), pts[:, 1].min()])
    hi = np.array([xs.max(), pts[:, 1].max()])
    scale = min((a.size[0] - 16) / max(hi[0] - lo[0], 1e-3), (a.size[1] - 16) / max(hi[1] - lo[1], 1e-3))
    bounds = ((lo + hi) / 2, scale)
    frames = []
    for (tris, cols), t in zip(posed, times):
        im = render(tris, cols, a.yaw, tuple(a.size), bounds)
        ImageDraw.Draw(im).text((4, 4), f"{t:.2f}s", fill=(255, 255, 255))
        frames.append(im)
    if a.out.endswith(".gif"):
        frames[0].save(a.out, save_all=True, append_images=frames[1:], duration=int(1000 * dur / max(len(frames) - 1, 1)), loop=0)
    else:
        sheet = Image.new("RGB", (a.size[0] * len(frames), a.size[1]))
        for i, f in enumerate(frames):
            sheet.paste(f, (i * a.size[0], 0))
        sheet.save(a.out)
    print(a.out)


if __name__ == "__main__":
    main()
