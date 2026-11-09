"""
Split the Tripo props sheet into separate props, decimate each (vertex clustering),
rescale to real-world size with pivot at bottom centre, and write one OBJ per prop.

FBX here is Z-up right-handed. OBJ is written Y-up right-handed (x, z, -y); Unity's OBJ importer
flips X for its left-handed space, so the prop's front (FBX -Y) ends up facing Unity +Z.
"""
import struct, sys, os, json
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fbxinfo  # noqa: E402

fbx_path, out_dir, target_tris = sys.argv[1], sys.argv[2], int(sys.argv[3])
os.makedirs(out_dir, exist_ok=True)

# name, approx centre in FBX (x, z) of the piece, real height in metres
PROPS = [
    ('SakeBarrel',  (-0.41, 0.50), 1.0),
    ('Crate',       (-0.23, 0.48), 0.9),
    ('RiceBale',    ( 0.06, 0.48), 0.7),
    ('ClayPot',     ( 0.37, 0.50), 0.8),
    ('Tate',        (-0.41, 0.30), 1.7),
    ('Taketaba',    (-0.13, 0.29), 0.85),
    ('StoneLantern',( 0.14, 0.30), 2.0),
    ('KatanaRack',  ( 0.39, 0.26), 0.75),
    ('SpearRack',   (-0.37, 0.10), 2.5),
    ('BambooFence', (-0.10, 0.10), 1.5),
]

with open(fbx_path, 'rb') as f:
    f.read(23)
    v = struct.unpack('<I', f.read(4))[0]
    while True:
        n = fbxinfo.read_node(f, v)
        if n is None or n[0] == 'Objects': break
geo = next(c for c in n[2] if c[0] == 'Geometry')
arr = lambda node: np.frombuffer(node[1][0][3], dtype={'d': '<f8', 'i': '<i4', 'f': '<f4'}[node[1][0][1]])
child = {c[0]: c for c in geo[2]}
V = arr(child['Vertices']).reshape(-1, 3)
PVI = arr(child['PolygonVertexIndex']).copy()
uvc = {c[0]: c for c in child['LayerElementUV'][2]}
UV = arr(uvc['UV']).reshape(-1, 2)
UVI = arr(uvc['UVIndex'])
idx = PVI.copy(); idx[idx < 0] = ~idx[idx < 0]
T = idx.reshape(-1, 3)              # vertex index per corner
TUV = UVI.reshape(-1, 3)            # uv index per corner

# connected components (union-find)
e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]]])
L = np.arange(len(V))
while True:
    la, lb = L[e[:, 0]], L[e[:, 1]]
    d = la != lb
    if not d.any(): break
    np.minimum.at(L, np.maximum(la[d], lb[d]), np.minimum(la[d], lb[d]))
    while True:
        L2 = L[L]
        if np.array_equal(L2, L): break
        L = L2
_, comp = np.unique(L, return_inverse=True)
tri_comp = comp[T[:, 0]]

# assign every component to the nearest prop centre (in FBX x/z)
ncomp = comp.max() + 1
centres = np.array([p[1] for p in PROPS])
comp_prop = np.zeros(ncomp, dtype=int)
for c in range(ncomp):
    vs = V[comp == c]
    ctr = (vs.min(0) + vs.max(0)) / 2
    comp_prop[c] = np.argmin(((centres - ctr[[0, 2]]) ** 2).sum(1))
tri_prop = comp_prop[tri_comp]

def cluster(tris, uvtris, cells):
    """Vertex clustering decimation. Returns (positions, faces, face_uv_indices)."""
    used = np.unique(tris)
    P = V[used]
    lo, hi = P.min(0), P.max(0)
    size = (hi - lo).max() / cells
    key = np.floor((P - lo) / size).astype(np.int64)
    k = key[:, 0] * 1_000_000 + key[:, 1] * 1000 + key[:, 2]
    uniq, cell_of = np.unique(k, return_inverse=True)
    remap = np.full(len(V), -1); remap[used] = cell_of
    newpos = np.zeros((len(uniq), 3)); np.add.at(newpos, cell_of, P)
    newpos /= np.bincount(cell_of)[:, None]
    F = remap[tris]
    ok = (F[:, 0] != F[:, 1]) & (F[:, 1] != F[:, 2]) & (F[:, 2] != F[:, 0])
    F, FU = F[ok], uvtris[ok]
    # drop duplicate faces (same vertex set)
    s = np.sort(F, axis=1)
    _, first = np.unique(s[:, 0] * 10**12 + s[:, 1] * 10**6 + s[:, 2], return_index=True)
    return newpos, F[first], FU[first]

report = {}
for pi, (name, _, height) in enumerate(PROPS):
    sel = tri_prop == pi
    tris, uvtris = T[sel], TUV[sel]
    # find the cell count that lands closest to the triangle budget
    best = None
    for cells in (160, 128, 112, 96, 84, 72, 64, 56, 48, 40):
        P, F, FU = cluster(tris, uvtris, cells)
        best = (cells, P, F, FU)
        if len(F) <= target_tris: break
    cells, P, F, FU = best
    # FBX z-up -> y-up
    Q = np.stack([P[:, 0], P[:, 2], -P[:, 1]], axis=1)
    # 原始排版裡道具是斜放的：找出佔地面積最小的轉角，把底面對齊 X/Z 軸
    xz = Q[:, [0, 2]] - Q[:, [0, 2]].mean(0)
    best_a, best_area = 0.0, 1e9
    for a in np.radians(np.arange(0, 90, 1.0)):
        c, s_ = np.cos(a), np.sin(a)
        r = xz @ np.array([[c, -s_], [s_, c]])
        area = np.ptp(r[:, 0]) * np.ptp(r[:, 1])
        if area < best_area: best_a, best_area = a, area
    c, s_ = np.cos(best_a), np.sin(best_a)
    r = xz @ np.array([[c, -s_], [s_, c]])
    # 長邊放 X（左右），讓牆邊擺放時面向場內
    if np.ptp(r[:, 1]) > np.ptp(r[:, 0]): r = r[:, ::-1] * np.array([1, -1])
    Q = np.stack([r[:, 0], Q[:, 1], r[:, 1]], axis=1)
    # real size, pivot at bottom centre
    lo, hi = Q.min(0), Q.max(0)
    scale = height / (hi[1] - lo[1])
    Q = (Q - np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])) * scale
    # smooth normals
    fn = np.cross(Q[F[:, 1]] - Q[F[:, 0]], Q[F[:, 2]] - Q[F[:, 0]])
    N = np.zeros_like(Q)
    for j in range(3): np.add.at(N, F[:, j], fn)
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    used_uv, uv_local = np.unique(FU, return_inverse=True)
    uv_local = uv_local.reshape(FU.shape)
    path = os.path.join(out_dir, f'SM_Shura_{name}.obj')
    with open(path, 'w') as o:
        o.write(f'# {name} — split & decimated from Tripo props sheet\nmtllib SM_ShuraProps.mtl\no {name}\n')
        o.write(''.join(f'v {a:.5f} {b:.5f} {c:.5f}\n' for a, b, c in Q))
        o.write(''.join(f'vt {a:.6f} {b:.6f}\n' for a, b in UV[used_uv]))
        o.write(''.join(f'vn {a:.4f} {b:.4f} {c:.4f}\n' for a, b, c in N))
        o.write('usemtl ShuraProps\n')
        for (a, b, c), (ta, tb, tc) in zip(F + 1, uv_local + 1):
            o.write(f'f {a}/{ta}/{a} {b}/{tb}/{b} {c}/{tc}/{c}\n')
    size = hi - lo
    report[name] = dict(source_tris=int(sel.sum()), tris=int(len(F)), cells=cells,
                        size_m=[round(float(x * scale), 2) for x in size])
    print(f'{name:13s} {sel.sum():7d} -> {len(F):6d} tris (cells {cells}), size m {np.round(size * scale, 2)}')

with open(os.path.join(out_dir, 'SM_ShuraProps.mtl'), 'w') as o:
    o.write('newmtl ShuraProps\nKd 1 1 1\nmap_Kd T_ShuraProps_BaseColor.jpg\n')
json.dump(report, open(os.path.join(out_dir, 'report.json'), 'w'), indent=1, ensure_ascii=False)
