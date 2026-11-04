# Biola Campus 3D

An unofficial, fan-made, walkable 3D recreation of **Biola University** (La Mirada, California) that
runs in the browser. Built with **Unity 6**, **Cesium for Unity** and **Google Photorealistic 3D Tiles**,
with building names, a minimap and the campus boundary from **OpenStreetMap**.

* **Hybrid world:** on campus you walk through our own models (lidar terrain, every OSM building modelled
  procedurally, 22 detailed "hero" buildings, ~900 lidar-detected trees, lamps, benches). Beyond the campus
  boundary the Google Photorealistic 3D Tiles take over (they are clipped away inside the boundary).
* URP rendering: real sun for La Mirada, soft shadows, SSAO (High), bloom, ACES tonemapping, colour
  grading, distance fog; **N** toggles day/night with working street lamps and lit windows
* Quality presets Low / Medium / High (Web defaults to Medium, the Windows build to High)
* First-person walking (WASD + mouse, sprint, jump, gravity) and a drone mode (**F**)
* Building names when you look at or walk up to a named building, plus floating labels
* Minimap drawn from OpenStreetMap (**M** enlarges it); invisible wall along the campus boundary
* Title screen with Start, Controls, Settings and Credits; on-screen renderer diagnostics (**F3**)

> Not affiliated with or endorsed by Biola University or Google.

---

## Contents

1. [Status and known gaps](#1-status-and-known-gaps)
2. [Repository secrets (GitHub Actions)](#2-repository-secrets-github-actions)
3. [Open the project in Unity locally](#3-open-the-project-in-unity-locally)
4. [OpenStreetMap data](#4-openstreetmap-data)
   · [How the campus is built (lidar, hero buildings, clipping)](#how-the-campus-is-built)
   · [CI screenshots and diagnostics](#ci-screenshots-and-diagnostics)
5. [Deploy the Web build](#5-deploy-the-web-build)
6. [**Restrict your API key once it is hosted**](#6-restrict-your-api-key-once-it-is-hosted)
7. [Web build settings](#7-web-build-settings)
8. [Controls](#8-controls)
9. [Google Map Tiles terms and attribution](#9-google-map-tiles-terms-and-attribution)
10. [Project layout](#10-project-layout)
11. [Troubleshooting](#11-troubleshooting)
12. [Credits and licenses](#12-credits-and-licenses)

---

## 1. Status and known gaps

| Item | State |
|---|---|
| Build | The WebGL build compiles and deploys from CI (GameCI, Unity 6000.3.28f1); a Windows build runs in the same workflow. |
| Google 3D Tiles | Verified streaming from the GitHub Pages site (first tile ≈ 4 s; key, referrer, CORS and quota OK). |
| OSM data | Real data from Overpass (`way/126445769`, 53 buildings, 104 paths). Refresh with the *Refresh OSM cache* workflow. |
| Lidar | USGS 3DEP `CA_LosAngeles_1_B23` (public domain). Terrain, building heights and trees are committed; refresh with *Refresh lidar terrain*. The geoid offset (NAVD88 → ellipsoid) used a −35.4 m constant because NOAA's API was unreachable when the files were generated; the CI refresh uses NOAA's GEOID18 value. |
| Facade colours | Chosen from written descriptions of the campus, not photos (photos could not be viewed from the authoring environment). Edit `ProceduralTextures.Styles` / `HeroCampus.StyleFor` to refine. |
| iPhone / iPad | Not supported yet (Cesium's experimental Web support fails to start in WebKit); the page says so. Desktop Chrome/Edge/Firefox work. |
| Google logo | A text wordmark is shown. Put the official logo PNG at `Assets/Resources/Attribution/google_logo.png`; it is picked up automatically. |
| Frame rate | Not measurable in CI (headless Chrome renders on the CPU at 1–5 fps). Check the FPS readout (**F3**) on your laptop; switch to Low if Medium is below 30 fps. |

---

## 2. Repository secrets (GitHub Actions)

The workflow `.github/workflows/build-webgl.yml` builds the Web player with
[GameCI](https://game.ci/) (`game-ci/unity-builder@v4`, Unity **6000.3.28f1**).

In your GitHub repo go to **Settings → Secrets and variables → Actions → New repository secret** and add:

| Secret | Value |
|---|---|
| `UNITY_LICENSE` | Contents of your Unity license file (`.ulf`). For a Personal license, follow GameCI's *Activation* guide (<https://game.ci/docs/github/activation>). |
| `UNITY_EMAIL` | Email address of your Unity account |
| `UNITY_PASSWORD` | Password of your Unity account |
| `GOOGLE_MAPS_API_KEY` | Your Google Maps Platform key with the **Map Tiles API** enabled |

The build writes `Assets/Config/secrets.json` from `GOOGLE_MAPS_API_KEY` at build time and deletes it
afterwards. If the secret is missing the build still succeeds, but the game runs in offline OSM mode.

Optional: add a **repository variable** (same page, *Variables* tab) `DEPLOY_PAGES` = `true` to deploy to
GitHub Pages automatically on every push to `main` (see §5).

**Secret hygiene**

* `Assets/Config/secrets.json`, its `.meta` file, `.env` and `Assets/Resources/Generated/` are in `.gitignore`.
* `Tools/git/check-secrets.sh` fails if anything that looks like a Google API key is tracked or staged.
  CI runs it first. Install it as a pre-commit hook with `bash Tools/git/install-hooks.sh`.
* Only `secrets.example.json` and `.env.example` (placeholders) are committed.

---

## 3. Open the project in Unity locally

1. Install **Unity Hub**, then Unity **6000.3.28f1** (or a newer 6000.3 LTS) with the **Web Build Support** module.
2. Clone the repo and create your secrets file:
   ```bash
   cp Assets/Config/secrets.example.json Assets/Config/secrets.json
   ```
   Put your key in `secrets.json` (`"googleMapsApiKey": "…"`). Optionally `cp .env.example .env` as well.
3. **Unity Hub → Add → Add project from disk** → select the repo folder. On first open Unity downloads
   Cesium for Unity 1.26.1 from `https://unity.pkg.cesium.com` (scoped registry in `Packages/manifest.json`)
   plus its dependencies.
   * If Unity asks to enable the new Input System backend, choose **Yes** (the game supports both backends).
4. The first open runs **Biola ▸ Setup Project** automatically. It creates `Assets/Scenes/Main.unity`
   (just a camera and the `GameBootstrap` object), materials under `Assets/Resources/BiolaMaterials`,
   and the Web player settings. You can re-run it from the **Biola** menu at any time, and you can commit
   the generated scene and materials.
5. Open `Assets/Scenes/Main.unity` and press **Play**. Click the Game view to capture the mouse.
6. Optional: **Biola ▸ Preview Google 3D Tiles in Scene View** streams the tiles while editing. The preview
   objects are never saved, so the key never ends up in a scene.
7. To build locally: **File ▸ Build Profiles ▸ Web ▸ Switch Platform**, then **Biola ▸ Build Web Player (WebGL 2)**
   (output in `Builds/WebGL`). Test it with **Build And Run**, or any HTTPS/localhost static server.

Everything (georeference, tileset, player, UI) is built at runtime by
`Assets/BiolaCampus/Scripts/Core/GameBootstrap.cs`, so no prefabs or hand-made scenes are needed.

---

## 4. OpenStreetMap data

`Tools/osm/fetch_osm.py` (Python 3, standard library only) runs this Overpass query:

* the campus: `nwr["amenity"="university"]["name"~"Biola",i]`, using the largest polygon named "Biola University";
* everything inside the campus area: buildings, highways and footways, leisure/landuse/natural/amenity areas, named nodes.

It writes:

* `Data/osm/biola_overpass_raw.json` — the raw Overpass response (not loaded by the game)
* `Assets/Resources/OSM/biola_campus.json` — the compact file the game reads (boundary rings, building
  footprints with names and levels/height, paths, areas, POIs, and the campus centroid used as the
  CesiumGeoreference origin)

```bash
python3 Tools/osm/fetch_osm.py
```

```bash
python3 Tools/osm/fetch_osm.py --from-raw Data/osm/biola_overpass_raw.json
```

The second command reprocesses an existing raw file without re-downloading. The **Refresh OSM cache**
workflow runs the same script monthly (and on demand) and commits the result. Pushes made by that
workflow do not trigger a new build, so run *Build Web (WebGL) player* manually afterwards.

Map data © OpenStreetMap contributors, available under the Open Database License
(<https://www.openstreetmap.org/copyright>). The credit appears on the minimap, in the attribution bar
and on the Credits screen.

---

## How the campus is built

Everything inside the campus boundary is our own geometry, generated at start-up from open data:

| Layer | Source | Code |
|---|---|---|
| Terrain (2 m grid, bare earth) | USGS 3DEP lidar, ground-classified points | `Tools/lidar/fetch_lidar.py` → `GroundBuilder` |
| Building heights, eave/ridge, flat vs pitched roof | USGS 3DEP lidar points inside each OSM footprint | `fetch_lidar.py` → `BuildingBuilder` |
| Footprints, names, walkways, roads, parking, fields, lawns, pools | OpenStreetMap (ODbL) | `CampusMap`, `GroundBuilder` |
| Trees (position, height, crown, palm vs broadleaf) | Canopy-height-model peaks from lidar (multi-return pulses) | `Vegetation` |
| Facades, roofs, ground surfaces | Procedural textures (window grids, mullions, floor bands, clay tile, pavers…) | `ProceduralTextures` |
| Lamps, benches, trash cans, bike racks | Procedural placement along OSM walkways / near entrances | `Vegetation` |

* **Hero buildings:** the 22 named buildings that score highest on size and closeness to Metzger Lawn get
  entrances (glass doors, canopy, columns) facing the nearest walkway and rooftop units; the Library gets its
  glowing roof beacon. All other footprints use the same generator with less detail.
* **Clipping:** a `CesiumPolygonRasterOverlay` (material key `Clipping`) with the campus boundary (+3 m)
  hides the Google tiles inside campus and skips tiles entirely inside it. Google tiles are only streamed
  and displayed; nothing is read back from them, cached to disk or used to build our models.
* **Heights:** 3DEP heights are NAVD88; Google tiles use WGS84 ellipsoid heights. The georeference origin
  is placed on the lidar ground at the campus centre (orthometric + GEOID18 undulation).

## CI screenshots and diagnostics

* The status line at the bottom-left always says what is rendering: `Google Photorealistic 3D Tiles:
  streaming` or `FALLBACK: offline OSM boxes — <cause>` (no key, HTTP 403 key/referrer, 429 quota,
  network/CORS). **F3** adds FPS, page origin, quality and GPU. The same lines are logged as `[BiolaDiag]`.
* `?shot=all` runs a fixed camera tour (main entrance, central quad, between hero buildings, drone view).
  Extra parameters: `quality=low|medium|high`, `night=1`, `time=HH:MM`, `date=YYYY-MM-DD`.
* After each deploy, the **Screenshots (headless Chrome)** job opens the live site with Playwright, saves
  the four views and the console log as the `screenshots-<run>` artifact, and publishes them to the
  `ci-screenshots` branch (`latest/` and `runs/<run>-<sha>/`).

## 5. Deploy the Web build

Every workflow run uploads the player as the artifact **BiolaCampus3D-WebGL** (a folder with `index.html`,
`Build/` and `coi-serviceworker.js`).

### Option A — GitHub Pages (recommended)

1. **Settings → Pages → Build and deployment → Source: GitHub Actions.**
2. Either run **Actions → Build Web (WebGL) player → Run workflow** with **deploy** ticked, or set the repo
   variable `DEPLOY_PAGES=true` to deploy on every push to `main`.
3. The site appears at `https://<user>.github.io/<repo>/`.

GitHub Pages cannot send custom headers, and it does not send `Content-Encoding: br`:

* **Brotli:** the build uses *decompression fallback*, so the loader decompresses in JavaScript.
* **Multithreading** needs a cross-origin-isolated page (COOP/COEP headers). The bundled
  `coi-serviceworker.js` adds those headers. The page reloads itself once on the first visit; that is expected.

### Option B — Google Cloud Storage bucket (or S3/R2/Azure)

```bash
gsutil mb -l us-west2 gs://YOUR_BUCKET
```

```bash
gsutil -m rsync -r path/to/BiolaCampus3D gs://YOUR_BUCKET
```

```bash
gsutil iam ch allUsers:objectViewer gs://YOUR_BUCKET
```

```bash
gsutil web set -m index.html gs://YOUR_BUCKET
```

Serve it over **HTTPS** (for example a load balancer with a managed certificate, or Firebase Hosting /
Cloudflare in front). A secure context is required. Buckets cannot add COOP/COEP response headers, so the
bundled service worker handles cross-origin isolation, just as on GitHub Pages.

### Option C — hosts with custom headers (Netlify, Cloudflare Pages, nginx)

Send these headers for the best performance (the service worker then does nothing):

```
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```

If you also want the server to send `Content-Encoding: br` for files ending in `.br` or `.unityweb`, keep
the correct `Content-Type` (`application/wasm` for the wasm, `application/javascript` for the js files).

---

### Windows desktop build

The workflow also builds **BiolaCampus3D-Windows** (artifact): download it from the run page, unzip and start
`BiolaCampus3D.exe`. Desktop defaults to the **High** preset (SSAO, 4-cascade shadows, SMAA). The key is
inside this build too, and a website restriction does not apply to desktop apps, so share it only with
people you trust, or create a second key restricted to the Map Tiles API for it.

## 6. Restrict your API key once it is hosted

> ⚠️ A Web game must send the Map Tiles key to the browser, so anyone can see it in the built files.
> Once the game is hosted, restrict the key so it only works from your site.

Google Cloud Console → **APIs & Services → Credentials →** your key:

1. **Application restrictions → Websites** → add your site(s), e.g.
   * `https://<user>.github.io/*`
   * `https://your-domain.example/*`
   * `http://localhost/*` (only while testing locally; remove later)
2. **API restrictions → Restrict key → Map Tiles API** only (already the case for this key).
3. Optionally set a daily quota on the Map Tiles API to cap costs.

When Google rejects the key (HTTP 403, e.g. wrong referrer), the game says so and switches to the offline OSM campus.

---

## 7. Web build settings

Applied in code by `BiolaProjectSetup.ApplyWebPlayerSettings` (menu **Biola ▸ Setup Project**, and on every CI build):

| Setting | Value | Why |
|---|---|---|
| Platform | Web (`BuildTarget.WebGL`), Unity 6000.3 LTS | Cesium for Unity ≥ 1.20 supports Web builds on Unity 6 |
| Native C/C++ multithreading | **On** (`PlayerSettings.WebGL.threadsSupport`) | Cesium decodes tiles on worker threads |
| Memory | initial 512 MB, **maximum 4096 MB**, geometric growth | Tile streaming needs much more than Unity's default heap |
| Graphics API | **WebGL 2** by default; **WebGPU** optional (with WebGL 2 fallback) | Workflow input `webgpu`, or menu **Biola ▸ Web Graphics** |
| Compression | Brotli + decompression fallback | Small download on any static host |
| Code | IL2CPP, *Optimize Size*, engine code stripping, managed stripping **Low** | Small but safe for Cesium's interop |
| Data caching | On | Caches the **Unity build files** only, never map tiles |
| Template | `Assets/WebGLTemplates/Biola` | Loading screen, cross-origin isolation check, service worker |

The download holds only code and UI. All imagery and geometry streams from Google at runtime, and the
OSM cache is a small JSON file.

---

## 8. Controls

| Key | Action |
|---|---|
| W A S D / arrows | Move |
| Mouse | Look (click the view to capture the mouse) |
| Shift | Sprint (fast in drone mode) |
| Space | Jump (climb in drone mode) |
| F | Toggle drone / fly mode |
| E / Q (or Ctrl, C) | Drone up / down |
| Mouse wheel | Drone cruise speed |
| M or Tab | Enlarge / shrink the minimap |
| N | Day / night |
| F3 | Renderer diagnostics |
| Esc | Release the mouse, open the pause menu |

---

## 9. Google Map Tiles terms and attribution

* **Stream only.** Tiles are fetched at runtime by `Cesium3DTileset` (URL source
  `https://tile.googleapis.com/v1/3dtiles/root.json`) and kept only in Cesium's in-memory cache. Nothing is
  cached to disk, exported, baked or committed. The OSM extrusion fallback is built only from OSM data.
* **Attribution is always on screen.** Cesium's credit system shows the per-tile data providers
  (`showCreditsOnScreen = true`), and an attribution bar ("Google" + "© OpenStreetMap contributors")
  stays visible on every screen. The Credits page lists everything.
* No Google Street View imagery is used, and Google Maps is not scraped.
* Terms: <https://cloud.google.com/maps-platform/terms> and the Map Tiles API policies page.

---

## 10. Project layout

```
Assets/
  BiolaCampus/Scripts/
    Core/      GameBootstrap (runtime scene builder), SecretsLoader, GameSettings, InputShim
    Geo/       WGS84 → local ENU math, polygon utilities (point-in-polygon, ear clipping)
    Osm/       CampusMap: loads + projects the OSM cache, building lookups, spawn point
    World/     CampusWorld (CesiumGeoreference + Google tileset), SunController, RenderingSetup (URP),
               BoundaryWall, OfflineCampus
    World/Campus/  HeroCampus, GroundBuilder, BuildingBuilder, Vegetation, ProceduralTextures,
                   CampusMaterials, TerrainField (lidar), GoogleClipping
    Player/    FirstPersonController (walk + drone)
    UI/        HUD, minimap, menus, attribution bar, uGUI factory
  BiolaCampus/Editor/   project setup, CI build method, secrets build processor, Scene view preview
  Config/secrets.example.json      (secrets.json is git-ignored)
  Resources/OSM/biola_campus.json  OSM cache read at runtime
  Resources/OSM/biola_lidar.json   lidar building heights / roof types / trees
  Resources/Terrain/biola_terrain.json  lidar terrain grid
  WebGLTemplates/Biola/            page template + coi-serviceworker.js
Data/osm/                          raw Overpass response (written by the fetch script)
Tools/osm/fetch_osm.py             Overpass → cache
Tools/lidar/fetch_lidar.py         USGS 3DEP lidar → terrain, building heights, trees
Tools/capture/capture.mjs          Playwright screenshot tour (CI)
Tools/git/                         secret scan + pre-commit hook installer
.github/workflows/                 build-webgl.yml (WebGL + Windows + Pages + screenshots),
                                   refresh-osm.yml, refresh-lidar.yml
```

---

## 11. Troubleshooting

* **"No Google Map Tiles API key found"**: create `Assets/Config/secrets.json` locally, or set the
  `GOOGLE_MAPS_API_KEY` secret for CI.
* **"key was rejected" / HTTP 403**: enable the Map Tiles API for the key's project and check that the
  website restriction matches the URL you are using (including `http://localhost/*` while testing).
* **"page is not cross-origin isolated"**: serve over HTTPS (or `localhost`); let the page reload once for
  the service worker; on custom hosts send the COOP/COEP headers from §5.
* **Stuck on "Streaming … tiles"**: after 45 s without ground the game switches to the offline campus.
  Check the browser console for network errors.
* **GameCI fails with "No space left on device"**: the workflow already frees runner disk; re-run the job,
  or delete the Library cache under *Actions → Caches*.
* **Package resolution fails**: make sure `https://unity.pkg.cesium.com` is reachable from your network.

---

## 12. Credits and licenses

* 3D imagery: **Google Photorealistic 3D Tiles** via Google Maps Platform (© Google and data providers)
* Map data: **© OpenStreetMap contributors**, ODbL 1.0
* Terrain, building heights and trees: **U.S. Geological Survey 3D Elevation Program** lidar (public domain)
* **Cesium for Unity** © Cesium GS, Inc. — Apache License 2.0
* **coi-serviceworker** © Guido Zuidhof — MIT (`Assets/WebGLTemplates/Biola/coi-serviceworker.js`)
* Built with **Unity**
