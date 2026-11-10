"""Minimal binary FBX reader: lists Model / Geometry nodes with vertex & polygon counts."""
import struct, sys, zlib

def read_props(f, n):
    props = []
    for _ in range(n):
        t = f.read(1)
        if t == b'Y': props.append(struct.unpack('<h', f.read(2))[0])
        elif t == b'C': props.append(f.read(1)[0])
        elif t == b'I': props.append(struct.unpack('<i', f.read(4))[0])
        elif t == b'F': props.append(struct.unpack('<f', f.read(4))[0])
        elif t == b'D': props.append(struct.unpack('<d', f.read(8))[0])
        elif t == b'L': props.append(struct.unpack('<q', f.read(8))[0])
        elif t in b'fdlib':
            length, enc, clen = struct.unpack('<III', f.read(12))
            data = f.read(clen)
            if enc == 1: data = zlib.decompress(data)
            fmt = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': 'b'}[t.decode()]
            props.append(('array', fmt, length, data))
        elif t in b'SR':
            ln = struct.unpack('<I', f.read(4))[0]
            props.append(f.read(ln))
        else:
            raise ValueError(f'bad prop type {t}')
    return props

def read_node(f, v):
    if v >= 7500:
        end, nprops, plen = struct.unpack('<QQQ', f.read(24))
    else:
        end, nprops, plen = struct.unpack('<III', f.read(12))
    nlen = f.read(1)[0]
    name = f.read(nlen).decode(errors='replace')
    if end == 0:
        return None
    props = read_props(f, nprops)
    children = []
    sentinel = 25 if v >= 7500 else 13
    while f.tell() < end - sentinel or (f.tell() < end and f.tell() + sentinel != end):
        c = read_node(f, v)
        if c is None: break
        children.append(c)
    f.seek(end)
    return (name, props, children)

def main(path):
    with open(path, 'rb') as f:
        f.read(23)
        v = struct.unpack('<I', f.read(4))[0]
        top = []
        while True:
            n = read_node(f, v)
            if n is None: break
            top.append(n)
            if n[0] == 'Objects': break
    print('FBX version', v)
    objs = next(n for n in top if n[0] == 'Objects')
    total_tris = 0
    for name, props, ch in objs[2]:
        if name == 'Model':
            print('Model   ', props[1].split(b'\x00')[0].decode(errors='replace'), props[2].decode(errors='replace'))
        elif name == 'Geometry':
            label = props[1].split(b'\x00')[0].decode(errors='replace')
            verts = polys = 0; idx_count = 0
            for cn, cp, _ in ch:
                if cn == 'Vertices': verts = cp[0][2] // 3
                if cn == 'PolygonVertexIndex':
                    arr = struct.unpack('<%di' % cp[0][2], cp[0][3])
                    idx_count = len(arr)
                    polys = sum(1 for x in arr if x < 0)
                    # triangles if fan-triangulated
                    tris = 0; k = 0
                    for x in arr:
                        k += 1
                        if x < 0: tris += k - 2; k = 0
            total_tris += tris
            print(f'Geometry {label!r}: verts={verts} polys={polys} tris={tris}')
        elif name == 'Material':
            print('Material', props[1].split(b'\x00')[0].decode(errors='replace'))
    print('TOTAL tris', total_tris)

if __name__ == '__main__':
    main(sys.argv[1])
