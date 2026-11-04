#!/usr/bin/env python3
"""
Fetch Biola University's campus data from OpenStreetMap (Overpass API) and write the cache the
game loads at runtime.

  Data/osm/biola_overpass_raw.json        raw Overpass response (for reference / reprocessing)
  Assets/Resources/OSM/biola_campus.json  compact file read by CampusMap.cs

Map data © OpenStreetMap contributors, available under the Open Database License (ODbL 1.0).
https://www.openstreetmap.org/copyright

Usage:
  python3 Tools/osm/fetch_osm.py                 # fetch from Overpass and write both files
  python3 Tools/osm/fetch_osm.py --if-placeholder  # only fetch when the committed cache is a placeholder
  python3 Tools/osm/fetch_osm.py --from-raw FILE # reprocess an existing raw Overpass JSON
  python3 Tools/osm/fetch_osm.py --placeholder   # write a clearly-marked placeholder (no network)

Standard library only. The campus is located with: nwr[amenity=university][name~"Biola",i]
"""
import argparse
import datetime as dt
import json
import math
import os
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW_PATH = os.path.join(ROOT, "Data", "osm", "biola_overpass_raw.json")
OUT_PATH = os.path.join(ROOT, "Assets", "Resources", "OSM", "biola_campus.json")

ENDPOINTS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
USER_AGENT = "biola-campus-3d/1.0 (OSM cache builder; github.com)"
ATTRIBUTION = "© OpenStreetMap contributors"
LICENSE = "ODbL 1.0 — https://www.openstreetmap.org/copyright"

# Approximate ellipsoidal height of the ground at the campus (≈55 m above sea level plus a geoid
# undulation of ≈ -35 m). Only used to place the georeference; the player spawns via raycast.
DEFAULT_CENTER_HEIGHT = 20.0

# NOT from OSM: only used for the placeholder file when Overpass could not be reached.
PLACEHOLDER_CENTER = (33.9058, -118.0148)

QUERY = """
[out:json][timeout:120];
nwr["amenity"="university"]["name"~"Biola",i]->.uni;
.uni out tags center geom;
.uni map_to_area->.campus;
(
  nwr["building"](area.campus);
  way["highway"](area.campus);
  nwr["leisure"](area.campus);
  way["landuse"](area.campus);
  way["natural"](area.campus);
  way["amenity"](area.campus);
  node["name"](area.campus);
);
out tags geom qt;
"""

WALKWAY = {"footway", "path", "pedestrian", "steps", "cycleway", "living_street", "track", "corridor"}
ROADS = {"service", "residential", "unclassified", "tertiary", "secondary", "primary", "trunk"}


# ----------------------------------------------------------------------------------- fetching

def fetch_overpass(query):
    data = urllib.parse.urlencode({"data": query}).encode()
    last = None
    for url in ENDPOINTS:
        for attempt in range(3):
            try:
                req = urllib.request.Request(url, data=data, headers={"User-Agent": USER_AGENT})
                with urllib.request.urlopen(req, timeout=180) as r:
                    payload = json.loads(r.read().decode("utf-8"))
                if "elements" not in payload:
                    raise RuntimeError("unexpected response")
                return payload, url
            except Exception as e:  # noqa: BLE001
                last = e
                print(f"  {url} attempt {attempt + 1} failed: {e}", file=sys.stderr)
                time.sleep(2 ** attempt * 2)
    raise RuntimeError(f"All Overpass endpoints failed: {last}")


# --------------------------------------------------------------------------------- geometry

def geom_to_flat(geom):
    out = []
    for p in geom or []:
        if p is None:
            continue
        out += [round(p["lat"], 7), round(p["lon"], 7)]
    return out


def is_closed(geom):
    return len(geom) > 3 and geom[0]["lat"] == geom[-1]["lat"] and geom[0]["lon"] == geom[-1]["lon"]


def stitch_rings(segments):
    """Join way geometries (lists of {lat,lon}) end-to-end into closed rings."""
    segs = [list(s) for s in segments if s and len(s) >= 2]
    rings = []
    key = lambda p: (round(p["lat"], 7), round(p["lon"], 7))  # noqa: E731
    while segs:
        ring = segs.pop(0)
        changed = True
        while key(ring[0]) != key(ring[-1]) and changed:
            changed = False
            for i, s in enumerate(segs):
                if key(s[0]) == key(ring[-1]):
                    ring += s[1:]
                elif key(s[-1]) == key(ring[-1]):
                    ring += list(reversed(s))[1:]
                elif key(s[-1]) == key(ring[0]):
                    ring = s[:-1] + ring
                elif key(s[0]) == key(ring[0]):
                    ring = list(reversed(s))[:-1] + ring
                else:
                    continue
                segs.pop(i)
                changed = True
                break
        if len(ring) >= 4:
            rings.append(ring)
    return rings


def element_rings(el, role_filter=("outer", "")):
    """Outer rings of a way (closed) or multipolygon relation."""
    if el["type"] == "way":
        g = el.get("geometry") or []
        return [g] if len(g) >= 4 else []
    if el["type"] == "relation":
        parts = [m.get("geometry") for m in el.get("members", [])
                 if m.get("type") == "way" and m.get("role", "") in role_filter and m.get("geometry")]
        return stitch_rings(parts)
    return []


def ring_area_centroid(ring):
    """Planar (equirectangular, metres) signed area + centroid of a lat/lon ring."""
    lat0 = sum(p["lat"] for p in ring) / len(ring)
    k = math.cos(math.radians(lat0))
    pts = [(p["lon"] * k * 111320.0, p["lat"] * 110540.0) for p in ring]
    a = cx = cy = 0.0
    for (x1, y1), (x2, y2) in zip(pts, pts[1:] + pts[:1]):
        c = x1 * y2 - x2 * y1
        a += c
        cx += (x1 + x2) * c
        cy += (y1 + y2) * c
    a *= 0.5
    if abs(a) < 1e-9:
        return 0.0, (lat0, sum(p["lon"] for p in ring) / len(ring))
    cx /= 6 * a
    cy /= 6 * a
    return abs(a), (cy / 110540.0, cx / (k * 111320.0))


def parse_float(v):
    if v is None:
        return 0.0
    try:
        return float(str(v).split(";")[0].replace("m", "").strip())
    except ValueError:
        return 0.0


# ------------------------------------------------------------------------------- processing

def process(raw, source):
    els = raw.get("elements", [])
    unis = [e for e in els if e.get("tags", {}).get("amenity") == "university"
            and "biola" in e.get("tags", {}).get("name", "").lower()]
    if not unis:
        raise RuntimeError('No element with amenity=university and name~"Biola" in the response')

    # Prefer the largest polygon named exactly "Biola University".
    def score(e):
        rings = element_rings(e)
        area = sum(ring_area_centroid(r)[0] for r in rings)
        exact = e.get("tags", {}).get("name", "").strip().lower() == "biola university"
        return (exact, area)
    uni = max(unis, key=score)
    rings = element_rings(uni)

    if rings:
        best = max(rings, key=lambda r: ring_area_centroid(r)[0])
        area, (clat, clon) = ring_area_centroid(best)
    elif "center" in uni:
        area, clat, clon = 0.0, uni["center"]["lat"], uni["center"]["lon"]
    else:
        area, clat, clon = 0.0, uni["lat"], uni["lon"]

    out = {
        "meta": {
            "source": source,
            "attribution": ATTRIBUTION,
            "license": LICENSE,
            "fetchedUtc": raw.get("osm3s", {}).get("timestamp_osm_base") or dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "placeholder": False,
            "placeholderReason": "",
            "campusName": uni.get("tags", {}).get("name", "Biola University"),
            "campusOsmId": f'{uni["type"]}/{uni["id"]}',
            "centerLat": round(clat, 7),
            "centerLon": round(clon, 7),
            "centerHeight": DEFAULT_CENTER_HEIGHT,
            "campusAreaM2": round(area),
        },
        "boundary": [{"pts": geom_to_flat(r)} for r in rings],
        "buildings": [], "paths": [], "areas": [], "pois": [],
    }

    seen = {f'{uni["type"]}/{uni["id"]}'}
    for e in els:
        ident = f'{e["type"]}/{e["id"]}'
        if ident in seen:
            continue
        seen.add(ident)
        t = e.get("tags", {})
        name = t.get("name") or t.get("official_name") or t.get("alt_name") or ""

        if "building" in t and e["type"] in ("way", "relation"):
            for r in element_rings(e):
                out["buildings"].append({
                    "id": ident, "name": name, "kind": t.get("building", "yes"),
                    "levels": parse_float(t.get("building:levels")),
                    "height": parse_float(t.get("height")),
                    "pts": geom_to_flat(r),
                })
        elif "highway" in t and e["type"] == "way":
            kind = t["highway"]
            if kind in WALKWAY or kind in ROADS:
                out["paths"].append({"id": ident, "kind": kind, "name": name, "pts": geom_to_flat(e.get("geometry"))})
        elif e["type"] in ("way", "relation") and any(k in t for k in ("leisure", "landuse", "natural", "amenity")):
            kind = t.get("leisure") or t.get("landuse") or t.get("natural") or t.get("amenity")
            for r in element_rings(e):
                out["areas"].append({"id": ident, "kind": kind, "name": name, "pts": geom_to_flat(r)})
        if e["type"] == "node" and name:
            kind = t.get("amenity") or t.get("leisure") or t.get("tourism") or t.get("historic") or t.get("building") or "poi"
            out["pois"].append({"id": ident, "name": name, "kind": kind, "lat": e["lat"], "lon": e["lon"]})

    out["buildings"].sort(key=lambda b: (b["name"] == "", b["name"]))
    return out


def placeholder(reason):
    lat, lon = PLACEHOLDER_CENTER
    # ~420 m radius octagon so the boundary wall/minimap still work; clearly flagged as fake.
    ring = []
    for i in range(9):
        a = 2 * math.pi * (i % 8) / 8
        ring += [round(lat + 420 * math.sin(a) / 110540.0, 7), round(lon + 420 * math.cos(a) / (111320.0 * math.cos(math.radians(lat))), 7)]
    return {
        "meta": {
            "source": "placeholder (not OpenStreetMap data)",
            "attribution": ATTRIBUTION, "license": LICENSE,
            "fetchedUtc": "", "placeholder": True, "placeholderReason": reason,
            "campusName": "Biola University", "campusOsmId": "",
            "centerLat": lat, "centerLon": lon, "centerHeight": DEFAULT_CENTER_HEIGHT, "campusAreaM2": 0,
        },
        "boundary": [{"pts": ring}], "buildings": [], "paths": [], "areas": [], "pois": [],
    }


def write_json(path, obj, compact):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        if compact:
            json.dump(obj, f, ensure_ascii=False, separators=(",", ":"))
        else:
            json.dump(obj, f, ensure_ascii=False, indent=1)
        f.write("\n")


def current_is_placeholder():
    try:
        with open(OUT_PATH, encoding="utf-8") as f:
            return bool(json.load(f)["meta"].get("placeholder", True))
    except Exception:  # noqa: BLE001
        return True


def summary(out):
    m = out["meta"]
    named = sum(1 for b in out["buildings"] if b["name"])
    print(f'{m["campusName"]} ({m["campusOsmId"] or "placeholder"}) centre {m["centerLat"]}, {m["centerLon"]}')
    print(f'  boundary rings: {len(out["boundary"])}, buildings: {len(out["buildings"])} ({named} named), '
          f'paths: {len(out["paths"])}, areas: {len(out["areas"])}, pois: {len(out["pois"])}')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--from-raw", help="process an existing Overpass JSON file instead of fetching")
    ap.add_argument("--placeholder", action="store_true", help="write a placeholder file (no network)")
    ap.add_argument("--if-placeholder", action="store_true", help="only fetch if the current cache is a placeholder")
    ap.add_argument("--reason", default="Overpass API not reachable when the cache was generated")
    args = ap.parse_args()

    if args.placeholder:
        out = placeholder(args.reason)
        write_json(OUT_PATH, out, compact=False)
        summary(out)
        return 0

    if args.if_placeholder and not current_is_placeholder():
        print("OSM cache already contains real data; nothing to do.")
        return 0

    if args.from_raw:
        with open(args.from_raw, encoding="utf-8") as f:
            raw = json.load(f)
        source = "Overpass API (reprocessed from " + os.path.basename(args.from_raw) + ")"
    else:
        print("Querying Overpass API…")
        raw, url = fetch_overpass(QUERY)
        source = "Overpass API " + url
        write_json(RAW_PATH, raw, compact=True)

    out = process(raw, source)
    write_json(OUT_PATH, out, compact=True)
    summary(out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
