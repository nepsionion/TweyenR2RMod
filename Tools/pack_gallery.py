"""Packs the rendered animation strips into a few atlas images and writes the browser page data.

usage: pack_gallery.py <gallery dir> <site dir> <page template.html> [--per-atlas 40]
Produces <site>/atlas_NN.jpg and <site>/index.html (template with the JSON data inlined).
"""
import argparse
import json
import os

from PIL import Image


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("gallery")
    ap.add_argument("site")
    ap.add_argument("template")
    ap.add_argument("--per-atlas", type=int, default=40)
    a = ap.parse_args()
    meta = json.load(open(os.path.join(a.gallery, "anims.json")))
    fw, fh, nf = meta["frame_w"], meta["frame_h"], meta["frames_per_strip"]
    anims = meta["anims"]
    os.makedirs(a.site, exist_ok=True)
    for i in range(0, len(anims), a.per_atlas):
        chunk = anims[i:i + a.per_atlas]
        atlas = Image.new("RGB", (fw * nf, fh * len(chunk)))
        for row, an in enumerate(chunk):
            strip = Image.open(os.path.join(a.gallery, "strips", f"{an['id']}.jpg"))
            atlas.paste(strip, (0, row * fh))
            an["atlas"] = i // a.per_atlas
            an["row"] = row
        atlas.save(os.path.join(a.site, f"atlas_{i // a.per_atlas:02d}.jpg"), quality=70, optimize=True, progressive=True)
    data = {"fw": fw, "fh": fh, "nf": nf, "perAtlas": a.per_atlas, "anims": anims}
    page = open(a.template, encoding="utf-8").read().replace("/*__DATA__*/null", json.dumps(data, separators=(",", ":")))
    open(os.path.join(a.site, "index.html"), "w", encoding="utf-8").write(page)
    total = sum(os.path.getsize(os.path.join(a.site, f)) for f in os.listdir(a.site))
    print(f"{len(anims)} animations, {len(os.listdir(a.site))} files, {total / 1e6:.1f} MB -> {a.site}")


if __name__ == "__main__":
    main()
