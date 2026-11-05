#!/usr/bin/env python3
"""
Generate the Roblox version of the Biola campus as a Rojo project (roblox/).

Inputs (all open data, no Google content):
  Assets/Resources/OSM/biola_campus.json      OpenStreetMap campus (ODbL)
  Data/osm/biola_surroundings.json            OpenStreetMap streets/houses around campus (ODbL)
  Assets/Resources/OSM/biola_lidar.json       USGS 3DEP lidar building heights + trees (public domain)
  Assets/Resources/Terrain/biola_terrain.json USGS 3DEP lidar terrain (public domain)

Outputs:
  roblox/src/Workspace/Campus/*.model.json    buildings, trees, props (Roblox Parts)
  roblox/src/ServerStorage/BiolaTerrain/      terrain data modules (heights + materials)
  roblox/preview/*.png                        rough software-rendered previews (optional, --preview)

Then:  rojo build roblox/default.project.json -o BiolaCampus.rbxl   (done in CI)

Scale: 1 stud = 0.3 m. World axes: +X = east, +Y = up, -Z = north (right-handed).
"""
import argparse
import base64
import json
import math
import os
import re
import shutil
import sys
import zlib

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OSM = os.path.join(ROOT, "Assets", "Resources", "OSM", "biola_campus.json")
SURR = os.path.join(ROOT, "Data", "osm", "biola_surroundings.json")
LIDAR = os.path.join(ROOT, "Assets", "Resources", "OSM", "biola_lidar.json")
TERRAIN = os.path.join(ROOT, "Assets", "Resources", "Terrain", "biola_terrain.json")
OUT = os.path.join(ROOT, "roblox", "src")

M_PER_STUD = 0.3
S = 1.0 / M_PER_STUD
VOXEL_M = 4 * M_PER_STUD          # Roblox terrain voxels are 4 studs
BAY = 3.2

# ----------------------------------------------------------------------------- vector math


def v(*a):
    return np.array(a, dtype=float)


def unit(a):
    n = np.linalg.norm(a)
    return a / n if n > 1e-12 else a


def R(e, y, n):
    """local ENU metres → Roblox axes (still metres)."""
    return v(e, y, -n)


def r2(p2):
    return v(p2[0], 0, -p2[1])


class Enu:
    def __init__(self, lat, lon):
        self.lat, self.lon = lat, lon
        a, f = 6378137.0, 1 / 298.257223563
        e2 = f * (2 - f)
        la, lo = math.radians(lat), math.radians(lon)
        self.sla, self.cla, self.slo, self.clo = math.sin(la), math.cos(la), math.sin(lo), math.cos(lo)
        self.e2, self.a = e2, a
        self.x0, self.y0, self.z0 = self.ecef(lat, lon)

    def ecef(self, lat, lon):
        la, lo = math.radians(lat), math.radians(lon)
        s = math.sin(la)
        n = self.a / math.sqrt(1 - self.e2 * s * s)
        return n * math.cos(la) * math.cos(lo), n * math.cos(la) * math.sin(lo), n * (1 - self.e2) * s

    def en(self, lat, lon):
        x, y, z = self.ecef(lat, lon)
        dx, dy, dz = x - self.x0, y - self.y0, z - self.z0
        e = -self.slo * dx + self.clo * dy
        n = -self.sla * self.clo * dx - self.sla * self.slo * dy + self.cla * dz
        return e, n

    def ring(self, flat):
        pts = [self.en(flat[i], flat[i + 1]) for i in range(0, len(flat) - 1, 2)]
        if len(pts) > 2 and math.dist(pts[0], pts[-1]) < 0.05:
            pts.pop()
        return pts


# ----------------------------------------------------------------------------- 2D polygon helpers


def area(poly):
    return 0.5 * sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly)))


def centroid(poly):
    a = area(poly)
    if abs(a) < 1e-6:
        return (sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly))
    cx = cy = 0
    for i in range(len(poly)):
        x0, y0 = poly[i]; x1, y1 = poly[(i + 1) % len(poly)]
        c = x0 * y1 - x1 * y0
        cx += (x0 + x1) * c; cy += (y0 + y1) * c
    return (cx / (6 * a), cy / (6 * a))


def inside(poly, p):
    x, y = p; c = False
    for i in range(len(poly)):
        x1, y1 = poly[i]; x2, y2 = poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1 + 1e-12) + x1:
            c = not c
    return c


def seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay
    L = dx * dx + dy * dy
    t = 0 if L < 1e-9 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / L))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def clean(poly):
    o = []
    for p in poly:
        if not o or math.dist(p, o[-1]) > 0.3:
            o.append(p)
    if len(o) > 2 and math.dist(o[0], o[-1]) < 0.3:
        o.pop()
    changed = True
    while changed and len(o) > 3:
        changed = False
        for i in range(len(o)):
            a, b, c = o[i - 1], o[i], o[(i + 1) % len(o)]
            d1 = unit(v(b[0] - a[0], b[1] - a[1])); d2 = unit(v(c[0] - b[0], c[1] - b[1]))
            if float(d1 @ d2) > 0.9985:
                o.pop(i); changed = True; break
    if area(o) < 0:
        o.reverse()
    return o


def offset(ring, d):
    """Miter offset of a CCW ring (positive = outward)."""
    n = len(ring); out = []
    for i in range(n):
        p0, p, p1 = v(*ring[i - 1]), v(*ring[i]), v(*ring[(i + 1) % n])
        e1, e2 = unit(p - p0), unit(p1 - p)
        n1, n2 = v(e1[1], -e1[0]), v(e2[1], -e2[0])
        m = n1 + n2
        if np.linalg.norm(m) < 1e-4:
            out.append(tuple(p + n1 * d)); continue
        m = unit(m)
        cos = max(0.35, float(m @ n1))
        out.append(tuple(p + m * (d / cos)))
    return out


def triangulate(poly):
    """Ear clipping; returns CCW index triples."""
    n = len(poly)
    if n < 3:
        return []
    idx = list(range(n)) if area(poly) > 0 else list(range(n - 1, -1, -1))
    tris = []

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    def in_tri(p, a, b, c):
        d1, d2, d3 = cross(a, b, p), cross(b, c, p), cross(c, a, p)
        return not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0))

    guard = 0
    while len(idx) > 3 and guard < 10000:
        guard += 1
        clipped = False
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            a, b, c = poly[i0], poly[i1], poly[i2]
            if cross(a, b, c) <= 1e-9:
                continue
            if any(in_tri(poly[j], a, b, c) for j in idx if j not in (i0, i1, i2)):
                continue
            tris.append((i0, i1, i2)); idx.pop(k); clipped = True
            break
        if not clipped:
            for k in range(1, len(idx) - 1):
                tris.append((idx[0], idx[k], idx[k + 1]))
            return tris
    if len(idx) == 3:
        tris.append(tuple(idx))
    return tris


def min_area_rect(pts):
    best = None
    for i in range(len(pts)):
        e = v(pts[(i + 1) % len(pts)][0] - pts[i][0], pts[(i + 1) % len(pts)][1] - pts[i][1])
        if np.linalg.norm(e) < 0.5:
            continue
        u = unit(e); w = v(-u[1], u[0])
        P = np.array(pts)
        pu, pw = P @ u, P @ w
        a = (pu.max() - pu.min()) * (pw.max() - pw.min())
        if best is None or a < best[0]:
            c = u * (pu.max() + pu.min()) / 2 + w * (pw.max() + pw.min()) / 2
            hu, hw = (pu.max() - pu.min()) / 2, (pw.max() - pw.min()) / 2
            best = (a, c, u, hu, hw) if hu >= hw else (a, c, w, hw, hu)
    return best


# ----------------------------------------------------------------------------- part emission


class Parts:
    """Collects Roblox parts (metres, Roblox axes) and writes Rojo .model.json."""

    def __init__(self):
        self.items = []

    @staticmethod
    def _cf(pos, right, up):
        right, up = unit(right), unit(up)
        back = np.cross(right, up)
        # Rojo implicit CFrame: [x, y, z, R00, R01, R02, R10, R11, R12, R20, R21, R22] (rows of the
        # rotation matrix whose columns are right, up, back).
        rows = [[right[i], up[i], back[i]] for i in range(3)]
        return [round(float(x) * S, 3) for x in pos] + [round(float(x), 6) for r in rows for x in r]

    def add(self, name, pos, size, right, up, material, color, cls="Part", shape=None, transparency=0.0, reflectance=0.0,
            collide=True, shadow=True, children=None, tri=None, attributes=None):
        props = {
            "Anchored": True,
            "Size": [round(max(0.002, float(s)) * S, 3) for s in size],
            "CFrame": self._cf(pos, right, up),
            "Material": material,
            "Color": [round(c, 4) for c in color],
            "CanCollide": collide,
            "CastShadow": shadow,
            "TopSurface": "Smooth",
            "BottomSurface": "Smooth",
        }
        if shape:
            props["Shape"] = shape
        if transparency:
            props["Transparency"] = transparency
        if reflectance:
            props["Reflectance"] = reflectance
        item = {"name": name, "className": cls, "properties": props}
        if children:
            item["children"] = children
        if attributes:
            item["attributes"] = attributes
        # keep a compact geometric description for the preview renderer
        item["_geo"] = (cls, shape, np.array(pos, float), np.array(size, float), unit(np.array(right, float)), unit(np.array(up, float)), color)
        self.items.append(item)
        return item

    def box(self, name, center, size, right, material, color, up=v(0, 1, 0), **kw):
        return self.add(name, center, size, right, up, material, color, **kw)

    def vcyl(self, name, base, height, diameter, material, color, **kw):
        """Vertical cylinder (Roblox cylinders run along local X)."""
        return self.add(name, base + v(0, height / 2, 0), (height, diameter, diameter), v(0, 1, 0), v(1, 0, 0), material, color, shape="Cylinder", **kw)

    def ball(self, name, center, d, material, color, **kw):
        # Roblox balls are always spherical (Size is forced uniform).
        return self.add(name, center, (d, d, d), v(1, 0, 0), v(0, 1, 0), material, color, shape="Ball", **kw)

    def triangle(self, name, a, b, c, thickness, material, color, **kw):
        """Any triangle from two WedgeParts (classic Roblox technique)."""
        a, b, c = np.array(a, float), np.array(b, float), np.array(c, float)
        ab, ac, bc = b - a, c - a, c - b
        abd, acd, bcd = ab @ ab, ac @ ac, bc @ bc
        if abd > acd and abd > bcd:
            c, a = a, c
        elif acd > bcd and acd > abd:
            a, b = b, a
        ab, ac, bc = b - a, c - a, c - b
        nrm = np.cross(ac, ab)
        if np.linalg.norm(nrm) < 1e-9:
            return
        right = unit(nrm)
        up = unit(np.cross(bc, right))
        back = unit(bc)
        height = abs(ab @ up)
        if height < 1e-3:
            return
        self.add(name, (a + b) / 2, (thickness, height, abs(ab @ back)), right, up, material, color, cls="WedgePart", **kw)
        self.add(name, (a + c) / 2, (thickness, height, abs(ac @ back)), -right, up, material, color, cls="WedgePart", **kw)

    def write(self, path, class_name="Model"):
        children = []
        for it in self.items:
            d = {k: it[k] for k in ("name", "className", "properties")}
            for k in ("children", "attributes"):
                if k in it:
                    d[k] = it[k]
            children.append(d)
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"className": class_name, "children": children}, f, separators=(",", ":"))


# ----------------------------------------------------------------------------- styles

def C(hexstr):
    h = hexstr.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


STYLES = {
    # wall material, wall colour, trim colour, glass colour, window w/h fraction, sill fraction, bands
    "cream":   dict(mat="Plaster", wall=C("e3d6b8"), trim=C("f2ece0"), glass=C("2e4656"), ww=0.56, wh=0.52, sill=0.27, bands=True),
    "sand":    dict(mat="Plaster", wall=C("cfb38b"), trim=C("f1ede4"), glass=C("314a59"), ww=0.44, wh=0.48, sill=0.29, bands=False),
    "brick":   dict(mat="Brick",   wall=C("9a5541"), trim=C("e2dccd"), glass=C("2a3d4b"), ww=0.50, wh=0.55, sill=0.25, bands=True),
    "modern":  dict(mat="Concrete", wall=C("d8d8d4"), trim=C("9ea1a4"), glass=C("3b5f74"), ww=0.82, wh=0.66, sill=0.18, bands=True),
    "mission": dict(mat="Plaster", wall=C("f1ede3"), trim=C("b5734d"), glass=C("2b3a46"), ww=0.40, wh=0.58, sill=0.24, bands=False),
    "garage":  dict(mat="Concrete", wall=C("bdbab3"), trim=C("cfccc5"), glass=C("1a1b1d"), ww=0.94, wh=0.45, sill=0.30, bands=True),
    "house":   dict(mat="Plaster", wall=C("e6dcc8"), trim=C("ffffff"), glass=C("34495a"), ww=0.30, wh=0.45, sill=0.33, bands=False),
}
ROOF_TILE = C("b45a34")
FLAT_ROOF = C("a8a6a0")
CONCRETE = C("c9c6be")
NOT_HERO = ["Parking", "Grove #", "Modulars", "Gatehouse", "Central Plant", "Central Stores", "Facilities", "Production Center", "Mail Services"]


def style_for(name, bid):
    n = (name or "").lower()
    if "parking" in n: return "garage"
    if "chapel" in n or "prayer" in n: return "mission"
    if any(k in n for k in ("library", "science", "cinema", "school of business", "student services", "union")): return "modern"
    if any(k in n for k in ("myers", "bardwell", "rood", "crowell hall", "feinberg")): return "brick"
    if is_residential(name): return "sand"
    return "sand" if zlib.crc32(bid.encode()) % 3 == 0 else "cream"


def is_residential(name):
    if not name: return False
    if any(k in name for k in ("Crowell", "Myers", "Bardwell", "Rood", "Feinberg")): return False
    return any(k in name for k in ("Apartments", "Hope", "Horton", "Alpha", "Sigma", "Stewart", "Hart", "Emerson", "Thompson", "Blackstone", "Hall"))


# ----------------------------------------------------------------------------- terrain


class Terrain:
    def __init__(self, path):
        t = json.load(open(path, encoding="utf-8"))
        m = t["meta"]
        self.meta = m
        self.nx, self.ny, self.cell, self.x0, self.z0 = m["nx"], m["ny"], m["cell"], m["x0"], m["z0"]
        self.h = np.frombuffer(base64.b64decode(t["heights"]), dtype="<i2").reshape(self.ny, self.nx).astype(float) / 100.0

    def height(self, e, n):
        fx = (e - self.x0) / self.cell - 0.5; fz = (n - self.z0) / self.cell - 0.5
        i = int(math.floor(fx)); j = int(math.floor(fz)); tx = fx - i; tz = fz - j
        def node(a, b):
            return self.h[min(max(b, 0), self.ny - 1), min(max(a, 0), self.nx - 1)]
        return (node(i, j) * (1 - tx) + node(i + 1, j) * tx) * (1 - tz) + (node(i, j + 1) * (1 - tx) + node(i + 1, j + 1) * tx) * tz


# ----------------------------------------------------------------------------- building generator


def build_building(P, bld, lidar, terrain, hero, entrance_toward, label):
    outline = clean(bld["ring"])
    if len(outline) < 3:
        return
    A = abs(area(outline))
    c2 = centroid(outline)
    lb = lidar.get(bld["id"]) if lidar else None
    ground = lb["ground"] if lb and lb.get("points", 0) > 0 else terrain.height(*c2)
    min_t = min([ground] + [terrain.height(*p) for p in outline])
    base = min_t - 0.5
    pitched = bool(lb and lb.get("roof") == "pitched")
    if lb and lb.get("roof"):
        total = lb["eave"] if pitched else lb["height"]
    else:
        total = bld.get("height") or (bld.get("levels", 0) * 3.4 + 1.5 if bld.get("levels") else (6.5 if bld.get("house") else 9.0))
    total = max(3.0, min(60.0, total))
    wall_top = ground + total
    st = STYLES[bld["style"]]
    parapet = 0.0 if pitched else (0.9 if A > 120 else 0.4)
    floor_h = 3.1 if bld.get("residential") or bld.get("house") else 3.9
    floors = max(1, round((total - parapet) / floor_h))
    eff = max(2.6, (total - parapet) / floors)
    t = 0.3   # wall thickness
    name = bld.get("name") or "Building"

    # walls + windows
    for i in range(len(outline)):
        a, c = v(*outline[i]), v(*outline[(i + 1) % len(outline)])
        L = float(np.linalg.norm(c - a))
        if L < 0.05:
            continue
        d = (c - a) / L
        out = v(d[1], -d[0])
        mid = (a + c) / 2 - out * (t / 2)
        right3 = r2(out);
        P.box("Wall", R(mid[0], (base + wall_top) / 2, mid[1]), (t, wall_top - base, L + t), right3, st["mat"], st["wall"])
        if L >= 2.2:
            bays = max(1, round(L / BAY)); bw = L / bays
            for f in range(floors):
                y0 = ground + f * eff
                for b_ in range(bays):
                    s_ = (b_ + 0.5) * bw
                    p2 = a + d * s_ + out * 0.04
                    wy = y0 + st["sill"] * eff + st["wh"] * eff / 2
                    P.box("Window", R(p2[0], wy, p2[1]), (0.12, st["wh"] * eff, st["ww"] * bw), right3, "Glass", st["glass"],
                          reflectance=0.25, transparency=0.0, collide=False, shadow=False)
                    sp = a + d * s_ + out * 0.08
                    P.box("Sill", R(sp[0], y0 + st["sill"] * eff - 0.06, sp[1]), (0.2, 0.12, st["ww"] * bw + 0.25), right3, "Concrete", st["trim"], collide=False)
                if st["bands"] and f > 0:
                    bp = a + d * (L / 2) + out * 0.05
                    P.box("Band", R(bp[0], y0, bp[1]), (0.12, 0.28, L + 0.1), right3, "Concrete", st["trim"], collide=False)
        # plinth
        pp = (a + c) / 2 + out * 0.06
        P.box("Plinth", R(pp[0], (base + ground + 0.45) / 2, pp[1]), (0.14, ground + 0.45 - base, L + 0.25), right3, "Concrete", CONCRETE)
        if not pitched:
            cp = (a + c) / 2
            P.box("Coping", R(cp[0], wall_top + 0.08, cp[1]), (t + 0.18, 0.16, L + t + 0.18), right3, "Concrete", st["trim"])

    # roof
    done = False
    if pitched:
        done = pitched_roof(P, outline, A, wall_top, min(max(ground + lb["ridge"], wall_top + 1.2), wall_top + 7), st)
    if not done:
        roof_y = wall_top - parapet if parapet > 0.01 else wall_top
        inner = offset(outline, -t) if parapet > 0.01 else outline
        if abs(area(inner)) < 1 or area(inner) <= 0:
            inner = outline
        for (i0, i1, i2) in triangulate(inner):
            P.triangle("Roof", R(inner[i0][0], roof_y, inner[i0][1]), R(inner[i1][0], roof_y, inner[i1][1]), R(inner[i2][0], roof_y, inner[i2][1]),
                       0.3, "Concrete", FLAT_ROOF)
        if hero and A > 500:
            rnd = np.random.default_rng(zlib.crc32(bld["id"].encode()))
            xs = [p[0] for p in inner]; ys = [p[1] for p in inner]
            placed = 0
            for _ in range(40):
                if placed >= 3: break
                q = (rnd.uniform(min(xs), max(xs)), rnd.uniform(min(ys), max(ys)))
                sx, sz, sy = rnd.uniform(2, 4.5), rnd.uniform(1.6, 3.6), rnd.uniform(1.2, 2.0)
                if not inside(inner, q) or min(seg_dist(q, inner[k], inner[(k + 1) % len(inner)]) for k in range(len(inner))) < max(sx, sz) * 0.75 + 0.5:
                    continue
                P.box("RooftopUnit", R(q[0], roof_y + sy / 2, q[1]), (sx, sy, sz), v(1, 0, 0), "Metal", C("9da3a8"))
                placed += 1

    if hero and entrance_toward is not None:
        entrance(P, outline, ground, entrance_toward, st)
    if name == "Library":
        top = wall_top + 0.2
        P.vcyl("Beacon", R(c2[0], top, c2[1]), 2.2, 8.4, "Glass", C("dbe8f5"), transparency=0.15, reflectance=0.3,
               children=[{"name": "BeaconLight", "className": "PointLight", "properties": {"Brightness": 3, "Range": 40, "Color": [0.85, 0.92, 1.0], "Shadows": False}}])
        P.vcyl("BeaconCap", R(c2[0], top + 2.2, c2[1]), 0.2, 9.0, "Concrete", st["trim"])
    if label:
        # The client script turns these anchors into BillboardGui name labels.
        P.box("Label", R(c2[0], wall_top + 4, c2[1]), (1, 1, 1), v(1, 0, 0), "SmoothPlastic", (1, 1, 1), transparency=1, collide=False, shadow=False,
              attributes={"BuildingName": name})
    return wall_top


def pitched_roof(P, outline, A, eave_y, ridge_y, st):
    r = min_area_rect(outline)
    if r is None:
        return False
    _, c, u, hu, hv = r
    if A / (4 * hu * hv) < 0.72:
        return False
    w = v(-u[1], u[0]); over = 0.55
    hu += over; hv += over
    eave = eave_y - 0.25
    def W(a, b, y):
        p = c + u * a + w * b
        return R(p[0], y, p[1])
    c00, c10, c11, c01 = W(-hu, -hv, eave), W(hu, -hv, eave), W(hu, hv, eave), W(-hu, hv, eave)
    gable = hu / hv > 1.45
    rh = hu if gable else max(0.0, hu - hv)
    r0, r1 = W(-rh, 0, ridge_y), W(rh, 0, ridge_y)
    th = 0.35
    for (a, b, cc) in ((c00, c10, r1), (c00, r1, r0), (c11, c01, r0), (c11, r0, r1)):
        P.triangle("RoofTiles", a, b, cc, th, "ClayRoofTiles", ROOF_TILE)
    if gable:
        P.triangle("Gable", c01, c00, r0, 0.3, st["mat"], st["wall"])
        P.triangle("Gable", c10, c11, r1, 0.3, st["mat"], st["wall"])
    else:
        P.triangle("RoofTiles", c01, c00, r0, th, "ClayRoofTiles", ROOF_TILE)
        P.triangle("RoofTiles", c10, c11, r1, th, "ClayRoofTiles", ROOF_TILE)
    return True


def entrance(P, outline, ground, toward, st):
    best, bd = None, 1e18
    for i in range(len(outline)):
        a, c = v(*outline[i]), v(*outline[(i + 1) % len(outline)])
        if np.linalg.norm(c - a) < 6.5:
            continue
        d = np.linalg.norm((a + c) / 2 - toward)
        if d < bd:
            bd, best = d, i
    if best is None:
        return
    a, c = v(*outline[best]), v(*outline[(best + 1) % len(outline)])
    d = unit(c - a); out = v(d[1], -d[0]); mid = (a + c) / 2
    right3 = r2(out)
    dp = mid + out * 0.08
    P.box("Doors", R(dp[0], ground + 1.35, dp[1]), (0.12, 2.7, 3.4), right3, "Glass", C("223442"), reflectance=0.3, collide=False)
    cp = mid + out * 1.35
    P.box("Canopy", R(cp[0], ground + 3.3, cp[1]), (2.7, 0.25, 5.0), right3, "Concrete", st["trim"])
    for s_ in (-1, 1):
        col = mid + out * 2.45 + d * (s_ * 2.2)
        P.vcyl("Column", R(col[0], ground, col[1]), 3.2, 0.34, "Concrete", st["trim"])
    lp = mid + out * 1.4
    P.box("Landing", R(lp[0], ground + 0.06, lp[1]), (3.0, 0.12, 5.4), right3, "Concrete", CONCRETE)


def simple_house(P, ring, terrain, levels, height, seed):
    """Low-part-count house/apartment for the street edge: rectangle walls, a few windows, hip roof."""
    r = min_area_rect(clean(ring)) if len(clean(ring)) >= 3 else None
    if r is None:
        return 0
    _, c, u, hu, hv = r
    if hu < 2 or hv < 2:
        return 0
    n0 = len(P.items)
    w = v(-u[1], u[0])
    ground = terrain.height(*c)
    corners = [c - u * hu - w * hv, c + u * hu - w * hv, c + u * hu + w * hv, c - u * hu + w * hv]
    base = min(terrain.height(*p) for p in corners) - 0.4
    floors = max(1, int(levels or (2 if hu * hv * 4 > 500 else 1)))
    h = height or floors * 3.1 + 0.4
    top = ground + h
    rng = np.random.default_rng(seed)
    palette = [C("e9dfcc"), C("d9c7a6"), C("f0ece4"), C("c9b79c"), C("dcd3c3"), C("e6d3b3")]
    wall = palette[int(rng.integers(len(palette)))]
    for k in range(4):
        a, b_ = corners[k], corners[(k + 1) % 4]
        L = float(np.linalg.norm(b_ - a)); d = (b_ - a) / L; out = v(d[1], -d[0])
        mid = (a + b_) / 2 - out * 0.15
        P.box("HouseWall", R(mid[0], (base + top) / 2, mid[1]), (0.3, top - base, L + 0.3), r2(out), "Plaster", wall)
        nwin = 1 if L < 9 else 2
        for f in range(floors):
            for i in range(nwin):
                p2 = a + d * (L * (i + 1) / (nwin + 1)) + out * 0.04
                P.box("Window", R(p2[0], ground + f * 3.1 + 1.6, p2[1]), (0.12, 1.3, 1.5), r2(out), "Glass", C("34495a"),
                      reflectance=0.2, collide=False, shadow=False)
    ridge = top + min(3.2, hv * 0.55)
    pitched_roof(P, [tuple(p) for p in corners], 4 * hu * hv, top, ridge, {"mat": "Plaster", "wall": wall})
    return len(P.items) - n0


# ----------------------------------------------------------------------------- vegetation & props


def tree(P, kind, p2, y, h, r, seed):
    rnd = np.random.default_rng(seed)
    if kind == "palm":
        P.vcyl("PalmTrunk", R(p2[0], y, p2[1]), h - 0.8, 0.45, "Wood", C("8a7458"))
        top = R(p2[0], y + h - 0.8, p2[1])
        P.ball("PalmHeart", top, 1.0, "LeafyGrass", C("5d7a2c"))
        n = 9
        for k in range(n):
            ang = 2 * math.pi * k / n + rnd.uniform(-0.15, 0.15)
            dirv = v(math.cos(ang), 0, math.sin(ang))
            L = rnd.uniform(2.6, 3.6)
            droop = rnd.uniform(0.25, 0.55)
            dvec = unit(dirv + v(0, -droop, 0))
            center = top + dvec * (L / 2) + v(0, 0.2, 0)
            side = unit(np.cross(v(0, 1, 0), dirv))
            upv = unit(np.cross(dvec, side))
            P.add("Frond", center, (0.9, 0.08, L), side, upv, "LeafyGrass", C("6f8f34"), collide=False)
        return
    trunk_top = y + max(1.5, h - r * 1.1)
    P.vcyl("Trunk", R(p2[0], y, p2[1]), trunk_top - y + 0.4, max(0.3, min(0.9, r * 0.12 + 0.18)), "Wood", C("5b4636"))
    green = [C("4e6b2e"), C("5d7a34"), C("435f28")] if kind != "jacaranda" else [C("8e7cc3"), C("9d8ad0"), C("7f6db5")]
    mat = "LeafyGrass" if kind != "jacaranda" else "Grass"
    cy = trunk_top + r * 0.35
    P.ball("Canopy", R(p2[0], cy, p2[1]), r * 1.45, mat, green[0], collide=False)
    for k in range(4):
        ang = 2 * math.pi * k / 4 + rnd.uniform(0, 1.2)
        off = v(math.cos(ang), 0, math.sin(ang)) * r * rnd.uniform(0.45, 0.6)
        P.ball("Canopy", R(p2[0], cy, p2[1]) + off + v(0, rnd.uniform(-0.3, 0.5) * r * 0.5, 0), r * rnd.uniform(0.95, 1.25), mat,
               green[k % 3], collide=False)


def lamp(P, p2, y):
    P.vcyl("LampPost", R(p2[0], y, p2[1]), 4.2, 0.16, "Metal", C("1e2124"))
    P.box("LampHead", R(p2[0], y + 4.25, p2[1]), (0.45, 0.55, 0.45), v(1, 0, 0), "Glass", C("efe6d0"), collide=False,
          children=[{"name": "LampLight", "className": "PointLight",
                     "properties": {"Brightness": 1.6, "Range": 18, "Color": [1.0, 0.82, 0.56], "Shadows": False, "Enabled": False}}])
    P.box("LampCap", R(p2[0], y + 4.58, p2[1]), (0.6, 0.1, 0.6), v(1, 0, 0), "Metal", C("1e2124"), collide=False)


def bench(P, p2, y, face):
    f3 = r2(face); side = np.cross(v(0, 1, 0), f3)
    base = R(p2[0], y, p2[1])
    P.box("BenchSeat", base + v(0, 0.45, 0), (1.8, 0.07, 0.45), side, "WoodPlanks", C("8c5e36"))
    P.box("BenchBack", base + v(0, 0.75, 0) - f3 * 0.2, (1.8, 0.4, 0.06), side, "WoodPlanks", C("8c5e36"))
    for s_ in (-0.8, 0.8):
        P.box("BenchLeg", base + v(0, 0.22, 0) + side * s_, (0.06, 0.45, 0.45), side, "Metal", C("2a2d30"))


# ----------------------------------------------------------------------------- terrain materials


def rasterize_materials(osm, surr, enu, terrain, campus_ring, buildings2d):
    """Material code per 4-stud voxel column."""
    x0, z0 = terrain.x0, terrain.z0
    W, H = terrain.nx * terrain.cell, terrain.ny * terrain.cell
    nx, nz = int(W // VOXEL_M), int(H // VOXEL_M)
    grid = np.full((nz, nx), ord("G"), dtype=np.uint8)
    xs = x0 + (np.arange(nx) + 0.5) * VOXEL_M
    zs = z0 + (np.arange(nz) + 0.5) * VOXEL_M

    def fill_poly(ring, code):
        ring = np.array(ring)
        jmin = max(0, int((ring[:, 1].min() - z0) / VOXEL_M)); jmax = min(nz - 1, int((ring[:, 1].max() - z0) / VOXEL_M) + 1)
        for j in range(jmin, jmax + 1):
            y = zs[j]; xsx = []
            for k in range(len(ring)):
                ax, ay = ring[k]; bx, by = ring[k - 1]
                if (ay > y) != (by > y):
                    xsx.append(ax + (y - ay) / (by - ay) * (bx - ax))
            xsx.sort()
            for a, b in zip(xsx[0::2], xsx[1::2]):
                i0 = max(0, int(math.ceil((a - x0) / VOXEL_M - 0.5))); i1 = min(nx - 1, int(math.floor((b - x0) / VOXEL_M - 0.5)))
                if i1 >= i0:
                    grid[j, i0:i1 + 1] = ord(code)

    def stroke(pts, width, code):
        for k in range(len(pts) - 1):
            a, b = pts[k], pts[k + 1]
            pad = width / 2 + VOXEL_M
            i0 = max(0, int((min(a[0], b[0]) - pad - x0) / VOXEL_M)); i1 = min(nx - 1, int((max(a[0], b[0]) + pad - x0) / VOXEL_M))
            j0 = max(0, int((min(a[1], b[1]) - pad - z0) / VOXEL_M)); j1 = min(nz - 1, int((max(a[1], b[1]) + pad - z0) / VOXEL_M))
            if i1 < i0 or j1 < j0:
                continue
            X, Z = np.meshgrid(xs[i0:i1 + 1], zs[j0:j1 + 1])
            dx, dy = b[0] - a[0], b[1] - a[1]
            L = dx * dx + dy * dy
            tt = np.clip(((X - a[0]) * dx + (Z - a[1]) * dy) / max(L, 1e-9), 0, 1)
            dist = np.hypot(X - a[0] - tt * dx, Z - a[1] - tt * dy)
            sub = grid[j0:j1 + 1, i0:i1 + 1]
            sub[dist <= width / 2] = ord(code)

    area_code = {"parking": "A", "pitch": "G", "stadium": "G", "grass": "G", "park": "G", "garden": "L", "forest": "L", "orchard": "L",
                 "wood": "L", "scrub": "L", "construction": "R", "swimming_pool": "W", "fountain": "W", "water": "W", "bicycle_parking": "C",
                 "residential": None}
    # surroundings first (campus wins)
    if surr:
        for a in surr["areas"]:
            code = area_code.get(a["kind"])
            if code and code != "G":
                fill_poly(enu.ring(a["pts"]), code)
        for p in surr["paths"]:
            k = p["kind"]
            pts = [enu.en(p["pts"][i], p["pts"][i + 1]) for i in range(0, len(p["pts"]) - 1, 2)]
            if k in ("primary", "secondary", "tertiary", "residential", "unclassified", "trunk", "living_street"):
                lanes = p.get("lanes") or (4 if k in ("primary", "secondary") else 2)
                w = max(7.5, lanes * 3.4 + 1.5)
                stroke(pts, w + 4.0, "P")     # sidewalks
                stroke(pts, w, "A")
            elif k == "service":
                stroke(pts, 5.0, "A")
            elif k in ("footway", "path", "pedestrian"):
                stroke(pts, 2.0, "P")
        for b in surr["buildings"]:
            fill_poly(enu.ring(b["pts"]), "C")
    # campus areas and paths
    for a in osm["areas"]:
        code = area_code.get(a["kind"])
        if a.get("name") and "sand" in a["name"].lower():
            code = "S"
        if code:
            fill_poly(enu.ring(a["pts"]), code)
    for p in osm["paths"]:
        pts = [enu.en(p["pts"][i], p["pts"][i + 1]) for i in range(0, len(p["pts"]) - 1, 2)]
        k = p["kind"]
        if k in ("service", "residential", "unclassified", "tertiary", "living_street"):
            stroke(pts, 6.5 if k == "service" else 8.5, "A")
    for p in osm["paths"]:
        pts = [enu.en(p["pts"][i], p["pts"][i + 1]) for i in range(0, len(p["pts"]) - 1, 2)]
        k = p["kind"]
        if k == "footway": stroke(pts, 3.0, "B")
        elif k == "pedestrian": stroke(pts, 6.0, "B")
        elif k in ("path", "track"): stroke(pts, 2.2, "R")
        elif k == "steps": stroke(pts, 2.5, "C")
    for ring in buildings2d:
        fill_poly(ring, "C")
    return grid, nx, nz


ALPH = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"


def encode_heights(h):
    """2 chars per value: centimetre-ish (0.05 m) steps offset from the minimum."""
    hmin = float(h.min())
    q = np.clip(np.round((h - hmin) / 0.05), 0, 4095).astype(int)
    chars = []
    for val in q.flatten():
        chars.append(ALPH[val >> 6]); chars.append(ALPH[val & 63])
    return "".join(chars), hmin


def rle_rows(grid):
    rows = []
    for j in range(grid.shape[0]):
        row = grid[j]; parts = []
        start = 0
        for i in range(1, len(row) + 1):
            if i == len(row) or row[i] != row[start]:
                parts.append(f"{chr(row[start])}{i - start}")
                start = i
        rows.append("".join(parts))
    return rows


def write_terrain_modules(terrain, grid, nx, nz, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    hs, hmin = encode_heights(terrain.h)
    chunk = 180000
    hparts = [hs[i:i + chunk] for i in range(0, len(hs), chunk)]
    for k, part in enumerate(hparts):
        with open(os.path.join(out_dir, f"Heights{k + 1}.luau"), "w") as f:
            f.write(f'-- Generated by Tools/roblox/build_roblox.py (USGS 3DEP lidar, public domain)\nreturn "{part}"\n')
    rows = rle_rows(grid)
    mparts, cur = [], []
    size = 0
    for r in rows:
        cur.append(r); size += len(r) + 1
        if size > 180000:
            mparts.append(cur); cur = []; size = 0
    if cur:
        mparts.append(cur)
    for k, part in enumerate(mparts):
        with open(os.path.join(out_dir, f"Materials{k + 1}.luau"), "w") as f:
            f.write("-- Generated: terrain material per 4-stud column, run-length encoded rows (OpenStreetMap, ODbL)\nreturn \"" + "|".join(part) + "\"\n")
    meta = {"heightChunks": len(hparts), "materialChunks": len(mparts), "hNx": terrain.nx, "hNz": terrain.ny, "hCell": terrain.cell,
            "x0": terrain.x0, "z0": terrain.z0, "hMin": hmin, "vNx": nx, "vNz": nz, "voxel": VOXEL_M, "mPerStud": M_PER_STUD}
    with open(os.path.join(out_dir, "Meta.luau"), "w") as f:
        f.write("-- Generated terrain metadata\nreturn {\n" + "".join(
            f"\t{k} = {json.dumps(val) if not isinstance(val, float) else repr(round(val, 4))},\n" for k, val in meta.items()) + "}\n")
    return meta


# ----------------------------------------------------------------------------- main


def safe(name):
    return re.sub(r"[^A-Za-z0-9 _.-]", "", name).strip().replace(" ", "_")[:60] or "Building"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true", help="also render rough previews into roblox/preview")
    args = ap.parse_args()

    osm = json.load(open(OSM, encoding="utf-8"))
    surr = json.load(open(SURR, encoding="utf-8")) if os.path.exists(SURR) else None
    lid = json.load(open(LIDAR, encoding="utf-8")) if os.path.exists(LIDAR) else {"buildings": [], "trees": []}
    terrain = Terrain(TERRAIN)
    enu = Enu(osm["meta"]["centerLat"], osm["meta"]["centerLon"])
    lidar_by_id = {b["id"]: b for b in lid["buildings"]}
    campus_ring = enu.ring(osm["boundary"][0]["pts"])

    campus_dir = os.path.join(OUT, "Workspace", "Campus")
    if os.path.isdir(campus_dir):
        shutil.rmtree(campus_dir)
    os.makedirs(os.path.join(campus_dir, "Buildings"), exist_ok=True)

    # walkway points for entrances
    walk = []
    for p in osm["paths"]:
        if p["kind"] in ("footway", "pedestrian", "path"):
            walk += [enu.en(p["pts"][i], p["pts"][i + 1]) for i in range(0, len(p["pts"]) - 1, 2)]
    walk = np.array(walk) if walk else np.zeros((0, 2))

    # hero selection (same rule as the Unity build)
    core = centroid(campus_ring)
    for a in osm["areas"]:
        if a.get("name") == "Metzger Lawn":
            core = centroid(enu.ring(a["pts"])); break
    scored = []
    for b in osm["buildings"]:
        if not b.get("name") or any(k in b["name"] for k in NOT_HERO):
            continue
        ring = enu.ring(b["pts"])
        scored.append((math.sqrt(abs(area(ring))) / (1 + math.dist(centroid(ring), core) / 160), b["id"]))
    heroes = {i for _, i in sorted(scored, reverse=True)[:22]}

    all_parts = []
    bld2d = []
    stats = {"buildings": 0, "parts": 0}
    for b in osm["buildings"]:
        ring = enu.ring(b["pts"])
        bld2d.append(ring)
        P = Parts()
        hero = b["id"] in heroes
        toward = None
        if hero and len(walk):
            c2 = v(*centroid(ring))
            toward = walk[np.argmin(((walk - c2) ** 2).sum(1))]
        build_building(P, {"id": b["id"], "name": b.get("name"), "ring": ring, "style": style_for(b.get("name"), b["id"]),
                           "residential": is_residential(b.get("name")), "levels": b.get("levels"), "height": b.get("height")},
                       lidar_by_id, terrain, hero, toward, label=bool(b.get("name")))
        fname = safe(b.get("name") or b["id"].replace("/", "_"))
        while os.path.exists(os.path.join(campus_dir, "Buildings", fname + ".model.json")):
            fname += "_2"
        P.write(os.path.join(campus_dir, "Buildings", fname + ".model.json"))
        stats["buildings"] += 1; stats["parts"] += len(P.items); all_parts += P.items

    # surroundings: houses (simple hip roofs) outside campus
    if surr:
        os.makedirs(os.path.join(campus_dir, "Neighborhood"), exist_ok=True)
        P = Parts(); n = 0
        for b in surr["buildings"]:
            ring = enu.ring(b["pts"])
            if len(ring) < 3: continue
            c2 = centroid(ring)
            if inside(campus_ring, c2): continue
            if not (terrain.x0 < c2[0] < terrain.x0 + terrain.nx * terrain.cell and terrain.z0 < c2[1] < terrain.z0 + terrain.ny * terrain.cell):
                continue
            # street edge only: within ~120 m of the campus boundary
            if min(seg_dist(c2, campus_ring[k], campus_ring[(k + 1) % len(campus_ring)]) for k in range(len(campus_ring))) > 120:
                continue
            bld2d.append(ring)
            if simple_house(P, ring, terrain, b.get("levels"), b.get("height"), zlib.crc32(b["id"].encode())) == 0:
                continue
            n += 1
        P.write(os.path.join(campus_dir, "Neighborhood", "Houses.model.json"))
        stats["houses"] = n; stats["parts"] += len(P.items); all_parts += P.items

    # trees from lidar (campus + 20 m)
    P = Parts(); nt = 0
    for i, t in enumerate(lid.get("trees", [])):
        p2 = (t["x"], t["z"])
        if any(inside(r, p2) for r in bld2d):
            continue
        y = terrain.height(*p2) - 0.1
        tree(P, t["kind"], p2, y, max(3, min(28, t["h"])), max(1.2, min(8, t["r"])), i)
        nt += 1
    P.write(os.path.join(campus_dir, "Trees.model.json"))
    stats["trees"] = nt; stats["parts"] += len(P.items); all_parts += P.items

    # lamps + benches along campus walkways
    P = Parts(); occupied = []
    def free(p2, r):
        return inside(campus_ring, p2) and not any(inside(rr, p2) for rr in bld2d[:len(osm["buildings"])]) and \
            all(math.dist(p2, o[:2]) > o[2] + r for o in occupied)
    for t in lid.get("trees", []):
        occupied.append((t["x"], t["z"], 1.0))
    nl = nb = 0
    for p in osm["paths"]:
        k = p["kind"]
        walkk = k in ("footway", "pedestrian", "path"); road = k in ("service", "residential")
        if not walkk and not road: continue
        half = 4.0 if road else (3.0 if k == "pedestrian" else 1.5)
        pts = [enu.en(p["pts"][i], p["pts"][i + 1]) for i in range(0, len(p["pts"]) - 1, 2)]
        dist, next_l, next_b, side = 0.0, 6.0, 14.0, 1
        for j in range(1, len(pts)):
            a, b_ = v(*pts[j - 1]), v(*pts[j])
            L = float(np.linalg.norm(b_ - a))
            if L < 1e-3: continue
            d = (b_ - a) / L; nrm = v(-d[1], d[0])
            s_ = 0.0
            while s_ < L:
                step = min(1.0, L - s_); s_ += step; dist += step
                q = a + d * s_
                if dist >= next_l:
                    next_l += 32 if road else 26
                    lp = tuple(q + nrm * side * (half + 0.8)); side = -side
                    if free(lp, 0.6):
                        lamp(P, lp, terrain.height(*lp)); occupied.append((lp[0], lp[1], 0.6)); nl += 1
                if walkk and dist >= next_b:
                    next_b += 38
                    bp = tuple(q - nrm * side * (half + 0.9))
                    if free(bp, 1.2):
                        bench(P, bp, terrain.height(*bp), unit(q - v(*bp))); occupied.append((bp[0], bp[1], 1.1)); nb += 1
    P.write(os.path.join(campus_dir, "StreetFurniture.model.json"))
    stats["lamps"], stats["benches"] = nl, nb; stats["parts"] += len(P.items); all_parts += P.items

    # spawn at the main entrance + map-edge walls
    P = Parts()
    gate = next((b for b in osm["buildings"] if b.get("name") and "Biola Ave. Entrance" in b["name"]), None)
    c0 = v(*centroid(campus_ring))
    gp = v(*centroid(enu.ring(gate["pts"]))) if gate else c0
    sp = gp + unit(core - gp if gate else v(1, 0)) * 18
    sy = terrain.height(*sp)
    P.box("Spawn", R(sp[0], sy + 0.1, sp[1]), (3, 0.2, 3), v(1, 0, 0), "SmoothPlastic", (1, 1, 1), cls="SpawnLocation", transparency=1, collide=False, shadow=False)
    ex0, ez0 = terrain.x0 + 4, terrain.z0 + 4
    ex1, ez1 = terrain.x0 + terrain.nx * terrain.cell - 4, terrain.z0 + terrain.ny * terrain.cell - 4
    for (a, b_) in (((ex0, ez0), (ex1, ez0)), ((ex1, ez0), (ex1, ez1)), ((ex1, ez1), (ex0, ez1)), ((ex0, ez1), (ex0, ez0))):
        a, b_ = v(*a), v(*b_); m = (a + b_) / 2; L = np.linalg.norm(b_ - a); d = unit(b_ - a)
        P.box("MapEdge", R(m[0], 60, m[1]), (1, 240, L), r2(v(d[1], -d[0])), "SmoothPlastic", (1, 1, 1), transparency=1, shadow=False)
    P.write(os.path.join(campus_dir, "World.model.json"))

    # terrain
    grid, vnx, vnz = rasterize_materials(osm, surr, enu, terrain, campus_ring, bld2d)
    meta = write_terrain_modules(terrain, grid, vnx, vnz, os.path.join(OUT, "ServerStorage", "BiolaTerrain", "Data"))

    stats["terrainVoxels"] = f"{vnx}x{vnz} columns"
    stats["spawn"] = [round(float(sp[0]) * S, 1), round(float(sy) * S, 1), round(float(-sp[1]) * S, 1)]
    print(json.dumps(stats))
    with open(os.path.join(ROOT, "roblox", "build-stats.json"), "w") as f:
        json.dump(stats, f, indent=1)

    if args.preview:
        sys.path.insert(0, os.path.dirname(__file__))
        import preview
        preview.render_all(all_parts, terrain, grid, meta, core=core, spawn=sp, out_dir=os.path.join(ROOT, "roblox", "preview"))


if __name__ == "__main__":
    main()
