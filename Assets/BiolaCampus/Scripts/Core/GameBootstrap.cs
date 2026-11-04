using BiolaCampus.Osm;
using BiolaCampus.Player;
using BiolaCampus.World;
using UnityEngine;

namespace BiolaCampus.Core
{
    /// <summary>
    /// Milestone 2: georeference at the OSM campus centre, Google Photorealistic 3D Tiles with physics
    /// meshes, and a first-person walker that spawns on the streamed ground. (Menus/HUD come later.)
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        CampusMap _map;
        CampusWorld _world;
        Camera _camera;
        FirstPersonController _player;
        bool _spawned;
        float _timer, _groundFoundAt = -1f;
        Vector2 _spawnXZ;
        string _status = "";

        void Awake()
        {
            _map = CampusMap.Load();
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            _camera.farClipPlane = 20000f;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            _world = new GameObject("World").AddComponent<CampusWorld>();
            _world.Initialize(_map);

            var go = new GameObject("Player") { layer = 2 };
            go.AddComponent<CharacterController>();
            _player = go.AddComponent<FirstPersonController>();
            go.SetActive(false);

            _spawnXZ = _map.FindSpawnPoint();
            _camera.transform.position = new Vector3(_spawnXZ.x, 60f, _spawnXZ.y);
            _camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
        }

        void Update()
        {
            if (_spawned)
            {
                if (Cursor.lockState != CursorLockMode.Locked && InputShim.LeftClickPressed) Cursor.lockState = CursorLockMode.Locked;
                return;
            }
            _timer += Time.deltaTime;
            float progress = _world.LoadProgress;
            if (Physics.Raycast(new Vector3(_spawnXZ.x, 1500f, _spawnXZ.y), Vector3.down, out var hit, 3000f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (_groundFoundAt < 0) _groundFoundAt = _timer;
                _camera.transform.position = Vector3.Lerp(_camera.transform.position, hit.point + Vector3.up * 1.7f, 0.05f);
                if ((progress >= 99.5f && _timer - _groundFoundAt > 1.5f) || _timer - _groundFoundAt > 12f)
                {
                    _player.gameObject.SetActive(true);
                    _player.Setup(_camera);
                    _player.Map = _map;
                    _player.GroundReferenceY = hit.point.y;
                    _player.Teleport(hit.point + Vector3.up * 0.15f, 0f);
                    _spawned = true;
                    _status = "";
                    return;
                }
            }
            else if (_timer > 45f && !_world.IsOffline)
                _world.GoOffline("No ground tiles arrived within 45 s.");
            _status = $"Streaming Google Photorealistic 3D Tiles… {progress:0}%";
        }

        void OnGUI()
        {
            if (!string.IsNullOrEmpty(_status)) GUI.Label(new Rect(20, 20, 800, 30), _status);
            if (_world != null && _world.IsOffline) GUI.Label(new Rect(20, 44, 1000, 30), _world.StatusMessage);
            GUI.Label(new Rect(Screen.width - 330, Screen.height - 48, 320, 24), "3D Tiles: Google  ·  Map data © OpenStreetMap contributors");
        }
    }
}
