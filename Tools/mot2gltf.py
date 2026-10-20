"""Converts Granblue Fantasy Relink .mot animations (PlatinumGames mot, version 0x20200619) to glTF.

Each animation becomes a skeleton-only .glb (same node names/hierarchy as the model glTF exported by
GBFRDataTools minfo-to-gltf), with one animation sampled per frame at 60 fps.

Format notes (verified against pl2100):
- records are 12 bytes: int16 bone, uint8 channel, uint8 encoding, uint16 count, uint16 pad, uint32 value/offset.
  offsets are relative to the record's own position.
- bone = hex value of the skeleton bone name ("_130" -> 0x130). -1 = whole-model root motion.
- channels: 0-2 translation xyz, 3-5 rotation (euler XYZ, radians, q = qz*qy*qx), 7-9 scale. 10+ = other
  properties (ignored here, listed in the report).
- values are absolute local transforms (frame 0 matches the bind pose).
- encodings 0-8 as in NieR:Automata (see NieR2Blender2NieR mot.py); spline segments are cubic Hermite using
  key[i].m1 as the outgoing tangent and key[i+1].m0 as the incoming one.

usage: mot2gltf.py <skeleton model .gltf> <mot file or folder> <output folder> [--report report.json]
"""
import argparse
import glob
import json
import math
import os
import struct

import numpy as np

FPS = 60.0


# ---------------------------------------------------------------- mot decoding

def pg_half(h):
    """PlatinumGames 16-bit float: 1 sign, 6 exponent (bias 47), 9 mantissa bits."""
    sign = h & 0x8000
    expo = (h & 0x7E00) >> 9
    mant = h & 0x01FF
    if expo == 0 and mant == 0:
        return 0.0
    if expo == 63:
        return float("-inf") if sign else (float("inf") if mant == 0 else float("nan"))
    bits = (sign << 16) | ((expo - 47 + 127) << 23) | (mant << 14)
    return struct.unpack("<f", struct.pack("<I", bits))[0]


def read_keys(d, base, enc, count, nframes):
    """Returns ('values', np.array per frame) or ('hermite', frames, p, m0, m1)."""
    if enc == 1:
        return ("values", np.frombuffer(d, "<f4", count, base).astype(np.float64))
    if enc == 2:
        p, dp = struct.unpack_from("<ff", d, base)
        q = np.frombuffer(d, "<u2", count, base + 8)
        return ("values", p + dp * q.astype(np.float64))
    if enc == 3:
        p, dp = (pg_half(x) for x in struct.unpack_from("<HH", d, base))
        q = np.frombuffer(d, "<u1", count, base + 4)
        return ("values", p + dp * q.astype(np.float64))
    if enc == 4:
        a = np.frombuffer(d, np.dtype([("f", "<u2"), ("pad", "<u2"), ("p", "<f4"), ("m0", "<f4"), ("m1", "<f4")]), count, base)
        return ("hermite", a["f"].astype(np.int64), a["p"].astype(np.float64), a["m0"].astype(np.float64), a["m1"].astype(np.float64))
    if enc == 5:
        p, dp, m0, dm0, m1, dm1 = struct.unpack_from("<6f", d, base)
        a = np.frombuffer(d, np.dtype([("f", "<u2"), ("cp", "<u2"), ("c0", "<u2"), ("c1", "<u2")]), count, base + 24)
    elif enc in (6, 7):
        p, dp, m0, dm0, m1, dm1 = (pg_half(x) for x in struct.unpack_from("<6H", d, base))
        a = np.frombuffer(d, np.dtype([("f", "<u1"), ("cp", "<u1"), ("c0", "<u1"), ("c1", "<u1")]), count, base + 12)
    elif enc == 8:
        p, dp, m0, dm0, m1, dm1 = (pg_half(x) for x in struct.unpack_from("<6H", d, base))
        a = np.frombuffer(d, np.dtype([("f", ">u2"), ("cp", "<u1"), ("c0", "<u1"), ("c1", "<u1")]), count, base + 12)
    else:
        raise ValueError(f"unknown encoding {enc}")
    frames = a["f"].astype(np.int64)
    if enc == 7:  # frame indices are deltas
        frames = np.cumsum(frames)
    return ("hermite", frames, p + dp * a["cp"], m0 + dm0 * a["c0"], m1 + dm1 * a["c1"])


def eval_channel(keys, nframes):
    if keys[0] == "values":
        v = keys[1]
        if len(v) >= nframes:
            return v[:nframes]
        return np.concatenate([v, np.full(nframes - len(v), v[-1] if len(v) else 0.0)])
    _, f, p, m0, m1 = keys
    out = np.empty(nframes)
    t_all = np.arange(nframes)
    if len(f) == 1:
        out[:] = p[0]
        return out
    seg = np.clip(np.searchsorted(f, t_all, side="right") - 1, 0, len(f) - 2)
    f0, f1 = f[seg], f[seg + 1]
    span = np.where(f1 > f0, f1 - f0, 1)
    t = np.clip((t_all - f0) / span, 0.0, 1.0)
    t2, t3 = t * t, t * t * t
    out = (2 * t3 - 3 * t2 + 1) * p[seg] + (t3 - 2 * t2 + t) * m1[seg] + (-2 * t3 + 3 * t2) * p[seg + 1] + (t3 - t2) * m0[seg + 1]
    out[t_all < f[0]] = p[0]
    out[t_all >= f[-1]] = p[-1]
    return out


def parse_mot(path):
    d = open(path, "rb").read()
    magic, version, flags, nframes, recoff, nrec = struct.unpack_from("<4sIHHII", d, 0)
    if magic != b"mot\0":
        raise ValueError(f"{path}: not a mot file")
    name = d[0x18:0x2C].split(b"\0")[0].decode("ascii", "replace")
    channels = {}  # (bone, channel) -> per-frame values
    for i in range(nrec):
        roff = recoff + i * 12
        bone, ch, enc, count, _pad, raw = struct.unpack_from("<hBBHHI", d, roff)
        if bone == 32767:  # terminator
            break
        if enc == 0:
            vals = np.full(nframes, struct.unpack("<f", struct.pack("<I", raw))[0])
        else:
            vals = eval_channel(read_keys(d, roff + raw, enc, count, nframes), nframes)
        channels[(bone, ch)] = vals
    return {"name": name, "version": version, "flags": flags, "frames": nframes, "channels": channels}


# ---------------------------------------------------------------- math

def euler_xyz_to_quat(x, y, z):
    """Vectorized: q = qz * qy * qx (xyzw)."""
    cx, sx = np.cos(x / 2), np.sin(x / 2)
    cy, sy = np.cos(y / 2), np.sin(y / 2)
    cz, sz = np.cos(z / 2), np.sin(z / 2)
    qw = cz * cy * cx + sz * sy * sx
    qx = cz * cy * sx - sz * sy * cx
    qy = cz * sy * cx + sz * cy * sx
    qz = sz * cy * cx - cz * sy * sx
    q = np.stack([qx, qy, qz, qw], axis=-1)
    # keep quaternions in one hemisphere frame to frame so linear interpolation doesn't flip
    for i in range(1, len(q)):
        if np.dot(q[i], q[i - 1]) < 0:
            q[i] = -q[i]
    return q


def quat_to_euler_xyz(q):
    x, y, z, w = q
    sinr = 2 * (w * x + y * z)
    cosr = 1 - 2 * (x * x + y * y)
    sinp = max(-1.0, min(1.0, 2 * (w * y - z * x)))
    siny = 2 * (w * z + x * y)
    cosy = 1 - 2 * (y * y + z * z)
    return math.atan2(sinr, cosr), math.asin(sinp), math.atan2(siny, cosy)


# ---------------------------------------------------------------- glTF output

class Skeleton:
    def __init__(self, gltf_path):
        g = json.load(open(gltf_path))
        nodes = g["nodes"]
        joint_set = set(g["skins"][0]["joints"])
        # armature = the parent of the root joints
        children_of = {i: n.get("children", []) for i, n in enumerate(nodes)}
        parent = {c: i for i, ch in children_of.items() for c in ch}
        roots = [j for j in joint_set if parent.get(j) not in joint_set]
        self.armature = parent[roots[0]]
        self.nodes = nodes
        self.children_of = children_of
        self.by_id = {}
        for j in joint_set:
            name = nodes[j]["name"]
            try:
                self.by_id[int(name[1:], 16)] = j
            except ValueError:
                pass

    def rest(self, j):
        n = self.nodes[j]
        return (n.get("translation", [0, 0, 0]), n.get("rotation", [0, 0, 0, 1]), n.get("scale", [1, 1, 1]))


def build_glb(skel, mot, out_path):
    nf = mot["frames"]
    ch = mot["channels"]
    # gather nodes in armature subtree, keep original names/rest TRS
    order, remap = [], {}

    def visit(i):
        remap[i] = len(order)
        order.append(i)
        for c in skel.children_of[i]:
            if c in skel.by_id.values() or c == skel.armature:
                visit(c)
    visit(skel.armature)
    out_nodes = []
    for i in order:
        n = skel.nodes[i]
        on = {"name": n.get("name", "Armature") if i != skel.armature else "Armature"}
        for k in ("translation", "rotation", "scale"):
            if k in n:
                on[k] = n[k]
        kids = [remap[c] for c in skel.children_of[i] if c in remap]
        if kids:
            on["children"] = kids
        out_nodes.append(on)

    buf = bytearray()
    accessors, views, samplers, channels = [], [], [], []

    def add(arr, typ, minmax=False):
        arr = np.ascontiguousarray(arr, np.float32)
        views.append({"buffer": 0, "byteOffset": len(buf), "byteLength": arr.nbytes})
        buf.extend(arr.tobytes())
        acc = {"bufferView": len(views) - 1, "componentType": 5126, "count": len(arr), "type": typ}
        if minmax:
            acc["min"] = [float(arr.min())]
            acc["max"] = [float(arr.max())]
        accessors.append(acc)
        return len(accessors) - 1

    times = add(np.arange(nf) / FPS, "SCALAR", minmax=True)
    times_static = add(np.array([0.0, (nf - 1) / FPS if nf > 1 else 0.0]), "SCALAR", minmax=True)

    bones = sorted({b for b, _ in ch})
    skipped = []
    for b in bones:
        if b == -1:
            node = remap[skel.armature]
            rest_t, rest_q, rest_s = [0, 0, 0], [0, 0, 0, 1], [1, 1, 1]
        elif b in skel.by_id and skel.by_id[b] in remap:
            node = remap[skel.by_id[b]]
            rest_t, rest_q, rest_s = skel.rest(skel.by_id[b])
        else:
            skipped.append(b)
            continue
        rest_e = quat_to_euler_xyz(rest_q)
        for path, comps, rest in (("translation", (0, 1, 2), rest_t), ("rotation", (3, 4, 5), rest_e), ("scale", (7, 8, 9), rest_s)):
            if not any((b, c) in ch for c in comps):
                continue
            cols = [ch.get((b, c), np.full(nf, rest[k])) for k, c in enumerate(comps)]
            if path == "rotation":
                vals = euler_xyz_to_quat(*cols)
                typ = "VEC4"
            else:
                vals = np.stack(cols, axis=-1)
                typ = "VEC3"
            static = np.allclose(vals, vals[0], atol=1e-6)
            out = add(np.repeat(vals[:1], 2, axis=0) if static else vals, typ)
            samplers.append({"input": times_static if static else times, "output": out, "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1, "target": {"node": node, "path": path}})

    while len(buf) % 4:
        buf.append(0)
    gltf = {
        "asset": {"version": "2.0", "generator": "TweyenR2RMod mot2gltf"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": out_nodes,
        "animations": [{"name": mot["name"], "samplers": samplers, "channels": channels}],
        "buffers": [{"byteLength": len(buf)}],
        "bufferViews": views,
        "accessors": accessors,
    }
    js = json.dumps(gltf, separators=(",", ":")).encode()
    js += b" " * ((4 - len(js) % 4) % 4)
    with open(out_path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(buf)))
        f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
        f.write(struct.pack("<II", len(buf), 0x004E4942) + bytes(buf))
    return skipped


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("skeleton_gltf")
    ap.add_argument("mot")
    ap.add_argument("out_dir")
    ap.add_argument("--report")
    a = ap.parse_args()
    skel = Skeleton(a.skeleton_gltf)
    files = sorted(glob.glob(os.path.join(a.mot, "*.mot"))) if os.path.isdir(a.mot) else [a.mot]
    os.makedirs(a.out_dir, exist_ok=True)
    report = []
    for f in files:
        mot = parse_mot(f)
        skipped = build_glb(skel, mot, os.path.join(a.out_dir, mot["name"] + ".glb"))
        other = sorted({c for (b, c) in mot["channels"] if c > 9})
        report.append({"name": mot["name"], "frames": mot["frames"], "seconds": round(mot["frames"] / FPS, 3),
                       "bones": len({b for b, _ in mot["channels"]}), "skipped_bones": [hex(b) for b in skipped],
                       "other_channels": other, "root_motion": any(b == -1 for b, _ in mot["channels"])})
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print(f"converted {len(report)} animations -> {a.out_dir}")


if __name__ == "__main__":
    main()
