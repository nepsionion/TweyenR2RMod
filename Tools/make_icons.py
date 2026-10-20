"""Renders placeholder skin icons and the placeholder skill icon for Tweyen into <out>/icons.

usage: make_icons.py <GBFRExtract dir> <out dir>
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_anim as pa  # noqa: E402

E, OUT = sys.argv[1], os.path.join(sys.argv[2], "icons")
os.makedirs(OUT, exist_ok=True)
SS = 3  # supersampling for smoother edges


def posed(v, anim="0000", t=0.0):
    body = pa.Model(f"{E}/model/pl2300_var{v:02d}.gltf", [])
    face = pa.Model(f"{E}/face/fp2300_var{v:02d}.gltf", [])
    g, b = pa.load_gltf(f"{E}/animations/pl2300_{anim}.glb")
    pose, _ = pa.sample_anim(g, b, t)
    pose.pop("Armature", None)
    shared, w = body.world(pose)
    bt, bc = body.skinned_tris(w)
    _, fw = face.world(pose, shared)
    ft, fc = face.skinned_tris(fw)
    return np.concatenate([bt, ft]), np.concatenate([bc, fc])


def shot(tris, cols, yaw, center, half_height, size, bg):
    W = size * SS
    scale = W / (2 * half_height)
    im = pa.render(tris, cols, yaw, (W, W), (np.array(center), scale))
    px = np.asarray(im).astype(int)
    # replace the renderer's backdrop with the icon background
    mask = (np.abs(px - np.array([40, 40, 48])).sum(-1) <= 3)
    out = Image.new("RGB", (W, W), bg)
    out.paste(im, (0, 0), Image.fromarray(((~mask) * 255).astype(np.uint8)))
    return out.resize((size, size), Image.LANCZOS)


bg = (34, 30, 44)
# (the character portrait is hand-made: Art/Icons/Character/TweyenCharacter.png)
tris, cols = posed(0)
top = tris.reshape(-1, 3)[:, 1].max()

# skin icons: upper body of every colour variant
for v in range(11):
    tris, cols = posed(v)
    shot(tris, cols, -20, (0.0, top - 0.55), 0.55, 128, bg).save(f"{OUT}/texTweyenSkin{v:02d}.png")

# placeholder skill icon: the bow on a dark tile
bow = pa.Model(f"{E}/weapons/wp2300/wp2300_var00.gltf", [])
_, w = bow.world({})
bt, bc = bow.skinned_tris(w)
rot = np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]], float)  # stand the bow upright
bt = bt @ rot.T
c = bt.reshape(-1, 3)
ic = shot(bt, bc, 0, ((c[:, 0].min() + c[:, 0].max()) / 2, (c[:, 1].min() + c[:, 1].max()) / 2), 1.0, 128, (52, 52, 60))
d = ImageDraw.Draw(ic)
d.rectangle([0, 0, 127, 127], outline=(110, 110, 120), width=3)
ic.save(f"{OUT}/texTweyenSkillPlaceholder.png")
print("icons ->", OUT, sorted(os.listdir(OUT)))
