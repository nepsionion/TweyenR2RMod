"""Renders a preview strip for every converted Tweyen animation and collects metadata for the animation browser.

Tweyen's weapons are not parented to a hand bone: the bow (wp2300) and arrow (wp2390) are separate objects at the
character root, each playing its own animation with the same ID as the body animation (wp2300_3100 <-> pl2300_3100).

usage: build_anim_gallery.py <GBFRExtract dir> <out dir> [--workers N] [--frames 8] [--only 3100,0000]
Writes <out>/strips/<id>.jpg (frames side by side) and <out>/anims.json.
"""
import argparse
import glob
import json
import math
import os
import sys
from multiprocessing import Pool

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_anim as pa  # noqa: E402

CHAR = "pl2300"
FACE = "fp2300"
WEAPONS = ("wp2300", "wp2390")  # bow, arrow
FRAME_W, FRAME_H = 128, 168
_models = None
_extract = None


def init_worker(extract):
    global _models, _extract
    _extract = extract
    body = pa.Model(f"{extract}/model/{CHAR}_var00.gltf", [])
    face = pa.Model(f"{extract}/face/{FACE}_var00.gltf", [])
    weapons = {w: pa.Model(f"{extract}/weapons/{w}/{w}_var00.gltf", []) for w in WEAPONS}
    _models = (body, face, weapons)


def load_anim(path):
    return pa.load_gltf(path) if os.path.exists(path) else None


def pose_at(anim, t):
    pose, _ = pa.sample_anim(anim[0], anim[1], t)
    pose.pop("Armature", None)  # previews stay centred; travel is reported separately
    return pose


def frame_tris(body_anim, weapon_anims, t):
    body, face, weapons = _models
    pose = pose_at(body_anim, t)
    shared, worlds = body.world(pose)
    tris, cols = body.skinned_tris(worlds)
    parts = [(tris, cols)]
    _, fw = face.world(pose, shared)
    parts.append(face.skinned_tris(fw))
    for w, anim in weapon_anims.items():
        _, ww = weapons[w].world(pose_at(anim, t))
        parts.append(weapons[w].skinned_tris(ww))
    return np.concatenate([p[0] for p in parts]), np.concatenate([p[1] for p in parts])


def root_track(g, buf):
    """Armature (root motion) translation per frame, or None."""
    anim = g["animations"][0]
    arm = [i for i, n in enumerate(g["nodes"]) if n.get("name") == "Armature"][0]
    for ch in anim["channels"]:
        if ch["target"]["node"] == arm and ch["target"]["path"] == "translation":
            return pa.acc(g, buf, anim["samplers"][ch["sampler"]]["output"])
    return None


def render_one(args):
    path, out_dir, nframes = args
    mid = os.path.basename(path)[len(CHAR) + 1:len(CHAR) + 5]
    body_anim = pa.load_gltf(path)
    weapon_anims = {}
    for w in WEAPONS:
        a = load_anim(f"{_extract}/weapon_animations/{w}/{w}_{mid}.glb")
        if a is not None:
            weapon_anims[w] = a
    _, dur = pa.sample_anim(body_anim[0], body_anim[1], 0)
    times = np.linspace(0, dur, nframes) if dur > 0 else [0.0] * nframes
    posed = [frame_tris(body_anim, weapon_anims, t) for t in times]
    yaw = 35
    pts = np.concatenate([p[0].reshape(-1, 3) for p in posed])
    a = math.radians(yaw)
    xs = pts[:, 0] * math.cos(a) + pts[:, 2] * math.sin(a)
    # frame on the character's body (ignore far-flung weapon/arrow points so she isn't tiny)
    lo = np.array([np.percentile(xs, 0.5), np.percentile(pts[:, 1], 0.2)])
    hi = np.array([np.percentile(xs, 99.5), np.percentile(pts[:, 1], 99.8)])
    scale = min((FRAME_W - 8) / max(hi[0] - lo[0], 1e-3), (FRAME_H - 14) / max(hi[1] - lo[1], 1e-3))
    sheet = Image.new("RGB", (FRAME_W * nframes, FRAME_H))
    for i, (tris, cols) in enumerate(posed):
        sheet.paste(pa.render(tris, cols, yaw, (FRAME_W, FRAME_H), ((lo + hi) / 2, scale)), (i * FRAME_W, 0))
    sheet.save(os.path.join(out_dir, "strips", f"{mid}.jpg"), quality=72, optimize=True)
    rt = root_track(*body_anim)
    motion = None
    if rt is not None and len(rt) > 1:
        d = rt[-1] - rt[0]
        motion = {"forward": round(float(d[2]), 2), "side": round(float(d[0]), 2), "up": round(float(d[1]), 2),
                  "horizontal": round(float(math.hypot(d[0], d[2])), 2),
                  "peak_height": round(float(rt[:, 1].max() - rt[0, 1]), 2)}
    return mid, round(float(dur), 3), motion, sorted(weapon_anims)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("extract")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=max(1, (os.cpu_count() or 2) - 1))
    ap.add_argument("--frames", type=int, default=8)
    ap.add_argument("--only")
    a = ap.parse_args()
    os.makedirs(os.path.join(a.out, "strips"), exist_ok=True)
    files = sorted(glob.glob(f"{a.extract}/animations/{CHAR}_*.glb"))
    if a.only:
        keep = set(a.only.split(","))
        files = [f for f in files if os.path.basename(f)[len(CHAR) + 1:len(CHAR) + 5] in keep]

    # which sequence-edit tracks (effects, sound cues, camera...) each animation has
    tracks = {}
    for f in glob.glob(f"{a.extract}/seq_xml/{CHAR}_*.xml"):
        b = os.path.basename(f)
        tracks.setdefault(b[len(CHAR) + 1:len(CHAR) + 5], set()).add(b.split("seq_edit_")[1][:-4])
    report = {r["name"][len(CHAR) + 1:]: r for r in json.load(open(f"{a.extract}/animations/report.json"))}

    with Pool(a.workers, initializer=init_worker, initargs=(a.extract,)) as pool:
        results = []
        for i, res in enumerate(pool.imap_unordered(render_one, [(f, a.out, a.frames) for f in files])):
            results.append(res)
            if i % 25 == 0:
                print(f"{i + 1}/{len(files)}", flush=True)

    names = {"wp2300": "bow", "wp2390": "arrow"}
    anims = []
    for mid, dur, motion, weapons in sorted(results):
        anims.append({"id": mid, "seconds": dur, "frames": report.get(mid, {}).get("frames"), "motion": motion,
                      "weapons": [names[w] for w in weapons],
                      "tracks": sorted(t for t in tracks.get(mid, set()) if t != "attack")})
    json.dump({"character": CHAR, "frames_per_strip": a.frames, "frame_w": FRAME_W, "frame_h": FRAME_H, "anims": anims},
              open(os.path.join(a.out, "anims.json"), "w"), indent=1)
    print(f"done: {len(anims)} animations -> {a.out}")


if __name__ == "__main__":
    main()
