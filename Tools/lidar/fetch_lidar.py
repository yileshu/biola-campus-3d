#!/usr/bin/env python3
"""
Build Biola's terrain, building heights/roof shapes and tree positions from USGS 3DEP lidar.

Source: USGS 3D Elevation Program (3DEP) lidar point clouds, public domain, read as Entwine Point
Tiles from the public AWS bucket `usgs-lidar-public` (index: github.com/hobuinc/usgs-lidar).
Only lidar and OpenStreetMap are used — never Google tiles.

Outputs (read by the game):
  Assets/Resources/Terrain/biola_terrain.json   bare-earth height grid (local ENU, metres)
  Assets/Resources/OSM/biola_lidar.json         per-OSM-building height/eave/roof type + trees

Usage:
  pip install "laspy[lazrs]" numpy
  python3 Tools/lidar/fetch_lidar.py [--depth 11] [--cell 2.0] [--margin 120]

Heights: 3DEP is NAVD88 (orthometric). Google Photorealistic 3D Tiles use WGS84 ellipsoidal
heights, so we add the GEOID18 undulation N (from NOAA's API when reachable, else a local
constant) and store everything relative to the CesiumGeoreference origin height.
"""
import argparse
import base64
import concurrent.futures as cf
import datetime as dt
import io
import json
import math
import os
import sys
import urllib.error
import urllib.request

import numpy as np

try:
    import laspy
except ImportError:  # pragma: no cover
    sys.exit('pip install "laspy[lazrs]" numpy')

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OSM_PATH = os.path.join(ROOT, "Assets", "Resources", "OSM", "biola_campus.json")
TERRAIN_OUT = os.path.join(ROOT, "Assets", "Resources", "Terrain", "biola_terrain.json")
LIDAR_OUT = os.path.join(ROOT, "Assets", "Resources", "OSM", "biola_lidar.json")

INDEX_URL = "https://raw.githubusercontent.com/hobuinc/usgs-lidar/master/boundaries/resources.geojson"
PREFERRED = ["CA_LosAngeles_1_B23", "USGS_LPC_CA_LosAngeles_2016_LAS_2018", "CA_OrangeCo_2011"]
NOAA_GEOID = "https://geodesy.noaa.gov/api/geoid/ght?lat={lat}&lon={lon}&model=18"
GEOID_FALLBACK = -35.4   # GEOID18 is about -35.4 m around La Mirada; refined from NOAA when reachable
UA = {"User-Agent": "biola-campus-3d lidar builder"}

R_MERC = 6378137.0
WGS_A = 6378137.0
WGS_F = 1 / 298.257223563
WGS_E2 = WGS_F * (2 - WGS_F)


def http_bytes(url, timeout=120, retries=6):
    import time
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code < 500 and e.code != 429:
                raise
            err = e
        except Exception as e:  # noqa: BLE001  (connection resets through proxies)
            err = e
        time.sleep(min(30, 2 ** attempt))
    raise err


def http_json(url, timeout=60):
    return json.loads(http_bytes(url, timeout))


# ------------------------------------------------------------------------------- geodesy

def merc_from_ll(lat, lon):
    return R_MERC * np.radians(lon), R_MERC * np.log(np.tan(np.pi / 4 + np.radians(lat) / 2))


def ll_from_merc(x, y):
    lon = np.degrees(x / R_MERC)
    lat = np.degrees(2 * np.arctan(np.exp(y / R_MERC)) - np.pi / 2)
    return lat, lon


class Enu:
    """Same transform as Assets/BiolaCampus/Scripts/Geo/GeoMath.cs (EnuFrame)."""

    def __init__(self, lat, lon, h=0.0):
        self.lat, self.lon, self.h = lat, lon, h
        self.x0, self.y0, self.z0 = self.ecef(np.array(lat), np.array(lon), np.array(h))
        la, lo = math.radians(lat), math.radians(lon)
        self.sla, self.cla, self.slo, self.clo = math.sin(la), math.cos(la), math.sin(lo), math.cos(lo)

    @staticmethod
    def ecef(lat, lon, h):
        la, lo = np.radians(lat), np.radians(lon)
        s = np.sin(la)
        n = WGS_A / np.sqrt(1 - WGS_E2 * s * s)
        return (n + h) * np.cos(la) * np.cos(lo), (n + h) * np.cos(la) * np.sin(lo), (n * (1 - WGS_E2) + h) * s

    def to_en(self, lat, lon):
        x, y, z = self.ecef(lat, lon, np.full_like(np.asarray(lat, dtype=float), self.h))
        dx, dy, dz = x - self.x0, y - self.y0, z - self.z0
        e = -self.slo * dx + self.clo * dy
        n = -self.sla * self.clo * dx - self.sla * self.slo * dy + self.cla * dz
        return e, n


def geoid_undulation(lat, lon):
    try:
        d = http_json(NOAA_GEOID.format(lat=lat, lon=lon), timeout=20)
        n = float(d.get("geoidHeight"))
        return n, "NOAA GEOID18 API"
    except Exception as e:  # noqa: BLE001
        print(f"  NOAA geoid API unavailable ({e}); using {GEOID_FALLBACK} m", file=sys.stderr)
        return GEOID_FALLBACK, "constant (NOAA API unreachable)"


# ---------------------------------------------------------------------------------- EPT

def pick_dataset(lat, lon):
    idx = http_json(INDEX_URL)
    hits = {}
    for f in idx["features"]:
        g = f["geometry"]
        polys = g["coordinates"] if g["type"] == "MultiPolygon" else [g["coordinates"]]
        if any(point_in_ring_ll(p[0], lon, lat) for p in polys):
            hits[f["properties"]["name"]] = f["properties"]["url"]
    for name in PREFERRED:
        if name in hits:
            return name, hits[name]
    if hits:
        name = sorted(hits)[0]
        return name, hits[name]
    raise RuntimeError("No USGS EPT dataset covers this location")


def point_in_ring_ll(ring, x, y):
    c = False
    for i in range(len(ring)):
        x1, y1 = ring[i][:2]
        x2, y2 = ring[i - 1][:2]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1 + 1e-15) + x1:
            c = not c
    return c


def select_nodes(base, ept, box, max_depth):
    b = ept["bounds"]
    cache = {}

    def hier(key):
        if key not in cache:
            cache[key] = http_json(base + f"ept-hierarchy/{key}.json")
        return cache[key]

    out = []

    def walk(d, x, y, z, h):
        k = f"{d}-{x}-{y}-{z}"
        if k not in h:
            return
        n = h[k]
        if n == -1:
            h = hier(k)
            n = h[k]
        w = (b[3] - b[0]) / 2 ** d
        x0, y0 = b[0] + x * w, b[1] + y * w
        if x0 + w < box[0] or x0 > box[2] or y0 + w < box[1] or y0 > box[3]:
            return
        if n > 0:
            out.append(k)
        if d < max_depth:
            for dx in (0, 1):
                for dy in (0, 1):
                    for dz in (0, 1):
                        walk(d + 1, 2 * x + dx, 2 * y + dy, 2 * z + dz, h)

    walk(0, 0, 0, 0, hier("0-0-0-0"))
    return out


def read_node(base, key, box):
    data = http_bytes(base + f"ept-data/{key}.laz")
    las = laspy.read(io.BytesIO(data))
    x, y, z = np.asarray(las.x), np.asarray(las.y), np.asarray(las.z)
    cls = np.asarray(las.classification, dtype=np.uint8)
    nret = np.asarray(las.number_of_returns, dtype=np.uint8)
    m = (x >= box[0]) & (x <= box[2]) & (y >= box[1]) & (y <= box[3])
    return x[m], y[m], z[m], cls[m], nret[m]


# ------------------------------------------------------------------------------ rasters

def grid_stat(e, n, v, x0, y0, cell, nx, ny, how):
    ix = np.floor((e - x0) / cell).astype(np.int64)
    iy = np.floor((n - y0) / cell).astype(np.int64)
    ok = (ix >= 0) & (ix < nx) & (iy >= 0) & (iy < ny)
    ix, iy, v = ix[ok], iy[ok], v[ok]
    flat = iy * nx + ix
    out = np.full(nx * ny, np.nan)
    if how == "mean":
        s = np.bincount(flat, weights=v, minlength=nx * ny)
        c = np.bincount(flat, minlength=nx * ny)
        with np.errstate(invalid="ignore", divide="ignore"):
            out = np.where(c > 0, s / np.maximum(c, 1), np.nan)
    elif how == "max":
        out[:] = -np.inf
        np.maximum.at(out, flat, v)
        out[np.isinf(out)] = np.nan
    elif how == "min":
        out[:] = np.inf
        np.minimum.at(out, flat, v)
        out[np.isinf(out)] = np.nan
    return out.reshape(ny, nx)


def fill_holes(g, iterations=400):
    """Fill NaNs (e.g. under buildings) by repeated neighbour averaging (discrete Laplace)."""
    g = g.copy()
    mask = np.isnan(g)
    if mask.all():
        return np.zeros_like(g)
    g[mask] = np.nanmean(g)
    for _ in range(iterations):
        p = np.pad(g, 1, mode="edge")
        avg = (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:]) / 4
        g[mask] = avg[mask]
    return g


def smooth(g, passes=1):
    for _ in range(passes):
        p = np.pad(g, 1, mode="edge")
        g = (4 * g + p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:]) / 8
    return g


def points_in_polygon(px, py, poly):
    x, y = poly[:, 0], poly[:, 1]
    inside = np.zeros(px.shape, dtype=bool)
    j = len(poly) - 1
    for i in range(len(poly)):
        cond = ((y[i] > py) != (y[j] > py)) & (px < (x[j] - x[i]) * (py - y[i]) / (y[j] - y[i] + 1e-12) + x[i])
        inside ^= cond
        j = i
    return inside


def bilinear(g, x0, y0, cell, e, n):
    ny, nx = g.shape
    fx = np.clip((e - x0) / cell - 0.5, 0, nx - 1.001)
    fy = np.clip((n - y0) / cell - 0.5, 0, ny - 1.001)
    ix, iy = fx.astype(int), fy.astype(int)
    tx, ty = fx - ix, fy - iy
    return (g[iy, ix] * (1 - tx) * (1 - ty) + g[iy, ix + 1] * tx * (1 - ty) +
            g[iy + 1, ix] * (1 - tx) * ty + g[iy + 1, ix + 1] * tx * ty)


# ---------------------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--depth", type=int, default=11, help="EPT depth (11 ≈ 0.8 m point spacing here)")
    ap.add_argument("--cell", type=float, default=2.0, help="terrain grid cell size (m)")
    ap.add_argument("--margin", type=float, default=120.0, help="metres beyond the campus bounds")
    ap.add_argument("--workers", type=int, default=4)
    args = ap.parse_args()

    osm = json.load(open(OSM_PATH, encoding="utf-8"))
    meta = osm["meta"]
    lat0, lon0 = meta["centerLat"], meta["centerLon"]
    enu = Enu(lat0, lon0)

    # Campus extent in ENU from the OSM boundary.
    ring = np.array(osm["boundary"][0]["pts"]).reshape(-1, 2)
    be, bn = enu.to_en(ring[:, 0], ring[:, 1])
    e_min, e_max = be.min() - args.margin, be.max() + args.margin
    n_min, n_max = bn.min() - args.margin, bn.max() + args.margin
    corners_lat, corners_lon = [], []
    for e, n in ((e_min, n_min), (e_max, n_max)):
        corners_lat.append(lat0 + n / 110950.0)
        corners_lon.append(lon0 + e / (111320.0 * math.cos(math.radians(lat0))))
    mx0, my0 = merc_from_ll(corners_lat[0] - 0.0005, corners_lon[0] - 0.0005)
    mx1, my1 = merc_from_ll(corners_lat[1] + 0.0005, corners_lon[1] + 0.0005)
    box = (float(mx0), float(my0), float(mx1), float(my1))

    name, url = pick_dataset(lat0, lon0)
    base = url.rsplit("/", 1)[0] + "/"
    print(f"Dataset {name}")
    ept = http_json(base + "ept.json")
    keys = select_nodes(base, ept, box, args.depth)
    print(f"  {len(keys)} EPT nodes to depth {args.depth}")

    xs, ys, zs, cs, rs = [], [], [], [], []
    with cf.ThreadPoolExecutor(args.workers) as pool:
        for i, (x, y, z, c, r) in enumerate(pool.map(lambda k: read_node(base, k, box), keys)):
            xs.append(x); ys.append(y); zs.append(z); cs.append(c); rs.append(r)
            if (i + 1) % 25 == 0:
                print(f"  read {i + 1}/{len(keys)}")
    x = np.concatenate(xs); y = np.concatenate(ys); z = np.concatenate(zs); c = np.concatenate(cs); nret = np.concatenate(rs)
    print(f"  {len(x):,} points in the campus box; classes: {dict(zip(*np.unique(c, return_counts=True)))}")

    lat, lon = ll_from_merc(x, y)
    e, n = enu.to_en(lat, lon)
    del x, y, lat, lon

    # ---------------- terrain (bare earth, class 2)
    cell = args.cell
    nx = int(math.ceil((e_max - e_min) / cell))
    ny = int(math.ceil((n_max - n_min) / cell))
    g = c == 2
    dtm = grid_stat(e[g], n[g], z[g], e_min, n_min, cell, nx, ny, "mean")
    coverage = float(np.mean(~np.isnan(dtm)))
    dtm = smooth(fill_holes(dtm), 1)

    N, n_src = geoid_undulation(lat0, lon0)
    center_ortho = float(bilinear(dtm, e_min, n_min, cell, np.array([0.0]), np.array([0.0]))[0])
    origin_h = center_ortho + N                          # ellipsoidal height of the ground at the origin
    rel = dtm + N - origin_h                             # Unity Y of the ground
    q = np.round(rel * 100).astype(np.int16)             # centimetres
    terrain = {
        "meta": {
            "source": f"USGS 3DEP lidar {name} (Entwine Point Tiles, usgs-lidar-public)",
            "license": "Public domain (U.S. Geological Survey)",
            "generatedUtc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "verticalDatum": "NAVD88 (assumed for 3DEP) + GEOID18 → WGS84 ellipsoid",
            "geoidUndulation": round(N, 3), "geoidSource": n_src,
            "originLat": lat0, "originLon": lon0,
            "originEllipsoidHeight": round(origin_h, 3),
            "originOrthometricHeight": round(center_ortho, 3),
            "cell": cell, "nx": nx, "ny": ny, "x0": round(e_min, 3), "z0": round(n_min, 3),
            "groundCoverage": round(coverage, 3),
            "encoding": "int16 little-endian centimetres relative to the origin, row-major (z rows, x columns), base64",
        },
        "heights": base64.b64encode(q.astype("<i2").tobytes()).decode("ascii"),
    }

    # ---------------- buildings (class 6, or non-ground well above the terrain)
    hag = z - bilinear(dtm, e_min, n_min, cell, e, n)
    is_bld = c == 6
    if is_bld.sum() < 1000:
        # Unclassified survey: roofs are mostly single-return pulses, foliage gives multiple returns.
        is_bld = (c != 2) & (c != 7) & (c != 18) & (hag > 2.5) & (nret == 1)
    results = []
    for bld in osm["buildings"]:
        pts = np.array(bld["pts"]).reshape(-1, 2)
        pe, pn = enu.to_en(pts[:, 0], pts[:, 1])
        poly = np.stack([pe, pn], 1)
        sel = is_bld & (e >= pe.min()) & (e <= pe.max()) & (n >= pn.min()) & (n <= pn.max())
        idx = np.nonzero(sel)[0]
        inside = idx[points_in_polygon(e[idx], n[idx], poly)] if len(idx) else idx
        ground = float(np.median(bilinear(dtm, e_min, n_min, cell, pe, pn)))
        entry = {"id": bld["id"], "name": bld.get("name", ""), "ground": round(ground + N - origin_h, 2), "points": int(len(inside))}
        if len(inside) >= 30:
            hz = hag[inside]
            p20, p50, p95, p99 = np.percentile(hz, [20, 50, 95, 99])
            spread = p95 - p20
            area = abs(0.5 * np.sum(pe * np.roll(pn, 1) - np.roll(pe, 1) * pn))
            # A real pitched roof spreads heights evenly between eave and ridge; a building with flat
            # wings at different heights is bimodal (few points in the middle of the range).
            mid = np.mean((hz > p20 + 0.25 * spread) & (hz < p95 - 0.25 * spread)) if spread > 0 else 0
            pitched = spread > 2.2 and area < 4000 and mid > 0.3 and spread < 9
            entry.update({
                "height": round(float(p95 if pitched else p50 + 0.4), 2),
                "eave": round(float(p20 if pitched else p50), 2),
                "ridge": round(float(p99 if pitched else p95), 2),
                "roof": "pitched" if pitched else "flat",
            })
        results.append(entry)
    measured = sum(1 for r in results if "height" in r)
    print(f"  building heights from lidar: {measured}/{len(results)}")

    # ---------------- trees (canopy height model local maxima)
    veg = ((c >= 3) & (c <= 5)) | ((c == 1) & (hag > 2.5) & (nret >= 2))
    tcell = 1.5
    tnx, tny = int(math.ceil((e_max - e_min) / tcell)), int(math.ceil((n_max - n_min) / tcell))
    chm = grid_stat(e[veg], n[veg], hag[veg], e_min, n_min, tcell, tnx, tny, "max")
    chm = np.nan_to_num(chm, nan=0.0)
    trees = []
    win = 3
    p = np.pad(chm, win, constant_values=0)
    local_max = np.ones_like(chm, dtype=bool)
    for dy in range(-win, win + 1):
        for dx in range(-win, win + 1):
            if dx == 0 and dy == 0:
                continue
            local_max &= chm >= p[win + dy: win + dy + tny, win + dx: win + dx + tnx]
    peaks = np.argwhere(local_max & (chm >= 4.0))
    bpolys = [np.stack(enu.to_en(np.array(b["pts"]).reshape(-1, 2)[:, 0], np.array(b["pts"]).reshape(-1, 2)[:, 1]), 1) for b in osm["buildings"]]
    bring = np.stack([be, bn], 1)

    def near_campus(te, tn, buffer=20.0):
        if points_in_polygon(np.array([te]), np.array([tn]), bring)[0]:
            return True
        a, b2 = bring, np.roll(bring, -1, 0)
        ab = b2 - a
        t = np.clip(((te - a[:, 0]) * ab[:, 0] + (tn - a[:, 1]) * ab[:, 1]) / np.maximum((ab ** 2).sum(1), 1e-9), 0, 1)
        d = np.hypot(a[:, 0] + t * ab[:, 0] - te, a[:, 1] + t * ab[:, 1] - tn)
        return d.min() < buffer

    def in_building(te, tn, pad=1.5):
        for bp in bpolys:
            if bp[:, 0].min() - pad <= te <= bp[:, 0].max() + pad and bp[:, 1].min() - pad <= tn <= bp[:, 1].max() + pad:
                if points_in_polygon(np.array([te]), np.array([tn]), bp)[0]:
                    return True
        return False

    for iy, ix in peaks:
        h = float(chm[iy, ix])
        te, tn = e_min + (ix + 0.5) * tcell, n_min + (iy + 0.5) * tcell
        if not near_campus(te, tn) or in_building(te, tn):
            continue
        radii = []
        for ang in np.linspace(0, 2 * np.pi, 8, endpoint=False):
            r = 0.0
            for step in range(1, 9):
                jx, jy = int(ix + math.cos(ang) * step), int(iy + math.sin(ang) * step)
                if not (0 <= jx < tnx and 0 <= jy < tny) or chm[jy, jx] < 0.5 * h:
                    break
                r = step * tcell
            radii.append(r)
        crown = float(np.clip(np.mean(radii) + 0.75, 1.5, 10.0))
        slender = h >= 10.5 and crown <= 3.2
        hsh = (int(te * 7.13) * 73856093 ^ int(tn * 3.71) * 19349663) & 0xFFFF
        kind = "palm" if slender else ("jacaranda" if (hsh % 100) < 28 and h < 12 else "oak")
        trees.append({"x": round(te, 2), "z": round(tn, 2), "h": round(h, 1), "r": round(crown, 1), "kind": kind})
    trees.sort(key=lambda t: -t["h"])
    trees = trees[:2500]
    print(f"  trees detected: {len(trees)} ({sum(t['kind'] == 'palm' for t in trees)} palms)")

    lidar = {
        "meta": {"source": terrain["meta"]["source"], "license": terrain["meta"]["license"],
                 "generatedUtc": terrain["meta"]["generatedUtc"], "originEllipsoidHeight": round(origin_h, 3),
                 "geoidUndulation": round(N, 3), "buildingsMeasured": measured},
        "buildings": results,
        "trees": trees,
    }

    os.makedirs(os.path.dirname(TERRAIN_OUT), exist_ok=True)
    with open(TERRAIN_OUT, "w", encoding="utf-8") as f:
        json.dump(terrain, f, separators=(",", ":"))
    with open(LIDAR_OUT, "w", encoding="utf-8") as f:
        json.dump(lidar, f, separators=(",", ":"))
    print(f"Wrote {os.path.relpath(TERRAIN_OUT, ROOT)} ({nx}x{ny} @ {cell} m, ground coverage {coverage:.0%}, "
          f"origin ellipsoid height {origin_h:.2f} m) and {os.path.relpath(LIDAR_OUT, ROOT)}")


if __name__ == "__main__":
    main()
