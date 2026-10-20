"""
Zips the mod (Thunderstore/: manifest.json, icon.png, README.md, CHANGELOG.md, plugins/) into
Build/<author>-<name>-<version>.zip, the format the Thunderstore Mod Manager imports ("Import local mod") and Thunderstore
accepts. Run after `dotnet build -c Release`, which fills Thunderstore/plugins.
"""
import json, os, sys, zipfile

AUTHOR = "nepsionion"
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
SRC = os.path.join(ROOT, "Thunderstore")
OUT_DIR = os.path.join(ROOT, "Build")
SKIP = (".pdb",)  # debug symbols aren't needed to play

manifest = json.load(open(os.path.join(SRC, "manifest.json"), encoding="utf-8-sig"))
for f in ("manifest.json", "icon.png", "README.md"):
    if not os.path.exists(os.path.join(SRC, f)):
        sys.exit(f"missing Thunderstore/{f}")
os.makedirs(OUT_DIR, exist_ok=True)
out = os.path.join(OUT_DIR, f"{AUTHOR}-{manifest['name']}-{manifest['version_number']}.zip")

with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for dirpath, _, files in os.walk(SRC):
        for f in files:
            if f.endswith(SKIP):
                continue
            path = os.path.join(dirpath, f)
            z.write(path, os.path.relpath(path, SRC).replace(os.sep, "/"))
    names = z.namelist()

print(out)
for n in sorted(names):
    print("  " + n)
