"""
Builds Tweyen's Wwise sound bank for RoR2 (TweyenR2RMod/Soundbanks/TweyenSounds.sound) without the Wwise authoring
tool: the objects are copied byte for byte from RoR2's own v150 banks (char_Huntress) with their ids, children and media
replaced.

- Relink sounds (Wwise Vorbis .wem) are embedded as they are; the user's mp3s are decoded with vgmstream and embedded
  as PCM.
- Every event plays one random container per layer (Relink's layered events keep their layers); loops are continuous
  containers that loop forever and have a Stop event.
- Everything hangs under Tweyen's own copy of RoR2's "SFX" > "SFX\\Characters" actor-mixers (new ids, same settings:
  Character bus, so the Sound Effects volume applies, 3D positioning and attenuation like the vanilla survivors). Not
  under the vanilla mixers themselves: a Wwise parent links its children from its own child list when it loads, and
  R2API loads this bank before RoR2's banks, so sounds hung on the vanilla mixer stay orphaned (straight to the master
  bus, 2D, ignoring the volume settings). The vanilla attenuation the mixer uses is included as is (same id: banks share
  objects that way).

Usage: python build_soundbank.py   (paths below; the extracted Relink files come from GBFRExtract/sound)
"""
import os, struct, subprocess, sys
sys.path.insert(0, os.path.dirname(__file__))
from wwise_read import fnv1, hirc, sections, didx, pck, resolve, children

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RELINK = os.path.join(ROOT, "GBFRExtract", "sound")
ART = os.path.join(ROOT, "Art", "Sounds")
OUT = os.path.join(ROOT, "TweyenR2RMod", "Soundbanks", "TweyenSounds.sound")
VGMSTREAM = os.path.join(ROOT, "Tools", "vgmstream", "vgmstream-cli.exe")
ROR2_BANKS = r"D:/steam/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/StreamingAssets/Audio/GeneratedSoundBanks/Windows"

BANK_NAME = "TweyenSounds"
VANILLA_SFX_MIXER = 901275870         # \Actor-Mixer Hierarchy\Default Work Unit\SFX
VANILLA_CHARACTERS_MIXER = 159402189  # ...\SFX\Characters (Character bus, 3D positioning)
VANILLA_ATTENUATION = 619618061       # the attenuation SFX\Characters uses

# ---------------------------------------------------------------- sources

se_bank = hirc(open(os.path.join(RELINK, "se", "pl2300_01_02_00.bnk"), "rb").read())[0]
town_bank = hirc(open(os.path.join(RELINK, "se", "pl2300_town_01_02_00.bnk"), "rb").read())[0]

media = {}
def add_media(path):
    d = open(path, "rb").read()
    if path.endswith(".pck"):
        for wid, (off, size) in pck(d).items(): media.setdefault(wid, d[off:off + size])
        return
    s = sections(d)
    if "DATA" in s:
        base = d.index(b"DATA") + 8
        for wid, (off, size) in didx(s).items(): media.setdefault(wid, d[base + off:base + off + size])
for f in ("pl2300_01_02_00_m.bnk", "pl2300_town_01_02_00.bnk", "pl_common_m.bnk"):
    add_media(os.path.join(RELINK, "se", f))
for f in os.listdir(os.path.join(RELINK, "japanese")):
    if f.endswith(".pck") or f.endswith("_m.bnk"): add_media(os.path.join(RELINK, "japanese", f))
voice_bank = hirc(open(os.path.join(RELINK, "japanese", "vo_pl2300_01_02_00.bnk"), "rb").read())[0]

def wems(objs, event_or_node):
    return [("vorbis", media[w]) for w in dict.fromkeys(resolve(objs, event_or_node))]

def layers(objs, event):
    """A Relink event's layers: one list of sounds per Play action."""
    t, b = objs[event]
    acts = [struct.unpack_from("<I", b, 5 + 4 * j)[0] for j in range(b[4])]
    return [wems(objs, a) for a in acts]

def switch_default(objs, switch):
    """The node a Relink switch container plays for its default switch (e.g. the default ground)."""
    t, b = objs[switch]
    kids = children(b, objs)
    k = b.find(struct.pack("<I", len(kids)) + struct.pack("<I", kids[0]))
    default = struct.unpack_from("<I", b, k - 5)[0]
    p = k + 4 + 4 * len(kids); n = struct.unpack_from("<I", b, p)[0]; p += 4
    for _ in range(n):
        sw, c = struct.unpack_from("<II", b, p); p += 8
        if sw == default: return struct.unpack_from("<I", b, p)[0]
        p += 4 * c
    raise ValueError("default switch not mapped")

def pcm_wem(wav_path):
    """A Wwise PCM .wem (16-bit) from a .wav."""
    import wave
    with wave.open(wav_path) as w:
        ch, sr, frames = w.getnchannels(), w.getframerate(), w.readframes(w.getnframes())
        assert w.getsampwidth() == 2
    layout = {1: 0x4, 2: 0x3}[ch]
    fmt = struct.pack("<HHIIHHHHI", 0xFFFE, ch, sr, sr * ch * 2, ch * 2, 16, 6, 0, ch | (1 << 8) | (layout << 12))
    body = b"WAVE" + b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", len(frames)) + frames
    if len(body) % 2: body += b"\0"
    return b"RIFF" + struct.pack("<I", len(body)) + body

def user_sounds(folder):
    out = []
    tmp = os.path.join(RELINK, "user_wav")
    os.makedirs(tmp, exist_ok=True)
    for f in sorted(os.listdir(os.path.join(ART, folder))):
        if not f.lower().endswith((".mp3", ".wav", ".ogg")): continue
        wav = os.path.join(tmp, os.path.splitext(f)[0] + ".wav")
        subprocess.run([VGMSTREAM, "-o", wav, os.path.join(ART, folder, f)], check=True, capture_output=True)
        out.append(("pcm", pcm_wem(wav)))
    if not out: raise FileNotFoundError(f"no sounds in Art/Sounds/{folder}")
    return out

def event_id(e):
    return e

# ---------------------------------------------------------------- what goes in the bank
# name -> (layers [list of sounds per layer], loop)

clincher_draw = 0x6CA15ADD; clincher_shot = 0xAA7E6096   # pl2300_ab_03_charge / _shoot (switch: both, random)
QUIET = -16.0   # dB: the arrow shot (Relink's sounds are mastered much louder than RoR2's)
LEVEL = -8.0    # dB: every other sound (8 dB above the shot: the user found them too low after the routing fix)
# (an event's volume can also be a list: one per layer)
EVENTS = {
    "Tweyen_Shot":         ([wems(se_bank, 0x39AB920B) + wems(se_bank, 0x39AB9208)], False, QUIET),  # normal attack (036, 037)
    "Tweyen_Mark":         ([wems(se_bank, 0x56FFDB41)], False, LEVEL - 8.0),        # Multilock Hail lock (041)
    "Tweyen_ClincherDraw": ([wems(se_bank, clincher_draw)], False, LEVEL),
    "Tweyen_ClincherShot": ([wems(se_bank, clincher_shot)], False, LEVEL),
    "Tweyen_JaegerCharge": ([wems(se_bank, 0x4ED341A4)], False, LEVEL),        # 075
    # exactly the two clips the user picked (079's event has a second, water-like variant), together, looping
    "Tweyen_MoveLoop":     ([wems(se_bank, 0x3A864F92)[:1], wems(se_bank, 0xC224519D)[:1]], True, [LEVEL - 2.0, LEVEL + 2.0]),  # 079, 084
    "Tweyen_Jump":         (layers(se_bank, fnv1("pl2300_mov_jump")), False, LEVEL),
    "Tweyen_Land":         ([wems(se_bank, switch_default(se_bank, 114359221))], False, LEVEL),  # pl2300_foot_land, default ground
    "Tweyen_HurtS":        ([wems(voice_bank, fnv1("PL2300_vo_DMG_default_s"))], False, LEVEL),
    "Tweyen_HurtM":        ([wems(voice_bank, fnv1("PL2300_vo_DMG_default_m"))], False, LEVEL),
    "Tweyen_HurtL":        ([wems(voice_bank, fnv1("PL2300_vo_DMG_default_l"))], False, LEVEL),
    "Tweyen_Death":        ([wems(voice_bank, fnv1("PL2300_vo_DMG_down"))], False, LEVEL),
    "Tweyen_Spawn":        ([user_sounds("Spawn")], False, LEVEL),
    "Tweyen_Select":       ([user_sounds("CharacterSelect")], False, LEVEL),
    "Tweyen_TwoCrown":     ([user_sounds("TwoCrown")], False, LEVEL),
}

# ---------------------------------------------------------------- templates (RoR2 char_Huntress, bank v150)

huntress = open(os.path.join(ROR2_BANKS, "char_Huntress.bnk"), "rb").read()
h_objs, h_sections = hirc(huntress)
T_BKHD = bytearray(h_sections["BKHD"])
T_CONTAINER = h_objs[464221024][1]    # huntress_R_aim_loop: continuous random container, loops forever
T_SOUND = h_objs[174193848][1]        # one of its sounds
T_PLAY = h_objs[291587155][1]         # Play action
T_STOP = h_objs[754038499][1]         # Stop action
T_SFX_MIXER = h_objs[VANILLA_SFX_MIXER][1]
T_CHARACTERS_MIXER = h_objs[VANILLA_CHARACTERS_MIXER][1]
assert struct.unpack_from("<I", T_CHARACTERS_MIXER, 12)[0] == VANILLA_SFX_MIXER, "mixer template layout changed"

BANK_ID = fnv1(BANK_NAME)
assert struct.unpack_from("<I", T_SOUND, 26)[0] == 464221024, "sound template layout changed"
assert struct.unpack_from("<I", T_CONTAINER, 12)[0] == 0x2E016F4E, "container template layout changed"

def oid(*parts):
    return fnv1("tweyen_obj_" + "_".join(str(p) for p in parts))

def sound(sid, parent, codec, wem_id, size):
    b = bytearray(T_SOUND)
    struct.pack_into("<I", b, 0, sid)
    struct.pack_into("<I", b, 4, 0x00010001 if codec == "pcm" else 0x00040001)
    b[8] = 0  # in the bank
    struct.pack_into("<II", b, 9, wem_id, size)
    struct.pack_into("<I", b, 26, parent)
    return bytes(b)

def container(cid, kids, loop, volume):
    b = bytearray(T_CONTAINER)
    struct.pack_into("<I", b, 0, cid)
    struct.pack_into("<I", b, 12, CHARACTERS_MIXER)  # Tweyen's copy
    old = children(bytes(b), {c: 1 for c in children(T_CONTAINER, h_objs)})
    k = b.find(struct.pack("<I", len(old)) + struct.pack("<I", old[0]))
    head = b[:k]
    assert struct.unpack_from("<f", head, k - 18)[0] == 1000.0 and head[k - 1] == 0x1A, "container template layout changed"
    struct.pack_into("<H", head, k - 24, 0 if loop else 1)        # loop count (0 = forever)
    head[k - 1] = 0x1A if loop else 0x12                          # continuous (loop) / step (one random pick)
    # properties (after the priority byte at 16): the template's make-up gain (5) of -3 dB becomes neutral, and a
    # volume (0) is added
    assert head[17] == 1 and head[18] == 5, "container template layout changed"
    head = head[:17] + bytes([2, 0, 5]) + struct.pack("<ff", volume, 0.0) + head[23:]
    tail = struct.pack("<I", len(kids)) + b"".join(struct.pack("<I", c) for c in kids)
    tail += struct.pack("<H", len(kids)) + b"".join(struct.pack("<Ii", c, 50000) for c in kids)
    return bytes(head) + tail

CHARACTERS_MIXER = None  # set below: Tweyen's copy of SFX\Characters
SFX_MIXER = None

def mixer(template, mid, parent, kids):
    """A copy of a vanilla actor-mixer with a new id, parent and child list (its last field)."""
    b = bytearray(template)
    # the child list ends the object: count, then that many ids of objects in the template's bank
    k = next((len(b) - 4 - 4 * n for n in range(1, 64)
              if struct.unpack_from("<I", b, len(b) - 4 - 4 * n)[0] == n
              and all(struct.unpack_from("<I", b, len(b) - 4 * j)[0] in h_objs for j in range(1, n + 1))), None)
    assert k, "mixer template layout changed"
    struct.pack_into("<I", b, 0, mid)
    struct.pack_into("<I", b, 12, parent)
    return bytes(b[:k]) + struct.pack("<I", len(kids)) + b"".join(struct.pack("<I", c) for c in kids)

def action(template, aid, target):
    b = bytearray(template)
    struct.pack_into("<I", b, 0, aid)
    struct.pack_into("<I", b, 6, target)
    if template is T_PLAY: struct.pack_into("<I", b, 14, BANK_ID)
    return bytes(b)

def event(eid, acts):
    return struct.pack("<IB", eid, len(acts)) + b"".join(struct.pack("<I", a) for a in acts)

# ---------------------------------------------------------------- build

CHARACTERS_MIXER = oid("mixer", "SFX", "Characters")
SFX_MIXER = oid("mixer", "SFX")
objects = []      # (type, bytes), written in order: attenuation, sounds, containers, mixers, actions, events
medias = {}       # wem id -> bytes
for name, (lays, loop, volume) in EVENTS.items():
    play, stop = [], []
    for li, lay in enumerate(lays):
        cid = oid(name, "layer", li)
        kids = []
        for si, (codec, data) in enumerate(lay):
            wem_id = oid(name, li, si, "media")
            medias[wem_id] = data
            sid = oid(name, li, si)
            objects.append((2, sound(sid, cid, codec, wem_id, len(data))))
            kids.append(sid)
        objects.append((5, container(cid, kids, loop, volume[li] if isinstance(volume, list) else volume)))
        aid = oid(name, "play", li); objects.append((3, action(T_PLAY, aid, cid))); play.append(aid)
        sid_ = oid(name, "stop", li); objects.append((3, action(T_STOP, sid_, cid))); stop.append(sid_)
    objects.append((4, event(fnv1("Play_" + name), play)))
    objects.append((4, event(fnv1("Stop_" + name), stop)))

containers = [struct.unpack_from("<I", b, 0)[0] for t, b in objects if t == 5]
objects.append((7, mixer(T_CHARACTERS_MIXER, CHARACTERS_MIXER, SFX_MIXER, containers)))
objects.append((7, mixer(T_SFX_MIXER, SFX_MIXER, 0, [CHARACTERS_MIXER])))
objects.append((14, h_objs[VANILLA_ATTENUATION][1]))
# children before their parents (a parent links them from its child list when it loads), like the game's banks
order = {14: 0, 2: 1, 5: 2, 7: 3, 3: 4, 4: 5}
objects.sort(key=lambda o: order[o[0]])

didx_b, data_b = bytearray(), bytearray()
for wid, data in medias.items():
    while len(data_b) % 16: data_b += b"\0"
    didx_b += struct.pack("<III", wid, len(data_b), len(data))
    data_b += data
hirc_b = struct.pack("<I", len(objects)) + b"".join(struct.pack("<BI", t, len(b)) + b for t, b in objects)

bkhd = bytearray(T_BKHD); struct.pack_into("<I", bkhd, 4, BANK_ID)
def section(tag, body): return tag + struct.pack("<I", len(body)) + bytes(body)
bank = section(b"BKHD", bkhd) + section(b"DIDX", didx_b) + section(b"DATA", data_b) + section(b"HIRC", hirc_b)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
open(OUT, "wb").write(bank)
print(f"{OUT}: {len(bank) / 1e6:.2f} MB, {len(medias)} sounds, {len(EVENTS)} events")
for name, (lays, loop, volume) in EVENTS.items():
    print(f"  Play_{name:22s} layers {[len(l) for l in lays]} {volume} dB{'  (loop)' if loop else ''}")
