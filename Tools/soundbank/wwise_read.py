"""Minimal Wwise .bnk reader: sections, HIRC objects, events -> actions -> targets -> wem ids (heuristic children lists)."""
import struct, sys

def fnv1(name):
    h = 2166136261
    for c in name.lower().encode():
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= c
    return h

def sections(data):
    out, i = {}, 0
    while i + 8 <= len(data):
        tag = data[i:i+4].decode('latin1'); size = struct.unpack_from('<I', data, i+4)[0]
        out[tag] = data[i+8:i+8+size]; i += 8 + size
    return out

def hirc(data):
    s = sections(data)
    out = {}
    if 'HIRC' not in s: return out, s
    h = s['HIRC']; n = struct.unpack_from('<I', h, 0)[0]; i = 4
    for _ in range(n):
        t = h[i]; size = struct.unpack_from('<I', h, i+1)[0]
        body = h[i+5:i+5+size]; oid = struct.unpack_from('<I', body, 0)[0]
        out[oid] = (t, body); i += 5 + size
    return out, s

def didx(s):
    d = {}
    if 'DIDX' in s:
        x = s['DIDX']
        for k in range(0, len(x), 12):
            wid, off, size = struct.unpack_from('<III', x, k); d[wid] = (off, size)
    return d

def children(body, ids):
    """Longest run 'count, id1..idN' of known object ids anywhere in the body (container child lists)."""
    best = []
    for k in range(4, len(body) - 8):
        n = struct.unpack_from('<I', body, k)[0]
        if 0 < n < 300 and k + 4 + 4*n <= len(body):
            lst = [struct.unpack_from('<I', body, k+4+4*j)[0] for j in range(n)]
            if all(x in ids for x in lst) and len(lst) > len(best): best = lst
    return best

def event_actions(body):
    n = body[4]  # u8 count in newer versions
    return [struct.unpack_from('<I', body, 5+4*j)[0] for j in range(n)]

def resolve(objs, oid, depth=0, seen=None):
    """wem source ids reachable from an object."""
    seen = seen or set()
    if oid in seen or oid not in objs or depth > 12: return []
    seen.add(oid); t, body = objs[oid]
    if t == 2:  # sound
        return [struct.unpack_from('<I', body, 9)[0]]
    if t == 3:  # action: type u16, target u32
        return resolve(objs, struct.unpack_from('<I', body, 6)[0], depth+1, seen)
    if t == 4:
        return [w for a in event_actions(body) for w in resolve(objs, a, depth+1, seen)]
    return [w for c in children(body, objs) for w in resolve(objs, c, depth+1, seen)]

def pck(data):
    """AKPK file package: {id: (offset, size)} for streamed sounds (32- and 64-bit id tables)."""
    assert data[:4] == b'AKPK'
    hsize, ver, lang, banks, sounds = struct.unpack_from('<5I', data, 4)
    ext = struct.unpack_from('<I', data, 24)[0] if ver >= 1 else 0
    pos = 28 + lang
    out = {}
    def table(pos, size, idsize):
        n = struct.unpack_from('<I', data, pos)[0]; p = pos + 4
        for _ in range(n):
            if idsize == 8:
                fid, block, fsize, off, lng = struct.unpack_from('<QIIII', data, p); p += 24
            else:
                fid, block, fsize, off, lng = struct.unpack_from('<IIIII', data, p); p += 20
            out[fid] = (off * block, fsize)
    table(pos, banks, 4); pos += banks
    table(pos, sounds, 4); pos += sounds
    if ext: table(pos, ext, 8)
    return out
