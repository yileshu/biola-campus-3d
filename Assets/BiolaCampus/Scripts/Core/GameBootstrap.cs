using BiolaCampus.Osm;
using BiolaCampus.Player;
using BiolaCampus.UI;
using BiolaCampus.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BiolaCampus.Core
{
    /// <summary>
    /// Single entry point placed in Assets/Scenes/Main.unity. Builds the whole game at runtime:
    /// georeference + Google 3D Tiles, OSM boundary wall, player, HUD, minimap, menus.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        enum State { Title, Spawning, Playing, Paused, Tour }

        State _state = State.Title;
        CampusMap _map;
        CampusWorld _world;
        Camera _camera;
        FirstPersonController _player;
        HudController _hud;
        MinimapController _minimap;
        MenuController _menu;
        float _orbitAngle;
        float _spawnTimer, _groundFoundAt;
        Vector2 _spawnXZ;
        float _groundY;
        bool _hadPointerLock;
        float _lockedFor;

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = -1;

            _map = CampusMap.Load();
            _groundY = 0f; // the georeference origin height approximates ground level at the campus centre

            SetupLighting();
            SetupCamera();

            var worldGo = new GameObject("World");
            _world = worldGo.AddComponent<CampusWorld>();
            _world.Initialize(_map);
            _world.WentOffline += reason => { _menu?.SetStatus(reason); _hud?.Toast(reason, 8f); };

            if (_map.HasBoundary)
            {
                var wall = new GameObject("Campus Boundary Wall (OSM)").AddComponent<BoundaryWall>();
                wall.Build(_map);
            }

            BuildPlayer();
            BuildUI();
            UpdateMenuStatus();
            MaybeStartScreenshotTour();
        }

        /// <summary>CI screenshot mode: <c>?shot=all</c> skips the title and runs the fixed camera tour.</summary>
        void MaybeStartScreenshotTour()
        {
            string shot = ScreenshotTour.RequestedShot;
            if (string.IsNullOrEmpty(shot)) return;
            string q = ScreenshotTour.Query("quality");
            if (!string.IsNullOrEmpty(q) && System.Enum.TryParse(q, true, out TileQuality tq)) GameSettings.Quality = tq;
            _menu.Close();
            _hud.SetVisible(false);
            _minimap.SetVisible(false);
            _state = State.Tour;
            gameObject.AddComponent<ScreenshotTour>().Begin(_camera, _world, _map, shot.ToLowerInvariant());
        }

        // ----------------------------------------------------------------------------- setup

        void SetupLighting()
        {
            new GameObject("Sun & Sky").AddComponent<SunController>().Initialize();
            RenderSettings.fog = false;
        }

        void SetupCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.55f, 0.72f, 0.92f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20000f;
            _camera.fieldOfView = 60f;
            UpdateTitleOrbit(0f);
        }

        void BuildPlayer()
        {
            var go = new GameObject("Player") { layer = 2 }; // Ignore Raycast: look/ground rays skip the player
            go.AddComponent<CharacterController>();
            _player = go.AddComponent<FirstPersonController>();
            _player.Map = _map;
            _player.enabled = false;
            go.SetActive(false);
        }

        void BuildUI()
        {
            UIFactory.EnsureEventSystem();

            var hudCanvas = UIFactory.Canvas("HUD Canvas", 0);
            _hud = hudCanvas.gameObject.AddComponent<HudController>();
            _hud.Build(hudCanvas, _map);

            var mapCanvas = UIFactory.Canvas("Minimap Canvas", 5);
            _minimap = mapCanvas.gameObject.AddComponent<MinimapController>();
            _minimap.Build(mapCanvas, _map);
            _minimap.SetVisible(false);

            var menuCanvas = UIFactory.Canvas("Menu Canvas", 10);
            _menu = menuCanvas.gameObject.AddComponent<MenuController>();
            _menu.Build(menuCanvas, _map);
            _menu.StartRequested += BeginSpawn;
            _menu.ResumeRequested += Resume;
            _menu.RespawnRequested += () => { Resume(); BeginSpawn(); };
            _menu.TitleRequested += () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            var attrCanvas = UIFactory.Canvas("Attribution Canvas", 50);
            attrCanvas.gameObject.AddComponent<AttributionBar>().Build(attrCanvas, !_world.IsOffline);
            attrCanvas.gameObject.AddComponent<DiagnosticsOverlay>().Build(attrCanvas, _world);
        }

        void UpdateMenuStatus()
        {
            string s = _world.StatusMessage;
            if (_map.IsPlaceholder)
                s = (s != null ? s + "\n" : "") + "OSM cache is a placeholder (" + (_map.Meta?.placeholderReason ?? "not downloaded") +
                    ") — building names, map and boundary are limited. Run Tools/osm/fetch_osm.py.";
            _menu.SetStatus(s);
        }

        // ------------------------------------------------------------------------------ flow

        void BeginSpawn()
        {
            _menu.Close();
            _state = State.Spawning;
            _spawnTimer = 0f;
            _groundFoundAt = -1f;
            _spawnXZ = _map.FindSpawnPoint();
            if (_player.gameObject.activeSelf) { _player.enabled = false; _player.gameObject.SetActive(false); }
            _camera.transform.SetParent(null, true);
            _camera.transform.position = new Vector3(_spawnXZ.x, _groundY + 60f, _spawnXZ.y);
            _camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
            _hud.SetVisible(false);
            _hud.SetLoading("Streaming campus tiles…");
            LockCursor();
        }

        void FinishSpawn(Vector3 feet)
        {
            var go = _player.gameObject;
            go.SetActive(true);
            if (_player.Camera == null) _player.Setup(_camera);
            else { _camera.transform.SetParent(_player.Camera.transform.parent, false); _camera.transform.localPosition = Vector3.zero; _camera.transform.localRotation = Quaternion.identity; }
            _player.enabled = true;
            _player.GroundReferenceY = feet.y;
            _player.Teleport(feet + Vector3.up * 0.15f, FacingYaw(feet));
            _player.InputEnabled = true;
            _player.SetFlying(false);
            _hud.SetPlayer(_player);
            _hud.SetVisible(true);
            _hud.SetLoading("");
            _hud.Toast(_map.IsPlaceholder ? "Welcome to Biola! (OSM data not downloaded — names unavailable)" : "Welcome to Biola University!");
            _minimap.SetPlayer(_player.transform);
            _minimap.SetVisible(true);
            _state = State.Playing;
        }

        /// <summary>Face the nearest named building so the first thing you see gets a label.</summary>
        float FacingYaw(Vector3 feet)
        {
            var b = _map.NearestNamedBuilding(CampusMap.XZ(feet), 400f, out _);
            if (b == null) return 0f;
            Vector2 d = b.Centroid - CampusMap.XZ(feet);
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        void Pause()
        {
            _state = State.Paused;
            _player.InputEnabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _hadPointerLock = false;
            _menu.OpenPause();
        }

        void Resume()
        {
            _menu.Close();
            if (_state == State.Paused) _state = State.Playing;
            if (_player != null) _player.InputEnabled = true;
            LockCursor();
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ------------------------------------------------------------------------------ update

        void Update()
        {
            switch (_state)
            {
                case State.Title:
                    UpdateTitleOrbit(Time.deltaTime);
                    float p = _world.LoadProgress;
                    if (!_world.IsOffline && p < 99.5f && !_map.IsPlaceholder) _menu.SetStatus($"Streaming Google 3D Tiles… {p:0}%");
                    else UpdateMenuStatus();
                    break;
                case State.Spawning:
                    UpdateSpawning(Time.deltaTime);
                    break;
                case State.Playing:
                    UpdatePointerLock();
                    _hud.SetLoading(_player.WaitingForGround ? "Waiting for ground tiles to stream in…" : "");
                    break;
            }
        }

        void UpdateTitleOrbit(float dt)
        {
            _orbitAngle += dt * 4f;
            Vector2 c = _map != null ? _map.Bounds.center : Vector2.zero;
            float r = _map != null ? Mathf.Clamp(Mathf.Max(_map.Bounds.width, _map.Bounds.height) * 0.75f, 300f, 900f) : 500f;
            float a = _orbitAngle * Mathf.Deg2Rad;
            var target = new Vector3(c.x, _groundY, c.y);
            _camera.transform.position = target + new Vector3(Mathf.Sin(a) * r, r * 0.55f, Mathf.Cos(a) * r);
            _camera.transform.LookAt(target);
        }

        void UpdateSpawning(float dt)
        {
            _spawnTimer += dt;
            float progress = _world.LoadProgress;
            bool hit = Physics.Raycast(new Vector3(_spawnXZ.x, _groundY + 1500f, _spawnXZ.y), Vector3.down, out var info, 3000f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (hit)
            {
                if (_groundFoundAt < 0f) _groundFoundAt = _spawnTimer;
                // Bring the camera down to eye level so the high-detail tiles around the spawn get requested.
                _camera.transform.position = Vector3.Lerp(_camera.transform.position, info.point + Vector3.up * 1.7f, 1f - Mathf.Exp(-3f * dt));
                bool settled = progress >= 99.5f && _spawnTimer - _groundFoundAt > 1.5f;
                if (settled || _spawnTimer - _groundFoundAt > 12f)
                {
                    // Re-probe: the refined tiles may have replaced the coarse collider.
                    if (Physics.Raycast(new Vector3(_spawnXZ.x, info.point.y + 300f, _spawnXZ.y), Vector3.down, out var fine, 600f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        info = fine;
                    _groundY = info.point.y;
                    FinishSpawn(info.point);
                    return;
                }
                _hud.SetLoading($"Loading campus tiles… {progress:0}%");
            }
            else
            {
                _hud.SetLoading($"Streaming Google Photorealistic 3D Tiles… {progress:0}%\n<size=18>(first load can take a few seconds)</size>");
                if (_spawnTimer > 45f && !_world.IsOffline)
                    _world.GoOffline("No ground tiles arrived within 45 s (network or API key problem). Showing the offline OSM campus.");
            }
        }

        void UpdatePointerLock()
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            // Only treat losing the lock as "Esc pressed" once the browser has really granted it for a moment.
            _lockedFor = locked ? _lockedFor + Time.unscaledDeltaTime : 0f;
            if (_lockedFor > 0.25f) _hadPointerLock = true;

            if (InputShim.Pressed(KeyCode.Escape) || (_hadPointerLock && !locked))
            {
                Pause();
                return;
            }
            // Browsers only grant pointer lock on a user gesture: re-request it on click.
            if (!locked && InputShim.LeftClickPressed) LockCursor();
        }
    }
}
