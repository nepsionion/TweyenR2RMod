"""Exports Tweyen's converted glTF data into a simple package the Unity builder script reads
(TweyenUnityProject/Assets/Editor/TweyenBuilder.cs).

Everything is converted to Unity's left-handed space here (glTF -> Unity: negate X), so the Unity side only builds assets.

usage: export_unity.py <GBFRExtract dir> <out dir>
Writes <out>/manifest.json, <out>/meshes.bin, <out>/clips/<name>.json and copies the textures into <out>/textures/.
"""
import json
import math
import os
import shutil
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import preview_anim as pa  # noqa: E402  (glTF loading helpers)

CHAR, FACE, BOW = "pl2300", "fp2300", "wp2300"
# Tweyen's bows, in Relink's weapon menu order: Bow of Dismissal, Desolation-Crown Bow, Hercules (Endless Ragnarok),
# Bow of Eternal Splendor (Endless Ragnarok). Their transcendence versions use the same models. Only BOW has its own
# skeleton and animations; the others use a subset of its bones (same names and rest pose), so they share them.
BOWS = ["wp2300", "wp2306", "wp2303", "wp2305"]

# Crystal parts drawn with Relink's effect shaders: the glTF converter can't link their materials, so they came out
# white. Their textures are known from the .mmat material files:
# - each bow's crystal piece uses its "ice" textures;
# - Tweyen's own crystals (glTF material index -> texture) are exported once per bow ("weapon" tag), recoloured to
#   match that bow's crystal texture (Bow of Dismissal: her original colours), so they follow the bow picked.
BODY_CRYSTALS = {6: "pl2300_jewelry", 7: "pl2300_p_jewelry", 9: "pl2300_wing"}  # legs, waist, head wings
VARIANTS = 11

# Unity clip name -> (body motion id, loop). Bow motions with the same id are merged into each clip.
CLIPS = {
    "Idle": ("0000", True),
    "RunForward": ("0010", True),
    "Sprint": ("0020", True),
    "WalkBackward": ("0071", True),
    "WalkRight": ("0072", True),
    "WalkLeft": ("0073", True),
    "JumpStart": ("0030", False),
    "JumpAscend": ("0031", False),
    "Fall": ("0032", True),
    "Land": ("0033", False),
    "BonusJumpStart": ("0034", False),
    "BonusJumpAscend": ("0035", False),
    "Merculight": ("3410", False),
    # Stellar Arrow (primary) = GBFR Attack 1 from neutral: action 100 -> 3600 draw and shoot, 3630 follow-through
    "StellarArrow": ("3600", False),
    "StellarArrowHold": ("3630", False),
    # Wheel of Death (utility) = GBFR action 1400 -> 3440: a 720 degree spin, edited below
    "WheelOfDeath": ("3440", False),
    # Clincher (secondary) = GBFR action 1200 -> 3420: draw (0-1.4 s), shot, recoil and recovery; edited below
    "Clincher": ("3420", False),
    # Two-Crown's Strife (special) = GBFR action 1700 -> 3460 (then 3461 fall, 3462 landing: not used)
    "TwoCrownStrife": ("3460", False),
    # Asteroid Jaeger (special) = GBFR action 200, "Multilock Hail" (the charged strong attack): 31c0 raise the bow
    # to aim, 31c1 hold the aim (loop), 31ca shoot (and recover to idle; trimmed below)
    "MultilockStart": ("31c0", False),
    "MultilockHold": ("31c1", True),
    "MultilockFire": ("31ca", False),
    # Valiant Weave (utility) = GBFR "light, strong" (action 150/151, the dash shot) in its forward version, 310b
    "ValiantWeave": ("310b", False),
}

# per-clip edits applied to the sampled 60 fps data before key reduction
CLIP_EDITS = {
    # Relink's spin has a ~1.5 s pause between its two turns, and the second turn is slower. Keep both turns, play
    # the second one faster so the hips turn as fast as in the first ("match"), blend across the cut, and record a
    # "Fire" event when the hips have turned each of these angles: 10 per rotation, the second rotation offset by
    # 18 degrees, so the 20 arrows end up exactly 18 degrees apart (each fired when she faces its direction).
    "WheelOfDeath": {
        "segments": [(0.0, 1.00, 1.0), (2.50, 3.70, "match")],
        "blend": 0.1,
        # then retime the spin so the hips turn at one constant speed (the first turn's), start to finish
        "uniform_spin": True,
        "spin_targets": [18 + 36 * k for k in range(10)] + [360 + 36 * k for k in range(10)],
    },
    # Relink's Clincher is 8 s: the shot is at ~1.4 s, then a 10.5 m recoil slide (root motion, dropped here: the
    # mod applies its own recoil) and recovery by 3 s; the rest is a long idle tail. Keep the first 3 s.
    "Clincher": {
        "segments": [(0.0, 3.0, 1.0)],
    },
    # 3460: crouch, leap back and up (0.4-0.8 s), draw in the air (0.8-2.0 s), shot and recoil hop, then she starts
    # to fall at ~2.55 s: keep everything before the fall (the mod moves her along the leap; root motion is dropped).
    "TwoCrownStrife": {
        "segments": [(0.0, 2.55, 1.0)],
    },
    # 31ca: release at ~0.2 s, Relink's recoil (root motion, dropped: the mod applies its own), settled by ~0.8 s;
    # the remaining 2.7 s are a slow return to idle. Its bow and arrow leave her hand after the release (Relink
    # hides them): kept in her hand instead (bow joint _010 on hand joint _a21, the closest pair while she holds it),
    # and the (invisible) arrow stays nocked, so every arrow of the volley leaves from the bow
    "MultilockFire": {
        "pin_weapons": [("bow", "_010", "_a21", ("bow", "arrow")), ("arrow", "_000", "_a21", ("arrow",))],
        "segments": [(0.0, 1.5, 1.0)],
    },
    # 310b: dash (most of it in 0.1-0.3 s, settled by 1.0 s) with the shot at 0.3 s, then 2.5 s of recovery to
    # idle: keep the dash only, sped up 2.5x (1.0 s -> 0.4 s; never scaled by stats)
    "ValiantWeave": {
        "segments": [(0.0, 1.0, 2.5)],
    },
}
FPS_OUT = 60.0

# clips that carry the arrow's own (invisible) animation from the game. The arrow isn't rendered in her hands; its
# animated tip ("ArrowTip" attachment) is where the projectile spawns, so the shot leaves the bow.
ARROW_ANIMATED_CLIPS = {"StellarArrow", "Clincher", "TwoCrownStrife", "MultilockFire", "ValiantWeave"}

POS_TOL = 0.0005   # metres
ROT_TOL = 0.0008   # quaternion component
SCL_TOL = 0.0005


# ---------------------------------------------------------------- coordinate conversion

def cv_pos(p):
    p = np.asarray(p, np.float64).copy()
    p[..., 0] *= -1
    return p


def cv_rot(q):  # xyzw
    q = np.asarray(q, np.float64).copy()
    q[..., 1] *= -1
    q[..., 2] *= -1
    return q


def cv_tan(t):
    t = np.asarray(t, np.float64).copy()
    t[..., 0] *= -1
    t[..., 3] *= -1
    return t


# ---------------------------------------------------------------- skeleton

class Skeleton:
    """Combined bone list. Group roots: Armature (body+face), BowArmature (bow)."""

    def __init__(self):
        self.bones = []         # dicts: name, parent, t, r, s
        self.index = {}         # (group, name) -> index
        self.paths = {}         # index -> path from model root

    def add(self, group, name, parent_idx, t, r, s):
        idx = len(self.bones)
        self.bones.append({"name": name, "parent": parent_idx,
                           "t": [float(x) for x in cv_pos(t)], "r": [float(x) for x in cv_rot(r)], "s": [float(x) for x in s]})
        self.index[(group, name)] = idx
        self.paths[idx] = name if parent_idx < 0 else f"{self.paths[parent_idx]}/{name}"
        return idx

    def add_gltf(self, g, group, root_name, merge_into=None):
        """Adds a glTF skin's joint hierarchy under a root node called root_name.
        merge_into: group whose bones are reused when a joint of the same name already exists there (face -> body)."""
        nodes = g["nodes"]
        joints = g["skins"][0]["joints"]
        jset = set(joints)
        parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        if (group, root_name) not in self.index and not merge_into:
            self.add(group, root_name, -1, [0, 0, 0], [0, 0, 0, 1], [1, 1, 1])
        node_to_bone = {}

        def visit(i, parent_bone):
            n = nodes[i]
            name = n["name"]
            existing = self.index.get((merge_into, name)) if merge_into else None
            if existing is not None:
                b = existing
            else:
                b = self.add(merge_into or group, name, parent_bone, n.get("translation", [0, 0, 0]),
                             n.get("rotation", [0, 0, 0, 1]), n.get("scale", [1, 1, 1]))
            node_to_bone[i] = b
            for c in n.get("children", []):
                if c in jset:
                    visit(c, b)

        root_bone = self.index[(merge_into or group, root_name)]
        for j in joints:
            if parent.get(j) not in jset:
                visit(j, root_bone)
        return [node_to_bone[j] for j in joints]   # skin joint index -> bone index


# ---------------------------------------------------------------- meshes and materials

def material_textures(g, mat_idx):
    m = g["materials"][mat_idx] if mat_idx is not None else {}
    def img(tex):
        if tex is None:
            return ""
        return g["images"][g["textures"][tex["index"]]["source"]]["uri"]
    return img(m.get("pbrMetallicRoughness", {}).get("baseColorTexture")), img(m.get("normalTexture"))


def export_meshes(g, buf, joint_bones, group, variant_gltfs, src_dir, out_bin, renderers, textures, adjust=None,
                  name_prefix=None, extra=None, include=None, texture_override=None):
    """adjust(mesh_name, glTF positions) -> (positions, note or None) lets a group move meshes before export.
    name_prefix: renderer names start with it instead of the group; extra: fields added to every renderer entry.
    include(primitive) -> bool picks the primitives to export (default: all).
    texture_override(primitive, albedo, normal) -> (albedo, normal) paths, or None to keep the material's own."""
    for mi, mesh in enumerate(g["meshes"]):
        if not mesh["name"].startswith("lod0."):
            continue
        for pi, prim in enumerate(mesh["primitives"]):
            if include and not include(prim):
                continue
            at = prim["attributes"]
            raw_pos = pa.acc(g, buf, at["POSITION"]).astype(np.float64)
            raw_nrm = pa.acc(g, buf, at["NORMAL"]).astype(np.float64) if "NORMAL" in at else np.zeros_like(raw_pos)
            raw_tan = pa.acc(g, buf, at["TANGENT"]).astype(np.float64) if "TANGENT" in at else np.tile([1.0, 0, 0, 1], (len(raw_pos), 1))
            uv = pa.acc(g, buf, at["TEXCOORD_0"]).astype(np.float64)
            raw_ind = pa.acc(g, buf, prim["indices"]).reshape(-1, 3)
            note = None
            if adjust:
                raw_pos, note = adjust(mesh["name"], raw_pos)
            pos = cv_pos(raw_pos)
            nrm = cv_pos(raw_nrm)
            tan = cv_tan(raw_tan)
            uv[:, 1] = 1.0 - uv[:, 1]
            n = len(pos)
            if "JOINTS_0" in at:
                js = [pa.acc(g, buf, at["JOINTS_0"]).astype(int)]
                ws = [pa.acc(g, buf, at["WEIGHTS_0"]).astype(np.float64)]
                if "JOINTS_1" in at:
                    js.append(pa.acc(g, buf, at["JOINTS_1"]).astype(int))
                    ws.append(pa.acc(g, buf, at["WEIGHTS_1"]).astype(np.float64))
                J = np.concatenate(js, 1)
                W = np.concatenate(ws, 1)
            else:  # rigid: bound to the skeleton root joint
                J = np.zeros((n, 1), int)
                W = np.ones((n, 1))
            # keep the 4 strongest influences
            order = np.argsort(-W, axis=1)[:, :4]
            J4 = np.take_along_axis(J, order, 1)
            W4 = np.take_along_axis(W, order, 1)
            if J4.shape[1] < 4:
                pad = 4 - J4.shape[1]
                J4 = np.pad(J4, ((0, 0), (0, pad)))
                W4 = np.pad(W4, ((0, 0), (0, pad)))
            s = W4.sum(1, keepdims=True)
            W4 = W4 / np.where(s > 0, s, 1)
            # renderer-local bone list
            gb = np.vectorize(lambda j: joint_bones[j])(J4)
            used = sorted(set(gb[W4 > 0].tolist())) or [joint_bones[0]]
            local = {b: k for k, b in enumerate(used)}
            LB = np.vectorize(lambda b: local.get(b, 0))(gb)
            # GBFR stores triangles clockwise (DirectX order) and the glTF keeps that order. The X mirror into Unity's
            # left-handed space reverses winding again, which makes them clockwise-front-facing for Unity: keep as is.
            ind = raw_ind

            off = out_bin.tell()
            for arr, dt in ((pos, "<f4"), (nrm, "<f4"), (tan, "<f4"), (uv, "<f4"), (LB, "<i4"), (W4, "<f4"), (ind, "<i4")):
                out_bin.write(np.ascontiguousarray(arr, dt).tobytes())

            # textures per colour variant (same primitive order in every variant file)
            per_variant = []
            for vg in variant_gltfs:
                vprim = vg["meshes"][mi]["primitives"][pi]
                alb, nor = material_textures(vg, vprim.get("material"))
                override = texture_override(vprim, alb, nor) if texture_override else None
                if override:
                    alb, nor = override
                for t in (alb, nor):
                    if t:
                        textures.add(os.path.join(src_dir, t))  # (absolute paths stay as they are)
                per_variant.append({"albedo": os.path.basename(alb), "normal": os.path.basename(nor)})
            renderers.append({
                "name": f"{name_prefix or group}_{mesh['name'][5:].replace('.', '_')}_{pi}",
                "group": group,
                "offset": off,
                "vertexCount": n,
                "indexCount": int(ind.size),
                "bones": used,
                "variants": per_variant,
                **({"adjusted": note} if note else {}),
                **(extra or {}),
            })


# ---------------------------------------------------------------- crystals

def bow_crystal_textures(extract, wp):
    """The bow's "ice" albedo and normal map (paths)."""
    d = f"{extract}/weapons/{wp}"
    alb = next(f for f in sorted(os.listdir(d)) if f.startswith(f"{wp}_ice") and f.endswith("_lod0_albd.png"))
    return os.path.join(d, alb), os.path.join(d, alb.replace("_albd", "_nrml"))


def _srgb_to_lab(rgb):
    c = rgb / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    xyz = c @ np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]]).T
    xyz /= np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def _lab_to_srgb(lab):
    fy = (lab[..., 0] + 16) / 116
    f = np.stack([fy + lab[..., 1] / 500, fy, fy - lab[..., 2] / 200], -1)
    xyz = np.where(f ** 3 > 0.008856, f ** 3, (f - 16 / 116) / 7.787) * np.array([0.95047, 1.0, 1.08883])
    c = xyz @ np.array([[3.2406, -1.5372, -0.4986], [-0.9689, 1.8758, 0.0415], [0.0557, -0.2040, 1.0570]]).T
    c = np.clip(c, 0, 1)
    c = np.where(c <= 0.0031308, 12.92 * c, 1.055 * c ** (1 / 2.4) - 0.055)
    return np.clip(c * 255, 0, 255)


def recolour_like(src_path, ref_path, out_path):
    """Copies src's texture with the colours of ref (mean and spread of each Lab channel matched), keeping src's
    pattern and shading. Alpha is kept."""
    from PIL import Image
    src = np.asarray(Image.open(src_path).convert("RGBA")).astype(float)
    ref = np.asarray(Image.open(ref_path).convert("RGB")).astype(float)
    a, b = _srgb_to_lab(src[..., :3]), _srgb_to_lab(ref)
    sm, ss = a.reshape(-1, 3).mean(0), a.reshape(-1, 3).std(0) + 1e-6
    rm, rs = b.reshape(-1, 3).mean(0), b.reshape(-1, 3).std(0)
    out = _lab_to_srgb((a - sm) / ss * rs + rm)
    Image.fromarray(np.dstack([out, src[..., 3]]).astype(np.uint8), "RGBA").save(out_path)


def body_crystal_textures(extract, generated, base, wp):
    """Tweyen's crystal texture `base` for bow wp: her own for the default bow, else recoloured like the bow's crystal."""
    alb = f"{extract}/model/{base}_lod0_albd.png"
    nor = f"{extract}/model/{base}_lod0_nrml.png"
    if wp == BOW:
        return alb, nor
    out = os.path.join(generated, f"{base}_{wp}_lod0_albd.png")
    if not os.path.exists(out):
        recolour_like(alb, bow_crystal_textures(extract, wp)[0], out)
    return out, nor


# ---------------------------------------------------------------- face mask

# Tweyen gets a blank face, like Mercenary's or Engineer's: a plain, smooth, skin-coloured shell replaces her face (no
# eyes, brows, nose or mouth). Nothing is deleted: everything of the old face that would show through is moved straight
# back into her head (the features by "push_back", and anything still in front of the shell, the nose and lips included,
# to "clearance" behind it). Original position = exported position moved forward again; the extracted glTF is untouched
# and each moved renderer records it in its "adjusted" note. The rest of the head is not touched.
FACE_MASK = {
    "source": "lod0.Face.2",                         # face skin mesh (shape, bone weights and material come from it)
    "features": ["lod0.Face.0", "lod0.Face.1", "lod0.Face.3", "lod0.Face.4", "lod0.Face.5"],  # eyes, mouth, brows/lashes
    "feature_max_y": 1.66,                           # above this, Face.5 is hairline strands, which stay visible
    "centre": (0.0, 1.596),                          # oval over brows, eyes, nose and mouth (glTF space, she faces +Z)
    "radii": (0.066, 0.058),                         # x, y: from just above the chin tip to just above the brows
    "rings": 40, "segments": 128,                    # mask mesh resolution (smooth oval outline)
    "map_cell": 0.0005,                              # depth-map resolution (m)
    "margin": 0.0012,                                # metres kept in front of the skin and of what it covers
    "push_back": 0.012,                              # metres the features move back (-Z in glTF, she faces +Z)
    "push_blend": 0.004,                             # Face.5 strands blend from pushed to unmoved over this height above feature_max_y
    "skin_fill_iterations": 200,                     # fills skin creases (eyelid folds, lip line) without lowering anything
    "surface_smooth_iterations": 80,                 # then softens the result, staying "margin" in front of the real skin
    "skin_uv": (0.582, 0.770),                       # plain lower-cheek texel: her skin tone in every skin
    "shell_cut": 0.0015,                             # skin more than this in front of the shell (nose, lips) is left out
                                                     # of its fit, so the shell stays plain instead of bulging over them
    "shell_blend_from": 0.55,                        # fraction of the oval where the shell starts blending into her skin
    "flat_inside": 0.30,                             # bone weights: rigid with the head inside this fraction of the oval,
                                                     # blending to the skin's own weights at the oval's edge
    "clearance": 0.002,                              # old face surfaces end at least this far behind the shell
}

_MASK = {}  # build_face_mask result, also used to move the old face behind the shell


def _face_prims(g, buf):
    out = {}
    for mi, mesh in enumerate(g["meshes"]):
        if mesh["name"].startswith("lod0."):
            for pi, prim in enumerate(mesh["primitives"]):
                out[mesh["name"]] = (mi, pi, prim)
    return out


def _raster_front_depth(P, I, x0, y0, cell, shape, front_only_normals=None):
    """Front-most z of the triangles at each cell of a grid (orthographic view from +Z). NaN where nothing."""
    H, W = shape
    depth = np.full(shape, np.nan)
    gx = (P[:, 0] - x0) / cell
    gy = (P[:, 1] - y0) / cell
    if front_only_normals is not None:
        I = I[front_only_normals[I].mean(1)[:, 2] > 0.0]
    for tri in I:
        xs, ys = gx[tri], gy[tri]
        mnx, mxx = max(int(np.floor(xs.min())), 0), min(int(np.ceil(xs.max())), W - 1)
        mny, mxy = max(int(np.floor(ys.min())), 0), min(int(np.ceil(ys.max())), H - 1)
        if mnx > mxx or mny > mxy:
            continue
        cx, cy = np.meshgrid(np.arange(mnx, mxx + 1), np.arange(mny, mxy + 1))
        d = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
        if abs(d) < 1e-12:
            continue
        a = ((ys[1] - ys[2]) * (cx - xs[2]) + (xs[2] - xs[1]) * (cy - ys[2])) / d
        b = ((ys[2] - ys[0]) * (cx - xs[2]) + (xs[0] - xs[2]) * (cy - ys[2])) / d
        c = 1 - a - b
        inside = (a >= -1e-6) & (b >= -1e-6) & (c >= -1e-6)
        if not inside.any():
            continue
        z = a * P[tri[0], 2] + b * P[tri[1], 2] + c * P[tri[2], 2]
        sub = depth[mny:mxy + 1, mnx:mxx + 1]
        upd = inside & (np.isnan(sub) | (z > sub))
        sub[upd] = z[upd]
    return depth


def _fill_nan(a, mask):
    """Fills NaN cells inside `mask` by repeatedly averaging their known neighbours (smooth inpainting)."""
    a = a.copy()
    while np.isnan(a[mask]).any():
        pad = np.pad(a, 1, constant_values=np.nan)
        stack = np.stack([pad[:-2, 1:-1], pad[2:, 1:-1], pad[1:-1, :-2], pad[1:-1, 2:]])
        cnt = (~np.isnan(stack)).sum(0)
        avg = np.where(cnt > 0, np.nansum(stack, 0) / np.maximum(cnt, 1), np.nan)
        hole = np.isnan(a) & mask & (cnt > 0)
        if not hole.any():
            break
        a[hole] = avg[hole]
    return a


def _bilinear(a, x, y):
    x0 = np.clip(np.floor(x).astype(int), 0, a.shape[1] - 2)
    y0 = np.clip(np.floor(y).astype(int), 0, a.shape[0] - 2)
    fx, fy = x - x0, y - y0
    return (a[y0, x0] * (1 - fx) * (1 - fy) + a[y0, x0 + 1] * fx * (1 - fy) +
            a[y0 + 1, x0] * (1 - fx) * fy + a[y0 + 1, x0 + 1] * fx * fy)


def push_back_features(name, P):
    """Moves the old face behind the shell: the features (eyes, mouth, brows, lashes) go back by push_back, and any face
    surface (features or skin) still less than `clearance` behind the shell goes back to that. Hairline strands and
    everything outside the shell's oval stay where they are. Needs build_face_mask to have run."""
    cfg = FACE_MASK
    if name not in cfg["features"] and name != cfg["source"]:
        return P, None
    orig = P
    P = P.copy()
    if name in cfg["features"]:
        k = np.clip((cfg["feature_max_y"] + cfg["push_blend"] - P[:, 1]) / cfg["push_blend"], 0.0, 1.0)
        P[:, 2] -= cfg["push_back"] * k
    m = _MASK["grid"]
    cx, cy = cfg["centre"]
    rx, ry = cfg["radii"]
    inside = ((P[:, 0] - cx) / rx) ** 2 + ((P[:, 1] - cy) / ry) ** 2 < 1.0
    limit = _bilinear(m["depth"], (P[:, 0] - m["x0"]) / m["cell"], (P[:, 1] - m["y0"]) / m["cell"]) - cfg["clearance"]
    P[:, 2] = np.where(inside, np.minimum(P[:, 2], limit), P[:, 2])
    moved = orig[:, 2] - P[:, 2]
    if moved.max() <= 0:
        return orig, None
    return P, {"axis": "-forward", "maxMetres": round(float(moved.max()), 4), "verticesMoved": int((moved > 0).sum())}


def build_face_mask(g, buf):
    if not _MASK:
        _MASK.update(_build_face_mask(g, buf))
    return _MASK


def _build_face_mask(g, buf):
    """A plain shell seen from the front: an ellipsoid fitted to her face (ignoring the nose and lips), blending into
    her skin shape (openings filled, creases smoothed, plus a small margin) at the oval's edge. The old face
    is moved behind it (push_back_features); this checks nothing pokes through. Built on an oval ring mesh.
    Returns glTF-space arrays plus the (mesh, primitive) index of the face skin, whose weights and materials it uses."""
    cfg = FACE_MASK
    prims = _face_prims(g, buf)
    mi, pi, src = prims[cfg["source"]]
    at = src["attributes"]
    P = pa.acc(g, buf, at["POSITION"]).astype(np.float64)
    N = pa.acc(g, buf, at["NORMAL"]).astype(np.float64)
    T = pa.acc(g, buf, at["TANGENT"]).astype(np.float64)
    J = np.concatenate([pa.acc(g, buf, at[k]).astype(int) for k in ("JOINTS_0", "JOINTS_1") if k in at], 1)
    Wt = np.concatenate([pa.acc(g, buf, at[k]).astype(np.float64) for k in ("WEIGHTS_0", "WEIGHTS_1") if k in at], 1)
    I = pa.acc(g, buf, src["indices"]).reshape(-1, 3)

    cx, cy = cfg["centre"]
    rx, ry = cfg["radii"]
    cell = cfg["map_cell"]
    pad = 0.006
    x0, y0 = cx - rx - pad, cy - ry - pad
    shape = (int(round((2 * (ry + pad)) / cell)) + 1, int(round((2 * (rx + pad)) / cell)) + 1)
    GY, GX = np.mgrid[0:shape[0], 0:shape[1]]
    oval = (((GX * cell + x0 - cx) / (rx + pad)) ** 2 + ((GY * cell + y0 - cy) / (ry + pad)) ** 2) <= 1.0

    skin = _raster_front_depth(P, I, x0, y0, cell, shape, front_only_normals=N)
    raw_known = ~np.isnan(skin)
    skin = _fill_nan(skin, np.ones(shape, bool))  # whole grid, so the blurs below never meet NaN
    covered_skin = skin.copy()
    for _ in range(cfg["skin_fill_iterations"]):  # clamped blur: creases fill in, nothing sinks
        pd = np.pad(skin, 1, mode="edge")
        avg = (pd[:-2, 1:-1] + pd[2:, 1:-1] + pd[1:-1, :-2] + pd[1:-1, 2:] + pd[1:-1, 1:-1]) / 5.0
        skin = np.where(oval, np.maximum(covered_skin, avg), skin)

    depth = skin + cfg["margin"]
    floor = covered_skin + cfg["margin"]
    for _ in range(cfg["surface_smooth_iterations"]):
        pd = np.pad(depth, 1, mode="edge")
        avg = (pd[:-2, 1:-1] + pd[2:, 1:-1] + pd[1:-1, :-2] + pd[1:-1, 2:] + pd[1:-1, 1:-1]) / 5.0
        depth = np.where(oval, np.maximum(floor, avg), depth)
    edge_depth = depth

    # the plain shell: an ellipsoid fitted to her face (refit without what sticks out in front of it: nose, lips), so it
    # wraps round the cheeks like her face does with nothing on it; it blends into her real skin shape at the oval's edge
    X = GX * cell + x0 - cx
    Y = GY * cell + y0 - cy
    rho = np.sqrt((X / rx) ** 2 + (Y / ry) ** 2)
    zref = covered_skin[raw_known & (rho <= 1.0)].mean()
    use = raw_known & (rho <= 1.0)
    for _ in range(10):
        x, y, z = X[use], Y[use], covered_skin[use] - zref
        q, *_ = np.linalg.lstsq(np.stack([x * x, y * y, z * z, x, y, z], 1), np.ones(len(x)), rcond=None)
        a, b = q[2], q[5]  # a z^2 + b z + c = 0 on every cell; the front root is the shell
        c = q[0] * X ** 2 + q[1] * Y ** 2 + q[3] * X + q[4] * Y - 1.0
        root = np.sqrt(np.maximum(b * b - 4 * a * c, 0.0))
        shell = np.maximum((-b + root) / (2 * a), (-b - root) / (2 * a)) + zref
        use = raw_known & (rho <= 1.0) & (covered_skin - shell < cfg["shell_cut"])
    t = np.clip((rho - cfg["shell_blend_from"]) / (1.0 - cfg["shell_blend_from"]), 0.0, 1.0)
    blend = 1.0 - t * t * (3.0 - 2.0 * t)
    depth = blend * (shell + cfg["margin"]) + (1.0 - blend) * edge_depth
    t = np.clip((rho - cfg["flat_inside"]) / (1.0 - cfg["flat_inside"]), 0.0, 1.0)
    flat = 1.0 - t * t * (3.0 - 2.0 * t)  # 1 = moves with the head only, 0 = with the skin it meets
    cover = depth - covered_skin
    _MASK["grid"] = {"depth": depth, "x0": x0, "y0": y0, "cell": cell}

    # check: nothing of the old face (as moved by push_back_features) pokes through the shell
    front = np.full(shape, np.nan)
    for name in cfg["features"] + [cfg["source"]]:
        if name not in prims:
            continue
        fp, _ = push_back_features(name, pa.acc(g, buf, prims[name][2]["attributes"]["POSITION"]).astype(np.float64))
        fi = pa.acc(g, buf, prims[name][2]["indices"]).reshape(-1, 3)
        d = _raster_front_depth(fp, fi, x0, y0, cell, shape)
        front = np.where(np.isnan(front), d, np.where(np.isnan(d), front, np.maximum(front, d)))
    inner = rho <= 0.98
    gap = (depth - front)[inner & ~np.isnan(front)]
    print(f"face mask: the old face is {gap.min() * 1000:.2f} mm or more behind the shell")
    assert gap.min() > 0.0002, "the old face pokes through the shell: raise clearance"

    # oval ring mesh: ring 0 = centre point, ring k at k/rings of the radii
    R, S = cfg["rings"], cfg["segments"]
    pts = [(cx, cy)]
    for k in range(1, R + 1):
        rho = k / R
        for j in range(S):
            th = 2 * np.pi * j / S
            pts.append((cx + rx * rho * np.cos(th), cy + ry * rho * np.sin(th)))
    pts = np.array(pts)
    zs = _bilinear(depth, (pts[:, 0] - x0) / cell, (pts[:, 1] - y0) / cell)
    pos = np.column_stack([pts, zs])
    def ring(k, j):
        return 0 if k == 0 else 1 + (k - 1) * S + (j % S)
    tris = []
    for j in range(S):
        tris.append((ring(0, 0), ring(1, j + 1), ring(1, j)))
    for k in range(1, R):
        for j in range(S):
            a, b, c, d = ring(k, j), ring(k, j + 1), ring(k + 1, j), ring(k + 1, j + 1)
            tris.append((a, b, c))
            tris.append((b, d, c))
    tris = np.array(tris, int)
    # match the game meshes' winding (clockwise seen from the front, +Z), and point normals out of the face
    fn = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
    if (fn[:, 2] > 0).mean() > 0.5:
        tris = tris[:, [0, 2, 1]]
        fn = -fn
    nrm = np.zeros_like(pos)
    for k in range(3):
        np.add.at(nrm, tris[:, k], fn)
    nrm = -nrm  # clockwise-front winding: cross product points into the face
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-12

    near = np.array([int(np.argmin(np.linalg.norm(P - q, axis=1))) for q in pos])
    # the plain part moves with the head bone only (no expressions show through); the edge keeps the skin's weights
    top = (np.abs(P[:, 0] - cx) < 0.01) & (np.abs(P[:, 1] - (cy + ry)) < 0.01) & (N[:, 2] > 0)
    head = int(np.bincount(J[top][np.arange(top.sum()), np.argmax(Wt[top], 1)]).argmax())
    fl = _bilinear(flat, (pts[:, 0] - x0) / cell, (pts[:, 1] - y0) / cell)[:, None]
    Jm = np.concatenate([J[near], np.full((len(pos), 1), head)], 1)
    Wm = np.concatenate([Wt[near] * (1.0 - fl), fl], 1)
    uv = np.tile(np.array(cfg["skin_uv"], float), (len(pos), 1))
    cov = _bilinear(cover, (pts[:, 0] - x0) / cell, (pts[:, 1] - y0) / cell)
    print(f"face mask: {len(pos)} vertices, {len(tris)} triangles, {cov.min() * 1000:.1f}-{cov.max() * 1000:.1f} mm in front of the skin")
    return {"pos": pos, "nrm": nrm, "tan": T[near], "uv": uv, "J": Jm, "W": Wm, "ind": tris, "mesh": mi, "prim": pi}


def export_face_mask(g, buf, joint_bones, variant_gltfs, src_dir, out_bin, renderers, textures):
    """Writes the mask as a renderer of the face group, using the face skin's material in every colour variant."""
    m = build_face_mask(g, buf)
    pos, nrm, tan = cv_pos(m["pos"]), cv_pos(m["nrm"]), cv_tan(m["tan"])
    uv = m["uv"].copy()
    uv[:, 1] = 1.0 - uv[:, 1]
    order = np.argsort(-m["W"], axis=1)[:, :4]
    J4 = np.take_along_axis(m["J"], order, 1)
    W4 = np.take_along_axis(m["W"], order, 1)
    W4 = W4 / np.where(W4.sum(1, keepdims=True) > 0, W4.sum(1, keepdims=True), 1)
    gb = np.vectorize(lambda j: joint_bones[j])(J4)
    used = sorted(set(gb[W4 > 0].tolist()))
    local = {b: k for k, b in enumerate(used)}
    LB = np.vectorize(lambda b: local.get(b, 0))(gb)
    off = out_bin.tell()
    for arr, dt in ((pos, "<f4"), (nrm, "<f4"), (tan, "<f4"), (uv, "<f4"), (LB, "<i4"), (W4, "<f4"), (m["ind"], "<i4")):
        out_bin.write(np.ascontiguousarray(arr, dt).tobytes())
    per_variant = []
    for vg in variant_gltfs:
        vprim = vg["meshes"][m["mesh"]]["primitives"][m["prim"]]
        alb, nor = material_textures(vg, vprim.get("material"))
        for tx in (alb, nor):
            if tx:
                textures.add(os.path.join(src_dir, tx))
        per_variant.append({"albedo": alb, "normal": nor})
    renderers.append({"name": "face_mask", "group": "face", "offset": off, "vertexCount": len(pos),
                      "indexCount": int(m["ind"].size), "bones": used, "variants": per_variant})


# ---------------------------------------------------------------- animation clips

def rdp_keep(t, v, tol):
    """Ramer-Douglas-Peucker on a 1D curve sampled at times t; returns kept indices."""
    n = len(v)
    if n <= 2:
        return list(range(n))
    keep = np.zeros(n, bool)
    keep[0] = keep[-1] = True
    stack = [(0, n - 1)]
    while stack:
        a, b = stack.pop()
        if b - a < 2:
            continue
        seg_t = t[a + 1:b]
        interp = v[a] + (v[b] - v[a]) * (seg_t - t[a]) / (t[b] - t[a])
        err = np.abs(v[a + 1:b] - interp)
        k = int(np.argmax(err))
        if err[k] > tol:
            m = a + 1 + k
            keep[m] = True
            stack.append((a, m))
            stack.append((m, b))
    return np.nonzero(keep)[0].tolist()


def add_curves(curves, path, prop, times, values, tol):
    """values: (frames, components). One reduced curve per component."""
    names = {"localPosition": "xyz", "localRotation": "xyzw", "localScale": "xyz"}[prop]
    for k, c in enumerate(names):
        v = values[:, k]
        if np.allclose(v, v[0], atol=tol):
            idx = [0, len(v) - 1] if len(v) > 1 else [0]
        else:
            idx = rdp_keep(times, v, tol)
        curves.append({"path": path, "prop": f"{prop}.{c}", "times": [round(float(times[i]), 5) for i in idx],
                       "values": [float(v[i]) for i in idx]})


def sample_clip(skel, group, anim_path):
    """Samples a mot2gltf animation onto a 60 fps grid in Unity space.
    Returns ({(path, prop): (frames, comps)}, root translation (frames, 3), frame count)."""
    g, buf = pa.load_gltf(anim_path)
    nodes = g["nodes"]
    anim = g["animations"][0]
    raw = []
    t_max = 0.0
    for ch in anim["channels"]:
        smp = anim["samplers"][ch["sampler"]]
        times = pa.acc(g, buf, smp["input"])[:, 0].astype(np.float64)
        vals = pa.acc(g, buf, smp["output"]).astype(np.float64)
        raw.append((nodes[ch["target"]["node"]].get("name"), ch["target"]["path"], times, vals))
        t_max = max(t_max, float(times[-1]))
    n = int(round(t_max * FPS_OUT)) + 1
    grid = np.arange(n) / FPS_OUT
    channels = {}
    root = np.zeros((n, 3))
    for name, kind, times, vals in raw:
        v = np.stack([np.interp(grid, times, vals[:, k]) for k in range(vals.shape[1])], 1)
        if kind == "rotation":
            v /= np.linalg.norm(v, axis=1, keepdims=True) + 1e-12
        if name == "Armature":
            if kind == "translation":
                root = cv_pos(v)
            continue  # root motion: movement is driven by the game, not the clip
        b = skel.index.get((group, name))
        if b is None:
            continue
        path = skel.paths[b]
        if kind == "translation":
            channels[(path, "localPosition")] = cv_pos(v)
        elif kind == "rotation":
            channels[(path, "localRotation")] = cv_rot(v)
        else:
            channels[(path, "localScale")] = v
    return channels, root, n


def pad_frames(v, n):
    return v if len(v) >= n else np.concatenate([v, np.repeat(v[-1:], n - len(v), axis=0)])


def resample(v, a, b, speed, is_rotation):
    """Frames a..b of v played `speed` times faster (linear interpolation, quaternions renormalised)."""
    return sample_at(v, np.arange(a, b + 1e-9, speed), is_rotation)


def sample_at(v, pos, is_rotation):
    """v evaluated at fractional frame positions (linear interpolation, quaternions renormalised)."""
    i0 = np.clip(np.floor(pos).astype(int), 0, len(v) - 1)
    i1 = np.clip(i0 + 1, 0, len(v) - 1)
    f = (pos - i0)[:, None]
    lo, hi = v[i0], v[i1].copy()
    if is_rotation:
        hi[(lo * hi).sum(1) < 0] *= -1
    out = lo * (1 - f) + hi * f
    if is_rotation:
        out /= np.linalg.norm(out, axis=1, keepdims=True) + 1e-12
    return out


def spin_speed(yaw, a, b):
    """Average turning speed (deg/s) of the hips while actually spinning within frames a..b."""
    d = np.abs(np.diff(yaw[a:b + 1]))
    moving = d > 0.3 * d.max()
    return d[moving].sum() / (moving.sum() / FPS_OUT)


def apply_segments(skel, channels, n, segments, blend):
    """Keeps the given (start, end, speed) time segments, speed 1 = as authored, "match" = as fast as the first
    segment's spin, and crossfades each join over `blend` seconds."""
    W = max(1, int(round(blend * FPS_OUT)))
    yaw = hips_yaw(skel, channels, n)
    spans = [(int(round(a * FPS_OUT)), min(int(round(b * FPS_OUT)), n - 1), sp) for a, b, sp in segments]
    ref_speed = spin_speed(yaw, spans[0][0], spans[0][1])
    speeds = []
    for a, b, sp in spans:
        if sp == "match":
            sp = ref_speed / spin_speed(yaw, a, b)
        speeds.append(float(sp))
    print("  segment playback speeds:", ", ".join(f"{x:.2f}x" for x in speeds))
    out = {}
    for key, v in channels.items():
        v = pad_frames(v, n)
        rot = key[1] == "localRotation"
        parts = [resample(v, a, b, sp, rot) for (a, b, _), sp in zip(spans, speeds)]
        result = parts[0]
        for nxt in parts[1:]:
            w = np.linspace(0, 1, W)[:, None]
            tail, head = result[-W:], nxt[:W].copy()
            if rot:
                head[(tail * head).sum(1) < 0] *= -1
            mixed = tail * (1 - w) + head * w
            if rot:
                mixed /= np.linalg.norm(mixed, axis=1, keepdims=True)
            result = np.concatenate([result[:-W], mixed, nxt[W:]])
        out[key] = result
    return out, len(next(iter(out.values())))


def quat_rotate(q, v):
    x, y, z, w = q
    R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    return R @ v


def quat_mul(a, b):
    x1, y1, z1, w1 = a
    x2, y2, z2, w2 = b
    return np.array([w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2, w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2,
                     w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2, w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2])


def hips_yaw(skel, channels, n):
    """Unwrapped facing yaw of the hips per frame (degrees, Unity: 0 = model forward, positive = clockwise from above)."""
    hips = skel.index[("body", "_000")]
    parent_rot = np.array([0.0, 0.0, 0.0, 1.0])
    b = skel.bones[hips]["parent"]
    while b >= 0:  # rest rotations of the (unanimated) ancestors
        parent_rot = quat_mul(np.array(skel.bones[b]["r"]), parent_rot)
        b = skel.bones[b]["parent"]
    q = pad_frames(channels[(skel.paths[hips], "localRotation")], n)
    yaw = []
    for f in range(n):
        fwd = quat_rotate(quat_mul(parent_rot, q[f]), np.array([0.0, 0.0, 1.0]))
        yaw.append(math.degrees(math.atan2(fwd[0], fwd[2])))
    return np.degrees(np.unwrap(np.radians(yaw)))


def uniform_spin(skel, channels, n, lead_in=5.0):
    """Retimes the spinning part of a clip so the hips turn at a constant speed: the average speed of its first
    full turn. The wind-up before the hips have turned `lead_in` degrees and the settle after the last
    `lead_in` degrees are kept as they are."""
    yaw = hips_yaw(skel, channels, n)
    turn = np.sign(yaw[-1] - yaw[0])
    prog = np.maximum.accumulate((yaw - yaw[0]) * turn) + np.arange(n) * 1e-6  # strictly increasing
    start = int(np.argmax(prog > lead_in))
    end = int(np.argmax(prog >= prog[-1] - lead_in))
    first_turn = int(np.argmax(prog >= prog[start] + 360.0)) if prog[-1] >= prog[start] + 360.0 else end
    speed = (prog[first_turn] - prog[start]) / max((first_turn - start) / FPS_OUT, 1e-3)  # deg/s
    frames_mid = max(2, int(round((prog[end] - prog[start]) / speed * FPS_OUT)))
    targets = np.linspace(prog[start], prog[end], frames_mid + 1)
    mid_pos = np.interp(targets, prog[start:end + 1], np.arange(start, end + 1))
    pos = np.concatenate([np.arange(0, start), mid_pos, np.arange(end + 1, n)]).astype(float)
    out = {key: sample_at(pad_frames(v, n), pos, key[1] == "localRotation") for key, v in channels.items()}
    print(f"  uniform spin: {speed:.0f} deg/s, spinning part {(end - start) / FPS_OUT:.2f} s -> {frames_mid / FPS_OUT:.2f} s")
    return out, len(pos)


def spin_events(skel, channels, n, targets):
    """A "Fire" event when the hips have turned each angle in `targets` (degrees from the clip's start, in the
    spin's direction): time (s) and the facing yaw at that moment."""
    yaw = hips_yaw(skel, channels, n)
    turn = np.sign(yaw[-1] - yaw[0])
    progress = (yaw - yaw[0]) * turn
    events = []
    for target in targets:
        reached = progress >= target
        idx = int(np.argmax(reached)) if reached.any() else n - 1
        if idx > 0 and reached.any():
            p0, p1 = progress[idx - 1], progress[idx]
            t = (idx - 1 + ((target - p0) / (p1 - p0) if p1 != p0 else 0.0)) / FPS_OUT
        else:
            t = idx / FPS_OUT
        events.append({"name": "Fire", "time": round(float(t), 4), "yaw": round(float(yaw[0] + turn * target), 2)})
    print(f"  spin: {progress[-1]:.0f} degrees over {n / FPS_OUT:.2f} s, events at " + ", ".join(f"{e['time']:.2f}s" for e in events))
    return events


def offset_weapon_root(skel, group, channels, n, offset):
    """Weapon animations carry their own root motion, which can differ from the body's (in 3440 the body moves 3.8 m
    while the bow's root stays put and the bow is placed in world space). Root motion is dropped, so express the
    weapon relative to the body: add (weapon root motion - body root motion) to its top joints."""
    if not np.any(np.abs(offset) > 1e-4):
        return
    for idx, bone in enumerate(skel.bones):
        if bone["parent"] < 0 or skel.bones[bone["parent"]]["parent"] >= 0:
            continue
        if skel.index.get((group, bone["name"])) != idx:
            continue
        key = (skel.paths[idx], "localPosition")
        base = pad_frames(channels[key], n) if key in channels else np.tile(bone["t"], (n, 1))
        channels[key] = base + offset


def pin_weapons_to_hand(skel, channels, n, weapon_group, weapon_joint, hand_joint, groups, tol=0.06):
    """Some weapon animations let the bow leave her hand (31ca: after the release it drifts 2+ m away; Relink hides
    it, the mod can't): from the first frame the weapon joint's distance to the hand joint changes by more than `tol`,
    the weapons in `groups` are moved rigidly so that joint keeps the pose relative to the hand it had on the frame
    before."""
    def rest(i, prop):
        b = skel.bones[i]
        return np.array(b["t"] if prop == "localPosition" else b["r"], float)

    def local(i, prop, f):
        key = (skel.paths[i], prop)
        return pad_frames(channels[key], n)[f] if key in channels else rest(i, prop)

    def world(i, f):
        chain = []
        while i >= 0:
            chain.append(i); i = skel.bones[i]["parent"]
        p, q = np.zeros(3), np.array([0.0, 0.0, 0.0, 1.0])
        for j in reversed(chain):
            p = p + quat_rotate(q, local(j, "localPosition", f))
            q = quat_mul(q, local(j, "localRotation", f))
        return p, q

    def inv(t):
        p, q = t; qi = np.array([-q[0], -q[1], -q[2], q[3]])
        return -quat_rotate(qi, p), qi

    def mul(a, b):
        return a[0] + quat_rotate(a[1], b[0]), quat_mul(a[1], b[1])

    w = skel.index[(weapon_group, weapon_joint)]
    h = skel.index[("body", hand_joint)]
    d0 = np.linalg.norm(world(w, 0)[0] - world(h, 0)[0])
    start = next((f for f in range(n) if abs(np.linalg.norm(world(w, f)[0] - world(h, f)[0]) - d0) > tol), None)
    if start is None or start == 0:
        return
    rel = mul(inv(world(h, start - 1)), world(w, start - 1))
    tops = [i for i, b in enumerate(skel.bones)
            if b["parent"] >= 0 and skel.bones[b["parent"]]["parent"] < 0
            and any(skel.index.get((g, b["name"])) == i for g in groups)]
    new = {i: (np.array([local(i, "localPosition", f) for f in range(n)]),
               np.array([local(i, "localRotation", f) for f in range(n)])) for i in tops}
    for f in range(start, n):
        fix = mul(mul(world(h, f), rel), inv(world(w, f)))
        for i in tops:
            root = world(skel.bones[i]["parent"], f)
            p, q = mul(inv(root), mul(fix, world(i, f)))
            new[i][0][f], new[i][1][f] = p, q / np.linalg.norm(q)
    for i, (p, q) in new.items():
        channels[(skel.paths[i], "localPosition")] = p
        channels[(skel.paths[i], "localRotation")] = q


ARROW = "wp2390"


def arrow_tip_attachment(skel, g, buf, root_path):
    """Local position (Unity space) of the arrow's tip relative to its root bone, for the "ArrowTip" marker."""
    pts = np.concatenate([pa.acc(g, buf, p["attributes"]["POSITION"]).astype(np.float64)
                          for m in g["meshes"] if m["name"].startswith("lod0.") for p in m["primitives"]])
    tip = pts[np.argmin(pts[:, 0])]  # the arrow lies along X with its point at -X
    node = [n for n in g["nodes"] if n.get("name") == "_000"][0]
    t = np.array(node.get("translation", [0, 0, 0]), float)
    q = np.array(node.get("rotation", [0, 0, 0, 1]), float)
    s = np.array(node.get("scale", [1, 1, 1]), float)
    x, y, z, w = q
    R = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    local = (R.T @ (tip - t)) / s  # glTF space, relative to the bone
    return {"name": "ArrowTip", "parentPath": root_path, "position": [float(v) for v in cv_pos(local)]}


def export_arrow(extract, out):
    """Tweyen's arrow (wp2390) as a static mesh for the projectile ghost: tip at the origin, pointing +Z.
    In the game file the arrow lies along X with the tip at -X; after the X mirror into Unity it points +X,
    so it is turned to +Z and shifted back until the tip sits on the origin."""
    g, buf = pa.load_gltf(f"{extract}/weapons/{ARROW}/{ARROW}_var00.gltf")
    turn = np.array([[0, 0, -1], [0, 1, 0], [1, 0, 0]], float)  # Unity +X -> +Z (proper rotation)
    parts = []
    for mesh in g["meshes"]:
        if not mesh["name"].startswith("lod0."):
            continue
        for prim in mesh["primitives"]:
            at = prim["attributes"]
            pos = cv_pos(pa.acc(g, buf, at["POSITION"])) @ turn.T
            nrm = cv_pos(pa.acc(g, buf, at["NORMAL"])) @ turn.T
            tan = cv_tan(pa.acc(g, buf, at["TANGENT"]))
            tan[:, :3] = tan[:, :3] @ turn.T
            uv = pa.acc(g, buf, at["TEXCOORD_0"]).astype(np.float64)
            uv[:, 1] = 1.0 - uv[:, 1]
            ind = pa.acc(g, buf, prim["indices"]).reshape(-1, 3)  # same winding rule as the character
            parts.append((pos, nrm, tan, uv, ind))
    tip = max(p[0][:, 2].max() for p in parts)
    props = []
    with open(os.path.join(out, "props.bin"), "wb") as fb:
        for i, (pos, nrm, tan, uv, ind) in enumerate(parts):
            pos = pos - np.array([0.0, 0.0, tip])
            off = fb.tell()
            for arr, dt in ((pos, "<f4"), (nrm, "<f4"), (tan, "<f4"), (uv, "<f4"), (ind, "<i4")):
                fb.write(np.ascontiguousarray(arr, dt).tobytes())
            props.append({"name": f"arrow_{i}", "group": "arrow", "offset": off, "vertexCount": len(pos), "indexCount": int(ind.size),
                          "albedo": f"{ARROW}_arrow_lod0_albd.png", "normal": f"{ARROW}_arrow_lod0_nrml.png"})
    for t in (f"{ARROW}_arrow_lod0_albd.png", f"{ARROW}_arrow_lod0_nrml.png"):
        shutil.copy2(f"{extract}/weapons/{ARROW}/{t}", os.path.join(out, "textures", t))
    length = max(-p[0][:, 2].min() + tip for p in parts)
    print(f"arrow: {len(parts)} parts, length {length:.2f} m (tip at origin, pointing +Z)")
    return props


def main():
    extract, out = sys.argv[1], sys.argv[2]
    os.makedirs(os.path.join(out, "clips"), exist_ok=True)
    os.makedirs(os.path.join(out, "textures"), exist_ok=True)

    body_g, body_buf = pa.load_gltf(f"{extract}/model/{CHAR}_var00.gltf")
    face_g, face_buf = pa.load_gltf(f"{extract}/face/{FACE}_var00.gltf")
    bow_g, bow_buf = pa.load_gltf(f"{extract}/weapons/{BOW}/{BOW}_var00.gltf")
    body_vars = [json.load(open(f"{extract}/model/{CHAR}_var{v:02d}.gltf")) for v in range(VARIANTS)]
    face_vars = [json.load(open(f"{extract}/face/{FACE}_var{v:02d}.gltf")) for v in range(VARIANTS)]

    skel = Skeleton()
    body_joints = skel.add_gltf(body_g, "body", "Armature")
    face_joints = skel.add_gltf(face_g, "face", "Armature", merge_into="body")
    bow_joints = skel.add_gltf(bow_g, "bow", "BowArmature")
    arrow_g, arrow_buf = pa.load_gltf(f"{extract}/weapons/{ARROW}/{ARROW}_var00.gltf")
    arrow_joints = skel.add_gltf(arrow_g, "arrow", "ArrowArmature")
    arrow_root_path = skel.paths[skel.index[("arrow", "_000")]]

    # the head bone = body bone carrying most face-mesh weight
    head_votes = {}
    for mesh in face_g["meshes"]:
        for prim in mesh["primitives"]:
            J = pa.acc(face_g, face_buf, prim["attributes"]["JOINTS_0"]).astype(int)
            W = pa.acc(face_g, face_buf, prim["attributes"]["WEIGHTS_0"])
            for j, w in zip(J.ravel(), W.ravel()):
                b = face_joints[j]
                if ("body", skel.bones[b]["name"]) in skel.index and b in body_joints:
                    head_votes[b] = head_votes.get(b, 0) + w
    head_bone = max(head_votes, key=head_votes.get)

    renderers, textures = [], set()
    with open(os.path.join(out, "meshes.bin"), "wb") as fb:
        crystal = lambda prim: prim.get("material") in BODY_CRYSTALS
        export_meshes(body_g, body_buf, body_joints, "body", body_vars, f"{extract}/model", fb, renderers, textures,
                      include=lambda prim: not crystal(prim))
        generated = os.path.join(out, "_generated")  # recoloured textures, remade every export
        shutil.rmtree(generated, ignore_errors=True)
        os.makedirs(generated, exist_ok=True)
        for wp in BOWS:
            export_meshes(body_g, body_buf, body_joints, "body", body_vars, f"{extract}/model", fb, renderers, textures,
                          name_prefix=f"body_{wp}", extra={"weapon": wp}, include=crystal,
                          texture_override=lambda prim, alb, nor, wp=wp:
                              body_crystal_textures(extract, generated, BODY_CRYSTALS[prim["material"]], wp))
        build_face_mask(face_g, face_buf)
        export_meshes(face_g, face_buf, face_joints, "face", face_vars, f"{extract}/face", fb, renderers, textures,
                      adjust=push_back_features)
        export_face_mask(face_g, face_buf, face_joints, face_vars, f"{extract}/face", fb, renderers, textures)
        # every bow on the shared bow skeleton; the mod shows the one picked in the loadout ("weapon")
        for wp in BOWS:
            g, b = (bow_g, bow_buf) if wp == BOW else pa.load_gltf(f"{extract}/weapons/{wp}/{wp}_var00.gltf")
            joints = bow_joints if wp == BOW else [skel.index[("bow", g["nodes"][j]["name"])] for j in g["skins"][0]["joints"]]
            var = [json.load(open(f"{extract}/weapons/{wp}/{wp}_var00.gltf"))] * VARIANTS  # bows have no colour variants
            export_meshes(g, b, joints, "bow", var, f"{extract}/weapons/{wp}", fb, renderers, textures,
                          name_prefix=f"bow_{wp}", extra={"weapon": wp},
                          texture_override=lambda prim, alb, nor, wp=wp: None if alb else bow_crystal_textures(extract, wp))

    for t in textures:
        shutil.copy2(t, os.path.join(out, "textures", os.path.basename(t)))
    # the eye-white textures get a painted iris and pupil (RoR2 has no equivalent of the game's eye shader)
    import paint_irises
    sys.argv = ["paint_irises.py", extract, os.path.join(out, "textures")]
    paint_irises.main()

    props = export_arrow(extract, out)

    clips = []
    for clip, (mid, loop) in CLIPS.items():
        channels, body_root, n = sample_clip(skel, "body", f"{extract}/animations/{CHAR}_{mid}.glb")
        weapons = [("bow", f"{extract}/weapon_animations/{BOW}/{BOW}_{mid}.glb")]
        if clip in ARROW_ANIMATED_CLIPS:
            weapons.append(("arrow", f"{extract}/weapon_animations/{ARROW}/{ARROW}_{mid}.glb"))
        for group, path in weapons:
            if not os.path.exists(path):
                continue
            wch, wroot, wn = sample_clip(skel, group, path)
            n = max(n, wn)
            offset_weapon_root(skel, group, wch, n, pad_frames(wroot, n) - pad_frames(body_root, n))
            channels.update(wch)
        edits = CLIP_EDITS.get(clip, {})
        for pin in edits.get("pin_weapons", []):
            pin_weapons_to_hand(skel, channels, n, *pin)
        if "segments" in edits:
            channels, n = apply_segments(skel, channels, n, edits["segments"], edits.get("blend", 0.2))
        if edits.get("uniform_spin"):
            channels, n = uniform_spin(skel, channels, n)
        events = spin_events(skel, channels, n, edits["spin_targets"]) if "spin_targets" in edits else []

        grid = np.arange(n) / FPS_OUT
        curves = []
        for (path, prop), v in channels.items():
            tol = {"localPosition": POS_TOL, "localRotation": ROT_TOL, "localScale": SCL_TOL}[prop]
            add_curves(curves, path, prop, grid, pad_frames(v, n), tol)
        length = (n - 1) / FPS_OUT
        json.dump({"name": clip, "motionId": mid, "length": length, "loop": loop, "curves": curves, "events": events},
                  open(os.path.join(out, "clips", f"{clip}.json"), "w"), separators=(",", ":"))
        clips.append({"name": clip, "motionId": mid, "loop": loop, "length": length, "events": events})
        print(f"clip {clip} ({mid}): {length:.2f}s, {len(curves)} curves, {sum(len(c['times']) for c in curves)} keys")

    # native travel speed of each locomotion clip (m/s), so the animator can match playback to movement speed
    speeds = {}
    for clip in ("RunForward", "Sprint", "WalkBackward", "WalkRight", "WalkLeft"):
        mid = CLIPS[clip][0]
        g, buf = pa.load_gltf(f"{extract}/animations/{CHAR}_{mid}.glb")
        anim = g["animations"][0]
        arm = [i for i, n in enumerate(g["nodes"]) if n.get("name") == "Armature"][0]
        for ch in anim["channels"]:
            if ch["target"]["node"] == arm and ch["target"]["path"] == "translation":
                smp = anim["samplers"][ch["sampler"]]
                tt = pa.acc(g, buf, smp["input"])[:, 0]
                vv = pa.acc(g, buf, smp["output"])
                speeds[clip] = float(np.hypot(*(vv[-1] - vv[0])[[0, 2]]) / tt[-1])

    manifest = {
        "bones": skel.bones,
        "bonePaths": [skel.paths[i] for i in range(len(skel.bones))],
        "headBone": head_bone,
        "renderers": renderers,
        "variantCount": VARIANTS,
        "clips": clips,
        "clipSpeeds": [{"clip": k, "speed": v} for k, v in speeds.items()],
        "props": props,
        "attachments": [arrow_tip_attachment(skel, arrow_g, arrow_buf, arrow_root_path)],
    }
    json.dump(manifest, open(os.path.join(out, "manifest.json"), "w"), indent=1)
    print(f"{len(skel.bones)} bones, {len(renderers)} renderers, {len(textures)} textures, head bone {skel.bones[head_bone]['name']}")
    print("clip speeds", speeds)


if __name__ == "__main__":
    main()
