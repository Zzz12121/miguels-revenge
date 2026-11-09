"""Contact sheet of the decimated OBJ props, shaded and coloured from the basecolor texture."""
import sys, os, glob, numpy as np
import matplotlib; matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import PolyCollection
from matplotlib.image import imread
d, tex_path, out = sys.argv[1], sys.argv[2], sys.argv[3]
tex = imread(tex_path).astype(float); tex = tex / 255.0 if tex.max() > 1 else tex
h, w = tex.shape[:2]
files = sorted(glob.glob(os.path.join(d, 'SM_Shura_*.obj')))
fig, axes = plt.subplots(2, 5, figsize=(25, 11), facecolor='#1e2026')
light = np.array([0.4, 0.7, 0.6]); light /= np.linalg.norm(light)
yaw = np.radians(-30); pitch = np.radians(15)
Ry = np.array([[np.cos(yaw), 0, np.sin(yaw)], [0, 1, 0], [-np.sin(yaw), 0, np.cos(yaw)]])
Rx = np.array([[1, 0, 0], [0, np.cos(pitch), -np.sin(pitch)], [0, np.sin(pitch), np.cos(pitch)]])
for ax, f in zip(axes.flat, files):
    Vs, VT, F, FT = [], [], [], []
    for line in open(f):
        if line.startswith('v '): Vs.append(list(map(float, line.split()[1:])))
        elif line.startswith('vt '): VT.append(list(map(float, line.split()[1:])))
        elif line.startswith('f '):
            parts = [p.split('/') for p in line.split()[1:]]
            F.append([int(p[0]) - 1 for p in parts]); FT.append([int(p[1]) - 1 for p in parts])
    V, VT, F, FT = np.array(Vs), np.array(VT), np.array(F), np.array(FT)
    V[:, 0] *= -1  # what Unity shows (OBJ importer flips X)
    P = V @ Ry.T @ Rx.T
    tri = P[F]
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    shade = 0.35 + 0.65 * np.abs(n @ light)
    uv = VT[FT].mean(1)
    col = tex[np.clip(((1 - uv[:, 1] % 1) * (h - 1)).astype(int), 0, h - 1), np.clip(((uv[:, 0] % 1) * (w - 1)).astype(int), 0, w - 1), :3]
    col = np.clip(col * shade[:, None], 0, 1)
    order = np.argsort(tri[:, :, 2].mean(1))  # far (−z) first; camera looks toward -z from +z
    pc = PolyCollection(tri[order][:, :, :2], facecolors=col[order], edgecolors='none')
    ax.add_collection(pc); ax.autoscale(); ax.set_aspect('equal'); ax.axis('off')
    ax.set_title(f'{os.path.basename(f)[9:-4]}  {len(F)} tris  h={np.ptp(V[:,1]):.2f}m', color='w', fontsize=13)
plt.tight_layout(); plt.savefig(out, dpi=70)
