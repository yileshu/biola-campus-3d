"""
Rough software preview of the generated Roblox parts + terrain (painter's algorithm, Lambert
shading). This is NOT what Roblox renders (no Future lighting, materials or shadows) — it is only
a sanity check of geometry, placement and colours without Roblox Studio.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw

TERRAIN_COLORS = {
    "G": (92, 140, 58), "L": (70, 105, 48), "A": (62, 63, 66), "P": (168, 166, 160), "B": (176, 118, 92),
    "C": (170, 168, 160), "S": (214, 196, 150), "R": (130, 108, 82), "W": (60, 120, 160),
}
SUN = np.array([-0.45, 0.75, 0.48])
SUN = SUN / np.linalg.norm(SUN)


def _unit(a):
    n = np.linalg.norm(a)
    return a / n if n > 1e-12 else a


def part_tris(geo):
    cls, shape, pos, size, right, up, color = geo
    back = np.cross(right, up)
    hx, hy, hz = size / 2

    def P(x, y, z):
        return pos + right * x + up * y + back * z

    tris = []
    if cls == "WedgePart":
        a0, a1 = P(-hx, -hy, -hz), P(hx, -hy, -hz)
        b0, b1 = P(-hx, -hy, hz), P(hx, -hy, hz)
        t0, t1 = P(-hx, hy, hz), P(hx, hy, hz)
        tris += [(a0, b0, t0), (a1, t1, b1), (a0, a1, t1), (a0, t1, t0), (b0, b1, t1), (b0, t1, t0), (a0, b0, b1), (a0, b1, a1)]
    elif shape == "Cylinder":
        n = 10
        r = min(hy, hz)
        ring = [(math.cos(2 * math.pi * k / n) * r, math.sin(2 * math.pi * k / n) * r) for k in range(n)]
        for k in range(n):
            (y0, z0), (y1, z1) = ring[k], ring[(k + 1) % n]
            p0, p1, q0, q1 = P(-hx, y0, z0), P(-hx, y1, z1), P(hx, y0, z0), P(hx, y1, z1)
            tris += [(p0, p1, q1), (p0, q1, q0), (P(hx, 0, 0), q0, q1)]
    elif shape == "Ball":
        r = hx
        lat, lon = 5, 8
        for i in range(lat):
            t0, t1 = math.pi * i / lat - math.pi / 2, math.pi * (i + 1) / lat - math.pi / 2
            for j in range(lon):
                p0, p1 = 2 * math.pi * j / lon, 2 * math.pi * (j + 1) / lon
                def S(t, p):
                    return pos + np.array([math.cos(t) * math.cos(p), math.sin(t), math.cos(t) * math.sin(p)]) * r
                tris += [(S(t0, p0), S(t1, p0), S(t1, p1)), (S(t0, p0), S(t1, p1), S(t0, p1))]
    else:
        c = [P(x, y, z) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        for f in faces:
            tris += [(c[f[0]], c[f[1]], c[f[2]]), (c[f[0]], c[f[2]], c[f[3]])]
    return tris, color


def render(parts, terrain, grid, meta, eye, target, path, fov=70, w=1280, h=720, terrain_radius=420, terrain_step=1):
    eye, target = np.array(eye, float), np.array(target, float)
    fwd = _unit(target - eye)
    rgt = _unit(np.cross(fwd, np.array([0, 1, 0])))
    upv = np.cross(rgt, fwd)
    f = (h / 2) / math.tan(math.radians(fov) / 2)

    polys = []

    def add(tri, col, shade=True):
        pts = np.array(tri)
        rel = pts - eye
        z = rel @ fwd
        if (z < 0.3).any():
            return
        x = rel @ rgt; y = rel @ upv
        sx = w / 2 + f * x / z; sy = h / 2 - f * y / z
        if (sx < -200).all() or (sx > w + 200).all() or (sy < -200).all() or (sy > h + 200).all():
            return
        if shade:
            n = _unit(np.cross(pts[1] - pts[0], pts[2] - pts[0]))
            if n @ (eye - pts[0]) < 0:
                n = -n
            k = 0.45 + 0.6 * max(0.0, float(n @ SUN))
        else:
            k = 1.0
        d = float(z.mean())
        fog = min(1.0, (d / 900.0) ** 2)
        rgb = tuple(int(min(255, (c * 255 * k) * (1 - fog) + 205 * fog)) for c in col)
        polys.append((d, list(zip(sx, sy)), rgb))

    for it in parts:
        geo = it["_geo"]
        if np.linalg.norm(geo[2] - eye) > 1400:
            continue
        tris, col = part_tris(geo)
        if geo[0] == "Part" and it["properties"].get("Transparency", 0) >= 1:
            continue
        for t in tris:
            add(t, col)

    # terrain heightfield (Roblox axes: x = east, z = -north)
    M = meta
    for j in range(0, terrain.ny - 1, terrain_step):
        for i in range(0, terrain.nx - 1, terrain_step):
            e = terrain.x0 + (i + 0.5) * terrain.cell; n = terrain.z0 + (j + 0.5) * terrain.cell
            if (e - eye[0]) ** 2 + (-n - eye[2]) ** 2 > terrain_radius ** 2:
                continue
            s = terrain_step
            e1 = e + s * terrain.cell; n1 = n + s * terrain.cell
            h00, h10 = terrain.h[j, i], terrain.h[j, min(i + s, terrain.nx - 1)]
            h01, h11 = terrain.h[min(j + s, terrain.ny - 1), i], terrain.h[min(j + s, terrain.ny - 1), min(i + s, terrain.nx - 1)]
            vi = int((e + s * terrain.cell / 2 - M["x0"]) / M["voxel"]); vj = int((n + s * terrain.cell / 2 - M["z0"]) / M["voxel"])
            code = chr(grid[min(max(vj, 0), grid.shape[0] - 1), min(max(vi, 0), grid.shape[1] - 1)])
            col = tuple(c / 255 for c in TERRAIN_COLORS.get(code, (92, 140, 58)))
            a, b, c_, d_ = (e, h00, -n), (e1, h10, -n), (e1, h11, -n1), (e, h01, -n1)
            add([np.array(a), np.array(b), np.array(c_)], col)
            add([np.array(a), np.array(c_), np.array(d_)], col)

    img = Image.new("RGB", (w, h))
    sky = ImageDraw.Draw(img)
    for yy in range(h):
        t = yy / h
        sky.line([(0, yy), (w, yy)], fill=(int(110 + 95 * t), int(160 + 60 * t), int(220 + 20 * t)))
    polys.sort(key=lambda p: -p[0])
    for _, pts, rgb in polys:
        sky.polygon(pts, fill=rgb)
    sky.text((10, h - 22), "Rough geometry preview (not Roblox rendering)", fill=(255, 255, 255))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    return path


def render_all(parts, terrain, grid, meta, core, spawn, out_dir):
    core = np.array(core); spawn = np.array(spawn)
    def g(e, n):
        return terrain.height(e, n)
    views = {
        "entrance": ((spawn[0], g(*spawn) + 1.7, -spawn[1]), (core[0], g(*core) + 6, -core[1])),
        "quad": ((core[0] - 40, g(core[0] - 40, core[1] - 30) + 1.7, -(core[1] - 30)), (core[0] + 30, g(*core) + 6, -(core[1] + 20))),
        "drone": ((core[0], 260, -(core[1] - 230)), (core[0], 0, -(core[1] + 40))),
    }
    out = []
    for name, (eye, tgt) in views.items():
        drone = name == "drone"
        out.append(render(parts, terrain, grid, meta, eye, tgt, os.path.join(out_dir, f"{name}.png"),
                          terrain_radius=900 if drone else 380, terrain_step=3 if drone else 1))
        print("preview", out[-1])
    return out
